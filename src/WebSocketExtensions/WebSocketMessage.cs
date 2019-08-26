using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketMessage
    {
        public Exception Exception { private set; get; }

        public WebSocketMessage(byte[] data) { BinData = data; }
        public WebSocketMessage(string data) { StringData = data; }

        public WebSocketMessage(Exception e)
        {
            Exception = e;
        }

        public WebSocketMessage(WebSocketCloseStatus? status, string closeStatDesc)
        {
            WebSocketCloseStatus = status;
            CloseStatDesc = closeStatDesc;
        }

        public byte[] BinData { get; private set; }
        public string StringData { get; private set; }
        public WebSocketCloseStatus? WebSocketCloseStatus { get; private set; }
        public string CloseStatDesc { get; private set; }
    }
}
