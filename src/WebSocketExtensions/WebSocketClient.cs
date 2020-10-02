using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public class WebSocketClient : WebSocketReciever, IDisposable
    {
        public WebSocketClient(Action<string, bool> logger = null) : base(logger)
        {

        }
        private ClientWebSocket _client;
        private Task _tsk;

        CancellationTokenSource _cts = new CancellationTokenSource();
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

            _tsk = Task.Run(async () =>
            {
                var buff = new byte[1048576];

                using (_client)
                {
                    while (true)
                    {
                        var msg = await _client.ReceiveMessageAsync(buff, null, _cts.Token).ConfigureAwait(false);

                        if (msg.BinData != null)
                        {
                            binBeh(new BinaryMessageReceivedEventArgs(msg.BinData, _client));
                        }
                        else if (msg.StringData != null)
                        {
                            strBeh(new StringMessageReceivedEventArgs(msg.StringData, _client));
                        }
                        else if (msg.Exception != null)
                        {
                            _logError($"Exception in read thread {msg.Exception}");
                        }
                        else
                        {
                            this._logInfo($"Websocket Connection Disconnected");
                            closeBeh(new WebSocketClosedEventArgs(null, msg.WebSocketCloseStatus, msg.CloseStatDesc));
                            break;
                        }
                    }

                }

            });
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
