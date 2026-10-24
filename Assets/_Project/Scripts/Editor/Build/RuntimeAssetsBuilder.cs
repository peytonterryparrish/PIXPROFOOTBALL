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
        const string AsciiPlus = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~Â°Ã—â€¢â€“â€”â†’â†â–¶Â»Â«";

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

        /// <summary>HDRP/Unlit material for the on-field overlays; a texture turns it into an alpha-tested decal (the marker ring).</summary>
        static Material MakeUnlit(string path, Color color, Texture2D map)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("HDRP/Unlit");
            m.SetColor("_UnlitColor", color);
            if (map != null)
            {
                m.SetTexture("_UnlitColorMap", map);
                m.SetColor("_UnlitColor", Color.white);
                m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.35f);
                m.SetFloat("_DoubleSidedEnable", 1f);
            }
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>v0.5: additive unlit material for the camera flashes in the stands (a soft round sprite drawn procedurally).</summary>
        static Material MakeFlashMaterial(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("HDRP/Unlit");
            // a soft radial star: white centre, falling off to transparent
            const string texPath = PixDir + "/Textures/PIX_Flash.png";
            if (!File.Exists(Path.GetFullPath(texPath)))
            {
                MaterialFactory.EnsureFolder(PixDir + "/Textures");
                var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f; float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - r * 1.15f); core *= core;
                    float star = Mathf.Clamp01(1f - Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy)) * 9f) * Mathf.Clamp01(1f - r);
                    float a = Mathf.Clamp01(core + star * 0.8f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                File.WriteAllBytes(Path.GetFullPath(texPath), t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            m.SetColor("_UnlitColor", Color.white); m.SetTexture("_UnlitColorMap", tex);
            m.SetColor("_EmissiveColor", Color.black);
            m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 1f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_AlphaCutoffEnable", 0f); m.SetFloat("_DoubleSidedEnable", 1f);
            m.renderQueue = 3000;
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
            return m;
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
                foreach (string k in new string[0])   // v0.11.0 AUDIO+CROWD: the owner's sung songs (music_pp_*) are not used any more; menu music comes from PixAudioBank (Draft Fever OST cues)
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

            // ---- v0.2 match UI sprites, SFX and field-marker materials ----
            rt.sfxTackle.Clear(); rt.sfxHit.Clear(); rt.sfxCatch.Clear(); rt.sfxKick.Clear(); rt.sfxWhistle.Clear(); rt.sfxCheers.Clear(); rt.sfxBeds.Clear();
            AddSprite(rt, "sb_local", "HUD/sb_holder_property_1_local.png");
            AddSprite(rt, "sb_visitor", "HUD/sb_holder_property_1_visitor.png");
            AddSprite(rt, "sb_timer", "HUD/sb_timerholder.png");
            AddSprite(rt, "sb_notification", "HUD/sb_notification.png");
            AddSprite(rt, "sb_flag", "HUD/sb_flag_1.png");
            AddSprite(rt, "sb_arrow", "HUD/sb_arrow_1.png");
            AddSprite(rt, "sb_to_on", "HUD/sb_timeoutactive_property_1_available.png");
            AddSprite(rt, "sb_to_off", "HUD/sb_timeoutactive_property_1_used.png");
            foreach (string k in new[] { "a", "b", "c", "d" }) AddSprite(rt, "ring_" + k, "HUD/Markers/marker_ring_" + k + ".png");
            // v0.3 play-call screen: play category tag pills from the kit (RUN / PASS / BLITZ / ZONE ... text is part of the art)
            foreach (string k in new[] { "run", "pass", "blitz", "zone", "man", "rpo", "kick", "punt" }) AddSprite(rt, "tag_" + k, "PlayCall/play_tag_category_" + k + ".png");
            AddSprite(rt, "tag_fg", "PlayCall/play_tag_category_field_goal.png");
            if (lib != null)
            {
                System.Action<System.Collections.Generic.List<AudioClip>, string, int> take = (list, key, n) => { int cnt = lib.AudioVariantCount(key); for (int i = 0; i < cnt && i < n; i++) { var c = lib.Audio(key, i); if (c != null) list.Add(c); } };
                take(rt.sfxTackle, "tackle_hit_hard", 5); take(rt.sfxHit, "tackle_hit", 2); take(rt.sfxHit, "ball_hit", 4); take(rt.sfxCatch, "ball_catch", 4);
                take(rt.sfxKick, "kick", 3); take(rt.sfxWhistle, "whistle", 2); take(rt.sfxCheers, "crowd_cheer", 4); take(rt.sfxBeds, "crowd_ambience", 2);
            }
            MaterialFactory.EnsureFolder(PixDir + "/Materials");
            rt.lineLos = MakeUnlit(PixDir + "/Materials/PIX_LineLos.mat", new Color(0.12f, 0.42f, 1f, 1f), null);
            rt.lineFirstDown = MakeUnlit(PixDir + "/Materials/PIX_LineFirstDown.mat", new Color(1f, 0.86f, 0.1f, 1f), null);
            rt.hashMark = MakeUnlit(PixDir + "/Materials/PIX_HashMark.mat", new Color(0.93f, 0.95f, 0.93f, 1f), null);
            rt.ringMarker = MakeUnlit(PixDir + "/Materials/PIX_RingMarker.mat", new Color(0.7f, 0.35f, 1f, 1f), AssetDatabase.LoadAssetAtPath<Texture2D>(UiRoot + "/HUD/Markers/marker_ring_a.png"));
            rt.flashMaterial = MakeFlashMaterial(PixDir + "/Materials/PIX_CameraFlash.mat");

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


