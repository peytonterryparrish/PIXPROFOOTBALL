using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.9.0 CONNECT audit (QA only, additive, flag <c>-pixConnectAudit</c>): the numbers of the connection rows that the engine audit does not take.
    /// Plays real plays through PlayStage at 60 Hz and measures, per play and per agent, frame by frame: view teleports (a frame displacement the simulation cannot explain), the stance that is dropped at the snap
    /// (a standing-locomotion frame while the body is still in its stance), the ball against the hands around every hand-off and catch, the exchange / catch frame against the clip's measured moment,
    /// the carrier's speed through every tackle (a sprinting carrier must not stop dead), and the smoothness of the rendered motion at a 144 Hz display.
    /// Run: Unity -projectPath ... -executeMethod PixFootball.EditorTools.ConnectAudit.Run -pixConnectAudit -pixConnectAuditTag before|after [-pixConnectAuditPlays 24] [-pixConnectAuditOut dir]. Batch mode WITHOUT -nographics.
    /// </summary>
    public static class ConnectAudit
    {
        public const string Flag = "-pixConnectAudit";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(60);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(60);
            new GameObject("PixConnectAudit").AddComponent<ConnectAuditRunner>();
        }
    }

    public sealed class ConnectAuditRunner : MonoBehaviour
    {
        string _out, _tag;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _rep = new StringBuilder();
        readonly Dictionary<string, List<double>> _acc = new Dictionary<string, List<double>>();
        readonly Dictionary<string, int> _cnt = new Dictionary<string, int>();
        int _playIndex;
        readonly Queue<string> _roll = new Queue<string>();
        readonly List<string> _tele = new List<string>(), _dump = new List<string>();
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }
        const float Dt = 1f / 60f;

        void Add(string k, double v) { if (double.IsNaN(v) || double.IsInfinity(v)) return; List<double> l; if (!_acc.TryGetValue(k, out l)) _acc[k] = l = new List<double>(); l.Add(v); }
        void Cnt(string k, int n = 1) { int c; _cnt.TryGetValue(k, out c); _cnt[k] = c + n; }
        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string Grp(Position p)
        {
            if (PositionGroup.IsOffensiveLine(p)) return "OL";
            if (PositionGroup.IsDefensiveLine(p)) return "DL";
            if (PositionGroup.IsLinebacker(p)) return "LB";
            if (p == Position.CB) return "CB";
            if (p == Position.FS || p == Position.SS) return "S";
            return p.ToString();
        }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = ConnectAudit.Arg("-pixConnectAuditOut", "D:/PIXFootball/Renders/connect_audit");
            _tag = ConnectAudit.Arg("-pixConnectAuditTag", "after");
            Directory.CreateDirectory(_out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception) { Debug.Log("[ConnectAudit] exception: " + c + " :: " + (st.Length > 400 ? st.Substring(0, 400) : st)); try { File.WriteAllText(_out + "/connect_audit_" + _tag + "_PARTIAL.txt", _rep.ToString()); } catch { } EditorApplication.Exit(3); } };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("ConnectAuditCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.tag = "MainCamera";
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("ConnectAuditStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            int nPlays = int.Parse(ConnectAudit.Arg("-pixConnectAuditPlays", "24"));
            string phase = ConnectAudit.Arg("-pixConnectAuditPhase", "all");
            if (phase == "all" || phase == "metrics") { yield return MetricPlays(nPlays); Debug.Log("[ConnectAudit] metric plays done"); yield return Smoothness(); Debug.Log("[ConnectAudit] smoothness done"); yield return PreSnapChanges(); Debug.Log("[ConnectAudit] presnap done"); }
            if (phase == "all" || phase == "strips") yield return Strips();
            Write();
            Debug.Log("[ConnectAudit] done");
            yield return null;
            EditorApplication.Exit(0);
        }

        PlaySimulation NewSim(Play off, Play def, GameState gs, int seed)
        {
            var sim = new PlaySimulation();
            var opt = new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f };
            sim.Setup(gs, off, def, null, null, _rules, new Rng(seed), opt);
            return sim;
        }

        GameState Scrim(int spot, int down, int dist)
        {
            var gs = new GameState(_rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = spot; gs.Down = down; gs.Distance = dist; gs.Possession = Team.Home;
            return gs;
        }

        Play DefFor(Play off)
        {
            var defs = _book.AllPlays.Where(p => p.Side == PlaySide.Defense).ToList();
            var zone = defs.Where(p => p.Category == PlayCategory.ZoneCoverage).ToList();
            var man = defs.Where(p => p.Category == PlayCategory.ManCoverage).ToList();
            var bl = defs.Where(p => p.Category == PlayCategory.Blitz).ToList();
            int h = Mathf.Abs(off.Name.GetHashCode() + off.FormationName.GetHashCode());
            if (off.IsPass && h % 5 == 0 && bl.Count > 0) return bl[h % bl.Count];
            if (h % 3 == 0 && man.Count > 0) return man[h % man.Count];
            return zone[h % zone.Count];
        }

        void Prep()
        {
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
        }

        sealed class Track
        {
            public Vector3 PrevPos; public bool Have; public string PrevClip = ""; public AgentAction PrevAct = (AgentAction)(-1);
            public float PrevSpeed; public bool StanceSeen; public float SnapT = -1f;
            public Transform Hips, LH, RH; public Vector3 PrevHipsLocal; public readonly Queue<string> Hist = new Queue<string>();
        }

        sealed class EventWin { public float T; public SimEventType Type; public int A, B; public List<float[]> Samples = new List<float[]>(); }

        IEnumerator MetricPlays(int n)
        {
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary").ToList();
            var rnd = new System.Random(4242);
            var chosen = new List<Play>();
            foreach (var cat in offense.Select(p => p.Category).Distinct()) { var l = offense.Where(p => p.Category == cat).ToList(); chosen.Add(l[rnd.Next(l.Count)]); }
            while (chosen.Count < n) chosen.Add(offense[rnd.Next(offense.Count)]);
            for (int pi = 0; pi < n; pi++)
            {
                var off = chosen[pi]; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(35, 1, 10), 500 + pi);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                var tr = new Track[22];
                for (int i = 0; i < 22; i++) tr[i] = new Track();
                // pre-snap: 120 frames (the huddle break, if any, plays here); teleports are counted per phase
                for (int f = 0; f < 150; f++)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null;
                    SampleTeleport(sim, tr, "presnap");
                }
                float t = 0f; var wins = new List<EventWin>(); int evSeen = 0;
                float snapT = 0f;
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                for (int i = 0; i < 22; i++) { tr[i].SnapT = 0f; }
                var ballHist = new List<Vector3>(); var handHist = new List<float>();
                int frames = 0;
                while (sim.Phase != PlayPhase.Dead && t < 12f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; frames++;
                    yield return null;
                    SampleTeleport(sim, tr, "live");
                    SampleSnapWindow(sim, tr, t);
                    if (sim.Ball.State == BallState.Pass && sim.Ball.Intended >= 0 && sim.Ball.Intended < 22)
                    {
                        var iv = _stage.Views[sim.Ball.Intended]; var ib = iv != null ? GetBones(iv) : null;
                        if (ib != null && ib.LH != null) { Vector3 bpp = _stage.Ball.Position; _roll.Enqueue("PRE t " + t.ToString("0.000") + " agent " + sim.Ball.Intended + " ballToHand " + Mathf.Min(Vector3.Distance(bpp, ib.LH.position), Vector3.Distance(bpp, ib.RH.position)).ToString("0.00") + " ballH " + bpp.y.ToString("0.00") + " action " + sim.Agents[sim.Ball.Intended].Action + " clip " + (iv.Rig != null ? iv.Rig.CurrentClipName : "") + " reachW " + ReachW(iv).ToString("0.00")); while (_roll.Count > 14) _roll.Dequeue(); }
                    }
                    // events
                    var evs = sim.Log.Events;
                    while (evSeen < evs.Count)
                    {
                        var e = evs[evSeen++];
                        if (e.Type == SimEventType.Handoff || e.Type == SimEventType.Catch || e.Type == SimEventType.Tackle) wins.Add(new EventWin { T = t, Type = e.Type, A = e.AgentA, B = e.AgentB });
                        if (e.Type == SimEventType.Catch && _dumpEvents.ContainsKey(SimEventType.Catch) == false || (e.Type == SimEventType.Catch && _dumpEvents[SimEventType.Catch] < 3)) _dump.AddRange(_roll.ToArray());
                    }
                    foreach (var w in wins) if (t - w.T < 0.65f && t - w.T > -0.01f) w.Samples.Add(SampleEvent(sim, w, t - w.T));
                    RecordPre(sim, wins, t);
                }
                for (int f = 0; f < 150 && sim.Phase == PlayPhase.Dead; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; SampleTeleport(sim, tr, "dead"); foreach (var w in wins) if (t - w.T < 0.65f) w.Samples.Add(SampleEvent(sim, w, t - w.T)); RecordPre(sim, wins, t); }
                foreach (var w in wins) Digest(sim, w);
                _rep.AppendLine("play " + pi + " " + off.Name + " vs " + def.Name + " ended " + sim.EndReason + " at " + t.ToString("0.0") + " events " + wins.Count);
                _stage.End();
                yield return null;
            }
        }

        // ------------------------------------------------------------------------------------------ teleports
        void SampleTeleport(PlaySimulation sim, Track[] tr, string phase)
        {
            for (int i = 0; i < 22; i++)
            {
                var v = _stage.Views[i]; if (v == null) continue;
                var t = tr[i];
                Vector3 p = v.transform.position;
                if (t.Have)
                {
                    float d = new Vector2(p.x - t.PrevPos.x, p.z - t.PrevPos.z).magnitude;
                    float sp = sim.Agents[i].Speed * 0.9144f;
                    // a frame displacement the simulation cannot explain: more than 0.45 m plus 2.5x the simulated speed
                    float allowed = 0.45f + 2.5f * sp * Dt + 6f * Dt;
                    if (d > allowed)
                    {
                        Cnt("teleport frames " + phase + " " + Grp(sim.Agents[i].PlayPos)); Cnt("teleport frames " + phase + " all"); Add("teleport size m " + phase, d);
                        Cnt("teleport by sim action " + phase + " " + sim.Agents[i].Action + (v.ContactDriven ? " [contact]" : ""));
                        if (_tele.Count < 12) { _tele.Add("   history of agent " + i + ": " + string.Join(" | ", t.Hist.ToArray())); }
                        if (_tele.Count < 60) _tele.Add("play " + _playIndex + " " + phase + " agent " + i + " " + sim.Agents[i].PlayPos + " action " + sim.Agents[i].Action + (v.ContactDriven ? " [contact]" : "") + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : "") + " simSpeed " + sp.ToString("0.0") + " moved " + d.ToString("0.00") + " m");
                    }
                    Cnt("frames " + phase);
                }
                t.Hist.Enqueue(p.x.ToString("0.00") + "," + p.z.ToString("0.00") + (v.ContactDriven ? "c" : "") + " " + (v.Rig != null ? v.Rig.ClipTime.ToString("0.00") : "-")); while (t.Hist.Count > 8) t.Hist.Dequeue();
                t.PrevPos = p; t.Have = true;
            }
        }

        // ------------------------------------------------------------------------------------------ the stance at the snap
        void SampleSnapWindow(PlaySimulation sim, Track[] tr, float t)
        {
            for (int i = 0; i < 22; i++)
            {
                var v = _stage.Views[i]; if (v == null || v.Rig == null) continue;
                var a = sim.Agents[i];
                string clip = v.Rig.CurrentClipName ?? "";
                float w = v.Rig.Weight;
                bool locoOnly = clip.Length == 0 || w < 0.2f;
                if (t < 0.5f)
                {
                    // the first half second: a body that is still in its stance (the sim has not moved him: speed < 0.5 yd/s) must keep the stance clip, not stand in the locomotion idle
                    if (a.Speed < 0.5f && locoOnly && a.PlayPos != Position.QB && a.PlayPos != Position.K && a.PlayPos != Position.P)
                    {
                        Cnt("snap-window frames in locomotion idle " + Grp(a.PlayPos));
                        Cnt("snap-window frames in locomotion idle all");
                    }
                    if (a.Speed < 0.5f) Cnt("snap-window frames still " + Grp(a.PlayPos));
                    if (a.Action == AgentAction.Idle && tr[i].PrevAct == AgentAction.Stance) Cnt("sim Stance->Idle at snap " + Grp(a.PlayPos));
                }
                tr[i].PrevAct = a.Action;
            }
        }

        // ------------------------------------------------------------------------------------------ event windows
        Bones GetBones(SimAgentView v)
        {
            var an = v.Avatar != null ? v.Avatar.Animator : null; if (an == null || !an.isHuman) return null;
            return new Bones { Hips = an.GetBoneTransform(HumanBodyBones.Hips), LH = an.GetBoneTransform(HumanBodyBones.LeftHand), RH = an.GetBoneTransform(HumanBodyBones.RightHand) };
        }
        sealed class Bones { public Transform Hips, LH, RH; }
        static float ReachW(SimAgentView v) { var p = typeof(SimAgentView).GetProperty("ReachWeight"); return p != null ? (float)p.GetValue(v, null) : -1f; }

        // sample = { dt, ballToHandMin, ballSpeed (m/s of the displayed ball), carrierSpeed (view root m/s), clipTime, hipsJump }
        readonly Dictionary<int, Vector3> _prevView = new Dictionary<int, Vector3>();
        Vector3 _prevBall; bool _haveBall;

        float[] SampleEvent(PlaySimulation sim, EventWin w, float dt)
        {
            int focus = w.Type == SimEventType.Tackle ? w.B : (w.Type == SimEventType.Handoff ? (w.B >= 0 ? w.B : w.A) : w.A);
            var v = focus >= 0 && focus < 22 ? _stage.Views[focus] : null;
            float handD = -1f, carrierSp = -1f, clipT = -1f, ballSp = -1f;
            Vector3 bp = _stage.Ball != null ? _stage.Ball.Position : Vector3.zero;
            if (_haveBall) ballSp = (bp - _prevBall).magnitude / Dt;
            if (v != null)
            {
                var b = GetBones(v);
                if (b != null && b.LH != null) handD = Mathf.Min(Vector3.Distance(bp, b.LH.position), Vector3.Distance(bp, b.RH.position));
                Vector3 p = v.transform.position; Vector3 pp;
                if (_prevView.TryGetValue(focus, out pp)) carrierSp = new Vector2(p.x - pp.x, p.z - pp.z).magnitude / Dt;
                clipT = v.Rig != null ? v.Rig.ClipTime : -1f;
            }
            if (v != null && EventDumpWanted(w)) _dump.Add(w.Type + " #" + _playIndex + " agent " + focus + " dt " + dt.ToString("0.000") + " ball " + bp.ToString("0.00") + " handD " + handD.ToString("0.00") + " carrierSp " + carrierSp.ToString("0.0") + " clip " + (v.Rig != null ? v.Rig.CurrentClipName + " T" + v.Rig.ClipTime.ToString("0.00") + " w" + v.Rig.Weight.ToString("0.00") : "-") + " pos " + v.transform.position.ToString("0.00") + " contact " + v.ContactDriven);
            return new[] { dt, handD, ballSp, carrierSp, clipT };
        }

        readonly Dictionary<EventWin, int> _dumpCount = new Dictionary<EventWin, int>();
        readonly Dictionary<SimEventType, int> _dumpEvents = new Dictionary<SimEventType, int>();
        readonly HashSet<EventWin> _dumpWins = new HashSet<EventWin>();
        bool EventDumpWanted(EventWin w)
        {
            if (_dumpWins.Contains(w)) return w.Samples.Count < 40;
            int n; _dumpEvents.TryGetValue(w.Type, out n);
            if (n >= 3) return false;
            _dumpEvents[w.Type] = n + 1; _dumpWins.Add(w); return true;
        }

        void RecordPre(PlaySimulation sim, List<EventWin> wins, float t)
        {
            _prevBall = _stage.Ball != null ? _stage.Ball.Position : Vector3.zero; _haveBall = true;
            for (int i = 0; i < 22; i++) { var v = _stage.Views[i]; if (v != null) _prevView[i] = v.transform.position; }
        }

        void Digest(PlaySimulation sim, EventWin w)
        {
            if (w.Samples.Count < 4) return;
            // ball: the largest displayed-ball speed jump around the event (a pop shows as a speed spike far above the ball's own flight)
            float maxHand = 0f; float handAt = -1f; float minHand = 9f;
            foreach (var s in w.Samples) { if (s[1] >= 0f) { if (s[0] <= 0.04f && handAt < 0f) handAt = s[1]; minHand = Mathf.Min(minHand, s[1]); maxHand = Mathf.Max(maxHand, s[1]); } }
            if (w.Type == SimEventType.Handoff)
            {
                Cnt("handoffs");
                if (handAt >= 0f) Add("handoff: ball to receiving hand at the exchange m", handAt);
                // displayed ball per-frame displacement around the exchange
                float maxJump = 0f; float prevSp = -1f;
                foreach (var s in w.Samples) { if (s[2] >= 0f) { if (prevSp >= 0f) maxJump = Mathf.Max(maxJump, Mathf.Abs(s[2] - prevSp) * Dt); prevSp = s[2]; } }
                Add("handoff: largest ball displacement change in one frame m", maxJump);
                // exchange frame vs the clip's measured moment
                var rb = w.B >= 0 && w.B < 22 ? _stage.Views[w.B] : null;
            }
            else if (w.Type == SimEventType.Catch)
            {
                Cnt("catches");
                if (handAt >= 0f) Add("catch: ball to nearest hand at the catch frame m", handAt);
                float after = 0f; int na = 0; foreach (var s in w.Samples) if (s[0] > 0.05f && s[0] < 0.4f && s[1] >= 0f) { after = Mathf.Max(after, s[1]); na++; }
                if (na > 0) Add("catch: worst ball to hand 0.05-0.4 s after the catch m", after);
                float maxJump = 0f; float prevSp = -1f;
                foreach (var s in w.Samples) { if (s[2] >= 0f) { if (prevSp >= 0f) maxJump = Mathf.Max(maxJump, Mathf.Abs(s[2] - prevSp) * Dt); prevSp = s[2]; } }
                Add("catch: largest ball displacement change in one frame m", maxJump);
            }
            else if (w.Type == SimEventType.Tackle)
            {
                Cnt("tackles");
                // carrier view speed before (-0.25 .. -0.05 s is not sampled: the window starts at the event) and after: samples hold the view speed from the event on
                float v0 = -1f, v1 = -1f, v3 = -1f;
                foreach (var s in w.Samples) { if (s[3] < 0f) continue; if (s[0] <= 0.03f && v0 < 0f) v0 = s[3]; if (s[0] >= 0.1f && s[0] < 0.14f && v1 < 0f) v1 = s[3]; if (s[0] >= 0.3f && s[0] < 0.34f && v3 < 0f) v3 = s[3]; }
                var c = w.B >= 0 && w.B < 22 ? sim.Agents[w.B] : null;
                if (v0 >= 0f && v1 >= 0f) { Add("tackle: carrier view speed at the event m/s", v0); Add("tackle: carrier view speed 0.1 s later m/s", v1); }
                if (v3 >= 0f) Add("tackle: carrier view speed 0.3 s later m/s", v3);
                if (v0 > 4.5f && v1 >= 0f) { Add("tackle (fast carrier > 4.5 m/s): speed 0.1 s after / at the event", v1 / Mathf.Max(0.1f, v0)); if (v1 < 1.2f) Cnt("tackles where a sprinting carrier stops dead within 0.1 s"); Cnt("tackles with a fast carrier"); }
            }
        }

        // ------------------------------------------------------------------------------------------ pre-snap changes: flip, shift, motion, huddle
        IEnumerator PreSnapChanges()
        {
            var off = _book.AllPlays.First(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.ShortPass);
            for (int round = 0; round < 3; round++)
            {
                var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(35, 1, 10), 1200 + round);
                var frame = new PlayFrame(true);
                var fi = typeof(PlayStage).GetField("NextBeginHuddle");
                if (fi != null && round == 2) fi.SetValue(_stage, true);       // v0.9.0: the huddle break (not in the old tree)
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                var tr = new Track[22]; for (int i = 0; i < 22; i++) tr[i] = new Track();
                string label = round == 0 ? "flip" : (round == 1 ? "defense shift" : "huddle break");
                int frames = 0; float maxD = 0f; int big = 0; int settleFrames = -1;
                for (int f = 0; f < 330; f++)
                {
                    if (f == 60 && round == 0) sim.FlipOffense();
                    if (f == 60 && round == 1) { var adj = new DefenseAdjust(); adj.Line = LineShift.Spread; adj.Backers = LinebackerShift.In; sim.SetDefenseAdjust(adj); }
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null;
                    for (int i = 0; i < 22; i++)
                    {
                        var v = _stage.Views[i]; if (v == null) continue;
                        Vector3 p = v.transform.position;
                        if (tr[i].Have)
                        {
                            float d = new Vector2(p.x - tr[i].PrevPos.x, p.z - tr[i].PrevPos.z).magnitude;
                            if (f > 2) { maxD = Mathf.Max(maxD, d); if (d > 0.45f) big++; }
                        }
                        tr[i].PrevPos = p; tr[i].Have = true;
                    }
                    frames++;
                }
                Add("pre-snap " + label + ": largest single-frame view displacement m (0.09 = a 5.4 m/s jog)", maxD);
                Add("pre-snap " + label + ": player-frames over 0.45 m in one frame", big);
                _stage.End();
                yield return null;
            }
        }

        // ------------------------------------------------------------------------------------------ smoothness at a 144 Hz display
        IEnumerator Smoothness()
        {
            var off = _book.AllPlays.First(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.OutsideRun);
            var def = DefFor(off);
            var sim = NewSim(off, def, Scrim(35, 1, 10), 901);
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
            for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
            sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
            int runner = 1; for (int i = 0; i < 11; i++) if (sim.Agents[i].PlayPos == Position.RB) runner = i;
            // frames at 144 Hz: the simulation steps 0 or 1 time per frame
            float fd = 1f / 144f; var disp = new List<double>(); Vector3 prev = _stage.Views[runner].transform.position; bool have = false;
            for (int f = 0; f < 700 && sim.Phase != PlayPhase.Dead; f++)
            {
                sim.Tick(fd, default(ControlInput), -1); _stage.Present(fd); yield return null;
                Vector3 p = _stage.Views[runner].transform.position;
                if (have && sim.Agents[runner].Speed > 4f && !_stage.Views[runner].ContactDriven && (sim.Agents[runner].Action == AgentAction.Carry || sim.Agents[runner].Action == AgentAction.Run || sim.Agents[runner].Action == AgentAction.Sprint)) disp.Add(new Vector2(p.x - prev.x, p.z - prev.z).magnitude);
                prev = p; have = true;
            }
            if (disp.Count > 30)
            {
                double mean = disp.Average(); double sd = Math.Sqrt(disp.Select(x => (x - mean) * (x - mean)).Average());
                Add("144 Hz: runner per-frame displacement coefficient of variation (0 = perfectly even)", sd / Math.Max(1e-6, mean));
                int zero = disp.Count(x => x < mean * 0.25);
                Add("144 Hz: runner frames with (almost) no movement, fraction", zero / (double)disp.Count);
            }
            _stage.End();
            yield return null;
        }

        // ------------------------------------------------------------------------------------------ strips
        Texture2D Shot(Vector3 q, Vector3 fwd, int w, int h, float dist = 3.4f)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            _cam.transform.position = q + right * dist + Vector3.up * 1.4f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 1.0f - _cam.transform.position); _cam.fieldOfView = 42f;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = rt; _cam.Render(); _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null; rt.Release(); Destroy(rt);
            return tex;
        }

        void Sheet(List<Texture2D> tiles, int cols, string path)
        {
            if (tiles.Count == 0) return;
            int w = tiles[0].width, h = tiles[0].height, rows = (tiles.Count + cols - 1) / cols;
            var tex = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Count; i++) { int cx = i % cols, cy = rows - 1 - i / cols; tex.SetPixels(cx * w, cy * h, w, h, tiles[i].GetPixels()); Destroy(tiles[i]); }
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
        }

        IEnumerator Strips()
        {
            Directory.CreateDirectory(_out + "/strips_" + _tag);
            // 1. a tackle, 2. a hand-off, 3. a catch and run, 4. an OL / DL engagement: the focal player and the marks are chosen from a dry run of the same sim
            var jobs = new List<object[]>
            {
                new object[] { "tackle", PlayCategory.OutsideRun, "tackle" },
                new object[] { "handoff", PlayCategory.InsideRun, "handoff" },
                new object[] { "catch_and_run", PlayCategory.ShortPass, "catch" },
                new object[] { "ol_dl_engagement", PlayCategory.DeepPass, "engage" },
            };
            int seed = 960;
            foreach (var job in jobs)
            {
                var cat = (PlayCategory)job[1]; string name = (string)job[0]; string what = (string)job[2];
                var off = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && p.Category == cat);
                if (off == null) { _rep.AppendLine("strip " + name + ": no play"); continue; }
                var def = DefFor(off);
                // dry run for the event time and the focal agent
                var dry = NewSim(off, def, Scrim(35, 1, 10), seed);
                for (int f = 0; f < 90; f++) dry.Tick(Dt, default(ControlInput), -1);
                dry.Tick(Dt, new ControlInput { Snap = true }, -1);
                float tEvent = -1f; int focal = -1; int g = 0; float t0 = dry.Time;
                while (dry.Phase != PlayPhase.Dead && g++ < 1500)
                {
                    dry.Tick(Dt, default(ControlInput), -1);
                    if (tEvent < 0f)
                        foreach (var e in dry.Log.Events)
                        {
                            if (what == "tackle" && e.Type == SimEventType.Tackle) { tEvent = e.Time - t0; focal = e.AgentB; break; }
                            if (what == "handoff" && e.Type == SimEventType.Handoff) { tEvent = e.Time - t0; focal = e.AgentB >= 0 ? e.AgentB : e.AgentA; break; }
                            if (what == "catch" && e.Type == SimEventType.Catch) { tEvent = e.Time - t0; focal = e.AgentA; break; }
                        }
                    if (what == "engage" && tEvent < 0f)
                        for (int i = 0; i < 11; i++) if (dry.Agents[i].Action == AgentAction.Engaged && PositionGroup.IsOffensiveLine(dry.Agents[i].PlayPos)) { tEvent = dry.Time - t0; focal = i; break; }
                }
                if (tEvent < 0f) { _rep.AppendLine("strip " + name + ": no event in the dry run"); seed++; continue; }
                Debug.Log("[ConnectAudit] strip " + name + ": event at " + tEvent.ToString("0.00") + " focal " + focal);
                var sim = NewSim(off, def, Scrim(35, 1, 10), seed++);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                for (int f = 0; f < 150; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                float[] marks = what == "engage" ? new[] { -0.3f, 0f, 0.2f, 0.4f, 0.7f, 1.0f, 1.4f, 1.8f } : new[] { tEvent - 0.9f, tEvent - 0.5f, tEvent - 0.25f, tEvent - 0.1f, tEvent, tEvent + 0.15f, tEvent + 0.4f, tEvent + 0.8f, tEvent + 1.3f, tEvent + 2.0f };
                var tiles = new List<Texture2D>(); int mi = 0; float t = 0f;
                var idx = new StringBuilder(name + " focal " + focal + " " + sim.Agents[focal].PlayPos + " event at " + tEvent.ToString("0.00") + ": ");
                if (marks[0] < 0f) { tiles.Add(Shot(_stage.Views[focal].transform.position, _stage.Views[focal].Forward, 540, 400)); idx.Append("-0.3 "); mi = 1; }
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                while (mi < marks.Length && t < tEvent + 3f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; yield return null;
                    if (t >= marks[mi]) { var vv = _stage.Views[focal]; tiles.Add(Shot(vv.transform.position, vv.Forward, 540, 400)); idx.Append(t.ToString("0.00") + "[" + (vv.Rig != null ? vv.Rig.CurrentClipName : "") + "|" + sim.Agents[focal].Action + "] "); mi++; }
                }
                Debug.Log("[ConnectAudit] strip " + name + ": " + tiles.Count + " tiles");
                Sheet(tiles, 5, _out + "/strips_" + _tag + "/" + name + ".png");
                _rep.AppendLine("strip " + idx);
                _stage.End();
                yield return null;
            }
        }

        void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# CONNECT audit (" + _tag + ")\n");
            foreach (var k in _acc.Keys.OrderBy(x => x))
            {
                var l = _acc[k]; l.Sort();
                Func<double, double> q = p => l[Math.Min(l.Count - 1, (int)(p * l.Count))];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-90} n={1,5} mean {2,8:F3} p50 {3,8:F3} p95 {4,8:F3} max {5,8:F3}", k, l.Count, l.Average(), q(.5), q(.95), l[l.Count - 1]));
            }
            sb.AppendLine("\n## counters");
            foreach (var kv in _cnt.OrderBy(x => x.Key)) sb.AppendLine(kv.Key + " = " + kv.Value);
            sb.AppendLine("\n## teleports (first 60)");
            foreach (var t in _tele) sb.AppendLine(t);
            sb.AppendLine("\n## event dumps (first 3 events of each type, 0.65 s after the event)");
            foreach (var d in _dump) sb.AppendLine(d);
            sb.AppendLine("\n## log");
            sb.Append(_rep.ToString());
            File.WriteAllText(_out + "/connect_audit_" + _tag + ".txt", sb.ToString());
        }
    }
}
