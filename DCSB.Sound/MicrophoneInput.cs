using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace DCSB.SoundPlayer
{
    // Captures the user's real microphone (WASAPI shared mode) and exposes it as an
    // ISampleProvider, so an AudioPlaybackEngine can mix the voice into its output.
    // This replaces the Windows "Listen to this device" workaround from the tutorial:
    // the voice reaches the virtual cable inside the app, with no system state changed.
    public class MicrophoneInput : IDisposable
    {
        public const string DisabledDeviceName = "Disabled";
        public const string DefaultDeviceName = "Default Input Device";

        // buffer for the stock WasapiCapture used when LowLatencyWasapiCapture
        // (IAudioClient3, engine-minimum period) is unavailable; small, to keep
        // the voice latency low even on the fallback path
        private const int CaptureBufferMilliseconds = 10;

        // upper bound on how much captured audio may pile up when the render side
        // stalls; overflow discards new data (a short glitch) instead of letting the
        // voice drift ever further behind or the buffer grow without limit
        private const int MaxBufferedMilliseconds = 250;

        // if the backlog between capture and render exceeds this, drop it and
        // resume live: a render stall (device rebuild, system hiccup) or capture/
        // render clock drift must show up as a brief glitch, not as voice latency
        // that ratchets up and stays for the rest of the session
        private const int LatencyResetMilliseconds = 80;

        private readonly IWaveIn _capture;
        private readonly BufferedWaveProvider _buffer;
        private bool _disposed;

        public ISampleProvider SampleProvider { get; private set; }

        // peak amplitude (0..1) of the most recent capture buffer; raised on the
        // capture thread, ~40 times per second - drives the input level meter
        public event EventHandler<float> LevelChanged;

        // raised when capturing stops because of an error (e.g. the microphone was
        // unplugged); not raised on normal disposal
        public event EventHandler<Exception> CaptureFailed;

        public MicrophoneInput(string deviceName)
        {
            MMDevice device = FindDevice(deviceName);
            if (device == null)
            {
                throw new ArgumentException($"Input device '{deviceName}' was not found.", nameof(deviceName));
            }

            try
            {
                _capture = new LowLatencyWasapiCapture(device);
            }
            catch (Exception)
            {
                // IAudioClient3 unavailable (pre-Win10, driver refusal, NAudio internals changed)
                _capture = new WasapiCapture(device, true, CaptureBufferMilliseconds);
            }
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(MaxBufferedMilliseconds),
                DiscardOnBufferOverflow = true
            };
            SampleProvider = _buffer.ToSampleProvider();

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }

        // Drops any captured audio that accumulated while nothing was reading the
        // buffer (e.g. while the secondary output was being rebuilt), so playback
        // resumes live instead of replaying a stale backlog.
        public void Flush()
        {
            _buffer.ClearBuffer();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_buffer.BufferedDuration.TotalMilliseconds > LatencyResetMilliseconds)
            {
                _buffer.ClearBuffer();
            }
            _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            if (LevelChanged != null)
            {
                LevelChanged(this, GetPeak(e.Buffer, e.BytesRecorded));
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (!_disposed && e.Exception != null && CaptureFailed != null)
            {
                CaptureFailed(this, e.Exception);
            }
        }

        // WASAPI shared-mode capture delivers 32-bit float (the endpoint mix format);
        // 16-bit PCM is handled for completeness, anything else just reports silence
        private float GetPeak(byte[] buffer, int bytesRecorded)
        {
            float peak = 0;
            if (_capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && _capture.WaveFormat.BitsPerSample == 32)
            {
                for (int i = 0; i + 4 <= bytesRecorded; i += 4)
                {
                    float sample = Math.Abs(BitConverter.ToSingle(buffer, i));
                    if (sample > peak) peak = sample;
                }
            }
            else if (_capture.WaveFormat.Encoding == WaveFormatEncoding.Pcm && _capture.WaveFormat.BitsPerSample == 16)
            {
                for (int i = 0; i + 2 <= bytesRecorded; i += 2)
                {
                    float sample = Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f);
                    if (sample > peak) peak = sample;
                }
            }
            return Math.Min(peak, 1f);
        }

        public static ICollection<string> EnumerateDevices()
        {
            List<string> devices = new List<string> { DisabledDeviceName };

            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
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

        // Returns the friendly name of the matching capture device (or
        // DefaultDeviceName); null when disabled or not found - a chosen device that
        // is currently missing must not be silently replaced with another microphone.
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
                    return enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) ? DefaultDeviceName : null;
                }

                MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (MMDevice endpoint in endpoints)
                {
                    if (endpoint.FriendlyName == deviceName)
                    {
                        return endpoint.FriendlyName;
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
                    return enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                        ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                        : null;
                }

                MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
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
            _disposed = true;
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
        }
    }
}
