using System;

namespace DCSB.SoundPlayer
{
    public static class AudioMetadata
    {
        // The caller owns disposing the returned reader. Reader selection lives
        // in AudioReaderFactory so playback, duration and loudness always use
        // identical codec rules.
        public static IAudioReader OpenReader(string fileName)
        {
            return AudioReaderFactory.CreateReader(fileName);
        }

        // Opens the file just long enough to read its length, then disposes the
        // reader. Returns null when the file can't be read.
        public static TimeSpan? GetDuration(string fileName)
        {
            IAudioReader reader = null;
            try
            {
                reader = OpenReader(fileName);
                return reader.TotalTime;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (reader != null) reader.Dispose();
            }
        }
    }
}
