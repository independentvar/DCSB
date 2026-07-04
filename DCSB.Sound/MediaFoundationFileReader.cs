using NAudio.Wave;
using System;

namespace DCSB.SoundPlayer
{
    internal class MediaFoundationFileReader : IAudioReader
    {
        private readonly MediaFoundationReader _reader;
        private readonly ISampleProvider _sampleProvider;

        public MediaFoundationFileReader(string fileName)
        {
            _reader = new MediaFoundationReader(fileName);
            _sampleProvider = _reader.ToSampleProvider();
        }

        public WaveFormat WaveFormat
        {
            get { return _sampleProvider.WaveFormat; }
        }

        public long Position
        {
            get { return _reader.Position; }
            set { _reader.Position = value; }
        }

        public TimeSpan CurrentTime
        {
            get { return _reader.CurrentTime; }
            set { _reader.CurrentTime = value; }
        }

        public TimeSpan TotalTime
        {
            get { return _reader.TotalTime; }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            return _sampleProvider.Read(buffer, offset, count);
        }

        public void Dispose()
        {
            _reader.Dispose();
        }
    }
}
