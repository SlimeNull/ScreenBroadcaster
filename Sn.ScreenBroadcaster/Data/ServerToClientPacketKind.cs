namespace Sn.ScreenBroadcaster.Data;

public enum ServerToClientPacketKind : ushort
{
    Frame,
    NotifyCanControl,
    NotifyCanNotControl,
    NotifyRejectControl
}
