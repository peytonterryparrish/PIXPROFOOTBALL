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
    /// v0.11.0 RB track (playtest note 7): ball-carrier audit. Runs REAL run plays through PlayStage (22 views + ball follower) and measures the carrier frame by frame:
    /// ball pose compliance (outside arm, high and tight), arm switches against cuts, planted-foot slide, cut speed loss and re-acceleration, hip sink, lean, free-arm swing and its
    /// opposition to the legs, move frequency per carry, yards per carry. Optionally renders broadcast + side film strips of the moves and cuts.
    /// Run: Unity -projectPath ... -executeMethod PixFootball.EditorTools.CarryAudit.Run -pixCarryAudit  (batch mode WITHOUT -nographics).
    /// Args: -pixCarryOut dir, -pixCarryPlays n (default 64), -pixCarryStrips 0|1, -pixCarryTag name, -pixCarrySeed n.
    /// </summary>
    public static class CarryAudit
    {
        public const string Flag = "-pixCarryAudit";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(90);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(90);
            new GameObject("PixCarryAudit").AddComponent<CarryAuditRunner>();
        }
    }

    public sealed class CarryAuditRunner : MonoBehaviour
    {
        const float Dt = 1f / 60f;
        string _out, _tag;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        int _playIndex;
        readonly StringBuilder _report = new StringBuilder();
        readonly Dictionary<string, List<double>> _acc = new Dictionary<string, List<double>>();
        readonly Dictionary<string, int> _hist = new Dictionary<string, int>();
        readonly StringBuilder _carries = new StringBuilder("play,play_name,defense,carrier,pos,carry_s,gain_yd,moves,cuts,arm_switches,wrong_arm_frames,threat_frames,end\n");
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }

        void Add(string k, double v) { List<double> l; if (!_acc.TryGetValue(k, out l)) _acc[k] = l = new List<double>(); l.Add(v); }
        void Hist(string k, int n = 1) { int c; _hist.TryGetValue(k, out c); _hist[k] = c + n; }

        sealed class B
        {
            public Transform Hips, Spine, Chest, Head, LF, RF, LT, RT, LH, RH, LUL, RUL, LLL, RLL, LUA, RUA, LLA, RLA;
            public Vector3 PrevLT, PrevRT, PrevLF, PrevRF; public bool PlantL, PlantR, HavePrev;
        }
        readonly Dictionary<SimAgentView, B> _bones = new Dictionary<SimAgentView, B>();

        B Bones(SimAgentView v)
        {
            B b;
            if (_bones.TryGetValue(v, out b) && b.Hips != null) return b;
            var an = v.Avatar.Animator; if (an == null || !an.isHuman) return null;
            b = new B();
            b.Hips = an.GetBoneTransform(HumanBodyBones.Hips); b.Spine = an.GetBoneTransform(HumanBodyBones.Spine); b.Chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.UpperChest);
            b.Head = an.GetBoneTransform(HumanBodyBones.Head);
            b.LF = an.GetBoneTransform(HumanBodyBones.LeftFoot); b.RF = an.GetBoneTransform(HumanBodyBones.RightFoot);
            b.LT = an.GetBoneTransform(HumanBodyBones.LeftToes); b.RT = an.GetBoneTransform(HumanBodyBones.RightToes);
            b.LH = an.GetBoneTransform(HumanBodyBones.LeftHand); b.RH = an.GetBoneTransform(HumanBodyBones.RightHand);
            b.LUL = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); b.RUL = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            b.LLL = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg); b.RLL = an.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            b.LUA = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); b.RUA = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            b.LLA = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); b.RLA = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _bones[v] = b;
            return b;
        }

        // per-carry series (60 Hz), analysed when the carry ends
        sealed class Carry
        {
            public int Play, Agent; public string Pos;
            public float Start = -1f, T0;
            public readonly List<float> Speed = new List<float>(), Heading = new List<float>(), HipsY = new List<float>(), FreeArm = new List<float>(), OppLeg = new List<float>(), SameLeg = new List<float>();
            public readonly List<bool> Moving = new List<bool>(), Contact = new List<bool>();
            public int Moves, ArmSwitches, WrongArm, Threat, Cuts;
            public int LastSide; public float LastSwitchT = -9f;
            public CarrierMove PrevMove;
            public int Frames;
            public readonly List<int> Plants = new List<int>(), PlantKind = new List<int>(); public readonly List<float> PlantAngle = new List<float>(); public CarryCutPhase PrevPhase;
        }

        class Cap
        {
            public string Name; public float Next, Step; public int Left;
            public readonly List<Texture2D> Side = new List<Texture2D>(), Bcast = new List<Texture2D>();
        }
        Cap _cap; readonly Dictionary<string, int> _capCount = new Dictionary<string, int>();
        bool _strips;
        int _capLimit;

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _tag = CarryAudit.Arg("-pixCarryTag", "run");
            _out = CarryAudit.Arg("-pixCarryOut", "D:/PIXFootball/anim_rb/v0110/audit_" + _tag);
            Directory.CreateDirectory(_out); Directory.CreateDirectory(_out + "/strips");
            _strips = CarryAudit.Arg("-pixCarryStrips", "0") == "1";
            _capLimit = int.Parse(CarryAudit.Arg("-pixCarryStripsPerKind", "2"));
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) Debug.Log("[CarryAudit] logged " + t + ": " + c + " :: " + (st.Length > 300 ? st.Substring(0, 300) : st)); };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("CarryAuditCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("CarryAuditStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            int n = int.Parse(CarryAudit.Arg("-pixCarryPlays", "64"));
            yield return Plays(n);
            WriteSummary();
            Debug.Log("[CarryAudit] done " + _out);
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

        void Prep()
        {
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
        }

        // ------------------------------------------------------------------------------------------ plays
        IEnumerator Plays(int n)
        {
            var runs = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && (p.Category == PlayCategory.InsideRun || p.Category == PlayCategory.OutsideRun || p.Category == PlayCategory.Draw)).ToList();
            var defs = _book.AllPlays.Where(p => p.Side == PlaySide.Defense && (p.Category == PlayCategory.ZoneCoverage || p.Category == PlayCategory.ManCoverage || p.Category == PlayCategory.Blitz || p.Category == PlayCategory.GoalLine)).ToList();
            int seed0 = int.Parse(CarryAudit.Arg("-pixCarrySeed", "7100"));
            int[] spots = { 25, 35, 45, 30, 55, 40, 20, 65 };
            for (int pi = 0; pi < n; pi++)
            {
                var off = runs[pi % runs.Count];
                var def = defs[(pi * 7 + pi / runs.Count * 3) % defs.Count];
                var sim = NewSim(off, def, Scrim(spots[pi % spots.Length], 1 + pi % 3, 10), seed0 + pi);
                var frame = new PlayFrame(pi % 2 == 0);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
                Prep();
                for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                foreach (var b in _bones.Values) b.HavePrev = false;
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                float t = 0f;
                Carry carry = null; int holderId = -1;
                while (sim.Phase != PlayPhase.Dead && t < 12f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt;
                    yield return null;
                    int h = sim.Ball.Holder;
                    bool holding = h >= 0 && sim.Ball.State == BallState.Held && sim.Agents[h].Side == SimSide.Offense && sim.Agents[h].Slot != 0;
                    if (holding && carry == null) { carry = new Carry { Play = pi, Agent = h, Pos = sim.Agents[h].PlayPos.ToString(), T0 = t }; holderId = h; }
                    if (holding && h == holderId && carry != null) SampleCarrier(sim, carry, t);
                    if (_cap != null) CapService(sim, holderId, t);
                }
                for (int f = 0; f < 100 && sim.Phase == PlayPhase.Dead; f++)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null;
                    if (holderId >= 0 && f > 20 && _stage.Views[holderId] != null) { var pb = Bones(_stage.Views[holderId]); if (pb != null) { Vector3 bp = _stage.Ball.Position; float dd = Mathf.Min(Vector3.Distance(bp, pb.LH.position), Vector3.Distance(bp, pb.RH.position)); Add("post-whistle ball to the carrier's nearest hand m (0.35-1.7 s after the whistle)", dd); if (dd > 1.0f) Hist("post-whistle frames with the ball > 1 m from the carrier's hands (B-023)"); Hist("post-whistle frames"); } }
                }
                int gain = sim.Result != null ? sim.Result.Yards : 0;
                if (carry != null && carry.Frames > 30)
                {
                    Analyse(carry, sim);
                    _carries.AppendLine(pi + "," + off.Name.Replace(',', ' ') + "," + def.Name.Replace(',', ' ') + "," + carry.Agent + "," + carry.Pos + "," + (carry.Frames * Dt).ToString("0.00", CultureInfo.InvariantCulture) + "," + gain + "," + carry.Moves + "," + carry.Cuts + "," + carry.ArmSwitches + "," + carry.WrongArm + "," + carry.Threat + "," + sim.EndReason);
                    Add("gain yd (RB carries)", gain);
                    Add("moves per carry", carry.Moves);
                    Add("cuts per carry", carry.Cuts);
                    Add("arm switches per carry", carry.ArmSwitches);
                    Add("carry duration s", carry.Frames * Dt);
                    Hist("carries");
                }
                _report.AppendLine("play " + pi + " " + off.Name + " vs " + def.Name + " ended " + sim.EndReason + " gain " + gain + " t " + t.ToString("0.0"));
                _cap = null;
                _stage.End();
                yield return null;
            }
        }

        // ------------------------------------------------------------------------------------------ per-frame sampling of the carrier
        static float Pitch(Vector3 v, Vector3 fwd) { return Mathf.Atan2(Vector3.Dot(v, fwd), v.y) * Mathf.Rad2Deg; }
        static float ArmPitch(Vector3 v, Vector3 fwd) { return Mathf.Atan2(Vector3.Dot(v, fwd), -v.y) * Mathf.Rad2Deg; }

        void SampleCarrier(PlaySimulation sim, Carry c, float t)
        {
            var a = sim.Agents[c.Agent]; var v = _stage.Views[c.Agent]; if (v == null || v.Avatar == null) return;
            var b = Bones(v); if (b == null) return;
            var frame = _stage.Frame;
            float gy = v.transform.position.y;
            float speedMs = a.Speed * 0.9144f;
            bool contact = v.ContactDriven || a.IsDown || a.Action == AgentAction.Tackled || a.Action == AgentAction.Fallen || a.Action == AgentAction.Celebrate || a.Action == AgentAction.Catch || a.Action == AgentAction.Handoff;
            if (c.Start < 0f) c.Start = t;
            float held = t - c.Start;
            Vector3 fwd = v.Forward; Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 vel = frame.VelToUnity(a.Vel);
            float heading = speedMs > 1.0f ? Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg : (c.Heading.Count > 0 ? c.Heading[c.Heading.Count - 1] : v.Yaw);
            c.Frames++;
            c.Speed.Add(speedMs); c.Heading.Add(heading); c.HipsY.Add(b.Hips.position.y - gy);
            c.Moving.Add(a.Move != CarrierMove.None);
            c.Contact.Add(contact || a.Body.StumbleTime > 0f || (a.State != AgentState.Normal));
            { var ph = a.CutPhase; if (ph == CarryCutPhase.Plant && c.PrevPhase != CarryCutPhase.Plant) { c.Plants.Add(c.Speed.Count - 1); c.PlantAngle.Add(a.CutAngle); c.PlantKind.Add((int)a.CutKind); Hist("plants " + a.CutKind); } if (ph == CarryCutPhase.Approach && c.PrevPhase != CarryCutPhase.Approach) Hist("cuts announced"); c.PrevPhase = ph; }
            // move transitions
            if (a.Move != CarrierMove.None && c.PrevMove == CarrierMove.None) { c.Moves++; Hist("move " + a.Move); }
            c.PrevMove = a.Move;
            // free arm / opposite leg sagittal angle series (arm swing and counter-rotation)
            int holdSideNow = HoldSide(b, _stage.Ball.Position);
            {
                Transform ua = holdSideNow > 0 ? b.LUA : b.RUA, la = holdSideNow > 0 ? b.LLA : b.RLA;      // the free arm is the other one
                Transform opp = holdSideNow > 0 ? b.LUL : b.RUL, oppK = holdSideNow > 0 ? b.LLL : b.RLL;      // the leg on the free arm's side: counter-swing means arm forward while the other-side leg is forward
                Transform same = holdSideNow > 0 ? b.RUL : b.LUL, sameK = holdSideNow > 0 ? b.RLL : b.LLL;
                float armA = ArmPitch(la.position - ua.position, fwd);
                float oppA = ArmPitch(oppK.position - opp.position, fwd), sameA = ArmPitch(sameK.position - same.position, fwd);
                c.FreeArm.Add(armA); c.OppLeg.Add(oppA); c.SameLeg.Add(sameA);
            }
            if (contact || held < 0.45f) { b.HavePrev = false; return; }

            // ---- ball pose
            Vector3 ball = _stage.Ball.Position;
            float chestD = Vector3.Distance(ball, b.Chest.position);
            float aboveHips = ball.y - b.Hips.position.y;
            float aboveChest = ball.y - b.Chest.position.y;
            { float dh = Mathf.Min(Vector3.Distance(ball, b.LH.position), Vector3.Distance(ball, b.RH.position)); Add("ball to nearest hand m", dh); if (dh > 0.40f) { Hist("ball > 40 cm from both hands while carried (B-065)"); Hist("B-065 detail: action " + a.Action + (v.CarrySwitching ? " [arm switch]" : "") + (a.Move != CarrierMove.None ? " move " + a.Move : "") + (a.CarryTwoHands ? " [two hands]" : "") + " held " + (held < 1f ? "<1s" : held < 2f ? "1-2s" : ">2s") + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : "")); } }
            if (_strips && Mathf.Min(Vector3.Distance(ball, b.LH.position), Vector3.Distance(ball, b.RH.position)) > 0.40f) TryCap("b065", sim, c.Agent, 0f, 0.1f, 8);
            Add("ball to chest m", chestD); Add("ball above hips m", aboveHips); Add("ball above chest m", aboveChest);
            bool tight = chestD <= 0.36f, high = aboveHips >= 0.12f;
            Hist("pose frames"); if (tight) Hist("tight frames (ball<=36cm of chest)"); if (high) Hist("high frames (ball>=12cm above hips)"); if (tight && high) Hist("high+tight frames");
            // elbow flexion of the holding arm
            {
                Transform ua = holdSideNow > 0 ? b.RUA : b.LUA, la = holdSideNow > 0 ? b.RLA : b.LLA, hd = holdSideNow > 0 ? b.RH : b.LH;
                float ang = Vector3.Angle(ua.position - la.position, hd.position - la.position);
                Add("holding elbow angle deg (180 = straight)", ang);
                // ball to the forearm midpoint
                Add("ball to holding forearm m", Vector3.Distance(ball, (la.position + hd.position) * 0.5f));
            }
            // ---- outside arm: away from the nearest threatening defender
            SimAgent near = null; float nd = 99f;
            for (int j = 11; j < 22; j++) { var o = sim.Agents[j]; if (o.IsDown || o.Body.FallenTime > 0.3f) continue; float d = (o.Pos - a.Pos).Length; if (d < nd) { nd = d; near = o; } }
            if (near != null && nd < 6f && speedMs > 1.5f)
            {
                Vector3 to = frame.VelToUnity(near.Pos - a.Pos);
                float lat = Vector3.Dot(right, to);
                if (Mathf.Abs(lat) > 0.55f)
                {
                    int expect = lat > 0f ? -1 : 1;      // defender on the right: ball in the left arm (-1)
                    c.Threat++; Hist("threat frames"); if (nd < 3f) Hist("threat frames <3yd");
                    bool ok = holdSideNow == expect;
                    if (!ok) { c.WrongArm++; Hist("wrong-arm frames"); if (nd < 3f) Hist("wrong-arm frames <3yd"); }
                }
            }
            if (near != null && nd < 6f && speedMs > 1.5f && a.CarryArm != 0)
            {
                Vec2 hd = a.Speed > 1.5f ? a.Vel.Norm() : a.Facing; float sl = (near.Pos - a.Pos).Dot(new Vec2(hd.Y, -hd.X));
                if (Mathf.Abs(sl) > 0.6f) { Hist("SIM threat frames"); if (a.CarryArm != (sl > 0f ? -1 : 1)) Hist("SIM wrong-arm frames"); if (holdSideNow != a.CarryArm) Hist("view arm differs from the sim arm frames"); }
            }
            if (c.LastSide != 0 && holdSideNow != c.LastSide && t - c.LastSwitchT > 0.15f)
            {
                c.ArmSwitches++; c.LastSwitchT = t; Hist("arm switches");
                // justified when the nearest defender (within 8 yd) now stands on the old arm's side
                if (near != null && nd < 8f)
                {
                    float lat2 = Vector3.Dot(right, frame.VelToUnity(near.Pos - a.Pos));
                    int expect2 = lat2 > 0f ? -1 : 1;
                    Hist(expect2 == holdSideNow ? "arm switches justified (defender now on the other side)" : "arm switches unjustified");
                }
                else Hist("arm switches with no defender within 8yd");
                if (_strips) TryCap("armswitch", sim, c.Agent, -0.25f);
            }
            c.LastSide = holdSideNow;

            // ---- lean and height
            float lean = Pitch(b.Chest.position - b.Hips.position, fwd);
            string sb = speedMs < 3f ? "<3" : speedMs < 5.5f ? "3-5.5" : speedMs < 7f ? "5.5-7" : ">=7";
            Add("trunk lean deg (+ = forward) speed " + sb, lean);
            Add("hips height m speed " + sb, b.Hips.position.y - gy);
            if (nd < 3f) { Add("hips height m, defender < 3 yd", b.Hips.position.y - gy); Add("trunk lean deg, defender < 3 yd", lean); }
            else if (nd > 7f && speedMs > 4f) { Add("hips height m, open field", b.Hips.position.y - gy); Add("trunk lean deg, open field", lean); }
            // forward acceleration from the sim velocity history
            if (c.Speed.Count > 12)
            {
                float acc = (c.Speed[c.Speed.Count - 1] - c.Speed[c.Speed.Count - 7]) / (6 * Dt);
                if (acc > 2f && speedMs > 2f) Add("trunk lean deg, accelerating (>2 m/s2)", lean);
                else if (acc < -3f && speedMs > 2f) Add("trunk lean deg, braking (<-3 m/s2)", lean);
            }

            // ---- planted-foot slide (contact-minimum, as EngineAudit)
            Vector3 ltP = b.LT != null ? b.LT.position : b.LF.position, rtP = b.RT != null ? b.RT.position : b.RF.position;
            float lth = ltP.y - gy, rth = rtP.y - gy, lah = b.LF.position.y - gy, rah = b.RF.position.y - gy;
            float lc = Mathf.Min(lth, lah - 0.09f), rc = Mathf.Min(rth, rah - 0.09f);
            bool plantL = b.PlantL ? lc < 0.07f : lc < 0.04f, plantR = b.PlantR ? rc < 0.07f : rc < 0.04f;
            if (b.HavePrev && speedMs > 1.5f)
            {
                Vector3 dl = ltP - b.PrevLT; dl.y = 0f; Vector3 dr = rtP - b.PrevRT; dr.y = 0f;
                Vector3 dla = b.LF.position - b.PrevLF; dla.y = 0f; Vector3 dra = b.RF.position - b.PrevRF; dra.y = 0f;
                string key = a.Move != CarrierMove.None ? "move " + a.Move : "carry run";
                if (plantL && b.PlantL) { float s = Mathf.Min(dl.magnitude, dla.magnitude) / Dt; Add("planted foot slide m/s, " + key, s); Add("planted foot slide m/s, carry (all)", s); }
                if (plantR && b.PlantR) { float s = Mathf.Min(dr.magnitude, dra.magnitude) / Dt; Add("planted foot slide m/s, " + key, s); Add("planted foot slide m/s, carry (all)", s); }
                if (lth > 0.10f && rth > 0.10f) Hist("flight frames (both toes > 10 cm)"); Hist("loco frames");
            }
            b.PlantL = plantL; b.PlantR = plantR; b.PrevLT = ltP; b.PrevRT = rtP; b.PrevLF = b.LF.position; b.PrevRF = b.RF.position; b.HavePrev = true;
            Add("carrier ground speed m/s", speedMs);
            if (_strips)
            {
                if (a.Move != CarrierMove.None) TryCap("move_" + a.Move, sim, c.Agent, -0.05f);
                else if (held > 1.2f && speedMs > 5f && c.Frames % 150 == 0) TryCap("carry_cruise", sim, c.Agent, -0.05f, 0.1f, 10);
            }
        }

        static int HoldSide(B b, Vector3 ball)
        {
            float dl = Vector3.Distance(ball, b.LH.position), dr = Vector3.Distance(ball, b.RH.position);
            // also the forearm midpoint: the ball sits on the forearm, not in the palm
            float fl = Vector3.Distance(ball, (b.LLA.position + b.LH.position) * 0.5f), fr = Vector3.Distance(ball, (b.RLA.position + b.RH.position) * 0.5f);
            return (dr + fr) <= (dl + fl) ? 1 : -1;
        }

        // ------------------------------------------------------------------------------------------ analysis of one carry
        void Analyse(Carry c, PlaySimulation sim)
        {
            int n = c.Speed.Count; if (n < 70) return;
            int lastCut = -999;
            for (int i = 24; i < n - 30; i++)
            {
                if (i - lastCut < 80) continue;
                float vPre0 = c.Speed[i - 24];
                if (vPre0 < 3.2f) continue;
                float dh = Mathf.Abs(Mathf.DeltaAngle(c.Heading[i - 24], c.Heading[i]));
                if (dh < 30f) continue;
                { bool bad = false; for (int k = i - 24; k <= Math.Min(n - 1, i + 60); k++) if (c.Contact[k]) { bad = true; break; } if (bad || i > n - 50) continue; }
                // the cut is complete when the heading stops changing: find the end of the turn
                lastCut = i; c.Cuts++;
                float vPre = 0f; for (int k = Math.Max(0, i - 40); k <= i - 20; k++) vPre = Mathf.Max(vPre, c.Speed[k]);
                float vMin = 99f; int tMin = i; for (int k = i - 20; k <= Math.Min(n - 1, i + 30); k++) if (c.Speed[k] < vMin) { vMin = c.Speed[k]; tMin = k; }
                float vPost = 0f; int end = Math.Min(n - 1, i + 90); for (int k = Math.Min(n - 1, i + 40); k <= end; k++) vPost = Mathf.Max(vPost, c.Speed[k]);
                float loss = 1f - vMin / Mathf.Max(0.1f, vPre);
                string ab = dh < 60f ? "40-60" : dh < 90f ? "60-90" : "90+";
                Add("cut speed loss fraction, angle " + ab + " deg", loss);
                Add("cut speed loss fraction (all)", loss);
                if (end - i >= 45) { Add("cut re-acceleration: speed 0.7-1.5 s after / before (all)", vPost / Mathf.Max(0.1f, vPre)); Hist(vPost >= 0.9f * vPre ? "cuts re-accelerated to >= 90%" : "cuts NOT re-accelerated to 90%"); }
                Add("cut time to speed minimum s", (tMin - (i - 24)) * Dt);
                // hip sink
                float base0 = 0f; int bc = 0; for (int k = Math.Max(0, i - 70); k < Math.Max(1, i - 30); k++) { base0 += c.HipsY[k]; bc++; }
                if (bc > 0) { base0 /= bc; float low = 9f; for (int k = Math.Max(0, i - 12); k <= Math.Min(n - 1, i + 20); k++) low = Mathf.Min(low, c.HipsY[k]); Add("cut hip sink m (below running mean)", base0 - low); }
                Hist("cuts " + ab);
            }
            for (int q = 0; q < c.Plants.Count; q++)
            {
                int p = c.Plants[q]; if (p < 40 || p > n - 50) { Hist("plants too close to the carry ends"); continue; }
                bool bad = false; for (int k = p - 6; k <= Math.Min(n - 1, p + 36); k++) if (c.Contact[k]) { bad = true; break; }
                if (bad) { Hist("plants in contact"); continue; }
                float vPre = 0f; for (int k = Math.Max(0, p - 36); k <= p - 6; k++) vPre = Mathf.Max(vPre, c.Speed[k]);
                float vMin = 99f; int tMin = p; for (int k = p - 6; k <= Math.Min(n - 1, p + 36); k++) if (c.Speed[k] < vMin) { vMin = c.Speed[k]; tMin = k; }
                float loss = 1f - vMin / Mathf.Max(0.1f, vPre); float ang = Mathf.Abs(c.PlantAngle[q]);
                string ab = ang < 60f ? "36-60" : ang < 90f ? "60-90" : "90+";
                Add("SIM cut speed loss fraction, angle " + ab + " deg", loss); Add("SIM cut speed loss fraction (all)", loss); Add("SIM cut speed before the plant m/s", vPre);
                Add("SIM cut time from plant to speed minimum s", (tMin - p) * Dt);
                float vPost = 0f; int e2 = Math.Min(n - 1, p + 110); int cnt = 0; for (int k = Math.Min(n - 1, p + 50); k <= e2; k++) { vPost = Mathf.Max(vPost, c.Speed[k]); cnt++; }
                if (cnt > 20) { Add("SIM cut re-acceleration: best speed 0.8-1.8 s after / before", vPost / Mathf.Max(0.1f, vPre)); Hist(vPost >= 0.9f * vPre ? "SIM cuts back to 90 percent of the pre-cut speed within 1.8 s" : "SIM cuts NOT back to 90 percent within 1.8 s"); }
                float b0 = 0f; int bc = 0; for (int k = Math.Max(0, p - 80); k < p - 30; k++) { b0 += c.HipsY[k]; bc++; }
                if (bc > 0) { b0 /= bc; float low = 9f; for (int k = Math.Max(0, p - 8); k <= Math.Min(n - 1, p + 24); k++) low = Mathf.Min(low, c.HipsY[k]); Add("SIM cut hip sink m (below the running mean)", b0 - low); }
            }
            // free arm swing amplitude and its opposition to the leg (cruising frames)
            int w = 36;
            for (int i = 0; i + w < n; i += w)
            {
                bool ok = true; float sm = 0f; for (int k = i; k < i + w; k++) { if (c.Moving[k] || c.Speed[k] < 4.2f) { ok = false; break; } sm += c.Speed[k]; }
                if (!ok) continue;
                float mn = 999f, mx = -999f, mean = 0f, meanO = 0f, meanS = 0f;
                for (int k = i; k < i + w; k++) { mn = Mathf.Min(mn, c.FreeArm[k]); mx = Mathf.Max(mx, c.FreeArm[k]); mean += c.FreeArm[k]; meanO += c.OppLeg[k]; meanS += c.SameLeg[k]; }
                mean /= w; meanO /= w; meanS /= w;
                float cov = 0f, va = 0f, vo = 0f, covS = 0f, vs = 0f;
                for (int k = i; k < i + w; k++) { float x = c.FreeArm[k] - mean, y = c.OppLeg[k] - meanO, z = c.SameLeg[k] - meanS; cov += x * y; va += x * x; vo += y * y; covS += x * z; vs += z * z; }
                Add("free arm swing range deg (cruise)", mx - mn);
                if (va > 1f && vo > 1f) Add("free arm vs leg-on-its-side correlation (+1 = swings WITH that leg, -1 = counter-swing)", cov / Mathf.Sqrt(va * vo));
                if (va > 1f && vs > 1f) Add("free arm vs opposite-side leg correlation (+1 = counter-swing)", covS / Mathf.Sqrt(va * vs));
            }
        }

        // ------------------------------------------------------------------------------------------ strips
        void TryCap(string name, PlaySimulation sim, int agent, float startOffset, float step = 0.07f, int count = 12)
        {
            if (_cap != null) return;
            int k; _capCount.TryGetValue(name, out k); if (k >= _capLimit) return;
            _capCount[name] = k + 1;
            _cap = new Cap { Name = name + "_p" + _playIndex + "_" + k, Next = 0f, Step = step, Left = count };
            _cap.Next = -1f;      // first shot on the next service call
        }

        void CapService(PlaySimulation sim, int agent, float t)
        {
            if (_cap == null || agent < 0) return;
            if (_cap.Next < 0f) _cap.Next = t;
            if (t < _cap.Next) return;
            _cap.Next += _cap.Step; _cap.Left--;
            _cap.Side.Add(Shot(agent, false)); _cap.Bcast.Add(Shot(agent, true));
            if (_cap.Left <= 0 || sim.Phase == PlayPhase.Dead)
            {
                Sheet(_cap.Side, 6, _out + "/strips/" + _cap.Name + "_side.png");
                Sheet(_cap.Bcast, 6, _out + "/strips/" + _cap.Name + "_bcast.png");
                _cap = null;
            }
        }

        Texture2D Shot(int agent, bool broadcast)
        {
            var v = _stage.Views[agent]; Vector3 q = v.transform.position;
            Vector3 down = _stage.Frame.DirToUnity(new Vec2(0f, 1f)); Vector3 dr = Vector3.Cross(Vector3.up, down);
            if (broadcast) { _cam.transform.position = q - down * 11f + Vector3.up * 6.0f + dr * 1.0f; _cam.transform.rotation = Quaternion.LookRotation(q + down * 2.5f + Vector3.up * 0.9f - _cam.transform.position); _cam.fieldOfView = 28f; }
            else { _cam.transform.position = q + dr * 4.6f + Vector3.up * 1.3f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 1.0f - _cam.transform.position); _cam.fieldOfView = 34f; }
            var hid = new List<Renderer>();
            int near = -1; { float bd = 99f; for (int j = 11; j < 22; j++) { var ov = _stage.Views[j]; if (ov == null) continue; float d = (ov.transform.position - q).magnitude; if (d < bd) { bd = d; near = j; } } }
            for (int j = 0; j < 22; j++) { var ov = _stage.Views[j]; if (ov == null || ov == v || j == near) continue; foreach (var rr in ov.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hid.Add(rr); } }
            var shot = ShotCam(400, 330);
            foreach (var rr in hid) rr.enabled = true;
            return shot;
        }

        Texture2D ShotCam(int w, int h)
        {
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

        // ------------------------------------------------------------------------------------------ summary
        static string Stat(List<double> l)
        {
            var s = l.OrderBy(x => x).ToList(); int n = s.Count;
            return "n " + n + "  mean " + (s.Sum() / n).ToString("0.000", CultureInfo.InvariantCulture) + "  p10 " + s[(int)(n * 0.1)].ToString("0.000", CultureInfo.InvariantCulture) + "  p50 " + s[n / 2].ToString("0.000", CultureInfo.InvariantCulture)
                   + "  p90 " + s[Math.Min(n - 1, (int)(n * 0.9))].ToString("0.000", CultureInfo.InvariantCulture) + "  max " + s[n - 1].ToString("0.000", CultureInfo.InvariantCulture);
        }

        void WriteSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# RB carry audit (" + _tag + ")");
            sb.AppendLine();
            foreach (var k in _acc.Keys.OrderBy(x => x)) if (_acc[k].Count > 0) sb.AppendLine(k + ": " + Stat(_acc[k]));
            sb.AppendLine();
            foreach (var k in _hist.Keys.OrderBy(x => x)) sb.AppendLine(k + ": " + _hist[k]);
            sb.AppendLine();
            int pf; _hist.TryGetValue("pose frames", out pf);
            Func<string, double> frac = key => { int c; _hist.TryGetValue(key, out c); return pf > 0 ? (double)c / pf : 0.0; };
            sb.AppendLine("derived: ball high+tight fraction " + frac("high+tight frames").ToString("0.000", CultureInfo.InvariantCulture));
            int th; _hist.TryGetValue("threat frames", out th); int wr; _hist.TryGetValue("wrong-arm frames", out wr);
            sb.AppendLine("derived: outside-arm compliance (threat frames) " + (th > 0 ? (1.0 - (double)wr / th).ToString("0.000", CultureInfo.InvariantCulture) : "n/a") + " of " + th + " frames");
            int th3; _hist.TryGetValue("threat frames <3yd", out th3); int wr3; _hist.TryGetValue("wrong-arm frames <3yd", out wr3);
            sb.AppendLine("derived: outside-arm compliance (defender < 3 yd) " + (th3 > 0 ? (1.0 - (double)wr3 / th3).ToString("0.000", CultureInfo.InvariantCulture) : "n/a") + " of " + th3 + " frames");
            int carries; _hist.TryGetValue("carries", out carries);
            int sw; _hist.TryGetValue("arm switches", out sw);
            sb.AppendLine("derived: carries " + carries + ", arm switches per carry " + (carries > 0 ? ((double)sw / carries).ToString("0.00", CultureInfo.InvariantCulture) : "n/a"));
            int fl; _hist.TryGetValue("flight frames (both toes > 10 cm)", out fl); int lf; _hist.TryGetValue("loco frames", out lf);
            sb.AppendLine("derived: flight fraction " + (lf > 0 ? ((double)fl / lf).ToString("0.000", CultureInfo.InvariantCulture) : "n/a"));
            File.WriteAllText(_out + "/carry_summary.txt", sb.ToString());
            File.WriteAllText(_out + "/carries.csv", _carries.ToString());
            File.WriteAllText(_out + "/carry_report.txt", _report.ToString());
        }
    }
}
