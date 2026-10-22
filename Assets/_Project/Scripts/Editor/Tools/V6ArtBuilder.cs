using System;
using System.Collections.Generic;
using System.IO;
using PixFootball.Art;
using PixFootball.Core;
using TMPro;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.6 "Graphics pass 1" art generator. Everything procedural (number atlas, turf blades, weather sprites, flood cookie, helmet scuffs) and the V6 materials
    /// (PIX/Lit modes: uniform, helmet, turf, markings, weather-aware stadium clones) are produced here into Assets/_Licensed/PIX/V6 (git-ignored) so the whole
    /// look is reproducible: <c>PIX Football/V6/Build All V6 Art</c>.
    /// </summary>
    public static class V6ArtBuilder
    {
        public const string Dir = "Assets/_Licensed/PIX/V6";
        public const string TexDir = Dir + "/Textures";
        public const string MatDir = Dir + "/Materials";
        public const string LookAssetsPath = "Assets/_Licensed/Resources/PixLookAssets.asset";
        const string Src = "Assets/_Licensed/DraftFever";
        const string UniformTex = Src + "/Packages/MainPlayer/Mid_Size_Main_Player/Textures/Uniform";

        // ---------------------------------------------------------------------------------------------- texture IO
        public enum Kind { Color, Linear, Normal, Sprite }

        static string Disk(string assetPath) { return Path.GetFullPath(assetPath); }

        static void Write(string name, int w, int h, Color32[] px, Kind kind, int maxSize = 2048, bool mips = true, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            MaterialFactory.EnsureFolder(TexDir);
            string path = TexDir + "/" + name;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply(false);
            File.WriteAllBytes(Disk(path), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Configure(path, kind, maxSize, mips, wrap);
        }

        public static void Configure(string path, Kind kind, int maxSize, bool mips, TextureWrapMode wrap)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.textureType = kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = kind == Kind.Color || kind == Kind.Sprite;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = kind == Kind.Sprite;
            ti.mipmapEnabled = mips; ti.maxTextureSize = maxSize; ti.wrapMode = wrap; ti.filterMode = FilterMode.Bilinear; ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC7: atlases with thin outlines and normal maps (QA-963)
            ti.SaveAndReimport();
        }

        static void SetImport(string path, Kind kind, int maxSize, bool mips = true, TextureWrapMode wrap = TextureWrapMode.Repeat) { Configure(path, kind, maxSize, mips, wrap); }

        public static Texture2D Tex(string name) { return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + name); }

        static uint _seed = 1;
        static float Rnd() { _seed = _seed * 1664525u + 1013904223u; return ((_seed >> 8) & 0xFFFFFF) / 16777216f; }
        static void Seed(uint s) { _seed = s * 2654435761u + 12345u; }

        // ---------------------------------------------------------------------------------------------- entry points
        [MenuItem("PIX Football/V6/Build All V6 Art")]
        public static void BuildAll()
        {
            BuildNumberAtlas();
            BuildLetterAtlas();
            BuildFieldMarkings();
            BuildUniformTextures();
            BuildTurfTextures();
            BuildFxTextures();
            AssetDatabase.Refresh();
            BuildMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("[V6] art built");
        }

        // ---------------------------------------------------------------------------------------------- number atlas
        /// <summary>Collegiate block numerals 0..9 in a 5 x 2 atlas (cell 512 x 768 = NumberAtlas.Advance aspect): R = fill, G = outline (dilated fill).</summary>
        [MenuItem("PIX Football/V6/Build Number Atlas")]
        public static void BuildNumberAtlas()
        {
            const int cw = 512, ch = 768;
            var fa = RuntimeAssetsBuilder.MakeFont(Src + "/Fonts/Cosmetics/CollegeRegular.ttf", "PIX_College_Numbers SDF");
            if (fa == null) { Debug.LogWarning("[V6] number font missing"); return; }
            var sdf = new SdfText(fa, "0123456789");
            var atlas = new PixelCanvas(cw * 5, ch * 2);
            atlas.Clear(new Color32(0, 0, 0, 0));
            float targetH = ch * 0.74f, targetW = cw * 0.86f;
            for (int d = 0; d < 10; d++)
            {
                string s = d.ToString();
                // measure the glyph box at a trial size to scale it to the cell
                var probe = new PixelCanvas(1200, 1200); probe.Clear(new Color32(0, 0, 0, 0));
                const float trial = 800f;
                sdf.Draw(probe, s, 150, 1000, trial, new Color32(255, 255, 255, 255));
                int x0 = probe.W, x1 = -1, y0 = probe.H, y1 = -1;
                for (int y = 0; y < probe.H; y++) for (int x = 0; x < probe.W; x++) if (probe.Get(x, y).a > 128) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 < 0) { Debug.LogWarning("[V6] digit " + d + " not rendered"); continue; }
                float gw = x1 - x0 + 1, gh = y1 - y0 + 1;
                float k = Mathf.Min(targetH / gh, targetW / gw);
                float size = trial * k;
                // pen position so the glyph box is centred in the cell
                float penX = 0, baseY = 0;
                float bx = (150 - x0) * k, by = (1000 - y0) * k;     // glyph origin relative to its box top-left, scaled
                float cellCx = cw * 0.5f, cellCy = ch * 0.5f;
                penX = cellCx - gw * k * 0.5f + bx; baseY = cellCy - gh * k * 0.5f + by;
                var fill = new PixelCanvas(cw, ch); fill.Clear(new Color32(0, 0, 0, 0));
                var edge = new PixelCanvas(cw, ch); edge.Clear(new Color32(0, 0, 0, 0));
                sdf.Draw(fill, s, penX, baseY, size, new Color32(255, 255, 255, 255), 0f, -0.02f);
                // outline: the same glyph drawn bolder (a ring around the fill); the SDF spread limits how far this can go
                sdf.Draw(edge, s, penX, baseY, size, new Color32(255, 255, 255, 255), 0f, 0.115f);
                int col = d % 5, row = d / 5;   // row 0 = top rows of the image = v 1..0.5
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        var f = fill.Get(x, y); var e = edge.Get(x, y);
                        byte r = f.a, g = (byte)Mathf.Max(e.a, f.a);
                        atlas.Set(col * cw + x, row * ch + y, new Color32(r, g, 0, 255));
                    }
            }
            Write("PIX_NumberAtlas.png", atlas.W, atlas.H, atlas.Px, Kind.Linear, 2560, true, TextureWrapMode.Clamp);
            Debug.Log("[V6] number atlas written (" + atlas.W + "x" + atlas.H + ")");
        }

        // ---------------------------------------------------------------------------------------------- letter atlas (names, wordmark)
        /// <summary>A-Z . - ' in a 7 x 5 atlas (TextAtlas layout), cell 192 x 310 (aspect 0.62): R = fill.</summary>
        [MenuItem("PIX Football/V6/Build Letter Atlas")]
        public static void BuildLetterAtlas()
        {
            const int cw = 192, ch = 310;
            var fa = RuntimeAssetsBuilder.MakeFont(Src + "/Fonts/Cosmetics/CollegeRegular.ttf", "PIX_College_Numbers SDF");
            if (fa == null) { Debug.LogWarning("[V6] letter font missing"); return; }
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ.-'";
            var sdf = new SdfText(fa, chars + "0123456789");
            var atlas = new PixelCanvas(cw * TextAtlas.Columns, ch * TextAtlas.Rows);
            atlas.Clear(new Color32(0, 0, 0, 255));
            // cap height from "H" so every letter shares one baseline and size
            var probe = new PixelCanvas(1400, 1400); probe.Clear(new Color32(0, 0, 0, 0));
            const float trial = 900f;
            sdf.Draw(probe, "H", 200, 1100, trial, new Color32(255, 255, 255, 255));
            int y0 = probe.H, y1 = -1;
            for (int y = 0; y < probe.H; y++) for (int x = 0; x < probe.W; x++) if (probe.Get(x, y).a > 128) { if (y < y0) y0 = y; if (y > y1) y1 = y; }
            float capH = y1 - y0 + 1;
            float k = (ch * 0.78f) / capH;
            float size = trial * k;
            float baseline = ch * 0.89f;
            for (int i = 0; i < chars.Length; i++)
            {
                string s = chars[i].ToString();
                float w = sdf.Measure(s, size);
                var cell = new PixelCanvas(cw, ch); cell.Clear(new Color32(0, 0, 0, 0));
                float x0 = (cw - w) * 0.5f;
                if (chars[i] == '.' || chars[i] == '\'' || chars[i] == '-') x0 = (cw - w) * 0.5f;
                sdf.Draw(cell, s, x0, baseline, size, new Color32(255, 255, 255, 255), 0f, 0.01f);
                int col = i % TextAtlas.Columns, row = i / TextAtlas.Columns;
                for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) { var p = cell.Get(x, y); atlas.Set(col * cw + x, row * ch + y, new Color32(p.a, 0, 0, 255)); }
            }
            Write("PIX_LetterAtlas.png", atlas.W, atlas.H, atlas.Px, Kind.Linear, 2048, true, TextureWrapMode.Clamp);
            Debug.Log("[V6] letter atlas written");
        }

        // ---------------------------------------------------------------------------------------------- uniform textures
        // jersey numerals baked into the licensed grey albedo (sleeve caps and the two big torso panels), 4096 px, origin top-left:
        // painted out so the per-player numbers are the only ones shown.
        static readonly int[][] BakedNumbers =
        {
            new[] { 640, 2890, 1290, 3650 },    // back panel (large numerals)
            new[] { 2280, 2935, 2815, 3555 },   // front panel
            new[] { 345, 1925, 655, 2200 },     // left sleeve cap
            new[] { 1220, 1850, 1530, 2120 },   // right sleeve cap
        };

        [MenuItem("PIX Football/V6/Build Uniform Textures")]
        public static void BuildUniformTextures()
        {
            string dir = BrandingCleaner.TexDir;
            Clean(dir + "/PIX_Uniform_BaseColor.png", "PIX_V6_Uniform_Gray.png", Kind.Color, 4096, true);   // authored in sRGB (QA-957)
            Clean(dir + "/PIX_Uniform_Normal.png", "PIX_V6_Uniform_Normal.png", Kind.Normal, 4096, true, true);
            // mask map: R metallic 0, G AO 1, B detail mask 1, A smoothness from the roughness map
            string rough = dir + "/PIX_Uniform_Roughness.png";
            if (File.Exists(Disk(rough)))
            {
                var r = PixelCanvas.Load(Disk(rough));
                { float sc = r.W / 4096f; foreach (var nr in BakedNumbers) r.RowInterp(Mathf.RoundToInt(nr[0] * sc), Mathf.RoundToInt(nr[1] * sc), Mathf.RoundToInt(nr[2] * sc), Mathf.RoundToInt(nr[3] * sc), Mathf.RoundToInt(10 * sc)); }
                var outp = new Color32[r.Px.Length];
                for (int i = 0; i < outp.Length; i++)
                {
                    float rv = r.Px[i].r / 255f;
                    float sm = Mathf.Clamp01(1f - rv);
                    outp[i] = new Color32(0, 255, 255, (byte)Mathf.RoundToInt(sm * 255f));
                }
                Write("PIX_V6_Uniform_Mask.png", r.W, r.H, outp, Kind.Linear, 2048, true);
            }
            // colour mask + dirt noise are the licensed maps, used as is (copied next to the V6 set so the material has no dependency on package folders)
            // textures written by the Blender scripts (Tools/Blender): make sure their import types are right (QA-958: the micro normals were imported as colour)
            SetImport(TexDir + "/PIX_FabricWeave_Normal.png", Kind.Normal, 512); SetImport(TexDir + "/PIX_Knit_Normal.png", Kind.Normal, 512);
            SetImport(TexDir + "/PIX_FabricWeave_AO.png", Kind.Linear, 512); SetImport(TexDir + "/PIX_Uniform_Regions.png", Kind.Linear, 2048, true, TextureWrapMode.Clamp);
            CopyTex(UniformTex + "/Uniforma_Color_Masks_Packed.png", "PIX_V6_Uniform_ColorMask.png", Kind.Linear, 2048);
            CopyTex(UniformTex + "/Uniform_MAT_User4.png", "PIX_V6_Uniform_Dirt.png", Kind.Linear, 2048);
            string hd = "Assets/_Licensed/DraftFever/Packages/HelmetShader/_Project/3D/Character_Players/Players_Textures/New_Helmet";
            CopyTex(hd + "/helmet_baseMap.png", "PIX_V6_Helmet_Base.png", Kind.Color, 4096);
            CopyTex(hd + "/helmet_maskMap.png", "PIX_V6_Helmet_Mask.png", Kind.Linear, 4096);
            CopyTex(hd + "/helmet_normal.png", "PIX_V6_Helmet_Normal.png", Kind.Normal, 4096);
            CopyTex(hd + "/RGB_1 (3).png", "PIX_V6_Helmet_ColorMask.png", Kind.Linear, 2048);
            AssetDatabase.Refresh();
        }

        static void Clean(string srcAsset, string outName, Kind kind, int maxSize, bool paintOutNumbers, bool neutralNormal = false)
        {
            if (!File.Exists(Disk(srcAsset))) { Debug.LogWarning("[V6] missing " + srcAsset); return; }
            var c = PixelCanvas.Load(Disk(srcAsset));
            float s = c.W / 4096f;
            if (paintOutNumbers)
            {
                foreach (var r in BakedNumbers)
                {
                    int x0 = Mathf.RoundToInt(r[0] * s), y0 = Mathf.RoundToInt(r[1] * s), x1 = Mathf.RoundToInt(r[2] * s), y1 = Mathf.RoundToInt(r[3] * s);
                    if (neutralNormal) c.FillRect(x0, y0, x1, y1, new Color32(128, 128, 255, 255)); else c.RowInterp(x0, y0, x1, y1, Mathf.RoundToInt(10 * s));
                }
            }
            Write(outName, c.W, c.H, c.Px, kind, maxSize, true, TextureWrapMode.Repeat);
        }

        static void CopyTex(string srcAsset, string outName, Kind kind, int maxSize)
        {
            if (!File.Exists(Disk(srcAsset))) { Debug.LogWarning("[V6] missing " + srcAsset); return; }
            var c = PixelCanvas.Load(Disk(srcAsset));
            Write(outName, c.W, c.H, c.Px, kind, maxSize, true, TextureWrapMode.Clamp);
        }

        // ---------------------------------------------------------------------------------------------- field markings without the painted league shield
        /// <summary>The painted shield at midfield is cleared (alpha 0): the home crest is drawn there at run time (FieldLogo).</summary>
        [MenuItem("PIX Football/V6/Build Field Markings")]
        public static void BuildFieldMarkings()
        {
            string src = BrandingCleaner.TexDir + "/PIX_grassMarkings_NEW.png";
            if (!File.Exists(Disk(src))) { Debug.LogWarning("[V6] cleaned markings missing"); return; }
            var c = PixelCanvas.Load(Disk(src));
            float s = c.W / 4096f;
            int x0 = Mathf.RoundToInt(1850 * s), x1 = Mathf.RoundToInt(2250 * s), y0 = Mathf.RoundToInt(2740 * s), y1 = Mathf.RoundToInt(3180 * s);
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { var p = c.Get(x, y); p.a = 0; c.Set(x, y, p); }
            Write("PIX_V6_FieldMarkings.png", c.W, c.H, c.Px, Kind.Sprite, 4096, true, TextureWrapMode.Clamp);
        }

        // ---------------------------------------------------------------------------------------------- turf
        [MenuItem("PIX Football/V6/Build Turf Textures")]
        public static void BuildTurfTextures()
        {
            const int n = 1024;
            var h = new float[n * n];
            Seed(77);
            // thousands of leaning blades drawn into a wrapped height field
            for (int i = 0; i < 52000; i++)
            {
                float x = Rnd() * n, y = Rnd() * n;
                float ang = Rnd() * 6.2832f;   // every direction: the old code leaned all blades one way and streaked the micro normal (QA-964)
                float len = 14f + Rnd() * 34f;
                float b = 0.35f + Rnd() * 0.65f;
                float dx = Mathf.Sin(ang), dy = Mathf.Cos(ang);
                for (float t = 0f; t < len; t += 0.8f)
                {
                    float taper = 1f - t / len;
                    float v = b * (0.45f + 0.55f * (t / len)) * (0.55f + 0.45f * taper);
                    int px = (int)(x + dx * t), py = (int)(y + dy * t);
                    for (int w = 0; w < 2; w++)
                    {
                        int xx = ((px + w) % n + n) % n, yy = (py % n + n) % n;
                        int idx = yy * n + xx; if (v > h[idx]) h[idx] = v;
                    }
                }
            }
            // slight blur (wrapped) so single pixels read as blades, not noise
            var hb = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++) s += h[(((y + oy) % n + n) % n) * n + ((x + ox) % n + n) % n] * ((ox == 0 && oy == 0) ? 2f : 1f);
                    hb[y * n + x] = s / 10f;
                }
            float mean = 0f; for (int i = 0; i < hb.Length; i++) mean += hb[i]; mean /= hb.Length;
            var tex = new Color32[n * n]; var nrm = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = hb[y * n + x];
                    byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(128f + (v - mean) * 330f), 0, 255);
                    tex[y * n + x] = new Color32(g, g, g, 255);
                    float hl = hb[y * n + ((x - 1 + n) % n)], hr = hb[y * n + ((x + 1) % n)], hd = hb[((y - 1 + n) % n) * n + x], hu = hb[((y + 1) % n) * n + x];
                    var nv = new Vector3((hl - hr) * 5.5f, (hd - hu) * 5.5f, 1f).normalized;
                    nrm[y * n + x] = new Color32((byte)Mathf.RoundToInt((nv.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((nv.y * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((nv.z * 0.5f + 0.5f) * 255f), 255);
                }
            Write("PIX_TurfMicro.png", n, n, tex, Kind.Linear, 1024, true);
            Write("PIX_TurfMicroNormal.png", n, n, nrm, Kind.Normal, 1024, true);
        }

        // ---------------------------------------------------------------------------------------------- weather sprites, cookie, scuffs
        [MenuItem("PIX Football/V6/Build Fx Textures")]
        public static void BuildFxTextures()
        {
            // rain streak: thin soft vertical line, tapered at both ends
            {
                const int w = 32, h = 256; var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f - w * 0.5f) / (w * 0.5f);
                        float ty = (y + 0.5f) / h;
                        float core = Mathf.Exp(-dx * dx * 22f);
                        float taper = Mathf.Pow(Mathf.Sin(ty * Mathf.PI), 0.7f) * Mathf.Lerp(0.55f, 1f, ty);
                        byte a = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(core * taper));
                        px[y * w + x] = new Color32(255, 255, 255, a);
                    }
                Write("PIX_RainStreak.png", w, h, px, Kind.Sprite, 256, true, TextureWrapMode.Clamp);
            }
            // snowflake: soft disc with a faint cross
            {
                const int n = 64; var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f - n * 0.5f) / (n * 0.5f), dy = (y + 0.5f - n * 0.5f) / (n * 0.5f);
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float disc = Mathf.SmoothStep(1f, 0.35f, r);
                        float star = Mathf.Exp(-Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy)) * 18f) * Mathf.SmoothStep(1f, 0.2f, r) * 0.45f;
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(disc + star)));
                    }
                Write("PIX_Snowflake.png", n, n, px, Kind.Sprite, 128, true, TextureWrapMode.Clamp);
            }
            // rain splash crown: thin ring + droplets
            {
                const int n = 128; var px = new Color32[n * n];
                Seed(9);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f - n * 0.5f) / (n * 0.5f), dy = (y + 0.5f - n * 0.5f) / (n * 0.5f);
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float ring = Mathf.Exp(-(r - 0.55f) * (r - 0.55f) * 320f);
                        float ang = Mathf.Atan2(dy, dx);
                        float spikes = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 4.5f)), 14f) * Mathf.Exp(-(r - 0.38f) * (r - 0.38f) * 90f) * 0.9f;
                        float a = Mathf.Clamp01(ring * 0.8f + spikes) * Mathf.SmoothStep(1f, 0.85f, r);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                    }
                Write("PIX_Splash.png", n, n, px, Kind.Sprite, 128, true, TextureWrapMode.Clamp);
            }
            // breath vapour / puff: soft cloud
            {
                const int n = 128; var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f - n * 0.5f) / (n * 0.5f), dy = (y + 0.5f - n * 0.5f) / (n * 0.5f);
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float nz = Mathf.PerlinNoise(x * 0.07f + 3.1f, y * 0.07f + 7.7f) * 0.6f + Mathf.PerlinNoise(x * 0.17f, y * 0.17f) * 0.4f;
                        float a = Mathf.SmoothStep(1f, 0.1f, r) * Mathf.Lerp(0.5f, 1f, nz);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a)));
                    }
                Write("PIX_Puff.png", n, n, px, Kind.Sprite, 128, true, TextureWrapMode.Clamp);
            }
            // floodlight cookie: a soft-edged beam with a gentle falloff toward the rim (stand-in for an IES profile) - grey, for spot lights
            {
                const int n = 256; var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f - n * 0.5f) / (n * 0.5f), dy = (y + 0.5f - n * 0.5f) / (n * 0.5f);
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        // lamp array reflector: bright core, shoulder, hard-ish cut-off with a soft rim
                        float core = 0.75f + 0.25f * Mathf.Cos(Mathf.Min(r, 1f) * Mathf.PI * 0.5f);
                        float cut = Mathf.SmoothStep(1f, 0.82f, r);
                        float louver = 0.94f + 0.06f * Mathf.Sin(dy * 14f);
                        float v = Mathf.Clamp01(core * cut * louver);
                        byte b = (byte)Mathf.RoundToInt(v * 255f);
                        px[y * n + x] = new Color32(b, b, b, 255);
                    }
                Write("PIX_FloodCookie.png", n, n, px, Kind.Linear, 256, true, TextureWrapMode.Clamp);
            }
            // helmet scuffs and scratches: R = fine scratches, G = scraped patches
            {
                const int n = 1024; var px = new Color32[n * n];
                var scr = new float[n * n]; var patch = new float[n * n];
                Seed(21);
                for (int i = 0; i < 420; i++)
                {
                    float x = Rnd() * n, y = Rnd() * n, ang = Rnd() * 6.283f, len = 20f + Rnd() * 110f, b = 0.5f + Rnd() * 0.5f;
                    float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
                    for (float t = 0; t < len; t += 0.7f)
                    {
                        int xx = (((int)(x + dx * t)) % n + n) % n, yy = (((int)(y + dy * t)) % n + n) % n;
                        scr[yy * n + xx] = Mathf.Max(scr[yy * n + xx], b * (1f - t / len * 0.6f));
                    }
                }
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float nz = Mathf.PerlinNoise(x * 0.012f + 11f, y * 0.012f + 5f) * 0.7f + Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.3f;
                        patch[y * n + x] = Mathf.Clamp01((nz - 0.52f) * 4f);
                        px[y * n + x] = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(scr[y * n + x]) * 255f), (byte)Mathf.RoundToInt(patch[y * n + x] * 255f), 0, 255);
                    }
                Write("PIX_HelmetScuff.png", n, n, px, Kind.Linear, 1024, true);
            }
            // grass blade card (alpha clip): a clump of blades fanning out of the ground, for the near-camera blades
            {
                const int w = 128, h = 192; var px = new Color32[w * h];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(70, 120, 40, 0);
                Seed(5);
                for (int b = 0; b < 11; b++)
                {
                    float baseX = w * (0.2f + 0.6f * Rnd()), lean = (Rnd() - 0.5f) * 0.9f, height = h * (0.55f + 0.4f * Rnd()), wid = 3.2f + Rnd() * 2f;
                    float shade = 0.65f + Rnd() * 0.5f;
                    for (int y = 0; y < height; y++)
                    {
                        float t = y / height;
                        float cx = baseX + lean * height * t * t * 0.6f;
                        float ww = wid * (1f - t * 0.92f);
                        for (int x = (int)(cx - ww - 1); x <= (int)(cx + ww + 1); x++)
                            if (x >= 0 && x < w && Mathf.Abs(x + 0.5f - cx) <= ww)
                                px[y * w + x] = new Color32((byte)Mathf.Clamp(60f * shade * (0.7f + 0.5f * t), 0, 255), (byte)Mathf.Clamp(130f * shade * (0.75f + 0.45f * t), 0, 255), (byte)Mathf.Clamp(35f * shade, 0, 255), 255);
                    }
                }
                Write("PIX_GrassBlades.png", w, h, px, Kind.Sprite, 256, true, TextureWrapMode.Clamp);
            }
        }

        // ---------------------------------------------------------------------------------------------- shaders
        static Shader PixLit { get { return Shader.Find("PIX/Lit"); } }

        /// <summary>Clones a material onto the PIX/Lit shader keeping every property and keyword (so all the HDRP Lit features stay as authored).</summary>
        public static Material CloneToPix(Material src, string assetPath, int mode)
        {
            MaterialFactory.EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            var m = existing != null ? existing : new Material(src);
            if (existing != null) { m.CopyPropertiesFromMaterial(src); }
            m.shader = PixLit;
            m.name = Path.GetFileNameWithoutExtension(assetPath);
            m.SetFloat("_PixMode", mode);
            if (src.IsKeywordEnabled("_DISABLE_SSR_TRANSPARENT")) m.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
            if (mode == 0 || mode == 1) { m.SetFloat("_EnableGeometricSpecularAA", 1f); m.SetFloat("_SpecularAAScreenSpaceVariance", 0.2f); m.SetFloat("_SpecularAAThreshold", 0.25f); m.EnableKeyword("_ENABLE_GEOMETRIC_SPECULAR_AA"); }
            if (existing == null) AssetDatabase.CreateAsset(m, assetPath); else EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------------------------------------- materials
        [MenuItem("PIX Football/V6/Build Materials")]
        public static void BuildMaterials()
        {
            MaterialFactory.EnsureFolder(MatDir);
            if (PixLit == null) { Debug.LogError("[V6] PIX/Lit shader not found"); return; }
            var assets = AssetDatabase.LoadAssetAtPath<PixLookAssets>(LookAssetsPath);
            if (assets == null) { assets = ScriptableObject.CreateInstance<PixLookAssets>(); AssetDatabase.CreateAsset(assets, LookAssetsPath); }

            // ---- uniform
            var uni = NewLit(MatDir + "/PIX_Uniform_V6.mat");
            uni.SetTexture("_BaseColorMap", Tex("PIX_V6_Uniform_Gray.png"));
            uni.SetTexture("_NormalMap", Tex("PIX_V6_Uniform_Normal.png"));
            uni.SetTexture("_MaskMap", Tex("PIX_V6_Uniform_Mask.png"));
            uni.SetFloat("_NormalScale", 0.95f); uni.SetFloat("_Metallic", 0f); uni.SetFloat("_Smoothness", 0.4f);
            uni.SetFloat("_SmoothnessRemapMin", 0.12f); uni.SetFloat("_SmoothnessRemapMax", 0.55f);
            uni.SetColor("_BaseColor", Color.white);
            uni.SetFloat("_EnableGeometricSpecularAA", 1f); uni.SetFloat("_SpecularAAScreenSpaceVariance", 0.15f); uni.SetFloat("_SpecularAAThreshold", 0.2f);
            Finish(uni);   // keywords + shader switch first: the PIX properties below do not exist on HDRP/Lit
            uni.SetTexture("_PixColorMask", Tex("PIX_V6_Uniform_ColorMask.png"));
            uni.SetTexture("_PixRegions", Tex("PIX_Uniform_Regions.png"));
            uni.SetTexture("_PixNumbers", Tex("PIX_NumberAtlas.png"));
            uni.SetTexture("_PixLetters", Tex("PIX_LetterAtlas.png"));
            uni.SetTexture("_PixDirtTex", Tex("PIX_V6_Uniform_Dirt.png"));
            var weave = Tex("PIX_FabricWeave_Normal.png");
            uni.SetTexture("_PixMicroNormal", weave != null ? weave : Tex("PIX_TurfMicroNormal.png"));
            uni.SetVector("_PixFabric", new Vector4(70f, 0.55f, 0.9f, 2.6f));
            uni.SetFloat("_PixMode", 2f);
            assets.uniformMaterial = uni;

            // ---- turf
            var oldGrass = MaterialFactory.FindMaterial("PIX_FieldGrass");
            if (oldGrass != null)
            {
                var turf = CloneToPix(oldGrass, MatDir + "/PIX_FieldTurf_V6.mat", 1);
                turf.SetTexture("_PixMicroTex", Tex("PIX_TurfMicro.png"));
                turf.SetTexture("_PixMicroNormal", Tex("PIX_TurfMicroNormal.png"));
                turf.SetVector("_PixTurf", new Vector4(5f, 0.13f, 1f, 0f));
                turf.SetVector("_PixMicro", new Vector4(3.1f, 0.9f, 0.5f, 0f));
                turf.SetColor("_BaseColor", new Color(0.82f, 0.95f, 0.72f));
                turf.SetFloat("_NormalScale", 1f);
                Finish(turf);
                assets.turfMaterial = turf;
            }
            var oldMarks = MaterialFactory.FindMaterial("PIX_FieldMarkings");
            if (oldMarks != null)
            {
                var marks = CloneToPix(oldMarks, MatDir + "/PIX_FieldMarkings_V6.mat", 4);
                var vm = Tex("PIX_V6_FieldMarkings.png"); if (vm != null) { marks.SetTexture("_BaseColorMap", vm); marks.SetTexture("_MainTex", vm); }
                Finish(marks);
                assets.markingsMaterial = marks;
            }

            // ---- particles and effects
            var fx = Shader.Find("PIX/FxUnlit");
            assets.fxShader = fx;
            assets.wearCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Shaders/PixFx/PixWear.compute");
            assets.rainStreak = Tex("PIX_RainStreak.png"); assets.snowflake = Tex("PIX_Snowflake.png"); assets.splash = Tex("PIX_Splash.png"); assets.puff = Tex("PIX_Puff.png");
            assets.floodCookie = Tex("PIX_FloodCookie.png"); assets.grassBlades = Tex("PIX_GrassBlades.png"); assets.helmetScuff = Tex("PIX_HelmetScuff.png");
            assets.rainMaterial = FxMaterial("PIX_Fx_Rain", assets.rainStreak, false);
            assets.snowMaterial = FxMaterial("PIX_Fx_Snow", assets.snowflake, false);
            assets.splashMaterial = FxMaterial("PIX_Fx_Splash", assets.splash, false);
            assets.puffMaterial = FxMaterial("PIX_Fx_Puff", assets.puff, false);
            assets.wearStampMaterial = FxMaterial("PIX_Fx_WearStamp", null, true);
            assets.turfMicroNormal = Tex("PIX_TurfMicroNormal.png");

            // ---- jersey number placements (uv, from the baked numerals of the licensed texture: see Docs/ArtPipeline.md)
            assets.numberBack = new Vector4(962f / 4096f, 1f - 3268f / 4096f, 605f / 4096f, -Mathf.PI * 0.5f);
            assets.numberFront = new Vector4(2547f / 4096f, 1f - 3243f / 4096f, 482f / 4096f, Mathf.PI * 0.5f);
            assets.numberShoulderL = new Vector4(496f / 4096f, 1f - 2062f / 4096f, 215f / 4096f, Mathf.PI);
            assets.numberShoulderR = new Vector4(1373f / 4096f, 1f - 1985f / 4096f, 214f / 4096f, 0f);
            // player name above the back number (up = toward the collar, same reading direction), team wordmark above the chest number
            assets.namePlacement = new Vector4(1385f / 4096f, 1f - 3268f / 4096f, 100f / 4096f, -Mathf.PI * 0.5f);
            assets.wordPlacement = new Vector4(2262f / 4096f, 1f - 3243f / 4096f, 74f / 4096f, Mathf.PI * 0.5f);
            assets.helmetDecal = new Vector4(0.4f, 0.04f, -0.02f, 1f);
            assets.textMaxWidth = new Vector2(1050f / 4096f, 560f / 4096f);
            assets.letterAtlas = Tex("PIX_LetterAtlas.png");

            // ---- helmet shell
            BuildHelmetMaterial(assets);

            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
        }

        static Material NewLit(string path)
        {
            // start from an HDRP/Lit material so every keyword / render-state is set the way HDRP expects, then move it to PIX/Lit
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material m;
            if (existing != null) m = existing;
            else
            {
                m = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("HDRP/Lit");
            return m;
        }

        static void Finish(Material m)
        {
            // keywords are decided while the material is still an HDRP/Lit one
            var pix = PixLit;
            if (m.shader != pix)
            {
                HDShaderUtils.ResetMaterialKeywords(m);
                m.shader = pix;
            }
            else m.shader = pix;
            m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
            if (m.GetTexture("_MaskMap") != null) m.EnableKeyword("_MASKMAP");
            EditorUtility.SetDirty(m);
        }

        static Material FxMaterial(string name, Texture tex, bool additiveStamp)
        {
            string path = MatDir + "/" + name + ".mat";
            var fx = Shader.Find("PIX/FxUnlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(fx); AssetDatabase.CreateAsset(m, path); }
            m.shader = fx;
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Additive", additiveStamp ? 1f : 0f);
            m.SetFloat("_SrcBlend", additiveStamp ? 1f : 5f); m.SetFloat("_DstBlend", additiveStamp ? 1f : 10f);   // stamp: One One; particles: SrcAlpha OneMinusSrcAlpha
            m.SetFloat("_ZTest", additiveStamp ? 8f : 4f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------------------------------------- helmet
        static void BuildHelmetMaterial(PixLookAssets assets)
        {
            var src = MaterialFactory.FindMaterial("New_Helmet_MAT");
            if (src == null) return;
            // the licensed helmet graph exposes: _Base_Map, _Color_Mask (R shell, G/B stripe channels), _Mask_Map (metallic / AO / detail / smoothness), _Normal, _Opacity_Mask
            string tdir = "Assets/_Licensed/DraftFever/Packages/HelmetShader/_Project/3D/Character_Players/Players_Textures/New_Helmet";
            var baseMap = Tex("PIX_V6_Helmet_Base.png");
            var maskMap = Tex("PIX_V6_Helmet_Mask.png");
            var normal = Tex("PIX_V6_Helmet_Normal.png");
            var colorMask = Tex("PIX_V6_Helmet_ColorMask.png");
            var h = NewLit(MatDir + "/PIX_Helmet_V6.mat");
            h.SetTexture("_BaseColorMap", baseMap); h.SetTexture("_MaskMap", maskMap); h.SetTexture("_NormalMap", normal);

            h.SetFloat("_Smoothness", 0.9f); h.SetFloat("_SmoothnessRemapMin", 0f); h.SetFloat("_SmoothnessRemapMax", 0.96f);
            h.SetFloat("_Metallic", 0.2f); h.SetFloat("_NormalScale", 0.35f);
            h.SetFloat("_CoatMask", 1f);
            h.SetFloat("_MaterialID", 1f);   // standard: an earlier build left 0 = subsurface scattering on the asset, which made every tertiary colour render magenta / black (no diffusion profile)
            h.SetFloat("_PixMode", 3f);
            h.SetFloat("_EnableGeometricSpecularAA", 1f);
            Finish(h);
            h.EnableKeyword("_MATERIAL_FEATURE_CLEAR_COAT");
            h.SetTexture("_PixColorMask", colorMask); h.SetTexture("_PixScuffTex", Tex("PIX_HelmetScuff.png"));
            h.SetFloat("_PixMode", 3f);
            EditorUtility.SetDirty(h);
            assets.helmetMaterial = h;
        }
    }
}
