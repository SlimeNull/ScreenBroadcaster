using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpDX.Win32;

namespace LibScreenCapture
{
    public interface IScreenCapture : IDisposable
    {
        public nint DataPointer { get; }
        public int PixelBytes { get; }
        public int Stride { get; }

        public int ScreenX { get; }
        public int ScreenY { get; }
        public int ScreenWidth { get; }
        public int ScreenHeight { get; }

        public bool Capture();
        public bool Capture(TimeSpan timeout);
    }
}
