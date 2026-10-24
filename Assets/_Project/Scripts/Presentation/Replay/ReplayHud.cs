using System.Globalization;
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
        PromptFooter _skipFooter;
        CanvasGroup _transportGroup;
        float _idle;
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
            _tag = UiKit.Rect(_root, "Tag"); UiKit.Anchor(_tag, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-250, -14), new Vector2(560, 100));
            var tagBg = TvUi.Strip(_tag, "Bg", TvUi.Gold, 52, 18, true, true); UiKit.Anchor(tagBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(430, 52));
            _tagText = TvUi.Text(_tag, "INSTANT REPLAY", 38, new Color(0.06f, 0.07f, 0.1f), true, TextAlignmentOptions.Center, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(430, 52), 4f);
            _titleText = TvUi.Text(_tag, "", 32, Color.white, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -62), new Vector2(560, 38), 3f);

            // slow motion tag (below the title)
            _slowTag = UiKit.Rect(_root, "SlowTag"); UiKit.Anchor(_slowTag, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-250, -132), new Vector2(340, 44));
            var sb = TvUi.Strip(_slowTag, "Bg", new Color(0.176f, 0.353f, 0.737f, 0.95f), 44, 14, true, true); UiKit.Anchor(sb.rectTransform, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(340, 44));
            _slowText = TvUi.Text(_slowTag, "SLOW MOTION 0.25x", 26, Color.white, true, TextAlignmentOptions.Center, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(340, 44), 2f);

            // skip hint (auto replay)
            _skip = UiKit.Rect(_root, "Skip"); UiKit.Anchor(_skip, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-HudMetrics.SafeX, HudMetrics.BarLift + 72f + 16f), new Vector2(520, 56));   // v0.11 B-045: above the score bar, not over its clock
            var skh = _skip.gameObject.AddComponent<HorizontalLayoutGroup>(); skh.spacing = 12; skh.childAlignment = TextAnchor.MiddleRight; skh.childControlWidth = true; skh.childControlHeight = true; skh.childForceExpandWidth = false;
            _skipFooter = PromptFooter.Attach(_skip, 48, 26, 12);
            _skipFooter.Set(PromptFooter.Item("glyph_a", "SKIP REPLAY"));

            BuildTransport();
            _wipe = UiKit.Panel(_root, "Wipe", new Color(1, 1, 1, 0)); UiKit.Stretch(_wipe.rectTransform); _wipe.raycastTarget = false;
            PixFootball.UI.Glass.GlassSkin.Apply(_root);
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
            // three separate columns (state / angle / target) so a long angle or player name never runs into its neighbour; each shrinks to fit (QA-862)
            _state = UiKit.Fit(TvUi.Text(_transport, "PLAYING  1x", 40, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -62), new Vector2(440, 54), 3f), 24);
            _angle = UiKit.Fit(TvUi.Text(_transport, "ANGLE  FREE CAMERA", 34, TvUi.Gold, true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-20, -62), new Vector2(440, 54), 3f), 22);
            _target = UiKit.Fit(TvUi.Text(_transport, "TARGET  #12 QB J. DOE", 34, Color.white, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -62), new Vector2(500, 54), 3f), 22);
            _dofText = TvUi.Text(_transport, "DEPTH OF FIELD ON", 22, TvUi.Muted, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -110), new Vector2(620, 30), 3f);
            // prompts (two rows)
            // LT zooms out and RT zooms in (the controller reads Sprint = RT); View steps to the previous clip
            Prompts(62f, 40f, PromptFooter.Item("glyph_a", "PLAY / PAUSE"), PromptFooter.Item("glyph_x", "SLOW-MO"), PromptFooter.Item("glyph_y", "ANGLE"), PromptFooter.Item("glyph_lb", "PREV PLAYER"), PromptFooter.Item("glyph_rb", "NEXT PLAYER"), PromptFooter.Item("glyph_view", "PREV CLIP"), PromptFooter.Item("glyph_b", "EXIT"));
            Prompts(12f, 40f, PromptFooter.Item("glyph_dpad_left", "REWIND"), PromptFooter.Item("glyph_dpad_right", "FORWARD"), PromptFooter.Item("glyph_dpad_up", "DEPTH OF FIELD"), PromptFooter.Item("glyph_dpad_down", "GROUND LEVEL"), PromptFooter.Item("glyph_rs", "ORBIT"), PromptFooter.Item("glyph_ls", "PAN"), PromptFooter.Item("glyph_lt", "ZOOM OUT"), PromptFooter.Item("glyph_rt", "ZOOM IN"));
            _transportGroup = _transport.gameObject.AddComponent<CanvasGroup>();
        }

        void Prompts(float y, float size, params PromptItem[] items)
        {
            var row = UiKit.Rect(_transport, "Prompts"); UiKit.Anchor(row, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, y), new Vector2(1560, 46));
            PromptFooter.Attach(row, size, UiTheme.IsGlass ? 19 : 24, UiTheme.IsGlass ? 14 : 26).Set(items);
        }

        // ============================================================================================ state
        /// <param name="reel">a halftime / postgame highlight reel: the tag says HIGHLIGHTS and A skips the whole reel</param>
        public void Show(bool manual, string title, string subtitle, bool reel = false)
        {
            _manual = manual; Visible = true; _idle = 0f;
            if (_skipFooter != null) _skipFooter.Set(PromptFooter.Item("glyph_a", reel ? "SKIP HIGHLIGHTS" : "SKIP REPLAY"));
            _canvas.gameObject.SetActive(true); _letterCanvas.gameObject.SetActive(true);
            _transport.gameObject.SetActive(manual);
            _skip.gameObject.SetActive(!manual);
            _titleText.text = manual && (title == null || title.ToUpperInvariant().StartsWith("INSTANT REPLAY")) ? "" : (title ?? "");   // v0.11 B-094: the same words three times in 400 px
            _tagText.text = manual ? "INSTANT REPLAY  -  FREE CAMERA" : (reel ? "HIGHLIGHTS" : "INSTANT REPLAY");
            var tagBg = _tag.GetChild(0).GetComponent<RectTransform>();
            tagBg.sizeDelta = new Vector2(manual ? 640f : (reel ? 360f : 430f), 52f); _tagText.rectTransform.sizeDelta = tagBg.sizeDelta - new Vector2(UiTheme.IsGlass ? 44f : 0f, 0f); _tagText.rectTransform.anchoredPosition = new Vector2(UiTheme.IsGlass ? -22f : 0f, -2f);
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
            if (show) _slowText.text = (speed < 1f ? "SLOW MOTION " : "REPLAY ") + speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
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

        static string Fmt(float t) { t = Mathf.Max(0f, t); return (int)(t / 60f) + ":" + (t % 60f).ToString("00.0", CultureInfo.InvariantCulture); }

        /// <summary>Any input keeps the transport bar fully visible; after a few quiet seconds it dims so it stops covering the play.</summary>
        public void NoteActivity() { _idle = 0f; }

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
            if (_manual && _transportGroup != null) { _idle += dt; _transportGroup.alpha = Mathf.MoveTowards(_transportGroup.alpha, _idle > 4f ? 0.25f : 1f, dt * 2.5f); }
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
