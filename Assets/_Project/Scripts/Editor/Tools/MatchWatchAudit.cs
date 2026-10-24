using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11 AUDIT track (QA only): "watch" full CPU-vs-CPU games the way the owner does - the real MatchDirector, the real broadcast camera, the real HUD - and log everything that is wrong.
    /// Per-frame checks (visibility of every renderer, missing parts, skinned-mesh bounds that cull limbs, floating / sunk feet, body overlap, ball off the hands, camera losing the ball, HUD rects),
    /// broadcast-camera film strips around every key event (a ring buffer of low-res frames is dumped when an event fires), HUD probes at 1080p / 1440p, and a play-by-play log.
    /// Run: Unity -batchmode -projectPath &lt;private copy&gt; -logFile ... -executeMethod PixFootball.EditorTools.MatchWatchAudit.Run -pixMatchWatch
    ///   [-pixMwOut dir] [-pixMwHome id] [-pixMwAway id] [-pixMwQuarter minutes] [-pixMwSeed n] [-pixMwWeather 0-5] [-pixMwTime 0-3] [-pixMwMaxPlays n] [-pixMwUser 1]
    /// </summary>
    public static class MatchWatchAudit
    {
        public const string Flag = "-pixMatchWatch";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(float.Parse(Arg("-pixMwWatchdog", "150"), CultureInfo.InvariantCulture));
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            if (UnityEngine.Object.FindFirstObjectByType<MatchWatchRunner>() != null) return;
            var go = new GameObject("PixMatchWatch");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MatchWatchRunner>();
        }
    }

    [DefaultExecutionOrder(5000)]
    public sealed partial class MatchWatchRunner : MonoBehaviour
    {
        string _out;
        readonly StringBuilder _events = new StringBuilder();
        readonly StringBuilder _plays = new StringBuilder();
        MatchDirector _dir;
        int _frame;
        int _lastPlayNo = -1;
        MatchPhase _lastPhase = MatchPhase.Booting;
        float _phaseT0;
        bool _started, _finishing;
        readonly Dictionary<string, int> _logCount = new Dictionary<string, int>();
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------------------------------------------------- issue registry
        sealed class IssueAgg { public string Type; public int Count; public HashSet<int> Plays = new HashSet<int>(); public List<string> Samples = new List<string>(); public double Sum; public double Max; }
        readonly SortedDictionary<string, IssueAgg> _issues = new SortedDictionary<string, IssueAgg>();
        readonly Dictionary<string, int> _shotCount = new Dictionary<string, int>(); int _totalShots;

        string Ctx()
        {
            string c = "?";
            try
            {
                var s = _dir != null && _dir.Flow != null ? _dir.Flow.State : null;
                if (s != null) c = "Q" + s.Quarter + " " + _dir.Flow.State.ClockDisplay + " " + s.Down + "&" + s.Distance + " spot" + s.BallSpot;
            }
            catch { }
            return c + " play#" + (_dir != null ? _dir.PlayNumber : -1) + " " + (_dir != null ? _dir.Phase.ToString() : "") + " f" + _frame + " t" + Time.time.ToString("0.0", CI);
        }

        /// <summary>Registers one instance of an issue type. Count/plays/max are aggregated; the first few samples keep their context. value is any metric worth taking a max of.</summary>
        void Issue(string type, string detail, double value = 0, int agent = -1, bool shot = false)
        {
            IssueAgg g;
            if (!_issues.TryGetValue(type, out g)) _issues[type] = g = new IssueAgg { Type = type };
            g.Count++; g.Sum += value; if (Math.Abs(value) > Math.Abs(g.Max)) g.Max = value;
            if (_dir != null) g.Plays.Add(_dir.PlayNumber);
            if (g.Samples.Count < 6) g.Samples.Add(Ctx() + " :: " + detail + (value != 0 ? " [" + value.ToString("0.###", CI) + "]" : ""));
            if (shot)
            {
                int n; _shotCount.TryGetValue(type, out n);
                if (n < 2 && _totalShots < 140) { _totalShots++; _shotCount[type] = n + 1; try { AnomalyShot(type + "_" + (n + 1), agent); } catch (Exception e) { Debug.Log("[MatchWatch] shot failed " + e.Message); } }
            }
        }

        void Ev(string s)
        {
            _events.AppendLine("[" + Time.time.ToString("0.00", CI) + " f" + _frame + " " + Ctx() + "] " + s);
            if (_events.Length > 200000) { try { File.AppendAllText(_out + "/events.log", _events.ToString()); } catch { } _events.Length = 0; }
        }

        // ---------------------------------------------------------------------------------------------------------- log capture (exceptions / errors / warnings)
        sealed class LogAgg { public int Count; public string First; public string Ctx; public LogType Type; public string Stack; }
        readonly Dictionary<string, LogAgg> _logs = new Dictionary<string, LogAgg>();
        void OnLog(string msg, string stack, LogType t)
        {
            if (t == LogType.Log) return;
            if (msg.StartsWith("[MatchWatch]")) return;
            string key = t + ":" + (msg.Length > 110 ? msg.Substring(0, 110) : msg);
            LogAgg a; if (!_logs.TryGetValue(key, out a)) { _logs[key] = a = new LogAgg { First = msg, Ctx = Ctx(), Type = t, Stack = stack.Length > 400 ? stack.Substring(0, 400) : stack }; }
            a.Count++;
        }

        // ---------------------------------------------------------------------------------------------------------- boot + match start
        IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            _oldCine = PixSettings.Cinematics; PixSettings.Cinematics = int.Parse(MatchWatchAudit.Arg("-pixMwCine", "0"), CI);   // the editor prefs of this machine have cine=2 (off); the shipped default is 0 (full)
            _out = MatchWatchAudit.Arg("-pixMwOut", "D:/PIXFootball/Renders/match_watch_v0100/g1");
            Directory.CreateDirectory(_out); Directory.CreateDirectory(_out + "/strips"); Directory.CreateDirectory(_out + "/anomaly"); Directory.CreateDirectory(_out + "/hud");
            Time.captureFramerate = 60;
            QualitySettings.vSyncCount = 0;
            yield return new WaitForSeconds(0.5f);
            var cfg = new MatchConfig
            {
                HomeId = MatchWatchAudit.Arg("-pixMwHome", "buffalo_ironhorns"), AwayId = MatchWatchAudit.Arg("-pixMwAway", "miami_reefrunners"),
                QuarterMinutes = int.Parse(MatchWatchAudit.Arg("-pixMwQuarter", "3"), CI), Seed = int.Parse(MatchWatchAudit.Arg("-pixMwSeed", "110110"), CI),
                UserTeam = Team.Home, CpuVsCpu = MatchWatchAudit.Arg("-pixMwUser", "0") != "1", Difficulty = Difficulty.Pro,
                Weather = (WeatherChoice)int.Parse(MatchWatchAudit.Arg("-pixMwWeather", "1"), CI), Time = (TimeChoice)int.Parse(MatchWatchAudit.Arg("-pixMwTime", "3"), CI)
            };
            if (!cfg.CpuVsCpu) MatchDirector.ForceAutopilot = true;
            _maxPlays = int.Parse(MatchWatchAudit.Arg("-pixMwMaxPlays", "400"), CI);
            Ev("start " + cfg.HomeId + " v " + cfg.AwayId + " q=" + cfg.QuarterMinutes + " seed=" + cfg.Seed + " weather=" + cfg.Weather + " time=" + cfg.Time + " cpuVsCpu=" + cfg.CpuVsCpu);
            _wall = System.Diagnostics.Stopwatch.StartNew();
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance.Flow == null || MatchDirector.Instance.Cams == null || MatchDirector.Instance.Flow == null) && _wall.Elapsed.TotalSeconds < 180) { wait += 0.05f; yield return null; }      // v0.11.0: wall-clock wait (the watch-mode coin toss uses real-time waits; 2400 captured frames pass in about 2 s)
            _dir = MatchDirector.Instance;
            if (_dir == null || _dir.Flow == null) { Ev("FAIL: match did not start"); Finish(2); yield break; }
            Ev("match scene up after " + wait.ToString("0.0", CI) + " s of game time, " + _wall.Elapsed.TotalSeconds.ToString("0") + " s wall");
            SetupHooks();
            SetupCapture();
            _started = true;
            StartCoroutine(CaptureWorker());
            // run until the final whistle
            float lastProgressT = Time.time; int lastPlay = -1;
            while (!_dir.Finished)
            {
                if (_dir.PlayNumber != lastPlay) { lastPlay = _dir.PlayNumber; lastProgressT = Time.time; }
                if (Time.time - lastProgressT > 150f) { Issue("FLOW_STALL_150S", "no new play for 150 s in phase " + _dir.Phase + " status=" + _dir.StatusText); Ev("STALL"); break; }
                if (_playsFinished >= _maxPlays) break;
                if (_wall.Elapsed.TotalMinutes > float.Parse(MatchWatchAudit.Arg("-pixMwWallMinutes", "110"), CI)) { Ev("wall budget reached"); break; }
                yield return new WaitForSeconds(1f);
            }
            for (int i = 0; i < 180; i++) yield return null;
            Finish(0);
        }

        int _oldCine; System.Diagnostics.Stopwatch _wall;
        int _maxPlays, _playsFinished;

        void Finish(int code)
        {
            if (_finishing) return; _finishing = true;
            try { WriteReports(); } catch (Exception e) { Debug.Log("[MatchWatch] report failed " + e); }
            Debug.Log("[MatchWatch] done");
            Application.logMessageReceived -= OnLog; PixSettings.Cinematics = _oldCine;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }

        // ---------------------------------------------------------------------------------------------------------- director hooks (play by play)
        sealed class PlayRec { public int No; public string Ctx; public string Kind; public string Off, Def; public float T0; public float LiveT0; public int Yards; public string Result; }
        PlayRec _cur;
        readonly List<string> _boxScoreLines = new List<string>();
        int _lastHome, _lastAway;
        float _lastPlayEnd = -1f;
        readonly List<float> _gapBetweenPlays = new List<float>();

        void SetupHooks()
        {
            _dir.PlayStarted += sim =>
            {
                _cur = new PlayRec { No = _dir.PlayNumber, Ctx = Ctx(), Kind = sim.Kind.ToString(), T0 = Time.time };
                if (_lastPlayEnd >= 0f) _gapBetweenPlays.Add(Time.time - _lastPlayEnd);
                Ev("PLAY START " + sim.Kind + " off=" + (sim.OffPlay != null ? sim.OffPlay.Name : "?") + " def=" + (sim.DefPlay != null ? sim.DefPlay.Name : "?") + " form=" + (sim.OffPlay != null ? sim.OffPlay.FormationName : "?"));
                _cur.Off = sim.OffPlay != null ? sim.OffPlay.Name : "?"; _cur.Def = sim.DefPlay != null ? sim.DefPlay.Name : "?";
                OnPlayStarted(sim);
            };
            _dir.PlayFinished += o =>
            {
                _playsFinished++; _lastPlayEnd = Time.time;
                var s = _dir.Flow.State;
                string line = "play#" + (_cur != null ? _cur.No : -1) + " " + (_cur != null ? _cur.Ctx : "") + " | " + o.Kind + " " + (_cur != null ? _cur.Off + " vs " + _cur.Def : "") + " => " + o.Headline + " \"" + o.Text + "\" score " + s.HomeScore + "-" + s.AwayScore + " dur " + (_cur != null ? (Time.time - _cur.T0).ToString("0.0", CI) : "?") + "s" + (o.PenaltyAccepted ? " PENALTY " + o.PenaltyName : "");
                _plays.AppendLine(line); Ev("PLAY END " + o.Headline + " " + o.Text);
                CheckOutcome(o); LogResult(o);
                OnPlayFinishedCapture(o);
            };
            _dir.SimEvent += e =>
            {
                OnSimEvent(e);
            };
            _dir.Milestone += s => { Ev("MILESTONE " + s); };
            _lastPhase = _dir.Phase; _phaseT0 = Time.time;
        }

        // outcome / scoreboard consistency
        void CheckOutcome(PlayOutcome o)
        {
            var s = _dir.Flow.State;
            int dh = s.HomeScore - _lastHome, da = s.AwayScore - _lastAway;
            int pts = dh + da;
            int expect = -1;
            switch (o.Headline)
            {
                case Headline.Touchdown: expect = 6; break;
                case Headline.FieldGoal: expect = 3; break;
                case Headline.ExtraPoint: expect = 1; break;
                case Headline.TwoPointGood: expect = 2; break;
                case Headline.Safety: expect = 2; break;
                case Headline.Incomplete: case Headline.Sack: case Headline.Punt: case Headline.Kickoff: case Headline.FieldGoalMissed: case Headline.Kneel: expect = 0; break;
            }
            if (expect >= 0 && pts != expect && !(o.Headline == Headline.Touchdown && (pts == 7 || pts == 8))) Issue("FLOW_SCORE_MISMATCH", o.Headline + " scored " + pts + " expected " + expect, pts);
            if (s.Down < 1 || s.Down > 4) Issue("FLOW_BAD_DOWN", "down=" + s.Down, s.Down);
            if (s.Distance < 1 && s.Phase == GamePhase.Scrimmage) Issue("FLOW_BAD_DISTANCE", "dist=" + s.Distance, s.Distance);
            if (s.BallSpot < 0 || s.BallSpot > 100) Issue("FLOW_BAD_SPOT", "spot=" + s.BallSpot, s.BallSpot);
            _lastHome = s.HomeScore; _lastAway = s.AwayScore;
            // fourth-down decisions
            _lastDownLog = s.Down;
        }
        int _lastDownLog;

        // ---------------------------------------------------------------------------------------------------------- per-frame driver
        void LateUpdate()
        {
            if (!_started || _dir == null || _finishing) return;
            _frame++;
            float wallMs = (float)(_wall.Elapsed.TotalMilliseconds - _lastWallMs); _lastWallMs = _wall.Elapsed.TotalMilliseconds;
            if (!_capturing && wallMs > 120f && _frame > 120) { _hitches++; if (_hitches < 400) Issue("PERF_HITCH_OVER_120MS", "phase " + _dir.Phase + " wall " + wallMs.ToString("0") + " ms (editor, camera not rendering)", wallMs); }
            if (_dir.Phase != _lastPhase)
            {
                Ev("PHASE " + _lastPhase + " -> " + _dir.Phase + " after " + (Time.time - _phaseT0).ToString("0.0", CI) + " s");
                PhaseSeconds(_lastPhase, Time.time - _phaseT0);
                _lastPhase = _dir.Phase; _phaseT0 = Time.time;
                OnPhaseChanged(_dir.Phase);
            }
            try { AudioProbe(); } catch (Exception e) { Issue("AUDIT_TOOL_EXCEPTION_AUDIO", e.Message); }
            try { PerFrameChecks(); } catch (Exception e) { Issue("AUDIT_TOOL_EXCEPTION", e.GetType().Name + " " + e.Message + " " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            try { RingTick(); } catch (Exception e) { Issue("AUDIT_TOOL_EXCEPTION_RING", e.Message); }
            if ((_frame % 1800) == 0) { try { Flush(); } catch { } }
        }
        double _lastWallMs; int _hitches;

        readonly Dictionary<MatchPhase, List<float>> _phaseSecs = new Dictionary<MatchPhase, List<float>>();
        void PhaseSeconds(MatchPhase p, float s) { List<float> l; if (!_phaseSecs.TryGetValue(p, out l)) _phaseSecs[p] = l = new List<float>(); l.Add(s); }

        void Flush()
        {
            File.AppendAllText(_out + "/events.log", _events.ToString()); _events.Length = 0;
            File.WriteAllText(_out + "/plays.txt", _plays.ToString());
            WriteIssues(_out + "/issues_partial.txt");
        }

        void WriteReports()
        {
            Flush();
            WriteIssues(_out + "/issues.txt");
            var sb = new StringBuilder();
            sb.AppendLine("== log messages (exceptions / errors / warnings, deduped) ==");
            foreach (var kv in _logs.OrderByDescending(k => k.Value.Type == LogType.Exception ? 1000000 + k.Value.Count : k.Value.Type == LogType.Error ? 500000 + k.Value.Count : k.Value.Count))
                sb.AppendLine("x" + kv.Value.Count + " " + kv.Value.Type + " first at [" + kv.Value.Ctx + "]: " + kv.Value.First.Replace("\n", " ") + (kv.Value.Type == LogType.Exception || kv.Value.Type == LogType.Error ? "\n      stack: " + kv.Value.Stack.Replace("\n", " | ") : ""));
            sb.AppendLine();
            sb.AppendLine("== phase durations (game seconds) ==");
            foreach (var kv in _phaseSecs) { var l = kv.Value.OrderBy(x => x).ToList(); sb.AppendLine(kv.Key + " n=" + l.Count + " min " + l[0].ToString("0.0", CI) + " median " + l[l.Count / 2].ToString("0.0", CI) + " p90 " + l[(int)(l.Count * 0.9)].ToString("0.0", CI) + " max " + l[l.Count - 1].ToString("0.0", CI)); }
            if (_gapBetweenPlays.Count > 0) { var l = _gapBetweenPlays.OrderBy(x => x).ToList(); sb.AppendLine("gap PlayFinished -> next PlayStarted n=" + l.Count + " min " + l[0].ToString("0.0", CI) + " median " + l[l.Count / 2].ToString("0.0", CI) + " p90 " + l[(int)(l.Count * 0.9)].ToString("0.0", CI) + " max " + l[l.Count - 1].ToString("0.0", CI)); }
            sb.AppendLine();
            sb.AppendLine(ExtraReport());
            sb.AppendLine(AudioReport());
            File.WriteAllText(_out + "/summary.txt", sb.ToString());
            var fs = _dir != null && _dir.Flow != null ? _dir.Flow.State : null;
            File.WriteAllText(_out + "/final.txt", "plays " + _playsFinished + " frames " + _frame + " wall " + _wall.Elapsed.TotalMinutes.ToString("0.0", CI) + " min" + (fs != null ? " score " + fs.HomeScore + "-" + fs.AwayScore + " Q" + fs.Quarter + " " + fs.ClockDisplay : "") + " finished=" + (_dir != null && _dir.Finished));
        }

        void WriteIssues(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("issue type | count | plays affected | max | samples");
            foreach (var g in _issues.Values.OrderByDescending(x => x.Count))
            {
                sb.AppendLine(g.Type + " | " + g.Count + " | " + g.Plays.Count + " | " + g.Max.ToString("0.###", CI));
                foreach (var s in g.Samples) sb.AppendLine("      " + s);
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
