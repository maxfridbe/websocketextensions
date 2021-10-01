using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public static class Extensions
    {
        public async static Task<WebSocketMessage> ReceiveMessageAsync(this WebSocket webSocket,
                                        ArraySegment<byte> buff,
                                        Guid connectionId,
                                        CancellationToken token = default(CancellationToken))
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        var receivedResult = await webSocket.ReceiveAsync(buff, token).ConfigureAwait(false);

                        if (receivedResult.MessageType == WebSocketMessageType.Binary
                            || receivedResult.MessageType == WebSocketMessageType.Text)
                        {

                            ms.Write(buff.Array, 0, receivedResult.Count);
                            if (receivedResult.EndOfMessage)
                            {
                                byte[] arr = ms.ToArray();
                                if (receivedResult.MessageType == WebSocketMessageType.Binary)
                                {
                                    return new WebSocketMessage(arr, connectionId);
                                }
                                else
                                {
                                    return new WebSocketMessage(Encoding.UTF8.GetString(arr), connectionId);
                                }

                            }
                        }
                        else if (receivedResult.MessageType == WebSocketMessageType.Close)
                        {

                            if (webSocket.State != WebSocketState.Closed)
                                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Honoring disconnect", token);

                            var closeStat = receivedResult.CloseStatus;
                            var closeStatDesc = receivedResult.CloseStatusDescription;

                            return new WebSocketMessage(closeStat, closeStatDesc, connectionId);
                        }
                    }

                    return new WebSocketMessage(null, $"Websocket State is {webSocket.State}", connectionId);
                }
            }
            catch (WebSocketException ex)
            {
                switch (ex.WebSocketErrorCode)
                {
                    case WebSocketError.ConnectionClosedPrematurely:
                        return new WebSocketMessage(WebSocketCloseStatus.EndpointUnavailable, "Connection Closed Prematurely", connectionId);
                    default:
                        return new WebSocketMessage($"ErrorCode: {ex.WebSocketErrorCode}", ex, connectionId);
                }
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested)
                {
                    if (webSocket.State == WebSocketState.Open)
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Thread requested disconnect", token);
                    return new WebSocketMessage(status: WebSocketCloseStatus.EndpointUnavailable, closeStatDesc: "Closing due to CancellationToken abort", connectionId: connectionId);
                }

                return new WebSocketMessage("Non WebSocketException", e, connectionId);
            }
        }

        public static async Task ProcessIncomingMessages(
            this WebSocket webSocket,
            PagingMessageQueue messageQueue,
            Guid connectionId,
            Action<StringMessageReceivedEventArgs> messageBehavior,
            Action<BinaryMessageReceivedEventArgs> binaryBehavior,
            Action<WebSocketReceivedResultEventArgs> closeBehavior,
            Action<string> logInfo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
          //  await Task.Factory.StartNew(async () => {
                byte[] messageBufferBytes = new byte[1048576];
                ArraySegment<byte> messageBuffer = new ArraySegment<byte>(messageBufferBytes);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var msg = await webSocket.ReceiveMessageAsync(messageBuffer, connectionId, cancellationToken).ConfigureAwait(false);

                    if (msg.IsDisconnect)
                    {
                        logInfo.Invoke($"Websocket Connection Disconnected");
                        closeBehavior(new WebSocketClosedEventArgs(connectionId, msg.WebSocketCloseStatus, msg.CloseStatDesc));
                        break;
                    }

                    msg.SetMessageHandlers(messageBehavior, binaryBehavior, webSocket);

                    messageQueue.Push(msg);
                }
          //  });
        }
    }
}
