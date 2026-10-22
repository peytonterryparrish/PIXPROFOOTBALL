using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>Tooltip: appears above the focused control after a short delay, hides on deselect. Plain text in a small glass bubble.</summary>
    public sealed class GlassTooltip : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerExitHandler
    {
        public string Text; public float Delay = 0.45f;
        GameObject _bubble; float _t = -1f;

        public static GlassTooltip Attach(Selectable target, string text, float delay = 0.45f)
        {
            var tt = UiKit.GetOrAdd<GlassTooltip>(target.gameObject);
            tt.Text = text; tt.Delay = delay;
            return tt;
        }

        public void OnSelect(BaseEventData e) { _t = 0f; }
        public void OnDeselect(BaseEventData e) { Hide(); }
        public void OnPointerExit(PointerEventData e) { }

        void Update()
        {
            if (_t < 0f) return;
            _t += Time.unscaledDeltaTime;
            if (_bubble == null && _t >= Delay && !string.IsNullOrEmpty(Text)) Show();
        }

        void Show()
        {
            var t = GlassTheme.T;
            var host = (RectTransform)transform;
            var g = GlassKit.Panel(host, "Tooltip", 1.9f, true, t.cornerRadius);
            _bubble = g.gameObject;
            var cv = _bubble.AddComponent<Canvas>(); cv.overrideSorting = true; var pc = GetComponentInParent<Canvas>(); cv.sortingOrder = (pc != null ? pc.sortingOrder : 0) + 9;
            GlassGraphic.Prepare(pc != null ? pc.rootCanvas : null);
            var tx = GlassKit.Text(g.rectTransform, Text, GlassText.Label, TextAlignmentOptions.Center, "Text"); GlassKit.Style(tx, 22f, t.textPrimary, false, 0f);
            tx.textWrappingMode = TextWrappingModes.Normal;
            float w = Mathf.Min(560f, tx.GetPreferredValues(Text, 520f, 0f).x + 44f);
            float h = tx.GetPreferredValues(Text, w - 44f, 0f).y + 30f;
            g.rectTransform.anchorMin = g.rectTransform.anchorMax = new Vector2(0.5f, 1f); g.rectTransform.pivot = new Vector2(0.5f, 0f); g.rectTransform.anchoredPosition = new Vector2(0f, 12f); g.rectTransform.sizeDelta = new Vector2(w, h);
            UiKit.Stretch(tx.rectTransform, 22f, 12f, 22f, 12f);
            var cg = _bubble.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
            _bubble.AddComponent<GlassReveal>().Duration = t.motionFast;
        }

        void Hide() { _t = -1f; if (_bubble != null) { Destroy(_bubble); _bubble = null; } }
        void OnDisable() { Hide(); }
    }
}

