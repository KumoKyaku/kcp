using System;
using System.Collections.Generic;
using System.Text;
using static System.Math;
using System.Collections.Concurrent;
using System.Buffers;
using BufferOwner = System.Buffers.IMemoryOwner<byte>;
using System.Linq;
using System.Buffers.Binary;
using System.Threading;
using System.Runtime.InteropServices;

namespace System.Net.Sockets.Protocol
{
    /// <summary>
    /// https://github.com/skywind3000/kcp/wiki/Network-Layer
    /// <para>外部buffer ----拆分拷贝----等待列表 -----移动----发送列表----拷贝----发送buffer---output</para>
    /// </summary>
    public partial class KCP : IKCPSetting, IKCPUpdate
    {
        #region encode

        ///使用大端格式

        public static int Ikcp_encode8u(Span<byte> p, int offset, byte c)
        {
            p[0 + offset] = c;
            return 1;
        }

        public static int ikcp_decode8u(Span<byte> p, int offset, ref byte c)
        {
            c = p[0 + offset];
            return 1;
        }

        public static int ikcp_encode16u(Span<byte> p, int offset, ushort w)
        {
            BinaryPrimitives.WriteUInt16BigEndian(p.Slice(offset), w);
            return 2;
        }

        public static int ikcp_decode16u(Span<byte> p, int offset, ref ushort c)
        {
            c = BinaryPrimitives.ReadUInt16BigEndian(p.Slice(offset));
            return 2;
        }

        
        public static int ikcp_encode32u(Span<byte> p, int offset, uint l)
        {
            BinaryPrimitives.WriteUInt32BigEndian(p.Slice(offset), l);
            return 4;
        }

        
        public static int ikcp_decode32u(Span<byte> p, int offset, ref uint c)
        {
            c = BinaryPrimitives.ReadUInt32BigEndian(p.Slice(offset));
            return 4;
        }

        #endregion

        /// <summary>
        /// create a new kcp control object, 'conv' must equal in two endpoint
        /// from the same connection.
        /// </summary>
        /// <param name="conv_"></param>
        /// <param name="output_"></param>
        public KCP(uint conv_, IKCPCallback kCPHandle)
        {
            conv = conv_;
            snd_wnd = IKCP_WND_SND;
            rcv_wnd = IKCP_WND_RCV;
            rmt_wnd = IKCP_WND_RCV;
            mtu = IKCP_MTU_DEF;
            mss = mtu - IKCP_OVERHEAD;

            rx_rto = IKCP_RTO_DEF;
            rx_minrto = IKCP_RTO_MIN;
            interval = IKCP_INTERVAL;
            ts_flush = IKCP_INTERVAL;
            ssthresh = IKCP_THRESH_INIT;
            dead_link = IKCP_DEADLINK;
            buffer = new byte[(mtu + IKCP_OVERHEAD) * 3];
            handle = kCPHandle;
        }
        #region Const

        public const int IKCP_RTO_NDL = 30;  // no delay min rto
        public const int IKCP_RTO_MIN = 100; // normal min rto
        public const int IKCP_RTO_DEF = 200;
        public const int IKCP_RTO_MAX = 60000;
        public const int IKCP_CMD_PUSH = 81; // cmd: push data
        public const int IKCP_CMD_ACK = 82; // cmd: ack
        public const int IKCP_CMD_WASK = 83; // cmd: window probe (ask)
        public const int IKCP_CMD_WINS = 84; // cmd: window size (tell)
        public const int IKCP_ASK_SEND = 1;  // need to send IKCP_CMD_WASK
        public const int IKCP_ASK_TELL = 2;  // need to send IKCP_CMD_WINS
        public const int IKCP_WND_SND = 32;
        public const int IKCP_WND_RCV = 32;
        public const int IKCP_MTU_DEF = 1400;
        public const int IKCP_ACK_FAST = 3;
        public const int IKCP_INTERVAL = 100;
        public const int IKCP_OVERHEAD = 24;
        public const int IKCP_DEADLINK = 10;
        public const int IKCP_THRESH_INIT = 2;
        public const int IKCP_THRESH_MIN = 2;
        public const int IKCP_PROBE_INIT = 7000;   // 7 secs to probe window size
        public const int IKCP_PROBE_LIMIT = 120000; // up to 120 secs to probe window

        #endregion

        #region kcp members
        /// <summary>
        /// 频道号
        /// </summary>
        uint conv;
        /// <summary>
        /// 最大传输单元（Maximum Transmission Unit，MTU）
        /// </summary>
        uint mtu;
        /// <summary>
        /// 最大报文段长度
        /// </summary>
        uint mss;
        uint state;
        uint snd_una;
        uint snd_nxt;
        /// <summary>
        /// 下一个等待接收消息ID
        /// </summary>
        uint rcv_nxt;
        uint ts_recent;
        uint ts_lastack;
        uint ssthresh;
        uint rx_rttval;
        uint rx_srtt;
        uint rx_rto;
        uint rx_minrto;
        uint snd_wnd;
        uint rcv_wnd;
        uint rmt_wnd;
        uint cwnd;
        uint probe;
        uint current;
        uint interval;
        uint ts_flush;
        uint xmit;
        uint nodelay;
        uint updated;
        uint ts_probe;
        uint probe_wait;
        uint dead_link;
        uint incr;
        int fastresend;
        int nocwnd;
        int logmask;
        private readonly object ackLock = new object(); 
        List<uint> acklist = new List<uint>();
        ConcurrentQueue<(uint sn, uint ts)> acklist2 = new ConcurrentQueue<(uint sn, uint ts)>();
        Memory<byte> buffer;

        #endregion

        /// <summary>
        /// KCP Segment Definition
        /// </summary>
        internal class Segment
        {
            internal uint conv = 0;
            internal uint cmd = 0;
            internal uint frg = 0;
            internal uint wnd = 0;
            /// <summary>
            /// 
            /// </summary>
            internal uint ts = 0;
            /// <summary>
            /// 当前消息序号
            /// </summary>
            internal uint sn = 0;
            internal uint una = 0;
            internal uint resendts = 0;
            internal uint rto = 0;
            internal uint fastack = 0;
            internal uint xmit = 0;
            internal BufferOwner data;

            internal Segment(BufferOwner buffer)
            {
                this.data = buffer;
            }

            // encode a segment into buffer
            internal int encode(Span<byte> ptr, int offset)
            {

                var offset_ = offset;

                offset += ikcp_encode32u(ptr, offset, conv);
                offset += Ikcp_encode8u(ptr, offset, (byte)cmd);
                offset += Ikcp_encode8u(ptr, offset, (byte)frg);
                offset += ikcp_encode16u(ptr, offset, (ushort)wnd);
                offset += ikcp_encode32u(ptr, offset, ts);
                offset += ikcp_encode32u(ptr, offset, sn);
                offset += ikcp_encode32u(ptr, offset, una);
                offset += ikcp_encode32u(ptr, offset, (uint)data.Memory.Length);

                return offset - offset_;
            }
        }

        private readonly object sendlock = new object();
        /// <summary>
        /// 发送等待队列
        /// </summary>
        ConcurrentQueue<Segment> sendWaitQ = new ConcurrentQueue<Segment>();
        /// <summary>
        /// 正在发送列表
        /// </summary>
        List<Segment> sendList = new List<Segment>();

        private readonly object recvWaitlock = new object();
        /// <summary>
        /// 正在等待触发接收回调函数消息列表
        /// </summary>
        List<Segment> recvWaitList = new List<Segment>();


        private readonly object recvlock = new object();
        /// <summary>
        /// 正在等待重组消息列表
        /// </summary>
        List<Segment> recvList = new List<Segment>();


        static uint Ibound(uint lower, uint middle, uint upper)
        {
            return Min(Max(lower, middle), upper);
        }

        static int Itimediff(uint later, uint earlier)
        {
            return ((int)(later - earlier));
        }

        void Shrink_buf()
        {
            lock (sendlock)
            {
                snd_una = sendList.Count > 0 ? sendList[0].sn : snd_nxt;
            }
        }

        // user/upper level recv: returns size, returns below zero for EAGAIN
        int Recv(Span<byte> buffer)
        {
            if (0 == recvWaitList.Count)
            {
                return -1;
            }

            var peekSize = PeekSize();
            if (0 > peekSize)
            {
                return -2;
            }

            if (peekSize > buffer.Length)
            {
                return -3;
            }

            var fast_recover = false;
            if (recvWaitList.Count >= rcv_wnd)
            {
                fast_recover = true;
            }

            #region merge fragment.
            // merge fragment.

            var recvLength = 0;
            lock (recvWaitlock)
            {
                var count = 0;
                foreach (var seg in recvWaitList)
                {
                    seg.data.Memory.Span.CopyTo(buffer.Slice(recvLength));
                    recvLength += seg.data.Memory.Length;
                    count++;
                    if (0 == seg.frg)
                    {
                        break;
                    }
                }

                if (count > 0)
                {
                    recvWaitList.RemoveRange(0, count);
                }
            }

            #endregion

            MoveRecvAvailableDate();

            #region fast recover
            /// fast recover
            if (recvWaitList.Count < rcv_wnd && fast_recover)
            {
                // ready to send back IKCP_CMD_WINS in ikcp_flush
                // tell remote my window size
                probe |= IKCP_ASK_TELL;
            }
            #endregion


            return recvLength;
        }

        /// <summary>
        /// when you received a low level packet (eg. UDP packet), call it
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public int Input(Span<byte> data)
        {
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

                offset += ikcp_decode32u(data, offset, ref conv_);

                if (conv != conv_)
                {
                    return -1;
                }

                offset += ikcp_decode8u(data, offset, ref cmd);
                offset += ikcp_decode8u(data, offset, ref frg);
                offset += ikcp_decode16u(data, offset, ref wnd);
                offset += ikcp_decode32u(data, offset, ref ts);
                offset += ikcp_decode32u(data, offset, ref sn);
                offset += ikcp_decode32u(data, offset, ref una);
                offset += ikcp_decode32u(data, offset, ref length);

                if (data.Length - offset < length)
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
                        lock (ackLock)
                        {
                            acklist.Add(sn);
                            acklist.Add(ts);
                        }

                        if (Itimediff(sn, rcv_nxt) >= 0)
                        {
                            var seg = new Segment(CreateBuffer((int)length))
                            {
                                conv = conv_,
                                cmd = cmd,
                                frg = frg,
                                wnd = wnd,
                                ts = ts,
                                sn = sn,
                                una = una
                            };

                            if (length > 0)
                            {
                                data.Slice(offset).CopyTo(seg.data.Memory.Span);
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

            if (Itimediff(this.snd_una, snd_una) > 0)
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

        // check the size of next message in the recv queue
        int PeekSize()
        {

            if (recvWaitList.Count == 0)
            {
                return -1;
            }

            var seq = recvWaitList[0];

            if (0 == seq.frg)
            {
                return seq.data.Memory.Length;
            }

            if (recvWaitList.Count < seq.frg + 1)
            {
                return -1;
            }

            lock (recvWaitlock)
            {
                int length = 0;

                foreach (var item in recvWaitList)
                {
                    length += item.data.Memory.Length;
                    if (0 == item.frg)
                    {
                        break;
                    }
                }

                return length;
            }
        }

        /// <summary>
        /// user/upper level send, returns below zero for error
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public int Send(Span<byte> buffer)
        {
            if (0 == buffer.Length)
            {
                return -1;
            }

            var count = 0;

            if (buffer.Length < mss)
            {
                count = 1;
            }
            else
            {
                count = (int)(buffer.Length + mss - 1) / (int)mss;
            }

            if (count > 255)
            {
                return -2;
            }

            if (count == 0)
            {
                count = 1;
            }

            var offset = 0;

            for (var i = 0; i < count; i++)
            {
                var size = 0;
                if (buffer.Length - offset > mss)
                {
                    size = (int)mss;
                }
                else
                {
                    size = buffer.Length - offset;
                }

                var seg = new Segment(CreateBuffer(size));
                buffer.Slice(offset,size).CopyTo(seg.data.Memory.Span);
                offset += size;
                seg.frg = (uint)(count - i - 1);
                sendWaitQ.Enqueue(seg);
            }

            return 0;
        }

        /// <summary>
        /// update state (call it repeatedly, every 10ms-100ms), or you can ask
        /// ikcp_check when to call it again (without ikcp_input/_send calling).
        /// </summary>
        /// <param name="time">DateTime.UtcNow</param>
        public void Update(DateTime time)
        {
            current = time.ConvertTime();

            if (0 == updated)
            {
                updated = 1;
                ts_flush = current;
            }

            var slap = Itimediff(current, ts_flush);

            if (slap >= 10000 || slap < -10000)
            {
                ts_flush = current;
                slap = 0;
            }

            if (slap >= 0)
            {
                ts_flush += interval;
                if (Itimediff(current, ts_flush) >= 0)
                {
                    ts_flush = current + interval;
                }

                Flush();
            }


            #region Receive

            int len;
            while ((len = PeekSize()) > 0)
            {
                var buffer = CreateBuffer(len);
                if(Recv(buffer.Memory.Span) >= 0)
                {
                    handle.Receive(buffer);
                } 
            }

            #endregion
        }

        /// <summary>
        /// flush pending data
        /// </summary>
        void Flush()
        {
            var current_ = current;
            var buffer_ = buffer;
            var change = 0;
            var lost = 0;

            if (updated == 0)
            {
                return;
            }

            var seg = new Segment(CreateBuffer(0));
            seg.conv = conv;
            seg.cmd = IKCP_CMD_ACK;
            seg.wnd = (uint)Wnd_unused();
            seg.una = rcv_nxt;
            var offset = 0;

            #region flush acknowledges
            // flush acknowledges

            
            lock (ackLock)
            {
                var count = acklist.Count / 2;
                for (var i = 0; i < count; i++)
                {
                    if (offset + IKCP_OVERHEAD > mtu)
                    {
                        handle.Output(buffer.Span.Slice(0, offset));
                        offset = 0;
                    }

                    seg.sn = acklist[i * 2 + 0];
                    seg.ts = acklist[i * 2 + 1];

                    offset += seg.encode(buffer.Span, offset);
                }
                acklist.Clear();
            }

            #endregion

            #region probe window size (if remote window size equals zero)
            // probe window size (if remote window size equals zero)
            if (0 == rmt_wnd)
            {
                if (0 == probe_wait)
                {
                    probe_wait = IKCP_PROBE_INIT;
                    ts_probe = current + probe_wait;
                }
                else
                {
                    if (Itimediff(current, ts_probe) >= 0)
                    {
                        if (probe_wait < IKCP_PROBE_INIT)
                            probe_wait = IKCP_PROBE_INIT;
                        probe_wait += probe_wait / 2;
                        if (probe_wait > IKCP_PROBE_LIMIT)
                            probe_wait = IKCP_PROBE_LIMIT;
                        ts_probe = current + probe_wait;
                        probe |= IKCP_ASK_SEND;
                    }
                }
            }
            else
            {
                ts_probe = 0;
                probe_wait = 0;
            }
            #endregion

            #region flush window probing commands
            // flush window probing commands
            if ((probe & IKCP_ASK_SEND) != 0)
            {
                seg.cmd = IKCP_CMD_WASK;
                if (offset + IKCP_OVERHEAD > (int)mtu)
                {
                    handle.Output(buffer.Span.Slice(0, offset));
                    offset = 0;
                }
                offset += seg.encode(buffer.Span, offset);
            }

            probe = 0;
            #endregion

            #region 刷新，将发送等待列表移动到发送列表


            // calculate window size
            var cwnd_ = Min(snd_wnd, rmt_wnd);
            if (0 == nocwnd)
            {
                cwnd_ = Min(cwnd, cwnd_);
            }

            while (Itimediff(snd_nxt, snd_una + cwnd_) < 0)
            {
                if (sendWaitQ.TryDequeue(out var newseg))
                {
                    newseg.conv = conv;
                    newseg.cmd = IKCP_CMD_PUSH;
                    newseg.wnd = seg.wnd;
                    newseg.ts = current_;
                    newseg.sn = snd_nxt;
                    newseg.una = rcv_nxt;
                    newseg.resendts = current_;
                    newseg.rto = rx_rto;
                    newseg.fastack = 0;
                    newseg.xmit = 0;
                    lock (sendlock)
                    {
                        sendList.Add(newseg);
                    }

                    snd_nxt++;
                }
                else
                {
                    break;
                }
            }

            #endregion

            #region 刷新 发送列表，调用Output

            // calculate resent
            var resent = (uint)fastresend;
            if (fastresend <= 0)
            {
                resent = 0xffffffff;
            }

            var rtomin = rx_rto >> 3;

            if (nodelay != 0)
            {
                rtomin = 0;
            }

            lock (sendlock)
            {
                // flush data segments
                foreach (var segment in sendList)
                {
                    var needsend = false;
                    var debug = Itimediff(current_, segment.resendts);
                    if (0 == segment.xmit)
                    {
                        needsend = true;
                        segment.xmit++;
                        segment.rto = rx_rto;
                        segment.resendts = current_ + segment.rto + rtomin;
                    }
                    else if (Itimediff(current_, segment.resendts) >= 0)
                    {
                        needsend = true;
                        segment.xmit++;
                        xmit++;
                        if (0 == nodelay)
                        {
                            segment.rto += rx_rto;
                        }
                        else
                        {
                            segment.rto += rx_rto / 2;
                        }

                        segment.resendts = current_ + segment.rto;
                        lost = 1;
                    }
                    else if (segment.fastack >= resent)
                    {
                        needsend = true;
                        segment.xmit++;
                        segment.fastack = 0;
                        segment.resendts = current_ + segment.rto;
                        change++;
                    }

                    if (needsend)
                    {
                        segment.ts = current_;
                        segment.wnd = seg.wnd;
                        segment.una = rcv_nxt;

                        var need = IKCP_OVERHEAD + segment.data.Memory.Length;
                        if (offset + need > mtu)
                        {
                            handle.Output(buffer.Span.Slice(0, offset));
                            offset = 0;
                        }

                        offset += segment.encode(buffer.Span, offset);
                        if (segment.data.Memory.Length > 0)
                        {
                            segment.data.Memory.Span.CopyTo(buffer.Span.Slice(offset));
                            offset += segment.data.Memory.Length;
                        }

                        if (segment.xmit >= dead_link)
                        {
                            state = 0;
                        }
                    }
                }
            }
            

            // flash remain segments
            if (offset > 0)
            {
                handle.Output(buffer.Span.Slice(0, offset));
                offset = 0;
            }

            #endregion

            #region update ssthresh
            // update ssthresh
            if (change != 0)
            {
                var inflight = snd_nxt - snd_una;
                ssthresh = inflight / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                    ssthresh = IKCP_THRESH_MIN;
                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }

            if (lost != 0)
            {
                ssthresh = cwnd / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                    ssthresh = IKCP_THRESH_MIN;
                cwnd = 1;
                incr = mss;
            }

            if (cwnd < 1)
            {
                cwnd = 1;
                incr = mss;
            }
            #endregion

        }

        ///// <summary>
        ///// Determine when should you invoke ikcp_update:
        ///// returns when you should invoke ikcp_update in millisec, if there
        ///// is no ikcp_input/_send calling. you can call ikcp_update in that
        ///// time, instead of call update repeatly.
        ///// <para></para>
        ///// Important to reduce unnacessary ikcp_update invoking. use it to
        ///// schedule ikcp_update (eg. implementing an epoll-like mechanism,
        ///// or optimize ikcp_update when handling massive kcp connections)
        ///// <para></para>
        ///// </summary>
        ///// <param name="time"></param>
        ///// <returns></returns>
        //[Obsolete("",true)]
        //public DateTime Check(DateTime time)
        //{

        //    if (updated == 0)
        //    {
        //        return time;
        //    }

        //    var current_ = time.ConvertTime();

        //    var ts_flush_ = ts_flush;
        //    var tm_flush_ = 0x7fffffff;
        //    var tm_packet = 0x7fffffff;
        //    var minimal = 0;

        //    if (Itimediff(current_, ts_flush_) >= 10000 || Itimediff(current_, ts_flush_) < -10000)
        //    {
        //        ts_flush_ = current_;
        //    }

        //    if (Itimediff(current_, ts_flush_) >= 0)
        //    {
        //        return time;
        //    }

        //    tm_flush_ = (int)Itimediff(ts_flush_, current_);

        //    foreach (var seg in sendList)
        //    {
        //        var diff = Itimediff(seg.resendts, current_);
        //        if (diff <= 0)
        //        {
        //            return time;
        //        }

        //        if (diff < tm_packet) tm_packet = (int)diff;
        //    }

        //    minimal = (int)tm_packet;
        //    if (tm_packet >= tm_flush_) minimal = (int)tm_flush_;
        //    if (minimal >= interval) minimal = (int)interval;

        //    return time + TimeSpan.FromMilliseconds(minimal);
        //}

        /// <summary>
        /// change MTU size, default is 1400
        /// </summary>
        /// <param name="mtu_"></param>
        /// <returns></returns>
        public int SetMtu(int mtu_)
        {
            if (mtu_ < 50 || mtu_ < IKCP_OVERHEAD)
            {
                return -1;
            }

            var buffer_ = new byte[(mtu_ + IKCP_OVERHEAD) * 3];
            if (null == buffer_)
            {
                return -2;
            }

            mtu = (uint)mtu_;
            mss = mtu - IKCP_OVERHEAD;
            buffer = buffer_;
            return 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="interval_"></param>
        /// <returns></returns>
        public int Interval(int interval_)
        {
            if (interval_ > 5000)
            {
                interval_ = 5000;
            }
            else if (interval_ < 10)
            {
                interval_ = 10;
            }
            interval = (uint)interval_;
            return 0;
        }

        /// <summary>
        /// fastest: ikcp_nodelay(kcp, 1, 20, 2, 1)
        /// </summary>
        /// <param name="nodelay_">0:disable(default), 1:enable</param>
        /// <param name="interval_">internal update timer interval in millisec, default is 100ms</param>
        /// <param name="resend_">0:disable fast resend(default), 1:enable fast resend</param>
        /// <param name="nc_">0:normal congestion control(default), 1:disable congestion control</param>
        /// <returns></returns>
        public int NoDelay(int nodelay_, int interval_, int resend_, int nc_)
        {

            if (nodelay_ > 0)
            {
                nodelay = (uint)nodelay_;
                if (nodelay_ != 0)
                {
                    rx_minrto = IKCP_RTO_NDL;
                }
                else
                {
                    rx_minrto = IKCP_RTO_MIN;
                }
            }

            if (interval_ >= 0)
            {
                if (interval_ > 5000)
                {
                    interval_ = 5000;
                }
                else if (interval_ < 10)
                {
                    interval_ = 10;
                }
                interval = (uint)interval_;
            }

            if (resend_ >= 0) fastresend = resend_;

            if (nc_ >= 0) nocwnd = nc_;

            return 0;
        }

        /// <summary>
        /// set maximum window size: sndwnd=32, rcvwnd=32 by default
        /// </summary>
        /// <param name="sndwnd"></param>
        /// <param name="rcvwnd"></param>
        /// <returns></returns>
        public int WndSize(int sndwnd, int rcvwnd)
        {
            if (sndwnd > 0)
            {
                snd_wnd = (uint)sndwnd;
            }

            if (rcvwnd > 0)
            {
                rcv_wnd = (uint)rcvwnd;
            }

            return 0;
        }

        /// <summary>
        /// get how many packet is waiting to be sent
        /// </summary>
        /// <returns></returns>
        public int WaitSnd => sendList.Count + sendWaitQ.Count;

        void Parse_una(uint una)
        {
            /// 删除给定时间之前的片段。保留之后的片段
            lock (sendlock)
            {
                var count = 0;

                foreach (var seg in sendList)
                {
                    if (Itimediff(una, seg.sn) > 0)
                        count++;
                    else
                        break;
                }

                if (count > 0)
                {
                    sendList.RemoveRange(0, count);
                }
            }
            
        }

        void Parse_ack(uint sn)
        {

            if (Itimediff(sn, snd_una) < 0 || Itimediff(sn, snd_nxt) >= 0)
            {
                return;
            }

            lock (sendlock)
            {
                Segment target = null;
                foreach (var seg in sendList)
                {
                    if (sn == seg.sn)
                    {
                        target = seg;
                        break;
                    }

                    if (Itimediff(sn,seg.sn) < 0)
                    {
                        break;
                    }
                }

                if (target != null)
                {
                    sendList.Remove(target);
                }
            }
        }

        void Parse_data(Segment newseg)
        {
            var sn = newseg.sn;

            if (Itimediff(sn, rcv_nxt + rcv_wnd) >= 0 || Itimediff(sn, rcv_nxt) < 0)
            {
                return;
            }

            lock (recvlock)
            {
                var n = recvList.Count - 1;
                var after_idx = -1;
                var repeat = false;

                ///检查是否重复消息和插入位置
                for (var i = n; i >= 0; i--)
                {
                    var seg = recvList[i];
                    if (seg.sn == sn)
                    {
                        repeat = true;
                        break;
                    }

                    if (Itimediff(sn, seg.sn) > 0)
                    {
                        after_idx = i;
                        break;
                    }
                }

                if (!repeat)
                {
                    if (after_idx == -1)
                    {
                        recvList.Insert(0, newseg);
                    }
                    else
                    {
                        recvList.Insert(after_idx + 1, newseg);
                    }
                }
            }

            MoveRecvAvailableDate();

        }

        void Parse_fastack(uint sn)
        {
            if (Itimediff(sn, snd_una) < 0 || Itimediff(sn, snd_nxt) >= 0)
            {
                return;
            }

            lock (sendlock)
            {
                foreach (var seg in sendList)
                {
                    if (Itimediff(sn, seg.sn) < 0)
                    {
                        break;
                    }
                    else if(sn != seg.sn)
                    {
                        seg.fastack++;
                    }
                }
            }
        }

        /// <summary>
        /// move available data from rcv_buf -> rcv_queue
        /// </summary>
        void MoveRecvAvailableDate()
        {
            lock (recvlock)
            {
                int count = 0;
                foreach (var seg in recvList)
                {
                    if (seg.sn == rcv_nxt && recvWaitList.Count < rcv_wnd)
                    {
                        lock (recvWaitlock)
                        {
                            recvWaitList.Add(seg);
                        }

                        rcv_nxt++;
                        count++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (count > 0)
                {
                    recvList.RemoveRange(0, count);
                }
            }
        }

        /// <summary>
        /// update ack.
        /// </summary>
        /// <param name="rtt"></param>
        void Update_ack(int rtt)
        {
            if (0 == rx_srtt)
            {
                rx_srtt = (uint)rtt;
                rx_rttval = (uint)rtt / 2;
            }
            else
            {
                var delta = (int)((uint)rtt - rx_srtt);

                if (0 > delta)
                {
                    delta = -delta;
                }

                rx_rttval = (3 * rx_rttval + (uint)delta) / 4;
                rx_srtt = (uint)((7 * rx_srtt + rtt) / 8);

                if (rx_srtt < 1)
                {
                    rx_srtt = 1;
                }
            }

            var rto = rx_srtt + Max(interval, 4 * rx_rttval);

            rx_rto = Ibound(rx_minrto, rto, IKCP_RTO_MAX);
        }

        int Wnd_unused()
        {
            if (recvWaitList.Count < rcv_wnd)
                return (int)rcv_wnd - recvWaitList.Count;
            return 0;
        }
    }
    
    //extension 重构和新增加的部分
    public partial class KCP
    {
        

        /// <summary>
        /// 如果外部能够提供缓冲区则使用外部缓冲区，否则new byte[]
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public BufferOwner CreateBuffer(int size)
        {
            var res = handle?.RentBuffer(size);
            if (res == null)
            {
                return new KCPInnerBuffer(size);
            }
            else
            {
                if (res.Memory.Length != size)
                {
                    throw new ArgumentException($"{nameof(handle.RentBuffer)} 指定的委托不符合标准，返回的" +
                        $"BufferOwner.Memory.Length 与 needLenght 不一致");
                }
            }

            return res;
        }

        internal protected class KCPInnerBuffer : BufferOwner
        {
            private readonly Memory<byte> _memory;

            public Memory<byte> Memory
            {
                get
                {
                    if (alreadyDisposed)
                    {
                        throw new ObjectDisposedException(nameof(KCPInnerBuffer));
                    }
                    return _memory;
                }
            }

            public KCPInnerBuffer(int size)
            {
                _memory = new Memory<byte>(new byte[size]);
            }

            bool alreadyDisposed = false;
            public void Dispose()
            {
                alreadyDisposed = true;
            }
        }

        IKCPCallback handle;
    }


    partial class KCP
    {
        ///死锁分析
        
        [System.Diagnostics.Conditional("DEADLOCK")]
        void TryInLock(string innerlock)
        {
            Console.WriteLine($"Thread[{Thread.CurrentThread.ManagedThreadId}] Try in  {innerlock}");
        }

        [System.Diagnostics.Conditional("DEADLOCK")]
        void InLock(string innerlock)
        {
            Console.WriteLine($"Thread[{Thread.CurrentThread.ManagedThreadId}]     in  {innerlock}");
        }

        [System.Diagnostics.Conditional("DEADLOCK")]
        void OutLock(string innerlock)
        {
            Console.WriteLine($"Thread[{Thread.CurrentThread.ManagedThreadId}]     Out {innerlock}");
        }

        
        public KCP KCPRemote;
    }


    public static class KCPExtension_FDF71D0BC31D49C48EEA8FAA51F017D4
    {
        private static readonly DateTime utc_time = new DateTime(1970, 1, 1);
        public static uint ConvertTime(this DateTime time)
        {
            return (uint)(Convert.ToInt64(time.Subtract(utc_time).TotalMilliseconds) & 0xffffffff);
        }
    }

    /// <summary>
    /// 调整了没存布局，直接拷贝块提升性能。
    /// </summary>
    public struct KCPSEG
    {
        readonly unsafe byte* ptr;
        private unsafe KCPSEG(byte* intPtr, uint appendDateSize)
        {
            this.ptr = intPtr;
            len = appendDateSize;
        }

        public static KCPSEG AllocHGlobal(int appendDateSize)
        {
            IntPtr intPtr = Marshal.AllocHGlobal(40 + appendDateSize);
            unsafe
            {
                return new KCPSEG((byte*)intPtr.ToPointer(), (uint)appendDateSize);
            }
        }

        public static void FreeHGlobal(KCPSEG seg)
        {
            unsafe
            {
                Marshal.FreeHGlobal((IntPtr)seg.ptr);
            }
        }
        
        /// 以下为本机使用的参数
        /// <summary>
        /// offset = 0
        /// </summary>
        public uint resendts
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + 0);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + 0) = value;
                }
            }
        }

        /// <summary>
        /// offset = 4
        /// </summary>
        public uint rto
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + 4);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + 4) = value;
                }
            }
        }

        /// <summary>
        /// offset = 8
        /// </summary>
        public uint fastack
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + 8);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + 8) = value;
                }
            }
        }

        /// <summary>
        /// offset = 12
        /// </summary>
        public uint xmit
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + 12);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + 12) = value;
                }
            }
        }

        ///以下为需要网络传输的参数
        public const int LocalOffset = 4 * 4;
        /// <summary>
        /// offset = <see cref="LocalOffset"/>
        /// </summary>
        public uint conv
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + LocalOffset + 0);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + LocalOffset + 0) = value;
                }
            }
        }

        /// <summary>
        /// offset = <see cref="LocalOffset"/> + 4
        /// </summary>
        public byte cmd
        {
            get
            {
                unsafe
                {
                    return *(ptr + LocalOffset + 4);
                }
            }
            set
            {
                unsafe
                {
                    *(ptr + LocalOffset + 4) = value;
                }
            }
        }
        
        /// <summary>
        /// offset = <see cref="LocalOffset"/> + 5
        /// </summary>
        public byte frg
        {
            get
            {
                unsafe
                {
                    return *(ptr + LocalOffset + 5);
                }
            }
            set
            {
                unsafe
                {
                    *(ptr + LocalOffset + 5) = value;
                }
            }
        }

        /// <summary>
        /// offset = <see cref="LocalOffset"/> + 6
        /// </summary>
        public ushort wnd
        {
            get
            {
                unsafe
                {
                    return *(ushort*)(ptr + LocalOffset + 6);
                }
            }
            set
            {
                unsafe
                {
                    *(ushort*)(ptr + LocalOffset + 6) = value;
                }
            }
        }

        /// <summary>
        /// offset = <see cref="LocalOffset"/> + 8
        /// </summary>
        public uint ts
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + LocalOffset + 8);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + LocalOffset + 8) = value;
                }
            }
        }
        
        /// <summary>
        /// offset = <see cref="LocalOffset"/> + 12
        /// </summary>
        public uint sn
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + LocalOffset + 12);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + LocalOffset + 12) = value;
                }
            }
        }

        /// <summary>
        /// offset = <see cref="LocalOffset"/> + 16
        /// </summary>
        public uint una
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + LocalOffset + 16);
                }
            }
            set
            {
                unsafe
                {
                    *(uint*)(ptr + LocalOffset + 16) = value;
                }
            }
        }

        /// <summary>
        /// offset = <see cref="LocalOffset"/> + 20
        /// </summary>
        public uint len
        {
            get
            {
                unsafe
                {
                    return *(uint*)(ptr + LocalOffset + 20);
                }
            }
            private set
            {
                unsafe
                {
                    *(uint*)(ptr + LocalOffset + 20) = value;
                }
            }
        }

        /// <summary>
        /// 将片段中的要发送的数据拷贝到指定缓冲区
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public int Encode(Span<byte> buffer)
        {
            var datelen = (int)(24 + len);
            if (BitConverter.IsLittleEndian)
            {
                ///网络传输统一使用大端编码
                ///小端机器则需要逐个参数按大端写入
                const int offset = 0;
                BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset), conv);
                buffer[offset + 4] = cmd;
                buffer[offset + 5] = frg;
                BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset +6), wnd);

                BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset + 8), ts);
                BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset + 12), sn);
                BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset + 16), una);
                BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset + 20), len);
                unsafe
                {
                    Span<byte> sendDate = new Span<byte>(ptr + LocalOffset + 24, (int)len);
                    sendDate.CopyTo(buffer);
                }
            }
            else
            {
                ///大端可以一次拷贝
                unsafe
                {
                    ///要发送的数据从LocalOffset开始。
                    ///这里调整要发送字段和本机使用字段的位置，让数据和附加数据连续，节约一次拷贝。
                    Span<byte> sendDate = new Span<byte>(ptr + LocalOffset, datelen);
                    sendDate.CopyTo(buffer);
                }
            }
            return datelen;
        }
    }

}










