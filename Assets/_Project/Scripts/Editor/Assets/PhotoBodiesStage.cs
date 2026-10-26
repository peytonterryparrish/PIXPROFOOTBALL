using System;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12.0 BODIES: builds the 15 roster bodies from FBX in the PRIVATE stage folder Assets/_BodiesStage/Meshes (Blender: Tools/Blender/phr2_build.py ... --out) into stage prefabs
    /// Assets/_BodiesStage/Prefabs (nothing under Assets/_Licensed is touched, other tracks keep running on the current bodies), and, at the very end, <see cref="Promote"/> copies the stage FBX over the live
    /// PHR_* FBX (same files, so the .meta GUIDs and every prefab reference hold), rebuilds the live prefabs in place and applies the v0.12 material numbers to the shared material assets.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.PhotoBodiesStage.BuildStageBatch   |   ...PromoteBatch
    /// </summary>
    public static class PhotoBodiesStage
    {
        /// <summary>Jersey / pants normal strength with the sculpted folds (v0.12: 0.5).</summary>
        public static float ClothNormalScale = 1.0f;
        public const string Root = "Assets/_BodiesStage", MeshDir = Root + "/Meshes", PrefabDir = Root + "/Prefabs";

        static string StageModel(string size, string b) { return MeshDir + "/PHR_" + size + "_" + b + ".fbx"; }
        static string StagePrefab(string size, string b) { return PrefabDir + "/PIX_PhotoPlayer_" + size + (b == "Std" ? "" : "_" + b) + ".prefab"; }

        public static void BuildStageBatch()
        {
            try { BuildStage(); } catch (Exception e) { Debug.LogError("[BodiesStage] " + e); }
            EditorApplication.Exit(0);
        }

        /// <summary>Forces a re-import of the 15 stage FBX (after a change of the import step, e.g. PhotoUnderCloth) and rebuilds the stage prefabs.</summary>
        public static void RebuildStageBatch()
        {
            try
            {
                foreach (string f in Directory.GetFiles(MeshDir, "PHR_*.fbx")) AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                BuildStage();
            }
            catch (Exception e) { Debug.LogError("[BodiesStage] " + e); }
            EditorApplication.Exit(0);
        }

        public static void BuildStage()
        {
            ImportStageTextures();
            AssetDatabase.Refresh();
            PhotoRosterV2.ConfigureModels(MeshDir, "_");
            var lib = AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(PhotoPlayerLibrary.EditorPath);
            var pads = PhotoRosterV2.LoadPadMats(PhotoPlayerBuilder.MatDir);
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder(Root, "Prefabs");
            int n = 0;
            foreach (string b in PhotoRosterV2.Bases)
                foreach (string s in PhotoRosterV2.Sizes)
                {
                    if (!File.Exists(StageModel(s, b))) { Debug.LogWarning("[BodiesStage] no stage FBX " + StageModel(s, b)); continue; }
                    if (PhotoRosterV2.BuildPrefab(StageModel(s, b), StagePrefab(s, b), s + "_" + b, lib, pads) != null) n++;
                }
            AssetDatabase.SaveAssets();
            Debug.Log("[BodiesStage] stage prefabs built: " + n + " (heads / skin / kit come from the live PhotoPlayerLibrary)");
        }

        /// <summary>v0.12 material numbers (softer fabric micro pattern; the helmet gloss is hard-coded 0.9 in PixLitHooks.hlsl line 381, a graphics-owned file: see the log Requests). Applied to the live shared material assets at promotion only; BodyShots applies the same numbers at runtime for stage renders.</summary>
        public static void ApplyMaterialNumbers(Material helmet, Material jersey, Material pants)
        {
            if (helmet != null) { helmet.SetFloat("_Smoothness", 0.66f); helmet.SetFloat("_SmoothnessRemapMax", 0.72f); helmet.SetFloat("_Metallic", 0.1f); }   // v0.12.0 (follow-up): satin; PixLitHooks.hlsl helmet branch now also caps it
            foreach (var m in new[] { jersey, pants })
            {
                if (m == null) continue;
                var fab = m.GetVector("_PixFabric"); fab.x = 52f; fab.y = 0.36f; m.SetVector("_PixFabric", fab);   // tiling 70 -> 52, strength 0.55 -> 0.36 (the dot grid on the numbers)
                m.SetFloat("_SmoothnessRemapMax", 0.46f);
                m.SetFloat("_NormalScale", ClothNormalScale);   // v0.13.0 BODIES-2: the baked folds are physical slopes (0.5 halved them)
            }
        }

        public const string TexDir = Root + "/Textures";

        /// <summary>Stage jersey Team mask (the sleeve-end bands are gone): same importer settings as the live map (linear, 2048, trilinear, aniso 8, compressed HQ).</summary>
        public static void ImportStageTextures()
        {
            AssetDatabase.Refresh();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) continue;
                bool nrm = path.EndsWith("_N.png"), gray = path.EndsWith("_Gray.png");   // v0.13.0 BODIES-2: the cloth maps (normal, grey albedo) use the importer settings of the live ones
                ti.textureType = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = gray; ti.alphaSource = TextureImporterAlphaSource.None; ti.maxTextureSize = 2048; ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.isReadable = false; ti.SaveAndReimport();
            }
        }

        /// <summary>v0.13.0 BODIES-2: the stage cloth maps on the instance materials of one body (jersey, pants, compression sleeve), so stage renders and probes show the new uniform before the promotion.</summary>
        public static void ApplyStageTextures(PhotoPlayer pp)
        {
            Action<Material, string> put = (m, part) =>
            {
                if (m == null) return;
                var g = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + part + "_Gray.png"); var n = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + part + "_N.png");
                if (g != null) { m.SetTexture("_BaseColorMap", g); m.SetTexture("_MainTex", g); }
                if (n != null) m.SetTexture("_NormalMap", n);
            };
            put(pp.JerseyMaterial, "jersey"); put(pp.PantsMaterial, "pants");
            var sl = pp.parts.FirstOrDefault(x => x != null && x.key == "SleeveR");
            if (sl != null && sl.lods != null && sl.lods.Length > 0 && sl.lods[0] != null) put(sl.lods[0].sharedMaterial, "armsleeve");
        }

        /// <summary>Stage cloth maps that the promotion copies over the live ones (the importer settings of the live files stay).</summary>
        public static readonly string[] ClothMaps = { "jersey_Team", "jersey_Gray", "jersey_N", "pants_Gray", "pants_N", "armsleeve_Gray", "armsleeve_N" };

        public static void PromoteBatch()
        {
            try { Promote(); } catch (Exception e) { Debug.LogError("[BodiesStage] promote failed: " + e); }
            EditorApplication.Exit(0);
        }

        /// <summary>Copies the 15 stage FBX over the live ones, re-imports, rebuilds the 15 live prefabs in place (GUIDs kept), applies the material numbers and re-registers the roster keys. Not called while another track builds.</summary>
        public static void Promote()
        {
            var lib = AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(PhotoPlayerLibrary.EditorPath);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string b in PhotoRosterV2.Bases)
                    foreach (string s in PhotoRosterV2.Sizes)
                    {
                        string src = StageModel(s, b), dst = PhotoRosterV2.LiveModelPath(s, b);
                        if (!File.Exists(src)) { Debug.LogWarning("[BodiesStage] stage mesh missing " + src); continue; }
                        File.Copy(src, dst, true);
                    }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (string mname in ClothMaps)
            {
                string stageMap = TexDir + "/" + mname + ".png", liveMap = PhotoPlayerBuilder.TexDir + "/Uniform/" + mname + ".png";
                if (File.Exists(stageMap)) File.Copy(stageMap, liveMap, true);
            }
            AssetDatabase.Refresh();
            PhotoRosterV2.ConfigureModels(PhotoPlayerBuilder.MeshDir, "_");
            var pads = PhotoRosterV2.LoadPadMats(PhotoPlayerBuilder.MatDir);
            foreach (string b in PhotoRosterV2.Bases)
                foreach (string s in PhotoRosterV2.Sizes)
                    PhotoRosterV2.BuildPrefab(PhotoRosterV2.LiveModelPath(s, b), PhotoRosterV2.LivePrefabPath(s, b), s + "_" + b, lib, pads);
            if (lib != null) ApplyMaterialNumbers(lib.helmet, lib.jersey, lib.pants);
            if (lib != null) { EditorUtility.SetDirty(lib.helmet); EditorUtility.SetDirty(lib.jersey); EditorUtility.SetDirty(lib.pants); }
            AssetDatabase.SaveAssets();
            CriticalAudit.EnsureRoster();
            Debug.Log("[BodiesStage] PROMOTED: 15 bodies rebuilt from the stage FBX, material numbers applied");
        }
    }
}
