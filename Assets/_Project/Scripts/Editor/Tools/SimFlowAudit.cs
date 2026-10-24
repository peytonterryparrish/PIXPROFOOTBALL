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
using UnityEngine.Rendering.HighDefinition;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 SIM-FLOW audit (QA only, behind <c>-pixFlowAudit</c>). Plays a whole CPU-vs-CPU game through the real <see cref="MatchDirector"/> (autopilot, watch mode) at a fixed 60 Hz
    /// and records what a viewer sees BETWEEN plays and how often things happen:
    ///  - dead time per play: whistle to next snap, split in whistle to the end of the aftermath window, aftermath to the next play being built, build to snap;
    ///  - teleports / pops: any body whose drawn position changes by more than 0.55 m in one frame (33 m/s), with the match phase it happened in;
    ///  - idle statues: bodies on the field that stand still (under 0.05 m/s) during the transition for more than 2.5 s;
    ///  - event frequencies per game from the simulation log, against the NFL reference table in simflow.md.
    /// Args: -pixFlowOut dir, -pixFlowTag name, -pixFlowSeed n, -pixFlowQuarter minutes (default 3), -pixFlowMaxMinutes realtime cap (default 40), -pixFlowHome/-pixFlowAway team ids.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.SimFlowAudit.Run -pixFlowAudit   (WITHOUT -nographics).
    /// </summary>
    public static class SimFlowAudit
    {
        public const string Flag = "-pixFlowAudit";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(float.Parse(Arg("-pixFlowMaxMinutes", "40"), CultureInfo.InvariantCulture) + 15f);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            if (FindRunner() != null) return;
            BatchWatchdog.Arm(float.Parse(Arg("-pixFlowMaxMinutes", "40"), CultureInfo.InvariantCulture) + 15f);
            var go = new GameObject("PixFlowAudit"); UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SimFlowAuditRunner>();
        }

        static SimFlowAuditRunner FindRunner() { return UnityEngine.Object.FindFirstObjectByType<SimFlowAuditRunner>(); }
    }

    public sealed class SimFlowAuditRunner : MonoBehaviour
    {
        const float Dt = 1f / 60f;
        string _out, _tag;
        readonly StringBuilder _log = new StringBuilder();
        readonly Dictionary<string, int> _ev = new Dictionary<string, int>();
        readonly List<float> _dead = new List<float>(), _deadWhistleToAfter = new List<float>(), _deadAfterToBuild = new List<float>(), _deadBuildToSnap = new List<float>();
        readonly List<string> _pops = new List<string>();
        readonly Dictionary<SimAgentView, Vector3> _prev = new Dictionary<SimAgentView, Vector3>();
        readonly Dictionary<SimAgentView, float> _seenAt = new Dictionary<SimAgentView, float>();
        readonly Dictionary<SimAgentView, float> _lastPop = new Dictionary<SimAgentView, float>();
        readonly Dictionary<SimAgentView, float> _still = new Dictionary<SimAgentView, float>();
        float _busyT; readonly List<float> _deadEx = new List<float>(); float _preT; string _blk = ""; float _breakT; bool _breakLogged; int _timeouts, _hurry; int _statues, _frames, _popCount, _plays, _popEv;
        readonly Dictionary<string, int> _popByPhase = new Dictionary<string, int>();
        float _clock;
        float _whistleAt = -1f, _afterAt = -1f, _buildAt = -1f, _snapAt = -1f;
        int _lastPlayNo = -1; PlayPhase _lastSimPhase = PlayPhase.PreSnap; MatchPhase _lastPhase = MatchPhase.Booting;
        readonly List<string> _perPlay = new List<string>();
        string _lastKind = "";
        PlaySimulation _lastSim;

        void Awake()
        {
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception) _log.AppendLine("[exception] " + c + " :: " + (st.Length > 300 ? st.Substring(0, 300) : st)); };
        }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = SimFlowAudit.Arg("-pixFlowOut", "D:/PIXFootball/anim_choreo/v0110/flow");
            _tag = SimFlowAudit.Arg("-pixFlowTag", "run");
            Directory.CreateDirectory(_out);
            float maxMin = float.Parse(SimFlowAudit.Arg("-pixFlowMaxMinutes", "40"), CultureInfo.InvariantCulture);
            var cfg = new MatchConfig
            {
                HomeId = SimFlowAudit.Arg("-pixFlowHome", "buffalo_ironhorns"), AwayId = SimFlowAudit.Arg("-pixFlowAway", "miami_reefrunners"),
                QuarterMinutes = int.Parse(SimFlowAudit.Arg("-pixFlowQuarter", "3")), UserTeam = CoreTeam.Home, Seed = int.Parse(SimFlowAudit.Arg("-pixFlowSeed", "11011")), CpuVsCpu = true, Difficulty = Difficulty.Pro,
                Weather = WeatherChoice.Clear, Time = TimeChoice.Day
            };
            MatchDirector.ForceAutopilot = true;
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance.Flow == null) && wait < 120f) { wait += Dt; yield return null; }
            var dir = MatchDirector.Instance;
            if (dir == null || dir.Flow == null) { _log.AppendLine("FAIL: match did not start"); Write(); EditorApplication.Exit(2); yield break; }
            _log.AppendLine("match started: " + dir.HomeInfo.Name + " vs " + dir.AwayInfo.Name + " quarter " + cfg.QuarterMinutes + " min, seed " + cfg.Seed);
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (SimFlowAudit.Arg("-pixFlowCamera", "0") != "1") c.enabled = false;
            dir.SimEvent += e => { Count(e.Type.ToString() + (e.Tackle != TackleType.None ? "/" + e.Tackle : "")); };
            dir.Milestone += m => { if (m != null && m.StartsWith("presnap") && dir.Stage != null && dir.Stage.NextBeginHurryUsed) _hurry++; };
            dir.PlayStarted += s => { _snapAt = _clock; OnSnap(s); };
            dir.PlayFinished += o => { _lastKind = o.Kind + "/" + o.Headline; };
            _shots = SimFlowAudit.Arg("-pixFlowShots", "0") == "1"; _shotMax = int.Parse(SimFlowAudit.Arg("-pixFlowShotPlays", "4")); if (_shots) _out = SimFlowAudit.Arg("-pixFlowShotOut", _out);
            var t0 = Time.realtimeSinceStartup;
            while (!dir.Finished && Time.realtimeSinceStartup - t0 < maxMin * 60f)
            {
                _clock += Dt; _frames++;
                Sample(dir);
                ShotTick(dir);
                yield return null;
            }
            _log.AppendLine("finished " + dir.Finished + " after " + (Time.realtimeSinceStartup - t0).ToString("0") + " s real, " + _frames + " frames (" + (_frames / 60f).ToString("0") + " s match time)");
            var s2 = dir.Flow.State;
            _log.AppendLine("score " + dir.HomeInfo.Name + " " + s2.HomeScore + " - " + s2.AwayScore + " " + dir.AwayInfo.Name);
            Write();
            Debug.Log("[SimFlowAudit] done");
            yield return null;
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------------------------------ broadcast strips (-pixFlowShots 1)
        Camera _shotCam;
        bool _shots; int _shotMax, _shotIdx, _shotPlayNo; readonly List<Texture2D> _shotTex = new List<Texture2D>(); RenderTexture _rt;
        static readonly float[] ShotAt = { 0.4f, 2.0f, 3.6f, 5.2f, 7.0f, 9.0f, 11.5f, 14.0f };

        void ShotTick(MatchDirector dir)
        {
            if (!_shots || _whistleAt < 0f || _shotPlayNo >= _shotMax || _plays < int.Parse(SimFlowAudit.Arg("-pixFlowShotSkip", "3"))) return;
            float since = _clock - _whistleAt;
            while (_shotIdx < ShotAt.Length && since >= ShotAt[_shotIdx]) { Capture(); _shotIdx++; }
        }

        void Capture()
        {
            var md0 = MatchDirector.Instance; if (md0 == null || md0.Stage == null) return;
            if (_shotCam == null) { var go = new GameObject("FlowShotCam"); _shotCam = go.AddComponent<Camera>(); var hd0 = go.AddComponent<HDAdditionalCameraData>(); hd0.antialiasing = HDAdditionalCameraData.AntialiasingMode.None; hd0.customRenderingSettings = true; hd0.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.MotionBlur] = true; hd0.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.MotionBlur, false); _shotCam.fieldOfView = 34f; _shotCam.nearClipPlane = 0.3f; _shotCam.farClipPlane = 600f; _shotCam.enabled = false; }
            var cam = _shotCam; PlayerLod.ViewCamera = cam;
            var pts = new List<Vector3>(); foreach (var v in md0.Stage.QaViews()) if (v != null && v.gameObject.activeInHierarchy && Mathf.Abs(v.transform.position.x) < 24f) pts.Add(v.transform.position);
            Vector3 focus = Vector3.zero; if (pts.Count > 0) { pts.Sort((a, b) => a.z.CompareTo(b.z)); float mz = pts[pts.Count / 2].z; int n = 0; foreach (var p in pts) if (Mathf.Abs(p.z - mz) < 14f) { focus += p; n++; } focus /= Mathf.Max(1, n); }
            float side = md0.Stage.Frame.TowardsPlusZ ? -1f : 1f;
            cam.transform.position = focus + new Vector3(side * 30f, 13f, 0f); cam.transform.LookAt(focus + Vector3.up * 1f);
                        if (_rt == null) _rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture; cam.targetTexture = _rt; cam.Render(); cam.targetTexture = prev;
            var old = RenderTexture.active; RenderTexture.active = _rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = old;
            _shotTex.Add(tex);
        }

        void FlushStrip(int playNo)
        {
            if (_shotTex.Count == 0) return;
            int cols = 4, rows = (_shotTex.Count + cols - 1) / cols;
            var sheet = new Texture2D(960 * cols, 540 * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < _shotTex.Count; i++) sheet.SetPixels((i % cols) * 960, (rows - 1 - i / cols) * 540, 960, 540, _shotTex[i].GetPixels());
            File.WriteAllBytes(Path.Combine(_out, "strip_" + _tag + "_play" + playNo + ".png"), sheet.EncodeToPNG());
            foreach (var f in _shotTex) Destroy(f); Destroy(sheet); _shotTex.Clear();
            _shotPlayNo++;
        }

        void Count(string k) { int c; _ev.TryGetValue(k, out c); _ev[k] = c + 1; }

        void OnSnap(PlaySimulation s)
        {
            _plays++;
            if (_whistleAt >= 0f)
            {
                float dead = _clock - _whistleAt;
                float deadEx = Mathf.Max(0f, dead - _busyT); _deadEx.Add(deadEx);
                _dead.Add(dead);
                if (_afterAt >= 0f) _deadWhistleToAfter.Add(_afterAt - _whistleAt);
                if (_afterAt >= 0f && _buildAt >= 0f) { _deadAfterToBuild.Add(Mathf.Max(0f, _buildAt - _afterAt)); _deadBuildToSnap.Add(_clock - _buildAt); }
                _perPlay.Add("  blockers" + _blk); _blk = "";
            _perPlay.Add("play " + _plays + " carried " + (MatchDirector.Instance != null ? MatchDirector.Instance.Stage.LastCarried + "/new " + MatchDirector.Instance.Stage.LastNewcomers + "/off " + MatchDirector.Instance.Stage.LastLeavers : "-") + " prev " + _lastKind + " dead " + dead.ToString("0.0") + " s (whistle->after " + (_afterAt >= 0 ? (_afterAt - _whistleAt).ToString("0.0") : "-") + ", after->built " + (_afterAt >= 0 && _buildAt >= 0 ? (_buildAt - _afterAt).ToString("0.0") : "-") + ", built->snap " + (_buildAt >= 0 ? (_clock - _buildAt).ToString("0.0") : "-") + ") kind " + s.Kind);
            }
            if (_shots) { FlushStrip(_plays); _shotIdx = 0; }
            _whistleAt = -1f; _afterAt = -1f; _buildAt = -1f;
            _busyT = 0f;
        }

        void Sample(MatchDirector dir)
        {
            var sim = dir.Sim;
            if (sim != null && !ReferenceEquals(sim, _lastSim)) { _lastSim = sim; if (_whistleAt >= 0f && _buildAt < 0f) _buildAt = _clock; }
            if (sim != null && sim.Phase == PlayPhase.Dead && _lastSimPhase != PlayPhase.Dead && _whistleAt < 0f) _whistleAt = _clock;
            if (_whistleAt >= 0f && dir.Broadcast != null && dir.Broadcast.Busy) _busyT += Dt;
            if (sim != null) _lastSimPhase = sim.Phase;
            var ph = dir.Phase;
            if (_whistleAt >= 0f && _afterAt < 0f && ph == MatchPhase.PostPlay && false) { }
            if (_whistleAt >= 0f && _afterAt < 0f && (dir.Sim == null || !ReferenceEquals(dir.Sim, _lastSim) || ph == MatchPhase.PlayCall || ph == MatchPhase.PreSnap || ph == MatchPhase.Break)) _afterAt = _clock;
            _lastPhase = ph;

            if (ph == MatchPhase.PreSnap) { float pt0 = _preT; _preT += Dt; foreach (float th in new[] { 5f, 9f, 13f, 18f, 24f, 30f }) if (pt0 < th && _preT >= th && dir.Stage != null) _blk += " [" + th + "s: " + dir.Stage.QaBreakBlockers() + "]"; } else if (ph != MatchPhase.PreSnap) _preT = 0f;
            if (ph == MatchPhase.PreSnap && dir.Stage != null && dir.Stage.BreakInProgress) { _breakT += Dt; if (_breakT > 18f && !_breakLogged) { _breakLogged = true; _log.AppendLine("break stuck at t=" + _clock.ToString("0.0") + ": " + dir.Stage.QaBreakBlockers()); } } else { _breakT = 0f; _breakLogged = false; }
            string phase = ph + "/" + (sim != null ? sim.Phase.ToString() : "none");
            var stage = dir.Stage;
            if (stage == null) return;
            var views = stage.QaViews();
            foreach (var v in views)
            {
                if (v == null || !v.gameObject.activeInHierarchy) { _prev.Remove(v); continue; }
                Vector3 p = v.transform.position; Vector3 q;
                float seen; bool wasSeen = _seenAt.TryGetValue(v, out seen) && _clock - seen < Dt * 1.5f; _seenAt[v] = _clock;
                if (_prev.TryGetValue(v, out q) && wasSeen)
                {
                    float d = new Vector2(p.x - q.x, p.z - q.z).magnitude;
                    if (d > 0.55f)
                    {
                        float lp0; bool freshPop = !_lastPop.TryGetValue(v, out lp0) || _clock - lp0 > 0.2f; _lastPop[v] = _clock; _popCount++; if (freshPop) _popEv++; int c; _popByPhase.TryGetValue(phase, out c); _popByPhase[phase] = c + 1;
                        if (_pops.Count < 500 && freshPop) _pops.Add("t=" + _clock.ToString("0.0") + " " + v.name + " " + d.ToString("0.0") + " m in " + phase);
                    }
                    // statues in the transition
                    bool transition = ph == MatchPhase.PostPlay && !v.Huddle && !(dir.Broadcast != null && dir.Broadcast.Busy);
                    if (transition && dir.Sim != null && v.AgentId >= 0 && v.AgentId < 22) { var ac = dir.Sim.Agents[v.AgentId].Action; if (ac == AgentAction.Celebrate || ac == AgentAction.Tackled || ac == AgentAction.Fallen || ac == AgentAction.GetUp || ac == AgentAction.TackleGang || ac == AgentAction.TackleWrapUp || ac == AgentAction.TackleBigHit || ac == AgentAction.TackleDive) transition = false; }      // these play a clip: not a statue
                    if (transition && d / Dt < 0.05f)
                    {
                        float s; _still.TryGetValue(v, out s); s += Dt; _still[v] = s;
                        if (Mathf.Abs(s - 2.5f) < Dt * 0.5f) _statues++;
                    }
                    else _still[v] = 0f;
                }
                SoleWatch(dir, v);
                _prev[v] = p;
            }
        }

        // sunk bodies: the lowest sole under the turf while the simulation has the man in a block or hand fight
        readonly Dictionary<string, int[]> _sink = new Dictionary<string, int[]>();
        readonly Dictionary<SimAgentView, Transform[]> _sb = new Dictionary<SimAgentView, Transform[]>();
        void SoleWatch(MatchDirector dir, SimAgentView v)
        {
            if (dir.Sim == null || dir.Sim.Phase != PlayPhase.Live || v.AgentId < 0 || v.AgentId > 21 || v.Avatar == null || v.Avatar.Animator == null || !v.Avatar.Animator.isHuman) return;
            Transform[] b; if (!_sb.TryGetValue(v, out b) || b[0] == null) { var an = v.Avatar.Animator; b = new[] { an.GetBoneTransform(HumanBodyBones.LeftFoot), an.GetBoneTransform(HumanBodyBones.RightFoot), an.GetBoneTransform(HumanBodyBones.LeftToes), an.GetBoneTransform(HumanBodyBones.RightToes) }; _sb[v] = b; }
            if (b[0] == null || b[1] == null) return;
            float gy = v.transform.position.y;
            float low = Mathf.Min(b[0].position.y - 0.085f, b[1].position.y - 0.085f); if (b[2] != null) low = Mathf.Min(low, b[2].position.y - 0.02f); if (b[3] != null) low = Mathf.Min(low, b[3].position.y - 0.02f);
            string act = dir.Sim.Agents[v.AgentId].Action.ToString();
            int[] c; if (!_sink.TryGetValue(act, out c)) _sink[act] = c = new int[3];
            c[0]++; if (low - gy < -0.05f) c[1]++; if (low - gy < -0.10f) c[2]++;
        }

        static string P(List<float> l, float q)
        {
            if (l.Count == 0) return "-";
            var s = l.OrderBy(x => x).ToList();
            return s[Mathf.Clamp(Mathf.RoundToInt(q * (s.Count - 1)), 0, s.Count - 1)].ToString("0.0");
        }

        void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SimFlowAudit " + _tag);
            sb.Append(_log);
            sb.AppendLine("plays " + _plays);
            sb.AppendLine("dead time per play (s, real time at 60 fps, whistle to next snap): n " + _dead.Count + " mean " + (_dead.Count > 0 ? _dead.Average().ToString("0.0") : "-") + " p10 " + P(_dead, 0.1f) + " p50 " + P(_dead, 0.5f) + " p90 " + P(_dead, 0.9f) + " max " + P(_dead, 1f));
            sb.AppendLine("dead time WITHOUT broadcast replays / overlays holding the field (s): n " + _deadEx.Count + " mean " + (_deadEx.Count > 0 ? _deadEx.Average().ToString("0.0") : "-") + " p10 " + P(_deadEx, 0.1f) + " p50 " + P(_deadEx, 0.5f) + " p90 " + P(_deadEx, 0.9f) + " max " + P(_deadEx, 1f));
            sb.AppendLine("  whistle -> aftermath over: mean " + (_deadWhistleToAfter.Count > 0 ? _deadWhistleToAfter.Average().ToString("0.0") : "-") + " p90 " + P(_deadWhistleToAfter, 0.9f));
            sb.AppendLine("  aftermath over -> next play built: mean " + (_deadAfterToBuild.Count > 0 ? _deadAfterToBuild.Average().ToString("0.0") : "-"));
            sb.AppendLine("  next play built -> snap: mean " + (_deadBuildToSnap.Count > 0 ? _deadBuildToSnap.Average().ToString("0.0") : "-") + " p90 " + P(_deadBuildToSnap, 0.9f));
            sb.AppendLine("teleports (> 0.55 m in one frame): " + _popEv + " events (" + _popCount + " frames) in " + _plays + " plays");
            foreach (var kv in _popByPhase.OrderByDescending(k => k.Value)) sb.AppendLine("  " + kv.Key + ": " + kv.Value);
            foreach (var s in _pops.Take(300)) sb.AppendLine("    " + s);
            sb.AppendLine("statues (a body standing still > 2.5 s during post-play / call / pre-snap): " + _statues);
            sb.AppendLine("bodies carried over between plays " + PlayStage.TotalCarried + ", substitutes jogging on " + PlayStage.TotalNewcomers + ", players jogging off " + PlayStage.TotalLeavers);
            sb.AppendLine("scenes: first-down points " + PlayStage.PointsStarted + ", frustrations " + PlayStage.FrustrationsStarted + ", cheers " + PlayStage.CheersStarted + ", blow-up hits shown " + ContactDirector.BlowUpsShown + ", measurements " + MatchDirector.MeasuresShown + ", timeouts " + MatchDirector.TimeoutsShown + ", hurry-up plays (no huddle) " + _hurry + ", players sent to the sideline " + PlayStage.TotalSidelined);
            sb.AppendLine("sunk soles (lowest sole more than 5 cm / 10 cm under the turf), live frames per simulated action:");
            foreach (var kv in _sink.OrderByDescending(k => k.Value[0])) if (kv.Value[0] > 500) sb.AppendLine("  " + kv.Key.PadRight(14) + "frames " + kv.Value[0] + "  >5cm " + (100.0 * kv.Value[1] / kv.Value[0]).ToString("0.0") + "%  >10cm " + (100.0 * kv.Value[2] / kv.Value[0]).ToString("0.0") + "%");
            sb.AppendLine("events:");
            foreach (var kv in _ev.OrderBy(k => k.Key)) sb.AppendLine("  " + kv.Key.PadRight(26) + kv.Value);
            sb.AppendLine("per play:");
            foreach (var s in _perPlay) sb.AppendLine("  " + s);
            File.WriteAllText(Path.Combine(_out, "flow_" + _tag + ".txt"), sb.ToString());
        }
    }
}
