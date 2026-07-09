using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace DCSB.SoundPlayer
{
    // Per-file loudness cache and make-up gain for volume normalization: every
    // clip is pulled toward the same perceived loudness so a whisper-quiet meme
    // and an ear-splitting airhorn come out comparable, regardless of how their
    // source files were mastered. Loudness is measured once per file per session
    // on a background thread (mirroring how sound durations are read) and cached
    // in memory only - if a file changes on disk it is simply remeasured next run.
    public static class LoudnessNormalization
    {
        // streaming-typical loudness target; per-sound volume sliders still apply
        // on top, so this only moves the baseline
        private const double TargetLufs = -16;

        // quiet sources get boosted at most this much: a clip mastered absurdly low
        // is usually mostly noise floor, and unbounded boost would amplify exactly
        // that (there is no limiter downstream)
        private const double MaxBoostDb = 12;

        // null value = measured but undecodable/silent; such files play at unity
        private static readonly ConcurrentDictionary<string, double?> _cache =
            new ConcurrentDictionary<string, double?>(StringComparer.OrdinalIgnoreCase);

        // Gates the background prefetch (set alongside the user's normalization
        // toggle) so disabled installs don't spend CPU decoding every sound file.
        public static bool PrefetchEnabled { get; set; }

        // Measures and caches the file's loudness; called from background threads.
        public static void Prefetch(string fileName)
        {
            if (!PrefetchEnabled || _cache.ContainsKey(fileName))
            {
                return;
            }
            _cache[fileName] = LoudnessMeter.MeasureFile(fileName);
        }

        // Linear gain that brings the file to the target loudness. When the file
        // has not been measured yet, returns unity now and starts a background
        // measurement so the next play is normalized - playback must never wait
        // on a full-file decode.
        public static float GetGain(string fileName)
        {
            double? lufs;
            if (!_cache.TryGetValue(fileName, out lufs))
            {
                Task.Run(() => _cache[fileName] = LoudnessMeter.MeasureFile(fileName));
                return 1f;
            }
            if (!lufs.HasValue)
            {
                return 1f;
            }
            double gainDb = Math.Min(TargetLufs - lufs.Value, MaxBoostDb);
            return (float)Math.Pow(10.0, gainDb / 20.0);
        }
    }
}
