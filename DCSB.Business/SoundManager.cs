using DCSB.Models;
using DCSB.SoundPlayer;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DCSB.Business
{
    public class SoundManager : IDisposable
    {
        private AudioPlaybackEngine _primarySoundPlayer;
        private AudioPlaybackEngine _secondarySoundPlayer;
        private MicrophoneInput _microphoneInput;
        private Random _random;

        private float _volume = 1f;
        public float Volume
        {
            get { return _volume; }
            set
            {
                _volume = value;
                if (_primarySoundPlayer != null) _primarySoundPlayer.Volume = _volume * _primaryDeviceVolume;
                if (_secondarySoundPlayer != null) _secondarySoundPlayer.Volume = _volume * _secondaryDeviceVolume;
            }
        }

        private float _primaryDeviceVolume = 1f;
        public float PrimaryDeviceVolume
        {
            get { return _primaryDeviceVolume; }
            set
            {
                _primaryDeviceVolume = value;
                if (_primarySoundPlayer != null) _primarySoundPlayer.Volume = _volume * _primaryDeviceVolume;
            }
        }

        private float _secondaryDeviceVolume = 1f;
        public float SecondaryDeviceVolume
        {
            get { return _secondaryDeviceVolume; }
            set
            {
                _secondaryDeviceVolume = value;
                if (_secondarySoundPlayer != null) _secondarySoundPlayer.Volume = _volume * _secondaryDeviceVolume;
            }
        }

        // linear gain for the microphone leg (1 = unity, up to 2 to boost a quiet
        // mic); deliberately independent of Volume/SecondaryDeviceVolume - muting
        // the sounds must not mute the user's voice in their call
        private float _microphoneVolume = 1f;
        public float MicrophoneVolume
        {
            get { return _microphoneVolume; }
            set
            {
                _microphoneVolume = value;
                if (_secondarySoundPlayer != null) _secondarySoundPlayer.MicrophoneVolume = value;
            }
        }

        private bool _overlap;
        public bool Overlap
        {
            get { return _overlap; }
            set
            {
                _overlap = value;
                if (_primarySoundPlayer != null) _primarySoundPlayer.Overlap = value;
                if (_secondarySoundPlayer != null) _secondarySoundPlayer.Overlap = value;
            }
        }

        // raised whenever playback (re)starts, from the UI or a global hotkey;
        // lets the UI run its seekbar updates only while something is playing
        public event EventHandler PlaybackStarted;

        // raised when the microphone stops working mid-run (e.g. unplugged); the UI
        // switches the microphone selection to Disabled so the failure is visible
        public event EventHandler<Exception> MicrophoneFailed;

        // peak level (0..1) of the captured microphone signal, raised from the
        // capture thread ~40 times per second; drives the settings level meter
        public event EventHandler<float> MicrophoneLevelChanged;

        public SoundManager(ConfigurationModel configurationModel)
        {
            _random = new Random();

            // the microphone gain must be known before ChangeMicrophoneInput attaches the mic
            _microphoneVolume = configurationModel.MicrophoneVolume / 100f;

            configurationModel.PrimaryOutput = ChangePrimaryOutput(configurationModel.PrimaryOutput);
            configurationModel.SecondaryOutput = ChangeSecondaryOutput(configurationModel.SecondaryOutput);
            configurationModel.MicrophoneInput = ChangeMicrophoneInput(configurationModel.MicrophoneInput);

            Volume = configurationModel.Volume / 100f;
            PrimaryDeviceVolume = configurationModel.PrimaryDeviceVolume / 100f;
            SecondaryDeviceVolume = configurationModel.SecondaryDeviceVolume / 100f;
            Overlap = configurationModel.Overlap;
        }

        // Reads a sound file's playback length without playing it; used to show
        // durations in the UI. Static because it opens and disposes its own reader
        // and needs no output device.
        public static TimeSpan? GetDuration(string file)
        {
            return AudioMetadata.GetDuration(file);
        }

        public void Play(Sound sound)
        {
            if (sound.Files.Count == 0)
            {
                return;
            }

            string file = sound.Files[_random.Next(sound.Files.Count)];
            try
            {
                if (_primarySoundPlayer != null) _primarySoundPlayer.PlaySound(file, sound.Volume / 100f, sound.Loop);
                if (_secondarySoundPlayer != null) _secondarySoundPlayer.PlaySound(file, sound.Volume / 100f, sound.Loop);
            }
            catch (Exception ex)
            {
                sound.Error = ex.ToString();
            }
            if (PlaybackStarted != null) PlaybackStarted(this, EventArgs.Empty);
        }

        // position of the most recently started sound; both outputs play the same
        // file in sync, so the primary output (or the secondary when the primary is
        // disabled) is the source of truth and seeking is applied to both
        public TimeSpan CurrentSoundPosition
        {
            get
            {
                if (_primarySoundPlayer != null) return _primarySoundPlayer.CurrentTime;
                if (_secondarySoundPlayer != null) return _secondarySoundPlayer.CurrentTime;
                return TimeSpan.Zero;
            }
            set
            {
                if (_primarySoundPlayer != null) _primarySoundPlayer.CurrentTime = value;
                if (_secondarySoundPlayer != null) _secondarySoundPlayer.CurrentTime = value;
            }
        }

        public TimeSpan CurrentSoundLength
        {
            get
            {
                if (_primarySoundPlayer != null) return _primarySoundPlayer.TotalTime;
                if (_secondarySoundPlayer != null) return _secondarySoundPlayer.TotalTime;
                return TimeSpan.Zero;
            }
        }

        public bool IsPlaying
        {
            get
            {
                if (_primarySoundPlayer != null) return _primarySoundPlayer.IsPlaying;
                if (_secondarySoundPlayer != null) return _secondarySoundPlayer.IsPlaying;
                return false;
            }
        }

        public void Pause()
        {
            if (_primarySoundPlayer != null) _primarySoundPlayer.Pause();
            if (_secondarySoundPlayer != null) _secondarySoundPlayer.Pause();
        }

        public void Continue()
        {
            if (_primarySoundPlayer != null) _primarySoundPlayer.Continue();
            if (_secondarySoundPlayer != null) _secondarySoundPlayer.Continue();
            if (PlaybackStarted != null) PlaybackStarted(this, EventArgs.Empty);
        }

        public void Stop()
        {
            if (_primarySoundPlayer != null) _primarySoundPlayer.Stop();
            if (_secondarySoundPlayer != null) _secondarySoundPlayer.Stop();
        }

        private string InstantiateDevice(string deviceName, bool primary, ref AudioPlaybackEngine soundPlayer)
        {
            // fall back to the default output device only when no device was ever chosen
            // (first run); an explicitly chosen device that is currently missing must not
            // be silently replaced with the speakers
            if (primary && string.IsNullOrEmpty(deviceName))
            {
                deviceName = AudioPlaybackEngine.DefaultDeviceName;
            }

            string resolvedName = AudioPlaybackEngine.ResolveDeviceName(deviceName);
            if (resolvedName == null)
            {
                soundPlayer = null;
                return AudioPlaybackEngine.DisabledDeviceName;
            }

            try
            {
                soundPlayer = new AudioPlaybackEngine(resolvedName);
            }
            catch (Exception e)
            {
                // opening the device can fail (e.g. when another application holds it
                // exclusively) - disable the output instead of crashing on startup
                Debug.WriteLine(e);
                soundPlayer = null;
                return AudioPlaybackEngine.DisabledDeviceName;
            }
            return resolvedName;
        }

        private string ChangeDevice(string deviceName, float deviceVolume, bool primary, ref AudioPlaybackEngine soundPlayer)
        {
            if (soundPlayer != null)
            {
                soundPlayer.Stop();
                soundPlayer.Dispose();
            }

            string selectedDeviceName = InstantiateDevice(deviceName, primary, ref soundPlayer);

            if (soundPlayer != null)
            {
                soundPlayer.Overlap = Overlap;
                soundPlayer.Volume = _volume * deviceVolume;
            }

            return selectedDeviceName;
        }

        public string ChangePrimaryOutput(string deviceName)
        {
            return ChangeDevice(deviceName, _primaryDeviceVolume, true, ref _primarySoundPlayer);
        }

        public string ChangeSecondaryOutput(string deviceName)
        {
            string selectedDeviceName = ChangeDevice(deviceName, _secondaryDeviceVolume, false, ref _secondarySoundPlayer);
            // ChangeDevice rebuilt the engine, which lost its microphone input
            AttachMicrophone();
            return selectedDeviceName;
        }

        // Opens the requested capture device and mixes it into the secondary output
        // only - the primary output is the user's own ears, so mixing the mic there
        // would echo their voice back at them. Returns the selected device name,
        // falling back to Disabled when the device is missing or cannot be opened
        // (same policy as InstantiateDevice for outputs).
        public string ChangeMicrophoneInput(string deviceName)
        {
            DisableMicrophone();

            string resolvedName = MicrophoneInput.ResolveDeviceName(deviceName);
            if (resolvedName == null)
            {
                return MicrophoneInput.DisabledDeviceName;
            }

            try
            {
                _microphoneInput = new MicrophoneInput(resolvedName);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                _microphoneInput = null;
                return MicrophoneInput.DisabledDeviceName;
            }

            _microphoneInput.CaptureFailed += OnMicrophoneCaptureFailed;
            _microphoneInput.LevelChanged += OnMicrophoneLevelChanged;
            AttachMicrophone();

            // AttachMicrophone disables the microphone when mixing it in fails
            return _microphoneInput != null ? resolvedName : MicrophoneInput.DisabledDeviceName;
        }

        private void DisableMicrophone()
        {
            if (_microphoneInput != null)
            {
                if (_secondarySoundPlayer != null) _secondarySoundPlayer.RemoveMicrophoneInput();
                _microphoneInput.Dispose();
                _microphoneInput = null;
            }
        }

        // capture keeps running even while the secondary output is disabled (the
        // level meter still shows input); the mic is attached once both ends exist
        private void AttachMicrophone()
        {
            if (_microphoneInput == null || _secondarySoundPlayer == null)
            {
                return;
            }

            try
            {
                // drop audio captured while nothing was consuming the buffer, so the
                // voice resumes live instead of replaying a backlog
                _microphoneInput.Flush();
                _secondarySoundPlayer.SetMicrophoneInput(_microphoneInput.SampleProvider, _microphoneVolume);
            }
            catch (Exception e)
            {
                // e.g. a microphone with more than two channels cannot be converted
                Debug.WriteLine(e);
                DisableMicrophone();
                if (MicrophoneFailed != null) MicrophoneFailed(this, e);
            }
        }

        private void OnMicrophoneCaptureFailed(object sender, Exception e)
        {
            Debug.WriteLine(e);
            DisableMicrophone();
            if (MicrophoneFailed != null) MicrophoneFailed(this, e);
        }

        private void OnMicrophoneLevelChanged(object sender, float level)
        {
            if (MicrophoneLevelChanged != null) MicrophoneLevelChanged(this, level);
        }

        public ICollection<string> EnumerateDevices()
        {
            return AudioPlaybackEngine.EnumerateDevices();
        }

        public ICollection<string> EnumerateInputDevices()
        {
            return MicrophoneInput.EnumerateDevices();
        }

        // Disposing the WASAPI engines stops their playback threads. NAudio's WasapiOut
        // render thread is a foreground thread, so leaving the engines alive keeps the
        // whole process running after the window closes.
        public void Dispose()
        {
            if (_microphoneInput != null)
            {
                _microphoneInput.Dispose();
                _microphoneInput = null;
            }
            if (_primarySoundPlayer != null)
            {
                _primarySoundPlayer.Dispose();
                _primarySoundPlayer = null;
            }
            if (_secondarySoundPlayer != null)
            {
                _secondarySoundPlayer.Dispose();
                _secondarySoundPlayer = null;
            }
            GC.SuppressFinalize(this);
        }

        ~SoundManager()
        {
            if (_microphoneInput != null) _microphoneInput.Dispose();
            if (_primarySoundPlayer != null) _primarySoundPlayer.Dispose();
            if (_secondarySoundPlayer != null) _secondarySoundPlayer.Dispose();
        }
    }
}
