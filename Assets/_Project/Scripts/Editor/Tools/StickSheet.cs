using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// CPU stick-figure contact sheets (no GPU rendering): for every clip a filmstrip of N poses, side view on top and front view below, ground line, planted foot highlighted,
    /// hips path. Many clips are packed into one PNG with labels. Input: Logs/sticksheet_in.txt, lines "clipName|frames[|t0|t1]" (clip looked up by ArtLibrary name, the
    /// Yan folder, or Clean/); optional first line "# title". Output: Docs/Screens/anim/sheet_<title>_<n>.png
    /// </summary>
    public static class StickSheet
    {
        const int CellW = 150, CellH = 200, Label = 14, Pad = 4;
        static readonly Color Bg = new Color(0.10f, 0.11f, 0.13f), Ground = new Color(0.35f, 0.40f, 0.35f), Text = new Color(0.9f, 0.9f, 0.7f);
        static readonly Color CHead = new Color(1f, 0.9f, 0.6f), CSpine = new Color(0.8f, 0.8f, 0.85f), CL = new Color(0.4f, 0.8f, 1f), CR = new Color(1f, 0.55f, 0.35f), Plant = new Color(1f, 0.2f, 0.2f);

        public static void Run()
        {
            BatchWatchdog.Arm(30);
            string[] lines = File.ReadAllLines("Logs/sticksheet_in.txt");
            string title = "sheet";
            var items = new List<string>();
            foreach (var raw in lines) { var l = raw.Trim(); if (l.Length == 0) continue; if (l.StartsWith("#")) { title = l.Substring(1).Trim(); continue; } items.Add(l); }
            RenderList(items, title, 6, 8);
        }

        public static AnimationClip FindClip(string name)
        {
            if (name.StartsWith("clean:")) return AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(name.Substring(6)));
            var lib = ArtLibrary.Load();
            var c = lib != null ? lib.ClipByName(name) : null;
            if (c != null) return c;
            foreach (string folder in new[] { "Assets/_Licensed/DraftFever/Animations", "Assets/_Licensed/DraftFever/Animations/Clean" })
            {
                foreach (string g in AssetDatabase.FindAssets(name, new[] { folder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (p.EndsWith(".anim")) { var a = AssetDatabase.LoadAssetAtPath<AnimationClip>(p); if (a != null && a.name == name) return a; }
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p)) { var cl = o as AnimationClip; if (cl != null && cl.name == name && !cl.name.StartsWith("__preview")) return cl; }
                }
            }
            return null;
        }

        /// <summary>Big renders live on the D: drive (C: is nearly full); falls back to Logs/ when there is no D: drive.</summary>
        static string OutDir { get { return Directory.Exists("D:/") ? "D:/PIXFootball/Renders/animation" : "Logs/anim_sheets"; } }

        public static void RenderList(List<string> items, string title, int frames, int perSheet)
        {
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            Directory.CreateDirectory(OutDir);
            int sheet = 0;
            for (int s = 0; s < items.Count; s += perSheet)
            {
                var group = items.Skip(s).Take(perSheet).ToList();
                var rows = new List<Texture2D>();
                foreach (var it in group)
                {
                    var p = it.Split('|');
                    var clip = FindClip(p[0]);
                    if (clip == null) { Debug.LogWarning("[StickSheet] missing " + p[0]); continue; }
                    int n = p.Length > 1 ? int.Parse(p[1]) : frames;
                    float t0 = p.Length > 2 ? float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                    float t1 = p.Length > 3 ? float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : Mathf.Min(clip.length, 24f);
                    if (t1 <= 0f || t1 > clip.length) t1 = clip.length;
                    rows.Add(Strip(rig, clip, n, t0, t1));
                }
                if (rows.Count == 0) continue;
                int W = rows.Max(r => r.width), H = rows.Sum(r => r.height) + 2;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                Fill(tex, 0, 0, W, H, Bg);
                int y = H;
                foreach (var r in rows) { y -= r.height; Blit(tex, r, 0, y); UnityEngine.Object.DestroyImmediate(r); }
                string path = OutDir + "/sheet_" + title + "_" + (sheet++) + ".png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                Debug.Log("[StickSheet] " + path + " (" + group.Count + " clips)");
            }
            rig.Dispose();
        }

        static Texture2D Strip(AnimRig rig, AnimationClip clip, int n, float t0, float t1)
        {
            float rate = 30f;
            var fr = rig.Sample(clip, 0f, Mathf.Min(clip.length, 24f), rate);
            int W = n * CellW + Pad, H = Label + 2 * CellH;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            Fill(tex, 0, 0, W, H, Bg);
            Vector3 hip0 = fr[0].P[AnimRig.Hips];
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Lerp(t0, t1, n == 1 ? 0f : i / (float)(n - 1));
                int fi = Mathf.Clamp(Mathf.RoundToInt(t * rate), 0, fr.Count - 1);
                var f = fr[fi];
                // planted foot: the lower of the two ankles if close to the ground
                bool leftLow = f.P[AnimRig.LF].y < f.P[AnimRig.RF].y;
                int x0 = Pad + i * CellW;
                // heading-relative projection: side view along the body forward axis at this frame
                Vector3 fwd = AnimRig.Forward(f); Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 hc = f.P[AnimRig.Hips];
                for (int v = 0; v < 2; v++)
                {
                    int oy = v == 0 ? CellH + Label : Label;       // side view top row, front view bottom row
                    DrawCell(tex, x0, oy, f, hc, v == 0 ? fwd : right, leftLow);
                    Rect(tex, x0, oy, CellW - 2, 1, Ground);
                }
                Str(tex, x0 + 2, Label + 2 * CellH - 10, (t).ToString("0.00") + "S", Text);
            }
            Str(tex, 2, 2, clip.name.Length > 70 ? clip.name.Substring(0, 70) : clip.name, new Color(0.6f, 1f, 0.7f));
            Str(tex, W - 160, 2, "LEN " + clip.length.ToString("0.00") + " FPS " + Mathf.RoundToInt(clip.frameRate), Text);
            return tex;
        }

        static void DrawCell(Texture2D tex, int x0, int y0, AnimRig.Frame f, Vector3 hc, Vector3 axis, bool leftLow)
        {
            // axis = horizontal screen axis in world space (side view: body forward, front view: body right). 1 m = 95 px. ground at y0+8, figure centred horizontally.
            float S = 95f;
            Func<int, Vector2> P = idx =>
            {
                Vector3 d = f.P[idx] - hc;
                float h = Vector3.Dot(d, axis);
                return new Vector2(x0 + CellW * 0.5f + h * S, y0 + 8 + f.P[idx].y * S);
            };
            // ground line is at world y = 0 (feet height shows floating / penetration)
            Seg(tex, P(AnimRig.Hips), P(AnimRig.Spine), CSpine); Seg(tex, P(AnimRig.Spine), P(AnimRig.Chest), CSpine); Seg(tex, P(AnimRig.Chest), P(AnimRig.Neck), CSpine); Seg(tex, P(AnimRig.Neck), P(AnimRig.Head), CHead);
            Seg(tex, P(AnimRig.Neck), P(AnimRig.LUA), CL); Seg(tex, P(AnimRig.LUA), P(AnimRig.LLA), CL); Seg(tex, P(AnimRig.LLA), P(AnimRig.LH), CL);
            Seg(tex, P(AnimRig.Neck), P(AnimRig.RUA), CR); Seg(tex, P(AnimRig.RUA), P(AnimRig.RLA), CR); Seg(tex, P(AnimRig.RLA), P(AnimRig.RH), CR);
            Seg(tex, P(AnimRig.Hips), P(AnimRig.LUL), CL); Seg(tex, P(AnimRig.LUL), P(AnimRig.LLL), CL); Seg(tex, P(AnimRig.LLL), P(AnimRig.LF), leftLow && f.P[AnimRig.LF].y < 0.13f ? Plant : CL); Seg(tex, P(AnimRig.LF), P(AnimRig.LT), CL);
            Seg(tex, P(AnimRig.Hips), P(AnimRig.RUL), CR); Seg(tex, P(AnimRig.RUL), P(AnimRig.RLL), CR); Seg(tex, P(AnimRig.RLL), P(AnimRig.RF), !leftLow && f.P[AnimRig.RF].y < 0.13f ? Plant : CR); Seg(tex, P(AnimRig.RF), P(AnimRig.RT), CR);
            var hd = P(AnimRig.Head); Dot(tex, (int)hd.x, (int)hd.y, 4, CHead);
        }

        // ---- raster helpers -----------------------------------------------------------------------------------
        static void Fill(Texture2D t, int x, int y, int w, int h, Color c)
        {
            var px = new Color[w * h]; for (int i = 0; i < px.Length; i++) px[i] = c; t.SetPixels(x, y, w, h, px);
        }
        static void Rect(Texture2D t, int x, int y, int w, int h, Color c)
        {
            x = Mathf.Max(0, x); y = Mathf.Max(0, y); w = Mathf.Min(w, t.width - x); h = Mathf.Min(h, t.height - y); if (w <= 0 || h <= 0) return; Fill(t, x, y, w, h, c);
        }
        static void Blit(Texture2D dst, Texture2D src, int x, int y) { dst.SetPixels(x, y, src.width, src.height, src.GetPixels()); }
        static void Dot(Texture2D t, int cx, int cy, int r, Color c)
        {
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++) if (x * x + y * y <= r * r) Put(t, cx + x, cy + y, c);
        }
        static void Put(Texture2D t, int x, int y, Color c) { if (x >= 0 && y >= 0 && x < t.width && y < t.height) t.SetPixel(x, y, c); }
        static void Seg(Texture2D t, Vector2 a, Vector2 b, Color c)
        {
            int n = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, n == 0 ? 0f : i / (float)n);
                int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                Put(t, x, y, c); Put(t, x + 1, y, c); Put(t, x, y + 1, c);
            }
        }

        // 5x7 pixel font (A-Z, 0-9, a few symbols)
        static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            {'A',"01110100011000110001111111000110001"}, {'B',"11110100011000111110100011000111110"}, {'C',"01110100011000010000100001000101110"},
            {'D',"11110100011000110001100011000111110"}, {'E',"11111100001000011110100001000011111"}, {'F',"11111100001000011110100001000010000"},
            {'G',"01110100011000010111100011000101110"}, {'H',"10001100011000111111100011000110001"}, {'I',"01110001000010000100001000010001110"},
            {'J',"00111000100001000010000101001001100"}, {'K',"10001100101010011000101001001010001"}, {'L',"10000100001000010000100001000011111"},
            {'M',"10001110111010110101100011000110001"}, {'N',"10001110011010110011100011000110001"}, {'O',"01110100011000110001100011000101110"},
            {'P',"11110100011000111110100001000010000"}, {'Q',"01110100011000110001101011001001101"}, {'R',"11110100011000111110101001001010001"},
            {'S',"01111100001000001110000010000111110"}, {'T',"11111001000010000100001000010000100"}, {'U',"10001100011000110001100011000101110"},
            {'V',"10001100011000110001100010101000100"}, {'W',"10001100011000110101101011101110001"}, {'X',"10001100010101000100010101000110001"},
            {'Y',"10001100010101000100001000010000100"}, {'Z',"11111000010001000100010001000011111"},
            {'0',"01110100011001110101110011000101110"}, {'1',"00100011000010000100001000010001110"}, {'2',"01110100010000100010001000100011111"},
            {'3',"11110000010000101110000010000111110"}, {'4',"00010001100101010010111110001000010"}, {'5',"11111100001111000001000010000111110"},
            {'6',"00110010001000011110100011000101110"}, {'7',"11111000010000100010001000100001000"}, {'8',"01110100011000101110100011000101110"},
            {'9',"01110100011000101111000010001001100"}, {'_',"00000000000000000000000000000011111"}, {'.',"00000000000000000000000000001100110"},
            {'-',"00000000000000011111000000000000000"}, {' ',"00000000000000000000000000000000000"}, {'|',"00100001000010000100001000010000100"},
        };

        static void Str(Texture2D t, int x, int y, string s, Color c)
        {
            foreach (char ch0 in s)
            {
                char ch = char.ToUpperInvariant(ch0);
                string g; if (!Font.TryGetValue(ch, out g)) g = Font[' '];
                for (int r = 0; r < 7; r++) for (int col = 0; col < 5; col++) if (g[r * 5 + col] == '1') Put(t, x + col, y + (6 - r), c);
                x += 6;
            }
        }
    }
}
