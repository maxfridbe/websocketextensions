using System;
using System.Net.WebSockets;

namespace WebSocketExtensions
{
    public class WebSocketReceivedResultEventArgs : EventArgs
    {
        public Exception Exception { get; internal set; }
        public WebSocketCloseStatus? CloseStatus { get; internal set; }
        public string CloseStatDescription { get; internal set; }

        public WebSocketReceivedResultEventArgs(Exception ex)
        {
            Exception = ex;
        }

        public WebSocketReceivedResultEventArgs(WebSocketCloseStatus? closeStatus, string closeStatDesc)
        {
            this.CloseStatus = closeStatus;
            this.CloseStatDescription = closeStatDesc;
        }
    }
}