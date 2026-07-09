using DCSB.SoundPlayer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace DCSB.Tests
{
    // BS.1770 reference points: the -0.691 offset in the loudness formula is chosen
    // so a 997 Hz sine is weighted with net 0 dB gain. A full-scale stereo 997 Hz
    // sine therefore measures -0.69 LUFS (per-channel mean square 0.5, summed = 1),
    // and every 20 dB of amplitude drop lowers the result by exactly 20 LU.
    [TestClass]
    public class LoudnessTests
    {
        private static string WriteToneWav(double amplitude, double seconds, double silenceSeconds = 0)
        {
            const int rate = 44100;
            const double frequency = 997;
            int toneCount = (int)(rate * seconds);
            int silenceCount = (int)(rate * silenceSeconds);
            short[] samples = new short[(toneCount + silenceCount) * 2];
            for (int i = 0; i < toneCount; i++)
            {
                short value = (short)(Math.Sin(2 * Math.PI * frequency * i / rate) * amplitude * short.MaxValue);
                samples[i * 2] = value;
                samples[i * 2 + 1] = value;
            }

            string path = Path.Combine(Path.GetTempPath(), "dcsb-loudness-" + Guid.NewGuid().ToString("N") + ".wav");
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                writer.Write("RIFF".ToCharArray()); writer.Write(36 + dataBytes);
                writer.Write("WAVEfmt ".ToCharArray()); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(rate);
                writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
                writer.Write("data".ToCharArray()); writer.Write(dataBytes);
                foreach (short sample in samples) writer.Write(sample);
            }
            return path;
        }

        private static void AssertLufs(double expected, double? actual, double tolerance, string message)
        {
            Assert.IsTrue(actual.HasValue, message + " (no measurement)");
            Assert.AreEqual(expected, actual.Value, tolerance, message + string.Format(" (measured {0:F2} LUFS)", actual.Value));
        }

        [TestMethod]
        public void FullScaleSineMeasuresReferenceLoudness()
        {
            string file = WriteToneWav(1.0, 3);
            try { AssertLufs(-0.69, LoudnessMeter.MeasureFile(file), 0.7, "full-scale 997 Hz stereo sine"); }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void TwentyDbQuieterSineMeasuresTwentyLuLower()
        {
            string file = WriteToneWav(0.1, 3);
            try { AssertLufs(-20.69, LoudnessMeter.MeasureFile(file), 0.7, "-20 dBFS sine"); }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void GatingIgnoresAppendedSilence()
        {
            string toneOnly = WriteToneWav(0.25, 3);
            string withSilence = WriteToneWav(0.25, 3, 5);
            try
            {
                double? reference = LoudnessMeter.MeasureFile(toneOnly);
                double? padded = LoudnessMeter.MeasureFile(withSilence);
                Assert.IsTrue(reference.HasValue && padded.HasValue, "both files should measure");
                Assert.AreEqual(reference.Value, padded.Value, 0.5,
                    string.Format("silence must be gated out ({0:F2} vs {1:F2} LUFS)", reference.Value, padded.Value));
            }
            finally
            {
                File.Delete(toneOnly);
                File.Delete(withSilence);
            }
        }

        [TestMethod]
        public void ClipShorterThanOneBlockStillMeasures()
        {
            string file = WriteToneWav(0.5, 0.2);
            try { AssertLufs(-6.71, LoudnessMeter.MeasureFile(file), 1.5, "200 ms clip (single ungated block)"); }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void SilenceMeasuresNull()
        {
            string file = WriteToneWav(0.0, 1);
            try { Assert.IsNull(LoudnessMeter.MeasureFile(file), "pure silence must not produce a loudness"); }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void GainPullsLoudAndQuietClipsToTheSameLevel()
        {
            LoudnessNormalization.PrefetchEnabled = true;
            string loud = WriteToneWav(0.6, 2);   // ~ -5.1 LUFS -> attenuated
            string quiet = WriteToneWav(0.06, 2); // ~ -25.1 LUFS -> boosted
            try
            {
                LoudnessNormalization.Prefetch(loud);
                LoudnessNormalization.Prefetch(quiet);
                float loudGain = LoudnessNormalization.GetGain(loud);
                float quietGain = LoudnessNormalization.GetGain(quiet);

                Assert.IsTrue(loudGain < 1f, string.Format("loud clip must be attenuated (gain {0:F3})", loudGain));
                Assert.IsTrue(quietGain > 1f, string.Format("quiet clip must be boosted (gain {0:F3})", quietGain));

                // both should land on the -16 LUFS target: amplitude * gain equal within tolerance
                double loudLevel = 0.6 * loudGain;
                double quietLevel = 0.06 * quietGain;
                Assert.AreEqual(loudLevel, quietLevel, loudLevel * 0.15,
                    string.Format("normalized levels should match ({0:F3} vs {1:F3})", loudLevel, quietLevel));
            }
            finally
            {
                File.Delete(loud);
                File.Delete(quiet);
            }
        }

        [TestMethod]
        public void BoostIsClampedForExtremelyQuietClips()
        {
            LoudnessNormalization.PrefetchEnabled = true;
            string nearSilent = WriteToneWav(0.005, 2); // ~ -46.7 LUFS, would need ~+31 dB
            try
            {
                LoudnessNormalization.Prefetch(nearSilent);
                float gain = LoudnessNormalization.GetGain(nearSilent);
                Assert.AreEqual(Math.Pow(10.0, 12.0 / 20.0), gain, 0.01,
                    string.Format("boost must clamp at +12 dB (gain {0:F3})", gain));
            }
            finally
            {
                File.Delete(nearSilent);
            }
        }

        [TestMethod]
        public void UnmeasuredFileGetsUnityGainImmediately()
        {
            // GetGain must never block playback: unknown file -> 1.0 now
            string missing = Path.Combine(Path.GetTempPath(), "dcsb-loudness-missing-" + Guid.NewGuid().ToString("N") + ".wav");
            Assert.AreEqual(1f, LoudnessNormalization.GetGain(missing));
        }
    }
}
