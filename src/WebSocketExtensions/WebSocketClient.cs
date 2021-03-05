using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public class WebSocketClient : WebSocketReciever, IDisposable
    {
        public WebSocketClient(Action<string, bool> logger = null, long recieveQueueLimitBytes = long.MaxValue) : base(logger)
        {
            _recieveQueueLimitBytes = recieveQueueLimitBytes;
        }
        private ClientWebSocket _client;
        private Task _tsk;
        private long _recieveQueueLimitBytes = long.MaxValue;
        CancellationTokenSource _cts = new CancellationTokenSource();
        private PagingMessageQueue _messageQueue;
        public Action<StringMessageReceivedEventArgs> MessageHandler { get; set; } = (e) => { };
        public Action<BinaryMessageReceivedEventArgs> BinaryHandler { get; set; } = (e) => { };
        public Action<WebSocketReceivedResultEventArgs> CloseHandler { get; set; } = (e) => { };
        public Action<ClientWebSocketOptions> ConfigureOptionsBeforeConnect { get; set; } = (e) => { };
        public async Task ConnectAsync(string url, CancellationToken tok = default(CancellationToken))
        {
            _client = new ClientWebSocket();
            ConfigureOptionsBeforeConnect(_client.Options);

            await _client.ConnectAsync(new Uri(url), tok);

            var binBeh = MakeSafe(BinaryHandler, "BinaryHandler");
            var strBeh = MakeSafe(MessageHandler, "MessageHandler");
            var closeBeh = MakeSafe(CloseHandler, "CloseHandler");
            
            _messageQueue = new PagingMessageQueue("WebSocketClient", _logError, _recieveQueueLimitBytes);
            
            _tsk = _client.ProcessIncomingMessages(_messageQueue, strBeh, binBeh, closeBeh, _logError, _logInfo, null, tok);
           
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
        public void Dispose()
        {
            if (_client.State == WebSocketState.Open)
            {
                try
                {
                    _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client Closing", CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    _logError($"Trying to close connection in dispose exception {e}");
                }
            }

            _client.CleanupSendMutex();

            _cts.Cancel();
            if (_tsk != null)
                _tsk.GetAwaiter().GetResult();
        }

        public Task SendStringAsync(string data, CancellationToken tok = default(CancellationToken))
        {
            return _client.SendStringAsync(data, tok);

        }

        public Task SendBytesAsync(byte[] data, CancellationToken tok = default(CancellationToken))
        {
            return _client.SendBytesAsync(data, tok);

        }
        public Task SendStreamAsync(Stream data, bool dispose = true, CancellationToken tok = default(CancellationToken))
        {
            return _client.SendStreamAsync(data, dispose, tok);

        }
    }
}
