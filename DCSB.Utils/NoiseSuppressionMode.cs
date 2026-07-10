namespace DCSB.Utils
{
    // Which denoiser runs on the microphone leg. Fast is rnnoise (~10 ms extra
    // voice latency); HighQuality is DeepFilterNet3 (~40 ms, noticeably better on
    // hard noise). Serialized by name into config.xml, so renaming members would
    // break existing configs.
    public enum NoiseSuppressionMode
    {
        Disabled,
        Fast,
        HighQuality
    }
}
