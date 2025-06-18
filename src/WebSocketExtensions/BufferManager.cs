using System.Collections.Concurrent;

namespace WebSocketExtensions
{
    // A simple buffer manager
    public static class BufferManager
    {
        private static readonly ConcurrentBag<byte[]> _buffers = new ConcurrentBag<byte[]>();
        private const int BufferSize = 1024 * 1024; // 1MB

        public static byte[] GetBuffer()
        {
            if (_buffers.TryTake(out var buffer))
            {
                return buffer;
            }
            return new byte[BufferSize];
        }

        public static void ReturnBuffer(byte[] buffer)
        {
            if(buffer != null)
                _buffers.Add(buffer);
        }
    }
}
