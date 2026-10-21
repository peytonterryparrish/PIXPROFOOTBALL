using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Explicit Humanoid bone mapping for the Auto-Rig-Pro style Draft Fever rigs (root.x / spine_01.x / arm_stretch.l /
    /// thigh_stretch.l ... or the underscore variant used by the body FBXs). Unity's automap fails on the full control
    /// rigs shipped in the BoNES QB / studio packs ("Required human bone 'Head' not found"), so we map by name and prefer
    /// the bones that the skinned meshes actually use (the deform skeleton).
    /// </summary>
    public static class HumanoidMapper
    {
        static IEnumerable<string> Variants(string humanBone)
        {
            string[] sides = { ".l", "_l", ".r", "_r" };
            bool left = humanBone.StartsWith("Left"), right = humanBone.StartsWith("Right");
            string[] sfx = left ? new[] { ".l", "_l", ".L", "_L" } : right ? new[] { ".r", "_r", ".R", "_R" } : new[] { "" };
            string core = left ? humanBone.Substring(4) : right ? humanBone.Substring(5) : humanBone;
            // Unity's HumanTrait names use spaces for fingers: "Left Thumb Proximal"
            core = core.Replace(" ", "");
            List<string> bases = new List<string>();
            switch (core)
            {
                case "Hips": bases.AddRange(new[] { "root.x", "hip", "Hips", "pelvis" }); break;
                case "Spine": bases.AddRange(new[] { "spine_01.x", "spine_01", "c_spine_01.x" }); break;
                case "Chest": bases.AddRange(new[] { "spine_02.x", "spine_02", "c_spine_02.x" }); break;
                case "UpperChest": bases.AddRange(new[] { "spine_03.x", "spine_03", "c_spine_03.x" }); break;
                case "Neck": bases.AddRange(new[] { "neck.x", "neck", "c_neck.x" }); break;
                case "Head": bases.AddRange(new[] { "head.x", "head", "c_head.x" }); break;
                case "Shoulder": bases.AddRange(new[] { "shoulder", "c_shoulder" }); break;
                case "UpperArm": bases.AddRange(new[] { "arm_stretch", "arm" }); break;
                case "LowerArm": bases.AddRange(new[] { "forearm_stretch", "forearm" }); break;
                case "Hand": bases.AddRange(new[] { "hand" }); break;
                case "UpperLeg": bases.AddRange(new[] { "thigh_stretch", "thigh" }); break;
                case "LowerLeg": bases.AddRange(new[] { "leg_stretch", "leg" }); break;
                case "Foot": bases.AddRange(new[] { "foot" }); break;
                case "Toes": bases.AddRange(new[] { "toes_01" }); break;
                case "Eye": bases.AddRange(new[] { "eye" }); break;
                case "Jaw": bases.AddRange(new[] { "jaw.x", "jaw" }); break;
                default:
                    // fingers: ThumbProximal -> thumb1, ThumbIntermediate -> thumb2, ThumbDistal -> thumb3
                    string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
                    foreach (string f in fingers)
                    {
                        if (!core.StartsWith(f)) continue;
                        string rest = core.Substring(f.Length);
                        int n = rest == "Proximal" ? 1 : rest == "Intermediate" ? 2 : 3;
                        string nm = (f == "Little" ? "pinky" : f.ToLowerInvariant()) + n;
                        bases.Add(nm); bases.Add("c_" + nm);
                    }
                    break;
            }
            foreach (string b in bases)
                foreach (string s in sfx)
                    yield return b + s;
        }

        /// <summary>Returns the human-bone -> transform-name map, or null if the essential bones are missing.</summary>
        public static Dictionary<string, string> Map(GameObject model, out string report)
        {
            var transforms = model.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>(transforms.Select(t => t.name));
            var used = new HashSet<string>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones) if (b != null) used.Add(b.name);

            var map = new Dictionary<string, string>();
            var missing = new List<string>();
            string[] humanNames = HumanTrait.BoneName;
            foreach (string hb in humanNames)
            {
                string pick = null;
                string firstExisting = null;
                foreach (string cand in Variants(hb))
                {
                    if (!names.Contains(cand)) continue;
                    if (firstExisting == null) firstExisting = cand;
                    if (used.Count == 0 || used.Contains(cand)) { pick = cand; break; }
                }
                if (pick == null) pick = firstExisting;
                if (pick != null) map[hb] = pick; else missing.Add(hb);
            }
            report = "mapped=" + map.Count + " missing=[" + string.Join(",", missing.ToArray()) + "] deformUsed=" + used.Count;
            string[] required = { "Hips", "Spine", "Head", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
                "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand" };
            foreach (string r in required) if (!map.ContainsKey(r)) return null;
            return map;
        }

        static void CollectSkeleton(Transform t, List<SkeletonBone> list)
        {
            list.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            foreach (Transform c in t) CollectSkeleton(c, list);
        }

        /// <summary>Applies an explicit mapping to the importer. Returns false if the model lacks the required bones.</summary>
        public static bool Apply(ModelImporter mi, out string report)
        {
            report = "";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(mi.assetPath);
            if (go == null) { report = "no model"; return false; }
            var map = Map(go, out report);
            if (map == null) return false;

            // skeleton: model root + (rig root subtree that contains the hips), skipping control-shape meshes and skinned mesh nodes
            Transform hips = go.GetComponentsInChildren<Transform>(true).First(t => t.name == map["Hips"]);
            Transform rigRoot = hips;
            while (rigRoot.parent != null && rigRoot.parent != go.transform) rigRoot = rigRoot.parent;
            var skel = new List<SkeletonBone>();
            skel.Add(new SkeletonBone { name = go.name, position = go.transform.localPosition, rotation = go.transform.localRotation, scale = go.transform.localScale });
            CollectSkeleton(rigRoot, skel);

            var desc = mi.humanDescription;
            var human = new List<HumanBone>();
            foreach (var kv in map)
            {
                var hb = new HumanBone { humanName = kv.Key, boneName = kv.Value };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            desc.human = human.ToArray();
            desc.skeleton = skel.ToArray();
            if (desc.upperArmTwist <= 0f) desc.upperArmTwist = 0.5f;
            if (desc.lowerArmTwist <= 0f) desc.lowerArmTwist = 0.5f;
            if (desc.upperLegTwist <= 0f) desc.upperLegTwist = 0.5f;
            if (desc.lowerLegTwist <= 0f) desc.lowerLegTwist = 0.5f;
            if (desc.armStretch <= 0f) desc.armStretch = 0.05f;
            if (desc.legStretch <= 0f) desc.legStretch = 0.05f;
            desc.hasTranslationDoF = false;
            mi.humanDescription = desc;
            return true;
        }

        public static Avatar GetAvatar(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        }

        public static bool IsGoodAvatar(string path)
        {
            var av = GetAvatar(path);
            return av != null && av.isValid && av.isHuman;
        }

        /// <summary>Make sure an FBX imports as a valid Humanoid; applies explicit mapping when automap failed.</summary>
        public static bool EnsureHumanoid(string path, out string report)
        {
            report = "";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return false;
            if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
            }
            if (IsGoodAvatar(path)) return true;
            if (!Apply(mi, out report)) return false;
            mi.SaveAndReimport();
            return IsGoodAvatar(path);
        }

        [MenuItem("PIX Football/Assets/Animations/Fix Invalid Humanoid Avatars")]
        public static void FixAllInvalid()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { AnimationImportSettings.AnimRoot });
            int fixedCount = 0, stillBad = 0, ok = 0;
            var bad = new List<string>();
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (IsGoodAvatar(path)) { ok++; continue; }
                string rep;
                if (EnsureHumanoid(path, out rep)) fixedCount++;
                else { stillBad++; bad.Add(path + " :: " + rep); }
            }
            Debug.Log("[HumanoidMapper] ok=" + ok + " fixed=" + fixedCount + " stillBad=" + stillBad + "\n" + string.Join("\n", bad.Take(30).ToArray()));
        }
    }
}
