using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class BinaryMessageReceivedEventArgs 
    {
        public byte[] Data { get; }
        public WebSocket WebSocket { get; set; }
        public BinaryMessageReceivedEventArgs(byte[] v, WebSocket webSocket)
        {
            Data = v;
            WebSocket = webSocket;
        }
    }
}