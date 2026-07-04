using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DCSB.Interactivity
{
    public static class Commands
    {
        public static readonly DependencyProperty DoubleClickProperty = DependencyProperty.RegisterAttached("DoubleClickCommand", typeof(ICommand), typeof(Commands),
                      new PropertyMetadata(new PropertyChangedCallback(AttachOrRemoveDoubleClickEvent)));

        public static ICommand GetDoubleClickCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(DoubleClickProperty);
        }

        public static void SetDoubleClickCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(DoubleClickProperty, value);
        }

        public static readonly DependencyProperty DoubleClickParameterProperty = DependencyProperty.RegisterAttached("DoubleClickCommandParameter", typeof(object), typeof(Commands),
              new PropertyMetadata(null));

        public static ICommand GetDoubleClickCommandParameter(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(DoubleClickParameterProperty);
        }

        public static void SetDoubleClickCommandParameter(DependencyObject obj, ICommand value)
        {
            obj.SetValue(DoubleClickParameterProperty, value);
        }

        public static void AttachOrRemoveDoubleClickEvent(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (obj is Control control)
            {
                ICommand cmd = (ICommand)args.NewValue;

                if (args.OldValue == null && args.NewValue != null)
                {
                    control.MouseDoubleClick += ExecuteDoubleClick;
                }
                else if (args.OldValue != null && args.NewValue == null)
                {
                    control.MouseDoubleClick -= ExecuteDoubleClick;
                }
            }
        }

        public static readonly DependencyProperty FileDropProperty = DependencyProperty.RegisterAttached("FileDropCommand", typeof(ICommand), typeof(Commands),
                      new PropertyMetadata(new PropertyChangedCallback(AttachOrRemoveFileDropEvent)));

        public static ICommand GetFileDropCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(FileDropProperty);
        }

        public static void SetFileDropCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(FileDropProperty, value);
        }

        public static void AttachOrRemoveFileDropEvent(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (obj is UIElement element)
            {
                if (args.OldValue == null && args.NewValue != null)
                {
                    element.AllowDrop = true;
                    element.DragOver += FileDragOver;
                    element.Drop += ExecuteFileDrop;
                }
                else if (args.OldValue != null && args.NewValue == null)
                {
                    element.AllowDrop = false;
                    element.DragOver -= FileDragOver;
                    element.Drop -= ExecuteFileDrop;
                }
            }
        }

        private static void FileDragOver(object sender, DragEventArgs args)
        {
            args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        }

        private static void ExecuteFileDrop(object sender, DragEventArgs args)
        {
            if (args.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])args.Data.GetData(DataFormats.FileDrop);
                DependencyObject obj = (DependencyObject)sender;
                ICommand cmd = (ICommand)obj.GetValue(FileDropProperty);
                if (cmd != null && cmd.CanExecute(files))
                {
                    cmd.Execute(files);
                    args.Handled = true;
                }
            }
        }

        private static void ExecuteDoubleClick(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton == MouseButton.Left)
            {
                DependencyObject obj = sender as DependencyObject;
                ICommand cmd = (ICommand)obj.GetValue(DoubleClickProperty);
                object parameter = obj.GetValue(DoubleClickParameterProperty);
                if (cmd != null)
                {
                    if (cmd.CanExecute(parameter))
                    {
                        cmd.Execute(parameter);
                    }
                }
            }
        }
    }
}
