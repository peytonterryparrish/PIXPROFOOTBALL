using System.Collections.Generic;
using PixFootball.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.Presentation
{
    /// <summary>Procedural sprites for the broadcast graphics (angled "parallelogram" ends like the UI kit's scorebug shapes, soft gradients) plus small layout helpers, all in the UI style guide palette.</summary>
    public static class TvUi
    {
        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        public static readonly Color Navy = new Color(0.043f, 0.055f, 0.094f, 0.96f);
        public static readonly Color NavyDeep = new Color(0.027f, 0.035f, 0.063f, 0.98f);
        public static readonly Color Panel = new Color(0.082f, 0.086f, 0.118f, 0.96f);
        public static readonly Color Gold = new Color(1f, 0.784f, 0.216f, 1f);
        public static readonly Color Blue = new Color(0.176f, 0.353f, 0.737f, 1f);
        public static readonly Color Row = new Color(0.165f, 0.169f, 0.216f, 0.96f);
        public static readonly Color RowAlt = new Color(0.115f, 0.12f, 0.16f, 0.96f);
        public static readonly Color Muted = new Color(0.77f, 0.77f, 0.77f, 1f);

        public static Color Readable(Color c) { float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; return l > 0.55f ? new Color(0.06f, 0.07f, 0.1f) : Color.white; }

        /// <summary>A white parallelogram strip of the given height whose ends lean by <paramref name="slant"/> pixels (9-sliced: stretch it horizontally and tint it).</summary>
        public static Sprite Para(int height, int slant, bool leftLean, bool rightLean)
        {
            string key = "para" + height + "_" + slant + "_" + (leftLean ? 1 : 0) + (rightLean ? 1 : 0);
            Sprite s;
            if (_cache.TryGetValue(key, out s) && s != null) return s;
            int pad = 2, w = (slant + pad) * 2 + 8;
            var tex = new Texture2D(w, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color32[w * height];
            const int ss = 4;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < w; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float fx = x + (sx + 0.5f) / ss, fy = y + (sy + 0.5f) / ss;
                            float k = fy / height;   // 0 bottom .. 1 top
                            // left edge: "/" leaning right toward the top; right edge: "/" too (a parallelogram) when rightLean, "\" never
                            float left = leftLean ? (slant + pad) * k : 0f;
                            float right = w - (rightLean ? (slant + pad) * (1f - k) : 0f);
                            if (fx >= left && fx <= right) hit++;
                        }
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * hit / (ss * ss)));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            float border = slant + pad + 1;
            s = Sprite.Create(tex, new Rect(0, 0, w, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, 0, border, 0));
            _cache[key] = s;
            return s;
        }

        /// <summary>Vertical alpha gradient (opaque at the bottom, transparent at the top), 9-sliced horizontally; flip with a negative scale or the Image's rotation.</summary>
        public static Sprite Fade()
        {
            Sprite s;
            if (_cache.TryGetValue("fade", out s) && s != null) return s;
            const int h = 128;
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            {
                float a = Mathf.SmoothStep(1f, 0f, y / (float)(h - 1));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, 4, h), new Vector2(0.5f, 0.5f), 100f);
            _cache["fade"] = s;
            return s;
        }

        public static Image Strip(Transform parent, string name, Color color, int height, int slant, bool leftLean, bool rightLean)
        {
            var img = UiKit.Panel(parent, name, color, Para(height, slant, leftLean, rightLean));
            img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f;
            return img;
        }

        public static Image Bar(Transform parent, string name, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var img = UiKit.Panel(parent, name, color);
            UiKit.Anchor(img.rectTransform, anchor, pivot, pos, size);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, bool title, TextAlignmentOptions align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, float spacing = 0f)
        {
            var t = UiKit.Label(parent, text, size, color, title, align);
            UiKit.Anchor(t.rectTransform, anchor, pivot, pos, box);
            t.characterSpacing = spacing;
            return t;
        }

        public static Image Crest(Transform parent, string key, float size, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            var sp = UiKit.Sprite(key);
            var img = UiKit.Panel(parent, "Crest", Color.white, sp);
            img.preserveAspect = true; img.enabled = sp != null;
            UiKit.Anchor(img.rectTransform, anchor, pivot, pos, new Vector2(size, size));
            return img;
        }

        public static float EaseOut(float u) { u = Mathf.Clamp01(u); return 1f - (1f - u) * (1f - u) * (1f - u); }
        public static float EaseIn(float u) { u = Mathf.Clamp01(u); return u * u * u; }
        public static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }
    }
}
