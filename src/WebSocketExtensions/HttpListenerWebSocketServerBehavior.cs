﻿using System;
using System.IO;
using System.Net;

namespace WebSocketExtensions
{
    public abstract class HttpListenerWebSocketServerBehavior
    {
        public DateTime StartTime { get; } = DateTime.UtcNow;

        public virtual void OnConnectionEstablished(Guid connectionId, HttpListenerContext listenerContext) { }
        public virtual bool OnValidateContext(HttpListenerContext listenerContext, ref int errStatusCode, ref string statusDescription) { return true; }
        public virtual void OnStringMessage(StringMessageReceivedEventArgs e) { }
        public virtual void OnBinaryMessage(BinaryMessageReceivedEventArgs e) { }
        public virtual void OnClose(WebSocketClosedEventArgs e) { }
        public virtual void OnError(ErrorEventArgs e) { }
    }
}
