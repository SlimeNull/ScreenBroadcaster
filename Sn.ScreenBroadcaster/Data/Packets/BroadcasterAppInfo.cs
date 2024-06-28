using System.Runtime.InteropServices;

namespace Sn.ScreenBroadcaster.Data.Packets
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BroadcasterAppInfo
    {
        public int Version;
    }
}
