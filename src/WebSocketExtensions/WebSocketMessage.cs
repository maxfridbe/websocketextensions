using System;
using System.IO;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketMessage : IDisposable
    {
        public Action<StringMessageReceivedEventArgs> StringBehavior { get; private set; }
        public Action<BinaryMessageReceivedEventArgs> BinaryBehavior { get; private set; }

        public Guid ConnectionId { get; private set; }
        public bool InMemory => _bindata != null;
        public string StringData { get; private set; }
        public WebSocketCloseStatus? WebSocketCloseStatus { get; private set; }
        public string CloseStatDesc { get; private set; }
        public bool IsDisconnect { get; private set; }
        public Exception Exception { private set; get; }
        public string ExceptionMessage { get; private set; }
        public long BinDataLen { get; private set; } = 0;
        public bool IsBinary { get; }
        public bool IsString { get;}

        private WebSocket _webSocket;
        private byte[] _bindata = null;
        private string _pagePath = null;
        private static string PAGING_TEMP_PATH = Path.GetTempPath();

        public WebSocketMessage(byte[] data, Guid connectionId) {
            _bindata = data;
            ConnectionId = connectionId;
            BinDataLen = _bindata.Length;
            IsBinary = true;
        }

        public WebSocketMessage(string data, Guid connectionId)
        {
            StringData = data;
            ConnectionId = connectionId;
            IsString = true;
        }

        public WebSocketMessage(string exceptionMessage, Exception e, Guid connectionId, bool isdisconnect)
        {
            Exception = e;
            ConnectionId = connectionId;
            ExceptionMessage = exceptionMessage;
            IsDisconnect = isdisconnect;
        }

        public WebSocketMessage(WebSocketCloseStatus? status, string closeStatDesc, Guid connectionId)
        {
            WebSocketCloseStatus = status;
            CloseStatDesc = closeStatDesc;
            ConnectionId = connectionId;
            IsDisconnect = true;
        }

        public void PageBinData()
        {
            _pagePath = PAGING_TEMP_PATH + Guid.NewGuid().ToString() + ".wse";
            File.WriteAllBytes(_pagePath, _bindata);//todo async
            _bindata = null;
        }

        public byte[] GetBinData()
        {
            if (_bindata != null)
                return _bindata;

            return File.ReadAllBytes(_pagePath);
        }

        public void SetMessageHandlers(
             Action<StringMessageReceivedEventArgs> messageBehavior,
             Action<BinaryMessageReceivedEventArgs> binaryBehavior,
             WebSocket webSocket)
        {
            StringBehavior = messageBehavior;
            BinaryBehavior = binaryBehavior;
            _webSocket = webSocket;
        }

        public void HandleMessage(Action<string> logError)
        {
            if (IsBinary)
            {
                var args = new BinaryMessageReceivedEventArgs(GetBinData(), _webSocket, ConnectionId);
                BinaryBehavior(args);
            }
            else if (StringData != null)
            {
                var args = new StringMessageReceivedEventArgs(StringData, _webSocket, ConnectionId);
                StringBehavior(args);
            }
            else if (Exception != null)
            {
               
                logError($"Exception in read thread of connection {ConnectionId}:\r\n {ExceptionMessage}\r\n{Exception}\r\n{ (Exception.InnerException != null ? Exception.InnerException.ToString() : String.Empty) }");
            }
        }

        public void Dispose()
        {
            if (!string.IsNullOrWhiteSpace(_pagePath))
                File.Delete(_pagePath);

            _webSocket = null;
            _bindata = null;
            StringBehavior = null;
            BinaryBehavior = null;
        }
    }
}
