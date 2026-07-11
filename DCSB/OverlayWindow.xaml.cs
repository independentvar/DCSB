using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DCSB.Models;
using DCSB.ViewModels;

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

        // Smallest box the user is allowed to shrink the overlay to while editing.
        private const double MinBoxWidth = 140;
        private const double MinBoxHeight = 36;
        // Smallest box the editor opens at, so the on-box controls (Reset/Done and
        // the hint) still fit even when a preset has very little content.
        private const double MinChromeWidth = 320;
        // Border + breathing room added around the measured sound content when the
        // editor first opens on an as-yet-unadjusted preset.
        private const double BoxChromePadding = 24;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);

        private readonly bool _editMode;
        // the overlay stays on its automatic (content-sized, top-centred) layout
        // until the user actually moves or resizes it, so merely opening the editor
        // to look does not freeze the box
        private bool _geometryChanged;
        private bool _resetRequested;

        public OverlayWindow() : this(false)
        {
        }

        public OverlayWindow(bool editMode)
        {
            InitializeComponent();
            _editMode = editMode;

            if (_editMode)
            {
                // Turn the click-through, non-interactive live overlay into an editable
                // window: swap in the simplified chrome and let it take mouse/keyboard.
                LiveRoot.Visibility = Visibility.Collapsed;
                EditRoot.Visibility = Visibility.Visible;
                IsHitTestVisible = true;
                Focusable = true;
                ShowActivated = true;
                MinWidth = MinBoxWidth;
                MinHeight = MinBoxHeight;

                Loaded += EditOverlayLoaded;
                KeyDown += EditOverlayKeyDown;
                EditRoot.MouseLeftButtonDown += EditOverlayDragMove;
                ResizeGrip.DragDelta += EditOverlayResize;
                DoneButton.Click += (sender, e) => Close();
                ResetButton.Click += (sender, e) => { _resetRequested = true; Close(); };
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (_editMode)
            {
                // editing needs input, so leave the window activatable and hit-testable
                return;
            }

            // click-through and non-activating, so the overlay never takes input
            // away from the game, and no taskbar/alt-tab presence
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        // Positions and sizes the overlay over the given monitor (bounds in physical
        // pixels) and shows it without activating; called every poll tick, so it also
        // pushes the window back to topmost - games re-assert their own z-order.
        public void ShowOver(int monitorLeft, int monitorTop, int monitorWidth, int monitorHeight)
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            helper.EnsureHandle();

            // WPF coordinates are DIPs; the app is system-DPI aware, so one
            // transform applies to every monitor
            Matrix transform = HwndSource.FromHwnd(helper.Handle).CompositionTarget.TransformFromDevice;
            Point monitorTopLeft = transform.Transform(new Point(monitorLeft, monitorTop));
            double monitorWidthDip = monitorWidth * transform.M11;
            double monitorHeightDip = monitorHeight * transform.M22;

            Preset preset = (DataContext as ViewModel)?.ConfigurationModel?.SelectedPreset;

            bool showAdministratorWarning = (DataContext as ViewModel)?.AdministratorOverlayWarningVisibility
                == Visibility.Visible;
            if (preset == null || !preset.OverlayCustomized || showAdministratorWarning)
            {
                // automatic layout: a content-sized pill centred at the top of the
                // monitor, exactly like the overlay looked before it was adjustable
                ApplyLiveLayout(customized: false);
                SizeToContent = SizeToContent.Height;
                Width = monitorWidthDip;
                Left = monitorTopLeft.X;
                Top = monitorTopLeft.Y;
            }
            else
            {
                // the box the user dragged/resized for this preset
                ApplyLiveLayout(customized: true);
                SizeToContent = SizeToContent.Manual;
                Width = preset.OverlayWidth;
                Height = preset.OverlayHeight;
                Left = monitorTopLeft.X + preset.OverlayPositionX * monitorWidthDip - preset.OverlayWidth / 2;
                Top = monitorTopLeft.Y + preset.OverlayPositionY * monitorHeightDip;
            }

            if (!IsVisible)
            {
                Show();
            }
            SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // Automatic: the pill hugs its content and sits centred against the top edge
        // (rounded bottom only). Customized: the pill fills the fixed box the user set.
        private void ApplyLiveLayout(bool customized)
        {
            if (customized)
            {
                LiveRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
                LiveRoot.VerticalAlignment = VerticalAlignment.Stretch;
                LiveRoot.CornerRadius = new CornerRadius(8);
            }
            else
            {
                LiveRoot.HorizontalAlignment = HorizontalAlignment.Center;
                LiveRoot.VerticalAlignment = VerticalAlignment.Top;
                LiveRoot.CornerRadius = new CornerRadius(0, 0, 8, 8);
            }
        }

        private void EditOverlayLoaded(object sender, RoutedEventArgs e)
        {
            Preset preset = (DataContext as ViewModel)?.ConfigurationModel?.SelectedPreset;
            if (preset == null)
            {
                return;
            }

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            if (preset.OverlayCustomized)
            {
                // reopen the box exactly where the user last left it
                Width = Math.Max(MinBoxWidth, preset.OverlayWidth);
                Height = Math.Max(MinBoxHeight, preset.OverlayHeight);
                Left = preset.OverlayPositionX * screenWidth - Width / 2;
                Top = preset.OverlayPositionY * screenHeight;
            }
            else
            {
                // start from the current automatic look: a box just big enough for the
                // sounds (with room for the edit chrome), centred at the top
                EditSounds.Measure(new Size(screenWidth, double.PositiveInfinity));
                Size content = EditSounds.DesiredSize;
                Width = Math.Max(MinChromeWidth, content.Width + BoxChromePadding);
                Height = Math.Max(MinBoxHeight, content.Height + BoxChromePadding);
                Left = screenWidth / 2 - Width / 2;
                Top = 0;
            }

            // keep the box fully on the primary screen so it is always grabbable
            Left = Clamp(Left, 0, Math.Max(0, screenWidth - Width));
            Top = Clamp(Top, 0, Math.Max(0, screenHeight - Height));

            Activate();
        }

        private void EditOverlayDragMove(object sender, MouseButtonEventArgs e)
        {
            // the resize grip handles its own mouse events, so a drag here is a move
            double startLeft = Left;
            double startTop = Top;
            DragMove();
            if (Left != startLeft || Top != startTop)
            {
                _geometryChanged = true;
            }
        }

        private void EditOverlayResize(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            Width = Clamp(Width + e.HorizontalChange, MinBoxWidth, Math.Max(MinBoxWidth, screenWidth - Left));
            Height = Clamp(Height + e.VerticalChange, MinBoxHeight, Math.Max(MinBoxHeight, screenHeight - Top));
            _geometryChanged = true;
        }

        private void EditOverlayKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                Close();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Preset preset = _editMode ? (DataContext as ViewModel)?.ConfigurationModel?.SelectedPreset : null;
            if (preset != null)
            {
                if (_resetRequested)
                {
                    // back to the automatic content-sized bar centred at the top
                    preset.OverlayCustomized = false;
                }
                else if (_geometryChanged)
                {
                    // persist the box the user set for this preset: position
                    // monitor-relative (0..1), size in DIPs
                    double screenWidth = SystemParameters.PrimaryScreenWidth;
                    double screenHeight = SystemParameters.PrimaryScreenHeight;

                    preset.OverlayWidth = Width;
                    preset.OverlayHeight = Height;
                    preset.OverlayPositionX = Clamp((Left + Width / 2) / screenWidth, 0, 1);
                    preset.OverlayPositionY = Clamp(Top / screenHeight, 0, 1);
                    preset.OverlayCustomized = true;
                }
                // opened but neither reset nor changed: leave the preset untouched so
                // it keeps its automatic layout
            }

            base.OnClosing(e);
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
