using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 MOTION: humanoid muscle statistics of the Mixamo takes (the curves are in normalised muscle space: +-1 is the limit of the muscle). Per muscle: mean, 5 / 95 percentile, largest |value|, share of samples beyond the limit;
    /// writes Logs/motion/muscles.txt. Used to find muscles the retarget pushes beyond human range (forearm twist, hand twist, shin twist ...) and as the input of the re-zeroing in <see cref="MotionClean"/>.
    /// </summary>
    public static class MuscleReport
    {
        /// <summary>The curve property of a finger muscle ("LeftHand.Index.1 Stretched") against the muscle name of HumanTrait ("Left Index 1 Stretched").</summary>
        public static string Canon(string prop)
        {
            if (prop.StartsWith("LeftHand.")) return "Left " + prop.Substring(9).Replace(".", " ");
            if (prop.StartsWith("RightHand.")) return "Right " + prop.Substring(10).Replace(".", " ");
            return prop;
        }

        public static bool IsFinger(string m) { return m.Contains("Thumb") || m.Contains("Index") || m.Contains("Middle") || m.Contains("Ring") || m.Contains("Little"); }

        public static Dictionary<string, List<float>> Collect(IEnumerable<AnimationClip> clips)
        {
            var d = new Dictionary<string, List<float>>(); var names = new HashSet<string>(HumanTrait.MuscleName);
            foreach (var c in clips)
                foreach (var b in AnimationUtility.GetCurveBindings(c))
                {
                    string cn = Canon(b.propertyName); if (b.type != typeof(Animator) || !names.Contains(cn)) continue;
                    var cv = AnimationUtility.GetEditorCurve(c, b); List<float> l; if (!d.TryGetValue(cn, out l)) d[cn] = l = new List<float>();
                    foreach (var k in cv.keys) l.Add(k.value);
                }
            return d;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory("Logs/motion");
            var clips = new List<AnimationClip>();
            foreach (string f in Directory.GetFiles(MixamoImport.Folder, "*.fbx").OrderBy(x => x)) { var c = MixamoImport.LoadClip(f.Replace("\\", "/")); if (c != null && MotionDbBuilder.Specs().Any(s => s.Name == c.name)) clips.Add(c); }
            var d = Collect(clips);
            var sb = new StringBuilder("muscle | n | mean | p5 | p95 | max|v| | beyond +-1 | beyond +-0.9\n");
            foreach (var kv in d.OrderByDescending(x => x.Value.Count(v => Mathf.Abs(v) > 1f) / (float)x.Value.Count))
            {
                var l = kv.Value; var s = l.OrderBy(x => x).ToList();
                sb.AppendLine(kv.Key + " | " + l.Count + " | " + l.Average().ToString("0.00") + " | " + s[(int)(0.05f * (s.Count - 1))].ToString("0.00") + " | " + s[(int)(0.95f * (s.Count - 1))].ToString("0.00") + " | " + l.Max(v => Mathf.Abs(v)).ToString("0.00") + " | " + (100f * l.Count(v => Mathf.Abs(v) > 1f) / l.Count).ToString("0.0") + "% | " + (100f * l.Count(v => Mathf.Abs(v) > 0.9f) / l.Count).ToString("0.0") + "%");
            }
            File.WriteAllText("Logs/motion/muscles.txt", sb.ToString());
            { var lim = new StringBuilder(); for (int i = 0; i < HumanTrait.MuscleCount; i++) lim.AppendLine(HumanTrait.MuscleName[i] + " | " + HumanTrait.GetMuscleDefaultMin(i).ToString("0") + " .. " + HumanTrait.GetMuscleDefaultMax(i).ToString("0")); File.WriteAllText("Logs/motion/muscle_limits.txt", lim.ToString()); }
            Debug.Log("[MuscleReport] clips " + clips.Count);
        }
    }
}
