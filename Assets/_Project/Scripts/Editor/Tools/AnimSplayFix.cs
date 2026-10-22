using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.7.0 A-001 data fix: the locomotion cycles plant their feet 45-80 degrees off the line of travel (the ball-carrier run / sprint loops: right toes OUT, the plain run / sprint cycles: right toes IN).
    /// For every cycle of the two locomotion trees the toe-out of each foot at ground contact is measured on the photoreal body; a constant offset is added to the three twist muscles of that leg
    /// (upper leg, lower leg, foot - so no joint carries more than its share) until the toe-out is about 6 degrees. The measured slope per muscle unit is used, so the sign convention of the clip does not matter.
    /// The original of every changed clip is kept in Animations/Clean/_backup_splay. Re-running CleanBatch regenerates the clips, so run this again afterwards.
    /// Run: -executeMethod PixFootball.EditorTools.AnimSplayFix.Run   Report: D:/PIXFootball/anim_qb/splay_fix.txt
    /// </summary>
    public static class AnimSplayFix
    {
        const float Target = 6f, Tolerance = 9f;
        static readonly string[] Parts = { "Upper Leg Twist In-Out", "Lower Leg Twist In-Out", "Foot Twist In-Out" };

        public static readonly string[] Clips =
        {
            "PIX_Walk_Cycle", "JogCycleJR", "RunCycleJR_1", "SprintCycleJR_1",
            "BGS__BGS_Sprint08_FL_Loop", "BGS__BGS_Sprint08_FR_Loop", "BGS__BGS_Run07_FL_Loop_BC", "BGS__BGS_Run07_FR_Loop_BC",
            "BGS__MOB1_Jog_F_Loop_BC", "BGS__MOB1_Run_F_Loop_BC", "BGS__BGS_Sprint08_F_Loop_BC",
            "BGS__MOB1_Jog_FL_Loop_BC", "BGS__MOB1_Jog_FR_Loop_BC", "BGS__MOB1_Run_FL_Loop_BC", "BGS__MOB1_Run_FR_Loop_BC",
            "BGS__BGS_Sprint08_FL_Loop_BC", "BGS__BGS_Sprint08_FR_Loop_BC",
            "BGS__MOB1_Jog_L_Loop_BC", "BGS__MOB1_Jog_R_Loop_BC", "BGS__MOB1_Run_L_Loop_BC", "BGS__MOB1_Run_R_Loop_BC",
            "BGS__MOB1_Jog_B_Loop_BC", "BGS__MOB1_Jog_BL_BkPd_Loop_BC", "BGS__MOB1_Jog_BR_BkPd_Loop_BC",
            "BGS__MOB1_Jog_B_Loop_REMAP.001", "BGS__MOB1_Jog_BL_BkPd_Loop_REMAP", "BGS__MOB1_Jog_BR_BkPd_Loop_REMAP",
            "BGS__BGS_CB_ShuffleBack", "BGS__BGS_CB_ShuffleBackLeft", "BGS__BGS_CB_ShuffleBackRight", "BGS__BGS_CB_ShuffleLeft", "BGS__BGS_CB_ShuffleRight", "BGS__BGS_CB_ShuffleFoLeft"
        };

        public static void Run()
        {
            BatchWatchdog.Arm(15);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var prefab = lib != null ? lib.Prefab("photo_player_medium") : null;
            if (prefab == null) { Debug.LogError("[SplayFix] no photo_player_medium"); return; }
            var rig = AnimRig.Create(prefab);
            var sb = new StringBuilder("clip,toeL_before,toeR_before,uL,uR,toeL_after,toeR_after\n");
            string backup = ClipCleaner.CleanDir + "/_backup_splay";
            if (!AssetDatabase.IsValidFolder(backup)) AssetDatabase.CreateFolder(ClipCleaner.CleanDir, "_backup_splay");
            int changed = 0;
            foreach (string n in Clips)
            {
                var clip = AnimatorControllerBuilder.FindClipByName(n);
                string path = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
                if (clip == null || path == null || !path.StartsWith(ClipCleaner.CleanDir) || !path.EndsWith(".anim")) { sb.AppendLine(n + ",skipped (not a Clean/ clip)"); continue; }
                float l0, r0; Measure(rig, clip, out l0, out r0);
                float uL = 0f, uR = 0f;
                if (!float.IsNaN(l0) && Mathf.Abs(l0 - Target) > Tolerance) uL = Solve(rig, clip, true, l0);
                if (!float.IsNaN(r0) && Mathf.Abs(r0 - Target) > Tolerance) uR = Solve(rig, clip, false, r0);
                if (uL != 0f || uR != 0f)
                {
                    string bp = backup + "/" + Path.GetFileName(path);
                    if (!File.Exists(bp)) AssetDatabase.CopyAsset(path, bp);
                    if (uL != 0f) Shift(clip, true, uL);
                    if (uR != 0f) Shift(clip, false, uR);
                    EditorUtility.SetDirty(clip); changed++;
                }
                float l1, r1; Measure(rig, clip, out l1, out r1);
                sb.AppendLine(n + "," + l0.ToString("0") + "," + r0.ToString("0") + "," + uL.ToString("0.00") + "," + uR.ToString("0.00") + "," + l1.ToString("0") + "," + r1.ToString("0"));
            }
            AssetDatabase.SaveAssets();
            rig.Dispose();
            Directory.CreateDirectory("D:/PIXFootball/anim_qb");
            File.WriteAllText("D:/PIXFootball/anim_qb/splay_fix.txt", sb.ToString());
            Debug.Log("[SplayFix] changed " + changed + " clips\n" + sb);
        }

        /// <summary>Mean toe-out (deg, + = outward) of each foot over the frames in which it is within 2.5 cm of its lowest point (ground contact); NaN when a foot never gets that low.</summary>
        static void Measure(AnimRig rig, AnimationClip clip, out float left, out float right)
        {
            var fr = rig.Sample(clip, 0f, Mathf.Max(0.3f, clip.length), 30f);
            left = Contact(fr, true); right = Contact(fr, false);
        }

        static float Contact(List<AnimRig.Frame> fr, bool leftFoot)
        {
            int foot = leftFoot ? AnimRig.LF : AnimRig.RF, toe = leftFoot ? AnimRig.LT : AnimRig.RT;
            float min = 9f; foreach (var f in fr) min = Mathf.Min(min, f.P[foot].y);
            double sum = 0; int n = 0;
            foreach (var f in fr)
            {
                if (f.P[foot].y > min + 0.025f) continue;
                Vector3 fwd = AnimRig.Forward(f), t = f.P[toe] - f.P[foot]; t.y = 0f;
                if (t.magnitude < 0.05f) continue;
                float a = Vector3.SignedAngle(fwd, t, Vector3.up);
                sum += leftFoot ? -a : a; n++;
            }
            return n > 0 ? (float)(sum / n) : float.NaN;
        }

        /// <summary>The offset (muscle units, added to all three twist muscles of the leg) that moves the contact toe-out to the target, from the measured slope.</summary>
        static float Solve(AnimRig rig, AnimationClip clip, bool left, float current)
        {
            const float probe = 0.2f;
            var trial = Object.Instantiate(clip);
            Shift(trial, left, probe);
            float l, r; Measure(rig, trial, out l, out r);
            Object.DestroyImmediate(trial);
            float moved = left ? l : r;
            if (float.IsNaN(moved)) return 0f;
            float slope = (moved - current) / probe;
            if (Mathf.Abs(slope) < 20f) return 0f;     // the twist muscles barely move this foot (a toe-walking take): leave it
            return Mathf.Clamp((Target - current) / slope, -0.9f, 0.9f);
        }

        static void Shift(AnimationClip clip, bool left, float u)
        {
            string side = left ? "Left " : "Right ";
            foreach (string part in Parts)
            {
                var b = EditorCurveBinding.FloatCurve("", typeof(Animator), side + part);
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null) c = AnimationCurve.Constant(0f, Mathf.Max(0.05f, clip.length), 0f);
                var keys = c.keys;
                for (int i = 0; i < keys.Length; i++) keys[i].value = Mathf.Clamp(keys[i].value + u, -1f, 1f);
                c.keys = keys;
                AnimationUtility.SetEditorCurve(clip, b, c);
            }
        }
    }
}
