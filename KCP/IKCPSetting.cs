namespace System.Net.Sockets.Protocol
{
    public interface IKCPSetting
    {
        int Interval(int interval_);
        int NoDelay(int nodelay_, int interval_, int resend_, int nc_);
        int SetMtu(int mtu_);
        int WndSize(int sndwnd, int rcvwnd);
    }
}