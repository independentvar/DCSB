using DCSB.Models;
using DCSB.SoundPlayer;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DCSB.Business
{
    public class SoundManager
    {
        private AudioPlaybackEngine _primarySoundPlayer;
        private AudioPlaybackEngine _secondarySoundPlayer;
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

        public SoundManager(ConfigurationModel configurationModel)
        {
            _random = new Random();

            configurationModel.PrimaryOutput = ChangePrimaryOutput(configurationModel.PrimaryOutput);
            configurationModel.SecondaryOutput = ChangeSecondaryOutput(configurationModel.SecondaryOutput);

            Volume = configurationModel.Volume / 100f;
            PrimaryDeviceVolume = configurationModel.PrimaryDeviceVolume / 100f;
            SecondaryDeviceVolume = configurationModel.SecondaryDeviceVolume / 100f;
            Overlap = configurationModel.Overlap;
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
            return ChangeDevice(deviceName, _secondaryDeviceVolume, false, ref _secondarySoundPlayer);
        }

        public ICollection<string> EnumerateDevices()
        {
            return AudioPlaybackEngine.EnumerateDevices();
        }

        ~SoundManager()
        {
            if (_primarySoundPlayer != null) _primarySoundPlayer.Dispose();
            if (_secondarySoundPlayer != null) _secondarySoundPlayer.Dispose();
        }
    }
}
