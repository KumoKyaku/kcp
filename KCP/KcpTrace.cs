using System;
using System.Collections.Generic;
using System.Text;

namespace System.Net.Sockets.Kcp
{
    public partial class KcpCore<Segment>
    {
#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
        public System.Diagnostics.TraceListener TraceListener { get; set; }
#endif
    }
}


