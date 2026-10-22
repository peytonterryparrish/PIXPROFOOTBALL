using System;

namespace PixFootball.Core
{
    /// <summary>
    /// The block-letter atlas used for player names on the back of the jersey and the team wordmark on the chest: 7 columns x 5 rows of cells (A-Z, then '.', '-', apostrophe, space).
    /// Shared by the generator (editor), the shader (PixLitHooks.hlsl PixTextMask) and the tests. Cells are <see cref="Advance"/> times as wide as the line height; text is centred
    /// on its placement and squeezed horizontally when it would be longer than the room it has.
    /// </summary>
    public static class TextAtlas
    {
        public const int Columns = 7, Rows = 5, MaxChars = 16;
        public const float Advance = 0.62f;
        public const int Blank = 29;

        /// <summary>Atlas cell of a character: A-Z 0..25, '.' 26, '-' 27, apostrophe 28, anything else (space) = <see cref="Blank"/>. Lower case maps to upper case.</summary>
        public static int Index(char c)
        {
            if (c >= 'a' && c <= 'z') c = (char)(c - 32);
            if (c >= 'A' && c <= 'Z') return c - 'A';
            switch (c) { case '.': return 26; case '-': return 27; case '\'': case '’': return 28; default: return Blank; }
        }

        /// <summary>Cleans a name for the jersey: trims, upper-cases, collapses spaces and cuts to <see cref="MaxChars"/>.</summary>
        public static string Clean(string s, int max = MaxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            bool lastSpace = true;
            foreach (char ch in s)
            {
                if (ch >= '0' && ch <= '9') continue;   // digits are not letters: dropped (the name stays readable), they must not turn into word gaps
                int i = Index(ch);
                if (i == Blank) { if (!lastSpace) { sb.Append(' '); lastSpace = true; } continue; }
                sb.Append(char.ToUpperInvariant(ch)); lastSpace = false;
                if (sb.Length >= max) break;
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Fills four vectors (16 slots) with the atlas index of each character, -1 for unused slots; returns the character count.</summary>
        public static int Pack(string s, float[] slots)
        {
            string t = Clean(s);
            for (int i = 0; i < MaxChars; i++) slots[i] = i < t.Length ? Index(t[i]) : -1f;
            return t.Length;
        }

        public static void CellRect(int index, out float u0, out float v0, out float u1, out float v1)
        {
            if (index < 0) index = 0; if (index >= Columns * Rows) index = Columns * Rows - 1;
            int col = index % Columns, row = index / Columns;   // row 0 = top
            u0 = col / (float)Columns; u1 = (col + 1) / (float)Columns;
            v1 = 1f - row / (float)Rows; v0 = v1 - 1f / Rows;
        }

        /// <summary>Squeeze factor (0..1] that makes <paramref name="count"/> characters of line height <paramref name="height"/> fit into <paramref name="maxWidth"/> (same units).</summary>
        public static float Squeeze(int count, float height, float maxWidth)
        {
            if (count <= 0 || height <= 0f) return 1f;
            float natural = count * Advance * height;
            return natural <= maxWidth ? 1f : Math.Max(0.35f, maxWidth / natural);
        }

        /// <summary>Total text width in height units for a given squeeze.</summary>
        public static float Width(int count, float squeeze = 1f) { return count * Advance * squeeze; }

        /// <summary>
        /// From a point in the text's own frame (x to the right, y up, origin at the centre of the line, 1 = the line height) to the atlas uv; false outside every character cell.
        /// </summary>
        public static bool Sample(float x, float y, string text, float squeeze, out float atlasU, out float atlasV)
        {
            atlasU = atlasV = 0f;
            string t = Clean(text);
            int n = t.Length; if (n == 0) return false;
            float adv = Advance * squeeze;
            float start = -0.5f * n * adv;
            float lx = (x - start) / adv;
            if (lx < 0f || lx >= n) return false;
            int i = (int)Math.Floor(lx);
            float fu = lx - i, fv = y + 0.5f;
            if (fv < 0f || fv > 1f) return false;
            int cell = Index(t[i]);
            if (cell == Blank) return false;
            float u0, v0, u1, v1; CellRect(cell, out u0, out v0, out u1, out v1);
            atlasU = u0 + (u1 - u0) * fu; atlasV = v0 + (v1 - v0) * fv;
            return true;
        }
    }
}
