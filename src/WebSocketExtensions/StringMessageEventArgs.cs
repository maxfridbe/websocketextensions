using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class StringMessageReceivedEventArgs : EventArgs
    {
        public StringMessageReceivedEventArgs(string v, WebSocket webSocket)
        {
            Data = v;
            WebSocket = webSocket;
        }

        public string Data { get; }
        public WebSocket WebSocket { get; }
    }
}
