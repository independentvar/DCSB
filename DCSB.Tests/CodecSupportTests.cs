using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using DCSB.SoundPlayer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace DCSB.Tests
{
    // Round-trip tests for the decoder chain in AudioMetadata.OpenReader. Opus
    // files are produced with Concentus's own encoder and AAC with the Windows
    // Media Foundation encoder, so every asset is generated - no binary fixtures.
    [TestClass]
    public class CodecSupportTests
    {
        private static string TempFile(string extension)
        {
            return Path.Combine(Path.GetTempPath(), "dcsb-codec-" + Guid.NewGuid().ToString("N") + extension);
        }

        // Small checked-in Ogg identification-page fixtures exercise the actual
        // lacing-table sniffing logic without requiring large audio assets.
        private static string CopyFixture(string fixtureName, string extension)
        {
            string path = TempFile(extension);
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
            File.WriteAllBytes(path, Convert.FromBase64String(File.ReadAllText(fixture).Trim()));
            return path;
        }

        [TestMethod]
        public void FactorySelectsOpusFromOggPacketRegardlessOfExtension()
        {
            string file = CopyFixture("opus-identification.ogg.base64", ".ogg");
            try
            {
                Assert.AreEqual(AudioReaderKind.Opus, AudioReaderFactory.GetReaderKind(file));
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void FactorySelectsVorbisFromOggPacketRegardlessOfExtension()
        {
            string file = CopyFixture("vorbis-identification.ogg.base64", ".opus");
            try
            {
                Assert.AreEqual(AudioReaderKind.Vorbis, AudioReaderFactory.GetReaderKind(file));
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void FactoryUsesExtensionWhenContentIsNotOgg()
        {
            string wav = TempFile(".wav");
            string flac = TempFile(".flac");
            File.WriteAllBytes(wav, new byte[] { 0 });
            File.WriteAllBytes(flac, new byte[] { 0 });
            try
            {
                Assert.AreEqual(AudioReaderKind.FileReader, AudioReaderFactory.GetReaderKind(wav));
                Assert.AreEqual(AudioReaderKind.MediaFoundation, AudioReaderFactory.GetReaderKind(flac));
            }
            finally
            {
                File.Delete(wav);
                File.Delete(flac);
            }
        }

        // 2 s 440 Hz tone, encoded as Ogg/Opus
        private static string WriteOpusFile(string extension, int channels)
        {
            const int rate = 48000;
            int frames = rate * 2;
            short[] pcm = new short[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                short value = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5 * short.MaxValue);
                for (int c = 0; c < channels; c++) pcm[i * channels + c] = value;
            }

            string path = TempFile(extension);
            using (FileStream stream = File.Create(path))
            {
                IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(rate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
                OpusOggWriteStream writer = new OpusOggWriteStream(encoder, stream, null, rate);
                writer.WriteSamples(pcm, 0, pcm.Length);
                writer.Finish();
            }
            return path;
        }

        private static double ReadPeakAndDuration(string file, out TimeSpan duration)
        {
            IAudioReader reader = AudioMetadata.OpenReader(file);
            try
            {
                duration = reader.TotalTime;
                float[] buffer = new float[48000];
                double peak = 0;
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        double sample = Math.Abs(buffer[i]);
                        if (sample > peak) peak = sample;
                    }
                }
                return peak;
            }
            finally
            {
                reader.Dispose();
            }
        }

        [TestMethod]
        public void OpusFileDecodes()
        {
            string file = WriteOpusFile(".opus", 2);
            try
            {
                TimeSpan duration;
                double peak = ReadPeakAndDuration(file, out duration);
                Assert.IsTrue(peak > 0.4 && peak < 0.6, $"decoded tone peak should be ~0.5, was {peak:F3}");
                Assert.AreEqual(2.0, duration.TotalSeconds, 0.2, $"duration should be ~2 s, was {duration}");
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void MonoOpusFileDecodes()
        {
            // the reader always decodes to stereo; a mono stream must still work
            string file = WriteOpusFile(".opus", 1);
            try
            {
                TimeSpan duration;
                double peak = ReadPeakAndDuration(file, out duration);
                Assert.IsTrue(peak > 0.4 && peak < 0.6, $"decoded mono tone peak should be ~0.5, was {peak:F3}");
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void OpusReaderRewindsCleanlyForLooping()
        {
            string file = WriteOpusFile(".opus", 2);
            IAudioReader reader = null;
            try
            {
                reader = AudioReaderFactory.CreateReader(file);
                float[] buffer = new float[4096];
                Assert.IsTrue(reader.Read(buffer, 0, buffer.Length) > 0, "the first pass should contain audio");

                while (reader.Read(buffer, 0, buffer.Length) > 0)
                {
                }

                reader.Position = 0;
                Assert.IsTrue(reader.Read(buffer, 0, buffer.Length) > 0, "rewinding for a loop should reopen and decode the clip");
            }
            finally
            {
                if (reader != null) reader.Dispose();
                File.Delete(file);
            }
        }

        [TestMethod]
        public void OggExtensionWithOpusContentDecodes()
        {
            // Discord and yt-dlp commonly produce Opus in files named .ogg; the
            // Vorbis decoder rejects them and the chain must fall through to Opus
            string file = WriteOpusFile(".ogg", 2);
            try
            {
                TimeSpan duration;
                double peak = ReadPeakAndDuration(file, out duration);
                Assert.IsTrue(peak > 0.4, $"opus-in-.ogg should decode (peak {peak:F3})");
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void MisleadingWavExtensionStillDecodes()
        {
            // content that doesn't match its extension must not error out as long
            // as some decoder in the chain can read it
            string file = WriteOpusFile(".wav", 2);
            try
            {
                TimeSpan duration;
                double peak = ReadPeakAndDuration(file, out duration);
                Assert.IsTrue(peak > 0.4, $"opus content named .wav should decode (peak {peak:F3})");
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void AacFileDecodesThroughMediaFoundation()
        {
            // encode a wav to AAC with the OS encoder, then decode it back
            string wav = TempFile(".wav");
            string m4a = TempFile(".m4a");
            const int rate = 44100;
            using (NAudio.Wave.WaveFileWriter writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(rate, 16, 2)))
            {
                for (int i = 0; i < rate * 2; i++)
                {
                    float value = (float)(Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5);
                    writer.WriteSample(value); // left
                    writer.WriteSample(value); // right
                }
            }
            try
            {
                NAudio.MediaFoundation.MediaFoundationApi.Startup();
                using (NAudio.Wave.WaveFileReader source = new NAudio.Wave.WaveFileReader(wav))
                {
                    NAudio.Wave.MediaFoundationEncoder.EncodeToAac(source, m4a);
                }

                TimeSpan duration;
                double peak = ReadPeakAndDuration(m4a, out duration);
                Assert.IsTrue(peak > 0.4 && peak < 0.6, $"AAC round-trip peak should be ~0.5, was {peak:F3}");
                Assert.AreEqual(2.0, duration.TotalSeconds, 0.3, $"duration should be ~2 s, was {duration}");
            }
            finally
            {
                File.Delete(wav);
                File.Delete(m4a);
            }
        }

        [TestMethod]
        public void UndecodableFileReportsSupportedFormats()
        {
            string file = TempFile(".mp3");
            File.WriteAllBytes(file, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33 });
            try
            {
                AggregateException error = Assert.ThrowsExactly<AggregateException>(() => AudioMetadata.OpenReader(file));
                StringAssert.Contains(error.Message, "Supported formats", "the error should name the supported formats");
                Assert.IsTrue(error.InnerExceptions.Count >= 4, "every decoder's failure should be preserved");
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void MissingFileThrowsFileNotFound()
        {
            Assert.ThrowsExactly<FileNotFoundException>(
                () => AudioMetadata.OpenReader(TempFile(".wav")));
        }
    }
}
