using System.Runtime.InteropServices;

namespace Sn.ScreenBroadcaster.Data.Packets;

[StructLayout(LayoutKind.Sequential)]
public struct GrantControlPacket
{
    public bool IsAdministrator;
}
