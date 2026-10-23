using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
    /// v0.10.0 QB track audit (QA only, behind <c>-pixQbChoreo</c>): runs the quarterback's whole choreographed sequence through the real PlayStage (huddle break, walk, survey, step in, crouch, cadence, snap, secure, drop,
    /// set, pocket, throw / scramble) and the three kicks, logs per-frame numbers for the quarterback / kicker and writes frame strips.
    /// Args: -pixQbChoreoOut dir, -pixQbChoreoPhase metrics|strips|kicks|all, -pixQbChoreoOnly name,name (scenario filter), -pixQbChoreoTag text.
    /// Outputs: qb_summary.txt, qb_frames_[scenario].csv, kick_report.txt, strips/*.png.
    /// </summary>
    public static class QbChoreoAudit
    {
        public const string Flag = "-pixQbChoreo";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(40);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(40);
            new GameObject("PixQbChoreoAudit").AddComponent<QbChoreoRunner>();
        }
    }

    public sealed class QbChoreoRunner : MonoBehaviour
    {
        string _out;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _report = new StringBuilder();
        int _playIndex;
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }
        const float Dt = 1f / 60f;

        sealed class Scn
        {
            public string Name; public Play Off; public Play Def; public int Seed; public bool UserScramble; public bool UserThrow; public float ScrambleAfter = 1.4f; public bool Huddle = true;
        }

        struct Fr
        {
            public float T, SimT; public int Frame; public QbPhase State; public AgentAction Act; public float Speed, HipsYaw, ChestYaw, HeadYaw, HeadErr, BallD, SlideL, SlideR, ToeL, ToeR, HipsY;
            public bool PlantL, PlantR; public int Holder; public BallState Ball; public string Seq; public bool Gliding; public int Steps; public float KickW; public Vector3 ToePosL, ToePosR; public float AnkL, AnkR, HandRb, Eta, RbDist, ShoulderRb, RbYaw; public float SimPosX, SimPosY, LookErr;
        }

        sealed class Result
        {
            public Scn S; public List<Fr> F = new List<Fr>(); public int ExchangeFrame = -1; public int SnapFrame = -1, CatchFrame = -1, ThrowFrame = -1, SetFrame = -1, DropStartFrame = -1, ReadyFrame = -1, GlideStartFrame = 0;
            public float SnapT = -1, CatchT = -1, ThrowT = -1, SetT = -1, DropT = -1, ReadyT = -1, End;
            public List<string> Notes = new List<string>();
        }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            if (QbChoreoAudit.Arg("-pixQbChoreoNoKickIk", "0") == "1") SimAgentView.DebugNoKickIk = true;
            _out = QbChoreoAudit.Arg("-pixQbChoreoOut", "D:/PIXFootball/anim_qb/v0100/run");
            Directory.CreateDirectory(_out); Directory.CreateDirectory(_out + "/strips");
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception) { Debug.Log("[QbChoreo] exception: " + c + " :: " + (st.Length > 400 ? st.Substring(0, 400) : st)); try { File.WriteAllText(_out + "/qb_report_PARTIAL.txt", _report.ToString()); } catch { } EditorApplication.Exit(3); } };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("QbChoreoCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("QbChoreoStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            string phase = QbChoreoAudit.Arg("-pixQbChoreoPhase", "all");
            string only = QbChoreoAudit.Arg("-pixQbChoreoOnly", "");
            var filter = only.Length > 0 ? only.Split(',') : null;
            var scns = Scenarios().Where(s => filter == null || Array.IndexOf(filter, s.Name) >= 0).ToList();
            var results = new List<Result>();
            if (phase == "all" || phase == "metrics" || phase == "strips")
            {
                foreach (var s in scns)
                {
                    Result r = null;
                    yield return RunScenario(s, null, null, x => r = x);
                    results.Add(r);
                    WriteCsv(r);
                    if (phase == "all" || phase == "strips")
                        foreach (var cam in StripCams(s.Name))
                            yield return RunScenario(s, r, cam, null);
                }
                WriteSummary(results);
            }
            if (phase == "all" || phase == "kicks")
            {
                string ko = QbChoreoAudit.Arg("-pixQbChoreoKickStrips", "1");
                yield return KickAudit(ko == "1" && (phase == "all" || phase == "kicks"));
            }
            File.WriteAllText(_out + "/qb_report.txt", _report.ToString());
            Debug.Log("[QbChoreo] done");
            yield return null;
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------------------------------ scenarios
        static string[] StripCams(string name) { return name == "sg_drop" || name == "sg_quick" ? new[] { "Side", "Behind" } : new[] { "Side", "Behind" }; }

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
            int h = Mathf.Abs(off.Name.GetHashCode() + off.FormationName.GetHashCode());
            if (h % 3 == 0 && man.Count > 0) return man[h % man.Count];
            return zone[h % zone.Count];
        }

        List<Scn> Scenarios()
        {
            Func<Func<Play, bool>, Play> pick = pr => _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && pr(p));
            Func<Play, QbAssignment> qa = p => p.Assignments[0] as QbAssignment;
            var uc5 = pick(p => qa(p) != null && qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps == 5 && p.Category != PlayCategory.Kneel);
            var uc3 = pick(p => qa(p) != null && qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps == 3);
            var uc7 = pick(p => qa(p) != null && qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps >= 7);
            var sgD = pick(p => qa(p) != null && qa(p).Action == QbAction.DropbackPass && qa(p).Shotgun);
            var sgQ = pick(p => qa(p) != null && qa(p).Action == QbAction.QuickPass && qa(p).Shotgun) ?? sgD;
            var pa = pick(p => qa(p) != null && qa(p).Action == QbAction.PlayActionFake);
            var hoUc = pick(p => qa(p) != null && qa(p).Action == QbAction.Handoff && !qa(p).Shotgun);
            var roll = pick(p => qa(p) != null && qa(p).Action == QbAction.Rollout) ?? pick(p => qa(p) != null && qa(p).Action == QbAction.Boot);
            var boot = pick(p => qa(p) != null && qa(p).Action == QbAction.Boot);
            var hoSg = pick(p => qa(p) != null && qa(p).Action == QbAction.Handoff && qa(p).Shotgun);
            var list = new List<Scn>();
            Action<string, Play, int, bool, bool> add = (n, p, seed, scr, thr) => { if (p != null) list.Add(new Scn { Name = n, Off = p, Def = DefFor(p), Seed = seed, UserScramble = scr, UserThrow = thr }); else _report.AppendLine("scenario " + n + ": no matching play"); };
            add("uc5", uc5, 910, false, false);
            add("uc3", uc3, 911, false, false);
            add("uc7", uc7, 912, false, false);
            add("sg_drop", sgD, 913, false, false);
            add("sg_quick", sgQ, 914, false, false);
            add("playaction", pa, 915, false, false);
            add("handoff_uc", hoUc, 916, false, false);
            add("rollout", roll, 917, false, false);
            if (boot != null && boot != roll) add("boot", boot, 920, false, false);
            add("handoff_sg", hoSg, 921, false, false);
            add("uc5_scramble", uc5, 918, true, false);
            add("sg_scramble", sgD, 919, true, false);
            return list;
        }

        // ------------------------------------------------------------------------------------------ bones
        sealed class Bones
        {
            public Transform Hips, Chest, Head, LF, RF, LT, RT, LH, RH, LUA, RUA, LUL, RUL;
            public Vector3 PrevLT, PrevRT, PrevLF, PrevRF; public bool PlantL, PlantR, HavePrev;
            public Transform HeadT; public Vector3 HeadLocalFwd; public bool HeadOk;
        }
        readonly Dictionary<SimAgentView, Bones> _bones = new Dictionary<SimAgentView, Bones>();

        Bones GetBones(SimAgentView v)
        {
            Bones b;
            if (_bones.TryGetValue(v, out b) && b.Hips != null) return b;
            var an = v.Avatar.Animator; if (an == null || !an.isHuman) return null;
            b = new Bones();
            b.Hips = an.GetBoneTransform(HumanBodyBones.Hips); b.Chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.UpperChest); b.Head = an.GetBoneTransform(HumanBodyBones.Head);
            b.LF = an.GetBoneTransform(HumanBodyBones.LeftFoot); b.RF = an.GetBoneTransform(HumanBodyBones.RightFoot);
            b.LT = an.GetBoneTransform(HumanBodyBones.LeftToes); b.RT = an.GetBoneTransform(HumanBodyBones.RightToes);
            b.LH = an.GetBoneTransform(HumanBodyBones.LeftHand); b.RH = an.GetBoneTransform(HumanBodyBones.RightHand);
            b.LUA = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); b.RUA = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            b.LUL = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); b.RUL = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            // the head's face axis (private to the pose layer): read by reflection so QA does not touch the other track's file
            try
            {
                var sl = v.StanceLayer; var t = typeof(StanceIK);
                var f1 = t.GetField("_head", BindingFlags.NonPublic | BindingFlags.Instance); var f2 = t.GetField("_headLocalFwd", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sl != null && f1 != null && f2 != null) { b.HeadT = (Transform)f1.GetValue(sl); b.HeadLocalFwd = (Vector3)f2.GetValue(sl); b.HeadOk = b.HeadT != null; }
            }
            catch { }
            _bones[v] = b;
            return b;
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

        // ------------------------------------------------------------------------------------------ one scenario
        IEnumerator RunScenario(Scn s, Result dry, string cam, Action<Result> done)
        {
            var sim = NewSim(s.Off, s.Def, Scrim(35, 1, 10), s.Seed);
            var frame = new PlayFrame(true);
            _stage.NextBeginHuddle = s.Huddle;
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
            var res = new Result { S = s };
            var qv = _stage.Views[0];
            var tiles = new List<Texture2D>();
            // marks for strips (seconds relative to the snap) from the metrics pass; pre-snap tiles are spread over the pre-snap sequence
            List<float> post = null; List<float> pre = null;
            if (dry != null) { post = StripPost(dry); pre = StripPre(dry); }
            int preI = 0, postI = 0;
            float t = 0f; int fi = 0;
            // ---- pre-snap: the huddle break, the walk, the sequence, until the stage says the team is set (like the director: nobody snaps while the break is in progress)
            float ready = -1f;
            while (t < 16f)
            {
                sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; fi++;
                res.F.Add(Sample(sim, t, fi));
                if (cam != null && pre != null && preI < pre.Count && t >= pre[preI]) { tiles.Add(StripShot(frame, cam, sim)); preI++; }
                if (!_stage.BreakInProgress && t > 0.5f) { if (ready < 0f) { ready = t; res.ReadyT = t; res.ReadyFrame = fi; } if (t > ready + 0.35f) break; }
            }
            // ---- the cadence window of the director
            float cadLen = MatchDirector.CadenceSeconds(s.Seed);
            _stage.BeginCadence(cadLen);
            float ct = 0f;
            while (ct < cadLen)
            {
                sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; ct += Dt; fi++;
                res.F.Add(Sample(sim, t, fi));
                if (cam != null && pre != null && preI < pre.Count && t >= pre[preI]) { tiles.Add(StripShot(frame, cam, sim)); preI++; }
            }
            _stage.EndCadence();
            // ---- the snap
            sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt); yield return null; t += Dt; fi++;
            res.SnapT = t; res.SnapFrame = fi; res.F.Add(Sample(sim, t, fi));
            float after = 0f; float scrambleHold = 0f;
            while (sim.Phase != PlayPhase.Dead && after < 9f)
            {
                var ci = default(ControlInput); int user = -1;
                var q = sim.Agents[0];
                if (s.UserScramble)
                {
                    user = 0;
                    if (after > s.ScrambleAfter && q.HasBall && !sim.PassIsThrown) { scrambleHold += Dt; if (scrambleHold < 2.4f) ci.Move = new Vec2(0.75f, 0.45f).Norm() * 0.95f; }
                    else if (after > s.ScrambleAfter + 2.6f && q.HasBall && !sim.PassIsThrown && q.QbState == QbPhase.Scramble) ci.ThrowToSlot = 1;
                }
                sim.Tick(Dt, ci, user); _stage.Present(Dt); yield return null; t += Dt; after += Dt; fi++;
                var f = Sample(sim, t, fi); res.F.Add(f);
                if (res.CatchFrame < 0 && sim.Ball.Holder == 0 && sim.Ball.State == BallState.Held) { res.CatchFrame = fi; res.CatchT = t; }
                if (res.DropStartFrame < 0 && sim.Agents[0].QbState == QbPhase.Dropback) { res.DropStartFrame = fi; res.DropT = t; }
                if (res.SetFrame < 0 && sim.Agents[0].QbState == QbPhase.Set) { res.SetFrame = fi; res.SetT = t; }
                if (res.ExchangeFrame < 0 && sim.QbPlanIsHandoff && sim.Ball.State == BallState.Held && sim.Ball.Holder > 0) { res.ExchangeFrame = fi; }
                if (res.ThrowFrame < 0 && sim.PassIsThrown) { res.ThrowFrame = fi; res.ThrowT = t; }
                if (cam != null && post != null && postI < post.Count && t - res.SnapT >= post[postI]) { tiles.Add(StripShot(frame, cam, sim)); postI++; }
                if (cam != null && post != null && postI >= post.Count) break;
            }
            res.End = t;
            if (cam != null) Sheet(tiles, 4, _out + "/strips/" + s.Name + "_" + cam + ".png");
            _stage.End();
            yield return null;
            if (done != null) done(res);
        }

        List<float> StripPost(Result r)
        {
            // seconds relative to the snap
            float cs = r.CatchT > 0 ? r.CatchT - r.SnapT : 0.3f;
            var l = new List<float> { 0f, cs * 0.5f, cs, cs + 0.12f, cs + 0.3f, cs + 0.55f, cs + 0.85f };
            if (r.SetT > 0) { l.Add(r.SetT - r.SnapT - 0.25f); l.Add(r.SetT - r.SnapT + 0.05f); l.Add(r.SetT - r.SnapT + 0.5f); }
            if (r.ThrowT > 0) { l.Add(r.ThrowT - r.SnapT - 0.28f); l.Add(r.ThrowT - r.SnapT - 0.12f); l.Add(r.ThrowT - r.SnapT); l.Add(r.ThrowT - r.SnapT + 0.2f); }
            else l.AddRange(new[] { 1.4f, 1.8f, 2.2f, 2.6f, 3.0f });
            var o = l.Where(x => x >= 0f).Distinct().OrderBy(x => x).ToList();
            while (o.Count > 16) o.RemoveAt(o.Count / 2);
            return o;
        }

        List<float> StripPre(Result r)
        {
            // from the start of the pre-snap, spread over the walk / survey / step in / crouch / cadence (absolute seconds)
            float end = r.SnapT > 0 ? r.SnapT : 6f;
            var l = new List<float>();
            int n = 12; for (int i = 0; i < n; i++) l.Add(0.4f + (end - 0.4f) * i / (float)n);
            l.Add(end - 0.12f);
            return l;
        }

        Fr Sample(PlaySimulation sim, float t, int fi)
        {
            var a = sim.Agents[0]; var v = _stage.Views[0];
            var f = new Fr { T = t, SimT = sim.Time, Frame = fi, State = a.QbState, Act = a.Action, Speed = a.Speed * 0.9144f, Holder = sim.Ball.Holder, Ball = sim.Ball.State, SimPosX = a.Pos.X, SimPosY = a.Pos.Y, SlideL = -1f, SlideR = -1f };
            var b = GetBones(v); if (b == null) return f;
            var frame = _stage.Frame;
            Vector3 north = frame.DirToUnity(new Vec2(0f, 1f)); float northYaw = Mathf.Atan2(north.x, north.z) * Mathf.Rad2Deg;
            float gy = v.transform.position.y;
            Vector3 acrossH = b.RUL.position - b.LUL.position, acrossS = b.RUA.position - b.LUA.position;
            f.HipsYaw = Mathf.DeltaAngle(northYaw, BodyHeading.YawOfAcross(acrossH));
            f.ChestYaw = Mathf.DeltaAngle(northYaw, BodyHeading.YawOfAcross(acrossS));
            if (b.HeadOk) { Vector3 hf = b.HeadT.rotation * b.HeadLocalFwd; hf.y = 0f; if (hf.sqrMagnitude > 1e-4f) f.HeadYaw = Mathf.DeltaAngle(northYaw, Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg); }
            f.HipsY = b.Hips.position.y - gy;
            Vector3 ltP = b.LT != null ? b.LT.position : b.LF.position, rtP = b.RT != null ? b.RT.position : b.RF.position;
            float lth = ltP.y - gy, rth = rtP.y - gy; f.ToeL = lth; f.ToeR = rth;
            float lc = Mathf.Min(lth, b.LF.position.y - gy - 0.09f), rc = Mathf.Min(rth, b.RF.position.y - gy - 0.09f);
            bool plantL = b.PlantL ? lc < 0.07f : lc < 0.04f, plantR = b.PlantR ? rc < 0.07f : rc < 0.04f;
            f.PlantL = plantL; f.PlantR = plantR; f.ToePosL = ltP; f.ToePosR = rtP;
            if (b.HavePrev)
            {
                Vector3 dl = ltP - b.PrevLT; dl.y = 0f; Vector3 dr = rtP - b.PrevRT; dr.y = 0f; Vector3 dla = b.LF.position - b.PrevLF; dla.y = 0f; Vector3 dra = b.RF.position - b.PrevRF; dra.y = 0f;
                if (plantL && b.PlantL) f.SlideL = Mathf.Min(dl.magnitude, dla.magnitude) / Dt;
                if (plantR && b.PlantR) f.SlideR = Mathf.Min(dr.magnitude, dra.magnitude) / Dt;
            }
            b.PrevLT = ltP; b.PrevRT = rtP; b.PrevLF = b.LF.position; b.PrevRF = b.RF.position; b.PlantL = plantL; b.PlantR = plantR; b.HavePrev = true;
            f.BallD = -1f;
            if (_stage.Ball != null && (sim.Ball.Holder == 0 || sim.Ball.State == BallState.Snap)) { Vector3 bp = _stage.Ball.Position; f.BallD = Mathf.Min(Vector3.Distance(bp, b.LH.position), Vector3.Distance(bp, b.RH.position)); }
            // the head against the read he is on (the sim's look target)
            int tid = a.QbState == QbPhase.Fake ? sim.QbFakeTargetId : sim.QbLookTargetId;
            if (b.HeadOk && tid >= 0 && tid < 22 && (a.QbState == QbPhase.Set || a.QbState == QbPhase.Windup || a.QbState == QbPhase.Dropback))
            {
                Vector3 to = _stage.WorldPos(tid) - v.transform.position; to.y = 0f; Vector3 hf = b.HeadT.rotation * b.HeadLocalFwd; hf.y = 0f;
                if (to.sqrMagnitude > 0.1f && hf.sqrMagnitude > 1e-4f) f.LookErr = Vector3.Angle(to, hf);
            }
            f.AnkL = b.LF.position.y - gy; f.AnkR = b.RF.position.y - gy; f.HandRb = -1f; f.Eta = v.QbMeshEtaNow;
            { int rid = sim.QbPlanIsHandoff ? sim.QbHandoffTargetId : sim.QbFakeTargetId; if (rid > 0 && rid < 22) { var rv = _stage.Views[rid]; if (rv != null && rv.Avatar != null && rv.Avatar.Animator != null) { var rh = rv.Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips); if (rh != null) { f.HandRb = Vector3.Distance((b.LH.position + b.RH.position) * 0.5f, rh.position); f.RbDist = Vector3.Distance(v.transform.position, rv.transform.position); { Vector3 dq = rv.transform.position - v.transform.position; f.RbYaw = Mathf.Atan2(dq.x, dq.z) * Mathf.Rad2Deg; } f.ShoulderRb = Vector3.Distance((b.LUA.position + b.RUA.position) * 0.5f, rh.position); } } } }
            f.Steps = v.FootGait.Valid ? v.FootGait.Steps : -1;
            f.Seq = v.QbSeqName;
            f.Gliding = (f.SlideL > 0.30f) || (f.SlideR > 0.30f);
            return f;
        }

        // ------------------------------------------------------------------------------------------ output
        static string F(float x) { return x.ToString("0.000", CultureInfo.InvariantCulture); }

        void WriteCsv(Result r)
        {
            var sb = new StringBuilder("frame,t,simT,state,action,seq,speedMs,hipsYaw,chestYaw,headYaw,lookErr,ballD,slideL,slideR,plantL,plantR,toeL,toeR,hipsY,holder,ballState,gliding\n");
            foreach (var f in r.F)
                sb.Append(f.Frame).Append(',').Append(F(f.T)).Append(',').Append(F(f.SimT)).Append(',').Append(f.State).Append(',').Append(f.Act).Append(',').Append(f.Seq).Append(',').Append(F(f.Speed)).Append(',').Append(F(f.HipsYaw)).Append(',').Append(F(f.ChestYaw)).Append(',').Append(F(f.HeadYaw)).Append(',').Append(F(f.LookErr)).Append(',').Append(F(f.BallD)).Append(',').Append(F(f.SlideL)).Append(',').Append(F(f.SlideR)).Append(',').Append(f.PlantL ? 1 : 0).Append(',').Append(f.PlantR ? 1 : 0).Append(',').Append(F(f.ToeL)).Append(',').Append(F(f.ToeR)).Append(',').Append(F(f.HipsY)).Append(',').Append(f.Holder).Append(',').Append(f.Ball).Append(',').Append(f.Gliding ? 1 : 0).Append(',').Append(f.Steps).Append(',').Append(F(f.AnkL)).Append(',').Append(F(f.AnkR)).Append(',').Append(F(f.HandRb)).Append(',').Append(F(f.ToePosL.x)).Append(',').Append(F(f.ToePosL.z)).Append(',').Append(F(f.ToePosR.x)).Append(',').Append(F(f.ToePosR.z)).Append(',').Append(F(f.Eta)).Append(',').Append(F(f.RbDist)).Append(',').Append(F(f.ShoulderRb)).Append(',').Append(F(f.RbYaw)).AppendLine();
            File.WriteAllText(_out + "/qb_frames_" + r.S.Name + ".csv", sb.ToString());
        }

        /// <summary>Slides (m/s) of every foot contact of 6 frames or more inside the frames chosen by <paramref name="pick"/> (a contact is judged on its core: the first and last 2 frames of each plant are dropped, the foot is the toe bone).</summary>
        static List<float> ContactSlides(List<Fr> fr, Func<Fr, bool> pick, List<string> log = null)
        {
            var res = new List<float>();
            for (int foot = 0; foot < 2; foot++)
            {
                int i = 0;
                while (i < fr.Count)
                {
                    bool pl = (foot == 0 ? fr[i].ToeL : fr[i].ToeR) < 0.03f && (foot == 0 ? fr[i].AnkL : fr[i].AnkR) < 0.145f;
                    if (!pl) { i++; continue; }
                    int j = i; while (j + 1 < fr.Count && (foot == 0 ? fr[j + 1].ToeL : fr[j + 1].ToeR) < 0.03f && (foot == 0 ? fr[j + 1].AnkL : fr[j + 1].AnkR) < 0.145f) j++;
                    if (j - i + 1 >= 6)
                    {
                        int a = i + 2, b = j - 2;
                        bool all = true; for (int k = a; k <= b; k++) if (!pick(fr[k])) { all = false; break; }
                        if (all && b > a)
                        {
                            Vector3 pa = foot == 0 ? fr[a].ToePosL : fr[a].ToePosR, pb = foot == 0 ? fr[b].ToePosL : fr[b].ToePosR; pa.y = 0f; pb.y = 0f;
                            res.Add((pb - pa).magnitude / ((b - a) * Dt)); if (log != null) log.Add((foot == 0 ? "L" : "R") + " frames " + fr[a].Frame + "-" + fr[b].Frame + " (" + fr[a].Seq + ") slide " + ((pb - pa).magnitude / ((b - a) * Dt)).ToString("0.000") + " m/s, toe moved " + (pb - pa).magnitude.ToString("0.000") + " m hipsYaw " + fr[a].HipsYaw.ToString("0") + "->" + fr[b].HipsYaw.ToString("0"));
                        }
                    }
                    i = j + 1;
                }
            }
            return res;
        }

        static string Stat(IEnumerable<float> xs)
        {
            var l = xs.OrderBy(x => x).ToList(); if (l.Count == 0) return "n=0";
            Func<double, float> q = p => l[Math.Min(l.Count - 1, (int)(p * l.Count))];
            return string.Format(CultureInfo.InvariantCulture, "n={0} mean {1:F3} p50 {2:F3} p95 {3:F3} max {4:F3}", l.Count, l.Average(), q(.5), q(.95), l[l.Count - 1]);
        }

        void WriteSummary(List<Result> rs)
        {
            var sb = new StringBuilder("# QB choreography audit (60 Hz, real PlayStage, huddle break on)\n\n");
            foreach (var r in rs)
            {
                var F0 = r.F;
                sb.AppendLine("## " + r.S.Name + ": " + r.S.Off.Name + " vs " + r.S.Def.Name);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "pre-snap ready at {0:F2} s (frame {1}), snap at {2:F2} s, catch {3:F3} s after the snap, drop starts {4:F2}, set {5:F2}, throw {6:F2}, end {7:F2}", r.ReadyT, r.ReadyFrame, r.SnapT, r.CatchT > 0 ? r.CatchT - r.SnapT : -1f, r.DropT > 0 ? r.DropT - r.SnapT : -1f, r.SetT > 0 ? r.SetT - r.SnapT : -1f, r.ThrowT > 0 ? r.ThrowT - r.SnapT : -1f, r.End - r.SnapT));
                // snap -> hands: ball to the nearest hand at the catch frame and the first frame the hand is within 12 cm of the ball
                if (r.CatchFrame > 0)
                {
                    var c = F0.FirstOrDefault(x => x.Frame == r.CatchFrame);
                    var holdFr = F0.Where(x => x.Frame >= r.CatchFrame + 12 && x.Frame <= r.CatchFrame + 30 && x.BallD >= 0f).Select(x => x.BallD).OrderBy(x => x).ToList(); float hold = holdFr.Count > 0 ? holdFr[holdFr.Count / 2] : 0.10f; float thr = c.BallD <= 0.05f ? 0.05f : hold + 0.035f;
                    int handFrame = -1; foreach (var x in F0) if (x.Frame >= r.SnapFrame && x.BallD >= 0f && x.BallD <= thr) { handFrame = x.Frame; break; }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "SNAP->HANDS: ball to nearest hand at the catch frame {0:F3} m; first frame within {4:F3} m of the hands (the steady hold is {5:F3} m): {1} (catch frame {2}) => error {3} frames", c.BallD, handFrame, r.CatchFrame, handFrame < 0 ? "never" : (handFrame - r.CatchFrame).ToString(), thr, hold));
                }
                var drop = F0.Where(x => x.State == QbPhase.Dropback && x.T > r.SnapT).ToList();
                var set = F0.Where(x => x.State == QbPhase.Set && x.T > r.SnapT).ToList();
                var pre = F0.Where(x => x.T <= r.SnapT).ToList();
                sb.AppendLine("DROP frames " + drop.Count + ": chest yaw vs north abs deg " + Stat(drop.Select(x => Mathf.Abs(x.ChestYaw))) + "; hips yaw abs " + Stat(drop.Select(x => Mathf.Abs(x.HipsYaw))));
                sb.AppendLine("DROP planted-foot slide m/s (frames with a plant) " + Stat(drop.Where(x => x.SlideL >= 0).Select(x => x.SlideL).Concat(drop.Where(x => x.SlideR >= 0).Select(x => x.SlideR))) + "; both toes >10cm up: " + drop.Count(x => x.ToeL > 0.10f && x.ToeR > 0.10f) + " frames");
                { int s0 = drop.Count > 0 ? drop.First().Steps : -1, s1 = set.Count > 0 ? set.First().Steps : (drop.Count > 0 ? drop.Last().Steps : -1); sb.AppendLine("DROP footfalls (planner landings from the first drop frame to the set) " + (s0 >= 0 && s1 >= 0 ? (s1 - s0).ToString() : "n/a") + " for a planned " + (r.S.Off.Assignments[0] is QbAssignment ? ((QbAssignment)r.S.Off.Assignments[0]).DropbackSteps : 0) + "-step drop"); }
                { var clog = new List<string>(); var cs = ContactSlides(F0, x => x.T > r.SnapT && (x.State == QbPhase.Dropback || x.State == QbPhase.Set), clog); foreach (var cl in clog) sb.AppendLine("   contact " + cl); sb.AppendLine("CONTACT SLIDE m/s, drop and pocket (planted-foot contacts of 6+ frames, core frames): " + Stat(cs) + "; contacts above 0.10 m/s: " + cs.Count(v0 => v0 > 0.10f)); }
                sb.AppendLine("DROP ball to hand m " + Stat(drop.Where(x => x.BallD >= 0).Select(x => x.BallD)));
                sb.AppendLine("POCKET (Set) frames " + set.Count + ": chest yaw vs north abs deg " + Stat(set.Select(x => Mathf.Abs(x.ChestYaw))) + "; head yaw abs " + Stat(set.Select(x => Mathf.Abs(x.HeadYaw))) + "; head vs current read deg " + Stat(set.Where(x => x.LookErr > 0).Select(x => x.LookErr)));
                sb.AppendLine("POCKET ball to hand m " + Stat(set.Where(x => x.BallD >= 0).Select(x => x.BallD)) + "; slide " + Stat(set.Where(x => x.SlideL >= 0).Select(x => x.SlideL).Concat(set.Where(x => x.SlideR >= 0).Select(x => x.SlideR))));
                var still = set.Where(x => x.Speed < 0.35f).ToList(); if (still.Count > 8) { var bl = still.Select(x => x.AnkL).ToList(); var br = still.Select(x => x.AnkR).ToList(); int cyc = 0; for (int q2 = 1; q2 < still.Count; q2++) if (still[q2].AnkL > still[q2 - 1].AnkL + 0.0f && still[q2 - 1].AnkL <= bl.Min() + 0.012f && still[q2].AnkL > bl.Min() + 0.012f) cyc++; sb.AppendLine("POCKET live feet: heel (ankle) lift peak-to-peak L " + ((bl.Max() - bl.Min()) * 100f).ToString("0.0") + " cm, R " + ((br.Max() - br.Min()) * 100f).ToString("0.0") + " cm, left-foot lift cycles " + cyc + " in " + (still.Count * Dt).ToString("0.00") + " s"); }
                if (r.ExchangeFrame > 0) { var ex = F0.FirstOrDefault(x => x.Frame == r.ExchangeFrame); var win = F0.Where(x => Math.Abs(x.Frame - r.ExchangeFrame) <= 6 && x.HandRb >= 0).ToList(); var after = F0.Where(x => x.Frame > r.ExchangeFrame + 24 && x.Frame < r.ExchangeFrame + 42).ToList(); sb.AppendLine("HAND-OFF: QB hands to the runner's belly at the exchange frame " + ex.HandRb.ToString("0.000") + " m, closest within +-6 frames " + (win.Count > 0 ? win.Min(x => x.HandRb) : -1f).ToString("0.000") + " m at frame offset " + (win.Count > 0 ? (win.OrderBy(x => x.HandRb).First().Frame - r.ExchangeFrame) : 0) + "; head yaw vs north 0.4-0.7 s after " + Stat(after.Select(x => Mathf.Abs(x.HeadYaw))) + "; segments " + string.Join(">", F0.Where(x => x.Frame >= r.ExchangeFrame - 30 && x.Frame <= r.ExchangeFrame + 50).Select(x => x.Seq).Distinct().ToArray())); }
                { var clog2 = new List<string>(); var cs2 = ContactSlides(F0, x => x.T > r.SnapT && x.State == QbPhase.Scramble, clog2); foreach (var cl2 in clog2) sb.AppendLine("   scramble contact " + cl2); if (cs2.Count > 0) sb.AppendLine("SCRAMBLE / ROLL contact slide m/s (6+ frame plants, core frames): " + Stat(cs2) + "; contacts above 0.10 m/s: " + cs2.Count(v1 => v1 > 0.10f)); }
                var all = F0.ToList();
                int glide = all.Count(x => x.Gliding), moving = all.Count(x => x.SlideL >= 0 || x.SlideR >= 0);
                sb.AppendLine("GLIDE: frames with a planted foot sliding >0.30 m/s: " + glide + " of " + all.Count + " (planted-foot frames " + moving + ")");
                // sim speed while the body shows a standing pose
                int standGlide = all.Count(x => x.Speed > 0.8f && (x.Seq == "Survey" || x.Seq == "Crouch" || x.Seq == "Under" || x.Seq == "Secure" || x.Seq == "Cadence"));
                sb.AppendLine("sim speed >0.8 m/s while a standing/crouch sequence segment shows: " + standGlide + " frames");
                var segs = new List<string>(); string last = null; int n = 0; float t0 = 0f;
                foreach (var x in F0) { string k = x.Seq + "|" + x.State; if (k != last) { if (last != null) segs.Add(last + "(" + n + "f," + (n * Dt).ToString("0.00") + "s)"); last = k; n = 0; t0 = x.T; } n++; }
                if (last != null) segs.Add(last + "(" + n + "f)");
                sb.AppendLine("SEGMENTS: " + string.Join(" > ", segs.ToArray()));
                sb.AppendLine("PRE-SNAP: speed during walk " + Stat(pre.Where(x => x.Speed > 0.3f).Select(x => x.Speed)) + "; planted slide " + Stat(pre.Where(x => x.SlideL >= 0).Select(x => x.SlideL).Concat(pre.Where(x => x.SlideR >= 0).Select(x => x.SlideR))));
                sb.AppendLine();
            }
            File.WriteAllText(_out + "/qb_summary.txt", sb.ToString());
        }

        // ------------------------------------------------------------------------------------------ kicks
        IEnumerator KickAudit(bool strips)
        {
            var kicks = new List<KeyValuePair<string, KeyValuePair<Play, Play>>>();
            Func<string, Play> spf = n => _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == n);
            kicks.Add(new KeyValuePair<string, KeyValuePair<Play, Play>>("kickoff", new KeyValuePair<Play, Play>(spf("Kickoff Deep Middle"), spf("Kick Return Middle Wedge"))));
            kicks.Add(new KeyValuePair<string, KeyValuePair<Play, Play>>("fieldgoal", new KeyValuePair<Play, Play>(spf("Field Goal"), spf("Field Goal Block Middle"))));
            var punt = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Special && p.Category == PlayCategory.Punt); var pret = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Special && p.Category == PlayCategory.PuntReturn);
            if (punt != null && pret != null) kicks.Add(new KeyValuePair<string, KeyValuePair<Play, Play>>("punt", new KeyValuePair<Play, Play>(punt, pret)));
            var sbk = new StringBuilder("# kick desync report (60 Hz). ball launch = the first frame the DISPLAYED ball leaves its spot (tee / hands / drop) after the sim kick; foot contact = frame of the minimum kicking-foot to ball distance\n\n");
            foreach (var kv in kicks)
            {
                for (int seed = 77; seed < 80; seed++)
                {
                    GameState gs = kv.Key == "kickoff" ? new GameState(_rules, Team.Away) : (kv.Key == "fieldgoal" ? Scrim(80, 4, 5) : Scrim(30, 4, 8));
                    var sim = NewSim(kv.Value.Key, kv.Value.Value, gs, seed);
                    var frame = new PlayFrame(true);
                    _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                    int kicker = 0; for (int i = 0; i < 22; i++) if (sim.Agents[i].PlayPos == Position.K || (kv.Key == "punt" && sim.Agents[i].PlayPos == Position.P)) { kicker = i; break; }
                    for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                    var kvw = _stage.Views[kicker]; var b = GetBones(kvw);
                    var tiles = new List<Texture2D>();
                    if (strips && seed == 77) tiles.Add(StripShotAt(frame, kicker, "Side", sim));
                    sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt); yield return null;
                    var rows = new List<float[]>();   // t, ballX,ballY,ballZ, footDist R (min of ankle / toe), footDist L, sim kick flag, rfx,rfy,rfz, ball state
                    float t = 0f; int simKickFrame = -1, fi = 0; Vector3 restBall = _stage.Ball.Position; Vector3 prevR = b.RF.position;
                    int launchFrame = -1; float peakSpeed = 0f; int peakFrame = -1;
                    var clipSeries = new List<string>(); var clipTSeries = new List<float>(); var rootSeries = new List<Vector3>(); var yawSeries = new List<float>(); var kwSeries = new List<float>(); var ballSeries = new List<Vector3>(); var rSeries = new List<Vector3>(); var lSeries = new List<Vector3>(); var rtSeries = new List<Vector3>(); var ltSeries = new List<Vector3>();
                    float next = 0f;
                    while (sim.Phase != PlayPhase.Dead && t < 7f)
                    {
                        sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; fi++;
                        rootSeries.Add(kvw.transform.position); yawSeries.Add(kvw.Yaw); kwSeries.Add(kvw.KickIkWeight); clipTSeries.Add(kvw.Rig != null ? kvw.Rig.ClipTime : 0f); clipSeries.Add(kvw.Rig != null ? kvw.Rig.CurrentClipName + "|" + sim.Agents[kicker].Action : ""); ballSeries.Add(_stage.Ball.Position); rSeries.Add(b.RF.position); lSeries.Add(b.LF.position); rtSeries.Add(b.RT != null ? b.RT.position : b.RF.position); ltSeries.Add(b.LT != null ? b.LT.position : b.LF.position);
                        if (simKickFrame < 0 && sim.Log.Has(SimEventType.Kick)) simKickFrame = fi;
                        if (strips && seed == 77 && tiles.Count < 12 && t >= next) { tiles.Add(StripShotAt(frame, kicker, "Side", sim)); next = t + (simKickFrame >= 0 ? 0.10f : 0.32f); }
                        if (simKickFrame > 0 && fi > simKickFrame + 40) break;
                    }
                    if (strips && seed == 77) Sheet(tiles, 4, _out + "/strips/kick_" + kv.Key + "_Side.png");
                    if (seed == 77)
                    {
                        var kc = new StringBuilder("frame,simKick,ballX,ballY,ballZ,rfX,rfY,rfZ,rtX,rtY,rtZ,lfX,lfY,lfZ,rfSpeed,rtSpeed,clipT,clip,action\n");
                        for (int i = 0; i < ballSeries.Count; i++)
                        {
                            float rs = i > 0 ? (rSeries[i] - rSeries[i - 1]).magnitude / Dt : 0f, rts = i > 0 ? (rtSeries[i] - rtSeries[i - 1]).magnitude / Dt : 0f;
                            kc.Append(i + 1).Append(',').Append(i + 1 == simKickFrame ? 1 : 0).Append(',').Append(F(ballSeries[i].x)).Append(',').Append(F(ballSeries[i].y)).Append(',').Append(F(ballSeries[i].z)).Append(',').Append(F(rSeries[i].x)).Append(',').Append(F(rSeries[i].y)).Append(',').Append(F(rSeries[i].z)).Append(',').Append(F(rtSeries[i].x)).Append(',').Append(F(rtSeries[i].y)).Append(',').Append(F(rtSeries[i].z)).Append(',').Append(F(lSeries[i].x)).Append(',').Append(F(lSeries[i].y)).Append(',').Append(F(lSeries[i].z)).Append(',').Append(F(rs)).Append(',').Append(F(rts)).Append(',').Append(clipTSeries[i].ToString("0.000", CultureInfo.InvariantCulture)).Append(',').Append(F(rootSeries[i].x)).Append(',').Append(F(rootSeries[i].z)).Append(',').Append(F(yawSeries[i])).Append(',').Append(F(kwSeries[i])).Append(',').Append(clipSeries[i]).AppendLine();
                        }
                        File.WriteAllText(_out + "/kick_frames_" + kv.Key + ".csv", kc.ToString());
                    }
                    // analysis: displayed ball launch = the first frame near the sim kick frame where the ball moves faster than 0.2 m per frame (12 m/s: a kick, not a drop or a placement)
                    int center = simKickFrame > 0 ? simKickFrame : Math.Min(ballSeries.Count - 1, 60);
                    for (int i = Math.Max(1, center - 10); i < Math.Min(ballSeries.Count, center + 20); i++) { if ((ballSeries[i] - ballSeries[i - 1]).magnitude > 0.20f) { launchFrame = i + 1; break; } }
                    int contactFrame = -1; float minD = 99f;
                    if (launchFrame > 0) for (int i = Math.Max(0, launchFrame - 14); i <= Math.Min(ballSeries.Count - 1, launchFrame + 1); i++) { Vector3 ip = Vector3.Lerp(rSeries[i], rtSeries[i], 0.55f), ipPrev = i > 0 ? Vector3.Lerp(rSeries[i - 1], rtSeries[i - 1], 0.55f) : ip; float d = SegDist(ipPrev, ip, ballSeries[i]) - 0.145f; if (d < minD) { minD = d; contactFrame = i + 1; } }
                    for (int i = 1; i < rSeries.Count; i++) { float spd = (rSeries[i] - rSeries[i - 1]).magnitude / Dt; if (launchFrame > 0 && i + 1 >= launchFrame - 20 && i + 1 <= launchFrame + 6 && spd > peakSpeed) { peakSpeed = spd; peakFrame = i + 1; } }
                    int lf0 = launchFrame > 1 ? launchFrame - 2 : 0;
                    float distAtLaunch = minD;
                    sbk.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} seed {1}: sim kick event frame {2}; displayed ball launch frame {3}; swept instep-to-ball gap (the foot path between frames to the ball centre - 0.145 m: ball radius + foot) {4:F3} m at frame {5}; right-foot peak speed {6:F1} m/s at frame {7}; => launch minus contact {8} frames, launch minus foot-speed peak {9} frames; same gap (best frame pair of the contact window) {10:F3} m", kv.Key, seed, simKickFrame, launchFrame, minD, contactFrame, peakSpeed, peakFrame, launchFrame > 0 && contactFrame > 0 ? (launchFrame - contactFrame).ToString() : "n/a", launchFrame > 0 && peakFrame > 0 ? (launchFrame - peakFrame).ToString() : "n/a", distAtLaunch));
                    _stage.End(); yield return null;
                }
            }
            File.WriteAllText(_out + "/kick_report.txt", sbk.ToString());
            _report.AppendLine(sbk.ToString());
        }

        static float SegDist(Vector3 a, Vector3 b, Vector3 p) { Vector3 ab = b - a; float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude); return (a + ab * t - p).magnitude; }

        // ------------------------------------------------------------------------------------------ strips
        Texture2D StripShot(PlayFrame frame, string cam, PlaySimulation sim) { return StripShotAt(frame, 0, cam, sim); }

        Texture2D StripShotAt(PlayFrame frame, int agent, string cam, PlaySimulation sim)
        {
            var v = _stage.Views[agent]; Vector3 q = v.transform.position;
            Vector3 down = frame.DirToUnity(new Vec2(0f, 1f)); Vector3 dr = Vector3.Cross(Vector3.up, down);
            bool kick = sim.Kind != PlayKind.Scrimmage;
            switch (cam)
            {
                case "Side": _cam.transform.position = q + dr * 3.6f + Vector3.up * 1.4f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 1.0f + down * (kick ? 1.2f : 0.2f) - _cam.transform.position); _cam.fieldOfView = kick ? 46f : 40f; break;
                case "Behind": _cam.transform.position = q - down * 4.4f + dr * 0.5f + Vector3.up * 2.0f; _cam.transform.rotation = Quaternion.LookRotation(q + down * 2f + Vector3.up * 0.9f - _cam.transform.position); _cam.fieldOfView = 42f; break;
                default: _cam.transform.position = q + Vector3.up * 9f - down * 0.5f; _cam.transform.rotation = Quaternion.LookRotation(q - _cam.transform.position, down); _cam.fieldOfView = 40f; break;
            }
            var hidden = new List<Renderer>();
            for (int j = 0; j < 22; j++) { var ov = _stage.Views[j]; if (ov == null || ov == v) continue; foreach (var rr in ov.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); } }
            if (QbChoreoAudit.Arg("-pixQbChoreoShowAll", "0") == "1") foreach (var rr in hidden) rr.enabled = true;
            var shot = ShotCam(540, 400);
            foreach (var rr in hidden) rr.enabled = true;
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
    }
}
