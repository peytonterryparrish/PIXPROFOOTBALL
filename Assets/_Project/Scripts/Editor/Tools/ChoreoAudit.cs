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
    /// v0.10.0 CHOREO audit (QA only, behind <c>-pixChoreoAudit</c>). Runs the same 36 real plays as EngineAudit (same seeds) through PlayStage and measures what the choreography is for:
    ///  - FLOATING during actions: frames where a full-body action clip plays and the view root moves differently from the clip's own root motion (more than 5 cm in one frame), and frames where a planted foot slides more than 0.10 m/s;
    ///  - the ball to hand gap at the catch (the displayed ball against the nearest hand bone of the catcher), the per-frame jump of the ball around the catch;
    ///  - clip changes per second and under 0.25 s apart, hips / chest rotation snaps;
    ///  - when the simulation has a choreographer: its bookings (kind, sync error, steering rate, exits).
    /// Args: -pixChoreoAuditOut dir, -pixChoreoAuditPlays n (36), -pixChoreoAuditTag name, -pixChoreoAuditChoreo 0|1 (sim choreography on), -pixChoreoAuditView 0|1 (view uses bookings).
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.ChoreoAudit.Run -pixChoreoAudit   (WITHOUT -nographics).
    /// </summary>
    public static class ChoreoAudit
    {
        public const string Flag = "-pixChoreoAudit";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(70);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(70);
            new GameObject("PixChoreoAudit").AddComponent<ChoreoAuditRunner>();
        }
    }

    public sealed class ChoreoAuditRunner : MonoBehaviour
    {
        const float Dt = 1f / 60f;
        string _out, _tag;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _report = new StringBuilder();
        readonly Dictionary<string, List<double>> _acc = new Dictionary<string, List<double>>();
        readonly Dictionary<string, int> _cnt = new Dictionary<string, int>();
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }
        ChoreoTable _table;
        bool _useChoreo;

        sealed class B
        {
            public Transform Hips, Chest, LF, RF, LT, RT, LH, RH, LUL, RUL;
            public Vector3 PrevRoot, PrevLT, PrevRT; public Quaternion PrevHipsRot = Quaternion.identity, PrevChestRot = Quaternion.identity;
            public AnimationClip PrevClip; public float PrevClipTime, PrevRemain; public AgentAction PrevAct;
            public int CntL, CntR; public float MinL = 99f, MinR = 99f; public bool ActL, ActR, UpL, UpR; public Vector3 PrevLF, PrevRF; public bool Have, PlantL, PlantR;
            public int ClipSwitches; public float Alive, LastSwitch = -9; public int Short;
            public readonly float[] FlipH = new float[90], FlipF = new float[90], FlipS = new float[90]; public readonly float[] FlipV = new float[90], FlipAng = new float[90]; public readonly string[] FlipN = new string[90]; public readonly bool[] FlipClean = new bool[90]; public float PrevYaw; public readonly float[] RC = new float[10], RTm = new float[10], RW = new float[10], RHp = new float[10]; public int RK; public int RN, Cd = -1; public float LastBack = -9f, CurPls = -1f; public Vec2 PrevFace;
        }
        readonly Dictionary<SimAgentView, B> _b = new Dictionary<SimAgentView, B>();

        void Add(string k, double v) { List<double> l; if (!_acc.TryGetValue(k, out l)) _acc[k] = l = new List<double>(); l.Add(v); }
        void Cnt(string k, int n = 1) { int c; _cnt.TryGetValue(k, out c); _cnt[k] = c + n; }
        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string Grp(Position p)
        {
            if (PositionGroup.IsOffensiveLine(p)) return "OL";
            if (PositionGroup.IsDefensiveLine(p)) return "DL";
            if (PositionGroup.IsLinebacker(p)) return "LB";
            if (p == Position.CB) return "CB";
            if (p == Position.FS || p == Position.SS) return "S";
            return p.ToString();
        }

        static bool ActionClass(AgentAction a)
        {
            switch (a)
            {
                case AgentAction.Catch: case AgentAction.Handoff: case AgentAction.Juke: case AgentAction.Spin: case AgentAction.StiffArm: case AgentAction.Truck: case AgentAction.Hurdle: case AgentAction.Dive:
                case AgentAction.TackleWrapUp: case AgentAction.TackleBigHit: case AgentAction.TackleShoestring: case AgentAction.TackleDive: case AgentAction.TackleGang:
                case AgentAction.Tackled: case AgentAction.Fallen: case AgentAction.Stumble: case AgentAction.GetUp: case AgentAction.Celebrate: case AgentAction.Jump: case AgentAction.Engaged: case AgentAction.Shed: case AgentAction.Slide:
                    return true;
            }
            return false;
        }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = ChoreoAudit.Arg("-pixChoreoAuditOut", "D:/PIXFootball/anim_choreo/v0100/audit");
            _tag = ChoreoAudit.Arg("-pixChoreoAuditTag", "run");
            _useChoreo = ChoreoAudit.Arg("-pixChoreoAuditChoreo", "0") == "1";
            Directory.CreateDirectory(_out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception) { Debug.Log("[ChoreoAudit] exception: " + c + " :: " + (st.Length > 400 ? st.Substring(0, 400) : st)); } };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("ChoreoAuditCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.tag = "MainCamera";
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _table = ChoreoTable.Load();
            if (_table == null) _report.AppendLine("WARNING: no ChoreoTable (run PIX Football/Assets/Build Choreo Table): floating cannot be measured");
            _stage = new GameObject("ChoreoAuditStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            int nPlays = int.Parse(ChoreoAudit.Arg("-pixChoreoAuditPlays", "36"));
            yield return Plays(nPlays);
            Cnt("get-off takes released early by the planned path", SimAgentView.GetOffFitCut); Cnt("sack celebrations started", PlayStage.CelebrationsStarted); Write();
            Debug.Log("[ChoreoAudit] done");
            yield return null;
            EditorApplication.Exit(0);
        }

        PlaySimulation NewSim(Play off, Play def, GameState gs, int seed)
        {
            var sim = new PlaySimulation();
            var opt = new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f };
            ChoreoHooks.Configure(opt, _useChoreo);
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

        B Bones(SimAgentView v)
        {
            B b;
            if (_b.TryGetValue(v, out b) && b.Hips != null) return b;
            var an = v.Avatar.Animator; if (an == null || !an.isHuman) return null;
            b = new B();
            b.Hips = an.GetBoneTransform(HumanBodyBones.Hips); b.Chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.UpperChest);
            b.LF = an.GetBoneTransform(HumanBodyBones.LeftFoot); b.RF = an.GetBoneTransform(HumanBodyBones.RightFoot);
            b.LT = an.GetBoneTransform(HumanBodyBones.LeftToes); b.RT = an.GetBoneTransform(HumanBodyBones.RightToes);
            b.LH = an.GetBoneTransform(HumanBodyBones.LeftHand); b.RH = an.GetBoneTransform(HumanBodyBones.RightHand);
            b.LUL = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); b.RUL = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _b[v] = b; return b;
        }

        struct CatchWatch { public int Holder; public int F0; public bool Active; public float MaxAfter, GapAt, Prev, MaxStep; public int Frames; public bool Int; }
        CatchWatch _cw;
        readonly List<float> _ring = new List<float>(); StringBuilder _trace; int _traceN;
        Vector3 _prevBall; BallState _prevBallState; int _prevHolder = -1; int _playFrame;

        IEnumerator Plays(int n)
        {
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary").ToList();
            var rnd = new System.Random(4242);
            var chosen = new List<Play>();
            foreach (var cat in offense.Select(p => p.Category).Distinct()) { var l = offense.Where(p => p.Category == cat).ToList(); chosen.Add(l[rnd.Next(l.Count)]); }
            while (chosen.Count < n) chosen.Add(offense[rnd.Next(offense.Count)]);
            int playIndex = 0;
            for (int pi = 0; pi < n; pi++)
            {
                var off = chosen[pi]; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(35, 1, 10), 500 + pi);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++playIndex);
                Prep();
                for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                foreach (var b in _b.Values) { b.Have = false; b.ClipSwitches = 0; b.Alive = 0; b.Short = 0; b.PlantL = b.PlantR = false; b.CntL = b.CntR = 0; }
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                float t = 0f; _cw = default(CatchWatch); _prevBallState = sim.Ball.State; _prevHolder = sim.Ball.Holder; _playFrame = 0;
                while (sim.Phase != PlayPhase.Dead && t < 12f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; yield return null;
                    Sample(sim, t, false);
                }
                for (int f = 0; f < 240 && (sim.Phase == PlayPhase.Dead || f < 30); f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; Sample(sim, t, true); }
                for (int i = 0; i < 22; i++)
                {
                    var v = _stage.Views[i]; B b; if (v == null || !_b.TryGetValue(v, out b)) continue;
                    if (b.Alive > 0.5f) { Add("clip changes per second " + Grp(sim.Agents[i].PlayPos), b.ClipSwitches / b.Alive); Add("clip changes <0.25s apart per play " + Grp(sim.Agents[i].PlayPos), b.Short); }
                }
                ChoreoHooks.Report(sim, pi, _report, this);
                _report.AppendLine("play " + pi + " " + off.Name + " vs " + def.Name + " ended " + sim.EndReason + " at " + t.ToString("0.0"));
                _stage.End();
                yield return null;
            }
        }

        /// <summary>Hook for the choreographer statistics of a finished play (filled in once the sim has one).</summary>
        public void Note(string key, double v) { Add(key, v); }
        public void Count(string key, int n = 1) { Cnt(key, n); }

        void Sample(PlaySimulation sim, float t, bool dead)
        {
            _playFrame++; _curSim = sim; if (_playFrame % 30 == 0) Add("editor frame dt (Time.deltaTime) ms", Time.deltaTime * 1000.0);
            var ball = _stage.Ball;
            // ---- catch watch (displayed ball against the nearest hand of the new holder)
            if (ball != null && sim.Ball.State == BallState.Pass && sim.Ball.Intended >= 0 && sim.Ball.Intended < 22)
            {
                var vi = _stage.Views[sim.Ball.Intended]; var bi = vi != null ? Bones(vi) : null;
                if (bi != null && bi.LH != null) { _ring.Add(Mathf.Min(Vector3.Distance(ball.Position, bi.LH.position), Vector3.Distance(ball.Position, bi.RH.position))); if (_ring.Count > 9) _ring.RemoveAt(0); }
            }
            if (sim.Ball.State == BallState.Held && _prevBallState == BallState.Pass && sim.Ball.Holder >= 0)
            {
                _cw = new CatchWatch { Holder = sim.Ball.Holder, F0 = _playFrame, Active = true, Int = sim.Agents[sim.Ball.Holder].Side == SimSide.Defense };
                _trace = new StringBuilder("   catch trace (ball to nearest hand of the target, m, frames before the catch): " + string.Join(" ", _ring.Select(x => x.ToString("0.00"))) + " | after:"); _traceN = 0; _ring.Clear();
            }
            if (_cw.Active && ball != null)
            {
                var vh = _stage.Views[_cw.Holder]; var bh = vh != null ? Bones(vh) : null;
                if (bh != null && bh.LH != null)
                {
                    Vector3 bp = ball.Position;
                    float gap = Mathf.Min(Vector3.Distance(bp, bh.LH.position), Vector3.Distance(bp, bh.RH.position));
                    float step = (bp - _prevBall).magnitude;
                    int rel = _playFrame - _cw.F0;
                    if (rel == 0) { _cw.GapAt = gap; }
                    if (_trace != null && rel >= 0 && rel <= 8) { _trace.Append(" " + gap.ToString("0.00") + "(" + ball.ModeName.Substring(0, 2) + " L" + Vector3.Distance(bp, bh.LH.position).ToString("0.00") + " R" + Vector3.Distance(bp, bh.RH.position).ToString("0.00") + " step" + step.ToString("0.00") + ")"); if (rel == 8) { _report.AppendLine(_trace.ToString() + "  receiver speed " + (sim.Agents[_cw.Holder].Speed * 0.9144f).ToString("0.0") + " m/s, booked " + (sim.Choreo != null && sim.Choreo.LastBookingOf(_cw.Holder) != null)); _trace = null; } }
                    if (rel >= 0 && rel <= 24) { _cw.MaxAfter = Mathf.Max(_cw.MaxAfter, gap); }
                    if (rel >= -6 && rel <= 24) _cw.MaxStep = Mathf.Max(_cw.MaxStep, step);
                    if (rel == 24)
                    {
                        string who = _cw.Int ? "interception" : "catch";
                        Add(who + " ball-to-hand gap at the sim catch frame m", _cw.GapAt); Add(who + " worst ball-to-hand gap 0..0.4 s after m", _cw.MaxAfter); Add(who + " largest ball step per frame (-0.1..+0.4 s) m", _cw.MaxStep);
                        _cw.Active = false;
                    }
                }
            }
            // ---- hand-off mesh: how close the quarterback's and the back's bodies get (hips to hips, m)
            if (sim.Agents[0].Action == AgentAction.Handoff && _stage.Views[0] != null)
            {
                var bq = Bones(_stage.Views[0]);
                for (int j = 1; j < 11 && bq != null; j++)
                {
                    var aj = sim.Agents[j]; if (!(aj.PlayPos == Position.RB || aj.PlayPos == Position.FB || aj.PlayPos == Position.WR || aj.PlayPos == Position.TE)) continue;
                    if ((aj.Pos - sim.Agents[0].Pos).Length > 2.2f || _stage.Views[j] == null) continue;
                    var bj = Bones(_stage.Views[j]); if (bj == null) continue;
                    float dd = Vector3.ProjectOnPlane(bq.Hips.position - bj.Hips.position, Vector3.up).magnitude;
                    _hoMin = Mathf.Min(_hoMin, dd);
                }
            }
            else if (_hoMin < 9f) { Add("handoff: closest hips-to-hips distance of the mesh, m", _hoMin); _hoMin = 9f; }
            if (ball != null) _prevBall = ball.Position;
            _prevBallState = sim.Ball.State; _prevHolder = sim.Ball.Holder;

            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i]; if (v == null || v.Avatar == null) continue;
                var b = Bones(v); if (b == null) continue;
                string g = Grp(a.PlayPos);
                float gy = v.transform.position.y;
                Vector3 root = v.transform.position;
                AnimationClip clip = v.Rig != null && v.Rig.Valid ? v.Rig.CurrentClip : null;
                float ct = v.Rig != null ? v.Rig.ClipTime : 0f;
                float w = v.Rig != null ? v.Rig.Weight : 0f;
                float nw = v.Rig != null ? v.Rig.NewestWeight : 0f;
                bool upper = v.Rig != null && v.Rig.UpperBodyActive;
                Vector3 ltP = b.LT != null ? b.LT.position : b.LF.position, rtP = b.RT != null ? b.RT.position : b.RF.position;
                float lth = ltP.y - gy, rth = rtP.y - gy, lah = b.LF.position.y - gy, rah = b.RF.position.y - gy;
                float lc = Mathf.Min(lth, lah - 0.09f), rc = Mathf.Min(rth, rah - 0.09f);
                bool plantL = b.PlantL ? lc < 0.07f : lc < 0.04f, plantR = b.PlantR ? rc < 0.07f : rc < 0.04f;
                if (b.Have)
                {
                    float hipsRot = Quaternion.Angle(b.Hips.rotation, b.PrevHipsRot) / Dt;
                    float chestRot = b.Chest != null ? Quaternion.Angle(b.Chest.rotation, b.PrevChestRot) / Dt : 0f;
                    if (hipsRot > 900f) Cnt("hips rotation snaps over 900 deg/s " + g);
                    if (chestRot > 1400f) Cnt("chest rotation snaps over 1400 deg/s " + g);
                    b.RC[b.RK % 10] = chestRot; b.RTm[b.RK % 10] = ct; b.RW[b.RK % 10] = w; b.RHp[b.RK % 10] = hipsRot; b.RK++;
                    if (chestRot > 1400f && a.Action == AgentAction.Block && Mathf.Abs(Mathf.DeltaAngle(v.transform.eulerAngles.y, b.PrevYaw)) / Dt < 600f && _snapTraces < 5) { _snapTraces++; var sb2 = new StringBuilder("   chest snap trace " + a.PlayPos + " " + (clip != null ? clip.name : "(none)") + " (oldest first: chest deg/s, hips deg/s, clip time, rig weight):"); for (int k = 9; k >= 0; k--) { int ix = (b.RK - 1 - k + 100) % 10; sb2.Append("\n     " + b.RC[ix].ToString("0") + " " + b.RHp[ix].ToString("0") + " " + b.RTm[ix].ToString("0.00") + " " + b.RW[ix].ToString("0.00")); } _report.AppendLine(sb2.ToString()); }
                    float rootYawRate = Mathf.Abs(Mathf.DeltaAngle(v.transform.eulerAngles.y, b.PrevYaw)) / Dt;
                    if (chestRot > 1400f) Cnt("chest snap source: " + (rootYawRate > 600f ? "view root yaw " : "pose ") + a.Action);
                    if (chestRot > 1400f && a.Action == AgentAction.Block) Cnt("chest snap clip: " + (clip != null ? clip.name : "(none)") + " t=" + (Mathf.Floor(ct * 4f) / 4f).ToString("0.00"));
                    if (chestRot > 1400f) Cnt("chest snap cause: " + a.Action + (upper ? " upper-mask" : " full") + (clip != b.PrevClip ? " clip-changed" : " same-clip") + (v.ContactDriven ? " contact" : ""));
                    if (hipsRot > 900f) Cnt("hips snap cause: " + a.Action + (upper ? " upper-mask" : " full") + (clip != b.PrevClip ? " clip-changed" : " same-clip") + (v.ContactDriven ? " contact" : ""));
                    if (!dead && (a.PlayPos == Position.CB || a.PlayPos == Position.FS || a.PlayPos == Position.SS)) DbFlipWatch(b, a, g, t, hipsRot, clip != null ? clip.name : "(loco)");
                    // ---- floating during actions
                    bool action = ActionClass(a.Action) || v.ContactDriven;
                    string cname = clip != null ? clip.name : "";
                    {
                        // foot slide by CONTACT, the way EngineAudit measures it: the slowest frame of a planted foot's contact (a foot that steps or rolls is not skating; one that slides through the whole contact is).
                        // Action contacts (the body is in a take) are reported next to plain locomotion contacts (the control: sprint and run read about 0.2 to 0.4 m/s).
                        float slL = Mathf.Min(Vector3.ProjectOnPlane(ltP - b.PrevLT, Vector3.up).magnitude, Vector3.ProjectOnPlane(b.LF.position - b.PrevLF, Vector3.up).magnitude) / Dt;
                        float slR = Mathf.Min(Vector3.ProjectOnPlane(rtP - b.PrevRT, Vector3.up).magnitude, Vector3.ProjectOnPlane(b.RF.position - b.PrevRF, Vector3.up).magnitude) / Dt;
                        bool teleport = (root - b.PrevRoot).magnitude > 1.0f;
                        FootContact(b, 0, plantL, slL, action, upper, a, dead, teleport);
                        FootContact(b, 1, plantR, slR, action, upper, a, dead, teleport);
                        if (!teleport && _curSim.Choreo != null && _curSim.Choreo.OwnerOf(a.Id) != null)
                        {
                            // a foot is truly planted when it is low AND moving backward under the body (a lunging foot that drags forward over the turf is swinging)
                            Vector3 rs = root - b.PrevRoot; rs.y = 0f; float bsp = rs.magnitude / Dt; Vector3 bdir = bsp > 0.3f ? rs.normalized : Vector3.zero;
                            Vector3 fl = Vector3.ProjectOnPlane(ltP - b.PrevLT, Vector3.up) - rs, fr = Vector3.ProjectOnPlane(rtP - b.PrevRT, Vector3.up) - rs;
                            if (plantL && b.PlantL && (bsp < 0.5f || Vector3.Dot(fl, bdir) < 0f)) Add("booked take, truly planted foot (low and backward under the body) slide m/s per frame", slL);
                            if (plantR && b.PlantR && (bsp < 0.5f || Vector3.Dot(fr, bdir) < 0f)) Add("booked take, truly planted foot (low and backward under the body) slide m/s per frame", slR);
                            if (plantL && b.PlantL) Add("booked take, planted by height only, slide m/s per frame", slL); if (plantR && b.PlantR) Add("booked take, planted by height only, slide m/s per frame", slR);
                        }
                        if (a.Action == AgentAction.TackleWrapUp && (plantL || plantR) && _wrapTraceFrames < 80 && clip != null && _table != null && _table.Get(cname0(clip)) != null && b.PrevClip == clip)
                        {
                            _wrapTraceFrames++;
                            Vector2 cd2 = ownedNow(sim, a.Id) ? _table.RootDeltaScaled(clip.name, b.PrevClipTime, ct) : _table.RootDelta(clip.name, b.PrevClipTime, ct);
                            _report.AppendLine("   wrap trace agent " + a.Id + " clipT " + ct.ToString("0.00") + " viewRoot " + ((root - b.PrevRoot).magnitude / Dt).ToString("0.00") + " clipRoot(m/s) " + (cd2.magnitude / Dt).ToString("0.00") + " simSpeed " + (a.Speed * 0.9144f).ToString("0.00") + " slideL " + slL.ToString("0.00") + (plantL ? "*" : "") + " slideR " + slR.ToString("0.00") + (plantR ? "*" : "") + " clip " + clip.name);
                        }
                        b.CurPls = Mathf.Min(plantL && b.PlantL ? slL : 99f, plantR && b.PlantR ? slR : 99f); if (b.CurPls > 90f) b.CurPls = -1f;
                        if (clip != null && clip.name.Contains("IdleToSprint") && b.CurPls >= 0f) { Add("get-off clip planted-foot slide m/s", b.CurPls); if (b.CurPls > 1f) Cnt("get-off clip planted frames over 1 m/s"); }
                    }
                    if (action && clip != null && clip == b.PrevClip && w > 0.5f && nw > 0.5f && !upper && _table != null)
                    {
                        var e = _table.Get(cname);
                        if (e != null)
                        {
                            bool ownedByBooking = sim.Choreo != null && sim.Choreo.OwnerOf(a.Id) != null;
                            Vector2 d = ownedByBooking && !GameChoreoLibrary.NoRootScale ? _table.RootDeltaScaled(cname, b.PrevClipTime, ct) : _table.RootDelta(cname, b.PrevClipTime, ct);
                            Vector3 rl = b.RUL.position - b.LUL.position; float pelvisYaw = Mathf.Atan2(-rl.z, rl.x) * Mathf.Rad2Deg; float yaw = (pelvisYaw - ChoreoTable.YawAt(e, ct)) * Mathf.Deg2Rad;
                            Vector2 exp = new Vector2(d.x * Mathf.Cos(yaw) + d.y * Mathf.Sin(yaw), -d.x * Mathf.Sin(yaw) + d.y * Mathf.Cos(yaw));
                            Vector2 act = new Vector2(root.x - b.PrevRoot.x, root.z - b.PrevRoot.z);
                            // a teleport of the whole view (re-layout) is not floating
                            if (act.magnitude < 1.0f)
                            {
                                float err = (act - exp).magnitude;
                                string ak = (dead ? "dead " : "live ") + a.Action;
                                Cnt("action frames"); Cnt("action frames " + ak);
                                Add("floating: view root vs clip root motion, m/frame", err);
                                if (err > 0.05f) { Cnt("floating frames (>5 cm vs clip root)"); Cnt("floating frames " + ak); }
                                Add("floating m/frame " + a.Action, err);
                                // planted foot slide
                                Add("view speed m/s " + a.Action, act.magnitude / Dt); Add("clip root speed m/s " + a.Action, exp.magnitude / Dt);
                                // skating = every planted foot slides (a foot that steps in the clip while the other holds is footwork, not floating)
                                float sl = float.MaxValue;
                                if (plantL && b.PlantL) sl = Mathf.Min(sl, Vector3.ProjectOnPlane(ltP - b.PrevLT, Vector3.up).magnitude / Dt);
                                if (plantR && b.PlantR) sl = Mathf.Min(sl, Vector3.ProjectOnPlane(rtP - b.PrevRT, Vector3.up).magnitude / Dt);
                                if (sl < float.MaxValue) { Cnt("planted frames in actions"); Cnt("planted frames " + ak); Add("foot slide during actions m/s", sl); if (sl > 0.10f) { Cnt("foot slide frames (>0.10 m/s) in actions"); Cnt("foot slide frames " + ak); } }
                                // the old gliding signature: the sim agent moves fast while a full-body clip plays
                                if (a.Speed * 0.9144f > 1.5f && !a.IsDown) Cnt("action frames with the sim body faster than 1.5 m/s");
                            }
                        }
                        else Cnt("action frames with a clip that is not in the table");
                    }
                    if (action && clip != null && !upper && w > 0.5f && nw > 0.5f && _table != null && _table.Get(cname) == null && clip == b.PrevClip) Cnt("untabled full-body clip frames");
                }
                if (b.Have && clip != b.PrevClip && b.PrevClip != null && b.PrevRemain > 0.2f && _table != null && _table.Get(b.PrevClip.name) != null) { Cnt("clips cut off with more than 0.2 s of their window left"); Cnt("clips cut off after " + b.PrevAct); Add("seconds left when a clip was cut off", b.PrevRemain); }
                if (b.Have && clip != b.PrevClip && !dead)
                {
                    { string pn = b.PrevClip != null ? b.PrevClip.name : "(loco)", cn = clip != null ? clip.name : "(loco)"; if (pn.Length > 26) pn = pn.Substring(0, 26); if (cn.Length > 26) cn = cn.Substring(0, 26); Cnt("switch " + g + " " + b.PrevAct + ">" + a.Action + ": " + pn + " -> " + cn); }
                    b.ClipSwitches++; if (t - b.LastSwitch < 0.25f) b.Short++; b.LastSwitch = t;
                }
                if (!dead) b.Alive += Dt;
                b.PrevLF = b.LF.position; b.PrevRF = b.RF.position;
                b.PrevYaw = v.transform.eulerAngles.y; b.PrevRoot = root; b.PrevLT = ltP; b.PrevRT = rtP; b.PrevHipsRot = b.Hips.rotation; b.PrevChestRot = b.Chest != null ? b.Chest.rotation : Quaternion.identity;
                b.PrevRemain = v.Rig != null && v.Rig.Valid ? v.Rig.RemainingSeconds : 0f; b.PrevAct = a.Action; b.PrevClip = clip; b.PrevClipTime = ct; b.PlantL = plantL; b.PlantR = plantR; b.Have = true;
            }
        }

        // The defensive back's turn-and-run: a window around every moment a back-pedaling back (sim velocity against his facing) is running (velocity along his facing) within 0.9 s.
        // Reported: the peak pelvis yaw rate and the peak sim facing rate in the 1.2 s window, the mean slide of the planted foot, and the hip snaps over 900 deg/s inside it.
        int _flipTraces, _slideTraces, _snapTraces, _wrapTraceFrames, _duelRolls, _duelBooks;
        static string cname0(AnimationClip c) { return c != null ? c.name : ""; }
        static bool ownedNow(PlaySimulation s, int id) { return s.Choreo != null && s.Choreo.OwnerOf(id) != null && !GameChoreoLibrary.NoRootScale; } PlaySimulation _curSim; float _hoMin = 9f;
        void DbFlipWatch(B b, SimAgent a, string g, float t, float hipsRot, string cname)
        {
            float sp = a.Speed * 0.9144f; Vec2 vd = a.Vel;
            float fr = Mathf.Acos(Mathf.Clamp(a.Facing.Dot(b.PrevFace), -1f, 1f)) * Mathf.Rad2Deg / Dt; if (b.PrevFace.LenSq() < 0.5f) fr = 0f; b.PrevFace = a.Facing;
            b.FlipH[b.RN % 90] = hipsRot; b.FlipF[b.RN % 90] = fr; b.FlipV[b.RN % 90] = sp; b.FlipAng[b.RN % 90] = SimMath.AngleBetween(a.Vel, a.Facing) * Mathf.Rad2Deg; b.FlipN[b.RN % 90] = a.Action + "/" + cname; b.FlipClean[b.RN % 90] = !a.IsDown && (a.Action == AgentAction.Run || a.Action == AgentAction.Sprint || a.Action == AgentAction.Route || a.Action == AgentAction.Backpedal || a.Action == AgentAction.Shuffle || a.Action == AgentAction.Idle) && !cname.Contains("IdleToSprint") && !cname.Contains("stance"); b.FlipS[b.RN % 90] = b.CurPls; b.RN++;
            float ang = SimMath.AngleBetween(vd, a.Facing);
            if (sp > 2.2f && ang > 1.9f) b.LastBack = t;
            if (b.Cd < 0 && sp > 2.2f && ang < 1.2f && b.LastBack > 0f && t - b.LastBack < 0.9f) { b.Cd = 24; b.LastBack = -9f; }
            else if (b.Cd > 0) b.Cd--;
            if (b.Cd == 0)
            {
                b.Cd = -1; int n = Math.Min(72, b.RN); float ph = 0f, pf = 0f, ss = 0f; int sn = 0, snaps = 0;
                for (int k = 0; k < n; k++) { int ix = (b.RN - 1 - k) % 90; ph = Mathf.Max(ph, b.FlipH[ix]); pf = Mathf.Max(pf, b.FlipF[ix]); if (b.FlipH[ix] > 900f) snaps++; if (b.FlipS[ix] >= 0f) { ss += b.FlipS[ix]; sn++; } }
                if (_flipTraces < 8 && t > 2.5f) { _flipTraces++; var tr = new StringBuilder("   dbflip trace " + g + " (every 4th frame, oldest first: speed m/s, vel-vs-facing deg, pelvis rate, planted slide, action/clip):"); for (int k = n - 1; k >= 0; k -= 4) { int ix = (b.RN - 1 - k) % 90; tr.Append("\n     " +  b.FlipV[ix].ToString("0.0") + " " + b.FlipAng[ix].ToString("0") + " " + b.FlipH[ix].ToString("0") + " " + b.FlipS[ix].ToString("0.00") + " " + b.FlipN[ix]); } _report.AppendLine(tr.ToString()); }
                Cnt("dbflip windows"); Cnt("dbflip windows " + g); bool clean = true; for (int k = 0; k < n; k++) if (!b.FlipClean[(b.RN - 1 - k) % 90]) { clean = false; break; }
                if (clean) { Cnt("dbflip clean windows (only plain locomotion in the 1.2 s)"); Add("dbflip clean peak pelvis yaw rate deg/s", ph); Add("dbflip clean peak sim facing rate deg/s", pf); if (sn > 0) Add("dbflip clean mean planted-foot slide m/s", ss / sn); }
                if (t > 2.5f) { Cnt("dbflip late windows"); Add("dbflip late peak pelvis yaw rate deg/s", ph); if (sn > 0) Add("dbflip late mean planted-foot slide m/s", ss / sn); }
                Add("dbflip peak pelvis yaw rate deg/s", ph); Add("dbflip peak sim facing rate deg/s", pf); if (sn > 0) Add("dbflip mean planted-foot slide m/s", ss / sn); if (snaps > 0) { Cnt("dbflip windows with a hip snap over 900 deg/s"); Cnt("dbflip hip snap frames", snaps); }
            }
        }

        void FootContact(B b, int foot, bool plant, float slide, bool action, bool upper, SimAgent a, bool dead, bool teleport)
        {
            bool was = foot == 0 ? b.PlantL : b.PlantR;
            ref int n = ref (foot == 0 ? ref b.CntL : ref b.CntR); ref float mn = ref (foot == 0 ? ref b.MinL : ref b.MinR); ref bool act = ref (foot == 0 ? ref b.ActL : ref b.ActR); ref bool up = ref (foot == 0 ? ref b.UpL : ref b.UpR);
            if (plant)
            {
                if (!was || teleport) { n = 0; mn = 99f; act = true; up = false; }
                n++; if (was && !teleport && slide < mn) mn = slide;
                act &= action; up |= upper;
            }
            else if (was)
            {
                if (n >= 3 && mn < 98f && !teleport)
                {
                    AgentAction ac = b.PrevAct;
                    if (act && mn > 3f && (ac == AgentAction.GetUp || ac == AgentAction.Fallen || ac == AgentAction.Jump || ac == AgentAction.Stumble) && _slideTraces++ < 12) _report.AppendLine("   slide trace " + ac + " agent " + a.Id + " " + a.PlayPos + " min slide " + mn.ToString("0.0") + " m/s, sim speed " + (a.Speed * 0.9144f).ToString("0.0") + " m/s, owner " + (_curSim != null && _curSim.Choreo != null && _curSim.Choreo.OwnerOf(a.Id) != null ? _curSim.Choreo.OwnerOf(a.Id).Kind.ToString() : "none") + ", view offset " + (_stage != null && _stage.Views[a.Id] != null ? _stage.Views[a.Id].ViewOffset.magnitude.ToString("0.00") : "?") + ", contact frames " + n);
                    if (act) { string k = (dead ? "dead " : "live ") + ac + (up ? " (upper-body take)" : ""); Add("contact min slide m/s ACTION " + k, mn); Cnt("slide contacts ACTION " + k); if (mn > 0.30f) Cnt("slide contacts over 0.30 m/s ACTION " + k); Add("contact min slide m/s ACTION all", mn); Cnt("slide contacts ACTION all"); if (mn > 0.30f) Cnt("slide contacts over 0.30 m/s ACTION all"); }
                    else if (!dead && (ac == AgentAction.Run || ac == AgentAction.Sprint || ac == AgentAction.Carry || ac == AgentAction.Route || ac == AgentAction.Backpedal || ac == AgentAction.Shuffle || ac == AgentAction.Idle))
                    { string k = ac + (a.Speed * 0.9144f < 3f ? " slow" : " fast"); Add("contact min slide m/s control " + k, mn); Cnt("slide contacts control all"); if (mn > 0.30f) Cnt("slide contacts over 0.30 m/s control all"); }
                }
                n = 0; mn = 99f;
            }
        }

        void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Choreo audit [" + _tag + "] choreography " + (_useChoreo ? "ON" : "off") + "\n");
            foreach (var k in _acc.Keys.OrderBy(x => x))
            {
                var l = _acc[k]; l.Sort();
                Func<double, double> q = p => l[Math.Min(l.Count - 1, (int)(p * l.Count))];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-72} n={1,6} mean {2,8:F3} p50 {3,8:F3} p95 {4,8:F3} max {5,8:F3}", k, l.Count, l.Average(), q(.5), q(.95), l[l.Count - 1]));
            }
            sb.AppendLine("\n## counters");
            foreach (var kv in _cnt.OrderBy(x => x.Key)) if (!kv.Key.StartsWith("switch ") && !kv.Key.StartsWith("slide")) sb.AppendLine(kv.Key + " = " + kv.Value);
            int af; _cnt.TryGetValue("action frames", out af);
            int ff; _cnt.TryGetValue("floating frames (>5 cm vs clip root)", out ff);
            int sf; _cnt.TryGetValue("foot slide frames (>0.10 m/s) in actions", out sf);
            int pf; _cnt.TryGetValue("planted frames in actions", out pf);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "\nFLOATING SHARE of action frames: {0:P1} ({1} of {2}); foot-slide share of planted action frames: {3:P1} ({4} of {5})", af > 0 ? (double)ff / af : 0.0, ff, af, pf > 0 ? (double)sf / pf : 0.0, sf, pf));
            sb.AppendLine("\n## floating by action (live = before the whistle, dead = after): frames | share over 5 cm | foot-slide share of planted | mean view m/s | mean clip m/s");
            foreach (string ph in new[] { "live", "dead" })
                foreach (AgentAction ac in Enum.GetValues(typeof(AgentAction)))
                {
                    string ak = ph + " " + ac; int fr, fl, pl, sd;
                    _cnt.TryGetValue("action frames " + ak, out fr); if (fr < 20) continue;
                    _cnt.TryGetValue("floating frames " + ak, out fl); _cnt.TryGetValue("planted frames " + ak, out pl); _cnt.TryGetValue("foot slide frames " + ak, out sd);
                    List<double> vs, cs; _acc.TryGetValue("view speed m/s " + ac, out vs); _acc.TryGetValue("clip root speed m/s " + ac, out cs);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-22} {1,7} {2,6:P1} {3,6:P1} view {4,5:F2} clip {5,5:F2}", ak, fr, (double)fl / fr, pl > 0 ? (double)sd / pl : 0.0, vs != null ? vs.Average() : 0.0, cs != null ? cs.Average() : 0.0));
                }
            {
                int ca, co, la, lo; _cnt.TryGetValue("slide contacts ACTION all", out ca); _cnt.TryGetValue("slide contacts over 0.30 m/s ACTION all", out co); _cnt.TryGetValue("slide contacts control all", out la); _cnt.TryGetValue("slide contacts over 0.30 m/s control all", out lo);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "\nFOOT SLIDE (slowest frame of each planted contact, target under 0.30 m/s): ACTION contacts {0:P1} of {1} over 0.30 m/s; CONTROL (plain locomotion) {2:P1} of {3}", ca > 0 ? (double)co / ca : 0.0, ca, la > 0 ? (double)lo / la : 0.0, la));
                sb.AppendLine("## foot slide by action: contacts | share over 0.30 m/s | mean m/s");
                foreach (var kv in _cnt.Where(k => k.Key.StartsWith("slide contacts ACTION ") && k.Key != "slide contacts ACTION all" && k.Value >= 15).OrderBy(k => k.Key))
                {
                    string name = kv.Key.Substring("slide contacts ACTION ".Length); int ov; _cnt.TryGetValue("slide contacts over 0.30 m/s ACTION " + name, out ov);
                    List<double> sl; _acc.TryGetValue("contact min slide m/s ACTION " + name, out sl);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-40} {1,7} {2,6:P1} {3,6:F2}", name, kv.Value, (double)ov / kv.Value, sl != null ? sl.Average() : 0.0));
                }
            }
            sb.AppendLine("\n## most frequent clip changes (group, sim action before > after: clip -> clip)");
            foreach (var kv in _cnt.Where(k => k.Key.StartsWith("switch ")).OrderByDescending(k => k.Value).Take(45)) sb.AppendLine(string.Format("{0,6}  {1}", kv.Value, kv.Key.Substring(7)));
            sb.AppendLine("\n## plays\n" + _report);
            File.WriteAllText(_out + "/choreo_audit_" + _tag + ".txt", sb.ToString());
        }
    }

    /// <summary>Choreographer hooks of the audit (switching the sim option, reading the bookings of a finished play).</summary>
    static partial class ChoreoHooks
    {
        public static void Configure(PlaySimOptions opt, bool on) { opt.ChoreoLibrary = on ? GameChoreoLibrary.Shared : null; }
        public static void Report(PlaySimulation sim, int play, StringBuilder report, ChoreoAuditRunner audit)
        {
            if (sim.Choreo == null) return;
            var st = sim.Choreo.Stats;
            audit.Count("choreo bookings", st.Bookings); audit.Count("choreo tackle bookings", st.TackleBookings); audit.Count("choreo catch bookings", st.CatchBookings);
            audit.Count("choreo follow ticks", st.FollowTicks); audit.Count("choreo steered ticks", st.SteeredTicks);
            foreach (float e in st.EtaError) audit.Note("choreo tackle forecast error at 0.5 s (s)", e);
            audit.Note("choreo steering: largest bend per play (yd/s)", st.MaxSteerRate);
            audit.Count("contested-catch duels rolled ahead (per run)", st.DuelRolls); audit.Count("contested-catch duel wins rolled (per run)", st.DuelCatches); audit.Count("contested-catch pair takes booked (per run)", st.DuelBookings);
            audit.Note("choreo hip flips planned per play", st.TurnBookings); foreach (var tn in sim.Choreo.Turns) { audit.Note("choreo hip flip: turn angle deg", tn.TotalRad * 57.29578); audit.Note("choreo hip flip: duration s", tn.Duration); }
            foreach (var b in sim.Choreo.Bookings) if (b.Kind == ChoreoKind.Tackle && b.FramesFollowed > 0) audit.Note("choreo booked take: largest one-tick position change (yd)", b.MaxStepErr);
        }
    }
}
