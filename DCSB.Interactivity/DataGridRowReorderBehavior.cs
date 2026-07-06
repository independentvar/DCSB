using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace DCSB.Interactivity
{
    // Lets the user reorder DataGrid rows by dragging them with the mouse. The moved
    // item(s) are repositioned in the grid's ItemsSource (an ObservableCollection), so
    // the new order is what gets persisted. Grabbing a row that is part of a
    // multi-selection drags the whole selection; grabbing any other row drags just it.
    //
    // Reordering rides on OLE drag/drop but only on the *Preview* (tunneling) drag
    // events with a private data format, so it lives alongside the file-drop behavior
    // in Commands.cs (which listens on the bubbling DragOver/Drop): a reorder drag is
    // handled here and marked Handled so the file-drop handlers are skipped, while an
    // external file drag carries no reorder payload and passes straight through to them.
    public static class DataGridRowReorderBehavior
    {
        private const string ReorderFormat = "DCSB.DataGridRowReorder";

        public static readonly DependencyProperty EnableRowReorderProperty =
            DependencyProperty.RegisterAttached("EnableRowReorder", typeof(bool), typeof(DataGridRowReorderBehavior),
                new PropertyMetadata(false, OnEnableRowReorderChanged));

        public static bool GetEnableRowReorder(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableRowReorderProperty);
        }

        public static void SetEnableRowReorder(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableRowReorderProperty, value);
        }

        private static readonly DependencyProperty ControllerProperty =
            DependencyProperty.RegisterAttached("Controller", typeof(RowReorderController), typeof(DataGridRowReorderBehavior),
                new PropertyMetadata(null));

        private static void OnEnableRowReorderChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (obj is DataGrid dataGrid)
            {
                if ((bool)args.NewValue)
                {
                    dataGrid.SetValue(ControllerProperty, new RowReorderController(dataGrid));
                }
                else if (dataGrid.GetValue(ControllerProperty) is RowReorderController controller)
                {
                    controller.Detach();
                    dataGrid.ClearValue(ControllerProperty);
                }
            }
        }

        // One instance per grid; holds the in-flight drag state and the handlers.
        private sealed class RowReorderController
        {
            private readonly DataGrid _dataGrid;
            private Point _startPoint;
            private object _draggedItem;
            private List<object> _selectionAtMouseDown;
            private List<object> _draggedItems;
            private bool _isDragging;
            private InsertionAdorner _adorner;

            public RowReorderController(DataGrid dataGrid)
            {
                _dataGrid = dataGrid;
                _dataGrid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
                _dataGrid.PreviewMouseMove += OnPreviewMouseMove;
                _dataGrid.PreviewDragOver += OnPreviewDragOver;
                _dataGrid.PreviewDrop += OnPreviewDrop;
                _dataGrid.DragLeave += OnDragLeave;
            }

            public void Detach()
            {
                _dataGrid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
                _dataGrid.PreviewMouseMove -= OnPreviewMouseMove;
                _dataGrid.PreviewDragOver -= OnPreviewDragOver;
                _dataGrid.PreviewDrop -= OnPreviewDrop;
                _dataGrid.DragLeave -= OnDragLeave;
                RemoveAdorner();
            }

            private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
            {
                _startPoint = args.GetPosition(null);
                DataGridRow row = GetRowFromVisual(args.OriginalSource as DependencyObject);
                _draggedItem = row?.Item;

                // This tunneling handler runs before the cell's bubbling mouse-down, which
                // is where the grid collapses a multi-selection down to the clicked row.
                // Snapshot the selection now so a drag can still move the whole set.
                _selectionAtMouseDown = new List<object>();
                foreach (object item in _dataGrid.SelectedItems)
                {
                    _selectionAtMouseDown.Add(item);
                }
            }

            private void OnPreviewMouseMove(object sender, MouseEventArgs args)
            {
                if (_isDragging || _draggedItem == null || args.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                Point position = args.GetPosition(null);
                if (Math.Abs(position.X - _startPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(position.Y - _startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                // Only reorder items that actually live in this grid's collection.
                if (!(_dataGrid.ItemsSource is IList list) || list.IndexOf(_draggedItem) < 0)
                {
                    _draggedItem = null;
                    return;
                }

                _draggedItems = BuildMovingItems(list);
                if (_draggedItems.Count == 0)
                {
                    _draggedItem = null;
                    return;
                }

                // The grid already collapsed a multi-selection to the grabbed row on
                // mouse-down; put the full set back so every dragged row stays visibly
                // selected during the drag.
                if (_draggedItems.Count > 1)
                {
                    _dataGrid.SelectedItems.Clear();
                    foreach (object item in _draggedItems)
                    {
                        _dataGrid.SelectedItems.Add(item);
                    }
                }

                _isDragging = true;
                try
                {
                    DataObject data = new DataObject(ReorderFormat, _draggedItem);
                    DragDrop.DoDragDrop(_dataGrid, data, DragDropEffects.Move);
                }
                finally
                {
                    _isDragging = false;
                    _draggedItem = null;
                    _draggedItems = null;
                    RemoveAdorner();
                }
            }

            // The rows that will move: the whole selection when the grabbed row is part of
            // it, otherwise just the grabbed row. Ordered by their position in the
            // collection so their relative order is preserved on drop. Uses the selection
            // snapshot from mouse-down, since the grid has since collapsed it to one row.
            private List<object> BuildMovingItems(IList list)
            {
                List<object> moving = new List<object>();
                List<object> selected = _selectionAtMouseDown;
                if (selected != null && selected.Count > 1 && selected.Contains(_draggedItem))
                {
                    foreach (object item in selected)
                    {
                        if (list.IndexOf(item) >= 0)
                        {
                            moving.Add(item);
                        }
                    }
                    moving.Sort((a, b) => list.IndexOf(a).CompareTo(list.IndexOf(b)));
                }
                else
                {
                    moving.Add(_draggedItem);
                }
                return moving;
            }

            private void OnPreviewDragOver(object sender, DragEventArgs args)
            {
                if (!args.Data.GetDataPresent(ReorderFormat))
                {
                    return;
                }

                args.Effects = DragDropEffects.Move;
                args.Handled = true;
                ShowInsertionAt(args.GetPosition(_dataGrid));
            }

            private void OnDragLeave(object sender, DragEventArgs args)
            {
                if (args.Data.GetDataPresent(ReorderFormat))
                {
                    RemoveAdorner();
                }
            }

            private void OnPreviewDrop(object sender, DragEventArgs args)
            {
                if (!args.Data.GetDataPresent(ReorderFormat))
                {
                    return;
                }

                args.Handled = true;
                RemoveAdorner();

                if (!(_dataGrid.ItemsSource is IList list) || _draggedItems == null)
                {
                    return;
                }

                // The set can have shrunk if items left the collection mid-drag; keep the
                // ones still present, in their current order.
                List<object> moving = new List<object>();
                foreach (object item in _draggedItems)
                {
                    if (list.IndexOf(item) >= 0)
                    {
                        moving.Add(item);
                    }
                }
                if (moving.Count == 0)
                {
                    return;
                }

                // Insertion point in the current list (0 = before the first row). Pulling
                // out the moving rows that sit above it shifts the target up by that many,
                // giving the slot among the rows that stay put.
                int refIndex = GetInsertionIndex(args.GetPosition(_dataGrid));
                int shift = 0;
                foreach (object item in moving)
                {
                    if (list.IndexOf(item) < refIndex)
                    {
                        shift++;
                    }
                }

                List<object> remaining = new List<object>();
                foreach (object item in list)
                {
                    if (!moving.Contains(item))
                    {
                        remaining.Add(item);
                    }
                }
                int insertAt = Math.Max(0, Math.Min(refIndex - shift, remaining.Count));

                // Bail out when the rows already sit exactly where they'd land, so a drop
                // in place doesn't churn the collection, selection and config save.
                if (IsAlreadyInPlace(list, remaining, moving, insertAt))
                {
                    return;
                }

                foreach (object item in moving)
                {
                    list.Remove(item);
                }
                for (int i = 0; i < moving.Count; i++)
                {
                    list.Insert(insertAt + i, moving[i]);
                }

                RestoreSelection(moving);
            }

            // True when reinserting the moving rows at insertAt reproduces the current
            // order (i.e. they are already contiguous and in place at that slot).
            private static bool IsAlreadyInPlace(IList list, List<object> remaining, List<object> moving, int insertAt)
            {
                if (list.Count != remaining.Count + moving.Count)
                {
                    return false;
                }
                for (int i = 0; i < moving.Count; i++)
                {
                    if (!ReferenceEquals(list[insertAt + i], moving[i]))
                    {
                        return false;
                    }
                }
                return true;
            }

            // Re-select the moved rows once the drag has fully unwound (the removals
            // stripped them from the grid's selection). Deferred so it runs after WPF
            // finishes the drop, and anchored on the grabbed row for keyboard focus.
            private void RestoreSelection(List<object> moving)
            {
                object anchor = _draggedItem != null && moving.Contains(_draggedItem) ? _draggedItem : moving[0];
                _dataGrid.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_dataGrid.ItemsSource == null)
                    {
                        return;
                    }
                    _dataGrid.SelectedItems.Clear();
                    foreach (object item in moving)
                    {
                        _dataGrid.SelectedItems.Add(item);
                    }
                    _dataGrid.CurrentItem = anchor;
                    _dataGrid.ScrollIntoView(anchor);
                }), DispatcherPriority.Input);
            }

            // Where the dragged item should land, expressed as a slot in the current
            // list (0 = before the first row, Count = after the last). A drop on the top
            // half of a row inserts before it, the bottom half after it.
            private int GetInsertionIndex(Point positionInGrid)
            {
                DataGridRow row = GetRowUnderPoint(positionInGrid);
                if (row == null)
                {
                    return _dataGrid.Items.Count;
                }

                int index = _dataGrid.ItemContainerGenerator.IndexFromContainer(row);
                Point inRow = _dataGrid.TranslatePoint(positionInGrid, row);
                if (inRow.Y > row.ActualHeight / 2)
                {
                    index++;
                }
                return index;
            }

            private void ShowInsertionAt(Point positionInGrid)
            {
                double y;
                DataGridRow row = GetRowUnderPoint(positionInGrid);
                if (row != null)
                {
                    Point top = row.TranslatePoint(new Point(0, 0), _dataGrid);
                    Point inRow = _dataGrid.TranslatePoint(positionInGrid, row);
                    y = inRow.Y > row.ActualHeight / 2 ? top.Y + row.ActualHeight : top.Y;
                }
                else
                {
                    // Below the last row: draw at the bottom edge of the last one.
                    DataGridRow last = GetLastRow();
                    if (last == null)
                    {
                        return;
                    }
                    Point top = last.TranslatePoint(new Point(0, 0), _dataGrid);
                    y = top.Y + last.ActualHeight;
                }

                if (_adorner == null)
                {
                    AdornerLayer layer = AdornerLayer.GetAdornerLayer(_dataGrid);
                    if (layer == null)
                    {
                        return;
                    }
                    _adorner = new InsertionAdorner(_dataGrid);
                    layer.Add(_adorner);
                }
                _adorner.SetLine(y);
            }

            private void RemoveAdorner()
            {
                if (_adorner != null)
                {
                    AdornerLayer layer = AdornerLayer.GetAdornerLayer(_dataGrid);
                    layer?.Remove(_adorner);
                    _adorner = null;
                }
            }

            private DataGridRow GetRowUnderPoint(Point positionInGrid)
            {
                return _dataGrid.InputHitTest(positionInGrid) is DependencyObject hit
                    ? GetRowFromVisual(hit)
                    : null;
            }

            private DataGridRow GetLastRow()
            {
                for (int i = _dataGrid.Items.Count - 1; i >= 0; i--)
                {
                    if (_dataGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                    {
                        return row;
                    }
                }
                return null;
            }

            private static DataGridRow GetRowFromVisual(DependencyObject visual)
            {
                while (visual != null && !(visual is DataGridRow))
                {
                    visual = visual is Visual || visual is Visual3D
                        ? VisualTreeHelper.GetParent(visual)
                        : LogicalTreeHelper.GetParent(visual);
                }
                return visual as DataGridRow;
            }
        }

        // Draws the horizontal insertion line across the grid at a given vertical offset.
        private sealed class InsertionAdorner : Adorner
        {
            private static readonly Pen LinePen = CreatePen();
            private double _y = double.NaN;

            public InsertionAdorner(UIElement adornedElement) : base(adornedElement)
            {
                IsHitTestVisible = false;
            }

            public void SetLine(double y)
            {
                if (_y != y)
                {
                    _y = y;
                    InvalidateVisual();
                }
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                if (double.IsNaN(_y))
                {
                    return;
                }
                double width = AdornedElement.RenderSize.Width;
                drawingContext.DrawLine(LinePen, new Point(0, _y), new Point(width, _y));
                // Small triangle markers at each end make the drop position easy to read.
                drawingContext.DrawGeometry(LinePen.Brush, null, CreateTriangle(0, _y, true));
                drawingContext.DrawGeometry(LinePen.Brush, null, CreateTriangle(width, _y, false));
            }

            private static Geometry CreateTriangle(double x, double y, bool pointingRight)
            {
                double d = pointingRight ? 5 : -5;
                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(new Point(x, y - 4), true, true);
                    context.LineTo(new Point(x + d, y), true, false);
                    context.LineTo(new Point(x, y + 4), true, false);
                }
                geometry.Freeze();
                return geometry;
            }

            private static Pen CreatePen()
            {
                Pen pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x82, 0xDA)), 2);
                pen.Freeze();
                return pen;
            }
        }
    }
}
