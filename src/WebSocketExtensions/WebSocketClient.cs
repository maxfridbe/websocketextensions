using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public class WebSocketClient : WebSocketReciever, IDisposable
    {
        public Action<StringMessageReceivedEventArgs> MessageHandler { get; set; } = (e) => { };
        public Action<BinaryMessageReceivedEventArgs> BinaryHandler { get; set; } = (e) => { };
        public Action<WebSocketReceivedResultEventArgs> CloseHandler { get; set; } = (e) => { };
        public Action<ClientWebSocketOptions> ConfigureOptionsBeforeConnect { get; set; } = (e) => { };

        private ClientWebSocket _client;
        private readonly int _streamSendBufferLen;
        private readonly int _incomingBufferSize;
        private Guid _clientId;
        private readonly int? _keepAliveIntervalS;
        private byte[] _streamSendBuffer;
        private Task _incomingMessagesTask;
        private PagingMessageQueue _messageQueue;
        private Action<WebSocketReceivedResultEventArgs> _closeBehavior;
        private long _recieveQueueLimitBytes = long.MaxValue;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private bool _disposing = false;

        public WebSocketClient(Action<string, bool> logger = null,
                                int streamSendBufferLen = 1024 * 1024,
                                int incomingBufferSize = 1048576 * 4,//4mb
                                Guid? clientId = null,
                                int? keepAliveIntervalS = null,
                                long recieveQueueLimitBytes = long.MaxValue) : base(logger)
        {
            _streamSendBufferLen = streamSendBufferLen;
            _incomingBufferSize = incomingBufferSize;
            _clientId = clientId ?? Guid.NewGuid();
            _keepAliveIntervalS = keepAliveIntervalS;
            _recieveQueueLimitBytes = recieveQueueLimitBytes;
        }

        public async Task ConnectAsync(string url, CancellationToken tok = default(CancellationToken))
        {
            _client = new ClientWebSocket();

            //System.Net.ServicePointManager.MaxServicePointIdleTime = int.MaxValue;


            ConfigureOptionsBeforeConnect(_client.Options);
            _client.Options.KeepAliveInterval = _keepAliveIntervalS.HasValue ? TimeSpan.FromSeconds(_keepAliveIntervalS.Value) : TimeSpan.Zero;

            await _client.ConnectAsync(new Uri(url), tok);

            var messageBehavior = MakeSafe(MessageHandler, "MessageHandler");
            var binaryBehavior = MakeSafe(BinaryHandler, "BinaryHandler");
            _closeBehavior = MakeSafe(CloseHandler, "CloseHandler");

            _messageQueue = new PagingMessageQueue("WebSocketClient", _logError, _recieveQueueLimitBytes);

            _incomingMessagesTask = Task.Factory.StartNew(async () =>
            {
                await _client.ProcessIncomingMessages(_messageQueue, _clientId, messageBehavior, binaryBehavior, _closeBehavior, _logInfo, _incomingBufferSize, _cancellationTokenSource.Token);
                _logError("WebSocketClient: Completed ProcessIncomingMessages");
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
                    _logError($"WebSocketClient: Error in handler '{handlerName}': {e}");
                }
            });
        }

        public Task SendStringAsync(string data, CancellationToken tok = default(CancellationToken))
        {
            return _client.SendStringAsync(data, tok);
        }

        public Task SendBytesAsync(byte[] data, CancellationToken tok = default(CancellationToken))
        {
            return _client.SendBytesAsync(data, tok);

        }
        public Task SendStreamAsync(Stream data, byte[] streamSendBuffer = null, bool dispose = true, CancellationToken tok = default(CancellationToken))
        {
            if (streamSendBuffer == null)
            {
                if (_streamSendBuffer == null)
                    _streamSendBuffer = new byte[_streamSendBufferLen];

                streamSendBuffer = _streamSendBuffer;
            }
            return _client.SendStreamAsync(data, streamSendBuffer, dispose, tok);
        }

        public void Dispose()
        {
            if (_disposing)
                return;

            _disposing = true;

            if (_client.State == WebSocketState.Open)
            {
                try
                {
                    _client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client Closing", CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    if (_client.State != WebSocketState.Aborted)
                        _logError($"WebSocketClient: Trying to close connection in dispose exception {e} {e.StackTrace}");
                }

                _closeBehavior(new WebSocketReceivedResultEventArgs(WebSocketCloseStatus.NormalClosure, "Closed because disposing"));
            }

            _client.CleanupSendMutex();

            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();

            if (_incomingMessagesTask != null)
            {
                _incomingMessagesTask.GetAwaiter().GetResult();
                _messageQueue.CompleteAdding();
            }

            _client.Dispose();
        }
    }
}
