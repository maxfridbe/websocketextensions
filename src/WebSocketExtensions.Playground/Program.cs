using System;
using System.Threading;
using WebSocketExtensions;

namespace ConsoleApp1
{
    //## The program entry point    
    // Passes an HttpListener prefix for the server to listen on. The prefix 'http://+:80/wsDemo/' indicates that the server should listen on 
    // port 80 for requests to wsDemo (e.g. http://localhost/wsDemo). For more information on HttpListener prefixes see [MSDN](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).            
    class Program
    {
        static void Main(string[] args)
        {
            var server = new WebSocketServer((s, err) => Console.WriteLine(s));
            server.AddRouteBehavior("/aaa", () => { return new test(); });
            server.StartAsync("http://localhost:8080/");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    public class test : WebSocketServerBehavior
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
