using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// QA renders of the photoreal player (Docs/QA/photoreal): portraits per body type, comparison with the current PIX_Player_* (same studio, same cameras), the 24-player
    /// fictional roster sheet, helmet-off face sheet and ~24 representative clips on every body.  Run WITHOUT -nographics.
    /// Command line: -qaWhat portraits,compare,roster,faces,clips,lods  -qaSizes Small,Medium,Large
    /// </summary>
    public static class PhotoPlayerQa
    {
        /// <summary>Renders are big: they go to D:\PIXFootball\Renders\photoreal when that drive exists (lead instruction 2026-10-21), else Docs/QA/photoreal.</summary>
        static readonly string OutDir = Directory.Exists("D:/PIXFootball") ? "D:/PIXFootball/Renders/photoreal" : "Docs/QA/photoreal";
        static Camera _cam; static Light _sun;

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void RunAll()
        {
            Directory.CreateDirectory(OutDir);
            ArtLibrary.ResetCache(); PhotoPlayerLibrary.ResetCache();
            string what = Arg("-qaWhat", "portraits,compare,roster,faces,clips,lods");
            if (what.Contains("portraits")) Portraits();
            if (what.Contains("compare")) Compare();
            if (what.Contains("roster")) Roster(true, "roster_sheet");
            if (what.Contains("faces")) Roster(false, "roster_faces");
            if (what.Contains("clips")) Clips();
            if (what.Contains("lods")) Lods();
            if (what.Contains("perf")) Perf();
            if (what.Contains("eyes")) EyeSweep();
            if (what.Contains("skin")) SkinSweep();
            if (what.Contains("kit")) KitSheet();
            Debug.Log("[PhotoQa] done");
        }
        public static void RunPortraits() { Directory.CreateDirectory(OutDir); Portraits(); }
        public static void RunCompare() { Directory.CreateDirectory(OutDir); Compare(); }
        public static void RunRoster() { Directory.CreateDirectory(OutDir); Roster(true, "roster_sheet"); Roster(false, "roster_faces"); }
        public static void RunClips() { Directory.CreateDirectory(OutDir); Clips(); }
        public static void RunLods() { Directory.CreateDirectory(OutDir); Lods(); }
        public static void RunPerf() { Directory.CreateDirectory(OutDir); Perf(); }
        /// <summary>Rebuild the photoreal player assets, then the quick checks (one Unity launch).</summary>
        public static void BuildAndCheck()
        {
            PhotoPlayerBuilder.BuildAll();
            AssetDatabase.Refresh();
            ArtLibrary.ResetCache(); PhotoPlayerLibrary.ResetCache();
            Directory.CreateDirectory(OutDir);
            DebugHelmet();
            Portraits();
        }

        /// <summary>Rebuild + root / skin debug + the visual checks used while iterating (one Unity launch).</summary>
        public static void BuildAndCheck2()
        {
            PhotoPlayerBuilder.BuildAll();
            AssetDatabase.Refresh();
            ArtLibrary.ResetCache(); PhotoPlayerLibrary.ResetCache();
            Directory.CreateDirectory(OutDir);
            DebugVariants();
            DebugRoot();
            SkinSweep();
            Portraits();
            Compare();
            Roster(true, "roster_sheet");
            Roster(false, "roster_faces");
            KitSheet();
            Clips();
        }

        /// <summary>Rebuild + every QA render (portraits, compare, rosters, kit sheet, clips, posed LODs).</summary>
        public static void BuildAndRender()
        {
            PhotoPlayerBuilder.BuildAll();
            AssetDatabase.Refresh();
            ArtLibrary.ResetCache(); PhotoPlayerLibrary.ResetCache();
            Directory.CreateDirectory(OutDir);
            Portraits(); Compare(); Roster(true, "roster_sheet"); Roster(false, "roster_faces"); KitSheet(); Clips(); PosedLods();
        }

        public static void Debug2() { Directory.CreateDirectory(OutDir); DebugRoot(); SkinSweep(); }
        public static void ClipsAndLods() { Directory.CreateDirectory(OutDir); Clips(); PosedLods(); }

        /// <summary>Every LOD of every size in three poses (3-point stance, sprint, tackle): mesh and helmet must follow the animated skeleton at every level.</summary>
        public static void PosedLods()
        {
            Studio(11f);
            var lib = ArtLibrary.Load();
            var sb = new System.Text.StringBuilder();
            foreach (string size in Sizes())
            {
                var cells = new List<Texture2D>();
                foreach (string key in new[] { "stance_3pt", "sprint", "tackle_wrap" })
                    for (int lv = 0; lv < 4; lv++)
                    {
                        var clip = lib.ClipVariant(key, 0);
                        var g = SpawnPhoto(size, PlayerAppearance.FromSeed(77, BodyOf(size)), Kits[1], 21, "QUINN");
                        var pp = g.GetComponent<PhotoPlayer>(); pp.autoLod = false; pp.Init(); pp.SetLevel(lv);
                        PoseClip(g, clip, clip.length * 0.45f);
                        Normalize(g);
                        var a = g.GetComponent<Animator>();
                        // numeric check: every skinned part's bones all live below the animator and the helmet shell is below the head bone
                        var head = a.GetBoneTransform(HumanBodyBones.Head);
                        foreach (var part in pp.parts)
                            foreach (var r in part.lods)
                            {
                                if (r == null || !r.gameObject.activeInHierarchy) continue;
                                var smr = r as SkinnedMeshRenderer;
                                if (smr != null)
                                {
                                    int bad = 0; foreach (var b in smr.bones) if (b == null || !b.IsChildOf(g.transform)) bad++;
                                    if (bad > 0 || smr.bones.Length != smr.sharedMesh.bindposes.Length) sb.AppendLine(size + " " + key + " LOD" + lv + " " + part.key + " bones outside rig " + bad + " bones " + smr.bones.Length + " bindposes " + smr.sharedMesh.bindposes.Length);
                                }
                                else if (part.key.StartsWith("Helmet") || part.key == "FaceMask") { if (!r.transform.IsChildOf(head)) sb.AppendLine(size + " " + key + " LOD" + lv + " " + part.key + " NOT under head bone"); }
                            }
                        Bake(g);
                        cells.Add(Shot(new Vector3(0.0f, 1.2f, 6.0f), new Vector3(0, 0.95f, 0), 26f, 300, 360));
                        UnityEngine.Object.DestroyImmediate(g);
                    }
                Save(Sheet(cells.ToArray(), 4, 300, 360), "posed_lods_" + size.ToLowerInvariant());
            }
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            Debug.Log("[PhotoQa] posed lods: " + (sb.Length == 0 ? "all skinned parts bound inside the rig, helmet under head bone at every LOD" : sb.ToString().Replace("\n", " | ")));
        }
        public static void DebugHarness()
        {
            var lib = ArtLibrary.Load();
            var sb = new System.Text.StringBuilder();
            foreach (string mode in new[] { "photo", "current" })
            {
                GameObject g = mode == "photo" ? SpawnPhoto("Medium", PlayerAppearance.FromSeed(3, PhotoBody.Medium), Kits[0], 7, "ALDER") : SpawnCurrent("Medium", Kits[0]);
                var anim = g.GetComponentInChildren<Animator>();
                var clip = lib.ClipVariant("idle", 0);
                Func<string> st = () => "hips y " + anim.GetBoneTransform(HumanBodyBones.Hips).position.y.ToString("F3") + " knee y " + anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position.y.ToString("F3") + " foot y " + anim.GetBoneTransform(HumanBodyBones.LeftFoot).position.y.ToString("F3") + " head y " + anim.GetBoneTransform(HumanBodyBones.Head).position.y.ToString("F3");
                sb.AppendLine("== " + mode + " rest: " + st());
                // B: AnimationMode
                AnimationMode.StartAnimationMode();
                AnimationMode.SampleAnimationClip(anim.gameObject, clip, 1.2f);
                sb.AppendLine("   B AnimationMode.Sample: " + st());
                AnimationMode.StopAnimationMode();
                sb.AppendLine("   after stop: " + st());
                // C: playable graph single evaluate with Play
                PlayableGraph graph;
                PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out graph);
                graph.Evaluate(1.2f);
                sb.AppendLine("   C graph one Evaluate(1.2): " + st());
                graph.Destroy();
                // D: playable graph manual time
                var graph2 = PlayableGraph.Create(); graph2.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph2, "o", anim); var cp = AnimationClipPlayable.Create(graph2, clip); output.SetSourcePlayable(cp);
                cp.SetTime(1.2); graph2.Evaluate(0f);
                sb.AppendLine("   D manual SetTime 1.2: " + st());
                graph2.Destroy();
                UnityEngine.Object.DestroyImmediate(g);
            }
            Debug.Log("[PhotoQa] harness: " + sb.ToString().Replace("\n", " | "));
        }
        public static void DebugClipCurves()
        {
            var lib = ArtLibrary.Load();
            var sb = new System.Text.StringBuilder();
            foreach (var key in new[] { "idle", "sprint" })
            {
                var clip = lib.ClipVariant(key, 0); if (clip == null) continue;
                sb.AppendLine("== " + key + " " + clip.name + " len " + clip.length + " path " + AssetDatabase.GetAssetPath(clip) + " bindings:");
                float t = clip.length * 0.45f;
                int n = 0;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    var c = AnimationUtility.GetEditorCurve(clip, b);
                    string pn = b.propertyName;
                    if (n < 3 || pn.Contains("Left Upper Leg") || pn.Contains("Left Lower Leg") || pn.StartsWith("RootQ") || pn.StartsWith("RootT") || pn.StartsWith("Spine Front") || pn.StartsWith("Left Arm Down"))
                        sb.AppendLine("   [" + b.type.Name + "] '" + b.path + "' " + pn + " = " + c.Evaluate(t).ToString("F3") + " (keys " + c.length + ")");
                    n++;
                }
                sb.AppendLine("   total bindings " + n);
            }
            Debug.Log("[PhotoQa] curves: " + sb.ToString().Replace("\n", " | "));
        }
        public static void DebugMuscles()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string mode in new[] { "photo", "current" })
            {
                GameObject g = mode == "photo" ? SpawnPhoto("Medium", PlayerAppearance.FromSeed(3, PhotoBody.Medium), Kits[0], 7, "ALDER") : SpawnCurrent("Medium", Kits[0]);
                var anim = g.GetComponentInChildren<Animator>();
                var h = new HumanPoseHandler(anim.avatar, anim.transform);
                var hp = new HumanPose(); h.GetHumanPose(ref hp);
                sb.AppendLine("== " + mode + " rest body pos " + hp.bodyPosition.ToString("F3") + " rot " + hp.bodyRotation.eulerAngles.ToString("F0"));
                var names = HumanTrait.MuscleName;
                for (int i = 0; i < hp.muscles.Length && i < names.Length; i++) if (Mathf.Abs(hp.muscles[i]) > 0.25f) sb.AppendLine("   rest muscle " + names[i] + " = " + hp.muscles[i].ToString("F2"));
                var zp = new HumanPose(); zp.bodyPosition = new Vector3(0, hp.bodyPosition.y, 0); zp.bodyRotation = Quaternion.identity; zp.muscles = new float[hp.muscles.Length];
                h.SetHumanPose(ref zp);
                foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head })
                    sb.AppendLine("   zero-pose " + hb + " wpos " + anim.GetBoneTransform(hb).position.ToString("F3"));
                h.Dispose();
                UnityEngine.Object.DestroyImmediate(g);
            }
            Debug.Log("[PhotoQa] muscles: " + sb.ToString().Replace("\n", " | "));
        }
        public static void DebugLegs()
        {
            var lib = ArtLibrary.Load();
            var sb = new System.Text.StringBuilder();
            foreach (string mode in new[] { "photo", "current" })
            {
                GameObject g = mode == "photo" ? SpawnPhoto("Medium", PlayerAppearance.FromSeed(3, PhotoBody.Medium), Kits[0], 7, "ALDER") : SpawnCurrent("Medium", Kits[0]);
                var anim = g.GetComponentInChildren<Animator>();
                sb.AppendLine("== " + mode + " controller " + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "none") + " enabled " + anim.enabled);
                foreach (HumanBodyBones hb in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HumanBodyBones.Spine, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand, HumanBodyBones.Head })
                {
                    var t = anim.GetBoneTransform(hb);
                    sb.AppendLine("   " + hb + " -> " + (t != null ? t.name + " parent " + (t.parent != null ? t.parent.name : "-") + " lpos " + t.localPosition.ToString("F3") + " lrot " + t.localEulerAngles.ToString("F0") + " wpos " + t.position.ToString("F3") : "NULL"));
                }
                foreach (var key in new[] { "idle", "sprint", "stance_3pt" })
                {
                    var clip = lib.ClipVariant(key, 0); if (clip == null) { sb.AppendLine("   " + key + " clip null"); continue; }
                    PoseClip(g, clip, clip.length * 0.45f);
                    sb.AppendLine("   -- " + key + " clip " + clip.name + " human " + clip.isHumanMotion + " legacy " + clip.legacy + " len " + clip.length);
                    foreach (HumanBodyBones hb in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot })
                    {
                        var t = anim.GetBoneTransform(hb);
                        sb.AppendLine("      " + hb + " lpos " + t.localPosition.ToString("F3") + " lrot " + t.localEulerAngles.ToString("F0") + " wpos " + t.position.ToString("F3"));
                    }
                }
                UnityEngine.Object.DestroyImmediate(g);
            }
            Debug.Log("[PhotoQa] legs: " + sb.ToString().Replace("\n", " | "));
        }

        public static void DebugRoot()
        {
            var lib = ArtLibrary.Load();
            var sb = new System.Text.StringBuilder();
            foreach (string mode in new[] { "photo", "current" })
            {
                GameObject g = mode == "photo" ? SpawnPhoto("Medium", PlayerAppearance.FromSeed(3, PhotoBody.Medium), Kits[0], 7, "ALDER") : SpawnCurrent("Medium", Kits[0]);
                var anim = g.GetComponentInChildren<Animator>();
                sb.AppendLine("== " + mode + " animator on " + anim.gameObject.name + " pos " + anim.transform.position + " lossyScale " + anim.transform.lossyScale + " avatar " + anim.avatar.name);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                for (var t = hips; t != null && t != anim.transform.parent; t = t.parent) sb.AppendLine("   chain " + t.name + " localScale " + t.localScale + " localPos " + t.localPosition + " world " + t.position);
                sb.AppendLine("   rest hips y " + hips.position.y + " foot y " + anim.GetBoneTransform(HumanBodyBones.LeftFoot).position.y + " head y " + anim.GetBoneTransform(HumanBodyBones.Head).position.y);
                foreach (var key in new[] { "idle", "sprint", "stance_3pt" })
                {
                    var clip = lib.ClipVariant(key, 0); if (clip == null) continue;
                    PoseClip(g, clip, clip.length * 0.45f);
                    sb.AppendLine("   " + key + ": hips y " + hips.position.y.ToString("F3") + " leftFoot y " + anim.GetBoneTransform(HumanBodyBones.LeftFoot).position.y.ToString("F3") + " rightFoot y " + anim.GetBoneTransform(HumanBodyBones.RightFoot).position.y.ToString("F3") + " root pos " + anim.transform.position + " hips world " + hips.position);
                }
                UnityEngine.Object.DestroyImmediate(g);
            }
            Debug.Log("[PhotoQa] root debug: " + sb.ToString().Replace("\n", " | "));
        }

        /// <summary>Which FBX export variant retargets correctly? Imports every FBX in MeshesTest as Humanoid, poses a few clips and reports hips / feet / hand heights.</summary>
        public static void DebugVariants()
        {
            var lib = ArtLibrary.Load();
            var sb = new System.Text.StringBuilder();
            string dir = PhotoPlayerBuilder.Root + "/MeshesTest";
            if (!Directory.Exists(dir)) { Debug.Log("[PhotoQa] variants: no MeshesTest"); return; }
            foreach (string f in Directory.GetFiles(dir, "*.fbx"))
            {
                string p = f.Replace('\\', '/');
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) { sb.AppendLine(p + " no importer"); continue; }
                mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = true;
                mi.SaveAndReimport();
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var av = HumanoidMapper.GetAvatar(p);
                sb.AppendLine("== " + Path.GetFileName(p) + " avatar " + (av != null ? "valid=" + av.isValid + " human=" + av.isHuman : "none"));
                if (asset == null || av == null || !av.isHuman) continue;
                var g = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                var anim = g.GetComponent<Animator>();
                if (anim == null) anim = g.AddComponent<Animator>();
                anim.avatar = av; anim.applyRootMotion = false;
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                for (var t = hips; t != null && t != g.transform.parent; t = t.parent) sb.AppendLine("   chain " + t.name + " scale " + t.localScale + " lossy " + t.lossyScale);
                Func<string> st = () => "hips y " + hips.position.y.ToString("F3") + " footL " + anim.GetBoneTransform(HumanBodyBones.LeftFoot).position.y.ToString("F3") + " footR " + anim.GetBoneTransform(HumanBodyBones.RightFoot).position.y.ToString("F3")
                    + " handL y " + anim.GetBoneTransform(HumanBodyBones.LeftHand).position.y.ToString("F3") + " head y " + anim.GetBoneTransform(HumanBodyBones.Head).position.y.ToString("F3");
                sb.AppendLine("   rest: " + st());
                foreach (var key in new[] { "idle", "sprint", "stance_3pt", "qb_throw", "catch_high" })
                {
                    var clip = lib.ClipVariant(key, 0); if (clip == null) continue;
                    PoseClip(g, clip, clip.length * 0.45f);
                    sb.AppendLine("   " + key + ": " + st());
                }
                UnityEngine.Object.DestroyImmediate(g);
            }
            Debug.Log("[PhotoQa] variants: " + sb.ToString().Replace("\n", " | "));
        }

        /// <summary>One player, twelve kit / face options: visors, sleeves, gloves, towel, pouch, beards (Docs/QA/photoreal/kit_options.png).</summary>
        static void KitSheet()
        {
            Studio();
            var cells = new List<Texture2D>();
            for (int i = 0; i < 12; i++)
            {
                var a = PlayerAppearance.FromSeed(77, PhotoBody.Medium, PlayerAppearance.RoleQuarterback);
                a.Visor = i == 0 ? VisorStyle.Clear : i == 1 ? VisorStyle.Smoke : i == 2 ? VisorStyle.Mirror : VisorStyle.None;
                a.Sleeves = i == 3 ? SleeveMode.Left : i == 4 ? SleeveMode.Right : i == 5 ? SleeveMode.Both : SleeveMode.None;
                a.SleeveColor = i == 5 ? AccentColor.TeamSecondary : AccentColor.White;
                a.Gloves = i == 6 || i == 7; a.GloveColor = i == 6 ? AccentColor.Black : AccentColor.TeamSecondary;
                a.Towel = i == 8; a.Pouch = i == 8 || i == 9; a.PlayCard = i == 9; a.Wristbands = i == 9 || i == 10;
                a.FacialHair = i == 10 ? FacialHair.FullBeard : i == 11 ? FacialHair.Goatee : FacialHair.None;
                var g = SpawnPhoto("Medium", a, Kits[(i / 3) % Kits.Length], 10 + i, "QUINN");
                Bake(g);
                g.transform.RotateAround(Vector3.zero, Vector3.up, 25f);
                cells.Add(Shot(new Vector3(0, 1.18f, 3.6f), new Vector3(0, 1.12f, 0), 24f, 330, 480));
                UnityEngine.Object.DestroyImmediate(g);
            }
            Save(Sheet(cells.ToArray(), 6, 330, 480), "kit_options");
        }

        public static void DebugHelmet()
        {
            var g = SpawnPhoto("Medium", PlayerAppearance.FromSeed(3, PhotoBody.Medium), Kits[0], 7, "ALDER");
            var pp = g.GetComponent<PhotoPlayer>();
            var m = pp.HelmetMaterial;
            var sb = new System.Text.StringBuilder();
            foreach (var n in new[] { "_PixHW0", "_PixHW1", "_PixHW2", "_PixHN0", "_PixHN1", "_PixHN2", "_PixHelmetDecal", "_PixPrimary", "_PixSecondary", "_PixTertiary" }) sb.AppendLine(n + " = " + m.GetVector(n).ToString("F4"));
            sb.AppendLine("shader " + m.shader.name + " mode " + m.GetFloat("_PixMode") + " colorMask " + (m.GetTexture("_PixColorMask") != null ? m.GetTexture("_PixColorMask").name : "null"));
            var shell = pp.parts.First(x => x.key == "Helmet_Shell").lods[0];
            var mf = shell.GetComponent<MeshFilter>();
            sb.AppendLine("shell mesh bounds " + mf.sharedMesh.bounds + " holder " + (pp.headBone.Find("Helmet") != null));
            var a = pp.animator;
            sb.AppendLine("animator human " + a.isHuman + " L " + a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position + " R " + a.GetBoneTransform(HumanBodyBones.RightUpperLeg).position);
            Debug.Log("[PhotoQa] helmet debug: " + sb.ToString().Replace("\n", " | "));
            UnityEngine.Object.DestroyImmediate(g);
        }

        public static void RunSkin() { Directory.CreateDirectory(OutDir); SkinSweep(); }

        /// <summary>Skin material response: roughness value sweep on the Arm_Skin graph (arms close-up + face close-up).</summary>
        static void SkinSweep()
        {
            Studio();
            var cells = new List<Texture2D>();
            foreach (float rv in new[] { 0.0f, 0.5f, 1.0f, 2.0f })
            {
                var a = PlayerAppearance.FromSeed(8, PhotoBody.Medium); a.SkinTone = 3; a.FacialHair = FacialHair.None; a.Gloves = false; a.Sleeves = SleeveMode.None;
                var g = SpawnPhoto("Medium", a, Kits[0], 7, "ALDER", false);
                var pp = g.GetComponent<PhotoPlayer>();
                foreach (var key in new[] { "Head", "Arms" })
                    foreach (var r in pp.parts.First(p => p.key == key).lods)
                    {
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++) { var m = new Material(mats[i]); m.SetFloat("_Roughness_Value", rv); mats[i] = m; }
                        r.sharedMaterials = mats;
                    }
                Bake(g);
                cells.Add(Shot(new Vector3(0.9f, 1.25f, 1.3f), new Vector3(0.6f, 1.22f, 0), 18f, 400, 400));
                cells.Add(Shot(new Vector3(0.3f, 1.72f, 0.9f), new Vector3(0f, 1.7f, 0), 16f, 400, 400));
                UnityEngine.Object.DestroyImmediate(g);
            }
            Save(Sheet(cells.ToArray(), 4, 400, 400), "skin_roughness_sweep");
        }

        public static void RunEyes() { Directory.CreateDirectory(OutDir); EyeSweep(); }

        /// <summary>Iris hue sweep of the Eyes_Shader (_Hue in degrees, _Saturation): used to pick the five eye colour presets of the library.</summary>
        static void EyeSweep()
        {
            Studio();
            var cells = new List<Texture2D>();
            float[] hues = { 0f, 30f, 47.8f, 70f, 100f, 140f, 180f, 200f, 220f, 250f, 290f, 330f };
            foreach (float h in hues)
            {
                var a = PlayerAppearance.FromSeed(8, PhotoBody.Medium); a.FacialHair = FacialHair.None; a.SkinTone = 2; a.Scalp = ScalpStyle.Shaved;
                var g = SpawnPhoto("Medium", a, Kits[0], 7, "ALDER", false);
                var pp = g.GetComponent<PhotoPlayer>();
                var eyes = pp.parts.First(p => p.key == "Eyes").lods[0].sharedMaterial;
                eyes.SetFloat("_Hue", h); eyes.SetFloat("_Saturation", 1f);
                Bake(g);
                cells.Add(Shot(new Vector3(0, 1.74f, 0.62f), new Vector3(0, 1.74f, 0), 14f, 420, 300));
                UnityEngine.Object.DestroyImmediate(g);
            }
            Save(Sheet(cells.ToArray(), 4, 420, 300), "eye_hue_sweep");
        }

        // ------------------------------------------------------------------ studio / rendering
        static void Studio(float exposure = 12.5f)
        {
            AssetScreenshots.NewStudio(exposure, out _sun);
            _cam = AssetScreenshots.MakeCamera(new Vector3(0, 1f, 8f), new Vector3(0, 1f, 0), 22f);
            _cam.backgroundColor = new Color(0.30f, 0.33f, 0.38f);
            var hd = _cam.GetComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = new Color(0.22f, 0.25f, 0.30f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "QaFloor"; floor.transform.position = new Vector3(0, -0.02f, 0); floor.transform.localScale = new Vector3(14f, 0.04f, 14f);
            var fm = new Material(Shader.Find("HDRP/Lit")); fm.SetColor("_BaseColor", new Color(0.14f, 0.2f, 0.13f)); fm.SetFloat("_Smoothness", 0.15f);
            floor.GetComponent<Renderer>().sharedMaterial = fm;
        }

        static Texture2D Shot(Vector3 camPos, Vector3 look, float fov, int w, int h)
        {
            _cam.transform.position = camPos; _cam.transform.LookAt(look); _cam.fieldOfView = fov; _cam.aspect = (float)w / h;
            return AssetScreenshots.Render(_cam, w, h);
        }

        /// <summary>Replaces every skinned renderer by a static copy of its posed mesh (edit-mode skinning does not refresh while rendering by hand).</summary>
        static void Bake(GameObject go)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, false);
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(smr.transform, false);
                var ls = smr.transform.lossyScale; child.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                var mr = child.AddComponent<MeshRenderer>(); mr.sharedMaterials = smr.sharedMaterials; mr.shadowCastingMode = smr.shadowCastingMode;
                smr.enabled = false;
            }
        }

        static void Normalize(GameObject go)
        {
            var a = go.GetComponent<Animator>();
            var L = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position; var R = a.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;
            var fwd = Vector3.Cross(R - L, Vector3.up); fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f) go.transform.RotateAround(a.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.up, Vector3.SignedAngle(fwd.normalized, Vector3.forward, Vector3.up));
            var hp = a.GetBoneTransform(HumanBodyBones.Hips).position;
            go.transform.position -= new Vector3(hp.x, 0f, hp.z);
        }

        static void PoseClip(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || clip == null) return;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(anim.gameObject, clip, t);
        }

        // ------------------------------------------------------------------ subjects
        public struct Kit { public string name, wordmark; public Color p, s, t; }
        public static readonly Kit[] Kits =
        {
            new Kit { name = "Harbor Rovers", wordmark = "ROVERS", p = Hex("#12306B"), s = Hex("#FFC837"), t = Hex("#F2F2F2") },
            new Kit { name = "Summit Foxes", wordmark = "FOXES", p = Hex("#B01E2A"), s = Hex("#F2F2F2"), t = Hex("#1A1A1A") },
            new Kit { name = "Pine Rangers", wordmark = "RANGERS", p = Hex("#0F5B3A"), s = Hex("#E8E2C8"), t = Hex("#F0B323") },
            new Kit { name = "Copper Kings", wordmark = "KINGS", p = Hex("#D9741A"), s = Hex("#1B2A41"), t = Hex("#F5F5F5") },
            new Kit { name = "Night Owls", wordmark = "OWLS", p = Hex("#2B2155"), s = Hex("#8E9BB5"), t = Hex("#F2F2F2") },
            new Kit { name = "Steel Hawks", wordmark = "HAWKS", p = Hex("#5B6770"), s = Hex("#0B1D3A"), t = Hex("#E84C3D") },
        };
        static readonly string[] Surnames = { "ALDER", "BRANDT", "CALLOWAY", "DUNMORE", "EVERETT", "FAIRBANK", "GRAYSON", "HOLLIS", "IRVING", "JASPER", "KESSLER", "LOWELL", "MARSDEN", "NORTON", "OAKLEY", "PRESCOTT", "QUINN", "RANDALL", "STANTON", "TALBOT", "UPTON", "VANCE", "WESTON", "YARROW" };

        static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c.linear; }

        public static GameObject SpawnPhoto(string size, PlayerAppearance a, Kit kit, int number, string surname, bool helmet = true)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhotoPlayerBuilder.PrefabDir + "/PIX_PhotoPlayer_" + size + ".prefab");
            if (prefab == null) return null;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.transform.position = Vector3.zero; g.transform.rotation = Quaternion.identity;
            var pp = g.GetComponent<PhotoPlayer>();
            pp.autoLod = false;
            pp.Init();
            pp.SetTeamColors(kit.p, kit.s, kit.t);
            pp.ApplyAppearance(a);
            var look = new JerseyLook { Number = number, HasNumber = true, Fill = kit.t, Trim = kit.s, Name = surname, Wordmark = kit.wordmark, Dirt = default(DirtState) };
            var jm = pp.JerseyMaterial;
            if (jm != null) { PixLook.ApplyJersey(jm, look); pp.ApplyLayout(jm); }
            if (!helmet) foreach (var k in new[] { "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Visor" }) pp.SetPartVisible(k, false);
            pp.SetLevel(0);
            return g;
        }

        static GameObject SpawnCurrent(string size, Kit kit)
        {
            var lib = ArtLibrary.Load();
            var prefab = lib != null ? lib.Prefab("player_" + size.ToLowerInvariant()) : null;
            if (prefab == null) return null;
            var av = PlayerAvatar.Spawn(null, size == "Small" ? PlayerSize.Small : size == "Large" ? PlayerSize.Large : PlayerSize.Medium, new TeamColors(kit.p, kit.s, kit.t), "current");
            if (av == null) return null;
            av.SetJersey(new JerseyLook { Number = 12, HasNumber = true, Fill = kit.t, Trim = kit.s, Name = "ALDER", Wordmark = kit.wordmark, Dirt = default(DirtState) });
            av.SetHelmetColors(new TeamColors(kit.p, kit.s, kit.t));
            return av.gameObject;
        }

        static void Save(Texture2D t, string name)
        {
            File.WriteAllBytes(OutDir + "/" + name + ".png", t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            Debug.Log("[PhotoQa] wrote " + OutDir + "/" + name + ".png");
        }

        static Texture2D Sheet(Texture2D[] cells, int cols, int cw, int ch)
        {
            int rows = (cells.Length + cols - 1) / cols;
            var t = new Texture2D(cols * cw, rows * ch, TextureFormat.RGB24, false);
            var fill = new Color32[t.width * t.height]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(28, 30, 34, 255); t.SetPixels32(fill);
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == null) continue;
                int cx = i % cols, cy = i / cols;
                t.SetPixels32(cx * cw, (rows - 1 - cy) * ch, cw, ch, cells[i].GetPixels32());
                UnityEngine.Object.DestroyImmediate(cells[i]);
            }
            t.Apply(false);
            return t;
        }

        static string[] Sizes() { return Arg("-qaSizes", "Small,Medium,Large").Split(','); }
        static PhotoBody BodyOf(string size) { return size == "Small" ? PhotoBody.Small : size == "Large" ? PhotoBody.Large : PhotoBody.Medium; }

        // ------------------------------------------------------------------ portraits
        static void Portraits()
        {
            Studio();
            foreach (string size in Sizes())
            {
                var a = PlayerAppearance.FromSeed(size == "Small" ? 11 : size == "Medium" ? 22 : 33, BodyOf(size), size == "Large" ? PlayerAppearance.RoleLine : PlayerAppearance.RoleSkill);
                var g = SpawnPhoto(size, a, Kits[0], size == "Small" ? 12 : size == "Medium" ? 28 : 74, "ALDER");
                if (g == null) { Debug.LogWarning("[PhotoQa] no prefab " + size); continue; }
                Bake(g);
                var cells = new List<Texture2D>();
                cells.Add(Shot(new Vector3(0, 1.05f, 7.5f), new Vector3(0, 0.95f, 0), 22f, 640, 900));
                var oldYaw = g.transform.rotation; g.transform.RotateAround(Vector3.zero, Vector3.up, 35f);
                cells.Add(Shot(new Vector3(0, 1.05f, 7.5f), new Vector3(0, 0.95f, 0), 22f, 640, 900));
                g.transform.rotation = oldYaw; g.transform.RotateAround(Vector3.zero, Vector3.up, 180f);
                cells.Add(Shot(new Vector3(0, 1.05f, 7.5f), new Vector3(0, 0.95f, 0), 22f, 640, 900));
                g.transform.rotation = oldYaw; g.transform.RotateAround(Vector3.zero, Vector3.up, 30f);
                cells.Add(Shot(new Vector3(0, 1.62f, 2.0f), new Vector3(0, 1.58f, 0), 20f, 640, 900));
                Save(Sheet(cells.ToArray(), 4, 640, 900), "unity_" + size.ToLowerInvariant() + "_portrait");
                UnityEngine.Object.DestroyImmediate(g);
            }
        }

        // ------------------------------------------------------------------ comparison with the current players
        static void Compare()
        {
            Studio();
            foreach (string size in Sizes())
            {
                var cells = new List<Texture2D>();
                var kit = Kits[0];
                var cur = SpawnCurrent(size, kit);
                var photo = SpawnPhoto(size, PlayerAppearance.FromSeed(size == "Small" ? 11 : size == "Medium" ? 22 : 33, BodyOf(size)), kit, 12, "ALDER");
                if (cur == null || photo == null) { Debug.LogWarning("[PhotoQa] compare: missing " + size); if (cur != null) UnityEngine.Object.DestroyImmediate(cur); if (photo != null) UnityEngine.Object.DestroyImmediate(photo); continue; }
                photo.SetActive(false);
                var curAnim = cur.GetComponentInChildren<Animator>();
                if (curAnim != null) { Normalize(curAnim.gameObject); }
                Bake(cur);
                foreach (float yaw in new[] { 0f, 35f, 180f }) { cur.transform.RotateAround(Vector3.zero, Vector3.up, yaw == 0f ? 0f : yaw == 35f ? 35f : 145f); cells.Add(Shot(new Vector3(0, 1.05f, 7.5f), new Vector3(0, 0.95f, 0), 22f, 560, 900)); }
                cells.Add(Shot(new Vector3(0, 1.6f, 2.2f), new Vector3(0, 1.55f, 0), 20f, 560, 900));
                UnityEngine.Object.DestroyImmediate(cur);
                photo.SetActive(true);
                Bake(photo);
                foreach (float yaw in new[] { 0f, 35f, 145f }) { photo.transform.RotateAround(Vector3.zero, Vector3.up, yaw); cells.Add(Shot(new Vector3(0, 1.05f, 7.5f), new Vector3(0, 0.95f, 0), 22f, 560, 900)); }
                photo.transform.rotation = Quaternion.identity; photo.transform.RotateAround(Vector3.zero, Vector3.up, 30f);
                cells.Add(Shot(new Vector3(0, 1.6f, 2.2f), new Vector3(0, 1.55f, 0), 20f, 560, 900));
                Save(Sheet(cells.ToArray(), 4, 560, 900), "compare_current_vs_photo_" + size.ToLowerInvariant());
                UnityEngine.Object.DestroyImmediate(photo);
            }
        }

        // ------------------------------------------------------------------ roster sheet
        static void Roster(bool helmet, string name)
        {
            Studio();
            var cells = new List<Texture2D>();
            int[] roles = { 0, 1, 2, 3, 0, 2 };
            for (int i = 0; i < 24; i++)
            {
                int seed = 1000 + i * 37;
                var body = i % 3 == 1 ? PhotoBody.Medium : (i % 3 == 2 ? PhotoBody.Large : PhotoBody.Small);
                var a = PlayerAppearance.FromSeed(seed, body, roles[i % roles.Length]);
                string size = body == PhotoBody.Small ? "Small" : body == PhotoBody.Large ? "Large" : "Medium";
                var kit = Kits[(i / 4) % Kits.Length];
                var g = SpawnPhoto(size, a, kit, 3 + (i * 11) % 96, Surnames[i], helmet);
                if (g == null) { cells.Add(null); continue; }
                Bake(g);
                g.transform.RotateAround(Vector3.zero, Vector3.up, helmet ? 22f : 14f);
                float scale = body == PhotoBody.Large ? 1.12f : 1f;
                cells.Add(Shot(new Vector3(0, 1.56f, 2.6f * scale), new Vector3(0, helmet ? 1.5f : 1.64f, 0), helmet ? 22f : 11f, 320, 460));
                UnityEngine.Object.DestroyImmediate(g);
            }
            Save(Sheet(cells.ToArray(), 8, 320, 460), name);
        }

        // ------------------------------------------------------------------ representative clips
        static readonly string[] ClipKeys =
        {
            "idle", "stance_2pt", "stance_3pt", "dl_stance", "ol_stance", "qb_stance_shotgun", "run", "sprint", "backpedal", "cut_hard", "stiff_arm", "tackle_wrap",
            "be_tackled", "ol_pass_set", "ol_block_engage", "dl_rush_swim", "qb_throw", "qb_drop_5", "catch_high", "catch_low", "catch_dive", "celebrate_td", "fall", "hurdle",
        };

        static void Clips()
        {
            Studio(11f);
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogWarning("[PhotoQa] no ArtLibrary"); return; }
            var rows = new List<string>();
            foreach (string size in Sizes())
            {
                var cells = new List<Texture2D>();
                foreach (string key in ClipKeys)
                {
                    var clip = lib.ClipVariant(key, 0);
                    if (clip == null) { cells.Add(null); rows.Add(size + " " + key + " MISSING"); continue; }
                    var g = SpawnPhoto(size, PlayerAppearance.FromSeed(77, BodyOf(size)), Kits[1], 21, "QUINN");
                    PoseClip(g, clip, clip.length * 0.45f);
                    Normalize(g);
                    Bake(g);
                    var hips = g.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position;
                    cells.Add(Shot(new Vector3(0.0f, 1.2f, 6.0f), new Vector3(0, 0.95f, 0), 26f, 300, 360));
                    UnityEngine.Object.DestroyImmediate(g);
                    rows.Add(size + " " + key);
                }
                Save(Sheet(cells.ToArray(), 6, 300, 360), "clips_" + size.ToLowerInvariant());
            }
            File.WriteAllText(OutDir + "/clips_index.txt", string.Join("\n", rows.ToArray()));
        }

        // ------------------------------------------------------------------ performance: 22 players at 2560x1440 (studio only: players + floor, no stadium / crowd)
        static void Perf()
        {
            Studio(11f);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("22 players, 2560x1440, Madden-style camera (10 m behind the line, 7 m up), studio lighting, frames timed with a GPU sync every 4 frames; machine: " + SystemInfo.graphicsDeviceName);
            foreach (string mode in new[] { "none", "photo", "current", "photo" })
                foreach (float dist in new[] { 13f })
                {
                    var players = new List<GameObject>();
                    for (int i = 0; i < 22; i++)
                    {
                        int row = i / 11, col = i % 11;
                        string size = col < 3 || col > 8 ? "Large" : (col % 2 == 0 ? "Medium" : "Small");
                        if (mode == "none") break;
                        GameObject g = mode == "photo" ? SpawnPhoto(size, PlayerAppearance.FromSeed(5000 + i, BodyOf(size)), Kits[row], 10 + i, Surnames[i]) : SpawnCurrent(size, Kits[row]);
                        if (g == null) continue;
                        g.transform.position = new Vector3((col - 5) * 1.25f, 0f, row * 1.6f);
                        g.transform.rotation = Quaternion.Euler(0, row == 0 ? 0f : 180f, 0);
                        players.Add(g);
                    }
                    _cam.transform.position = new Vector3(0f, 7f, -dist + 0f); _cam.transform.LookAt(new Vector3(0f, 1f, 3f)); _cam.fieldOfView = 38f;
                    var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    _cam.targetTexture = rt; _cam.aspect = 2560f / 1440f;
                    PhotoPlayer.ViewCamera = _cam;
                    int[] lodCount = new int[4]; long tris = 0;
                    foreach (var g in players)
                    {
                        var pp = g.GetComponent<PhotoPlayer>();
                        if (pp != null)
                        {
                            int lv = PhotoPlayer.LevelFor(Vector3.Distance(_cam.transform.position, g.transform.position), pp.near, pp.mid, pp.far);
                            pp.SetLevel(lv); lodCount[lv]++;
                        }
                        var av = g.GetComponent<PlayerAvatar>();
                        if (av != null && av.Lod != null) { PlayerLod.ViewCamera = _cam; av.Lod.Update(g.transform.position); lodCount[Mathf.Clamp(av.Lod.Level, 0, 3)]++; }
                        foreach (var r in g.GetComponentsInChildren<Renderer>())
                        {
                            Mesh m = null; var smr = r as SkinnedMeshRenderer; if (smr != null) m = smr.sharedMesh; else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
                            if (m != null) for (int sm = 0; sm < m.subMeshCount; sm++) tris += m.GetIndexCount(sm) / 3;
                        }
                    }
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    for (int i = 0; i < 20; i++) _cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
                    int frames = 80; var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < frames; i++)
                    {
                        _cam.Render();
                        if (i % 4 == 3) { RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null; }
                    }
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds / frames;
                    sb.AppendLine(string.Format("{0,-8} camera {1,2} m: {2,6:F2} ms/frame = {3,6:F1} fps | triangles in the active renderers: {4} | LOD0/1/2/3 players: {5}/{6}/{7}/{8}", mode, dist, ms, 1000.0 / ms, tris, lodCount[0], lodCount[1], lodCount[2], lodCount[3]));
                    _cam.targetTexture = null; rt.Release();
                    foreach (var g in players) UnityEngine.Object.DestroyImmediate(g);
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            File.WriteAllText(OutDir + "/perf.txt", sb.ToString());
            Debug.Log("[PhotoQa] perf: " + sb);
        }

        // ------------------------------------------------------------------ LODs
        static void Lods()
        {
            Studio();
            foreach (string size in Sizes())
            {
                var cells = new List<Texture2D>();
                var a = PlayerAppearance.FromSeed(size == "Small" ? 11 : size == "Medium" ? 22 : 33, BodyOf(size));
                for (int lod = 0; lod < 4; lod++)
                {
                    var g = SpawnPhoto(size, a, Kits[2], 44, "HOLLIS");
                    g.GetComponent<PhotoPlayer>().SetLevel(lod);
                    Bake(g);
                    int tris = 0;
                    foreach (var mf in g.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null && mf.gameObject.activeInHierarchy) tris += mf.sharedMesh.triangles.Length / 3;
                    foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true)) { }
                    Debug.Log("[PhotoQa] " + size + " LOD" + lod + " drawn tris (baked, incl. active parts) = " + tris);
                    cells.Add(Shot(new Vector3(0, 1.05f, 7.5f), new Vector3(0, 0.95f, 0), 22f, 400, 700));
                    UnityEngine.Object.DestroyImmediate(g);
                }
                Save(Sheet(cells.ToArray(), 4, 400, 700), "lods_" + size.ToLowerInvariant());
            }
        }
    }
}
