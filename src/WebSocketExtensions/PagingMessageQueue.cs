using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;

namespace WebSocketExtensions
{
    public class PagingMessageQueue
    {
        private readonly Action<string> _logError;
        private readonly Action<string> _logInfo;
        private readonly long _maxPageSize;
        private long _queueBinarySizeBytes;
        private BlockingCollection<WebSocketMessage> _messageQueue;

        public Action<StringMessageReceivedEventArgs> StringBehavior { get; private set; }
        public Action<BinaryMessageReceivedEventArgs> BinaryBehavior { get; private set; }
        public Action<WebSocketClosedEventArgs> CloseBehavior { get; private set; }

        private WebSocket _ws;


        public PagingMessageQueue(string location, Action<string> logError, Action<string> logInfo, long maxPageSize = long.MaxValue)
        {
            _logError = logError;
            _logInfo = logInfo;
            _maxPageSize = maxPageSize;
            _queueBinarySizeBytes = 0L;
            _messageQueue = new BlockingCollection<WebSocketMessage>();

            new Thread(() =>
            {
                foreach (var msg in _messageQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        using (msg)
                        {
                            HandleMessage(msg);
                            if (msg.IsBinary && msg.InMemory)
                            {
                                _queueBinarySizeBytes -= msg.BinDataLen;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logError($"{location}: Error in processing Queue, {e.ToString()}");
                    }
                }
                _messageQueue.Dispose();
                _messageQueue = null;
            }).Start();
        }
        public bool Push(WebSocketMessage msg)
        {
            if (msg.IsDisconnect)
            {
                CloseBehavior(new WebSocketClosedEventArgs(msg.ConnectionId, msg.WebSocketCloseStatus, msg.CloseStatDesc));
                _logInfo.Invoke($"Websocket Connection Disconnected ConnectionId:{msg.ConnectionId}");
                return false;
            }

            if (msg.IsBinary)
            {
                if ((_queueBinarySizeBytes + msg.BinDataLen) > _maxPageSize)
                {
                    msg.PageBinData();
                }
                else
                {
                    _queueBinarySizeBytes += msg.BinDataLen;
                }
            }
            _messageQueue.Add(msg);
            return true;
        }


        public void CompleteAdding()
        {
            _messageQueue.CompleteAdding();
        }

        public void SetMessageHandler(
             Action<StringMessageReceivedEventArgs> strBeh
            , Action<BinaryMessageReceivedEventArgs> binBeh
            , Action<WebSocketClosedEventArgs> closeBeh
             , WebSocket ws)
        {
            StringBehavior = strBeh;
            BinaryBehavior = binBeh;
            CloseBehavior = closeBeh;
            _ws = ws;
        }

       

        public void HandleMessage(WebSocketMessage msg)
        {
            if (msg.IsBinary)
            {
                var args = new BinaryMessageReceivedEventArgs(msg.GetBinData(), _ws, msg.ConnectionId);
                BinaryBehavior(args);
            }
            else if (msg.StringData != null)
            {
                var args = new StringMessageReceivedEventArgs(msg.StringData, _ws, msg.ConnectionId);
                StringBehavior(args);
            }
            else if (msg.Exception != null)
            {
                if (msg.Exception is OperationCanceledException)
                    return;

                _logError($"Exception in read thread of connection {msg.ConnectionId}:\r\n {msg.ExceptionMessage}\r\n{msg.Exception}\r\n{ (msg.Exception.InnerException != null ? msg.Exception.InnerException.ToString() : String.Empty) }");
            }
        }
    }
}
