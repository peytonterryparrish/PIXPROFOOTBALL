using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 MOTION: builds the locomotion motion database (<see cref="MotionDb"/>) from the selected locomotion clips. Every source frame is sampled on the match body (30 fps, root motion applied), its features are
    /// computed in the root frame of that frame (feet, foot velocities, root velocity, planned-path-like trajectory at +0.33 / +0.66 / +1.0 s from the clip's own root motion), the foot-contact tags are derived
    /// from the sole heights, and the features are normalised and weighted. Run: -executeMethod PixFootball.EditorTools.MotionDbBuilder.Build  (also writes Logs/motion/db_report.txt).
    /// </summary>
    public static class MotionDbBuilder
    {
        /// <summary>Weights of the 21 features (after normalisation by the standard deviation over all rows).</summary>
        static readonly float[] Weights =
        {
            1.0f, 1.0f, 1.0f, 1.0f, 0.7f, 0.7f,       // ankle x z, height
            0.6f, 0.6f, 0.6f, 0.6f,                   // ankle velocity
            3.0f, 3.0f,                               // root velocity
            1.5f, 1.5f, 1.5f, 1.5f, 1.5f, 1.5f,       // trajectory
            0.8f, 0.8f, 0.8f,                         // facing change
        };
        static readonly float[] MinStd = { 0.05f, 0.05f, 0.05f, 0.05f, 0.03f, 0.03f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 8f, 8f, 8f };

        /// <summary>Clips that go into the database (file name without extension) and their role flags. Everything else in the folder (goalkeeper, soccer, football actions) belongs to the action systems.</summary>
        public sealed class Spec { public string Name; public int Kind; public bool ForceOneShot; public float SpeedScale = 0f; public bool Lib; }

        public static List<Spec> Specs()
        {
            var L = new List<Spec>();
            Func<string, int, Spec> add = (n, k) => { var s = new Spec { Name = n, Kind = k }; L.Add(s); return s; };
            const int S = (int)MotionDb.Kind.Strafe, B = (int)MotionDb.Kind.Back, T = (int)MotionDb.Kind.Turn, St = (int)MotionDb.Kind.Start, Sp = (int)MotionDb.Kind.Stop, C = (int)MotionDb.Kind.Cut;
            // forward gaits: only takes whose measured flight fraction is low (jog / run <= 0.2, sprint <= 0.4); the bounding ones (Sprint_Standard 0.59, Run_Slow_Run 0.57, Jog_Runningg 0.55, Jog_Runningf 0.41, Jog_Jogging 0.37, Run_Standard_Run 0.30) are left out
            foreach (var n in new[] { "Jog_Running", "Jog_Runningk", "Run_Jog_Forward", "Run_Medium_Run", "Run_Fast_Run" }) add(n, 0);      // Run_Two_Cycle_Sprint (flight 0.39 in the scan, 0.44 in play) is out: Run_Fast_Run (0.18 / 0.25) covers the sprint with cadence and stride
            add("Armature|PIX_Walk_Cycle", 0).Lib = true;      // the walk of the existing library (authored for the turned body model: legacyConv); Mixamo has no walk in the set
            foreach (var n in new[] { "Run_Jog_Backward", "Run_Running_Backward", "Run_Jog_Backward_Diagonal" }) add(n, B);
            foreach (var n in new[] { "Jog_Jog_Strafe_Left", "Jog_Jog_Strafe_Right", "Jog_Left_Strafe_2", "Jog_Right_Strafe", "Run_Jog_Forward_Diagonal", "Jog_Jog_Forward_Diagonal_2" }) add(n, S);
            // one-shot starts / stops / turns are OUT: their root turning fights the simulated yaw (planted slide 2-3.4 m/s against 0.5-1.3 for the loops); stops and cuts are the loops with the planned path, the stride warp, the orientation warp and the foot lock


            return L;
        }

        /// <summary>Steady loops (runs, jogs, strafes, back-pedals) bake the body's yaw sway into the pose; starts, stops and turns keep their turning in the root transform.</summary>
        public static bool BakesRotation(string name)
        {
            return false;
        }

        public const string CalibPath = "D:/PIXFootball/anim_loco/mm_calibration.csv";
        static readonly Dictionary<string, float> Calib = new Dictionary<string, float>();

        struct Pose2 { public Vector2 P; public float Yaw; }

        static Vector2 Rot(Vector2 v, float deg) { float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c); }       // world xz -> local (x right, y forward) for a root with yaw deg

        public static void Build()
        {
            BatchWatchdog.Arm(25);
            Directory.CreateDirectory("Logs/motion");
            AssetDatabase.Refresh(); ArtLibrary.ResetCache(); ArtLibrary.Load();
            var rig = AvRig.Create(PlayerSize.Medium); if (rig == null) { Debug.LogError("[MotionDb] no rig"); return; }
            var plant = new FootPlant(rig.A);
            var db = ScriptableObject.CreateInstance<MotionDb>();
            db.refLeg = rig.Leg;
            var rows = new List<float[]>(); var vel = new List<float>(); var yawr = new List<float>(); var contact = new List<byte>(); var clipOf = new List<int>();
            var report = new StringBuilder();
            string only = LocoCycleScan.CmdArg("-pixMmOnly", "");
            Calib.Clear();
            if (LocoCycleScan.CmdArg("-pixMmCalib", "0") == "1" && File.Exists(CalibPath)) foreach (var ln in File.ReadAllLines(CalibPath).Skip(1)) { var p = ln.Split(','); float t; if (p.Length >= 3 && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out t)) Calib[p[0]] = t; }
            // humanoid muscles inside human range: re-zero the left arm chain, soft-limit the rest (MotionClean)
            bool noClean = LocoCycleScan.CmdArg("-pixMmNoClean", "0") == "1"; int cleanBefore = 0, cleanAfter = 0;
            var allClips = new List<AnimationClip>(); foreach (var sp in Specs()) if (!sp.Lib) { var cc = MixamoImport.LoadClip(MixamoImport.Folder + "/" + sp.Name + ".fbx"); if (cc != null) allClips.Add(cc); }
            var deltas = MotionClean.Deltas(MuscleReport.Collect(allClips)); report.AppendLine("muscle re-zero: " + string.Join(", ", deltas.Select(kv => kv.Key + " " + kv.Value.ToString("+0.00;-0.00"))));
            foreach (var spec in Specs())
            {
                if (only.Length > 0 && !only.Split(',').Contains(spec.Name)) continue;
                string path = MixamoImport.Folder + "/" + spec.Name + ".fbx";
                var clip = spec.Lib ? LocoCycleBuilder.FindExact(spec.Name) : MixamoImport.LoadClip(path);
                if (clip == null) { Debug.LogWarning("[MotionDb] missing " + path); continue; }
                if (!spec.Lib && !noClean) { int cb, ca; clip = MotionClean.Clean(clip, spec.Name, deltas, out cb, out ca); cleanBefore += cb; cleanAfter += ca; }
                AddClip(db, rig, plant, clip, spec, rows, vel, yawr, contact, clipOf, report);
                if (!spec.Lib && (spec.Kind & ((int)MotionDb.Kind.Start | (int)MotionDb.Kind.Stop | (int)MotionDb.Kind.Turn | (int)MotionDb.Kind.Cut)) == 0 && LocoCycleScan.CmdArg("-pixMmNoMirror", "0") != "1")
                {
                    int orig = db.clips.Count - 1; var tw = MakeMirror(clip, spec.Name);
                    AddClip(db, rig, plant, tw, new Spec { Name = spec.Name + "_M", Kind = spec.Kind }, rows, vel, yawr, contact, clipOf, report);
                    db.clips[db.clips.Count - 1].mirrorOf = orig; db.clips[orig].mirrorOf = db.clips.Count - 1;
                }
            }
            // normalise
            int n = rows.Count, D = MotionDb.Dim;
            var mean = new float[D]; var std = new float[D];
            for (int i = 0; i < D; i++)
            {
                double m = 0; for (int r = 0; r < n; r++) m += rows[r][i]; m /= Math.Max(1, n);
                double v = 0; for (int r = 0; r < n; r++) { double d = rows[r][i] - m; v += d * d; }
                mean[i] = (float)m; std[i] = Mathf.Max(MinStd[i], (float)Math.Sqrt(v / Math.Max(1, n)));
            }
            db.mean = mean; db.scale = new float[D];
            for (int i = 0; i < D; i++) db.scale[i] = Weights[i] / std[i];
            db.feat = new float[n * D];
            for (int r = 0; r < n; r++) for (int i = 0; i < D; i++) db.feat[r * D + i] = (rows[r][i] - mean[i]) * db.scale[i];
            db.vel = vel.ToArray(); db.yawRate = yawr.ToArray(); db.contact = contact.ToArray(); db.clipOf = clipOf.ToArray();
            string asset = MotionDb.EditorPath;
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            AssetDatabase.DeleteAsset(asset);
            AssetDatabase.CreateAsset(db, asset);
            AssetDatabase.SaveAssets();
            report.AppendLine("muscle samples beyond the limit: " + cleanBefore + " before, " + cleanAfter + " after cleaning");
            report.AppendLine("rows " + n + " clips " + db.clips.Count + " std " + string.Join(" ", std.Select(x => x.ToString("0.00"))));
            File.WriteAllText("Logs/motion/db_report.txt", report.ToString());
            rig.Dispose();
            Debug.Log("[MotionDb] built " + db.clips.Count + " clips, " + n + " rows");
        }

        /// <summary>The ground velocity (root frame, m/s) the planted feet say the take moves at: the feet stand still in the world, so the body moves over them at minus their velocity relative to the root. Sampled at 120 Hz over the middle 40 percent of every contact (the touch-down and lift-off frames skid).</summary>
        /// <summary>A left / right mirrored twin of a take (the humanoid mirror flag of the clip: the same muscle curves swapped left for right). The two feet of a mocap take are never alike (one lands flatter, one turns out more); with the twin in the database the matcher has both, so no side of the body is the one that always plays the worse foot.</summary>
        static AnimationClip MakeMirror(AnimationClip clip, string name)
        {
            string dir = MixamoImport.Folder + "/Mirrored";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(MixamoImport.Folder, "Mirrored");
            string path = dir + "/" + name + "_M.anim";
            AssetDatabase.DeleteAsset(path); AnimationClip tw = null;      // always rebuilt from the (cleaned) source
            if (tw == null) { tw = UnityEngine.Object.Instantiate(clip); tw.name = name + "_M"; AssetDatabase.CreateAsset(tw, path); }
            var set = AnimationUtility.GetAnimationClipSettings(tw); set.mirror = true; set.loopTime = false; AnimationUtility.SetAnimationClipSettings(tw, set);
            EditorUtility.SetDirty(tw); AssetDatabase.SaveAssets();
            return tw;
        }

        /// <summary>Third percentile of one foot's sole height over the take: the height its sole stands on when it is down (the two feet of a retargeted take differ by a few centimetres).</summary>
        static float Pct3(float[] a) { var l = new List<float>(a); l.Sort(); return l[Mathf.Clamp((int)(0.03f * l.Count), 0, l.Count - 1)]; }

        static bool NativeVelocity(AvRig rig, FootPlant plant, AnimationClip clip, Vector2 travelLocal, float rootRef, float bodyYaw, out Vector2 v, out int runs)
        {
            v = Vector2.zero; runs = 0;
            var fr = MixamoScan.Sample(rig, clip, null, 120f, bodyYaw); int N = fr.Count - 1; const float dt = 1f / 120f;
            var lc = new float[N + 1]; var rc = new float[N + 1];
            for (int i = 0; i <= N; i++) { lc[i] = Mathf.Min(fr[i].LT.y - plant.RestToeL, fr[i].LA.y - plant.RestAnkleL); rc[i] = Mathf.Min(fr[i].RT.y - plant.RestToeR, fr[i].RA.y - plant.RestAnkleR); }
            var lows = new List<float>(); for (int i = 0; i <= N; i++) lows.Add(Mathf.Min(lc[i], rc[i])); lows.Sort();
            float floor = lows[Mathf.Clamp((int)(0.03f * lows.Count), 0, lows.Count - 1)]; float floorL = Pct3(lc), floorR = Pct3(rc);
            Vector2 sum = Vector2.zero; int cnt = 0;
            for (int foot = 0; foot < 2; foot++)
            {
                var h = foot == 0 ? lc : rc; bool on = false; int start = 0; float fl = foot == 0 ? floorL : floorR;
                for (int i = 0; i <= N + 1; i++)
                {
                    bool c = i <= N && (on ? h[i] - fl < 0.07f : h[i] - fl < 0.04f);
                    if (c && rootRef > 1.5f && i >= 1 && i < N) { Vector3 q1 = foot == 0 ? (fr[i + 1].LA + fr[i + 1].LT) * 0.5f : (fr[i + 1].RA + fr[i + 1].RT) * 0.5f, q0 = foot == 0 ? (fr[i - 1].LA + fr[i - 1].LT) * 0.5f : (fr[i - 1].RA + fr[i - 1].RT) * 0.5f; Vector2 tl = travelLocal.normalized; float vv = (new Vector2(q1.x - q0.x, q1.z - q0.z) / (2f * dt)).x * tl.x + (new Vector2(q1.x - q0.x, q1.z - q0.z) / (2f * dt)).y * tl.y; if (vv > -0.28f * rootRef) c = false; }
                    if (c && !on) { on = true; start = i; }
                    else if (!c && on)
                    {
                        on = false; int len = i - start;
                        if (len >= 8)
                        {
                            runs++;
                            int a = start + (int)(len * 0.3f), b = i - 1 - (int)(len * 0.3f);
                            for (int k = Mathf.Max(a, 1); k <= Mathf.Min(b, N - 1); k++)
                            {
                                Vector3 p1 = foot == 0 ? (fr[k + 1].LA + fr[k + 1].LT) * 0.5f : (fr[k + 1].RA + fr[k + 1].RT) * 0.5f, p0 = foot == 0 ? (fr[k - 1].LA + fr[k - 1].LT) * 0.5f : (fr[k - 1].RA + fr[k - 1].RT) * 0.5f;
                                sum += -new Vector2(p1.x - p0.x, p1.z - p0.z) / (2f * dt); cnt++;
                            }
                        }
                    }
                }
            }
            if (cnt < 3) return false;
            v = sum / cnt; return true;
        }

        static void AddClip(MotionDb db, AvRig rig, FootPlant plant, AnimationClip clip, Spec spec, List<float[]> rows, List<float> vel, List<float> yawr, List<byte> contact, List<int> clipOf, StringBuilder report)
        {
            float by = spec.Lib ? PlayerAvatar.ModelYaw : 0f;      // takes of the older library are authored for a body model turned by ModelYaw
            var fr = MixamoScan.Sample(rig, clip, null, MotionDb.Fps, by);
            int N = fr.Count - 1; const float dt = 1f / MotionDb.Fps;
            var sc = MixamoScan.Analyze(rig, plant, clip, false, null, by);
            bool seam = sc.Seam < 0.06f && !spec.ForceOneShot && (spec.Kind & ((int)MotionDb.Kind.Start | (int)MotionDb.Kind.Stop | (int)MotionDb.Kind.Turn | (int)MotionDb.Kind.Cut)) == 0;
            int count = seam ? N : N + 1;       // a loop drops the closing duplicate frame
            // world root track
            var pos = new Vector2[N + 1]; var yaw = new float[N + 1];
            for (int i = 0; i <= N; i++) { pos[i] = new Vector2(fr[i].Root.x, fr[i].Root.z); yaw[i] = fr[i].Yaw; }
            float dyaw = Mathf.DeltaAngle(yaw[0], yaw[N]);
            // unwrapped yaw
            var uy = new float[N + 1]; uy[0] = yaw[0]; for (int i = 1; i <= N; i++) uy[i] = uy[i - 1] + Mathf.DeltaAngle(yaw[i - 1], yaw[i]);
            // loop transform: W(p) = Rot(dth) p + t
            float dth = uy[N] - uy[0];
            Func<Vector2, float, Vector2> rotw = (v, d) => { float r = d * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c); };      // Unity yaw: forward = (sin, cos)
            Vector2 tLoop = pos[N] - rotw(pos[0], dth);
            Vector2 vMean = (pos[N] - pos[0]) / (N * dt);
            Func<int, Pose2> at = j =>
            {
                if (seam) return new Pose2 { P = pos[0] + vMean * (j * dt), Yaw = uy[0] + dth / N * j };      // a steady loop travels in a straight line at its mean velocity: no sway in the trajectory
                if (j < 0) j = 0;
                if (j <= N) return new Pose2 { P = pos[j], Yaw = uy[j] };
                int a = Mathf.Max(0, N - 3); Vector2 v = (pos[N] - pos[a]) / (N - a); float yr = (uy[N] - uy[a]) / (N - a);
                return new Pose2 { P = pos[N] + v * (j - N), Yaw = uy[N] + yr * (j - N) };
            };
            // contact tags and floor
            var lc = new float[N + 1]; var rc = new float[N + 1];
            for (int i = 0; i <= N; i++)
            {
                lc[i] = Mathf.Min(fr[i].LT.y - plant.RestToeL, fr[i].LA.y - plant.RestAnkleL); rc[i] = Mathf.Min(fr[i].RT.y - plant.RestToeR, fr[i].RA.y - plant.RestAnkleR);
            }
            var lows = new List<float>(); for (int i = 0; i <= N; i++) lows.Add(Mathf.Min(lc[i], rc[i])); lows.Sort();
            float floor = lows[Mathf.Clamp((int)(0.03f * lows.Count), 0, lows.Count - 1)]; float floorL = Pct3(lc), floorR = Pct3(rc);
            var cl = new bool[N + 1]; var cr = new bool[N + 1]; bool sl = false, sr = false;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i <= N; i++)
                {
                    float hl = lc[i] - floorL, hr = rc[i] - floorR;
                    sl = sl ? hl < 0.07f : hl < 0.04f; sr = sr ? hr < 0.07f : hr < 0.04f;
                    if (pass == 1) { cl[i] = sl; cr[i] = sr; }
                }
            // a foot that skims the turf while it swings forward is not in contact: during a stance the foot moves BACKWARD relative to the body (along the travel direction) at a good share of the ground speed
            if (seam && vMean.magnitude > 1.5f)
            {
                Vector2 dl = Rot(vMean, at(0).Yaw).normalized; float thr = -0.28f * vMean.magnitude;
                for (int i = 0; i <= N; i++)
                {
                    int a = seam ? (i - 1 + N) % N : Mathf.Max(i - 1, 0), b = seam ? (i + 1) % N : Mathf.Min(i + 1, N);
                    Vector3 l1 = (fr[b].LA + fr[b].LT) * 0.5f, l0 = (fr[a].LA + fr[a].LT) * 0.5f, r1 = (fr[b].RA + fr[b].RT) * 0.5f, r0 = (fr[a].RA + fr[a].RT) * 0.5f;
                    float span = 2f * dt;
                    float vl = (new Vector2(l1.x - l0.x, l1.z - l0.z) / span).x * dl.x + (new Vector2(l1.x - l0.x, l1.z - l0.z) / span).y * dl.y;
                    float vr = (new Vector2(r1.x - r0.x, r1.z - r0.z) / span).x * dl.x + (new Vector2(r1.x - r0.x, r1.z - r0.z) / span).y * dl.y;
                    if (vl > thr) cl[i] = false; if (vr > thr) cr[i] = false;
                }
            }
            // speed scale: the feet's idea of the ground speed against the root motion's
            float scale = spec.SpeedScale > 0f ? spec.SpeedScale : 1f;
            int planted = cl.Count(x => x) + cr.Count(x => x);
            Vector2 nat; int natRuns = 0; bool haveNat = NativeVelocity(rig, plant, clip, Rot(vMean, at(0).Yaw), vMean.magnitude, by, out nat, out natRuns);
            float rootMean = vMean.magnitude;
            if (spec.SpeedScale <= 0f && seam && haveNat && natRuns >= 2 && nat.magnitude > 0.8f && rootMean > 0.8f) scale = Mathf.Clamp(nat.magnitude / rootMean, 0.5f, 1.3f);      // the take moves at the speed its planted feet say (stride warp and playback rate are measured against it), not at the root motion speed
            { float tr; if (seam && Calib.TryGetValue(spec.Name, out tr)) scale = Mathf.Clamp(scale / tr, 0.4f, 1.6f); }      // learned on the treadmill (LocoShots mmlearn)
            _ = planted;
            var ci = new MotionDb.Clip { name = spec.Name, clip = clip, first = rows.Count, count = count, loop = seam, kind = spec.Kind | (seam ? (int)MotionDb.Kind.Loop : 0) };
            int clipIndex = db.clips.Count;
            // per-row features
            float sumSpeed = 0f; Vector2 sumVel = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                var f = new float[MotionDb.Dim];
                Func<int, int> wrap = j => seam ? ((j % N) + N) % N : Mathf.Clamp(j, 0, N);
                var fi = fr[i];
                float h(float v) { return v; }
                f[0] = fi.LA.x; f[1] = fi.LA.z; f[2] = fi.RA.x; f[3] = fi.RA.z;
                f[4] = lc[i] - floorL; f[5] = rc[i] - floorR;
                var fa = fr[wrap(i + 1)]; var fb = fr[wrap(i - 1)];
                int dtFrames = seam || (i > 0 && i < N) ? 2 : 1;
                f[6] = (fa.LA.x - fb.LA.x) / (dtFrames * dt); f[7] = (fa.LA.z - fb.LA.z) / (dtFrames * dt); f[8] = (fa.RA.x - fb.RA.x) / (dtFrames * dt); f[9] = (fa.RA.z - fb.RA.z) / (dtFrames * dt);
                // root velocity in the root frame of this frame
                int ia = seam ? i - 1 : Mathf.Max(i - 1, 0), ib = seam ? i + 1 : Mathf.Min(i + 1, N);
                var pm = at(ia); var pp = at(ib);
                Vector2 vw = (pp.P - pm.P) / ((ib - ia) * dt);
                Vector2 vl = Rot(vw, at(i).Yaw) * scale;
                f[10] = vl.x; f[11] = vl.y;
                var p0 = at(i);
                for (int k = 0; k < 3; k++)
                {
                    var pk = at(i + Mathf.RoundToInt(MotionDb.Horizon[k] * MotionDb.Fps));
                    Vector2 d = Rot(pk.P - p0.P, p0.Yaw) * scale;
                    f[12 + 2 * k] = d.x; f[13 + 2 * k] = d.y; f[18 + k] = pk.Yaw - p0.Yaw;
                }
                rows.Add(f);
                vel.Add(vl.x); vel.Add(vl.y);
                float yrate = (pp.Yaw - pm.Yaw) / ((ib - ia) * dt); yawr.Add(yrate);
                contact.Add((byte)((cl[i] ? 1 : 0) | (cr[i] ? 2 : 0)));
                clipOf.Add(clipIndex);
                sumSpeed += vl.magnitude; sumVel += vl;
            }
            ci.legacyConv = spec.Lib;
            ci.speed = sumSpeed / Mathf.Max(1, count);
            ci.angle = sumVel.magnitude > 0.2f * Mathf.Max(0.01f, sumSpeed) ? Mathf.Atan2(sumVel.x, sumVel.y) * Mathf.Rad2Deg : 0f;
            ci.turnRate = dth / (N * dt);
            ci.speedScale = scale; ci.floorY = Mathf.Clamp(floor, -0.05f, 0.16f);
            ci.stepsPerSec = sc.SteppsPerSec;
            db.clips.Add(ci);
            report.AppendLine(spec.Name.PadRight(40) + " rootMean " + rootMean.ToString("0.00") + " native " + (haveNat ? nat.magnitude.ToString("0.00") : "-") + " runs " + natRuns + " scale " + scale.ToString("0.00") + " frames " + count + (seam ? " loop" : " once") + " speed " + ci.speed.ToString("0.00") + " angle " + ci.angle.ToString("0") + " turn " + ci.turnRate.ToString("0") + " floor " + floor.ToString("0.000") + " contactL " + cl.Count(x => x) + " contactR " + cr.Count(x => x));
        }
    }
}
