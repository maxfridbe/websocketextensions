using System;
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
        private static ResourceLocker _locker = new ResourceLocker();

        public static Task GetContextAsync(this HttpListener listener)
        {
            return Task.Factory.FromAsync<HttpListenerContext>(listener.BeginGetContext, listener.EndGetContext, TaskCreationOptions.None);
        }

        public static void CleanupSendMutex(this WebSocket ws)
        {
            _locker.RemoveLock(ws);
        }

        public static async Task SendStreamAsync(this WebSocket ws, Stream stream, bool dispose = false, CancellationToken tok = default(CancellationToken))
        {
            if (ws == null)
                throw new Exception("SendStreamAsync:Websocket is null");

            if (ws.State != WebSocketState.Open)
            {
                throw new Exception("SendStreamAsync: Websocket not open.");
            }

          
            try
            {
                await _locker.EnterLockAsync(ws, tok);
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
                catch (Exception e)
                {
                    Console.Write(e.ToString());
                    throw;
                }
                finally
                {
                    if (dispose)
                        stream.Dispose();
                }
            }
            finally
            {
                _locker.ExitLock(ws);
            }
        }

        public static async Task SendCloseAsync(this WebSocket ws, WebSocketCloseStatus stat, string msg, CancellationToken tok = default(CancellationToken))
        {
            if (ws == null)
                throw new Exception("SendCloseAsync: Websocket is null");

            if (ws.State != WebSocketState.Open)
            {
                throw new Exception("SendCloseAsync: Websocket not open.");
            }

            await _locker.EnterLockAsync(ws,tok);

            try
            {
                await ws.CloseAsync(stat, msg, tok);
            }
            finally
            {
                _locker.ExitLock(ws);
            }
        }

        public static async Task CloseOutputNormalAsync(this WebSocket ws,  string msg, CancellationToken tok = default(CancellationToken))
        {
            if (ws == null)
                throw new Exception("CloseOutputAsync: Websocket is null");
            

            await _locker.EnterLockAsync(ws, tok);

            try
            {
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, msg, tok);
            }
            finally
            {
                _locker.ExitLock(ws);
            }
        }

        public static async Task SendBytesAsync(this WebSocket ws, byte[] data, CancellationToken tok = default(CancellationToken))
        {
            if (ws == null)
                throw new Exception("SendBytesAsync: Websocket is null");

            if (ws.State != WebSocketState.Open)
            {
                throw new Exception("SendBytesAsync: Websocket not open.");
            }

            await _locker.EnterLockAsync(ws, tok);

            try
            {
                await _send(ws, new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, tok);
            }
            finally
            {
               _locker.ExitLock(ws);
            }

        }

        public static async Task SendStringAsync(this WebSocket ws, string data, CancellationToken tok = default(CancellationToken))
        {
            if (ws == null)
                throw new Exception("SendStringAsync: Websocket is null");

            if (ws.State != WebSocketState.Open)
            {
                throw new Exception("SendStringAsync: Websocket not open.");
            }

            await _locker.EnterLockAsync(ws, tok);
            try
            {
                await _send(ws, new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, true, tok);
            }
            finally
            {
                _locker.ExitLock(ws);
            }
        }

        private static Task _send(WebSocket ws, ArraySegment<byte> messageSegment, WebSocketMessageType type, bool EOM, CancellationToken tok)
        {
            return ws.SendAsync(messageSegment, type, EOM, tok);
        }
    }
}
