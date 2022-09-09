using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebSocketExtensions;
using WebSocketExtensions.Kestrel;

namespace ConsoleApp1
{
    //## The program entry point    
    // Passes an HttpListener prefix for the server to listen on. The prefix 'http://+:80/wsDemo/' indicates that the server should listen on 
    // port 80 for requests to wsDemo (e.g. http://localhost/wsDemo). For more information on HttpListener prefixes see [MSDN](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).            
    class Program
    {
        static void Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(loggingBuilder => loggingBuilder
                                .SetMinimumLevel(LogLevel.Trace)
                                .AddConsole());

            ILogger logger = loggerFactory.CreateLogger<Program>();


            var server = new KestrelWebSocketServer(logger);
            server.AddRouteBehavior("/aaa", () => { return new test(); });
            server.StartAsync("http://127.0.0.1:8008");
            bool running = false;

            Task.Factory.StartNew(async () =>
            {
                Console.WriteLine("Connecting");
                var c = new ClientWebSocket();
                await c.ConnectAsync(new Uri($"ws://127.0.0.1:8008/aaa"), CancellationToken.None);
                running = true;
                while (running)
                {
                    Console.WriteLine("Sending");
                    await c.SendBytesAsync(Encoding.UTF8.GetBytes("hi"));
                    await Task.Delay(3000);
                }

            });
            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
            running = false;
            server.Dispose();
        }
    }

    public class test : KestrelWebSocketServerBehavior
    {
        public override void OnBinaryMessage(BinaryMessageReceivedEventArgs e)
        {
            Console.WriteLine($"binary message recieved len {e.Data.Length}");
        }
        public override void OnStringMessage(StringMessageReceivedEventArgs e)
        {
            Console.WriteLine($"String message recieved {e.Data}");

            e.WebSocket.SendStringAsync((e.Data + " OK"), CancellationToken.None);

        }
    }
}
