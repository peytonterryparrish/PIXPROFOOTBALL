using UnityEngine;

namespace PixFootball.UI
{
    /// <summary>
    /// v0.11 match HUD tokens, taken from the chosen menu direction 16 "Tile Hub Modern" (D:\PIXFootball\Work\UI16\SPEC.md): charcoal tiles, one gold accent, square corners, nothing under 20 canvas units.
    /// </summary>
    public static class HudStyle
    {
        static Color C(int hex, float a = 1f) { return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, a); }

        public static readonly Color Gold = C(0xF2A63A), GoldHi = C(0xFFCF7A), OnGold = C(0x17130A);
        public static readonly Color Tile = C(0x25272C, 0.94f), TileSolid = C(0x25272C), Panel = C(0x202228, 0.94f), PanelHead = C(0x15171B, 0.96f), Row = C(0x1A1C20, 0.94f);
        public static readonly Color Border = C(0x34373E), Hairline = C(0x2C2F35);
        public static readonly Color Text1 = Color.white, Text2 = C(0xC1C5CF), Muted = C(0x8D919B);
        public static readonly Color Good = C(0x7EE08F), Bad = C(0xFF7A6A), TagPass = C(0x4FC3FF), TagRun = C(0xFF8F3A), Flag = C(0xFFEA00);
    }

    /// <summary>
    /// v0.11 HUD sizes, in canvas units of the 1920 x 1080 reference (the CanvasScaler multiplies them by one factor for every resolution, so a pop-up has the same share of the screen at 1080p, 1440p, 4K
    /// and on a 21:9 canvas, which only grows sideways). Everything is anchored to the title-safe margins, never to the raw screen edge.
    /// </summary>
    public static class HudMetrics
    {
        /// <summary>Smallest text anywhere in the HUD (canvas units).</summary>
        public const float MinFont = 20f;
        /// <summary>Title-safe margins (left / right, top / bottom) and the gap between stacked pieces.</summary>
        public const float SafeX = 58f, SafeY = 36f, Gap = 12f;
        /// <summary>Widest the score bar grows (canvas units): on an ultrawide canvas it stays a centred block instead of stretching across the whole screen.</summary>
        public const float BarMaxWidth = 1760f;
        /// <summary>The pop-up lane: top centre, below the safe margin. All event pop-ups (banner, whistle, flag, toast) stack here so two never sit on top of each other.</summary>
        public static float PopupTop = 40f; public const float PopupMaxWidth = 880f;
        /// <summary>Helper bar: glyph size and how long it shows in Auto mode (first show / after a press), seconds.</summary>
        public static float BarLift = 24f;
        /// <summary>Keeps the top / bottom margins at the title-safe 3 % of the screen height on tall canvases (4:3 grows the canvas upward, a fixed 24 units would fall inside the safe band).</summary>
        public static void SetCanvasHeight(float h) { BarLift = Mathf.Max(24f, 0.034f * h); PopupTop = Mathf.Max(40f, 0.04f * h); }
        /// <summary>Width of the bottom score bar on a canvas of the given width (canvas units): the canvas minus the safe margins, never wider than <see cref="BarMaxWidth"/>.</summary>
        public static float BarWidth(float canvasWidth) { return Mathf.Min(canvasWidth - 2f * SafeX + 48f, BarMaxWidth); }
        /// <summary>Top of the next pop-up in the lane: the one above it ends at <paramref name="cursor"/> (negative y from the top), <paramref name="height"/> tall, then one gap.</summary>
        public static float NextLaneY(float cursor, float height) { return cursor - height - Gap; }
        public const float HelperGlyph = 30f, HelperFirstSeconds = 8f, HelperActivitySeconds = 4f;
        /// <summary>Hint bar of the user-control game fades after this many seconds (Auto mode).</summary>
        public const float HintSeconds = 4.5f;
        /// <summary>TV overlay cards (lower third, side card, next play) are drawn at this share of their design size.</summary>
        public const float CardScale = 0.86f;
    }
}
