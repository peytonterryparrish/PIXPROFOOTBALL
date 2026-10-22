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
    /// Biped clips of "american-football-player-animated" (3ds Max Biped, 76 bones) as Humanoid clips for the v0.7 photoreal roster (STAGING).
    /// Only the ANIMATION of the purchased files is used: the FBX are copied into Staging_v07/Biped, imported as Humanoid, the takes are cleaned with ClipCleaner
    /// (trim, despike, filter, loop seam, ground, facing) into muscle-space .anim assets in Staging_v07/Clips, measured (foot contact / loop closure), listed in clips.json,
    /// and the imported FBX copies (which contain the seller's body / scanned head) are deleted again.
    /// New keys only (kick_off_biped, qb_throw_biped, sprint_biped, bc_sprint_biped, helmet_off, clinch_a, clinch_b, walk_macho + mirrored _m); nothing existing is overwritten.
    /// </summary>
    public static class PhotoRosterV2Clips
    {
        sealed class Spec
        {
            public string file, key, id; public string avatarPrefix = "Bip001"; public bool loop, inPlace, autoTrim = true, faceForward = true, keepRoot, ground = true, mirror; public float rate = 30f; public string note = "";
        }

        static readonly Spec[] Specs =
        {
            new Spec { file = "Kick", key = "kick_off_biped", id = "kick_off_biped", mirror = true, note = "place kick / kickoff: 2-3 approach steps, plant, swing" },
            new Spec { file = "Throw", key = "qb_throw_biped", id = "qb_throw_biped", mirror = true, note = "QB throw with footwork" },
            new Spec { file = "sprint", key = "sprint_biped", id = "sprint_biped", loop = true, inPlace = true, autoTrim = false, rate = 60f, note = "one gait cycle, in place" },
            new Spec { file = "sprintball", key = "bc_sprint_biped", id = "bc_sprint_biped", loop = true, inPlace = true, autoTrim = false, rate = 60f, note = "ball carrier sprint cycle, in place" },
            new Spec { file = "Helmet_TakeOn", key = "helmet_off", id = "helmet_off", inPlace = true, autoTrim = false, note = "two-handed helmet removal (pre / post game cutaway)" },
            new Spec { file = "Clinch", key = "clinch_a", id = "clinch_a", keepRoot = true, autoTrim = false, faceForward = false, ground = false, note = "two-player clinch, player A (Bip001)" },
            new Spec { file = "Clinch", key = "clinch_b", id = "clinch_b", avatarPrefix = "Bip002", keepRoot = true, autoTrim = false, faceForward = false, ground = false, note = "two-player clinch, player B (Bip002)" },
            new Spec { file = "MachoWalk", key = "walk_macho", id = "walk_macho", loop = true, inPlace = true, autoTrim = false, rate = 60f, note = "strutting walk loop (not a neutral walk)" },
        };

        static string Src(string file) { string[] c = { BipedFolder + "/" + file + ".FBX", BipedFolder + "/" + file + ".fbx" }; return c.FirstOrDefault(File.Exists); }
        const string BipedFolder = "D:/PIXFootball/Source/PPF_NEW/player_animated/anims";

        public static void ImportAndCleanBatch() { ImportAndClean(); }

        [MenuItem("PIX Football/Photoreal Player/Roster v2 (staging)/Import Biped clips")]
        public static void ImportAndClean()
        {
            double t0 = EditorApplication.timeSinceStartup;
            var log = new StringBuilder();
            Directory.CreateDirectory(PhotoRosterV2.BipedDir);
            Directory.CreateDirectory(PhotoRosterV2.ClipDir);
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { PhotoRosterV2.ClipDir })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(g));
            AssetDatabase.Refresh();
            // 1) copy + import every distinct file (Clinch twice: one avatar per skeleton)
            var imported = new Dictionary<string, string>();
            foreach (var sp in Specs)
            {
                string dstName = sp.file == "Clinch" ? "Clinch_" + (sp.avatarPrefix == "Bip001" ? "A" : "B") : sp.file;
                string dst = PhotoRosterV2.BipedDir + "/" + dstName + ".fbx";
                string src = Src(sp.file);
                if (src == null) { log.AppendLine("MISSING source " + sp.file); continue; }
                if (!File.Exists(dst)) File.Copy(src, dst);
                imported[sp.id] = dst;
            }
            AssetDatabase.Refresh();
            foreach (var sp in Specs)
            {
                if (!imported.ContainsKey(sp.id)) continue;
                string path = imported[sp.id];
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { log.AppendLine("no importer " + path); continue; }
                AnimationImportSettings.ConfigureHumanoid(mi);
                mi.SaveAndReimport();
                var av = HumanoidMapper.GetAvatar(path);
                string rep = "";
                bool ok = av != null && av.isValid && av.isHuman;
                if (!ok || sp.avatarPrefix != "Bip001")
                {
                    ok = ApplyBipedMap(mi, sp.avatarPrefix, out rep);
                    mi.SaveAndReimport();
                    av = HumanoidMapper.GetAvatar(path);
                    ok = ok && av != null && av.isValid && av.isHuman;
                }
                log.AppendLine("IMPORT " + sp.id + " " + path + " avatar valid=" + (av != null && av.isValid) + " human=" + (av != null && av.isHuman) + " " + rep);
                if (!ok) log.AppendLine("  !! no valid humanoid avatar for " + sp.id);
            }
            // 2) clean
            var rig = AnimRig.CreateMedium();
            var man = new PhotoRosterV2.ClipManifest();
            if (rig == null) { Debug.LogError("[PhotoRosterClips] no AnimRig"); return; }
            try
            {
                foreach (var sp in Specs)
                {
                    if (!imported.ContainsKey(sp.id)) continue;
                    if (EditorApplication.timeSinceStartup - t0 > 1500) throw new TimeoutException("clip import watchdog");
                    string path = imported[sp.id];
                    var raw = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                    if (raw == null) { log.AppendLine("NO CLIP in " + path); continue; }
                    log.AppendLine("RAW " + sp.id + " clip '" + raw.name + "' len " + raw.length.ToString("0.000") + " s rate " + raw.frameRate + " humanMotion=" + raw.isHumanMotion + " loopTime=" + raw.isLooping);
                    raw.name = sp.id;     // ClipCleaner names its output after the clip (the take is called "Take 001" in every file)
                    var plan = ClipCleaner.PlanFor(sp.id, raw.length, sp.loop);
                    plan.Loop = sp.loop; plan.InPlace = sp.inPlace; plan.AutoTrim = sp.autoTrim; plan.FaceForward = sp.faceForward; plan.KeepRootFrame = sp.keepRoot;
                    plan.GroundFix = sp.ground; plan.AlsoMirror = sp.mirror; plan.Rate = sp.rate; plan.FindLoop = false; plan.Note = sp.note;
                    if (sp.loop) plan.AutoTrim = false;
                    var results = ClipCleaner.CleanClip(rig, raw, plan);
                    foreach (var r in results)
                    {
                        string nm = Path.GetFileNameWithoutExtension(r.path);
                        string dst = PhotoRosterV2.ClipDir + "/" + nm + ".anim";
                        if (File.Exists(dst)) AssetDatabase.DeleteAsset(dst);
                        string err = AssetDatabase.MoveAsset(r.path, dst);
                        if (!string.IsNullOrEmpty(err)) { log.AppendLine("move failed " + r.path + " " + err); continue; }
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
                        bool mir = nm.EndsWith("_M");
                        string key = sp.key + (mir ? "_m" : "");
                        // loop flags on the asset
                        var cs = AnimationUtility.GetAnimationClipSettings(clip);
                        cs.loopTime = sp.loop; cs.loopBlend = sp.loop; cs.keepOriginalPositionY = true; cs.keepOriginalPositionXZ = sp.keepRoot; cs.keepOriginalOrientation = true;
                        AnimationUtility.SetAnimationClipSettings(clip, cs);
                        EditorUtility.SetDirty(clip);
                        man.clips.Add(new PhotoRosterV2.ClipRec { key = key, name = nm, source = sp.file + (sp.file == "Clinch" ? "/" + sp.avatarPrefix : ""), length = clip.length, loop = sp.loop, note = sp.note });
                        log.AppendLine("CLEAN " + key + " -> " + dst + " len " + clip.length.ToString("0.000") + " s (in " + r.lenIn.ToString("0.00") + ") ground " + r.groundFix.ToString("0.000") + " heading " + r.heading.ToString("0.0") + " trim [" + r.t0.ToString("0.00") + "," + r.t1.ToString("0.00") + "] spikes " + r.spikes + " clamped " + r.clamped);
                        log.AppendLine("     " + Measure(rig, clip, sp, mir));
                    }
                }
            }
            finally { rig.Dispose(); }
            File.WriteAllText(PhotoRosterV2.ClipDir + "/clips.json", JsonUtility.ToJson(man, true));
            AssetDatabase.SaveAssets();
            // 3) the imported FBX copies hold the seller's body / scanned head: they are not needed any more (the .anim assets are independent of them)
            foreach (var p in imported.Values.Distinct()) AssetDatabase.DeleteAsset(p);
            if (AssetDatabase.IsValidFolder(PhotoRosterV2.BipedDir) && AssetDatabase.FindAssets("", new[] { PhotoRosterV2.BipedDir }).Length == 0) AssetDatabase.DeleteAsset(PhotoRosterV2.BipedDir);
            AssetDatabase.Refresh();
            Directory.CreateDirectory("D:/PIXFootball/Renders/roster_v2");
            File.WriteAllText("D:/PIXFootball/Renders/roster_v2/biped_clips_report.txt", log.ToString());
            Debug.Log("[PhotoRosterClips] done\n" + log);
        }

        // ------------------------------------------------------------------------------------------------ explicit Biped mapping (fallback / second skeleton)
        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

        static Dictionary<string, string> BipedNames(string pre)
        {
            var m = new Dictionary<string, string>
            {
                { "Hips", pre + " Pelvis" }, { "Spine", pre + " Spine" }, { "Chest", pre + " Spine1" }, { "UpperChest", pre + " Spine2" }, { "Neck", pre + " Neck" }, { "Head", pre + " Head" },
            };
            foreach (var side in new[] { "Left", "Right" })
            {
                string s = side == "Left" ? " L " : " R ";
                m[side + "Shoulder"] = pre + s + "Clavicle"; m[side + "UpperArm"] = pre + s + "UpperArm"; m[side + "LowerArm"] = pre + s + "Forearm"; m[side + "Hand"] = pre + s + "Hand";
                m[side + "UpperLeg"] = pre + s + "Thigh"; m[side + "LowerLeg"] = pre + s + "Calf"; m[side + "Foot"] = pre + s + "Foot"; m[side + "Toes"] = pre + s + "Toe0";
                string[] f = { "Finger0", "Finger1", "Finger2", "Finger3", "Finger4" };
                for (int i = 0; i < 5; i++)
                {
                    string stem = pre + s + f[i];
                    m[side + " " + Fingers[i] + " Proximal"] = stem;
                    m[side + " " + Fingers[i] + " Intermediate"] = stem + "1";
                    m[side + " " + Fingers[i] + " Distal"] = stem + "2";
                }
            }
            return m;
        }

        static void CollectSkeleton(Transform t, List<SkeletonBone> list)
        {
            list.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            foreach (Transform c in t) CollectSkeleton(c, list);
        }

        static bool ApplyBipedMap(ModelImporter mi, string pre, out string report)
        {
            report = "";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(mi.assetPath);
            if (go == null) { report = "no model"; return false; }
            var all = go.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>(all.Select(t => t.name));
            var map = BipedNames(pre);
            var human = new List<HumanBone>();
            var missing = new List<string>();
            foreach (var kv in map)
            {
                if (!names.Contains(kv.Value)) { missing.Add(kv.Key + "=" + kv.Value); continue; }
                var hb = new HumanBone { humanName = kv.Key, boneName = kv.Value };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            report = "mapped " + human.Count + " missing [" + string.Join(", ", missing.Take(12).ToArray()) + "]";
            string[] req = { "Hips", "Spine", "Head", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand" };
            if (req.Any(r => !human.Any(h => h.humanName == r))) return false;
            // skeleton = model root + the subtree that contains this skeleton's pelvis
            var pelvis = all.First(t => t.name == map["Hips"]);
            var rigRoot = pelvis;
            while (rigRoot.parent != null && rigRoot.parent != go.transform) rigRoot = rigRoot.parent;
            var skel = new List<SkeletonBone> { new SkeletonBone { name = go.name, position = go.transform.localPosition, rotation = go.transform.localRotation, scale = go.transform.localScale } };
            CollectSkeleton(rigRoot, skel);
            var d = mi.humanDescription;
            d.human = human.ToArray(); d.skeleton = skel.ToArray();
            if (d.upperArmTwist <= 0f) d.upperArmTwist = 0.5f; if (d.lowerArmTwist <= 0f) d.lowerArmTwist = 0.5f;
            if (d.upperLegTwist <= 0f) d.upperLegTwist = 0.5f; if (d.lowerLegTwist <= 0f) d.lowerLegTwist = 0.5f;
            if (d.armStretch <= 0f) d.armStretch = 0.05f; if (d.legStretch <= 0f) d.legStretch = 0.05f;
            d.hasTranslationDoF = false;
            mi.humanDescription = d;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ measurements of the cleaned clips
        static string Measure(AnimRig rig, AnimationClip clip, Spec sp, bool mirrored)
        {
            var fr = rig.Sample(clip, 0f, clip.length, 60f);
            if (fr.Count < 3) return "no frames";
            float minL = 9f, minR = 9f; int n = fr.Count;
            // frame positions relative to the start frame's hips (XZ) for the loop closure
            float closure = 0f; float maxRootTravel = 0f;
            Vector3 h0 = fr[0].P[AnimRig.Hips], hN = fr[n - 1].P[AnimRig.Hips];
            maxRootTravel = new Vector2(hN.x - h0.x, hN.z - h0.z).magnitude;
            for (int i = 0; i < n; i++) { minL = Mathf.Min(minL, fr[i].P[AnimRig.LF].y); minR = Mathf.Min(minR, fr[i].P[AnimRig.RF].y); }
            for (int b = 0; b < AnimRig.NB; b++)
            {
                Vector3 a = fr[0].P[b] - new Vector3(h0.x, 0f, h0.z), z = fr[n - 1].P[b] - new Vector3(hN.x, 0f, hN.z);
                closure = Mathf.Max(closure, Vector3.Distance(a, z));
            }
            // kicking foot: time of the fastest foot (right foot unless mirrored)
            int foot = mirrored ? AnimRig.LF : AnimRig.RF; float best = 0f; int bi = 0;
            for (int i = 1; i < n; i++)
            {
                float v = (fr[i].P[foot] - fr[i - 1].P[foot]).magnitude * 60f;
                if (v > best) { best = v; bi = i; }
            }
            string s = "lowest foot y L " + minL.ToString("0.000") + " R " + minR.ToString("0.000") + " | start->end pose distance (hips-relative) " + closure.ToString("0.000") + " m | hips travel " + maxRootTravel.ToString("0.00") + " m";
            if (sp.key.Contains("kick")) s += " | kick: fastest foot " + best.ToString("0.0") + " m/s at t=" + (bi / 60f).ToString("0.00") + " s";
            if (sp.loop) s += " | loop " + (closure < 0.08f ? "seamless" : "SEAM " + closure.ToString("0.00"));
            return s;
        }
    }
}
