using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Sockets.Kcp.Simple;
using System.Threading.Tasks;

namespace TestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            SimpleKcpClient kcpClient = new SimpleKcpClient(40001);
            kcpClient.kcp.TraceListener = new ConsoleTraceListener();
            Task.Run(async () =>
            {
                while (true)
                {
                    kcpClient.kcp.Update(DateTimeOffset.UtcNow);
                    await Task.Delay(10);
                }
            });

            StartRecv(kcpClient);
            Console.ReadLine();
        }

        static async void StartRecv(SimpleKcpClient client)
        {
            while (true)
            {
                var res = await client.ReceiveAsync();
                var str = System.Text.Encoding.UTF8.GetString(res);
                if ("发送一条消息" == str)
                {
                    Console.WriteLine(str);

                    var buffer = System.Text.Encoding.UTF8.GetBytes("回复一条消息");
                    client.SendAsync(buffer, buffer.Length);
                }
            }
        }
    }
}
