using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using PixFootball.UI;
using UnityEngine;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Presentation
{
    /// <summary>
    /// v0.11 WATCH+HUD probe (QA only, flag <c>-pixWatchProbe</c>): starts an ordinary game through the real entry point (GameSession.StartMatch with a normal config, so it proves the watch-mode default),
    /// watches it on the broadcast camera and takes a screenshot of every phase (intro, kickoff, pre-snap, snap, pass in the air, catch, run, tackle, banners, field goal, punt, extra point, halftime, final),
    /// writing the measured HUD rectangles and overlaps of each frame next to it (<see cref="HudBounds"/>).
    /// Args: -pixWatchOut dir, -pixWatchQuarter minutes (2), -pixWatchSpeed x (2), -pixWatchMinutes real-time budget (16), -pixWatchSeed n, -pixWatchShotsOnly (no bounds), -pixWatchMaxShots n.
    /// </summary>
    public sealed class WatchProbe : MonoBehaviour
    {
        public const string Flag = "-pixWatchProbe";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        static string Arg(string name, string def) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return def; }
        static float ArgF(string name, float def) { float v; return float.TryParse(Arg(name, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def; }

        /// <summary>Set by an editor hook: switches the game view to a resolution (w, h).</summary>
        public static Action<int, int> SetResolution;

        string _out;
        readonly StringBuilder _log = new StringBuilder(), _bounds = new StringBuilder();
        readonly HashSet<string> _done = new HashSet<string>();
        readonly Dictionary<string, int> _count = new Dictionary<string, int>();
        MatchDirector _dir; int _shots, _maxShots; bool _busy; int _errors;
        readonly List<string> _headlines = new List<string>();
        readonly Dictionary<string, int[]> _ballFrames = new Dictionary<string, int[]>();
        readonly Dictionary<string, float[]> _visStats = new Dictionary<string, float[]>();
        bool _prevOver; int _camHitchJumps; Vector3 _lastCamPos; bool _haveCam; int _camJumps; float _camJumpMax;
        int _dbg; int _camStep; int _plays; string _lastHeadline = ""; float _lastPlayEnd = -99f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has || FindFirstObjectByType<WatchProbe>() != null) return;
            var go = new GameObject("PixWatchProbe"); DontDestroyOnLoad(go); go.AddComponent<WatchProbe>();
        }

        void Log(string s) { _log.AppendLine(s); Debug.Log("[WatchProbe] " + s); }

        void Awake()
        {
            _out = Arg("-pixWatchOut", Path.Combine(Application.dataPath, "..", "Logs", "watch"));
            _maxShots = (int)ArgF("-pixWatchMaxShots", 90f);
            Directory.CreateDirectory(_out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) { _errors++; if (_errors < 40) _log.AppendLine("  ! " + t + ": " + c + (t == LogType.Exception ? "\n" + st : "")); } };
        }

        bool Once(string key) { return _done.Add(key); }
        bool Limit(string key, int n) { int c; _count.TryGetValue(key, out c); if (c >= n) return false; _count[key] = c + 1; return true; }

        IEnumerator Start()
        {
            Log("watch probe " + Screen.width + "x" + Screen.height + " user control " + WatchMode.UserControl + " (watch " + WatchMode.Enabled + ")");
            yield return null; yield return null;
            var cfg = new MatchConfig { HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = (int)ArgF("-pixWatchQuarter", 2f), UserTeam = CoreTeam.Home, Seed = (int)ArgF("-pixWatchSeed", 20261111), CpuVsCpu = false, Difficulty = Difficulty.Pro };
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance.Flow == null) && wait < 180f)
            {
                wait += Time.unscaledDeltaTime;
                var d0 = MatchDirector.Instance;
                if (d0 != null && d0.Phase == MatchPhase.CoinToss && Once("toss")) { yield return new WaitForSecondsRealtime(0.8f); yield return Shot("toss"); }
                yield return null;
            }
            _dir = MatchDirector.Instance;
            if (_dir == null || _dir.Flow == null) { Log("FAIL: match did not start"); Finish(2); yield break; }
            Log("match started in " + wait.ToString("0.0") + "s; watch active " + _dir.WatchActive + ", cpu vs cpu " + _dir.Config.CpuVsCpu + ", camera " + _dir.Cams.Mode);
            if (!_dir.WatchActive) Log("FAIL: the game is not a watch game");
            MatchCameraRig.DebugJumps = true;
            _dir.PlayFinished += o => { _plays++; _lastHeadline = o.Headline.ToString().ToLowerInvariant(); _lastPlayEnd = Time.unscaledTime; _headlines.Add(o.Headline.ToString()); };
            float speed = ArgF("-pixWatchSpeed", 2f);
            WatchMode.SpeedIndex = speed > 1.5f ? 1 : 0;
            float start = Time.unscaledTime, budget = ArgF("-pixWatchMinutes", 16f) * 60f;
            float phaseT = 0f; MatchPhase lastPhase = MatchPhase.Booting; int lastPlayNo = -1; string lastKind = "";
            var bd = BroadcastDirector.Instance;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pixWatchStress") >= 0) { yield return Stress(); yield break; }
            while (!_dir.Finished && Time.unscaledTime - start < budget && _dir != null)
            {
                var sim = _dir.Sim; var ph = _dir.Phase;
                if (ph != lastPhase || _dir.PlayNumber != lastPlayNo) { lastPhase = ph; lastPlayNo = _dir.PlayNumber; phaseT = 0f; }
                phaseT += Time.unscaledDeltaTime;
                string kind = sim != null ? sim.Kind.ToString().ToLowerInvariant() : "none";
                string show = bd != null ? (bd.CurrentShow ?? "") : "";
                if (bd != null && (bd.Busy || show.Length > 0) && ph != MatchPhase.Final)
                {
                    if (show.Length > 0 && Limit("show_" + show, 2) && phaseT > 0.1f) { yield return new WaitForSecondsRealtime(show == "intro" ? 3.5f : 1.2f); yield return Shot("show_" + show); }
                }
                if (ph == MatchPhase.PreSnap && sim != null)
                {
                    string k = "pre_" + kind;
                    if (phaseT > 0.9f && Limit(k, kind == "scrimmage" ? 3 : 2)) yield return Shot(k);
                }
                if (ph == MatchPhase.Live && sim != null)
                {
                    string k = "live_" + kind;
                    float t = sim.Time;
                    if (kind == "kickoff") { if (t > 0.9f && Once("ko1_" + _dir.PlayNumber) && Limit("ko_live", 3)) yield return Shot("kickoff_t1"); if (t > 3.0f && Once("ko3_" + _dir.PlayNumber) && Limit("ko_live3", 3)) yield return Shot("kickoff_t3"); if (t > 5.5f && Once("ko5_" + _dir.PlayNumber) && Limit("ko_live5", 2)) yield return Shot("kickoff_t5.5"); }
                    else if (kind == "punt") { if (sim.Ball.State == BallState.Kick && sim.Ball.FlightTime > 1.2f && Once("pt_" + _dir.PlayNumber) && Limit("punt_air", 2)) yield return Shot("punt_air"); }
                    else if (kind == "fieldgoal") { if (sim.Ball.State == BallState.Kick && sim.Ball.FlightTime > 0.8f && Once("fg_" + _dir.PlayNumber) && Limit("fg_air", 2)) yield return Shot("fg_air"); }
                    else if (kind == "scrimmage")
                    {
                        if (sim.Ball.State == BallState.Pass && sim.Ball.FlightTime > 0.5f && Once("pass_" + _dir.PlayNumber) && Limit("pass_air", 4)) yield return Shot("pass_air");
                        if (sim.Carrier != null && sim.Carrier.Slot != 0 && sim.Time > 2.0f && Once("run_" + _dir.PlayNumber) && Limit("run", 3)) yield return Shot("run");
                        if (sim.Time > 0.8f && sim.Agents[0].HasBall && !sim.PassIsThrown && Once("dropback_" + _dir.PlayNumber) && Limit("dropback", 2)) yield return Shot("dropback");
                    }
                }
                if (ph == MatchPhase.PostPlay && sim != null)
                {
                    float since = Time.unscaledTime - _lastPlayEnd;
                    string k1 = "post_" + _lastHeadline;
                    if (since > 0.45f && Once("p1_" + _dir.PlayNumber) && Limit(k1, 2)) yield return Shot(k1 + "_a");
                    if (since > 1.9f && Once("p2_" + _dir.PlayNumber) && Limit(k1 + "_b", 1)) yield return Shot(k1 + "_b");
                }
                if (_plays >= 12 && ph == MatchPhase.PreSnap && phaseT > 0.6f && Once("pausetest"))
                {
                    _dir.WatchTogglePause(); yield return new WaitForSecondsRealtime(1.0f);
                    Log("pause open: " + (_dir.Ui != null && _dir.Ui.PauseOpen) + ", paused " + _dir.Paused);
                    yield return Shot("pause_menu");
                    _dir.WatchTogglePause(); yield return new WaitForSecondsRealtime(0.3f);
                    Log("after resume: paused " + _dir.Paused + ", time scale " + Time.timeScale);
                }
                // framing metric: while a kick or a pass is in the air, is the ball inside the picture (not under the score bar, not off an edge)?
                if (ph == MatchPhase.Live && sim != null && _dir.Stage != null && _dir.Stage.Ball != null && _dir.MainCamera != null && !_dir.Cams.Overridden && (sim.Ball.State == BallState.Kick || sim.Ball.State == BallState.Pass) && sim.Ball.FlightTime > 0.15f)
                {
                    var vp = _dir.MainCamera.WorldToViewportPoint(_dir.Stage.Ball.Position);
                    bool inside = vp.z > 0f && vp.x > 0.03f && vp.x < 0.97f && vp.y > 0.10f && vp.y < 0.97f;
                    string mk = sim.Ball.State == BallState.Kick ? (sim.Kind == PlayKind.Kickoff ? "kickoff" : sim.Kind == PlayKind.Punt ? "punt" : "fieldgoal") : "pass";
                    int[] a; if (!_ballFrames.TryGetValue(mk, out a)) { a = new int[2]; _ballFrames[mk] = a; }
                    a[0]++; if (inside) a[1]++;
                    if (!inside && (mk == "punt" || mk == "kickoff") && a[0] % 40 == 0 && _dbg++ < 16) Log("  off-frame " + mk + " t=" + sim.Ball.FlightTime.ToString("0.00") + " vp=" + vp.ToString("0.00") + " ball=" + _dir.Stage.Ball.Position.ToString("0.0") + " cam=" + _dir.MainCamera.transform.position.ToString("0.0") + " fov=" + _dir.MainCamera.fieldOfView.ToString("0.0") + " mode=" + _dir.Cams.Mode);
                }
                // framing metric: how many of the 22 bodies are inside the picture during a kickoff / punt, and how tall a body is on screen
                if ((ph == MatchPhase.PreSnap || ph == MatchPhase.Live) && sim != null && (sim.Kind == PlayKind.Kickoff || sim.Kind == PlayKind.Punt || sim.Kind == PlayKind.Scrimmage || sim.Kind == PlayKind.FieldGoal) && _dir.Stage != null && _dir.MainCamera != null && !_dir.Cams.Overridden && _dir.Cams.Mode == CameraMode.Broadcast)
                {
                    int vis = 0; float hsum = 0f; var cam = _dir.MainCamera;
                    for (int i = 0; i < 22; i++)
                    {
                        var v = _dir.Stage.Views[i]; if (v == null) continue;
                        var p0 = cam.WorldToViewportPoint(v.transform.position); var p1 = cam.WorldToViewportPoint(v.transform.position + Vector3.up * 1.9f);
                        if (p0.z > 0f && p0.x > 0.01f && p0.x < 0.99f && p0.y > 0.08f && p1.y < 0.99f) { vis++; hsum += (p1.y - p0.y) * Screen.height; }
                    }
                    string vk = sim.Kind.ToString().ToLowerInvariant() + "_" + (ph == MatchPhase.PreSnap ? "set" : (sim.Ball.State == BallState.Held ? "carry" : "air"));
                    float[] a2; if (!_visStats.TryGetValue(vk, out a2)) { a2 = new float[5]; a2[3] = 22f; _visStats[vk] = a2; }
                    a2[0]++; if (vis == 22) a2[1]++; a2[2] += vis > 0 ? hsum / vis : 0f; a2[3] = Mathf.Min(a2[3], vis); a2[4] += vis;
                }
                // camera teleports: a live-camera move over 6 m in one frame (the sideline flip / hard cut at each snap)
                if (_dir.MainCamera != null && _dir.Cams != null && !_dir.Cams.Overridden && !(_dir.Phase == MatchPhase.Final))
                {
                    Vector3 cp = _dir.MainCamera.transform.position; if (_dir.Cams.Mode != CameraMode.Broadcast) { _haveCam = false; goto camDone; }
                    if (_haveCam) { float jd = (cp - _lastCamPos).magnitude; if (jd > 6f && Time.unscaledDeltaTime > 0.1f) _camHitchJumps++; else if (jd > 6f && Time.deltaTime > 0f) { _camJumps++; _camJumpMax = Mathf.Max(_camJumpMax, jd); if (_camJumps <= 12) Log("  cam jump [prevOver " + _prevOver + " shot " + (BroadcastDirector.Instance != null && BroadcastDirector.Instance.ShotActive) + " busy " + (BroadcastDirector.Instance != null && BroadcastDirector.Instance.Busy) + "] " + jd.ToString("0.0") + " m phase " + _dir.Phase + " kind " + (_dir.Sim != null ? _dir.Sim.Kind.ToString() : "-") + " simT " + (_dir.Sim != null ? _dir.Sim.Time.ToString("0.0") : "-") + " from " + _lastCamPos.ToString("0") + " to " + cp.ToString("0") + " fov " + _dir.MainCamera.fieldOfView.ToString("0")); } }
                    _lastCamPos = cp; _haveCam = true; _prevOver = _dir.Cams.Overridden;
                }
                else _haveCam = false;
                camDone:;
                // camera cycling in a watch game: broadcast -> behind (3 presets) -> broadcast, one shot of each while a scrimmage play is live
                if (_plays >= 6 && _camStep < 4 && ph == MatchPhase.PreSnap && sim != null && sim.Kind == PlayKind.Scrimmage && phaseT > 0.3f && Once("camstep_" + _camStep + "_" + _dir.PlayNumber))
                {
                    _dir.WatchCycleCamera(); _camStep++;
                    yield return new WaitForSecondsRealtime(0.8f);
                    yield return Shot("cam" + _camStep + "_" + _dir.Cams.Mode + (_dir.Cams.Mode == CameraMode.Behind ? "_" + PixSettings.UserCameraStyle : "") + "_pre");
                }
                if (_camStep > 0 && _camStep < 5 && ph == MatchPhase.Live && sim != null && sim.Kind == PlayKind.Scrimmage && sim.Time > 1.8f && Once("camlive_" + _camStep)) yield return Shot("cam" + _camStep + "_live");
                if (ph == MatchPhase.Break && phaseT > 1.0f && Once("break_" + _dir.Flow.State.Quarter)) yield return Shot("break_q" + _dir.Flow.State.Quarter);
                lastKind = kind;
                yield return null;
            }
            Log("camera jumps over 6 m in one frame: " + _camJumps + " (largest " + _camJumpMax.ToString("0.0") + " m); moves over 6 m in a hitch frame (> 0.1 s): " + _camHitchJumps);
            foreach (var kv in _visStats) Log("bodies [" + kv.Key + "]: all 22 in frame " + (100f * kv.Value[1] / Mathf.Max(1f, kv.Value[0])).ToString("0.0") + " % of " + kv.Value[0] + " frames, mean in frame " + (kv.Value[4] / Mathf.Max(1f, kv.Value[0])).ToString("0.0") + ", min " + kv.Value[3] + ", mean body height " + (kv.Value[2] / Mathf.Max(1f, kv.Value[0])).ToString("0") + " px");
            foreach (var kv in _ballFrames) Log("ball in frame [" + kv.Key + "]: " + kv.Value[1] + " / " + kv.Value[0] + " frames = " + (100f * kv.Value[1] / Mathf.Max(1, kv.Value[0])).ToString("0.0") + " %");
            Log("loop ended: finished " + _dir.Finished + ", plays " + _plays + ", real seconds " + (Time.unscaledTime - start).ToString("0"));
            if (_dir.Finished) { yield return new WaitForSecondsRealtime(1.5f); yield return Shot("final"); }
            Finish(0);
        }


        // ------------------------------------------------------------------------------------------ stress: every HUD piece at once, measured per resolution
        static readonly int[][] StressSizes = { new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 }, new[] { 3440, 1440 }, new[] { 1440, 1080 } };

        IEnumerator Stress()
        {
            var bd = BroadcastDirector.Instance;
            while (!(_dir.Phase == MatchPhase.PreSnap && _dir.Sim != null && _dir.Sim.Kind == PlayKind.Scrimmage && _dir.PlayNumber >= 3)) yield return null;
            yield return new WaitForSecondsRealtime(2.5f);
            GameSession.SetPaused(true);
            var ui = _dir.Ui;
            var s = new ScoreSnapshot { HomeAbbr = "BUF", AwayAbbr = "MIA", HomeCrest = "crest_buffalo_ironhorns", AwayCrest = "crest_miami_reefrunners", HomeColor = new Color(0.05f, 0.24f, 0.7f), AwayColor = new Color(0.98f, 0.8f, 0.1f), HomeScore = 14, AwayScore = 10, Quarter = "2ND", Clock = "4:12", PlayClock = 18, DownDistance = "3RD & 7", Spot = "BUF 33", PossessionHome = true, HomeTimeouts = 2, AwayTimeouts = 3, FlagText = "FLAG - MIA #77 - OFFENSIVE HOLDING, 10 YARDS" };
            ui.SetScore(s);
            ui.ShowBanner("TOUCHDOWN", new Color(0.95f, 0.78f, 0.1f), 99f, "BUFFALO IRONHORNS  -  #12 J. DOE  -  34 YD PASS");
            ui.ShowWhistle("TACKLED BY #47  -  6 YARD GAIN", 99f);
            ui.ShowToast("TIMEOUT CALLED", 99f);
            ui.SetKickMeter(true, 0.5f, "Perfect", 0.5f, "Centered");
            ui.SetWind(45f, 14f);
            ui.SetHint("[A][B][X][Y][LB][RB] THROW (HOLD = BULLET, TAP = LOB)   [RS] LEAD   [LT] ROUTES   [LS] SCRAMBLE");
            if (bd != null && bd.Overlays != null)
            {
                var a = new TeamVis("MIA", "Miami Reefrunners", "crest_miami_reefrunners", new Color(0.98f, 0.8f, 0.1f)); var h = new TeamVis("BUF", "Buffalo Ironhorns", "crest_buffalo_ironhorns", new Color(0.05f, 0.24f, 0.7f));
                bd.Overlays.ShowPenalty("PENALTY - OFFENSIVE HOLDING", "MIA #77   10 YARDS", a, 99f);
                bd.Overlays.ShowScoreUpdate(a, 10, h, 14, "BUF: TOUCHDOWN", "4:12", 99f, 1);
                bd.Overlays.ShowNextPlay("3RD & 7", "BUF 33", "4:12", 99f);
            }
            foreach (var sz in StressSizes)
            {
                if (SetResolution != null) SetResolution(sz[0], sz[1]);
                int frames = 0; while ((Screen.width != sz[0] || Screen.height != sz[1]) && frames++ < 200) yield return null;
                for (int i = 0; i < 25; i++) yield return null;
                var groups = HudBounds.MeasureAll();
                _bounds.AppendLine(HudBounds.Report("stress", groups, Screen.width, Screen.height));
                if (sz[0] == 1920 || sz[0] == 3440) { yield return new WaitForEndOfFrame(); var tex = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(_out, "stress_" + sz[0] + "x" + sz[1] + ".png"), tex.EncodeToPNG()); Destroy(tex); }
                Log("stress " + Screen.width + "x" + Screen.height + ": " + groups.Count + " groups, " + HudBounds.Overlaps(groups).Count + " overlaps");
            }
            Finish(0);
        }

        IEnumerator Shot(string key)
        {
            if (_busy || _shots >= _maxShots) yield break;
            _busy = true;
            yield return new WaitForEndOfFrame();
            string name = (_shots + 1).ToString("00") + "_" + key;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            if (_dir != null && !_dir.Finished || true)
            {
                var groups = HudBounds.MeasureAll();
                string st = _dir != null && _dir.Flow != null ? " Q" + _dir.Flow.State.Quarter + " " + _dir.Flow.State.ClockDisplay + " " + _dir.Phase + " cam " + (_dir.Cams != null ? _dir.Cams.Mode.ToString() : "") : "";
                _bounds.AppendLine(HudBounds.Report(name + st, groups, Screen.width, Screen.height));
            }
            _shots++;
            Log("shot " + name);
            _busy = false;
        }

        void Finish(int code)
        {
            try
            {
                File.WriteAllText(Path.Combine(_out, "watch_probe.txt"), _log.ToString());
                File.WriteAllText(Path.Combine(_out, "hud_bounds.txt"), _bounds.ToString());
            }
            catch (Exception e) { Debug.LogError(e); }
            Debug.Log("[WatchProbe] done, errors " + _errors);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
