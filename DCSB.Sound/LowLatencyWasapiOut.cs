using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace DCSB.SoundPlayer
{
    // WASAPI shared-mode output that initializes through IAudioClient3 at the audio
    // engine's minimum period (typically 2.7-10 ms) instead of WasapiOut's fixed
    // buffer, cutting the microphone passthrough latency to near the OS floor while
    // other apps keep using the device. NAudio (2.3.0) does not expose IAudioClient3,
    // so the underlying COM object is queried for it directly; everything else
    // (event handle, render client, start/stop) still goes through NAudio's wrapper.
    // When IAudioClient3 is unavailable (pre-Win10, driver refusal, NAudio internals
    // changed) Init falls back to a plain shared-mode stream on a fresh client.
    public class LowLatencyWasapiOut : IWavePlayer
    {
        private const int FallbackLatencyMilliseconds = 30;
        private const uint StreamFlagsEventCallback = 0x00040000; // AUDCLNT_STREAMFLAGS_EVENTCALLBACK

        private readonly MMDevice _device;
        private AudioClient _audioClient;
        private IWaveProvider _sourceProvider;
        private EventWaitHandle _frameEvent;
        private Thread _playThread;
        private volatile PlaybackState _playbackState;
        private int _bufferFrameCount;
        private byte[] _readBuffer;

        public event EventHandler<StoppedEventArgs> PlaybackStopped;

        public PlaybackState PlaybackState
        {
            get { return _playbackState; }
        }

        public WaveFormat OutputWaveFormat { get; private set; }

        // stream period actually granted by IAudioClient3, in frames; 0 on the
        // plain shared-mode fallback path
        public int PeriodInFrames { get; private set; }

        // required by IWavePlayer; the engine controls loudness with sample
        // providers instead, so this intentionally adjusts nothing
        public float Volume { get; set; } = 1.0f;

        public LowLatencyWasapiOut(MMDevice device)
        {
            _device = device;
        }

        public void Init(IWaveProvider waveProvider)
        {
            if (_sourceProvider != null)
            {
                throw new InvalidOperationException("Already initialized");
            }
            _sourceProvider = waveProvider;
            OutputWaveFormat = waveProvider.WaveFormat;
            _audioClient = _device.AudioClient;

            int formatSize = 18 + waveProvider.WaveFormat.ExtraSize;
            IntPtr formatPtr = Marshal.AllocHGlobal(formatSize);
            try
            {
                Marshal.StructureToPtr(waveProvider.WaveFormat, formatPtr, false);
                try
                {
                    IAudioClient3 client3 = (IAudioClient3)AudioClient3Interop.GetComInterface(_audioClient);
                    uint defaultPeriod, fundamental, minPeriod, maxPeriod;
                    client3.GetSharedModeEnginePeriod(formatPtr, out defaultPeriod, out fundamental, out minPeriod, out maxPeriod);
                    client3.InitializeSharedAudioStream(StreamFlagsEventCallback, minPeriod, formatPtr, IntPtr.Zero);
                    PeriodInFrames = (int)minPeriod;
                }
                catch (Exception)
                {
                    // the failed client may be in an unusable state - activate a fresh one
                    _audioClient.Dispose();
                    _audioClient = _device.AudioClient;
                    _audioClient.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback,
                        FallbackLatencyMilliseconds * 10000L, 0, waveProvider.WaveFormat, Guid.Empty);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(formatPtr);
            }

            _frameEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            _audioClient.SetEventHandle(_frameEvent.SafeWaitHandle.DangerousGetHandle());
            _bufferFrameCount = _audioClient.BufferSize;
            _readBuffer = new byte[_bufferFrameCount * waveProvider.WaveFormat.BlockAlign];
        }

        public void Play()
        {
            if (_playbackState == PlaybackState.Playing)
            {
                return;
            }
            if (_playbackState == PlaybackState.Paused)
            {
                _playbackState = PlaybackState.Playing;
                return;
            }
            _playbackState = PlaybackState.Playing;
            _playThread = new Thread(PlaybackThread)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "LowLatencyWasapiOut"
            };
            _playThread.Start();
        }

        // while paused the render loop keeps running but writes silence, so the
        // stream position and event cadence stay intact (mirrors what the engine
        // needs: it pauses the sound branch, not the device)
        public void Pause()
        {
            if (_playbackState == PlaybackState.Playing)
            {
                _playbackState = PlaybackState.Paused;
            }
        }

        public void Stop()
        {
            if (_playbackState == PlaybackState.Stopped)
            {
                return;
            }
            _playbackState = PlaybackState.Stopped;
            _frameEvent.Set();
            if (_playThread != null && _playThread != Thread.CurrentThread)
            {
                _playThread.Join();
            }
            _playThread = null;
        }

        private void PlaybackThread()
        {
            Exception error = null;
            try
            {
                AudioRenderClient renderClient = _audioClient.AudioRenderClient;
                FillBuffer(renderClient, _bufferFrameCount);
                _audioClient.Start();
                try
                {
                    while (_playbackState != PlaybackState.Stopped)
                    {
                        if (!_frameEvent.WaitOne(100))
                        {
                            continue; // spurious stall; keep waiting, Stop() sets the event
                        }
                        if (_playbackState == PlaybackState.Stopped)
                        {
                            break;
                        }
                        int frames = _bufferFrameCount - _audioClient.CurrentPadding;
                        if (frames > 0)
                        {
                            FillBuffer(renderClient, frames);
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
                _playbackState = PlaybackState.Stopped;
                EventHandler<StoppedEventArgs> handler = PlaybackStopped;
                if (handler != null)
                {
                    handler(this, new StoppedEventArgs(error));
                }
            }
        }

        private void FillBuffer(AudioRenderClient renderClient, int frameCount)
        {
            int bytesNeeded = frameCount * OutputWaveFormat.BlockAlign;
            int read = _playbackState == PlaybackState.Paused
                ? 0
                : _sourceProvider.Read(_readBuffer, 0, bytesNeeded);
            if (read < bytesNeeded)
            {
                Array.Clear(_readBuffer, read, bytesNeeded - read);
            }
            IntPtr buffer = renderClient.GetBuffer(frameCount);
            Marshal.Copy(_readBuffer, 0, buffer, bytesNeeded);
            renderClient.ReleaseBuffer(frameCount, AudioClientBufferFlags.None);
        }

        public void Dispose()
        {
            Stop();
            if (_audioClient != null)
            {
                _audioClient.Dispose();
                _audioClient = null;
            }
            if (_frameEvent != null)
            {
                _frameEvent.Dispose();
                _frameEvent = null;
            }
        }
    }

    // NAudio (2.3.0) keeps the raw IAudioClient COM object in a private field and
    // offers no IAudioClient3 surface; digging it out lets the low-latency classes
    // QueryInterface for IAudioClient3 while reusing NAudio's wrapper for the rest.
    internal static class AudioClient3Interop
    {
        // returns the COM RCW; cast to IAudioClient3 performs the QueryInterface
        // (InvalidCastException when the platform doesn't support it)
        public static object GetComInterface(AudioClient audioClient)
        {
            FieldInfo field = typeof(AudioClient).GetField("audioClientInterface", BindingFlags.NonPublic | BindingFlags.Instance);
            object comObject = field != null ? field.GetValue(audioClient) : null;
            if (comObject == null)
            {
                throw new NotSupportedException("NAudio's audioClientInterface field was not found.");
            }
            return comObject;
        }
    }

    // Vtable of Windows' IAudioClient3 (inherits IAudioClient2 : IAudioClient); the
    // slot order and count must match exactly, but only the last three methods are
    // ever called through this interface - the earlier ones are slot placeholders.
    [ComImport]
    [Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient3
    {
        // IAudioClient
        void Initialize(AudioClientShareMode shareMode, AudioClientStreamFlags streamFlags, long bufferDuration, long devicePeriod, IntPtr waveFormat, IntPtr audioSessionGuid);
        int GetBufferSize();
        long GetStreamLatency();
        int GetCurrentPadding();
        [PreserveSig]
        int IsFormatSupported(AudioClientShareMode shareMode, IntPtr format, IntPtr closestMatchFormat);
        IntPtr GetMixFormat();
        void GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr eventHandle);
        IntPtr GetService(ref Guid interfaceId);
        // IAudioClient2
        void IsOffloadCapable(int category, out bool offloadCapable);
        void SetClientProperties(IntPtr properties);
        void GetBufferSizeLimits(IntPtr format, bool eventDriven, out long minBufferDuration, out long maxBufferDuration);
        // IAudioClient3
        void GetSharedModeEnginePeriod(IntPtr format, out uint defaultPeriodInFrames, out uint fundamentalPeriodInFrames, out uint minPeriodInFrames, out uint maxPeriodInFrames);
        void GetCurrentSharedModeEnginePeriod(out IntPtr format, out uint currentPeriodInFrames);
        void InitializeSharedAudioStream(uint streamFlags, uint periodInFrames, IntPtr format, IntPtr audioSessionGuid);
    }
}
