using System;

namespace DCSB.SoundPlayer
{
    // A denoiser that NoiseSuppressionSampleProvider can drive: all current
    // implementations (rnnoise, DeepFilterNet3) consume 48 kHz mono audio in
    // fixed frames of FrameSize samples on the +-1 float scale, processed in
    // place. Dispose frees the native state.
    public interface INoiseSuppressor : IDisposable
    {
        int FrameSize { get; }

        void Process(float[] frame);
    }
}
