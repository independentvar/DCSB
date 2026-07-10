using CommunityToolkit.Mvvm.ComponentModel;

namespace DCSB.Models
{
    public class SoundShortcuts : ObservableObject
    {
        private Shortcut _pause;
        public Shortcut Pause
        {
            get { return _pause; }
            set
            {
                _pause = value;
                OnPropertyChanged("Pause");
            }
        }

        private Shortcut _continue;
        public Shortcut Continue
        {
            get { return _continue; }
            set
            {
                _continue = value;
                OnPropertyChanged("Continue");
            }
        }

        private Shortcut _stop;
        public Shortcut Stop
        {
            get { return _stop; }
            set
            {
                _stop = value;
                OnPropertyChanged("Stop");
            }
        }

        private Shortcut _muteMicrophone;
        public Shortcut MuteMicrophone
        {
            get { return _muteMicrophone; }
            set
            {
                _muteMicrophone = value;
                OnPropertyChanged("MuteMicrophone");
            }
        }

        public SoundShortcuts()
        {
            _pause = new Shortcut();
            _continue = new Shortcut();
            _stop = new Shortcut();
            _muteMicrophone = new Shortcut();

            _pause.PropertyChanged += (sender, e) => OnPropertyChanged("Pause");
            _continue.PropertyChanged += (sender, e) => OnPropertyChanged("Continue");
            _stop.PropertyChanged += (sender, e) => OnPropertyChanged("Stop");
            _muteMicrophone.PropertyChanged += (sender, e) => OnPropertyChanged("MuteMicrophone");
        }
    }
}
