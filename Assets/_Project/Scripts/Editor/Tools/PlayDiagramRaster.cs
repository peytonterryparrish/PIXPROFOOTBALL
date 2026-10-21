using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using PixFootball.Core;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Engine-free software rasteriser for <see cref="PlayDiagram"/>s: draws play cards into an RGBA buffer and encodes PNG.
    /// Used by the playbook atlas editor tool (and a headless harness) to review every play's routes and blocks.
    /// </summary>
    public sealed class Raster
    {
        public readonly int W, H;
        public readonly byte[] Px;
        public Raster(int w, int h, uint bg) { W = w; H = h; Px = new byte[w * h * 4]; Clear(bg); }

        public static uint Rgb(int r, int g, int b, int a = 255) { return (uint)((a << 24) | (r << 16) | (g << 8) | b); }

        public void Clear(uint c) { for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) Set(x, y, c); }

        public void Set(int x, int y, uint c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = (y * W + x) * 4;
            int a = (int)(c >> 24) & 255;
            if (a == 255) { Px[i] = (byte)((c >> 16) & 255); Px[i + 1] = (byte)((c >> 8) & 255); Px[i + 2] = (byte)(c & 255); Px[i + 3] = 255; return; }
            float t = a / 255f;
            Px[i] = (byte)(Px[i] * (1 - t) + ((c >> 16) & 255) * t); Px[i + 1] = (byte)(Px[i + 1] * (1 - t) + ((c >> 8) & 255) * t); Px[i + 2] = (byte)(Px[i + 2] * (1 - t) + (c & 255) * t); Px[i + 3] = 255;
        }

        public void Rect(int x, int y, int w, int h, uint c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Set(i, j, c); }

        public void Disc(float cx, float cy, float r, uint c)
        {
            for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++) for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                { float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); if (d <= r) Set(x, y, c); }
        }

        public void Ring(float cx, float cy, float r, float th, uint c)
        {
            for (int y = (int)(cy - r - th); y <= cy + r + th; y++) for (int x = (int)(cx - r - th); x <= cx + r + th; x++)
                { float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); if (Math.Abs(d - r) <= th * 0.5f) Set(x, y, c); }
        }

        public void Ellipse(float cx, float cy, float rx, float ry, uint c)
        {
            for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++) for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
                { float u = (x - cx) / rx, v = (y - cy) / ry; if (u * u + v * v <= 1f) Set(x, y, c); }
        }

        public void Line(float x0, float y0, float x1, float y1, float th, uint c, bool dashed = false)
        {
            float dx = x1 - x0, dy = y1 - y0; float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) { Disc(x0, y0, th * 0.5f, c); return; }
            float step = Math.Max(0.5f, th * 0.3f);
            int n = (int)(len / step) + 1;
            float dash = th * 3.2f;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n; float d = t * len;
                if (dashed && ((int)(d / dash) & 1) == 1) continue;
                Disc(x0 + dx * t, y0 + dy * t, th * 0.5f, c);
            }
        }

        // ---- 5x7 font ----
        static readonly Dictionary<char, string[]> Font = BuildFont();
        static Dictionary<char, string[]> BuildFont()
        {
            var f = new Dictionary<char, string[]>();
            Action<char, string> add = (ch, s) => f[ch] = s.Split('/');
            add('A', "01110/10001/10001/11111/10001/10001/10001"); add('B', "11110/10001/10001/11110/10001/10001/11110");
            add('C', "01111/10000/10000/10000/10000/10000/01111"); add('D', "11110/10001/10001/10001/10001/10001/11110");
            add('E', "11111/10000/10000/11110/10000/10000/11111"); add('F', "11111/10000/10000/11110/10000/10000/10000");
            add('G', "01111/10000/10000/10011/10001/10001/01111"); add('H', "10001/10001/10001/11111/10001/10001/10001");
            add('I', "11111/00100/00100/00100/00100/00100/11111"); add('J', "00111/00010/00010/00010/00010/10010/01100");
            add('K', "10001/10010/10100/11000/10100/10010/10001"); add('L', "10000/10000/10000/10000/10000/10000/11111");
            add('M', "10001/11011/10101/10101/10001/10001/10001"); add('N', "10001/11001/10101/10011/10001/10001/10001");
            add('O', "01110/10001/10001/10001/10001/10001/01110"); add('P', "11110/10001/10001/11110/10000/10000/10000");
            add('Q', "01110/10001/10001/10001/10101/10010/01101"); add('R', "11110/10001/10001/11110/10100/10010/10001");
            add('S', "01111/10000/10000/01110/00001/00001/11110"); add('T', "11111/00100/00100/00100/00100/00100/00100");
            add('U', "10001/10001/10001/10001/10001/10001/01110"); add('V', "10001/10001/10001/10001/10001/01010/00100");
            add('W', "10001/10001/10001/10101/10101/11011/10001"); add('X', "10001/10001/01010/00100/01010/10001/10001");
            add('Y', "10001/10001/01010/00100/00100/00100/00100"); add('Z', "11111/00001/00010/00100/01000/10000/11111");
            add('0', "01110/10001/10011/10101/11001/10001/01110"); add('1', "00100/01100/00100/00100/00100/00100/01110");
            add('2', "01110/10001/00001/00010/00100/01000/11111"); add('3', "11110/00001/00001/01110/00001/00001/11110");
            add('4', "00010/00110/01010/10010/11111/00010/00010"); add('5', "11111/10000/11110/00001/00001/10001/01110");
            add('6', "00110/01000/10000/11110/10001/10001/01110"); add('7', "11111/00001/00010/00100/01000/01000/01000");
            add('8', "01110/10001/10001/01110/10001/10001/01110"); add('9', "01110/10001/10001/01111/00001/00010/01100");
            add('-', "00000/00000/00000/11111/00000/00000/00000"); add('&', "01100/10010/10100/01000/10101/10010/01101");
            add('/', "00001/00010/00010/00100/01000/01000/10000"); add('.', "00000/00000/00000/00000/00000/01100/01100");
            add('(', "00010/00100/01000/01000/01000/00100/00010"); add(')', "01000/00100/00010/00010/00010/00100/01000");
            add(' ', "00000/00000/00000/00000/00000/00000/00000"); add('+', "00000/00100/00100/11111/00100/00100/00000");
            add('\'', "00100/00100/00000/00000/00000/00000/00000"); add(':', "00000/01100/01100/00000/01100/01100/00000");
            return f;
        }

        public int TextWidth(string s, int scale) { return s.Length * 6 * scale; }

        public void Text(string s, int x, int y, int scale, uint c)
        {
            s = s.ToUpperInvariant();
            foreach (char ch in s)
            {
                string[] g; if (!Font.TryGetValue(ch, out g)) g = Font[' '];
                for (int r = 0; r < 7; r++) for (int col = 0; col < 5; col++)
                    if (g[r][col] == '1') Rect(x + col * scale, y + r * scale, scale, scale, c);
                x += 6 * scale;
            }
        }

        // ---- PNG ----
        public byte[] EncodePng()
        {
            var raw = new MemoryStream();
            for (int y = 0; y < H; y++) { raw.WriteByte(0); raw.Write(Px, y * W * 4, W * 4); }
            byte[] data = raw.ToArray();
            var ms = new MemoryStream();
            ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            var ihdr = new MemoryStream();
            WriteInt(ihdr, W); WriteInt(ihdr, H); ihdr.WriteByte(8); ihdr.WriteByte(6); ihdr.WriteByte(0); ihdr.WriteByte(0); ihdr.WriteByte(0);
            Chunk(ms, "IHDR", ihdr.ToArray());
            var z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x9C);
            using (var ds = new DeflateStream(z, CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (byte v in data) { a = (a + v) % 65521; b = (b + a) % 65521; }
            WriteInt(z, (int)((b << 16) | a));
            Chunk(ms, "IDAT", z.ToArray());
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        static void WriteInt(Stream s, int v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

        static uint[] _crcTable;
        static uint Crc(byte[] buf, int off, int len, uint crc)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _crcTable[n] = c; }
            }
            for (int i = 0; i < len; i++) crc = _crcTable[(crc ^ buf[off + i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            WriteInt(s, data.Length);
            byte[] t = new byte[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            s.Write(t, 0, 4); s.Write(data, 0, data.Length);
            uint crc = 0xFFFFFFFFu; crc = Crc(t, 0, 4, crc); crc = Crc(data, 0, data.Length, crc);
            WriteInt(s, (int)(crc ^ 0xFFFFFFFFu));
        }
    }

    /// <summary>Draws a <see cref="PlayDiagram"/> as a card onto a <see cref="Raster"/>.</summary>
    public static class PlayCardPainter
    {
        static uint InkColor(InkKind k)
        {
            switch (k)
            {
                case InkKind.PrimaryRoute: return Raster.Rgb(255, 200, 55);
                case InkKind.HotRoute: return Raster.Rgb(255, 120, 40);
                case InkKind.Route: return Raster.Rgb(235, 240, 255);
                case InkKind.Run: return Raster.Rgb(250, 120, 5);
                case InkKind.Fake: return Raster.Rgb(250, 160, 80);
                case InkKind.Block: return Raster.Rgb(120, 190, 255);
                case InkKind.Handoff: return Raster.Rgb(250, 120, 5);
                case InkKind.Dropback: return Raster.Rgb(170, 175, 190);
                case InkKind.Blitz: return Raster.Rgb(235, 40, 40);
                case InkKind.Rush: return Raster.Rgb(255, 120, 120);
                case InkKind.Man: return Raster.Rgb(255, 234, 0);
                case InkKind.Spy: return Raster.Rgb(200, 120, 255);
                case InkKind.Kick: return Raster.Rgb(17, 240, 85);
                default: return Raster.Rgb(120, 120, 120);
            }
        }

        public static uint ZoneColor(DiagramZone z)
        {
            if (z.Deep) return Raster.Rgb(80, 110, 255, 90);
            return Raster.Rgb(40, 220, 170, 90);
        }

        /// <summary>Paints the diagram into the rectangle (x, y, w, h) of the raster.</summary>
        public static void Paint(Raster r, PlayDiagram d, int x, int y, int w, int h)
        {
            r.Rect(x, y, w, h, Raster.Rgb(20, 32, 24));
            float sx = w / (d.MaxX - d.MinX), sy = h / (d.MaxY - d.MinY);
            float s = Math.Min(sx, sy);
            Func<float, float> px = ax => x + (ax - d.MinX) * s + (w - (d.MaxX - d.MinX) * s) * 0.5f;
            Func<float, float> py = ay => y + h - ((ay - d.MinY) * s + (h - (d.MaxY - d.MinY) * s) * 0.5f);
            // yard lines every 5 yards, line of scrimmage
            for (float yy = (float)Math.Ceiling(d.MinY / 5f) * 5f; yy <= d.MaxY; yy += 5f)
                r.Line(px(d.MinX), py(yy), px(d.MaxX), py(yy), 1f, Raster.Rgb(46, 66, 50));
            r.Line(px(d.MinX), py(0f), px(d.MaxX), py(0f), 2f, Raster.Rgb(60, 110, 255));
            foreach (DiagramZone z in d.Zones)
            {
                float cx = px(z.CenterX), cy = py(z.CenterY);
                r.Ellipse(cx, cy, Math.Max(6f, Math.Min(z.Width, 22f) * 0.5f * s), Math.Max(6f, z.Height * 0.5f * s), ZoneColor(z));
            }
            foreach (DiagramStroke st in d.Strokes)
            {
                if (st.Points.Count < 2) continue;
                uint c = InkColor(st.Ink);
                float th = st.Thick ? Math.Max(3f, s * 0.42f) : Math.Max(2f, s * 0.26f);
                for (int i = 0; i + 1 < st.Points.Count; i++)
                    r.Line(px(st.Points[i].X), py(st.Points[i].Y), px(st.Points[i + 1].X), py(st.Points[i + 1].Y), th, c, st.Dashed);
                Vec2 a = st.Points[st.Points.Count - 2], b = st.Points[st.Points.Count - 1];
                float ex = px(b.X), ey = py(b.Y), bx = px(a.X), by = py(a.Y);
                float dx = ex - bx, dy = ey - by; float len = (float)Math.Sqrt(dx * dx + dy * dy); if (len < 0.01f) continue;
                dx /= len; dy /= len;
                if (st.TEnd)
                    r.Line(ex - dy * s * 0.9f, ey + dx * s * 0.9f, ex + dy * s * 0.9f, ey - dx * s * 0.9f, th * 1.1f, c);
                else if (st.Arrow)
                {
                    float hl = s * 1.3f, hw = s * 0.7f;
                    r.Line(ex, ey, ex - dx * hl - dy * hw, ey - dy * hl + dx * hw, th, c);
                    r.Line(ex, ey, ex - dx * hl + dy * hw, ey - dy * hl - dx * hw, th, c);
                }
            }
            foreach (DiagramPlayer p in d.Players)
            {
                float cx = px(p.X), cy = py(p.Y); float rad = Math.Max(5f, s * 0.85f);
                if (p.Ghost) { r.Line(cx - rad * 0.6f, cy - rad * 0.6f, cx + rad * 0.6f, cy + rad * 0.6f, 2f, Raster.Rgb(150, 150, 150)); r.Line(cx - rad * 0.6f, cy + rad * 0.6f, cx + rad * 0.6f, cy - rad * 0.6f, 2f, Raster.Rgb(150, 150, 150)); continue; }
                bool line = PositionGroup.IsOffensiveLine(p.Position);
                uint fill = p.Defense ? Raster.Rgb(190, 40, 50) : (line ? Raster.Rgb(70, 74, 92) : (p.Highlight ? Raster.Rgb(250, 120, 5) : Raster.Rgb(34, 34, 36)));
                if (line && !p.Defense) r.Rect((int)(cx - rad * 0.9f), (int)(cy - rad * 0.9f), (int)(rad * 1.8f), (int)(rad * 1.8f), fill);
                else r.Disc(cx, cy, rad, fill);
                r.Ring(cx, cy, rad, 1.6f, Raster.Rgb(255, 200, 55));
                if (p.Label.Length > 0 && !p.Defense == false || p.Label.Length > 0) { int sc = rad > 9 ? 2 : 1; r.Text(p.Label, (int)(cx - p.Label.Length * 3 * sc), (int)(cy - 3.5f * sc), sc, Raster.Rgb(255, 255, 255)); }
            }
        }

        /// <summary>Atlas pages: 4 x 3 cards per page with the play name, formation and category under each diagram.</summary>
        public static List<byte[]> Atlas(Playbook book, IList<Play> plays, out List<string> pageNotes)
        {
            const int cols = 4, rows = 3, cw = 460, ch = 460, pad = 16, label = 70;
            int perPage = cols * rows;
            var pages = new List<byte[]>(); pageNotes = new List<string>();
            for (int start = 0; start < plays.Count; start += perPage)
            {
                var r = new Raster(cols * (cw + pad) + pad, rows * (ch + label + pad) + pad, Raster.Rgb(12, 14, 20));
                string note = "";
                for (int i = 0; i < perPage && start + i < plays.Count; i++)
                {
                    Play p = plays[start + i];
                    int cx = pad + (i % cols) * (cw + pad), cy = pad + (i / cols) * (ch + label + pad);
                    PlayDiagram d = PlayDiagram.For(book, p);
                    Paint(r, d, cx, cy, cw, ch);
                    r.Text(p.Name, cx + 4, cy + ch + 8, 3, Raster.Rgb(255, 255, 255));
                    r.Text(p.FormationName + " " + p.Category, cx + 4, cy + ch + 40, 2, Raster.Rgb(255, 200, 55));
                    note += (start + i) + ":" + p.FormationName + "/" + p.Name + "  ";
                }
                pages.Add(r.EncodePng()); pageNotes.Add(note);
            }
            return pages;
        }
    }
}

