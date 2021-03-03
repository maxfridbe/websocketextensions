using System;
using System.IO;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketMessage : IDisposable
    {
        private static string tempdir = Path.GetTempPath();


        public WebSocketMessage(byte[] data) { _bindata = data; BinDataLen = _bindata.Length; }
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

        public void PageBinData()
        {
            _pagePath = Path.Combine(tempdir, Path.GetTempFileName());
            File.WriteAllBytes(_pagePath, _bindata);//todo async
            _bindata = null;
        }
        private byte[] _bindata = null;
        private string _pagePath = null;
        public long BinDataLen { get; private set; } = 0;
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
        public bool NotPaged =>  _bindata != null;
        public string StringData { get; private set; }
        public WebSocketCloseStatus? WebSocketCloseStatus { get; private set; }
        public string CloseStatDesc { get; private set; }
        public bool IsDisconnect { get; private set; }
        public Exception Exception { private set; get; }
        
    }
}
