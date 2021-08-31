using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace WebSocketExtensions.Tests
{
    public class IntegrationTests_WebListener
    {
        private ITestOutputHelper _output;

        public IntegrationTests_WebListener(ITestOutputHelper output)
        {
            _output = output;
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

        [Fact]
        public void TestCreateServer()
        {
            //arrange

            //act
            var server = new WebListenerWebSocketServer();

            //assert

        }

        [Fact]
        public async Task TestCreateServer_start()
        {
            //arrange
            var port = _FreeTcpPort();
            using (var server = new WebListenerWebSocketServer())
            {

                //act
                await server.StartAsync($"http://localhost:{port}/");
                await Task.Delay(100);
                //assert
            }

        }
        public class testBeh : WebListenerWebSocketServerBehavior
        {
            public Action<StringMessageReceivedEventArgs> StringMessageHandler = (_) => { };
            public Action<BinaryMessageReceivedEventArgs> BinaryMessageHandler = (_) => { };
            public Action<WebSocketClosedEventArgs> ClosedHandler = (_) => { };

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

        [Fact]
        public async Task TestCreateServer_connect()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            server.AddRouteBehavior("/aaa", () => new testBeh());
            await server.StartAsync($"http://localhost:{port}/");

            var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/aaa"), CancellationToken.None);

            //act

            //assert

        }


        [Fact]
        public async Task TestCreateServer_connect_recv_text()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            string data = null;
            var port = _FreeTcpPort();

            Action<StringMessageReceivedEventArgs> handler = (s) => data = s.Data;

            server.AddRouteBehavior("/aaa", () => new testBeh() { StringMessageHandler = handler });
            await server.StartAsync($"http://localhost:{port}/");

            var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/aaa"), CancellationToken.None);


            //act
            await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hi")), WebSocketMessageType.Text, true, CancellationToken.None);

            await Task.Delay(100);

            //assert
            Assert.Equal("hi", data);

        }

        [Fact]
        public async Task TestCreateServer_connect_recv_echo()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) => { e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None); };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/aaa"), CancellationToken.None);

            //act
            await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hi")), WebSocketMessageType.Text, true, CancellationToken.None);
            var byt = new byte[1024];
            await Task.Delay(100);
            var read = await client.ReceiveAsync(new ArraySegment<byte>(byt), CancellationToken.None);


            //assert
            Assert.Equal("hihi", Encoding.UTF8.GetString(new ArraySegment<byte>(byt, 0, read.Count)));

        }


        [Fact]
        public async Task TestServerDispose()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) => { e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None); };
            var u = $"://localhost:{port}/";
            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync("http" + u);
            var closed = false;
            var client = new WebSocketClient()
            {
                CloseHandler = (c) =>
                closed = true
            };
            await client.ConnectAsync("ws" + u + "aaa");

            //act

            await Task.Delay(100);
            server.Dispose();
            await Task.Delay(100);
            //asssert
            Assert.True(closed);
            client.Dispose();
        }



        [Fact]
        public async Task TestClientDispose()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) => { e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None); };
            var u = $"://localhost:{port}/";
            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync("http" + u);
            var closed = false;
            var client = new WebSocketClient()
            {
                CloseHandler = (c) =>
                    closed = true
            };
            await client.ConnectAsync("ws" + u + "aaa");

            //act

            await Task.Delay(100);

            client.Dispose();
            await Task.Delay(100);

            server.Dispose();
            await Task.Delay(100);
            //asssert
            Assert.True(closed);

        }



        [Fact]
        public async Task TestServer_ClientDisconnect()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();
            bool disconnected = false;
            var beh = new testBeh()
            {
                ClosedHandler = (e) => {
                    disconnected = true;

                }
            };
            beh.StringMessageHandler = (e) => { e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None); };
            var u = $"://localhost:{port}/";
            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync("http" + u);
            var closed = false;
            var client = new WebSocketClient()
            {
                CloseHandler = (c) =>
                    closed = true
            };
            await client.ConnectAsync("ws" + u + "aaa");

            //act

            await Task.Delay(100);

            client.Dispose();
            await Task.Delay(100);

            Assert.True(disconnected);

            server.Dispose();
            await Task.Delay(100);
            //asssert
            Assert.True(closed);

        }






        [Fact]
        public async Task TestMemLeak()
        {

            //arrange
            //var server = new HttpListenerWebSocketServer();
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            //var beh = new testBeh()
            //{
            //};
            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        //await Task.Delay(100);
                        await e.WebSocket.SendStringAsync(string.Empty, CancellationToken.None);
                    }
                    catch { }
                });
                Task.Run(async () =>
                {
                    try
                    {
                        //await Task.Delay(100);
                        await e.WebSocket.SendStringAsync(string.Empty, CancellationToken.None);
                    }
                    catch { }
                });
            };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            List<long> memu = new List<long>();
            for (var i = 0; i < 3000; i++)
            {
                string res = null;
                using (var client = new WebSocketClient())
                {
                    client.MessageHandler = (e) => res = e.Data;
                    await client.ConnectAsync($"ws://localhost:{port}/aaa");
                    Console.WriteLine($"Connect {i}");
                    await client.SendStringAsync("hi" + i.ToString(), CancellationToken.None);
                    Console.WriteLine($"Disconnect {i}");
                }

                if (i % 300 == 0)
                {
                    GC.Collect();
                    memu.Add(getmem());
                }
            }

            var centroid = memu[memu.Count / 2];
            var last = memu.Last();
            var diff = last - centroid;
            var per = (diff / (float)centroid) * 100;
            Assert.True(per < 5);
            _output.WriteLine("Completed");
        }




        private static long getmem()
        {
            long totalsize = 0;
            foreach (var aProc in Process.GetProcesses())
                totalsize += aProc.WorkingSet64 / 1024l;
            return totalsize;
        }


        [Fact]
        public async Task TestClientDisposeReconnect()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) => { e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None); };
            var u = $"://localhost:{port}/";
            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync("http" + u);
            var closed = false;
            var client = new WebSocketClient()
            {
                CloseHandler = (c) =>
                    closed = true
            };
            await client.ConnectAsync("ws" + u + "aaa");

            //act

            await Task.Delay(100);

            client.Dispose();
            await Task.Delay(1000);

            //client Reconnect
            var client2 = new WebSocketClient() { };
            await client2.ConnectAsync("ws" + u + "aaa");

            server.Dispose();
            await Task.Delay(100);
            //asssert
            Assert.True(closed);

        }

        [Fact]
        public async Task TestCreateServerClient_connect_recv_echo()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) =>
            {
                e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None);
            };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            string res = null;
            var client = new WebSocketClient()
            {
                MessageHandler = (e) => res = e.Data,
            };

            await client.ConnectAsync($"ws://localhost:{port}/aaa");

            //act
            await client.SendStringAsync("hi", CancellationToken.None);
            await Task.Delay(100);

            //assert
            Assert.Equal("hihi", res);

        }


        [Fact]
        public async Task TestCreateServerClient_connect_recv_echo_twice ()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) =>
            {
                //try
                //{
                    var data = e.Data;
                    Task.Run(() => e.WebSocket.SendStringAsync(data + data, CancellationToken.None).GetAwaiter().GetResult());
                    Task.Run(() => e.WebSocket.SendStringAsync(data +  data, CancellationToken.None).GetAwaiter().GetResult());
                    //await Task.Delay(1000);
                //}
                //catch (Exception o)
                //{

                //}

            };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            string res = null;
            var client = new WebSocketClient()
            {
                MessageHandler = (e) => res = e.Data,
            };

            await client.ConnectAsync($"ws://localhost:{port}/aaa");

            //act
            var tasks = new List<Task>();
            for (var i = 0; i < 400; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                   // try
                   // {
                        client.SendStringAsync("hi" + i.ToString(), CancellationToken.None).GetAwaiter().GetResult();
                  //  }
                  //  catch (Exception e) {
                  //  }
                }));

            }
            await Task.WhenAll(tasks);
            await Task.Delay(100);

            //assert
            //Assert.Equal("hi", t1.res);
            
        }


        [Fact]
        public async Task TestCreateServerClient_LargeFile()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            int recievedSize = 0;
            beh.BinaryMessageHandler = (e) =>
            {
                recievedSize = e.Data.Length;
            };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            string res = null;
            var client = new WebSocketClient()
            {
                MessageHandler = (e) => res = e.Data,
            };

            await client.ConnectAsync($"ws://localhost:{port}/aaa");

            //act
            var s = _getFile("tst", 20);
            await client.SendStreamAsync(File.OpenRead(s));
            await Task.Delay(100);

            //assert
            Assert.Equal(new FileInfo(s).Length, recievedSize);

        }


        [Fact]
        public async Task TestCreateServerClient_connect_2_boot_1()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) =>
            {
                e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None);
            };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            string res = null;
            string kickoffRes = null;

            var client = new WebSocketClient()
            {
                MessageHandler = (e) => res = e.Data,
                CloseHandler = (e) =>
                    kickoffRes = e.CloseStatDescription
            };
            await client.ConnectAsync($"ws://localhost:{port}/aaa");
            await client.SendStringAsync("hi", CancellationToken.None);

            var client2 = new WebSocketClient()
            {
                MessageHandler = (e) => res = e.Data,
                CloseHandler = (e) =>
                kickoffRes = e.CloseStatDescription
            };
            await client2.ConnectAsync($"ws://localhost:{port}/aaa");
            await client2.SendStringAsync("hi", CancellationToken.None);

            //act
            await Task.Delay(100);
            var clients = server.GetActiveConnectionIds();
            Assert.Equal(2, clients.Count);

            await server.DisconnectConnection(clients[1], "dontlikeu");
            clients = server.GetActiveConnectionIds();
            Assert.Equal(1, clients.Count);
            while (kickoffRes == null)
            {
                await Task.Delay(100);

            }
            Assert.Equal("dontlikeu", kickoffRes);
            //assert
            Assert.Equal("hihi", res);

        }


        [Fact]
        public async Task TestCreateServer_connect_recv_echo_exception()
        {
            //arrange
            var server = new WebListenerWebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            bool exceptionoccured = false;
            beh.StringMessageHandler = (e) => {
                if (exceptionoccured)
                {
                    e.WebSocket.SendStringAsync("hihi");
                    return;
                }
                exceptionoccured = true;
                throw new Exception("arghhh"); };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/aaa"), CancellationToken.None);
            
            //act
            await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hi")), WebSocketMessageType.Text, true, CancellationToken.None);

            await Task.Delay(100);


            Assert.Equal(true, exceptionoccured);
            Assert.Equal(client.State, WebSocketState.Open);
            var byt = new byte[2000];
            await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hi")), WebSocketMessageType.Text, true, CancellationToken.None);

            var read = await client.ReceiveAsync(new ArraySegment<byte>(byt), CancellationToken.None);


            //assert

            Assert.Equal("hihi", Encoding.UTF8.GetString(new ArraySegment<byte>(byt, 0, read.Count)));

        }



        [Fact]
        public async Task TestCreateServerClient_LoadThrottling()
        {
            //arrange
            var server = new WebListenerWebSocketServer(queueThrottleLimitBytes: 200L * 1024 * 1024);// 
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.BinaryMessageHandler = (e) =>
            {
                Thread.Sleep(200);
            };

            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            string res = null;
            var client = new WebSocketClient()
            {
                MessageHandler = (e) => res = e.Data,
            };

            await client.ConnectAsync($"ws://localhost:{port}/aaa");

            //act
            for (int i = 0; i < 200; i++)
            {

                var s = _getFile("tst2", 10);
                await client.SendStreamAsync(File.OpenRead(s));
                await Task.Delay(1);
                //Assert.Equal(new FileInfo(s).Length, recievedSize);
            }
            //assert

        }


    }
}
