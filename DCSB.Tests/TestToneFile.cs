using System;
using System.IO;

namespace DCSB.Tests
{
    public static class TestToneFile
    {
        public static void Create(string path, int seconds)
        {
            const int sampleRate = 44100;
            const short channels = 1;
            const short bitsPerSample = 16;
            int sampleCount = sampleRate * seconds;
            int dataLength = sampleCount * channels * (bitsPerSample / 8);

            using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataLength);
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * (bitsPerSample / 8));
                writer.Write((short)(channels * (bitsPerSample / 8)));
                writer.Write(bitsPerSample);
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataLength);
                for (int index = 0; index < sampleCount; index++)
                {
                    short sample = (short)(Math.Sin(2 * Math.PI * 440 * index / sampleRate) * 12000);
                    writer.Write(sample);
                }
            }
        }
    }
}
