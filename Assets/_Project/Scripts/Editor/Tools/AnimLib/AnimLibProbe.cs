using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 ANIMLIB: inventory of what the game has (FBX takes per source folder, cleaned clips, clips registered in the ArtLibrary) and a probe of the hand data
    /// (finger muscles) so the hand checks are tuned on real numbers. Output: Logs/animlib/*.csv|txt. Batch: PixFootball.EditorTools.AnimLibProbe.Run
    /// </summary>
    public static class AnimLibProbe
    {
        public const string OutDir = "Logs/animlib";
        const string AnimRoot = "Assets/_Licensed/DraftFever/Animations";

        public static void Run()
        {
            BatchWatchdog.Arm(25);
            Directory.CreateDirectory(OutDir);
            Inventory();
            JointProbe();
            Debug.Log("[AnimLibProbe] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void HandsOnlyProbe()
        {
            Directory.CreateDirectory(OutDir); HandProbe();
            var sb = new StringBuilder();
            foreach (string cn in new[] { "CatchNormalBackCHigh", "TackleWrapBackCHighDmo_A", "BGS__BGS_Run07_F_Loop_BC" })
            {
                var clip = StickSheet.FindClip(cn); if (clip == null) continue;
                var q = PoseSeq.FromClip(clip, 0f, Mathf.Min(clip.length, 10f), 30f); int last = q.N - 1;
                sb.Append("SPREAD " + cn + " (last frame, Left idx/mid/ring/little): ");
                foreach (string f in new[] { "Index", "Middle", "Ring", "Little" }) { int mi = System.Array.IndexOf(HumanTrait.MuscleName, "Left " + f + " Spread"); sb.Append(q.M[mi][last].ToString("0.00") + " "); }
                sb.AppendLine();
            }
            File.AppendAllText(OutDir + "/hand_probe.txt", sb.ToString());
            AnimLibBatch.Done();
        }

        public static void Inventory()
        {
            var sb = new StringBuilder("folder,fbx,clips,seconds\n");
            var perFolder = new Dictionary<string, int[]>();
            var perFolderSec = new Dictionary<string, float>();
            var allRaw = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { AnimRoot }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                string rel = p.Substring(AnimRoot.Length + 1); string folder = rel.Contains("/") ? rel.Substring(0, rel.IndexOf('/')) : "(root)";
                int[] a; if (!perFolder.TryGetValue(folder, out a)) perFolder[folder] = a = new int[2];
                a[0]++;
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                {
                    if (c.name.StartsWith("__preview")) continue;
                    a[1]++; float s; perFolderSec.TryGetValue(folder, out s); perFolderSec[folder] = s + c.length;
                    if (!allRaw.ContainsKey(c.name)) allRaw[c.name] = folder;
                }
            }
            int cleanN = 0; float cleanSec = 0f;
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipCleaner.CleanDir }))
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)); if (c == null) continue; cleanN++; cleanSec += c.length;
            }
            foreach (var kv in perFolder.OrderBy(k => k.Key)) sb.AppendLine(kv.Key + "," + kv.Value[0] + "," + kv.Value[1] + "," + perFolderSec[kv.Key].ToString("0"));
            sb.AppendLine("Clean(.anim)," + cleanN + "," + cleanN + "," + cleanSec.ToString("0"));
            File.WriteAllText(OutDir + "/inventory_folders.csv", sb.ToString());

            var lib = ArtLibrary.Load();
            var rows = new StringBuilder("clip,keys,source,length\n");
            var byClip = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var srcOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var lenOf = new Dictionary<string, float>(StringComparer.Ordinal);
            if (lib != null)
                foreach (var e in lib.clips)
                {
                    if (e == null || e.clip == null) continue;
                    List<string> l; if (!byClip.TryGetValue(e.clip.name, out l)) byClip[e.clip.name] = l = new List<string>();
                    if (!l.Contains(e.key)) l.Add(e.key);
                    srcOf[e.clip.name] = e.source; lenOf[e.clip.name] = e.length;
                }
            foreach (var kv in byClip.OrderBy(k => k.Key, StringComparer.Ordinal)) rows.AppendLine(Q(kv.Key) + "," + Q(string.Join("+", kv.Value)) + "," + Q(srcOf[kv.Key]) + "," + lenOf[kv.Key].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllText(OutDir + "/lib_clips.csv", rows.ToString());

            // every clean clip name (registered or not)
            var cl = new StringBuilder("clip,length,registered\n");
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipCleaner.CleanDir }))
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)); if (c == null) continue;
                cl.AppendLine(Q(c.name) + "," + c.length.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," + (byClip.ContainsKey(c.name) ? 1 : 0));
            }
            File.WriteAllText(OutDir + "/clean_clips.csv", cl.ToString());
            Debug.Log("[AnimLibProbe] raw clips " + allRaw.Count + " clean " + cleanN + " registered clips " + byClip.Count + " lib entries " + (lib != null ? lib.clips.Count : 0));
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }

        public static void JointProbe()
        {
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var sb = new StringBuilder();
            string[] sample = { "Player_Standing_Idle", "JogCycleJR", "SprintCycleJR_1", "3pstance_aggressive_001", "CatchNormalBackCHigh", "BGS__BGS_CB_Idle_M2M", "Avoid45degR", "TackleWrapBackCHighDmo_A", "BONES_QB_pass_quick_bullet_001_s01" };
            string[] ms = { "Left Lower Leg Stretch", "Right Lower Leg Stretch", "Left Forearm Stretch", "Right Forearm Stretch" };
            foreach (string cn in sample)
            {
                var clip = StickSheet.FindClip(cn); if (clip == null) continue;
                var s = PoseSeq.FromClip(clip, 0f, Mathf.Min(clip.length, 10f), 30f);
                var fr = rig.Sample(clip, 0f, Mathf.Min(clip.length, 10f), 30f);
                sb.Append(clip.name + ": ");
                foreach (string m in ms)
                {
                    int mi = Array.IndexOf(HumanTrait.MuscleName, m); float mn = 9, mx = -9; int imn = 0, imx = 0;
                    for (int i = 0; i < s.N; i++) { float v = s.M[mi][i]; if (v < mn) { mn = v; imn = i; } if (v > mx) { mx = v; imx = i; } }
                    bool left = m.StartsWith("Left"); bool knee = m.Contains("Leg");
                    Func<int, float> flex = i => knee ? AnimRig.KneeFlex(fr[Mathf.Min(i, fr.Count - 1)], left) : AnimRig.ElbowFlex(fr[Mathf.Min(i, fr.Count - 1)], left);
                    sb.Append(m.Replace(" Stretch", "") + " v[" + mn.ToString("0.00") + ".." + mx.ToString("0.00") + "] flex@min=" + flex(imn).ToString("0") + " flex@max=" + flex(imx).ToString("0") + " | ");
                }
                var f0 = fr[Mathf.Min(10, fr.Count - 1)];
                sb.AppendLine("   frame10 ankleY L/R " + f0.P[AnimRig.LF].y.ToString("0.000") + "/" + f0.P[AnimRig.RF].y.ToString("0.000") + " toeY " + f0.P[AnimRig.LT].y.ToString("0.000") + "/" + f0.P[AnimRig.RT].y.ToString("0.000") + " hips " + f0.P[AnimRig.Hips].y.ToString("0.000"));
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/joint_probe.txt", sb.ToString());
        }

        // ------------------------------------------------------------------------------------------------ hands
        public static void HandProbe()
        {
            var names = HumanTrait.MuscleName;
            var sb = new StringBuilder();
            sb.AppendLine("muscle count " + names.Length);
            for (int m = 55; m < Mathf.Min(names.Length, 75); m++) sb.AppendLine(m + " " + names[m] + " [" + HumanTrait.GetMuscleDefaultMin(m).ToString("0") + "," + HumanTrait.GetMuscleDefaultMax(m).ToString("0") + "]");
            foreach (string w in new[] { "Spine Twist", "Chest Twist", "UpperChest Twist", "Neck Turn", "Head Turn", "Left Hand", "Left Forearm", "Left Lower Leg", "Spine Front", "Spine Left", "Chest Front", "UpperChest Front" })
                for (int m = 0; m < names.Length; m++) if (names[m].StartsWith(w)) sb.AppendLine(m + " " + names[m] + " [" + HumanTrait.GetMuscleDefaultMin(m).ToString("0") + "," + HumanTrait.GetMuscleDefaultMax(m).ToString("0") + "]");

            // finger statistics of representative clips
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            // curl direction per finger muscle: pose the avatar with the muscle at -1 and +1 and measure the fingertip distance to the wrist
            try
            {
                var anim = rig.Anim;
                var h = new HumanPoseHandler(anim.avatar, anim.transform);
                var pose = new HumanPose(); h.GetHumanPose(ref pose);
                var tip = anim.GetBoneTransform(HumanBodyBones.LeftIndexDistal); var hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                var tt = anim.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
                sb.AppendLine("pose muscles " + pose.muscles.Length);
                foreach (string mn in new[] { "Left Index 1 Stretched", "Left Index 2 Stretched", "Left Thumb 1 Stretched", "Left Thumb 2 Stretched", "Left Index Spread" })
                {
                    int mi = Array.IndexOf(names, mn); if (mi < 0) continue;
                    foreach (float v in new[] { -1f, -0.5f, 0f, 0.5f, 1f })
                    {
                        var p2 = pose; p2.muscles = (float[])pose.muscles.Clone(); p2.muscles[mi] = v;
                        h.SetHumanPose(ref p2);
                        sb.AppendLine(mn + " = " + v.ToString("0.0") + " : index tip -> hand " + Vector3.Distance(tip.position, hand.position).ToString("0.000") + ", thumb tip -> hand " + Vector3.Distance(tt.position, hand.position).ToString("0.000"));
                    }
                }
                h.SetHumanPose(ref pose);
                h.Dispose();
            }
            catch (Exception ex) { sb.AppendLine("calibration failed: " + ex.Message); }

            string[] sample = { "Player_Standing_Idle", "JogCycleJR", "SprintCycleJR_1", "CatchNormalBackCHigh", "IntNormalBackCHigh", "TackleWrapBackCHighDmo_A", "HB_Stiff_Arm_003_A", "BONES_QB_pass_quick_bullet_001_s01",
                "BONES_QB_undercenter_to_catch_loop_001", "BGS__BGS_Run07_F_Loop_BC", "BGS__BGS_Sprint04_F_Loop_BC", "3pstance_aggressive_001", "DlineIdle3ptR", "BGS__BGS_CB_Idle_M2M", "CelebrationTouchdownBreakdance", "YAN__K_kickoff_kick_00_Rework",
                "BONES_QB_celebration_001_s01", "HandoffInormalHbDraw_A", "BlockDriveCPass_A", "BGS__BGS_FIX_MCU_am_Stand_Idle_09_FingerGuns", "AvoidAnything" };
            sb.AppendLine("\nname | frames | per hand: mean(stretch of the 4 fingers) std over time | thumb mean std");
            foreach (string cn in sample)
            {
                var clip = StickSheet.FindClip(cn); if (clip == null) { sb.AppendLine(cn + " : missing"); continue; }
                var s = PoseSeq.FromClip(clip, 0f, Mathf.Min(clip.length, 20f), 30f);
                sb.Append(clip.name + " | " + s.N + " | ");
                foreach (string side in new[] { "Left", "Right" })
                {
                    foreach (string f in new[] { "Index 1", "Middle 1", "Ring 1", "Little 1", "Thumb 1" })
                    {
                        int mi = Array.IndexOf(names, side + " " + f + " Stretched"); if (mi < 0) continue;
                        float mean = 0f, sd = 0f; for (int i = 0; i < s.N; i++) mean += s.M[mi][i]; mean /= s.N;
                        for (int i = 0; i < s.N; i++) { float d = s.M[mi][i] - mean; sd += d * d; }
                        sd = Mathf.Sqrt(sd / s.N);
                        sb.Append(side[0] + f.Substring(0, 2) + " " + mean.ToString("0.00") + "/" + sd.ToString("0.00") + "  ");
                    }
                    sb.Append(" || ");
                }
                sb.AppendLine();
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/hand_probe.txt", sb.ToString());
        }
    }
}
