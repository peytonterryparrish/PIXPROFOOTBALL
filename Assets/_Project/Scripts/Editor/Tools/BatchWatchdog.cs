using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Hard time limit for long batch methods: a background thread ends the editor process (exit code 2) after N minutes, even when the
    /// main thread is blocked (a synchronous loop) or play mode never ticks (WaitForEndOfFrame does not fire in -batchmode).
    /// Arm it as the first line of every long batch entry point; Arm again after a domain reload (entering play mode resets statics).
    /// Command line override: -pixWatchdog &lt;minutes&gt;.
    /// </summary>
    public static class BatchWatchdog
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RearmAfterDomainReload()
        {
            // entering play mode in a batch run reloads the domain and drops the timer: re-arm for the play-mode tools that are started with their own flag
            var a = Environment.GetCommandLineArgs();
            if (Array.IndexOf(a, "-pixGallery") >= 0 || Array.IndexOf(a, "-pixAnimSeq") >= 0) Arm(15);
        }

        static Timer _timer;
        static DateTime _deadline = DateTime.MinValue;

        public static void Arm(float minutes)
        {
            if (!Application.isBatchMode) return;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-pixWatchdog" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m)) minutes = m;
            var dl = DateTime.UtcNow.AddMinutes(minutes);
            if (_timer != null && dl <= _deadline) return;      // nested entry points only ever extend the limit
            _deadline = dl;
            if (_timer != null) _timer.Dispose();
            long ms = (long)(minutes * 60000f);
            _timer = new Timer(_ =>
            {
                try { System.IO.File.AppendAllText("Logs/batch_watchdog.txt", DateTime.Now.ToString("s") + " watchdog fired after " + minutes + " min, exiting\n"); } catch { }
                Environment.Exit(2);
            }, null, ms, Timeout.Infinite);
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>The stance gallery of the STANCE track with a hard 15 minute limit (it renders in play mode: a stalled frame wait must not hold the project).</summary>
    public static class GalleryWithWatchdog
    {
        public static void Run() { BatchWatchdog.Arm(15); StanceGallery.Run(); }
    }
}
