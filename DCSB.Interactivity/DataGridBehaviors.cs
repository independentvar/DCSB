using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace DCSB.Interactivity
{
    public static class DataGridBehaviors
    {
        public static readonly DependencyProperty RestoreFocusOnRemoveProperty = DependencyProperty.RegisterAttached("RestoreFocusOnRemove", typeof(bool), typeof(DataGridBehaviors),
                      new PropertyMetadata(false, new PropertyChangedCallback(AttachOrRemoveSelectionChangedEvent)));

        // Set between the SelectionChanged that clears selection because the selected row
        // was removed and the SelectionChanged that restores it to a neighbouring row.
        private static readonly DependencyProperty PendingRefocusProperty = DependencyProperty.RegisterAttached("PendingRefocus", typeof(bool), typeof(DataGridBehaviors),
                      new PropertyMetadata(false));

        public static bool GetRestoreFocusOnRemove(DependencyObject obj)
        {
            return (bool)obj.GetValue(RestoreFocusOnRemoveProperty);
        }

        public static void SetRestoreFocusOnRemove(DependencyObject obj, bool value)
        {
            obj.SetValue(RestoreFocusOnRemoveProperty, value);
        }

        // OneWayToSource-bindable mirror of DataGrid.SelectedItems; the grid's live
        // SelectedItems list is pushed into the binding source once the grid loads.
        public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.RegisterAttached("SelectedItems", typeof(IList), typeof(DataGridBehaviors),
                      new PropertyMetadata(null));

        public static IList GetSelectedItems(DependencyObject obj)
        {
            return (IList)obj.GetValue(SelectedItemsProperty);
        }

        public static void SetSelectedItems(DependencyObject obj, IList value)
        {
            obj.SetValue(SelectedItemsProperty, value);
        }

        public static readonly DependencyProperty SyncSelectedItemsProperty = DependencyProperty.RegisterAttached("SyncSelectedItems", typeof(bool), typeof(DataGridBehaviors),
                      new PropertyMetadata(false, new PropertyChangedCallback(AttachOrRemoveSyncSelectedItems)));

        public static bool GetSyncSelectedItems(DependencyObject obj)
        {
            return (bool)obj.GetValue(SyncSelectedItemsProperty);
        }

        public static void SetSyncSelectedItems(DependencyObject obj, bool value)
        {
            obj.SetValue(SyncSelectedItemsProperty, value);
        }

        public static void AttachOrRemoveSyncSelectedItems(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (obj is DataGrid dataGrid && (bool)args.NewValue)
            {
                if (dataGrid.IsLoaded)
                {
                    dataGrid.SetCurrentValue(SelectedItemsProperty, dataGrid.SelectedItems);
                }
                else
                {
                    dataGrid.Loaded += PushSelectedItems;
                }
            }
        }

        private static void PushSelectedItems(object sender, RoutedEventArgs args)
        {
            DataGrid dataGrid = (DataGrid)sender;
            dataGrid.Loaded -= PushSelectedItems;
            dataGrid.SetCurrentValue(SelectedItemsProperty, dataGrid.SelectedItems);
        }

        public static void AttachOrRemoveSelectionChangedEvent(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (obj is DataGrid dataGrid)
            {
                if ((bool)args.NewValue)
                {
                    dataGrid.SelectionChanged += HandleSelectionChanged;
                }
                else
                {
                    dataGrid.SelectionChanged -= HandleSelectionChanged;
                }
            }
        }

        private static void HandleSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            DataGrid dataGrid = (DataGrid)sender;
            if (dataGrid.SelectedItem == null)
            {
                bool removedFromItems = args.RemovedItems.Count > 0 && !dataGrid.Items.Contains(args.RemovedItems[0]);
                dataGrid.SetValue(PendingRefocusProperty, removedFromItems);
            }
            else if ((bool)dataGrid.GetValue(PendingRefocusProperty))
            {
                dataGrid.SetValue(PendingRefocusProperty, false);
                object item = dataGrid.SelectedItem;
                dataGrid.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (dataGrid.SelectedItem != item || dataGrid.Columns.Count == 0)
                    {
                        return;
                    }
                    dataGrid.ScrollIntoView(item);
                    dataGrid.UpdateLayout();
                    dataGrid.CurrentCell = new DataGridCellInfo(item, dataGrid.Columns[0]);
                    if (dataGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                    {
                        DataGridCellsPresenter presenter = FindVisualChild<DataGridCellsPresenter>(row);
                        if (presenter?.ItemContainerGenerator.ContainerFromIndex(0) is DataGridCell cell)
                        {
                            cell.Focus();
                        }
                    }
                }), DispatcherPriority.Input);
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }
                T descendant = FindVisualChild<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }
            return null;
        }
    }
}
