using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;
using System;
using System.IO;

namespace DCSB.SoundPlayer
{
    // Decodes Ogg/Opus files (Discord recordings, modern meme clips) via the
    // managed Concentus decoder. Opus always runs at 48 kHz internally and the
    // decoder is created stereo - per the Opus spec a decoder renders mono
    // streams into whatever channel count the caller asks for.
    internal class OpusFileReader : IAudioReader
    {
        private const int SampleRate = 48000;
        private const int Channels = 2;

        private readonly string _fileName;
        private FileStream _stream;
        private OpusOggReadStream _oggStream;

        // decoded samples not yet handed to the caller (a packet rarely aligns
        // with the requested read size)
        private short[] _pending;
        private int _pendingOffset;

        public WaveFormat WaveFormat { get; private set; }

        public OpusFileReader(string fileName)
        {
            _fileName = fileName;
            Open();
        }

        private void Open()
        {
            _stream = File.OpenRead(_fileName);
            try
            {
                _oggStream = new OpusOggReadStream(OpusCodecFactory.CreateDecoder(SampleRate, Channels), _stream);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

                // OpusOggReadStream reports failure through LastError instead of
                // throwing; surface it as the exception the decoder chain expects
                if (_oggStream.LastError != null)
                {
                    throw new InvalidDataException($"Not an Ogg/Opus file: {_oggStream.LastError}");
                }
            }
            catch
            {
                _stream.Dispose();
                _stream = null;
                throw;
            }
        }

        public TimeSpan CurrentTime
        {
            get { return _oggStream.CurrentTime; }
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    // Concentus reads Ogg packets forward. Reopening for a rewind
                    // makes loop/replay reliable even when an input is not seekable.
                    _stream.Dispose();
                    Open();
                    _pending = null;
                    _pendingOffset = 0;
                    return;
                }
                _oggStream.SeekTo(value);
                _pending = null;
                _pendingOffset = 0;
            }
        }

        public TimeSpan TotalTime
        {
            get { return _oggStream.TotalTime; }
        }

        // SampleReader uses Position only to detect "still at the start" (loop
        // guard) and to rewind for looping, so a sample-frame position derived
        // from time is exact enough
        public long Position
        {
            get { return (long)(_oggStream.CurrentTime.TotalSeconds * SampleRate) * Channels; }
            set { CurrentTime = TimeSpan.FromSeconds((double)value / Channels / SampleRate); }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int written = 0;
            while (written < count)
            {
                if (_pending == null || _pendingOffset >= _pending.Length)
                {
                    _pending = null;
                    _pendingOffset = 0;
                    if (!_oggStream.HasNextPacket)
                    {
                        break;
                    }
                    // a corrupt packet decodes to null; skip it and continue with
                    // the rest of the file instead of cutting playback short
                    _pending = _oggStream.DecodeNextPacket();
                    continue;
                }

                int available = _pending.Length - _pendingOffset;
                int needed = count - written;
                int toCopy = Math.Min(available, needed);
                for (int i = 0; i < toCopy; i++)
                {
                    buffer[offset + written + i] = _pending[_pendingOffset + i] / 32768f;
                }
                _pendingOffset += toCopy;
                written += toCopy;
            }
            return written;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
