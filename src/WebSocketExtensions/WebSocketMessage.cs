using System;
using System.IO;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketMessage : IDisposable
    {
        public WebSocketMessage(byte[] data, Guid connectionId) { _bindata = data; ConnectionId = connectionId; BinDataLen = _bindata.Length; IsBinary = true; }
        public WebSocketMessage(string data, Guid connectionId) { StringData = data;
            ConnectionId = connectionId;
        }
        public WebSocketMessage(string exceptionMessage, Exception e, Guid connectionId)
        {
            Exception = e;
            ConnectionId = connectionId;
            ExceptionMessage = exceptionMessage;
        }
        public WebSocketMessage(WebSocketCloseStatus? status, string closeStatDesc, Guid connectionId)
        {
            WebSocketCloseStatus = status;
            CloseStatDesc = closeStatDesc;
            ConnectionId = connectionId;
            IsDisconnect = true;
        }
        public Action<StringMessageReceivedEventArgs> StringBehavior { get; private set; }
        public Action<BinaryMessageReceivedEventArgs> BinaryBehavior { get; private set; }
              
       

        public void PageBinData()
        {
            _pagePath = Path.GetTempFileName();
            File.WriteAllBytes(_pagePath, _bindata);//todo async
            _bindata = null;
        }
        private byte[] _bindata = null;
        public Guid ConnectionId { get; private set; }
        private string _pagePath = null;
        public long BinDataLen { get; private set; } = 0;
        public bool IsBinary { get; }

        public byte[] GetBinData()
        {
            if (_bindata != null)
                return _bindata;

            return File.ReadAllBytes(_pagePath);

        }

        public void Dispose()
        {
            if (!string.IsNullOrWhiteSpace(_pagePath))
                File.Delete(_pagePath);
            _bindata = null;
            StringBehavior = null;
            BinaryBehavior = null;

        }
        public bool InMemory => _bindata != null;
        public string StringData { get; private set; }
        public WebSocketCloseStatus? WebSocketCloseStatus { get; private set; }
        public string CloseStatDesc { get; private set; }
        public bool IsDisconnect { get; private set; }
        public Exception Exception { private set; get; }
        public string ExceptionMessage { get; private set; }
    }
}
