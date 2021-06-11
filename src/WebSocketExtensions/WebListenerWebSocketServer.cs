using Microsoft.Net.Http.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{

    //## The Server class        
    public class WebListenerWebSocketServer : WebSocketReciever, IDisposable
    {
        public WebListenerWebSocketServer(Action<string, bool> logger = null, long queueThrottleLimitBytes = long.MaxValue) : base(logger)
        {
            _behaviors = new ConcurrentDictionary<string, Func<WebListenerWebSocketServerBehavior>>();
            _clients = new ConcurrentDictionary<string, WebSocket>();
            _queueThrottleLimit = queueThrottleLimitBytes;
        }
        private int count = 0;
        private ConcurrentDictionary<string, Func<WebListenerWebSocketServerBehavior>> _behaviors;
        private CancellationTokenSource _cts;
        private WebListener _webListener;
        private Task _listenTask;
        private ConcurrentDictionary<string, WebSocket> _clients;
        private PagingMessageQueue _messageQueue;
        private readonly long _queueThrottleLimit;


        public IList<string> GetActiveClientIds()
        {
            return _clients.Where(c => c.Value.State == WebSocketState.Open).Select(c => c.Key).ToList();
        }
        public bool IsListening()
        {
            if (_webListener == null)
                return false;
            return _webListener.IsListening;
        }
        public Task DisconnectClientById(string clientId, string description, WebSocketCloseStatus status = WebSocketCloseStatus.EndpointUnavailable)
        {
            WebSocket ws = null;
            _clients.TryGetValue(clientId, out ws);
            return ws.SendCloseAsync(status, description, CancellationToken.None);
        }
        public Task SendStreamAsync(string clientId, Stream stream, bool dispose = true, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = null;
            _clients.TryGetValue(clientId, out ws);
            return ws.SendStreamAsync(stream, dispose, tok);
        }
        public Task SendBytesAsync(string clientId, byte[] data, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = null;
            _clients.TryGetValue(clientId, out ws);
            return ws.SendBytesAsync(data, tok);
        }

        public Task SendStringAsync(string clientId, string data, CancellationToken tok = default(CancellationToken))
        {
            WebSocket ws = null;
            _clients.TryGetValue(clientId, out ws);
            return ws.SendStringAsync(data, tok);

        }
        public bool AddRouteBehavior<TBehavior>(string route, Func<TBehavior> p) where TBehavior : WebListenerWebSocketServerBehavior
        {
            return _behaviors.TryAdd(route, p);
        }
        private void _stopListeningThread()
        {
            
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
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

        public Task StartAsync(string listenerPrefix, CancellationToken listeningToken = default(CancellationToken))
        {
            _stopListeningThread();
            var listenerThredStarted = new TaskCompletionSource<bool>();

            _cts = new CancellationTokenSource();
            _webListener = new WebListener();
            _webListener.Settings.UrlPrefixes.Add(listenerPrefix);
            _webListener.Start();
            _logInfo($"Listener Started on {listenerPrefix}");
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
                            await ListenLoop(_webListener, _cts.Token);
                    }
                }
                catch (Exception e)
                {
                    _logError("WebSocketServer: Exception in the listenTask" + e.ToString());
                }
            });

            return listenerThredStarted.Task;
        }
        private async Task ListenLoop(WebListener listener, CancellationToken tok)
        {
            _logInfo($"Listening loop Started");

            while (true)
            {
                try
                {
                    if (!listener.IsListening || tok.IsCancellationRequested)
                        break;

                    var requestContext = await listener.AcceptAsync().ConfigureAwait(false);// listener.GetContextAsync().ConfigureAwait(false);

                    Func<WebListenerWebSocketServerBehavior> builder = null;
                    if (!_behaviors.TryGetValue(requestContext.Request.RawUrl, out builder))
                    {
                        _logError($"There is no behavior defined for {requestContext.Request.RawUrl}");
                        requestContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        requestContext.Abort();
                    }
                    else
                    {
                        Task.Run(async () => await HandleClient(requestContext, builder, tok));
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
            _logInfo($"Listening loop Stopped");

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
                    _logError($"WebListenerWebSocketServer: Error in handler {handlerName} \r\n {e} \r\n {e.StackTrace}");
                }

            });

        }


        private async Task HandleClient<TWebSocketBehavior>(RequestContext requestContext, Func<TWebSocketBehavior> behaviorBuilder, CancellationToken token)
            where TWebSocketBehavior : WebListenerWebSocketServerBehavior
        {
            WebSocket ws = null;
            WebListenerWebSocketServerBehavior behavior = null;
            string clientId;
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

                    _logError($"Failed to validate client context. Closing connection. Status: {statusCode}. Description: {statusDescription}.");

                    return;
                }
                ws = await requestContext.AcceptWebSocketAsync();


                clientId = behavior.GetClientId(requestContext);

                _clients.TryAdd(clientId, ws);
                Interlocked.Increment(ref count);
                _logInfo($"Client id:{clientId} accepted now there are {count} clients");
                var safeconnected = MakeSafe<string>(behavior.OnClientConnected, "behavior.OnClientConnected");
                safeconnected(clientId);
            }
            catch (Exception e)
            {
                requestContext.Response.StatusCode = 500;
                requestContext.Abort();//.Response.Close();
                
                _logError($"Exception: {e}");
                requestContext.Dispose();
                return;
            }

            try
            {
                using (ws)
                {
                    var closeBeh = MakeSafe<WebSocketReceivedResultEventArgs>((r) => behavior.OnClose(new WebSocketClosedEventArgs(clientId, r)), "behavior.OnClose");
                    var strBeh = MakeSafe<StringMessageReceivedEventArgs>(behavior.OnStringMessage, "behavior.OnStringMessage");
                    var binBeh = MakeSafe<BinaryMessageReceivedEventArgs>(behavior.OnBinaryMessage, "behavior.OnBinaryMessage");

                    await ws.ProcessIncomingMessages(_messageQueue, strBeh, binBeh, closeBeh, _logError, _logInfo, clientId, token);
                }

            }
            finally
            {
                Interlocked.Decrement(ref count);
                this._logInfo($"Client {clientId ?? "_unidentified_"} disconnected. now {count} connected clients");
               
                ws?.CleanupSendMutex();
                requestContext.Dispose();
                if (!string.IsNullOrEmpty(clientId))
                {
                    _clients.TryRemove(clientId, out ws);

                }
                _logInfo($"Completed Receive Loop for clientid {clientId ?? "_unidentified_"}");

            }
        }

        bool _isdisposing = false;
        public void Dispose()
        {
            if (!_isdisposing)
            {
                _isdisposing = true;
                _stopListeningThread();
                _messageQueue?.CompleteAdding();
            }
        }
    }
}
