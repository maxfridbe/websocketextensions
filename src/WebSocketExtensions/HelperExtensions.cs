using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{

    public static class HelperExtensions
    {
        public static Task GetContextAsync(this HttpListener listener)
        {
            return Task.Factory.FromAsync<HttpListenerContext>(listener.BeginGetContext, listener.EndGetContext, TaskCreationOptions.None);
        }

        public static async Task SendStreamAsync(this WebSocket ws, Stream stream, bool dispose = false, CancellationToken tok =  default(CancellationToken))
        {
            try
            {
                var buffSize = 1024 * 1024;
                var len = stream.Length;
                var chunksize = len > buffSize ? buffSize : len;
                var remaining = len;
                byte[] buffer = new byte[buffSize];
                while (remaining > 0)
                {
                    if (remaining < buffer.Length)
                    {
                        buffer = new byte[remaining];
                    }
                    var read = stream.Read(buffer, 0, buffer.Length);
                    bool isLast = (remaining - buffer.Length) == 0;
                    await _send(ws, new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, isLast, tok);
                    remaining -= buffer.Length;
                }
            }
            finally {
                if (dispose)
                    stream.Dispose();
            }
        }


        public static Task SendBytesAsync(this WebSocket ws, byte[] data, CancellationToken tok = default(CancellationToken))
        {
            return _send(ws, new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, tok);

        }

        public static Task SendStringAsync(this WebSocket ws, string data, CancellationToken tok = default(CancellationToken))
        {
            return _send(ws, new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, true, tok);

        }

        private static Task _send(WebSocket ws, ArraySegment<byte> messageSegment, WebSocketMessageType type, bool EOM, CancellationToken tok)
        {
            Monitor.Enter(ws);
            try
            {
                return ws.SendAsync(messageSegment, type, EOM, tok);
            }
            finally
            {
                Monitor.Exit(ws);
            }
        }
    }
}
