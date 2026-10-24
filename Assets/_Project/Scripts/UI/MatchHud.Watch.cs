using PixFootball.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>v0.11 watch mode pieces of the match HUD: the user hide (Y), the small watch helper bar, the helper preference for the controller hint bar.</summary>
    public partial class MatchHud
    {
        bool _userHidden, _watchMode;
        OptionSelector _pauseHelper;

        public void SetHudUserHidden(bool hidden)
        {
            _userHidden = hidden;
            if (_canvas != null) _canvas.enabled = !hidden && _broadcastVisible;
        }

        public void SetWatchMode(bool on)
        {
            _watchMode = on;
            if (on) { _helperShowUntil = Time.unscaledTime + HudMetrics.HelperFirstSeconds; EnsureHelper(); }
            else if (_helper != null) _helper.gameObject.SetActive(false);
            WatchMode.HelperChanged -= OnHelperChanged; if (on) WatchMode.HelperChanged += OnHelperChanged;
        }

        void OnHelperChanged() { _helperShowUntil = Time.unscaledTime + HudMetrics.HelperActivitySeconds; if (!_watchMode && _hintBar != null && !string.IsNullOrEmpty(_hintRaw)) RenderHint(); }

        void OnDestroy() { WatchMode.HelperChanged -= OnHelperChanged; }

        // ============================================================================================ watch helper bar
        RectTransform _helper; CanvasGroup _helperGroup; RectTransform _helperRow; PromptFooter _helperFooter; float _helperShowUntil, _lastActivitySeen;

        void EnsureHelper()
        {
            if (_helper != null || _root == null) return;
            _helper = UiKit.Rect(_root, "WatchHelper");
            _helper.anchorMin = _helper.anchorMax = new Vector2(1f, 0f); _helper.pivot = new Vector2(1f, 0f);
            _helperGroup = _helper.gameObject.AddComponent<CanvasGroup>(); _helperGroup.blocksRaycasts = false; _helperGroup.interactable = false; _helperGroup.alpha = 0f;
            var bg = UiKit.Panel(_helper, "Bg", HudStyle.Tile); UiKit.Stretch(bg.rectTransform);
            var bar = UiKit.Panel(_helper, "Gold", HudStyle.Gold); bar.rectTransform.anchorMin = new Vector2(0f, 0f); bar.rectTransform.anchorMax = new Vector2(0f, 1f); bar.rectTransform.pivot = new Vector2(0f, 0.5f); bar.rectTransform.sizeDelta = new Vector2(4f, 0f); bar.rectTransform.anchoredPosition = Vector2.zero;
            var h = _helper.gameObject.AddComponent<HorizontalLayoutGroup>(); h.padding = new RectOffset(20, 16, 6, 6); h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var fit = _helper.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _helperRow = UiKit.Rect(_helper, "Row");
            var rl = _helperRow.gameObject.AddComponent<LayoutElement>(); rl.preferredHeight = HudMetrics.HelperGlyph; rl.minHeight = HudMetrics.HelperGlyph;
            _helperFooter = PromptFooter.Attach(_helperRow, HudMetrics.HelperGlyph, HudMetrics.MinFont, 20f);
            _helperFooter.Set(PromptFooter.Item("glyph_menu", "PAUSE"), PromptFooter.Item("glyph_view", "CAMERA"), PromptFooter.Item("glyph_y", "HUD"), PromptFooter.Item("glyph_x", "HELPER"), PromptFooter.Item("glyph_rb", "SPEED"));
            _helper.gameObject.SetActive(false);
            PlaceHelper();
        }

        void PlaceHelper()
        {
            if (_helper == null) return;
            float s = _hudMinimal && !_replayMode ? 0.66f : 1f;
            float inset = _bottomActive ? BarHeight * s + HudMetrics.BarLift : 0f;
            _helper.anchoredPosition = new Vector2(-HudMetrics.SafeX, inset + HudMetrics.Gap);
        }

        /// <summary>The helper bar of a watch game: Auto = for a few seconds at the start and after any press, On = always (small), Off = never.</summary>
        void TickHelper()
        {
            if (!_watchMode || _helper == null) return;
            var mode = WatchMode.Helper;
            if (InputDeviceTracker.LastActivityTime > _lastActivitySeen) { _lastActivitySeen = InputDeviceTracker.LastActivityTime; _helperShowUntil = Mathf.Max(_helperShowUntil, Time.unscaledTime + HudMetrics.HelperActivitySeconds); }
            bool allowed = mode != HelperBarMode.Off && _chromeVisible && !_replayMode && !ModalOpen && (_tossCaption == null || !_tossCaption.gameObject.activeSelf);
            bool show = allowed && (mode == HelperBarMode.On || Time.unscaledTime < _helperShowUntil);
            float a = Mathf.MoveTowards(_helperGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            _helperGroup.alpha = a;
            bool act = a > 0.01f || show;
            if (_helper.gameObject.activeSelf != act) { _helper.gameObject.SetActive(act); if (act) PlaceHelper(); }
        }
    }
}
