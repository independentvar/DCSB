using System;
using System.Windows;
using System.Windows.Interop;
using DCSB.Input;

namespace DCSB
{
    /// <summary>
    /// Interaction logic for BindKeysWindow.xaml
    /// </summary>
    public partial class BindKeysWindow : Window
    {
        public BindKeysWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            MouseInput.ExcludedWindowHandle = new WindowInteropHelper(this).Handle;
        }

        protected override void OnClosed(EventArgs e)
        {
            MouseInput.ExcludedWindowHandle = IntPtr.Zero;
            base.OnClosed(e);
        }
    }
}
