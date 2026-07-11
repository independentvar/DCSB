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
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;
        private const int TokenElevation = 20;

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_ELEVATION
        {
            public int TokenIsElevated;
        }

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
            out TOKEN_ELEVATION tokenInformation, int tokenInformationLength, out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        // True when another process owns the foreground, fills its monitor, and is
        // elevated. This does not depend on DCSB receiving input from that process.
        public static bool IsElevatedFullscreenAppForeground()
        {
            if (!TryGetForeignForegroundWindow(out IntPtr foreground))
                return false;

            IntPtr monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (!SpansMonitor(foreground, monitor, out _))
                return false;

            GetWindowThreadProcessId(foreground, out uint processId);
            return IsProcessElevated(processId);
        }

        // true when another process' foreground window spans the entire monitor that
        // hwnd is on - i.e. a fullscreen or borderless-fullscreen application (a game)
        // is covering us; a foreground window on a different monitor does not count
        public static bool IsCoveredByFullscreenApp(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!TryGetForeignForegroundWindow(out IntPtr foreground))
            {
                return false;
            }

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != MonitorFromWindow(foreground, MonitorDefaultToNearest))
            {
                return false;
            }

            return SpansMonitor(foreground, monitor, out _);
        }

        // true when another process' foreground window spans the entire monitor it is
        // on (a fullscreen or borderless-fullscreen game, on any monitor); outputs that
        // monitor's bounds in physical pixels
        public static bool TryGetFullscreenAppBounds(out int left, out int top, out int width, out int height)
        {
            left = top = width = height = 0;

            if (!TryGetForeignForegroundWindow(out IntPtr foreground))
            {
                return false;
            }

            IntPtr monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (!SpansMonitor(foreground, monitor, out MONITORINFO info))
            {
                return false;
            }

            left = info.Monitor.Left;
            top = info.Monitor.Top;
            width = info.Monitor.Right - info.Monitor.Left;
            height = info.Monitor.Bottom - info.Monitor.Top;
            return true;
        }

        private static bool TryGetForeignForegroundWindow(out IntPtr foreground)
        {
            foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == GetShellWindow() || foreground == GetDesktopWindow())
            {
                return false;
            }

            GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
            return foregroundProcessId != _currentProcessId;
        }

        private static bool SpansMonitor(IntPtr window, IntPtr monitor, out MONITORINFO info)
        {
            info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(window, out RECT rect))
            {
                return false;
            }

            return rect.Left <= info.Monitor.Left
                && rect.Top <= info.Monitor.Top
                && rect.Right >= info.Monitor.Right
                && rect.Bottom >= info.Monitor.Bottom;
        }

        private static bool IsProcessElevated(uint processId)
        {
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
                return false;

            try
            {
                if (!OpenProcessToken(process, TokenQuery, out IntPtr token))
                    return false;

                try
                {
                    int size = Marshal.SizeOf(typeof(TOKEN_ELEVATION));
                    return GetTokenInformation(token, TokenElevation, out TOKEN_ELEVATION elevation, size, out _)
                        && elevation.TokenIsElevated != 0;
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }
    }
}
