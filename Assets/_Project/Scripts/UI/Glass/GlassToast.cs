using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>Toast: a small glass pill that slides up, stays for its time and fades. One toast at a time (a new one replaces the old).</summary>
    public sealed class GlassToast : MonoBehaviour
    {
        public RectTransform Root; public TextMeshProUGUI Label; public Image Icon; public CanvasGroup Group;
        float _t, _dur; bool _active; Vector2 _rest;

        public static GlassToast Create(Transform parent, float bottom = 150f)
        {
            var t = GlassTheme.T;
            var g = GlassKit.Panel(parent, "Toast", 1.5f, true, GlassKit.Rad(22f));
            var rt = g.rectTransform; UiKit.Anchor(rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, bottom), new Vector2(560f, 64f));
            var toast = g.gameObject.AddComponent<GlassToast>(); toast.Root = rt;
            if (t.flat) { var bar = GlassKit.Bar(rt, "GoldBar", t.gold, 6f, 64f); bar.rectTransform.anchorMin = new Vector2(0f, 0f); bar.rectTransform.anchorMax = new Vector2(0f, 1f); bar.rectTransform.pivot = new Vector2(0f, 0.5f); bar.rectTransform.anchoredPosition = Vector2.zero; bar.rectTransform.sizeDelta = new Vector2(6f, 0f); }
            toast.Group = g.gameObject.AddComponent<CanvasGroup>(); toast.Group.blocksRaycasts = false; toast.Group.interactable = false;
            toast.Icon = GlassKit.Icon(rt, "ui_info", 28f, t.accent);
            UiKit.Anchor(toast.Icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(28f, 28f));
            toast.Label = GlassKit.Text(rt, "", GlassText.Label, TextAlignmentOptions.MidlineLeft, "Text"); GlassKit.Style(toast.Label, 24f, t.textPrimary, true, 1.5f);
            UiKit.Stretch(toast.Label.rectTransform, 68f, 0f, 24f, 0f); UiKit.Fit(toast.Label, 16f);
            toast._rest = rt.anchoredPosition;
            g.gameObject.SetActive(false);
            return toast;
        }

        /// <summary>Edit-mode tools (no Update loop): show the toast fully visible at rest.</summary>
        public void ShowNow(string text, string iconKey = "ui_info")
        {
            Show(text, 999f, iconKey); Group.alpha = 1f; Root.anchoredPosition = _rest;
        }

        public void SetRest(Vector2 p) { _rest = p; Root.anchoredPosition = p; }

        public void Show(string text, float seconds, string iconKey = "ui_info")
        {
            gameObject.SetActive(true);
            Label.text = text; _dur = Mathf.Max(0.6f, seconds); _t = 0f; _active = true;
            var s = GlassIcons.Get(iconKey); Icon.sprite = s; Icon.enabled = s != null;
            Apply(0f);
        }

        void Update()
        {
            if (!_active) return;
            _t += Time.unscaledDeltaTime;
            float inT = GlassTheme.T.motionSlow, outT = GlassTheme.T.motionSlow;
            float a = _t < inT ? _t / inT : _t > _dur - outT ? Mathf.Clamp01((_dur - _t) / outT) : 1f;
            Apply(a);
            if (_t >= _dur) { _active = false; gameObject.SetActive(false); }
        }

        void Apply(float a)
        {
            float e = a * a * (3f - 2f * a);
            Group.alpha = e;
            Root.anchoredPosition = _rest + new Vector2(0f, (1f - e) * -22f);
        }
    }
}

