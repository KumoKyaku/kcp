namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// Kcp报头
    /// https://zhuanlan.zhihu.com/p/559191428
    /// </summary>
    public interface IKcpHeader
    {
        /// <summary>
        /// 会话编号，两方一致才会通信
        /// </summary>
        uint conv { get; set; }
        /// <summary>
        /// 指令类型 ACK报文、数据报文、探测window报文、响应窗口报文四种
        /// </summary>
        byte cmd { get; set; }
        /// <summary>
        /// 分片编号 倒数第几个seg。主要就是用来合并一块被分段的数据。
        /// </summary>
        byte frg { get; set; }
        /// <summary>
        /// 自己可用窗口大小    
        /// </summary>
        ushort wnd { get; set; }
        /// <summary>
        /// 发送时的时间戳 <seealso cref="DateTimeOffset.ToUnixTimeMilliseconds"/>
        /// </summary>
        uint ts { get; set; }
        /// <summary>
        /// 编号 确认编号或者报文编号
        /// </summary>
        uint sn { get; set; }
        /// <summary>
        /// 代表编号前面的所有报都收到了的标志
        /// </summary>
        uint una { get; set; }
        /// <summary>
        /// 数据内容长度
        /// </summary>
        uint len { get; }
    }
    public interface IKcpSegment : IKcpHeader
    {
        /// <summary>
        /// 重传的时间戳。超过当前时间重发这个包
        /// </summary>
        uint resendts { get; set; }
        /// <summary>
        /// 超时重传时间，根据网络去定
        /// </summary>
        uint rto { get; set; }
        /// <summary>
        /// 快速重传机制，记录被跳过的次数，超过次数进行快速重传
        /// </summary>
        uint fastack { get; set; }
        /// <summary>
        /// 重传次数
        /// </summary>
        uint xmit { get; set; }

        /// <summary>
        /// 数据内容
        /// </summary>
        Span<byte> data { get; }
        /// <summary>
        /// 将IKcpSegment编码成字节数组，并返回总长度（包括Kcp报头）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        int Encode(Span<byte> buffer);
    }

    public interface ISegmentManager<Segment> where Segment : IKcpSegment
    {
        Segment Alloc(int appendDateSize);
        void Free(Segment seg);
    }

}



