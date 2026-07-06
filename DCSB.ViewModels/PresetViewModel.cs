using DCSB.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DCSB.ViewModels
{
    public class PresetViewModel : ObservableObject
    {
        public PresetViewModel(ApplicationStateModel applicationStateModel, ConfigurationModel configurationModel)
        {
            _applicationStateModel = applicationStateModel;
            _configurationModel = configurationModel;

            _selectedCounters = new ObservableCollection<Counter>();
            _selectedSounds = new ObservableCollection<Sound>();
        }

        private ApplicationStateModel _applicationStateModel;
        public ApplicationStateModel ApplicationStateModel
        {
            get { return _applicationStateModel; }
        }

        private ConfigurationModel _configurationModel;
        public ConfigurationModel ConfigurationModel
        {
            get { return _configurationModel; }
        }

        private Preset _selectedPreset;
        public Preset SelectedPreset
        {
            get { return _selectedPreset; }
            set
            {
                _selectedPreset = value;
                OnPropertyChanged("SelectedPreset");
            }
        }

        private ObservableCollection<Counter> _selectedCounters;
        public ObservableCollection<Counter> SelectedCounters
        {
            get { return _selectedCounters; }
            set
            {
                _selectedCounters = value;
                OnPropertyChanged("SelectedCounters");
            }
        }

        private ObservableCollection<Sound> _selectedSounds;
        public ObservableCollection<Sound> SelectedSounds
        {
            get { return _selectedSounds; }
            set
            {
                _selectedSounds = value;
                OnPropertyChanged("SelectedSounds");
            }
        }

        public ICommand AddPresetCommand
        {
            get { return new RelayCommand(AddPreset); }
        }
        private void AddPreset()
        {
            Preset preset = new Preset() { Name = "New Preset" };
            _configurationModel.PresetCollection.Add(preset);
            SelectedPreset = preset;
        }

        public ICommand RemovePresetCommand
        {
            get { return new RelayCommand(RemovePreset); }
        }
        private void RemovePreset()
        {
            if (SelectedPreset != null)
            {
                if (_configurationModel.PresetCollection.Count == 1)
                {
                    AddPreset();
                    _configurationModel.PresetCollection.Remove(SelectedPreset);
                }
                else
                {
                    if (SelectedPreset == _configurationModel.SelectedPreset)
                    {
                        _configurationModel.SelectedPresetIndex = 0;
                    }
                    _configurationModel.PresetCollection.Remove(SelectedPreset);
                }
            }
        }

        public ICommand ClonePresetCommand
        {
            get { return new RelayCommand(ClonePreset); }
        }
        private void ClonePreset()
        {
            if (SelectedPreset != null)
            {
                Preset preset = (Preset)SelectedPreset.Clone();
                _configurationModel.PresetCollection.Add(preset);
                SelectedPreset = preset;
            }
        }

        public ICommand BindKeysCommand
        {
            get { return new RelayCommand<IBindable>(BindKeys); }
        }
        // Toggles inline key capture: click to start listening, click again to cancel.
        public void BindKeys(IBindable bindable)
        {
            if (bindable != null)
            {
                _applicationStateModel.ModifiedBindable =
                    _applicationStateModel.ModifiedBindable == bindable ? null : bindable;
            }
        }

        // Clears a specific bindable's keys directly, for the inline shortcut fields.
        public ICommand ClearBindableCommand
        {
            get { return new RelayCommand<IBindable>(ClearBindable); }
        }
        private void ClearBindable(IBindable bindable)
        {
            if (bindable != null)
                bindable.Keys.Clear();
        }

        public ICommand AddCounterCommand
        {
            get { return new RelayCommand(AddCounter); }
        }
        private void AddCounter()
        {
            if (SelectedPreset != null)
            {
                Counter counter = new Counter();
                SelectedPreset.CounterCollection.Add(counter);
                SelectedCounters.Clear();
                SelectedCounters.Add(counter);
                _applicationStateModel.ModifiedCounter = counter;
                _applicationStateModel.CounterOpened = true;
            }
        }

        public ICommand AddSoundCommand
        {
            get { return new RelayCommand(AddSound); }
        }
        private void AddSound()
        {
            if (SelectedPreset != null)
            {
                Sound sound = new Sound();
                SelectedPreset.SoundCollection.Add(sound);
                SelectedSounds.Clear();
                SelectedSounds.Add(sound);
                _applicationStateModel.ModifiedSound = sound;
                _applicationStateModel.SoundOpened = true;
            }
        }

        public ICommand RemoveCountersCommand
        {
            get { return new RelayCommand(RemoveCounters); }
        }
        private void RemoveCounters()
        {
            RemoveItemsFromList(SelectedCounters, SelectedPreset.CounterCollection);
        }

        public ICommand RemoveSoundsCommand
        {
            get { return new RelayCommand(RemoveSounds); }
        }
        private void RemoveSounds()
        {
            RemoveItemsFromList(SelectedSounds, SelectedPreset.SoundCollection);
        }

        public ICommand OpenCounterCommand
        {
            get { return new RelayCommand(OpenCounter); }
        }
        private void OpenCounter()
        {
            if (SelectedCounters.Count > 0)
            {
                _applicationStateModel.ModifiedCounter = SelectedCounters[0];
                _applicationStateModel.CounterOpened = true;
            }
        }

        public ICommand OpenSoundCommand
        {
            get { return new RelayCommand(OpenSound); }
        }
        private void OpenSound()
        {
            if (SelectedSounds.Count > 0)
            {
                _applicationStateModel.ModifiedSound = SelectedSounds[0];
                _applicationStateModel.SoundOpened = true;
            }
        }

        private void RemoveItemsFromList<T>(IList<T> items, IList<T> list)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                list.Remove(items[i]);
            }
        }
    }
}
