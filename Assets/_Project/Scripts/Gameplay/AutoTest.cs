using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Smoke test for built players: run the exe with -pixAutotest [-pixAutotestOut dir] [-pixAutotestMatchOnly] [-pixAutotestSpeed 2].
    /// Part 1 (v0.1): menu, team select, free roam cameras and moves. Part 2 (v0.2): a full CPU-vs-CPU game with 2-minute quarters in which the
    /// user's side is played by the director's autopilot (snaps, throws to a receiver, runs with jukes, tackles), with screenshots of the key
    /// moments (call menu, formation, pass, tackle, touchdown banner, halftime, final score), FPS logging and a box score, then exits.
    /// </summary>
    public partial class AutoTest : MonoBehaviour
    {
        public static bool Requested
        {
            get { foreach (string a in Environment.GetCommandLineArgs()) if (a == "-pixAutotest" || a == "-pixAutotestCareer") return true; return false; }
        }
        static bool Flag(string name) { foreach (string a in Environment.GetCommandLineArgs()) if (a == name) return true; return false; }
        static string ArgValueOf(string name) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return null; }

        string _out;
        readonly StringBuilder _log = new StringBuilder();
        readonly List<float> _frames = new List<float>();
        readonly List<float> _matchFrames = new List<float>();
        int _errors;
        bool _recording;
        float _maxMph;
        readonly HashSet<string> _shots = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            var go = new GameObject("PixAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoTest>();
        }

        void Log(string s) { _log.AppendLine(s); Debug.Log("[AutoTest] " + s); }

        // ---- v0.5: the Presentation assembly (replays, shows, overlays) registers its own autotest segment here
        public static readonly List<Func<AutoTest, IEnumerator>> ExtraParts = new List<Func<AutoTest, IEnumerator>>();
        public static bool BroadcastOnly { get { return Flag("-pixAutotestBroadcastOnly"); } }
        /// <summary>v0.8: the career play-through (-pixAutotestCareer), registered by the UI assembly.</summary>
        public static Func<AutoTest, IEnumerator> CareerPart;
        public static string ArgOf(string name) { return ArgValueOf(name); }
        public static bool HasFlag(string name) { return Flag(name); }
        public void AddFrameSample(float dt) { _matchFrames.Add(dt); }
        public IEnumerator Capture(string name) { return Shot(name); }
        public void LogLine(string s) { Log(s); }
        public int ErrorCount { get { return _errors; } }
        public void StartFpsCapture() { _frames.Clear(); _recording = true; }
        public void StopFpsCapture(string label) { _recording = false; ReportFps(label); }
        public string OutputDirectory { get { return _out; } }
        public int LastCaptureFrame { get; private set; }

        void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            _out = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-pixAutotestOut") _out = args[i + 1];
            if (string.IsNullOrEmpty(_out)) _out = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "autotest"));
            Directory.CreateDirectory(_out);
            for (int i = 0; i < args.Length - 1; i++)
            {
                int tier; float scale; int upscaler;
                if (args[i] == "-pixUpscaler" && int.TryParse(args[i + 1], out upscaler)) UpscalerControl.Override(upscaler);
                if (args[i] == "-pixLodPx") { var lp = args[i + 1].Split(','); if (lp.Length == 3) { float a, b, c; var ci = System.Globalization.CultureInfo.InvariantCulture; if (float.TryParse(lp[0], System.Globalization.NumberStyles.Float, ci, out a) && float.TryParse(lp[1], System.Globalization.NumberStyles.Float, ci, out b) && float.TryParse(lp[2], System.Globalization.NumberStyles.Float, ci, out c)) { PlayerLod.Lod0Px = a; PlayerLod.Lod1Px = b; PlayerLod.Lod2Px = c; } } }   // v0.9 GFX A/B of the screen-size LOD edges (px of a 1.9 m player at 1440 p)
                if (args[i] == "-pixTier" && int.TryParse(args[i + 1], out tier)) { QualityTiers.Override(tier, -1f); }
                float lodv;
                if ((args[i] == "-pixLodNear" || args[i] == "-pixLodMid" || args[i] == "-pixLodFar") && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lodv))   // v0.7 A/B of the player LOD distances
                { PlayerLod.UseScreenSize = false; if (args[i] == "-pixLodNear") PlayerLod.Near = lodv; else if (args[i] == "-pixLodMid") PlayerLod.Mid = lodv; else PlayerLod.Far = lodv; }
                if (args[i] == "-pixRenderScale" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale)) { QualityTiers.Override(-1, scale); }
            }
            if (!Flag("-pixAutotestVsync")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }   // measure real frame cost, not the display refresh
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) { _errors++; if (_errors < 40) _log.AppendLine("  ! " + t + ": " + c + (t == LogType.Exception ? "\n" + st : "")); } };
        }

        int _skipFrames;
        readonly FrameTiming[] _ft = new FrameTiming[1];
        double _cpuSum, _gpuSum, _mainSum, _renderSum; int _ftN;

        void Update()
        {
            if (_recording) _frames.Add(Time.unscaledDeltaTime);
            var d = MatchDirector.Instance;
            if (d != null && d.Phase == MatchPhase.Live && _skipFrames <= 0)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _ft) > 0 && _ft[0].gpuFrameTime > 0) { _cpuSum += _ft[0].cpuFrameTime; _gpuSum += _ft[0].gpuFrameTime; _mainSum += _ft[0].cpuMainThreadFrameTime; _renderSum += _ft[0].cpuRenderThreadFrameTime; _ftN++; }
            }
        }

        IEnumerator Start()
        {
            Log("PIX autotest starting. " + Screen.width + "x" + Screen.height + " fullscreen=" + Screen.fullScreen + " gpu=" + SystemInfo.graphicsDeviceName + " api=" + SystemInfo.graphicsDeviceType + " quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            Log("scene: " + SceneManager.GetActiveScene().name);
            if (Flag("-pixAutotestCareer"))
            {
                if (CareerPart == null) { Log("FAIL: the career autotest is not registered"); Finish(2); yield break; }
                yield return CareerPart(this);
                ReportMatchFps();
                Log("errors/exceptions logged: " + _errors);
                Finish(_errors > 0 ? 3 : 0);
                yield break;
            }
            if (Flag("-pixAutotestLookOnly"))
            {
                yield return new WaitForSeconds(1.0f);
                yield return LookPart();
                Log("errors/exceptions logged: " + _errors);
                Finish(_errors > 0 ? 3 : 0);
                yield break;
            }
            if (Flag("-pixAutotestContactOnly"))
            {
                yield return ContactPart();
                Log("errors/exceptions logged: " + _errors);
                Finish(_errors > 0 ? 3 : 0);
                yield break;
            }
            if (BroadcastOnly)
            {
                yield return new WaitForSeconds(1.0f);
                foreach (var part in ExtraParts.ToArray()) yield return part(this);
                Log("errors/exceptions logged: " + _errors);
                Finish(_errors > 0 ? 3 : 0);
                yield break;
            }
            if (!Flag("-pixAutotestMatchOnly"))
            {
                yield return new WaitForSeconds(3.0f);
                yield return Shot("menu");
                var menu = FindFirstObjectByType<MonoBehaviour>(FindObjectsInactive.Include);
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (mb != null && mb.GetType().Name == "MainMenuController") { mb.SendMessage("OpenTeamSelect", SendMessageOptions.DontRequireReceiver); break; }
                yield return new WaitForSeconds(1.2f);
                yield return Shot("teamselect");
                if (Flag("-pixAutotestMenuOnly")) { Log("menu-only autotest finished"); Finish(0); yield break; }
                yield return FreeRoamPart();
            }
            if (!Flag("-pixAutotestV3Only") && !Flag("-pixAutotestCollegeOnly")) yield return MatchPart();
            if (!Flag("-pixAutotestCollegeOnly") && !Flag("-pixAutotestNoV3")) yield return V3Part();
            if (!Flag("-pixAutotestV3Only") && !Flag("-pixAutotestNoV3")) yield return CollegePart();
            if (!Flag("-pixAutotestV3Only") && !Flag("-pixAutotestNoV3") && !Flag("-pixAutotestNoContact")) yield return ContactPart(true);
            if (!Flag("-pixAutotestV3Only") && !Flag("-pixAutotestNoV3") && !Flag("-pixAutotestNoBroadcast")) foreach (var part in ExtraParts.ToArray()) yield return part(this);
            Log("errors/exceptions logged: " + _errors);
            Finish(_errors > 0 ? 3 : 0);
        }

        // ============================================================================================ free roam (v0.1)
        IEnumerator FreeRoamPart()
        {
            GameSession.StartFreeRoam();
            float wait = 0f;
            while ((FreeRoamGame.Instance == null || FreeRoamGame.Instance.Player == null || FreeRoamGame.Instance.Cameras == null) && wait < 60f) { wait += Time.unscaledDeltaTime; yield return null; }
            var game = FreeRoamGame.Instance;
            if (game == null) { Log("FAIL: free roam stadium did not load"); yield break; }
            Log("scene: " + SceneManager.GetActiveScene().name + " player spawned in " + wait.ToString("0.0") + "s");
            game.Lighting.Apply(LightingPreset.Night, false);   // not remembered: a test run must not rewrite the developer's Settings (QA-894)
            game.Cameras.SetMode(CameraMode.Broadcast, true);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("night_broadcast_idle");

            _frames.Clear(); _recording = true;
            float t0 = Time.unscaledTime;
            var player = game.Player;
            Vector3 startPos = player.Position;
            var moves = new[] { new KeyValuePair<float, string>(3.3f, "JukeLeft"), new KeyValuePair<float, string>(4.0f, "JukeRight"), new KeyValuePair<float, string>(4.7f, "Spin"),
                new KeyValuePair<float, string>(5.4f, "Truck"), new KeyValuePair<float, string>(6.0f, "Hurdle"), new KeyValuePair<float, string>(6.9f, "Dive") };
            int mi = 0; bool shotSprint = false;
            while (true)
            {
                float t = Time.unscaledTime - t0;
                if (t > 8.5f) break;
                var f = new InputFrame { MoveIsWorld = true };
                Vector2 dir;
                if (t < 0.5f) dir = Vector2.zero; else if (t < 2.4f) dir = new Vector2(0f, 1f); else if (t < 3.0f) dir = new Vector2(-0.9f, 0.45f).normalized;
                else if (t < 4.4f) dir = new Vector2(0.55f, 0.85f).normalized; else if (t < 6.9f) dir = new Vector2(0f, 1f); else dir = Vector2.zero;
                f.Move = dir; f.Sprint = t > 1.0f && t < 7.0f;
                while (mi < moves.Length && t >= moves[mi].Key)
                {
                    switch (moves[mi].Value)
                    {
                        case "JukeLeft": f.JukeLeft = true; break; case "JukeRight": f.JukeRight = true; break; case "Spin": f.Spin = true; break;
                        case "Truck": f.Truck = true; break; case "Hurdle": f.Hurdle = true; break; case "Dive": f.Dive = true; break;
                    }
                    mi++;
                }
                FreeRoamGame.ScriptedInput = f;
                _maxMph = Mathf.Max(_maxMph, player.SpeedMph);
                if (!shotSprint && t > 2.2f) { shotSprint = true; _recording = false; yield return Shot("night_broadcast_sprint"); _recording = true; }
                yield return null;
            }
            FreeRoamGame.ScriptedInput = new InputFrame { MoveIsWorld = true };
            _recording = false;
            Log("free roam run moved " + Vector3.Distance(startPos, player.Position).ToString("0.0") + " m, max speed " + _maxMph.ToString("0.0") + " mph");
            ReportFps("free roam (night, broadcast cam)");
            FreeRoamGame.ScriptedInput = null;
        }

        // ============================================================================================ match (v0.2)
        IEnumerator MatchPart()
        {
            var cfg = new MatchConfig
            {
                HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = 2, UserTeam = Team.Home, Seed = 20261021, CpuVsCpu = false, Difficulty = Difficulty.Pro
            };
            // team, rules, difficulty and seed can be chosen from the command line (QA-303): -pixAutotestHome <id> -pixAutotestAway <id> -pixAutotestRules pro|college -pixAutotestSeed <n> -pixAutotestQuarter <min>
            string v;
            if ((v = ArgValueOf("-pixAutotestHome")) != null) cfg.HomeId = v;
            if ((v = ArgValueOf("-pixAutotestAway")) != null) cfg.AwayId = v;
            if ((v = ArgValueOf("-pixAutotestRules")) != null) cfg.CollegeRules = v.ToLowerInvariant() == "college";
            int iv;
            if ((v = ArgValueOf("-pixAutotestSeed")) != null && int.TryParse(v, out iv)) cfg.Seed = iv;
            if ((v = ArgValueOf("-pixAutotestQuarter")) != null && int.TryParse(v, out iv) && iv > 0) cfg.QuarterMinutes = iv;
            MatchDirector.ForceAutopilot = true;
            float speed = 2f;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-pixAutotestSpeed") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speed);
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance.Flow == null) && wait < 90f)
            {
                wait += Time.unscaledDeltaTime;
                var d0 = MatchDirector.Instance;
                if (d0 != null && d0.Phase == MatchPhase.CoinToss && !_shots.Contains("cointoss")) { _shots.Add("cointoss"); yield return new WaitForSeconds(0.9f); yield return Shot("match_cointoss"); }
                yield return null;
            }
            var dir = MatchDirector.Instance;
            if (dir == null || dir.Flow == null) { Log("FAIL: match did not start"); Finish(2); yield break; }
            Log("match started in " + wait.ToString("0.0") + "s: " + dir.HomeInfo.Name + " vs " + dir.AwayInfo.Name + ", 2-minute quarters, autopilot plays " + dir.Config.UserTeam);
            if (Flag("-pixAutotestPerf")) { yield return PerfSweep(dir); Finish(0); yield break; }
            Time.timeScale = speed;
            float matchStart = Time.unscaledTime;
            int tackleShots = 0, passShots = 0, carryShots = 0, kickoffShots = 0, bannerShots = 0, preSnapShots = 0, callShots = 0, iconShots = 0, specialShots = 0;
            int plays = 0, tds = 0, tackles = 0, passes = 0, ints = 0, fumbles = 0, sacks = 0, fgs = 0, punts = 0, penalties = 0, firstDowns = 0;
            bool halfShot = false, finalDone = false;
            string lastStatus = "";
            dir.PlayFinished += o =>
            {
                plays++;
                if (o.Headline == Headline.Touchdown) tds++;
                if (o.Headline == Headline.Interception) ints++;
                if (o.Headline == Headline.FumbleLost || o.Headline == Headline.Fumble) fumbles++;
                if (o.Headline == Headline.Sack) sacks++;
                if (o.Headline == Headline.FieldGoal) fgs++;
                if (o.Headline == Headline.Punt) punts++;
                if (o.Headline == Headline.Penalty) penalties++;
                if (o.Headline == Headline.FirstDown) firstDowns++;
                if (o.Result != null && o.Result.Type == PlayType.Pass) passes++;
            };
            dir.Milestone += s => { lastStatus = s; Log("  " + s); };
            bool pendingBanner = false, tackleFlag = false; float bannerAt = 0f; string bannerName = "";
            var evq = new Queue<PlayEvent>(); int closeTk = 0, closeCatch = 0, closeThrow = 0, closeHand = 0, closeKick = 0, closeBlock = 0;
            dir.SimEvent += e => { if (e.Type == SimEventType.Tackle && e.AgentA >= 0) { tackles++; tackleFlag = true; } evq.Enqueue(e); };
            bool staged = false;
            dir.PrePlayHook = fl => { if (!staged && tds == 0 && fl.State.Quarter >= 2 && fl.State.Phase == GamePhase.Scrimmage && fl.State.Possession == fl.UserTeam) { staged = true; fl.State.BallSpot = 94; fl.State.Down = 1; fl.State.Distance = 6; Log("  (autotest staged a goal-line situation)"); } };
            dir.PlayFinished += o =>
            {
                if ((o.Headline == Headline.Touchdown || o.Headline == Headline.FieldGoal || o.Headline == Headline.Interception || o.Headline == Headline.Safety || o.Headline == Headline.FirstDown) && !_shots.Contains("banner_" + o.Headline))
                { _shots.Add("banner_" + o.Headline); pendingBanner = true; bannerAt = Time.unscaledTime + 0.75f; bannerName = "match_banner_" + o.Headline.ToString().ToLowerInvariant(); }
            };
            float safety = 60f * 14f;
            int lastPlays = -1; float lastProgress = Time.unscaledTime;
            while (!dir.Finished && Time.unscaledTime - matchStart < safety)
            {
                var sim = dir.Sim;
                if (!_ablated && dir.Phase == MatchPhase.PreSnap) { _ablated = true; ApplyAblate(dir); }
                if (Flag("-pixAutotestMeasure") && Time.unscaledTime - matchStart > 80f) break;   // v0.9 GFX: 80 s of live play for A/B frame rates
                if (_skipFrames > 0) _skipFrames--; else if (dir.Phase == MatchPhase.Live || dir.Phase == MatchPhase.PostPlay) _matchFrames.Add(Time.unscaledDeltaTime);
                if (Flag("-pixAutotestMeasure")) SpikeLog(dir, matchStart);
                if (plays != lastPlays) { lastPlays = plays; lastProgress = Time.unscaledTime; }
                if (Time.unscaledTime - lastProgress > 120f) { Log("FAIL: match stalled for 120 s at '" + lastStatus + "' phase " + dir.Phase); break; }

                if (dir.Phase == MatchPhase.PlayCall && callShots < 1 && !_shots.Contains("playcall")) { _shots.Add("playcall"); callShots++; yield return new WaitForSecondsRealtime(0.5f); yield return Shot("match_playcall"); }
                if (pendingBanner && Time.unscaledTime >= bannerAt) { pendingBanner = false; yield return Shot(bannerName); bannerShots++; if (bannerName.EndsWith("touchdown") && dir.Sim != null && dir.Sim.Carrier != null) { dir.Cams.CloseUp(dir.Stage.WorldPos(dir.Sim.Carrier.Id), new Vector3(4.5f, 1.6f, -2.2f)); yield return new WaitForSecondsRealtime(0.4f); yield return Shot("match_td_close"); dir.Cams.CloseUp(null, Vector3.zero); } }
                if (sim != null && dir.Phase == MatchPhase.PreSnap && sim.Kind == PlayKind.Scrimmage && preSnapShots < 2 && !_shots.Contains("ps" + dir.PlayNumber)) { _shots.Add("ps" + dir.PlayNumber); preSnapShots++; if (preSnapShots == 1) LookDump(); yield return new WaitForSecondsRealtime(0.35f); yield return Shot("match_presnap_" + preSnapShots); }
                if (sim != null && dir.Phase == MatchPhase.PreSnap && sim.Kind == PlayKind.Kickoff && kickoffShots < 1 && !_shots.Contains("ko" + dir.PlayNumber)) { _shots.Add("ko" + dir.PlayNumber); kickoffShots++; yield return new WaitForSecondsRealtime(0.35f); yield return Shot("match_kickoff_setup"); }
                if (sim != null && dir.Phase == MatchPhase.PreSnap && (sim.Kind == PlayKind.Punt || sim.Kind == PlayKind.FieldGoal) && specialShots < 2 && !_shots.Contains("sp" + dir.PlayNumber)) { _shots.Add("sp" + dir.PlayNumber); specialShots++; yield return new WaitForSecondsRealtime(0.35f); yield return Shot("match_special_" + sim.Kind.ToString().ToLowerInvariant()); }
                if (sim != null && dir.Phase == MatchPhase.Live)
                {
                    if (sim.Ball.State == BallState.Pass && passShots < 3 && sim.Ball.FlightTime > 0.45f && !_shots.Contains("pass" + dir.PlayNumber)) { _shots.Add("pass" + dir.PlayNumber); passShots++; yield return Shot("match_pass_" + passShots); }
                    if (sim.Kind == PlayKind.Scrimmage && sim.Agents[0].HasBall && !sim.PassIsThrown && sim.Time > 1.3f && iconShots < 1 && dir.UserAgent == 0) { iconShots++; yield return Shot("match_qb_icons"); }
                    if (sim.Kind == PlayKind.Scrimmage && sim.Carrier != null && sim.Carrier.Slot != 0 && sim.Carrier.Side == SimSide.Offense && sim.Time > 2.2f && carryShots < 2 && !_shots.Contains("carry" + dir.PlayNumber)) { _shots.Add("carry" + dir.PlayNumber); carryShots++; yield return Shot("match_carry_" + carryShots); }
                    if (tackleFlag) { tackleFlag = false; if (tackleShots < 3 && !_shots.Contains("tk" + dir.PlayNumber)) { _shots.Add("tk" + dir.PlayNumber); tackleShots++; yield return new WaitForSecondsRealtime(0.28f); yield return Shot("match_tackle_" + tackleShots); } }
                }
                while (evq.Count > 0)
                {
                    var e = evq.Dequeue();
                    if (dir.Sim == null || dir.Phase != MatchPhase.Live && dir.Phase != MatchPhase.PostPlay) continue;
                    Vector3 off = new Vector3(4.2f, 1.5f, -1.6f);
                    if (e.Type == SimEventType.Tackle && e.AgentB >= 0 && closeTk < 4) { closeTk++; yield return new WaitForSecondsRealtime(0.1f); dir.Cams.CloseUp(dir.Stage.WorldPos(e.AgentB), off); yield return new WaitForSecondsRealtime(0.15f); yield return Shot("match_tackle_close_" + closeTk); dir.Cams.CloseUp(null, off); }
                    else if (e.Type == SimEventType.Catch && e.AgentA >= 0 && closeCatch < 3) { closeCatch++; yield return new WaitForSecondsRealtime(0.12f); dir.Cams.CloseUp(dir.Stage.WorldPos(e.AgentA), off); yield return new WaitForSecondsRealtime(0.1f); yield return Shot("match_catch_close_" + closeCatch); dir.Cams.CloseUp(null, off); }
                    else if (e.Type == SimEventType.Throw && e.AgentA >= 0 && closeThrow < 2) { closeThrow++; yield return new WaitForSecondsRealtime(0.04f); dir.Cams.CloseUp(dir.Stage.WorldPos(e.AgentA), off); yield return new WaitForSecondsRealtime(0.08f); yield return Shot("match_throw_close_" + closeThrow); dir.Cams.CloseUp(null, off); }
                    else if (e.Type == SimEventType.Handoff && e.AgentA >= 0 && closeHand < 2) { closeHand++; dir.Cams.CloseUp(dir.Stage.WorldPos(e.AgentA), off); yield return new WaitForSecondsRealtime(0.1f); yield return Shot("match_handoff_close_" + closeHand); dir.Cams.CloseUp(null, off); }
                    else if (e.Type == SimEventType.Kick && e.AgentA >= 0 && closeKick < 2) { closeKick++; dir.Cams.CloseUp(dir.Stage.WorldPos(e.AgentA), new Vector3(3.2f, 1.2f, -2.4f)); yield return new WaitForSecondsRealtime(0.08f); yield return Shot("match_kick_close_" + closeKick); dir.Cams.CloseUp(null, off); }
                    else if (e.Type == SimEventType.Pancake && e.AgentA >= 0 && closeBlock < 1) { closeBlock++; dir.Cams.CloseUp(dir.Stage.WorldPos(e.AgentA), off); yield return new WaitForSecondsRealtime(0.1f); yield return Shot("match_block_close_" + closeBlock); dir.Cams.CloseUp(null, off); }
                }
                if (Flag("-pixAutotestQuick") && preSnapShots >= 2 && closeTk >= 1) break;   // v0.9 GFX: a 40 second look run (two pre-snap shots and a tackle close-up)
                if (!halfShot && dir.Flow.State.Quarter == 3 && dir.Phase == MatchPhase.Break) { halfShot = true; yield return new WaitForSecondsRealtime(0.5f); yield return Shot("match_halftime"); }
                yield return null;
            }
            Time.timeScale = 1f;
            if (dir.Finished)
            {
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("match_final");
                finalDone = true;
            }
            else Log("match did not finish within the time budget (phase " + dir.Phase + ", Q" + dir.Flow.State.Quarter + " " + dir.Flow.State.ClockDisplay + ")");
            ReportMatchFps();
            WriteBoxScore(dir, plays, tds, passes, tackles, ints, fumbles, sacks, fgs, punts, penalties, firstDowns);
            Log("match part done (final reached: " + finalDone + ")");
        }

        void WriteBoxScore(MatchDirector d, int plays, int tds, int passes, int tackles, int ints, int fumbles, int sacks, int fgs, int punts, int penalties, int firstDowns)
        {
            var f = d.Flow; var s = f.State;
            var sb = new StringBuilder();
            sb.AppendLine("FINAL  " + d.HomeInfo.Name + " " + s.HomeScore + "  -  " + s.AwayScore + " " + d.AwayInfo.Name + (s.IsFinal ? "" : "  (not final)"));
            sb.AppendLine("by quarter  home " + string.Join(" ", f.HomeByQuarter) + " | away " + string.Join(" ", f.AwayByQuarter));
            sb.AppendLine("snaps " + f.Snaps + "  plays-finished " + plays + "  touchdowns " + tds + "  fg " + fgs + "  punts " + punts + "  passes " + passes + "  tackle events " + tackles + "  sacks " + sacks + "  interceptions " + ints + "  fumbles " + fumbles + "  penalties " + penalties + "  first downs " + firstDowns);
            sb.AppendLine("HOME yards " + f.HomeStats.TotalYards + " (pass " + f.HomeStats.PassYards + " rush " + f.HomeStats.RushYards + ") first downs " + f.HomeStats.FirstDowns + " turnovers " + f.HomeStats.Turnovers + " penalties " + f.HomeStats.Penalties);
            sb.AppendLine("AWAY yards " + f.AwayStats.TotalYards + " (pass " + f.AwayStats.PassYards + " rush " + f.AwayStats.RushYards + ") first downs " + f.AwayStats.FirstDowns + " turnovers " + f.AwayStats.Turnovers + " penalties " + f.AwayStats.Penalties);
            sb.AppendLine("--- play log");
            foreach (string l in f.Log) sb.AppendLine(l);
            Log(sb.ToString());
            try { File.WriteAllText(Path.Combine(_out, "boxscore.txt"), sb.ToString()); } catch (Exception e) { Debug.LogError(e); }
        }

        void ReportMatchFps()
        {
            if (_matchFrames.Count < 10) return;
            var f = new List<float>(_matchFrames);
            float sum = 0f, max = 0f; foreach (float x in f) { sum += x; max = Mathf.Max(max, x); }
            f.Sort();
            float p99 = f[Mathf.Min(f.Count - 1, (int)(f.Count * 0.99f))], p95 = f[Mathf.Min(f.Count - 1, (int)(f.Count * 0.95f))];
            Log("FPS [match live play, 22 players] frames=" + f.Count + " avg=" + (f.Count / sum).ToString("0.0") + " min=" + (1f / max).ToString("0.0") + " 5%low=" + (1f / p95).ToString("0.0") + " 1%low=" + (1f / p99).ToString("0.0") + "  at " + Screen.width + "x" + Screen.height);
            if (_ftN > 0) Log("frame timing [live play] cpu=" + (_cpuSum / _ftN).ToString("0.0") + " ms  main thread=" + (_mainSum / _ftN).ToString("0.0") + " ms  render thread=" + (_renderSum / _ftN).ToString("0.0") + " ms  gpu=" + (_gpuSum / _ftN).ToString("0.0") + " ms  (" + _ftN + " samples)");
            Log(RenderSizeReport());
        }

        // ============================================================================================ GPU / CPU cost sweep
        IEnumerator MeasureFor(string label, float seconds)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            double gpu = 0, cpu = 0, main = 0; int n = 0; float t = 0f; var ft = new FrameTiming[1];
            var frames = new List<float>();
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime; frames.Add(Time.unscaledDeltaTime);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].gpuFrameTime > 0) { gpu += ft[0].gpuFrameTime; cpu += ft[0].cpuFrameTime; main += ft[0].cpuMainThreadFrameTime; n++; }
                yield return null;
            }
            float sum = 0f; foreach (float x in frames) sum += x;
            Log("PERF " + label.PadRight(34) + " fps=" + (frames.Count / sum).ToString("0.0") + "  gpu=" + (n > 0 ? gpu / n : 0).ToString("0.0") + " ms  cpu=" + (n > 0 ? cpu / n : 0).ToString("0.0") + " ms  main=" + (n > 0 ? main / n : 0).ToString("0.0") + " ms");
        }

        bool _ablated;
        /// <summary>v0.9 GFX A/B: -pixAblate a,b,c switches individual look features off at the first pre-snap (ssgi, dof, mb, clouds, ao, cs, ssr, aniso, taa, cascades, proxy, lod, blobs, bloom, fog).</summary>
        void ApplyAblate(MatchDirector dir)
        {
            string list = ArgValueOf("-pixAblate"); if (string.IsNullOrEmpty(list)) return;
            var vols = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (string tok in list.Split(','))
            {
                switch (tok)
                {
                    case "ssgi": SetOverride<GlobalIllumination>(vols, false); break;
                    case "dof": SetOverride<DepthOfField>(vols, false); break;
                    case "mb": SetOverride<MotionBlur>(vols, false); break;
                    case "clouds": SetOverride<CloudLayer>(vols, false); break;
                    case "ao": SetOverride<ScreenSpaceAmbientOcclusion>(vols, false); break;
                    case "cs": SetOverride<ContactShadows>(vols, false); break;
                    case "ssr": SetOverride<ScreenSpaceReflection>(vols, false); break;
                    case "bloom": SetOverride<Bloom>(vols, false); break;
                    case "fog": SetFog(vols, false); break;
                    case "aniso": QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable; break;
                    case "taa": foreach (var c in FindObjectsByType<HDAdditionalCameraData>(FindObjectsSortMode.None)) { c.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.Medium; c.taaSharpenMode = HDAdditionalCameraData.TAASharpenMode.LowQuality; c.taaSharpenStrength = 0.4f; c.taaAntiFlicker = 0.5f; c.taaHistorySharpening = 0.35f; } break;
                    case "cascades": foreach (var v in vols) { HDShadowSettings sh; if (v.profile != null && v.profile.TryGet(out sh)) { sh.cascadeShadowSplitCount.value = 3; sh.maxShadowDistance.value = 90f; sh.cascadeShadowSplit0.value = 0.04f; sh.cascadeShadowSplit1.value = 0.12f; } } break;
                    case "proxy": PixFootball.Art.PhotoPlayer.ShadowProxy = false; foreach (var pp in FindObjectsByType<PixFootball.Art.PhotoPlayer>(FindObjectsSortMode.None)) pp.SetPartVisible("Jersey", true); break;
                    case "lod": PlayerLod.UseScreenSize = false; break;
                    case "blobs": foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == FootShadow.ChildName) t.gameObject.SetActive(false); break;
                }
            }
            Log("ablate: " + list);
        }

        IEnumerator PerfSweep(MatchDirector dir)
        {
            // wait for the first scrimmage pre-snap so all 22 players are on the field
            float w = 0f;
            while (!(dir.Phase == MatchPhase.PreSnap && dir.Sim != null && dir.Sim.Kind == PlayKind.Scrimmage) && w < 120f) { w += Time.unscaledDeltaTime; yield return null; }
            Time.timeScale = 0f;   // freeze the simulation, keep rendering the same frame
            Log("perf sweep at " + Screen.width + "x" + Screen.height + " (simulation frozen on a 22-player pre-snap formation)");
            yield return MeasureFor("baseline", 4f);
            Log(RenderSizeReport());
            if (Flag("-pixGpuBreakdown")) { MotionVectorCensus(); yield return GpuBreakdown(3f); }
            var vols = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            Action<string, Action, Action> trial = null;
            var list = new List<KeyValuePair<string, KeyValuePair<Action, Action>>>();
            Action<string, Action, Action> add = (n, a, r) => list.Add(new KeyValuePair<string, KeyValuePair<Action, Action>>(n, new KeyValuePair<Action, Action>(a, r)));
            foreach (var v in vols) { if (v.profile == null || !v.gameObject.activeInHierarchy) continue; }
            add("no volumetric fog", () => SetFog(vols, false), () => SetFog(vols, true));
            add("no SSAO", () => SetOverride<ScreenSpaceAmbientOcclusion>(vols, false), () => SetOverride<ScreenSpaceAmbientOcclusion>(vols, true));
            add("no SSR", () => SetOverride<ScreenSpaceReflection>(vols, false), () => SetOverride<ScreenSpaceReflection>(vols, true));
            add("no contact shadows", () => SetOverride<ContactShadows>(vols, false), () => SetOverride<ContactShadows>(vols, true));
            add("no motion blur", () => SetOverride<MotionBlur>(vols, false), () => SetOverride<MotionBlur>(vols, true));
            add("no SSGI", () => SetOverride<GlobalIllumination>(vols, false), () => SetOverride<GlobalIllumination>(vols, true));
            add("no depth of field", () => SetOverride<DepthOfField>(vols, false), () => SetOverride<DepthOfField>(vols, true));
            add("no cloud layer", () => SetOverride<CloudLayer>(vols, false), () => SetOverride<CloudLayer>(vols, true));
            add("old LOD distances 9 / 22 / 55 m", () => PlayerLod.UseScreenSize = false, () => PlayerLod.UseScreenSize = true);
            add("all players LOD2", () => { PlayerLod.UseScreenSize = false; PlayerLod.Near = 0.1f; PlayerLod.Mid = 0.2f; PlayerLod.Far = 500f; }, () => { PlayerLod.UseScreenSize = true; PlayerLod.Near = 9f; PlayerLod.Mid = 22f; PlayerLod.Far = 55f; });
            add("no bloom+grain+vignette", () => { SetOverride<Bloom>(vols, false); SetOverride<FilmGrain>(vols, false); SetOverride<Vignette>(vols, false); }, () => { SetOverride<Bloom>(vols, true); SetOverride<FilmGrain>(vols, true); SetOverride<Vignette>(vols, true); });
            var shadowLights = new List<Light>(); foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.shadows != LightShadows.None) shadowLights.Add(l);
            add("no light shadows (" + shadowLights.Count + ")", () => { foreach (var l in shadowLights) l.shadows = LightShadows.None; }, () => { foreach (var l in shadowLights) l.shadows = LightShadows.Soft; });
            var crowd = FindFirstObjectByType<CrowdInfo>();
            if (crowd != null) add("no crowd", () => crowd.gameObject.SetActive(false), () => crowd.gameObject.SetActive(true));
            add("players hidden", () => dir.Stage.gameObject.SetActive(false), () => dir.Stage.gameObject.SetActive(true));
            var allLights = new List<Light>(FindObjectsByType<Light>(FindObjectsSortMode.None));
            add("only moon light", () => { foreach (var l in allLights) if (l.type == LightType.Spot) l.enabled = false; }, () => { foreach (var l in allLights) l.enabled = true; });
            var prs = new List<Renderer>(dir.Stage.GetComponentsInChildren<Renderer>(true));
            var shadowBefore = new System.Collections.Generic.Dictionary<Renderer, ShadowCastingMode>();   // restore each renderer's own mode (eyes, hands and visors are Off on purpose) (QA-743)
            add("players cast no shadows", () => { foreach (var r in prs) { shadowBefore[r] = r.shadowCastingMode; r.shadowCastingMode = ShadowCastingMode.Off; } }, () => { foreach (var r in prs) { ShadowCastingMode m; r.shadowCastingMode = shadowBefore.TryGetValue(r, out m) ? m : ShadowCastingMode.On; } });
            add("players no motion vectors", () => { foreach (var r in prs) r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; }, () => { foreach (var r in prs) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object; });
            var spotShadow = new List<Light>(); foreach (var l in shadowLights) if (l.type == LightType.Spot) spotShadow.Add(l);
            add("spot shadows off (moon stays)", () => { foreach (var l in spotShadow) l.shadows = LightShadows.None; }, () => { foreach (var l in spotShadow) l.shadows = LightShadows.Soft; });
            add("shadow cascades 2", () => SetCascades(vols, 2), () => SetCascades(vols, 4));
            // v0.4: where do the 22 players spend their GPU time? hide one body part group at a time / swap the materials for a plain lit one
            var parts = new List<Renderer>(dir.Stage.GetComponentsInChildren<Renderer>(false));
            Func<string[], List<Renderer>> pick = names => { var l = new List<Renderer>(); foreach (var r in parts) foreach (var n in names) if (r.name.StartsWith(n)) { l.Add(r); break; } return l; };
            var handsShoes = pick(new[] { "Hands", "Shoes" }); var faceParts = pick(new[] { "Head", "Eyes" }); var helmets = pick(new[] { "Basic_Helmet", "FM_", "visor" }); var uniform = pick(new[] { "Torso", "Legs", "Arms" });
            add("hide hands+shoes", () => { foreach (var r in handsShoes) r.enabled = false; }, () => { foreach (var r in handsShoes) r.enabled = true; });
            add("hide head+eyes", () => { foreach (var r in faceParts) r.enabled = false; }, () => { foreach (var r in faceParts) r.enabled = true; });
            add("hide helmets", () => { foreach (var r in helmets) r.enabled = false; }, () => { foreach (var r in helmets) r.enabled = true; });
            add("hide torso+legs+arms", () => { foreach (var r in uniform) r.enabled = false; }, () => { foreach (var r in uniform) r.enabled = true; });
            var saved = new Dictionary<Renderer, Material[]>(); Material plain = null;
            add("all player materials plain lit", () => { plain = new Material(Shader.Find("HDRP/Lit")); foreach (var r in parts) { saved[r] = r.sharedMaterials; var m = new Material[r.sharedMaterials.Length]; for (int i = 0; i < m.Length; i++) m[i] = plain; r.sharedMaterials = m; } }, () => { foreach (var kv in saved) kv.Key.sharedMaterials = kv.Value; });
            // v0.9.1: renderers outside the players that write per-object motion vectors / skinned bodies outside the stage (officials, sideline)
            var others = new List<Renderer>(); foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.enabled && !r.transform.IsChildOf(dir.Stage.transform) && (r is SkinnedMeshRenderer || r.motionVectorGenerationMode == MotionVectorGenerationMode.Object)) others.Add(r);
            add("others hidden", () => { foreach (var r in others) r.enabled = false; }, () => { foreach (var r in others) r.enabled = true; });
            foreach (var kv in list)
            {
                kv.Value.Key();
                yield return MeasureFor(kv.Key, 3f);
                kv.Value.Value();
            }
            yield return MeasureFor("baseline (again)", 3f);
            Time.timeScale = 1f;
            Log("errors/exceptions logged: " + _errors);
        }



        /// <summary>v0.9.1 perf: which visible renderers can draw into the object motion vector pass (skinned, or per-object motion), grouped by root object, with triangle counts.</summary>
        void MotionVectorCensus()
        {
            var by = new Dictionary<string, int[]>();   // root -> {renderers, triangles, skinned}
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var smr = r as SkinnedMeshRenderer;
                if (smr == null && r.motionVectorGenerationMode != MotionVectorGenerationMode.Object) continue;
                Mesh m = smr != null ? smr.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                int tris = 0; if (m != null) for (int s = 0; s < m.subMeshCount; s++) tris += (int)(m.GetIndexCount(s) / 3);
                string key = r.transform.root.name + (smr != null ? " [skinned]" : r.isPartOfStaticBatch ? " [static]" : " [mesh]");
                int[] v; if (!by.TryGetValue(key, out v)) by[key] = v = new int[2];
                v[0]++; v[1] += tris;
            }
            var l = new List<KeyValuePair<string, int[]>>(by); l.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));
            Log("motion vector census (visible renderers that are skinned or per-object motion):");
            for (int i = 0; i < l.Count && i < 25; i++) Log("  mv " + l[i].Value[1].ToString().PadLeft(9) + " tris  " + l[i].Value[0].ToString().PadLeft(4) + " renderers  " + l[i].Key);
        }

        int _spikeLines, _lastLodSwitches, _lastGc;
        /// <summary>v0.9.1 perf: one line per slow frame (over 20 ms) in live play: what happened in that frame (LOD switches, garbage collections, phase).</summary>
        void SpikeLog(MatchDirector dir, float matchStart)
        {
            int sw = PlayerLod.Switches, gc = GC.CollectionCount(0);
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.02f && _skipFrames <= 0 && _spikeLines < 80 && (dir.Phase == MatchPhase.Live || dir.Phase == MatchPhase.PostPlay))
            {
                _spikeLines++;
                FrameTimingManager.CaptureFrameTimings(); string ft = "";
                if (FrameTimingManager.GetLatestTimings(1, _ft) > 0) ft = " main " + _ft[0].cpuMainThreadFrameTime.ToString("0.0") + " render " + _ft[0].cpuRenderThreadFrameTime.ToString("0.0") + " gpu " + _ft[0].gpuFrameTime.ToString("0.0");
                Log("  spike " + (dt * 1000f).ToString("0.0") + " ms at " + (Time.unscaledTime - matchStart).ToString("0.0") + " s, " + dir.Phase + ", lod switches " + (sw - _lastLodSwitches) + ", gc " + (gc - _lastGc) + ft);
            }
            _lastLodSwitches = sw; _lastGc = gc;
        }
        /// <summary>v0.9.1 perf: the actual render size of the gameplay camera (shows whether DLSS / FSR / render scale is really in use).</summary>
        string RenderSizeReport()
        {
            var cam = Camera.main; if (cam == null) return "render size: no main camera";
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            var hc = HDCamera.GetOrCreate(cam);
            return "render size " + hc.actualWidth + "x" + hc.actualHeight + " of " + cam.pixelWidth + "x" + cam.pixelHeight + " (dynres scale " + DynamicResolutionHandler.instance.GetCurrentScale().ToString("0.00") + ", camera allowDynRes=" + (hd != null && hd.allowDynamicResolution) + " allowDLSS=" + (hd != null && hd.allowDeepLearningSuperSampling) + ")";
        }

        /// <summary>v0.9.1 perf: GPU time of every profiling sampler over a few frames (editor / development players only: release players have no samplers), top 40 logged.</summary>
        IEnumerator GpuBreakdown(float seconds)
        {
            var names = new List<string>(); UnityEngine.Profiling.Sampler.GetNames(names);
            var recs = new List<KeyValuePair<string, UnityEngine.Profiling.Recorder>>();
            foreach (var n in names) { var r = UnityEngine.Profiling.Recorder.Get(n); if (r != null && r.isValid) { r.enabled = true; recs.Add(new KeyValuePair<string, UnityEngine.Profiling.Recorder>(n, r)); } }
            if (recs.Count == 0) { Log("gpu breakdown: no samplers in this player"); yield break; }
            var sum = new Dictionary<string, double>(); int frames = 0; float t = 0f;
            yield return null;
            while (t < seconds) { t += Time.unscaledDeltaTime; frames++; foreach (var kv in recs) { long g = kv.Value.gpuElapsedNanoseconds; if (g > 0) { double s; sum.TryGetValue(kv.Key, out s); sum[kv.Key] = s + g; } } yield return null; }
            foreach (var kv in recs) kv.Value.enabled = false;
            var top = new List<KeyValuePair<string, double>>(sum); top.Sort((a, b) => b.Value.CompareTo(a.Value));
            Log("gpu breakdown (" + frames + " frames, ms per frame, nested samplers include their children):");
            for (int i = 0; i < top.Count && i < 45; i++) Log("  gpu " + (top[i].Value / frames / 1e6).ToString("0.00").PadLeft(6) + "  " + top[i].Key);
        }
        static void SetFog(Volume[] vols, bool on)
        {
            foreach (var v in vols) { Fog f; if (v.profile != null && v.profile.TryGet(out f)) f.enableVolumetricFog.value = on; }
        }
        static void SetOverride<T>(Volume[] vols, bool on) where T : VolumeComponent
        {
            foreach (var v in vols) { T c; if (v.profile != null && v.profile.TryGet(out c)) c.active = on; }
        }
        static void SetCascades(Volume[] vols, int n)
        {
            foreach (var v in vols) { HDShadowSettings sh; if (v.profile != null && v.profile.TryGet(out sh)) sh.cascadeShadowSplitCount.value = n; }
        }

        /// <summary>v0.9 GFX: what the lighting actually is when a shot is taken (weather, sun, which lights cast shadows, how many player renderers cast).</summary>
        void LookDump()
        {
            var ws = WeatherSystem.Instance;
            if (ws != null) Log("look: " + ws.Preset.Label + " " + ws.DebugState());
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.isActiveAndEnabled && (l.type == LightType.Directional || l.shadows != LightShadows.None)) Log("  light " + l.name + " " + l.type + " lux/int " + l.intensity.ToString("0") + " shadows " + l.shadows + " rot " + l.transform.eulerAngles);
            int on = 0, off = 0, so = 0;
            var d = MatchDirector.Instance;
            if (d != null && d.Stage != null) foreach (var r in d.Stage.GetComponentsInChildren<Renderer>(false)) { if (r.shadowCastingMode == ShadowCastingMode.On) on++; else if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) so++; else off++; }
            { var lv = new int[4]; if (d != null && d.Stage != null) foreach (var v in d.Stage.Views) if (v != null && v.Avatar != null && v.Avatar.Lod != null) lv[Mathf.Clamp(v.Avatar.Lod.Level, 0, 3)]++; Log("  players per LOD: " + lv[0] + " / " + lv[1] + " / " + lv[2] + " / " + lv[3]); }
            Log("  player renderers active: shadow on " + on + ", shadows only " + so + ", off " + off);
            { var vc = PlayerLod.ViewCamera; Log("  lod camera: " + (vc != null ? vc.name + " fov " + vc.fieldOfView.ToString("0.0") + " pixelHeight " + vc.pixelHeight + " pos " + vc.transform.position : "NULL") + " px(20 m) " + PlayerLod.ScreenPixels(20f, vc != null ? vc.fieldOfView : 46f, vc != null ? vc.pixelHeight : 1440f).ToString("0")); }
            { var vc2 = PlayerLod.ViewCamera; var sb2 = new StringBuilder("  lod per player (level @ px):"); if (d != null && d.Stage != null && vc2 != null) foreach (var v in d.Stage.Views) if (v != null && v.Avatar != null && v.Avatar.Lod != null) sb2.Append(" ").Append(v.Avatar.Lod.Level).Append("@").Append(PlayerLod.ScreenPixels(Vector3.Distance(vc2.transform.position, v.transform.position), vc2.fieldOfView, vc2.pixelHeight).ToString("0")); Log(sb2.ToString()); }
            Log("  tier " + QualityTiers.Current + " lodBias " + PlayerLod.Bias + " " + UpscalerControl.Report());
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string path = Path.Combine(_out, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            _skipFrames = 6; LastCaptureFrame = Time.frameCount;
            Log("screenshot " + path);
        }

        void ReportFps(string label)
        {
            if (_frames.Count < 5) return;
            var f = new List<float>(_frames);
            float sum = 0f, max = 0f;
            foreach (float x in f) { sum += x; max = Mathf.Max(max, x); }
            f.Sort();
            float p99 = f[Mathf.Min(f.Count - 1, (int)(f.Count * 0.99f))];
            Log("FPS [" + label + "] frames=" + f.Count + " avg=" + (f.Count / sum).ToString("0.0") + " min=" + (1f / max).ToString("0.0") + " 1%low=" + (1f / p99).ToString("0.0"));
        }

        void Finish(int code)
        {
            try { File.WriteAllText(Path.Combine(_out, "autotest.txt"), _log.ToString()); } catch (Exception e) { Debug.LogError(e); }
            Application.Quit(code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}

