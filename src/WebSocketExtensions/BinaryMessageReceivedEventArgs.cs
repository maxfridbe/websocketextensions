using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class BinaryMessageReceivedEventArgs
    {
        public Guid ConnectionId { get; set; }
        public WebSocket WebSocket { get; set; }
        public byte[] Data { get; }
        
        public BinaryMessageReceivedEventArgs(byte[] v, WebSocket webSocket, Guid connectionId)
        {
            Data = v;
            WebSocket = webSocket;
            ConnectionId = connectionId;
        }
    }
}