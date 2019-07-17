using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public abstract class WebSocketReciever
    {
        internal readonly Action<string, bool> _logger;
        private readonly bool _useThreadPool;

        internal WebSocketReciever(Action<string, bool> logger, bool useThreadPool = true)
        {
            _logger = logger;
            _useThreadPool = useThreadPool;
        }

        internal void _logInfo(string msg)
        {
            _logger?.Invoke(msg, false);
        }
        internal void _logError(string msg)
        {
            _logger?.Invoke(msg, true);
        }
        internal async Task RecieveLoop(WebSocket webSocket,
                                        Action<BinaryMessageReceivedEventArgs> binHandler,
                                        Action<StringMessageReceivedEventArgs> stringHandler,
                                        Action<WebSocketReceivedResultEventArgs> closeHandler,
                                        CancellationToken token = default(CancellationToken))
        {
            MemoryStream ms = null;
            try
            {
                var buff = new byte[1048576];
                ms = new MemoryStream();

                while (webSocket.State == WebSocketState.Open)
                {

                    var receivedResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buff), token);

                    if (receivedResult.MessageType == WebSocketMessageType.Binary
                        || receivedResult.MessageType == WebSocketMessageType.Text)
                    {
                        ms.Write(buff, 0, receivedResult.Count);
                        if (receivedResult.EndOfMessage)
                        {
                            using (ms)
                            {
                                var arr = ms.ToArray();
                                if (receivedResult.MessageType == WebSocketMessageType.Binary)
                                {
                                    BinaryMessageReceivedEventArgs args = new BinaryMessageReceivedEventArgs(arr, webSocket);

                                    if (_useThreadPool)
                                        Task.Run(() => binHandler(args));
                                    else
                                        binHandler(args);

                                }
                                else
                                {
                                    var args = new StringMessageReceivedEventArgs(Encoding.UTF8.GetString(arr), webSocket);
                                    if (_useThreadPool)
                                        Task.Run(() => stringHandler(args));
                                    else
                                        stringHandler(args);
                                }
                            }
                            ms = new MemoryStream();
                        }
                    }
                    else if (receivedResult.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

                        closeHandler(new WebSocketReceivedResultEventArgs(receivedResult));

                    }

                }
            }
            catch (Exception e)
            {
                _logError($"Exception: {e}");
                if (webSocket.State == WebSocketState.Aborted || webSocket.State == WebSocketState.Closed)
                    closeHandler(new WebSocketReceivedResultEventArgs(e));

            }
            finally
            {
                if (ms != null)
                    ms.Dispose();
            }
        }

    }
}
