using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DCSB
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // global hotkeys use RIDEV_INPUTSINK, so two running instances would both
        // react to every shortcut - allow only one instance per session
        private const string MutexName = "DCSB_SingleInstance_2E8A0F3C";
        private const string ElevatedRestartArgument = "--restart-elevated";

        internal static readonly int ShowExistingInstanceMessage =
            RegisterWindowMessage("DCSB_ShowExistingInstance");

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        private Mutex _singleInstanceMutex;
        private bool _ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out _ownsMutex);
            if (!_ownsMutex && Array.IndexOf(e.Args, ElevatedRestartArgument) >= 0)
            {
                try
                {
                    // The non-elevated instance launches us before shutting down, so
                    // allow it time to dispose audio/input resources and release the
                    // per-session mutex. Normal second launches still exit immediately.
                    _ownsMutex = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(15));
                }
                catch (AbandonedMutexException)
                {
                    _ownsMutex = true;
                }
            }
            if (!_ownsMutex)
            {
                // another instance is already running - ask it to show its window
                // (it may be hidden in the tray) and exit
                PostMessage(HWND_BROADCAST, ShowExistingInstanceMessage, IntPtr.Zero, IntPtr.Zero);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsMutex)
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.Dispose();
            }

            base.OnExit(e);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
