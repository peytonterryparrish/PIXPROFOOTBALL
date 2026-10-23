using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 MOTION: the 68 Mixamo locomotion clips (X Bot rig, FBX for Unity, skeleton only, 30 fps, root motion kept) under Assets/_Licensed/Mixamo/ (git-ignored).
    /// Humanoid, avatar from the file, every clip renamed to the file name (Mixamo names every take "mixamo.com"), root motion curves kept (the match ignores them, the offline scan uses them).
    /// The settings are written to each importer explicitly (no AssetPostprocessor: a postprocessor with OnPreprocessModel would change the import hash of every model in the project).
    /// </summary>
    public static class MixamoImport
    {
        public const string Folder = "Assets/_Licensed/Mixamo";

        /// <summary>Sets the importer of every Mixamo FBX and re-imports it.</summary>
        public static void Run()
        {
            BatchWatchdog.Arm(25);
            int n = 0, ok = 0;
            foreach (string f in Directory.GetFiles(Folder, "*.fbx"))
            {
                string path = f.Replace('\\', '/');
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { Debug.LogWarning("[Mixamo] no importer " + path); continue; }
                Configure(mi, path);
                mi.SaveAndReimport();
                n++;
                if (LoadClip(path) != null) ok++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Mixamo] configured " + n + ", clips found " + ok);
        }

        /// <summary>Only the clip settings (root bake flags): the avatar of each file (the shared T-pose) stays as MixamoTPose set it.</summary>
        public static void RunKeepAvatar()
        {
            BatchWatchdog.Arm(25);
            int n = 0;
            foreach (string f in Directory.GetFiles(Folder, "*.fbx"))
            {
                string path = f.Replace("\\", "/");
                var mi = AssetImporter.GetAtPath(path) as ModelImporter; if (mi == null) continue;
                var clips = mi.clipAnimations; if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
                string nm = Path.GetFileNameWithoutExtension(path);
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i].name = clips.Length == 1 ? nm : nm + "_" + i; clips[i].loopTime = false;
                    clips[i].lockRootRotation = MotionDbBuilder.BakesRotation(nm); clips[i].lockRootHeightY = false; clips[i].lockRootPositionXZ = false;
                }
                mi.clipAnimations = clips; mi.SaveAndReimport(); n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Mixamo] clip settings updated: " + n);
        }

        static void Configure(ModelImporter mi, string path)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.resampleCurves = true;
            var clips = mi.defaultClipAnimations;
            if (clips != null && clips.Length > 0)
            {
                string n = Path.GetFileNameWithoutExtension(path);
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i].name = clips.Length == 1 ? n : n + "_" + i;
                    clips[i].loopTime = false;
                    clips[i].lockRootRotation = MotionDbBuilder.BakesRotation(n); clips[i].lockRootHeightY = false; clips[i].lockRootPositionXZ = false;      // steady loops keep the sway of the body in the pose (the match ignores root motion), turns keep it in the root
                }
                mi.clipAnimations = clips;
            }
        }

        public static AnimationClip LoadClip(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__")) return c;
            }
            return null;
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>
    /// The skeleton-only Mixamo exports carry the FIRST FRAME of each take as the rest pose of their avatar, not a T-pose: every file would be retargeted from a different (and wrong) zero pose (one foot ends up
    /// 5-9 cm above the other in every run). This builds ONE correct T-pose for the X Bot skeleton (every limb aligned to the world axes by the shortest rotation, twist from the file), writes it into the reference file's
    /// avatar and makes every other file copy that avatar. Run: -executeMethod PixFootball.EditorTools.MixamoTPose.Run
    /// </summary>
    public static class MixamoTPose
    {
        public const string Ref = "Assets/_Licensed/Mixamo/Sprint_Standard.fbx";

        static Transform Find(Transform root, string suffix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "mixamorig:" + suffix || t.name == suffix) return t;
            return null;
        }

        static void Aim(Transform b, Transform child, Vector3 dir)
        {
            if (b == null || child == null) return;
            Vector3 cur = child.position - b.position; if (cur.sqrMagnitude < 1e-10f) return;
            b.rotation = Quaternion.FromToRotation(cur.normalized, dir.normalized) * b.rotation;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(25);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Ref);
            var inst = Object.Instantiate(model); inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            var R = inst.transform;
            Transform hips = Find(R, "Hips"), lul = Find(R, "LeftUpLeg"), rul = Find(R, "RightUpLeg");
            if (hips == null || lul == null) { Debug.LogError("[MixamoTPose] bones not found"); Object.DestroyImmediate(inst); return; }
            Vector3 side = (lul.position - rul.position); side.y = 0f; float sx = Mathf.Sign(side.x != 0f ? side.x : 1f);
            // character left in the file's root space: +X or -X; facing = left x up
            Vector3 left = new Vector3(sx, 0f, 0f), up = Vector3.up, fwd = Vector3.Cross(left, up) * -1f;      // left (x) cross up (y) = +z for left = +x ... fwd chosen so that left = fwd x up
            fwd = Vector3.Cross(up, left).normalized;      // Unity: right = up x forward, so forward = up x left
            Debug.Log("[MixamoTPose] left axis " + left + " forward " + fwd);
            // hips + spine
            Aim(hips, Find(R, "Spine"), up);
            Vector3 hs = lul.position - rul.position; hs.y = 0f;
            hips.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(hs, left, Vector3.up), Vector3.up) * hips.rotation;
            Aim(Find(R, "Spine"), Find(R, "Spine1"), up); Aim(Find(R, "Spine1"), Find(R, "Spine2"), up); Aim(Find(R, "Spine2"), Find(R, "Neck"), up); Aim(Find(R, "Neck"), Find(R, "Head"), up); Aim(Find(R, "Head"), Find(R, "HeadTop_End"), up);
            foreach (var s in new[] { "Left", "Right" })
            {
                Vector3 sd = s == "Left" ? left : -left;
                Aim(Find(R, s + "Shoulder"), Find(R, s + "Arm"), sd);
                Aim(Find(R, s + "Arm"), Find(R, s + "ForeArm"), sd); Aim(Find(R, s + "ForeArm"), Find(R, s + "Hand"), sd);
                Aim(Find(R, s + "Hand"), Find(R, s + "HandMiddle1"), sd);
                Aim(Find(R, s + "UpLeg"), Find(R, s + "Leg"), -up); Aim(Find(R, s + "Leg"), Find(R, s + "Foot"), -up);
                Aim(Find(R, s + "Foot"), Find(R, s + "ToeBase"), (fwd * Mathf.Cos(35f * Mathf.Deg2Rad) - up * Mathf.Sin(35f * Mathf.Deg2Rad)));
                Aim(Find(R, s + "ToeBase"), Find(R, s + "Toe_End"), fwd);
            }
            var mi = AssetImporter.GetAtPath(Ref) as ModelImporter;
            var hd = mi.humanDescription;
            var sk = hd.skeleton; int n = 0;
            var dump = new System.Text.StringBuilder();
            for (int i = 0; i < sk.Length; i++)
            {
                Transform t = null; foreach (var c in R.GetComponentsInChildren<Transform>(true)) if (c.name == sk[i].name) { t = c; break; }
                if (t == null) continue;
                sk[i].rotation = t.localRotation; sk[i].position = t.localPosition; n++;
            }
            hd.skeleton = sk;
            mi.humanDescription = hd;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.SaveAndReimport();
            Object.DestroyImmediate(inst);
            Avatar refAv = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Ref)) if (o is Avatar) { refAv = (Avatar)o; break; }
            Debug.Log("[MixamoTPose] reference bones written: " + n + ", avatar " + (refAv != null ? refAv.name + " human=" + refAv.isHuman + " valid=" + refAv.isValid : "NONE"));
            if (refAv == null) return;
            int m = 0;
            foreach (string f in Directory.GetFiles(MixamoImport.Folder, "*.fbx"))
            {
                string path = f.Replace("\\", "/");
                if (path == Ref) continue;
                var mj = AssetImporter.GetAtPath(path) as ModelImporter; if (mj == null) continue;
                mj.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mj.sourceAvatar = refAv;
                mj.SaveAndReimport(); m++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[MixamoTPose] files switched to the reference avatar: " + m);
        }
    }
}
