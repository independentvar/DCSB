using NAudio.Wave;
using System;

namespace DCSB.SoundPlayer
{
    public interface IAudioReader
    {
        WaveFormat WaveFormat { get; }
        long Position { get; set; }
        TimeSpan CurrentTime { get; set; }
        TimeSpan TotalTime { get; }

        int Read(float[] buffer, int offset, int count);
        void Dispose();
    }
}
