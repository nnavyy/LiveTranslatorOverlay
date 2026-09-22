using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LiveTranslatorOverlay.Core
{
    public static class CaptureExclusion
    {
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_SNAPSHOT = 0x2C;
        private const int VK_S = 0x53;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_SHIFT = 0x10;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private static IntPtr _hookId = IntPtr.Zero;
        private static readonly LowLevelKeyboardProc _hookProc = HookCallback;
        private static DispatcherTimer? _restoreTimer;

        public static bool IsExclusionEnabled { get; set; } = false;

        public static event Action? ScreenshotOccurred;
        public static event Action? ScreenshotFinished;

        public static void Initialize()
        {
            if (_hookId != IntPtr.Zero) return;
            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr moduleHandle = GetModuleHandle(curModule?.ModuleName);
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);
            }
            catch { }
        }

        public static void Shutdown()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _restoreTimer?.Stop();
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool isPrtScn = (vkCode == VK_SNAPSHOT);
                bool isWinShiftS = (vkCode == VK_S) &&
                    (((GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0) &&
                     (GetKeyState(VK_SHIFT) & 0x8000) != 0);

                if ((isPrtScn || isWinShiftS) && IsExclusionEnabled)
                {
                    HandleScreenshotKey();
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static void HandleScreenshotKey()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.InvokeAsync(() =>
            {
                // Temporarily disable affinity to prevent Snipping Tool from stalling
                ApplyAffinityDirect(false);
                ScreenshotOccurred?.Invoke();

                if (_restoreTimer == null)
                {
                    _restoreTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(1500)
                    };
                    _restoreTimer.Tick += (s, e) =>
                    {
                        _restoreTimer.Stop();
                        if (IsExclusionEnabled)
                        {
                            ApplyAffinityDirect(true);
                            ScreenshotFinished?.Invoke();
                        }
                    };
                }
                _restoreTimer.Stop();
                _restoreTimer.Start();
            });
        }

        public static bool SetExclusion(Window? window, bool hideFromCapture)
        {
            if (window == null) return false;
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return false;

                uint affinity = hideFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
                return SetWindowDisplayAffinity(hwnd, affinity);
            }
            catch
            {
                return false;
            }
        }

        public static bool ExcludeFromCapture(Window? window)
        {
            return SetExclusion(window, IsExclusionEnabled);
        }

        public static void ApplyToAllWindows(bool hideFromCapture)
        {
            IsExclusionEnabled = hideFromCapture;
            ApplyAffinityDirect(hideFromCapture);
        }

        private static void ApplyAffinityDirect(bool hideFromCapture)
        {
            try
            {
                if (Application.Current != null)
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        // ONLY apply to windows that are currently visible to prevent reviving closed/hidden windows
                        if (window != null && window.IsVisible)
                        {
                            SetExclusion(window, hideFromCapture);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
