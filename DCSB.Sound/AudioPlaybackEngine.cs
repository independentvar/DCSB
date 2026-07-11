using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;

namespace DCSB.SoundPlayer
{
    public class AudioPlaybackEngine
    {
        public const string DisabledDeviceName = "Disabled";
        public const string DefaultDeviceName = "Default Output Device";

        // WaveOut device names (used by older versions) were truncated to 31 characters
        private const int LegacyDeviceNameLength = 31;

        // buffer for the stock WasapiOut used when LowLatencyWasapiOut fails
        // outright; sets the floor of the microphone passthrough latency, so it
        // must stay small - but not so small that cheap USB devices underrun
        private const int OutputLatencyMilliseconds = 30;

        private readonly IWavePlayer _outputDevice;
        private readonly MixingSampleProvider _mixer;
        private readonly VolumeSampleProvider _masterVolume;
        private readonly MeteringSampleProvider _meter;
        private readonly PausableSampleProvider _soundBranch;
        private readonly MixingSampleProvider _outputMixer;

        // peak amplitude (0..1) of the sounds leaving this engine (post master volume),
        // raised on the render thread a few dozen times per second; drives the setup
        // wizard's per-output level meter. Mirrors MicrophoneInput.LevelChanged.
        public event EventHandler<float> LevelChanged;

        // persistent microphone input on the output mixer; unlike sounds it is not
        // affected by Stop/Pause or the master volume (turning game sounds down must
        // not quiet the user's voice in their call)
        private ISampleProvider _microphoneMixerInput;
        private VolumeSampleProvider _microphoneVolume;
        private NoiseSuppressionSampleProvider _noiseSuppression;

        private int _volumePowBase = 100;

        // the most recently started sound; the seekbar tracks and seeks this one
        private volatile SampleReader _currentReader;
        private readonly object _activeSoundsLock = new object();
        private readonly List<ActiveSound> _activeSounds = new List<ActiveSound>();

        private sealed class ActiveSound
        {
            public object Id { get; set; }
            public SampleReader Reader { get; set; }
            public ISampleProvider MixerInput { get; set; }
            public PausableSampleProvider PauseControl { get; set; }
        }

        public TimeSpan CurrentTime
        {
            get
            {
                SampleReader reader = _currentReader;
                return reader != null ? reader.CurrentTime : TimeSpan.Zero;
            }
            set
            {
                SampleReader reader = _currentReader;
                if (reader != null)
                {
                    reader.CurrentTime = value;
                }
            }
        }

        public TimeSpan TotalTime
        {
            get
            {
                SampleReader reader = _currentReader;
                return reader != null ? reader.TotalTime : TimeSpan.Zero;
            }
        }

        // true while the tracked sound is actually progressing (sounds not paused,
        // sound not yet finished); the device itself keeps "playing" silence even
        // with no inputs, hence the reader check
        public bool IsPlaying
        {
            get
            {
                SampleReader reader = _currentReader;
                if (reader == null || reader.IsDisposed || _soundBranch.IsPaused
                    || _outputDevice.PlaybackState != PlaybackState.Playing)
                {
                    return false;
                }
                lock (_activeSoundsLock)
                {
                    ActiveSound currentSound = _activeSounds.Find(x => ReferenceEquals(x.Reader, reader));
                    return currentSound != null && !currentSound.PauseControl.IsPaused;
                }
            }
        }

        public float Volume
        {
            get { return RevertVolume(_masterVolume.Volume); }
            set { _masterVolume.Volume = AdjustVolume(value); }
        }

        public bool Overlap { get; set; }

        public AudioPlaybackEngine(string deviceName)
        {
            MMDevice device = FindDevice(deviceName);
            if (device == null)
            {
                throw new ArgumentException($"Output device '{deviceName}' was not found.", nameof(deviceName));
            }

            // mix at the endpoint's own sample rate so no rate conversion happens between
            // the app and the device (avoids crackling with virtual cables, see issue #154)
            WaveFormat mixFormat = device.AudioClient.MixFormat;
            int channels = Math.Min(mixFormat.Channels, 2);
            WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(mixFormat.SampleRate, channels);

            // two-stage graph: sounds mix in _mixer and pass through the master volume
            // and the pause switch; the microphone joins at _outputMixer, past all of
            // them, so stopping/pausing/muting sounds can never interrupt the voice
            _mixer = new MixingSampleProvider(format) { ReadFully = true };
            _masterVolume = new VolumeSampleProvider(_mixer);
            // meter the sounds right after the master volume: the output mixer pulls the
            // sound branch continuously (ReadFully), so StreamVolume fires steadily and
            // reports ~0 when idle and the real peak while a sound plays
            _meter = new MeteringSampleProvider(_masterVolume, format.SampleRate / 25);
            _meter.StreamVolume += OnStreamVolume;
            _soundBranch = new PausableSampleProvider(_meter);
            _outputMixer = new MixingSampleProvider(format) { ReadFully = true };
            _outputMixer.AddMixerInput(_soundBranch);

            _outputDevice = CreateOutputDevice(device, _outputMixer);
            _outputDevice.Play();
        }

        // Prefers the IAudioClient3-based output (engine-minimum period, ~3-10 ms);
        // any failure there falls back to the stock WasapiOut at a small buffer, so
        // an exotic device or OS can never lose audio entirely to the fast path.
        private static IWavePlayer CreateOutputDevice(MMDevice device, MixingSampleProvider outputMixer)
        {
            LowLatencyWasapiOut lowLatencyOutput = new LowLatencyWasapiOut(device);
            try
            {
                lowLatencyOutput.Init(new SampleToWaveProvider(outputMixer));
                return lowLatencyOutput;
            }
            catch (Exception)
            {
                lowLatencyOutput.Dispose();
                WasapiOut fallbackOutput = new WasapiOut(device, AudioClientShareMode.Shared, true, OutputLatencyMilliseconds);
                fallbackOutput.Init(outputMixer);
                return fallbackOutput;
            }
        }

        private void OnStreamVolume(object sender, StreamVolumeEventArgs e)
        {
            EventHandler<float> handler = LevelChanged;
            if (handler == null)
            {
                return;
            }

            float peak = 0f;
            foreach (float channelPeak in e.MaxSampleValues)
            {
                if (channelPeak > peak) peak = channelPeak;
            }
            handler(this, Math.Min(peak, 1f));
        }

        // normalizationGain is a linear factor applied on top of the exponential
        // volume curve (1 = none); the caller computes it from the file's measured
        // loudness so differently mastered clips play equally loud
        public void PlaySound(string fileName, float volume, bool loop, float normalizationGain = 1f, object soundId = null)
        {
            if (!Overlap)
            {
                Stop();
            }

            IAudioReader input = AudioReaderFactory.CreateReader(fileName);

            SampleReader reader = new SampleReader(input, loop);
            PausableSampleProvider mixerInput = AddMixerInput(reader, volume, normalizationGain);
            lock (_activeSoundsLock)
            {
                RemoveFinishedSounds();
                _activeSounds.Add(new ActiveSound
                {
                    Id = soundId,
                    Reader = reader,
                    MixerInput = mixerInput,
                    PauseControl = mixerInput
                });
            }
            _currentReader = reader;

            // starting a new sound resumes paused ones, matching the old behavior of
            // restarting the paused output device
            _soundBranch.IsPaused = false;

            if (_outputDevice.PlaybackState != PlaybackState.Playing)
            {
                _outputDevice.Play();
            }
        }

        public bool IsSoundPlaying(object soundId)
        {
            lock (_activeSoundsLock)
            {
                RemoveFinishedSounds();
                return _activeSounds.Exists(x => ReferenceEquals(x.Id, soundId) && !x.Reader.IsDisposed);
            }
        }

        public bool IsSoundPaused(object soundId)
        {
            lock (_activeSoundsLock)
            {
                RemoveFinishedSounds();
                List<ActiveSound> matchingSounds = _activeSounds.FindAll(x => ReferenceEquals(x.Id, soundId));
                return matchingSounds.Count != 0 && matchingSounds.TrueForAll(x => x.PauseControl.IsPaused);
            }
        }

        public void SetSoundPaused(object soundId, bool paused)
        {
            lock (_activeSoundsLock)
            {
                RemoveFinishedSounds();
                foreach (ActiveSound sound in _activeSounds.FindAll(x => ReferenceEquals(x.Id, soundId)))
                {
                    sound.PauseControl.IsPaused = paused;
                }
            }
            if (!paused && _outputDevice.PlaybackState != PlaybackState.Playing)
            {
                _outputDevice.Play();
            }
        }

        public void StopSound(object soundId)
        {
            List<ActiveSound> soundsToStop;
            lock (_activeSoundsLock)
            {
                RemoveFinishedSounds();
                soundsToStop = _activeSounds.FindAll(x => ReferenceEquals(x.Id, soundId));
                _activeSounds.RemoveAll(x => ReferenceEquals(x.Id, soundId));
                if (soundsToStop.Exists(x => ReferenceEquals(x.Reader, _currentReader)))
                {
                    _currentReader = FindMostRecentReader();
                }
            }

            foreach (ActiveSound sound in soundsToStop)
            {
                _mixer.RemoveMixerInput(sound.MixerInput);
                sound.Reader.Dispose();
            }
        }

        // pause/continue silence the sound branch instead of pausing the output
        // device, so a microphone input keeps flowing while sounds are paused
        public void Pause()
        {
            _soundBranch.IsPaused = true;
        }

        public void Continue()
        {
            _soundBranch.IsPaused = false;
            if (_outputDevice.PlaybackState != PlaybackState.Playing)
            {
                _outputDevice.Play();
            }
        }

        public void Stop()
        {
            List<ActiveSound> activeSounds;
            lock (_activeSoundsLock)
            {
                activeSounds = new List<ActiveSound>(_activeSounds);
                _activeSounds.Clear();
            }
            _currentReader = null;
            _mixer.RemoveAllMixerInputs();
            foreach (ActiveSound sound in activeSounds)
            {
                // Removing a mixer input does not dispose it. Release the
                // decoder stream immediately so stopping an Opus (or any other)
                // clip cannot leave its file locked.
                sound.Reader.Dispose();
            }
            _soundBranch.IsPaused = false;
            if (_microphoneMixerInput == null)
            {
                _outputDevice.Stop();
            }
        }

        // Attaches a continuous microphone stream as a persistent input of the output
        // mixer. Volume is a linear gain (1 = unity); values above 1 boost a quiet
        // microphone, and the exponential sound volume curve does not apply.
        // noiseSuppression is an optional pre-built denoiser already wrapping
        // micSource - built by the caller (off the UI thread: loading the
        // DeepFilterNet model takes seconds) and owned by the engine from here on.
        public void SetMicrophoneInput(ISampleProvider micSource, float micVolume, NoiseSuppressionSampleProvider noiseSuppression = null)
        {
            RemoveMicrophoneInput();

            if (noiseSuppression != null)
            {
                _noiseSuppression = noiseSuppression;
                micSource = noiseSuppression;
            }

            ISampleProvider convertedInput = ConvertToRightSampleRate(ConvertToRightChannelCount(micSource));
            _microphoneVolume = new VolumeSampleProvider(convertedInput) { Volume = micVolume };
            _microphoneMixerInput = _microphoneVolume;
            _outputMixer.AddMixerInput(_microphoneMixerInput);

            if (_outputDevice.PlaybackState != PlaybackState.Playing)
            {
                _outputDevice.Play();
            }
        }

        public void RemoveMicrophoneInput()
        {
            if (_microphoneMixerInput != null)
            {
                _outputMixer.RemoveMixerInput(_microphoneMixerInput);
                _microphoneMixerInput = null;
                _microphoneVolume = null;
            }
            if (_noiseSuppression != null)
            {
                // safe once the mixer input is gone: MixingSampleProvider.Read and
                // RemoveMixerInput share a lock, so no render-thread Read is still
                // inside the denoiser
                _noiseSuppression.Dispose();
                _noiseSuppression = null;
            }
        }

        public float MicrophoneVolume
        {
            set
            {
                if (_microphoneVolume != null)
                {
                    _microphoneVolume.Volume = value;
                }
            }
        }

        private ISampleProvider ConvertToRightChannelCount(ISampleProvider input)
        {
            if (input.WaveFormat.Channels == _mixer.WaveFormat.Channels)
            {
                return input;
            }
            if (input.WaveFormat.Channels == 1 && _mixer.WaveFormat.Channels == 2)
            {
                return new MonoToStereoSampleProvider(input);
            }
            if (input.WaveFormat.Channels == 2 && _mixer.WaveFormat.Channels == 1)
            {
                return new StereoToMonoSampleProvider(input);
            }
            throw new NotImplementedException($"Channel conversion from {input.WaveFormat.Channels} to {_mixer.WaveFormat.Channels} is not supported.");
        }

        private ISampleProvider ConvertToRightSampleRate(ISampleProvider input)
        {
            if (input.WaveFormat.SampleRate == _mixer.WaveFormat.SampleRate)
            {
                return input;
            }
            return new WdlResamplingSampleProvider(input, _mixer.WaveFormat.SampleRate);
        }

        private PausableSampleProvider AddMixerInput(ISampleProvider input, float volume, float normalizationGain)
        {
            ISampleProvider convertedInput = ConvertToRightSampleRate(ConvertToRightChannelCount(input));
            VolumeSampleProvider volumeSampleProvider = new VolumeSampleProvider(convertedInput) { Volume = AdjustVolume(volume) * normalizationGain };
            PausableSampleProvider pauseControl = new PausableSampleProvider(volumeSampleProvider);
            _mixer.AddMixerInput(pauseControl);
            return pauseControl;
        }

        private void RemoveFinishedSounds()
        {
            _activeSounds.RemoveAll(x => x.Reader.IsDisposed);
        }

        private SampleReader FindMostRecentReader()
        {
            for (int index = _activeSounds.Count - 1; index >= 0; index--)
            {
                if (!_activeSounds[index].Reader.IsDisposed)
                {
                    return _activeSounds[index].Reader;
                }
            }
            return null;
        }

        private float AdjustVolume(float volume)
        {
            return (float)((Math.Pow(_volumePowBase, volume) - 1) / (_volumePowBase - 1));
        }

        private int RevertVolume(float volume)
        {
            return (int)(Math.Log(volume * (_volumePowBase - 1) + 1) / Math.Log(_volumePowBase));
        }

        public static ICollection<string> EnumerateDevices()
        {
            List<string> devices = new List<string> { DisabledDeviceName };

            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                if (endpoints.Count != 0)
                {
                    devices.Add(DefaultDeviceName);
                    foreach (MMDevice endpoint in endpoints)
                    {
                        devices.Add(endpoint.FriendlyName);
                    }
                }
            }

            return devices;
        }

        // Returns the full friendly name of the matching device (or DefaultDeviceName),
        // upgrading names truncated by the old WaveOut enumeration; null when not found.
        public static string ResolveDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName) || deviceName == DisabledDeviceName)
            {
                return null;
            }

            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                if (deviceName == DefaultDeviceName)
                {
                    return enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) ? DefaultDeviceName : null;
                }

                MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (MMDevice endpoint in endpoints)
                {
                    if (endpoint.FriendlyName == deviceName)
                    {
                        return endpoint.FriendlyName;
                    }
                }
                if (deviceName.Length >= LegacyDeviceNameLength)
                {
                    foreach (MMDevice endpoint in endpoints)
                    {
                        if (endpoint.FriendlyName.StartsWith(deviceName, StringComparison.Ordinal))
                        {
                            return endpoint.FriendlyName;
                        }
                    }
                }
            }

            return null;
        }

        private static MMDevice FindDevice(string deviceName)
        {
            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                if (deviceName == DefaultDeviceName)
                {
                    return enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                        ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                        : null;
                }

                MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (MMDevice endpoint in endpoints)
                {
                    if (endpoint.FriendlyName == deviceName)
                    {
                        return endpoint;
                    }
                }
            }

            return null;
        }

        public void Dispose()
        {
            _outputDevice.Dispose();
            // free the native rnnoise state; the output device is stopped, so the
            // render thread can no longer be inside the denoiser
            if (_noiseSuppression != null)
            {
                _noiseSuppression.Dispose();
                _noiseSuppression = null;
            }
        }
    }
}
