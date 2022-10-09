using System;
using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets.Kcp;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnitTestProject1;

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

        public class TL: ConsoleTraceListener
        {
            public override void WriteLine(string message, string category)
            {
                base.WriteLine(message, $"[{Name}]  {category}");
            }
        }


        static void Main(string[] args)
        {
            Console.WriteLine(ShowThread);
            Random random = new Random();

            var handle1 = new Handle();
            var handle2 = new Handle();

            const int conv = 123;
            //var kcp1 = new UnSafeSegManager.Kcp(conv, handle1);
            //var kcp2 = new UnSafeSegManager.Kcp(conv, handle2);

            var kcp1 = new PoolSegManager.Kcp(conv, handle1);
            var kcp2 = new PoolSegManager.Kcp(conv, handle2);
            kcp1.TraceListener = new TL() { Name = "Kcp1" };
            kcp2.TraceListener = new TL() { Name = "Kcp2" };
            kcp1.NoDelay(1, 10, 2, 1);//fast
            kcp1.WndSize(128, 128);
            //kcp1.SetMtu(512);

            kcp2.NoDelay(1, 10, 2, 1);//fast
            kcp2.WndSize(128, 128);
            //kcp2.SetMtu(512);

            var sendbyte = Encoding.ASCII.GetBytes(UnitTest1.message);

            handle1.Out += buffer =>
            {
                var next = random.Next(100);
                if (next >= 15)///随机丢包
                {
                    //Console.WriteLine($"11------Thread[{Thread.CurrentThread.ManagedThreadId}]");
                    Task.Run(() =>
                    {
                        //Console.WriteLine($"12------Thread[{Thread.CurrentThread.ManagedThreadId}]");
                        kcp2.Input(buffer.Span);
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
                        kcp1.Input(buffer.Span);
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
                var str = Encoding.ASCII.GetString(buffer);
                count++;
                if (UnitTest1.message == str)
                {
                    Console.WriteLine($"kcp  echo----{count}");
                }
                var res = kcp1.Send(buffer);
                if (res != 0)
                {
                    Console.WriteLine($"kcp send error");
                }
            };

            int recvCount = 0;
            handle2.Recv += buffer =>
            {
                recvCount++;
                Console.WriteLine($"kcp2 recv----{recvCount}");
                var res = kcp2.Send(buffer);
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
                        kcp1.Update(DateTimeOffset.UtcNow);

                        int len;
                        while ((len = kcp1.PeekSize()) > 0)
                        {
                            var buffer = new byte[len];
                            if (kcp1.Recv(buffer) >= 0)
                            {
                                handle1.Receive(buffer);
                            }
                        }

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
                        kcp2.Update(DateTimeOffset.UtcNow);

                        //var utcNow = DateTime.UtcNow;
                        //var res = kcp2.Check(utcNow);

                        int len;
                        do
                        {
                            var (buffer, avalidSzie) = kcp2.TryRecv();
                            len = avalidSzie;
                            if (buffer != null)
                            {
                                var temp = new byte[len];
                                buffer.Memory.Span.Slice(0, len).CopyTo(temp);
                                handle2.Receive(temp);
                            }
                        } while (len > 0);

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
