using System;
using System.Collections.Generic;
using System.Text;
using static System.Math;
using System.Collections.Concurrent;
using System.Buffers;
using BufferOwner = System.Buffers.IMemoryOwner<byte>;
using System.Linq;
using System.Buffers.Binary;

namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// KCP回调
    /// </summary>
    public interface IKcpCallback
    {
        /// <summary>
        /// kcp 发送方向输出
        /// </summary>
        /// <param name="buffer">kcp 无法交出发送缓冲区控制权 </param>
        /// <returns>不需要返回值</returns>
        /// <remarks>因为kcp需要丢包重传，所以无法交出发送缓冲区的控制权/remarks>
        void Output(ReadOnlySpan<byte> buffer);
        
        /// <summary>
        /// 外部提供缓冲区
        /// <para></para>
        /// 注意 BufferOwner.Memory.Length 需要等于 needLenght。
        /// </summary>
        BufferOwner RentBuffer(int lenght);
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