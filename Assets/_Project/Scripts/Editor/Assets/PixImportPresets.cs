using System;
using System.IO;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.6 import pipeline (Docs/ArtPipeline.md section 9): <c>.preset</c> assets (normal map, HDRP mask map, base colour, fabric / tileable detail, player body model) and an
    /// <see cref="AssetPostprocessor"/> that applies them by folder and file name to NEW assets under <c>Assets/_Licensed/PIX</c> and <c>Assets/_Licensed/Art</c>
    /// (assets that already have import settings are never touched, so hand tuned textures and the DraftFever rules keep working).
    /// Naming: <c>*_N / *Normal*</c> normal map, <c>*_M / *MaskMap* / *Regions* / *_R</c> data (sRGB off), <c>*_BC / *BaseColor* / *Base_color*</c> colour (sRGB on).
    /// Size budgets: Hero 4096, Gear 2048, Props 1024, tileable fabric / turf detail 1024 (fabric 512).
    /// </summary>
    public class PixImportPresets : AssetPostprocessor
    {
        public const string PresetDir = "Assets/_Project/Settings/ImportPresets";
        public enum TexKind { Other, Normal, Mask, BaseColor }

        static bool InScope(string path)
        {
            path = path.Replace('\\', '/');
            return path.StartsWith("Assets/_Licensed/PIX/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Assets/_Licensed/Art/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Texture role from the file name (pure, tested).</summary>
        public static TexKind Classify(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (n.Contains("normal") || n.EndsWith("_n") || n.Contains("_nrm")) return TexKind.Normal;
            if (n.Contains("maskmap") || n.EndsWith("_m") || n.EndsWith("_r") || n.Contains("regions") || n.Contains("colormask") || n.Contains("_mask") || n.Contains("detailmap")) return TexKind.Mask;
            if (n.EndsWith("_bc") || n.Contains("basecolor") || n.Contains("base_color") || n.Contains("basemap") || n.EndsWith("_albedo")) return TexKind.BaseColor;
            return TexKind.Other;
        }

        /// <summary>Resolution budget for a path (pure, tested): Hero 4096, Gear / Uniform 2048, Props 1024, tileable fabric and turf detail 1024 (fabric 512).</summary>
        public static int MaxSizeFor(string path, int current = 4096)
        {
            path = path.Replace('\\', '/');
            string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            int cap;
            if (n.Contains("fabric") || n.Contains("knit")) cap = 512;
            else if (n.Contains("turfmicro") || n.Contains("tile")) cap = 1024;
            else if (path.Contains("/Props/")) cap = 1024;
            else if (path.Contains("/Gear/") || path.Contains("/Uniform/")) cap = 2048;
            else if (path.Contains("/Hero/") || path.Contains("/Helmet") || path.Contains("/Player")) cap = 4096;
            else cap = Math.Min(current, 2048);
            return Math.Min(current, cap);
        }

        static Preset Load(string name) { return AssetDatabase.LoadAssetAtPath<Preset>(PresetDir + "/" + name + ".preset"); }

        void OnPreprocessTexture()
        {
            if (!InScope(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            if (!ti.importSettingsMissing) return;
            var kind = Classify(assetPath);
            string p = assetPath.Replace('\\', '/');
            string n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            Preset preset = null;
            if (n.Contains("fabric") || n.Contains("knit") || n.Contains("turfmicro")) preset = Load("Tex_Fabric");
            else if (kind == TexKind.Normal) preset = Load("Tex_Normal");
            else if (kind == TexKind.Mask) preset = Load("Tex_Mask");
            else if (kind == TexKind.BaseColor) preset = Load("Tex_BaseColor");
            if (preset != null) preset.ApplyTo(ti);
            else
            {   // presets not created yet: apply the same rules directly
                ApplyRules(ti, kind);
            }
            ti.maxTextureSize = MaxSizeFor(p, ti.maxTextureSize);
        }

        static void ApplyRules(TextureImporter ti, TexKind kind)
        {
            ti.textureType = kind == TexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = kind == TexKind.BaseColor || kind == TexKind.Other;
            ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.alphaSource = kind == TexKind.BaseColor ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
        }

        void OnPreprocessModel()
        {
            if (!InScope(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (!mi.importSettingsMissing) return;
            string p = assetPath.Replace('\\', '/');
            bool body = p.Contains("/Characters/") || p.EndsWith("_LODs.fbx", StringComparison.OrdinalIgnoreCase) || p.Contains("/Bodies/");
            var preset = Load(body ? "Model_PlayerBody" : "Model_Prop");
            if (preset != null) preset.ApplyTo(mi);
            if (preset != null && body)
            {
                // QA-954: the preset was captured from MediumPlayer.fbx: never carry that body's skeleton / human bone map onto another file (new LOD or body FBX gets its own avatar)
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                var hd = mi.humanDescription; hd.human = new HumanBone[0]; hd.skeleton = new SkeletonBone[0]; mi.humanDescription = hd;
            }
            else
            {
                mi.isReadable = false; mi.optimizeMeshPolygons = true; mi.optimizeMeshVertices = true; mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false;
                mi.importTangents = ModelImporterTangents.CalculateMikk; mi.weldVertices = true;
                if (body) { mi.animationType = ModelImporterAnimationType.Human; mi.importAnimation = false; }
            }
        }

        // ---------------------------------------------------------------- preset creation
        static void Save(Preset p, string name)
        {
            MaterialFactory.EnsureFolder(PresetDir);
            string path = PresetDir + "/" + name + ".preset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(p, path);
        }

        /// <summary>Creates the six .preset assets (Assets/_Project/Settings/ImportPresets) from freshly configured importers of the V6 art.</summary>
        [MenuItem("PIX Football/V6/Create Import Presets")]
        public static void CreatePresets()
        {
            string td = V6ArtBuilder.TexDir;
            Make(td + "/PIX_V6_Uniform_Normal.png", "Tex_Normal", ti => ApplyRules(ti, TexKind.Normal));
            Make(td + "/PIX_V6_Uniform_Mask.png", "Tex_Mask", ti => ApplyRules(ti, TexKind.Mask));
            Make(td + "/PIX_V6_Helmet_Base.png", "Tex_BaseColor", ti => ApplyRules(ti, TexKind.BaseColor));
            Make(td + "/PIX_TurfMicro.png", "Tex_Fabric", ti => { ApplyRules(ti, TexKind.Mask); ti.wrapMode = TextureWrapMode.Repeat; ti.maxTextureSize = 1024; });
            string body = "Assets/_Licensed/DraftFever/Characters/Bodies/MediumPlayer.fbx";
            var mi = AssetImporter.GetAtPath(body) as ModelImporter;
            if (mi != null) { var pr = new Preset(mi); Save(pr, "Model_PlayerBody"); }
            string prop = "Assets/_Licensed/DraftFever/Props/Field/footballField_GEO.fbx";
            var mp = AssetImporter.GetAtPath(prop) as ModelImporter;
            if (mp != null) { var pr = new Preset(mp); Save(pr, "Model_Prop"); }
            AssetDatabase.SaveAssets();
            Debug.Log("[V6] import presets written to " + PresetDir);
        }

        static void Make(string samplePath, string presetName, Action<TextureImporter> configure)
        {
            var ti = AssetImporter.GetAtPath(samplePath) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[V6] preset sample missing " + samplePath); return; }
            configure(ti);
            ti.SaveAndReimport();
            ti = AssetImporter.GetAtPath(samplePath) as TextureImporter;
            Save(new Preset(ti), presetName);
        }
    }
}
