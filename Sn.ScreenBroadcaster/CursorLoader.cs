using Windows.Win32.Foundation;
using Windows.Win32;
using SkiaSharp;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.Graphics.Gdi;
using System.IO;

namespace Sn.ScreenBroadcaster
{
    public class CursorLoader : IDisposable
    {
        public static unsafe bool GetCursurMaskValue(int maskWidth, int maskHeight, byte* maskData, int x, int y)
        {
            var bitPosition = maskWidth * y + x;
            var bytePosition = bitPosition / 8;
            var bitShift = bitPosition % 8;

            return (maskData[bytePosition] & (1 << (7 - bitShift))) != 0;
        }

        public record struct CursorData(int HotspotX, int HotspotY, int Width, int Height, SKBitmap? DirectBitmap, SKBitmap? InvertBitmap);

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

        private readonly Dictionary<nint, CursorData> _cache = new();

        private unsafe CursorData? LoadCursor(nint hCursor)
        {
            var iconInfo = new ICONINFO();
            HICON hIcon = *(HICON*)&hCursor;
            PInvoke.GetIconInfo(hIcon, &iconInfo);

            HBITMAP hMaskBitmap = iconInfo.hbmMask;
            HBITMAP hColorBitmap = iconInfo.hbmColor;
            BITMAP maskBitmapInfo;
            BITMAP colorBitmapInfo;

            PInvoke.GetObject(hMaskBitmap, sizeof(BITMAP), &maskBitmapInfo);
            PInvoke.GetObject(hColorBitmap, sizeof(BITMAP), &colorBitmapInfo);

            var hDC = PInvoke.CreateCompatibleDC(default);
            var result = default(CursorData?);

            if (colorBitmapInfo.bmBitsPixel == 32)
            {
                var cursorWidth = colorBitmapInfo.bmWidth;
                var cursorHeight = colorBitmapInfo.bmHeight;

                var bitmapInfo = default(BITMAPINFO);
                bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);

                // fill bitmap pixels data
                SKBitmap colorBitmap = new SKBitmap(cursorWidth, cursorHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                PInvoke.GetDIBits(hDC, hColorBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);

                bitmapInfo.bmiHeader.biHeight = -bitmapInfo.bmiHeader.biHeight;
                PInvoke.GetDIBits(hDC, hColorBitmap, 0, (uint)cursorHeight, (void*)colorBitmap.GetPixels(), &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);

                bitmapInfo = default(BITMAPINFO);
                bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);

                // mask
                var maskBuffer = stackalloc byte[cursorWidth * cursorHeight / 8];
                PInvoke.GetDIBits(hDC, hMaskBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);

                bitmapInfo.bmiHeader.biHeight = -bitmapInfo.bmiHeader.biHeight;
                PInvoke.GetDIBits(hDC, hMaskBitmap, 0, (uint)cursorHeight, maskBuffer, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);

                for (int y = 0; y < colorBitmap.Height; y++)
                {
                    for (int x = 0; x < colorBitmap.Width; x++)
                    {
                        var color = colorBitmap.GetPixel(x, y);
                        var maskValue = GetCursurMaskValue(maskBitmapInfo.bmWidth, maskBitmapInfo.bmHeight, maskBuffer, x, y);
                        if (maskValue)
                        {
                            color = color.WithAlpha(0);
                        }

                        colorBitmap.SetPixel(x, y, color);
                    }
                }

                result = new CursorData((int)iconInfo.xHotspot, (int)iconInfo.yHotspot, cursorWidth, cursorHeight, colorBitmap, null);
            }
            else if (colorBitmapInfo.bmBitsPixel == 1)
            {
                var cursorWidth = colorBitmapInfo.bmWidth;
                var cursorHeight = colorBitmapInfo.bmHeight / 2;

                var colorBuffer = stackalloc byte[colorBitmapInfo.bmWidth * colorBitmapInfo.bmHeight / 8];
                var maskBuffer = stackalloc byte[maskBitmapInfo.bmWidth * maskBitmapInfo.bmHeight / 8];

                var andMaskPtr = maskBuffer;
                var orMaskPtr = maskBuffer + (maskBitmapInfo.bmWidth * maskBitmapInfo.bmHeight / 8 / 2);

                var bitmapInfo = default(BITMAPINFO);
                bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);

                // fill color
                PInvoke.GetDIBits(hDC, hColorBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);

                bitmapInfo.bmiHeader.biHeight = -bitmapInfo.bmiHeader.biHeight;
                PInvoke.GetDIBits(hDC, hColorBitmap, 0, (uint)colorBitmapInfo.bmHeight, colorBuffer, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);

                // fill mask
                PInvoke.GetDIBits(hDC, hMaskBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);

                bitmapInfo.bmiHeader.biHeight = -bitmapInfo.bmiHeader.biHeight;
                PInvoke.GetDIBits(hDC, hMaskBitmap, 0, (uint)colorBitmapInfo.bmHeight, maskBuffer, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);

                SKBitmap colorBitmap = new SKBitmap(colorBitmapInfo.bmWidthBytes, colorBitmapInfo.bmHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);

                for (int y = 0; y < colorBitmap.Height; y++)
                {
                    for (int x = 0; x < colorBitmap.Width; x++)
                    {
                        var color = colorBitmap.GetPixel(x, y);
                        var maskValue = GetCursurMaskValue(colorBitmapInfo.bmWidth, colorBitmapInfo.bmHeight, colorBuffer, x, y);

                        if (maskValue)
                        {
                            color = color.WithAlpha(0);
                        }

                        colorBitmap.SetPixel(x, y, color);
                    }
                }

                result = new CursorData((int)iconInfo.xHotspot, (int)iconInfo.yHotspot, cursorWidth, cursorHeight, colorBitmap, null);
            }
            else
            {
                var cursorWidth = maskBitmapInfo.bmWidth;
                var cursorHeight = maskBitmapInfo.bmHeight / 2;

                var maskBuffer = stackalloc byte[maskBitmapInfo.bmWidth * maskBitmapInfo.bmHeight / 8];

                var andMaskPtr = maskBuffer;
                var orMaskPtr = maskBuffer + (maskBitmapInfo.bmWidth * maskBitmapInfo.bmHeight / 8 / 2);

                var bitmapInfo = default(BITMAPINFO);
                bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);

                // fill mask
                PInvoke.GetDIBits(hDC, hMaskBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);

                bitmapInfo.bmiHeader.biHeight = -bitmapInfo.bmiHeader.biHeight;
                PInvoke.GetDIBits(hDC, hMaskBitmap, 0, (uint)maskBitmapInfo.bmHeight, maskBuffer, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);

                SKBitmap directBitmap = new SKBitmap(cursorWidth, cursorHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                SKBitmap invertBitmap = new SKBitmap(cursorWidth, cursorHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);

                for (int y = 0; y < directBitmap.Height; y++)
                {
                    for (int x = 0; x < directBitmap.Width; x++)
                    {
                        var color = directBitmap.GetPixel(x, y);
                        var andMaskValue = GetCursurMaskValue(cursorWidth, cursorHeight, andMaskPtr, x, y);
                        var xorMaskValue = GetCursurMaskValue(cursorWidth, cursorHeight, orMaskPtr, x, y);

                        if (!andMaskValue)
                        {
                            if (xorMaskValue)
                            {
                                directBitmap.SetPixel(x, y, new SKColor(255, 255, 255, 255));
                            }
                            else
                            {
                                directBitmap.SetPixel(x, y, new SKColor(0, 0, 0, 255));
                            }
                        }
                        else
                        {
                            if (xorMaskValue)
                            {
                                invertBitmap.SetPixel(x, y, new SKColor(255, 255, 255, 255));
                            }
                        }
                    }
                }

                result = new CursorData((int)iconInfo.xHotspot, (int)iconInfo.yHotspot, cursorWidth, cursorHeight, directBitmap, invertBitmap);
            }

            PInvoke.DeleteDC(hDC);
            PInvoke.DeleteObject(hMaskBitmap);
            PInvoke.DeleteObject(hColorBitmap);

            return result;

            //int ret = PInvoke.GetDIBits(hDC, hColorBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);
            //if (ret != 1)
            //{
            //    return null;
            //}

            //var width = bitmapInfo.bmiHeader.biWidth;
            //var height = bitmapInfo.bmiHeader.biHeight;
            //var maskBuffer = stackalloc byte[width * height];

            //SKBitmap skColorBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            //// Fill Color Bitmap
            //bitmapInfo.bmiHeader.biHeight = -bitmapInfo.bmiHeader.biHeight;
            //PInvoke.GetDIBits(hDC, hColorBitmap, 0, (uint)height, (void*)skColorBitmap.GetPixels(), &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS);

            //// Mask
            //bitmapInfo = default;
            //bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            //PInvoke.GetDIBits(hDC, hMaskBitmap, 0, 0, null, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);
            //PInvoke.GetDIBits(hDC, hMaskBitmap, 0, (uint)height, maskBuffer, &bitmapInfo, DIB_USAGE.DIB_PAL_COLORS);
        }

        public unsafe void Initialize()
        {
            int index = 0;
            foreach (var cursorName in _cursors)
            {
                var hCursor = PInvoke.LoadCursor(HINSTANCE.Null, cursorName);

                if (hCursor == IntPtr.Zero)
                {
                    continue;
                }

                if (LoadCursor(hCursor) is { } cursorData)
                {
                    _cache[hCursor] = cursorData;
                    using var fs = File.Create($"Origin_Cursor{index++}.png");
                    cursorData.DirectBitmap?.Encode(fs, SKEncodedImageFormat.Png, 0);
                }
            }
        }

        public unsafe CursorData? GetCursor(nint hCursor)
        {
            if (_cache.TryGetValue(hCursor, out var cachedCursor))
            {
                return cachedCursor;
            }

            if (LoadCursor(hCursor) is { } loadedCursor)
            {
                _cache[hCursor] = loadedCursor;
                return loadedCursor;
            }

            return null;
        }

        public void Dispose()
        {
            foreach (var bitmap in _cache.Values)
            {
                bitmap.DirectBitmap?.Dispose();
                bitmap.InvertBitmap?.Dispose();
            }
            _cache.Clear();
        }
    }
}