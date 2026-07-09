using NAudio.Vorbis;
using NAudio.Wave;
using System;
using System.IO;

namespace DCSB.SoundPlayer
{
    // Ogg/Vorbis decoding via NVorbis. Wraps rather than subclasses
    // VorbisWaveReader and owns the FileStream itself: VorbisWaveReader's
    // file-name constructor leaks its open stream when construction fails
    // (e.g. handed an Opus file), which would keep the user's file locked
    // while the decoder chain falls through to the next codec.
    public class OggFileReader : IAudioReader
    {
        private readonly FileStream _stream;
        private readonly VorbisWaveReader _reader;

        public OggFileReader(string fileName)
        {
            _stream = File.OpenRead(fileName);
            try
            {
                _reader = new VorbisWaveReader(_stream, false);
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        public WaveFormat WaveFormat
        {
            get { return _reader.WaveFormat; }
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
            return _reader.Read(buffer, offset, count);
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }
}
