using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using DCSB.Business;
using DCSB.Input;
using DCSB.Models;
using DCSB.Utils;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Principal;
using System.Diagnostics;
using System.Windows.Threading;

namespace DCSB.ViewModels
{
    public class ViewModel : ObservableObject, IDisposable
    {
        private ApplicationStateModel _applicationStateModel;
        private ConfigurationModel _configurationModel;

        private ConfigurationManager _configurationManager;
        private OpenFileManager _openFileManager;
        private ShortcutManager _shortcutManager;
        private SoundManager _soundManager;
        private UpdateManager _updateManager;
        private KeyboardInput _keyboardInput;

        private PresetConfigurationViewModel _presetConfigurationViewModel;
        private WizardViewModel _wizardViewModel;

        private double _previousVolume;
        private double _previousPrimaryVolume;
        private double _previousSecondaryVolume;

        private double _soundPositionSeconds;
        private double _soundLengthSeconds;

        // the reader position only advances in whole audio-buffer chunks (~100 ms),
        // so the displayed position is interpolated with a wall clock between real
        // readings to make the seekbar move smoothly
        private double _lastRawPositionSeconds;
        private bool _wasPlaying;
        private bool _seekbarRenderingAttached;
        private IntPtr _windowHandle;
        private DispatcherTimer _seekbarWatchdog;
        private readonly Stopwatch _positionInterpolation = new Stopwatch();

        public ViewModel()
        {
            // must be wired before Load() so sounds get their duration as they deserialize
            Sound.DurationProvider = SoundManager.GetDuration;

            _applicationStateModel = new ApplicationStateModel();
            _configurationManager = new ConfigurationManager();
            _configurationModel = _configurationManager.Load();
            if (_configurationModel.PresetCollection.Count == 0) _configurationModel.PresetCollection.Add(new Preset() { Name = "New Preset" } );
            _openFileManager = new OpenFileManager();
            _soundManager = new SoundManager(_configurationModel);
            _shortcutManager = new ShortcutManager(_applicationStateModel, _configurationModel, _soundManager);
            _updateManager = new UpdateManager();

            _presetConfigurationViewModel = new PresetConfigurationViewModel(_applicationStateModel, _configurationModel);
            _wizardViewModel = new WizardViewModel(this, _configurationModel, _soundManager);

            _configurationModel.PropertyChanged += (sender, e) => _configurationManager.Save((ConfigurationModel)sender);

            _configurationModel.CounterShortcuts.Next.Command = NextCounterCommand;
            _configurationModel.CounterShortcuts.Previous.Command = PreviousCounterCommand;
            _configurationModel.CounterShortcuts.Increment.Command = IncrementCommand;
            _configurationModel.CounterShortcuts.Decrement.Command = DecrementCommand;
            _configurationModel.CounterShortcuts.Reset.Command = ResetCommand;
            _configurationModel.SoundShortcuts.Pause.Command = PauseCommand;
            _configurationModel.SoundShortcuts.Continue.Command = ContinueCommand;
            _configurationModel.SoundShortcuts.Stop.Command = StopCommand;

            // the seekbar updates once per rendered frame, but only while a sound is
            // playing and the window can actually be seen (not minimized, not hidden
            // to tray, not covered by a fullscreen game on the same monitor) - a
            // permanent CompositionTarget.Rendering subscription would keep WPF
            // compositing continuously even when the app idles behind a game
            _soundManager.PlaybackStarted += (sender, e) => RefreshSeekbarRendering();

            // level meter for the settings page; LevelChanged arrives on the capture
            // thread - WPF marshals PropertyChanged for scalar bindings itself
            _soundManager.MicrophoneLevelChanged += (sender, level) =>
            {
                _microphoneLevel = level;
                OnPropertyChanged("MicrophoneLevel");
            };

            // e.g. the microphone was unplugged - show the selection as Disabled
            // instead of failing silently
            _soundManager.MicrophoneFailed += (sender, e) => MicrophoneInput = DCSB.SoundPlayer.MicrophoneInput.DisabledDeviceName;

            // while a sound plays covered/minimized, no window event fires when the
            // cover goes away (e.g. the game closes); retry once a second until the
            // seekbar is visible again or playback ends
            _seekbarWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _seekbarWatchdog.Tick += (sender, e) => RefreshSeekbarRendering();

            Task.Run(() => _updateManager.AutoUpdateCheck(Version));
        }

        public double SoundPositionSeconds
        {
            get { return _soundPositionSeconds; }
            set
            {
                // only a user-initiated change (drag/click on the seekbar) arrives here
                // with a different value; the timer writes the backing field directly
                if (Math.Abs(value - _soundPositionSeconds) > 0.001)
                {
                    _soundPositionSeconds = value;
                    _soundManager.CurrentSoundPosition = TimeSpan.FromSeconds(value);
                    _lastRawPositionSeconds = value;
                    _positionInterpolation.Restart();
                    OnPropertyChanged(nameof(SoundPositionSeconds));
                }
            }
        }

        public double SoundLengthSeconds
        {
            get { return _soundLengthSeconds; }
        }

        // called on playback start, window activation/restore and by the watchdog;
        // brings the seekbar up to date and resumes per-frame updates when they are
        // worth running again
        public void RefreshSeekbarRendering()
        {
            UpdateSoundPosition();

            if (!_soundManager.IsPlaying)
            {
                _seekbarWatchdog.Stop();
            }
            else if (IsSeekbarVisibleToUser())
            {
                _seekbarWatchdog.Stop();
                if (!_seekbarRenderingAttached)
                {
                    _seekbarRenderingAttached = true;
                    System.Windows.Media.CompositionTarget.Rendering += OnSeekbarRendering;
                }
            }
            else
            {
                _seekbarWatchdog.Start();
            }
        }

        private void OnSeekbarRendering(object sender, EventArgs e)
        {
            UpdateSoundPosition();

            // paused, stopped, finished, or the window can no longer be seen - stop
            // per-frame updates; the final UpdateSoundPosition above already left the
            // seekbar showing the resting position, and the watchdog brings it back
            // for a sound that is still playing
            if (!_soundManager.IsPlaying || !IsSeekbarVisibleToUser())
            {
                _seekbarRenderingAttached = false;
                System.Windows.Media.CompositionTarget.Rendering -= OnSeekbarRendering;
                if (_soundManager.IsPlaying)
                {
                    _seekbarWatchdog.Start();
                }
            }
        }

        private bool IsSeekbarVisibleToUser()
        {
            Window window = Application.Current != null ? Application.Current.MainWindow : null;
            if (window == null || !window.IsVisible || window.WindowState == WindowState.Minimized)
            {
                return false;
            }
            return !FullscreenDetector.IsCoveredByFullscreenApp(_windowHandle);
        }

        private void UpdateSoundPosition()
        {
            double length = _soundManager.CurrentSoundLength.TotalSeconds;
            double rawPosition = _soundManager.CurrentSoundPosition.TotalSeconds;

            bool isPlaying = _soundManager.IsPlaying;
            if (Math.Abs(rawPosition - _lastRawPositionSeconds) > 0.001 || isPlaying != _wasPlaying)
            {
                // a fresh reading from the decoder (new buffer, seek or loop wrap)
                // or a pause/resume - resync the wall clock to the real position
                _lastRawPositionSeconds = rawPosition;
                _positionInterpolation.Restart();
                _wasPlaying = isPlaying;
            }

            double position = rawPosition;
            if (isPlaying)
            {
                // between decoder readings, advance with the wall clock; a new reading
                // arrives every audio buffer, so cap the extrapolation at one buffer to
                // keep a stalled pipeline from running the bar ahead
                position += Math.Min(_positionInterpolation.Elapsed.TotalSeconds, 0.2);
            }
            position = Math.Min(position, length);

            // when a buffer arrives late, the resynced position can land slightly behind
            // the extrapolated one; hold still instead of ticking backwards (real backward
            // jumps from seeking or looping are larger and pass through)
            if (isPlaying && position < _soundPositionSeconds && _soundPositionSeconds - position < 0.3)
            {
                position = _soundPositionSeconds;
            }

            if (Math.Abs(length - _soundLengthSeconds) > 0.001)
            {
                _soundLengthSeconds = length;
                OnPropertyChanged(nameof(SoundLengthSeconds));
            }
            if (Math.Abs(position - _soundPositionSeconds) > 0.001)
            {
                _soundPositionSeconds = position;
                OnPropertyChanged(nameof(SoundPositionSeconds));
            }
        }

        public IntPtr WindowHandle
        {
            set
            {
                _windowHandle = value;
                _keyboardInput = new KeyboardInput(value);
                _keyboardInput.KeyUp += _shortcutManager.KeyUp;
                _keyboardInput.KeyDown += _shortcutManager.KeyDown;
                _keyboardInput.KeyPress += _shortcutManager.KeyPress;
            }
        }

        public ApplicationStateModel ApplicationStateModel
        {
            get { return _applicationStateModel; }
        }

        // Live SelectedItems lists of the counter/sound DataGrids, pushed in by
        // DataGridBehaviors.SyncSelectedItems so Remove can delete multi-selections.
        public IList SelectedCounters { get; set; }

        public IList SelectedSounds { get; set; }

        public ConfigurationModel ConfigurationModel
        {
            get { return _configurationModel; }
        }

        public PresetConfigurationViewModel PresetConfigurationViewModel
        {
            get { return _presetConfigurationViewModel; }
        }

        public WizardViewModel WizardViewModel
        {
            get { return _wizardViewModel; }
        }

        public GridLength CountersWidth
        {
            get { return new GridLength(_configurationModel.CountersWidth, GridUnitType.Star); }
            set
            {
                _configurationModel.CountersWidth = value.Value;
                OnPropertyChanged("CountersWidth");
            }
        }

        public GridLength SoundsWidth
        {
            get { return new GridLength(_configurationModel.SoundsWidth, GridUnitType.Star); }
            set
            {
                _configurationModel.SoundsWidth = value.Value;
                OnPropertyChanged("SoundsWidth");
            }
        }

        public Version Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public double CurrentVolume
        {
            get { return _configurationModel.Volume; }
            set
            {
                _configurationModel.Volume = (int)value;
                _soundManager.Volume = _configurationModel.Volume / 100f;
                OnPropertyChanged("CurrentVolume");
            }
        }

        public double PrimaryDeviceVolume
        {
            get { return _configurationModel.PrimaryDeviceVolume; }
            set
            {
                _configurationModel.PrimaryDeviceVolume = (int)value;
                _soundManager.PrimaryDeviceVolume = _configurationModel.PrimaryDeviceVolume / 100f;
                OnPropertyChanged("PrimaryDeviceVolume");
            }
        }

        public double SecondaryDeviceVolume
        {
            get { return _configurationModel.SecondaryDeviceVolume; }
            set
            {
                _configurationModel.SecondaryDeviceVolume = (int)value;
                _soundManager.SecondaryDeviceVolume = _configurationModel.SecondaryDeviceVolume / 100f;
                OnPropertyChanged("SecondaryDeviceVolume");
            }
        }

        public bool Overlap
        {
            get { return _configurationModel.Overlap; }
            set
            {
                _configurationModel.Overlap = value;
                _soundManager.Overlap = _configurationModel.Overlap;
                OnPropertyChanged("Overlap");
            }
        }

        public DisplayOption Enable
        {
            get { return _configurationModel.Enable; }
            set
            {
                _configurationModel.Enable = value;
                OnPropertyChanged("Enable");
                if (_configurationModel.Enable != DisplayOption.Sounds && _configurationModel.Enable != DisplayOption.Both)
                {
                    _soundManager.Stop();
                }
            }
        }

        public ICollection<string> AvailableOutputs
        {
            get { return _soundManager.EnumerateDevices(); }
        }

        public string PrimaryOutput
        {
            get
            {
                return _configurationModel.PrimaryOutput;
            }
            set
            {
                string selectedDeviceName = _soundManager.ChangePrimaryOutput(value);
                _configurationModel.PrimaryOutput = selectedDeviceName;
                OnPropertyChanged("PrimaryOutput");
            }
        }

        public string SecondaryOutput
        {
            get
            {
                return _configurationModel.SecondaryOutput;
            }
            set
            {
                string selectedDeviceName = _soundManager.ChangeSecondaryOutput(value);
                _configurationModel.SecondaryOutput = selectedDeviceName;
                OnPropertyChanged("SecondaryOutput");
            }
        }

        public ICollection<string> AvailableInputs
        {
            get { return _soundManager.EnumerateInputDevices(); }
        }

        public string MicrophoneInput
        {
            get
            {
                return _configurationModel.MicrophoneInput;
            }
            set
            {
                string selectedDeviceName = _soundManager.ChangeMicrophoneInput(value);
                _configurationModel.MicrophoneInput = selectedDeviceName;
                _microphoneLevel = 0;
                OnPropertyChanged("MicrophoneInput");
                OnPropertyChanged("MicrophoneLevel");
            }
        }

        public double MicrophoneVolume
        {
            get { return _configurationModel.MicrophoneVolume; }
            set
            {
                _configurationModel.MicrophoneVolume = (int)value;
                _soundManager.MicrophoneVolume = _configurationModel.MicrophoneVolume / 100f;
                OnPropertyChanged("MicrophoneVolume");
            }
        }

        // peak of the captured microphone signal (0..1); lets the user see in the
        // settings that their voice is actually being picked up
        private float _microphoneLevel;
        public double MicrophoneLevel
        {
            get { return _microphoneLevel; }
        }

        public Visibility NotAdministrator
        {
            get
            {
                return (new WindowsPrincipal(WindowsIdentity.GetCurrent()))
                    .IsInRole(WindowsBuiltInRole.Administrator) ?
                    Visibility.Collapsed : 
                    Visibility.Visible;
            }
        }

        public ICommand CheckForUpdatesCommand
        {
            get { return new RelayCommand(CheckForUpdates); }
        }
        private async void CheckForUpdates()
        {
            await _updateManager.ManualUpdateCheck(Version);
        }

        public ICommand PresetSelectedCommand
        {
            get { return new RelayCommand<Preset>(PresetSelected); }
        }
        private void PresetSelected(Preset selectedPreset)
        {
            _configurationModel.SelectedPreset = selectedPreset;
            foreach (Counter counter in selectedPreset.CounterCollection)
            {
                counter.ReadFromFile();
            }
        }

        public ICommand BackupSettingsCommand
        {
            get { return new RelayCommand(BackupSettings); }
        }
        private void BackupSettings()
        {
            string path = _openFileManager.SaveBackupFile();
            if (path == null)
            {
                return;
            }
            try
            {
                _configurationManager.Backup(_configurationModel, path);
                MessageBox.Show("Settings and key bindings backed up successfully.",
                    "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception e)
            {
                MessageBox.Show("Failed to save backup:\n" + e.Message,
                    "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public ICommand RestoreSettingsCommand
        {
            get { return new RelayCommand(RestoreSettings); }
        }
        private void RestoreSettings()
        {
            string path = _openFileManager.OpenBackupFile();
            if (path == null)
            {
                return;
            }

            var confirm = MessageBox.Show(
                "Restoring will replace your current settings and key bindings, then restart DCSB.\n\n" +
                "Continue?",
                "Restore Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _configurationManager.Restore(path);
            }
            catch (Exception e)
            {
                MessageBox.Show("The selected file is not a valid DCSB backup:\n" + e.Message,
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // restart so the whole app reloads from the restored config.xml and rewires
            // commands, shortcuts and bindings from scratch
            var exeName = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(new ProcessStartInfo(exeName));
            Application.Current.Shutdown();
        }

        public ICommand OpenSettingsCommand
        {
            get { return new RelayCommand(OpenSettings); }
        }
        private void OpenSettings()
        {
            _applicationStateModel.SettingsOpened = true;
        }

        public ICommand OpenWizardCommand
        {
            get { return new RelayCommand(OpenWizard); }
        }
        private void OpenWizard()
        {
            _applicationStateModel.WizardOpened = true;
        }

        public ICommand OpenCounterCommand
        {
            get { return new RelayCommand(OpenCounter, AreCountersEnabled); }
        }
        private void OpenCounter()
        {
            if (_configurationModel.SelectedPreset.SelectedCounter != null)
            {
                _applicationStateModel.ModifiedCounter = _configurationModel.SelectedPreset.SelectedCounter;
                _applicationStateModel.CounterOpened = true;
            }
        }

        public ICommand OpenSoundCommand
        {
            get { return new RelayCommand(OpenSound, AreSoundsEnabled); }
        }
        private void OpenSound()
        {
            if (_configurationModel.SelectedPreset.SelectedSound != null)
            {
                _applicationStateModel.ModifiedSound = _configurationModel.SelectedPreset.SelectedSound;
                _applicationStateModel.SoundOpened = true;
            }
        }

        public ICommand OpenAboutCommand
        {
            get { return new RelayCommand(OpenAbout); }
        }
        private void OpenAbout()
        {
            _applicationStateModel.AboutOpened = true;
        }

        public ICommand OpenOverlayEditCommand
        {
            get { return new RelayCommand(OpenOverlayEdit); }
        }
        private void OpenOverlayEdit()
        {
            _applicationStateModel.OverlayEditOpened = true;
        }

        public ICommand OpenNotAdministratorCommand
        {
            get { return new RelayCommand(OpenNotAdministrator); }
        }
        private void OpenNotAdministrator()
        {
            var result = MessageBox.Show("DCSB is not running as an administrator.\n" +
                "This is fine as long as keybinds work when other app is focused.\n" +
                "If you focus other app and keybins stop working, you'll need to run DCSB as admin.\n\n" +
                "Restart DCSB and run it as admin now?", 
                "Not Admin",
                MessageBoxButton.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                var startInfo = new ProcessStartInfo(exeName) { Verb = "runas" };
                Process.Start(startInfo);
                Application.Current.Shutdown();
            }
        }

        public ICommand OpenCounterFileDialogCommand
        {
            get { return new RelayCommand(OpenCounterFileDialog, AreCountersEnabled); }
        }
        private void OpenCounterFileDialog()
        {
            string result = _openFileManager.OpenCounterFile();
            if (result != null)
            {
                Counter counter = _configurationModel.SelectedPreset.SelectedCounter;
                counter.File = result;
                if (string.IsNullOrWhiteSpace(counter.Name))
                {
                    counter.Name = Path.GetFileNameWithoutExtension(result);
                }
            }
        }

        public ICommand OpenSoundFileDialogCommand
        {
            get { return new RelayCommand(OpenSoundFileDialog, AreSoundsEnabled); }
        }
        private void OpenSoundFileDialog()
        {
            string[] result = _openFileManager.OpenSoundFiles();
            if (result != null)
            {
                Sound sound = _configurationModel.SelectedPreset.SelectedSound;
                sound.Files.Clear();
                foreach (string file in result)
                {
                    sound.Files.Add(file);
                }
                if (result.Length > 0 && string.IsNullOrWhiteSpace(sound.Name))
                {
                    sound.Name = Path.GetFileNameWithoutExtension(result[0]);
                }
            }
        }

        public ICommand AddCounterCommand
        {
            get { return new RelayCommand(AddCounter, AreCountersEnabled); }
        }
        private void AddCounter()
        {
            Counter counter = new Counter();
            _configurationModel.SelectedPreset.CounterCollection.Add(counter);
            _configurationModel.SelectedPreset.SelectedCounter = counter;
            _applicationStateModel.ModifiedCounter = counter;
            _applicationStateModel.CounterOpened = true;
        }

        public ICommand DropCounterFilesCommand
        {
            get { return new RelayCommand<string[]>(DropCounterFiles, files => AreCountersEnabled()); }
        }
        private void DropCounterFiles(string[] files)
        {
            foreach (string file in files)
            {
                if (Path.GetExtension(file).ToLowerInvariant() != ".txt")
                {
                    continue;
                }
                Counter counter = new Counter() { Name = Path.GetFileNameWithoutExtension(file), File = file };
                _configurationModel.SelectedPreset.CounterCollection.Add(counter);
                _configurationModel.SelectedPreset.SelectedCounter = counter;
            }
        }

        public ICommand RemoveCounterCommand
        {
            get { return new RelayCommand(RemoveCounter, AreCountersEnabled); }
        }
        private void RemoveCounter()
        {
            Preset preset = _configurationModel.SelectedPreset;
            List<Counter> countersToRemove = new List<Counter>();
            if (SelectedCounters != null && SelectedCounters.Count > 0)
            {
                foreach (object item in SelectedCounters)
                {
                    countersToRemove.Add((Counter)item);
                }
            }
            else if (preset.SelectedCounter != null)
            {
                countersToRemove.Add(preset.SelectedCounter);
            }
            if (countersToRemove.Count == 0)
            {
                return;
            }

            int index = preset.CounterCollection.Count;
            foreach (Counter counter in countersToRemove)
            {
                int counterIndex = preset.CounterCollection.IndexOf(counter);
                if (counterIndex >= 0 && counterIndex < index)
                {
                    index = counterIndex;
                }
            }
            foreach (Counter counter in countersToRemove)
            {
                preset.CounterCollection.Remove(counter);
            }
            if (preset.CounterCollection.Count > 0)
            {
                preset.SelectedCounter = preset.CounterCollection[Math.Min(index, preset.CounterCollection.Count - 1)];
            }
        }

        public ICommand IncrementCommand
        {
            get { return new RelayCommand(Increment, AreCountersEnabled); }
        }
        private void Increment()
        {
            if (_configurationModel.SelectedPreset.SelectedCounter != null)
            {
                _configurationModel.SelectedPreset.SelectedCounter.Count += _configurationModel.SelectedPreset.SelectedCounter.Increment;
            }
        }

        public ICommand DecrementCommand
        {
            get { return new RelayCommand(Decrement, AreCountersEnabled); }
        }
        private void Decrement()
        {
            if (_configurationModel.SelectedPreset.SelectedCounter != null)
            {
                _configurationModel.SelectedPreset.SelectedCounter.Count -= _configurationModel.SelectedPreset.SelectedCounter.Increment;
            }
        }

        public ICommand ResetCommand
        {
            get { return new RelayCommand(Reset, AreCountersEnabled); }
        }
        private void Reset()
        {
            if (_configurationModel.SelectedPreset.SelectedCounter != null)
            {
                _configurationModel.SelectedPreset.SelectedCounter.Count = 0;
            }
        }

        public ICommand NextCounterCommand
        {
            get { return new RelayCommand(NextCounter, AreCountersEnabled); }
        }
        private void NextCounter()
        {
            if (_configurationModel.SelectedPreset.SelectedCounter == null )
            {
                if (_configurationModel.SelectedPreset.CounterCollection.Count != 0)
                {
                    _configurationModel.SelectedPreset.SelectedCounter = _configurationModel.SelectedPreset.CounterCollection[0];
                }
            }
            else
            {
                int currentIndex = _configurationModel.SelectedPreset.CounterCollection.IndexOf(_configurationModel.SelectedPreset.SelectedCounter);
                int nextIndex = (currentIndex + 1) % _configurationModel.SelectedPreset.CounterCollection.Count;
                _configurationModel.SelectedPreset.SelectedCounter = _configurationModel.SelectedPreset.CounterCollection[nextIndex];
            }
        }

        public ICommand PreviousCounterCommand
        {
            get { return new RelayCommand(PreviousCounter, AreCountersEnabled); }
        }
        private void PreviousCounter()
        {
            if (_configurationModel.SelectedPreset.SelectedCounter == null)
            {
                if (_configurationModel.SelectedPreset.CounterCollection.Count != 0)
                {
                    _configurationModel.SelectedPreset.SelectedCounter = _configurationModel.SelectedPreset.CounterCollection[0];
                }
            }
            else
            {
                int currentIndex = _configurationModel.SelectedPreset.CounterCollection.IndexOf(_configurationModel.SelectedPreset.SelectedCounter);
                int previousIndex = (currentIndex - 1 + _configurationModel.SelectedPreset.CounterCollection.Count) % _configurationModel.SelectedPreset.CounterCollection.Count;
                _configurationModel.SelectedPreset.SelectedCounter = _configurationModel.SelectedPreset.CounterCollection[previousIndex];
            }
        }

        public ICommand MuteCommand
        {
            get { return new RelayCommand(Mute, AreSoundsEnabled); }
        }
        private void Mute()
        {
            if (CurrentVolume == 0)
            {
                CurrentVolume = _previousVolume;
            }
            else
            {
                _previousVolume = CurrentVolume;
                CurrentVolume = 0;
            }
        }

        public ICommand MutePrimaryCommand
        {
            get { return new RelayCommand(MutePrimary); }
        }
        private void MutePrimary()
        {
            if (PrimaryDeviceVolume == 0)
            {
                PrimaryDeviceVolume = _previousPrimaryVolume;
            }
            else
            {
                _previousPrimaryVolume = PrimaryDeviceVolume;
                PrimaryDeviceVolume = 0;
            }
        }

        public ICommand MuteSecondaryCommand
        {
            get { return new RelayCommand(MuteSecondary); }
        }
        private void MuteSecondary()
        {
            if (SecondaryDeviceVolume == 0)
            {
                SecondaryDeviceVolume = _previousSecondaryVolume;
            }
            else
            {
                _previousSecondaryVolume = SecondaryDeviceVolume;
                SecondaryDeviceVolume = 0;
            }
        }

        public ICommand AddSoundCommand
        {
            get { return new RelayCommand(AddSound, AreSoundsEnabled); }
        }
        private void AddSound()
        {
            Sound sound = new Sound();
            AssignNextAvailableKeys(sound);
            _configurationModel.SelectedPreset.SelectedSound = sound;
            _configurationModel.SelectedPreset.SoundCollection.Add(sound);
            _applicationStateModel.ModifiedSound = sound;
            _applicationStateModel.SoundOpened = true;
        }

        private static readonly VKey[] _autoAssignNumberRowKeys =
        {
            VKey.KEY_1, VKey.KEY_2, VKey.KEY_3, VKey.KEY_4, VKey.KEY_5,
            VKey.KEY_6, VKey.KEY_7, VKey.KEY_8, VKey.KEY_9, VKey.KEY_0
        };
        private static readonly VKey[] _autoAssignNumpadKeys =
        {
            VKey.NUMPAD1, VKey.NUMPAD2, VKey.NUMPAD3, VKey.NUMPAD4, VKey.NUMPAD5,
            VKey.NUMPAD6, VKey.NUMPAD7, VKey.NUMPAD8, VKey.NUMPAD9, VKey.NUMPAD0
        };
        private static readonly VKey[][] _autoAssignModifierLevels =
        {
            new VKey[0], new[] { VKey.SHIFT }, new[] { VKey.CAPITAL }, new[] { VKey.TAB }
        };

        private void AssignNextAvailableKeys(Sound sound)
        {
            if (!_configurationModel.AutoAssignKeys)
            {
                return;
            }

            VKey[] keys = _configurationModel.AutoAssignKeySet == AutoAssignKeySet.Numpad
                ? _autoAssignNumpadKeys
                : _autoAssignNumberRowKeys;
            foreach (VKey[] modifiers in _autoAssignModifierLevels)
            {
                foreach (VKey key in keys)
                {
                    if (!IsComboTaken(modifiers, key))
                    {
                        foreach (VKey modifier in modifiers)
                        {
                            sound.Keys.Add(modifier);
                        }
                        sound.Keys.Add(key);
                        return;
                    }
                }
            }
        }

        private bool IsComboTaken(VKey[] modifiers, VKey key)
        {
            bool takenBySound = _configurationModel.SelectedPreset.SoundCollection.Any(s =>
                s.Keys.Count == modifiers.Length + 1 &&
                s.Keys.Contains(key) &&
                modifiers.All(modifier => s.Keys.Contains(modifier)));
            if (takenBySound)
            {
                return true;
            }

            // counter/sound shortcuts and preset bindings fire whenever their keys are a
            // subset of the pressed keys, so any such combination would trigger them too
            IEnumerable<IBindable> reserved = new IBindable[]
            {
                _configurationModel.CounterShortcuts.Next,
                _configurationModel.CounterShortcuts.Previous,
                _configurationModel.CounterShortcuts.Increment,
                _configurationModel.CounterShortcuts.Decrement,
                _configurationModel.CounterShortcuts.Reset,
                _configurationModel.SoundShortcuts.Pause,
                _configurationModel.SoundShortcuts.Continue,
                _configurationModel.SoundShortcuts.Stop
            }.Concat(_configurationModel.PresetCollection);

            return reserved.Any(bindable =>
                bindable.Keys.Count > 0 &&
                bindable.Keys.All(k => k == key || modifiers.Contains(k)));
        }

        private static readonly string[] _supportedSoundExtensions = { ".wma", ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".mp4", ".aiff", ".flac" };

        public ICommand DropSoundFilesCommand
        {
            get { return new RelayCommand<string[]>(DropSoundFiles, files => AreSoundsEnabled()); }
        }
        private void DropSoundFiles(string[] files)
        {
            foreach (string file in files)
            {
                if (Array.IndexOf(_supportedSoundExtensions, Path.GetExtension(file).ToLowerInvariant()) < 0)
                {
                    continue;
                }
                Sound sound = new Sound() { Name = Path.GetFileNameWithoutExtension(file) };
                sound.Files.Add(file);
                AssignNextAvailableKeys(sound);
                _configurationModel.SelectedPreset.SoundCollection.Add(sound);
                _configurationModel.SelectedPreset.SelectedSound = sound;
            }
        }

        public ICommand RemoveSoundCommand
        {
            get { return new RelayCommand(RemoveSound, AreSoundsEnabled); }
        }
        private void RemoveSound()
        {
            Preset preset = _configurationModel.SelectedPreset;
            List<Sound> soundsToRemove = new List<Sound>();
            if (SelectedSounds != null && SelectedSounds.Count > 0)
            {
                foreach (object item in SelectedSounds)
                {
                    soundsToRemove.Add((Sound)item);
                }
            }
            else if (preset.SelectedSound != null)
            {
                soundsToRemove.Add(preset.SelectedSound);
            }
            if (soundsToRemove.Count == 0)
            {
                return;
            }

            int index = preset.SoundCollection.Count;
            foreach (Sound sound in soundsToRemove)
            {
                int soundIndex = preset.SoundCollection.IndexOf(sound);
                if (soundIndex >= 0 && soundIndex < index)
                {
                    index = soundIndex;
                }
            }
            foreach (Sound sound in soundsToRemove)
            {
                preset.SoundCollection.Remove(sound);
            }
            if (preset.SoundCollection.Count > 0)
            {
                preset.SelectedSound = preset.SoundCollection[Math.Min(index, preset.SoundCollection.Count - 1)];
            }
        }

        public ICommand PlayCommand
        {
            get { return new RelayCommand(Play, AreSoundsEnabled); }
        }
        private void Play()
        {
            if (_configurationModel.SelectedPreset.SelectedSound != null)
            {
                _soundManager.Play(_configurationModel.SelectedPreset.SelectedSound);
            }
        }

        public ICommand PauseCommand
        {
            get { return new RelayCommand(Pause, AreSoundsEnabled); }
        }
        private void Pause()
        {
            _soundManager.Pause();
        }

        public ICommand ContinueCommand
        {
            get { return new RelayCommand(Continue, AreSoundsEnabled); }
        }
        private void Continue()
        {
            _soundManager.Continue();
        }

        public ICommand StopCommand
        {
            get { return new RelayCommand(Stop, AreSoundsEnabled); }
        }
        private void Stop()
        {
            _soundManager.Stop();
        }

        public ICommand BindKeysCommand
        {
            get { return new RelayCommand<IBindable>(BindKeys); }
        }
        // Toggles inline key capture for a field: click to start listening, click the
        // same field again to cancel. The global hook writes the keys on the next
        // key-up (see ShortcutManager.KeyUp) and clears ModifiedBindable.
        public void BindKeys(IBindable bindable)
        {
            _applicationStateModel.ModifiedBindable =
                _applicationStateModel.ModifiedBindable == bindable ? null : bindable;
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

        public ICommand ClosingCommand
        {
            get { return new RelayCommand(Closing); }
        }
        private void Closing()
        {
            _configurationManager.Dispose();
        }

        // Called when the main window closes. Tears down everything that would
        // otherwise keep DCSB.exe alive after the window is gone: the global input
        // hooks and the WASAPI playback threads (a foreground thread that blocks
        // process exit), plus the seekbar rendering hook, timer and config flush.
        public void Dispose()
        {
            if (_seekbarRenderingAttached)
            {
                _seekbarRenderingAttached = false;
                System.Windows.Media.CompositionTarget.Rendering -= OnSeekbarRendering;
            }
            if (_seekbarWatchdog != null)
            {
                _seekbarWatchdog.Stop();
            }
            if (_keyboardInput != null)
            {
                _keyboardInput.Dispose();
            }
            if (_soundManager != null)
            {
                _soundManager.Dispose();
            }
            if (_configurationManager != null)
            {
                _configurationManager.Dispose();
            }
        }

        private bool AreCountersEnabled()
        {
            return _configurationModel.Enable == DisplayOption.Counters || _configurationModel.Enable == DisplayOption.Both;
        }

        private bool AreSoundsEnabled()
        {
            return _configurationModel.Enable == DisplayOption.Sounds || _configurationModel.Enable == DisplayOption.Both;
        }
    }
}
