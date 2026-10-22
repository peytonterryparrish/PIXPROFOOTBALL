using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Photoreal roster v2 (v0.7 STAGING, nothing here touches the live PIX_PhotoPlayer_* prefabs until <see cref="Promote"/> is called by the lead):
    ///  * 15 body FBX (3 sizes x 5 bases, Blender: Tools/Blender/phr2_build.py) with the shoulder pads, imported as Humanoid
    ///  * pad textures + materials (shell = PIX/Lit uniform mode, trim, metal)
    ///  * 15 prefabs PIX_PhotoPlayer_{Size}_{Base} (same layout as PhotoPlayerBuilder: Animator + PhotoPlayer + TeamTint, plus PhotoPadTint)
    ///  * the Biped clips of "american-football-player-animated" (muscle-space clips, Humanoid import, ClipCleaner) in Staging_v07/Clips + clips.json
    /// Promote() swaps the staging content into the live photoreal folders / prefabs (same prefab assets, GUIDs kept) and registers the ArtLibrary keys.
    /// Log: Docs/QA/photoreal_v2.md.
    /// </summary>
    public static class PhotoRosterV2
    {
        public const string Staging = PhotoPlayerBuilder.Root + "/Staging_v07";
        public const string MeshDir = Staging + "/Meshes", TexDir = Staging + "/Textures", MatDir = Staging + "/Materials", PrefabDir = Staging + "/Prefabs", ClipDir = Staging + "/Clips", BipedDir = Staging + "/Biped";
        public static readonly string[] Sizes = { "Small", "Medium", "Large" };
        public static readonly string[] Bases = { "Std", "Stocky", "Athletic", "Tall", "Runner" };
        const string BipedSource = "D:/PIXFootball/Source/PPF_NEW/player_animated/anims";
        const float WatchdogSeconds = 3000f;

        static double _t0;
        static void Watchdog(string what)
        {
            if (EditorApplication.timeSinceStartup - _t0 > WatchdogSeconds) throw new TimeoutException("PhotoRosterV2 watchdog: " + what);
        }
        static void Begin() { _t0 = EditorApplication.timeSinceStartup; }

        public static string ModelPath(string size, string b) { return MeshDir + "/PHR_" + size + "_" + b + ".fbx"; }
        /// <summary>Staging prefab while it exists, the live one after Promote (the QA renders keep working).</summary>
        public static string PrefabPath(string size, string b) { string p = PrefabDir + "/PIX_PhotoPlayer_" + size + (b == "Std" ? "" : "_" + b) + ".prefab"; return File.Exists(p) || !File.Exists(LivePrefabPath(size, b)) ? p : LivePrefabPath(size, b); }
        public static string LivePrefabPath(string size, string b) { return PhotoPlayerBuilder.PrefabDir + "/PIX_PhotoPlayer_" + size + (b == "Std" ? "" : "_" + b) + ".prefab"; }
        public static string LiveModelPath(string size, string b) { return b == "Std" ? PhotoPlayerBuilder.ModelPath(size) : PhotoPlayerBuilder.MeshDir + "/PHR_" + size + "_" + b + ".fbx"; }

        // ================================================================================================ build staging
        public static void BuildStagingBatch() { Begin(); BuildStaging(); }

        /// <summary>One launch: staging prefabs + Biped clip import / clean (each step guarded, the log tells which one failed).</summary>
        public static void Stage1Batch()
        {
            Begin();
            try { BuildStaging(); } catch (Exception e) { Debug.LogError("[PhotoRoster] BuildStaging failed: " + e); }
            try { PhotoRosterV2Clips.ImportAndClean(); } catch (Exception e) { Debug.LogError("[PhotoRoster] clip import failed: " + e); }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("PIX Football/Photoreal Player/Roster v2 (staging)/Build staging")]
        public static void BuildStaging()
        {
            Begin();
            ImportPadTextures(TexDir);
            ConfigureModels(MeshDir, "_");
            var lib = AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(PhotoPlayerLibrary.EditorPath);
            if (lib == null) { Debug.LogError("[PhotoRoster] no live PhotoPlayerLibrary"); return; }
            var pads = BuildPadMaterials(TexDir, MatDir, lib);
            MaterialFactory.EnsureFolder(PrefabDir);
            foreach (string b in Bases)
                foreach (string s in Sizes)
                {
                    Watchdog(s + b);
                    BuildPrefab(ModelPath(s, b), PrefabPath(s, b), s + "_" + b, lib, pads);
                }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PhotoRoster] staging built: 15 prefabs in " + PrefabDir);
        }

        // ------------------------------------------------------------------------------------------------ pad textures / materials
        public static void ImportPadTextures(string texRoot)
        {
            string dir = texRoot + "/Pads";
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) continue;
                string n = Path.GetFileNameWithoutExtension(path);
                bool normal = n.EndsWith("_N");
                bool color = n.EndsWith("_Gray") || n.EndsWith("_Albedo");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (!normal) ti.sRGBTexture = color;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.maxTextureSize = 2048; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.isReadable = false;
                ti.SaveAndReimport();
            }
        }

        public sealed class PadMats { public Material shell, trim, metal; }

        static Texture2D Tex(string rel, string texRoot) { return AssetDatabase.LoadAssetAtPath<Texture2D>(texRoot + "/Pads/" + rel); }

        public static PadMats BuildPadMaterials(string texRoot, string matRoot, PhotoPlayerLibrary lib)
        {
            MaterialFactory.EnsureFolder(matRoot);
            var look = AssetDatabase.LoadAssetAtPath<PixLookAssets>(PixLookAssets.EditorPath);
            var res = new PadMats();
            // shell: PIX/Lit uniform mode (gray x team colour via the empty mask = primary), cloned from the V6 uniform like the jersey
            var sh = new Material(look.uniformMaterial);
            sh.SetTexture("_BaseColorMap", Tex("PadShell_Gray.png", texRoot));
            sh.SetTexture("_NormalMap", Tex("PadShell_N.png", texRoot));
            sh.SetTexture("_PixColorMask", Tex("PadShell_Team.png", texRoot));
            sh.SetTexture("_PixRegions", Tex("PadShell_Regions.png", texRoot));
            sh.SetTexture("_MaskMap", lib.jersey != null ? lib.jersey.GetTexture("_MaskMap") : null);
            sh.SetFloat("_NormalScale", 0.7f); sh.SetFloat("_Smoothness", 0.62f);
            sh.SetVector("_PixFabric", new Vector4(0f, 0f, 0f, 1f));
            sh.SetFloat("_PixMode", 2f); sh.SetColor("_BaseColor", Color.white);
            sh.EnableKeyword("_NORMALMAP"); sh.EnableKeyword("_NORMALMAP_TANGENT_SPACE"); sh.EnableKeyword("_MASKMAP");
            res.shell = SaveMat(sh, matRoot, "PHR_PadShell");
            // trim: textured neutral material (mode 0)
            var tr = new Material(Shader.Find("HDRP/Lit"));
            tr.SetTexture("_BaseColorMap", Tex("PadTrim_Albedo.png", texRoot));
            tr.SetTexture("_NormalMap", Tex("PadTrim_N.png", texRoot));
            tr.SetFloat("_NormalScale", 0.8f); tr.SetFloat("_Metallic", 0f); tr.SetFloat("_Smoothness", 0.28f);
            HDShaderUtils.ResetMaterialKeywords(tr);
            var pix = Shader.Find("PIX/Lit"); if (pix != null) tr.shader = pix;
            tr.SetFloat("_PixMode", 0f);
            res.trim = SaveMat(tr, matRoot, "PHR_PadTrim");
            var me = new Material(Shader.Find("HDRP/Lit"));
            me.SetColor("_BaseColor", new Color(0.72f, 0.74f, 0.77f)); me.SetFloat("_Metallic", 1f); me.SetFloat("_Smoothness", 0.78f);
            HDShaderUtils.ResetMaterialKeywords(me);
            if (pix != null) me.shader = pix;
            me.SetFloat("_PixMode", 0f);
            res.metal = SaveMat(me, matRoot, "PHR_PadMetal");
            return res;
        }

        static Material SaveMat(Material m, string dir, string name)
        {
            string path = dir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = m.shader; existing.CopyPropertiesFromMaterial(m); existing.shaderKeywords = m.shaderKeywords; existing.renderQueue = m.renderQueue;
                EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(m); return existing;
            }
            m.name = name; AssetDatabase.CreateAsset(m, path); return m;
        }

        public static PadMats LoadPadMats(string matRoot)
        {
            return new PadMats
            {
                shell = AssetDatabase.LoadAssetAtPath<Material>(matRoot + "/PHR_PadShell.mat"),
                trim = AssetDatabase.LoadAssetAtPath<Material>(matRoot + "/PHR_PadTrim.mat"),
                metal = AssetDatabase.LoadAssetAtPath<Material>(matRoot + "/PHR_PadMetal.mat"),
            };
        }

        // ------------------------------------------------------------------------------------------------ models
        /// <summary>Same importer setup as PhotoPlayerBuilder.ConfigureModels for every FBX in a folder whose name starts with PHR_.</summary>
        public static void ConfigureModels(string folder, string contains)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(p).StartsWith("PHR_")) continue;
                Watchdog(p);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter; if (mi == null) continue;
                bool dirty = false;
                if (mi.globalScale != 1f) { mi.globalScale = 1f; dirty = true; }
                if (!mi.importBlendShapes) { mi.importBlendShapes = true; dirty = true; }
                if (mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; dirty = true; }
                if (mi.importTangents != ModelImporterTangents.CalculateMikk) { mi.importTangents = ModelImporterTangents.CalculateMikk; dirty = true; }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (mi.isReadable) { mi.isReadable = false; dirty = true; }
                if (mi.meshCompression != ModelImporterMeshCompression.Off) { mi.meshCompression = ModelImporterMeshCompression.Off; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; dirty = true; }
                if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
                if (mi.skinWeights != ModelImporterSkinWeights.Custom) { mi.skinWeights = ModelImporterSkinWeights.Custom; mi.maxBonesPerVertex = 4; dirty = true; }
                if (dirty) mi.SaveAndReimport();
                string rep = "";
                bool ok = HumanoidMapper.IsGoodAvatar(p) || HumanoidMapper.EnsureHumanoid(p, out rep);
                var av = HumanoidMapper.GetAvatar(p);
                Debug.Log("[PhotoRoster] " + p + " humanoid=" + ok + " avatar=" + (av != null ? "valid=" + av.isValid + " human=" + av.isHuman : "none") + " " + rep);
            }
        }

        // ------------------------------------------------------------------------------------------------ prefabs
        static readonly string[] PartKeys =
        {
            "Jersey", "Pants", "Socks", "Undersock", "Arms", "Cleats", "SleeveR", "SleeveL", "Wristband", "PlayCard", "Pouch", "Towel", "Head", "Eyes",
            "Head_Brows", "Head_Beard", "Head_Scalp", "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Visor", "Pads",
        };
        static readonly HashSet<string> HideBeyond = new HashSet<string> { "Eyes", "Head_Brows", "Head_Beard", "Head_Scalp", "Visor", "Pads" };   // v0.7: the pads are an under-layer of the jersey: only LOD0 / LOD1 exist in the prefab (the collar roll is the only visible bit)

        /// <summary>One prefab from one body FBX (layout identical to PhotoPlayerBuilder.BuildPrefabs + the Pads part and PhotoPadTint). Overwrites the prefab at prefabPath in place (GUID kept).</summary>
        public static GameObject BuildPrefab(string modelPath, string prefabPath, string label, PhotoPlayerLibrary lib, PadMats pads)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) { Debug.LogWarning("[PhotoRoster] missing model " + modelPath); return null; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = Path.GetFileNameWithoutExtension(prefabPath);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
            foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lg);
            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform holder = null;
            if (headBone != null) { holder = new GameObject("Helmet").transform; holder.SetParent(headBone, false); }
            var all = inst.GetComponentsInChildren<Renderer>(true);
            var parts = new List<PhotoPlayer.Part>();
            foreach (string key in PartKeys)
            {
                var lods = new List<Renderer>();
                for (int li = 0; li < 4; li++)
                {
                    var r = all.FirstOrDefault(x => x != null && x.name == key + "_LOD" + li);
                    if (r == null) continue;
                    if (key == "Pads" && li >= 2) { UnityEngine.Object.DestroyImmediate(r.gameObject); continue; }
                    lods.Add(r);
                }
                if (lods.Count == 0) continue;
                string partKey = key == "Head_Brows" ? "HeadBrows" : key == "Head_Beard" ? "HeadBeard" : key == "Head_Scalp" ? "HeadScalp" : key;
                parts.Add(new PhotoPlayer.Part { key = partKey, lods = lods.ToArray(), hideBeyond = HideBeyond.Contains(key) });
                if (holder != null && (key == "Helmet_Shell" || key == "FaceMask" || key == "Chinstrap" || key == "HelmetPad" || key == "Visor"))
                {
                    var g = new GameObject(key == "Helmet_Shell" ? "Helmet_Shell" : key).transform;
                    g.SetParent(holder, false);
                    foreach (var r in lods) r.transform.SetParent(g, true);
                }
            }
            var pp = inst.AddComponent<PhotoPlayer>();
            pp.library = lib; pp.animator = animator; pp.headBone = headBone; pp.parts = parts.ToArray();
            AssignDefaultMaterials(pp, lib, pads);
            var tint = inst.AddComponent<TeamTint>();
            tint.primaryRenderers = pp.parts.Where(p => p.key == "Jersey" || p.key == "Pants" || p.key == "Socks").SelectMany(p => p.lods).ToArray();
            tint.secondaryRenderers = pp.parts.Where(p => p.key == "Helmet_Shell").SelectMany(p => p.lods).ToArray();
            tint.overrideMaterialColors = false;
            var padPart = pp.parts.FirstOrDefault(p => p.key == "Pads");
            if (padPart != null)
            {
                var pt = inst.AddComponent<PhotoPadTint>();
                pt.photo = pp; pt.pads = padPart.lods;
            }
            foreach (var p in pp.parts) for (int i = 0; i < p.lods.Length; i++) p.lods[i].gameObject.SetActive(i == 0);
            foreach (var k in new[] { "Visor", "HeadBeard", "HeadScalp", "SleeveL", "SleeveR", "Towel", "Pouch", "PlayCard", "Wristband" })
            { var p = pp.parts.FirstOrDefault(x => x.key == k); if (p != null) foreach (var r in p.lods) r.gameObject.SetActive(false); }
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            UnityEngine.Object.DestroyImmediate(inst);
            Debug.Log("[PhotoRoster] saved " + prefabPath + " parts=" + parts.Count);
            return saved;
        }

        static void AssignDefaultMaterials(PhotoPlayer pp, PhotoPlayerLibrary lib, PadMats pads)
        {
            Func<string, Material> mat = k =>
            {
                switch (k)
                {
                    case "Jersey": return lib.jersey; case "Pants": return lib.pants; case "Socks": case "Undersock": return lib.socks; case "Cleats": return lib.cleats;
                    case "SleeveR": case "SleeveL": return lib.sleeve; case "Wristband": case "PlayCard": return lib.band; case "Pouch": return lib.pouch; case "Towel": return lib.towel;
                    case "Helmet_Shell": return lib.helmet; case "FaceMask": return lib.faceMask; case "Chinstrap": case "HelmetPad": return lib.padding; case "Visor": return lib.visor;
                    case "HeadBrows": case "HeadBeard": case "HeadScalp": return lib.hair; case "Head": return lib.skinHead[3]; case "Eyes": return lib.eyes;
                    default: return null;
                }
            };
            foreach (var p in pp.parts)
            {
                if (p.key == "Arms") { foreach (var r in p.lods) r.sharedMaterials = new[] { lib.skinArms[3], lib.skinArms[3] }; continue; }
                if (p.key == "Pads") { if (pads != null) foreach (var r in p.lods) r.sharedMaterials = new[] { pads.shell, pads.trim, pads.metal }; continue; }
                var m = mat(p.key); if (m == null) continue;
                foreach (var r in p.lods) r.sharedMaterial = m;
            }
        }

        // ================================================================================================ Promote (lead only, after v0.6.0 shipped)
        /// <summary>
        /// One call that swaps staging into the live photoreal assets: pad textures / materials move to the live folders, the 15 body FBX replace / join PHR_{Small,Medium,Large}.fbx
        /// (Std base replaces the live file content, so the live prefab assets keep their GUIDs), the 15 prefabs are rebuilt at the live paths (PIX_PhotoPlayer_{Size} for Std,
        /// PIX_PhotoPlayer_{Size}_{Base} for the others), the new Biped clips move to PhotorealPlayer/Clips and every key is registered in the ArtLibrary (additive).
        /// DO NOT call while a release build is running. After Promote use <see cref="BuildLive"/> (not PhotoPlayerBuilder.BuildAll, which does not know the Pads part) to rebuild.
        /// </summary>
        [MenuItem("PIX Football/Photoreal Player/Roster v2 (staging)/PROMOTE staging to live")]
        public static void Promote()
        {
            Begin();
            if (!Directory.Exists(Staging)) { Debug.LogError("[PhotoRoster] no staging folder"); return; }
            var lib = AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(PhotoPlayerLibrary.EditorPath);
            // 1) pad textures + materials
            string liveTex = PhotoPlayerBuilder.TexDir + "/Pads", liveMat = PhotoPlayerBuilder.MatDir;
            MoveFolder(TexDir + "/Pads", liveTex);
            foreach (string n in new[] { "PHR_PadShell", "PHR_PadTrim", "PHR_PadMetal" })
            {
                string src = MatDir + "/" + n + ".mat", dst = liveMat + "/" + n + ".mat";
                if (File.Exists(src)) { if (File.Exists(dst)) AssetDatabase.DeleteAsset(dst); AssetDatabase.MoveAsset(src, dst); }
            }
            AssetDatabase.Refresh();
            ImportPadTextures(PhotoPlayerBuilder.TexDir);
            var pads = BuildPadMaterials(PhotoPlayerBuilder.TexDir, PhotoPlayerBuilder.MatDir, lib);      // re-point the moved materials at the live textures
            // 2) meshes
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string b in Bases)
                    foreach (string s in Sizes)
                    {
                        string src = ModelPath(s, b), dst = LiveModelPath(s, b);
                        if (!File.Exists(src)) { Debug.LogWarning("[PhotoRoster] staging mesh missing " + src); continue; }
                        File.Copy(src, dst, true);
                    }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            ConfigureModels(PhotoPlayerBuilder.MeshDir, "_");
            // 3) prefabs at the live paths
            foreach (string b in Bases)
                foreach (string s in Sizes) BuildPrefab(LiveModelPath(s, b), LivePrefabPath(s, b), s + "_" + b, lib, pads);
            // 4) clips + ArtLibrary keys
            string liveClips = PhotoPlayerBuilder.Root + "/Clips";
            if (Directory.Exists(ClipDir)) { MoveFolder(ClipDir, liveClips); }
            RegisterArtLibrary(liveClips);
            AssetDatabase.SaveAssets();
            // the staging copies (310 MB of FBX, prefabs that point at them) are no longer needed: the live prefabs were rebuilt from the live FBX
            if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
            if (AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.DeleteAsset(PrefabDir);
            AssetDatabase.Refresh();
            Debug.Log("[PhotoRoster] PROMOTED: 15 bodies + pads + Biped clips are live (staging meshes / prefabs deleted)");
        }

        /// <summary>One launch: re-import the 15 staging FBX, rebuild the staging prefabs, then promote them (batch entry for the lead).</summary>
        public static void PromoteBatch()
        {
            Begin();
            BuildStaging();
            Promote();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Rebuilds the 15 live prefabs from the live FBX (the Pads-aware replacement for PhotoPlayerBuilder.BuildPrefabs).</summary>
        public static void BuildLive()
        {
            Begin();
            var lib = AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(PhotoPlayerLibrary.EditorPath);
            var pads = LoadPadMats(PhotoPlayerBuilder.MatDir);
            foreach (string b in Bases) foreach (string s in Sizes) BuildPrefab(LiveModelPath(s, b), LivePrefabPath(s, b), s + "_" + b, lib, pads);
            AssetDatabase.SaveAssets();
        }

        static void MoveFolder(string src, string dst)
        {
            if (!AssetDatabase.IsValidFolder(src)) return;
            if (AssetDatabase.IsValidFolder(dst))
            {
                foreach (string g in AssetDatabase.FindAssets("", new[] { src }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (AssetDatabase.IsValidFolder(p)) continue;
                    string t = dst + p.Substring(src.Length);
                    if (File.Exists(t)) AssetDatabase.DeleteAsset(t);
                    AssetDatabase.MoveAsset(p, t);
                }
            }
            else
            {
                string err = AssetDatabase.MoveAsset(src, dst);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[PhotoRoster] move " + src + " -> " + dst + ": " + err);
            }
        }

        [Serializable] public class ClipRec { public string key, name, source; public float length; public bool loop; public string note; }
        [Serializable] public class ClipManifest { public List<ClipRec> clips = new List<ClipRec>(); }

        /// <summary>Adds the 15 prefab keys (photo_player_{size}[_{base}]) and the Biped clip keys to the ArtLibrary assets (additive, existing entries untouched).</summary>
        public static void RegisterArtLibrary(string clipRoot)
        {
            ClipManifest man = null;
            string json = clipRoot + "/clips.json";
            if (File.Exists(json)) man = JsonUtility.FromJson<ClipManifest>(File.ReadAllText(json));
            foreach (string path in new[] { "Assets/_Licensed/ArtLibrary.asset", "Assets/_Licensed/Resources/ArtLibrary.asset" })
            {
                var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(path);
                if (lib == null) continue;
                foreach (string b in Bases)
                    foreach (string s in Sizes)
                    {
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(LivePrefabPath(s, b));
                        if (go == null) continue;
                        string key = PlayerAppearance.PrefabKey(s == "Small" ? PhotoBody.Small : s == "Large" ? PhotoBody.Large : PhotoBody.Medium, (BodyBase)Array.IndexOf(Bases, b));
                        lib.prefabs.RemoveAll(e => e != null && e.key == key);
                        lib.prefabs.Add(new ArtLibrary.PrefabEntry { key = key, prefab = go });
                    }
                if (man != null)
                    foreach (var c in man.clips)
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipRoot + "/" + c.name + ".anim");
                        if (clip == null) continue;
                        lib.clips.RemoveAll(e => e != null && e.key == c.key && e.source == "biped_v07");
                        lib.clips.Add(new ArtLibrary.ClipEntry { key = c.key, clip = clip, source = "biped_v07", length = clip.length, loop = c.loop });
                    }
                EditorUtility.SetDirty(lib);
            }
            AssetDatabase.SaveAssets();
            ArtLibrary.ResetCache();
        }
    }
}
