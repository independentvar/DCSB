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

        // In-game overlay box geometry, per preset: each preset shows a different
        // number of sounds, so each wants its own size and placement. Position is
        // stored monitor-relative (0..1) so it survives resolution/monitor
        // differences between where it is adjusted and the game it is shown over;
        // X is the box centre, Y its top. Size is in device-independent pixels.
        private double _overlayPositionX;
        public double OverlayPositionX
        {
            get { return _overlayPositionX; }
            set
            {
                _overlayPositionX = value;
                OnPropertyChanged("OverlayPositionX");
            }
        }

        private double _overlayPositionY;
        public double OverlayPositionY
        {
            get { return _overlayPositionY; }
            set
            {
                _overlayPositionY = value;
                OnPropertyChanged("OverlayPositionY");
            }
        }

        private double _overlayWidth;
        public double OverlayWidth
        {
            get { return _overlayWidth; }
            set
            {
                _overlayWidth = value;
                OnPropertyChanged("OverlayWidth");
            }
        }

        private double _overlayHeight;
        public double OverlayHeight
        {
            get { return _overlayHeight; }
            set
            {
                _overlayHeight = value;
                OnPropertyChanged("OverlayHeight");
            }
        }

        // False until the user drags/resizes the overlay for this preset. While
        // false the live overlay ignores the geometry above and renders like the
        // original bar: a content-sized pill centred at the top of the screen, so a
        // brand-new preset is always as small as it can be and fits all its sounds.
        private bool _overlayCustomized;
        public bool OverlayCustomized
        {
            get { return _overlayCustomized; }
            set
            {
                _overlayCustomized = value;
                OnPropertyChanged("OverlayCustomized");
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

            // default overlay box: horizontally centred at the top, 520x56 DIP
            _overlayPositionX = 0.5;
            _overlayPositionY = 0.0;
            _overlayWidth = 520;
            _overlayHeight = 56;

            Keys.CollectionChanged += (sender, e) => OnPropertyChanged("Keys");
            CounterCollection.CollectionChanged += (sender, e) => OnPropertyChanged("CounterCollection");
            CounterCollection.CollectionChanged += (sender, e) => OnPropertyChanged("SelectedCounter");
            SoundCollection.CollectionChanged += (sender, e) => OnPropertyChanged("SoundCollection");
            SoundCollection.CollectionChanged += (sender, e) => OnPropertyChanged("SelectedSound");
        }

        public object Clone()
        {
            Preset clonedPreset = new Preset()
            {
                Name = $"{Name} copy",
                OverlayPositionX = OverlayPositionX,
                OverlayPositionY = OverlayPositionY,
                OverlayWidth = OverlayWidth,
                OverlayHeight = OverlayHeight,
                OverlayCustomized = OverlayCustomized
            };
            foreach (VKey key in Keys) clonedPreset.Keys.Add(key);
            foreach (Counter counter in CounterCollection) clonedPreset.CounterCollection.Add((Counter)counter.Clone());
            foreach (Sound sound in SoundCollection) clonedPreset.SoundCollection.Add((Sound)sound.Clone());
            return clonedPreset;
        }
    }
}
