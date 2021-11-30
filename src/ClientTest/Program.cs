using System;
using System.Text;
using System.Threading.Tasks;
using WebSocketExtensions;

namespace ClientTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string targetIP = "localhost";
            if (args.Length == 2)
                targetIP = args[1];

            //arrange
            RunClient(int.Parse(args[0]), targetIP).GetAwaiter().GetResult();

        }
        static bool connected = false;
        static async Task RunClient(int port, string ip )
        {
            var u = $"://{ip}:{port}/";

            var client = new WebSocketClient()
            {
                CloseHandler = (c) => connected = false,
                BinaryHandler = (d) =>
                {
                    var str = Encoding.UTF8.GetString(d.Data);
                    Console.WriteLine(str);
                }

            };
            var loc = "ws" + u + "aaa";
            await client.ConnectAsync(loc);
            connected = true;
            Console.WriteLine($"Connected to {loc}");

            while (connected)
            {
                await Task.Delay(100);
              
            }
            Console.WriteLine("Disconnected.");
        }
    }
}
