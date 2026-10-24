using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Reskins a flat <see cref="Image"/> of the code-built Classic screens as glass without touching the screen's own code: the Image stays (the screen keeps reading and writing its colour,
    /// enabled state and size) but is made invisible, and a <see cref="GlassGraphic"/> child behind its content draws the surface. The surface follows the Image's colour every frame:
    /// dark panels become frosted glass, near-black ones dark wells, the accent colour becomes the orange focus edge (selected rows), saturated colours (team colours, status chips) stay flat tints.
    /// Thin bars, sprites, full-screen scrims and masks are left alone. Used by the play call screen, the pre-snap panel, the penalty panel, the TV overlays and the replay HUD.
    /// </summary>
    [DisallowMultipleComponent, ExecuteAlways]
    public sealed class GlassImageSkin : MonoBehaviour
    {
        public GlassSkinRole Role = GlassSkinRole.Auto;
        /// <summary>Corner radius override (negative = by size).</summary>
        public float Radius = -1f;

        Image _img;
        GlassGraphic _g;
        Color _lastColor = new Color(-1f, -1f, -1f, -1f);
        Vector2 _lastSize;
        GlassSkinRole _lastResolved = GlassSkinRole.Skip;
        bool _lastActive = true;
        Sprite _lastSprite;

        public static bool Eligible(Image img)
        {
            if (img == null || img.sprite != null) return false;
            if (img.type != Image.Type.Simple) return false;
            if (img.GetComponent<GlassGraphic>() != null) return false;
            if (img.GetComponent<Mask>() != null) return false;
            if (img.GetComponent<GlassNoSkin>() != null) return false;
            return true;
        }

        void Awake() { _img = GetComponent<Image>(); }

        void OnEnable()
        {
            if (_img == null) _img = GetComponent<Image>();
            EnsureChild();
            Refresh();
        }

        void OnDisable()
        {
            if (_g != null) _g.gameObject.SetActive(false);
            if (_img != null && _img.canvasRenderer != null) _img.canvasRenderer.SetAlpha(1f);
        }

        void EnsureChild()
        {
            if (_g != null) { _g.gameObject.SetActive(true); return; }
            var t = transform.Find("GlassSkin");
            if (t != null) _g = t.GetComponent<GlassGraphic>();
            if (_g == null)
            {
                var go = new GameObject("GlassSkin", typeof(RectTransform)); go.transform.SetParent(transform, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                _g = go.AddComponent<GlassGraphic>(); _g.raycastTarget = false;
                rt.SetAsFirstSibling();
            }
            _g.gameObject.SetActive(true);
        }

        /// <summary>Re-reads the Image now (edit-mode tools have no LateUpdate loop).</summary>
        public void Refresh()
        {
            if (_img == null) _img = GetComponent<Image>();
            if (_img == null) return;
            EnsureChild();
            _lastColor = new Color(-1f, -1f, -1f, -1f);
            LateUpdate();
        }

        void LateUpdate()
        {
            if (_img == null || _g == null) return;
            var r = ((RectTransform)transform).rect;
            var c = _img.color;
            bool active = _img.enabled;
            var sp = _img.sprite;
            if (c == _lastColor && r.size == _lastSize && active == _lastActive && sp == _lastSprite) { SetImageAlpha(); return; }   // a sprite assigned later (crests, glyphs) flips the role to Skip and the Image draws again
            _lastColor = c; _lastSize = r.size; _lastActive = active; _lastSprite = sp;
            Apply(active, c, r.size);
            SetImageAlpha();
        }

        // the Image itself never draws (unless the glass leaves it alone: sprites, thin bars); the glass child does. Applied after the role is resolved so edit-mode refreshes are right in one pass.
        void SetImageAlpha() { _img.canvasRenderer.SetAlpha(_lastResolved == GlassSkinRole.Skip ? 1f : 0f); }

        void Apply(bool active, Color c, Vector2 size)
        {
            var t = GlassTheme.T;
            var role = _img.sprite != null ? GlassSkinRole.Skip : (Role == GlassSkinRole.Auto ? Classify(c, size, name) : Role);   // a crest or glyph assigned later draws as the Image
            _lastResolved = role;
            if (!active || role == GlassSkinRole.Skip || c.a < 0.02f)
            {
                _g.gameObject.SetActive(false);
                return;
            }
            _g.gameObject.SetActive(true);
            float radius = Radius >= 0f ? Radius : Mathf.Min(t.cornerRadius, Mathf.Min(size.x, size.y) * 0.18f);
            _g.Radius = radius;
            _g.Shadow = 0f; _g.Focus = 0f;
            switch (role)
            {
                case GlassSkinRole.Well:
                    _g.Kind = GlassKind.Well; _g.Density = 1f; _g.color = new Color(1f, 1f, 1f, t.flat ? Mathf.Clamp01(c.a * 1.05f) : 1f); break;
                case GlassSkinRole.Glass:
                    _g.Kind = t.flat ? GlassKind.Panel : GlassKind.Glass; _g.Density = Mathf.Lerp(0.7f, 1.4f, Mathf.Clamp01(c.a)); _g.color = t.flat ? new Color(1f, 1f, 1f, Mathf.Clamp01(0.35f + c.a)) : Color.white; break;
                case GlassSkinRole.Selected:
                    if (t.flat) { _g.Kind = GlassKind.Row; _g.Density = 0.7f; _g.Focus = 1f; _g.EdgeWidth = 1f; _g.FocusEdge = 4f; _g.color = Color.white; break; }
                    _g.Kind = GlassKind.Glass; _g.Density = 1.2f; _g.Focus = 0.9f; _g.color = new Color(1f, 0.84f, 0.66f, 1f); break;
                default:
                    _g.Kind = GlassKind.Solid; _g.EdgeWidth = 0f; _g.Density = 1f; _g.color = c; break;
            }
            if (role != GlassSkinRole.Solid && !(t.flat && role == GlassSkinRole.Selected)) _g.EdgeWidth = -1f;
            _g.SetVerticesDirty();
        }

        /// <summary>Pure classification of a flat panel colour (EditMode tested).</summary>
        public static GlassSkinRole Classify(Color c, Vector2 size, string objectName)
        {
            if (c.a < 0.02f) return GlassSkinRole.Skip;
            string n = objectName ?? "";
            if (n.IndexOf("Scrim", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Fade", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Band", System.StringComparison.OrdinalIgnoreCase) >= 0) return GlassSkinRole.Skip;
            if (Mathf.Min(size.x, size.y) <= 12f) return GlassSkinRole.Skip;       // accent bars, rules, pips
            var t = GlassTheme.T;
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            float sat = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (IsAccent(c, t)) return GlassSkinRole.Selected;
            if (lum < 0.28f && sat < 0.16f)
            {
                if (lum < 0.06f && c.a >= 0.9f) return GlassSkinRole.Well;
                return GlassSkinRole.Glass;
            }
            if (c.a < 0.3f && sat < 0.1f) return GlassSkinRole.Solid;           // faint white veils (inactive tabs, play clock plates)
            return GlassSkinRole.Solid;
        }

        static bool IsAccent(Color c, GlassTokens t)
        {
            return Near(c, t.accent) || Near(c, t.accentHot) || Near(c, t.accentDeep);
        }

        static bool Near(Color a, Color b) { return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f; }
    }
}

