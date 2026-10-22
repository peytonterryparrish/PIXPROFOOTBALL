using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Measures key moments in the mocap clips the match animation uses and stores them in PixClipTiming.asset:
    /// the release of every throw (peak speed of the throwing hand) and the catch (the two hands closest together while reaching).
    /// The runtime trims each clip so that moment lands exactly when the simulation releases / catches the ball.
    /// Menu: PIX Football/Assets/Build Clip Timing.
    /// </summary>
    public static class ClipTimingBuilder
    {
        static readonly string[] ThrowKeys = { "qb_throw_bullet", "qb_throw_lob_short", "qb_throw_lob_long" };
        static readonly string[] CatchKeys = { "catch_high", "catch_low", "catch_run", "catch_jump", "catch_side", "interception" };

        [MenuItem("PIX Football/Assets/Build Clip Timing")]
        public static void Run()
        {
            BatchWatchdog.Arm(20);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[ClipTiming] no ArtLibrary"); return; }
            var prefab = lib.Prefab("player_medium");
            var go = Object.Instantiate(prefab);
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
            var anim = go.GetComponent<Animator>();
            var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);

            var table = AssetDatabase.LoadAssetAtPath<ClipTimingTable>(ClipTimingTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<ClipTimingTable>(); MaterialFactory.EnsureFolder("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, ClipTimingTable.EditorPath); }
            table.entries.Clear();
            var log = new StringBuilder();

            foreach (string key in ThrowKeys)
                for (int v = 0; v < lib.ClipVariantCount(key); v++)
                {
                    var clip = lib.ClipVariant(key, v);
                    if (clip.length > 8f) continue;
                    float t = PeakHandSpeed(go, anim, rh, clip);
                    table.entries.Add(new ClipTimingTable.Entry { clip = clip.name, t = t });
                    log.AppendLine("throw " + key + "[" + v + "] " + clip.name + " len=" + clip.length.ToString("0.00") + " release=" + t.ToString("0.00"));
                }
            foreach (string key in CatchKeys)
                for (int v = 0; v < lib.ClipVariantCount(key); v++)
                {
                    var clip = lib.ClipVariant(key, v);
                    if (clip.length > 4f) continue;
                    float t = HandsTogether(go, anim, rh, lh, clip);
                    table.entries.Add(new ClipTimingTable.Entry { clip = clip.name, t = t });
                    log.AppendLine("catch " + key + "[" + v + "] " + clip.name + " len=" + clip.length.ToString("0.00") + " catch=" + t.ToString("0.00"));
                }
            // v0.8: kicks - the foot contact is the peak speed of the faster foot (the kicking leg)
            var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot); var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            foreach (string key in new[] { "kick_off", "kick_field_goal", "punt", "punt_fast", "kick_return_catch" })
                for (int v = 0; v < lib.ClipVariantCount(key); v++)
                {
                    var clip = lib.ClipVariant(key, v);
                    if (clip.length > 9f) continue;
                    float t = key == "kick_return_catch" ? HandsTogether(go, anim, rh, lh, clip) : Mathf.Max(PeakHandSpeedOf(go, anim, rf, clip, 0.2f, 0.95f, true), 0f);
                    float t2 = key == "kick_return_catch" ? 0f : PeakHandSpeedOf(go, anim, lf, clip, 0.2f, 0.95f, true);
                    float s1 = key == "kick_return_catch" ? 0f : PeakHandSpeedOf(go, anim, rf, clip, 0.2f, 0.95f, false), s2 = key == "kick_return_catch" ? 0f : PeakHandSpeedOf(go, anim, lf, clip, 0.2f, 0.95f, false);
                    if (key != "kick_return_catch" && s2 > s1) t = t2;
                    table.entries.Add(new ClipTimingTable.Entry { clip = clip.name, t = t });
                    log.AppendLine("kick " + key + "[" + v + "] " + clip.name + " len=" + clip.length.ToString("0.00") + " contact=" + t.ToString("0.00"));
                }
            Object.DestroyImmediate(go);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            ClipTimingTable.ResetCache();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/clip_timing.txt", log.ToString());
            Debug.Log("[ClipTiming] " + table.entries.Count + " entries -> Logs/clip_timing.txt");
        }

        static void Pose(GameObject go, Animator anim, AnimationClip clip, float t, out PlayableGraph graph)
        {
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out graph);
            graph.Evaluate(0f); if (t > 0f) graph.Evaluate(t);
        }

        static float PeakHandSpeed(GameObject go, Animator anim, Transform hand, AnimationClip clip)
        {
            const float step = 1f / 30f;
            float best = 0f, bestT = clip.length * 0.45f; Vector3 prev = Vector3.zero; bool have = false;
            PlayableGraph g;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out g);
            g.Evaluate(0f);
            for (float t = 0f; t <= clip.length; t += step)
            {
                if (t > 0f) g.Evaluate(step);
                Vector3 p = go.transform.InverseTransformPoint(hand.position);
                if (have && t > clip.length * 0.15f && t < clip.length * 0.9f)
                {
                    float sp = (p - prev).magnitude / step;
                    if (sp > best) { best = sp; bestT = t; }
                }
                prev = p; have = true;
            }
            g.Destroy();
            return bestT;
        }

        /// <summary>Time (returnTime = true) or value (false) of the peak speed of a bone in the character frame inside [f0, f1] of the clip.</summary>
        static float PeakHandSpeedOf(GameObject go, Animator anim, Transform bone, AnimationClip clip, float f0, float f1, bool returnTime)
        {
            const float step = 1f / 30f;
            float best = 0f, bestT = clip.length * 0.5f; Vector3 prev = Vector3.zero; bool have = false;
            PlayableGraph g;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out g);
            g.Evaluate(0f);
            for (float t = 0f; t <= clip.length; t += step)
            {
                if (t > 0f) g.Evaluate(step);
                Vector3 p = go.transform.InverseTransformPoint(bone.position);
                if (have && t > clip.length * f0 && t < clip.length * f1) { float sp = (p - prev).magnitude / step; if (sp > best) { best = sp; bestT = t; } }
                prev = p; have = true;
            }
            g.Destroy();
            return returnTime ? bestT : best;
        }

        static float HandsTogether(GameObject go, Animator anim, Transform rh, Transform lh, AnimationClip clip)
        {
            const float step = 1f / 30f;
            float best = float.MaxValue, bestT = clip.length * 0.35f;
            PlayableGraph g;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out g);
            g.Evaluate(0f);
            for (float t = 0f; t <= clip.length; t += step)
            {
                if (t > 0f) g.Evaluate(step);
                if (t < clip.length * 0.12f || t > clip.length * 0.75f) continue;
                float d = Vector3.Distance(rh.position, lh.position);
                // prefer hands that are up and in front of the body (a reach), not hanging at the sides
                Vector3 local = go.transform.InverseTransformPoint((rh.position + lh.position) * 0.5f);
                float reach = Mathf.Max(0.05f, local.y);
                float score = d / reach;
                if (score < best) { best = score; bestT = t; }
            }
            g.Destroy();
            return bestT;
        }
    }
}
