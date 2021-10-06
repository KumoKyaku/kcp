using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

//[assembly: InternalsVisibleTo("UnitTestProject1")]

namespace System.Net.Sockets.Kcp
{
    internal static class KcpExtension_FDF71D0BC31D49C48EEA8FAA51F017D4
    {
        private static readonly DateTime utc_time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ConvertTime(this in DateTime time)
        {
            return (uint)(Convert.ToInt64(time.Subtract(utc_time).TotalMilliseconds) & 0xffffffff);
        }

        private static readonly DateTimeOffset utc1970 = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ConvertTime(this in DateTimeOffset time)
        {
            return (uint)(Convert.ToInt64(time.Subtract(utc1970).TotalMilliseconds) & 0xffffffff);
        }
    }
}
