using System;

namespace PixFootball.Core
{
    /// <summary>Where a jersey number sits on the uniform texture: centre (uv), height (uv units), rotation about the centre (radians), mirrored or not.</summary>
    public struct NumberPlacement
    {
        public float CenterU, CenterV, Height, Rotation;
        public bool Mirrored;

        public NumberPlacement(float u, float v, float height, float rotation, bool mirrored = false)
        { CenterU = u; CenterV = v; Height = height; Rotation = rotation; Mirrored = mirrored; }

        /// <summary>The shader's packed form: (cx, cy, +-height, rotation); a negative height mirrors the glyphs horizontally.</summary>
        public void Pack(out float x, out float y, out float z, out float w)
        {
            x = CenterU; y = CenterV; z = Mirrored ? -Math.Abs(Height) : Math.Abs(Height); w = Rotation;
        }
    }

    /// <summary>
    /// Number atlas layout shared by the generator (editor), the shader (PixLitHooks.hlsl PixNumberMask) and the tests.
    /// The atlas holds the digits 0..9 in 5 columns x 2 rows (0..4 on the top row, 5..9 below); every cell is <see cref="Advance"/> times as wide as it is high
    /// (the generator draws a collegiate block numeral that fills the cell height). Channel R = fill, G = outline.
    /// A one or two digit number is laid out centred on the placement: one digit at 0, two digits at +- Advance / 2 (tens left of ones).
    /// </summary>
    public static class NumberAtlas
    {
        public const int Columns = 5, Rows = 2;
        /// <summary>Width of one digit cell in units of the number height.</summary>
        public const float Advance = 2f / 3f;
        public const int MaxNumber = 99;

        public static int Clamp(int number) { return number < 0 ? 0 : (number > MaxNumber ? MaxNumber : number); }

        public static int DigitCount(int number) { return Clamp(number) >= 10 ? 2 : 1; }

        /// <summary>The digits (tens, ones) of a number; tens is -1 for a single digit number.</summary>
        public static void Digits(int number, out int tens, out int ones)
        {
            number = Clamp(number);
            ones = number % 10; tens = number >= 10 ? number / 10 : -1;
        }

        /// <summary>Atlas cell of a digit in texture uv (v = 0 at the bottom row).</summary>
        public static void CellRect(int digit, out float u0, out float v0, out float u1, out float v1)
        {
            if (digit < 0) digit = 0; if (digit > 9) digit = 9;
            int col = digit % Columns, row = digit / Columns;   // row 0 = top
            u0 = col / (float)Columns; u1 = (col + 1) / (float)Columns;
            v1 = 1f - row / (float)Rows; v0 = v1 - 1f / Rows;
        }

        /// <summary>Horizontal centre (height units) of digit slot <paramref name="slot"/> (0 = tens or the only digit, 1 = ones) of a number with <paramref name="count"/> digits.</summary>
        public static float SlotCenter(int count, int slot)
        {
            if (count <= 1) return 0f;
            return (slot == 0 ? -0.5f : 0.5f) * Advance;
        }

        /// <summary>Total width of a number in height units.</summary>
        public static float Width(int number) { return DigitCount(number) * Advance; }

        /// <summary>From a point in the number's own frame (x to the right, y up, origin at the centre, 1 = the number height) to the atlas uv; false outside every digit cell.</summary>
        public static bool Sample(float x, float y, int number, out float atlasU, out float atlasV)
        {
            atlasU = atlasV = 0f;
            int tens, ones; Digits(number, out tens, out ones);
            int count = tens >= 0 ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                int digit = count == 1 ? ones : (i == 0 ? tens : ones);
                float lx = (x - SlotCenter(count, i)) / Advance + 0.5f, ly = y + 0.5f;
                if (lx < 0f || lx > 1f || ly < 0f || ly > 1f) continue;
                float u0, v0, u1, v1; CellRect(digit, out u0, out v0, out u1, out v1);
                atlasU = u0 + (u1 - u0) * lx; atlasV = v0 + (v1 - v0) * ly;
                return true;
            }
            return false;
        }

        /// <summary>Maps a texture uv point of the uniform to the number's frame for a placement (the inverse of how the shader positions the glyphs).</summary>
        public static void ToNumberSpace(NumberPlacement p, float u, float v, out float x, out float y)
        {
            float dx = u - p.CenterU, dy = v - p.CenterV;
            double s = Math.Sin(p.Rotation), c = Math.Cos(p.Rotation);
            float h = Math.Max(Math.Abs(p.Height), 1e-5f);
            x = (float)(c * dx + s * dy) / h;
            y = (float)(-s * dx + c * dy) / h;
            if (p.Mirrored) x = -x;
        }

        /// <summary>Convenience: texture uv -> atlas uv for a placement (false when the point is not on a glyph cell).</summary>
        public static bool Map(NumberPlacement p, int number, float u, float v, out float atlasU, out float atlasV)
        {
            float x, y; ToNumberSpace(p, u, v, out x, out y);
            return Sample(x, y, number, out atlasU, out atlasV);
        }
    }
}
