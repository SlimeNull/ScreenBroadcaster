using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace LibScreenCapture
{
    public record struct ScreenInfo(bool IsPrimary, int X, int Y, int Width, int Height, int DpiX, int DpiY)
    {
        public static int GetScreenCount()
        {
            return PInvoke.GetSystemMetrics(Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CMONITORS);
        }

        public static unsafe ScreenInfo[] GetScreens()
        {
            var screenCount = GetScreenCount();
            var result = new ScreenInfo[screenCount];

            var desktopWindow = PInvoke.GetDesktopWindow();
            var hdc = PInvoke.GetDC(desktopWindow);

            var screenIndex = 0;

            PInvoke.EnumDisplayMonitors(hdc, (RECT*)null, (monitor, hdc, rectPointer, param) =>
            {
                var rect = *rectPointer;
                var monitorInfo = default(MONITORINFO);
                monitorInfo.cbSize = (uint)sizeof(MONITORINFO);

                PInvoke.GetMonitorInfo(monitor, &monitorInfo);
                PInvoke.GetDpiForMonitor(monitor, Windows.Win32.UI.HiDpi.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY);

                bool monitorIsPrimary = (monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;

                result[screenIndex] = new ScreenInfo(monitorIsPrimary, rect.X, rect.Y, rect.Width, rect.Height, (int)dpiX, (int)dpiY);
                screenIndex++;

                return true;
            }, 0);

            PInvoke.ReleaseDC(desktopWindow, hdc);

            return result;
        }

        public static int GetTotalScreenWidth(IEnumerable<ScreenInfo> screens)
        {
            int left = 0;
            int right = 0;

            foreach (var screen in screens)
            {
                left = Math.Min(left, screen.X);
                right = Math.Max(right, screen.X + screen.Width);
            }

            return right - left;
        }
        public static int GetTotalScreenHeight(IEnumerable<ScreenInfo> screens)
        {
            int top = 0;
            int bottom = 0;

            foreach (var screen in screens)
            {
                top = Math.Min(top, screen.Y);
                bottom = Math.Max(bottom, screen.Y + screen.Height);
            }

            return bottom - top;
        }

        public static int GetPrimaryScreenWidth()
        {
            var hdc = PInvoke.GetDC(HWND.Null);
            var result = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.HORZRES);

            PInvoke.ReleaseDC(HWND.Null, hdc);

            return result;
        }

        public static int GetPrimaryScreenHeight()
        {
            var hdc = PInvoke.GetDC(HWND.Null);
            var result = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.VERTRES);

            PInvoke.ReleaseDC(HWND.Null, hdc);

            return result;
        }

        public override string ToString()
        {
            if (IsPrimary)
                return $"{Width}x{Height} @ {X},{Y} (Primary)";
            else
                return $"{Width}x{Height} @ {X},{Y}";
        }
    }
}
