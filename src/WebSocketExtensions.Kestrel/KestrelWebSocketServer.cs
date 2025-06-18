namespace WebSocketExtensions.Kestrel;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public record class KestrelWebSocketServerStats(PagingMessageQueueStats QueueStats, int ClientCount);

public class KestrelWebSocketServer : IDisposable
{
    private ConcurrentDictionary<Guid, WebSocket> _clients;
    private ConcurrentDictionary<string, Func<KestrelWebSocketServerBehavior>> _behaviors;
    private PagingMessageQueue _messageQueue = null;
    private CancellationTokenSource _cancellationTokenSource = null;
    private int _connectedClientCount = 0;
    private readonly ILogger _logger = null;
    private readonly long _queueThrottleLimit;
    private bool _queueStringMessages;
    private readonly int _incomingBufferSizeBytes;
    private readonly string _pingResponseRoute;
    // private readonly TimeSpan _keepAlivePingInterval;
    private bool _isDisposing = false;
    private IWebHost _host = null;
    private Func<HttpContext, KestrelWebSocketServerStats, Task> _pingHandler = async (listenerContext, stats) =>
    {
        listenerContext.Response.ContentType = "application/json";
        listenerContext.Response.StatusCode = (int)HttpStatusCode.OK;

        string jsonResponse = JsonSerializer.Serialize(stats);
        await listenerContext.Response.WriteAsync(jsonResponse);
    };

    public KestrelWebSocketServer(ILogger logger,
            long queueThrottleLimitBytes = long.MaxValue,
            // int keepAlivePingIntervalS = 30,
            int incomingBufferSizeBytes = 1048576 * 5,//5mb
            bool queueStringMessages = false,
            string httpPingResponseRoute = null,
            Func<HttpContext, KestrelWebSocketServerStats, Task> pingHandler = null)
    {
        _behaviors = new ConcurrentDictionary<string, Func<KestrelWebSocketServerBehavior>>();
        _clients = new ConcurrentDictionary<Guid, WebSocket>();
        _logger = logger;
        _queueThrottleLimit = queueThrottleLimitBytes;
        _queueStringMessages = queueStringMessages;
        _incomingBufferSizeBytes = incomingBufferSizeBytes;
        _pingResponseRoute = httpPingResponseRoute;

        _pingHandler = pingHandler ?? _pingHandler;
        //ping needs to be handled at the message layer to be smart enough
        // _keepAlivePingInterval = TimeSpan.FromSeconds(keepAlivePingIntervalS);
    }

    public IList<Guid> GetActiveConnectionIds()
    {
        return _clients.Where(c => c.Value.State == WebSocketState.Open).Select(c => c.Key).ToList();
    }

    public bool IsListening()
    {
        return (_host != null);
    }
    public KestrelWebSocketServerStats GetStats()
    {
        var stats = new KestrelWebSocketServerStats(_messageQueue?.GetQueueStats() ?? new PagingMessageQueueStats(0, 0), _connectedClientCount);
        return stats;
    }
    public Task AbortConnectionAsync(Guid connectionid, CancellationToken tok = default(CancellationToken))
    {
        WebSocket ws = null;
        if (!_clients.TryGetValue(connectionid, out ws))
        {
            return Task.CompletedTask;
        }
        //ws.Dispose();
        //ws.Abort();
        return ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Abort Connection", tok);
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

    private WebSocket _getWebSocketFromConnectionId(Guid connectionId)
    {
        if (_clients.TryGetValue(connectionId, out WebSocket ws))
            return ws;

        throw new Exception($"KestrelWebSocketServer: connectionId {connectionId} is no longer a client");

    }
    public Task SendStreamAsync(Guid connectionId, Stream stream, bool dispose = true, CancellationToken tok = default(CancellationToken))
    {
        WebSocket ws = _getWebSocketFromConnectionId(connectionId);


        return ws.SendStreamAsync(stream, dispose, tok);
    }

    public Task SendBytesAsync(Guid connectionId, byte[] data, CancellationToken tok = default(CancellationToken))
    {
        WebSocket ws = _getWebSocketFromConnectionId(connectionId);


        return ws.SendBytesAsync(data, tok);
    }

    public Task SendStringAsync(Guid connectionId, string data, CancellationToken tok = default(CancellationToken))
    {
        WebSocket ws = _getWebSocketFromConnectionId(connectionId);


        return ws.SendStringAsync(data, tok);
    }

    public bool AddRouteBehavior<TBehavior>(string route, Func<TBehavior> p) where TBehavior : KestrelWebSocketServerBehavior
    {
        return _behaviors.TryAdd(route, p);
    }

    public Task StartAsync(string listenerPrefix, CancellationToken listeningToken = default(CancellationToken))
    {
        stopListeningThread();

        var listenerThredStarted = new TaskCompletionSource<bool>();

        _cancellationTokenSource = new CancellationTokenSource();

        var hostBuilder = new WebHostBuilder()
            .UseKestrel()
            .UseUrls(listenerPrefix);
        hostBuilder.Configure(app =>
        {

            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(10)

            });
            app.Run(async context =>
            {
                Func<KestrelWebSocketServerBehavior> builder = null;
                if (_behaviors.TryGetValue(context.Request.Path, out builder))
                {
                    await handleClient(context, builder, _cancellationTokenSource.Token);
                }

                if (context.Request.Path.HasValue
                   && !string.IsNullOrEmpty(_pingResponseRoute)
                   && context.Request.Path.Value.Contains(_pingResponseRoute)
                   && !context.WebSockets.IsWebSocketRequest)
                {
                    var stats = GetStats();
                    await _pingHandler(context, stats);
                    //    await listenerContext.Response..CompleteAsync();//.Close();
                    return;
                }
            });
        });
        _host = hostBuilder.Build();
        var startedHostTask = _host.StartAsync();

        _logger.LogInformation($"KestrelWebSocketServer: Listener Started on {listenerPrefix}.");
        _messageQueue = new PagingMessageQueue("WebSocketServer", (e) => _logger.LogError(e), _queueThrottleLimit);

        return startedHostTask;
    }
    // src/WebSocketExtensions.Kestrel/KestrelWebSocketServer.cs

    public Action<T> MakeSafe<T>(Action<T> torun, string handlerName)
    {
        return (T data) =>
        {
            try
            {
                torun(data);
            }
            catch (Exception e)
            {
                _logger.LogError($"KestrelWebSocketServer: Error in handler '{handlerName}': \r\n {e} \r\n {e.StackTrace}");
            }
        };
    }

    public Action<T, T2> MakeSafe<T, T2>(Action<T, T2> torun, string handlerName)
    {
        return (T data, T2 data2) =>
         {
             try
             {
                 torun(data, data2);
             }
             catch (Exception e)
             {
                 _logger.LogError($"KestrelWebSocketServer: Error in handler {handlerName} {e}");
             }
         };
    }

    private async Task handleClient<TWebSocketBehavior>(HttpContext listenerContext, Func<TWebSocketBehavior> behaviorBuilder, CancellationToken token)
        where TWebSocketBehavior : KestrelWebSocketServerBehavior
    {
        Guid connectionId;
        WebSocket webSocket = null;
        KestrelWebSocketServerBehavior behavior = null;


        try
        {



            int statusCode = 500;
            var statusDescription = "BadContext";

            behavior = behaviorBuilder();

            if (!behavior.OnValidateContext(listenerContext, ref statusCode, ref statusDescription)
            || !listenerContext.WebSockets.IsWebSocketRequest)
            {
                //  listenerContext.Response.StatusDescription = statusDescription;
                listenerContext.Response.StatusCode = statusCode;

                // await listenerContext.Response.CompleteAsync();

                _logger.LogError($"KestrelWebSocketServer: Failed to validate client context. Closing connection. Status: {statusCode}. Description: {statusDescription}.");

                return;
            }

            connectionId = Guid.NewGuid();
            webSocket = await listenerContext.WebSockets.AcceptWebSocketAsync();

            bool clientAdded = _clients.TryAdd(connectionId, webSocket);
            if (!clientAdded)
            {
                throw new ArgumentException($"KestrelWebSocketServer: Attempted to add a new web socket connection to server for connection id '{connectionId}' that already exists.");
            }

            Interlocked.Increment(ref _connectedClientCount);
            _logger.LogInformation($"Connection id '{connectionId}' accepted; there are now {_connectedClientCount} total clients.");

            var safeconnected = MakeSafe<Guid, HttpContext>(behavior.OnConnectionEstablished, "behavior.OnClientConnected");
            safeconnected(connectionId, listenerContext);
        }
        catch (Exception e)
        {
            _logger.LogError($"Client handler exception: {e}");

            listenerContext.Response.StatusCode = 500;
            listenerContext.Abort();

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
                await webSocket.ProcessIncomingMessages(_messageQueue, connectionId, stringBehavior, binaryBehavior, closeBehavior, (i) => _logger.LogInformation(i), _queueStringMessages, _incomingBufferSizeBytes, token);
                _logger.LogInformation($"KestrelWebSocketServer: ProcessIncomingMessages completed for {connectionId}");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _connectedClientCount);
            _logger.LogInformation($"KestrelWebSocketServer: Connection id '{connectionId}' disconnected; there are now {_connectedClientCount} total clients.");

            webSocket?.CleanupSendMutex();
            listenerContext.Abort();

            bool clientRemoved = _clients.TryRemove(connectionId, out webSocket);
            if (clientRemoved)
            {
                closeBehavior(new WebSocketReceivedResultEventArgs(closeStatus: WebSocketCloseStatus.EndpointUnavailable, closeStatDesc: "Removing Client Due to other error"));
            }
            else
            {
                _logger.LogError($"KestrelWebSocketServer: Attempted to remove an existing web socket connection to server for connection id '{connectionId}' that no longer exists.");
            }

            _logger.LogInformation($"KestrelWebSocketServer: Completed HandleClient task for connection id '{connectionId}'.");
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
        if (_host != null)
        {
            _host.Dispose();
            _host = null;
        }

        _clients.Clear();

    }

    public void Dispose()
    {
        if (!_isDisposing)
        {
            _isDisposing = true;
            stopListeningThread();
            _messageQueue?.Dispose();//will allow message queue to complete processing 
        }
    }
}
