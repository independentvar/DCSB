using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DCSB.SoundPlayer
{
    // Provides the bundled setup-wizard test tone as a real file path. The engines'
    // PlaySound only takes a file name, so the embedded beep.wav is extracted to a
    // temp file once and reused, keeping the wizard test on the exact code path a
    // real sound takes through both output engines.
    public static class TestSound
    {
        private static readonly object _lock = new object();
        private static string _cachedPath;

        public static string GetPath()
        {
            lock (_lock)
            {
                if (_cachedPath != null && File.Exists(_cachedPath))
                {
                    return _cachedPath;
                }

                Assembly assembly = typeof(TestSound).Assembly;
                string resourceName = assembly.GetManifestResourceNames()
                    .First(name => name.EndsWith("beep.wav", StringComparison.OrdinalIgnoreCase));

                string path = Path.Combine(Path.GetTempPath(), "dcsb_test_beep.wav");
                using (Stream source = assembly.GetManifestResourceStream(resourceName))
                using (FileStream destination = File.Create(path))
                {
                    source.CopyTo(destination);
                }

                _cachedPath = path;
                return path;
            }
        }
    }
}
