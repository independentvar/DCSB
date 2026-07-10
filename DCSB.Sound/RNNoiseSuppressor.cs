using System;
using System.Runtime.InteropServices;

namespace DCSB.SoundPlayer
{
    // Thin owned wrapper around the vendored rnnoise.dll (see ThirdParty\rnnoise:
    // built from a pinned xiph/rnnoise tag by build-rnnoise.ps1). The native API
    // is three calls around an opaque state pointer and has been stable for
    // years, so this wrapper is the whole integration surface - no third-party
    // wrapper package to go stale.
    public class RNNoiseSuppressor : INoiseSuppressor
    {
        // rnnoise processes fixed 10 ms frames of 48 kHz mono audio
        public const int SampleRate = 48000;

        private static class Native
        {
            private const string Dll = "rnnoise";

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int rnnoise_get_frame_size();

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr rnnoise_create(IntPtr model);

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void rnnoise_destroy(IntPtr state);

            // returns the voice-activity probability (unused); in and out may be
            // the same buffer (upstream's own demo processes in place)
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern float rnnoise_process_frame(IntPtr state, float[] output, float[] input);
        }

        public int FrameSize
        {
            get { return Native.rnnoise_get_frame_size(); }
        }

        private IntPtr _state;

        public RNNoiseSuppressor()
        {
            // IntPtr.Zero selects the built-in model
            _state = Native.rnnoise_create(IntPtr.Zero);
            if (_state == IntPtr.Zero)
            {
                throw new InvalidOperationException("rnnoise_create failed.");
            }
        }

        // Denoises one FrameSize-sample frame in place. rnnoise works on the
        // 16-bit sample scale, so the +-1 float samples are scaled up and back.
        public void Process(float[] frame)
        {
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] *= 32768f;
            }
            Native.rnnoise_process_frame(_state, frame, frame);
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] /= 32768f;
            }
        }

        public void Dispose()
        {
            if (_state != IntPtr.Zero)
            {
                Native.rnnoise_destroy(_state);
                _state = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~RNNoiseSuppressor()
        {
            Dispose();
        }
    }
}
