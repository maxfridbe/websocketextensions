using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketReceivedResultEventArgs : EventArgs
    {
        public WebSocketReceivedResultEventArgs(Exception ex)
        {
            Exception = ex;
        }
        public WebSocketReceivedResultEventArgs(WebSocketReceiveResult receiveResult)
        {
            ReceivedResult = receiveResult;
        }

        public WebSocketReceiveResult ReceivedResult { get; }
        public Exception Exception { get; }
    }
}