using System;
using System.Collections.Concurrent;
using System.Threading;

namespace WebSocketExtensions
{
    public class PagingMessageQueue 
    {
        private readonly Action<string> _logError;
        private readonly long _maxPageSize;
        private long _queueBinarySizeBytes;
        private BlockingCollection<WebSocketMessage> _messageQueue;

        public PagingMessageQueue(string location, Action<string> logError, long maxPageSize = long.MaxValue)
        {
            _logError = logError;
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
                            msg.HandleMessage(_logError);
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
        public void Push(WebSocketMessage msg)
        {
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
        }


        public void CompleteAdding()
        {
            _messageQueue.CompleteAdding();
        }
    }
}
