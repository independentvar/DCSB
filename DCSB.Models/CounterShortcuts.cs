using CommunityToolkit.Mvvm.ComponentModel;
using DCSB.Utils;

namespace DCSB.Models
{
    public class CounterShortcuts : ObservableObject
    {
        private Shortcut _next;
        public Shortcut Next
        {
            get { return _next; }
            set
            {
                _next = value;
                OnPropertyChanged("Next");
            }
        }

        private Shortcut _previous;
        public Shortcut Previous
        {
            get { return _previous; }
            set
            {
                _previous = value;
                OnPropertyChanged("Previous");
            }
        }

        private Shortcut _increment;
        public Shortcut Increment
        {
            get { return _increment; }
            set
            {
                _increment = value;
                OnPropertyChanged("Increment");
            }
        }

        private Shortcut _decrement;
        public Shortcut Decrement
        {
            get { return _decrement; }
            set
            {
                _decrement = value;
                OnPropertyChanged("Decrement");
            }
        }

        private Shortcut _reset;
        public Shortcut Reset
        {
            get { return _reset; }
            set
            {
                _reset = value;
                OnPropertyChanged("Reset");
            }
        }

        public CounterShortcuts()
        {
            _next = new Shortcut();
            _previous = new Shortcut();
            _increment = new Shortcut();
            _decrement = new Shortcut();
            _reset = new Shortcut();

            _next.PropertyChanged += (sender, e) => OnPropertyChanged("Next");
            _previous.PropertyChanged += (sender, e) => OnPropertyChanged("Previous");
            _increment.PropertyChanged += (sender, e) => OnPropertyChanged("Increment");
            _decrement.PropertyChanged += (sender, e) => OnPropertyChanged("Decrement");
            _reset.PropertyChanged += (sender, e) => OnPropertyChanged("Reset");

            _next.Keys.Add(VKey.MULTIPLY);
            _increment.Keys.Add(VKey.ADD);
            _decrement.Keys.Add(VKey.SUBTRACT);
        }
    }
}
