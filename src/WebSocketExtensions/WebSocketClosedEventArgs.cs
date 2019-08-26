using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketClosedEventArgs : WebSocketReceivedResultEventArgs
    {
        public WebSocketClosedEventArgs(string clientid, WebSocketCloseStatus? res, string closeStatDesc) : base(res, closeStatDesc)
        {
            ClientId = clientid;
        }

        public string ClientId { get; }
    }
}