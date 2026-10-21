using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PixFootball.EditorTools
{
    /// <summary>Creates PixRuntimeAssets.asset (Resources) + TMP font assets from the Draft Fever fonts, UI sprites, audio picks, animator controller and cleaned uniform material.</summary>
    public static class RuntimeAssetsBuilder
    {
        public const string PixDir = "Assets/_Licensed/PIX";
        public const string FontDir = PixDir + "/Fonts";
        const string UiRoot = "Assets/_Licensed/UI/DFB";
        const string FontRoot = "Assets/_Licensed/DraftFever/Fonts/Main";
        public const string FontRootPath = FontRoot;
        const string AsciiPlus = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~°×•–—→←▶»«";

        public static TMP_FontAsset MakeFont(string ttfPath, string assetName)
        {
            string outPath = FontDir + "/" + assetName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
            if (existing != null) return existing;
            MaterialFactory.EnsureFolder(FontDir);
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null) { Debug.LogWarning("[PIX] font missing " + ttfPath); return null; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = assetName;
            AssetDatabase.CreateAsset(fa, outPath);
            fa.atlasTextures[0].name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            uint[] missing;
            fa.TryAddCharacters(AsciiPlus, out string notAdded);
            // multi-atlas overflow textures must be persisted too
            for (int i = 1; i < fa.atlasTextures.Length; i++) if (!AssetDatabase.Contains(fa.atlasTextures[i])) { fa.atlasTextures[i].name = assetName + " Atlas " + i; AssetDatabase.AddObjectToAsset(fa.atlasTextures[i], fa); }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log("[PIX] font asset " + outPath + " glyphs=" + fa.characterTable.Count + " atlases=" + fa.atlasTextures.Length + (string.IsNullOrEmpty(notAdded) ? "" : " missing:" + notAdded));
            return fa;
        }

        static Sprite EnsureSprite(string assetPath, bool point)
        {
            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) return null;
            bool dirty = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
            var want = point ? FilterMode.Point : FilterMode.Bilinear;
            if (ti.filterMode != want) { ti.filterMode = want; dirty = true; }
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        static void AddSprite(PixRuntimeAssets rt, string key, string rel)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(UiRoot + "/" + rel);
            if (s == null) { Debug.LogWarning("[PIX] sprite missing " + rel); return; }
            rt.sprites.Add(new PixRuntimeAssets.SpriteEntry { key = key, sprite = s });
        }

        [MenuItem("PIX Football/Build/Build Runtime Assets")]
        public static void Build()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            MaterialFactory.EnsureFolder(PixDir);
            MaterialFactory.EnsureFolder("Assets/_Licensed/Resources");
            PlayerPrefabBuilder.RegisterDiffusionProfiles();

            var rt = AssetDatabase.LoadAssetAtPath<PixRuntimeAssets>(PixRuntimeAssets.EditorPath);
            if (rt == null) { rt = ScriptableObject.CreateInstance<PixRuntimeAssets>(); AssetDatabase.CreateAsset(rt, PixRuntimeAssets.EditorPath); }
            rt.sprites.Clear(); rt.menuMusic.Clear();

            // fonts
            rt.fontTitle = MakeFont(FontRoot + "/Saira Condensed/Saira_Condensed-Bold.ttf", "PIX_Saira_Bold SDF");
            rt.fontBody = MakeFont(FontRoot + "/Barlow Semi Condensed/BarlowSemiCondensed-Medium.ttf", "PIX_Barlow_Medium SDF");

            // sprites (Xbox glyphs, backgrounds)
            foreach (var k in new[] { "a", "b", "x", "y", "lb", "rb", "lt", "rt", "analog_all", "analog_click" }) AddSprite(rt, "glyph_" + k, "Glyphs/Xbox/xbox_ui3_" + k + ".png");
            AddSprite(rt, "glyph_ls", "Glyphs/Xbox/xbox_l_all_directions_perspective_white_filled_32.png");
            AddSprite(rt, "glyph_rs", "Glyphs/Xbox/xbox_r_all_directions_perspective_white_filled_32.png");
            AddSprite(rt, "glyph_view", "Glyphs/Xbox/xbox_view_default_top_white_filled_32.png");
            AddSprite(rt, "glyph_menu", "Glyphs/Xbox/xbox_menu_default_top_white_filled_32.png");
            foreach (var d in new[] { "up", "down", "left", "right" }) AddSprite(rt, "glyph_dpad_" + d, "Glyphs/Xbox/xbox_dpad_" + d + "_top_white_outlined_32.png");
            AddSprite(rt, "bg_fog", "Backgrounds/bg_main_menu_bg_fog.png");
            AddSprite(rt, "bg_pause", "Backgrounds/bg_in_game_pause_overlay.png");
            AddSprite(rt, "bg_diamond", "Backgrounds/bg_tactics_01.png");
            var logo = EnsureSprite(BrandingCleaner.LogoPath, false);
            if (logo != null) rt.sprites.Add(new PixRuntimeAssets.SpriteEntry { key = "logo_league", sprite = logo });
            rt.teamsCsv = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/Pro/teams.csv");
            if (rt.teamsCsv != null)
            {
                foreach (string line in rt.teamsCsv.text.Split((char)10).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string id = line.Split((char)44)[0].Trim((char)34, (char)13, (char)0xFEFF);
                    var cs = EnsureSprite("Assets/_Licensed/UI/Logos/ProV6/" + id + ".png", true);
                    if (cs != null) rt.sprites.Add(new PixRuntimeAssets.SpriteEntry { key = "crest_" + id, sprite = cs });
                }
            }

            // audio
            if (lib != null)
            {
                foreach (string k in new[] { "music_pp_gates_of_glory_9_10_25", "music_pp_peytonparrishfistsintotheairmixmasterrev6", "music_pp_carry_the_cross_9_10_25", "music_pp_catch_up_to_you_8_31_25", "music_pp_peytonparrishrefuseitallmixmasterrev92final" })
                {
                    var c = lib.Audio(k); if (c != null) rt.menuMusic.Add(c); else Debug.LogWarning("[PIX] missing music key " + k);
                }
                rt.crowdAmbience = lib.Audio("crowd_ambience", 0);
                rt.crowdCheer = lib.Audio("crowd_cheer", 0);
                rt.footstep = lib.Audio("footstep_grass", 0);
                rt.whistle = lib.Audio("whistle", 0);
                rt.uiClick = lib.Audio("ui_click", 0);
                rt.uiMove = lib.Audio("ui_click", 3);
            }

            // animator
            var speeds = new float[5];
            var ctrl = AnimatorControllerBuilder.BuildAndReport(speeds);
            rt.playerController = ctrl;
            rt.clipSpeeds = AnimatorControllerBuilder.LastSpeeds;

            // cleaned uniform material (BrandingCleaner), falling back to the original
            var clean = AssetDatabase.LoadAssetAtPath<Material>(BrandingCleaner.UniformMatPath);
            rt.uniformMaterial = clean != null ? clean : MaterialFactory.FindMaterial("Mid_Size_Player_Uniform_MAT");
            var bb = AssetDatabase.LoadAssetAtPath<Material>(CrowdBuilder.BillboardMaterialPath);
            rt.crowdBillboardMaterial = bb;

            EditorUtility.SetDirty(rt);
            AssetDatabase.SaveAssets();
            PixRuntimeAssets.ResetCache();
            Debug.Log("[PIX] runtime assets built: sprites=" + rt.sprites.Count + " music=" + rt.menuMusic.Count + " uniform=" + (rt.uniformMaterial != null ? rt.uniformMaterial.name : "none") + " controller=" + (ctrl != null));
        }
    }
}
