using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Net.Sockets.Kcp.Simple;
using System.Threading.Tasks;

namespace TestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Press F1 send word.......");

            SimpleKcpClient kcpClient = new SimpleKcpClient(50001, end);
            Task.Run(async () =>
            {
                while (true)
                {
                    kcpClient.kcp.Update(DateTime.UtcNow);
                    await Task.Delay(10);
                }
            });

            while (true)
            {
                var k = Console.ReadKey();
                if (k.Key == ConsoleKey.F1)
                {
                    Send(kcpClient, "发送一条消息");
                }
            }

            Console.ReadLine();
        }

        static IPEndPoint end = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 40001);
        static async void Send(SimpleKcpClient client, string v)
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(v);
            client.SendAsync(buffer, buffer.Length);
            var resp = await client.ReceiveAsync();
            var respstr = System.Text.Encoding.UTF8.GetString(resp);
            Console.WriteLine($"收到服务器回复:    {respstr}");
        }
    }
}
