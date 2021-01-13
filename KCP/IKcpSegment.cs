using System.Runtime.InteropServices;

namespace System.Net.Sockets.Kcp
{
    public interface IKcpSegment
    {
        byte cmd { get; set; }
        uint conv { get; set; }
        Span<byte> data { get; }
        uint fastack { get; set; }
        byte frg { get; set; }
        uint len { get; }
        uint resendts { get; set; }
        uint rto { get; set; }
        uint sn { get; set; }
        uint ts { get; set; }
        uint una { get; set; }
        ushort wnd { get; set; }
        uint xmit { get; set; }

        int Encode(Span<byte> buffer);
    }

    public interface ISegmentManager<S> where S: IKcpSegment
    {
        S Alloc(int appendDateSize);
        void FreeHGlobal(S seg);
    }

    public class SimpleSegManager: ISegmentManager<KcpSegment>
    {
        public KcpSegment Alloc(int appendDateSize)
        {
            var total = KcpSegment.LocalOffset + KcpSegment.HeadOffset + appendDateSize;
            IntPtr intPtr = Marshal.AllocHGlobal(total);
            unsafe
            {
                ///清零    不知道是不是有更快的清0方法？
                Span<byte> span = new Span<byte>(intPtr.ToPointer(), total);
                span.Clear();

                return new KcpSegment((byte*)intPtr.ToPointer(), (uint)appendDateSize);
            }
        }

        public void FreeHGlobal(KcpSegment seg)
        {
            unsafe
            {
                Marshal.FreeHGlobal((IntPtr)seg.ptr);
            }
        }
    }
}