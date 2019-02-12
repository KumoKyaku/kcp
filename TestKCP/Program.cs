using NUnit.TestsKCP;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets.Kcp;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestKCP
{

    class Program
    {
        static string ShowThread
        {
            get
            {
                return $"  ThreadID[{Thread.CurrentThread.ManagedThreadId}]";
            }
        }


        static void Main(string[] args)
        {
            Console.WriteLine(ShowThread);
            Random random = new Random();

            var handle1 = new Handle();
            var handle2 = new Handle();

            const int conv = 123;
            var kcp1 = new Kcp(conv, handle1);
            var kcp2 = new Kcp(conv, handle2);

            kcp1.KCPRemote = kcp2;
            kcp2.KCPRemote = kcp1;
            
            kcp1.NoDelay(1, 10, 2, 1);//fast
            kcp1.WndSize(64, 64);
            kcp1.SetMtu(512);

            kcp2.NoDelay(1, 10, 2, 1);//fast
            kcp2.WndSize(64, 64);
            kcp2.SetMtu(512);

            var sendbyte = Encoding.ASCII.GetBytes(TestClass.message);

            handle1.Out += buffer =>
            {
                var next = random.Next(100);
                if (next >= 5)///随机丢包
                {
                    //Console.WriteLine($"11------Thread[{Thread.CurrentThread.ManagedThreadId}]");
                    Task.Run(() =>
                    {
                        //Console.WriteLine($"12------Thread[{Thread.CurrentThread.ManagedThreadId}]");
                        kcp2.Input(buffer);
                    });
                    
                }
                else
                {
                    //Console.WriteLine("Send miss");
                }
            };

            handle2.Out += buffer =>
            {
                var next = random.Next(100);
                if (next >= 0)///随机丢包
                {
                    Task.Run(() =>
                    {
                        kcp1.Input(buffer);
                    });
                }
                else
                {
                    Console.WriteLine("Resp miss");
                }
            };
            int count = 0;

            handle1.Recv += buffer =>
            {
                unsafe
                {
                    using (MemoryHandle p = buffer.Memory.Pin())
                    {
                        var str = Encoding.ASCII.GetString((byte*)p.Pointer, buffer.Memory.Length);
                        count++;
                        if (TestClass.message == str)
                        {
                            Console.WriteLine($"kcp  echo----{count}");
                        }
                        var res= kcp1.Send(buffer.Memory.Span);
                        if (res != 0)
                        {
                            Console.WriteLine($"kcp send error");
                        }
                    }

                }
            };

            int recvCount = 0;
            handle2.Recv += buffer =>
            {
                recvCount++;
                Console.WriteLine($"kcp2 recv----{recvCount}");
                var res = kcp2.Send(buffer.Memory.Span);
                if (res != 0)
                {
                    Console.WriteLine($"kcp send error");
                }
            };

            Task.Run(async () =>
            {
                try
                {
                    int updateCount = 0;
                    while (true)
                    {
                        kcp1.Update(DateTime.UtcNow);
                        await Task.Delay(5);
                        updateCount++;
                        if (updateCount % 1000 == 0)
                        {
                            Console.WriteLine($"KCP1 ALIVE {updateCount}----{ShowThread}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

            });

            Task.Run(async () =>
            {
                try
                {
                    int updateCount = 0;
                    while (true)
                    {
                        kcp2.Update(DateTime.UtcNow);
                        await Task.Delay(5);
                        updateCount++;
                        if (updateCount % 1000 == 0)
                        {
                            Console.WriteLine($"KCP2 ALIVE {updateCount}----{ShowThread}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

            kcp1.Send(sendbyte);

            while (true)
            {
                Thread.Sleep(1000);
                GC.Collect();
            }

            Console.ReadLine();
        }
    }
}
