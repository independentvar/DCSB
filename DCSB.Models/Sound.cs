using DCSB.Utils;
using System.Collections.ObjectModel;
using System.Xml.Serialization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;

namespace DCSB.Models
{
    public class Sound : ObservableObject, IBindable, ICloneable
    {
        private SynchronizationContext _synchronizationContext;

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

        private ObservableCollection<string> _files;
        public ObservableCollection<string> Files
        {
            get { return _files; }
            set
            {
                _files = value;
                OnPropertyChanged("Files");
            }
        }

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

        private int _volume;
        public int Volume
        {
            get { return _volume; }
            set
            {
                _volume = value;
                OnPropertyChanged("Volume");
            }
        }

        private bool _loop;
        public bool Loop
        {
            get { return _loop; }
            set
            {
                _loop = value;
                OnPropertyChanged("Loop");
            }
        }

        private string _error;
        [XmlIgnore]
        public string Error
        {
            get { return _error; }
            set
            {
                _error = value;
                OnPropertyChanged("Error");
            }
        }

        // Reads a sound file's playback length. Wired up at startup by the app layer,
        // because the audio decoders live in a higher layer than this model.
        public static Func<string, TimeSpan?> DurationProvider;

        private TimeSpan? _duration;
        [XmlIgnore]
        public TimeSpan? Duration
        {
            get { return _duration; }
            private set
            {
                _duration = value;
                OnPropertyChanged("Duration");
            }
        }

        public Sound()
        {
            _synchronizationContext = SynchronizationContext.Current;
            _keys = new ObservableCollection<VKey>();
            _files = new ObservableCollection<string>();

            _keys.CollectionChanged += (sender, e) => OnPropertyChanged("Keys");
            _files.CollectionChanged += (sender, e) => OnPropertyChanged("Files");
            _files.CollectionChanged += (sender, e) => ValidateFiles();
            _files.CollectionChanged += (sender, e) => RecalculateDuration();

            Volume = 100;
        }

        public object Clone()
        {
            Sound clonedSound = new Sound() { Name = Name, Volume = Volume, Loop = Loop };
            foreach (string file in Files) clonedSound.Files.Add(file);
            foreach (VKey key in Keys) clonedSound.Keys.Add(key);
            return clonedSound;
        }

        private void ValidateFiles()
        {
            Error = null;
            List<string> missing_files = new List<string>();
            foreach (string file in Files)
            {
                if (!File.Exists(file))
                {
                    missing_files.Add(file);
                }
            }
            if (missing_files.Count != 0)
            {
                Error = string.Format("Following files do not exist:\n{0}", string.Join("\n", missing_files));
            }
        }

        // Reads the duration off the UI thread so populating a large sound list
        // (or editing a sound's files) doesn't block on opening audio files.
        // A sound plays one of its files at random, so the total length across all
        // its files is shown. Files is snapshotted first because it can be edited
        // on the UI thread while the background read runs.
        private void RecalculateDuration()
        {
            CaptureSynchronizationContext();

            Func<string, TimeSpan?> provider = DurationProvider;
            List<string> files = Files.Where(File.Exists).ToList();
            if (provider == null || files.Count == 0)
            {
                SetDuration(null);
                return;
            }
            Task.Run(() =>
            {
                TimeSpan total = TimeSpan.Zero;
                foreach (string file in files)
                {
                    TimeSpan? length = provider(file);
                    if (length.HasValue) total += length.Value;
                }
                SetDuration(total);
            });
        }

        private void SetDuration(TimeSpan? duration)
        {
            SynchronizationContext synchronizationContext = CaptureSynchronizationContext();

            if (synchronizationContext != null && SynchronizationContext.Current != synchronizationContext)
            {
                synchronizationContext.Post(_ => Duration = duration, null);
                return;
            }

            Duration = duration;
        }

        private SynchronizationContext CaptureSynchronizationContext()
        {
            if (_synchronizationContext == null && SynchronizationContext.Current != null)
            {
                _synchronizationContext = SynchronizationContext.Current;
            }

            return _synchronizationContext;
        }
    }
}
