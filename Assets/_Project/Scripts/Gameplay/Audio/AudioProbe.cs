using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD QA probe (flag <c>-pixAudioProbe</c>): plays a CPU-vs-CPU watch game and measures the sound. Writes audio_report.txt: sound events per sim event type against the target list
    /// (did each event start a sound within a second), SFX started per play, per-key counts, dropped / missing keys, limiter peak and clip count, momentary / short-term / integrated LUFS of the final
    /// mix, frame time while live, the music cues that played and the crowd animation info; plus broadcast-camera screenshots of the idle crowd and of the crowd after a home score.
    /// Args: -pixAudioOut dir, -pixAudioQuarter minutes (2), -pixAudioSpeed x (2), -pixAudioMinutes real-time budget (6), -pixAudioSeed n. Combine with -pixAudioSolo music|crowd|sfx for per-bus loudness.
    /// </summary>
    public sealed class AudioProbe : MonoBehaviour
    {
        const string Flag = "-pixAudioProbe";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        static string Arg(string name, string def) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return def; }
        static float ArgF(string name, float def) { float v; return float.TryParse(Arg(name, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has || FindFirstObjectByType<AudioProbe>() != null) return;
            var go = new GameObject("PixAudioProbe"); DontDestroyOnLoad(go); go.AddComponent<AudioProbe>();
        }

        string _out;
        MatchDirector _dir;
        readonly Dictionary<SimEventType, int> _events = new Dictionary<SimEventType, int>(), _heard = new Dictionary<SimEventType, int>();
        sealed class Pend { public float At; public SimEventType T; public int Before; }
        readonly List<Pend> _pend = new List<Pend>();
        readonly List<float> _mom = new List<float>(), _frames = new List<float>();
        int _plays, _startedAtPlay;
        readonly List<int> _perPlay = new List<int>();
        readonly List<string> _log = new List<string>();
        string _crowdAtScore = "";
        bool _shotIdle, _shotScore; float _shotScoreAt = -1f;

        int Sounds { get { var h = AudioHub.Instance; var m = MatchAudio.Current; return (h != null ? h.TotalStarted : 0) + (m != null ? m.Crowd.Reactions + m.Music.StingerCount : 0); } }

        void Awake() { _out = Arg("-pixAudioOut", Path.Combine(Application.dataPath, "..", "Logs", "audio")); Directory.CreateDirectory(_out); CrowdMix.Reacted += OnCue; }
        void OnDestroy() { CrowdMix.Reacted -= OnCue; }

        void OnCue(CrowdCue c, float s, int b)
        {
            if (!_shotScore && (c == CrowdCue.Touchdown || c == CrowdCue.Turnover || c == CrowdCue.FieldGoal) && _shotScoreAt < 0f)
            {
                _shotScoreAt = Time.unscaledTime + 1.4f;
                var m = MatchAudio.Current; if (m != null) _crowdAtScore = m.Crowd.Describe();
            }
        }

        IEnumerator Start()
        {
            yield return null; yield return null;
            var cfg = new MatchConfig { HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = (int)ArgF("-pixAudioQuarter", 2f), UserTeam = CoreTeam.Home, Seed = (int)ArgF("-pixAudioSeed", 20261111), CpuVsCpu = false, Difficulty = Difficulty.Pro };
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance.Flow == null) && wait < 240f) { wait += Time.unscaledDeltaTime; yield return null; }
            _dir = MatchDirector.Instance;
            if (_dir == null) { Write("FAIL: match did not start"); Quit(2); yield break; }
            _log.Add("match started after " + wait.ToString("0.0") + " s, watch " + _dir.WatchActive);
            WatchMode.SpeedIndex = ArgF("-pixAudioSpeed", 2f) > 1.5f ? 1 : 0;
            _dir.SimEvent += e => { int n; _events.TryGetValue(e.Type, out n); _events[e.Type] = n + 1; _pend.Add(new Pend { At = Time.unscaledTime + 1.0f, T = e.Type, Before = Sounds }); };
            _dir.PlayStarted += s => { _startedAtPlay = Sounds; };
            _dir.PlayFinished += o => { _plays++; _perPlay.Add(Sounds - _startedAtPlay); };
            if (AudioHub.Instance != null) AudioHub.Instance.ResetStats();
            float start = Time.unscaledTime, budget = ArgF("-pixAudioMinutes", 6f) * 60f, nextMom = 0f;
            while (!_dir.Finished && Time.unscaledTime - start < budget && _dir != null)
            {
                float dt = Time.unscaledDeltaTime;
                if (_dir.Phase == MatchPhase.Live || _dir.Phase == MatchPhase.PreSnap) _frames.Add(dt * 1000f);
                if (Time.unscaledTime >= nextMom)
                {
                    nextMom = Time.unscaledTime + 1f;
                    var lim = MasterLimiter.Instance;
                    if (lim != null) { float m, st, ig; lim.Loudness(out m, out st, out ig); if (m > -69f) _mom.Add(m); }
                }
                for (int i = _pend.Count - 1; i >= 0; i--)
                {
                    var p = _pend[i]; if (Time.unscaledTime < p.At) continue;
                    _pend.RemoveAt(i);
                    int n; _heard.TryGetValue(p.T, out n); if (Sounds > p.Before) _heard[p.T] = n + 1; else if (!_heard.ContainsKey(p.T)) _heard[p.T] = n;
                }
                if (!_shotIdle && _plays >= 3 && _dir.Phase == MatchPhase.PreSnap) { _shotIdle = true; Shot("crowd_idle"); }
                if (!_shotScore && _shotScoreAt > 0f && Time.unscaledTime >= _shotScoreAt) { _shotScore = true; Shot("crowd_after_home_score"); }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            Write(null); Quit(0);
        }

        void Shot(string name) { try { ScreenCapture.CaptureScreenshot(Path.Combine(_out, name + ".png")); _log.Add("screenshot " + name); } catch (Exception e) { _log.Add("screenshot failed: " + e.Message); } }

        static float Pct(List<float> l, float p) { if (l.Count == 0) return 0; var s = l.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(p * (s.Count - 1)), 0, s.Count - 1)]; }

        void Write(string failure)
        {
            var sb = new StringBuilder();
            var hub = AudioHub.Instance; var lim = MasterLimiter.Instance; var ma = MatchAudio.Current;
            sb.AppendLine("# audio probe " + DateTime.Now.ToString("s") + (failure != null ? "  " + failure : ""));
            foreach (var l in _log) sb.AppendLine(l);
            sb.AppendLine("solo bus: " + (Arg("-pixAudioSolo", "none")) + ", sample rate " + AudioSettings.outputSampleRate);
            sb.AppendLine("plays " + _plays + ", sounds per play (sfx+crowd shots+stingers): avg " + (_perPlay.Count > 0 ? _perPlay.Average().ToString("0.0") : "0") + ", min " + (_perPlay.Count > 0 ? _perPlay.Min() : 0) + ", max " + (_perPlay.Count > 0 ? _perPlay.Max() : 0));
            if (hub != null) sb.AppendLine("sfx started " + hub.TotalStarted + ", dropped by limiter " + hub.TotalDropped + ", max simultaneous voices " + hub.MaxSimultaneous + " of 28; missing groups: " + (hub.Missing.Count == 0 ? "none" : string.Join(", ", hub.Missing.Select(k => k.Key + "x" + k.Value))));
            if (ma != null) sb.AppendLine("crowd reactions " + ma.Crowd.Reactions + " (" + string.Join(", ", ma.Crowd.ReactionCounts.OrderByDescending(k => k.Value).Select(k => k.Key + "x" + k.Value)) + "); contacts heard " + ma.ContactsSeen + ", sim events seen " + ma.EventsSeen);
            if (ma != null) sb.AppendLine("music cues: " + string.Join(" > ", ma.Music.History) + "; stingers " + ma.Music.StingerCount);
            sb.AppendLine("crowd beds at the home score: " + _crowdAtScore);
            if (lim != null)
            {
                float pre, post, m, st, ig; lim.Peaks(out pre, out post); lim.Loudness(out m, out st, out ig);
                sb.AppendLine("master: peak before limiter " + pre.ToString("0.0") + " dBFS, after " + post.ToString("0.0") + " dBFS, limiter engaged on " + lim.LimitedSamples + " samples; integrated " + ig.ToString("0.0") + " LUFS, short-term now " + st.ToString("0.0") + ", momentary p10/p50/p90/max " + Pct(_mom, 0.1f).ToString("0.0") + "/" + Pct(_mom, 0.5f).ToString("0.0") + "/" + Pct(_mom, 0.9f).ToString("0.0") + "/" + Pct(_mom, 1f).ToString("0.0") + " LUFS (" + _mom.Count + " samples)");
            }
            else sb.AppendLine("master: no limiter (no listener?)");
            if (_frames.Count > 0)
            {
                var s = _frames.OrderBy(x => x).ToList(); float avg = _frames.Average();
                float med = s[s.Count / 2];
                float low = s.Skip((int)(s.Count * 0.99)).DefaultIfEmpty(0).Average();
                sb.AppendLine("frame ms (pre-snap + live, " + _frames.Count + " frames): avg " + avg.ToString("0.00") + " median " + med.ToString("0.00") + " (" + (1000f / avg).ToString("0") + " fps), p99 " + low.ToString("0.00") + " (1% low " + (1000f / Mathf.Max(0.01f, low)).ToString("0") + " fps)");
            }
            var crowd = FindFirstObjectByType<PixFootball.Gameplay.CrowdInfo>();
            sb.AppendLine("crowd animation: " + (CrowdAnimInfo != null ? CrowdAnimInfo() : "n/a"));
            sb.AppendLine();
            sb.AppendLine("sim event | count | had a sound within 1 s");
            foreach (var t in _events.Keys.OrderBy(k => k.ToString())) { int h; _heard.TryGetValue(t, out h); sb.AppendLine(t + " | " + _events[t] + " | " + h + (h == _events[t] ? "" : "  (" + (100 * h / Math.Max(1, _events[t])) + "%)")); }
            sb.AppendLine();
            sb.AppendLine("sfx started per key:");
            if (hub != null) foreach (var kv in hub.Started.OrderByDescending(k => k.Value)) sb.AppendLine("  " + kv.Key + " " + kv.Value);
            if (hub != null && hub.Dropped.Count > 0) { sb.AppendLine("dropped (limits / distance):"); foreach (var kv in hub.Dropped.OrderByDescending(k => k.Value)) sb.AppendLine("  " + kv.Key + " " + kv.Value); }
            File.WriteAllText(Path.Combine(_out, "audio_report.txt"), sb.ToString());
            Debug.Log("[AudioProbe] report written to " + Path.GetFullPath(Path.Combine(_out, "audio_report.txt")));
        }

        /// <summary>Set by the Presentation assembly (crowd animation stats).</summary>
        public static Func<string> CrowdAnimInfo;

        static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
