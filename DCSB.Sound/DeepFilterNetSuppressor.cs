using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DCSB.SoundPlayer
{
    // Thin owned wrapper around the vendored deepfilter.dll - libDF's own C API
    // compiled from a pinned Rikorose/DeepFilterNet tag (see
    // ThirdParty\deepfilternet). Clearly better suppression than rnnoise on hard
    // noise (clatter, background voices), at ~40 ms algorithmic latency instead
    // of ~10 ms. df_create loads the DeepFilterNet3 model from a tar.gz path, so
    // the embedded model resource is extracted to a temp file once, like the
    // wizard's beep.wav.
    public class DeepFilterNetSuppressor : INoiseSuppressor
    {
        // DeepFilterNet3 processes 10 ms hops of 48 kHz mono audio
        public const int SampleRate = 48000;

        // suppression headroom in dB; matches the model's full range (upstream's
        // LADSPA plugin default), leaving how much noise to keep to the model
        private const float AttenuationLimitDb = 100f;

        private const string ModelResourceName = "DeepFilterNet3_onnx.tar.gz";

        private static class Native
        {
            private const string Dll = "deepfilter";

            // path is *const c_char interpreted as UTF-8 (Rust CStr::to_str), so
            // it is marshaled as a null-terminated UTF-8 byte array, not LPStr
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr df_create(byte[] path, float attenLim);

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern UIntPtr df_get_frame_length(IntPtr state);

            // in and out must be distinct buffers of df_get_frame_length()
            // samples on the +-1 float scale; returns the frame's local SNR
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern float df_process_frame(IntPtr state, float[] input, float[] output);

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void df_free(IntPtr state);
        }

        private static readonly object _modelLock = new object();
        private static string _cachedModelPath;

        // Extracts the embedded DeepFilterNet3 model to a temp file once (same
        // pattern as TestSound.GetPath): df_create only takes a file path, and the
        // installer ships no loose non-dll files.
        private static string GetModelPath()
        {
            lock (_modelLock)
            {
                if (_cachedModelPath != null && File.Exists(_cachedModelPath))
                {
                    return _cachedModelPath;
                }

                Assembly assembly = typeof(DeepFilterNetSuppressor).Assembly;
                string resourceName = assembly.GetManifestResourceNames()
                    .First(name => name.EndsWith(ModelResourceName, StringComparison.OrdinalIgnoreCase));

                string path = Path.Combine(Path.GetTempPath(), "dcsb_" + ModelResourceName);
                using (Stream source = assembly.GetManifestResourceStream(resourceName))
                using (FileStream destination = File.Create(path))
                {
                    source.CopyTo(destination);
                }

                _cachedModelPath = path;
                return path;
            }
        }

        private IntPtr _state;
        private readonly float[] _output;

        public int FrameSize { get; private set; }

        public DeepFilterNetSuppressor()
        {
            string modelPath = GetModelPath();

            // df_create panics (aborting the process) instead of returning null
            // when the model cannot be read - never hand it a missing file
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("DeepFilterNet model not found.", modelPath);
            }

            byte[] utf8Path = Encoding.UTF8.GetBytes(modelPath + "\0");
            _state = Native.df_create(utf8Path, AttenuationLimitDb);
            if (_state == IntPtr.Zero)
            {
                throw new InvalidOperationException("df_create failed.");
            }

            FrameSize = (int)Native.df_get_frame_length(_state);
            _output = new float[FrameSize];
        }

        // Denoises one FrameSize-sample frame in place (via a scratch buffer:
        // unlike rnnoise, df_process_frame must not alias input and output).
        public void Process(float[] frame)
        {
            Native.df_process_frame(_state, frame, _output);
            Array.Copy(_output, frame, frame.Length);
        }

        public void Dispose()
        {
            if (_state != IntPtr.Zero)
            {
                Native.df_free(_state);
                _state = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~DeepFilterNetSuppressor()
        {
            Dispose();
        }
    }
}
