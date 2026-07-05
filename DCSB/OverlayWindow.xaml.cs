using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DCSB
{
    /// <summary>
    /// Interaction logic for OverlayWindow.xaml
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);

        public OverlayWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // click-through and non-activating, so the overlay never takes input
            // away from the game, and no taskbar/alt-tab presence
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        // Stretches the overlay across the top of the given monitor (bounds in
        // physical pixels) and shows it without activating; the bar itself centers
        // within the window. Called every poll tick, so it also pushes the window
        // back to topmost - games re-assert their own z-order.
        public void ShowOver(int monitorLeft, int monitorTop, int monitorWidth)
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            helper.EnsureHandle();

            // WPF coordinates are DIPs; the app is system-DPI aware, so one
            // transform applies to every monitor
            Matrix transform = HwndSource.FromHwnd(helper.Handle).CompositionTarget.TransformFromDevice;
            Point topLeft = transform.Transform(new Point(monitorLeft, monitorTop));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = monitorWidth * transform.M11;

            if (!IsVisible)
            {
                Show();
            }
            SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
