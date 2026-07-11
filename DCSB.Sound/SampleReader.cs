using NAudio.Wave;
using System;

namespace DCSB.SoundPlayer
{
    public class SampleReader : ISampleProvider
    {
        private volatile bool _isDisposed;
        public bool IsDisposed
        {
            get { return _isDisposed; }
            protected set { _isDisposed = value; }
        }

        private readonly IAudioReader _reader;
        private readonly object _readerLock = new object();
        private bool _loop;

        public WaveFormat WaveFormat { get; private set; }

        public TimeSpan CurrentTime
        {
            get
            {
                lock (_readerLock)
                {
                    return IsDisposed ? TimeSpan.Zero : _reader.CurrentTime;
                }
            }
            set
            {
                lock (_readerLock)
                {
                    if (!IsDisposed)
                    {
                        _reader.CurrentTime = value;
                    }
                }
            }
        }

        public TimeSpan TotalTime
        {
            get
            {
                lock (_readerLock)
                {
                    return IsDisposed ? TimeSpan.Zero : _reader.TotalTime;
                }
            }
        }

        public SampleReader(IAudioReader reader, bool loop)
        {
            _reader = reader;
            WaveFormat = reader.WaveFormat;
            _loop = loop;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_readerLock)
            {
                if (IsDisposed)
                    return 0;

                int read = 0;
                while (read < count)
                {
                    int bytesRead = _reader.Read(buffer, offset + read, count - read);
                    if (bytesRead == 0)
                    {
                        if (_reader.Position == 0 || !_loop)
                        {
                            break;
                        }
                        _reader.Position = 0;
                    }
                    read += bytesRead;
                }

                if (read < count)
                {
                    Dispose();
                }
                return read;
            }
        }

        public void Dispose()
        {
            lock (_readerLock)
            {
                if (IsDisposed)
                {
                    return;
                }
                // Publish the disposed state before entering the decoder's Dispose;
                // position readers that arrive next will return zero without touching
                // NAudio's already torn-down WaveStream internals.
                IsDisposed = true;
                _reader.Dispose();
            }
        }
    }
}
