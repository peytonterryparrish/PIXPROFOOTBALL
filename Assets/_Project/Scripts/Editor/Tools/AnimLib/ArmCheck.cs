using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 ANIMLIB (BODIES report): finds the T-pose look inside a take: both hands at shoulder height, wide apart, arms out to the sides for a stretch of time (a calibration pose or a retarget
    /// glitch inside a block / get-off take). Output Logs/animlib/arm_check.csv for the clips of the lines (OL, DL, stances, blocks) or every clip (Logs/animlib/arm_all.txt present).
    /// </summary>
    public static class ArmCheck
    {
        public sealed class Hit { public int frames, firstFrame, runFrames; public float pct, firstT, runT; }

        /// <summary>Frames where the arms are out like a T-pose: hands within 30 cm of shoulder height, more than 85 cm apart and each more than 36 cm to its own side.</summary>
        public static bool[] ArmsOut(List<AnimRig.Frame> fr)
        {
            var o = new bool[fr.Count];
            for (int i = 0; i < fr.Count; i++)
            {
                var f = fr[i];
                float sy = 0.5f * (f.P[AnimRig.LUA].y + f.P[AnimRig.RUA].y);
                Vector3 right = Vector3.Cross(Vector3.up, AnimRig.Forward(f)); Vector3 mid = 0.5f * (f.P[AnimRig.LUA] + f.P[AnimRig.RUA]);
                float lx = Vector3.Dot(f.P[AnimRig.LH] - mid, right), rx = Vector3.Dot(f.P[AnimRig.RH] - mid, right);
                Vector3 d = f.P[AnimRig.LH] - f.P[AnimRig.RH]; d.y = 0f;
                o[i] = Mathf.Abs(f.P[AnimRig.LH].y - sy) < 0.30f && Mathf.Abs(f.P[AnimRig.RH].y - sy) < 0.30f && d.magnitude > 0.85f && rx > 0.36f && lx < -0.36f;
            }
            return o;
        }

        public static Hit Measure(bool[] o, float rate)
        {
            var h = new Hit { firstFrame = -1 };
            int run = 0;
            for (int i = 0; i < o.Length; i++)
            {
                if (o[i]) { h.frames++; run++; if (run > h.runFrames) { h.runFrames = run; if (run >= 3 && h.firstFrame < 0) h.firstFrame = i - run + 1; } } else run = 0;
            }
            h.pct = 100f * h.frames / Mathf.Max(1, o.Length); h.runT = h.runFrames / rate; h.firstT = h.firstFrame >= 0 ? h.firstFrame / rate : -1f;
            return h;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(40);
            Directory.CreateDirectory(AnimLibBatch.OutDir);
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            bool all = File.Exists("Logs/animlib/arm_all.txt");
            var sb = new StringBuilder("name,len,frames,pct,firstT,longestRunT,score\n");
            int n = 0, hit = 0;
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipCleaner.CleanDir }))
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)); if (c == null) continue;
                string nm = c.name.ToLowerInvariant();
                bool line = all || nm.Contains("oline") || nm.Contains("olm_") || nm.Contains("dline") || nm.Contains("stance") || nm.Contains("block") || nm.Contains("pocket") || nm.Contains("trench") || nm.Contains("3point") || nm.Contains("4point") || nm.Contains("press_jam") || nm.Contains("shove") || nm.Contains("swim") || nm.Contains("bull") || nm.Contains("wrong_arm");
                if (!line) continue;
                n++;
                var fr = rig.Sample(c, 0f, Mathf.Min(c.length, 24f), 30f);
                var h = Measure(ArmsOut(fr), 30f);
                if (h.runFrames >= 3) { hit++; sb.AppendLine("\"" + c.name + "\"," + c.length.ToString("0.00") + "," + h.frames + "," + h.pct.ToString("0") + "," + h.firstT.ToString("0.00") + "," + h.runT.ToString("0.00") + ","); }
            }
            foreach (string pn in new[] { "OLine01_016__OLINE_MAN_3_stance_to_Block_Run_Foot_Work_Left_001_s01", "OLine01_016__OLINE_MAN_3_stance_to_Block_Run_Foot_Work_Left_002_s01" })
            {
                var pc = StickSheet.FindClip(pn); if (pc == null) continue;
                var pf = rig.Sample(pc, 0f, Mathf.Min(pc.length, 4f), 30f); var sbp = new StringBuilder(pn + " len " + pc.length.ToString("0.00") + ": handY-shoulderY / spread every 0.1s: ");
                for (int i = 0; i < pf.Count; i += 3) { var f = pf[i]; float sy = 0.5f * (f.P[AnimRig.LUA].y + f.P[AnimRig.RUA].y); Vector3 dd = f.P[AnimRig.LH] - f.P[AnimRig.RH]; dd.y = 0f; sbp.Append((f.P[AnimRig.LH].y - sy).ToString("0.00") + "/" + (f.P[AnimRig.RH].y - sy).ToString("0.00") + "/" + dd.magnitude.ToString("0.00") + " "); }
                Debug.Log("[ArmProfile] " + sbp);
            }
            rig.Dispose();
            File.WriteAllText(AnimLibBatch.OutDir + "/arm_check.csv", sb.ToString());
            Debug.Log("[ArmCheck] checked " + n + " clips, arms-out runs (>= 0.1 s) in " + hit);
        }

        public static void RunAndSheets() { Run(); AnimLibBatch.Sheets(); }
    }
}
