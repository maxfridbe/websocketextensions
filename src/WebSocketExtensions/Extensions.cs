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
                                        byte[] buff,
                                        string clientId = null,
                                        CancellationToken token = default(CancellationToken))
        {

            try
            {

                using (var ms = new MemoryStream())
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        var receivedResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buff), token).ConfigureAwait(false);

                        if (receivedResult.MessageType == WebSocketMessageType.Binary
                            || receivedResult.MessageType == WebSocketMessageType.Text)
                        {
                            ms.Write(buff, 0, receivedResult.Count);
                            if (receivedResult.EndOfMessage)
                            {
                                byte[] arr = ms.ToArray();
                                if (receivedResult.MessageType == WebSocketMessageType.Binary)
                                {
                                    return new WebSocketMessage(arr);
                                }
                                else
                                {
                                    return new WebSocketMessage(Encoding.UTF8.GetString(arr));
                                }

                            }
                        }
                        else if (receivedResult.MessageType == WebSocketMessageType.Close)
                        {

                            if (webSocket.State != WebSocketState.Closed)
                                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Honoring disconnect", token);
                               // Task.Run(() => webSocket.SendCloseAsync(WebSocketCloseStatus.NormalClosure, "Ack Disconnect Req", CancellationToken.None));

                            var closeStat = receivedResult.CloseStatus;
                            var closeStatDesc = receivedResult.CloseStatusDescription;

                            return new WebSocketMessage(closeStat, closeStatDesc);

                        }
                    }

                    return new WebSocketMessage(null, $"Websocket State is {webSocket.State}");
                }

            }
            catch (WebSocketException ex)
            {
                switch (ex.WebSocketErrorCode)
                {
                    case WebSocketError.ConnectionClosedPrematurely:
                        return new WebSocketMessage(WebSocketCloseStatus.EndpointUnavailable, "Connection Closed Prematurely");
                    default:
                        return new WebSocketMessage(ex);
                }
            }
            catch (Exception e)
            {
                return new WebSocketMessage(e);

            }
        }

    }
}
