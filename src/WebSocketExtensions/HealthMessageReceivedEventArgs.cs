using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class HealthMessageReceivedEventArgs
    {
        public Guid ConnectionId { get; set; }
        public WebSocket WebSocket { get; set; }
        
        public HealthMessageReceivedEventArgs(WebSocket webSocket, Guid connectionId)
        {
            WebSocket = webSocket;
            ConnectionId = connectionId;
        }
    }
}