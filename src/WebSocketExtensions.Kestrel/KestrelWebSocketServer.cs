﻿namespace WebSocketExtensions.Kestrel;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public class KestrelWebSocketServer : IDisposable
{


    private ConcurrentDictionary<Guid, WebSocket> _clients;
    private ConcurrentDictionary<string, Func<KestrelWebSocketServerBehavior>> _behaviors;
    private PagingMessageQueue _messageQueue = null;
    private CancellationTokenSource _cancellationTokenSource = null;
    private int _connectedClientCount = 0;
    private readonly ILogger _logger = null;
    private readonly long _queueThrottleLimit;
    private readonly string _pingResponseRoute;
    private readonly TimeSpan _keepAlivePingInterval;
    private bool _isDisposing = false;
    private IWebHost _host = null;

    public KestrelWebSocketServer(ILogger logger,
            long queueThrottleLimitBytes = long.MaxValue,
            int keepAlivePingIntervalS = 30,
            string httpPingResponseRoute = null)
    {
        _behaviors = new ConcurrentDictionary<string, Func<KestrelWebSocketServerBehavior>>();
        _clients = new ConcurrentDictionary<Guid, WebSocket>();
        _logger = logger;
        _queueThrottleLimit = queueThrottleLimitBytes;
        _pingResponseRoute = httpPingResponseRoute;
        _keepAlivePingInterval = TimeSpan.FromSeconds(keepAlivePingIntervalS);
    }

    public IList<Guid> GetActiveConnectionIds()
    {
        return _clients.Where(c => c.Value.State == WebSocketState.Open).Select(c => c.Key).ToList();
    }

    public bool IsListening()
    {
        return (_host != null);
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
       return ws.CloseAsync(WebSocketCloseStatus.NormalClosure,"Abort Connection", tok);
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

        return ws.SendStreamAsync(stream, sendBuffer,dispose,  tok);
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
            
            app.UseWebSockets(new WebSocketOptions{
                KeepAliveInterval=TimeSpan.FromSeconds(10)

            });
            app.Run(async context =>
            {
                Func<KestrelWebSocketServerBehavior> builder = null;
                if (_behaviors.TryGetValue(context.Request.Path, out builder))
                {
                    await handleClient(context, builder, _cancellationTokenSource.Token);
                }
            });
        });
        _host = hostBuilder.Build();
        var startedHostTask = _host.StartAsync();

        _logger.LogInformation($"Listener Started on {listenerPrefix}.");
        _messageQueue = new PagingMessageQueue("WebSocketServer", (e) => _logger.LogError(e), _queueThrottleLimit);

        return startedHostTask;
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
                _logger.LogError($"KestrelWebSocketServer: Error in handler '{handlerName}': \r\n {e} \r\n {e.StackTrace}");
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
                 _logger.LogError($"Error in handler {handlerName} {e}");
             }
         });
    }

    private async Task handleClient<TWebSocketBehavior>(HttpContext listenerContext, Func<TWebSocketBehavior> behaviorBuilder, CancellationToken token)
        where TWebSocketBehavior : KestrelWebSocketServerBehavior
    {
        Guid connectionId;
        WebSocket webSocket = null;
        KestrelWebSocketServerBehavior behavior = null;


        try
        {
            if (listenerContext.Request.Path.HasValue
                && !string.IsNullOrEmpty(_pingResponseRoute )
                && listenerContext.Request.Path.Value.Contains(_pingResponseRoute)
                && !listenerContext.WebSockets.IsWebSocketRequest)
            {
                listenerContext.Response.StatusCode = (int)HttpStatusCode.OK;
                await listenerContext.Response.WriteAsync("ok");
                //    await listenerContext.Response..CompleteAsync();//.Close();
                return;
            }


            int statusCode = 500;
            var statusDescription = "BadContext";

            behavior = behaviorBuilder();

            if (!behavior.OnValidateContext(listenerContext, ref statusCode, ref statusDescription)
            || !listenerContext.WebSockets.IsWebSocketRequest)
            {
                //  listenerContext.Response.StatusDescription = statusDescription;
                listenerContext.Response.StatusCode = statusCode;

                // await listenerContext.Response.CompleteAsync();

                _logger.LogError($"Failed to validate client context. Closing connection. Status: {statusCode}. Description: {statusDescription}.");

                return;
            }

            connectionId = Guid.NewGuid();
            webSocket = await listenerContext.WebSockets.AcceptWebSocketAsync();

            bool clientAdded = _clients.TryAdd(connectionId, webSocket);
            if (!clientAdded)
            {
                throw new ArgumentException($"Attempted to add a new web socket connection to server for connection id '{connectionId}' that already exists.");
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
                await webSocket.ProcessIncomingMessages(_messageQueue, connectionId, stringBehavior, binaryBehavior, closeBehavior,
                (i) => _logger.LogInformation(i), token);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _connectedClientCount);
            _logger.LogInformation($"Connection id '{connectionId}' disconnected; there are now {_connectedClientCount} total clients.");

            webSocket?.CleanupSendMutex();
            listenerContext.Abort();

            bool clientRemoved = _clients.TryRemove(connectionId, out webSocket);
            if (clientRemoved)
            {
                closeBehavior(new WebSocketReceivedResultEventArgs(closeStatus: WebSocketCloseStatus.EndpointUnavailable, closeStatDesc: "Removing Client Due to other error"));
            }
            else
            {
                _logger.LogError($"Attempted to remove an existing web socket connection to server for connection id '{connectionId}' that no longer exists.");
            }

            _logger.LogInformation($"Completed HandleClient task for connection id '{connectionId}'.");
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
            _messageQueue?.CompleteAdding();
        }
    }
}
