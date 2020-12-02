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
    public class KcpCore: IKcpSetting, IKcpUpdate, IDisposable
    {
        // 为了减少阅读难度，变量名尽量于 C版 统一
        /*
        conv 会话ID
        mtu 最大传输单元
        mss 最大分片大小
        state 连接状态（0xFFFFFFFF表示断开连接）
        snd_una 第一个未确认的包
        snd_nxt 待发送包的序号
        rcv_nxt 待接收消息序号
        ssthresh 拥塞窗口阈值
        rx_rttvar ack接收rtt浮动值
        rx_srtt ack接收rtt静态值
        rx_rto 由ack接收延迟计算出来的复原时间
        rx_minrto 最小复原时间
        snd_wnd 发送窗口大小
        rcv_wnd 接收窗口大小
        rmt_wnd,	远端接收窗口大小
        cwnd, 拥塞窗口大小
        probe 探查变量，IKCP_ASK_TELL表示告知远端窗口大小。IKCP_ASK_SEND表示请求远端告知窗口大小
        interval    内部flush刷新间隔
        ts_flush 下次flush刷新时间戳
        nodelay 是否启动无延迟模式
        updated 是否调用过update函数的标识
        ts_probe, 下次探查窗口的时间戳
        probe_wait 探查窗口需要等待的时间
        dead_link 最大重传次数
        incr 可发送的最大数据量
        fastresend 触发快速重传的重复ack个数
        nocwnd 取消拥塞控制
        stream 是否采用流传输模式

        snd_queue 发送消息的队列
        rcv_queue 接收消息的队列
        snd_buf 发送消息的缓存
        rcv_buf 接收消息的缓存
        acklist 待发送的ack列表
        buffer 存储消息字节流的内存
        output udp发送消息的回调函数
        */

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
        public const int IKCP_WND_RCV = 128; // must >= max fragment size
        public const int IKCP_MTU_DEF = 1400;
        public const int IKCP_ACK_FAST = 3;
        public const int IKCP_INTERVAL = 100;
        public const int IKCP_OVERHEAD = 24;
        public const int IKCP_DEADLINK = 20;
        public const int IKCP_THRESH_INIT = 2;
        public const int IKCP_THRESH_MIN = 2;
        public const int IKCP_PROBE_INIT = 7000;   // 7 secs to probe window size
        public const int IKCP_PROBE_LIMIT = 120000; // up to 120 secs to probe window
        public const int IKCP_FASTACK_LIMIT = 5;		// max times to trigger fastack
        #endregion

        #region kcp members
        /// <summary>
        /// 频道号
        /// </summary>
        public uint conv { get; protected set; }
        /// <summary>
        /// 最大传输单元（Maximum Transmission Unit，MTU）
        /// </summary>
        protected uint mtu;

        /// <summary>
        /// 缓冲区最小大小
        /// </summary>
        protected int BufferNeedSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (int)((mtu/* + IKCP_OVERHEAD*/) /** 3*/);
            }
        }

        /// <summary>
        /// 最大报文段长度
        /// </summary>
        protected uint mss;
        protected int state;
        protected uint snd_una;
        protected uint snd_nxt;
        /// <summary>
        /// 下一个等待接收消息ID
        /// </summary>
        protected uint rcv_nxt;
        protected uint ts_recent;
        protected uint ts_lastack;
        protected uint ssthresh;
        protected uint rx_rttval;
        protected uint rx_srtt;
        protected uint rx_rto;
        protected uint rx_minrto;
        protected uint snd_wnd;
        protected uint rcv_wnd;
        protected uint rmt_wnd;
        protected uint cwnd;
        protected uint probe;
        protected uint current;
        protected uint interval;
        protected uint ts_flush;
        protected uint xmit;
        protected uint nodelay;
        protected uint updated;
        protected uint ts_probe;
        protected uint probe_wait;
        protected uint dead_link;
        protected uint incr;
        protected int fastresend;
        protected int fastlimit;
        protected int nocwnd;
        protected int logmask;
        public int stream;
        protected BufferOwner buffer;

        /// <summary>
        /// <para>https://github.com/skywind3000/kcp/issues/53</para>
        /// 按照 C版 设计，使用小端字节序
        /// </summary>
        public static bool IsLittleEndian = true;

        #endregion

        #region 锁和容器

        protected readonly object snd_bufLock = new object();
        protected readonly object rcv_queueLock = new object();
        protected readonly object rcv_bufLock = new object();

        /// <summary>
        /// 发送 ack 队列 
        /// </summary>
        protected ConcurrentQueue<(uint sn, uint ts)> acklist = new ConcurrentQueue<(uint sn, uint ts)>();
        /// <summary>
        /// 发送等待队列
        /// </summary>
        internal ConcurrentQueue<KcpSegment> snd_queue = new ConcurrentQueue<KcpSegment>();
        /// <summary>
        /// 正在发送列表
        /// </summary>
        internal LinkedList<KcpSegment> snd_buf = new LinkedList<KcpSegment>();
        /// <summary>
        /// 正在等待触发接收回调函数消息列表
        /// <para>需要执行的操作  添加 遍历 删除</para>
        /// </summary>
        internal List<KcpSegment> rcv_queue = new List<KcpSegment>();
        /// <summary>
        /// 正在等待重组消息列表
        /// <para>需要执行的操作  添加 插入 遍历 删除</para>
        /// </summary>
        internal LinkedList<KcpSegment> rcv_buf = new LinkedList<KcpSegment>();

        /// <summary>
        /// get how many packet is waiting to be sent
        /// </summary>
        /// <returns></returns>
        public int WaitSnd => snd_buf.Count + snd_queue.Count;

        #endregion

        public KcpCore(uint conv_)
        {
            conv = conv_;

            snd_wnd = IKCP_WND_SND;
            rcv_wnd = IKCP_WND_RCV;
            rmt_wnd = IKCP_WND_RCV;
            mtu = IKCP_MTU_DEF;
            mss = mtu - IKCP_OVERHEAD;
            buffer = CreateBuffer(BufferNeedSize);

            rx_rto = IKCP_RTO_DEF;
            rx_minrto = IKCP_RTO_MIN;
            interval = IKCP_INTERVAL;
            ts_flush = IKCP_INTERVAL;
            ssthresh = IKCP_THRESH_INIT;
            fastlimit = IKCP_FASTACK_LIMIT;
            dead_link = IKCP_DEADLINK;
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        /// <summary>
        /// 是否正在释放
        /// </summary>
        private bool m_disposing = false;

        protected bool CheckDispose()
        {
            if (m_disposing)
            {
                return true;
            }

            if (disposedValue)
            {
                throw new ObjectDisposedException(
                    $"{nameof(Kcp)} [conv:{conv}]");
            }

            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                m_disposing = true;
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // 释放托管状态(托管对象)。
                        callbackHandle = null;
                        acklist = null;
                        buffer = null;
                    }

                    // 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                    // 将大型字段设置为 null。
                    void FreeCollection(IEnumerable<KcpSegment> collection)
                    {
                        if (collection == null)
                        {
                            return;
                        }
                        foreach (var item in collection)
                        {
                            try
                            {
                                KcpSegment.FreeHGlobal(item);
                            }
                            catch (Exception)
                            {
                                //理论上此处不会有任何异常
                            }
                        }
                    }

                    while (snd_queue != null &&
                        (snd_queue.TryDequeue(out var segment)
                        || !snd_queue.IsEmpty)
                        )
                    {
                        try
                        {
                            KcpSegment.FreeHGlobal(segment);
                        }
                        catch (Exception)
                        {
                            //理论上这里没有任何异常；
                        }
                    }
                    snd_queue = null;

                    lock (snd_bufLock)
                    {
                        FreeCollection(snd_buf);
                        snd_buf?.Clear();
                        snd_buf = null;
                    }

                    lock (rcv_bufLock)
                    {
                        FreeCollection(rcv_buf);
                        rcv_buf?.Clear();
                        rcv_buf = null;
                    }

                    lock (rcv_queueLock)
                    {
                        FreeCollection(rcv_queue);
                        rcv_queue?.Clear();
                        rcv_queue = null;
                    }


                    disposedValue = true;
                }
            }
            finally
            {
                m_disposing = false;
            }

        }

        // 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        ~KcpCore()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(false);
        }

        // 添加此代码以正确实现可处置模式。
        /// <summary>
        /// 释放不是严格线程安全的，尽量使用和Update相同的线程调用，
        /// 或者等待析构时自动释放。
        /// </summary>
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // 如果在以上内容中替代了终结器，则取消注释以下行。
            GC.SuppressFinalize(this);
        }

        #endregion

        internal protected IKcpCallback callbackHandle;

        protected static uint Ibound(uint lower, uint middle, uint upper)
        {
            return Min(Max(lower, middle), upper);
        }

        protected static int Itimediff(uint later, uint earlier)
        {
            return ((int)(later - earlier));
        }

        internal protected virtual BufferOwner CreateBuffer(int needSize)
        {
            return new KcpInnerBuffer(needSize);
        }

        internal protected class KcpInnerBuffer : BufferOwner
        {
            private readonly Memory<byte> _memory;

            public Memory<byte> Memory
            {
                get
                {
                    if (alreadyDisposed)
                    {
                        throw new ObjectDisposedException(nameof(KcpInnerBuffer));
                    }
                    return _memory;
                }
            }

            public KcpInnerBuffer(int size)
            {
                _memory = new Memory<byte>(new byte[size]);
            }

            bool alreadyDisposed = false;
            public void Dispose()
            {
                alreadyDisposed = true;
            }
        }


        #region 功能逻辑

        //功能函数

        /// <summary>
        /// Determine when should you invoke ikcp_update:
        /// returns when you should invoke ikcp_update in millisec, if there
        /// is no ikcp_input/_send calling. you can call ikcp_update in that
        /// time, instead of call update repeatly.
        /// <para></para>
        /// Important to reduce unnacessary ikcp_update invoking. use it to
        /// schedule ikcp_update (eg. implementing an epoll-like mechanism,
        /// or optimize ikcp_update when handling massive kcp connections)
        /// <para></para>
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public DateTime Check(DateTime time)
        {
            if (CheckDispose())
            {
                //检查释放
                return default;
            }

            if (updated == 0)
            {
                return time;
            }

            var current_ = time.ConvertTime();

            var ts_flush_ = ts_flush;
            var tm_flush_ = 0x7fffffff;
            var tm_packet = 0x7fffffff;
            var minimal = 0;

            if (Itimediff(current_, ts_flush_) >= 10000 || Itimediff(current_, ts_flush_) < -10000)
            {
                ts_flush_ = current_;
            }

            if (Itimediff(current_, ts_flush_) >= 0)
            {
                return time;
            }

            tm_flush_ = Itimediff(ts_flush_, current_);

            lock (snd_bufLock)
            {
                foreach (var seg in snd_buf)
                {
                    var diff = Itimediff(seg.resendts, current_);
                    if (diff <= 0)
                    {
                        return time;
                    }

                    if (diff < tm_packet)
                    {
                        tm_packet = diff;
                    }
                }
            }

            minimal = tm_packet < tm_flush_ ? tm_packet : tm_flush_;
            if (minimal >= interval) minimal = (int)interval;

            return time + TimeSpan.FromMilliseconds(minimal);
        }

        /// <summary>
        /// move available data from rcv_buf -> rcv_queue
        /// </summary>
        protected void Move_Rcv_buf_2_Rcv_queue()
        {
            lock (rcv_bufLock)
            {
                while (rcv_buf.Count > 0)
                {
                    var seg = rcv_buf.First.Value;
                    if (seg.sn == rcv_nxt && rcv_queue.Count < rcv_wnd)
                    {
                        rcv_buf.RemoveFirst();
                        lock (rcv_queueLock)
                        {
                            rcv_queue.Add(seg);
                        }

                        rcv_nxt++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// update ack.
        /// </summary>
        /// <param name="rtt"></param>
        protected void Update_ack(int rtt)
        {
            if (rx_srtt == 0)
            {
                rx_srtt = (uint)rtt;
                rx_rttval = (uint)rtt / 2;
            }
            else
            {
                int delta = (int)((uint)rtt - rx_srtt);

                if (delta < 0)
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

        protected void Shrink_buf()
        {
            lock (snd_bufLock)
            {
                snd_una = snd_buf.Count > 0 ? snd_buf.First.Value.sn : snd_nxt;
            }
        }

        protected void Parse_ack(uint sn)
        {
            if (Itimediff(sn, snd_una) < 0 || Itimediff(sn, snd_nxt) >= 0)
            {
                return;
            }

            lock (snd_bufLock)
            {
                for (var p = snd_buf.First; p != null; p = p.Next)
                {
                    var seg = p.Value;
                    if (sn == seg.sn)
                    {
                        snd_buf.Remove(p);
                        KcpSegment.FreeHGlobal(seg);
                        break;
                    }

                    if (Itimediff(sn, seg.sn) < 0)
                    {
                        break;
                    }
                }
            }
        }

        protected void Parse_una(uint una)
        {
            /// 删除给定时间之前的片段。保留之后的片段
            lock (snd_bufLock)
            {
                while (snd_buf.First != null)
                {
                    var seg = snd_buf.First.Value;
                    if (Itimediff(una, seg.sn) > 0)
                    {
                        KcpSegment.FreeHGlobal(seg);
                        snd_buf.RemoveFirst();
                    }
                    else
                    {
                        break;
                    }
                }
            }

        }

        protected void Parse_fastack(uint sn)
        {
            if (Itimediff(sn, snd_una) < 0 || Itimediff(sn, snd_nxt) >= 0)
            {
                return;
            }

            lock (snd_bufLock)
            {
                foreach (var item in snd_buf)
                {
                    var seg = item;
                    if (Itimediff(sn, seg.sn) < 0)
                    {
                        break;
                    }
                    else if (sn != seg.sn)
                    {
                        seg.fastack++;
                    }
                }
            }
        }

        internal virtual void Parse_data(KcpSegment newseg)
        {
            var sn = newseg.sn;

            lock (rcv_bufLock)
            {
                if (Itimediff(sn, rcv_nxt + rcv_wnd) >= 0 || Itimediff(sn, rcv_nxt) < 0)
                {
                    KcpSegment.FreeHGlobal(newseg);
                    return;
                }

                var repeat = false;

                ///检查是否重复消息和插入位置
                LinkedListNode<KcpSegment> p;
                for (p = rcv_buf.Last; p != null; p = p.Previous)
                {
                    var seg = p.Value;
                    if (seg.sn == sn)
                    {
                        repeat = true;
                        break;
                    }

                    if (Itimediff(sn, seg.sn) > 0)
                    {
                        break;
                    }
                }

                if (!repeat)
                {
                    if (p == null)
                    {
                        rcv_buf.AddFirst(newseg);
                    }
                    else
                    {
                        rcv_buf.AddAfter(p, newseg);
                    }

                }
                else
                {
                    KcpSegment.FreeHGlobal(newseg);
                }
            }

            Move_Rcv_buf_2_Rcv_queue();
        }

        protected ushort Wnd_unused()
        {
            ///此处没有加锁，所以不要内联变量，否则可能导致 判断变量和赋值变量不一致
            int waitCount = rcv_queue.Count;

            if (waitCount < rcv_wnd)
            {
                /// fix https://github.com/skywind3000/kcp/issues/126
                /// 实际上 rcv_wnd 不应该大于65535
                var count = rcv_wnd - waitCount;
                return (ushort)Min(count, ushort.MaxValue);
            }

            return 0;
        }

        /// <summary>
        /// flush pending data
        /// </summary>
        protected void Flush()
        {
            var current_ = current;
            var buffer_ = buffer;
            var change = 0;
            var lost = 0;
            var offset = 0;

            if (updated == 0)
            {
                return;
            }

            ushort wnd_ = Wnd_unused();

            unsafe
            {
                ///在栈上分配这个segment,这个segment随用随销毁，不会被保存
                const int len = KcpSegment.LocalOffset + KcpSegment.HeadOffset;
                var ptr = stackalloc byte[len];
                KcpSegment seg = new KcpSegment(ptr, 0);
                //seg = KcpSegment.AllocHGlobal(0);

                seg.conv = conv;
                seg.cmd = IKCP_CMD_ACK;
                //seg.frg = 0;
                seg.wnd = wnd_;
                seg.una = rcv_nxt;
                //seg.len = 0;
                //seg.sn = 0;
                //seg.ts = 0;

                #region flush acknowledges

                if (CheckDispose())
                {
                    //检查释放
                    return;
                }

                while (acklist.TryDequeue(out var temp))
                {
                    if (offset + IKCP_OVERHEAD > mtu)
                    {
                        callbackHandle.Output(buffer, offset);
                        offset = 0;
                        buffer = CreateBuffer(BufferNeedSize);
                    }

                    seg.sn = temp.sn;
                    seg.ts = temp.ts;
                    offset += seg.Encode(buffer.Memory.Span.Slice(offset));
                }

                #endregion

                #region probe window size (if remote window size equals zero)
                // probe window size (if remote window size equals zero)
                if (rmt_wnd == 0)
                {
                    if (probe_wait == 0)
                    {
                        probe_wait = IKCP_PROBE_INIT;
                        ts_probe = current + probe_wait;
                    }
                    else
                    {
                        if (Itimediff(current, ts_probe) >= 0)
                        {
                            if (probe_wait < IKCP_PROBE_INIT)
                            {
                                probe_wait = IKCP_PROBE_INIT;
                            }

                            probe_wait += probe_wait / 2;

                            if (probe_wait > IKCP_PROBE_LIMIT)
                            {
                                probe_wait = IKCP_PROBE_LIMIT;
                            }

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
                        callbackHandle.Output(buffer, offset);
                        offset = 0;
                        buffer = CreateBuffer(BufferNeedSize);
                    }
                    offset += seg.Encode(buffer.Memory.Span.Slice(offset));
                }

                if ((probe & IKCP_ASK_TELL) != 0)
                {
                    seg.cmd = IKCP_CMD_WINS;
                    if (offset + IKCP_OVERHEAD > (int)mtu)
                    {
                        callbackHandle.Output(buffer, offset);
                        offset = 0;
                        buffer = CreateBuffer(BufferNeedSize);
                    }
                    offset += seg.Encode(buffer.Memory.Span.Slice(offset));
                }

                probe = 0;
                #endregion
            }

            #region 刷新，将发送等待列表移动到发送列表

            // calculate window size
            var cwnd_ = Min(snd_wnd, rmt_wnd);
            if (nocwnd == 0)
            {
                cwnd_ = Min(cwnd, cwnd_);
            }

            while (Itimediff(snd_nxt, snd_una + cwnd_) < 0)
            {
                if (snd_queue.TryDequeue(out var newseg))
                {
                    newseg.conv = conv;
                    newseg.cmd = IKCP_CMD_PUSH;
                    newseg.wnd = wnd_;
                    newseg.ts = current_;
                    newseg.sn = snd_nxt;
                    snd_nxt++;
                    newseg.una = rcv_nxt;
                    newseg.resendts = current_;
                    newseg.rto = rx_rto;
                    newseg.fastack = 0;
                    newseg.xmit = 0;
                    lock (snd_bufLock)
                    {
                        snd_buf.AddLast(newseg);
                    }
                }
                else
                {
                    break;
                }
            }

            #endregion

            #region 刷新 发送列表，调用Output

            // calculate resent
            var resent = fastresend > 0 ? (uint)fastresend : 0xffffffff;
            var rtomin = nodelay == 0 ? (rx_rto >> 3) : 0;

            lock (snd_bufLock)
            {
                // flush data segments
                foreach (var item in snd_buf)
                {
                    var segment = item;
                    var needsend = false;
                    var debug = Itimediff(current_, segment.resendts);
                    if (segment.xmit == 0)
                    {
                        needsend = true;
                        segment.xmit++;
                        segment.rto = rx_rto;
                        segment.resendts = current_ + rx_rto + rtomin;
                    }
                    else if (Itimediff(current_, segment.resendts) >= 0)
                    {
                        needsend = true;
                        segment.xmit++;
                        this.xmit++;
                        if (nodelay == 0)
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
                        if (segment.xmit <= fastlimit
                            || fastlimit <= 0)
                        {
                            needsend = true;
                            segment.xmit++;
                            segment.fastack = 0;
                            segment.resendts = current_ + segment.rto;
                            change++;
                        }
                    }

                    if (needsend)
                    {
                        segment.ts = current_;
                        segment.wnd = wnd_;
                        segment.una = rcv_nxt;

                        var need = IKCP_OVERHEAD + segment.len;
                        if (offset + need > mtu)
                        {
                            callbackHandle.Output(buffer, offset);
                            offset = 0;
                            buffer = CreateBuffer(BufferNeedSize);
                        }

                        offset += segment.Encode(buffer.Memory.Span.Slice(offset));

                        if (segment.xmit >= dead_link)
                        {
                            state = -1;
                        }
                    }
                }
            }


            // flash remain segments
            if (offset > 0)
            {
                callbackHandle.Output(buffer, offset);
                offset = 0;
                buffer = CreateBuffer(BufferNeedSize);
            }

            #endregion

            #region update ssthresh
            // update ssthresh
            if (change != 0)
            {
                var inflight = snd_nxt - snd_una;
                ssthresh = inflight / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                {
                    ssthresh = IKCP_THRESH_MIN;
                }

                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }

            if (lost != 0)
            {
                ssthresh = cwnd / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                {
                    ssthresh = IKCP_THRESH_MIN;
                }

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

        /// <summary>
        /// update state (call it repeatedly, every 10ms-100ms), or you can ask
        /// ikcp_check when to call it again (without ikcp_input/_send calling).
        /// </summary>
        /// <param name="time">DateTime.UtcNow</param>
        public void Update(in DateTime time)
        {
            if (CheckDispose())
            {
                //检查释放
                return;
            }

            current = time.ConvertTime();

            if (updated == 0)
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
        }
        #endregion

        #region 设置控制

        /// <summary>
        /// change MTU size, default is 1400
        /// <para>** 这个方法不是线程安全的。请在没有发送和接收时调用 。</para>
        /// </summary>
        /// <param name="mtu"></param>
        /// <returns></returns>
        public int SetMtu(int mtu)
        {
            if (mtu < 50 || mtu < IKCP_OVERHEAD)
            {
                return -1;
            }

            var buffer_ = CreateBuffer(BufferNeedSize);
            if (null == buffer_)
            {
                return -2;
            }

            this.mtu = (uint)mtu;
            mss = this.mtu - IKCP_OVERHEAD;
            buffer.Dispose();
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
            else if (interval_ < 0)
            {
                /// 将最小值 10 改为 0；
                ///在特殊形况下允许CPU满负荷运转；
                interval_ = 0;
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

            if (resend_ >= 0)
            {
                fastresend = resend_;
            }

            if (nc_ >= 0)
            {
                nocwnd = nc_;
            }

            return Interval(interval_);
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

        #endregion
    }
}
