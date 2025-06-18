using System;
using System.Collections.Concurrent;
using System.Threading;

namespace WebSocketExtensions
{
    public record PagingMessageQueueStats(int Count, long QueueBinarySizeBytes);
    public class PagingMessageQueue : IDisposable
    {
        private BlockingCollection<WebSocketMessage> _messageQueue;
        private readonly long _maxPageSize;
        private readonly string _location;
        private readonly Action<string> _logError;

        private long _queueBinarySizeBytes;

        public PagingMessageQueue(string location, Action<string> logError, long maxPageSize = long.MaxValue)
        {
            _messageQueue = new BlockingCollection<WebSocketMessage>();
            _location = location;
            _logError = logError;
            _maxPageSize = maxPageSize;

            _queueBinarySizeBytes = 0L;

            var t = new Thread(() =>
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
            });
            t.IsBackground = false;

            t.Start();
        }
        public PagingMessageQueueStats GetQueueStats()
        {
            if (_messageQueue != null)
            {
                return new PagingMessageQueueStats(_messageQueue.Count, _queueBinarySizeBytes);
            }
            return new PagingMessageQueueStats(0, _queueBinarySizeBytes); ;
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



        public void Dispose()
        {
            try
            {
                _messageQueue?.CompleteAdding();
            }
            catch (Exception e)
            {
                _logError($"{_location}: Error in processing Queue Dispose, {e.ToString()}");
            }
            //handled by thread cleanup
            // _messageQueue.Dispose();
        }
    }
}
