using NAudio;
using NAudio.Wave;
using System;
using System.Runtime.InteropServices;

namespace DCSB.SoundPlayer
{
    public static class AudioMetadata
    {
        // Opens the file just long enough to read its length, using the same
        // decoder fallback chain as playback (see AudioPlaybackEngine.PlaySound),
        // then disposes the reader. Returns null when the file can't be read.
        public static TimeSpan? GetDuration(string fileName)
        {
            IAudioReader reader = null;
            try
            {
                try
                {
                    reader = new FileReader(fileName);
                }
                catch (COMException)
                {
                    reader = new OggFileReader(fileName);
                }
                catch (MmException)
                {
                    reader = new MediaFoundationFileReader(fileName);
                }
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
