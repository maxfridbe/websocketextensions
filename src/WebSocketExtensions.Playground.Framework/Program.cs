using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Net.Http.Server;

namespace WebSocketExtensions.Playground.Framework
{
    class Program
    {
        private static readonly string ServerUrl = "http://localhost:5000/ws/";

        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            var server = new WebListenerWebSocketServer((string a, bool b) => { Console.WriteLine(a); }, 200L * 1024 * 1024);
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            int recievedSize = 0;
            beh.BinaryMessageHandler = (e) =>
            {
                recievedSize = e.Data.Length;
            };

            beh.ConnectionEstablished = (a, b) =>
            {
                Task.Run(async () =>
                {
                    while (true)
                    {
                        byte[] buffer = Encoding.UTF8.GetBytes($"Do the thing!");

                        // Send the message using SendAsync  
                        await server.SendBytesAsync(a, buffer);

                        await Task.Delay(250);
                    }
                });

                Task.Run(async () =>
                {
                    while (true)
                    {
                        byte[] buffer = Encoding.UTF8.GetBytes($"Do the thing!");

                        // Send the message using SendAsync  
                        await server.SendBytesAsync(a, buffer);

                        await Task.Delay(375);
                    }
                });

                Task.Run(async () =>
                {
                    while (true)
                    {
                        byte[] buffer = Encoding.UTF8.GetBytes($"Do the thing!");

                        // Send the message using SendAsync  
                        await server.SendBytesAsync(a, buffer);

                        await Task.Delay(888);
                    }
                });
            };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            var s = _getFile("tst", 10);
            var bytes = File.ReadAllBytes(s);

            var client = new WebSocketClient((string a, bool b) => { Console.WriteLine(a); })
            {
                MessageHandler = (e) => Console.WriteLine(e),
            };
            int sent = 0;
            client.BinaryHandler = async (e) =>
            {
                await client.SendBytesAsync(bytes);
                Interlocked.Increment(ref sent);
                Console.WriteLine($"Sent {sent}");
                await Task.Delay(100);
            };

            await client.ConnectAsync($"ws://localhost:{port}/aaa");

            //try
            //{
            //    var serverTask = StartWebSocketServer();
            //    var clientTask = StartWebSocketClient();

            //    await Task.WhenAll(serverTask, clientTask);
            //}
            //catch (Exception e)
            //{
            //    Console.WriteLine($"{Task.CurrentId}: {e}");
            //}

            Console.ReadLine();
        }

        public class testBeh : WebListenerWebSocketServerBehavior
        {
            public Action<StringMessageReceivedEventArgs> StringMessageHandler = (_) => { };
            public Action<BinaryMessageReceivedEventArgs> BinaryMessageHandler = (_) => { };
            public Action<WebSocketClosedEventArgs> ClosedHandler = (_) => { };
            public Action<Guid, RequestContext> ConnectionEstablished = (a, b) => { };
            public override void OnConnectionEstablished(Guid connectionId, RequestContext requestContext)
            {
                base.OnConnectionEstablished(connectionId, requestContext);
                ConnectionEstablished(connectionId, requestContext);
            }
            public override void OnStringMessage(StringMessageReceivedEventArgs e)
            {
                StringMessageHandler(e);
            }
            public override void OnBinaryMessage(BinaryMessageReceivedEventArgs e)
            {
                BinaryMessageHandler(e);
            }
            public override void OnClose(WebSocketClosedEventArgs e)
            {
                ClosedHandler(e);
            }
        }

        static int _FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static string _getFile(string name, int sizeInMB = 10, [CallerMemberName] string caller = "")
        {
            var filename = $"{Path.GetTempPath()}{caller}_{name}.tmp";
            if (!File.Exists(filename))
            {
                byte[] data = new byte[8192];
                Random rng = new Random();
                using (FileStream stream = File.OpenWrite(filename))
                {
                    for (int i = 0; i < sizeInMB * 128; i++)
                    {
                        rng.NextBytes(data);
                        stream.Write(data, 0, data.Length);
                    }
                }
            }

            return filename;
        }

        private static async Task StartWebSocketServer()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(ServerUrl);
            listener.Start();

            while (true)
            {
                HttpListenerContext context = await listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    WebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
                    WebSocket webSocket = webSocketContext.WebSocket;

                    // Handle WebSocket communication here
                    // ...

                    var receiveTask = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                await ReceiveMessage(webSocket);

                                await Task.Delay(10000);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Server - ReceiveAsync: {e}");
                            }
                        }
                    });

                    var sendTask = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                byte[] buffer = Encoding.UTF8.GetBytes($"Do the thing!");
                                ArraySegment<byte> segment = new ArraySegment<byte>(buffer);

                                // Send the message using SendAsync  
                                await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);

                                Console.WriteLine($"Server sent message.");
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Server - SendAsync: {e}");
                            }
                        }
                    });

                    await Task.WhenAll(receiveTask, sendTask);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }

        private static async Task StartWebSocketClient()
        {
            await Task.Delay(1000); // Give the server a moment to start

            ClientWebSocket webSocket = new ClientWebSocket();
            await webSocket.ConnectAsync(new Uri("ws://localhost:5000/ws"), CancellationToken.None);

            var receiveTask = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await ReceiveMessage(webSocket);

                        await Task.Delay(10000);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Client - ReceiveAsync: {e}");
                    }
                }
            });

            var sendTask = Task.Run(async () =>
            {
                var bytes = File.ReadAllBytes(@"C:\Users\ryanb-docker\Downloads\25861521.pdf");

                while (true)
                {
                    try
                    {
                        ArraySegment<byte> segment = new ArraySegment<byte>(bytes);

                        // Send the message using SendAsync  
                        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);


                        Console.WriteLine($"Client sent message.");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Client - SendAsync: {e}");
                    }
                }
            });



            //int numberOfTasks = 5;
            //Task[] sendTasks = new Task[numberOfTasks];

            //for (int i = 0; i < numberOfTasks; i++)
            //{
            //    sendTasks[i] = Task.Run(async () =>
            //    {
            //        try
            //        {
            //            byte[] buffer = Encoding.UTF8.GetBytes($"Hello, WebSocket from Task {Task.CurrentId}!");
            //            ArraySegment<byte> segment = new ArraySegment<byte>(buffer);

            //            // Send the message using SendAsync  
            //            await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            //        }
            //        catch(Exception e)
            //        {
            //            Console.WriteLine($"{Task.CurrentId}: {e}");

            //            Console.WriteLine(System.Environment.NewLine);
            //            Console.WriteLine(System.Environment.NewLine);
            //        }
            //    });
            //}

            // Optionally, wait for all sendTasks to complete  
            await Task.WhenAll(receiveTask, sendTask);
        }

        private static async Task ReceiveMessage(WebSocket websocket)
        {
            byte[] buffer = new byte[4 * 1024];

            if (websocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                using (var memoryStream = new MemoryStream())
                {
                    do
                    {
                        result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        memoryStream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("Server requested to close the connection.");
                        await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    else
                    {
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        string receivedMessage = Encoding.UTF8.GetString(memoryStream.ToArray());
                        Console.WriteLine($"Received message: {receivedMessage}");
                    }
                }
            }
        }
    }
}

