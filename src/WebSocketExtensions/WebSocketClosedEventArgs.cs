using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketClosedEventArgs : WebSocketReceivedResultEventArgs
    {
        public WebSocketClosedEventArgs(string clientid, WebSocketReceiveResult res) : base(res)
        {
            ClientId = clientid;
        }

        public string ClientId { get; }
    }
}