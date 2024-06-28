namespace Sn.ScreenBroadcaster.Data;

public enum ClientToServerPacketKind : ushort
{
    Control,
    RequestControl,
    RelinquishControl,
}