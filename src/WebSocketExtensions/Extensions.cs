﻿using System;
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
                                        ArraySegment<byte> buff,
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
                        return new WebSocketMessage($"ErrorCode: {ex.WebSocketErrorCode}", ex);
                }
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested)
                {
                    if (webSocket.State == WebSocketState.Open) 
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Thread requested disconnect", token);
                    return new WebSocketMessage(status: WebSocketCloseStatus.EndpointUnavailable, closeStatDesc: "Closing due to CancellationToken abort");
                }

                return new WebSocketMessage("Non WebSocketException", e);

            }
        }

        public static async Task ProcessIncomingMessages(
            this WebSocket webSocket
            , PagingMessageQueue messageQueue
            , Action<StringMessageReceivedEventArgs> strBeh
            , Action<BinaryMessageReceivedEventArgs> binBeh
            , Action<WebSocketReceivedResultEventArgs> CloseHandler
            , Action<string> logError
            , Action<string> logInfo
            , string clientId = null
            , CancellationToken token = default(CancellationToken))
        {
            var buff = new byte[1048576];


            var s = new ArraySegment<byte>(buff);
            while (!token.IsCancellationRequested)
            {
                var msg = await webSocket.ReceiveMessageAsync(s, token).ConfigureAwait(false);

                if (msg.IsDisconnect)
                {
                    logInfo.Invoke($"Websocket Connection Disconnected");
                    CloseHandler(new WebSocketClosedEventArgs(clientId, msg.WebSocketCloseStatus, msg.CloseStatDesc));
                    break;
                }
                msg.SetHandlers(strBeh, binBeh, webSocket);

                messageQueue.Push(msg);
            }

        }

    }
}
