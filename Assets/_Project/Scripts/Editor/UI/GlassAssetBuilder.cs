using System.Collections.Generic;
using System.IO;
using PixFootball.UI.Glass;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds the committed glass assets (DejaVu TMP font assets from the pack's bundled fonts, the glass material and GlassTheme asset under Assets/_Project/UI/Glass/Resources/PixGlass)
    /// and the licensed icon registry (Assets/_Licensed/Resources/PixGlassIcons.asset from the rasterised icons). Menu: PIX Football/Glass UI/Build assets.
    /// The icons themselves come from Tools/Glass/rasterize_icons.ps1 (headless-Edge SVG rasterisation at an exact pixel size).
    /// </summary>
    public static class GlassAssetBuilder
    {
        public const string Root = "Assets/_Project/UI/Glass";
        public const string FontDir = Root + "/Fonts";
        public const string ResDir = Root + "/Resources/PixGlass";
        public const string IconDir = "Assets/_Licensed/UI/Glass/Icons";
        const string FontSource = @"D:\PIXFootball\Source\Glass6\source\fonts";
        const string AsciiPlus = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~\u00B0\u00D7\u2022\u2013\u2014\u2192\u2190\u25B6\u00BB\u00AB\u00B7\u2191\u2193\u2605\u2713";

        [MenuItem("PIX Football/Glass UI/Build assets")]
        public static void BuildAll()
        {
            BuildFonts();
            BuildMaterialAndTheme();
            BuildIcons();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GlassAssets] done");
        }

        // ------------------------------------------------------------------ fonts
        public static void BuildFonts()
        {
            MaterialFactory.EnsureFolder(FontDir);
            foreach (string f in new[] { "DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "LICENSE-DejaVu.txt" })
            {
                string dst = FontDir + "/" + f;
                if (!File.Exists(dst) && File.Exists(Path.Combine(FontSource, f))) File.Copy(Path.Combine(FontSource, f), dst);
            }
            AssetDatabase.Refresh();
            MakeFont(FontDir + "/DejaVuSans.ttf", "PIX_Glass_Body SDF");
            MakeFont(FontDir + "/DejaVuSans-Bold.ttf", "PIX_Glass_Title SDF");
        }

        public static TMP_FontAsset MakeFont(string ttfPath, string assetName)
        {
            string outPath = FontDir + "/" + assetName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null) { Debug.LogWarning("[GlassAssets] font missing " + ttfPath); return null; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = assetName;
            AssetDatabase.CreateAsset(fa, outPath);
            fa.atlasTextures[0].name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            string notAdded;
            fa.TryAddCharacters(AsciiPlus, out notAdded);
            for (int i = 1; i < fa.atlasTextures.Length; i++) if (!AssetDatabase.Contains(fa.atlasTextures[i])) { fa.atlasTextures[i].name = assetName + " Atlas " + i; AssetDatabase.AddObjectToAsset(fa.atlasTextures[i], fa); }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log("[GlassAssets] font asset " + outPath + " glyphs=" + fa.characterTable.Count + (string.IsNullOrEmpty(notAdded) ? "" : " missing:" + notAdded));
            return fa;
        }

        // ------------------------------------------------------------------ material + theme
        public static void BuildMaterialAndTheme()
        {
            MaterialFactory.EnsureFolder(ResDir);
            var shader = Shader.Find(GlassTheme.ShaderName);
            if (shader == null) { Debug.LogError("[GlassAssets] shader " + GlassTheme.ShaderName + " not found (is Assets/_Project/Shaders/PixGlass/PixGlassUI.shader imported and compiling?)"); return; }
            string matPath = ResDir + "/PixGlassUI.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            var theme = AssetDatabase.LoadAssetAtPath<GlassTheme>(GlassTheme.EditorPath);
            if (theme == null) { theme = ScriptableObject.CreateInstance<GlassTheme>(); AssetDatabase.CreateAsset(theme, GlassTheme.EditorPath); }
            theme.material = mat;
            theme.fontBody = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/PIX_Glass_Body SDF.asset");
            theme.fontTitle = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/PIX_Glass_Title SDF.asset");
            theme.Apply(mat);
            EditorUtility.SetDirty(mat); EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            GlassTheme.ResetCache();
            Debug.Log("[GlassAssets] theme " + GlassTheme.EditorPath + " body=" + (theme.fontBody != null) + " title=" + (theme.fontTitle != null) + " material=" + (theme.material != null));
        }

        // ------------------------------------------------------------------ icons
        public static void BuildIcons()
        {
            if (!Directory.Exists(IconDir)) { Debug.LogWarning("[GlassAssets] no icons at " + IconDir + " (run Tools/Glass/rasterize_icons.ps1)"); return; }
            MaterialFactory.EnsureFolder("Assets/_Licensed/Resources");
            AssetDatabase.Refresh();
            var reg = AssetDatabase.LoadAssetAtPath<GlassIcons>(GlassIcons.EditorPath);
            if (reg == null) { reg = ScriptableObject.CreateInstance<GlassIcons>(); AssetDatabase.CreateAsset(reg, GlassIcons.EditorPath); }
            reg.icons = new List<GlassIcons.Entry>();
            foreach (string f in Directory.GetFiles(IconDir, "*.png"))
            {
                string ap = IconDir + "/" + Path.GetFileName(f);
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(ap);
                if (s == null) { AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate); s = AssetDatabase.LoadAssetAtPath<Sprite>(ap); }
                if (s != null) reg.icons.Add(new GlassIcons.Entry { key = Path.GetFileNameWithoutExtension(f), sprite = s });
            }
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            GlassIcons.ResetCache();
            Debug.Log("[GlassAssets] icon registry: " + reg.icons.Count + " icons");
        }
    }
}
