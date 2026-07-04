using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DCSB.Utils
{
    public static class FullscreenDetector
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT WorkArea;
            public uint Flags;
        }

        private const uint MonitorDefaultToNearest = 2;

        private static readonly uint _currentProcessId = (uint)Process.GetCurrentProcess().Id;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        // true when another process' foreground window spans the entire monitor that
        // hwnd is on - i.e. a fullscreen or borderless-fullscreen application (a game)
        // is covering us; a foreground window on a different monitor does not count
        public static bool IsCoveredByFullscreenApp(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == GetShellWindow() || foreground == GetDesktopWindow())
            {
                return false;
            }

            GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
            if (foregroundProcessId == _currentProcessId)
            {
                return false;
            }

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != MonitorFromWindow(foreground, MonitorDefaultToNearest))
            {
                return false;
            }

            MONITORINFO info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(foreground, out RECT rect))
            {
                return false;
            }

            return rect.Left <= info.Monitor.Left
                && rect.Top <= info.Monitor.Top
                && rect.Right >= info.Monitor.Right
                && rect.Bottom >= info.Monitor.Bottom;
        }
    }
}
