using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class StringMessageReceivedEventArgs : EventArgs
    {
        public StringMessageReceivedEventArgs(string v, WebSocket webSocket, Guid connectionId)
        {
            Data = v;
            WebSocket = webSocket;
            ConnectionId = connectionId;
        }

        public string Data { get; }
        public string ClientId { get; set; }
        public WebSocket WebSocket { get; }
        public Guid ConnectionId { get; set; }
    }
}
