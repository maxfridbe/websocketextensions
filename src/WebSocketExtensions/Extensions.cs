using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        public static async Task ProcessIncomingMessages(
            this WebSocket webSocket
            , Action<StringMessageReceivedEventArgs> strBeh
           , Action<BinaryMessageReceivedEventArgs> binBeh
           , Action<WebSocketReceivedResultEventArgs> CloseHandler
          , Action<string> logError
            , Action<string> logInfo = null
           , string clientId = null
            , long pageAfterBytes = long.MaxValue
            , CancellationToken token = default(CancellationToken))
        {
            //recieve Loop
            var buff = new byte[1048576];

            var messageQueue = new BlockingCollection<WebSocketMessage>();
            long queueBinarySize = 0;

            new Thread(() =>
            {
                foreach (var msg in messageQueue.GetConsumingEnumerable())
                {
                    using (msg)
                    {
                        if (msg.BinDataLen > 0)
                        {
                            binBeh(new BinaryMessageReceivedEventArgs(msg.GetBinData(), webSocket));

                            if(msg.NotPaged)
                                queueBinarySize -= msg.BinDataLen;
                        }
                        else if (msg.StringData != null)
                        {
                            strBeh(new StringMessageReceivedEventArgs(msg.StringData, webSocket));
                        }
                        else if (msg.Exception != null)
                        {
                            logError($"Exception in read thread {msg.Exception}");
                        }
                    }
                }

                messageQueue.Dispose();
                messageQueue = null;
            }).Start();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var msg = await webSocket.ReceiveMessageAsync(buff, clientId, token).ConfigureAwait(false);

                    if (msg.IsDisconnect)
                    {
                        logInfo?.Invoke($"Websocket Connection Disconnected");
                        CloseHandler(new WebSocketClosedEventArgs(clientId, msg.WebSocketCloseStatus, msg.CloseStatDesc));
                        break;
                    }

                    if(queueBinarySize + msg.BinDataLen > pageAfterBytes)
                    {
                        msg.PageBinData();
                    }
                    else
                    {
                        queueBinarySize += msg.BinDataLen;
                    }
                
                    messageQueue.Add(msg);
                    
                    
                }
            }
            finally
            {
                messageQueue?.CompleteAdding();
            }
        }

    }
}
