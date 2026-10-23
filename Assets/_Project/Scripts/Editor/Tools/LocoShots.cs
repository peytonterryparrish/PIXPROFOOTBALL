using System;
using System.Collections;
using System.Collections.Generic;
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
    /// v0.9.0 LOCOMOTION (LOCO-006): measurement and film for the gait system.
    ///  - treadmill: a real match view driven at constant speed / direction; planted-foot speed, contact foot height, cadence and hips bob per gait.
    ///  - plays: real plays of the match stage; every running player is logged each frame (motion dump CSV) and measured the same way, per speed band.
    ///  - strips: 12-frame contact sheets of a sprinting RB and a running WR from the game camera and a side camera.
    /// Run: -executeMethod PixFootball.EditorTools.LocoShots.Run -pixLoco [-pixLocoOff] [-pixLocoTag name] [-pixLocoModes treadmill,plays,strips]   (batch mode WITHOUT -nographics)
    /// </summary>
    public static class LocoShots
    {
        public const string Flag = "-pixLoco";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def) { return LocoCycleScan.CmdArg(name, def); }
        public static bool Off { get { return Array.IndexOf(Environment.GetCommandLineArgs(), "-pixLocoOff") >= 0; } }

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
            new GameObject("PixLoco").AddComponent<LocoRunner>();
        }
    }

    public sealed class LocoRunner : MonoBehaviour
    {
        string _out, _tag;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _report = new StringBuilder();
        int _playIndex;
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }

        // ---------------------------------------------------------------------------------------- foot log and metrics
        public sealed class Frm { public float T, Speed, Hips; public Vector3 AL, TL, AR, TR; public float SoleL, SoleR, HeelL, BallL, HeelR, BallR, Hand, AnkHL, AnkHR, ToeHL, ToeHR; public bool Carry; public string Clip; }

        public sealed class Stats
        {
            public List<float> WinSpeed = new List<float>(), InnerSpeed = new List<float>(), WinHeight = new List<float>(), HeightMin = new List<float>();
            public List<float> Cadence = new List<float>(), Bob = new List<float>(), Fwd = new List<float>(), Swing = new List<float>(), QbSlide = new List<float>();
            public int Windows;
            public void Add(Stats o) { QbSlide.AddRange(o.QbSlide); WinSpeed.AddRange(o.WinSpeed); InnerSpeed.AddRange(o.InnerSpeed); Fwd.AddRange(o.Fwd); WinHeight.AddRange(o.WinHeight); HeightMin.AddRange(o.HeightMin); Cadence.AddRange(o.Cadence); Bob.AddRange(o.Bob); Swing.AddRange(o.Swing); Windows += o.Windows; }
        }

        static float Mean(List<float> l) { return l.Count == 0 ? float.NaN : l.Average(); }
        static float Pct(List<float> l, float p) { if (l.Count == 0) return float.NaN; var s = l.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(p * (s.Count - 1) + 0.5f), 0, s.Count - 1)]; }

        /// <summary>Measures one continuous stretch of a running player (60 Hz frames): stance windows per foot, the speed of the ground-contact point (heel or ball, whichever is lower) inside them, its height and the cadence.</summary>
        public static StringBuilder Verbose;
        public static Stats Measure(List<Frm> f, float dt, Vector3 dir = default(Vector3))
        {
            var st = new Stats();
            if (f.Count < 30) return st;
            double dur = (f.Count - 1) * dt; int contacts = 0;
            int n = f.Count;
            for (int foot = 0; foot < 2; foot++)
            {
                var sole = new float[n]; var spd = new float[n]; var fwd = new float[n];
                for (int i = 0; i < n; i++)
                {
                    sole[i] = foot == 0 ? f[i].SoleL : f[i].SoleR;
                    if (i == 0) { spd[i] = 99f; continue; }
                    // the contact point: the lower of heel / ball, the same point in both frames
                    bool toe = foot == 0 ? (f[i].BallL < f[i].HeelL && f[i - 1].BallL < f[i - 1].HeelL) : (f[i].BallR < f[i].HeelR && f[i - 1].BallR < f[i - 1].HeelR);
                    Vector3 aP = foot == 0 ? f[i - 1].AL : f[i - 1].AR, aC = foot == 0 ? f[i].AL : f[i].AR, tP = foot == 0 ? f[i - 1].TL : f[i - 1].TR, tC = foot == 0 ? f[i].TL : f[i].TR;
                    Vector3 d = toe ? tC - tP : aC - aP; d.y = 0f;
                    spd[i] = d.magnitude / dt; fwd[i] = dir.sqrMagnitude > 0.01f ? Vector3.Dot(d, dir.normalized) / dt : 0f;
                }
                var sorted = (float[])sole.Clone(); Array.Sort(sorted);
                float lo = sorted[Mathf.Max(0, (int)(n * 0.05f))];
                // stance = the sole is down AND the foot is slower over the ground than the body (a foot that skims the turf faster than the body is swinging)
                var c = new bool[n];
                for (int i = 0; i < n; i++) c[i] = sole[i] < lo + 0.02f && spd[i] < 0.85f * Mathf.Max(1f, f[i].Speed);
                int i0 = 0;
                while (i0 < n)
                {
                    if (!c[i0]) { i0++; continue; }
                    int i1 = i0; while (i1 + 1 < n && c[i1 + 1]) i1++;
                    int len = i1 - i0 + 1;
                    if (len >= 3 && i0 > 1 && i1 < n - 1)      // complete windows only
                    {
                        contacts++;
                        var sp = new List<float>();
                        for (int i = i0; i <= i1; i++) sp.Add(spd[i]);
                        if (Verbose != null) Verbose.AppendLine("foot " + foot + " frames " + i0 + "-" + i1 + " len " + len + " h " + sole[i0].ToString("0.000") + ".." + sole.Skip(i0).Take(len).Min().ToString("0.000") + " speeds " + string.Join(" ", sp.Select(x => x.ToString("0.0"))));
                        st.WinSpeed.Add(sp.Average());
                        if (dir.sqrMagnitude > 0.01f) { var fl = new List<float>(); for (int i = i0; i <= i1; i++) fl.Add(fwd[i]); int q0 = Mathf.RoundToInt(fl.Count * 0.2f), q1 = fl.Count - q0; st.Fwd.Add(q1 > q0 ? fl.Skip(q0).Take(q1 - q0).Average() : fl.Average()); }
                        int s0 = Mathf.RoundToInt(sp.Count * 0.2f), s1 = sp.Count - s0;
                        st.InnerSpeed.Add(s1 > s0 ? sp.Skip(s0).Take(s1 - s0).Average() : sp.Average());
                        float hm = 0f; for (int i = i0; i <= i1; i++) hm += sole[i]; st.WinHeight.Add(hm / len);
                        st.HeightMin.Add(sole.Skip(i0).Take(len).Min());
                        st.Windows++;
                    }
                    i0 = i1 + 1;
                }
            }
            // the QB audit's contact slide: toe below 3 cm and ankle below 14.5 cm, 6+ frames, core frames (two in from each end), net toe displacement over the core
            for (int foot = 0; foot < 2; foot++)
            {
                int i = 0;
                while (i < n)
                {
                    Func<int, bool> pl = k2 => (foot == 0 ? f[k2].ToeHL : f[k2].ToeHR) < 0.03f && (foot == 0 ? f[k2].AnkHL : f[k2].AnkHR) < 0.145f;
                    if (!pl(i)) { i++; continue; }
                    int j = i; while (j + 1 < n && pl(j + 1)) j++;
                    if (j - i + 1 >= 6 && i > 0 && j < n - 1)
                    {
                        int a = i + 2, b = j - 2;
                        if (b > a) { Vector3 pa = foot == 0 ? f[a].TL : f[a].TR, pb = foot == 0 ? f[b].TL : f[b].TR; pa.y = 0f; pb.y = 0f; st.QbSlide.Add((pb - pa).magnitude / ((b - a) * dt)); }
                    }
                    i = j + 1;
                }
            }
            if (dur > 0.3) st.Cadence.Add((float)(contacts / dur));
            float bmin = 9, bmax = -9; foreach (var x in f) { bmin = Mathf.Min(bmin, x.Hips); bmax = Mathf.Max(bmax, x.Hips); }
            st.Bob.Add(bmax - bmin);
            { float hmin = 9, hmax = -9; foreach (var x in f) { hmin = Mathf.Min(hmin, x.Hand); hmax = Mathf.Max(hmax, x.Hand); } st.Swing.Add(hmax - hmin); }
            return st;
        }

        static string Row(string name, Stats s)
        {
            return "| " + name + " | " + s.Windows + " | " + Mean(s.WinSpeed).ToString("0.00") + " | " + Pct(s.WinSpeed, 0.9f).ToString("0.00") + " | " + Mean(s.InnerSpeed).ToString("0.00") + " | " + Mean(s.WinHeight).ToString("0.000") + " | " + Pct(s.WinHeight, 0.9f).ToString("0.000") + " | " + (s.WinHeight.Count > 0 ? (100f * s.WinHeight.Count(x => x > 0.03f) / s.WinHeight.Count).ToString("0") + "%" : "-") + " | " + Mean(s.Cadence).ToString("0.0") + " | " + Mean(s.Bob).ToString("0.000") + " | " + Mean(s.Swing).ToString("0.00") + " | " + Mean(s.QbSlide).ToString("0.00") + " / " + Pct(s.QbSlide, 0.5f).ToString("0.00") + " (" + s.QbSlide.Count + ") |";
        }
        const string Head = "| gait | stance windows | planted speed mean (m/s) | p90 | inner 60% mean | contact height mean (m) | p90 | windows > 3 cm | contacts/s (both feet) | hips bob (m) | arm swing (m) | toe slide QB metric mean / p50 (n) |\n|---|---|---|---|---|---|---|---|---|---|---|---|";

        Frm Capture(SimAgentView v, float t, float speed, bool carry)
        {
            var an = v.Avatar.Animator;
            Transform la = an.GetBoneTransform(HumanBodyBones.LeftFoot), ra = an.GetBoneTransform(HumanBodyBones.RightFoot), lt = an.GetBoneTransform(HumanBodyBones.LeftToes), rt = an.GetBoneTransform(HumanBodyBones.RightToes), hp = an.GetBoneTransform(HumanBodyBones.Hips);
            var pl = v.Motion != null ? v.Motion.Plant : null;
            float gy = v.transform.position.y;
            var f = new Frm { T = t, Speed = speed, Carry = carry, AL = la.position, AR = ra.position, TL = lt != null ? lt.position : la.position, TR = rt != null ? rt.position : ra.position, Hips = hp.position.y - gy, Clip = v.Rig != null ? v.Rig.CurrentClipName : "" };
            float rAL = pl != null ? pl.RestAnkleL : 0.097f, rTL = pl != null ? pl.RestToeL : 0.024f, rAR = pl != null ? pl.RestAnkleR : 0.097f, rTR = pl != null ? pl.RestToeR : 0.024f;
            { Transform rh = an.GetBoneTransform(HumanBodyBones.RightHand); f.Hand = rh != null ? Vector3.Dot(rh.position - hp.position, v.Forward) : 0f; }
            f.HeelL = f.AL.y - gy - rAL; f.BallL = f.TL.y - gy - rTL; f.HeelR = f.AR.y - gy - rAR; f.BallR = f.TR.y - gy - rTR;
            f.SoleL = Mathf.Min(f.HeelL, f.BallL); f.SoleR = Mathf.Min(f.HeelR, f.BallR);
            f.AnkHL = f.AL.y - gy; f.AnkHR = f.AR.y - gy; f.ToeHL = f.TL.y - gy; f.ToeHR = f.TR.y - gy;
            return f;
        }

        // ---------------------------------------------------------------------------------------- setup
        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _tag = LocoShots.Arg("-pixLocoTag", LocoShots.Off ? "before" : "after");
            _out = "D:/PIXFootball/anim_loco/" + _tag; Directory.CreateDirectory(_out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) Debug.Log("[Loco] logged " + t + ": " + c); };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("LocoCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            GaitPlayer.DebugOn = true;
            { float bf; if (float.TryParse(LocoShots.Arg("-pixLocoBackFactor", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bf)) { var ll2 = LocoCycleLibrary.Load(); foreach (var c in ll2.cycles) if (Mathf.Abs(c.Angle) > 150f) GaitPlayer.Factors[c] = new float[] { bf, bf, bf }; } }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixLocoNoArms") >= 0) BodyMotion.ArmSwingScaleOn = false;
            { var ll = LocoCycleLibrary.Load(); var lsb = new StringBuilder("library: "); if (ll != null) foreach (var c in ll.cycles) lsb.AppendLine(c.name + (c.carry ? " BC" : "") + " speed " + c.speed.ToString("0.00") + " ang " + c.Angle.ToString("0") + " len " + c.length.ToString("0.00") + " knots " + c.lLift.ToString("0.00") + "/" + c.rPlant.ToString("0.00") + "/" + c.rLift.ToString("0.00")); _report.AppendLine(lsb.ToString()); }
            if (LocoShots.Off) { SimAgentView.DebugNoGait = true; FootPlant.Enabled = false; ActionAnimator.DisableGait = true; }
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("LocoStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            var modes = LocoShots.Arg("-pixLocoModes", "treadmill,plays,strips").Split(',');
            if (modes.Contains("learn")) yield return Learn();
            if (modes.Contains("mmlearn")) yield return MmLearn();
            if (modes.Contains("calibrate")) { FootPlant.Enabled = false; yield return Calibrate(); FootPlant.Enabled = !LocoShots.Off; }
            if (modes.Contains("treadmill")) yield return Treadmill();
            if (modes.Contains("legs")) yield return Legs();
            if (modes.Contains("maneuvers")) yield return Maneuvers();
            if (modes.Contains("kicks")) yield return Kicks();
            if (modes.Contains("plays")) yield return Plays();
            if (modes.Contains("strips")) yield return Strips();
            File.WriteAllText(_out + "/metrics_" + string.Join("_", modes) + ".md", "# Locomotion metrics (" + _tag + ")\n\nplanted speed = horizontal speed of the ground-contact point (heel or ball of the foot, whichever is lower) during a stance window; contact height = lowest sole point above the turf (the zero is the bind pose of the avatar).\n\n" + _treadText + _legText + "\n" + _maneuverText + "\n" + _kickText + "\n" + _playText + "\n" + _report);
            Debug.Log("[Loco] done " + _out);
            yield return null;
            EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------------------------------- learning the native speeds
        /// <summary>Plays every cycle of the library alone on the treadmill at its estimated speed with the foot planting on: the gait player learns the real native speed of the take from the slide of the planted foot.
        /// The factors are written to calibration_factors.csv; LocoCycleBuilder.Build bakes them into the library.</summary>
        IEnumerator Learn()
        {
            var lib = LocoCycleLibrary.Load();
            var root = new GameObject("Learn").transform;
            const float dt = 1f / 60f;
            var sb = new StringBuilder("name,carry,factor,events\n");
            GaitPlayer.Factors.Clear();
            for (int round = 0; round < 2; round++)    // two rounds: the second one starts from the learned factors
            foreach (var c in lib.cycles)
            {
                var view = SimAgentView.CreateForMatch(root, PlayerSize.Medium, "Learn");
                view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.ResetForPlay(Vector3.zero, 0f);
                yield return null; yield return null;
                var h = new AnimHints { Seed = 5, Side = 'C', Carrying = c.carry };
                Vector3 dir = Quaternion.Euler(0f, c.Angle, 0f) * Vector3.forward;
                Vector3 pos = Vector3.zero; float sp = 0f;
                string key = c.name + (c.carry ? "|BC#" : "#") + 1;
                if (view.Gait != null) { view.Gait.ForceCycle = c; view.Gait.ForceRate = 1f; }
                int frames = 420;
                for (int f = 0; f < frames; f++)
                {
                    float target = c.speed * GaitPlayer.FactorOf(c, 1) * view.GaitLegScale;
                    sp = Mathf.MoveTowards(sp, target, 8f * dt);
                    Vector3 vel = dir * sp; pos += vel * dt;
                    view.ApplyMatch(pos, vel, Vector3.forward, c.carry ? AgentAction.Carry : AgentAction.Run, h, 0f, false, false, dt);
                    yield return null;
                }
                _report.AppendLine("learn round " + round + " " + c.name + " factor " + GaitPlayer.FactorOf(c, 1).ToString("0.000") + " events " + (view.Gait != null ? view.Gait.LearnEvents : -1));
                if (round == 1) sb.AppendLine(c.name + (string.IsNullOrEmpty(c.donor) ? "" : "#d") + "," + (c.carry ? 1 : 0) + "," + GaitPlayer.FactorOf(c, 1).ToString("0.0000") + "," + (view.Gait != null ? view.Gait.LearnEvents : -1));
                Destroy(view.gameObject); yield return null;
            }
            Directory.CreateDirectory("D:/PIXFootball/anim_loco");
            File.WriteAllText("D:/PIXFootball/anim_loco/calibration_factors.csv", sb.ToString());
            Destroy(root.gameObject);
        }

        // ---------------------------------------------------------------------------------------- stops and cuts (treadmill)
        string _maneuverText = "";

        /// <summary>A runner brakes from a steady speed (stop) or turns the velocity by 90 degrees losing 30 percent of the speed (cut): planted-foot speed over the manoeuvre and a film.</summary>
        IEnumerator Maneuvers()
        {
            const float dt = 1f / 60f;
            var root = new GameObject("Man").transform;
            var sb = new StringBuilder("## manoeuvres (treadmill; stance windows over the whole manoeuvre)\n\n| manoeuvre | stance windows | planted speed mean (m/s) | inner 60% | p90 | contact height mean (m) | toe slide QB metric mean / p50 (n) |\n|---|---|---|---|---|---|\n");
            foreach (var spec in new[] { new { Name = "stop from 4 m/s", V = 4f, Cut = false, Film = false }, new { Name = "stop from 6 m/s", V = 6f, Cut = false, Film = true }, new { Name = "stop from 8 m/s", V = 8f, Cut = false, Film = false }, new { Name = "cut 90 deg at 6 m/s", V = 6f, Cut = true, Film = true }, new { Name = "cut 90 deg at 8 m/s", V = 8f, Cut = true, Film = false } })
            {
                var all = new Stats(); var tiles = new List<Texture2D>();
                for (int rep = 0; rep < 3; rep++)
                {
                    var view = SimAgentView.CreateForMatch(root, PlayerSize.Medium, "Man");
                    view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    view.ResetForPlay(Vector3.zero, 0f);
                    yield return null; yield return null;
                    Vector3 pos = Vector3.zero; float sp = 0f; var h = new AnimHints { Seed = 5 + rep, Side = 'C' };
                    Vector3 dir = Vector3.forward, face = Vector3.forward; float yaw = 0f;
                    var frames = new List<Frm>(); float t = 0f; int shot = 0; float next = 0f;
                    var mtrace = (rep == 0 && LocoShots.Arg("-pixLocoManTrace", "0") == "1") ? new StringBuilder("tm,speed,layer,clip,hintL,hintR,plantL,plantR,soleL,soleR,aL_spd,tL_spd,aR_spd,tR_spd,yaw\n") : null;
                    int total = 360 + 120 + rep * 7;      // run-up, the manoeuvre, a second of aftermath
                    // the scripted motion, simulated once: the simulation knows where the runner will be (the matcher is given the planned path, +0.33 / +0.66 / +1.0 s)
                    int pre = total + 80; var pArr = new Vector3[pre]; var vArr = new Vector3[pre]; var fArr = new Vector3[pre]; var sArr = new float[pre];
                    { Vector3 pp = Vector3.zero, dd = Vector3.forward; float ss = 0f, yy = 0f;
                      for (int f = 0; f < pre; f++)
                      {
                        float tm0 = f * dt - (360 + rep * 7) * dt;
                        if (tm0 < 0f) ss = Mathf.MoveTowards(ss, spec.V, 8f * dt);
                        else if (!spec.Cut) ss = Mathf.MoveTowards(ss, 0f, 10f * dt);
                        else { float a0 = Mathf.Clamp01(tm0 / 0.45f) * 90f; dd = Quaternion.Euler(0f, a0, 0f) * Vector3.forward; ss = Mathf.MoveTowards(ss, spec.V * 0.7f, spec.V * 0.3f / 0.3f * dt); }
                        float tg = Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg; yy = Mathf.MoveTowardsAngle(yy, tg, 720f * dt);
                        Vector3 vv = dd * ss; pp += vv * dt; pArr[f] = pp; vArr[f] = vv; sArr[f] = ss; fArr[f] = new Vector3(Mathf.Sin(yy * Mathf.Deg2Rad), 0f, Mathf.Cos(yy * Mathf.Deg2Rad));
                      } }
                    for (int f = 0; f < total; f++)
                    {
                        float tm = f * dt - (360 + rep * 7) * dt;       // time since the manoeuvre started
                        if (tm < 0f) sp = Mathf.MoveTowards(sp, spec.V, 8f * dt);
                        else if (!spec.Cut) sp = Mathf.MoveTowards(sp, 0f, 10f * dt);
                        else
                        {
                            // velocity turns 90 degrees to the right over 0.45 s, the speed drops to 70 percent over 0.3 s, the body faces along the velocity at 720 deg/s
                            float a = Mathf.Clamp01(tm / 0.45f) * 90f; dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                            sp = Mathf.MoveTowards(sp, spec.V * 0.7f, spec.V * 0.3f / 0.3f * dt);
                        }
                        float target = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                        yaw = Mathf.MoveTowardsAngle(yaw, target, 720f * dt); face = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
                        Vector3 vel = dir * sp; pos += vel * dt; t += dt;
                        view.SetMotionPlan(pArr[Mathf.Min(f + 20, pre - 1)] - pArr[f], pArr[Mathf.Min(f + 40, pre - 1)] - pArr[f], pArr[Mathf.Min(f + 60, pre - 1)] - pArr[f]);      // v0.10.0: the planned path
                        view.ApplyMatch(pos, vel, face, spec.Cut ? AgentAction.Run : (sp > 0.5f ? AgentAction.Run : AgentAction.Idle), h, 0f, false, false, dt);
                        yield return null;
                        if (tm >= 0f && tm < 0.8f)
                        {
                            var fr = Capture(view, t, sp, false); frames.Add(fr);
                            if (mtrace != null && frames.Count > 1) { var pl = view.Motion != null ? view.Motion.Plant : null; var gp = view.Mm; var c1 = frames[frames.Count - 2]; Func<Vector3, Vector3, float> sx = (u, w) => new Vector2(u.x - w.x, u.z - w.z).magnitude / dt; mtrace.AppendLine(tm.ToString("0.000") + "," + sp.ToString("0.00") + "," + (gp != null ? gp.Layer.ToString("0.00") + "/" + gp.ClipName : "-") + "," + fr.Clip + "," + (pl != null ? pl.HintLeft + "," + pl.HintRight + "," + (pl.PlantedLeft ? 1 : 0) + "," + (pl.PlantedRight ? 1 : 0) : "-,-,-,-") + "," + fr.SoleL.ToString("0.000") + "," + fr.SoleR.ToString("0.000") + "," + sx(fr.AL, c1.AL).ToString("0.00") + "," + sx(fr.TL, c1.TL).ToString("0.00") + "," + sx(fr.AR, c1.AR).ToString("0.00") + "," + sx(fr.TR, c1.TR).ToString("0.00") + "," + yaw.ToString("0")); }
                            if (spec.Film && rep == 0 && tm >= next && shot < 12)
                            {
                                Vector3 right = Vector3.Cross(Vector3.up, Vector3.forward);
                                _cam.transform.position = pos - right * 5.0f + Vector3.up * 1.1f; _cam.transform.rotation = Quaternion.LookRotation(pos + Vector3.up * 0.8f - _cam.transform.position); _cam.fieldOfView = 36f;
                                tiles.Add(ShotCam(560, 420)); shot++; next += 0.07f;
                            }
                        }
                    }
                    all.Add(Measure(frames, dt));
                    if (mtrace != null) File.WriteAllText(_out + "/trace_man_" + spec.Name.Replace(" ", "_").Replace("/", "") + ".csv", mtrace.ToString());
                    Destroy(view.gameObject); yield return null;
                }
                sb.AppendLine("| " + spec.Name + " | " + all.Windows + " | " + Mean(all.WinSpeed).ToString("0.00") + " | " + Mean(all.InnerSpeed).ToString("0.00") + " | " + Pct(all.WinSpeed, 0.9f).ToString("0.00") + " | " + Mean(all.WinHeight).ToString("0.000") + " | " + Mean(all.QbSlide).ToString("0.00") + " / " + Pct(all.QbSlide, 0.5f).ToString("0.00") + " (" + all.QbSlide.Count + ") |");
                if (tiles.Count > 0) Sheet(tiles, 4, _out + "/strip_maneuver_" + spec.Name.Replace(" ", "_").Replace("/", "") + ".png");
            }
            _maneuverText = sb.ToString();
            Destroy(root.gameObject);
        }

        // ---------------------------------------------------------------------------------------- kicks (action ground pass check)
        string _kickText = "";
        IEnumerator Kicks()
        {
            const float dt = 1f / 60f;
            var sb = new StringBuilder("## kicks: one line per 0.1 s of the kicker (action / clip / lowest sole before the action pass / offset the pass applied / lowest toe above the turf)\n\n");
            Func<string, Play> sp = n => _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == n);
            foreach (var spec in new[] { new { Name = "fieldgoal", Off = "Field Goal", Def = "Field Goal Block Middle", Gs = Scrim(80, 4, 5), Kicker = true }, new { Name = "kickoff", Off = "Kickoff Deep Middle", Def = "Kick Return Middle Wedge", Gs = new GameState(_rules, Team.Away), Kicker = true }, new { Name = "punt", Off = "Punt", Def = "Punt Return Middle", Gs = Scrim(40, 4, 8), Kicker = true } })
            {
                Play off, def; try { off = sp(spec.Off); def = sp(spec.Def); } catch { sb.AppendLine("(missing play " + spec.Off + ")"); continue; }
                var sim = new PlaySimulation();
                sim.Setup(spec.Gs, off, def, null, null, _rules, new Rng(77), new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f });
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
                foreach (var v in _stage.Views) { if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue; v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true; }
                int kicker = 0; for (int i = 0; i < 22; i++) if (sim.Agents[i].PlayPos == Position.K) { kicker = i; break; }
                for (int f = 0; f < 90; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt);
                sb.AppendLine("### " + spec.Name + " (kicker agent " + kicker + ")");
                float t = 0f; int n = 0, hover = 0, frames = 0;
                while (sim.Phase != PlayPhase.Dead && t < 4.5f)
                {
                    sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); t += dt; yield return null;
                    var v = _stage.Views[kicker]; var an = v.Avatar.Animator;
                    Transform lt = an.GetBoneTransform(HumanBodyBones.LeftToes), rt = an.GetBoneTransform(HumanBodyBones.RightToes);
                    float gy = v.transform.position.y; float low = Mathf.Min(lt.position.y, rt.position.y) - gy;
                    frames++; if (low > 0.07f) hover++;
                    if (++n % 6 == 0) sb.AppendLine("t=" + t.ToString("0.0") + " " + sim.Agents[kicker].Action + " clip=" + v.Rig.CurrentClipName + " w=" + v.Rig.Weight.ToString("0.00") + " gap=" + v.LastSoleGap.ToString("0.000") + " off=" + v.ActionGroundOffset.ToString("0.000") + " lowToe=" + low.ToString("0.000"));
                }
                sb.AppendLine("frames " + frames + " with no toe within 7 cm of the turf: " + hover + "\n");
                _stage.End(); yield return null;
            }
            // quarterback plays: frames with no toe within 7 cm of the turf between the snap and the end of the play
            foreach (var pl in PlayList())
            {
                var sim = StartPlay(pl.Value, 77);
                int q = 0; for (int i = 0; i < 22; i++) if (sim.Agents[i].PlayPos == Position.QB && sim.Agents[i].Side == SimSide.Offense) { q = i; break; }
                for (int f = 0; f < 90; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt);
                float t = 0f; int frames = 0, hover = 0, hover20 = 0;
                while (sim.Phase != PlayPhase.Dead && t < 4f)
                {
                    sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); t += dt; yield return null;
                    var v = _stage.Views[q]; var an = v.Avatar.Animator; if (v.ContactDriven) continue;
                    Transform lt = an.GetBoneTransform(HumanBodyBones.LeftToes), rt = an.GetBoneTransform(HumanBodyBones.RightToes);
                    float low = Mathf.Min(lt.position.y, rt.position.y) - v.transform.position.y;
                    frames++; if (low > 0.07f) hover++; if (low > 0.20f) hover20++;
                }
                sb.AppendLine("QB " + pl.Key + ": frames " + frames + ", no toe within 7 cm of the turf " + hover + " (" + (frames > 0 ? (100f * hover / frames).ToString("0.0") : "-") + "%), above 20 cm " + hover20);
                _stage.End(); yield return null;
            }
            _kickText = sb.ToString();
        }

        // ---------------------------------------------------------------------------------------- calibration
        /// <summary>Measures the real native speed of one cycle on one body: the cycle is played alone at its natural cadence with the foot planting off, the body walks the treadmill at the estimated speed, and the average
        /// forward speed of the ground-contact point over the stance windows says how far the estimate is off (a planted foot has speed zero). Three passes converge.</summary>
        IEnumerator CalibrateCycle(LocoCycleLibrary.Cycle c, PlayerSize size, Transform root, float[] result)
        {
            const float dt = 1f / 60f;
            var view = SimAgentView.CreateForMatch(root, size, "Cal");
            view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.ResetForPlay(Vector3.zero, 0f);
            yield return null; yield return null;
            var g = view.Gait;
            if (g == null) { result[0] = c.speed; yield break; }
            g.ForceCycle = c; g.ForceRate = 1f;
            var h = new AnimHints { Seed = 5, Side = 'C', Carrying = c.carry };
            Vector3 dir = Quaternion.Euler(0f, c.Angle, 0f) * Vector3.forward;
            float s = c.speed, s0 = c.speed; Vector3 pos = Vector3.zero;
            for (int pass = 0; pass < 4; pass++)
            {
                var frames = new List<Frm>();
                for (int f = 0; f < 200; f++)
                {
                    Vector3 vel = dir * s; pos += vel * dt;
                    view.ApplyMatch(pos, vel, Vector3.forward, c.carry ? AgentAction.Carry : AgentAction.Run, h, 0f, false, false, dt);
                    yield return null;
                    if (f >= 60) frames.Add(Capture(view, f * dt, s, c.carry));
                }
                var st = Measure(frames, dt, dir);
                if (st.Fwd.Count >= 3)
                {
                    float fm = st.Fwd.Average();
                    s = Mathf.Clamp(s - fm, 0.5f * s0, 1.8f * s0);
                }
                result[1 + pass] = s;
            }
            result[0] = s;
            Destroy(view.gameObject); yield return null;
        }

        IEnumerator Calibrate()
        {
            var lib = LocoCycleLibrary.Load();
            var root = new GameObject("Cal").transform;
            var sb = new StringBuilder("name,carry,estimate,calibrated,pass1,pass2,pass3,pass4\n");
            var res = new float[5];
            var byName = new Dictionary<string, float>();
            foreach (var c in lib.cycles)
            {
                yield return CalibrateCycle(c, PlayerSize.Medium, root, res);
                sb.AppendLine(c.name + (string.IsNullOrEmpty(c.donor) ? "" : "#d") + "," + (c.carry ? 1 : 0) + "," + c.speed.ToString("0.00") + "," + res[0].ToString("0.00") + "," + res[1].ToString("0.00") + "," + res[2].ToString("0.00") + "," + res[3].ToString("0.00") + "," + res[4].ToString("0.00"));
                byName[c.name + (c.carry ? "|BC" : "")] = res[0];
            }
            // body-size scale: three reference takes on the small and the large body
            var sbSize = new StringBuilder("size,ratio\n");
            foreach (var size in new[] { PlayerSize.Small, PlayerSize.Large })
            {
                float num = 0f, den = 0f;
                foreach (var c in lib.cycles)
                {
                    if (c.carry || !(c.name.Contains("Run07_F") || c.name.Contains("Sprint08_F_Loop") || c.name.Contains("Sprint09_F_Loop"))) continue;
                    yield return CalibrateCycle(c, size, root, res);
                    float m; if (byName.TryGetValue(c.name, out m)) { num += res[0]; den += m; }
                }
                sbSize.AppendLine(size + "," + (den > 0f ? num / den : 1f).ToString("0.000"));
            }
            Directory.CreateDirectory("D:/PIXFootball/anim_loco");
            File.WriteAllText("D:/PIXFootball/anim_loco/calibration.csv", sb.ToString());
            File.WriteAllText("D:/PIXFootball/anim_loco/calibration_sizes.csv", sbSize.ToString());
            Destroy(root.gameObject);
        }

        // ---------------------------------------------------------------------------------------- v0.10.0 MOTION: learning the true ground speed of every database take
        /// <summary>Plays every looping take of the motion database alone on the treadmill at its estimated ground speed with the foot planting on: the planted foot's slide teaches the matcher the speed the take really moves at
        /// (<see cref="MotionMatcher.Trim"/>). Writes D:/PIXFootball/anim_loco/mm_calibration.csv (name, size, trim); <c>MotionDbBuilder.Build</c> bakes the trims into the database.</summary>
        IEnumerator MmLearn()
        {
            var db = MotionDb.Load(); if (db == null) yield break;
            var root = new GameObject("MmLearn").transform;
            const float dt = 1f / 60f;
            var sb = new StringBuilder("name,size,trim,events,speed\n");
            MotionMatcher.Trim.Clear();
            for (int round = 0; round < 2; round++)
                for (int ci = 0; ci < db.clips.Count; ci++)
                {
                    var c = db.clips[ci]; if (!c.loop) continue;
                    var view = SimAgentView.CreateForMatch(root, PlayerSize.Medium, "Ml");
                    view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    view.ResetForPlay(Vector3.zero, 0f);
                    yield return null; yield return null;
                    var mm = view.Mm; if (mm == null) { Destroy(view.gameObject); break; }
                    mm.ForceClip = ci;
                    var h = new AnimHints { Seed = 5, Side = 'C' };
                    Vector3 dir = Quaternion.Euler(0f, c.angle, 0f) * Vector3.forward; float target = Mathf.Max(2.0f, c.speed * view.GaitLegScale), sp = 0f; Vector3 pos = Vector3.zero;
                    for (int f = 0; f < 720; f++)
                    {
                        sp = Mathf.MoveTowards(sp, target, 7f * dt); Vector3 vel = dir * sp; pos += vel * dt;
                        view.ApplyMatch(pos, vel, Vector3.forward, AgentAction.Run, h, 0f, false, false, dt);
                        yield return null;
                    }
                    sb.AppendLine(c.name + ",1,0,0," + c.speed.ToString("0.00"));
                    Destroy(view.gameObject); yield return null;
                }
            var sb2 = new StringBuilder("name,size,trim,events\n");
            foreach (var kv in MotionMatcher.Trim) sb2.AppendLine(kv.Key + ",1," + kv.Value[1].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "," + MotionMatcher.Trim.Count);
            File.WriteAllText("D:/PIXFootball/anim_loco/mm_calibration.csv", sb2.ToString());
            Destroy(root.gameObject);
        }

        // ---------------------------------------------------------------------------------------- treadmill
        struct Gait { public string Name; public float Angle; public float Speed; public bool Carry; public AgentAction Action; }
        string _treadText = "";

        IEnumerator Treadmill()
        {
            var root = new GameObject("Treadmill").transform;
            var cases = new List<Gait>();
            foreach (float s in new[] { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 4.5f, 5.0f, 5.5f, 6.0f, 6.5f, 7.0f, 7.5f, 8.0f, 8.5f }) cases.Add(new Gait { Name = "free forward", Angle = 0, Speed = s, Action = s > 6.5f ? AgentAction.Sprint : AgentAction.Run });
            foreach (float s in new[] { 2.5f, 4.0f, 5.5f, 7.0f, 8.5f }) cases.Add(new Gait { Name = "carry forward", Angle = 0, Speed = s, Carry = true, Action = AgentAction.Carry });
            foreach (float s in new[] { 3.0f, 5.0f, 7.0f }) { cases.Add(new Gait { Name = "free forward-left 20", Angle = -20, Speed = s, Action = AgentAction.Run }); cases.Add(new Gait { Name = "free forward-right 20", Angle = 20, Speed = s, Action = AgentAction.Run }); }
            foreach (float s in new[] { 1.5f, 2.5f, 3.5f, 4.5f }) cases.Add(new Gait { Name = "free backpedal", Angle = 180, Speed = s, Action = AgentAction.Backpedal });
            foreach (float s in new[] { 2f, 3f }) cases.Add(new Gait { Name = "free shuffle right", Angle = 90, Speed = s, Action = AgentAction.Shuffle });
            var sb = new StringBuilder("## treadmill (a real match view driven at constant velocity after a 7 m/s^2 ramp; metrics over the last 3.5 s)\n\n" + Head + "\n");
            var tiles = new List<Texture2D>();
            int k = 100; const float dt = 1f / 60f;
            string onlyG = LocoShots.Arg("-pixLocoOnly", "");
            foreach (var g in cases)
            {
                if (onlyG.Length > 0 && g.Name.IndexOf(onlyG, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var view = SimAgentView.CreateForMatch(root, PlayerSize.Medium, "Tm");
                view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.ResetForPlay(Vector3.zero, 0f);
                yield return null; yield return null;
                Vector3 pos = Vector3.zero; float sp = 0f;
                var h = new AnimHints { Seed = 5, Side = 'C', Carrying = g.Carry };
                Vector3 dir = Quaternion.Euler(0f, g.Angle, 0f) * Vector3.forward;
                var frames = new List<Frm>(); StringBuilder trace = (g.Name == "free forward" && (g.Speed == 3.0f || g.Speed == 4.5f || g.Speed == 5.0f || g.Speed == 7.0f)) ? new StringBuilder("f,speed,layer,warp,rate,phase,hintL,hintR,plantL,plantR,wL,wR,corrL,corrR,hipDrop,soleL,soleR,liftL,pressL,heelL_spd,ballL_spd,heelR_spd,ballR_spd,cycles\n") : null;
                float t = 0f;
                for (int f = 0; f < 480; f++)
                {
                    sp = Mathf.MoveTowards(sp, g.Speed, 7f * dt);
                    Vector3 vel = dir * sp; pos += vel * dt; t += dt;
                    view.ApplyMatch(pos, vel, Vector3.forward, g.Action, h, 0f, false, false, dt);
                    yield return null;
                    if (f >= 150) frames.Add(Capture(view, t, sp, g.Carry));
                    if (trace != null && f >= 150) { var gp = view.Gait; var mmv = view.Mm; var pp = view.Motion.Plant; var c0 = frames[frames.Count - 1]; var c1 = frames.Count > 1 ? frames[frames.Count - 2] : c0; Func<Vector3, Vector3, float> sx = (a, b) => new Vector2(a.x - b.x, a.z - b.z).magnitude / dt; trace.AppendLine(f + "," + sp.ToString("0.00") + "," + (mmv != null && mmv.Layer > 0f ? mmv.Layer.ToString("0.00") + "/" + mmv.ClipName : (gp != null ? gp.Layer.ToString("0.00") : "-")) + "," + (mmv != null && mmv.Layer > 0f ? mmv.Warp.ToString("0.00") : (gp != null ? gp.Warp.ToString("0.00") : "-")) + "," + (mmv != null && mmv.Layer > 0f ? mmv.Rate.ToString("0.00") + "/row" + mmv.Row : (gp != null ? gp.Rate.ToString("0.00") + "/mix" + gp.MixSpeed.ToString("0.00") : "-")) + "," + (gp != null ? gp.Phase.ToString("0.000") : "-") + "," + pp.HintLeft + "," + pp.HintRight + "," + (pp.PlantedLeft ? 1 : 0) + "," + (pp.PlantedRight ? 1 : 0) + "," + pp.WeightLeft.ToString("0.00") + "," + pp.WeightRight.ToString("0.00") + "," + Vector2.Dot(pp.CorrVecLeft, new Vector2(dir.x, dir.z)).ToString("0.000") + "," + Vector2.Dot(pp.CorrVecRight, new Vector2(dir.x, dir.z)).ToString("0.000") + "," + pp.HipDrop.ToString("0.000") + "," + c0.SoleL.ToString("0.000") + "," + c0.SoleR.ToString("0.000") + "," + pp.LiftLeft.ToString("0.000") + "," + pp.PressLeft.ToString("0.000") + "," + sx(c0.AL, c1.AL).ToString("0.00") + "," + sx(c0.TL, c1.TL).ToString("0.00") + "," + sx(c0.AR, c1.AR).ToString("0.00") + "," + sx(c0.TR, c1.TR).ToString("0.00") + "," + (gp != null ? gp.DebugCycles : "") + "," + pp.ResidLeft.ToString("0.000") + "," + pp.ResidRight.ToString("0.000") + "," + pp.HipDrop.ToString("0.000")); }
                }
                if (g.Name == "free forward" && (g.Speed == 5.0f || g.Speed == 7.0f)) Verbose = new StringBuilder();
                var st = Measure(frames, dt);
                if (trace != null) File.WriteAllText(_out + "/trace_" + g.Speed.ToString("0.0") + ".csv", trace.ToString());
                if (Verbose != null) { File.WriteAllText(_out + "/windows_" + g.Speed.ToString("0.0") + ".txt", Verbose.ToString()); Verbose = null; }
                sb.AppendLine(Row(g.Name + " " + g.Speed.ToString("0.0") + " m/s", st));
                Destroy(view.gameObject); yield return null;
                k++;
            }
            _treadText = sb.ToString();
            Destroy(root.gameObject);
        }

        // ---------------------------------------------------------------------------------------- leg quality (round 3, LOCO-016)
        string _legText = "", _muscleText = "";
        struct LegCase { public string Name; public float Speed; public float Angle; public bool Carry; public AgentAction Action; public bool Film; }

        IEnumerator Legs()
        {
            const float dt = 1f / 60f;
            var args = System.Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-pixLocoNoPlant") >= 0) FootPlant.Enabled = false;
            if (Array.IndexOf(args, "-pixLocoNoWarp") >= 0) FootPlant.DebugNoWarp = true;
            if (Array.IndexOf(args, "-pixLocoNoIK") >= 0) FootPlant.DebugNoIK = true;
            var swapped = new List<KeyValuePair<LocoCycleLibrary.Cycle, AnimationClip>>();
            if (Array.IndexOf(args, "-pixLocoOrigClips") >= 0)
            {
                var lib = LocoCycleLibrary.Load();
                foreach (var c in lib.cycles)
                {
                    if (c.clip == null) continue;
                    var bk = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.CleanDir + "/_backup_splay/" + c.clip.name + ".anim");
                    if (bk != null) { swapped.Add(new KeyValuePair<LocoCycleLibrary.Cycle, AnimationClip>(c, c.clip)); c.clip = bk; }
                }
                _report.AppendLine("original (pre-splay-fix) clips swapped in: " + swapped.Count);
            }
            var cases = new List<LegCase>
            {
                new LegCase { Name = "jog 3.2 free", Speed = 3.2f, Action = AgentAction.Run, Film = true },
                new LegCase { Name = "run 4.5 free", Speed = 4.5f, Action = AgentAction.Run },
                new LegCase { Name = "run 6.0 free (WR)", Speed = 6.0f, Action = AgentAction.Run, Film = true },
                new LegCase { Name = "sprint 7.5 free", Speed = 7.5f, Action = AgentAction.Sprint, Film = true },
                new LegCase { Name = "shuffle right 3.0", Speed = 3.0f, Angle = 90, Action = AgentAction.Shuffle, Film = true },
                new LegCase { Name = "backpedal 3.5 at 150", Speed = 3.5f, Angle = 150, Action = AgentAction.Backpedal },
                new LegCase { Name = "backpedal 3.5 at 125", Speed = 3.5f, Angle = 125, Action = AgentAction.Backpedal },
                new LegCase { Name = "shuffle 3.0 at 60", Speed = 3.0f, Angle = 60, Action = AgentAction.Shuffle },
                new LegCase { Name = "shuffle 3.0 at 120", Speed = 3.0f, Angle = 120, Action = AgentAction.Shuffle },
                new LegCase { Name = "run 5.0 at 30", Speed = 5.0f, Angle = 30, Action = AgentAction.Run },
                new LegCase { Name = "run 5.0 at -30", Speed = 5.0f, Angle = -30, Action = AgentAction.Run },
                new LegCase { Name = "carry 4.0", Speed = 4.0f, Carry = true, Action = AgentAction.Carry },
                new LegCase { Name = "carry sprint 7.5 (RB)", Speed = 7.5f, Carry = true, Action = AgentAction.Carry, Film = true },
                new LegCase { Name = "backpedal 2.5", Speed = 2.5f, Angle = 180, Action = AgentAction.Backpedal },
                new LegCase { Name = "backpedal 3.5 (CB)", Speed = 3.5f, Angle = 180, Action = AgentAction.Backpedal, Film = true },
            };
            string onlyG = LocoShots.Arg("-pixLocoOnly", "");
            bool film = LocoShots.Arg("-pixLocoFilm", "1") == "1";
            var rows = new SortedDictionary<string, LegProbe.Acc>();
            var root = new GameObject("Legs").transform;
            foreach (var g in cases)
            {
                if (onlyG.Length > 0 && g.Name.IndexOf(onlyG, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var view = SimAgentView.CreateForMatch(root, PlayerSize.Medium, "Lg");
                view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.ResetForPlay(Vector3.zero, 0f);
                yield return null; yield return null;
                var probe = new LegProbe(view.Avatar.Animator, view.Motion != null ? view.Motion.Plant : null); var acc = new LegProbe.Acc(); rows[g.Name] = acc;
                HumanPoseHandler hph = null; HumanPose hp = default(HumanPose); var overN = new Dictionary<int, int>(); var overMax = new Dictionary<int, float>(); int poseFrames = 0, anyOver = 0;
                try { hph = new HumanPoseHandler(view.Avatar.Animator.avatar, view.Avatar.Animator.transform); } catch { }
                Vector3 pos = Vector3.zero; float sp = 0f; var h = new AnimHints { Seed = 5, Side = 'C', Carrying = g.Carry };
                Vector3 dir = Quaternion.Euler(0f, g.Angle, 0f) * Vector3.forward;
                var views = new Dictionary<string, List<Texture2D>> { { "side", new List<Texture2D>() }, { "front", new List<Texture2D>() }, { "behind", new List<Texture2D>() } };
                int shot = 0;
                for (int f = 0; f < 480; f++)
                {
                    sp = Mathf.MoveTowards(sp, g.Speed, 7f * dt);
                    Vector3 vel = dir * sp; pos += vel * dt;
                    view.ApplyMatch(pos, vel, Vector3.forward, g.Action, h, 0f, false, false, dt);
                    yield return null;
                    if (f >= 150) probe.Sample(acc, view.Forward, vel, sp, view.transform.position.y);
                    if (f >= 150 && hph != null) { hph.GetHumanPose(ref hp); poseFrames++; bool any = false; for (int mi = 0; mi < hp.muscles.Length; mi++) if (Mathf.Abs(hp.muscles[mi]) > 1f && !MuscleReport.IsFinger(HumanTrait.MuscleName[mi])) { any = true; int cnt; overN.TryGetValue(mi, out cnt); overN[mi] = cnt + 1; float om; overMax.TryGetValue(mi, out om); overMax[mi] = Mathf.Max(om, Mathf.Abs(hp.muscles[mi])); } if (any) anyOver++; }
                    if (film && g.Film && f >= 260 && (f - 260) % 6 == 0 && shot < 12)
                    {
                        Vector3 aim = view.transform.position + Vector3.up * 0.78f; const float d = 3.1f;
                        foreach (var vw in new[] { "side", "front", "behind" })
                        {
                            Vector3 off = vw == "side" ? Vector3.left : (vw == "front" ? Vector3.forward : Vector3.back);
                            _cam.transform.position = aim + off * d; _cam.transform.rotation = Quaternion.LookRotation(aim - _cam.transform.position); _cam.fieldOfView = 36f;
                            views[vw].Add(ShotCam(300, 440));
                        }
                        shot++;
                    }
                }
                _muscleText += "| " + g.Name + " | " + poseFrames + " | " + anyOver + " | " + overN.Count + " | " + string.Join(", ", overN.OrderByDescending(x => x.Value).Take(4).Select(x => HumanTrait.MuscleName[x.Key] + " " + x.Value + " (max " + overMax[x.Key].ToString("0.00") + ")")) + " |\n";
                if (film && g.Film) foreach (var kv in views) Sheet(kv.Value, 6, _out + "/strip_legs_" + g.Name.Split(' ')[0] + (g.Carry ? "_carry" : "") + "_" + kv.Key + ".png");
                Destroy(view.gameObject); yield return null;
            }
            foreach (var kv in swapped) kv.Key.clip = kv.Value;
            _legText = "## leg quality (treadmill, final pose, frames 150-480; tag " + _tag + ")\n\n" + LegProbe.Table(rows) + "\n";
            _legText += "## humanoid muscles beyond +-1 in the final pose\n\n| case | frames | frames with any muscle beyond | muscles beyond | worst |\n|---|---|---|---|---|\n" + _muscleText + "\n";
            Destroy(root.gameObject);
        }

        // ---------------------------------------------------------------------------------------- plays
        string _playText = "";
        readonly Dictionary<string, Stats> _bands = new Dictionary<string, Stats>();

        GameState Scrim(int spot, int down, int dist)
        {
            var gs = new GameState(_rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = spot; gs.Down = down; gs.Distance = dist; gs.Possession = Team.Home;
            return gs;
        }

        List<KeyValuePair<string, Play>> PlayList()
        {
            var list = new List<KeyValuePair<string, Play>>();
            Func<Play, QbAssignment> qa = p => p.Assignments[0] as QbAssignment;
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && qa(p) != null).ToList();
            Action<string, Func<Play, bool>> add = (name, f) => { var p = offense.FirstOrDefault(f); if (p != null) list.Add(new KeyValuePair<string, Play>(name, p)); };
            add("handoff_shotgun", p => qa(p).Action == QbAction.Handoff && qa(p).Shotgun);
            add("handoff", p => qa(p).Action == QbAction.Handoff && !qa(p).Shotgun);
            add("drop_shotgun", p => (qa(p).Action == QbAction.DropbackPass || qa(p).Action == QbAction.QuickPass) && qa(p).Shotgun);
            add("drop5_undercenter", p => qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps == 5);
            add("rollout", p => qa(p).Action == QbAction.Rollout || qa(p).Action == QbAction.Boot);
            add("playaction", p => qa(p).Action == QbAction.PlayActionFake);
            return list;
        }

        PlaySimulation StartPlay(Play off, int seed)
        {
            var def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == _book.Formations.First(f => f.Side == PlaySide.Defense).Name);
            var sim = new PlaySimulation();
            sim.Setup(Scrim(35, 1, 10), off, def, null, null, _rules, new Rng(seed), new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f });
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
            return sim;
        }

        static string Band(float speed, bool carry)
        {
            string b = speed < 2f ? "walk <2" : speed < 3.5f ? "jog 2-3.5" : speed < 5f ? "run 3.5-5" : speed < 6.5f ? "run 5-6.5" : speed < 8f ? "sprint 6.5-8" : "sprint >8";
            return (carry ? "carry " : "free ") + b;
        }

        IEnumerator Plays()
        {
            const float dt = 1f / 60f;
            var dump = new StringBuilder("play,t,agent,pos,simSpeed,action,carry,clip,heelL_y,toeL_y,heelR_y,toeR_y,soleL,soleR,AL_x,AL_z,TL_x,TL_z,AR_x,AR_z,TR_x,TR_z,hips_y\n");
            int pi = 0;
            foreach (var pl in PlayList())
            {
                var sim = StartPlay(pl.Value, 77 + pi++);
                for (int f = 0; f < 90; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt);
                var logs = new Dictionary<int, List<Frm>>(); var cur = new Dictionary<int, List<Frm>>();
                float t = 0f;
                while (sim.Phase != PlayPhase.Dead && t < 7f)
                {
                    sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); t += dt;
                    yield return null;
                    for (int i = 0; i < 22; i++)
                    {
                        var a = sim.Agents[i]; var v = _stage.Views[i];
                        if (v == null || v.Avatar == null) continue;
                        float sp = a.Speed * 0.9144f;
                        bool valid = !v.ContactDriven && !a.IsDown && sp > 1.0f && v.Rig != null && v.Rig.Weight < 0.15f && !(v.Ragdoll != null && v.Ragdoll.Active) && (a.Action == AgentAction.Run || a.Action == AgentAction.Sprint || a.Action == AgentAction.Carry || a.Action == AgentAction.Route || a.Action == AgentAction.Idle || a.Action == AgentAction.Backpedal || a.Action == AgentAction.Shuffle);
                        if (valid)
                        {
                            if (!cur.ContainsKey(i)) cur[i] = new List<Frm>();
                            var fr = Capture(v, t, sp, a.HasBall); cur[i].Add(fr);
                            if (cur[i].Count % 2 == 0)
                                dump.AppendLine(pl.Key + "," + t.ToString("0.000") + "," + i + "," + a.PlayPos + "," + sp.ToString("0.00") + "," + a.Action + "," + (a.HasBall ? 1 : 0) + "," + fr.Clip + "," + fr.AL.y.ToString("0.000") + "," + fr.TL.y.ToString("0.000") + "," + fr.AR.y.ToString("0.000") + "," + fr.TR.y.ToString("0.000") + "," + fr.SoleL.ToString("0.000") + "," + fr.SoleR.ToString("0.000") + "," + fr.AL.x.ToString("0.000") + "," + fr.AL.z.ToString("0.000") + "," + fr.TL.x.ToString("0.000") + "," + fr.TL.z.ToString("0.000") + "," + fr.AR.x.ToString("0.000") + "," + fr.AR.z.ToString("0.000") + "," + fr.TR.x.ToString("0.000") + "," + fr.TR.z.ToString("0.000") + "," + fr.Hips.ToString("0.000"));
                        }
                        else if (cur.ContainsKey(i)) { Flush(i, cur, logs, dt); }
                    }
                }
                foreach (var key in cur.Keys.ToList()) Flush(key, cur, logs, dt);
                _stage.End();
                yield return null;
                _report.AppendLine("play " + pl.Key + " ended " + sim.EndReason + " at " + t.ToString("0.0") + " s");
            }
            File.WriteAllText(_out + "/motion_dump.csv", dump.ToString());
            var sb = new StringBuilder("## real plays (every running player of " + PlayList().Count + " plays; stretches of at least 0.5 s of plain locomotion, split by the simulated ground speed)\n\n" + Head + "\n");
            foreach (var kv in _bands.OrderBy(k => k.Key)) sb.AppendLine(Row(kv.Key, kv.Value));
            var all = new Stats(); foreach (var kv in _bands) if (kv.Key.Contains("run") || kv.Key.Contains("sprint")) all.Add(kv.Value);
            sb.AppendLine(Row("ALL run + sprint", all));
            _playText = sb.ToString();
        }

        void Flush(int i, Dictionary<int, List<Frm>> cur, Dictionary<int, List<Frm>> logs, float dt)
        {
            var l = cur[i]; cur.Remove(i);
            if (l.Count < 36) return;
            // split a stretch whose speed changes a lot (acceleration) into 0.6 s pieces by band so every piece has one speed
            int p = 0;
            while (p + 30 <= l.Count)
            {
                int q = p + 1; float s0 = l[p].Speed;
                while (q < l.Count && Mathf.Abs(l[q].Speed - s0) < Mathf.Max(0.5f, 0.1f * s0) && q - p < 240) q++;
                if (q - p >= 30)
                {
                    var piece = l.GetRange(p, q - p);
                    string band = Band(piece.Average(x => x.Speed), piece[0].Carry);
                    Stats s; if (!_bands.TryGetValue(band, out s)) _bands[band] = s = new Stats();
                    s.Add(Measure(piece, dt));
                }
                p = Math.Max(q, p + 1);
            }
        }

        // ---------------------------------------------------------------------------------------- strips
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

        static void Sheet(List<Texture2D> tiles, int cols, string path)
        {
            if (tiles.Count == 0) return;
            int w = tiles[0].width, h = tiles[0].height, rows = (tiles.Count + cols - 1) / cols;
            var tex = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Count; i++) { int cx = i % cols, cy = rows - 1 - i / cols; tex.SetPixels(cx * w, cy * h, w, h, tiles[i].GetPixels()); Destroy(tiles[i]); }
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
        }

        void Aim(SimAgentView v, PlayFrame frame, PlaySimulation sim, bool side)
        {
            Vector3 q = v.transform.position;
            Vector3 d = frame.DirToUnity(new Vec2(0f, 1f)); if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            Vector3 r = Vector3.Cross(Vector3.up, d);
            if (side)
            {
                _cam.transform.position = q - r * 6.5f + Vector3.up * 1.2f + d * 0.4f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 0.8f + d * 0.4f - _cam.transform.position); _cam.fieldOfView = 30f;
            }
            else
            {
                // the game camera: behind and above the offence looking downfield, close enough to read the feet
                Vector3 fd = frame.DirToUnity(new Vec2(0f, 1f));
                _cam.transform.position = q - fd * 8.5f + Vector3.up * 4.2f; _cam.transform.rotation = Quaternion.LookRotation(q + fd * 2.0f + Vector3.up * 0.5f - _cam.transform.position); _cam.fieldOfView = 34f;
            }
        }

        IEnumerator Strips()
        {
            const float dt = 1f / 60f;
            foreach (var spec in new[] { new { Name = "rb_sprint", Play = "handoff_shotgun", Pos = Position.RB, T0 = 1.9f, Min = 5.5f }, new { Name = "wr_run", Play = "drop_shotgun", Pos = Position.WR, T0 = 0.8f, Min = 3.0f } })
            {
                var pl = PlayList().FirstOrDefault(x => x.Key == spec.Play); if (pl.Value == null) continue;
                var sim = StartPlay(pl.Value, 91);
                var frame = _stage.Frame;
                for (int f = 0; f < 90; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt);
                int who = -1; float t = 0f;
                var game = new List<Texture2D>(); var side = new List<Texture2D>(); float next = -1f; int shots = 0;
                while (sim.Phase != PlayPhase.Dead && t < 6f && shots < 12)
                {
                    sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); t += dt;
                    yield return null;
                    if (who < 0 && t >= spec.T0)
                    {
                        // the fastest player of the position that is running plainly right now
                        float best = 0f;
                        for (int i = 0; i < 22; i++)
                        {
                            var a = sim.Agents[i]; if (a.PlayPos != spec.Pos) continue;
                            var v = _stage.Views[i]; if (v == null || v.ContactDriven || v.Rig.Weight > 0.15f) continue;
                            float sp = a.Speed * 0.9144f; if (sp > best && (spec.Pos != Position.RB || a.HasBall || sp > spec.Min)) { best = sp; who = i; }
                        }
                        if (best < spec.Min) who = -1; else next = t;
                    }
                    if (who >= 0 && t >= next)
                    {
                        var v = _stage.Views[who];
                        Aim(v, frame, sim, false); game.Add(ShotCam(560, 420));
                        Aim(v, frame, sim, true); side.Add(ShotCam(560, 420));
                        next += 0.1f; shots++;
                        _report.AppendLine("strip " + spec.Name + " t=" + t.ToString("0.00") + " speed " + (sim.Agents[who].Speed * 0.9144f).ToString("0.0") + " m/s clip " + v.Rig.CurrentClipName + " gait " + (v.Gait != null ? v.Gait.DebugCycles + " layer " + v.Gait.Layer.ToString("0.00") + " warp " + v.Gait.Warp.ToString("0.00") : "-"));
                    }
                }
                Sheet(game, 4, _out + "/strip_" + spec.Name + "_game.png");
                Sheet(side, 4, _out + "/strip_" + spec.Name + "_side.png");
                _stage.End();
                yield return null;
            }
            // treadmill film: a jog and a sprint from the side, 12 frames every 0.1 s
            var root = new GameObject("TmFilm").transform;
            foreach (var spec in new[] { new { Name = "jog_3p2", Speed = 3.2f, Carry = false, Act = AgentAction.Run }, new { Name = "sprint_7p5", Speed = 7.5f, Carry = false, Act = AgentAction.Sprint }, new { Name = "carry_5p5", Speed = 5.5f, Carry = true, Act = AgentAction.Carry } })
            {
                var view = SimAgentView.CreateForMatch(root, PlayerSize.Medium, "Tm");
                view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.ResetForPlay(Vector3.zero, 0f);
                yield return null; yield return null;
                Vector3 pos = Vector3.zero; float sp = 0f; var h = new AnimHints { Seed = 5, Side = 'C', Carrying = spec.Carry };
                var tiles = new List<Texture2D>(); int shot = 0;
                for (int f = 0; f < 400 && shot < 12; f++)
                {
                    sp = Mathf.MoveTowards(sp, spec.Speed, 7f * dt); Vector3 vel = Vector3.forward * sp; pos += vel * dt;
                    view.ApplyMatch(pos, vel, Vector3.forward, spec.Act, h, 0f, false, false, dt);
                    yield return null;
                    if (f >= 200 && (f - 200) % 6 == 0)
                    {
                        _cam.transform.position = pos + Vector3.left * 4.0f + Vector3.up * 0.9f; _cam.transform.rotation = Quaternion.LookRotation(pos + Vector3.up * 0.75f - _cam.transform.position); _cam.fieldOfView = 34f;
                        tiles.Add(ShotCam(560, 420)); shot++;
                    }
                }
                Sheet(tiles, 4, _out + "/strip_treadmill_" + spec.Name + "_side.png");
                Destroy(view.gameObject); yield return null;
            }
            Destroy(root.gameObject);
        }
    }
}
