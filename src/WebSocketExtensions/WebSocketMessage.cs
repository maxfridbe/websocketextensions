using System;
using System.IO;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketMessage : IDisposable
    {
        public WebSocketMessage(byte[] data) { _bindata = data; BinDataLen = _bindata.Length; IsBinary = true; }
        public WebSocketMessage(string data) { StringData = data; }
        public WebSocketMessage(Exception e)
        {
            Exception = e;
        }
        public WebSocketMessage(WebSocketCloseStatus? status, string closeStatDesc)
        {
            WebSocketCloseStatus = status;
            CloseStatDesc = closeStatDesc;
            IsDisconnect = true;
        }
        public Action<StringMessageReceivedEventArgs> StringBehavior { get; private set; }
        public Action<BinaryMessageReceivedEventArgs> BinaryBehavior { get; private set; }

        private WebSocket _ws;

        public void SetHandlers(
            Action<StringMessageReceivedEventArgs> strBeh
           , Action<BinaryMessageReceivedEventArgs> binBeh
            , WebSocket ws)
        {
            StringBehavior = strBeh;
            BinaryBehavior = binBeh;
            _ws = ws;
        }
        public void HandleMessage(Action<string> logError)
        {
            if (IsBinary) {
                var args = new BinaryMessageReceivedEventArgs(GetBinData(), _ws);
                BinaryBehavior(args);
            }else if (StringData != null)
            {
                var args = new StringMessageReceivedEventArgs(StringData, _ws);
                StringBehavior(args);
            }else  if (Exception != null)
            {
                logError($"Exception in read thread {Exception}");
            }
        }

        public void PageBinData()
        {
            _pagePath = Path.GetTempFileName();
            File.WriteAllBytes(_pagePath, _bindata);//todo async
            _bindata = null;
        }
        private byte[] _bindata = null;
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
        }
        public bool InMemory =>  _bindata != null;
        public string StringData { get; private set; }
        public WebSocketCloseStatus? WebSocketCloseStatus { get; private set; }
        public string CloseStatDesc { get; private set; }
        public bool IsDisconnect { get; private set; }
        public Exception Exception { private set; get; }
        
    }
}
