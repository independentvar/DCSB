using CommunityToolkit.Mvvm.ComponentModel;
using DCSB.Utils;
using System.Xml.Serialization;

namespace DCSB.Models
{
    public class ConfigurationModel : ObservableObject
    {
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

        private int _primaryDeviceVolume;
        public int PrimaryDeviceVolume
        {
            get { return _primaryDeviceVolume; }
            set
            {
                _primaryDeviceVolume = value;
                OnPropertyChanged("PrimaryDeviceVolume");
            }
        }

        private int _secondaryDeviceVolume;
        public int SecondaryDeviceVolume
        {
            get { return _secondaryDeviceVolume; }
            set
            {
                _secondaryDeviceVolume = value;
                OnPropertyChanged("SecondaryDeviceVolume");
            }
        }

        private bool _overlap;
        public bool Overlap
        {
            get { return _overlap; }
            set
            {
                _overlap = value;
                OnPropertyChanged("Overlap");
            }
        }

        private bool _normalizeVolume;
        public bool NormalizeVolume
        {
            get { return _normalizeVolume; }
            set
            {
                _normalizeVolume = value;
                OnPropertyChanged("NormalizeVolume");
            }
        }

        private string _primaryOutput;
        public string PrimaryOutput
        {
            get { return _primaryOutput; }
            set
            {
                _primaryOutput = value;
                OnPropertyChanged("PrimaryOutput");
            }
        }

        private string _secondaryOutput;
        public string SecondaryOutput
        {
            get { return _secondaryOutput; }
            set
            {
                _secondaryOutput = value;
                OnPropertyChanged("SecondaryOutput");
            }
        }

        // capture device mixed into the secondary output; null/absent (older configs)
        // means disabled, so existing setups keep working unchanged
        private string _microphoneInput;
        public string MicrophoneInput
        {
            get { return _microphoneInput; }
            set
            {
                _microphoneInput = value;
                OnPropertyChanged("MicrophoneInput");
            }
        }

        // null/empty means disabled. Keeping this opt-in ensures startup never opens
        // a MIDI handle for existing users.
        private string _midiInputDevice;
        public string MidiInputDevice
        {
            get { return _midiInputDevice; }
            set
            {
                _midiInputDevice = value;
                OnPropertyChanged(nameof(MidiInputDevice));
            }
        }

        // silences the microphone leg without dropping the capture device, so
        // unmuting is instant; persisted so a muted mic stays muted across restarts
        private bool _microphoneMuted;
        public bool MicrophoneMuted
        {
            get { return _microphoneMuted; }
            set
            {
                _microphoneMuted = value;
                OnPropertyChanged("MicrophoneMuted");
            }
        }

        // which denoiser runs on the microphone leg; absent in older configs, so
        // existing setups load with it disabled
        private NoiseSuppressionMode _noiseSuppressionMode;
        public NoiseSuppressionMode NoiseSuppressionMode
        {
            get { return _noiseSuppressionMode; }
            set
            {
                _noiseSuppressionMode = value;
                OnPropertyChanged("NoiseSuppressionMode");
            }
        }

        // Legacy 4.22.x element: noise suppression was a bool before it became a
        // mode. Reading <NoiseSuppression>true</NoiseSuppression> migrates to the
        // Fast (rnnoise) mode that bool controlled; the *Specified pattern below
        // keeps the old element from ever being written again.
        public bool NoiseSuppression
        {
            get { return _noiseSuppressionMode != NoiseSuppressionMode.Disabled; }
            set
            {
                if (value)
                {
                    NoiseSuppressionMode = NoiseSuppressionMode.Fast;
                }
            }
        }

        // always false on write, so the legacy element is dropped on the next save
        [XmlIgnore]
        public bool NoiseSuppressionSpecified
        {
            get { return false; }
            set { }
        }

        // 0-200: values above 100 boost a quiet microphone
        private int _microphoneVolume;
        public int MicrophoneVolume
        {
            get { return _microphoneVolume; }
            set
            {
                _microphoneVolume = value;
                OnPropertyChanged("MicrophoneVolume");
            }
        }

        private double _windowWidth;
        public double WindowWidth
        {
            get { return _windowWidth; }
            set
            {
                _windowWidth = value;
                OnPropertyChanged("WindowWidth");
            }
        }

        private double _windowHeight;
        public double WindowHeight
        {
            get { return _windowHeight; }
            set
            {
                _windowHeight = value;
                OnPropertyChanged("WindowHeight");
            }
        }

        private double _countersWidth;
        public double CountersWidth
        {
            get { return _countersWidth; }
            set
            {
                _countersWidth = value;
                OnPropertyChanged("CountersWidth");
            }
        }

        private double _soundsWidth;
        public double SoundsWidth
        {
            get { return _soundsWidth; }
            set
            {
                _soundsWidth = value;
                OnPropertyChanged("SoundsWidth");
            }
        }

        private bool _minimizeToTray;
        public bool MinimizeToTray
        {
            get { return _minimizeToTray; }
            set
            {
                _minimizeToTray = value;
                OnPropertyChanged("MinimizeToTray");
            }
        }

        private bool _autoAssignKeys;
        public bool AutoAssignKeys
        {
            get { return _autoAssignKeys; }
            set
            {
                _autoAssignKeys = value;
                OnPropertyChanged("AutoAssignKeys");
            }
        }

        private AutoAssignKeySet _autoAssignKeySet;
        public AutoAssignKeySet AutoAssignKeySet
        {
            get { return _autoAssignKeySet; }
            set
            {
                _autoAssignKeySet = value;
                OnPropertyChanged("AutoAssignKeySet");
            }
        }

        private bool _overlayEnabled;
        public bool OverlayEnabled
        {
            get { return _overlayEnabled; }
            set
            {
                _overlayEnabled = value;
                OnPropertyChanged("OverlayEnabled");
            }
        }

        private int _overlayOpacity;
        public int OverlayOpacity
        {
            get { return _overlayOpacity; }
            set
            {
                _overlayOpacity = value;
                OnPropertyChanged("OverlayOpacity");
            }
        }

        private DisplayOption _enable;
        public DisplayOption Enable
        {
            get { return _enable; }
            set
            {
                _enable = value;
                OnPropertyChanged("Enable");
            }
        }

        // Gate for the first-run setup wizard. A brand-new install starts false so the
        // wizard runs once; completing (or skipping) it sets this true. Configs written
        // before the wizard existed have no <SetupCompleted> element, so the companion
        // SetupCompletedSpecified stays false on load - ConfigurationManager uses that to
        // migrate existing users to "completed" so they never see the wizard.
        private bool _setupCompleted;
        public bool SetupCompleted
        {
            get { return _setupCompleted; }
            set
            {
                _setupCompleted = value;
                OnPropertyChanged("SetupCompleted");
            }
        }

        // XmlSerializer's *Specified pattern: set to true during deserialization only
        // when the element was actually present in the file, and consulted when writing.
        [XmlIgnore]
        public bool SetupCompletedSpecified { get; set; }

        private ObservableObjectCollection<Preset> _presetCollection;
        public ObservableObjectCollection<Preset> PresetCollection
        {
            get { return _presetCollection; }
            set
            {
                _presetCollection = value;
                OnPropertyChanged("PresetCollection");
            }
        }

        private int _selectedPresetIndex;
        public int SelectedPresetIndex
        {
            get { return _selectedPresetIndex; }
            set
            {
                _selectedPresetIndex = value;
                OnPropertyChanged("SelectedPreset");
            }
        }

        [XmlIgnore]
        public Preset SelectedPreset
        {
            get { return _selectedPresetIndex < PresetCollection.Count ? PresetCollection[_selectedPresetIndex] : PresetCollection[0]; }
            set
            {
                int index = PresetCollection.IndexOf(value);
                SelectedPresetIndex = index == -1 ? 0 : index;
            }
        }

        private CounterShortcuts _counterShortcuts;
        public CounterShortcuts CounterShortcuts
        {
            get { return _counterShortcuts; }
            set
            {
                _counterShortcuts = value;
                OnPropertyChanged("CounterShortcuts");
            }
        }

        private SoundShortcuts _soundShortcuts;
        public SoundShortcuts SoundShortcuts
        {
            get { return _soundShortcuts; }
            set
            {
                _soundShortcuts = value;
                OnPropertyChanged("SoundShortcuts");
            }
        }

        public ConfigurationModel()
        {
            PresetCollection = new ObservableObjectCollection<Preset>();
            CounterShortcuts = new CounterShortcuts();
            SoundShortcuts = new SoundShortcuts();

            PresetCollection.CollectionChanged += (sender, e) => OnPropertyChanged("PresetCollection");
            PresetCollection.CollectionChanged += (sender, e) => OnPropertyChanged("SelectedPreset");
            CounterShortcuts.PropertyChanged += (sender, e) => OnPropertyChanged("CounterShortcuts");
            SoundShortcuts.PropertyChanged += (sender, e) => OnPropertyChanged("SoundShortcuts");

            AutoAssignKeys = true;
            OverlayEnabled = true;
            OverlayOpacity = 100;
            Volume = 100;
            PrimaryDeviceVolume = 100;
            SecondaryDeviceVolume = 100;
            MicrophoneVolume = 100;
            WindowHeight = 300;
            WindowWidth = 500;
            CountersWidth = 1;
            SoundsWidth = 1;
        }
    }
}
