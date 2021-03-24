using Microsoft.Net.Http.Server;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public abstract class HttpListenerWebSocketServerBehavior
    {
      
        public DateTime StartTime { get; } = DateTime.UtcNow;

       
        public virtual string GetClientId(WebSocketContext ctx)
        {
            return Guid.NewGuid().ToString();
        }
        public virtual bool OnValidateContext(WebSocketContext context, ref int errStatusCode , ref string statusDescription) { return true; }
        public virtual void OnClose(WebSocketClosedEventArgs e) { }
        public virtual void OnError(ErrorEventArgs e) { }
        public virtual void OnStringMessage(StringMessageReceivedEventArgs e) { }
        public virtual void OnBinaryMessage(BinaryMessageReceivedEventArgs e) { }
        public virtual void OnClientConnected(string clientId) { }

    }
}
