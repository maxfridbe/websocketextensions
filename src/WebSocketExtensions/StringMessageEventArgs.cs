using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class StringMessageReceivedEventArgs : EventArgs
    {
        public Guid ConnectionId { get; set; }
        public WebSocket WebSocket { get; }
        public string Data { get; }

        public StringMessageReceivedEventArgs(string v, WebSocket webSocket, Guid connectionId)
        {
            Data = v;
            WebSocket = webSocket;
            ConnectionId = connectionId;
        }
    }
}
