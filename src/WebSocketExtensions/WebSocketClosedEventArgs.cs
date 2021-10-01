using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketClosedEventArgs : WebSocketReceivedResultEventArgs
    {
        public Guid ConnectionId { get; }

        public WebSocketClosedEventArgs(Guid connectionId, WebSocketReceivedResultEventArgs args) : base(args.CloseStatus, args.CloseStatDescription)
        {
            Exception = args.Exception;
            ConnectionId = connectionId;
        }

        public WebSocketClosedEventArgs(Guid connectionId, WebSocketCloseStatus? res, string closeStatDesc) : base(res, closeStatDesc)
        {
            ConnectionId = connectionId;
        }
    }
}