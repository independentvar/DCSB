using System;
using System.Collections.Generic;
using System.IO;

namespace DCSB.SoundPlayer
{
    // Kept public so the selection rules can be tested without creating an audio
    // output device.  The reader implementations themselves remain an internal
    // detail of the sound player.
    public enum AudioReaderKind
    {
        FileReader,
        Vorbis,
        Opus,
        MediaFoundation
    }

    /// <summary>
    /// Selects the most appropriate decoder before opening an audio file.  A
    /// fallback is still important for incorrectly named files and for Windows
    /// installations missing a Media Foundation or ACM codec, but it is no
    /// longer the normal method of codec detection.
    /// </summary>
    public static class AudioReaderFactory
    {
        private static readonly HashSet<string> FileReaderExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".wav", ".mp3", ".aif", ".aiff"
            };

        public static IAudioReader CreateReader(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException($"Sound file not found: {fileName}", fileName);
            }

            AudioReaderKind preferred = GetReaderKind(fileName);
            List<AudioReaderKind> candidates = new List<AudioReaderKind> { preferred };

            // These are deliberately fallback-only.  They preserve support for
            // content behind a misleading extension (and codec availability that
            // differs between Windows installations) without using exceptions to
            // choose every normal reader.
            AddCandidate(candidates, AudioReaderKind.FileReader);
            AddCandidate(candidates, AudioReaderKind.Vorbis);
            AddCandidate(candidates, AudioReaderKind.Opus);
            AddCandidate(candidates, AudioReaderKind.MediaFoundation);

            List<Exception> failures = new List<Exception>();
            foreach (AudioReaderKind candidate in candidates)
            {
                try
                {
                    return Open(candidate, fileName);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }

            throw new AggregateException(
                $"'{Path.GetFileName(fileName)}' could not be decoded. Supported formats: WAV, MP3, OGG (Vorbis/Opus), FLAC, AAC/M4A, WMA, AIFF.",
                failures);
        }

        public static AudioReaderKind GetReaderKind(string fileName)
        {
            OggCodec oggCodec = SniffOggCodec(fileName);
            if (oggCodec == OggCodec.Opus)
            {
                return AudioReaderKind.Opus;
            }
            if (oggCodec == OggCodec.Vorbis)
            {
                return AudioReaderKind.Vorbis;
            }

            string extension = Path.GetExtension(fileName);
            return FileReaderExtensions.Contains(extension)
                ? AudioReaderKind.FileReader
                : AudioReaderKind.MediaFoundation;
        }

        private static void AddCandidate(List<AudioReaderKind> candidates, AudioReaderKind candidate)
        {
            if (!candidates.Contains(candidate))
            {
                candidates.Add(candidate);
            }
        }

        private static IAudioReader Open(AudioReaderKind readerKind, string fileName)
        {
            switch (readerKind)
            {
                case AudioReaderKind.FileReader:
                    return new FileReader(fileName);
                case AudioReaderKind.Vorbis:
                    return new OggFileReader(fileName);
                case AudioReaderKind.Opus:
                    return new OpusFileReader(fileName);
                default:
                    return new MediaFoundationFileReader(fileName);
            }
        }

        // Ogg's first packet starts in its first page.  Read the lacing values
        // rather than searching a byte prefix: that avoids mistaking codec text
        // in an unrelated file for an Ogg stream and works with comments/pages
        // following the identification packet as well.
        private static OggCodec SniffOggCodec(string fileName)
        {
            try
            {
                using (FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] header = new byte[27];
                    if (!ReadExactly(stream, header, header.Length)
                        || header[0] != (byte)'O' || header[1] != (byte)'g'
                        || header[2] != (byte)'g' || header[3] != (byte)'S')
                    {
                        return OggCodec.Unknown;
                    }

                    int segmentCount = header[26];
                    byte[] lacingValues = new byte[segmentCount];
                    if (!ReadExactly(stream, lacingValues, segmentCount))
                    {
                        return OggCodec.Unknown;
                    }

                    using (MemoryStream firstPacket = new MemoryStream())
                    {
                        for (int i = 0; i < lacingValues.Length; i++)
                        {
                            int length = lacingValues[i];
                            byte[] segment = new byte[length];
                            if (!ReadExactly(stream, segment, length))
                            {
                                return OggCodec.Unknown;
                            }
                            firstPacket.Write(segment, 0, segment.Length);
                            if (length < 255)
                            {
                                return IdentifyOggPacket(firstPacket.GetBuffer(), (int)firstPacket.Length);
                            }
                        }
                    }
                }
            }
            catch (IOException)
            {
                // Reader creation will give the user the useful error (including
                // access-denied details); selection simply falls back by extension.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return OggCodec.Unknown;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                {
                    return false;
                }
                offset += read;
            }
            return true;
        }

        private static OggCodec IdentifyOggPacket(byte[] packet, int length)
        {
            if (length >= 8
                && packet[0] == (byte)'O' && packet[1] == (byte)'p'
                && packet[2] == (byte)'u' && packet[3] == (byte)'s'
                && packet[4] == (byte)'H' && packet[5] == (byte)'e'
                && packet[6] == (byte)'a' && packet[7] == (byte)'d')
            {
                return OggCodec.Opus;
            }

            if (length >= 7 && packet[0] == 1
                && packet[1] == (byte)'v' && packet[2] == (byte)'o'
                && packet[3] == (byte)'r' && packet[4] == (byte)'b'
                && packet[5] == (byte)'i' && packet[6] == (byte)'s')
            {
                return OggCodec.Vorbis;
            }

            return OggCodec.Unknown;
        }

        private enum OggCodec
        {
            Unknown,
            Vorbis,
            Opus
        }
    }
}
