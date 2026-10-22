using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using PixFootball.Gameplay;
using PixFootball.Core;
using Object = UnityEngine.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>The body that plays the cycles in the match, posed offline: a spawned PlayerAvatar (the photoreal body of one size) with the root fixed and no root motion, exactly like the match view, so the foot motion measured on it is the foot motion the game shows.</summary>
    public sealed class AvRig
    {
        public GameObject Go; public Animator A; public float AnkleRest, ToeRest, Leg; public PlayerSize Size;
        Transform _lf, _rf, _lt, _rt, _hips, _lul, _rul;

        public static AvRig Create(PlayerSize size)
        {
            var av = PlayerAvatar.Spawn(null, size, TeamColors.Home, "LocoRig_" + size);
            var r = new AvRig { Go = av.gameObject, A = av.Animator, Size = size };
            if (r.A == null || !r.A.isHuman) { Object.DestroyImmediate(av.gameObject); return null; }
            r.A.applyRootMotion = false; r.A.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            r._lf = r.A.GetBoneTransform(HumanBodyBones.LeftFoot); r._rf = r.A.GetBoneTransform(HumanBodyBones.RightFoot);
            r._lt = r.A.GetBoneTransform(HumanBodyBones.LeftToes); r._rt = r.A.GetBoneTransform(HumanBodyBones.RightToes);
            r._hips = r.A.GetBoneTransform(HumanBodyBones.Hips); r._lul = r.A.GetBoneTransform(HumanBodyBones.LeftUpperLeg); r._rul = r.A.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            float gy = r.A.transform.position.y;
            r.AnkleRest = Mathf.Min(r._lf.position.y, r._rf.position.y) - gy;
            r.ToeRest = Mathf.Min(r._lt.position.y, r._rt.position.y) - gy;
            var llo = r.A.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            r.Leg = Vector3.Distance(r._lul.position, llo.position) + Vector3.Distance(llo.position, r._lf.position);
            return r;
        }

        public List<LocoCycleScan.Samp> Sample(AnimationClip clip, float rate, float maxLen = 12f)
        {
            var list = new List<LocoCycleScan.Samp>();
            var g = PlayableGraph.Create("LocoAv");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var o = AnimationPlayableOutput.Create(g, "o", A);
            var cp = AnimationClipPlayable.Create(g, clip); cp.SetApplyFootIK(false); cp.SetSpeed(1.0);
            o.SetSourcePlayable(cp);
            cp.SetTime(0); g.Evaluate(0f);
            float dt = 1f / rate;
            int n = Mathf.Max(2, Mathf.RoundToInt(Mathf.Min(clip.length, maxLen) * rate));
            Vector3 root = A.transform.position;
            for (int i = 0; i <= n; i++)
            {
                list.Add(new LocoCycleScan.Samp { Root = root, Yaw = 0f, LA = _lf.position, RA = _rf.position, LT = _lt.position, RT = _rt.position, Hips = _hips.position, LUL = _lul.position, RUL = _rul.position });
                if (i < n) g.Evaluate(dt);
            }
            g.Destroy();
            return list;
        }

        public void Dispose() { if (Go != null) Object.DestroyImmediate(Go); }
    }

    /// <summary>
    /// v0.9.0 LOCOMOTION (LOCO-001): measures every locomotion candidate clip of the animation library the way the match view plays it (root frame, no root motion applied):
    /// the planted-foot speed in the world (clip quality), the ground speed that makes the planted foot stand still (the native speed), foot contact intervals (normalised
    /// gait phase), cadence, stride, direction of travel and loop seam. Output: Logs/loco/cycles.csv and Logs/loco/maneuvers.csv (start / stop / cut / turn takes).
    /// Run: -executeMethod PixFootball.EditorTools.LocoCycleScan.Run
    /// </summary>
    public static class LocoCycleScan
    {
        public const string CleanFolder = "Assets/_Licensed/DraftFever/Animations/Clean";
        public const float Rate = 60f;
        static readonly Regex CycleRx = new Regex("run|sprint|jog|walk|shuffle|bkpd|backpedal|_cir_|cycle|stroll|crossover|step_", RegexOptions.IgnoreCase);
        static readonly Regex ManeuverRx = new Regex("transition|trans_|stance_to|to_sprint|idletosprint|idle_to|_stop|stop_|turn|cut|plant|decel|pivot|run_to|jog_to|avoid", RegexOptions.IgnoreCase);
        static readonly Regex NoRx = new Regex("tackle|block|catch|sack|celebr|collision|swat|juke|hurdle|bullrush|dline|oline|linebacker|pass|throw|kick|cadence|dive|spin|stiffarm|_a$|_b$|_co$|olm_|fumble|snap", RegexOptions.IgnoreCase);

        public sealed class Samp
        {
            public Vector3 Root; public float Yaw;
            public Vector3 LA, RA, LT, RT;       // world (root-motion applied)
            public Vector3 Hips, LUL, RUL;
        }

        public static List<Samp> Sample(AnimRig rig, AnimationClip clip, float rate, float maxLen = 12f)
        {
            var list = new List<Samp>();
            var g = PlayableGraph.Create("LocoScan");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var o = AnimationPlayableOutput.Create(g, "o", rig.Anim);
            var cp = AnimationClipPlayable.Create(g, clip); cp.SetApplyFootIK(false); cp.SetSpeed(1.0);
            o.SetSourcePlayable(cp);
            rig.Go.transform.position = Vector3.zero; rig.Go.transform.rotation = AnimRig.StartRot;
            cp.SetTime(0); g.Evaluate(0f);
            float dt = 1f / rate;
            int n = Mathf.Max(2, Mathf.RoundToInt(Mathf.Min(clip.length, maxLen) * rate));
            Func<Samp> cap = () => new Samp
            {
                Root = rig.Anim.transform.position, Yaw = rig.Anim.transform.eulerAngles.y,
                LA = rig.T[AnimRig.LF].position, RA = rig.T[AnimRig.RF].position, LT = rig.T[AnimRig.LT] != null ? rig.T[AnimRig.LT].position : rig.T[AnimRig.LF].position, RT = rig.T[AnimRig.RT] != null ? rig.T[AnimRig.RT].position : rig.T[AnimRig.RF].position,
                Hips = rig.T[AnimRig.Hips].position, LUL = rig.T[AnimRig.LUL].position, RUL = rig.T[AnimRig.RUL].position
            };
            list.Add(cap());
            for (int i = 0; i < n; i++) { g.Evaluate(dt); list.Add(cap()); }
            g.Destroy();
            return list;
        }

        public static void Dump()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory("Logs/loco");
            string[] names = (CmdArg("-pixLocoDump", "RunCycleJR_1")).Split(',');
            ArtLibrary.ResetCache(); ArtLibrary.Load();
            var rig = AvRig.Create(PlayerSize.Medium); if (rig == null) return;
            foreach (string n in names)
            {
                var clip = LocoCycleBuilder.FindExact(n); if (clip == null) { Debug.LogWarning("[LocoDump] missing " + n); continue; }
                var s = rig.Sample(clip, Rate); int N = s.Count - 1; float dt = 1f / Rate;
                var sb = new StringBuilder("frame,t,hipsY,heelL_y,toeL_y,heelR_y,toeR_y,toeL_fwd,toeL_side,toeR_fwd,toeR_side,heelL_fwd,heelR_fwd,hips_fwd\n");
                float yaw = MeanHeading(s); var inv = Quaternion.Euler(0f, -yaw, 0f);
                for (int i = 0; i <= N; i++)
                {
                    float ry = s[i].Root.y;
                    Vector3 tl = inv * (s[i].LT - s[i].Root), tr = inv * (s[i].RT - s[i].Root), hl = inv * (s[i].LA - s[i].Root), hr = inv * (s[i].RA - s[i].Root), hp = inv * (s[i].Hips - s[i].Root);
                    sb.AppendLine(i + "," + (i * dt).ToString("0.000") + "," + (s[i].Hips.y - ry).ToString("0.000") + "," + (s[i].LA.y - ry - rig.AnkleRest).ToString("0.000") + "," + (s[i].LT.y - ry - rig.ToeRest).ToString("0.000") + "," + (s[i].RA.y - ry - rig.AnkleRest).ToString("0.000") + "," + (s[i].RT.y - ry - rig.ToeRest).ToString("0.000") + "," + tl.z.ToString("0.000") + "," + tl.x.ToString("0.000") + "," + tr.z.ToString("0.000") + "," + tr.x.ToString("0.000") + "," + hl.z.ToString("0.000") + "," + hr.z.ToString("0.000") + "," + hp.z.ToString("0.000"));
                }
                File.WriteAllText("Logs/loco/dump_" + n.Replace("|", "_") + ".csv", sb.ToString());
            }
            rig.Dispose();
        }

        /// <summary>Per-frame dump of a few clips (-pixLocoDump name1,name2): sole heights and sole velocity relative to the root (forward = travel axis of the clip).</summary>

        public static string CmdArg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(40);
            Directory.CreateDirectory("Logs/loco");
            var lib = ArtLibrary.Load();
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            // rest heights of the ankle / toe bones in a flat standing pose (the same reference FitGround uses)
            var idle = AnimatorControllerBuilder.FindClipByName("Player_Standing_Idle") ?? lib.Clip(ArtKeys.Idle);
            var ip = rig.PoseAt(idle, 0.5f);
            float ankleRest = Mathf.Min(ip.P[AnimRig.LF].y, ip.P[AnimRig.RF].y), toeRest = Mathf.Min(ip.P[AnimRig.LT].y, ip.P[AnimRig.RT].y);
            float legLen = Vector3.Distance(ip.P[AnimRig.LUL], ip.P[AnimRig.LLL]) + Vector3.Distance(ip.P[AnimRig.LLL], ip.P[AnimRig.LF]);
            var cyc = new StringBuilder("clip,len,loop,rootSpeed,nativeSpeed,vx,vz,angle,turnDegPerS,stanceL,stanceR,lStart,lEnd,rStart,rEnd,contactsL,contactsR,plantSlideL,plantSlideR,seam,hipsMin,hipsMax,hipsMean,sole0L,sole0R,steps,stride\n");
            var man = new StringBuilder("clip,len,loop,v0x,v0z,v1x,v1z,speed0,speed1,peakSpeed,yawDelta,travel,hips0,hips1,rootAngle0,angle1\n");
            int nc = 0, nm = 0;
            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { CleanFolder });
            var seen = new HashSet<string>();
            foreach (string gd in guids)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(gd));
                if (clip == null || !seen.Add(clip.name)) continue;
                string n = clip.name;
                bool isCycle = CycleRx.IsMatch(n) && !NoRx.IsMatch(n) && !ManeuverRx.IsMatch(n);
                bool isMan = ManeuverRx.IsMatch(n) && !Regex.IsMatch(n, "tackle|block|catch|sack|celebr|collision|swat|juke_|hurdle|dive|pass|throw|stiffarm|_a$|_b$|_co$|oline|dline|linebacker|quarterback|cadence", RegexOptions.IgnoreCase);
                if (!isCycle && !isMan) continue;
                try
                {
                    var s = Sample(rig, clip, Rate);
                    if (isCycle) { cyc.AppendLine(CycleRow(clip, s, ankleRest, toeRest)); nc++; }
                    else { man.AppendLine(ManeuverRow(clip, s)); nm++; }
                }
                catch (Exception e) { Debug.LogWarning("[LocoScan] " + n + ": " + e.Message); }
            }
            File.WriteAllText("Logs/loco/cycles.csv", cyc.ToString());
            File.WriteAllText("Logs/loco/maneuvers.csv", man.ToString());
            File.WriteAllText("Logs/loco/rig.txt", "legLength " + legLen.ToString("0.0000") + "\nankleRest " + ankleRest.ToString("0.0000") + "\ntoeRest " + toeRest.ToString("0.0000") + "\nhipsRest " + ip.P[AnimRig.Hips].y.ToString("0.0000") + "\n");
            rig.Dispose();
            Debug.Log("[LocoScan] cycles " + nc + " maneuvers " + nm + " legLen " + legLen.ToString("0.000"));
        }

        public static float Heading(Samp s)
        {
            Vector3 r = s.RUL - s.LUL; r.y = 0f;
            if (r.sqrMagnitude < 1e-6f) return 0f;
            Vector3 f = Vector3.Cross(r.normalized, Vector3.up);
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
        public static float MeanHeading(List<Samp> s)
        {
            double sx = 0, sz = 0;
            foreach (var p in s) { float h = Heading(p) * Mathf.Deg2Rad; sx += Mathf.Sin(h); sz += Mathf.Cos(h); }
            return (float)(Math.Atan2(sx, sz) * Mathf.Rad2Deg);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Longest cyclic run of true values: returns start index and length (start = first frame in the run).</summary>
        public static bool LongestRun(bool[] m, out int start, out int len, out int runs)
        {
            int n = m.Length; start = 0; len = 0; runs = 0;
            int ones = 0; for (int i = 0; i < n; i++) if (m[i]) ones++;
            if (ones == 0) return false;
            if (ones == n) { start = 0; len = n; runs = 1; return true; }
            // begin at a false element so a run is never split by the array end
            int b = 0; while (m[b]) b++;
            int cur = 0, curStart = 0;
            for (int k = 1; k <= n; k++)
            {
                int i = (b + k) % n;
                if (m[i]) { if (cur == 0) curStart = i; cur++; }
                if (!m[i] || k == n)
                {
                    if (cur > 0) { runs++; if (cur > len) { len = cur; start = curStart; } cur = 0; }
                }
            }
            return len > 0;
        }

        static string CycleRow(AnimationClip clip, List<Samp> s, float ankleRest, float toeRest)
        {
            int N = s.Count - 1;      // N intervals; frames 0..N (N is the loop point)
            float dt = 1f / Rate, len = N * dt;
            Vector3 d = Flat(s[N].Root - s[0].Root);
            float rootSpeed = d.magnitude / len;
            float yaw0 = MeanHeading(s);
            Quaternion inv0 = Quaternion.Euler(0f, -yaw0, 0f);
            Vector3 lv = inv0 * (d / len);
            float turn = Mathf.DeltaAngle(Heading(s[0]), Heading(s[N])) / len;
            // sole heights
            var hL = new float[N]; var hR = new float[N];
            for (int i = 0; i < N; i++)
            {
                float ry = s[i].Root.y;
                hL[i] = Mathf.Min(s[i].LA.y - ry - ankleRest, s[i].LT.y - ry - toeRest);
                hR[i] = Mathf.Min(s[i].RA.y - ry - ankleRest, s[i].RT.y - ry - toeRest);
            }
            float lo(float[] h) { var c = (float[])h.Clone(); Array.Sort(c); return c[Mathf.Max(0, (int)(c.Length * 0.04f))]; }
            float lL = lo(hL), lR = lo(hR);
            // contact: sole within 3.5 cm of the foot's own lowest point (hysteresis through the longest cyclic run)
            var cL = new bool[N]; var cR = new bool[N];
            for (int i = 0; i < N; i++) { cL[i] = hL[i] < lL + 0.035f; cR[i] = hR[i] < lR + 0.035f; }
            int sL, nL, runsL, sR, nR, runsR;
            bool okL = LongestRun(cL, out sL, out nL, out runsL), okR = LongestRun(cR, out sR, out nR, out runsR);
            // foot world speed during contact (clip quality) and the root velocity that makes the planted feet stand still
            double slideL = 0, slideR = 0; int slNL = 0, slNR = 0; Vector3 nativeSum = Vector3.zero; int natN = 0;
            for (int foot = 0; foot < 2; foot++)
            {
                bool ok = foot == 0 ? okL : okR; int st = foot == 0 ? sL : sR, ln = foot == 0 ? nL : nR;
                if (!ok || ln < 4) continue;
                int skip = Mathf.Max(1, ln / 5);
                for (int k = skip; k < ln - skip; k++)
                {
                    int i = (st + k) % N; int ip = (i + 1) % (N + 1) == 0 ? N : i + 1; int im = i == 0 ? 0 : i - 1;
                    if (i + 1 > N) continue;
                    float span = (ip - im) * dt; if (span <= 0f) continue;
                    Vector3 rootV = Flat(s[ip].Root - s[im].Root) / span;
                    Vector3 fa = foot == 0 ? (s[ip].LA - s[im].LA) : (s[ip].RA - s[im].RA);
                    Vector3 ft = foot == 0 ? (s[ip].LT - s[im].LT) : (s[ip].RT - s[im].RT);
                    Vector3 fv = Flat((fa + ft) * 0.5f) / span;
                    if (foot == 0) { slideL += fv.magnitude; slNL++; } else { slideR += fv.magnitude; slNR++; }
                    nativeSum += rootV - fv; natN++;
                }
            }
            int slN = slNL + slNR;
            Vector3 nat = natN > 0 ? nativeSum / natN : d / len; Vector3 natLocal = inv0 * nat;
            double hipsSum = 0; float hMin = 9, hMax = -9;
            for (int i = 0; i < N; i++) { float hy = s[i].Hips.y - s[i].Root.y; hipsSum += hy; hMin = Mathf.Min(hMin, hy); hMax = Mathf.Max(hMax, hy); }
            // seam: root-frame foot / hips position at the loop point
            Func<int, Vector3[]> loc = i => { var inv = inv0; return new[] { inv * (s[i].LA - s[i].Root), inv * (s[i].RA - s[i].Root), inv * (s[i].Hips - s[i].Root) }; };
            var a0 = loc(0); var a1 = loc(N);
            float seam = Mathf.Max((a0[0] - a1[0]).magnitude, (a0[1] - a1[1]).magnitude, (a0[2] - a1[2]).magnitude);
            float ang = Mathf.Atan2(natLocal.x, natLocal.z) * Mathf.Rad2Deg;
            float natSpeed = natLocal.magnitude;
            int steps = Mathf.Max(runsL, runsR) * 2;
            float stride = natSpeed * len / Mathf.Max(1, steps);
            return string.Join(",", new[]
            {
                clip.name, len.ToString("0.000"), clip.isLooping ? "1" : "0", rootSpeed.ToString("0.00"), natSpeed.ToString("0.00"), natLocal.x.ToString("0.00"), natLocal.z.ToString("0.00"), ang.ToString("0"), turn.ToString("0.0"),
                (nL / (float)N).ToString("0.00"), (nR / (float)N).ToString("0.00"),
                okL ? (sL / (float)N).ToString("0.000") : "-", okL ? (((sL + nL) % N) / (float)N).ToString("0.000") : "-", okR ? (sR / (float)N).ToString("0.000") : "-", okR ? (((sR + nR) % N) / (float)N).ToString("0.000") : "-",
                runsL.ToString(), runsR.ToString(), slNL > 0 ? (slideL / slNL).ToString("0.00") : "-", slNR > 0 ? (slideR / slNR).ToString("0.00") : "-",
                seam.ToString("0.000"), hMin.ToString("0.000"), hMax.ToString("0.000"), (hipsSum / N).ToString("0.000"), lL.ToString("0.000"), lR.ToString("0.000"), steps.ToString(), stride.ToString("0.00")
            });
        }

        static string ManeuverRow(AnimationClip clip, List<Samp> s)
        {
            int N = s.Count - 1; float dt = 1f / Rate, len = N * dt;
            int w = Mathf.Min(N / 2, Mathf.RoundToInt(0.25f * Rate));
            Quaternion inv0 = Quaternion.Euler(0f, -Heading(s[0]), 0f), invN = Quaternion.Euler(0f, -Heading(s[N]), 0f);
            Vector3 v0 = inv0 * Flat(s[w].Root - s[0].Root) / (w * dt);
            Vector3 v1 = invN * Flat(s[N].Root - s[N - w].Root) / (w * dt);
            float peak = 0f; for (int i = 4; i < N; i++) peak = Mathf.Max(peak, Flat(s[i].Root - s[i - 4].Root).magnitude / (4 * dt));
            float yawD = Mathf.DeltaAngle(Heading(s[0]), Heading(s[N]));
            float travel = Flat(s[N].Root - s[0].Root).magnitude;
            Vector3 dir = Flat(s[N].Root - s[0].Root);
            return string.Join(",", new[]
            {
                clip.name, len.ToString("0.00"), clip.isLooping ? "1" : "0", v0.x.ToString("0.00"), v0.z.ToString("0.00"), v1.x.ToString("0.00"), v1.z.ToString("0.00"), v0.magnitude.ToString("0.00"), v1.magnitude.ToString("0.00"), peak.ToString("0.00"),
                yawD.ToString("0"), travel.ToString("0.00"), (s[0].Hips.y - s[0].Root.y).ToString("0.00"), (s[N].Hips.y - s[N].Root.y).ToString("0.00"),
                (dir.sqrMagnitude > 0.01f ? Mathf.DeltaAngle(Heading(s[0]), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg) : 0f).ToString("0"), ""
            });
        }
    }
}
