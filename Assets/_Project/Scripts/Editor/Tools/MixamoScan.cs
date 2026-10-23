using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>One sampled frame of a clip played on the match body with root motion applied (30 fps source frames).</summary>
    public struct MmFrame
    {
        public Vector3 Root; public float Yaw;              // root transform (world), yaw degrees
        public Vector3 Hips, LA, RA, LT, RT;                  // bone positions in the ROOT frame (x right, y up, z forward)
        public Quaternion HipsRot;                            // hips rotation in the root frame
    }

    /// <summary>
    /// v0.10.0 MOTION: objective measurement of every Mixamo locomotion clip on the match body (the same photoreal PlayerAvatar the game plays), per clip: root speed, foot-based native speed, cadence,
    /// flight fraction (both toes more than 10 cm up, the audit's definition), foot slide at the clip's own root speed, toe-out, knee plane, foot height, left / right asymmetry, loop seam.
    /// Output: Logs/motion/mixamo_scan.csv and Logs/motion/mixamo_scan.md. Run: -executeMethod PixFootball.EditorTools.MixamoScan.Run
    /// </summary>
    public static class MixamoScan
    {
        public const float Fps = 30f;

        public static List<MmFrame> Sample(AvRig rig, AnimationClip clip, Action<int, int, MmFrame> perFrame = null, float fps = Fps, float bodyYaw = 0f)
        {
            var list = new List<MmFrame>();
            var A = rig.A;
            A.applyRootMotion = true;
            var tr = A.transform;
            var hips = A.GetBoneTransform(HumanBodyBones.Hips);
            var lf = A.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = A.GetBoneTransform(HumanBodyBones.RightFoot);
            var lt = A.GetBoneTransform(HumanBodyBones.LeftToes); var rt = A.GetBoneTransform(HumanBodyBones.RightToes);
            tr.position = Vector3.zero; tr.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
            var g = PlayableGraph.Create("MmSample");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var o = AnimationPlayableOutput.Create(g, "o", A);
            var cp = AnimationClipPlayable.Create(g, clip); cp.SetApplyFootIK(false); cp.SetSpeed(1.0);
            o.SetSourcePlayable(cp);
            cp.SetTime(0); g.Evaluate(0f);
            float dt = 1f / fps;
            int n = Mathf.Max(2, Mathf.RoundToInt(clip.length * fps));
            for (int i = 0; i <= n; i++)
            {
                var inv = Quaternion.Inverse(tr.rotation * Quaternion.Euler(0f, -bodyYaw, 0f)); var p0 = tr.position;
                var f = new MmFrame
                {
                    Root = p0, Yaw = tr.eulerAngles.y - bodyYaw,
                    Hips = inv * (hips.position - p0), LA = inv * (lf.position - p0), RA = inv * (rf.position - p0),
                    LT = inv * ((lt != null ? lt.position : lf.position) - p0), RT = inv * ((rt != null ? rt.position : rf.position) - p0),
                    HipsRot = inv * hips.rotation
                };
                list.Add(f);
                if (perFrame != null) perFrame(i, n, f);
                if (i < n) g.Evaluate(dt);
            }
            g.Destroy();
            return list;
        }

        static float Pct(List<float> l, float p) { if (l.Count == 0) return float.NaN; var s = l.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(p * (s.Count - 1) + 0.5f), 0, s.Count - 1)]; }
        static float Mean(List<float> l) { return l.Count == 0 ? float.NaN : l.Average(); }
        static string F(float v, string fmt = "0.00") { return float.IsNaN(v) ? "-" : v.ToString(fmt, CultureInfo.InvariantCulture); }

        public sealed class Row
        {
            public string Name; public float Len; public int Frames;
            public float RootSpeed, Native, TurnRate, TravelAngle, SteppsPerSec, FlightRel, FlightAbs, SlideMean, SlideP95, FloorAbs, LowToeMean, Seam, HipsY, Stride;
            public float ContactL, ContactR;
            public float ToeOutMean, ToeOutP95, KneeDev, KneeHinge, RollMean, ToeOutL, ToeOutR, RollL, RollR, KneeL, KneeR, ShinTwist, Asym;
            public bool Loop;
            public string Band;
        }

        public static Row Analyze(AvRig rig, FootPlant plant, AnimationClip clip, bool probeLegs, string dumpDir = null, float bodyYaw = 0f)
        {
            var rows = Sample(rig, clip, null, Fps, bodyYaw);
            int N = rows.Count - 1; float dt = 1f / Fps, len = N * dt;
            var r = new Row { Name = clip.name, Len = len, Frames = N };
            // root kinematics
            double path = 0; Vector3 disp = rows[N].Root - rows[0].Root; disp.y = 0f;
            for (int i = 1; i <= N; i++) { Vector3 d = rows[i].Root - rows[i - 1].Root; d.y = 0f; path += d.magnitude; }
            r.RootSpeed = (float)(path / len);
            r.TurnRate = Mathf.DeltaAngle(rows[0].Yaw, rows[N].Yaw) / len;
            float yawSum = 0; for (int i = 1; i <= N; i++) yawSum += Mathf.Abs(Mathf.DeltaAngle(rows[i - 1].Yaw, rows[i].Yaw));
            r.TurnRate = Mathf.DeltaAngle(rows[0].Yaw, rows[N].Yaw) / len;
            // travel direction in the pelvis heading frame
            Vector3 dl = Quaternion.Inverse(Quaternion.Euler(0f, rows[0].Yaw, 0f)) * disp;
            r.TravelAngle = disp.magnitude > 0.1f ? Mathf.Atan2(dl.x, dl.z) * Mathf.Rad2Deg : 0f;
            // heights relative to the root (audit definition) and to the clip's own floor
            var lc = new float[N + 1]; var rc = new float[N + 1]; var lth = new float[N + 1]; var rth = new float[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float lt = rows[i].LT.y, rt = rows[i].RT.y, la = rows[i].LA.y, ra = rows[i].RA.y;
                lth[i] = lt; rth[i] = rt;
                lc[i] = Mathf.Min(lt - plant.RestToeL, la - plant.RestAnkleL); rc[i] = Mathf.Min(rt - plant.RestToeR, ra - plant.RestAnkleR);
            }
            var lows = new List<float>(); for (int i = 0; i <= N; i++) lows.Add(Mathf.Min(lc[i], rc[i]));
            float floor = Pct(lows, 0.03f); r.FloorAbs = floor;
            r.LowToeMean = 0; for (int i = 0; i <= N; i++) r.LowToeMean += Mathf.Min(lth[i], rth[i]); r.LowToeMean /= (N + 1);
            var pl = new bool[N + 1]; var pr = new bool[N + 1];
            bool sl = false, sr = false;
            for (int i = 0; i <= N; i++)
            {
                float hl = lc[i] - floor, hr = rc[i] - floor;
                sl = sl ? hl < 0.07f : hl < 0.04f; sr = sr ? hr < 0.07f : hr < 0.04f;
                pl[i] = sl; pr[i] = sr;
            }
            int edges = 0, flightRel = 0, flightAbs = 0, cl = 0, cr = 0;
            for (int i = 0; i <= N; i++)
            {
                if (i > 0 && pl[i] && !pl[i - 1]) edges++;
                if (i > 0 && pr[i] && !pr[i - 1]) edges++;
                if (pl[i]) cl++; if (pr[i]) cr++;
                float tlr = lth[i] - rig.ToeRest + 0.03f - floor, trr = rth[i] - rig.ToeRest + 0.03f - floor;       // toe bone height above a planted foot's toe bone (rest 3 cm)
                if (tlr > 0.10f && trr > 0.10f) flightRel++;
                if (lth[i] > 0.10f && rth[i] > 0.10f) flightAbs++;
            }
            r.FlightRel = flightRel / (float)(N + 1); r.FlightAbs = flightAbs / (float)(N + 1); r.ContactL = cl / (float)(N + 1); r.ContactR = cr / (float)(N + 1);
            r.SteppsPerSec = edges / len;
            // slide at the clip's own root speed, native speed of the planted feet
            var slide = new List<float>(); Vector3 natSum = Vector3.zero; int natN = 0;
            for (int i = 1; i <= N; i++)
            {
                for (int s = 0; s < 2; s++)
                {
                    bool c0 = s == 0 ? pl[i - 1] : pr[i - 1], c1 = s == 0 ? pl[i] : pr[i];
                    if (!c0 || !c1) continue;
                    Vector3 rp0 = rows[i - 1].Root, rp1 = rows[i].Root;
                    var q0 = Quaternion.Euler(0f, rows[i - 1].Yaw, 0f); var q1 = Quaternion.Euler(0f, rows[i].Yaw, 0f);
                    Vector3 a0 = rp0 + q0 * (s == 0 ? rows[i - 1].LA : rows[i - 1].RA), a1 = rp1 + q1 * (s == 0 ? rows[i].LA : rows[i].RA);
                    Vector3 t0 = rp0 + q0 * (s == 0 ? rows[i - 1].LT : rows[i - 1].RT), t1 = rp1 + q1 * (s == 0 ? rows[i].LT : rows[i].RT);
                    Vector3 da = a1 - a0, dtv = t1 - t0; da.y = 0f; dtv.y = 0f;
                    slide.Add(Mathf.Min(da.magnitude, dtv.magnitude) / dt);
                    Vector3 rootV = rp1 - rp0; rootV.y = 0f; rootV /= dt;
                    Vector3 fv = (da + dtv) * 0.5f / dt;
                    natSum += rootV - fv; natN++;
                }
            }
            r.SlideMean = Mean(slide); r.SlideP95 = Pct(slide, 0.95f);
            r.Native = natN > 0 ? (natSum / natN).magnitude : r.RootSpeed;
            r.Stride = r.SteppsPerSec > 0.1f ? r.Native / r.SteppsPerSec : 0f;
            r.HipsY = 0; for (int i = 0; i <= N; i++) r.HipsY += rows[i].Hips.y; r.HipsY /= (N + 1);
            // loop seam: feet / hips in the root frame, first against last
            r.Seam = Mathf.Max((rows[0].LA - rows[N].LA).magnitude, (rows[0].RA - rows[N].RA).magnitude, (rows[0].Hips - rows[N].Hips).magnitude);
            // legs via LegProbe on the live pose (second pass; travel = root velocity)
            if (probeLegs)
            {
                var acc = new LegProbe.Acc(); var probe = new LegProbe(rig.A, plant);
                var tr = rig.A.transform;
                Sample(rig, clip, (i, n, f) =>
                {
                    int a = Mathf.Max(0, i - 1), b = Mathf.Min(N, i + 1);
                    Vector3 v = (rows[b].Root - rows[a].Root) / ((b - a) * dt); v.y = 0f;
                    Vector3 fwd = tr.forward; fwd.y = 0f;
                    probe.Sample(acc, fwd, v, v.magnitude, tr.position.y + floor);
                });
                Func<string, float> M = k => { List<float> l; return acc.V.TryGetValue(k, out l) ? Mean(l) : float.NaN; };
                r.ToeOutMean = M("foot toe-out deg contact (abs)"); List<float> tl; acc.V.TryGetValue("foot toe-out deg contact (abs)", out tl); r.ToeOutP95 = tl != null ? Pct(tl, 0.95f) : float.NaN;
                r.KneeDev = M("knee dev cm stance"); r.KneeHinge = M("knee hinge yaw deg stance"); r.RollMean = M("ankle roll deg stance (abs)");
                r.ToeOutL = M("foot toe-out deg contact (signed, + = out) L"); r.ToeOutR = M("foot toe-out deg contact (signed, + = out) R");
                r.RollL = M("ankle roll deg stance (signed, + = outer edge down) L"); r.RollR = M("ankle roll deg stance (signed, + = outer edge down) R");
                r.KneeL = M("knee inward cm stance (signed) L"); r.KneeR = M("knee inward cm stance (signed) R");
                r.ShinTwist = M("shin twist deg (abs)");
                r.Asym = Mathf.Max(float.IsNaN(r.ToeOutL) || float.IsNaN(r.ToeOutR) ? 0f : Mathf.Abs(r.ToeOutL - r.ToeOutR), float.IsNaN(r.RollL) || float.IsNaN(r.RollR) ? 0f : Mathf.Abs(r.RollL - r.RollR));
            }
            if (dumpDir != null)
            {
                var sb = new StringBuilder("i,t,rootX,rootZ,yaw,hipsY,lowL,lowR,plantL,plantR\n");
                for (int i = 0; i <= N; i++) sb.AppendLine(i + "," + (i * dt).ToString("0.000") + "," + rows[i].Root.x.ToString("0.000") + "," + rows[i].Root.z.ToString("0.000") + "," + rows[i].Yaw.ToString("0.0") + "," + rows[i].Hips.y.ToString("0.000") + "," + (lc[i] - floor).ToString("0.000") + "," + (rc[i] - floor).ToString("0.000") + "," + (pl[i] ? 1 : 0) + "," + (pr[i] ? 1 : 0));
                File.WriteAllText(Path.Combine(dumpDir, clip.name + ".csv"), sb.ToString());
            }
            return r;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(30);
            Directory.CreateDirectory("Logs/motion"); Directory.CreateDirectory("Logs/motion/frames");
            AssetDatabase.Refresh();
            var rig = AvRig.Create(PlayerSize.Medium); if (rig == null) { Debug.LogError("[MixamoScan] no rig"); return; }
            var plant = new FootPlant(rig.A);
            string filter = LocoCycleScan.CmdArg("-pixMmFilter", "");
            var rows = new List<Row>();
            foreach (string f in Directory.GetFiles(MixamoImport.Folder, "*.fbx").OrderBy(x => x))
            {
                var clip = MixamoImport.LoadClip(f.Replace('\\', '/'));
                if (clip == null) { Debug.LogWarning("[MixamoScan] no clip " + f); continue; }
                if (filter.Length > 0 && !clip.name.Contains(filter)) continue;
                try { rows.Add(Analyze(rig, plant, clip, true, "Logs/motion/frames")); }
                catch (Exception e) { Debug.LogWarning("[MixamoScan] " + clip.name + ": " + e.Message); }
            }
            WriteReport(rows, "Logs/motion/mixamo_scan");
            rig.Dispose();
            Debug.Log("[MixamoScan] clips " + rows.Count);
        }

        public static void WriteReport(List<Row> rows, string basePath)
        {
            var csv = new StringBuilder("clip,len,frames,rootSpeed,native,turnDegPerS,travelAngle,stepsPerSec,stride,flightRel,flightAbs,slideMean,slideP95,floorAbs,lowToeMean,seam,hipsY,contactL,contactR,toeOut,toeOutP95,kneeDev,kneeHinge,roll,toeOutL,toeOutR,rollL,rollR,kneeInL,kneeInR,shinTwist,asym\n");
            var md = new StringBuilder("| clip | len s | root m/s | native m/s | turn deg/s | travel deg | steps/s | stride m | flight (rel / abs) | slide mean / p95 m/s | floor cm | low toe cm | seam cm | toe-out deg (mean / p95) | knee dev cm | knee hinge deg | roll deg | L/R toe-out | L/R roll | L/R knee in | asym deg |\n|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (var r in rows)
            {
                csv.AppendLine(string.Join(",", new[] { r.Name, F(r.Len), r.Frames.ToString(), F(r.RootSpeed), F(r.Native), F(r.TurnRate, "0.0"), F(r.TravelAngle, "0"), F(r.SteppsPerSec), F(r.Stride), F(r.FlightRel), F(r.FlightAbs), F(r.SlideMean), F(r.SlideP95), F(r.FloorAbs, "0.000"), F(r.LowToeMean, "0.000"), F(r.Seam, "0.000"), F(r.HipsY, "0.000"), F(r.ContactL), F(r.ContactR), F(r.ToeOutMean, "0.0"), F(r.ToeOutP95, "0.0"), F(r.KneeDev, "0.0"), F(r.KneeHinge, "0.0"), F(r.RollMean, "0.0"), F(r.ToeOutL, "0.0"), F(r.ToeOutR, "0.0"), F(r.RollL, "0.0"), F(r.RollR, "0.0"), F(r.KneeL, "0.0"), F(r.KneeR, "0.0"), F(r.ShinTwist, "0.0"), F(r.Asym, "0.0") }));
                md.AppendLine("| " + r.Name + " | " + F(r.Len) + " | " + F(r.RootSpeed) + " | " + F(r.Native) + " | " + F(r.TurnRate, "0") + " | " + F(r.TravelAngle, "0") + " | " + F(r.SteppsPerSec, "0.0") + " | " + F(r.Stride) + " | " + F(r.FlightRel) + " / " + F(r.FlightAbs) + " | " + F(r.SlideMean) + " / " + F(r.SlideP95) + " | " + F(r.FloorAbs * 100f, "0.0") + " | " + F(r.LowToeMean * 100f, "0.0") + " | " + F(r.Seam * 100f, "0.0") + " | " + F(r.ToeOutMean, "0.0") + " / " + F(r.ToeOutP95, "0") + " | " + F(r.KneeDev, "0.0") + " | " + F(r.KneeHinge, "0") + " | " + F(r.RollMean, "0.0") + " | " + F(r.ToeOutL, "0.0") + " / " + F(r.ToeOutR, "0.0") + " | " + F(r.RollL, "0.0") + " / " + F(r.RollR, "0.0") + " | " + F(r.KneeL, "0.0") + " / " + F(r.KneeR, "0.0") + " | " + F(r.Asym, "0.0") + " |");
            }
            File.WriteAllText(basePath + ".csv", csv.ToString());
            File.WriteAllText(basePath + ".md", md.ToString());
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>Bind-pose symmetry of the match bodies (Small / Medium / Large): per-side heights of the leg joints, leg lengths, foot sizes. Run: -executeMethod PixFootball.EditorTools.RigSymmetry.Run</summary>
    public static class RigSymmetry
    {
        public static void Run()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory("Logs/motion");
            var sb = new StringBuilder();
            foreach (var size in new[] { PlayerSize.Small, PlayerSize.Medium, PlayerSize.Large })
            {
                var rig = AvRig.Create(size); if (rig == null) continue;
                var a = rig.A; var tr = a.transform;
                Func<HumanBodyBones, Vector3> P = b => { var t = a.GetBoneTransform(b); return t == null ? Vector3.zero : tr.InverseTransformPoint(t.position); };
                sb.AppendLine("== " + size);
                foreach (var pair in new[] { ("UpperLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg), ("LowerLeg", HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg), ("Foot", HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot), ("Toes", HumanBodyBones.LeftToes, HumanBodyBones.RightToes), ("Shoulder", HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm), ("Hand", HumanBodyBones.LeftHand, HumanBodyBones.RightHand) })
                {
                    Vector3 l = P(pair.Item2), r = P(pair.Item3);
                    sb.AppendLine(pair.Item1 + " L " + l.ToString("F4") + "  R " + r.ToString("F4") + "  dy " + (l.y - r.y).ToString("F4") + "  mirrorX " + (l.x + r.x).ToString("F4") + "  dz " + (l.z - r.z).ToString("F4"));
                }
                float ll = Vector3.Distance(P(HumanBodyBones.LeftUpperLeg), P(HumanBodyBones.LeftLowerLeg)) + Vector3.Distance(P(HumanBodyBones.LeftLowerLeg), P(HumanBodyBones.LeftFoot));
                float rl = Vector3.Distance(P(HumanBodyBones.RightUpperLeg), P(HumanBodyBones.RightLowerLeg)) + Vector3.Distance(P(HumanBodyBones.RightLowerLeg), P(HumanBodyBones.RightFoot));
                sb.AppendLine("leg length L " + ll.ToString("F4") + " R " + rl.ToString("F4"));
                var plant = new FootPlant(a);
                sb.AppendLine("rest ankle L " + plant.RestAnkleL.ToString("F4") + " R " + plant.RestAnkleR.ToString("F4") + "  rest toe L " + plant.RestToeL.ToString("F4") + " R " + plant.RestToeR.ToString("F4"));
                Vector3 la, lb, ra, rb;
                plant.SoleAxes(true, out la, out lb); plant.SoleAxes(false, out ra, out rb);
                sb.AppendLine("foot local lat L " + la.ToString("F3") + " R " + ra.ToString("F3") + " fwd L " + lb.ToString("F3") + " R " + rb.ToString("F3"));
                var lf = a.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
                sb.AppendLine("foot world rot euler L " + lf.rotation.eulerAngles.ToString("F1") + " R " + rf.rotation.eulerAngles.ToString("F1"));
                rig.Dispose();
            }
            File.WriteAllText("Logs/motion/rig_symmetry.txt", sb.ToString());
            Debug.Log("[RigSymmetry] done");
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>Does every Mixamo FBX carry the same T-pose in its avatar definition? (A skeleton-only export may carry the first animation frame as its rest pose, which skews the retarget of every muscle.) Writes Logs/motion/mixamo_avatars.txt</summary>
    public static class MixamoAvatarDump
    {
        public static void Run()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory("Logs/motion");
            var sb = new StringBuilder();
            string[] keys = { "mixamorig:LeftUpLeg", "mixamorig:RightUpLeg", "mixamorig:LeftLeg", "mixamorig:RightLeg", "mixamorig:LeftFoot", "mixamorig:RightFoot", "mixamorig:LeftArm", "mixamorig:RightArm", "mixamorig:Spine", "mixamorig:Hips" };
            foreach (string f in Directory.GetFiles(MixamoImport.Folder, "*.fbx").OrderBy(x => x))
            {
                var mi = AssetImporter.GetAtPath(f.Replace("\\", "/")) as ModelImporter; if (mi == null) continue;
                var hd = mi.humanDescription;
                sb.Append(Path.GetFileNameWithoutExtension(f)).Append(" |");
                foreach (string k in keys)
                {
                    foreach (var s in hd.skeleton)
                        if (s.name.EndsWith(k.Substring(k.IndexOf(':') + 1)) || s.name == k) { sb.Append(' ').Append(k.Substring(k.IndexOf(':') + 1)).Append('=').Append(s.rotation.eulerAngles.ToString("F0")); break; }
                }
                sb.AppendLine();
            }
            File.WriteAllText("Logs/motion/mixamo_avatars.txt", sb.ToString());
            Debug.Log("[MixamoAvatarDump] done");
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>Which way does the pelvis face when a Mixamo take is played on the match body with and without root motion? Writes Logs/motion/heading.txt</summary>
    public static class MixamoHeading
    {
        public static void Run()
        {
            BatchWatchdog.Arm(10);
            var sb = new StringBuilder();
            var rig = AvRig.Create(PlayerSize.Medium); var A = rig.A;
            var hips = A.GetBoneTransform(HumanBodyBones.Hips); var lul = A.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rul = A.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var chest = A.GetBoneTransform(HumanBodyBones.Chest);
            sb.AppendLine("animator transform: " + A.transform.name + " euler " + A.transform.eulerAngles + " parent " + (A.transform.parent != null ? A.transform.parent.name + " " + A.transform.parent.eulerAngles : "-") + " go " + rig.Go.name + " " + rig.Go.transform.eulerAngles);
            foreach (string name in new[] { "Run_Standard_Run", "Jog_Jogging", "Sprint_Standard", "Run_Jog_Backward" })
            {
                var clip = MixamoImport.LoadClip(MixamoImport.Folder + "/" + name + ".fbx"); if (clip == null) continue;
                foreach (bool rm in new[] { false, true })
                {
                    A.applyRootMotion = rm; A.transform.position = Vector3.zero; A.transform.rotation = Quaternion.identity;
                    var g = PlayableGraph.Create("hd"); g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var o = AnimationPlayableOutput.Create(g, "o", A); var cp = AnimationClipPlayable.Create(g, clip); cp.SetApplyFootIK(false); o.SetSourcePlayable(cp);
                    cp.SetTime(0.2); g.Evaluate(0f);
                    Vector3 f = Vector3.Cross(rul.position - lul.position, Vector3.up); f.y = 0f;
                    float pel = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                    Vector3 cf = chest != null ? chest.forward : Vector3.zero;
                    sb.AppendLine(name + " rootMotion=" + rm + "  pelvis heading " + pel.ToString("0") + "  hips.forward " + hips.forward.ToString("F2") + "  animator yaw " + A.transform.eulerAngles.y.ToString("0"));
                    g.Destroy();
                }
            }
            File.WriteAllText("Logs/motion/heading.txt", sb.ToString());
            rig.Dispose();
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>Do the DraftFever source FBXs carry a real T-pose in their avatars, or the first frame of the take? Writes Logs/motion/draftfever_avatars.txt (left / right leg and arm rest rotations of up to 60 files).</summary>
    public static class DraftFeverAvatarDump
    {
        public static void Run()
        {
            BatchWatchdog.Arm(15);
            Directory.CreateDirectory("Logs/motion");
            var sb = new StringBuilder();
            int n = 0;
            string[] roots = { "Assets/_Licensed/DraftFever/Animations/BoNES_Yan", "Assets/_Licensed/DraftFever/Animations/BoNES_QB", "Assets/_Licensed/DraftFever/Animations/BoNES_Retargets", "Assets/_Licensed/DraftFever/Animations/BoNES_Extra", "Assets/_Licensed/DraftFever/Animations" };
            var seen = new HashSet<string>();
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string f in Directory.GetFiles(root, "*.fbx", SearchOption.TopDirectoryOnly).OrderBy(x => x))
                {
                    if (n >= 60 || !seen.Add(f)) continue;
                    var mi = AssetImporter.GetAtPath(f.Replace("\\", "/")) as ModelImporter; if (mi == null || mi.animationType != ModelImporterAnimationType.Human) continue;
                    var hd = mi.humanDescription; sb.Append(Path.GetFileNameWithoutExtension(f)).Append(" |");
                    foreach (string k in new[] { "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftArm", "RightArm" })
                        foreach (var s in hd.skeleton) if (s.name.EndsWith(k)) { sb.Append(' ').Append(k).Append('=').Append(s.rotation.eulerAngles.ToString("F0")); break; }
                    sb.AppendLine(); n++;
                }
            }
            File.WriteAllText("Logs/motion/draftfever_avatars.txt", sb.ToString());
            Debug.Log("[DraftFeverAvatarDump] files " + n);
        }
    }
}
