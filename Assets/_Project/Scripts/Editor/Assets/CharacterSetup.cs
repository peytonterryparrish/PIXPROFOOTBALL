using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Humanoid avatar setup for non-animation character FBXs (player bodies, referee, coaches, crowd).</summary>
    public static class CharacterSetup
    {
        public const string Root = "Assets/_Licensed/DraftFever";
        public static readonly string[] BodyPaths =
        {
            Root + "/Characters/Bodies/SmallPlayer.fbx",
            Root + "/Characters/Bodies/MediumPlayer.fbx",
            Root + "/Characters/Bodies/LargePlayer.fbx",
        };

        [MenuItem("PIX Football/Assets/Characters/Configure Body Avatars")]
        public static void ConfigureBodies()
        {
            foreach (string p in BodyPaths)
            {
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) { Debug.LogWarning("[CharacterSetup] missing " + p); continue; }
                bool dirty = false;
                if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; dirty = true; }
                if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (mi.isReadable) { mi.isReadable = false; dirty = true; }
                if (dirty) mi.SaveAndReimport();
                string rep;
                bool ok = HumanoidMapper.IsGoodAvatar(p) || HumanoidMapper.EnsureHumanoid(p, out rep);
                var av = HumanoidMapper.GetAvatar(p);
                Debug.Log("[CharacterSetup] " + p + " humanoid=" + ok + " avatar=" + (av != null ? av.name + " valid=" + av.isValid + " human=" + av.isHuman : "none"));
            }
        }

        /// <summary>Report bounds of every renderer + head bone world position for the bodies and helmets (used while tuning attachments).</summary>
        public static void ReportBodies()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string p in BodyPaths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) continue;
                sb.AppendLine("== " + p);
                var inst = (GameObject)Object.Instantiate(go);
                inst.transform.position = Vector3.zero;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine("  " + r.name + " bounds center=" + r.bounds.center.ToString("F3") + " size=" + r.bounds.size.ToString("F3"));
                var anim = inst.GetComponent<Animator>();
                if (anim != null && anim.avatar != null && anim.avatar.isHuman)
                {
                    foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.LeftFoot, HumanBodyBones.LeftHand })
                    {
                        var t = anim.GetBoneTransform(b);
                        sb.AppendLine("  bone " + b + " " + (t != null ? t.name + " pos=" + t.position.ToString("F3") : "null"));
                    }
                }
                Object.DestroyImmediate(inst);
            }
            System.IO.Directory.CreateDirectory("Docs/_dumps");
            System.IO.File.WriteAllText("Docs/_dumps/bodies.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }

        public static void ConfigureAndReport() { ConfigureBodies(); AssetDatabase.Refresh(); ReportBodies(); }
    }
}
