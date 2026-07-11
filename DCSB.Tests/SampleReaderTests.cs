using DCSB.SoundPlayer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DCSB.Tests
{
    [TestClass]
    public class SampleReaderTests
    {
        [TestMethod]
        public void DisposeWaitsForConcurrentCurrentTimeRead()
        {
            BlockingAudioReader audioReader = new BlockingAudioReader();
            SampleReader sampleReader = new SampleReader(audioReader, false);

            Task<TimeSpan> positionRead = Task.Run(() => sampleReader.CurrentTime);
            Assert.IsTrue(audioReader.CurrentTimeEntered.Wait(1000));

            Task dispose = Task.Run(() => sampleReader.Dispose());
            try
            {
                Assert.IsFalse(audioReader.DisposeEntered.Wait(100));
            }
            finally
            {
                audioReader.AllowCurrentTime.Set();
            }
            Assert.AreEqual(TimeSpan.FromSeconds(1), positionRead.GetAwaiter().GetResult());
            dispose.GetAwaiter().GetResult();
            Assert.IsTrue(sampleReader.IsDisposed);
            Assert.AreEqual(TimeSpan.Zero, sampleReader.CurrentTime);
        }

        private sealed class BlockingAudioReader : IAudioReader
        {
            public readonly ManualResetEventSlim CurrentTimeEntered = new ManualResetEventSlim();
            public readonly ManualResetEventSlim AllowCurrentTime = new ManualResetEventSlim();
            public readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();

            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            public long Position { get; set; }
            public TimeSpan CurrentTime
            {
                get
                {
                    CurrentTimeEntered.Set();
                    AllowCurrentTime.Wait();
                    return TimeSpan.FromSeconds(1);
                }
                set { }
            }
            public TimeSpan TotalTime { get { return TimeSpan.FromSeconds(2); } }
            public int Read(float[] buffer, int offset, int count) { return 0; }
            public void Dispose() { DisposeEntered.Set(); }
        }
    }
}
