using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DCSB.Controls
{
    /// <summary>
    /// A click-to-record field for key bindings. Clicking anywhere on the box runs
    /// <see cref="Command"/> (which starts capturing keys); the hover ✕ runs
    /// <see cref="ClearCommand"/>. When <see cref="IsActive"/> is set the box shows a
    /// "listening" state. Purely presentational — the actual key capture lives in the
    /// existing global-hook path.
    /// </summary>
    public class ShortcutBox : Control
    {
        static ShortcutBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ShortcutBox), new FrameworkPropertyMetadata(typeof(ShortcutBox)));
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
            new PropertyMetadata(false));

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

        // Fire on button-up (like the single-click dialog pickers): the global mouse hook
        // processes the release first, so the click that starts binding isn't itself
        // captured as a mouse-button binding. If the clear (✕) button handled the click,
        // the event is already marked handled and this override isn't invoked.
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (e.Handled) return;
            if (Command != null && Command.CanExecute(CommandParameter))
            {
                Command.Execute(CommandParameter);
                e.Handled = true;
            }
        }
    }
}
