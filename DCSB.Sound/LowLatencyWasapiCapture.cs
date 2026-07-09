using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DCSB.SoundPlayer
{
    // Capture-side counterpart of LowLatencyWasapiOut: a WASAPI shared-mode
    // recorder initialized through IAudioClient3 at the engine's minimum period
    // (typically 2.7-10 ms), so microphone packets reach the mixer several times
    // faster than WasapiCapture's buffer allows. Records in 32-bit float at the
    // endpoint's mix rate - the same data WasapiCapture delivers, so consumers
    // (BufferedWaveProvider, the level meter) see an identical stream. Unlike the
    // render class this has no internal fallback: any failure throws from the
    // constructor and the caller uses a stock WasapiCapture instead.
    public class LowLatencyWasapiCapture : IWaveIn
    {
        private const uint StreamFlagsEventCallback = 0x00040000; // AUDCLNT_STREAMFLAGS_EVENTCALLBACK

        private readonly AudioClient _audioClient;
        private readonly EventWaitHandle _frameEvent;
        private Thread _captureThread;
        private volatile bool _capturing;

        public event EventHandler<WaveInEventArgs> DataAvailable;
        public event EventHandler<StoppedEventArgs> RecordingStopped;

        public WaveFormat WaveFormat
        {
            get;
            set; // IWaveIn requires the setter; the format is fixed at construction
        }

        // stream period granted by IAudioClient3, in frames
        public int PeriodInFrames { get; private set; }

        public LowLatencyWasapiCapture(MMDevice device)
        {
            _audioClient = device.AudioClient;
            try
            {
                WaveFormat mixFormat = _audioClient.MixFormat;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mixFormat.SampleRate, mixFormat.Channels);

                IntPtr formatPtr = Marshal.AllocHGlobal(18 + WaveFormat.ExtraSize);
                try
                {
                    Marshal.StructureToPtr(WaveFormat, formatPtr, false);
                    IAudioClient3 client3 = (IAudioClient3)AudioClient3Interop.GetComInterface(_audioClient);
                    uint defaultPeriod, fundamental, minPeriod, maxPeriod;
                    client3.GetSharedModeEnginePeriod(formatPtr, out defaultPeriod, out fundamental, out minPeriod, out maxPeriod);
                    client3.InitializeSharedAudioStream(StreamFlagsEventCallback, minPeriod, formatPtr, IntPtr.Zero);
                    PeriodInFrames = (int)minPeriod;
                }
                finally
                {
                    Marshal.FreeHGlobal(formatPtr);
                }

                _frameEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
                _audioClient.SetEventHandle(_frameEvent.SafeWaitHandle.DangerousGetHandle());
            }
            catch (Exception)
            {
                _audioClient.Dispose();
                if (_frameEvent != null)
                {
                    _frameEvent.Dispose();
                }
                throw;
            }
        }

        public void StartRecording()
        {
            if (_capturing)
            {
                return;
            }
            _capturing = true;
            _captureThread = new Thread(CaptureThread)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "LowLatencyWasapiCapture"
            };
            _captureThread.Start();
        }

        public void StopRecording()
        {
            if (!_capturing)
            {
                return;
            }
            _capturing = false;
            _frameEvent.Set();
            if (_captureThread != null && _captureThread != Thread.CurrentThread)
            {
                _captureThread.Join();
            }
            _captureThread = null;
        }

        private void CaptureThread()
        {
            Exception error = null;
            byte[] recordBuffer = new byte[Math.Max(_audioClient.BufferSize, PeriodInFrames * 4) * WaveFormat.BlockAlign];
            try
            {
                AudioCaptureClient captureClient = _audioClient.AudioCaptureClient;
                _audioClient.Start();
                try
                {
                    while (_capturing)
                    {
                        if (!_frameEvent.WaitOne(100))
                        {
                            continue; // device stall; keep waiting, StopRecording sets the event
                        }
                        int packetFrames = captureClient.GetNextPacketSize();
                        while (packetFrames > 0 && _capturing)
                        {
                            int framesRead;
                            AudioClientBufferFlags flags;
                            IntPtr packet = captureClient.GetBuffer(out framesRead, out flags);
                            int bytes = framesRead * WaveFormat.BlockAlign;
                            if (bytes > recordBuffer.Length)
                            {
                                recordBuffer = new byte[bytes];
                            }
                            if ((flags & AudioClientBufferFlags.Silent) == AudioClientBufferFlags.Silent)
                            {
                                Array.Clear(recordBuffer, 0, bytes);
                            }
                            else
                            {
                                Marshal.Copy(packet, recordBuffer, 0, bytes);
                            }
                            captureClient.ReleaseBuffer(framesRead);

                            EventHandler<WaveInEventArgs> handler = DataAvailable;
                            if (handler != null && bytes > 0)
                            {
                                handler(this, new WaveInEventArgs(recordBuffer, bytes));
                            }
                            packetFrames = captureClient.GetNextPacketSize();
                        }
                    }
                }
                finally
                {
                    _audioClient.Stop();
                    _audioClient.Reset();
                }
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                _capturing = false;
                EventHandler<StoppedEventArgs> handler = RecordingStopped;
                if (handler != null)
                {
                    handler(this, new StoppedEventArgs(error));
                }
            }
        }

        public void Dispose()
        {
            StopRecording();
            _audioClient.Dispose();
            if (_frameEvent != null)
            {
                _frameEvent.Dispose();
            }
        }
    }
}
