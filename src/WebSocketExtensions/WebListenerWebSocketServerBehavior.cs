using Microsoft.Net.Http.Server;
using System;
using System.IO;

namespace WebSocketExtensions
{
    public abstract class WebListenerWebSocketServerBehavior
    {

        public DateTime StartTime { get; } = DateTime.UtcNow;


        public virtual string GetClientId(RequestContext ctx)
        {
         
            return Guid.NewGuid().ToString();
        }
        public virtual bool OnValidateContext(RequestContext context, ref int errStatusCode, ref string statusDescription) { return true; }
        public virtual void OnClose(WebSocketClosedEventArgs e) { }
        public virtual void OnError(ErrorEventArgs e) { }
        public virtual void OnStringMessage(StringMessageReceivedEventArgs e) { }
        public virtual void OnBinaryMessage(BinaryMessageReceivedEventArgs e) { }
        public virtual void OnClientConnected(string clientId, Guid connectionId) { }

    }
}
