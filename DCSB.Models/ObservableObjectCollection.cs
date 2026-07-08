using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections;
using System.Threading;

namespace DCSB.Models
{
    public class ObservableObjectCollection<T> : ObservableCollection<T> where T : INotifyPropertyChanged
    {
        private SynchronizationContext _synchronizationContext = SynchronizationContext.Current;

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CaptureSynchronizationContext();

            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                RegisterPropertyChanged(e.NewItems);
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                UnRegisterPropertyChanged(e.OldItems);
            }
            else if (e.Action == NotifyCollectionChangedAction.Replace)
            {
                UnRegisterPropertyChanged(e.OldItems);
                RegisterPropertyChanged(e.NewItems);
            }

            base.OnCollectionChanged(e);
        }

        protected override void ClearItems()
        {
            UnRegisterPropertyChanged(this);
            base.ClearItems();
        }

        private void RegisterPropertyChanged(IList items)
        {
            foreach (INotifyPropertyChanged item in items)
            {
                if (item != null)
                {
                    item.PropertyChanged += new PropertyChangedEventHandler(ItemPropertyChanged);
                }
            }
        }

        private void UnRegisterPropertyChanged(IList items)
        {
            foreach (INotifyPropertyChanged item in items)
            {
                if (item != null)
                {
                    item.PropertyChanged -= new PropertyChangedEventHandler(ItemPropertyChanged);
                }
            }
        }

        private void ItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SynchronizationContext synchronizationContext = CaptureSynchronizationContext();

            if (synchronizationContext != null && SynchronizationContext.Current != synchronizationContext)
            {
                synchronizationContext.Post(_ => NotifyItemPropertyChanged(), null);
                return;
            }

            NotifyItemPropertyChanged();
        }

        private SynchronizationContext CaptureSynchronizationContext()
        {
            if (_synchronizationContext == null && SynchronizationContext.Current != null)
            {
                _synchronizationContext = SynchronizationContext.Current;
            }

            return _synchronizationContext;
        }

        private void NotifyItemPropertyChanged()
        {
            base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
