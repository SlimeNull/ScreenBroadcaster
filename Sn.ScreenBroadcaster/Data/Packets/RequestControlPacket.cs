using System.Runtime.InteropServices;

namespace Sn.ScreenBroadcaster.Data.Packets;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RequestControlPacket
{
    public fixed char UserName[32];

    public static RequestControlPacket Create(string userName)
    {
        var result = default(RequestControlPacket);

        int i = 0;
        for (; i < userName.Length && i < 31; i++)
            result.UserName[i] = userName[i];

        result.UserName[i] = '\0';

        return result;
    }
}