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

    public interface ISegmentManager<Segment> where Segment : IKcpSegment
    {
        Segment Alloc(int appendDateSize);
        void Free(Segment seg);
    }

}



