using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using static System.Math;
using BufferOwner = System.Buffers.IMemoryOwner<byte>;

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
        int Input(ReadOnlySpan<byte> span);
        /// <summary>
        /// 下层收到数据后添加到kcp协议中
        /// </summary>
        /// <param name="span"></param>
        int Input(ReadOnlySequence<byte> span);
        /// <summary>
        /// 从kcp中取出一个整合完毕的数据包
        /// </summary>
        /// <returns></returns>
        ValueTask Recv(IBufferWriter<byte> writer, object option = null);

        /// <summary>
        /// 将要发送到网络的数据Send到kcp协议中
        /// </summary>
        /// <param name="span"></param>
        /// <param name="option"></param>
        int Send(ReadOnlySpan<byte> span, object option = null);
        /// <summary>
        /// 将要发送到网络的数据Send到kcp协议中
        /// </summary>
        /// <param name="span"></param>
        /// <param name="option"></param>
        int Send(ReadOnlySequence<byte> span, object option = null);
        /// <summary>
        /// 从kcp协议中取出需要发送到网络的数据。
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="option"></param>
        /// <returns></returns>
        ValueTask Output(IBufferWriter<byte> writer, object option = null);
    }
    /// <summary>
    /// 异步缓存管道
    /// <para/>也可以通过（bool isEnd,T value）元组，来实现终止信号
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class SimplePipeQueue<T> : Queue<T>
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

    
    public class KcpIO : KcpCore, IKcpIO
    {
        OutputQ outq;
        public KcpIO(uint conv_) : base(conv_)
        {
            outq = new OutputQ();
            callbackHandle = outq;
        }

        public int Input(ReadOnlySpan<byte> data)
        {
            if (CheckDispose())
            {
                //检查释放
                return -4;
            }

            uint temp_una = snd_una;

            if (data.Length < IKCP_OVERHEAD)
            {
                return -1;
            }

            var offset = 0;
            int flag = 0;
            uint maxack = 0;
            while (true)
            {
                uint ts = 0;
                uint sn = 0;
                uint length = 0;
                uint una = 0;
                uint conv_ = 0;
                ushort wnd = 0;
                byte cmd = 0;
                byte frg = 0;

                if (data.Length - offset < IKCP_OVERHEAD)
                {
                    break;
                }

                if (IsLittleEndian)
                {
                    conv_ = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                    offset += 4;

                    if (conv != conv_)
                    {
                        return -1;
                    }

                    cmd = data[offset];
                    offset += 1;
                    frg = data[offset];
                    offset += 1;
                    wnd = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
                    offset += 2;

                    ts = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                    offset += 4;
                    sn = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                    offset += 4;
                    una = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                    offset += 4;
                    length = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                    offset += 4;
                }
                else
                {
                    conv_ = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
                    offset += 4;

                    if (conv != conv_)
                    {
                        return -1;
                    }

                    cmd = data[offset];
                    offset += 1;
                    frg = data[offset];
                    offset += 1;
                    wnd = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
                    offset += 2;

                    ts = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
                    offset += 4;
                    sn = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
                    offset += 4;
                    una = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
                    offset += 4;
                    length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
                    offset += 4;
                }


                if (data.Length - offset < length || (int)length < 0)
                {
                    return -2;
                }

                switch (cmd)
                {
                    case IKCP_CMD_PUSH:
                    case IKCP_CMD_ACK:
                    case IKCP_CMD_WASK:
                    case IKCP_CMD_WINS:
                        break;
                    default:
                        return -3;
                }

                rmt_wnd = wnd;
                Parse_una(una);
                Shrink_buf();

                if (IKCP_CMD_ACK == cmd)
                {
                    if (Itimediff(current, ts) >= 0)
                    {
                        Update_ack(Itimediff(current, ts));
                    }
                    Parse_ack(sn);
                    Shrink_buf();

                    if (flag == 0)
                    {
                        flag = 1;
                        maxack = sn;
                    }
                    else if (Itimediff(sn, maxack) > 0)
                    {
                        maxack = sn;
                    }

                }
                else if (IKCP_CMD_PUSH == cmd)
                {
                    if (Itimediff(sn, rcv_nxt + rcv_wnd) < 0)
                    {
                        ///instead of ikcp_ack_push
                        acklist.Enqueue((sn, ts));

                        if (Itimediff(sn, rcv_nxt) >= 0)
                        {
                            var seg = KcpSegment.AllocHGlobal((int)length);
                            seg.conv = conv_;
                            seg.cmd = cmd;
                            seg.frg = frg;
                            seg.wnd = wnd;
                            seg.ts = ts;
                            seg.sn = sn;
                            seg.una = una;
                            //seg.len = length;  长度在分配时确定，不能改变

                            if (length > 0)
                            {
                                data.Slice(offset, (int)length).CopyTo(seg.data);
                            }

                            Parse_data(seg);
                        }
                    }
                }
                else if (IKCP_CMD_WASK == cmd)
                {
                    // ready to send back IKCP_CMD_WINS in Ikcp_flush
                    // tell remote my window size
                    probe |= IKCP_ASK_TELL;
                }
                else if (IKCP_CMD_WINS == cmd)
                {
                    // do nothing
                }
                else
                {
                    return -3;
                }

                offset += (int)length;
            }

            if (flag != 0)
            {
                Parse_fastack(maxack);
            }

            if (Itimediff(this.snd_una, temp_una) > 0)
            {
                if (cwnd < rmt_wnd)
                {
                    var mss_ = mss;
                    if (cwnd < ssthresh)
                    {
                        cwnd++;
                        incr += mss_;
                    }
                    else
                    {
                        if (incr < mss_)
                        {
                            incr = mss_;
                        }
                        incr += (mss_ * mss_) / incr + (mss_ / 16);
                        if ((cwnd + 1) * mss_ <= incr)
                        {
                            cwnd++;
                        }
                    }
                    if (cwnd > rmt_wnd)
                    {
                        cwnd = rmt_wnd;
                        incr = rmt_wnd * mss_;
                    }
                }
            }

            return 0;
        }

        public int Input(ReadOnlySequence<byte> sequence)
        {
            byte[] temp = ArrayPool<byte>.Shared.Rent((int)sequence.Length);
            Span<byte> data = new Span<byte>(temp, 0, (int)sequence.Length);
            sequence.CopyTo(data);

            var ret = Input(data);

            ArrayPool<byte>.Shared.Return(temp);
            return ret;
        }

        internal override void Parse_data(KcpSegment newseg)
        {
            base.Parse_data(newseg);
            FastChechRecv();
        }

        SimplePipeQueue<List<KcpSegment>> recvSignal = new SimplePipeQueue<List<KcpSegment>>();
        private void FastChechRecv()
        {
            if (rcv_queue.Count == 0)
            {
                ///没有可用包
                return;
            }

            var seq = rcv_queue[0];

            if (seq.frg == 0)
            {
                return;
            }

            if (rcv_queue.Count < seq.frg + 1)
            {
                ///没有足够的包
                return;
            }
            else
            {
                ///至少含有一个完整消息

                List<KcpSegment> kcpSegments = new List<KcpSegment>();
                
                var recover = false;
                if (rcv_queue.Count >= rcv_wnd)
                {
                    recover = true;
                }

                #region merge fragment.
                /// merge fragment.

                lock (rcv_queueLock)
                {
                    var count = 0;
                    foreach (var seg in rcv_queue)
                    {
                        kcpSegments.Add(seg);

                        count++;
                        int frg = seg.frg;

                        if (frg == 0)
                        {
                            break;
                        }
                    }

                    if (count > 0)
                    {
                        rcv_queue.RemoveRange(0, count);
                    }
                }

                #endregion

                Move_Rcv_buf_2_Rcv_queue();

                #region fast recover
                /// fast recover
                if (rcv_queue.Count < rcv_wnd && recover)
                {
                    // ready to send back IKCP_CMD_WINS in ikcp_flush
                    // tell remote my window size
                    probe |= IKCP_ASK_TELL;
                }
                #endregion

                recvSignal.Write(kcpSegments);
            }
        }

        public async ValueTask Recv(IBufferWriter<byte> writer, object option = null)
        {
            FastChechRecv();
            var list = await recvSignal.ReadAsync().ConfigureAwait(false);
            foreach (var seg in list)
            {
                WriteRecv(writer, seg);
            }
            list.Clear();
        }

        private static void WriteRecv(IBufferWriter<byte> writer, KcpSegment seg)
        {
            var curCount = (int)seg.len;
            var target = writer.GetSpan(curCount);
            seg.data.CopyTo(target);
            KcpSegment.FreeHGlobal(seg);
            writer.Advance(curCount);
        }

        public int Send(ReadOnlySpan<byte> span, object option = null)
        {
            if (CheckDispose())
            {
                //检查释放
                return -4;
            }

            if (mss <= 0)
            {
                throw new InvalidOperationException($" mss <= 0 ");
            }


            if (span.Length == 0)
            {
                return -1;
            }
            var offset = 0;
            int count;

            #region append to previous segment in streaming mode (if possible)
            /// 基于线程安全和数据结构的等原因,移除了追加数据到最后一个包行为。
            #endregion

            #region fragment

            if (span.Length <= mss)
            {
                count = 1;
            }
            else
            {
                count = (int)(span.Length + mss - 1) / (int)mss;
            }

            if (count > IKCP_WND_RCV)
            {
                return -2;
            }

            if (count == 0)
            {
                count = 1;
            }

            for (var i = 0; i < count; i++)
            {
                int size;
                if (span.Length - offset > mss)
                {
                    size = (int)mss;
                }
                else
                {
                    size = span.Length - offset;
                }

                var seg = KcpSegment.AllocHGlobal(size);
                span.Slice(offset, size).CopyTo(seg.data);
                offset += size;
                seg.frg = (byte)(count - i - 1);
                snd_queue.Enqueue(seg);
            }

            #endregion

            return 0;
        }

        public int Send(ReadOnlySequence<byte> span, object option = null)
        {
            if (CheckDispose())
            {
                //检查释放
                return -4;
            }

            if (mss <= 0)
            {
                throw new InvalidOperationException($" mss <= 0 ");
            }


            if (span.Length == 0)
            {
                return -1;
            }
            var offset = 0;
            int count;

            #region append to previous segment in streaming mode (if possible)
            /// 基于线程安全和数据结构的等原因,移除了追加数据到最后一个包行为。
            #endregion

            #region fragment

            if (span.Length <= mss)
            {
                count = 1;
            }
            else
            {
                count = (int)(span.Length + mss - 1) / (int)mss;
            }

            if (count > IKCP_WND_RCV)
            {
                return -2;
            }

            if (count == 0)
            {
                count = 1;
            }

            for (var i = 0; i < count; i++)
            {
                int size;
                if (span.Length - offset > mss)
                {
                    size = (int)mss;
                }
                else
                {
                    size = (int)span.Length - offset;
                }

                var seg = KcpSegment.AllocHGlobal(size);
                span.Slice(offset, size).CopyTo(seg.data);
                offset += size;
                seg.frg = (byte)(count - i - 1);
                snd_queue.Enqueue(seg);
            }

            #endregion

            return 0;
        }

        public async ValueTask Output(IBufferWriter<byte> writer, object option = null)
        {
            var (Owner, Count) = await outq.ReadAsync().ConfigureAwait(false);
            WriteOut(writer, Owner, Count);
        }

        private static void WriteOut(IBufferWriter<byte> writer, BufferOwner Owner, int Count)
        {
            var target = writer.GetSpan(Count);
            Owner.Memory.Span.Slice(0, Count).CopyTo(target);
            writer.Advance(Count);
            Owner.Dispose();
        }

        internal class OutputQ: SimplePipeQueue<(BufferOwner Owner,int Count)>,
            IKcpCallback
        {
            public void Output(BufferOwner buffer, int avalidLength)
            {
                Write((buffer, avalidLength));
            }
        }
    }
}
