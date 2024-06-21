using System.Runtime.InteropServices;

namespace Sn.ScreenBroadcaster.Data
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BroadcasterAppInfo
    {
        public int Version;
    }
}
