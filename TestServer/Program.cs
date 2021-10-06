﻿using System;
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
            Task.Run(async () =>
            {
                while (true)
                {
                    kcpClient.kcp.Update(DateTime.UtcNow);
                    await Task.Delay(10);
                }
            });

            StartRecv(kcpClient);
            Console.ReadLine();
        }

        static async void StartRecv(SimpleKcpClient client)
        {
            var res = await client.ReceiveAsync();
            StartRecv(client);

            await Task.Delay(1);
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