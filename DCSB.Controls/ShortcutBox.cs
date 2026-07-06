using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DCSB.Input;

namespace DCSB.Controls
{
    /// <summary>
    /// A click-to-record field for key bindings. Clicking the box runs <see cref="Command"/>
    /// to start capturing; while listening, clicking a mouse button inside the box binds
    /// that button, and the hover ✕ (when idle) runs <see cref="ClearCommand"/>. When
    /// <see cref="IsActive"/> is set the box shows a "listening" state and publishes its
    /// on-screen bounds so the global hook captures mouse buttons only inside it.
    /// </summary>
    public class ShortcutBox : Control
    {
        // The box that is currently capturing, so the one that published the binding
        // region is the one that clears it (guards against out-of-order IsActive
        // callbacks when the user clicks straight from one field to another).
        private static ShortcutBox _activeBox;

        // Whether the box was already listening when the current left-click began. If so
        // the click was a mouse-button binding (captured by the hook), not a start click,
        // so the button-up must not toggle capture off.
        private bool _wasActiveOnPress;

        static ShortcutBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ShortcutBox), new FrameworkPropertyMetadata(typeof(ShortcutBox)));
        }

        public ShortcutBox()
        {
            // If the field is torn down mid-capture (tab switch, window close), stop
            // listening so a stray keypress isn't swallowed into this binding.
            Unloaded += (s, e) =>
            {
                if (IsActive && Command != null && Command.CanExecute(CommandParameter))
                {
                    Command.Execute(CommandParameter);
                }
                ReleaseBindingRegion();
            };
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(ShortcutBox),
            new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder), typeof(string), typeof(ShortcutBox),
            new PropertyMetadata("Click to set shortcut"));

        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        // True while this box is the one currently capturing keys.
        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(ShortcutBox),
            new PropertyMetadata(false, OnIsActiveChanged));

        public bool IsActive
        {
            get { return (bool)GetValue(IsActiveProperty); }
            set { SetValue(IsActiveProperty, value); }
        }

        private static readonly DependencyPropertyKey HasValuePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasValue), typeof(bool), typeof(ShortcutBox), new PropertyMetadata(false));

        public static readonly DependencyProperty HasValueProperty = HasValuePropertyKey.DependencyProperty;

        // Drives visibility of the placeholder and the clear button in the template.
        public bool HasValue
        {
            get { return (bool)GetValue(HasValueProperty); }
            private set { SetValue(HasValuePropertyKey, value); }
        }

        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
            nameof(Command), typeof(ICommand), typeof(ShortcutBox), new PropertyMetadata(null));

        public ICommand Command
        {
            get { return (ICommand)GetValue(CommandProperty); }
            set { SetValue(CommandProperty, value); }
        }

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
            nameof(CommandParameter), typeof(object), typeof(ShortcutBox), new PropertyMetadata(null));

        public object CommandParameter
        {
            get { return GetValue(CommandParameterProperty); }
            set { SetValue(CommandParameterProperty, value); }
        }

        public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(
            nameof(ClearCommand), typeof(ICommand), typeof(ShortcutBox), new PropertyMetadata(null));

        public ICommand ClearCommand
        {
            get { return (ICommand)GetValue(ClearCommandProperty); }
            set { SetValue(ClearCommandProperty, value); }
        }

        public static readonly DependencyProperty ClearCommandParameterProperty = DependencyProperty.Register(
            nameof(ClearCommandParameter), typeof(object), typeof(ShortcutBox), new PropertyMetadata(null));

        public object ClearCommandParameter
        {
            get { return GetValue(ClearCommandParameterProperty); }
            set { SetValue(ClearCommandParameterProperty, value); }
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ShortcutBox)d).HasValue = !string.IsNullOrEmpty((string)e.NewValue);
        }

        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ShortcutBox box = (ShortcutBox)d;
            if ((bool)e.NewValue)
            {
                // Capture mouse buttons only inside this box: clicking it binds the button,
                // while clicks elsewhere are ignored (so nearby clicks aren't captured).
                _activeBox = box;
                box.PublishBindingRegion();
            }
            else
            {
                box.ReleaseBindingRegion();
            }
        }

        private void PublishBindingRegion()
        {
            if (PresentationSource.FromVisual(this) == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                MouseInput.BindingRegion = System.Drawing.Rectangle.Empty;
                return;
            }
            // PointToScreen yields physical pixels, matching the low-level hook's point.
            Point topLeft = PointToScreen(new Point(0, 0));
            Point bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            MouseInput.BindingRegion = new System.Drawing.Rectangle(
                (int)topLeft.X, (int)topLeft.Y,
                (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
        }

        private void ReleaseBindingRegion()
        {
            if (_activeBox == this)
            {
                _activeBox = null;
                MouseInput.BindingRegion = System.Drawing.Rectangle.Empty;
            }
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            _wasActiveOnPress = IsActive;
        }

        // Fire on button-up (like the single-click dialog pickers): the global mouse hook
        // processes the release first, so the click that starts binding isn't itself
        // captured as a mouse-button binding. If the clear (✕) button handled the click,
        // the event is already marked handled and this override isn't invoked.
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (e.Handled) return;
            // The box was already listening when this click began: the hook has captured
            // it as a "Left Click" binding, so don't also toggle capture off here.
            if (_wasActiveOnPress)
            {
                e.Handled = true;
                return;
            }
            if (Command != null && Command.CanExecute(CommandParameter))
            {
                Command.Execute(CommandParameter);
                e.Handled = true;
            }
        }
    }
}
