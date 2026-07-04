using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DCSB.SoundPlayer
{
    public class AudioPlaybackEngine
    {
        public const string DisabledDeviceName = "Disabled";
        public const string DefaultDeviceName = "Default Output Device";

        // WaveOut device names (used by older versions) were truncated to 31 characters
        private const int LegacyDeviceNameLength = 31;

        private readonly WasapiOut _outputDevice;
        private readonly MixingSampleProvider _mixer;
        private readonly VolumeSampleProvider _masterVolume;

        private int _volumePowBase = 100;

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
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(mixFormat.SampleRate, channels)) { ReadFully = true };
            _masterVolume = new VolumeSampleProvider(_mixer);

            _outputDevice = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            _outputDevice.Init(_masterVolume);
            _outputDevice.Play();
        }

        public void PlaySound(string fileName, float volume, bool loop)
        {
            if (!Overlap)
            {
                Stop();
            }

            IAudioReader input;
            try
            {
                input = new FileReader(fileName);
            }
            catch (COMException)
            {
                input = new OggFileReader(fileName);
            }
            catch (MmException)
            {
                // No ACM MP3 codec installed (e.g. Windows N editions) - decode via Media Foundation instead
                input = new MediaFoundationFileReader(fileName);
            }

            SampleReader reader = new SampleReader(input, loop);
            AddMixerInput(reader, volume);

            if (_outputDevice.PlaybackState != PlaybackState.Playing)
            {
                _outputDevice.Play();
            }
        }

        public void Pause()
        {
            if (_outputDevice.PlaybackState == PlaybackState.Playing)
            {
                _outputDevice.Pause();
            }
        }

        public void Continue()
        {
            if (_outputDevice.PlaybackState == PlaybackState.Paused)
            {
                _outputDevice.Play();
            }
        }

        public void Stop()
        {
            _mixer.RemoveAllMixerInputs();
            _outputDevice.Stop();
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

        private void AddMixerInput(ISampleProvider input, float volume)
        {
            ISampleProvider convertedInput = ConvertToRightSampleRate(ConvertToRightChannelCount(input));
            VolumeSampleProvider volumeSampleProvider = new VolumeSampleProvider(convertedInput) { Volume = AdjustVolume(volume) };
            _mixer.AddMixerInput(volumeSampleProvider);
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
        }
    }
}
