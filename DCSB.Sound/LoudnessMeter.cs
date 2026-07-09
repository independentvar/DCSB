using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace DCSB.SoundPlayer
{
    // Measures a sound file's integrated loudness (LUFS) per ITU-R BS.1770-4:
    // K-weighting (a high shelf approximating the head's acoustic effect followed
    // by a high-pass), energy over 400 ms blocks stepped every 100 ms, then an
    // absolute -70 LUFS gate and a relative -10 LU gate so lead-in silence and
    // quiet tails don't drag the number down. This is the same algorithm the
    // EBU R128 loudness recommendation builds on.
    public static class LoudnessMeter
    {
        private const double AbsoluteGateLufs = -70;
        private const double RelativeGateLu = 10;

        // channel weights above 2 channels are all 1.0 (surround weighting needs a
        // channel layout, which soundboard files don't carry); mono and stereo -
        // effectively everything a soundboard plays - are exact per the spec

        // Returns the file's integrated loudness, or null when the file cannot be
        // decoded or contains only silence.
        public static double? MeasureFile(string fileName)
        {
            IAudioReader reader = null;
            try
            {
                reader = AudioMetadata.OpenReader(fileName);
                return Measure(reader);
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

        internal static double? Measure(IAudioReader reader)
        {
            WaveFormat format = reader.WaveFormat;
            int channels = format.Channels;
            int sampleRate = format.SampleRate;
            if (channels < 1 || sampleRate < 8000)
            {
                return null;
            }

            KWeightingFilter[] filters = new KWeightingFilter[channels];
            for (int c = 0; c < channels; c++)
            {
                filters[c] = new KWeightingFilter(sampleRate);
            }

            // energy is accumulated in 100 ms steps; a 400 ms measurement block is
            // the mean of 4 consecutive steps, giving the spec's 75% overlap
            int stepFrames = sampleRate / 10;
            double stepSum = 0;
            int stepFill = 0;
            List<double> stepEnergies = new List<double>();

            // whole-file accumulation, for clips shorter than one 400 ms block
            double totalSum = 0;
            long totalFrames = 0;

            float[] buffer = new float[sampleRate * channels]; // 1 s of audio
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i + channels <= read; i += channels)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        double weighted = filters[c].Process(buffer[i + c]);
                        stepSum += weighted * weighted;
                    }
                    stepFill++;
                    totalFrames++;
                    if (stepFill == stepFrames)
                    {
                        stepEnergies.Add(stepSum / stepFrames);
                        totalSum += stepSum;
                        stepSum = 0;
                        stepFill = 0;
                    }
                }
            }
            totalSum += stepSum;

            List<double> blockEnergies = new List<double>();
            for (int i = 0; i + 4 <= stepEnergies.Count; i++)
            {
                blockEnergies.Add((stepEnergies[i] + stepEnergies[i + 1] + stepEnergies[i + 2] + stepEnergies[i + 3]) / 4);
            }
            if (blockEnergies.Count == 0)
            {
                // too short for a single measurement block: treat the whole clip as
                // one block, ungated (a short clip is all attack, nothing to gate off)
                if (totalFrames == 0)
                {
                    return null;
                }
                double clipEnergy = totalSum / totalFrames;
                return clipEnergy > 0 ? Loudness(clipEnergy) : (double?)null;
            }

            // absolute gate
            List<double> gated = new List<double>();
            foreach (double energy in blockEnergies)
            {
                if (energy > 0 && Loudness(energy) > AbsoluteGateLufs)
                {
                    gated.Add(energy);
                }
            }
            if (gated.Count == 0)
            {
                return null; // silence
            }

            // relative gate, -10 LU under the loudness of what passed the absolute gate
            double relativeThreshold = Loudness(Mean(gated)) - RelativeGateLu;
            List<double> relativeGated = new List<double>();
            foreach (double energy in gated)
            {
                if (Loudness(energy) > relativeThreshold)
                {
                    relativeGated.Add(energy);
                }
            }
            List<double> result = relativeGated.Count > 0 ? relativeGated : gated;
            return Loudness(Mean(result));
        }

        private static double Loudness(double energy)
        {
            return -0.691 + 10 * Math.Log10(energy);
        }

        private static double Mean(List<double> values)
        {
            double sum = 0;
            foreach (double value in values) sum += value;
            return sum / values.Count;
        }

        // The two-stage K-weighting filter, with coefficients derived for any sample
        // rate from the spec's analog prototype (same derivation libebur128 uses;
        // at 48 kHz these reproduce the coefficient table printed in BS.1770-4).
        private sealed class KWeightingFilter
        {
            private readonly double _b0a, _b1a, _b2a, _a1a, _a2a; // stage 1: high shelf
            private readonly double _b0b, _b1b, _b2b, _a1b, _a2b; // stage 2: high pass
            private double _z1a, _z2a, _z1b, _z2b;

            public KWeightingFilter(int sampleRate)
            {
                // stage 1: +4 dB high shelf
                double f0 = 1681.974450955533;
                double gainDb = 3.999843853973347;
                double q = 0.7071752369554196;
                double k = Math.Tan(Math.PI * f0 / sampleRate);
                double vh = Math.Pow(10.0, gainDb / 20.0);
                double vb = Math.Pow(vh, 0.4996667741545416);
                double a0 = 1.0 + k / q + k * k;
                _b0a = (vh + vb * k / q + k * k) / a0;
                _b1a = 2.0 * (k * k - vh) / a0;
                _b2a = (vh - vb * k / q + k * k) / a0;
                _a1a = 2.0 * (k * k - 1.0) / a0;
                _a2a = (1.0 - k / q + k * k) / a0;

                // stage 2: high pass
                f0 = 38.13547087602444;
                q = 0.5003270373238773;
                k = Math.Tan(Math.PI * f0 / sampleRate);
                a0 = 1.0 + k / q + k * k;
                _b0b = 1.0;
                _b1b = -2.0;
                _b2b = 1.0;
                _a1b = 2.0 * (k * k - 1.0) / a0;
                _a2b = (1.0 - k / q + k * k) / a0;
            }

            // both stages in transposed direct form II
            public double Process(double x)
            {
                double y = _b0a * x + _z1a;
                _z1a = _b1a * x - _a1a * y + _z2a;
                _z2a = _b2a * x - _a2a * y;

                double z = _b0b * y + _z1b;
                _z1b = _b1b * y - _a1b * z + _z2b;
                _z2b = _b2b * y - _a2b * z;
                return z;
            }
        }
    }
}
