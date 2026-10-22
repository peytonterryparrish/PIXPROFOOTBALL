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
    /// v0.9.0 LOCOMOTION (LOCO-002): measures a curated list of gait clips and writes the speed-banded cycle library (<see cref="LocoCycleLibrary"/>).
    /// Per clip: the cycle window (starts on the left foot's plant), the native ground speed (the speed at which the planted foot stands still in the world, measured on the stance frames), the travel direction,
    /// the stride and the gait phase of the four foot events. Run: -executeMethod PixFootball.EditorTools.LocoCycleBuilder.Report  (writes Logs/loco/report.txt)  or .Build (also writes the asset).
    /// </summary>
    public static class LocoCycleBuilder
    {
        // (clip name, carries the ball). The names are the library clips of Animations/Clean.
        static readonly string[] Free =
        {
            "Armature|PIX_Walk_Cycle", "JogCycleJR", "JogCycleLeon_1", "CornerbackShuffleFwdPreplay", "BGS__BGS_CB_ShuffleForward", "BGS__BGS_Run08_F_Loop", "RunCycleLeon_1", "BGS__BGS_Run07_F_Loop", "BGS__BGS_Run_F_Loop", "RunCycleLeon_2", "RunCycleJR_1", "RunCycleJR_2",
            "SprintCycleLeon_2", "BGS__BGS_Sprint08_F_Loop", "SprintCycleJR_1", "SprintCycleLeon_1", "SprintCycleJR_2", "BGS__BGS_Sprint09_F_Loop", "BGS__BGS_CB_SprintForward_Loop", "BGS__BGS_Sprint04_F_Loop_BC",
            "BGS__BGS_Run07_FL_Loop_BC", "BGS__BGS_Run07_FR_Loop_BC", "BGS__BGS_Sprint08_FL_Loop", "BGS__BGS_Sprint08_FR_Loop", "BGS__BGS_Sprint09_FL_Loop", "BGS__BGS_Sprint09_FR_Loop", "BGS__BGS_Sprint_FL_Loop", "BGS__BGS_Sprint_FR_Loop", "BGS__BGS_CB_SprintFoRight_Loop", "BGS__BGS_CB_ShuffleFoLeft",
            "BGS__MOB1_Jog_B_Loop_REMAP.001", "BGS__MOB1_Jog_BL_BkPd_Loop_REMAP", "BGS__MOB1_Jog_BR_BkPd_Loop_REMAP", "BGS__BGS_CB_ShuffleBack", "BGS__BGS_CB_ShuffleBackLeft", "BGS__BGS_CB_ShuffleBackRight",
            "BGS__BGS_CB_ShuffleLeft", "BGS__BGS_CB_ShuffleRight", "slow_shuffle_loop_090_001", "slow_shuffle_loop_270_001",
        };
        static readonly string[] Carry =
        {
            "BGS__MOB1_Jog_F_Loop_BC", "BGS__BGS_Run_F_Loop_BC", "BGS__MOB1_Run_F_Loop_BC", "BGS__BGS_Run07_F_Loop_BC", "BGS__BGS_Sprint08_F_Loop_BC", "BGS__BGS_Sprint09_F_Loop_BC", "BGS__BGS_Sprint04_F_Loop_BC",
            "BGS__MOB1_Jog_FL_Loop_BC", "BGS__MOB1_Jog_FR_Loop_BC", "BGS__MOB1_Run_FL_Loop_BC", "BGS__MOB1_Run_FR_Loop_BC", "BGS__BGS_Sprint08_FL_Loop_BC", "BGS__BGS_Sprint08_FR_Loop_BC", "BGS__BGS_Sprint09_FL_Loop_BC", "BGS__BGS_Sprint09_FR_Loop_BC", "BGS__BGS_Sprint_FL_Loop_BC", "BGS__BGS_Sprint_FR_Loop_BC",
            "BGS__MOB1_Jog_B_Loop_BC", "BGS__MOB1_Jog_BL_BkPd_Loop_BC", "BGS__MOB1_Jog_BR_BkPd_Loop_BC", "BGS__MOB1_Jog_L_Loop_BC", "BGS__MOB1_Jog_R_Loop_BC", "BGS__MOB1_Run_L_Loop_BC", "BGS__MOB1_Run_R_Loop_BC",
        };

        public static AnimationClip FindExact(string name)
        {
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { LocoCycleScan.CleanFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (c != null && c.name == name) return c;
            }
            return null;
        }

        public sealed class Result { public LocoCycleLibrary.Cycle C; public string Diag; public bool Ok; }

        public static Result Analyze(AvRig rig, AnimationClip clip, bool carry)
        {
            var res = new Result { C = new LocoCycleLibrary.Cycle(), Ok = false };
            var c = res.C; c.name = clip.name; c.clip = clip; c.carry = carry;
            var s = rig.Sample(clip, LocoCycleScan.Rate);
            const float dt = 1f / LocoCycleScan.Rate;
            int N = s.Count - 1;
            float psi = LocoCycleScan.MeanHeading(s); c.heading = psi;
            var inv = Quaternion.Euler(0f, -psi, 0f);
            var hL = new float[N]; var hR = new float[N];
            var fL = new Vector2[N]; var fR = new Vector2[N];       // sole midpoint relative to the root, heading frame (x right, y forward)
            for (int i = 0; i < N; i++)
            {
                float ry = s[i].Root.y;
                hL[i] = Mathf.Min(s[i].LA.y - ry - rig.AnkleRest, s[i].LT.y - ry - rig.ToeRest);
                hR[i] = Mathf.Min(s[i].RA.y - ry - rig.AnkleRest, s[i].RT.y - ry - rig.ToeRest);
                Vector3 l = inv * ((s[i].LA + s[i].LT) * 0.5f - s[i].Root), r = inv * ((s[i].RA + s[i].RT) * 0.5f - s[i].Root);
                fL[i] = new Vector2(l.x, l.z); fR[i] = new Vector2(r.x, r.z);
            }
            Func<float[], float> low = h => { var q = (float[])h.Clone(); Array.Sort(q); return q[Mathf.Max(0, (int)(q.Length * 0.04f))]; };
            float mL = low(hL), mR = low(hR);
            const float thr = 0.035f;
            var cL = new bool[N]; var cR = new bool[N];
            for (int i = 0; i < N; i++) { cL[i] = hL[i] < mL + thr; cR[i] = hR[i] < mR + thr; }
            // rising edges of the left contact
            var edges = new List<int>();
            for (int i = 0; i < N; i++) if (cL[i] && !cL[(i + N - 1) % N]) edges.Add(i);
            if (edges.Count == 0) { res.Diag = clip.name + ": no left-foot contact"; return res; }
            int e0 = edges[0];
            int W = edges.Count >= 2 ? edges[1] - edges[0] : N;
            if (W < 6) { res.Diag = clip.name + ": window too short"; return res; }
            // window arrays
            var wl = new bool[W]; var wr = new bool[W]; var pl = new Vector2[W]; var pr = new Vector2[W];
            for (int k = 0; k < W; k++) { int i = (e0 + k) % N; wl[k] = cL[i]; wr[k] = cR[i]; pl[k] = fL[i]; pr[k] = fR[i]; }
            int lLen = 0; while (lLen < W && wl[lLen]) lLen++;
            int rs, rn, rruns;
            if (!LocoCycleScan.LongestRun(wr, out rs, out rn, out rruns)) { res.Diag = clip.name + ": no right-foot contact"; return res; }
            if (rs + rn > W) rn = W - rs;      // never wrap past the cycle end
            float lLift = lLen / (float)W, rPlant = rs / (float)W, rLift = (rs + rn) / (float)W;
            if (rPlant < lLift) { float m = (rPlant + lLift) * 0.5f; rPlant = m; lLift = m; }
            c.lLift = Mathf.Clamp(lLift, 0.05f, 0.9f); c.rPlant = Mathf.Clamp(rPlant, c.lLift + 0.01f, 0.97f); c.rLift = Mathf.Clamp(rLift, c.rPlant + 0.04f, 0.995f);
            // native velocity: how fast the ball of each planted foot sweeps back under the body (the toe bone stands still on the turf for most of the stance: its pelvis-relative velocity is the ground speed that
            // makes it stand still). The toe-height run is taken per foot, the first and last frame of the run are dropped; both feet are averaged.
            Vector2[] footV = new Vector2[2]; bool[] footOk = new bool[2]; double wobble = 0; int wn = 0;
            for (int foot = 0; foot < 2; foot++)
            {
                var tp = new Vector2[W]; var th = new float[W];
                for (int k = 0; k < W; k++)
                {
                    int i = (e0 + k) % N; Vector3 tw = foot == 0 ? s[i].LT : s[i].RT;
                    Vector3 t = inv * (tw - s[i].Root); tp[k] = new Vector2(t.x, t.z); th[k] = tw.y - s[i].Root.y - rig.ToeRest;
                }
                float tm = 9f; foreach (var x in th) tm = Mathf.Min(tm, x);
                var mk = new bool[W]; for (int k = 0; k < W; k++) mk[k] = th[k] < tm + 0.025f;
                int ts, tn, truns;
                if (!LocoCycleScan.LongestRun(mk, out ts, out tn, out truns) || tn < 4) continue;
                var vv = new List<Vector2>();
                for (int k = ts + 1; k < ts + tn - 1; k++) vv.Add(-(tp[(k + 1) % W] - tp[(k + W - 1) % W]) / (2f * dt));
                if (vv.Count < 2) continue;
                Vector2 mv = Vector2.zero; foreach (var v in vv) mv += v; mv /= vv.Count;
                footV[foot] = mv; footOk[foot] = true;
                foreach (var v in vv) { wobble += (v - mv).magnitude; wn++; }
            }
            if (!footOk[0] && !footOk[1]) { res.Diag = clip.name + ": no stance of the ball of the foot"; return res; }
            Vector2 nat = footOk[0] && footOk[1] ? (footV[0] + footV[1]) * 0.5f : (footOk[0] ? footV[0] : footV[1]);
            float asym = footOk[0] && footOk[1] ? Mathf.Abs(footV[0].magnitude - footV[1].magnitude) / Mathf.Max(0.1f, nat.magnitude) : -1f;
            wobble = wn > 0 ? wobble / wn : 0;
            c.speed = nat.magnitude; if (c.speed < 0.4f) { res.Diag = clip.name + ": native speed " + c.speed.ToString("0.00") + " too low"; return res; }
            // flight: both feet clearly off the turf (4.5 cm above the bind pose)
            int fl = 0; for (int k = 0; k < W; k++) { int i = (e0 + k) % N; if (Mathf.Min(hL[i], hR[i]) > 0.045f) fl++; }
            float flight = fl / (float)W;
            c.dir = nat / c.speed;
            c.tStart = e0 * dt; c.length = W * dt;
            c.stride = c.speed * c.length * 0.5f;
            double hm = 0; float sl = 9, sr = 9; for (int k = 0; k < W; k++) { int i = (e0 + k) % N; hm += s[i].Hips.y - s[i].Root.y; }
            c.hipsMean = (float)(hm / W); c.soleMinL = mL; c.soleMinR = mR;
            Vector3 rd = inv * Flat0(s[N].Root - s[0].Root); float rootAng = rd.sqrMagnitude > 0.01f ? Mathf.Atan2(rd.x, rd.z) * Mathf.Rad2Deg : 0f; float rootSpd = new Vector2(rd.x, rd.z).magnitude / (N * dt);
            res.Ok = true;
            res.Diag = string.Format("{0,-40} {1} win {2:0.00}s ({3}cyc of {4:0.00}s) speed {5:0.00} ang {6,4:0} wobble {7:0.00} stride {8:0.00} cadence {9:0.0}/s knots L0 {10:0.00} R {11:0.00}-{12:0.00} head {13:0} sole {14:0.000}/{15:0.000} hips {16:0.00} flight {17:0.00} asym {18:0.00} L {19:0.00} R {20:0.00}",
                clip.name, carry ? "BC" : "  ", c.length, edges.Count, N * dt, c.speed, c.Angle, wobble, c.stride, 2f / c.length, c.lLift, c.rPlant, c.rLift, psi, mL, mR, c.hipsMean, flight, asym, footOk[0] ? footV[0].magnitude : 0f, footOk[1] ? footV[1].magnitude : 0f);
            return res;
        }

        static Vector3 Flat0(Vector3 v) { v.y = 0f; return v; }

        static AvRig Setup(PlayerSize size)
        {
            ArtLibrary.ResetCache();
            ArtLibrary.Load();
            return AvRig.Create(size);
        }

        // Reference cycles used to measure how the speed of a foot-planted take scales with the body (Small / Medium / Large).
        static readonly string[] SizeRef = { "BGS__BGS_Run07_F_Loop", "BGS__BGS_Sprint08_F_Loop", "BGS__BGS_Sprint09_F_Loop", "Armature|PIX_Walk_Cycle" };

        // The library, in priority order (earlier entries win when two cycles have nearly the same speed). Backpedal / side shuffles stay on the animator tree (no cycle of the library is long enough for their speed).
        static readonly string[] FinalFree =
        {
            "Armature|PIX_Walk_Cycle", "BGS__BGS_Run08_F_Loop", "BGS__BGS_Run07_F_Loop", "BGS__BGS_Sprint08_F_Loop", "BGS__BGS_Sprint09_F_Loop",
            "BGS__BGS_Sprint08_FL_Loop", "BGS__BGS_Sprint09_FL_Loop", "BGS__BGS_Sprint09_FR_Loop",
        };
        // takes that exist only with ball-carrier arms: legs from the take, arms from the free donor cycle on the same gait phase (the free-arm jog and back-pedal)
        static readonly string[][] FinalFreeDonor =
        {
            new[] { "BGS__MOB1_Jog_F_Loop_BC", "BGS__BGS_Run08_F_Loop" },
            new[] { "BGS__MOB1_Jog_B_Loop_BC", "BGS__BGS_Run08_F_Loop" },
            new[] { "SprintCycleLeon_1", "BGS__BGS_Sprint09_F_Loop" },      // this take swings no arms (0.14 m): the arms of the grounded sprint take drive instead
        };
        static readonly string[] FinalCarry =
        {
            "BGS__MOB1_Jog_F_Loop_BC", "BGS__BGS_Run_F_Loop_BC", "BGS__BGS_Sprint08_F_Loop_BC", "BGS__BGS_Sprint09_F_Loop_BC",
            "BGS__MOB1_Jog_FL_Loop_BC", "BGS__MOB1_Jog_FR_Loop_BC", "BGS__MOB1_Run_FL_Loop_BC", "BGS__MOB1_Run_FR_Loop_BC", "BGS__BGS_Sprint08_FL_Loop_BC", "BGS__BGS_Sprint09_FL_Loop_BC", "BGS__BGS_Sprint09_FR_Loop_BC", "BGS__MOB1_Jog_B_Loop_BC",
        };

        // learned native speeds (LocoShots -pixLocoModes learn): the planted foot of the cycle played alone says by how much the measured estimate is off
        static Dictionary<string, float> _factors;
        static void ApplyFactor(LocoCycleLibrary.Cycle c)
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixLocoNoFactors") >= 0) return;
            if (_factors == null)
            {
                _factors = new Dictionary<string, float>();
                const string p = "D:/PIXFootball/anim_loco/calibration_factors.csv";
                if (File.Exists(p)) foreach (var line in File.ReadAllLines(p).Skip(1)) { var t = line.Split(','); if (t.Length >= 3) _factors[t[0] + "|" + t[1]] = float.Parse(t[2], System.Globalization.CultureInfo.InvariantCulture); }
            }
            float f; if (_factors.TryGetValue(c.name + (string.IsNullOrEmpty(c.donor) ? "" : "#d") + "|" + (c.carry ? 1 : 0), out f)) c.speed *= f;
        }

        public static void Build()
        {
            BatchWatchdog.Arm(30);
            Directory.CreateDirectory("Logs/loco");
            var rigM = Setup(PlayerSize.Medium);
            if (rigM == null) { Debug.LogError("[LocoBuild] no medium avatar"); return; }
            var lib = AssetDatabase.LoadAssetAtPath<LocoCycleLibrary>(LocoCycleLibrary.EditorPath);
            bool fresh = lib == null;
            if (fresh) lib = ScriptableObject.CreateInstance<LocoCycleLibrary>();
            lib.cycles.Clear(); lib.refLegLength = rigM.Leg;
            var sb = new StringBuilder("# built cycle library (medium avatar leg length " + rigM.Leg.ToString("0.0000") + ")\n");
            foreach (var pair in FinalFree.Select(n => new { n, c = false, d = (string)null }).Concat(FinalFreeDonor.Select(n => new { n = n[0], c = false, d = n[1] })).Concat(FinalCarry.Select(n => new { n, c = true, d = (string)null })))
            {
                var clip = FindExact(pair.n);
                if (clip == null) { sb.AppendLine("MISSING " + pair.n); continue; }
                var r = Analyze(rigM, clip, pair.c);
                sb.AppendLine((r.Ok ? "ok   " : "REJ  ") + (pair.d != null ? "[arms from " + pair.d + "] " : "") + r.Diag);
                if (r.Ok)
                {
                    r.C.donor = pair.d; ApplyFactor(r.C);
                    // the travel angle of a take is its name: a measured +-20 degrees is the roll-over of the foot, not a direction
                    r.C.dir = pair.n.Contains("_FL_") ? new Vector2(-0.4f, 0.9165f) : pair.n.Contains("_FR_") ? new Vector2(0.4f, 0.9165f) : pair.n.Contains("_B_Loop") ? new Vector2(0f, -1f) : Vector2.up;
                    lib.cycles.Add(r.C);
                }
            }
            // body-size scale: native speed of the same takes on the other bodies relative to the medium one
            lib.sizeScale = new float[3] { 1f, 1f, 1f }; lib.legLength = new float[3] { 0f, rigM.Leg, 0f };
            var rigs = new[] { Setup(PlayerSize.Small), rigM, Setup(PlayerSize.Large) };
            for (int i = 0; i < 3; i++)
            {
                if (rigs[i] == null) continue;
                lib.legLength[i] = rigs[i].Leg;
                float num = 0, den = 0;
                foreach (var n in SizeRef)
                {
                    var clip = FindExact(n); if (clip == null) continue;
                    var m = Analyze(rigM, clip, false); var o = Analyze(rigs[i], clip, false);
                    if (m.Ok && o.Ok) { num += o.C.speed; den += m.C.speed; }
                }
                lib.sizeScale[i] = den > 0f ? num / den : 1f;
                sb.AppendLine("size " + (PlayerSize)i + ": leg " + rigs[i].Leg.ToString("0.000") + " speed scale " + lib.sizeScale[i].ToString("0.000") + " (leg ratio " + (rigs[i].Leg / rigM.Leg).ToString("0.000") + ")");
            }
            foreach (var r in rigs) if (r != null) r.Dispose();
            if (fresh) AssetDatabase.CreateAsset(lib, LocoCycleLibrary.EditorPath); else EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/loco/build.txt", sb.ToString());
            LocoCycleLibrary.ResetCache();
            Debug.Log("[LocoBuild] library with " + lib.cycles.Count + " cycles");
        }

        /// <summary>Measures every looping take of the scan (Logs/loco/cycles.csv) on the match avatar: the speed / flight survey the library is chosen from.</summary>
        public static void ReportAll()
        {
            BatchWatchdog.Arm(40);
            Directory.CreateDirectory("Logs/loco");
            var rig = Setup(PlayerSize.Medium);
            var sb = new StringBuilder("# survey of looping takes on the match avatar\n");
            foreach (var line in File.ReadAllLines("Logs/loco/cycles.csv").Skip(1))
            {
                var t = line.Split(','); if (t.Length < 3 || t[2] != "1") continue;
                float len; if (!float.TryParse(t[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out len) || len > 3f) continue;
                var clip = FindExact(t[0]); if (clip == null) continue;
                var r = Analyze(rig, clip, t[0].EndsWith("_BC") || t[0].Contains("_BC_"));
                sb.AppendLine((r.Ok ? "ok   " : "REJ  ") + r.Diag);
            }
            rig.Dispose();
            File.WriteAllText("Logs/loco/survey.txt", sb.ToString());
        }

        public static void Report()
        {
            BatchWatchdog.Arm(30);
            Directory.CreateDirectory("Logs/loco");
            var rig = Setup(PlayerSize.Medium);
            var sb = new StringBuilder("# candidates measured on the match avatar (native speed = ground speed at which the planted foot stands still)\n");
            foreach (var pair in Free.Select(n => new { n, c = false }).Concat(Carry.Select(n => new { n, c = true })))
            {
                var clip = FindExact(pair.n);
                if (clip == null) { sb.AppendLine(pair.n + ": MISSING"); continue; }
                var r = Analyze(rig, clip, pair.c);
                sb.AppendLine((r.Ok ? "ok   " : "REJ  ") + r.Diag);
            }
            rig.Dispose();
            File.WriteAllText("Logs/loco/report.txt", sb.ToString());
            Debug.Log("[LocoBuild] report written");
        }
    }
}
