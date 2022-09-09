namespace WebSocketExtensions.Kestrel;
using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Http;

public abstract class KestrelWebSocketServerBehavior
{
    public DateTime StartTime { get; } = DateTime.UtcNow;

    public virtual void OnConnectionEstablished(Guid connectionId, HttpContext listenerContext) { }
    public virtual bool OnValidateContext(HttpContext listenerContext, ref int errStatusCode, ref string statusDescription) { return true; }
    public virtual void OnStringMessage(StringMessageReceivedEventArgs e) { }
    public virtual void OnBinaryMessage(BinaryMessageReceivedEventArgs e) { }
    public virtual void OnClose(WebSocketClosedEventArgs e) { }
    public virtual void OnError(ErrorEventArgs e) { }
}




