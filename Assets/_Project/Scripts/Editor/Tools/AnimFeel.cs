using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.7.0 Track A measurements (offline, no play mode): per locomotion clip the toe-out angle of each foot during its stance phase (degrees, + = the toes point away from the body's centre line),
    /// measured on the legacy body and on the photoreal body. Used to find the "right foot splays out" defect (A-001). Output: D:/PIXFootball/anim_qb/foot_splay.csv.
    /// </summary>
    public static class AnimFeel
    {
        public const string OutDir = "D:/PIXFootball/anim_qb";

        static readonly string[] LocoClips =
        {
            "PIX_Walk_Cycle", "JogCycleJR", "RunCycleJR_1", "SprintCycleJR_1",
            "BGS__BGS_CB_ShuffleBack", "BGS__BGS_CB_ShuffleLeft", "BGS__BGS_CB_ShuffleRight",
            "BGS__BGS_Sprint08_FL_Loop", "BGS__BGS_Sprint08_FR_Loop", "BGS__BGS_Run07_FL_Loop_BC", "BGS__BGS_Run07_FR_Loop_BC",
            "BGS__MOB1_Jog_F_Loop_BC", "BGS__MOB1_Run_F_Loop_BC", "BGS__BGS_Sprint08_F_Loop_BC",
            "BGS__MOB1_Jog_FL_Loop_BC", "BGS__MOB1_Jog_FR_Loop_BC", "BGS__MOB1_Run_FL_Loop_BC", "BGS__MOB1_Run_FR_Loop_BC",
            "BGS__BGS_Sprint08_FL_Loop_BC", "BGS__BGS_Sprint08_FR_Loop_BC",
            "BGS__MOB1_Jog_L_Loop_BC", "BGS__MOB1_Jog_R_Loop_BC", "BGS__MOB1_Run_L_Loop_BC", "BGS__MOB1_Run_R_Loop_BC",
            "BGS__MOB1_Jog_B_Loop_BC", "BGS__MOB1_Jog_BL_BkPd_Loop_BC", "BGS__MOB1_Jog_BR_BkPd_Loop_BC"
        };

        public static void FootSplay()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder("body,clip,length,leftToeOut,rightToeOut,leftStanceFrames,rightStanceFrames,hipTravelMps\n");
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            foreach (string key in new[] { "player_medium", "photo_player_medium" })
            {
                var prefab = lib != null ? lib.Prefab(key) : null;
                if (prefab == null) { sb.AppendLine(key + ",MISSING"); continue; }
                var rig = AnimRig.Create(prefab);
                foreach (string n in LocoClips)
                {
                    var clip = AnimatorControllerBuilder.FindClipByName(n);
                    if (clip == null) { sb.AppendLine(key + "," + n + ",missing clip"); continue; }
                    Measure(rig, clip, key, sb);
                }
                rig.Dispose();
            }
            File.WriteAllText(OutDir + "/foot_splay.csv", sb.ToString());
            Debug.Log("[AnimFeel] foot splay written\n" + sb);
        }

        /// <summary>Dumps every variant (name, length, loop flag, measured moment) of the animation keys this track uses: D:/PIXFootball/anim_qb/keys.txt.</summary>
        public static void DumpKeys()
        {
            BatchWatchdog.Arm(5);
            Directory.CreateDirectory(OutDir);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var tb = ClipTimingTable.Load();
            var sb = new StringBuilder();
            foreach (string key in new List<string>(lib.ClipKeys))
            {
                bool want = key.StartsWith("qb_") || key.StartsWith("stance") || key.StartsWith("handoff") || key.StartsWith("celebrate") || key.StartsWith("cb_") || key.StartsWith("idle") || key.StartsWith("ol_pocket") || key.StartsWith("bc_stop") || key.StartsWith("stop_")
                    || key.StartsWith("catch_") || key == "interception" || key.StartsWith("wr_") || key.StartsWith("dl_") || key.StartsWith("huddle") || key.StartsWith("fall") || key.StartsWith("be_") || key.StartsWith("tackle") || key.StartsWith("contact_");
                if (!want) continue;
                sb.AppendLine("## " + key + " (" + lib.ClipVariantCount(key) + ")");
                for (int i = 0; i < lib.ClipVariantCount(key); i++)
                {
                    var c = lib.ClipVariant(key, i); if (c == null) continue;
                    float t = 0f, t2 = 0f; if (tb != null) tb.TryGet(c.name, out t, out t2);
                    sb.AppendLine("  " + i + " " + c.name + " len=" + c.length.ToString("0.00") + (c.isLooping ? " loop" : "") + (t > 0f ? " moment=" + t.ToString("0.00") : ""));
                }
            }
            File.WriteAllText(OutDir + "/keys.txt", sb.ToString());
            Debug.Log("[AnimFeel] keys dumped");
        }

        /// <summary>Per clip profile every 0.2 s: hips speed (m/s, + = along the body heading at the start), hips height, hand span, right hand height above the hips (D:/PIXFootball/anim_qb/profile.txt). Clip names come from Logs/profile_in.txt.</summary>
        public static void Profile()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory(OutDir);
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var sb = new StringBuilder();
            foreach (var raw in File.ReadAllLines("Logs/profile_in.txt"))
            {
                string name = raw.Trim(); if (name.Length == 0) continue;
                var clip = StickSheet.FindClip(name);
                if (clip == null) { sb.AppendLine("## " + name + " MISSING"); continue; }
                var fr = rig.Sample(clip, 0f, Mathf.Min(clip.length, 12f), 30f);
                sb.AppendLine("## " + clip.name + " len=" + clip.length.ToString("0.00"));
                Vector3 fwd0 = AnimRig.Forward(fr[0]);
                for (int i = 0; i < fr.Count; i += 6)
                {
                    int j = Mathf.Min(fr.Count - 1, i + 6);
                    Vector3 d = fr[j].P[AnimRig.Hips] - fr[i].P[AnimRig.Hips]; d.y = 0f;
                    float dt = Mathf.Max(1e-3f, (j - i) / 30f);
                    float along = Vector3.Dot(d, fwd0) / dt, side = Vector3.Dot(d, Vector3.Cross(Vector3.up, fwd0)) / dt;
                    var f = fr[i];
                    float span = Vector3.Distance(f.P[AnimRig.LH], f.P[AnimRig.RH]);
                    float rhUp = f.P[AnimRig.RH].y - f.P[AnimRig.Hips].y;
                    float lean = Vector3.Angle(Vector3.up, f.P[AnimRig.Chest] - f.P[AnimRig.Hips]);
                    sb.AppendLine("  t=" + (i / 30f).ToString("0.0") + " hipsAlong=" + along.ToString("0.0") + " side=" + side.ToString("0.0") + " hipsY=" + f.P[AnimRig.Hips].y.ToString("0.00") + " handSpan=" + span.ToString("0.00") + " rhUp=" + rhUp.ToString("0.00") + " lean=" + lean.ToString("0"));
                }
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/profile.txt", sb.ToString());
            Debug.Log("[AnimFeel] profile written");
        }

        static void Measure(AnimRig rig, AnimationClip clip, string body, StringBuilder sb)
        {
            float len = Mathf.Max(0.3f, clip.length);
            var frames = rig.Sample(clip, 0f, len, 30f);
            float minL = 9f, minR = 9f;
            foreach (var f in frames) { minL = Mathf.Min(minL, f.P[AnimRig.LF].y); minR = Mathf.Min(minR, f.P[AnimRig.RF].y); }
            float sumL = 0f, sumR = 0f; int nL = 0, nR = 0;
            foreach (var f in frames)
            {
                Vector3 fwd = AnimRig.Forward(f);
                if (f.P[AnimRig.LF].y < minL + 0.05f) { sumL += ToeOut(f, fwd, true); nL++; }
                if (f.P[AnimRig.RF].y < minR + 0.05f) { sumR += ToeOut(f, fwd, false); nR++; }
            }
            Vector3 d = frames[frames.Count - 1].P[AnimRig.Hips] - frames[0].P[AnimRig.Hips]; d.y = 0f;
            sb.AppendLine(body + "," + clip.name + "," + len.ToString("0.00") + "," + (nL > 0 ? (sumL / nL).ToString("0.0") : "-") + "," + (nR > 0 ? (sumR / nR).ToString("0.0") : "-") + "," + nL + "," + nR + "," + (d.magnitude / len).ToString("0.00"));
        }

        static float ToeOut(AnimRig.Frame f, Vector3 fwd, bool left)
        {
            int foot = left ? AnimRig.LF : AnimRig.RF, toe = left ? AnimRig.LT : AnimRig.RT;
            Vector3 t = f.P[toe] - f.P[foot]; t.y = 0f;
            if (t.sqrMagnitude < 1e-6f) return 0f;
            float a = Vector3.SignedAngle(fwd, t, Vector3.up);     // + = toes point to the body's right
            return left ? -a : a;
        }
    }
}
