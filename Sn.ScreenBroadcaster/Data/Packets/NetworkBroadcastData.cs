using System.Reflection;
using System.Runtime.InteropServices;

namespace Sn.ScreenBroadcaster.Data.Packets
{
    /// <summary>
    /// For UDP Broadcast
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NetworkBroadcastData
    {
        public fixed char AppName[32];
        public int Version;

        public static NetworkBroadcastData Create()
        {
            var result = default(NetworkBroadcastData);

            string appName = "Sn.ScreenBroadcaster";
            for (int i = 0; i < appName.Length; i++)
            {
                result.AppName[i] = appName[i];
            }

            result.Version = Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0;

            return result;
        }

        public bool IsValid()
        {
            string appName = "Sn.ScreenBroadcaster";
            for (int i = 0; i < appName.Length; i++)
            {
                if (AppName[i] != appName[i])
                    return false;
            }

            if (Version != (Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0))
                return false;

            return true;
        }
    }
}
