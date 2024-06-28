using System.Runtime.InteropServices;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Sn.ScreenBroadcaster.Data.Packets;

[StructLayout(LayoutKind.Sequential)]
internal struct ControlPacketData
{
    public ControlKind Kind;
    public InputUnion Input;

    internal enum ControlKind
    {
        Mouse,
        Keyboard,
        Hardware
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT MouseInput;

        [FieldOffset(0)]
        public KEYBDINPUT KeyboardInput;

        [FieldOffset(0)]
        public HARDWAREINPUT HardwareInput;
    }
}