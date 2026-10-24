using System.Collections.Generic;
using System.IO;
using PixFootball.UI;
using PixFootball.UI.Glass;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds the Tile (Direction 16) assets under Assets/_Project/UI/Glass: the Barlow Semi Condensed Bold and Medium TMP SDF fonts (the TTFs are copied from the Draft Fever font folder next to the OFL licence text, so
    /// the committed assets never reference _Licensed), the tile material and the TileTheme asset (tokens from <see cref="GlassTokens.Tile"/>). Menu: PIX Football/Glass UI/Build tile assets.
    /// Barlow is SIL Open Font License 1.1 (the licence string is inside the font files' name table; checked 2026-10-23), so it may be embedded and redistributed with the game.
    /// </summary>
    public static partial class GlassAssetBuilder
    {
        const string BarlowSource = "Assets/_Licensed/DraftFever/Fonts/Main/Barlow Semi Condensed";
        // ASCII, Latin-1 letters and punctuation, typographic quotes, bullets, dashes, degree and times signs, the diamond separator (Barlow has no arrows: DejaVu is the fallback)
        public const string BarlowSet = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~ ¡¢£¥§©«®°±·»¿" +
            "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÑÒÓÔÕÖ×ØÙÚÛÜÝß" +
            "àáâãäåæçèéêëìíîïñòóôõö÷øùúûüýÿ" +
            "–—‘’“”•…′″€™◆";

        [MenuItem("PIX Football/Glass UI/Build tile assets")]
        public static void BuildTileAssets()
        {
            BuildTileFonts();
            BuildTileTheme();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GlassAssets] tile assets done");
        }

        public static void BuildTileFonts()
        {
            MaterialFactory.EnsureFolder(FontDir);
            foreach (string f in new[] { "BarlowSemiCondensed-Bold.ttf", "BarlowSemiCondensed-Medium.ttf" })
            {
                string src = BarlowSource + "/" + f, dst = FontDir + "/" + f;
                if (!File.Exists(dst) && File.Exists(src)) File.Copy(src, dst);
            }
            string lic = FontDir + "/LICENSE-Barlow-OFL.txt";
            if (!File.Exists(lic)) File.WriteAllText(lic, BarlowLicense);
            AssetDatabase.Refresh();
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/PIX_Glass_Title SDF.asset");
            string extra; if (fallback != null) { fallback.TryAddCharacters("◆•", out extra); EditorUtility.SetDirty(fallback); foreach (var at in fallback.atlasTextures) EditorUtility.SetDirty(at); }   // the diamond separator comes from DejaVu
            MakeTileFont(FontDir + "/BarlowSemiCondensed-Bold.ttf", "PIX_Barlow_Bold SDF", fallback);
            MakeTileFont(FontDir + "/BarlowSemiCondensed-Medium.ttf", "PIX_Barlow_Medium SDF", fallback);
        }

        public static TMP_FontAsset MakeTileFont(string ttfPath, string assetName, TMP_FontAsset fallback)
        {
            string outPath = FontDir + "/" + assetName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null) { Debug.LogWarning("[GlassAssets] font missing " + ttfPath); return null; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 80, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = assetName;
            AssetDatabase.CreateAsset(fa, outPath);
            fa.atlasTextures[0].name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            string notAdded;
            fa.TryAddCharacters(BarlowSet, out notAdded);
            for (int i = 1; i < fa.atlasTextures.Length; i++) if (!AssetDatabase.Contains(fa.atlasTextures[i])) { fa.atlasTextures[i].name = assetName + " Atlas " + i; AssetDatabase.AddObjectToAsset(fa.atlasTextures[i], fa); }
            if (fallback != null) fa.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            // baked: every glyph is in the saved atlas, nothing is generated at runtime (no hitch, no allocation); glyphs Barlow lacks come from the fallback
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            foreach (var at in fa.atlasTextures) EditorUtility.SetDirty(at);
            EditorUtility.SetDirty(fa.material);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log("[GlassAssets] font asset " + outPath + " glyphs=" + fa.characterTable.Count + " atlases=" + fa.atlasTextures.Length + " atlas=" + fa.atlasWidth + "x" + fa.atlasHeight + (string.IsNullOrEmpty(notAdded) ? " missing: none" : " missing:" + notAdded));
            return fa;
        }

        public static void BuildTileTheme()
        {
            MaterialFactory.EnsureFolder(ResDir);
            var shader = Shader.Find(GlassTheme.ShaderName);
            if (shader == null) { Debug.LogError("[GlassAssets] shader " + GlassTheme.ShaderName + " not found"); return; }
            string matPath = ResDir + "/PixTileUI.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            var theme = AssetDatabase.LoadAssetAtPath<GlassTheme>(GlassTheme.TileEditorPath);
            if (theme == null) { theme = ScriptableObject.CreateInstance<GlassTheme>(); AssetDatabase.CreateAsset(theme, GlassTheme.TileEditorPath); }
            theme.tokens = GlassTokens.Tile();
            theme.material = mat;
            theme.fontBody = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/PIX_Barlow_Medium SDF.asset");
            theme.fontTitle = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/PIX_Barlow_Bold SDF.asset");
            theme.Apply(mat);
            // the Glass material and theme get the new flat properties (all zero) as well
            var gm = AssetDatabase.LoadAssetAtPath<Material>(ResDir + "/PixGlassUI.mat");
            var gt = AssetDatabase.LoadAssetAtPath<GlassTheme>(GlassTheme.EditorPath);
            if (gm != null && gt != null) { gt.Apply(gm); EditorUtility.SetDirty(gm); }
            EditorUtility.SetDirty(mat); EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            GlassTheme.ResetCache();
            Debug.Log("[GlassAssets] tile theme " + GlassTheme.TileEditorPath + " body=" + (theme.fontBody != null) + " title=" + (theme.fontTitle != null) + " material=" + (theme.material != null));
        }

        /// <summary>Counts the characters of <paramref name="text"/> that neither Barlow asset nor its fallback can draw (missing-glyph measure).</summary>
        public static int MissingGlyphs(TMP_FontAsset font, string text)
        {
            int missing = 0;
            foreach (char c in text)
            {
                if (font.HasCharacter(c, true, true)) continue;
                missing++;
            }
            return missing;
        }

        const string BarlowLicense =
@"Copyright 2017 The Barlow Project Authors (https://github.com/jpt/barlow)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://openfontlicense.org


-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
""Font Software"" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

""Reserved Font Name"" refers to any names specified as such after the
copyright statement(s).

""Original Version"" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

""Modified Version"" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

""Author"" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
";
    }
}

namespace PixFootball.EditorTools
{
    public static partial class GlassAssetBuilder
    {
        /// <summary>Fonts, theme, material and the tile prefab set in one editor session (batch: -executeMethod PixFootball.EditorTools.GlassAssetBuilder.BuildTileEverything).</summary>
        [MenuItem("PIX Football/Glass UI/Build tile assets and prefabs")]
        public static void BuildTileEverything()
        {
            BuildTileAssets();
            GlassPrefabBuilder.BuildTile();
            GlassPrefabBuilder.BuildAll();
        }
    }
}
