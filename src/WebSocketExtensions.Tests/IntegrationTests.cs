using System;
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

namespace WebSocketExtensions.Tests
{
    public class IntegrationTests
    {

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
            var server = new WebSocketServer();

            //assert

        }

        [Fact]
        public async Task TestCreateServer_start()
        {
            //arrange
            var port = _FreeTcpPort();
            using (var server = new WebSocketServer())
            {

                //act
                await server.StartAsync($"http://localhost:{port}/");
                await Task.Delay(100);
                //assert
            }

        }
        public class testBeh : WebSocketServerBehavior
        {
            public Action<StringMessageReceivedEventArgs> StringMessageHandler = (_)=> { };
            public Action<BinaryMessageReceivedEventArgs> BinaryMessageHandler = (_) => { };

            public override void OnStringMessage(StringMessageReceivedEventArgs e)
            {
                StringMessageHandler(e);
            }
            public override void OnBinaryMessage(BinaryMessageReceivedEventArgs e)
            {
                BinaryMessageHandler(e);
            }
        }

        [Fact]
        public async Task TestCreateServer_connect()
        {
            //arrange
            var server = new WebSocketServer();
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
            var server = new WebSocketServer();
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
            var server = new WebSocketServer();
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
            var server = new WebSocketServer();
            var port = _FreeTcpPort();

            var beh = new testBeh()
            {
            };
            beh.StringMessageHandler = (e) => { e.WebSocket.SendStringAsync(e.Data + e.Data, CancellationToken.None); };
            var u = $"://localhost:{port}/";
            server.AddRouteBehavior("/aaa", () => beh);
            await server.StartAsync("http" + u);
            var closed = false;
            var client = new WebSocketClient() {
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

        }

        [Fact]
        public async Task TestClientDispose()
        {
            //arrange
            var server = new WebSocketServer();
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
        public async Task TestCreateServerClient_connect_recv_echo() 
        {
            //arrange
            var server = new WebSocketServer();
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
        public async Task TestCreateServerClient_LargeFile()
        {
            //arrange
            var server = new WebSocketServer();
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


    }
}
