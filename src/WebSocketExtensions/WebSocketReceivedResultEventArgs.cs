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
        public WebSocketReceivedResultEventArgs(WebSocketCloseStatus? closeStatus, string closeStatDesc)
        {
            this.CloseStatus = closeStatus;
            this.CloseStatDescription = closeStatDesc;
        }

        public Exception Exception { get; }
        public WebSocketCloseStatus? CloseStatus { get; }
        public string CloseStatDescription { get; }

    }
}