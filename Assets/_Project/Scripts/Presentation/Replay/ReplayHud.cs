using PixFootball.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.Presentation
{
    /// <summary>
    /// Replay user interface in the UI kit style: cinematic letterbox, the "INSTANT REPLAY" tag with the play title, a slow-motion tag, and (manual replay) a transport bar with a scrub
    /// track, the state (play / pause / speed), the camera angle, the target player and the controller prompts.
    /// </summary>
    public sealed class ReplayHud : MonoBehaviour
    {
        Canvas _canvas, _letterCanvas; RectTransform _root;
        RectTransform _barTop, _barBottom;
        RectTransform _tag, _slowTag, _transport, _skip, _titleBlock;
        TextMeshProUGUI _tagText, _titleText, _subText, _slowText, _state, _angle, _target, _timeL, _timeR, _skipText, _dofText;
        Image _fill, _head, _key, _wipe;
        RectTransform _trackRect;
        bool _manual;
        float _letter, _letterTarget, _wipeAlpha;
        float _slowPulse;
        public bool Visible { get; private set; }
        public float LetterboxHeight = 112f;

        public static ReplayHud Create(Transform parent)
        {
            var go = new GameObject("ReplayHud");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<ReplayHud>();
            h.Build();
            return h;
        }

        void Build()
        {
            _canvas = UiKit.CreateCanvas("ReplayCanvas", 45, transform);
            _canvas.GetComponent<GraphicRaycaster>().enabled = false;
            _root = (RectTransform)_canvas.transform;
            _letterCanvas = UiKit.CreateCanvas("LetterboxCanvas", 18, transform);
            _letterCanvas.GetComponent<GraphicRaycaster>().enabled = false;
            var lroot = (RectTransform)_letterCanvas.transform;
            // letterbox bars
            _barTop = TvUi.Bar(lroot, "BarTop", Color.black, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 0)).rectTransform; _barTop.anchorMax = new Vector2(1, 1); _barTop.sizeDelta = new Vector2(0, 0);
            _barBottom = TvUi.Bar(lroot, "BarBottom", Color.black, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 0)).rectTransform; _barBottom.anchorMax = new Vector2(1, 0); _barBottom.sizeDelta = new Vector2(0, 0);

            // replay tag + title (top right, inside the top bar)
            _tag = UiKit.Rect(_root, "Tag"); UiKit.Anchor(_tag, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-70, -14), new Vector2(560, 100));
            var tagBg = TvUi.Strip(_tag, "Bg", TvUi.Gold, 52, 18, true, true); UiKit.Anchor(tagBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(430, 52));
            _tagText = TvUi.Text(_tag, "INSTANT REPLAY", 38, new Color(0.06f, 0.07f, 0.1f), true, TextAlignmentOptions.Center, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(430, 52), 4f);
            _titleText = TvUi.Text(_tag, "", 32, Color.white, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -62), new Vector2(560, 38), 3f);

            // slow motion tag (below the title)
            _slowTag = UiKit.Rect(_root, "SlowTag"); UiKit.Anchor(_slowTag, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-70, -132), new Vector2(340, 44));
            var sb = TvUi.Strip(_slowTag, "Bg", new Color(0.176f, 0.353f, 0.737f, 0.95f), 44, 14, true, true); UiKit.Anchor(sb.rectTransform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(340, 44));
            _slowText = TvUi.Text(_slowTag, "SLOW MOTION 0.25x", 26, Color.white, true, TextAlignmentOptions.Center, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(340, 44), 2f);

            // skip hint (auto replay)
            _skip = UiKit.Rect(_root, "Skip"); UiKit.Anchor(_skip, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-70, 24), new Vector2(520, 56));
            var skh = _skip.gameObject.AddComponent<HorizontalLayoutGroup>(); skh.spacing = 12; skh.childAlignment = TextAnchor.MiddleRight; skh.childControlWidth = true; skh.childControlHeight = true; skh.childForceExpandWidth = false;
            UiKit.FooterPrompt(_skip, "glyph_a", "SKIP REPLAY", 48);

            BuildTransport();
            _wipe = UiKit.Panel(_root, "Wipe", new Color(1, 1, 1, 0)); UiKit.Stretch(_wipe.rectTransform); _wipe.raycastTarget = false;
            _canvas.gameObject.SetActive(false); _letterCanvas.gameObject.SetActive(false);
        }

        void BuildTransport()
        {
            _transport = UiKit.Rect(_root, "Transport"); UiKit.Anchor(_transport, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 112), new Vector2(1640, 250));
            var back = TvUi.Strip(_transport, "Back", new Color(0.04f, 0.05f, 0.08f, 0.92f), 250, 26, true, true); UiKit.Anchor(back.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(1640, 250));
            // scrub track
            var track = TvUi.Bar(_transport, "Track", new Color(1f, 1f, 1f, 0.2f), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(1380, 12));
            _trackRect = track.rectTransform;
            _fill = TvUi.Bar(track.transform, "Fill", TvUi.Gold, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 12));
            _key = TvUi.Bar(track.transform, "Key", new Color(1f, 1f, 1f, 0.9f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 30));
            _head = TvUi.Bar(track.transform, "Head", Color.white, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 34));
            _timeL = TvUi.Text(_transport, "0:00.0", 28, TvUi.Muted, true, TextAlignmentOptions.MidlineRight, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(-700, -20), new Vector2(120, 40), 1f);
            _timeR = TvUi.Text(_transport, "0:00.0", 28, TvUi.Muted, true, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(700, -20), new Vector2(120, 40), 1f);
            _state = TvUi.Text(_transport, "PLAYING  1x", 40, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -62), new Vector2(480, 54), 3f);
            _angle = TvUi.Text(_transport, "ANGLE  FREE CAMERA", 34, TvUi.Gold, true, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-60, -62), new Vector2(560, 54), 3f);
            _target = TvUi.Text(_transport, "TARGET  #12 QB J. DOE", 34, Color.white, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110, -62), new Vector2(620, 54), 3f);
            _dofText = TvUi.Text(_transport, "DEPTH OF FIELD ON", 22, TvUi.Muted, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110, -110), new Vector2(620, 30), 3f);
            // prompts (two rows)
            string[][] row1 = { new[] { "glyph_a", "PLAY / PAUSE" }, new[] { "glyph_x", "SLOW-MO" }, new[] { "glyph_y", "ANGLE" }, new[] { "glyph_lb", "PREV" }, new[] { "glyph_rb", "NEXT PLAYER" }, new[] { "glyph_b", "EXIT" } };
            string[][] row2 = { new[] { "glyph_dpad_left", "REWIND" }, new[] { "glyph_dpad_right", "FORWARD" }, new[] { "glyph_dpad_up", "DEPTH OF FIELD" }, new[] { "glyph_dpad_down", "GROUND LEVEL" }, new[] { "glyph_rs", "ORBIT" }, new[] { "glyph_ls", "PAN" }, new[] { "glyph_lt", "ZOOM" } };
            Prompts(row1, 62f); Prompts(row2, 12f);
        }

        void Prompts(string[][] items, float y)
        {
            var row = UiKit.Rect(_transport, "Prompts"); UiKit.Anchor(row, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, y), new Vector2(1560, 46));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 30; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            foreach (var it in items) UiKit.FooterPrompt(row, it[0], it[1], 40);
        }

        // ============================================================================================ state
        public void Show(bool manual, string title, string subtitle)
        {
            _manual = manual; Visible = true;
            _canvas.gameObject.SetActive(true); _letterCanvas.gameObject.SetActive(true);
            _transport.gameObject.SetActive(manual);
            _skip.gameObject.SetActive(!manual);
            _titleText.text = title ?? "";
            _tagText.text = manual ? "INSTANT REPLAY  -  FREE CAMERA" : "INSTANT REPLAY";
            var tagBg = _tag.GetChild(0).GetComponent<RectTransform>();
            tagBg.sizeDelta = new Vector2(manual ? 640f : 430f, 52f); _tagText.rectTransform.sizeDelta = tagBg.sizeDelta;
            _letterTarget = manual ? 0.55f : 1f;
            _slowTag.gameObject.SetActive(false);
        }

        public void Hide()
        {
            Visible = false; _letterTarget = 0f; _letter = 0f; ApplyLetter();
            if (_canvas != null) { _canvas.gameObject.SetActive(false); _letterCanvas.gameObject.SetActive(false); }
        }

        public void SetTitle(string title) { if (_titleText != null) _titleText.text = title ?? ""; }

        public void SetSlow(float speed, bool show)
        {
            _slowTag.gameObject.SetActive(show);
            if (show) _slowText.text = (speed < 1f ? "SLOW MOTION " : "REPLAY ") + speed.ToString("0.##") + "x";
        }

        public void SetTransport(float t01, float key01, float time, float duration, string state, string angle, string target, bool dof)
        {
            if (!_manual) return;
            float w = _trackRect.rect.width;
            _fill.rectTransform.sizeDelta = new Vector2(w * Mathf.Clamp01(t01), 12f);
            _head.rectTransform.anchoredPosition = new Vector2(w * Mathf.Clamp01(t01), 0f);
            _key.rectTransform.anchoredPosition = new Vector2(w * Mathf.Clamp01(key01), 0f);
            _timeL.text = Fmt(time); _timeR.text = Fmt(duration);
            _state.text = state; _angle.text = "ANGLE  " + angle; _target.text = target; _dofText.text = dof ? "DEPTH OF FIELD  ON" : "DEPTH OF FIELD  OFF";
        }

        static string Fmt(float t) { t = Mathf.Max(0f, t); return (int)(t / 60f) + ":" + (t % 60f).ToString("00.0"); }

        /// <summary>A quick white flash (the wipe between replay angles).</summary>
        public void Flash(float strength = 0.9f) { _wipeAlpha = strength; }

        void Update()
        {
            if (!Visible) return;
            float dt = Time.unscaledDeltaTime;
            _letter = Mathf.MoveTowards(_letter, _letterTarget, dt / 0.45f);
            ApplyLetter();
            if (_wipeAlpha > 0f) { _wipeAlpha = Mathf.MoveTowards(_wipeAlpha, 0f, dt / 0.35f); _wipe.color = new Color(1f, 1f, 1f, _wipeAlpha); }
            _slowPulse += dt;
            if (_slowTag.gameObject.activeSelf) _slowText.alpha = 0.78f + 0.22f * Mathf.Sin(_slowPulse * 5f);
        }

        void ApplyLetter()
        {
            float h = LetterboxHeight * TvUi.Smooth(_letter);
            if (_barTop != null) _barTop.sizeDelta = new Vector2(0f, h);
            if (_barBottom != null) _barBottom.sizeDelta = new Vector2(0f, h * (_manual ? 0.0f : 1f));
        }
    }
}
