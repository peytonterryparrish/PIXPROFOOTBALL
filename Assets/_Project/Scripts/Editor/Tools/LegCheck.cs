using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Leg-extension check (MDL-083): per clip the smallest hip-to-foot / leg-length ratio over 9 sample times (1 = straight leg, ~0.1 = folded).
    /// Poses are evaluated the way the game plays them (Foot IK off). A second column shows what the Unity default PlayClip (Foot IK ON) gives for
    /// the clips without foot goal curves, which is what made the cleaned clips look folded in the offline renderers.
    /// Output: Logs/anim_audit/leg_extension.csv (all library clips) and a summary in the log. Verdict FOLDED below 0.30 (kneeling / prone takes are listed with their ratio).
    /// </summary>
    public static class LegCheck
    {
        public static void Run()
        {
            BatchWatchdog.Arm(30);
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var lib = ArtLibrary.Load();
            var clips = new Dictionary<string, AnimationClip>();
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Licensed/DraftFever/Animations/Clean" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (c != null && !clips.ContainsKey(p)) clips[p] = c;
            }
            // every key / variant the game can play (library clips that are not in Clean/ are measured as well)
            foreach (var key in lib.ClipKeys.ToList())
            {
                if (!lib.HasClip(key)) continue;
                for (int i = 0; i < lib.ClipVariantCount(key); i++)
                {
                    var c = lib.ClipVariant(key, i); if (c == null) continue;
                    string p = AssetDatabase.GetAssetPath(c) + "#" + c.name;
                    if (!clips.ContainsKey(p) && !clips.ContainsKey(AssetDatabase.GetAssetPath(c))) clips[p] = c;
                }
            }
            var sb = new StringBuilder("path,clip,length,minRatioIkOff,medianRatioIkOff,minRatioPlayClipDefault,animationModeSample,verdict\n");
            int folded = 0, n = 0; var bad = new List<string>();
            foreach (var kv in clips.OrderBy(x => x.Key))
            {
                var c = kv.Value; float len = Mathf.Max(0.05f, c.length);
                var ratios = new List<float>();
                for (int s = 0; s < 9; s++)
                {
                    var f = rig.PoseAt(c, len * s / 8f);
                    ratios.Add(Mathf.Min(Ratio(f, AnimRig.LUL, AnimRig.LLL, AnimRig.LF), Ratio(f, AnimRig.RUL, AnimRig.RLL, AnimRig.RF)));
                }
                ratios.Sort();
                float min = ratios[0], med = ratios[4];
                float def = -1f, am = -1f;
                if (n % 12 == 0) { def = DefaultPlayClipRatio(rig, c); am = SampleModeRatio(rig, c); }
                string v = min < 0.30f ? "FOLDED" : "ok";
                if (v == "FOLDED") { folded++; bad.Add(kv.Key + " " + min.ToString("0.00")); }
                sb.AppendLine(kv.Key + "," + c.name + "," + len.ToString("0.00") + "," + min.ToString("0.00") + "," + med.ToString("0.00") + "," + (def < 0 ? "" : def.ToString("0.00")) + "," + (am < 0 ? "" : am.ToString("0.00")) + "," + v);
                n++;
            }
            rig.Dispose();
            Directory.CreateDirectory("Logs/anim_audit");
            File.WriteAllText("Logs/anim_audit/leg_extension.csv", sb.ToString());
            Debug.Log("[LegCheck] clips=" + n + " folded=" + folded + "\n" + string.Join("\n", bad.Take(60)));
        }

        static float Ratio(AnimRig.Frame f, int hip, int knee, int foot)
        {
            float leg = Vector3.Distance(f.P[hip], f.P[knee]) + Vector3.Distance(f.P[knee], f.P[foot]);
            return leg < 1e-4f ? 1f : Vector3.Distance(f.P[hip], f.P[foot]) / leg;
        }

        /// <summary>The reliable sampler (AnimationMode.SampleAnimationClip, what the editor preview uses), pose at the middle of the clip.</summary>
        static float SampleModeRatio(AnimRig rig, AnimationClip c)
        {
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(rig.Go, c, c.length * 0.5f);
            AnimationMode.EndSampling();
            var a = rig.Anim;
            Vector3 hip = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position, knee = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position, foot = a.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            float leg = Vector3.Distance(hip, knee) + Vector3.Distance(knee, foot);
            float r = Vector3.Distance(hip, foot) / leg;
            AnimationMode.StopAnimationMode();
            return r;
        }

        /// <summary>What a default Unity PlayClip (Foot IK on) pose gives for the clip (at its middle).</summary>
        static float DefaultPlayClipRatio(AnimRig rig, AnimationClip c)
        {
            PlayableGraph g;
            var output = default(AnimationPlayableOutput);
            g = PlayableGraph.Create("LegCheckDefault"); g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            output = AnimationPlayableOutput.Create(g, "o", rig.Anim);
            var cp = AnimationClipPlayable.Create(g, c);      // default Foot IK
            output.SetSourcePlayable(cp);
            cp.SetTime(c.length * 0.5f); g.Evaluate(0f);
            var a = rig.Anim;
            Vector3 hip = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position, knee = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position, foot = a.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            float leg = Vector3.Distance(hip, knee) + Vector3.Distance(knee, foot);
            g.Destroy();
            return Vector3.Distance(hip, foot) / leg;
        }
    }
}
