using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            UdpClient client = new UdpClient(40001);
            StartRecv(client);
            Console.ReadLine();
        }

        static async void StartRecv(UdpClient client)
        {
            var res = await client.ReceiveAsync();
            StartRecv(client);

            await Task.Delay(1);
            var str = System.Text.Encoding.UTF8.GetString(res.Buffer);
            Console.WriteLine(str);
            client.SendAsync(res.Buffer, res.Buffer.Length, res.RemoteEndPoint);
        }

    }
}
