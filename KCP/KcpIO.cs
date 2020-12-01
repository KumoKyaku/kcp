using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// kcp协议输入输出标准接口
    /// </summary>
    public interface IKcpIO
    {
        /// <summary>
        /// 下层收到数据后添加到kcp协议中
        /// </summary>
        /// <param name="span"></param>
        void Input(ReadOnlySpan<byte> span);
        /// <summary>
        /// 下层收到数据后添加到kcp协议中
        /// </summary>
        /// <param name="span"></param>
        void Input(ReadOnlySequence<byte> span);
        /// <summary>
        /// 从kcp中取出一个整合完毕的数据包
        /// </summary>
        /// <returns></returns>
        ValueTask<ReadOnlySequence<byte>> Recv();

        /// <summary>
        /// 将要发送到网络的数据Send到kcp协议中
        /// </summary>
        /// <param name="span"></param>
        /// <param name="option"></param>
        void Send(ReadOnlySpan<byte> span, object option = null);
        /// <summary>
        /// 将要发送到网络的数据Send到kcp协议中
        /// </summary>
        /// <param name="span"></param>
        /// <param name="option"></param>
        void Send(ReadOnlySequence<byte> span, object option = null);
        /// <summary>
        /// 从kcp协议中取出需要发送到网络的数据。
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="option"></param>
        /// <returns></returns>
        ValueTask Output(IBufferWriter<byte> writer, object option = null);
    }


    /// <summary>
    /// 用于调试的KCP IO 类，没有Kcp功能
    /// </summary>
    public class FakeKcpIO : IKcpIO
    {
        /// <summary>
        /// 异步缓存管道
        /// <para/>也可以通过（bool isEnd,T value）元组，来实现终止信号
        /// </summary>
        /// <typeparam name="T"></typeparam>
        protected internal class SimplePipeQueue<T> : Queue<T>
        {
            readonly object _innerLock = new object();
            private TaskCompletionSource<T> source;

            //线程同步上下文由Task机制保证，无需额外处理
            //SynchronizationContext callbackContext;
            //public bool UseSynchronizationContext { get; set; } = true;

            public void Write(T item)
            {
                lock (_innerLock)
                {
                    if (source == null)
                    {
                        Enqueue(item);
                    }
                    else
                    {
                        if (Count > 0)
                        {
                            throw new Exception("内部顺序错误，不应该出现，请联系作者");
                        }

                        var next = source;
                        source = null;
                        next.TrySetResult(item);
                    }
                }
            }

            public ValueTask<T> ReadAsync()
            {
                lock (_innerLock)
                {
                    if (this.Count > 0)
                    {
                        var next = Dequeue();
                        return new ValueTask<T>(next);
                    }
                    else
                    {
                        source = new TaskCompletionSource<T>();
                        return new ValueTask<T>(source.Task);
                    }
                }
            }
        }


        SimplePipeQueue<byte[]> recv = new SimplePipeQueue<byte[]>();
        public void Input(ReadOnlySpan<byte> span)
        {
            byte[] buffer = new byte[span.Length];
            span.CopyTo(buffer);
            recv.Write(buffer);
        }

        public void Input(ReadOnlySequence<byte> span)
        {
            byte[] buffer = new byte[span.Length];
            span.CopyTo(buffer);
            recv.Write(buffer);
        }

        public async ValueTask<ReadOnlySequence<byte>> Recv()
        {
            var buffer = await recv.ReadAsync().ConfigureAwait(false);
            ReadOnlySequence<byte> ret = new ReadOnlySequence<byte>(buffer, 0, buffer.Length);
            return ret;
        }


        SimplePipeQueue<byte[]> send = new SimplePipeQueue<byte[]>();
        public void Send(ReadOnlySpan<byte> span, object option = null)
        {
            byte[] buffer = new byte[span.Length];
            span.CopyTo(buffer);
            send.Write(buffer);
        }

        public void Send(ReadOnlySequence<byte> span, object option = null)
        {
            byte[] buffer = new byte[span.Length];
            span.CopyTo(buffer);
            send.Write(buffer);
        }

        public async ValueTask Output(IBufferWriter<byte> writer, object option = null)
        {
            var buffer = await send.ReadAsync().ConfigureAwait(false);
            Write(writer, buffer);
        }

        private static void Write(IBufferWriter<byte> writer, byte[] buffer)
        {
            var span = writer.GetSpan(buffer.Length);
            buffer.AsSpan().CopyTo(span);
            writer.Advance(buffer.Length);
        }
    }
}
