using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using DCSB.ViewModels;

namespace DCSB
{
    public partial class MainWindow : Window
    {
        NotifyIcon notifyIcon;
        OverlayManager overlayManager;

        public MainWindow()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            DataContext = new ViewModel();

            Icon icon;
            using (Stream stream = System.Windows.Application.GetResourceStream(new Uri("icon.ico", UriKind.Relative)).Stream)
            {
                icon = new Icon(stream);
            }

            ToolStripMenuItem open = new ToolStripMenuItem("Open", null, (sender, e) => Open());
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit", null, (sender, e) => Close());

            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.AddRange(new ToolStripItem[] { open, exit });

            notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "Deathcounter and Soundboard",
                Visible = true,
                ContextMenuStrip = contextMenu
            };

            notifyIcon.Click += (sender, args) => Open();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.ExceptionObject.ToString(), "Unhandled exception");
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (DataContext is ViewModel viewModel)
            {
                if (viewModel.ConfigurationModel.MinimizeToTray && WindowState == WindowState.Minimized)
                    Hide();

                viewModel.RefreshSeekbarRendering();
            }

            base.OnStateChanged(e);
        }

        protected override void OnActivated(EventArgs e)
        {
            if (DataContext is ViewModel viewModel)
                viewModel.RefreshSeekbarRendering();

            base.OnActivated(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (notifyIcon != null)
                notifyIcon.Dispose();

            if (overlayManager != null)
                overlayManager.Dispose();

            if (DataContext is ViewModel viewModel)
                viewModel.Dispose();

            base.OnClosed(e);

            // Ensure the app actually terminates: tear down the WPF dispatcher even if the
            // close came from a nested message loop (e.g. the tray icon's context menu).
            System.Windows.Application.Current?.Shutdown();
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModel viewModel)
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                viewModel.WindowHandle = handle;
                HwndSource.FromHwnd(handle)?.AddHook(WndProc);

                overlayManager = new OverlayManager(viewModel);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == App.ShowExistingInstanceMessage)
            {
                Open();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void Open()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }
}
