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
        public const string Root = "Assets/_BodiesStage", MeshDir = Root + "/Meshes", PrefabDir = Root + "/Prefabs";

        static string StageModel(string size, string b) { return MeshDir + "/PHR_" + size + "_" + b + ".fbx"; }
        static string StagePrefab(string size, string b) { return PrefabDir + "/PIX_PhotoPlayer_" + size + (b == "Std" ? "" : "_" + b) + ".prefab"; }

        public static void BuildStageBatch()
        {
            try { BuildStage(); } catch (Exception e) { Debug.LogError("[BodiesStage] " + e); }
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
            }
        }

        public const string TexDir = Root + "/Textures";

        /// <summary>Stage jersey Team mask (the sleeve-end bands are gone): same importer settings as the live map (linear, 2048, trilinear, aniso 8, compressed HQ).</summary>
        public static void ImportStageTextures()
        {
            AssetDatabase.Refresh();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                var ti = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter; if (ti == null) continue;
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.None; ti.maxTextureSize = 2048; ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.isReadable = false; ti.SaveAndReimport();
            }
        }

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
            string stageTeam = TexDir + "/jersey_Team.png", liveTeam = PhotoPlayerBuilder.TexDir + "/Uniform/jersey_Team.png";
            if (File.Exists(stageTeam)) File.Copy(stageTeam, liveTeam, true);
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
