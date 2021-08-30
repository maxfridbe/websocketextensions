using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class BinaryMessageReceivedEventArgs
    {
        public byte[] Data { get; }
        public WebSocket WebSocket { get; set; }
        public Guid ConnectionId { get; set; }
        public string ClientId { get; set; }
        public BinaryMessageReceivedEventArgs(byte[] v, WebSocket webSocket, Guid connectionId)
        {
            Data = v;
            WebSocket = webSocket;
            ConnectionId = connectionId;
        }
    }
}