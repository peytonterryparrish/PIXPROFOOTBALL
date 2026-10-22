using System;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The design tokens of the glass UI (Docs/UIStyleGuide_Glass.md). Values come from the art pack's design_tokens.json / source/ui.py (graphite surface gradient, silver hairline gradient,
    /// white and grey type, restrained orange) plus the in-engine additions the pack cannot express (backdrop blur, motion, focus). Pure data: serialisable to JSON, no engine objects.
    /// All lengths are canvas units on the 1920 x 1080 reference canvas.
    /// </summary>
    [Serializable]
    public class GlassTokens
    {
        // ---- surface: the pack's three-stop diagonal gradient (top left -> middle -> bottom right) with its opacities
        public Color surfaceTop = Hex("#66666A"), surfaceMid = Hex("#242426"), surfaceBottom = Hex("#555558");
        public float opacityTop = 0.48f, opacityMid = 0.40f, opacityBottom = 0.38f;
        // ---- edge: thin silver hairline with the pack's gradient
        public Color edgeA = Hex("#DBDBDE"), edgeB = Hex("#77777B"), edgeC = Hex("#E0E0E3");
        public float edgeWidth = 1.4f, edgeOpacity = 0.9f;
        // ---- action (primary button) gradient
        public Color actionLeft = Hex("#F3F3F3"), actionRight = Hex("#C7C9CC");
        // ---- reflection and texture
        public float sheen = 1f, noise = 0.012f;
        // ---- backdrop blur
        public float blurRadius = 16f;       // gaussian radius in half-resolution pixels
        public int blurPasses = 2;
        public float backdropDim = 0.58f, backdropDesaturate = 0.35f, refraction = 0.012f;
        public Color frostDark = Hex("#161719"), frostLight = Hex("#333439");   // static frosted fallback (Low tier)
        // ---- type colours
        public Color textPrimary = Hex("#F5F5F5"), textSecondary = Hex("#BFC1C4"), textMuted = Hex("#8E9094"), textOnAction = Hex("#1B1B1D");
        // ---- accents
        public Color accent = Hex("#F49A52"), accentHot = Hex("#FFB070"), accentDeep = Hex("#C9722F");
        public Color lightGrey = Hex("#E1E2E4"), midGrey = Hex("#A7AAAF");
        public Color danger = Hex("#E2685C"), success = Hex("#86C9A0"), warning = Hex("#F4C152");
        // ---- shape
        public float cornerRadius = 12f, cornerRadiusSmall = 8f, cornerRadiusLarge = 18f;
        // ---- spacing grid
        public float grid = 8f, panelPadding = 28f, gutter = 16f, rowHeight = 64f, safeMarginX = 96f, safeMarginY = 54f;
        // ---- motion (seconds)
        public float motionFast = 0.12f, motionBase = 0.16f, motionSlow = 0.2f, focusScale = 1.025f;
        // ---- type scale (reference canvas pixels) and tracking (TMP character spacing)
        public float sizeCaption = 20f, sizeLabel = 24f, sizeBody = 28f, sizeHeading = 36f, sizeTitle = 56f, sizeHero = 104f;
        public float trackingCaps = 3f, trackingTitle = 4f;

        /// <summary>Pure managed hex parse ("#RRGGBB"): safe in field initialisers that run on loading threads.</summary>
        public static Color Hex(string h)
        {
            if (string.IsNullOrEmpty(h)) return Color.magenta;
            if (h[0] == '#') h = h.Substring(1);
            int v = Convert.ToInt32(h.Substring(0, 6), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public string ToJson() { return JsonUtility.ToJson(this, true); }

        public static GlassTokens FromJson(string json)
        {
            var t = new GlassTokens();
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, t);
            return t;
        }

        /// <summary>The surface colour at diagonal parameter t (0 top left, 1 bottom right) with its alpha, exactly as the shader evaluates it.</summary>
        public Color SurfaceAt(float t)
        {
            t = Mathf.Clamp01(t);
            Color c; float a;
            if (t < 0.55f) { float u = t / 0.55f; c = Color.Lerp(surfaceTop, surfaceMid, u); a = Mathf.Lerp(opacityTop, opacityMid, u); }
            else { float u = (t - 0.55f) / 0.45f; c = Color.Lerp(surfaceMid, surfaceBottom, u); a = Mathf.Lerp(opacityMid, opacityBottom, u); }
            c.a = a;
            return c;
        }

        /// <summary>WCAG contrast ratio between two colours (both treated as opaque sRGB).</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        static float Lin(float v) { return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }
        public static float Luminance(Color c) { return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b); }
    }
}
