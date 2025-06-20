﻿using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using Microsoft.Net.Http.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebSocketExtensions;

namespace WebSocketExtensions.WebListenerServer
{

    /// <summary>
    /// Web Listener listens on WINDOWS Dotnet48
    /// Use Kestrel for dotnet6 etc
    /// </summary>
    [Obsolete("Use KestrelWebSocketServer from WebSocketExtensions.Kestrel")]
    public class WebListenerWebSocketServer : WebSocketReciever, IDisposable
    {
        private ConcurrentDictionary<Guid, WebSocket> _clients;
        private ConcurrentDictionary<string, Func<WebListenerWebSocketServerBehavior>> _behaviors;
        private WebListener _webListener;
        private Task _listenTask;
        private PagingMessageQueue _messageQueue;
        private CancellationTokenSource _cancellationTokenSource;
        private int _connectedClientCount = 0;
        private readonly long _queueThrottleLimit;
        private readonly int _incomingBufferSize;
        private readonly TimeSpan _keepAlivePingInterval;
        private bool _queueStringMessages;
        private bool _isDisposing = false;

        public WebListenerWebSocketServer(Action<string, bool> logger = null,
            long queueThrottleLimitBytes = long.MaxValue,
            bool queueStringMessages = false,
            int incomingBufferSize = 1048576 * 5,//5mb
            int? keepAlivePingIntervalS = null) : base(logger)
        {
            _behaviors = new ConcurrentDictionary<string, Func<WebListenerWebSocketServerBehavior>>();
            _clients = new ConcurrentDictionary<Guid, WebSocket>();
            _queueThrottleLimit = queueThrottleLimitBytes;
            _incomingBufferSize = incomingBufferSize;
            _keepAlivePingInterval = keepAlivePingIntervalS.HasValue ? TimeSpan.FromSeconds(keepAlivePingIntervalS.Value) : TimeSpan.Zero;
            _queueStringMessages = queueStringMessages;
        }

        public IList<Guid> GetActiveConnectionIds()
        {
            return _clients.Where(c => c.Value.State == WebSocketState.Open).Select(c => c.Key).ToList();
        }

        public bool IsListening()
        {
            if (_webListener == null)
                return false;

            return _webListener.IsListening;
        }

        public Task AbortConnection(Guid connectionid)
        {
            WebSocket ws = null;
            if (!_clients.TryGetValue(connectionid, out ws))
            {
                return Task.CompletedTask;
            }
            return ws.CloseOutputNormalAsync($"Aborting Connection {connectionid}", CancellationToken.None);

            //ws.Abort();
        }

        public Task DisconnectConnection(Guid connectionid, string description, WebSocketCloseStatus status = WebSocketCloseStatus.EndpointUnavailable)
        {
            WebSocket ws = null;
            if (!_clients.TryGetValue(connectionid, out ws))
            {
                return Task.CompletedTask;
            }

            return ws.SendCloseAsync(status, description, CancellationToken.None);
        }

        private WebSocket _getWsFromConnectionId(Guid connectionid)
        {
            if (_clients.TryGetValue(connectionid, out WebSocket ws))
                return ws;

            throw new Exception($"WebListenerWebSocketServer: connectionId {connectionid} is no longer a client");
        }
        public Task SendStreamAsync(Guid connectionid, Stream stream, bool dispose = true, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = _getWsFromConnectionId(connectionid);

            return ws.SendStreamAsync(stream, dispose, tok);
        }

        public Task SendBytesAsync(Guid connectionid, byte[] data, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = _getWsFromConnectionId(connectionid);

            return ws.SendBytesAsync(data, tok);
        }

        public Task SendStringAsync(Guid connectionid, string data, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = _getWsFromConnectionId(connectionid);


            return ws.SendStringAsync(data, tok);
        }

        public bool AddRouteBehavior<TBehavior>(string route, Func<TBehavior> p) where TBehavior : WebListenerWebSocketServerBehavior
        {
            return _behaviors.TryAdd(route, p);
        }

        public Task StartAsync(string listenerPrefix, CancellationToken listeningToken = default(CancellationToken))
        {
            stopListeningThread();

            var listenerThredStarted = new TaskCompletionSource<bool>();

            _cancellationTokenSource = new CancellationTokenSource();
            _webListener = new WebListener();
            // _webListener.Settings.Timeouts.IdleConnection = TimeSpan.FromHours(1);
            _webListener.Settings.UrlPrefixes.Add(listenerPrefix);
            _webListener.Start();
            _logInfo($"WebListenerWebSocketServer: Listener started on {listenerPrefix}.");
            _messageQueue = new PagingMessageQueue("WebSocketServer", _logError, _queueThrottleLimit);

            _listenTask = Task.Run(async () =>
            {
                try
                {
                    if (listeningToken.IsCancellationRequested)
                    {
                        listenerThredStarted.TrySetCanceled();
                    }
                    else
                    {
                        listenerThredStarted.TrySetResult(true);

                        using (_webListener)
                            await listenLoop(_webListener, _cancellationTokenSource.Token);
                    }
                }
                catch (Exception e)
                {
                    _logError("WebSocketServer exception in the listenTask: " + e.ToString());
                }
            });

            return listenerThredStarted.Task;
        }

        public Action<T> MakeSafe<T>(Action<T> torun, string handlerName)
        {
            return new Action<T>((T data) =>
            {
                try
                {
                    torun(data);
                }
                catch (Exception e)
                {
                    _logError($"WebListenerWebSocketServer: Error in handler '{handlerName}': \r\n {e} \r\n {e.StackTrace}");
                }
            });
        }

        public Action<T, T2> MakeSafe<T, T2>(Action<T, T2> torun, string handlerName)
        {
            return new Action<T, T2>((T data, T2 data2) =>
            {
                try
                {
                    torun(data, data2);
                }
                catch (Exception e)
                {
                    _logError($"WebListenerWebSocketServer: Error in handler '{handlerName}': {e}");
                }
            });
        }

        private async Task listenLoop(WebListener listener, CancellationToken tok)
        {
            _logInfo($"WebListenerWebSocketServer: Listening loop started.");

            while (true)
            {
                try
                {
                    if (!listener.IsListening || tok.IsCancellationRequested)
                        break;

                    var requestContext = await listener.AcceptAsync().ConfigureAwait(false);

                    Func<WebListenerWebSocketServerBehavior> builder = null;
                    if (!_behaviors.TryGetValue(requestContext.Request.RawUrl, out builder))
                    {
                        _logError($"WebListenerWebSocketServer: There is no behavior defined for {requestContext.Request.RawUrl}");
                        requestContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        requestContext.Abort();
                    }
                    else
                    {
                        Task.Run(async () => await handleClient(requestContext, builder, tok));
                    }
                }
                catch (HttpListenerException listenerex)
                {
                    _logInfo($"WebListenerWebSocketServer: HttpListenerException {listenerex}");
                }
                catch (OperationCanceledException canceledex)
                {
                    _logInfo($"WebListenerWebSocketServer: OperationCanceledException {canceledex}");
                }
                catch (Exception e)
                {
                    _logError($"WebListenerWebSocketServer: Exception {e.ToString()}");
                }
            }

            _logInfo($"WebListenerWebSocketServer: Listening loop stopped.");
        }

        private async Task handleClient<TWebSocketBehavior>(RequestContext requestContext, Func<TWebSocketBehavior> behaviorBuilder, CancellationToken token)
            where TWebSocketBehavior : WebListenerWebSocketServerBehavior
        {
            Guid connectionId;
            WebSocket webSocket = null;
            WebListenerWebSocketServerBehavior behavior = null;
            try
            {
                int statusCode = 500;
                var statusDescription = "BadContext";

                behavior = behaviorBuilder();

                if (!behavior.OnValidateContext(requestContext, ref statusCode, ref statusDescription))
                {
                    requestContext.Response.ReasonPhrase = statusDescription;
                    requestContext.Response.StatusCode = statusCode;
                    requestContext.Abort();

                    _logError($"WebListenerWebSocketServer: Failed to validate client context. Closing connection. Status: {statusCode}. Description: {statusDescription}.");

                    return;
                }

                connectionId = Guid.NewGuid();

                webSocket = await requestContext.AcceptWebSocketAsync(null, _keepAlivePingInterval);

                bool clientAdded = _clients.TryAdd(connectionId, webSocket);
                if (!clientAdded)
                {
                    throw new ArgumentException($"WebListenerWebSocketServer: Attempted to add a new web socket connection to server for connection id '{connectionId}' that already exists.");
                }

                Interlocked.Increment(ref _connectedClientCount);
                _logInfo($"WebListenerWebSocketServer: Connection id '{connectionId}' accepted; there are now {_connectedClientCount} total clients.");

                var safeconnected = MakeSafe<Guid, RequestContext>(behavior.OnConnectionEstablished, "behavior.OnClientConnected");
                safeconnected(connectionId, requestContext);
            }
            catch (Exception e)
            {
                _logError($"WebListenerWebSocketServer: Client handler exception: {e}");

                requestContext.Response.StatusCode = 500;
                requestContext.Abort();
                requestContext.Dispose();

                return;
            }

            var stringBehavior = MakeSafe<StringMessageReceivedEventArgs>(behavior.OnStringMessage, "behavior.OnStringMessage");
            var binaryBehavior = MakeSafe<BinaryMessageReceivedEventArgs>(behavior.OnBinaryMessage, "behavior.OnBinaryMessage");
            bool single = false;
            var closeBehavior = MakeSafe<WebSocketReceivedResultEventArgs>((r) =>
            {
                if (!single)
                    behavior.OnClose(new WebSocketClosedEventArgs(connectionId, r));
                single = true;
            }, "behavior.OnClose");

            try
            {
                using (webSocket)
                {
                    await webSocket.ProcessIncomingMessages(_messageQueue, connectionId, stringBehavior, binaryBehavior, closeBehavior, _logInfo, _queueStringMessages, _incomingBufferSize, token);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _connectedClientCount);
                _logInfo($"WebListenerWebSocketServer: Connection id '{connectionId}' disconnected; there are now {_connectedClientCount} total clients.");

                webSocket?.CleanupSendMutex();
                requestContext.Dispose();

                bool clientRemoved = _clients.TryRemove(connectionId, out webSocket);
                if (clientRemoved)
                {
                    closeBehavior(new WebSocketReceivedResultEventArgs(closeStatus: WebSocketCloseStatus.EndpointUnavailable, closeStatDesc: "Removing Client Due to other error"));
                }
                else
                {
                    _logError($"WebListenerWebSocketServer: Attempted to remove an existing web socket connection to server for connection id '{connectionId}' that no longer exists.");
                }

                _logInfo($"WebListenerWebSocketServer: Completed HandleClient task for connection id '{connectionId}'.");
            }
        }

        private void stopListeningThread()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            if (_webListener != null && _webListener.IsListening)
            {
                _webListener.Dispose();
                _webListener = null;
            }

            _clients.Clear();

            if (_listenTask != null && !_listenTask.IsCompleted)
                _listenTask.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (!_isDisposing)
            {
                _isDisposing = true;
                stopListeningThread();
                _messageQueue?.Dispose();
            }
        }
    }

}