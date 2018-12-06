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

            _tsk = new Task(async () =>
            {
                await RecieveLoop(_client, BinaryHandler, MessageHandler, CloseHandler, _cts.Token);
            });
            _tsk.Start();


        }

        public void Dispose()
        {
            _client.Dispose();
            _cts.Cancel();
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
