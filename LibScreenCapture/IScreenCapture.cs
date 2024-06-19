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


        public int Width { get; }
        public int Height { get; }

        public bool Capture();
        public bool Capture(TimeSpan timeout);
    }
}
