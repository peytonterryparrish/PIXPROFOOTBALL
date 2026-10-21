using System;
using System.Collections.Generic;
using PixFootball.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// Draws a <see cref="PlayDiagram"/> (routes as lines with arrows, blocks as T's, run paths, QB action, zone bubbles, blitz arrows,
    /// position chips) as a UI mesh inside its rect, plus small TMP labels on the chips. Used by the play-call cards and previews.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PlayDiagramGraphic : MaskableGraphic
    {
        PlayDiagram _d;
        readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        Color _field = new Color(0.07f, 0.11f, 0.09f, 1f);
        public bool ShowField = true;
        public float LabelScale = 1f;

        public static readonly Color Gold = new Color(1f, 0.784f, 0.216f), White = new Color(0.93f, 0.95f, 1f), Orange = new Color(0.98f, 0.47f, 0.02f);

        protected override void Awake() { base.Awake(); raycastTarget = false; }

        public void SetDiagram(PlayDiagram d) { _d = d; SetVerticesDirty(); RefreshLabels(); }

        protected override void OnRectTransformDimensionsChange() { base.OnRectTransformDimensionsChange(); RefreshLabels(); }

        static Color Ink(InkKind k)
        {
            switch (k)
            {
                case InkKind.PrimaryRoute: return Gold;
                case InkKind.HotRoute: return new Color(1f, 0.45f, 0.15f);
                case InkKind.Route: return White;
                case InkKind.Run: return Orange;
                case InkKind.Fake: return new Color(0.98f, 0.62f, 0.3f);
                case InkKind.Block: return new Color(0.47f, 0.75f, 1f);
                case InkKind.Handoff: return Orange;
                case InkKind.Dropback: return new Color(0.68f, 0.7f, 0.76f);
                case InkKind.Blitz: return new Color(0.95f, 0.18f, 0.18f);
                case InkKind.Rush: return new Color(1f, 0.45f, 0.45f);
                case InkKind.Man: return new Color(1f, 0.92f, 0f);
                case InkKind.Spy: return new Color(0.78f, 0.47f, 1f);
                case InkKind.Kick: return new Color(0.07f, 0.94f, 0.33f);
                default: return Color.gray;
            }
        }

        // ---- mapping ----
        float _scale, _vMinY, _vMaxY; Vector2 _origin; Rect _rect;
        Vector2 P(float x, float y) { return new Vector2(_origin.x + (x - _d.MinX) * _scale, _origin.y + (y - _vMinY) * _scale); }

        void Layout()
        {
            _rect = GetPixelAdjustedRect();
            _vMinY = _d.MinY; _vMaxY = _d.MaxY;
            // non-square cards: keep the full width in view and let the visible depth follow the card height (deep routes run off the top)
            if (_d.Side != PlaySide.Special && _rect.width > 1f) _vMaxY = _d.MinY + (_d.MaxX - _d.MinX) * _rect.height / _rect.width;
            float sx = _rect.width / (_d.MaxX - _d.MinX), sy = _rect.height / (_vMaxY - _vMinY);
            _scale = Mathf.Min(sx, sy);
            _origin = new Vector2(_rect.x + (_rect.width - (_d.MaxX - _d.MinX) * _scale) * 0.5f, _rect.y + (_rect.height - (_vMaxY - _vMinY) * _scale) * 0.5f);
        }

        // ---- mesh helpers ----
        VertexHelper _vh;
        void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int i = _vh.currentVertCount;
            _vh.AddVert(a, col, Vector2.zero); _vh.AddVert(b, col, Vector2.zero); _vh.AddVert(c, col, Vector2.zero);
            _vh.AddTriangle(i, i + 1, i + 2);
        }
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) { Tri(a, b, c, col); Tri(a, c, d, col); }
        void RectFill(float x0, float y0, float x1, float y1, Color col) { Quad(new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1), col); }
        void Seg(Vector2 a, Vector2 b, float w, Color col)
        {
            Vector2 d = b - a; float l = d.magnitude; if (l < 0.01f) return;
            Vector2 n = new Vector2(-d.y, d.x) / l * (w * 0.5f);
            Quad(a - n, a + n, b + n, b - n, col);
        }
        void Disc(Vector2 c, float r, Color col, int segs = 18)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Tri(c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, col);
            }
        }
        void Ellipse(Vector2 c, float rx, float ry, Color col, int segs = 28)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Tri(c, c + new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry), c + new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry), col);
            }
        }
        void Ring(Vector2 c, float r, float w, Color col, int segs = 22)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Seg(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, w, col);
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_d == null) return;
            _vh = vh;
            Layout();
            float u = _scale;   // pixels per yard
            if (ShowField)
            {
                RectFill(_rect.xMin, _rect.yMin, _rect.xMax, _rect.yMax, _field);
                Color yl = new Color(0.20f, 0.28f, 0.22f, 1f);
                for (float y = Mathf.Ceil(_vMinY / 5f) * 5f; y <= _vMaxY; y += 5f)
                { Vector2 p = P(_d.MinX, y); RectFill(_rect.xMin, p.y - 1f, _rect.xMax, p.y + 1f, yl); }
                // hash ticks
                for (float y = Mathf.Ceil(_vMinY); y <= _vMaxY; y += 1f)
                {
                    Vector2 a = P(-3.1f, y), b = P(3.1f, y);
                    RectFill(a.x - 1f, a.y - 0.5f, a.x + 3f, a.y + 0.5f, yl); RectFill(b.x - 3f, b.y - 0.5f, b.x + 1f, b.y + 0.5f, yl);
                }
                Vector2 los = P(_d.MinX, 0f);
                RectFill(_rect.xMin, los.y - 2.2f, _rect.xMax, los.y + 2.2f, new Color(0.24f, 0.45f, 1f, 1f));
            }
            foreach (DiagramZone z in _d.Zones)
            {
                Vector2 c = P(z.CenterX, z.CenterY);
                float rx = Mathf.Max(6f, Mathf.Min(z.Width, 26f) * 0.5f * u), ry = Mathf.Max(6f, z.Height * 0.5f * u);
                Color zc = z.Deep ? new Color(0.31f, 0.43f, 1f, 0.38f) : new Color(0.16f, 0.86f, 0.67f, 0.38f);
                Ellipse(c, rx, ry, zc);
                Ring(c, Mathf.Min(rx, ry) * 0.2f, 0.01f, zc);
            }
            foreach (DiagramStroke s in _d.Strokes) DrawStroke(s, u);
            foreach (DiagramPlayer p in _d.Players) DrawPlayer(p, u);
            _vh = null;
        }

        Vector2 SP(DiagramStroke s, int i) { return P(s.Points[i].X, Mathf.Min(s.Points[i].Y, _d.Side == PlaySide.Special ? 1e9f : _vMaxY - 1.4f)); }

        void DrawStroke(DiagramStroke s, float u)
        {
            if (s.Points.Count < 2) return;
            Color c = Ink(s.Ink);
            float w = s.Thick ? Mathf.Max(4f, u * 0.5f) : Mathf.Max(3f, u * 0.3f);
            for (int i = 0; i + 1 < s.Points.Count; i++)
            {
                Vector2 a = SP(s, i), b = SP(s, i + 1);
                if (s.Dashed)
                {
                    Vector2 d = b - a; float l = d.magnitude; if (l < 0.5f) continue; Vector2 dir = d / l;
                    float dash = w * 2.6f, gap = w * 1.8f;
                    for (float t = 0f; t < l; t += dash + gap) Seg(a + dir * t, a + dir * Mathf.Min(l, t + dash), w, c);
                }
                else Seg(a, b, w, c);
                if (i > 0 && !s.Dashed) Disc(a, w * 0.5f, c, 8);
            }
            Vector2 pa = SP(s, s.Points.Count - 2), pb = SP(s, s.Points.Count - 1);
            Vector2 dd = pb - pa; float len = dd.magnitude; if (len < 0.5f) return;
            Vector2 dn = dd / len, nn = new Vector2(-dn.y, dn.x);
            if (s.TEnd) { Seg(pb - nn * u * 0.95f, pb + nn * u * 0.95f, w * 1.25f, c); }
            else if (s.Arrow)
            {
                float hl = Mathf.Max(10f, u * 1.7f), hw = Mathf.Max(6f, u * 0.95f);
                Tri(pb, pb - dn * hl + nn * hw, pb - dn * hl - nn * hw, c);
            }
        }

        void DrawPlayer(DiagramPlayer p, float u)
        {
            Vector2 c = P(p.X, p.Y);
            float r = Mathf.Max(8f, u * 1.05f);
            if (p.Ghost)
            {
                Color g = new Color(0.62f, 0.62f, 0.66f, 0.8f); float k = r * 0.55f;
                Seg(c + new Vector2(-k, -k), c + new Vector2(k, k), 3f, g); Seg(c + new Vector2(-k, k), c + new Vector2(k, -k), 3f, g); return;
            }
            bool line = PositionGroup.IsOffensiveLine(p.Position);
            Color fill = p.Defense ? new Color(0.76f, 0.16f, 0.2f, 1f) : (line ? new Color(0.28f, 0.29f, 0.36f, 1f) : (p.Highlight ? Orange : new Color(0.13f, 0.13f, 0.15f, 1f)));
            if (line && !p.Defense) RectFill(c.x - r * 0.92f, c.y - r * 0.92f, c.x + r * 0.92f, c.y + r * 0.92f, fill);
            else Disc(c, r, fill);
            if (line && !p.Defense) { Seg(new Vector2(c.x - r * 0.92f, c.y - r * 0.92f), new Vector2(c.x + r * 0.92f, c.y - r * 0.92f), 2f, Gold); Seg(new Vector2(c.x - r * 0.92f, c.y + r * 0.92f), new Vector2(c.x + r * 0.92f, c.y + r * 0.92f), 2f, Gold); Seg(new Vector2(c.x - r * 0.92f, c.y - r * 0.92f), new Vector2(c.x - r * 0.92f, c.y + r * 0.92f), 2f, Gold); Seg(new Vector2(c.x + r * 0.92f, c.y - r * 0.92f), new Vector2(c.x + r * 0.92f, c.y + r * 0.92f), 2f, Gold); }
            else Ring(c, r, 2.4f, Gold);
        }

        // ---- labels ----
        void RefreshLabels()
        {
            int n = 0;
            if (_d != null && _rect.width >= 0f)
            {
                Layout();
                foreach (DiagramPlayer p in _d.Players)
                {
                    if (p.Ghost || string.IsNullOrEmpty(p.Label)) continue;
                    var t = Label(n++);
                    t.gameObject.SetActive(true);
                    Vector2 c = P(p.X, p.Y);
                    var rt = t.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = c - (Vector2)rectTransform.rect.center;
                    rt.sizeDelta = new Vector2(_scale * 3f, _scale * 2f);
                    t.fontSize = Mathf.Max(9f, _scale * 1.05f) * LabelScale;
                    t.text = p.Label;
                }
            }
            for (int i = n; i < _labels.Count; i++) _labels[i].gameObject.SetActive(false);
        }

        TextMeshProUGUI Label(int i)
        {
            while (_labels.Count <= i)
            {
                var t = UiKit.Label(transform, "", 12f, Color.white, true, TextAlignmentOptions.Center, "Chip");
                t.margin = Vector4.zero;
                _labels.Add(t);
            }
            return _labels[i];
        }
    }
}


