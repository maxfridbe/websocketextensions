using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketExtensions;

namespace WEbSocketExtensions.NF_Playground
{
    public class testBeh : HttpListenerWebSocketServerBehavior
    {
        public Action<StringMessageReceivedEventArgs> StringMessageHandler = (_) => { };
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
    //## The program entry point    
    // Passes an HttpListener prefix for the server to listen on. The prefix 'http://+:80/wsDemo/' indicates that the server should listen on 
    // port 80 for requests to wsDemo (e.g. http://localhost/wsDemo). For more information on HttpListener prefixes see [MSDN](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).            
    class Program
    {
        static int _FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        static void Main(string[] args)
        {
            Task.Run(async () =>
            {
                //var server = new WebSocketServer((s, err) => Console.WriteLine(s));
                //server.AddRouteBehavior("/aaa", () => { return new test(); });
                //server.StartAsync("http://localhost:8080/");
                //Console.WriteLine("Press any key to exit...");
                //Console.ReadKey();
                //arrange
                var server = new HttpListenerWebSocketServer();
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
                    Task.Run(() => e.WebSocket.SendStringAsync(data + data, CancellationToken.None).GetAwaiter().GetResult());
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
                for (var i = 0; i < 200; i++)
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



            }).GetAwaiter().GetResult();


        }
    }




}
