using Microsoft.Net.Http.Server;
using System;
using System.IO;

namespace WebSocketExtensions.WebListenerServer
{

    public abstract class WebListenerWebSocketServerBehavior
    {
        public DateTime StartTime { get; } = DateTime.UtcNow;

        public virtual void OnConnectionEstablished(Guid connectionId, RequestContext requestContext) { }
        public virtual bool OnValidateContext(RequestContext requestContext, ref int errorStatusCode, ref string statusDescription) { return true; }
        public virtual void OnStringMessage(StringMessageReceivedEventArgs e) { }
        public virtual void OnBinaryMessage(BinaryMessageReceivedEventArgs e) { }
        public virtual void OnClose(WebSocketClosedEventArgs e) { }
        public virtual void OnError(ErrorEventArgs e) { }
    }

}