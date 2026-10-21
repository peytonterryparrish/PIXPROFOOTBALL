using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Produces branding-free copies of the licensed textures (jersey lettering, stadium banners / wall pads / screens, prop logos) under
    /// Assets/_Licensed/PIX/Textures and material clones that use them. Originals are never modified. Run: PIX Football/Branding/Clean All.
    /// </summary>
    public static class BrandingCleaner
    {
        const string Src = "Assets/_Licensed/DraftFever";
        public const string PixDir = "Assets/_Licensed/PIX";
        public const string TexDir = PixDir + "/Textures";
        public const string MatDir = PixDir + "/Materials";
        public const string UniformMatPath = MatDir + "/PIX_Uniform_MAT.mat";
        public const string TextureMapPath = TexDir + "/cleaned_textures.txt";
        public const string MaterialMapPath = MatDir + "/cleaned_materials.txt";

        static readonly string StadiumTex = Src + "/Packages/Stadium_Regular/_Project/Textures/Stadium";
        static readonly string Banner01 = Src + "/Packages/Stadium_Regular/_Project/3D/Stadium_Regular/Textures/Banner01";
        static readonly string Banner02 = Src + "/Packages/Stadium_Regular/_Project/3D/Stadium_Regular/Textures/Banner02";
        static readonly string RtTest = Src + "/Packages/Stadium_Regular/_Project/3D/ShellGrass/RT Test";
        static readonly string Props = Src + "/Props/StadiumProps";

        static readonly Color32 White = new Color32(246, 246, 246, 255);
        static readonly Color32 Gold = new Color32(255, 200, 55, 255);
        static readonly Color32 Navy = new Color32(18, 30, 80, 255);

        static readonly List<string> _texLines = new List<string>();
        static SdfText _italic, _bold;
        public const string LogoPath = "Assets/_Licensed/UI/Logos/League/pix_pro_football_league.png";
        static PixelCanvas _logo;
        static PixelCanvas Logo { get { if (_logo == null) _logo = File.Exists(Disk(LogoPath)) ? PixelCanvas.Load(Disk(LogoPath)) : null; return _logo; } }

        static void InitFonts()
        {
            const string chars = "PIXFOTBALLRGMENHYDWSUCVZQJK-0123456789. ";
            var fi = RuntimeAssetsBuilder.MakeFont(RuntimeAssetsBuilder.FontRootPath + "/Saira Condensed/Saira_Condensed-BoldItalic.ttf", "PIX_Saira_BoldItalic SDF");
            var fb = RuntimeAssetsBuilder.MakeFont(RuntimeAssetsBuilder.FontRootPath + "/Saira Condensed/Saira_Condensed-Bold.ttf", "PIX_Saira_Bold SDF");
            _italic = new SdfText(fi, chars + "abcdefghijklmnopqrstuvwxyz");
            _bold = new SdfText(fb, chars + "abcdefghijklmnopqrstuvwxyz");
        }

        static string Disk(string assetPath) { return Path.GetFullPath(assetPath); }

        static void SaveTexture(PixelCanvas c, string srcAssetPath, string outName, bool alpha = false)
        {
            MaterialFactory.EnsureFolder(TexDir);
            string outPath = TexDir + "/" + outName;
            c.Save(Disk(outPath));
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            var ni = AssetImporter.GetAtPath(outPath) as TextureImporter;
            var si = srcAssetPath != null ? AssetImporter.GetAtPath(srcAssetPath) as TextureImporter : null;
            if (ni != null)
            {
                if (si != null)
                {
                    ni.textureType = si.textureType; ni.sRGBTexture = si.sRGBTexture; ni.maxTextureSize = Mathf.Min(si.maxTextureSize, 4096);
                    ni.textureCompression = si.textureCompression; ni.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : si.alphaSource;
                    ni.alphaIsTransparency = alpha; ni.mipmapEnabled = true; ni.wrapMode = si.wrapMode; ni.filterMode = FilterMode.Bilinear; ni.anisoLevel = 8;
                }
                else { ni.maxTextureSize = 2048; ni.mipmapEnabled = true; ni.alphaIsTransparency = alpha; }
                ni.SaveAndReimport();
            }
            if (srcAssetPath != null) _texLines.Add(srcAssetPath + "|" + outPath);
            Debug.Log("[PIX] cleaned texture " + outPath);
        }

        // =============================================================== jersey
        static void CleanJersey()
        {
            string p = Src + "/Packages/MainPlayer/Mid_Size_Main_Player/Textures/Uniform/Mid_Size_Main_Player_Low_Texturing_Uniform_MAT_BaseColor.png";
            var c = PixelCanvas.Load(Disk(p));
            float s = c.W / 4096f;
            // "DALLAS COWBOYS" lettering next to the numbers (front)
            c.RowInterp(Sc(1316, s), Sc(2890, s), Sc(1434, s), Sc(3645, s), Sc(8, s));
            c.RowInterp(Sc(2174, s), Sc(2940, s), Sc(2276, s), Sc(3545, s), Sc(8, s));
            SaveTexture(c, p, "PIX_Uniform_BaseColor.png");
            // the lettering is also embossed in the normal map and printed in the roughness / metallic maps: clean the same two columns there
            string dir = Path.GetDirectoryName(p).Replace((char)92, '/');
            foreach (var pair in new[] { new[] { "Uniform_MAT_Normal_OpenGL.png", "PIX_Uniform_Normal.png" }, new[] { "Uniform_MAT_Roughness.png", "PIX_Uniform_Roughness.png" }, new[] { "Uniform_MAT_Metallic.png", "PIX_Uniform_Metallic.png" } })
            {
                string sp = dir + "/" + pair[0];
                if (!File.Exists(Disk(sp))) continue;
                var cc = PixelCanvas.Load(Disk(sp)); float ss = cc.W / 4096f;
                cc.RowInterp(Sc(1316, ss), Sc(2890, ss), Sc(1434, ss), Sc(3645, ss), Sc(8, ss));
                cc.RowInterp(Sc(2174, ss), Sc(2940, ss), Sc(2276, ss), Sc(3545, ss), Sc(8, ss));
                SaveTexture(cc, sp, pair[1]);
            }
            AssetDatabase.Refresh();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/PIX_Uniform_BaseColor.png");
            var orig = MaterialFactory.FindMaterial("Mid_Size_Player_Uniform_MAT");
            if (orig == null) { Debug.LogWarning("[PIX] uniform material not found"); return; }
            MaterialFactory.EnsureFolder(MatDir);
            AssetDatabase.DeleteAsset(UniformMatPath);
            var m = new Material(orig) { name = "PIX_Uniform_MAT" };
            m.SetTexture("_Albedo", tex);
            foreach (var pr in new[] { new[] { "_Normal", "PIX_Uniform_Normal.png" }, new[] { "_Roughness", "PIX_Uniform_Roughness.png" }, new[] { "_Metallic", "PIX_Uniform_Metallic.png" } })
            {
                var ct = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + pr[1]);
                if (ct != null) m.SetTexture(pr[0], ct);
            }
            AssetDatabase.CreateAsset(m, UniformMatPath);
        }

        static int Sc(float v, float s) { return Mathf.RoundToInt(v * s); }

        // =============================================================== text helpers
        static void Centered(SdfText f, PixelCanvas c, string text, float cx, float baseline, float size, Color32 col, float bias = 0.02f, float spacing = 0f)
        {
            float w = f.Measure(text, size) + spacing * (text.Length - 1);
            f.Draw(c, text, cx - w * 0.5f, baseline, size, col, 0f, bias, spacing);
        }

        // =============================================================== stadium
        static void CleanPerimeterBoard()
        {
            string p = StadiumTex + "/bannerTest_BaseMap.png";
            var c = PixelCanvas.Load(Disk(p)); float s = c.W / 2048f;
            foreach (int oy in new[] { 0, 1527 })
            {
                c.RowInterp(Sc(630, s), Sc(86 + oy, s), Sc(1416, s), Sc(424 + oy, s), Sc(10, s));
                if (Logo != null)
                {
                    c.DrawScaled(Logo, 775 * s, (254 + oy) * s, 310 * s, 319 * s);
                    Centered(_italic, c, "PIX PRO", 1175 * s, (252 + oy) * s, 150 * s, White, 0.02f, 2f * s);
                    Centered(_italic, c, "FOOTBALL", 1175 * s, (356 + oy) * s, 112 * s, White, 0.02f, 3f * s);
                    c.FillRectBlend(Sc(930, s), Sc(374 + oy, s), Sc(1400, s), Sc(380 + oy, s), Gold);
                }
                else
                {
                    Centered(_italic, c, "PIX", 1023 * s, (262 + oy) * s, 235 * s, White);
                    Centered(_italic, c, "FOOTBALL", 1023 * s, (384 + oy) * s, 150 * s, White, 0.02f, 3f * s);
                }
            }
            SaveTexture(c, p, "PIX_bannerTest_BaseMap.png");
        }

        static void CleanBanner01Text()
        {
            string p = Banner01 + "/Banner01_Text.png";
            var c = PixelCanvas.Load(Disk(p)); float s = c.W / 2048f;
            c.FillRect(Sc(300, s), Sc(1250, s), Sc(1545, s), Sc(1790, s), new Color32(0, 0, 0, 255));
            var g = new Color32(228, 228, 228, 255);
            if (Logo != null) c.DrawScaled(Logo, 922 * s, 1522 * s, 480 * s, 494 * s, 0.95f, true, 1.25f);
            else
            {
                Centered(_italic, c, "PIX", 922 * s, 1480 * s, 340 * s, g);
                Centered(_italic, c, "FOOTBALL", 922 * s, 1700 * s, 245 * s, g, 0.02f, 4f * s);
            }
            SaveTexture(c, p, "PIX_Banner01_Text.png");
        }

        static void DrawAdSheet(PixelCanvas c)
        {
            int S = c.W; int h = S / 4;
            c.Clear(new Color32(255, 255, 255, 255));
            var black = new Color32(0, 0, 0, 255); var orange = new Color32(255, 95, 5, 255);
            var red = new Color32(229, 0, 30, 255); var blue = new Color32(5, 12, 110, 255);
            // band 0: black with orange bars
            c.FillRect(0, 0, S, h, black);
            c.FillRect(0, (int)(0.06f * h), S, (int)(0.22f * h), orange);
            c.FillRect(0, (int)(0.78f * h), S, (int)(0.94f * h), orange);
            Centered(_italic, c, "PIX FOOTBALL", S * 0.5f, h * 0.70f, h * 0.40f, White, 0.02f, 2f);
            // band 1: black, brand name + tagline
            c.FillRect(0, h, S, 2 * h, black);
            Centered(_italic, c, "PIX FOOTBALL", S * 0.5f, h + h * 0.60f, h * 0.52f, White);
            Centered(_bold, c, "GAME NIGHT. EVERY NIGHT.", S * 0.5f, h + h * 0.84f, h * 0.12f, White, 0.02f, 3f);
            // band 2: red with white rule
            c.FillRect(0, 2 * h, S, 3 * h, red);
            c.FillRect(0, 2 * h + (int)(0.90f * h), S, 2 * h + (int)(0.94f * h), White);
            Centered(_italic, c, "PRO LEAGUE", S * 0.5f, 2 * h + h * 0.62f, h * 0.50f, White);
            // band 3: navy
            c.FillRect(0, 3 * h, S, 4 * h, blue);
            Centered(_italic, c, "GAME DAY", S * 0.5f, 3 * h + h * 0.62f, h * 0.52f, White);
            Centered(_bold, c, "PIX SPORTS", S * 0.5f, 3 * h + h * 0.86f, h * 0.13f, White, 0.02f, 4f);
        }

        static void CleanAds()
        {
            foreach (string n in new[] { "adBanner01_Color", "adBanner01_Emissive", "adBanner02_Color", "adBanner03_Emissive", "adBanner04_Emissive", "adBanner_02_Emissive" })
            {
                string p = Banner02 + "/" + n + ".png";
                if (!File.Exists(Disk(p))) continue;
                var tmp = PixelCanvas.Load(Disk(p));
                var c = new PixelCanvas(tmp.W, tmp.H);
                DrawAdSheet(c);
                SaveTexture(c, p, "PIX_" + n + ".png");
            }
        }

        static void CleanScreenTest()
        {
            foreach (string p in new[] { StadiumTex + "/screenTest.png", Src + "/Packages/Stadium_Regular/_Project/3D/Stadium [to be reviewed]/screenTest.png" })
            {
                if (!File.Exists(Disk(p))) continue;
                var c = PixelCanvas.Load(Disk(p)); float s = c.W / 1024f;
                c.RowInterp(Sc(60, s), Sc(60, s), Sc(965, s), Sc(200, s), Sc(10, s));
                c.RowInterp(Sc(360, s), Sc(300, s), Sc(665, s), Sc(450, s), Sc(10, s));
                c.RowInterp(Sc(220, s), Sc(598, s), Sc(690, s), Sc(690, s), Sc(10, s));
                c.RowInterp(Sc(220, s), Sc(850, s), Sc(690, s), Sc(945, s), Sc(10, s));
                Centered(_italic, c, "PIX FOOTBALL", 512 * s, 170 * s, 118 * s, White);
                if (Logo != null) c.DrawScaled(Logo, 512 * s, 380 * s, 140 * s, 144 * s); else Centered(_italic, c, "PIX", 512 * s, 415 * s, 130 * s, White);
                Centered(_italic, c, "PIX FOOTBALL", 450 * s, 665 * s, 66 * s, White);
                Centered(_italic, c, "PIX FOOTBALL", 450 * s, 920 * s, 66 * s, White);
                string name = p.Contains("to be reviewed") ? "PIX_screenTest_b.png" : "PIX_screenTest.png";
                SaveTexture(c, p, name);
            }
        }

        static void CleanEndzoneNames()
        {
            foreach (string n in new[] { "Endzone_Tigers_02", "Endzone_Tigers" })
            {
                string p = RtTest + "/" + n + ".png";
                if (!File.Exists(Disk(p))) continue;
                var tmp = PixelCanvas.Load(Disk(p));
                var c = new PixelCanvas(tmp.W, tmp.H); float s = c.W / 1024f;
                c.Clear(new Color32(255, 255, 255, 0));
                if (Logo != null) c.DrawScaled(Logo, 268 * s, 508 * s, 132 * s, 136 * s);
                else { c.FillCircle(268 * s, 508 * s, 68 * s, Gold); c.FillCircle(268 * s, 508 * s, 56 * s, Navy); Centered(_italic, c, "P", 268 * s, 540 * s, 108 * s, White, 0.02f); }
                _italic.Draw(c, "PIX FOOTBALL", 360 * s, 545 * s, 98 * s, new Color32(255, 255, 255, 255), 0f, 0.03f, 1f * s);
                SaveTexture(c, p, "PIX_" + n + ".png", true);
            }
        }

        static void CleanStadiumBase()
        {
            string p = StadiumTex + "/stadium_grey_baseMap_2.png";
            if (!File.Exists(Disk(p))) return;
            var c = PixelCanvas.Load(Disk(p)); float s = c.W / 4096f;
            int[] ys = { 3072, 3330, 3585, 3840, 4096 };
            for (int b = 0; b < 4; b++)
            {
                int y0 = ys[b], y1 = ys[b + 1];
                var bg = c.Average(Sc(8, s), Sc(y0 + 6, s), Sc(60, s), Sc(y0 + 40, s));
                c.FillRect(0, Sc(y0, s), Sc(1024, s), Sc(y1, s), bg);
                if (Logo != null) c.DrawScaled(Logo, 150 * s, (y0 + 128) * s, 200 * s, 206 * s);
                Centered(_italic, c, "PIX", 610 * s, (y0 + 125) * s, 120 * s, White, 0.02f, 2f * s);
                Centered(_italic, c, "FOOTBALL", 610 * s, (y0 + 205) * s, 78 * s, White, 0.02f, 2f * s);
            }
            SaveTexture(c, p, "PIX_stadium_grey_baseMap_2.png");
        }

        public static void CleanJerseyOnly() { CleanJersey(); AssetDatabase.SaveAssets(); }

        public static void CleanFieldMarkingsOnly() { CleanFieldMarkings(); AssetDatabase.SaveAssets(); }

        static void CleanFieldMarkings()
        {
            string p = Src + "/Props/Field/grassMarkings_NEW.png";
            if (!File.Exists(Disk(p)) || Logo == null) return;
            var c = PixelCanvas.Load(Disk(p)); float s = c.W / 4096f;
            // the marking plane is 137.75 x 76.75 m stretched over a square texture: 1 m = 29.7 px along u, 53.4 px along v
            float pxPerMU = c.W / 137.75f, pxPerMV = c.H / 76.75f;
            float wM = 10.2f, hM = wM * Logo.H / Logo.W;   // regulation size: about 11 yards across (v0.2 fix: the texture maps uniformly, so use one px-per-metre for both axes)
            // the field plane is mapped rotated 180 degrees (near-side numbers come from the top half of the texture), so draw the logo rotated too
            c.DrawScaled(Logo.Rotate90(2), 2048 * s, 2955 * s, wM * pxPerMU, hM * pxPerMU, 0.92f);
            SaveTexture(c, p, "PIX_grassMarkings_NEW.png", true);
        }

        // =============================================================== props
        static void CleanProps()
        {
            // bench
            {
                string p = Props + "/bench/bench_baseMap.png";
                var c = PixelCanvas.Load(Disk(p));
                c.FillSample(420, 250, 925, 670, 120, 300, 330, 600, 2f);
                c.FillSample(1510, 350, 2020, 715, 1250, 380, 1450, 700, 2f);
                c.FillSample(10, 1240, 385, 1385, 10, 1410, 385, 1470, 1.5f);
                c.FillSample(10, 3900, 385, 4045, 10, 3790, 385, 3860, 1.5f);
                c.ColInterp(1140, 2370, 1600, 2880, 6);
                SaveTexture(c, p, "PIX_bench_baseMap.png");
            }
            // hydration station
            {
                string p = Props + "/hydratationStands/delivery/hydrationStation/hydrationStation_baseMap.png";
                var c = PixelCanvas.Load(Disk(p));
                c.Patch(215, 1320, 790, 1915, 0, -620);       // left logo
                c.Patch(215, 2690, 710, 3490, 0, -800);       // left text
                c.Patch(1235, 1320, 1790, 1915, 0, -620);     // right logo
                c.Patch(1260, 2690, 1790, 3490, 0, -800);     // right text
                SaveTexture(c, p, "PIX_hydrationStation_baseMap.png");
            }
            // down marker
            {
                string p = Props + "/downMarker/delivery/downMarker_baseMap.png";
                var c = PixelCanvas.Load(Disk(p));
                c.FillSample(3180, 3330, 3590, 3990, 3040, 3400, 3150, 3950, 1.5f);
                SaveTexture(c, p, "PIX_downMarker_baseMap.png");
            }
            // goal post
            {
                string p = Props + "/goalPost/delivery/goalPost_baseColor.png";
                var c = PixelCanvas.Load(Disk(p));
                c.FillSample(340, 1610, 1740, 1980, 40, 1640, 330, 1960, 1.5f);
                c.Patch(730, 2290, 1240, 3690, 610, 0);
                SaveTexture(c, p, "PIX_goalPost_baseColor.png");
            }
            // football
            {
                string p = Props + "/footballBall/delivery/footballBall_baseMap.png";
                var c = PixelCanvas.Load(Disk(p));
                c.Patch(1580, 2290, 2150, 2820, -1380, 0);
                SaveTexture(c, p, "PIX_footballBall_baseMap.png");
            }
        }

        // =============================================================== materials
        static Dictionary<string, string> LoadMap(string path)
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(Disk(path))) return d;
            foreach (string l in File.ReadAllLines(Disk(path))) { var p = l.Split('|'); if (p.Length == 2) d[p[0]] = p[1]; }
            return d;
        }

        static void CloneMaterials()
        {
            var texMap = LoadMap(TextureMapPath);
            var folders = new[] { Src + "/Materials", Src + "/Packages/Stadium_Regular/_Project", Src + "/Packages/Stadium_College/_Project", Src + "/Packages/StadiumKit" };
            folders = folders.Where(AssetDatabase.IsValidFolder).ToArray();
            var lines = new List<string>();
            MaterialFactory.EnsureFolder(MatDir);
            foreach (string g in AssetDatabase.FindAssets("t:Material", folders))
            {
                string mp = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null || m.shader == null || mp.EndsWith(".shadergraph")) continue;
                var sh = m.shader; int pc = sh.GetPropertyCount();
                var swaps = new List<KeyValuePair<string, Texture2D>>();
                for (int i = 0; i < pc; i++)
                {
                    if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                    string pn = sh.GetPropertyName(i);
                    var t = m.GetTexture(pn); if (t == null) continue;
                    string tp = AssetDatabase.GetAssetPath(t);
                    string clean;
                    if (texMap.TryGetValue(tp, out clean)) swaps.Add(new KeyValuePair<string, Texture2D>(pn, AssetDatabase.LoadAssetAtPath<Texture2D>(clean)));
                }
                if (swaps.Count == 0) continue;
                string outPath = MatDir + "/PIX_" + Path.GetFileNameWithoutExtension(mp) + "_" + g.Substring(0, 6) + ".mat";
                AssetDatabase.DeleteAsset(outPath);
                var clone = new Material(m) { name = Path.GetFileNameWithoutExtension(outPath) };
                foreach (var sw in swaps) clone.SetTexture(sw.Key, sw.Value);
                AssetDatabase.CreateAsset(clone, outPath);
                lines.Add(mp + "|" + outPath);
                Debug.Log("[PIX] cleaned material " + outPath + " (from " + mp + ")");
            }
            File.WriteAllLines(Disk(MaterialMapPath), lines.ToArray());
        }

        /// <summary>Swaps every renderer material that has a branding-free clone.</summary>
        public static int ApplyCleanMaterials(GameObject root)
        {
            var map = LoadMap(MaterialMapPath);
            var cache = new Dictionary<string, Material>();
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string mp = AssetDatabase.GetAssetPath(mats[i]), cp;
                    if (!map.TryGetValue(mp, out cp)) continue;
                    Material cm; if (!cache.TryGetValue(cp, out cm)) cache[cp] = cm = AssetDatabase.LoadAssetAtPath<Material>(cp);
                    if (cm != null) { mats[i] = cm; dirty = true; n++; }
                }
                if (dirty) r.sharedMaterials = mats;
            }
            return n;
        }

        [MenuItem("PIX Football/Branding/Clean All")]
        public static void CleanAll()
        {
            _texLines.Clear();
            MaterialFactory.EnsureFolder(PixDir);
            MaterialFactory.EnsureFolder(TexDir);
            InitFonts();
            CleanJersey();
            CleanPerimeterBoard();
            CleanBanner01Text();
            CleanAds();
            CleanScreenTest();
            CleanEndzoneNames();
            CleanProps();
            CleanFieldMarkings();
            CleanStadiumBase();
            File.WriteAllLines(Disk(TextureMapPath), _texLines.ToArray());
            AssetDatabase.Refresh();
            CloneMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PIX] branding cleaned: " + _texLines.Count + " textures");
        }
    }
}
