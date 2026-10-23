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
    /// v0.10.0 BODIES: pose sanity probe and line / ball film strips (QA only, behind <c>-pixBodyProbe</c>). Runs real plays through PlayStage and measures every body each frame in humanoid muscle space:
    /// spine / chest twist (pelvis-to-chest yaw), neck and head pitch / yaw, forearm and upper-arm twist (candy-wrapper risk), elbow and knee hyperextension, shoulder rotation, plus the share of frames outside the joint limits;
    /// the skinned jersey / pants / arms edge-length ratio (volume loss and stretch) at waist, shoulders, wrists and knees; the ball against the carrying forearm; film strips of the OL / DL line pre-snap and at the snap.
    /// Run: Unity -projectPath ... -executeMethod PixFootball.EditorTools.BodyProbe.Run -pixBodyProbe  (batch mode WITHOUT -nographics).
    /// Args: -pixBodyProbeOut dir, -pixBodyProbePhase lines|plays|ball|all, -pixBodyProbePlays n, -pixBodyProbeTag name.
    /// </summary>
    public static class BodyProbe
    {
        public const string Flag = "-pixBodyProbe";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(45);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(45);
            new GameObject("PixBodyProbe").AddComponent<BodyProbeRunner>();
        }
    }

    /// <summary>Humanoid muscle readout in degrees (default limits) for one animator.</summary>
    public sealed class MuscleReader
    {
        readonly HumanPoseHandler _h; HumanPose _pose;
        static string[] _names; static float[] _min, _max;
        static readonly Dictionary<string, int> _idx = new Dictionary<string, int>();
        public readonly bool Ok;

        static void Init()
        {
            if (_names != null) return;
            _names = HumanTrait.MuscleName;
            _min = new float[_names.Length]; _max = new float[_names.Length];
            for (int i = 0; i < _names.Length; i++) { _min[i] = HumanTrait.GetMuscleDefaultMin(i); _max[i] = HumanTrait.GetMuscleDefaultMax(i); _idx[_names[i]] = i; }
        }

        public MuscleReader(Animator an)
        {
            Init();
            if (an == null || !an.isHuman || an.avatar == null) return;
            _h = new HumanPoseHandler(an.avatar, an.transform); Ok = true;
        }

        public void Read() { if (Ok) _h.GetHumanPose(ref _pose); }

        public static int Index(string name) { Init(); int i; return _idx.TryGetValue(name, out i) ? i : -1; }
        public static string Name(int i) { Init(); return _names[i]; }
        public static int Count { get { Init(); return _names.Length; } }
        public float Raw(int i) { return i >= 0 && Ok ? _pose.muscles[i] : 0f; }
        public float Deg(int i)
        {
            if (i < 0 || !Ok) return 0f;
            float v = _pose.muscles[i];
            return v >= 0f ? v * _max[i] : -v * _min[i];
        }
        public float Deg(string name) { return Deg(Index(name)); }
    }

    public sealed class BodyProbeRunner : MonoBehaviour
    {
        string _out, _tag;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _report = new StringBuilder();
        readonly Dictionary<string, List<double>> _acc = new Dictionary<string, List<double>>();
        readonly Dictionary<string, int> _hist = new Dictionary<string, int>();
        int _playIndex;
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }
        const float Dt = 1f / 60f;

        // muscle ids (resolved once)
        int mSpineFB, mSpineLR, mSpineTw, mChestFB, mChestLR, mChestTw, mUChestFB, mUChestLR, mUChestTw, mNeckNod, mNeckTilt, mNeckTurn, mHeadNod, mHeadTilt, mHeadTurn;
        int[] mShFB = new int[2], mShUD = new int[2], mArmUD = new int[2], mArmFB = new int[2], mArmTw = new int[2], mForeStretch = new int[2], mForeTw = new int[2], mHandUD = new int[2], mHandIO = new int[2];
        int[] mULFB = new int[2], mULIO = new int[2], mULTw = new int[2], mLLStretch = new int[2], mLLTw = new int[2], mFootUD = new int[2];

        sealed class Rec
        {
            public Animator An; public MuscleReader M; public Transform Hips, Spine, Chest, UChest, Neck, Head, LH, RH, LUA, RUA, LLA, RLA, LUL, RUL, LLL, RLL, LF, RF;
            public SkinProbe Skin;
        }
        readonly Dictionary<SimAgentView, Rec> _recs = new Dictionary<SimAgentView, Rec>();

        void Add(string k, double v) { List<double> l; if (!_acc.TryGetValue(k, out l)) _acc[k] = l = new List<double>(); l.Add(v); }
        void Hist(string k) { int n; _hist.TryGetValue(k, out n); _hist[k] = n + 1; }
        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string Grp(Position p)
        {
            if (p == Position.C) return "C";
            if (p == Position.LT || p == Position.RT) return "OT";
            if (p == Position.LG || p == Position.RG) return "OG";
            if (PositionGroup.IsDefensiveLine(p)) return "DL";
            if (PositionGroup.IsLinebacker(p)) return "LB";
            if (p == Position.CB) return "CB";
            if (p == Position.FS || p == Position.SS) return "S";
            return p.ToString();
        }

        void ResolveMuscles()
        {
            Func<string, int> I = MuscleReader.Index;
            mSpineFB = I("Spine Front-Back"); mSpineLR = I("Spine Left-Right"); mSpineTw = I("Spine Twist Left-Right");
            mChestFB = I("Chest Front-Back"); mChestLR = I("Chest Left-Right"); mChestTw = I("Chest Twist Left-Right");
            mUChestFB = I("UpperChest Front-Back"); mUChestLR = I("UpperChest Left-Right"); mUChestTw = I("UpperChest Twist Left-Right");
            mNeckNod = I("Neck Nod Down-Up"); mNeckTilt = I("Neck Tilt Left-Right"); mNeckTurn = I("Neck Turn Left-Right");
            mHeadNod = I("Head Nod Down-Up"); mHeadTilt = I("Head Tilt Left-Right"); mHeadTurn = I("Head Turn Left-Right");
            string[] s = { "Left ", "Right " };
            for (int k = 0; k < 2; k++)
            {
                mShFB[k] = I(s[k] + "Shoulder Front-Back"); mShUD[k] = I(s[k] + "Shoulder Down-Up");
                mArmUD[k] = I(s[k] + "Arm Down-Up"); mArmFB[k] = I(s[k] + "Arm Front-Back"); mArmTw[k] = I(s[k] + "Arm Twist In-Out");
                mForeStretch[k] = I(s[k] + "Forearm Stretch"); mForeTw[k] = I(s[k] + "Forearm Twist In-Out");
                mHandUD[k] = I(s[k] + "Hand Down-Up"); mHandIO[k] = I(s[k] + "Hand In-Out");
                mULFB[k] = I(s[k] + "Upper Leg Front-Back"); mULIO[k] = I(s[k] + "Upper Leg In-Out"); mULTw[k] = I(s[k] + "Upper Leg Twist In-Out");
                mLLStretch[k] = I(s[k] + "Lower Leg Stretch"); mLLTw[k] = I(s[k] + "Lower Leg Twist In-Out"); mFootUD[k] = I(s[k] + "Foot Up-Down");
            }
        }

        Rec GetRec(SimAgentView v)
        {
            Rec r;
            if (_recs.TryGetValue(v, out r) && r.Hips != null) return r;
            var an = v.Avatar != null ? v.Avatar.Animator : null; if (an == null || !an.isHuman) return null;
            r = new Rec { An = an, M = new MuscleReader(an) };
            r.Hips = an.GetBoneTransform(HumanBodyBones.Hips); r.Spine = an.GetBoneTransform(HumanBodyBones.Spine); r.Chest = an.GetBoneTransform(HumanBodyBones.Chest);
            r.UChest = an.GetBoneTransform(HumanBodyBones.UpperChest); r.Neck = an.GetBoneTransform(HumanBodyBones.Neck); r.Head = an.GetBoneTransform(HumanBodyBones.Head);
            r.LH = an.GetBoneTransform(HumanBodyBones.LeftHand); r.RH = an.GetBoneTransform(HumanBodyBones.RightHand);
            r.LUA = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); r.RUA = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            r.LLA = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); r.RLA = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            r.LUL = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); r.RUL = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            r.LLL = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg); r.RLL = an.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            r.LF = an.GetBoneTransform(HumanBodyBones.LeftFoot); r.RF = an.GetBoneTransform(HumanBodyBones.RightFoot);
            _recs[v] = r;
            return r;
        }

        // ------------------------------------------------------------------------------------------ pose readout
        struct PoseNow
        {
            public float SpineTwist, SpineBend, NeckNod, NeckTurn, HeadNod, HeadTurn, NeckPitchTot, NeckYawTot;
            public float ForeTwL, ForeTwR, ArmTwL, ArmTwR, ArmTwAbs, ForeTwAbs, ElbowL, ElbowR, KneeL, KneeR, ShL, ShR, ArmL, ArmR, HandL, HandR, ULTwAbs;
            public float OutOfLimits;
            public float FootLow, FootHigh, UpArm, ForeArm; public float GSpine, GHeadYaw, GHeadPitch, GForeRoll, GHandRoll, GUpArmRoll, GThighRoll, GElbowMin, GKneeMin, FixAny; public float[] Fix;
        }

        PoseNow Measure(Rec r, SimAgentView v = null)
        {
            r.M.Read();
            var m = r.M; var p = new PoseNow();
            float st = m.Deg(mSpineTw), ct = m.Deg(mChestTw), ut = m.Deg(mUChestTw);
            p.SpineTwist = Mathf.Abs(st + ct + ut);       // signed sum: opposite twists in the chain cancel in the real pelvis-to-chest yaw
            p.SpineBend = m.Deg(mSpineFB) + m.Deg(mChestFB) + m.Deg(mUChestFB);
            p.NeckNod = m.Deg(mNeckNod); p.HeadNod = m.Deg(mHeadNod); p.NeckTurn = m.Deg(mNeckTurn); p.HeadTurn = m.Deg(mHeadTurn);
            p.NeckPitchTot = p.NeckNod + p.HeadNod; p.NeckYawTot = p.NeckTurn + p.HeadTurn;
            p.ForeTwL = m.Deg(mForeTw[0]); p.ForeTwR = m.Deg(mForeTw[1]); p.ArmTwL = m.Deg(mArmTw[0]); p.ArmTwR = m.Deg(mArmTw[1]);
            p.ForeTwAbs = Mathf.Max(Mathf.Abs(p.ForeTwL), Mathf.Abs(p.ForeTwR)); p.ArmTwAbs = Mathf.Max(Mathf.Abs(p.ArmTwL), Mathf.Abs(p.ArmTwR));
            p.ElbowL = m.Deg(mForeStretch[0]); p.ElbowR = m.Deg(mForeStretch[1]); p.KneeL = m.Deg(mLLStretch[0]); p.KneeR = m.Deg(mLLStretch[1]);
            p.ShL = m.Deg(mShUD[0]); p.ShR = m.Deg(mShUD[1]); p.ArmL = m.Deg(mArmFB[0]); p.ArmR = m.Deg(mArmFB[1]);
            p.HandL = m.Deg(mHandUD[0]); p.HandR = m.Deg(mHandUD[1]);
            p.ULTwAbs = Mathf.Max(Mathf.Abs(m.Deg(mULTw[0])), Mathf.Abs(m.Deg(mULTw[1])));
            int over = 0;
            for (int i = 0; i < MuscleReader.Count; i++) if (Mathf.Abs(m.Raw(i)) > 1.0f) over++;
            p.OutOfLimits = over;
            if (v != null && r.LF != null && r.RF != null) { float gy0 = v.transform.position.y; p.FootLow = Mathf.Min(r.LF.position.y, r.RF.position.y) - gy0; p.FootHigh = Mathf.Max(r.LF.position.y, r.RF.position.y) - gy0; p.UpArm = Vector3.Distance(r.LUA.position, r.LLA.position); p.ForeArm = Vector3.Distance(r.LLA.position, r.LH.position); }
            var sn = v != null ? v.Sanity : null;
            if (sn != null && sn.Ok)
            {
                p.GSpine = sn.SpineTwistNow(); float hy, hp; sn.HeadAnglesNow(out hy, out hp); p.GHeadYaw = hy; p.GHeadPitch = hp;
                p.GForeRoll = Mathf.Max(Mathf.Abs(sn.ForearmRoll(0)), Mathf.Abs(sn.ForearmRoll(1))); p.GHandRoll = Mathf.Max(Mathf.Abs(sn.HandRoll(0)), Mathf.Abs(sn.HandRoll(1)));
                p.GUpArmRoll = Mathf.Max(Mathf.Abs(sn.UpperArmRoll(0)), Mathf.Abs(sn.UpperArmRoll(1))); p.GThighRoll = Mathf.Max(Mathf.Abs(sn.ThighRoll(0)), Mathf.Abs(sn.ThighRoll(1)));
                p.GElbowMin = Mathf.Min(sn.ElbowAngle(0), sn.ElbowAngle(1)); p.GKneeMin = Mathf.Min(sn.KneeAngle(0), sn.KneeAngle(1));
                p.Fix = new[] { sn.FixSpine, sn.FixHead, sn.FixForearm, sn.FixHand, sn.FixElbow, sn.FixKnee, sn.FixThigh, sn.FixUpperArm }; p.FixAny = Mathf.Max(p.Fix);
            }
            return p;
        }

        void Record(string key, PoseNow p, bool detail = true)
        {
            Add("spine+chest twist deg | " + key, p.SpineTwist);
            Add("neck+head pitch (nod) deg | " + key, p.NeckPitchTot);
            Add("neck+head yaw deg | " + key, Mathf.Abs(p.NeckYawTot));
            Add("forearm twist |deg| | " + key, p.ForeTwAbs);
            Add("upper-arm twist |deg| | " + key, p.ArmTwAbs);
            Add("upper-leg twist |deg| | " + key, p.ULTwAbs);
            Add("elbow stretch deg (min of L,R) | " + key, Mathf.Min(p.ElbowL, p.ElbowR));
            Add("elbow stretch deg (max of L,R) | " + key, Mathf.Max(p.ElbowL, p.ElbowR));
            Add("knee stretch deg (min of L,R) | " + key, Mathf.Min(p.KneeL, p.KneeR));
            Add("knee stretch deg (max of L,R) | " + key, Mathf.Max(p.KneeL, p.KneeR));
            Add("shoulder up-down |deg| max | " + key, Mathf.Max(Mathf.Abs(p.ShL), Mathf.Abs(p.ShR)));
            Add("muscles beyond joint limit (count) | " + key, p.OutOfLimits);
            Add("ankle height above turf, lower foot m | " + key, p.FootLow);
            Add("upper arm length m (left) | " + key, p.UpArm); Add("forearm length m (left) | " + key, p.ForeArm);
            Add("GEOM spine twist deg (signed, 3 bones) | " + key, p.GSpine);
            Add("GEOM head yaw vs chest deg | " + key, p.GHeadYaw);
            Add("GEOM head pitch vs chest deg (+up) | " + key, p.GHeadPitch);
            Add("GEOM forearm roll |deg| | " + key, p.GForeRoll);
            Add("GEOM hand roll on forearm |deg| | " + key, p.GHandRoll);
            Add("GEOM upper-arm roll |deg| | " + key, p.GUpArmRoll);
            Add("GEOM thigh roll |deg| | " + key, p.GThighRoll);
            Add("GEOM elbow hinge deg (min of L,R; negative = backwards) | " + key, p.GElbowMin);
            Add("GEOM knee hinge deg (min of L,R; negative = backwards) | " + key, p.GKneeMin);
            if (p.FixAny > 0.01f) { Hist("FRAMES sanity layer corrected something | " + key); Add("sanity correction size deg | " + key, p.FixAny); string[] fn = { "spine", "head", "forearm", "hand", "elbow", "knee", "thigh", "upper-arm" }; for (int fi = 0; fi < 8; fi++) if (p.Fix[fi] > 0.01f) { Add("sanity fix " + fn[fi] + " deg | " + key, p.Fix[fi]); } }
            if (p.SpineTwist > 35f) Hist("FRAMES spine twist >35 | " + key);
            if (p.ForeTwAbs > 90f) Hist("FRAMES forearm twist >90 | " + key);
            if (Mathf.Abs(p.NeckYawTot) > 70f) Hist("FRAMES neck yaw >70 | " + key);
            if (p.NeckPitchTot > 55f || p.NeckPitchTot < -55f) Hist("FRAMES neck pitch beyond 55 | " + key);
            Hist("FRAMES | " + key);
        }

        // ------------------------------------------------------------------------------------------ setup
        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = BodyProbe.Arg("-pixBodyProbeOut", "D:/PIXFootball/Renders/bodies_v0100");
            _tag = BodyProbe.Arg("-pixBodyProbeTag", "run");
            Directory.CreateDirectory(_out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) Debug.Log("[BodyProbe] logged " + t + ": " + c + " :: " + (st.Length > 300 ? st.Substring(0, 300) : st)); };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("BodyProbeCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("BodyProbeStage").AddComponent<PlayStage>();
            _stage.Init();
            ResolveMuscles();
            if (BodyProbe.Arg("-pixBodySanity", "1") == "0") BodySanity.Enabled = false;
            if (BodyProbe.Arg("-pixOldIk", "0") == "1") { LimbIK.AlignHinges = false; LimbIK.HandKeepWorld = 1f; }
            yield return null;
            string phase = BodyProbe.Arg("-pixBodyProbePhase", "all");
            int nPlays = int.Parse(BodyProbe.Arg("-pixBodyProbePlays", "20"));
            if (phase == "all" || phase == "lines") yield return Lines();
            if (phase == "all" || phase == "plays") yield return Plays(nPlays);
            if (phase == "all" || phase == "ball") yield return BallScenes();
            WriteSummary();
            _report.AppendLine("skin probe diag: " + SkinProbe.Diag);
            File.WriteAllText(_out + "/" + _tag + "_report.txt", _report.ToString());
            Debug.Log("[BodyProbe] done");
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

        // ------------------------------------------------------------------------------------------ lines: pre-snap and snap strips
        IEnumerator Lines()
        {
            var csv = new StringBuilder("scene,moment,agent,pos,stance,hipsY,shoulderY,headY,handLowY,trunkFromVertDeg,neckExtDeg,headFacePitchDeg,spineTwist,neckPitchTot,neckYawTot,foreTwAbs,elbowMin,kneeMin,stanceLeanSolved,muscleOver,clip,rigW,action,kneeRawL,kneeRawR\n");
            var scenes = new List<KeyValuePair<string, Play>>();
            var run = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.InsideRun);
            Func<Play, QbAssignment> qa = p => p.Assignments != null && p.Assignments.Length > 0 ? p.Assignments[0] as QbAssignment : null;
            var pass = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && qa(p) != null && qa(p).Shotgun && (qa(p).Action == QbAction.QuickPass || qa(p).Action == QbAction.DropbackPass));
            var pass2 = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && qa(p) != null && !qa(p).Shotgun && qa(p).Action == QbAction.DropbackPass);
            if (run != null) scenes.Add(new KeyValuePair<string, Play>("run", run));
            if (pass2 != null) scenes.Add(new KeyValuePair<string, Play>("passUC", pass2));
            if (pass != null) scenes.Add(new KeyValuePair<string, Play>("passSG", pass));
            foreach (var sc in scenes)
            {
                var off = sc.Value; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(35, 1, 10), 321);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                for (int f = 0; f < 110; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                if (_playIndex == 1) { var sbn = new StringBuilder("BONES: "); DumpBones(_stage.Views[2].Avatar.Animator.transform, 0, sbn); _report.AppendLine(sbn.ToString()); }
                _report.AppendLine("scene " + sc.Key + ": " + off.Name + " [" + off.FormationName + "] vs " + def.Name + " [" + def.FormationName + "]");
                yield return LineShots(sim, frame, sc.Key, "pre", csv);
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                int lgIdx = -1; for (int i = 0; i < 22; i++) if (sim.Agents[i].PlayPos == Position.LG) { lgIdx = i; break; }
                var seq = new List<Texture2D>(); var seqCsv = new StringBuilder("frame,clip,rigW,stanceW,stanceActive,action,armAbductL,armAbductR,wristYL,wristYR\n");
                for (int f = 0; f < 20; f++)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null;
                    if (sc.Key == "run" && lgIdx >= 0)
                    {
                        var vv = _stage.Views[lgIdx]; var rr = GetRec(vv);
                        if (rr != null)
                        {
                            seqCsv.AppendLine(string.Join(",", new[] { f.ToString(), vv.Rig != null ? vv.Rig.CurrentClipName : "", vv.Rig != null ? F(vv.Rig.Weight) : "", vv.StanceLayer != null ? F(vv.StanceLayer.Weight) : "", vv.StanceLayer != null ? vv.StanceLayer.Active.ToString() : "", sim.Agents[lgIdx].Action.ToString(), F(Vector3.Angle(Vector3.down, rr.LLA.position - rr.LUA.position)), F(Vector3.Angle(Vector3.down, rr.RLA.position - rr.RUA.position)), F(rr.LH.position.y - vv.transform.position.y), F(rr.RH.position.y - vv.transform.position.y) }));
                            if (f % 2 == 0)
                            {
                                var hid = new List<Renderer>(); HideOthers(new[] { lgIdx }, hid);
                                Vector3 q0 = vv.transform.position; Vector3 r0 = Vector3.Cross(Vector3.up, vv.Forward);
                                PlaceCam(q0 + vv.Forward * 3.0f - r0 * 0.9f + Vector3.up * 1.3f, q0 + Vector3.up * 1.05f, 34f);
                                seq.Add(ShotCam(240, 300)); foreach (var rr2 in hid) rr2.enabled = true;
                            }
                        }
                    }
                }
                if (seq.Count > 0) { Sheet(seq, 5, _out + "/" + _tag + "_snapseq_LG.png"); File.WriteAllText(_out + "/" + _tag + "_snapseq_LG.csv", seqCsv.ToString()); }
                yield return LineShots(sim, frame, sc.Key, "snap20f", csv);
                for (int f = 0; f < 24; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                yield return LineShots(sim, frame, sc.Key, "snap44f", csv);
                _stage.End();
                yield return null;
            }
            File.WriteAllText(_out + "/" + _tag + "_lines.csv", csv.ToString());
        }

        static void DumpBones(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 14) return;
            if (t.GetComponent<Renderer>() == null) sb.Append(new string(' ', 0)).Append(depth).Append(':').Append(t.name).Append(' ');
            for (int i = 0; i < t.childCount; i++) DumpBones(t.GetChild(i), depth + 1, sb);
        }

        void HideOthers(IEnumerable<int> keep, List<Renderer> hidden)
        {
            var k = new HashSet<int>(keep);
            for (int j = 0; j < 22; j++)
            {
                if (k.Contains(j)) continue;
                var ov = _stage.Views[j]; if (ov == null) continue;
                foreach (var rr in ov.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); }
            }
        }

        IEnumerator LineShots(PlaySimulation sim, PlayFrame frame, string scene, string moment, StringBuilder csv)
        {
            var ol = new List<int>(); var dl = new List<int>();
            for (int i = 0; i < 22; i++)
            {
                var p = sim.Agents[i].PlayPos;
                if (PositionGroup.IsOffensiveLine(p)) ol.Add(i); else if (PositionGroup.IsDefensiveLine(p)) dl.Add(i);
            }
            Vector3 down = frame.DirToUnity(new Vec2(0f, 1f)); Vector3 dr = Vector3.Cross(Vector3.up, down);
            ol.Sort((a, b) => Vector3.Dot(_stage.Views[a].transform.position, dr).CompareTo(Vector3.Dot(_stage.Views[b].transform.position, dr)));
            dl.Sort((a, b) => Vector3.Dot(_stage.Views[a].transform.position, dr).CompareTo(Vector3.Dot(_stage.Views[b].transform.position, dr)));
            // metrics
            foreach (var i in ol.Concat(dl))
            {
                var v = _stage.Views[i]; var r = GetRec(v); if (r == null) continue;
                var a = sim.Agents[i]; float gy = v.transform.position.y;
                var pn = Measure(r, v);
                Vector3 hips = r.Hips.position, sh = (r.RUA.position + r.LUA.position) * 0.5f, hd = r.Head.position;
                Vector3 trunk = (r.Neck != null ? r.Neck.position : r.Chest.position) - hips;
                float fromVert = Vector3.Angle(Vector3.up, trunk);
                float lowHand = Mathf.Min(r.LH.position.y, r.RH.position.y) - gy;
                float neckExt = 0f, facePitch = 0f;
                if (v.StanceLayer != null && v.StanceLayer.Ok) { neckExt = v.StanceLayer.NeckExtensionDeg(); facePitch = v.StanceLayer.HeadFacePitchDeg(); }
                csv.AppendLine(string.Join(",", new[] { scene, moment, i.ToString(), a.PlayPos.ToString(), v.Stance.Kind.ToString(), F(hips.y - gy), F(sh.y - gy), F(hd.y - gy), F(lowHand), F(fromVert), F(neckExt), F(facePitch), F(pn.SpineTwist), F(pn.NeckPitchTot), F(pn.NeckYawTot), F(pn.ForeTwAbs), F(Mathf.Min(pn.ElbowL, pn.ElbowR)), F(Mathf.Min(pn.KneeL, pn.KneeR)), F(v.StanceLayer != null ? v.StanceLayer.LastLean : 0f), F(pn.OutOfLimits), v.Rig != null ? v.Rig.CurrentClipName : "", v.Rig != null ? F(v.Rig.Weight) : "", a.Action.ToString(), v.Sanity != null ? F(v.Sanity.KneeRaw[0]) : "", v.Sanity != null ? F(v.Sanity.KneeRaw[1]) : "" }));
                Record((moment == "pre" ? "stance " : "snap ") + a.PlayPos, pn);
            }
            // sheets
            Vector3 olc = ol.Count > 0 ? ol.Select(i => _stage.Views[i].transform.position).Aggregate((x, y) => x + y) / ol.Count : _stage.Views[0].transform.position;
            Vector3 dlc = dl.Count > 0 ? dl.Select(i => _stage.Views[i].transform.position).Aggregate((x, y) => x + y) / dl.Count : olc + down;
            var tiles = new List<Texture2D>();
            var hidden = new List<Renderer>();
            // OL from the defence side (defenders hidden), DL from the offence side (offence hidden), OL from behind with everyone, whole line from a high 3/4
            HideOthers(ol, hidden); PlaceCam(olc + down * 7.0f + Vector3.up * 1.5f, olc + Vector3.up * 0.8f, 46f); yield return null; tiles.Add(ShotCam(720, 400)); foreach (var rr in hidden) rr.enabled = true; hidden.Clear();
            HideOthers(dl, hidden); PlaceCam(dlc - down * 7.5f + Vector3.up * 1.5f, dlc + Vector3.up * 0.8f, 46f); yield return null; tiles.Add(ShotCam(720, 400)); foreach (var rr in hidden) rr.enabled = true; hidden.Clear();
            PlaceCam(olc - down * 6.5f + Vector3.up * 2.6f, olc + down * 1.0f + Vector3.up * 0.7f, 52f); yield return null; tiles.Add(ShotCam(720, 400));
            PlaceCam(olc + dr * 9.0f - down * 1.5f + Vector3.up * 3.2f, olc + down * 0.5f + Vector3.up * 0.6f, 40f); yield return null; tiles.Add(ShotCam(720, 400));
            Sheet(tiles, 2, _out + "/" + _tag + "_" + scene + "_" + moment + "_line.png");
            // profile tiles per lineman (others hidden)
            var prof = new List<Texture2D>(); var index = new StringBuilder();
            foreach (var i in ol.Concat(dl))
            {
                var v = _stage.Views[i]; Vector3 q = v.transform.position;
                HideOthers(new[] { i }, hidden);
                PlaceCam(q + dr * 4.6f + Vector3.up * 1.0f, q + Vector3.up * 0.95f, 34f); yield return null; prof.Add(ShotCam(300, 330));
                foreach (var rr in hidden) rr.enabled = true; hidden.Clear();
                index.Append(sim.Agents[i].PlayPos + " ");
            }
            Sheet(prof, Mathf.Max(4, (prof.Count + 1) / 2), _out + "/" + _tag + "_" + scene + "_" + moment + "_profiles.png");
            _report.AppendLine("  profiles(" + moment + ") order: " + index);
            // DL close-ups: front 3/4 and profile, tight
            if (dl.Count > 1)
            {
                var close = new List<Texture2D>();
                int mid = dl[dl.Count / 2];
                var v = _stage.Views[mid]; Vector3 q = v.transform.position; Vector3 fwd = v.Forward; Vector3 right = Vector3.Cross(Vector3.up, fwd);
                HideOthers(new[] { mid }, hidden);
                PlaceCam(q + fwd * 2.6f - right * 1.4f + Vector3.up * 1.2f, q + Vector3.up * 0.8f, 32f); yield return null; close.Add(ShotCam(420, 420));
                PlaceCam(q + right * 2.8f + Vector3.up * 1.0f, q + Vector3.up * 0.75f, 30f); yield return null; close.Add(ShotCam(420, 420));
                PlaceCam(q - fwd * 2.4f + right * 1.0f + Vector3.up * 1.5f, q + Vector3.up * 0.8f, 32f); yield return null; close.Add(ShotCam(420, 420));
                foreach (var rr in hidden) rr.enabled = true; hidden.Clear();
                Sheet(close, 3, _out + "/" + _tag + "_" + scene + "_" + moment + "_dlclose.png");
            }
            if (ol.Count > 2)
            {
                var cl = new List<Texture2D>(); int ci = ol[ol.Count / 2];
                var cv = _stage.Views[ci]; Vector3 cq = cv.transform.position; Vector3 cf = cv.Forward; Vector3 cr = Vector3.Cross(Vector3.up, cf);
                HideOthers(new[] { ci }, hidden);
                PlaceCam(cq + cf * 3.0f - cr * 1.6f + Vector3.up * 1.3f, cq + Vector3.up * 0.6f, 32f); yield return null; cl.Add(ShotCam(420, 420));
                PlaceCam(cq + cr * 3.0f + Vector3.up * 0.8f, cq + Vector3.up * 0.6f, 30f); yield return null; cl.Add(ShotCam(420, 420));
                PlaceCam(cq - cf * 2.8f + cr * 1.6f + Vector3.up * 1.3f, cq + Vector3.up * 0.6f, 32f); yield return null; cl.Add(ShotCam(420, 420));
                foreach (var rr in hidden) rr.enabled = true; hidden.Clear();
                Sheet(cl, 3, _out + "/" + _tag + "_" + scene + "_" + moment + "_cclose.png");
            }
        }

        void PlaceCam(Vector3 pos, Vector3 look, float fov) { _cam.transform.position = pos; _cam.transform.rotation = Quaternion.LookRotation(look - pos); _cam.fieldOfView = fov; }

        // ------------------------------------------------------------------------------------------ plays: pose sanity
        IEnumerator Plays(int n)
        {
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary").ToList();
            var rnd = new System.Random(777);
            var chosen = new List<Play>();
            foreach (var cat in offense.Select(p => p.Category).Distinct()) { var l = offense.Where(p => p.Category == cat).ToList(); chosen.Add(l[rnd.Next(l.Count)]); }
            while (chosen.Count < n) chosen.Add(offense[rnd.Next(offense.Count)]);
            if (chosen.Count > n) chosen = chosen.Take(n).ToList();
            var skinSample = new List<int>();
            for (int pi = 0; pi < chosen.Count; pi++)
            {
                var off = chosen[pi]; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(35, 1, 10), 1000 + pi);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                int fr = 0;
                for (int f = 0; f < 100; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; if (f % 20 == 99 % 20) SamplePlay(sim, frame, fr++, true, pi); }
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                float t = 0f;
                while (sim.Phase != PlayPhase.Dead && t < 12f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; yield return null;
                    SamplePlay(sim, frame, fr++, false, pi);
                }
                for (int f = 0; f < 150 && sim.Phase == PlayPhase.Dead; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; SamplePlay(sim, frame, fr++, false, pi); }
                _report.AppendLine("play " + pi + " " + off.Name + " vs " + def.Name + " ended " + sim.EndReason + " at " + t.ToString("0.0"));
                _stage.End();
                yield return null;
            }
        }

        void SamplePlay(PlaySimulation sim, PlayFrame frame, int fr, bool preSnap, int play)
        {
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i]; if (v == null || v.Avatar == null) continue;
                var r = GetRec(v); if (r == null) continue;
                var pn = Measure(r, v);
                string g = Grp(a.PlayPos);
                string act = a.Action.ToString();
                if (v.ContactDriven) act += "[contact]";
                if (v.Ragdoll != null && v.Ragdoll.Active) act += "[ragdoll]";
                Record(g + " " + act, pn);
                Record("ALL " + (preSnap ? "Stance" : act.Replace("[contact]", "").Replace("[ragdoll]", "")), pn);
                // skinned mesh deformation, every 6th frame for a handful of bodies
                if (fr % 6 == 0 && (i == 0 || i == 1 || i == 2 || i == 3 || i == 5 || i == 12 || i == 13 || i == 14 || i == 16))
                {
                    if (r.Skin == null) r.Skin = new SkinProbe(v);
                    r.Skin.Sample();
                }
            }
        }

        // ------------------------------------------------------------------------------------------ skin deformation
        public sealed class SkinProbe
        {
            public sealed class Part
            {
                public SkinnedMeshRenderer Smr; public Mesh Baked; public int[] A, B; public byte[] Region; public float[] Sum, Min, Max; public int N; public string Name;
            }
            public readonly List<Part> Parts = new List<Part>();
            /// <summary>A ring of vertices around a bone axis (forearm near the wrist, forearm middle, the waist): its mean distance to the axis over the run says whether the limb pinches (a candy wrapper) or swells.</summary>
            public sealed class Ring { public Part P; public int[] V; public Transform A, B; public string Name; public List<float> R = new List<float>(); }
            public readonly List<Ring> Rings = new List<Ring>();
            public static readonly string[] Regions = { "waist", "shoulder", "forearm-wrist", "knee-thigh", "neck", "other" };
            public static string Diag = "";

            static int RegionOf(string bone)
            {
                string b = bone.ToLowerInvariant();
                if (b.Contains("forearm") || b.Contains("hand")) return 2;
                if (b.Contains("shoulder") || b.EndsWith("arm")) return 1;
                if (b.Contains("upleg") || b.EndsWith("leg")) return 3;
                if (b.Contains("neck") || b.Contains("head")) return 4;
                if (b.Contains("hips") || b.Contains("spine")) return 0;
                return 5;
            }

            public SkinProbe(SimAgentView v)
            {
                var photo = v.Avatar != null ? v.Avatar.Photo : null; if (photo == null) return;
                foreach (var p in photo.parts)
                {
                    if (p == null || p.lods == null || p.lods.Length == 0) continue;
                    if (p.key != "Jersey" && p.key != "Pants" && p.key != "Arms" && p.key != "Pads") continue;
                    var smr = p.lods[0] as SkinnedMeshRenderer; if (smr == null || smr.sharedMesh == null) continue;
                    try
                    {
                        var baked = new Mesh(); smr.BakeMesh(baked, true);
                        var tris = baked.triangles; var bw = smr.sharedMesh.boneWeights; var bones = smr.bones;
                        if (tris.Length == 0 || bw.Length != baked.vertexCount) { Diag += p.key + ": baked tris " + tris.Length + " verts " + baked.vertexCount + " weights " + bw.Length + "; "; continue; }
                        var seen = new HashSet<long>(); var A = new List<int>(); var B = new List<int>();
                        for (int t = 0; t + 2 < tris.Length; t += 3)
                            for (int e = 0; e < 3; e++)
                            {
                                int a = tris[t + e], b = tris[t + (e + 1) % 3]; if (a > b) { int x = a; a = b; b = x; }
                                long key = ((long)a << 32) | (uint)b;
                                if (seen.Add(key)) { A.Add(a); B.Add(b); }
                            }
                        var pt = new Part { Smr = smr, Baked = baked, A = A.ToArray(), B = B.ToArray(), Name = p.key };
                        int ne = pt.A.Length; pt.Region = new byte[ne]; pt.Sum = new float[ne]; pt.Min = new float[ne]; pt.Max = new float[ne];
                        var vreg = new byte[baked.vertexCount];
                        for (int i = 0; i < vreg.Length; i++)
                        {
                            var w = bw[i]; int bi = w.boneIndex0; float best = w.weight0;
                            if (w.weight1 > best) { best = w.weight1; bi = w.boneIndex1; }
                            vreg[i] = (byte)(bi >= 0 && bi < bones.Length && bones[bi] != null ? RegionOf(bones[bi].name) : 5);
                        }
                        for (int i = 0; i < ne; i++) { pt.Region[i] = vreg[pt.A[i]]; pt.Min[i] = 1e9f; }
                        Parts.Add(pt);
                        // rings: near the wrist and mid forearm of each arm (Arms part), the waist (Jersey part: vertices that follow the lower spine bone)
                        var all = new Dictionary<string, Transform>(); foreach (var bt in bones) if (bt != null) all[bt.name] = bt;
                        var bv = baked.vertices;
                        Func<int, string> dom = vi => { var w = bw[vi]; int bi = w.boneIndex0; float best = w.weight0; if (w.weight1 > best) { best = w.weight1; bi = w.boneIndex1; } return best >= 0.5f && bi >= 0 && bi < bones.Length && bones[bi] != null ? bones[bi].name : ""; };
                        if (p.key == "Arms")
                            foreach (string sd in new[] { "Left", "Right" })
                            {
                                Transform fa, ha; if (!all.TryGetValue("rig:" + sd + "ForeArm", out fa) || !all.TryGetValue("rig:" + sd + "Hand", out ha)) continue;
                                Vector3 e = smr.transform.InverseTransformPoint(fa.position), wr = smr.transform.InverseTransformPoint(ha.position); Vector3 ax = wr - e; float L = ax.magnitude; if (L < 0.05f) continue; ax /= L;
                                var wl = new List<int>(); var ml = new List<int>();
                                for (int vi = 0; vi < bv.Length; vi++)
                                {
                                    if (dom(vi) != "rig:" + sd + "ForeArm") continue;
                                    float tt = Vector3.Dot(bv[vi] - e, ax) / L;
                                    if (tt >= 0.78f && tt <= 1.0f) wl.Add(vi); else if (tt >= 0.30f && tt <= 0.55f) ml.Add(vi);
                                }
                                if (wl.Count > 20 && ml.Count > 20) { Rings.Add(new Ring { P = pt, V = wl.ToArray(), A = fa, B = ha, Name = "wrist" }); Rings.Add(new Ring { P = pt, V = ml.ToArray(), A = fa, B = ha, Name = "forearm mid" }); }
                            }
                        if (p.key == "Jersey")
                        {
                            Transform hp, sp, s1; if (all.TryGetValue("rig:Hips", out hp) && all.TryGetValue("rig:Spine", out sp) && all.TryGetValue("rig:Spine1", out s1))
                            {
                                var wl = new List<int>(); for (int vi = 0; vi < bv.Length; vi++) if (dom(vi) == "rig:Spine") wl.Add(vi);
                                if (wl.Count > 30) Rings.Add(new Ring { P = pt, V = wl.ToArray(), A = hp, B = s1, Name = "waist" });
                            }
                        }
                    }
                    catch (Exception ex) { Diag += p.key + ": " + ex.Message + "; "; }
                }
            }

            public void Sample()
            {
                foreach (var rg in Rings)
                {
                    var pt = rg.P; if (pt.Smr == null || rg.A == null || rg.B == null) continue;
                    pt.Smr.BakeMesh(pt.Baked, true); var vs2 = pt.Baked.vertices; if (vs2.Length == 0) continue;
                    Vector3 a = pt.Smr.transform.InverseTransformPoint(rg.A.position), b = pt.Smr.transform.InverseTransformPoint(rg.B.position); Vector3 ax = (b - a); float L = ax.magnitude; if (L < 1e-4f) continue; ax /= L;
                    float sum = 0f; foreach (int vi in rg.V) { Vector3 d = vs2[vi] - a; d -= Vector3.Dot(d, ax) * ax; sum += d.magnitude; }
                    rg.R.Add(sum / rg.V.Length);
                }
                foreach (var pt in Parts)
                {
                    if (pt.Smr == null) continue;
                    pt.Smr.BakeMesh(pt.Baked, true);
                    var vs = pt.Baked.vertices; if (vs.Length == 0) continue;
                    int n = pt.A.Length;
                    for (int i = 0; i < n; i++)
                    {
                        float l = (vs[pt.A[i]] - vs[pt.B[i]]).magnitude;
                        pt.Sum[i] += l; if (l < pt.Min[i]) pt.Min[i] = l; if (l > pt.Max[i]) pt.Max[i] = l;
                    }
                    pt.N++;
                }
            }
        }

        /// <summary>Wrist pinch (mean distance of the wrist ring to the forearm axis over the same of the forearm middle) and waist radius, each against its own median over the run: below 0.7 the skin pinches (candy wrapper), above 1.3 it swells.</summary>
        void RingSummary(StringBuilder sb)
        {
            var wrist = new List<float>(); var waist = new List<float>(); var mid = new List<float>();
            foreach (var r in _recs.Values)
            {
                if (r.Skin == null) continue;
                var rs = r.Skin.Rings;
                for (int i = 0; i < rs.Count; i++)
                {
                    var g = rs[i];
                    if (g.Name == "wrist" && i + 1 < rs.Count && rs[i + 1].Name == "forearm mid" && g.R.Count >= 10 && rs[i + 1].R.Count == g.R.Count)
                    {
                        var ratio = new List<float>(); for (int k = 0; k < g.R.Count; k++) ratio.Add(g.R[k] / Mathf.Max(1e-4f, rs[i + 1].R[k]));
                        var s2 = ratio.OrderBy(x => x).ToList(); float med = s2[s2.Count / 2]; foreach (var x in ratio) wrist.Add(x / med);
                        var m2 = rs[i + 1].R.OrderBy(x => x).ToList(); float mm = m2[m2.Count / 2]; foreach (var x in rs[i + 1].R) mid.Add(x / mm);
                    }
                    else if (g.Name == "waist" && g.R.Count >= 10) { var s2 = g.R.OrderBy(x => x).ToList(); float med = s2[s2.Count / 2]; foreach (var x in g.R) waist.Add(x / med); }
                }
            }
            Action<string, List<float>> line = (name, l) =>
            {
                if (l.Count == 0) return; l.Sort(); Func<double, float> q = p => l[Math.Min(l.Count - 1, (int)(p * l.Count))];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-44} n={1,7} min {2:0.00} p01 {3:0.00} p05 {4:0.00} p50 {5:0.00} p95 {6:0.00} p99 {7:0.00} max {8:0.00}", name, l.Count, l[0], q(.01), q(.05), q(.5), q(.95), q(.99), l[l.Count - 1]));
            };
            sb.AppendLine("\n## ring volume (1.00 = the body's own median)");
            line("wrist radius over forearm-mid radius", wrist); line("forearm mid radius", mid); line("waist radius (lower spine)", waist);
        }

        /// <summary>Per part and region: how much the edges of the skinned mesh shrink and stretch over the whole run (min and max length against the mean of the same edge); a candy-wrapped wrist or a collapsed waist shows as a small min ratio.</summary>
        void SkinSummary(StringBuilder sb)
        {
            var minR = new Dictionary<string, List<float>>(); var maxR = new Dictionary<string, List<float>>();
            foreach (var r in _recs.Values)
            {
                if (r.Skin == null) continue;
                foreach (var pt in r.Skin.Parts)
                {
                    if (pt.N < 10) continue;
                    for (int i = 0; i < pt.A.Length; i++)
                    {
                        float mean = pt.Sum[i] / pt.N; if (mean < 0.004f) continue;
                        string k = pt.Name + " " + SkinProbe.Regions[pt.Region[i]];
                        List<float> l1, l2; if (!minR.TryGetValue(k, out l1)) { minR[k] = l1 = new List<float>(); maxR[k] = l2 = new List<float>(); } else l2 = maxR[k];
                        l1.Add(pt.Min[i] / mean); l2.Add(pt.Max[i] / mean);
                    }
                }
            }
            sb.AppendLine("\n## skinned mesh edge change over the run (edge length min / mean and max / mean; 1.00 = no change)");
            foreach (var k in minR.Keys.OrderBy(x => x))
            {
                var a = minR[k]; var b = maxR[k]; a.Sort(); b.Sort();
                Func<List<float>, double, float> q = (l, p) => l[Math.Min(l.Count - 1, (int)(p * l.Count))];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-28} edges {1,7}  shrink: worst {2:0.00} p01 {3:0.00} p05 {4:0.00} p50 {5:0.00}  stretch: p50 {6:0.00} p95 {7:0.00} p99 {8:0.00} worst {9:0.00}", k, a.Count, a[0], q(a, .01), q(a, .05), q(a, .5), q(b, .5), q(b, .95), q(b, .99), b[b.Count - 1]));
            }
        }


        // ------------------------------------------------------------------------------------------ ball scenes: celebrate, walk back
        MatchOfficials _crew;
        IEnumerator BallScenes()
        {
            try { _crew = MatchOfficials.Create(transform, null, null); _stage.CrewOverride = _crew; } catch (Exception ex) { _report.AppendLine("officials unavailable: " + ex.Message); }
            // a touchdown run from the 4 yard line, the carrier is followed through the celebration and the walk off
            var runs = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && (p.Category == PlayCategory.InsideRun || p.Category == PlayCategory.OutsideRun || p.Category == PlayCategory.ShortPass)).ToList();
            int found = 0;
            for (int seed = 0; seed < 40 && found < 2; seed++)
            {
                var off = runs[seed % runs.Count]; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(96, 1, 4), 2000 + seed);
                var frame = new PlayFrame(true);
                var dry = NewSim(off, def, Scrim(96, 1, 4), 2000 + seed);
                for (int f = 0; f < 90; f++) dry.Tick(Dt, default(ControlInput), -1);
                dry.Tick(Dt, new ControlInput { Snap = true }, -1);
                int g = 0; while (dry.Phase != PlayPhase.Dead && g++ < 1500) dry.Tick(Dt, default(ControlInput), -1);
                if (dry.EndReason != EndReason.Touchdown) continue;
                found++;
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep(); if (_crew != null) _crew.BeginPlay(frame, sim);
                for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                float t = 0f; var tiles = new List<Texture2D>(); float nextShot = 0f; int carrier = -1; float tdAt = -1f; float whistleAt = -1f;
                var bsb = new StringBuilder("t,phase,holder,action,mode,ballToHandR,ballToHandL,ballToForearmAxis,ballFromChest,swingDegPerS\n");
                Vector3 lastRel = Vector3.zero; bool haveRel = false;
                while (t < 14f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; if (_crew != null) _crew.Tick(Dt, sim, sim.Phase == PlayPhase.Live); yield return null;
                    int h = sim.Ball.Holder; if (h >= 0) carrier = h;
                    if (sim.EndReason == EndReason.Touchdown && tdAt < 0f) tdAt = t;
                    if (sim.Phase == PlayPhase.Dead && whistleAt < 0f) { whistleAt = t; if (_crew != null) _crew.Signal(OfficialSignal.DeadBall); }
                    if (carrier >= 0 && _stage.Ball != null && sim.Ball.Holder == carrier)
                    {
                        var v = _stage.Views[carrier]; var r = GetRec(v); if (r == null) continue;
                        Vector3 bp = _stage.Ball.Position;
                        float dR = Vector3.Distance(bp, r.RH.position), dL = Vector3.Distance(bp, r.LH.position);
                        // distance to the nearer forearm axis
                        float axis = Mathf.Min(SegDist(bp, r.RLA.position, r.RH.position), SegDist(bp, r.LLA.position, r.LH.position));
                        Vector3 chest = r.Chest.position;
                        bsb.AppendLine(string.Join(",", new[] { F(t), sim.Phase.ToString(), carrier.ToString(), sim.Agents[carrier].Action.ToString(), _stage.Ball.ModeName, F(dR), F(dL), F(axis), F(Vector3.Distance(bp, chest)), "0" }));
                        string st = sim.Phase == PlayPhase.Dead ? "afterWhistle-" + sim.Agents[carrier].Action : (sim.Agents[carrier].Action.ToString());
                        Add("ball to nearer hand m | " + st, Mathf.Min(dR, dL)); Add("ball to forearm axis m | " + st, axis);
                        if (sim.Agents[carrier].Action == AgentAction.Celebrate || (whistleAt > 0f && t < whistleAt + 9f)) Hist("ball held frames " + st);
                    }
                    if (carrier >= 0 && tdAt > 0f && t >= nextShot && t < tdAt + 9f && tiles.Count < 16)
                    {
                        var v = _stage.Views[carrier]; Vector3 q = v.transform.position; Vector3 fwd = v.Forward; Vector3 right = Vector3.Cross(Vector3.up, fwd);
                        var hidden = new List<Renderer>(); HideOthers(new[] { carrier }, hidden);
                        PlaceCam(q + fwd * 2.4f + right * 1.3f + Vector3.up * 1.35f, q + Vector3.up * 1.15f, 36f);
                        tiles.Add(ShotCam(360, 360));
                        foreach (var rr in hidden) rr.enabled = true;
                        nextShot = t + 0.6f;
                    }
                    if (tdAt > 0f && t > tdAt + 9f) break;
                    if (tdAt < 0f && t > 10f) break;
                }
                Sheet(tiles, 4, _out + "/" + _tag + "_td" + found + "_celebration.png");
                File.WriteAllText(_out + "/" + _tag + "_td" + found + "_ball.csv", bsb.ToString());
                _report.AppendLine("TD scene " + found + ": " + off.Name + " carrier " + carrier + " td at " + tdAt.ToString("0.0") + " whistle " + whistleAt.ToString("0.0"));
                _stage.End();
                yield return null;
            }
            // a tackle (walk back with the ball after the whistle): the first normal run from midfield
            {
                var off = runs[0]; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(30, 1, 10), 3001);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep(); if (_crew != null) _crew.BeginPlay(frame, sim);
                for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                float t = 0f; int carrier = -1; float whistleAt = -1f; var tiles = new List<Texture2D>(); float nextShot = 0f; var carryTiles = new List<Texture2D>(); float nextCarry = 0.3f;
                while (t < 16f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; if (_crew != null) _crew.Tick(Dt, sim, sim.Phase == PlayPhase.Live); yield return null;
                    int h = sim.Ball.Holder; if (h >= 0) carrier = h;
                    if (whistleAt < 0f && carrier >= 0 && sim.Ball.Holder == carrier && sim.Agents[carrier].Action == AgentAction.Carry && t >= nextCarry && carryTiles.Count < 8)
                    {
                        var cv = _stage.Views[carrier]; Vector3 cq = cv.transform.position; Vector3 cf = cv.Forward; Vector3 cr = Vector3.Cross(Vector3.up, cf);
                        var chid = new List<Renderer>(); HideOthers(new[] { carrier }, chid);
                        PlaceCam(cq + cf * 2.6f - cr * 1.5f + Vector3.up * 1.3f, cq + Vector3.up * 1.05f, 36f);
                        carryTiles.Add(ShotCam(360, 360)); foreach (var rr in chid) rr.enabled = true; nextCarry = t + 0.35f;
                    }
                    if (sim.Phase == PlayPhase.Dead && whistleAt < 0f) { whistleAt = t; if (_crew != null) _crew.Signal(OfficialSignal.DeadBall); }
                    if (carrier >= 0 && sim.Ball.Holder == carrier && _stage.Ball != null)
                    {
                        var r = GetRec(_stage.Views[carrier]);
                        if (r != null)
                        {
                            Vector3 bp = _stage.Ball.Position;
                            float axis = Mathf.Min(SegDist(bp, r.RLA.position, r.RH.position), SegDist(bp, r.LLA.position, r.LH.position));
                            string st = sim.Phase == PlayPhase.Dead ? "afterWhistle-" + sim.Agents[carrier].Action + " mode " + _stage.Ball.ModeName : sim.Agents[carrier].Action + " mode " + _stage.Ball.ModeName;
                            Add("ball to forearm axis m | " + st, axis); if (whistleAt > 0f) { Hist("BALLFLOW state " + _stage.BallFlowState + " taker " + (_crew != null && _crew.HasTaker) + " action " + sim.Agents[carrier].Action); Vector3 tpp; if (_crew != null && _crew.TakerPosition(out tpp)) Add("taker distance to carrier m", Vector3.Distance(tpp, _stage.Views[carrier].transform.position)); }
                        }
                    }
                    if (whistleAt > 0f && t >= nextShot && t < whistleAt + 10f && tiles.Count < 12 && carrier >= 0)
                    {
                        var v = _stage.Views[carrier]; Vector3 q = v.transform.position; Vector3 fwd = v.Forward; Vector3 right = Vector3.Cross(Vector3.up, fwd);
                        var hidden = new List<Renderer>(); HideOthers(new[] { carrier }, hidden);
                        PlaceCam(q + fwd * 2.6f + right * 1.4f + Vector3.up * 1.3f, q + Vector3.up * 1.0f, 38f);
                        tiles.Add(ShotCam(360, 360));
                        foreach (var rr in hidden) rr.enabled = true;
                        nextShot = t + 0.8f;
                    }
                    if (whistleAt > 0f && t > whistleAt + 10f) break;
                }
                Sheet(tiles, 4, _out + "/" + _tag + "_walkback.png"); Sheet(carryTiles, 4, _out + "/" + _tag + "_carry.png");
                _report.AppendLine("tackle scene: carrier " + carrier + " end " + sim.EndReason + " whistle " + whistleAt.ToString("0.0"));
                _stage.End();
                yield return null;
            }
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float l2 = ab.sqrMagnitude; if (l2 < 1e-8f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
            return Vector3.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------------------------------ output
        public static string SkinDiag { get { return SkinProbe.Diag; } }

        void WriteSummary()
        {
            var sb = new StringBuilder("# Body probe " + _tag + " (humanoid muscle space, degrees; Unity default limits)\n\n");
            foreach (var k in _acc.Keys.OrderBy(x => x))
            {
                var l = _acc[k]; l.Sort();
                Func<double, double> q = p => l[Math.Min(l.Count - 1, (int)(p * l.Count))];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-88} n={1,6} mean {2,8:F2} p01 {3,8:F2} p50 {4,8:F2} p95 {5,8:F2} p99 {6,8:F2} max {7,8:F2}", k, l.Count, l.Average(), q(.01), q(.5), q(.95), q(.99), l[l.Count - 1]));
            }
            SkinSummary(sb); RingSummary(sb);
            sb.AppendLine(Environment.NewLine + "## counters");
            foreach (var kv in _hist.OrderBy(x => x.Key)) sb.AppendLine(kv.Key + " = " + kv.Value);
            File.WriteAllText(_out + "/" + _tag + "_summary.txt", sb.ToString());
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
