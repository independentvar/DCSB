using NAudio;
using NAudio.Wave;
using System;
using System.Runtime.InteropServices;

namespace DCSB.SoundPlayer
{
    public static class AudioMetadata
    {
        // The same decoder fallback chain playback uses (see
        // AudioPlaybackEngine.PlaySound); the caller owns disposing the reader.
        public static IAudioReader OpenReader(string fileName)
        {
            try
            {
                return new FileReader(fileName);
            }
            catch (COMException)
            {
                return new OggFileReader(fileName);
            }
            catch (MmException)
            {
                return new MediaFoundationFileReader(fileName);
            }
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
