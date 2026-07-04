using DCSB.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DCSB.Input
{
    public class MouseInput : IDisposable
    {
        public delegate void MouseButtonCallback(VKey button);

        public event MouseButtonCallback ButtonDown;
        public event MouseButtonCallback ButtonUp;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;

        private const uint GA_ROOT = 2;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Clicks landing on this window are ignored, so the key binding dialog's own buttons
        // can be clicked without triggering or committing a mouse button binding.
        public static IntPtr ExcludedWindowHandle { get; set; }

        // Keep a reference to the hook procedure so the garbage collector does not release it.
        private readonly LowLevelMouseProc _hookProcedure;
        private readonly IntPtr _hookHandle;

        // Buttons whose down event was swallowed because it landed on the excluded window;
        // their matching up event has to be swallowed as well.
        private readonly HashSet<VKey> _suppressedButtons = new HashSet<VKey>();

        private bool _disposed;

        public MouseInput()
        {
            _hookProcedure = HookCallback;
            _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProcedure, GetModuleHandle(null), 0);
            if (_hookHandle == IntPtr.Zero)
            {
                Debug.Print("Installing low level mouse hook failed. Error: {0}", Marshal.GetLastWin32Error());
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int message = wParam.ToInt32();
                VKey button = TranslateButton(message, data.mouseData);
                if (button != VKey.NUL)
                {
                    if (message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN || message == WM_MBUTTONDOWN || message == WM_XBUTTONDOWN)
                    {
                        if (IsOverExcludedWindow(data.pt))
                        {
                            _suppressedButtons.Add(button);
                        }
                        else
                        {
                            _suppressedButtons.Remove(button);
                            ButtonDown?.Invoke(button);
                        }
                    }
                    else
                    {
                        if (!_suppressedButtons.Remove(button))
                        {
                            ButtonUp?.Invoke(button);
                        }
                    }
                }
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private VKey TranslateButton(int message, uint mouseData)
        {
            switch (message)
            {
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                    return VKey.LBUTTON;
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                    return VKey.RBUTTON;
                case WM_MBUTTONDOWN:
                case WM_MBUTTONUP:
                    return VKey.MBUTTON;
                case WM_XBUTTONDOWN:
                case WM_XBUTTONUP:
                    return (mouseData >> 16) == 1 ? VKey.XBUTTON1 : VKey.XBUTTON2;
                default:
                    return VKey.NUL;
            }
        }

        private bool IsOverExcludedWindow(POINT point)
        {
            IntPtr excluded = ExcludedWindowHandle;
            if (excluded == IntPtr.Zero) return false;
            IntPtr window = WindowFromPoint(point);
            return window != IntPtr.Zero && (window == excluded || GetAncestor(window, GA_ROOT) == excluded);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
            }
            _disposed = true;
        }

        ~MouseInput()
        {
            Dispose(false);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    }
}
