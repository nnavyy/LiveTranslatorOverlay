using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LiveTranslatorOverlay.Core
{
    public static class CaptureExclusion
    {
        // Konstanta untuk mengeksklusi window dari capture
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        /// <summary>
        /// Menerapkan efek exclude from capture pada WPF Window,
        /// sehingga window tidak muncul saat di-screen share menggunakan OBS, Discord, Zoom, dll.
        /// </summary>
        /// <param name="window">WPF Window yang ingin dikecualikan</param>
        /// <returns>True jika berhasil, False jika gagal</returns>
        public static bool ExcludeFromCapture(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                // Jika handle belum dibuat (misalnya dipanggil di konstruktor sebelum UI diinisialisasi)
                // Sebaiknya panggil method ini di event SourceInitialized
                return false;
            }

            uint result = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            return result != 0;
        }
    }
}
