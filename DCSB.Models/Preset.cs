using CommunityToolkit.Mvvm.ComponentModel;
using DCSB.Utils;
using System.Collections.ObjectModel;
using System.Xml.Serialization;
using System;

namespace DCSB.Models
{
    public class Preset : ObservableObject, IBindable, ICloneable
    {
        private ObservableCollection<VKey> _keys;
        public ObservableCollection<VKey> Keys
        {
            get { return _keys; }
            set
            {
                _keys = value;
                OnPropertyChanged("Keys");
            }
        }

        private ObservableObjectCollection<Counter> _counterCollection;
        public ObservableObjectCollection<Counter> CounterCollection
        {
            get { return _counterCollection; }
            set
            {
                _counterCollection = value;
                OnPropertyChanged("CounterCollection");
            }
        }

        private ObservableObjectCollection<Sound> _soundCollection;
        public ObservableObjectCollection<Sound> SoundCollection
        {
            get { return _soundCollection; }
            set
            {
                _soundCollection = value;
                OnPropertyChanged("SoundCollection");
            }
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        private Counter _selectedCounter;
        [XmlIgnore]
        public Counter SelectedCounter
        {
            get { return _selectedCounter; }
            set
            {
                _selectedCounter = value;
                OnPropertyChanged("SelectedCounter");
            }
        }

        private Sound _selectedSound;
        [XmlIgnore]
        public Sound SelectedSound
        {
            get { return _selectedSound; }
            set
            {
                _selectedSound = value;
                OnPropertyChanged("SelectedSound");
            }
        }

        public Preset()
        {
            _keys = new ObservableCollection<VKey>();
            _counterCollection = new ObservableObjectCollection<Counter>();
            _soundCollection = new ObservableObjectCollection<Sound>();

            Keys.CollectionChanged += (sender, e) => OnPropertyChanged("Keys");
            CounterCollection.CollectionChanged += (sender, e) => OnPropertyChanged("CounterCollection");
            CounterCollection.CollectionChanged += (sender, e) => OnPropertyChanged("SelectedCounter");
            SoundCollection.CollectionChanged += (sender, e) => OnPropertyChanged("SoundCollection");
            SoundCollection.CollectionChanged += (sender, e) => OnPropertyChanged("SelectedSound");
        }

        public object Clone()
        {
            Preset clonedPreset = new Preset() { Name = $"{Name} copy" };
            foreach (VKey key in Keys) clonedPreset.Keys.Add(key);
            foreach (Counter counter in CounterCollection) clonedPreset.CounterCollection.Add((Counter)counter.Clone());
            foreach (Sound sound in SoundCollection) clonedPreset.SoundCollection.Add((Sound)sound.Clone());
            return clonedPreset;
        }
    }
}
