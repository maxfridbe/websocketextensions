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
        public WebSocketServer(Action<string, bool> logger = null, bool useThreadPool = true) : base(logger, useThreadPool)
        {
            _behaviors = new ConcurrentDictionary<string, Func<WebSocketServerBehavior>>();
            _clients = new ConcurrentDictionary<string, WebSocketContext>();
        }
        private int count = 0;
        private ConcurrentDictionary<string, Func<WebSocketServerBehavior>> _behaviors;
        private CancellationTokenSource _cts;
        private HttpListener _listener;
        private Task _listenTask;
        private ConcurrentDictionary<string, WebSocketContext> _clients;

        private void _cleanup()
        {
            if (_listener != null && _listener.IsListening)
            {
                //_listener.Stop();
                _listener.Close();
                //_listener = null;

                _listenTask.GetAwaiter().GetResult();
                //_listener.Close();
            }

            if (_cts != null)
            {
                _cts.Cancel();
            }
            foreach (var c in _clients)
            {
                c.Value.WebSocket.Dispose();
            }

        }
        public IList<string> GetActiveClientIds()
        {
            return _clients.Where(c => c.Value.WebSocket.State == WebSocketState.Open).Select(c => c.Key).ToList();
        }
        public bool IsListening()
        {
            return _listener.IsListening;
        }
        public Task DisconnectClientById(string clientId, string description, WebSocketCloseStatus status = WebSocketCloseStatus.EndpointUnavailable)
        {
            WebSocketContext ctx = null;
            _clients.TryGetValue(clientId, out ctx);
            return ctx.WebSocket.CloseAsync(status, description, CancellationToken.None);
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
        public Task StartAsync(string listenerPrefix, CancellationToken tok = default(CancellationToken))
        {
            _cleanup();

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(listenerPrefix);
            _listener.Start();
            _logInfo($"Listening on {listenerPrefix}");

            tok.Register(() =>
            {
                _cleanup();
            });

            _listenTask = new Task(async () =>
            {
                try
                {
                    while (true)
                    {

                        _cts.Token.ThrowIfCancellationRequested();

                        try
                        {
                            HttpListenerContext listenerContext = await _listener.GetContextAsync().ConfigureAwait(false);

                            _handleContext(listenerContext);
                        }
                        catch (OperationCanceledException)
                        {
                            // We cancelled successfully
                        }
                        catch (Exception e)
                        {
                            _logError(e.ToString());
                        }

                    }
                }
                catch (Exception e)
                {
                    _logError(e.ToString());
                }

            });
            _listenTask.Start();

            return Task.CompletedTask;
        }

        private void _handleContext(HttpListenerContext listenerContext)
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
                    ProcessRequest(listenerContext, builder);
            }
            else
            {
                _logError("Request recieved is not a websocket request");
                listenerContext.Response.StatusCode = 400;
                listenerContext.Response.Close();
            }
        }

        public bool AddRouteBehavior<TBehavior>(string route, Func<TBehavior> p) where TBehavior : WebSocketServerBehavior
        {
            return _behaviors.TryAdd(route, p);
        }


        private async Task ProcessRequest<TWebSocketBehavior>(HttpListenerContext listenerContext, Func<TWebSocketBehavior> behaviorBuilder) where TWebSocketBehavior : WebSocketServerBehavior
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
                    return;
                }
                clientId = behavior.GetClientId(webSocketContext);

                _clients.TryAdd(clientId, webSocketContext);
                Interlocked.Increment(ref count);
                _logInfo($"Client id:{clientId} accepted now there are {count} clients");
            }
            catch (Exception e)
            {
                listenerContext.Response.StatusCode = 500;
                listenerContext.Response.Close();

                this._logError($"Exception: {e}");
                return;
            }

            try
            {

                await RecieveLoop(webSocketContext.WebSocket, behavior.OnBinaryMessage, behavior.OnStringMessage, (e) =>
               {
                   Interlocked.Decrement(ref count);
                   this._logInfo($"Client {clientId} disconnected. now {count} connected clients");
                   behavior.OnClose(new WebSocketClosedEventArgs(clientId, e.ReceivedResult));
               }, clientId);
            }
            finally
            {
                if (webSocketContext.WebSocket != null)
                    webSocketContext.WebSocket.Dispose();

                // _cleanup();


                if (!string.IsNullOrEmpty(clientId))
                    _clients.TryRemove(clientId, out webSocketContext);

            }
        }

        public void Dispose()
        {
            _cleanup();
        }
    }
}
