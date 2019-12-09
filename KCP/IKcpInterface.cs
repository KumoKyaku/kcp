using BufferOwner = System.Buffers.IMemoryOwner<byte>;

namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// Kcp回调
    /// </summary>
    public interface IKcpCallback
    {
        /// <summary>
        /// kcp 发送方向输出
        /// </summary>
        /// <param name="buffer">kcp 交出发送缓冲区控制权，缓冲区来自<see cref="RentBuffer(int)"/></param>
        /// <param name="avalidLength">数据的有效长度</param>
        /// <returns>不需要返回值</returns>
        /// <remarks>通过增加 avalidLength 能够在协议栈中有效的减少数据拷贝</remarks>
        void Output(BufferOwner buffer, int avalidLength);
    }


    /// <summary>
    /// 外部提供缓冲区,可以在外部链接一个内存池
    /// </summary>
    public interface IRentable
    {
        /// <summary>
        /// 外部提供缓冲区,可以在外部链接一个内存池
        /// </summary>
        BufferOwner RentBuffer(int length);
    }

    public interface IKcpSetting
    {
        int Interval(int interval_);
        int NoDelay(int nodelay_, int interval_, int resend_, int nc_);
        int SetMtu(int mtu_);
        int WndSize(int sndwnd, int rcvwnd);
    }

    public interface IKcpUpdate
    {
        void Update(in DateTime time);
    }
}