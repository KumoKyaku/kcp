using System;
using System.Net;
using System.Net.Sockets;

namespace TestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Press F1 send word.......");
            UdpClient client = new UdpClient();

            while (true)
            {
                var k = Console.ReadKey();
                if (k.Key == ConsoleKey.F1)
                {
                    Send(client, "发送一条消息");
                }
            }

            Console.ReadLine();
        }

        static IPEndPoint end = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 40001);
        static async void Send(UdpClient client, string v)
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(v);
            client.SendAsync(buffer, buffer.Length, end);
            var resp = await client.ReceiveAsync();
            var respstr = System.Text.Encoding.UTF8.GetString(resp.Buffer);
            Console.WriteLine($"收到服务器回复:    {respstr}");
        }
    }
}
