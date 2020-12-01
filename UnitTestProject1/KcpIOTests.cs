using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Sockets.Kcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers;

namespace System.Net.Sockets.Kcp.Tests
{
    [TestClass()]
    public class KcpIOTests
    {
        [TestMethod()]
        public void InputTest()
        {
            IKcpIO kcpIO = new FakeKcpIO();
            const int length = 10000;
            byte[] testBuffer = new byte[length];
            for (int i = 0; i < length; i++)
            {
                testBuffer[i] = (byte)(i % byte.MaxValue);
            }

            kcpIO.Send(new ReadOnlySequence<byte>(testBuffer));
            Writer writer = new Writer();
            kcpIO.Output(writer).AsTask().Wait();

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(writer.buffer[i], testBuffer[i]);
            }

            kcpIO.Input(new ReadOnlySequence<byte>(writer.buffer, 0, writer.Count));

            Writer recv = new Writer();
            kcpIO.Recv(recv).AsTask().Wait();

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(recv.buffer[i], testBuffer[i]);
            }
        }

        public class Writer: IBufferWriter<byte> 
        {
            public byte[] buffer = new byte[65535];
            public int Count = 0;
            public void Advance(int count)
            {
                Count += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                return buffer.AsMemory().Slice(Count, sizeHint);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                return buffer.AsSpan().Slice(Count, sizeHint);
            }
        }
    }
}