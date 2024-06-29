using System.Reflection;
using System.Runtime.InteropServices;
using SharpDX;

namespace Sn.ScreenBroadcaster.Data.Packets
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BroadcasterAppInfo
    {
        public int Version;
    }
}
