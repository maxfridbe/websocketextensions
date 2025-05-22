using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    //## The Server class        
    [Obsolete("Use KestrelWebSocketServer from WebSocketExtensions.Kestrel")]
    public class HttpListenerWebSocketServer : WebSocketReciever, IDisposable
    {
        private int _incomingBufferSize;
        private ConcurrentDictionary<Guid, WebSocket> _clients;
        private ConcurrentDictionary<string, Func<HttpListenerWebSocketServerBehavior>> _behaviors;
        private HttpListener _httpListener;
        private Task _listenTask;
        private PagingMessageQueue _messageQueue;
        private CancellationTokenSource _cancellationTokenSource;

        private int _connectedClientCount = 0;
        private readonly long _queueThrottleLimit;
        private readonly TimeSpan _keepAlivePingInterval;
        private bool _enablePingResponse = false;
        private readonly string pingURIPath;
        private Action<HttpListenerContext> _pingHandler = (listenerContext) =>
        {
            listenerContext.Response.StatusCode = (int)HttpStatusCode.OK;

        };
        private bool _isDisposing = false;

        public HttpListenerWebSocketServer(Action<string, bool> logger = null,
            long queueThrottleLimitBytes = long.MaxValue,
            int keepAlivePingIntervalS = 30,
            int incomingBufferSize = 1024 * 1024 * 5,
            bool enablePingResponse = false,
            string pingURIPath = "/ping_ms",
            Action<HttpListenerContext> pingHandler = null
            ) : base(logger)

        {
            _incomingBufferSize = incomingBufferSize;
            _behaviors = new ConcurrentDictionary<string, Func<HttpListenerWebSocketServerBehavior>>();
            _clients = new ConcurrentDictionary<Guid, WebSocket>();
            _queueThrottleLimit = queueThrottleLimitBytes;
            _keepAlivePingInterval = TimeSpan.FromSeconds(keepAlivePingIntervalS);
            _enablePingResponse = enablePingResponse;
            this.pingURIPath = pingURIPath;

            this._pingHandler = pingHandler ?? _pingHandler;
        }

        public IList<Guid> GetActiveConnectionIds()
        {
            return _clients.Where(c => c.Value.State == WebSocketState.Open).Select(c => c.Key).ToList();
        }

        public bool IsListening()
        {
            if (_httpListener == null)
                return false;

            return _httpListener.IsListening;
        }

        public Task AbortConnection(Guid connectionid)
        {
            WebSocket ws = null;
            if (!_clients.TryGetValue(connectionid, out ws))
            {
                return Task.CompletedTask;
            }
            return ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Abort Connection", CancellationToken.None);

            //ws.Abort();
        }

        public Task DisconnectConnection(Guid connectionId, string description, WebSocketCloseStatus status = WebSocketCloseStatus.EndpointUnavailable)
        {
            WebSocket ws = null;
            if (!_clients.TryGetValue(connectionId, out ws))
            {
                return Task.CompletedTask;
            }

            return ws.SendCloseAsync(status, description, CancellationToken.None);
        }

        public Task SendStreamAsync(Guid connectionId, Stream stream, byte[] sendBuffer = null, bool dispose = true, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = null;
            if (!_clients.TryGetValue(connectionId, out ws))
            {
                throw new Exception($"connectionId {connectionId} is no longer a client");
            }

            return ws.SendStreamAsync(stream, sendBuffer, dispose, tok);
        }

        public Task SendBytesAsync(Guid connectionId, byte[] data, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = null;
            if (!_clients.TryGetValue(connectionId, out ws))
            {
                throw new Exception($"connectionId {connectionId} is no longer a client");
            }

            return ws.SendBytesAsync(data, tok);
        }

        public Task SendStringAsync(Guid connectionId, string data, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = null;
            if (!_clients.TryGetValue(connectionId, out ws))
            {
                throw new Exception($"connectionId {connectionId} is no longer a client");
            }

            return ws.SendStringAsync(data, tok);
        }

        public bool AddRouteBehavior<TBehavior>(string route, Func<TBehavior> p) where TBehavior : HttpListenerWebSocketServerBehavior
        {
            return _behaviors.TryAdd(route, p);
        }

        public Task StartAsync(string listenerPrefix, CancellationToken listeningToken = default(CancellationToken))
        {
            stopListeningThread();

            var listenerThredStarted = new TaskCompletionSource<bool>();

            _cancellationTokenSource = new CancellationTokenSource();
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(listenerPrefix);
            _httpListener.Start();
            _logInfo($"Listener Started on {listenerPrefix}.");
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

                        using (_httpListener)
                            await listenLoop(_httpListener, _cancellationTokenSource.Token);
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
                    _logError($"HttpListenerWebSocketServer: Error in handler '{handlerName}': \r\n {e} \r\n {e.StackTrace}");
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
                     _logError($"Error in handler {handlerName} {e}");
                 }
             });
        }

        private async Task listenLoop(HttpListener listener, CancellationToken tok)
        {
            _logInfo($"Listening loop started.");

            while (true)
            {
                try
                {
                    if (!listener.IsListening || tok.IsCancellationRequested)
                        break;

                    HttpListenerContext listenerContext = await listener.GetContextAsync().ConfigureAwait(false);

                    Func<HttpListenerWebSocketServerBehavior> builder = null;
                    if (!_behaviors.TryGetValue(listenerContext.Request.RawUrl, out builder))
                    {
                        if (_enablePingResponse && listenerContext.Request.RawUrl.Contains(pingURIPath))
                        {
                            _pingHandler(listenerContext);
                            listenerContext.Response.Close();
                            return;
                        }

                        _logError($"There is no behavior defined for {listenerContext.Request.RawUrl}");
                        listenerContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        listenerContext.Response.Abort();
                    }
                    else
                    {
                        Task.Run(async () => await handleClient(listenerContext, builder, tok));
                    }
                }
                catch (HttpListenerException listenerex)
                {
                    _logInfo($"HttpListenerException {listenerex}");
                }
                catch (OperationCanceledException canceledex)
                {
                    _logInfo($"OperationCanceledException {canceledex}");
                }
                catch (Exception e)
                {
                    _logError(e.ToString());
                }
            }

            _logInfo($"Listening loop stopped.");
        }

        private async Task handleClient<TWebSocketBehavior>(HttpListenerContext listenerContext, Func<TWebSocketBehavior> behaviorBuilder, CancellationToken token)
            where TWebSocketBehavior : HttpListenerWebSocketServerBehavior
        {
            Guid connectionId;
            WebSocket webSocket = null;
            HttpListenerWebSocketServerBehavior behavior = null;

            try
            {
                int statusCode = 500;
                var statusDescription = "BadContext";

                behavior = behaviorBuilder();

                if (!behavior.OnValidateContext(listenerContext, ref statusCode, ref statusDescription))
                {
                    listenerContext.Response.StatusDescription = statusDescription;
                    listenerContext.Response.StatusCode = statusCode;
                    listenerContext.Response.Close();

                    _logError($"Failed to validate client context. Closing connection. Status: {statusCode}. Description: {statusDescription}.");

                    return;
                }

                connectionId = Guid.NewGuid();

                webSocket = (await listenerContext.AcceptWebSocketAsync(null, _keepAlivePingInterval)).WebSocket;

                bool clientAdded = _clients.TryAdd(connectionId, webSocket);
                if (!clientAdded)
                {
                    throw new ArgumentException($"Attempted to add a new web socket connection to server for connection id '{connectionId}' that already exists.");
                }

                Interlocked.Increment(ref _connectedClientCount);
                _logInfo($"Connection id '{connectionId}' accepted; there are now {_connectedClientCount} total clients.");

                var safeconnected = MakeSafe<Guid, HttpListenerContext>(behavior.OnConnectionEstablished, "behavior.OnClientConnected");
                safeconnected(connectionId, listenerContext);
            }
            catch (Exception e)
            {
                _logError($"Client handler exception: {e}");

                listenerContext.Response.StatusCode = 500;
                listenerContext.Response.Abort();
                listenerContext.Response.Close();

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
                    await webSocket.ProcessIncomingMessages(_messageQueue, connectionId, stringBehavior, binaryBehavior, closeBehavior, _logInfo, false, _incomingBufferSize, token);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _connectedClientCount);
                _logInfo($"Connection id '{connectionId}' disconnected; there are now {_connectedClientCount} total clients.");

                webSocket?.CleanupSendMutex();
                listenerContext.Response.Close();

                bool clientRemoved = _clients.TryRemove(connectionId, out webSocket);
                if (clientRemoved)
                {
                    closeBehavior(new WebSocketReceivedResultEventArgs(closeStatus: WebSocketCloseStatus.EndpointUnavailable, closeStatDesc: "Removing Client Due to other error"));
                }
                else
                {
                    _logError($"Attempted to remove an existing web socket connection to server for connection id '{connectionId}' that no longer exists.");
                }

                _logInfo($"Completed HandleClient task for connection id '{connectionId}'.");
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

            if (_httpListener != null && _httpListener.IsListening)
            {
                _httpListener.Close();
                _httpListener = null;
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
                _messageQueue?.CompleteAdding();
            }
        }
    }
}
