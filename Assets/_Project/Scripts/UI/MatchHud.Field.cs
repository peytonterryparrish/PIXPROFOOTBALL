using System.Collections;
using System.Collections.Generic;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>v0.7 field widgets of the match HUD: the read numbers at the route ends (hold LT), the marker over the player the user controls, and the whistle banner that says why a play ended.</summary>
    public partial class MatchHud
    {
        // ============================================================================================ route badges
        sealed class BadgeWidget { public RectTransform Rt; public Graphic Bg; public TextMeshProUGUI Text; }
        readonly List<BadgeWidget> _badgePool = new List<BadgeWidget>();

        BadgeWidget MakeBadge()
        {
            var w = new BadgeWidget();
            w.Rt = UiKit.Rect(_iconLayer, "RouteBadge"); w.Rt.anchorMin = w.Rt.anchorMax = new Vector2(0.5f, 0.5f); w.Rt.sizeDelta = new Vector2(56f, 56f);
            if (UiTheme.IsGlass) { var g = GlassKit.Solid(w.Rt, "Bg", Color.white, 28f, 2f); UiKit.Stretch(g.rectTransform); w.Bg = g; }
            else { var p = UiKit.Panel(w.Rt, "Bg", Color.white); UiKit.Stretch(p.rectTransform); w.Bg = p; }
            w.Text = UiKit.Label(w.Rt, "1", 32, Color.black, true, TextAlignmentOptions.Center, "Num"); UiKit.Stretch(w.Text.rectTransform);
            if (UiTheme.IsGlass) w.Text.font = GlassTheme.Current.Title;
            return w;
        }

        /// <summary>Read numbers (1 = first read) and RUN tags at the route ends; null or an empty list hides them.</summary>
        public void SetRouteBadges(IList<RouteOverlay.Badge> badges, Camera cam)
        {
            int n = badges == null || cam == null ? 0 : badges.Count;
            for (int i = 0; i < _badgePool.Count; i++) if (i >= n && _badgePool[i].Rt.gameObject.activeSelf) _badgePool[i].Rt.gameObject.SetActive(false);
            if (n == 0) return;
            while (_badgePool.Count < n) _badgePool.Add(MakeBadge());
            for (int i = 0; i < n; i++)
            {
                var b = badges[i]; var w = _badgePool[i];
                Vector3 sp = cam.WorldToScreenPoint(b.World + Vector3.up * 1.3f);
                bool ok = sp.z > 0f && !(b.Text.Length == 0);
                w.Rt.gameObject.SetActive(ok);
                if (!ok) continue;
                Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(_iconLayer, sp, null, out local);
                w.Rt.anchoredPosition = local;
                bool run = b.Run;
                Color fill = b.Hot ? new Color(1f, 0.62f, 0.2f, 1f) : (b.Primary ? new Color(0.97f, 0.97f, 0.95f, 1f) : new Color(0.09f, 0.1f, 0.12f, 0.92f));
                w.Bg.color = fill;
                w.Text.color = b.Hot || b.Primary ? new Color(0.08f, 0.09f, 0.11f) : Color.white;
                w.Text.text = b.Text;
                w.Text.fontSize = run ? 22f : 32f;
                w.Rt.sizeDelta = run ? new Vector2(92f, 44f) : new Vector2(56f, 56f);
                w.Rt.localScale = Vector3.one * (b.Primary || b.Hot ? 1.12f : 1f);
            }
        }

        // ============================================================================================ marker over the controlled player
        RectTransform _marker; TextMeshProUGUI _markerText; Graphic _markerPill; RectTransform _markerTip;
        bool _markerWanted;

        void BuildMarker()
        {
            _marker = UiKit.Rect(_iconLayer, "UserMarker"); _marker.anchorMin = _marker.anchorMax = new Vector2(0.5f, 0.5f); _marker.sizeDelta = new Vector2(190f, 84f); _marker.pivot = new Vector2(0.5f, 0f);
            Color accent = UiTheme.IsGlass ? GlassTheme.T.accent : new Color(1f, 0.78f, 0.22f, 1f);
            // the pointer: a diamond under the pill
            var tip = UiKit.Panel(_marker, "Tip", accent); tip.rectTransform.sizeDelta = new Vector2(26f, 26f); tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            tip.rectTransform.anchoredPosition = new Vector2(0f, 6f); tip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f); _markerTip = tip.rectTransform;
            var pill = UiTheme.IsGlass ? (Graphic)GlassKit.Solid(_marker, "Pill", accent, 22f, 2f) : UiKit.Panel(_marker, "Pill", accent);
            pill.rectTransform.anchorMin = new Vector2(0.5f, 0f); pill.rectTransform.anchorMax = new Vector2(0.5f, 0f); pill.rectTransform.pivot = new Vector2(0.5f, 0f);
            pill.rectTransform.anchoredPosition = new Vector2(0f, 18f); pill.rectTransform.sizeDelta = new Vector2(190f, 50f); _markerPill = pill;
            _markerText = UiKit.Label(pill.rectTransform, "YOU", 28, new Color(0.08f, 0.09f, 0.11f), true, TextAlignmentOptions.Center, "Label"); UiKit.Stretch(_markerText.rectTransform);
            if (UiTheme.IsGlass) _markerText.font = GlassTheme.Current.Title;
            _markerText.characterSpacing = 2f;
            _marker.gameObject.SetActive(false);
        }

        /// <summary>The marker above the player the user controls (offense and defense, switches with the player at once). Always drawn over the field, so no body or camera angle can hide it.</summary>
        public void SetUserMarker(bool visible, Vector3 world, Camera cam, string label)
        {
            if (_marker == null) BuildMarker();
            _markerWanted = visible && cam != null;
            if (!_markerWanted || !_chromeVisible || _replayMode || _hudMinimal || _callShown || PauseOpen || _tossRoot != null || _finalRoot != null || (_penalty != null && _penalty.IsOpen)) { if (_marker.gameObject.activeSelf) _marker.gameObject.SetActive(false); return; }
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) { _marker.gameObject.SetActive(false); return; }
            Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(_iconLayer, sp, null, out local);
            Rect r = _iconLayer.rect;
            local.x = Mathf.Clamp(local.x, r.xMin + 110f, r.xMax - 110f); local.y = Mathf.Clamp(local.y, r.yMin + 190f, r.yMax - 200f);
            local.y += Mathf.Sin(Time.unscaledTime * 5f) * 5f;
            _marker.anchoredPosition = local;
            if (_markerText.text != label) _markerText.text = label;
            if (!_marker.gameObject.activeSelf) _marker.gameObject.SetActive(true);
        }

        // ============================================================================================ whistle banner
        RectTransform _whistle; TextMeshProUGUI _whistleText; CanvasGroup _whistleGroup; Coroutine _whistleCo;

        void BuildWhistle()
        {
            _whistle = UiKit.Rect(_root, "Whistle"); _whistle.anchorMin = _whistle.anchorMax = new Vector2(0.5f, 1f); _whistle.pivot = new Vector2(0.5f, 1f);
            _whistle.anchoredPosition = new Vector2(0f, -176f); _whistle.sizeDelta = new Vector2(640f, 46f);
            _whistleGroup = _whistle.gameObject.AddComponent<CanvasGroup>(); _whistleGroup.blocksRaycasts = false; _whistleGroup.interactable = false;
            Graphic bg = UiKit.Panel(_whistle, "Bg", HudStyle.Tile);   // v0.11: flat tile, same as the banner
            UiKit.Stretch(bg.rectTransform);
            var rail = UiKit.Panel(_whistle, "Rail", HudStyle.Gold);
            UiKit.Anchor(rail.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(5f, 46f));
            _whistleText = UiKit.Label(_whistle, "", 24, Color.white, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(_whistleText.rectTransform, 24f, 0f, 16f, 0f);
            _whistleText.characterSpacing = 2f; UiKit.Fit(_whistleText, HudMetrics.MinFont);
            if (UiTheme.IsGlass) _whistleText.font = GlassTheme.Current.Title;
            _whistle.gameObject.SetActive(false);
        }

        /// <summary>The whistle: why the play ended ("TACKLED BY #52", "OUT OF BOUNDS", "INCOMPLETE"), in a small pill under the scorebug for a couple of seconds.</summary>
        public void ShowWhistle(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (BannerSays(text)) return;   // v0.11 B-044: the banner already says it (headline or sub-line)
            seconds = Mathf.Min(seconds, 1.8f);
            if (_whistle == null) BuildWhistle();
            _whistleText.text = text.ToUpperInvariant();
            if (_whistleCo != null) StopCoroutine(_whistleCo);
            _whistleCo = StartCoroutine(WhistleRoutine(seconds));
        }

        IEnumerator WhistleRoutine(float seconds)
        {
            _whistle.gameObject.SetActive(true);
            _whistle.SetAsLastSibling(); if (_fade != null) _fade.transform.SetAsLastSibling();
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(Mathf.Min(t / 0.12f, (seconds - t) / 0.3f));
                _whistleGroup.alpha = a;
                // the call screen, replay and pause own the whole screen
                if (_replayMode || _callShown || PauseOpen) _whistleGroup.alpha = 0f;
                yield return null;
            }
            _whistle.gameObject.SetActive(false); _whistleCo = null;
        }
    }
}
