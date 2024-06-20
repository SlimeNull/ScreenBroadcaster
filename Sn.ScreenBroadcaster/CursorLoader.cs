using Windows.Win32.Foundation;
using Windows.Win32;
using SkiaSharp;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.Graphics.Gdi;

namespace Sn.ScreenBroadcaster
{
    public class CursorLoader : IDisposable
    {
        private static readonly PCWSTR[] _cursors =
        [
            PInvoke.IDC_ARROW,
            PInvoke.IDC_IBEAM,
            PInvoke.IDC_WAIT,
            PInvoke.IDC_CROSS,
            PInvoke.IDC_UPARROW,
            PInvoke.IDC_SIZENWSE,
            PInvoke.IDC_SIZENESW,
            PInvoke.IDC_SIZEWE,
            PInvoke.IDC_SIZENS,
            PInvoke.IDC_SIZEALL,
            PInvoke.IDC_NO,
            PInvoke.IDC_HAND,
            PInvoke.IDC_APPSTARTING,
            PInvoke.IDC_HELP,
            PInvoke.IDC_PIN,
            PInvoke.IDC_PERSON
        ];

        private readonly Dictionary<nint, (SKBitmap Bitmap, uint XHotspot, uint YHotspot)> _cache = new();

        private unsafe SKBitmap? LoadBitmap(nint hCursor, out uint xHotspot, out uint yHotspot)
        {
            var iconInfo = new ICONINFO();
            HICON hIcon = *(HICON*)&hCursor;
            PInvoke.GetIconInfo(hIcon, &iconInfo);
            HBITMAP hBitmap = iconInfo.hbmColor;

            xHotspot = iconInfo.xHotspot;
            yHotspot = iconInfo.yHotspot;

            BITMAPINFO bitmapInfo = new();
            bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);

            var dc = PInvoke.CreateCompatibleDC(default);
            int ret = PInvoke.GetDIBits(dc, hBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);
            if (ret != 1)
            {
                return null;
            }

            var width = bitmapInfo.bmiHeader.biWidth;
            var height = bitmapInfo.bmiHeader.biHeight;

            SKBitmap skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var buffer = skBitmap.GetPixels();

            bitmapInfo.bmiHeader.biHeight = -bitmapInfo.bmiHeader.biHeight;
            PInvoke.GetDIBits(dc, hBitmap, 0, (uint)height, (void*)buffer, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);

            PInvoke.DeleteDC(dc);
            PInvoke.DeleteObject(iconInfo.hbmMask);
            PInvoke.DeleteObject(iconInfo.hbmColor);

            return skBitmap;
        }

        public unsafe void Initialize()
        {
            foreach (var cursor in _cursors)
            {
                var hCursor = PInvoke.LoadCursor(HINSTANCE.Null, cursor);

                if (hCursor == IntPtr.Zero)
                {
                    continue;
                }

                if (LoadBitmap(hCursor, out var xHotspot, out var yHotspot) is { } bitmap)
                {
                    _cache[hCursor] = (bitmap, xHotspot, yHotspot);
                }
            }
        }

        public unsafe SKBitmap? GetCursor(nint hCursor, out uint xHotspot, out uint yHotspot)
        {
            if (_cache.TryGetValue(hCursor, out var cachedCursor))
            {
                xHotspot = cachedCursor.XHotspot;
                yHotspot = cachedCursor.YHotspot;
                return cachedCursor.Bitmap;
            }

            if (LoadBitmap(hCursor, out xHotspot, out yHotspot) is { } loadedBitmap)
            {
                _cache[hCursor] = (loadedBitmap, xHotspot, yHotspot);
                return loadedBitmap;
            }

            return null;
        }

        public void Dispose()
        {
            foreach (var bitmap in _cache.Values)
            {
                bitmap.Bitmap.Dispose();
            }
            _cache.Clear();
        }
    }
}