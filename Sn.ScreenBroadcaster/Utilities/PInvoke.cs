using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinmdRoot = global::Windows.Win32;

namespace Windows.Win32
{
    internal partial class PInvoke
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern WinmdRoot.UI.WindowsAndMessaging.MESSAGEBOX_RESULT MessageBoxTimeout(
           IntPtr hWnd,
           PCWSTR lpText,
           PCWSTR lpCaption,
           MESSAGEBOX_STYLE uType,
           ushort wLanguageId,
           uint dwMilliseconds
        );

        [DllImport("user32.dll", SetLastError = true)]
        public static extern WinmdRoot.UI.WindowsAndMessaging.MESSAGEBOX_RESULT MessageBoxTimeout(
           IntPtr hWnd,
           string lpText,
           string lpCaption,
           MESSAGEBOX_STYLE uType,
           ushort wLanguageId,
           uint dwMilliseconds
        );
    }
}
