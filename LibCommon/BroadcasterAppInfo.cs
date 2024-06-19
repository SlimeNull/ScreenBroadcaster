using System.Runtime.InteropServices;

namespace LibCommon
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BroadcasterAppInfo
    {
        public int Version;
    }
}
