using System;
using System.IO;
using TMPro;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>CPU image canvas with top-left origin helpers: fills, patches, row/column interpolation, SDF text (from a TMP font atlas).</summary>
    public sealed class PixelCanvas
    {
        public int W, H;
        public Color32[] Px; // stored bottom-left origin (Unity convention)

        public PixelCanvas(int w, int h) { W = w; H = h; Px = new Color32[w * h]; }

        public static PixelCanvas Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new Exception("cannot load " + path);
            var c = new PixelCanvas(tex.width, tex.height) { Px = tex.GetPixels32() };
            UnityEngine.Object.DestroyImmediate(tex);
            return c;
        }

        public void Save(string path)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(Px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        int Idx(int x, int yTop) { return (H - 1 - yTop) * W + x; }
        public Color32 Get(int x, int yTop) { return Px[Idx(Mathf.Clamp(x, 0, W - 1), Mathf.Clamp(yTop, 0, H - 1))]; }
        public void Set(int x, int yTop, Color32 c) { if (x >= 0 && x < W && yTop >= 0 && yTop < H) Px[Idx(x, yTop)] = c; }

        public void Clear(Color32 c) { for (int i = 0; i < Px.Length; i++) Px[i] = c; }

        public void FillRect(int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++) Px[Idx(x, y)] = c;
        }

        public Color32 Average(int x0, int y0, int x1, int y1)
        {
            long r = 0, g = 0, b = 0, a = 0; int n = 0;
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++) { var c = Px[Idx(x, y)]; r += c.r; g += c.g; b += c.b; a += c.a; n++; }
            if (n == 0) return new Color32(0, 0, 0, 255);
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
        }

        /// <summary>Fills the rect with the average colour of a clean sample rect, plus tiny deterministic noise so it does not read as a flat hole.</summary>
        public void FillSample(int x0, int y0, int x1, int y1, int sx0, int sy0, int sx1, int sy1, float noise = 2f)
        {
            var c = Average(sx0, sy0, sx1, sy1);
            uint seed = 12345;
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++)
                {
                    seed = seed * 1664525u + 1013904223u;
                    float n = ((seed >> 16) & 0xFF) / 255f - 0.5f;
                    Px[Idx(x, y)] = new Color32(Cl(c.r + n * noise), Cl(c.g + n * noise), Cl(c.b + n * noise), c.a);
                }
        }

        static byte Cl(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255); }

        /// <summary>Copies the pixels from the rect shifted by (dx, dy) over the rect.</summary>
        public void Patch(int x0, int y0, int x1, int y1, int dx, int dy)
        {
            int w = x1 - x0, h = y1 - y0;
            var tmp = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tmp[y * w + x] = Get(x0 + x + dx, y0 + y + dy);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) Set(x0 + x, y0 + y, tmp[y * w + x]);
        }

        /// <summary>Per row linear interpolation between the pixels just outside the rect (horizontal fill).</summary>
        public void RowInterp(int x0, int y0, int x1, int y1, int margin = 4)
        {
            for (int y = y0; y < y1; y++)
            {
                var a = AvgRow(x0 - margin - 1, x0 - 1, y);
                var b = AvgRow(x1, x1 + margin, y);
                for (int x = x0; x < x1; x++)
                {
                    float t = (x - x0 + 0.5f) / (x1 - x0);
                    Set(x, y, Color32.Lerp(a, b, t));
                }
            }
        }

        /// <summary>Per column linear interpolation between the pixels just above and below the rect (vertical fill; keeps vertical stripes intact).</summary>
        public void ColInterp(int x0, int y0, int x1, int y1, int margin = 4)
        {
            for (int x = x0; x < x1; x++)
            {
                var a = AvgCol(x, y0 - margin - 1, y0 - 1);
                var b = AvgCol(x, y1, y1 + margin);
                for (int y = y0; y < y1; y++)
                {
                    float t = (y - y0 + 0.5f) / (y1 - y0);
                    Set(x, y, Color32.Lerp(a, b, t));
                }
            }
        }

        Color32 AvgRow(int xa, int xb, int y)
        {
            long r = 0, g = 0, b = 0, a = 0; int n = 0;
            for (int x = xa; x <= xb; x++) { var c = Get(x, y); r += c.r; g += c.g; b += c.b; a += c.a; n++; }
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
        }

        Color32 AvgCol(int x, int ya, int yb)
        {
            long r = 0, g = 0, b = 0, a = 0; int n = 0;
            for (int y = ya; y <= yb; y++) { var c = Get(x, y); r += c.r; g += c.g; b += c.b; a += c.a; n++; }
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
        }

        public void Blend(int x, int yTop, Color32 c, float alpha)
        {
            if (x < 0 || x >= W || yTop < 0 || yTop >= H || alpha <= 0f) return;
            int i = Idx(x, yTop);
            var d = Px[i];
            float a = alpha * (c.a / 255f);
            byte oa = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(d.a, a * 255f)), 0, 255);
            Px[i] = new Color32(Cl(d.r + (c.r - d.r) * a), Cl(d.g + (c.g - d.g) * a), Cl(d.b + (c.b - d.b) * a), oa);
        }

        public void FillRectBlend(int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++) for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++) Blend(x, y, c, 1f);
        }

        public void FillPoly(Vector2[] pts, Color32 c)
        {
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
            for (int y = Mathf.Max(0, (int)minY); y <= Mathf.Min(H - 1, (int)maxY); y++)
                for (int x = Mathf.Max(0, (int)minX); x <= Mathf.Min(W - 1, (int)maxX); x++)
                    if (Inside(pts, x + 0.5f, y + 0.5f)) Blend(x, y, c, 1f);
        }

        static bool Inside(Vector2[] p, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if (((p[i].y > y) != (p[j].y > y)) && (x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)) inside = !inside;
            return inside;
        }

        public void FillCircle(float cx, float cy, float r, Color32 c)
        {
            for (int y = Mathf.Max(0, (int)(cy - r - 1)); y <= Mathf.Min(H - 1, (int)(cy + r + 1)); y++)
                for (int x = Mathf.Max(0, (int)(cx - r - 1)); x <= Mathf.Min(W - 1, (int)(cx + r + 1)); x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (a > 0f) Blend(x, y, c, a);
                }
        }

        /// <summary>Returns a rotated copy (quarter turns clockwise).</summary>
        public PixelCanvas Rotate90(int quarterTurnsCw)
        {
            quarterTurnsCw = ((quarterTurnsCw % 4) + 4) % 4;
            if (quarterTurnsCw == 0) return this;
            var cur = this;
            for (int q = 0; q < quarterTurnsCw; q++)
            {
                var n = new PixelCanvas(cur.H, cur.W);
                for (int y = 0; y < cur.H; y++) for (int x = 0; x < cur.W; x++) n.Set(cur.H - 1 - y, x, cur.Get(x, y));
                cur = n;
            }
            return cur;
        }

        /// <summary>Draws src scaled to w x h pixels centred at (cx, cy) (top-origin), box-filtered, alpha blended. asMask: write luminance*alpha as white with that alpha.</summary>
        public void DrawScaled(PixelCanvas src, float cx, float cy, float w, float h, float opacity = 1f, bool asMask = false, float maskGain = 1f)
        {
            int x0 = Mathf.FloorToInt(cx - w * 0.5f), y0 = Mathf.FloorToInt(cy - h * 0.5f), x1 = Mathf.CeilToInt(cx + w * 0.5f), y1 = Mathf.CeilToInt(cy + h * 0.5f);
            float sx = src.W / w, sy = src.H / h;
            int ss = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(sx, sy)));
            ss = Mathf.Min(ss, 6);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int j = 0; j < ss; j++)
                        for (int i = 0; i < ss; i++)
                        {
                            float u = (x + (i + 0.5f) / ss - (cx - w * 0.5f)) * sx, v = (y + (j + 0.5f) / ss - (cy - h * 0.5f)) * sy;
                            n++;
                            if (u < 0 || v < 0 || u >= src.W || v >= src.H) continue;
                            var c = src.Get((int)u, (int)v); float ca = c.a / 255f;
                            r += c.r * ca; g += c.g * ca; b += c.b * ca; a += ca;
                        }
                    if (a <= 0f) continue;
                    float al = a / n;
                    if (asMask) { float lum = (0.3f * r + 0.59f * g + 0.11f * b) / a / 255f; Blend(x, y, new Color32(255, 255, 255, 255), Mathf.Clamp01(al * lum * maskGain) * opacity); }
                    else Blend(x, y, new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), 255), al * opacity);
                }
        }

        public void Composite(PixelCanvas src, int dx, int dy)
        {
            for (int y = 0; y < src.H; y++) for (int x = 0; x < src.W; x++) { var c = src.Get(x, y); if (c.a > 0) Blend(dx + x, dy + y, c, 1f); }
        }
    }

    /// <summary>Signed-distance-field text renderer using the atlas of a TextMeshPro font asset.</summary>
    public sealed class SdfText
    {
        readonly TMP_FontAsset _fa;
        readonly byte[][] _atlas; readonly int[] _aw, _ah;
        readonly float _pad, _point;

        public SdfText(TMP_FontAsset fa, string chars)
        {
            _fa = fa;
            string missing;
            fa.TryAddCharacters(chars, out missing);
            _pad = fa.atlasPadding; _point = fa.faceInfo.pointSize;
            int n = fa.atlasTextures.Length;
            _atlas = new byte[n][]; _aw = new int[n]; _ah = new int[n];
            for (int i = 0; i < n; i++)
            {
                var t = fa.atlasTextures[i];
                _aw[i] = t.width; _ah[i] = t.height;
                var arr = new byte[t.width * t.height];
                if (t.format == TextureFormat.Alpha8 || t.format == TextureFormat.R8)
                {
                    var raw = t.GetRawTextureData<byte>();
                    for (int k = 0; k < arr.Length; k++) arr[k] = raw[k];
                }
                else
                {
                    var px = t.GetPixels32();
                    for (int k = 0; k < arr.Length; k++) arr[k] = t.format == TextureFormat.RGBA32 ? px[k].a : px[k].r;
                }
                _atlas[i] = arr;
            }
        }

        float Sample(int ai, float x, float y)
        {
            int w = _aw[ai], h = _ah[ai];
            x -= 0.5f; y -= 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float v00 = P(ai, x0, y0, w, h), v10 = P(ai, x0 + 1, y0, w, h), v01 = P(ai, x0, y0 + 1, w, h), v11 = P(ai, x0 + 1, y0 + 1, w, h);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
        }

        float P(int ai, int x, int y, int w, int h)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return 0f;
            return _atlas[ai][y * w + x] / 255f;
        }

        public float Measure(string s, float size)
        {
            float scale = size / _point, w = 0f;
            foreach (char c in s)
            {
                TMP_Character ch;
                if (_fa.characterLookupTable.TryGetValue((uint)c, out ch)) w += ch.glyph.metrics.horizontalAdvance * scale;
            }
            return w;
        }

        /// <summary>Draws text with its baseline at yBase (top-origin px), starting at x. size = font size in px (em). bias lowers the SDF threshold (bolder).</summary>
        public void Draw(PixelCanvas img, string s, float x, float yBase, float size, Color32 color, float skew = 0f, float bias = 0f, float letterSpacing = 0f)
        {
            float scale = size / _point;
            float pen = x;
            foreach (char c in s)
            {
                TMP_Character ch;
                if (!_fa.characterLookupTable.TryGetValue((uint)c, out ch)) continue;
                var g = ch.glyph; var gr = g.glyphRect; var m = g.metrics;
                int ai = Mathf.Clamp(g.atlasIndex, 0, _atlas.Length - 1);
                if (c != ' ' && gr.width > 0)
                {
                    float left = pen + (m.horizontalBearingX - _pad) * scale;
                    float top = yBase - (m.horizontalBearingY + _pad) * scale;
                    float wD = (gr.width + 2f * _pad) * scale, hD = (gr.height + 2f * _pad) * scale;
                    float sk = Mathf.Abs(skew) * hD;
                    int ix0 = Mathf.FloorToInt(left - sk) - 1, ix1 = Mathf.CeilToInt(left + wD + sk) + 1;
                    int iy0 = Mathf.FloorToInt(top) - 1, iy1 = Mathf.CeilToInt(top + hD) + 1;
                    float aa = Mathf.Max(0.25f / (scale * _pad), 0.015f);
                    for (int j = iy0; j < iy1; j++)
                        for (int i = ix0; i < ix1; i++)
                        {
                            float shift = skew * (yBase - (j + 0.5f));
                            float ax = gr.x - _pad + (i + 0.5f - left - shift) / scale;
                            float ay = gr.y + gr.height + _pad - (j + 0.5f - top) / scale;
                            if (ax < gr.x - _pad || ax > gr.x + gr.width + _pad || ay < gr.y - _pad || ay > gr.y + gr.height + _pad) continue;
                            float d = Sample(ai, ax, ay);
                            float a = Mathf.SmoothStep(0f, 1f, (d - (0.5f - bias - aa)) / (2f * aa));
                            if (a > 0.003f) img.Blend(i, j, color, a);
                        }
                }
                pen += m.horizontalAdvance * scale + letterSpacing;
            }
        }
    }
}
