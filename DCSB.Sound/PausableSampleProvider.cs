using NAudio.Wave;
using System;

namespace DCSB.SoundPlayer
{
    // Passes its source through until paused; while paused it produces silence
    // without reading the source, so the sounds behind it hold their position.
    // This lets an engine pause its sounds while other mixer inputs (the
    // microphone) keep flowing, which pausing the output device could not do.
    public class PausableSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public bool IsPaused { get; set; }

        public WaveFormat WaveFormat
        {
            get { return _source.WaveFormat; }
        }

        public PausableSampleProvider(ISampleProvider source)
        {
            _source = source;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (IsPaused)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            return _source.Read(buffer, offset, count);
        }
    }
}
