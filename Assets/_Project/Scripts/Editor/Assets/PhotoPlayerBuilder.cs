using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Photoreal player (R1 body / rig / topology + original PIX uniform + Draft Fever heads): texture import settings, Humanoid FBX setup, materials (PIX/Lit uniform + helmet,
    /// Arm_Skin SSS skin per tone, Eyes_Shader, alpha-clip hair shells), PhotoPlayerLibrary asset, the three prefabs PIX_PhotoPlayer_{Small,Medium,Large} and the
    /// ArtLibrary keys photo_player_small / medium / large (nothing else of the game is touched).  Everything lives under Assets/_Licensed/PhotorealPlayer.
    /// Blender side: Tools/Blender/phr_*.py (see Docs/QA/photoreal.md).
    /// </summary>
    public static class PhotoPlayerBuilder
    {
        public const string Root = "Assets/_Licensed/PhotorealPlayer";
        public const string MeshDir = Root + "/Meshes", TexDir = Root + "/Textures", MatDir = Root + "/Materials", PrefabDir = Root + "/Prefabs";
        public static readonly string[] Sizes = { "Small", "Medium", "Large" };
        const string MainPlayerMats = "Assets/_Licensed/DraftFever/Packages/MainPlayer/Mid_Size_Main_Player/Materials/From_Shaders";

        [MenuItem("PIX Football/Photoreal Player/Build All")]
        public static void BuildAll()
        {
            ImportTextures();
            ConfigureModels();
            var lib = BuildLibrary();
            BuildPrefabs(lib);
            RegisterArtLibrary();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PhotoPlayer] build complete");
        }

        // ------------------------------------------------------------------------------------------------ textures
        [MenuItem("PIX Football/Photoreal Player/Import Textures")]
        public static void ImportTextures()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool dirty = false;
                Action<bool> srgb = v => { if (ti.sRGBTexture != v) { ti.sRGBTexture = v; dirty = true; } };
                int max = 2048;
                TextureImporterType type = TextureImporterType.Default;
                if (name.EndsWith("_N")) { type = TextureImporterType.NormalMap; if (name.Contains("Head_N")) max = 4096; }
                if (ti.textureType != type) { ti.textureType = type; dirty = true; }
                if (type == TextureImporterType.Default)
                {
                    bool color = name.EndsWith("_Gray") || path.Contains("/Skin/PHR_Arms_T") || path.Contains("/Skin/PHR_Head_T") || path.Contains("/Hair/");
                    srgb(color);
                    if (path.Contains("/Hair/"))
                    {
                        if (ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; dirty = true; }
                        if (ti.alphaIsTransparency) { ti.alphaIsTransparency = false; dirty = true; }
                        if (!ti.mipMapsPreserveCoverage) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f; dirty = true; }
                    }
                    else if (ti.alphaSource != TextureImporterAlphaSource.None) { ti.alphaSource = TextureImporterAlphaSource.None; dirty = true; }
                }
                if (name.EndsWith("_Gray") && !path.Contains("/Skin/")) { }
                if (ti.maxTextureSize != max) { ti.maxTextureSize = max; dirty = true; }
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
                if (ti.filterMode != FilterMode.Bilinear && ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
                if (ti.anisoLevel < 4) { ti.anisoLevel = 8; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
                if (ti.wrapMode != TextureWrapMode.Repeat && name.Contains("Hair")) { }
                if (ti.isReadable) { ti.isReadable = false; dirty = true; }
                if (dirty) { ti.SaveAndReimport(); n++; }
            }
            Debug.Log("[PhotoPlayer] texture importers updated: " + n);
        }

        // ------------------------------------------------------------------------------------------------ models
        public static string ModelPath(string size) { return MeshDir + "/PHR_" + size + ".fbx"; }

        [MenuItem("PIX Football/Photoreal Player/Configure Models")]
        public static void ConfigureModels()
        {
            foreach (string size in Sizes)
            {
                string p = ModelPath(size);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) { Debug.LogWarning("[PhotoPlayer] missing " + p); continue; }
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
                Debug.Log("[PhotoPlayer] " + p + " humanoid=" + ok + " avatar=" + (av != null ? av.name + " valid=" + av.isValid + " human=" + av.isHuman : "none") + " " + rep);
            }
        }

        // ------------------------------------------------------------------------------------------------ materials
        static Texture2D T(string rel)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + rel);
            if (t == null) Debug.LogWarning("[PhotoPlayer] missing texture " + rel);
            return t;
        }

        static Material Save(Material m, string name)
        {
            MaterialFactory.EnsureFolder(MatDir);
            string path = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                existing.shaderKeywords = m.shaderKeywords;
                existing.renderQueue = m.renderQueue;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(m);
                return existing;
            }
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D SolidTexture(string name, Color c, bool linear)
        {
            string path = TexDir + "/" + name + ".png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = c;
                t.SetPixels(px); t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                ti.sRGBTexture = !linear; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A uniform-mode PIX/Lit material (gray x team colour via the colour mask) cloned from the V6 uniform so every keyword / default stays as the graphics track authored it.</summary>
        static Material Uniform(PixLookAssets look, string part, float smooth, float normalScale, Vector4 fabric, Texture2D maskMap)
        {
            var m = new Material(look.uniformMaterial);
            m.SetTexture("_BaseColorMap", T("Uniform/" + part + "_Gray.png"));
            m.SetTexture("_NormalMap", T("Uniform/" + part + "_N.png"));
            m.SetTexture("_PixColorMask", T("Uniform/" + part + "_Team.png"));
            m.SetTexture("_PixRegions", T("Uniform/" + part + "_Regions.png"));
            m.SetTexture("_MaskMap", maskMap);
            m.SetFloat("_NormalScale", normalScale);
            m.SetFloat("_Smoothness", smooth);
            m.SetVector("_PixFabric", fabric);
            m.SetFloat("_PixMode", 2f);
            m.SetColor("_BaseColor", Color.white);
            m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_NORMALMAP_TANGENT_SPACE"); m.EnableKeyword("_MASKMAP");
            return m;
        }

        static Material SkinFrom(string templateName, string albedo, string normal, float normalIntensity)
        {
            var src = MaterialFactory.FindMaterial(templateName);
            if (src == null) { Debug.LogError("[PhotoPlayer] skin template missing: " + templateName); return null; }
            var m = new Material(src);
            m.SetTexture("_Albedo", T(albedo));
            m.SetTexture("_Normal", T(normal));
            m.SetTexture("_Roughness", T("Skin/PHR_Skin_Rough.png"));
            m.SetFloat("_Normal_Intensity", normalIntensity);
            m.SetFloat("_Hue", 0f);
            m.SetFloat("_Roughness_Value", 0.1f);      // the graph reads it as glossiness: 0 = matte skin (Docs/QA/photoreal/skin_roughness_sweep.png)
            return m;
        }

        [MenuItem("PIX Football/Photoreal Player/Build Library")]
        public static PhotoPlayerLibrary BuildLibrary()
        {
            MaterialFactory.EnsureFolder(MatDir);
            PlayerPrefabBuilder.RegisterDiffusionProfiles();
            var look = AssetDatabase.LoadAssetAtPath<PixLookAssets>(PixLookAssets.EditorPath);
            if (look == null || look.uniformMaterial == null || look.helmetMaterial == null) { Debug.LogError("[PhotoPlayer] PixLookAssets (V6 uniform / helmet materials) missing: run PIX Football/V6/Build All V6 Art first"); return null; }
            var lib = AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(PhotoPlayerLibrary.EditorPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<PhotoPlayerLibrary>(); AssetDatabase.CreateAsset(lib, PhotoPlayerLibrary.EditorPath); }

            var fabricMask = SolidTexture("Uniform/PHR_Fabric_Mask", new Color(0f, 1f, 0f, 0.1f), true);     // R metallic 0, G AO 1, B detail 0, A smoothness 0.1 (cloth is matte)
            var fabricFine = new Vector4(70f, 0.55f, 0.9f, 2.6f);
            lib.jersey = Save(Uniform(look, "jersey", 0.3f, 0.5f, fabricFine, fabricMask), "PHR_Jersey");
            lib.pants = Save(Uniform(look, "pants", 0.3f, 0.45f, fabricFine, fabricMask), "PHR_Pants");
            lib.socks = Save(Uniform(look, "socks", 0.3f, 0.5f, new Vector4(110f, 0.45f, 0.9f, 2.6f), fabricMask), "PHR_Socks");
            lib.cleats = Save(Uniform(look, "cleats", 0.5f, 0.8f, new Vector4(40f, 0.1f, 0.4f, 3f), fabricMask), "PHR_Cleats");
            lib.sleeve = Save(Uniform(look, "armsleeve", 0.3f, 0.4f, new Vector4(120f, 0.5f, 0.9f, 2.6f), fabricMask), "PHR_Sleeve");
            lib.band = Save(Uniform(look, "wristband", 0.4f, 0.6f, new Vector4(60f, 0.4f, 0.8f, 2.6f), fabricMask), "PHR_Wristband");
            lib.pouch = Save(Uniform(look, "pouch", 0.45f, 0.7f, new Vector4(60f, 0.4f, 0.8f, 2.6f), fabricMask), "PHR_Pouch");
            lib.towel = Save(Uniform(look, "towel", 0.15f, 0.9f, new Vector4(30f, 0.2f, 0.6f, 3f), fabricMask), "PHR_Towel");
            lib.gloves = Save(Uniform(look, "gloves", 0.45f, 0.8f, new Vector4(90f, 0.35f, 0.8f, 2.6f), fabricMask), "PHR_Gloves");
            lib.padding = Save(Uniform(look, "wristband", 0.3f, 0.5f, new Vector4(60f, 0.4f, 0.8f, 2.6f), fabricMask), "PHR_Padding");

            // helmet shell (mode 3: shell colour from the colour mask R channel, stripe / crest from the helmet frame the shader derives from the Helmet holder)
            var hm = new Material(look.helmetMaterial);
            hm.SetTexture("_BaseColorMap", T("Uniform/helmet_Gray.png"));
            hm.SetTexture("_PixColorMask", T("Uniform/helmet_Team.png"));
            hm.SetTexture("_NormalMap", T("Uniform/helmet_N.png"));
            var helmetMask = SolidTexture("Uniform/PHR_Helmet_Mask", new Color(0.35f, 1f, 0f, 1f), true);
            hm.SetTexture("_MaskMap", helmetMask);
            lib.helmet = Save(hm, "PHR_Helmet");

            // face mask / visor / chin strap: solid HDRP-Lit style materials on PIX/Lit
            lib.faceMask = Save(SolidPix(new Color(0.07f, 0.07f, 0.08f), 0.7f, 0.65f, false), "PHR_FaceMask");
            lib.visor = Save(SolidPix(new Color(0.82f, 0.9f, 1f, 0.16f), 0f, 0.97f, true), "PHR_Visor");
            lib.hair = Save(HairPix(), "PHR_HairShell");

            // skin per tone + eyes
            for (int i = 0; i < PlayerAppearance.SkinToneCount; i++)
            {
                lib.skinHead[i] = Save(SkinFrom("Mid_Size_Player_Head_MAT", "Skin/PHR_Head_T" + i + ".png", "Skin/PHR_Head_N.png", 0.9f), "PHR_SkinHead_T" + i);
                lib.skinArms[i] = Save(SkinFrom("Mid_Size_Player_Arms_MAT", "Skin/PHR_Arms_T" + i + ".png", "Skin/PHR_Arms_N.png", 0.9f), "PHR_SkinArms_T" + i);
            }
            var eyeSrc = MaterialFactory.FindMaterial("Mid_Size_Player_Eyes_MAT");
            if (eyeSrc != null) lib.eyes = Save(new Material(eyeSrc), "PHR_Eyes");
            // iris hue (degrees) / saturation presets from Docs/QA/photoreal/eye_hue_sweep.png: brown, dark brown, blue, green, hazel
            lib.eyeHueSaturation = new[] { new Vector2(0f, 1f), new Vector2(0f, 0.45f), new Vector2(200f, 1.3f), new Vector2(110f, 1.1f), new Vector2(40f, 1f) };

            // hair shell textures
            string[] beard = { "stubble", "goatee", "full", "moustache" }, brow = { "thin", "normal", "thick" }, scalp = { "buzz", "short" };
            for (int i = 0; i < beard.Length; i++) lib.beard[i] = T("Hair/PHR_Beard_" + beard[i] + ".png");
            for (int i = 0; i < brow.Length; i++) lib.brow[i] = T("Hair/PHR_Brow_" + brow[i] + ".png");
            for (int i = 0; i < scalp.Length; i++) lib.scalp[i] = T("Hair/PHR_Scalp_" + scalp[i] + ".png");

            // jersey layout (Tools/Blender/phr_layout.py)
            string json = Root + "/jersey_layout.json";
            if (File.Exists(json)) ApplyLayout(lib, File.ReadAllText(json));
            else Debug.LogWarning("[PhotoPlayer] jersey_layout.json missing (run Tools/Blender/phr_layout.py)");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        [Serializable] class PlaceJson { public float cx, cy, h, rot; }
        [Serializable] class LayoutJson { public PlaceJson front, back, shoulderL, shoulderR, name, word; public float nameMaxW_uv, wordMaxW_uv; }

        static Vector4 V(PlaceJson p) { return new Vector4(p.cx, p.cy, p.h, p.rot); }

        static void ApplyLayout(PhotoPlayerLibrary lib, string json)
        {
            var l = JsonUtility.FromJson<LayoutJson>(json);
            lib.numberFront = V(l.front); lib.numberBack = V(l.back); lib.numberShoulderL = V(l.shoulderL); lib.numberShoulderR = V(l.shoulderR);
            lib.namePlacement = V(l.name); lib.wordPlacement = V(l.word);
            lib.textMaxWidth = new Vector2(l.nameMaxW_uv, l.wordMaxW_uv);
        }

        /// <summary>Opaque or transparent solid colour on PIX/Lit (state decided while the material is still HDRP/Lit, like the V6 builder).</summary>
        static Material SolidPix(Color c, float metallic, float smooth, bool transparent)
        {
            var m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            if (transparent)
            {
                m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_AlphaCutoffEnable", 0f);
                m.SetFloat("_DoubleSidedEnable", 1f); m.SetFloat("_CullMode", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
            }
            HDShaderUtils.ResetMaterialKeywords(m);
            var pix = Shader.Find("PIX/Lit");
            if (pix != null) m.shader = pix;
            m.SetFloat("_PixMode", 0f);
            return m;
        }

        /// <summary>Alpha-clipped, double sided shell (beard / brow / scalp hair) on PIX/Lit.</summary>
        static Material HairPix()
        {
            var m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.35f);
            m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.5f);
            m.SetFloat("_DoubleSidedEnable", 1f); m.SetFloat("_CullMode", 0f);
            m.SetFloat("_SupportDecals", 0f);
            HDShaderUtils.ResetMaterialKeywords(m);
            var pix = Shader.Find("PIX/Lit");
            if (pix != null) m.shader = pix;
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_PixMode", 0f);
            return m;
        }

        // ------------------------------------------------------------------------------------------------ prefabs
        static readonly string[] PartKeys =
        {
            "Jersey", "Pants", "Socks", "Undersock", "Arms", "Cleats", "SleeveR", "SleeveL", "Wristband", "PlayCard", "Pouch", "Towel", "Head", "Eyes",
            "Head_Brows", "Head_Beard", "Head_Scalp", "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Visor",
        };
        static readonly HashSet<string> HideBeyond = new HashSet<string> { "Eyes", "Head_Brows", "Head_Beard", "Head_Scalp", "Visor" };

        [MenuItem("PIX Football/Photoreal Player/Build Prefabs")]
        public static void BuildPrefabsMenu() { BuildPrefabs(AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(PhotoPlayerLibrary.EditorPath)); }

        public static void BuildPrefabs(PhotoPlayerLibrary lib)
        {
            if (lib == null) { Debug.LogError("[PhotoPlayer] no library"); return; }
            MaterialFactory.EnsureFolder(PrefabDir);
            foreach (string size in Sizes)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(size));
                if (model == null) { Debug.LogWarning("[PhotoPlayer] missing model for " + size); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "PIX_PhotoPlayer_" + size;
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);   // the FBX hierarchy is re-organised (Helmet holder / gear groups)
                inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
                // the FBX importer turns every <Name>_LOD0..3 renderer group into a LODGroup: PhotoPlayer (distance based, shared with PlayerLod) owns the level selection instead
                foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lg);
                var animator = inst.GetComponent<Animator>();
                if (animator == null) animator = inst.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var headBone = animator.GetBoneTransform(HumanBodyBones.Head);

                // helmet holder under the head bone (PlayerLod expects Helmet/<gear>/<LOD renderers>)
                Transform holder = null;
                if (headBone != null)
                {
                    holder = new GameObject("Helmet").transform;
                    holder.SetParent(headBone, false);
                }
                var all = inst.GetComponentsInChildren<Renderer>(true);
                var parts = new List<PhotoPlayer.Part>();
                foreach (string key in PartKeys)
                {
                    var lods = new List<Renderer>();
                    for (int li = 0; li < 4; li++)
                    {
                        var r = all.FirstOrDefault(x => x.name == key + "_LOD" + li);
                        if (r != null) lods.Add(r);
                    }
                    if (lods.Count == 0) continue;
                    string partKey = key == "Head_Brows" ? "HeadBrows" : key == "Head_Beard" ? "HeadBeard" : key == "Head_Scalp" ? "HeadScalp" : key;
                    parts.Add(new PhotoPlayer.Part { key = partKey, lods = lods.ToArray(), hideBeyond = HideBeyond.Contains(key) });
                    // gear groups for PlayerLod
                    if (holder != null && (key == "Helmet_Shell" || key == "FaceMask" || key == "Chinstrap" || key == "HelmetPad" || key == "Visor"))
                    {
                        var g = new GameObject(key == "Helmet_Shell" ? "Helmet_Shell" : key).transform;
                        g.SetParent(holder, false);
                        foreach (var r in lods) r.transform.SetParent(g, true);
                    }
                }
                // default look in the editor: library templates + tone 3 skin
                var pp = inst.AddComponent<PhotoPlayer>();
                pp.library = lib; pp.animator = animator; pp.headBone = headBone; pp.parts = parts.ToArray();
                AssignDefaultMaterials(pp, lib);
                var tint = inst.AddComponent<TeamTint>();
                tint.primaryRenderers = pp.parts.Where(p => p.key == "Jersey" || p.key == "Pants" || p.key == "Socks").SelectMany(p => p.lods).ToArray();
                tint.secondaryRenderers = pp.parts.Where(p => p.key == "Helmet_Shell").SelectMany(p => p.lods).ToArray();
                tint.overrideMaterialColors = false;
                // LOD1..3 and optional parts start disabled (PhotoPlayer.Init also enforces this at runtime)
                foreach (var p in pp.parts) for (int i = 0; i < p.lods.Length; i++) p.lods[i].gameObject.SetActive(i == 0);
                foreach (var k in new[] { "Visor", "HeadBeard", "HeadScalp", "SleeveL", "SleeveR", "Towel", "Pouch", "PlayCard", "Wristband" })
                { var p = pp.parts.FirstOrDefault(x => x.key == k); if (p != null) foreach (var r in p.lods) r.gameObject.SetActive(false); }

                string path = PrefabDir + "/PIX_PhotoPlayer_" + size + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, path);
                UnityEngine.Object.DestroyImmediate(inst);
                Debug.Log("[PhotoPlayer] saved " + path + " parts=" + parts.Count);
            }
        }

        static void AssignDefaultMaterials(PhotoPlayer pp, PhotoPlayerLibrary lib)
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
                var m = mat(p.key); if (m == null) continue;
                foreach (var r in p.lods) r.sharedMaterial = m;
            }
        }

        // ------------------------------------------------------------------------------------------------ ArtLibrary keys
        public static void AddArtLibraryKeys(ArtLibrary lib)
        {
            if (lib == null) return;
            foreach (string size in Sizes)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/PIX_PhotoPlayer_" + size + ".prefab");
                if (go == null) continue;
                string key = "photo_player_" + size.ToLowerInvariant();
                lib.prefabs.RemoveAll(e => e != null && e.key == key);
                lib.prefabs.Add(new ArtLibrary.PrefabEntry { key = key, prefab = go });
            }
        }

        [MenuItem("PIX Football/Photoreal Player/Register ArtLibrary Keys")]
        public static void RegisterArtLibrary()
        {
            foreach (string path in new[] { "Assets/_Licensed/ArtLibrary.asset", "Assets/_Licensed/Resources/ArtLibrary.asset" })
            {
                var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(path);
                if (lib == null) continue;
                AddArtLibraryKeys(lib);
                EditorUtility.SetDirty(lib);
            }
            AssetDatabase.SaveAssets();
            ArtLibrary.ResetCache();
            Debug.Log("[PhotoPlayer] ArtLibrary keys photo_player_small / medium / large registered");
        }

        // batch entry points
        public static void BuildAllBatch() { BuildAll(); }
    }
}
