using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 MOTION: keeps the Mixamo takes inside human range. The muscle statistics of the takes (<see cref="MuscleReport"/>) show a systematic offset on the LEFT arm chain (left shoulder front-back mean +1.63 against -0.22 on the right,
    /// left forearm twist mean +0.81 against +0.02: forearm twist beyond the +-90 degree limit in 17 percent of the samples, shoulders depressed 10 degrees on both sides): the rest pose (T-pose) of the skeleton-only export fixes the zero of every muscle
    /// and the twist of the left clavicle / forearm of that pose is off. The curves are re-zeroed where a left muscle's mean is more than 0.5 away from its right twin (the left takes the right's mean), the shoulders' down-up mean is centred,
    /// and whatever still lies beyond 0.9 of the limit is soft-limited (tanh) so no muscle reaches or passes +-1. Cleaned copies are written next to the clips (Mixamo/Clean/*_C.anim); the originals stay untouched.
    /// </summary>
    public static class MotionClean
    {
        public const float SoftStart = 0.9f;

        public static Dictionary<string, float> Deltas(Dictionary<string, List<float>> stats)
        {
            var d = new Dictionary<string, float>();
            Func<string, float> mean = k => { List<float> l; return stats.TryGetValue(k, out l) && l.Count > 0 ? l.Average() : 0f; };
            foreach (string k in stats.Keys.ToList())
            {
                if (!k.StartsWith("Left ")) continue;
                string r = "Right " + k.Substring(5);
                if (!stats.ContainsKey(r) || k.Contains("Hand") || k.Contains("Toes")) continue;
                float diff = mean(r) - mean(k);
                if (Mathf.Abs(diff) > 0.5f) d[k] = diff;
            }
            // finger poses of the export: relaxed hands curled beyond the finger limit in every frame; centred on a relaxed hand
            foreach (string k in stats.Keys.ToList()) if (MuscleReport.IsFinger(k)) { float m = mean(k); if (Mathf.Abs(m) > 0.9f) d[k] = (k.Contains("Stretched") ? -0.35f : 0f) - m; }
            foreach (string k in new[] { "Left Shoulder Down-Up", "Right Shoulder Down-Up" })
            {
                float m = mean(k); if (m < -0.4f) d[k] = (d.ContainsKey(k) ? d[k] : 0f) + (-0.15f - m);
            }
            return d;
        }

        static float Soft(float v)
        {
            float a = Mathf.Abs(v);
            if (a <= SoftStart) return v;
            float s = 1f - SoftStart;
            return Mathf.Sign(v) * (SoftStart + s * 0.97f * (float)Math.Tanh((a - SoftStart) / s));
        }

        /// <summary>Re-zeroed and soft-limited copy; <paramref name="before"/> / <paramref name="after"/> count the samples beyond +-1.</summary>
        public static AnimationClip Clean(AnimationClip src, string name, Dictionary<string, float> deltas, out int before, out int after)
        {
            before = after = 0;
            string dir = MixamoImport.Folder + "/Clean";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(MixamoImport.Folder, "Clean");
            string path = dir + "/" + name + "_C.anim";
            AssetDatabase.DeleteAsset(path);
            var c = UnityEngine.Object.Instantiate(src); c.name = name + "_C";
            var names = new HashSet<string>(HumanTrait.MuscleName);
            foreach (var b in AnimationUtility.GetCurveBindings(c))
            {
                string cn = MuscleReport.Canon(b.propertyName); if (b.type != typeof(Animator) || !names.Contains(cn)) continue;
                var cv = AnimationUtility.GetEditorCurve(c, b); var keys = cv.keys; float dl; deltas.TryGetValue(cn, out dl);
                for (int i = 0; i < keys.Length; i++)
                {
                    float v = keys[i].value; if (Mathf.Abs(v) > 1f) before++;
                    float n = Soft(v + dl); if (Mathf.Abs(n) > 1f) after++;
                    keys[i].value = n;
                }
                cv.keys = keys; for (int i = 0; i < cv.length; i++) cv.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(c, b, cv);
            }
            AssetDatabase.CreateAsset(c, path);
            return c;
        }
    }
}
