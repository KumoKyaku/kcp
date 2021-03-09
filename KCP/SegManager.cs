using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace System.Net.Sockets.Kcp
{
    public class SimpleSegManager : ISegmentManager<KcpSegment>
    {
        public static SimpleSegManager Default { get; } = new SimpleSegManager();
        public KcpSegment Alloc(int appendDateSize)
        {
            return KcpSegment.AllocHGlobal(appendDateSize);
        }

        public void Free(KcpSegment seg)
        {
            KcpSegment.FreeHGlobal(seg);
        }
    }

    /// <summary>
    /// 使用这个就不能SetMtu了，大小已经写死
    /// </summary>
    /// <remarks>需要大量测试</remarks>
    public unsafe class UnSafeSegManager : ISegmentManager<KcpSegment>
    {
        public static UnSafeSegManager Default { get; } = new UnSafeSegManager();
        /// <summary>
        /// 因为默认mtu是1400，并且内存需要内存行/内存页对齐。这里直接512对齐。
        /// </summary>
        public const int blockSize = 512 * 3;
        public HashSet<IntPtr> header = new HashSet<IntPtr>();
        public Stack<IntPtr> blocks = new Stack<IntPtr>();
        public readonly object locker = new object();
        public UnSafeSegManager()
        {
            Alloc();
        }

        void Alloc()
        {
            int count = 50;
            IntPtr intPtr = Marshal.AllocHGlobal(blockSize * count);
            header.Add(intPtr);
            for (int i = 0; i < count; i++)
            {
                blocks.Push(intPtr + blockSize * i);
            }
        }

        ~UnSafeSegManager()
        {
            foreach (var item in header)
            {
                Marshal.FreeHGlobal(item);
            }
        }

        public KcpSegment Alloc(int appendDateSize)
        {
            lock (locker)
            {
                var total = KcpSegment.LocalOffset + KcpSegment.HeadOffset + appendDateSize;
                if (total > blockSize)
                {
                    throw new ArgumentOutOfRangeException();
                }

                if (blocks.Count > 0)
                {

                }
                else
                {
                    Alloc();
                }

                var ptr = blocks.Pop();
                Span<byte> span = new Span<byte>(ptr.ToPointer(), blockSize);
                span.Clear();
                return new KcpSegment((byte*)ptr.ToPointer(), (uint)appendDateSize);
            }
        }

        public void Free(KcpSegment seg)
        {
            IntPtr ptr = (IntPtr)seg.ptr;
            blocks.Push(ptr);
        }

        public class Kcp : Kcp<KcpSegment>
        {
            public Kcp(uint conv_, IKcpCallback callback, IRentable rentable = null)
                : base(conv_, callback, rentable)
            {
                SegmentManager = Default;
            }
        }
    }
}

