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
    public class WebSocketServer : WebSocketReciever, IDisposable
    {
        public WebSocketServer(Action<string, bool> logger = null, long queueThrottleLimitBytes = long.MaxValue) : base(logger)
        {
            _behaviors = new ConcurrentDictionary<string, Func<WebSocketServerBehavior>>();
            _clients = new ConcurrentDictionary<string, WebSocketContext>();
            _queueThrottleLimit = queueThrottleLimitBytes;
        }
        private int count = 0;
        private ConcurrentDictionary<string, Func<WebSocketServerBehavior>> _behaviors;
        private CancellationTokenSource _cts;
        private HttpListener _httpListener;
        private Task _listenTask;
        private ConcurrentDictionary<string, WebSocketContext> _clients;
        private readonly long _queueThrottleLimit;


        public IList<string> GetActiveClientIds()
        {
            return _clients.Where(c => c.Value.WebSocket.State == WebSocketState.Open).Select(c => c.Key).ToList();
        }
        public bool IsListening()
        {
            if (_httpListener == null)
                return false;
            return _httpListener.IsListening;
        }
        public Task DisconnectClientById(string clientId, string description, WebSocketCloseStatus status = WebSocketCloseStatus.EndpointUnavailable)
        {
            WebSocketContext ctx = null;
            _clients.TryGetValue(clientId, out ctx);
            return ctx.WebSocket.SendCloseAsync(status, description, CancellationToken.None);
        }
        public Task SendStreamAsync(string clientId, Stream stream, bool dispose = true, CancellationToken tok = default(CancellationToken))
        {
            WebSocketContext ctx = null;
            _clients.TryGetValue(clientId, out ctx);
            return ctx.WebSocket.SendStreamAsync(stream, dispose, tok);
        }
        public Task SendBytesAsync(string clientId, byte[] data, CancellationToken tok = default(CancellationToken))
        {
            WebSocketContext ctx = null;
            _clients.TryGetValue(clientId, out ctx);
            return ctx.WebSocket.SendBytesAsync(data, tok);
        }

        public Task SendStringAsync(string clientId, string data, CancellationToken tok = default(CancellationToken))
        {
            WebSocketContext ctx = null;
            _clients.TryGetValue(clientId, out ctx);
            return ctx.WebSocket.SendStringAsync(data, tok);

        }
        public bool AddRouteBehavior<TBehavior>(string route, Func<TBehavior> p) where TBehavior : WebSocketServerBehavior
        {
            return _behaviors.TryAdd(route, p);
        }
        private void _stopListeningThread()
        {
            if (_httpListener != null && _httpListener.IsListening)
            {
                _httpListener.Stop();
            }
            if (_cts != null)
            {
                _cts.Cancel();
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
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(listenerPrefix);
            _httpListener.Start();
            _logInfo($"Listener Started on {listenerPrefix}");


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
                            await ListenLoop(_httpListener, _cts.Token);
                    }

                }

                catch (Exception e)
                {
                    _logError(e.ToString());
                }
            });

            return listenerThredStarted.Task;
        }
        private async Task ListenLoop(HttpListener listener, CancellationToken tok)
        {
            _logInfo($"Listening loop Started");

            while (true)
            {
                try
                {
                    if (!listener.IsListening || tok.IsCancellationRequested)
                        break;

                    HttpListenerContext listenerContext = await listener.GetContextAsync().ConfigureAwait(false);

                    _handleContext(listenerContext, tok);
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
        private void _handleContext(HttpListenerContext listenerContext, CancellationToken token)
        {
            if (listenerContext.Request.IsWebSocketRequest)
            {

                Func<WebSocketServerBehavior> builder = null;
                if (!_behaviors.TryGetValue(listenerContext.Request.RawUrl, out builder))
                {
                    _logError($"There is no behavior defined for {listenerContext.Request.RawUrl}");
                    listenerContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    listenerContext.Response.Close();
                }
                else
                    Task.Run(async () => await HandleClient(listenerContext, builder, token));
            }
            else
            {
                _logError("Request recieved is not a websocket request");
                listenerContext.Response.StatusCode = 400;
                listenerContext.Response.Close();
            }
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
                    _logError($"Error in handler {handlerName} {e}");
                }

            });

        }


        private async Task HandleClient<TWebSocketBehavior>(HttpListenerContext listenerContext, Func<TWebSocketBehavior> behaviorBuilder, CancellationToken token)
            where TWebSocketBehavior : WebSocketServerBehavior
        {
            WebSocketContext webSocketContext = null;
            WebSocketServerBehavior behavior = null;
            string clientId;
            try
            {
                webSocketContext = await listenerContext.AcceptWebSocketAsync(subProtocol: null);
                behavior = behaviorBuilder();
                int statusCode = 500;
                var statusDescription = "BadContext";
                if (!behavior.OnValidateContext(webSocketContext, ref statusCode, ref statusDescription))
                {
                    listenerContext.Response.StatusDescription = statusDescription;
                    listenerContext.Response.StatusCode = statusCode;
                    listenerContext.Response.Close();

                    _logError($"Failed to validate client context. Closing connection. Status: {statusCode}. Description: {statusDescription}.");

                    return;
                }
                clientId = behavior.GetClientId(webSocketContext);

                _clients.TryAdd(clientId, webSocketContext);
                Interlocked.Increment(ref count);
                _logInfo($"Client id:{clientId} accepted now there are {count} clients");
                var safeconnected = MakeSafe<string>(behavior.OnClientConnected, "behavior.OnClientConnected");
                safeconnected(clientId);
            }
            catch (Exception e)
            {
                listenerContext.Response.StatusCode = 500;
                listenerContext.Response.Close();

                _logError($"Exception: {e}");
                return;
            }

            try
            {
                using (webSocketContext.WebSocket)
                {
                    var closeBeh = MakeSafe<WebSocketReceivedResultEventArgs>((r) => behavior.OnClose(new WebSocketClosedEventArgs(clientId, r)), "behavior.OnClose");
                    var strBeh = MakeSafe<StringMessageReceivedEventArgs>(behavior.OnStringMessage, "behavior.OnStringMessage");
                    var binBeh = MakeSafe<BinaryMessageReceivedEventArgs>(behavior.OnBinaryMessage, "behavior.OnBinaryMessage");

                    await webSocketContext.WebSocket.ProcessIncomingMessages(strBeh, binBeh, closeBeh, _logError, _logInfo, clientId, _queueThrottleLimit, token);
                }

            }
            finally
            {
                Interlocked.Decrement(ref count);
                this._logInfo($"Client {clientId ?? "_unidentified_"} disconnected. now {count} connected clients");

                webSocketContext?.WebSocket.CleanupSendMutex();

                if (!string.IsNullOrEmpty(clientId))
                {
                    _clients.TryRemove(clientId, out webSocketContext);

                }
                _logInfo($"Completed Receive Loop for clientid {clientId ?? "_unidentified_"}");

            }
        }

        public void Dispose()
        {
            _stopListeningThread();
        }
    }
}
