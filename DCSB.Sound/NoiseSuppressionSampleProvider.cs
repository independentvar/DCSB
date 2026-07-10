using DCSB.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;

namespace DCSB.SoundPlayer
{
    // Runs the microphone stream through the selected denoiser. Both suppressors
    // want 48 kHz mono in fixed 10 ms frames, so the source is downmixed/resampled
    // here and read in whole frames (up to 10 ms of extra buffering latency, plus
    // whatever the model's own lookahead adds); the engine's usual converters then
    // take the denoised mono stream to the mixer format.
    public class NoiseSuppressionSampleProvider : ISampleProvider, IDisposable
    {
        private const int TargetSampleRate = 48000;

        private readonly ISampleProvider _source;
        private readonly INoiseSuppressor _suppressor;
        private readonly float[] _frame;
        private int _position;

        public WaveFormat WaveFormat { get; private set; }

        public NoiseSuppressionSampleProvider(ISampleProvider source, NoiseSuppressionMode mode)
        {
            if (source.WaveFormat.Channels == 2)
            {
                source = new StereoToMonoSampleProvider(source);
            }
            else if (source.WaveFormat.Channels != 1)
            {
                // same policy as AudioPlaybackEngine.ConvertToRightChannelCount
                throw new NotImplementedException($"Channel conversion from {source.WaveFormat.Channels} to 1 is not supported.");
            }
            if (source.WaveFormat.SampleRate != TargetSampleRate)
            {
                source = new WdlResamplingSampleProvider(source, TargetSampleRate);
            }
            _source = source;

            // creating the state is also the availability check: a missing or
            // broken native dll throws here, before the provider joins the graph
            switch (mode)
            {
                case NoiseSuppressionMode.Fast:
                    _suppressor = new RNNoiseSuppressor();
                    break;
                case NoiseSuppressionMode.HighQuality:
                    _suppressor = new DeepFilterNetSuppressor();
                    break;
                default:
                    throw new ArgumentException($"No suppressor for mode '{mode}'.", nameof(mode));
            }
            _frame = new float[_suppressor.FrameSize];
            _position = _frame.Length;

            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int written = 0;
            while (written < count)
            {
                if (_position == _frame.Length)
                {
                    // the microphone source is a BufferedWaveProvider chain that
                    // pads with silence, so a short read only happens at end of
                    // stream - denoise whatever arrived and pass the rest through
                    int read = ReadFrame();
                    if (read == 0)
                    {
                        break;
                    }
                    for (int i = read; i < _frame.Length; i++)
                    {
                        _frame[i] = 0f;
                    }
                    _suppressor.Process(_frame);
                    _position = 0;
                }

                int toCopy = Math.Min(count - written, _frame.Length - _position);
                Array.Copy(_frame, _position, buffer, offset + written, toCopy);
                _position += toCopy;
                written += toCopy;
            }
            return written;
        }

        private int ReadFrame()
        {
            int filled = 0;
            while (filled < _frame.Length)
            {
                int read = _source.Read(_frame, filled, _frame.Length - filled);
                if (read == 0)
                {
                    break;
                }
                filled += read;
            }
            return filled;
        }

        public void Dispose()
        {
            _suppressor.Dispose();
        }
    }
}
