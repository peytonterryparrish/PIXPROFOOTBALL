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

        // ======================================================================== Tile theme (Direction 16 "Tile Hub Modern", SPEC section 1)
        // These fields are inert in the Glass look (flat = false). Colours are sRGB hex from the mock's CSS; lengths are canvas units on the 1920 x 1080 reference canvas.
        /// <summary>Flat solid surfaces: square corners, no backdrop sampling, no sheen, gold focus (the shader's flat branch).</summary>
        public bool flat = false;
        public Color gold = Hex("#F2A63A"), goldHi = Hex("#FFCF7A"), onGold = Hex("#17130A");
        public Color bgTop = Hex("#17181C"), bgBottom = Hex("#101114"), bgGlow = Hex("#2A2D34");
        public Color topBarTop = Hex("#0C0D0F"), topBarBottom = Hex("#15161A"), topBarLine = Hex("#2B2D33");
        public Color tickerBg = Hex("#0C0D10"), footerBg = Hex("#0A0B0D");
        public Color tileFill = Hex("#25272C"), tileFlatA = Hex("#2C2F35"), tileFlatB = Hex("#1D1F24");
        public Color panelFill = Hex("#202228"), panelHead = Hex("#15171B"), panelHeadAlt = Hex("#181A1F");
        public Color rowFill = Hex("#1A1C20"), rowFocus = Hex("#272A31");
        public Color tabOff = Hex("#1D1F24"), textTab = Hex("#9A9EA8");
        public Color border = Hex("#34373E"), borderPanel = Hex("#3A3D45"), hairline = Hex("#2C2F35");
        public Color shade = Hex("#0A0B0E");
        public float shadeAlpha0 = 0.92f, shadeAlpha45 = 0.35f, shadeAlpha75 = 0.05f;
        public Color tagPass = Hex("#4FC3FF"), tagRun = Hex("#FF8F3A"), tagText = Hex("#0B0C0E");
        public Color good = Hex("#7EE08F"), bad = Hex("#FF7A6A");
        public Color fieldTop = Hex("#1E5A2C"), fieldBottom = Hex("#123A1D");
        public Color glyphA = Hex("#2F9E4F"), glyphB = Hex("#D2423B"), glyphX = Hex("#2F6FD6"), glyphY = Hex("#E5B021");
        public Color glyphKeyTop = Hex("#777777"), glyphKeyBottom = Hex("#3C3C3C"), glyphKeyText = Hex("#F2F2F2");
        // focus / edge
        public float restEdge = 2f, focusEdge = 4f, ringWidth = 2f, ringAlpha = 0.35f, glowAlpha = 0.55f, glowRadius = 46f, sheenAlpha = 0.16f, shadowAlpha = 0.5f, pressScale = 0.985f;
        public float tileScale = 1.035f, heroScale = 1.02f, cardScale = 1.04f;
        // layout
        public float tabSkew = -14f, tabGap = 6f, tabPadX = 34f, tabHeight = 54f, topBarHeight = 112f, tickerHeight = 46f, footerHeight = 62f, tickerSpeed = 96f;
        public float tileGutter = 20f, cardGutter = 32f, marginX = 60f, contentTop = 138f, listRowHeight = 124f, tableRowHeight = 50f, tableHeadHeight = 56f;
        // motion (seconds / units)
        public float tabSlide = 0.16f, crossfade = 0.20f, crossDrift = 24f, revealStagger = 0.04f, revealRise = 18f, revealScale = 0.97f, glowPulse = 0f;
        // type scale (canvas px; nothing below 20)
        public float sizeTab = 28f, sizeTabSmall = 25f, sizeTileTitle = 38f, sizeHeroTitle = 56f, sizePanelTitle = 30f, sizeGroupTitle = 24f, sizeSelectedPlay = 44f, sizeCardTitle = 28f, sizeRowTitle = 27f, sizeTableBody = 22f, sizeTableHead = 20f, sizeSub = 20f, sizeBadge = 20f, sizeLegend = 22f;

        /// <summary>CSS letter-spacing (px at a given font size) as TMP character spacing (TMP counts hundredths of an em): 3 px at 28 px = 10.7.</summary>
        public static float Em(float px, float fontSize) { return fontSize > 0.01f ? px / fontSize * 100f : 0f; }

        /// <summary>The Tile theme (Direction 16) tokens: the numbers of SPEC section 1, in the same schema as the glass tokens (the extra tile fields above carry the rest).</summary>
        public static GlassTokens Tile()
        {
            var t = new GlassTokens();
            t.flat = true;
            // surface (tile body gradient 160deg #2C2F35 -> #1D1F24, opaque) and edges (border / panel border / hairline)
            t.surfaceTop = t.tileFlatA; t.surfaceMid = Hex("#25272C"); t.surfaceBottom = t.tileFlatB; t.opacityTop = t.opacityMid = t.opacityBottom = 1f;
            t.edgeA = t.border; t.edgeB = t.borderPanel; t.edgeC = t.hairline; t.edgeWidth = 2f; t.edgeOpacity = 1f;
            t.actionLeft = t.goldHi; t.actionRight = t.gold;
            t.sheen = 0f; t.noise = 0.004f;
            t.blurRadius = 0f; t.blurPasses = 0; t.backdropDim = 0f; t.backdropDesaturate = 0f; t.refraction = 0f;
            t.frostDark = t.bgBottom; t.frostLight = t.bgTop;
            t.textPrimary = Color.white; t.textSecondary = Hex("#C1C5CF"); t.textMuted = Hex("#8D919B"); t.textOnAction = t.onGold;
            t.accent = t.gold; t.accentHot = t.goldHi; t.accentDeep = Hex("#C98A2A");
            t.lightGrey = Hex("#E6E8EE"); t.midGrey = Hex("#A7ABB5");
            t.danger = t.bad; t.success = t.good; t.warning = t.gold;
            t.cornerRadius = 0f; t.cornerRadiusSmall = 0f; t.cornerRadiusLarge = 0f;
            t.grid = 8f; t.panelPadding = 24f; t.gutter = 20f; t.rowHeight = 64f; t.safeMarginX = 60f; t.safeMarginY = 28f;
            t.motionFast = 0.12f; t.motionBase = 0.16f; t.motionSlow = 0.2f; t.focusScale = t.tileScale;
            t.sizeCaption = 20f; t.sizeLabel = 24f; t.sizeBody = 26f; t.sizeHeading = 34f; t.sizeTitle = 56f; t.sizeHero = 62f;
            t.trackingCaps = Em(3f, 28f); t.trackingTitle = Em(1f, 38f);
            return t;
        }

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
