using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static System.Math;
using BufferOwner = System.Buffers.IMemoryOwner<byte>;

namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// https://github.com/skywind3000/kcp/wiki/Network-Layer
    /// <para>外部buffer ----拆分拷贝----等待列表 -----移动----发送列表----拷贝----发送buffer---output</para>
    /// https://github.com/skywind3000/kcp/issues/118#issuecomment-338133930
    /// </summary>
    public class KcpV2 : KcpCore
    {
        /// <summary>
        /// create a new kcp control object, 'conv' must equal in two endpoint
        /// from the same connection.
        /// </summary>
        /// <param name="conv_"></param>
        /// <param name="rentable">可租用内存的回调</param>
        public KcpV2(uint conv_, IRentable rentable = null):base(conv_)
        {
            this.rentable = rentable;
        }

        //extension 重构和新增加的部分============================================

        IRentable rentable;

        public (BufferOwner buffer, int avalidLength) TryRecv()
        {
            if (rcv_queue.Count == 0)
            {
                ///没有可用包
                return (null, -1);
            }

            var peekSize = -1;
            var seq = rcv_queue[0];

            if (seq.frg == 0)
            {
                peekSize = (int)seq.len;
            }

            if (rcv_queue.Count < seq.frg + 1)
            {
                ///没有足够的包
                return (null, -1);
            }

            lock (rcv_queueLock)
            {
                uint length = 0;

                foreach (var item in rcv_queue)
                {
                    length += item.len;
                    if (item.frg == 0)
                    {
                        break;
                    }
                }

                peekSize = (int)length;
            }

            if (peekSize <= 0)
            {
                return (null, -2);
            }

            var buffer = CreateBuffer(peekSize);
            var recvlength = UncheckRecv(buffer.Memory.Span);
            return (buffer, recvlength);
        }

        /// <summary>
        /// user/upper level recv: returns size, returns below zero for EAGAIN
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public int Recv(Span<byte> buffer)
        {
            if (0 == rcv_queue.Count)
            {
                return -1;
            }

            var peekSize = PeekSize();
            if (peekSize < 0)
            {
                return -2;
            }

            if (peekSize > buffer.Length)
            {
                return -3;
            }

            /// 拆分函数
            var recvLength = UncheckRecv(buffer);

            return recvLength;
        }

        /// <summary>
        /// 这个函数不检查任何参数
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        int UncheckRecv(Span<byte> buffer)
        {
            var recover = false;
            if (rcv_queue.Count >= rcv_wnd)
            {
                recover = true;
            }

            #region merge fragment.
            /// merge fragment.

            var recvLength = 0;
            lock (rcv_queueLock)
            {
                var count = 0;
                foreach (var seg in rcv_queue)
                {
                    seg.data.CopyTo(buffer.Slice(recvLength));
                    recvLength += (int)seg.len;

                    count++;
                    int frg = seg.frg;

                    KcpSegment.FreeHGlobal(seg);
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
            return recvLength;
        }

        /// <summary>
        /// check the size of next message in the recv queue
        /// </summary>
        /// <returns></returns>
        public int PeekSize()
        {

            if (rcv_queue.Count == 0)
            {
                ///没有可用包
                return -1;
            }

            var seq = rcv_queue[0];

            if (seq.frg == 0)
            {
                return (int)seq.len;
            }

            if (rcv_queue.Count < seq.frg + 1)
            {
                ///没有足够的包
                return -1;
            }

            lock (rcv_queueLock)
            {
                uint length = 0;

                foreach (var item in rcv_queue)
                {
                    length += item.len;
                    if (item.frg == 0)
                    {
                        break;
                    }
                }

                return (int)length;
            }
        }

        
        /// <summary>
        /// when you received a low level packet (eg. UDP packet), call it
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public int Input(Span<byte> data)
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
    }

}










