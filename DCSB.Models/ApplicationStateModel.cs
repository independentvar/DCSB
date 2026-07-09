using CommunityToolkit.Mvvm.ComponentModel;

namespace DCSB.Models
{
    public class ApplicationStateModel : ObservableObject
    {
        private bool _settingsOpened;
        public bool SettingsOpened
        {
            get { return _settingsOpened; }
            set
            {
                _settingsOpened = value;
                OnPropertyChanged("SettingsOpened");
            }
        }

        private bool _soundOpened;
        public bool SoundOpened
        {
            get { return _soundOpened; }
            set
            {
                _soundOpened = value;
                OnPropertyChanged("SoundOpened");
            }
        }

        private bool _counterOpened;
        public bool CounterOpened
        {
            get { return _counterOpened; }
            set
            {
                _counterOpened = value;
                OnPropertyChanged("CounterOpened");
            }
        }

        private bool _aboutOpened;
        public bool AboutOpened
        {
            get { return _aboutOpened; }
            set
            {
                _aboutOpened = value;
                OnPropertyChanged("AboutOpened");
            }
        }

        private bool _wizardOpened;
        public bool WizardOpened
        {
            get { return _wizardOpened; }
            set
            {
                _wizardOpened = value;
                OnPropertyChanged("WizardOpened");
            }
        }

        private bool _overlayEditOpened;
        public bool OverlayEditOpened
        {
            get { return _overlayEditOpened; }
            set
            {
                _overlayEditOpened = value;
                OnPropertyChanged("OverlayEditOpened");
            }
        }

        private IBindable _modifiedBindable;
        public IBindable ModifiedBindable
        {
            get { return _modifiedBindable; }
            set
            {
                _modifiedBindable = value;
                OnPropertyChanged("ModifiedBindable");
            }
        }

        private Counter _modifiedCounter;
        public Counter ModifiedCounter
        {
            get { return _modifiedCounter; }
            set
            {
                _modifiedCounter = value;
                OnPropertyChanged("ModifiedCounter");
            }
        }

        private Sound _modifiedSound;
        public Sound ModifiedSound
        {
            get { return _modifiedSound; }
            set
            {
                _modifiedSound = value;
                OnPropertyChanged("ModifiedSound");
            }
        }
    }
}
