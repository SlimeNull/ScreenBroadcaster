using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace LibScreenCapture
{
    public class GdiScreenCapture : IScreenCapture, IDisposable
    {
        private readonly HWND _desktopWindow;
        private readonly HDC _desktopDC;
        private readonly HDC _memoryDC;
        private readonly HBITMAP _bitmap;
        private readonly HGDIOBJ _selectedObject;
        private readonly nint _bitmapDataPtr;

        private readonly ushort _bitCount;
        private readonly int _pixelBytes;
        private readonly int _stride;
        private readonly int _desktopWidth;
        private readonly int _desktopHeight;


        private bool _disposedValue;

        public nint DataPointer => _bitmapDataPtr;
        public int PixelBytes => _pixelBytes;
        public int Stride => _stride;
        public int Width => _desktopWidth;
        public int Height => _desktopHeight;

        public unsafe GdiScreenCapture(bool noAlphaChannel)
        {
            _desktopWindow = PInvoke.GetDesktopWindow();
            _desktopDC = PInvoke.GetDC(_desktopWindow);

            _desktopWidth = PInvoke.GetDeviceCaps(_desktopDC, GET_DEVICE_CAPS_INDEX.DESKTOPHORZRES);
            _desktopHeight = PInvoke.GetDeviceCaps(_desktopDC, GET_DEVICE_CAPS_INDEX.DESKTOPVERTRES);

            _bitCount = noAlphaChannel ? (ushort)24 : (ushort)32;
            _pixelBytes = _bitCount / 8;
            _stride = (((_desktopWidth * _bitCount) + 31) & ~31) >> 3;

            BITMAPINFO bitmapInfo = new BITMAPINFO();
            bitmapInfo.bmiHeader = new BITMAPINFOHEADER();
            bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            bitmapInfo.bmiHeader.biWidth = _desktopWidth;
            bitmapInfo.bmiHeader.biHeight = -_desktopHeight;    // 这里把高度反过来, 要不然内容就反过来了, 只有它的值为复数, 内容才是从上往下的
            bitmapInfo.bmiHeader.biPlanes = 1;
            bitmapInfo.bmiHeader.biBitCount = _bitCount;
            bitmapInfo.bmiHeader.biSizeImage = (uint)(_desktopWidth * _desktopHeight);
            bitmapInfo.bmiHeader.biCompression = 0;  // RGB

            _memoryDC = PInvoke.CreateCompatibleDC(_desktopDC);

            void* bitmapDataPtr;
            _bitmap = PInvoke.CreateDIBSection(_memoryDC, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS, &bitmapDataPtr, HANDLE.Null, 0);
            _bitmapDataPtr = (nint)bitmapDataPtr;

            _selectedObject = PInvoke.SelectObject(_memoryDC, _bitmap);
        }

        public GdiScreenCapture() : this(false)
        {

        }

        public bool Capture()
        {
            PInvoke.BitBlt(_memoryDC, 0, 0, _desktopWidth, _desktopHeight, _desktopDC, 0, 0, ROP_CODE.SRCCOPY);
            return true;
        }

        public bool Capture(TimeSpan timeout)
        {
            return Capture();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null

                PInvoke.SelectObject(_memoryDC, _selectedObject);
                PInvoke.DeleteObject(_bitmap);
                PInvoke.DeleteDC(_memoryDC);
                PInvoke.ReleaseDC(_desktopWindow, _desktopDC);

                _disposedValue = true;
            }
        }

        // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~GdiScreenCapture()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
