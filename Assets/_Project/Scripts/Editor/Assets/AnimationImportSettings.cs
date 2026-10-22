using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Humanoid setup for every FBX under Assets/_Licensed/DraftFever/Animations.
    /// Pass 1 (AssetPostprocessor, on first import): Humanoid + avatar created from the file, cameras/lights off,
    /// materials off.  Pass 2 (menu / DraftFeverImporter, after the takes exist): per-clip loop flags and root-motion
    /// bake flags, driven by the clip name.
    /// </summary>
    public static class AnimationImportSettings
    {
        public const string AnimRoot = "Assets/_Licensed/DraftFever/Animations";

        static readonly string[] LoopWords =
        {
            "idle", "walk", "jog", "run", "sprint", "backpedal", "strafe", "shuffle", "loop", "cycle", "stance", "lookaround",
            "cadence", "cheer", "neutral", "freestyle", "huddle", "bench"
        };

        static readonly string[] NoLoopWords =
        {
            "_to_", "to_stand", "transition", "trans_", "turn", "_90", "_180", "avoid", "dodge", "juke", "stiff", "truck", "dive", "kick", "punt",
            "catch", "block", "tackle", "idleto", "stand_to", "handoff", "throw", "pass"
        };

        /// <summary>Applied from the AssetPostprocessor (first import).</summary>
        public static void ConfigureHumanoid(ModelImporter mi)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.isReadable = false;
            mi.optimizeGameObjects = false;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReductionAndCompression;
            mi.resampleCurves = true;
            // QA-906: tight keyframe-reduction tolerances (the 0.5 defaults add wrist and foot jitter on 60 fps takes)
            mi.animationRotationError = 0.1f; mi.animationPositionError = 0.1f; mi.animationScaleError = 0.1f;
        }

        public static bool IsLoopName(string clipName)
        {
            string n = clipName.ToLowerInvariant();
            if (n.Contains("loop")) return true;
            for (int i = 0; i < NoLoopWords.Length; i++) if (n.Contains(NoLoopWords[i])) return false;
            for (int i = 0; i < LoopWords.Length; i++) if (n.Contains(LoopWords[i])) return true;
            return false;
        }

        static bool IsInPlace(string clipName)
        {
            string n = clipName.ToLowerInvariant();
            return n.Contains("idle") || n.Contains("stance") || n.Contains("cadence") || n.Contains("neutral") || n.Contains("lookaround")
                   || n.Contains("cheer") || n.Contains("celebrat") || n.Contains("freestyle");
        }

        /// <summary>Pass 2: write clipAnimations (loop + root-motion flags) for one model. Returns true if reimport needed.</summary>
        public static bool ConfigureClips(ModelImporter mi)
        {
            if (mi.animationType != ModelImporterAnimationType.Human) return false;
            ModelImporterClipAnimation[] defaults = mi.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0) return false;
            var list = new List<ModelImporterClipAnimation>();
            foreach (var d in defaults)
            {
                var c = d;
                string n = c.takeName ?? c.name;
                if (string.Equals(n, "Take 001", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "mixamo.com", StringComparison.OrdinalIgnoreCase))
                    c.name = Path.GetFileNameWithoutExtension(mi.assetPath);
                bool loop = IsLoopName(c.name);
                c.loopTime = loop;
                c.loopPose = loop;
                // root motion baking: rotation + height are baked into pose (physics capsule owns yaw/height);
                // XZ translation is kept as root motion except for in-place clips.
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = IsInPlace(c.name);
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = false;
                c.heightFromFeet = false;
                c.cycleOffset = 0f;
                list.Add(c);
            }
            var arr = list.ToArray();
            // QA-908: only touch (and re-import) the model when a flag really changed
            var old = mi.clipAnimations;
            bool changed = old == null || old.Length != arr.Length;
            for (int i = 0; !changed && i < arr.Length; i++)
            {
                var a = old[i]; var b = arr[i];
                changed = a.name != b.name || a.loopTime != b.loopTime || a.loopPose != b.loopPose || a.lockRootRotation != b.lockRootRotation || a.lockRootHeightY != b.lockRootHeightY
                          || a.lockRootPositionXZ != b.lockRootPositionXZ || a.keepOriginalOrientation != b.keepOriginalOrientation || a.keepOriginalPositionY != b.keepOriginalPositionY
                          || a.keepOriginalPositionXZ != b.keepOriginalPositionXZ || a.heightFromFeet != b.heightFromFeet;
            }
            if (!changed) return false;
            mi.clipAnimations = arr;
            return true;
        }

        /// <summary>Humanoid + clip flags for the models of one folder only (new imports).</summary>
        public static int ApplyFolder(string folder)
        {
            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    var mi = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g)) as ModelImporter;
                    if (mi == null) continue;
                    bool dirty = false;
                    if (mi.animationType != ModelImporterAnimationType.Human) { ConfigureHumanoid(mi); dirty = true; }
                    if (ConfigureClips(mi)) dirty = true;
                    if (dirty) { mi.SaveAndReimport(); changed++; }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return changed;
        }

        [MenuItem("PIX Football/Assets/Animations/Apply Humanoid + Clip Flags")]
        public static void ApplyAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { AnimRoot });
            int changed = 0, total = 0, notHuman = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string g in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (mi == null) continue;
                    total++;
                    bool dirty = false;
                    if (mi.animationType != ModelImporterAnimationType.Human) { ConfigureHumanoid(mi); dirty = true; }
                    if (ConfigureClips(mi)) dirty = true;
                    if (dirty) { mi.SaveAndReimport(); changed++; }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log("[AnimationImportSettings] models=" + total + " reimported=" + changed + " nonHuman=" + notHuman);
        }

        /// <summary>Two-phase: humanoid first (avatars/takes exist after import), then clips.</summary>
        public static void ApplyAllBatch()
        {
            HumanoidMapper.FixAllInvalid();
            AssetDatabase.Refresh();
            ApplyAll();
            AssetDatabase.Refresh();
            ReportAvatars();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("PIX Football/Assets/Animations/Report Avatar Validity")]
        public static void ReportAvatars()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { AnimRoot });
            int ok = 0, bad = 0;
            var sb = new System.Text.StringBuilder();
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                Avatar av = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) { if (o is Avatar a) { av = a; break; } }
                if (av != null && av.isValid && av.isHuman) ok++;
                else { bad++; sb.AppendLine("  BAD AVATAR " + path); }
            }
            Debug.Log("[AnimationImportSettings] avatars valid=" + ok + " invalid=" + bad + "\n" + sb);
        }
    }
}
