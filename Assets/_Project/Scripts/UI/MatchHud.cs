using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The in-game presentation layer for a match, built in code in the UI-kit style: scorebug (crests, score, quarter, clock, play clock, down and distance,
    /// timeouts, possession), event banners, the pre-snap play-call panel, receiver button icons, coin toss, final score and the pause menu.
    /// </summary>
    public class MatchHud : MonoBehaviour, IMatchPresentation
    {
        // ---- interface events ----
        public event Action ResumeRequested, QuitRequested, CameraCycleRequested, LightingToggleRequested;
        public bool PauseOpen { get { return _pauseRoot != null && _pauseRoot.activeSelf; } }

        MatchDirector _dir;
        Canvas _canvas; RectTransform _root;
        // scorebug
        sealed class Block { public Image Bg, Crest; public TextMeshProUGUI Abbr, Score; public GameObject Poss; public Image[] Pips; }
        Block _away, _home;
        TextMeshProUGUI _quarter, _clock, _playClock, _down, _spot, _inPlay;
        Image _clockBg, _playClockBg;
        // banner
        RectTransform _banner; Image _bannerBg, _bannerBase, _bannerSlab, _bannerRail; TextMeshProUGUI _bannerText, _bannerSub; Coroutine _bannerCo;
        RectTransform _toast; TextMeshProUGUI _toastText; Coroutine _toastCo;
        // play call / pre-snap / penalty (v0.3)
        PlayCallScreen _callScreen; PreSnapPanel _preSnap; PenaltyPanel _penalty; Action<bool> _penaltyDecision;
        bool _callShown, _chromeVisible = true;
        RectTransform _scorebugRoot; GameObject _hintBar; RectTransform _flagStrip; TextMeshProUGUI _flagText;
        // coin toss / final / pause
        GameObject _tossRoot, _finalRoot, _pauseRoot;
        TextMeshProUGUI _hint, _camLabel;
        Image _fade;
        readonly List<GameObject> _iconPool = new List<GameObject>();
        OptionSelector _pauseCam, _pauseLight;
        Button _pauseResume;
        RectTransform _iconLayer;

        // ============================================================================================ build
        public void Build(MatchDirector director)
        {
            _dir = director;
            UiKit.EnsureEventSystem();
            _canvas = UiKit.CreateCanvas("MatchHud", 20, transform);
            _root = (RectTransform)_canvas.transform;
            _iconLayer = UiKit.Rect(_root, "ReceiverIcons"); UiKit.Stretch(_iconLayer);
            BuildScorebug();
            BuildBanner();
            BuildHint();
            _callScreen = PlayCallScreen.Create(_root); _callScreen.Hide();
            _preSnap = PreSnapPanel.Create(_root); _preSnap.Hide();
            _penalty = PenaltyPanel.Create(_root);
            _fade = UiKit.Panel(_root, "Fade", new Color(0, 0, 0, 0)); UiKit.Stretch(_fade.rectTransform); _fade.raycastTarget = false;
            _fade.transform.SetAsLastSibling();
            BuildPause();
        }

        Sprite Sp(string k) { return UiKit.Sprite(k); }

        static Color Readable(Color c) { float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; return l > 0.55f ? new Color(0.06f, 0.07f, 0.1f) : Color.white; }

        // ---------------------------------------------------------------------------------------- scorebug
        Block MakeBlock(RectTransform parent, string name, Vector2 pos, bool local)
        {
            var b = new Block();
            var r = UiKit.Rect(parent, name); UiKit.Anchor(r, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(430, 84));
            b.Bg = r.gameObject.AddComponent<Image>(); b.Bg.sprite = Sp(local ? "sb_local" : "sb_visitor"); b.Bg.type = Image.Type.Simple; b.Bg.raycastTarget = false;
            if (b.Bg.sprite == null) b.Bg.color = new Color(0.15f, 0.16f, 0.2f, 0.95f);
            b.Crest = UiKit.Panel(r, "Crest", Color.white); b.Crest.preserveAspect = true;
            UiKit.Anchor(b.Crest.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(66, 66));
            b.Abbr = UiKit.Label(r, "ABC", 44, Color.white, true, TextAlignmentOptions.MidlineLeft, "Abbr");
            UiKit.Anchor(b.Abbr.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(122, 4), new Vector2(170, 60)); b.Abbr.characterSpacing = 2f;
            b.Score = UiKit.Label(r, "0", 58, Color.white, true, TextAlignmentOptions.MidlineRight, "Score");
            UiKit.Anchor(b.Score.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-66, 4), new Vector2(110, 70));
            var poss = UiKit.Panel(r, "Poss", UiColors.Gold, Sp("sb_arrow")); poss.preserveAspect = true; poss.rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
            UiKit.Anchor(poss.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 2), new Vector2(34, 34)); b.Poss = poss.gameObject;
            b.Pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(r, "TO" + i, UiColors.Gold);
                UiKit.Anchor(p.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(124 + i * 24, 8), new Vector2(18, 7));
                b.Pips[i] = p;
            }
            return b;
        }

        void BuildScorebug()
        {
            var bug = UiKit.Rect(_root, "Scorebug"); UiKit.Anchor(bug, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -44), new Vector2(1240, 140));
            _scorebugRoot = bug;
            _away = MakeBlock(bug, "Away", new Vector2(0, 0), false);
            _home = MakeBlock(bug, "Home", new Vector2(420, 0), true);
            // clock block
            var cb = UiKit.Panel(bug, "ClockBlock", new Color(0.07f, 0.08f, 0.11f, 0.94f)); _clockBg = cb;
            UiKit.Anchor(cb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(850, 0), new Vector2(320, 84));
            var edge = UiKit.Panel(cb.rectTransform, "Edge", UiColors.Gold); UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 84)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0);
            _quarter = UiKit.Label(cb.rectTransform, "1ST", 34, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Quarter");
            UiKit.Anchor(_quarter.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, 2), new Vector2(90, 50));
            _clock = UiKit.Label(cb.rectTransform, "5:00", 60, Color.white, true, TextAlignmentOptions.MidlineLeft, "Clock");
            UiKit.Anchor(_clock.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(108, 2), new Vector2(150, 70));
            _playClockBg = UiKit.Panel(cb.rectTransform, "PlayClockBg", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Anchor(_playClockBg.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(64, 56));
            _playClock = UiKit.Label(_playClockBg.rectTransform, "25", 36, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "PlayClock"); UiKit.Stretch(_playClock.rectTransform);
            // lower strip: down and distance, ball spot
            var strip = UiKit.Panel(bug, "Strip", new Color(0.05f, 0.06f, 0.09f, 0.9f));
            UiKit.Anchor(strip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -84), new Vector2(1170, 46));
            _down = UiKit.Label(strip.rectTransform, "1ST & 10", 30, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Down");
            UiKit.Anchor(_down.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(300, 44)); _down.characterSpacing = 2f;
            _spot = UiKit.Label(strip.rectTransform, "BALL ON BUF 25", 26, Color.white, true, TextAlignmentOptions.MidlineLeft, "Spot");
            UiKit.Anchor(_spot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(330, 0), new Vector2(420, 44));
            // flag strip (penalty announcements) hangs under the scorebug
            _flagStrip = UiKit.Rect(bug, "FlagStrip"); UiKit.Anchor(_flagStrip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -130), new Vector2(1170, 44));
            var fimg = _flagStrip.gameObject.AddComponent<Image>(); fimg.color = new Color(1f, 0.918f, 0f, 1f); fimg.raycastTarget = false;
            _flagText = UiKit.Label(_flagStrip, "", 28, Color.black, true, TextAlignmentOptions.MidlineLeft, "FlagText"); UiKit.Stretch(_flagText.rectTransform, 24, 0, 24, 0); _flagText.characterSpacing = 2f;
            _flagStrip.gameObject.SetActive(false);
            _camLabel = UiKit.Label(strip.rectTransform, "BROADCAST", 22, UiColors.TextMuted, true, TextAlignmentOptions.MidlineRight, "Cam");
            UiKit.Anchor(_camLabel.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(300, 44));
        }

        public void SetScore(ScoreSnapshot s)
        {
            if (_home == null || s == null) return;
            Fill(_home, s.HomeAbbr, s.HomeCrest, s.HomeColor, s.HomeScore, s.HomeTimeouts, s.PossessionHome);
            Fill(_away, s.AwayAbbr, s.AwayCrest, s.AwayColor, s.AwayScore, s.AwayTimeouts, !s.PossessionHome);
            _quarter.text = s.Quarter; _clock.text = s.Clock;
            _clock.color = s.TwoMinute ? UiColors.Orange : Color.white;
            _playClock.text = s.PlayClock >= 0 ? s.PlayClock.ToString() : "--";
            _playClock.color = s.PlayClock >= 0 && s.PlayClock <= 5 ? UiColors.Danger : UiColors.TextSecondary;
            _down.text = s.DownDistance; _spot.text = s.Spot;
            bool flag = !string.IsNullOrEmpty(s.FlagText);
            if (_flagStrip != null) { _flagStrip.gameObject.SetActive(flag && _chromeVisible); if (flag) _flagText.text = s.FlagText; }
        }

        void Fill(Block b, string abbr, string crest, Color color, int score, int timeouts, bool poss)
        {
            b.Abbr.text = abbr; b.Score.text = score.ToString();
            var sp = Sp(crest); b.Crest.sprite = sp; b.Crest.enabled = sp != null;
            Color c = Color.Lerp(color, Color.white, 0.0f);
            if (b.Bg.sprite != null) b.Bg.color = new Color(c.r, c.g, c.b, 1f);
            b.Poss.SetActive(poss);
            for (int i = 0; i < 3; i++) b.Pips[i].color = i < timeouts ? UiColors.Gold : new Color(1f, 1f, 1f, 0.18f);
        }

        public void SetCameraLabel(string label) { if (_camLabel != null) _camLabel.text = label; }

        // ---------------------------------------------------------------------------------------- banner and toast
        void BuildBanner()
        {
            _banner = UiKit.Rect(_root, "Banner"); UiKit.Anchor(_banner, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(1160, 190));
            // fully opaque base (the glassy kit art is only used as a subtle tint on top) so the headline always reads
            var baseRect = UiKit.Rect(_banner, "Base"); UiKit.Stretch(baseRect);
            _bannerBase = baseRect.gameObject.AddComponent<Image>(); _bannerBase.color = new Color(0.04f, 0.05f, 0.09f, 1f); _bannerBase.raycastTarget = false;
            var topRect = UiKit.Rect(_banner, "Tint"); UiKit.Stretch(topRect);
            _bannerBg = topRect.gameObject.AddComponent<Image>(); _bannerBg.sprite = Sp("sb_notification"); _bannerBg.raycastTarget = false;
            var slab = UiKit.Panel(_banner, "Slab", Color.white); UiKit.Anchor(slab.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(26, 0)); slab.rectTransform.anchorMax = new Vector2(0, 1); slab.rectTransform.sizeDelta = new Vector2(26, 0); _bannerSlab = slab;
            var rail = UiKit.Panel(_banner, "Rail", Color.white); UiKit.Anchor(rail.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 8)); rail.rectTransform.anchorMax = new Vector2(1, 0); rail.rectTransform.sizeDelta = new Vector2(0, 8); _bannerRail = rail;
            _bannerText = UiKit.Label(_banner, "TOUCHDOWN", 92, Color.white, true, TextAlignmentOptions.Center, "Text");
            UiKit.Anchor(_bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(1000, 120)); _bannerText.characterSpacing = 6f;
            _bannerSub = UiKit.Label(_banner, "", 34, Color.white, true, TextAlignmentOptions.Center, "Sub");
            UiKit.Anchor(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(900, 50)); _bannerSub.characterSpacing = 3f;            _banner.gameObject.SetActive(false);

            _toast = UiKit.Rect(_root, "Toast"); UiKit.Anchor(_toast, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(800, 60));
            var tb = _toast.gameObject.AddComponent<Image>(); tb.color = new Color(0.05f, 0.06f, 0.09f, 0.85f); tb.raycastTarget = false;
            _toastText = UiKit.Label(_toast, "", 30, Color.white, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(_toastText.rectTransform);
            _toast.gameObject.SetActive(false);
        }

        public void ShowBanner(string text, Color color, float seconds, string sub)
        {
            if (_banner == null) return;
            if (_bannerCo != null) StopCoroutine(_bannerCo);
            _bannerCo = StartCoroutine(BannerRoutine(text, color, seconds, sub));
        }

        IEnumerator BannerRoutine(string text, Color color, float seconds, string sub)
        {
            _banner.gameObject.SetActive(true);
            _bannerBg.color = new Color(color.r, color.g, color.b, 0.30f);
            _bannerSlab.color = color; _bannerRail.color = color;
            Color sc = Color.Lerp(color, Color.white, 0.45f);
            _bannerText.text = text; _bannerText.color = Color.white; _bannerSub.text = sub ?? ""; _bannerSub.color = sc;
            var cg = _banner.GetComponent<CanvasGroup>() ?? _banner.gameObject.AddComponent<CanvasGroup>();
            float t = 0f;
            while (t < 0.28f) { t += Time.unscaledDeltaTime; float u = Mathf.SmoothStep(0, 1, t / 0.28f); _banner.anchoredPosition = new Vector2(Mathf.Lerp(-900f, 0f, u), -250f); cg.alpha = u; yield return null; }
            _banner.anchoredPosition = new Vector2(0f, -250f); cg.alpha = 1f;
            t = 0f; while (t < seconds - 0.5f) { t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            while (t < 0.25f) { t += Time.unscaledDeltaTime; float u = t / 0.25f; cg.alpha = 1f - u; _banner.anchoredPosition = new Vector2(Mathf.Lerp(0f, 500f, u), -250f); yield return null; }
            _banner.gameObject.SetActive(false);
        }

        public void ShowToast(string text, float seconds)
        {
            if (_toast == null) return;
            if (_toastCo != null) StopCoroutine(_toastCo);
            _toastCo = StartCoroutine(ToastRoutine(text, seconds));
        }

        IEnumerator ToastRoutine(string text, float seconds)
        {
            _toast.gameObject.SetActive(true); _toastText.text = text;
            float t = 0f; while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            _toast.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------- hint bar and fade
        void BuildHint()
        {
            var bar = UiKit.Panel(_root, "HintBar", new Color(0.04f, 0.05f, 0.08f, 0.72f));
            UiKit.Anchor(bar.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(1640, 54));
            _hintBar = bar.gameObject;
            _hint = UiKit.Label(bar.rectTransform, "", 26, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "Hint"); UiKit.Stretch(_hint.rectTransform);
            _hint.characterSpacing = 1.5f;
        }

        public void SetHint(string hint)
        {
            if (_hint == null) return;
            _hint.text = hint ?? "";
            _hint.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(hint) && _chromeVisible);
        }

        public void SetFade(float alpha) { if (_fade != null) _fade.color = new Color(0, 0, 0, Mathf.Clamp01(alpha)); }

        // ---------------------------------------------------------------------------------------- play call screen (v0.3)
        public void ShowPlayCall(PlayCallModel model, float playClock)
        {
            if (_callScreen == null) return;
            if (!_callShown) { _callShown = true; SetHint(""); }
            _callScreen.Show(model, playClock);
            _callScreen.transform.SetAsLastSibling(); if (_fade != null) _fade.transform.SetAsLastSibling();
        }

        public void HidePlayCall()
        {
            if (_callScreen != null) _callScreen.Hide();
            _callShown = false;
            if (_tossRoot != null) Destroy(_tossRoot);
        }

        public void SetPreSnap(PreSnapHud hud)
        {
            if (_preSnap == null) return;
            if (hud == null) { _preSnap.Hide(); return; }
            _preSnap.Show(hud);
            if (_preSnap.transform.GetSiblingIndex() < _root.childCount - 2) _preSnap.transform.SetSiblingIndex(_root.childCount - 2);
            SetHint("");
        }

        public void ShowPenalty(PenaltyPanelData data, Action<bool> onDecision)
        {
            if (_penalty == null) return;
            _penalty.Show(data, onDecision);
            _penaltyDecision = onDecision;
            if (_fade != null) _fade.transform.SetAsLastSibling();
        }

        public void RefreshPenalty(PenaltyPanelData data) { if (_penalty != null) _penalty.Refresh(data); }
        public void HidePenalty() { if (_penalty != null) _penalty.Hide(); _penaltyDecision = null; }

        static string KeyFor(Btn b) { switch (b) { case Btn.A: return "SPACE"; case Btn.B: return "F"; case Btn.X: return "C"; case Btn.Y: return "R"; case Btn.LB: return "Z"; default: return "X"; } }
        static string GlyphKey(Btn b) { return "glyph_" + b.ToString().ToLowerInvariant(); }

        Transform MakeButtonIcon(Transform parent, Btn b, float size)
        {
            var holder = UiKit.Rect(parent, "Icon_" + b);
            holder.sizeDelta = new Vector2(size * 1.4f, size);
            if (MatchInput.GamepadActive)
            {
                var g = UiKit.Glyph(holder, GlyphKey(b), size);
                UiKit.Anchor(g.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, g.rectTransform.sizeDelta);
            }
            else
            {
                var key = UiKit.Panel(holder, "Key", new Color(0.92f, 0.92f, 0.95f)); UiKit.Anchor(key.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 1.35f, size * 0.85f));
                var t = UiKit.Label(key.rectTransform, KeyFor(b), size * 0.42f, new Color(0.08f, 0.09f, 0.12f), true, TextAlignmentOptions.Center, "KeyText"); UiKit.Stretch(t.rectTransform);
            }
            return holder;
        }

        void LateUpdate()
        {
            // full-screen panels hide the scorebug, banner and hint bar (nothing may show through the final screen, the coin toss, the call screen or the pause menu)
            bool want = !(_callShown || _tossRoot != null || _finalRoot != null || PauseOpen);
            if (want != _chromeVisible) SetChrome(want);
        }

        /// <summary>Full-screen panels (play call, coin toss, final, pause) hide the scorebug, banner and hint bar so nothing shows through them.</summary>
        void SetChrome(bool visible)
        {
            if (_scorebugRoot != null) _scorebugRoot.gameObject.SetActive(visible);
            if (_flagStrip != null && !visible) _flagStrip.gameObject.SetActive(false);
            if (_hintBar != null && !visible) _hintBar.SetActive(false);
            if (_banner != null && !visible) _banner.gameObject.SetActive(false);
            _chromeVisible = visible;
        }
        // ---------------------------------------------------------------------------------------- receiver icons
        readonly List<Image> _leaders = new List<Image>();
        readonly List<Vector2> _iconPos = new List<Vector2>(), _iconAnchor = new List<Vector2>();
        readonly List<bool> _iconOk = new List<bool>();

        public void SetReceiverIcons(List<ReceiverIcon> icons, Camera cam)
        {
            int n = icons == null ? 0 : icons.Count;
            while (_iconPool.Count < n) _iconPool.Add(MakeReceiverIcon());
            for (int i = 0; i < _iconPool.Count; i++) _iconPool[i].SetActive(i < n);
            for (int i = 0; i < _leaders.Count; i++) _leaders[i].gameObject.SetActive(false);
            if (n == 0 || cam == null) return;
            _iconPos.Clear(); _iconAnchor.Clear(); _iconOk.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(icons[i].World);
                Vector2 local = Vector2.zero; bool ok = sp.z >= 0f;
                if (ok) RectTransformUtility.ScreenPointToLocalPointInRectangle(_iconLayer, sp, null, out local);
                _iconPos.Add(local); _iconAnchor.Add(local); _iconOk.Add(ok);
            }
            SeparateIcons();
            for (int i = 0; i < n; i++)
            {
                var go = _iconPool[i];
                if (!_iconOk[i]) { go.SetActive(false); continue; }
                ((RectTransform)go.transform).anchoredPosition = _iconPos[i];
                UpdateIconGlyph(go, icons[i].Button, icons[i].Arrow);
                var ring = go.transform.GetChild(0).GetComponent<Image>();
                if (ring != null) ring.color = icons[i].Highlight ? new Color(1f, 0.85f, 0.2f, 1f) : (ring.sprite != null ? Color.white : new Color(0.55f, 0.1f, 1f, 0.8f));
                go.transform.localScale = Vector3.one * (icons[i].Highlight ? 1.18f : 1f);
                // a thin leader from the displaced icon back to its player
                if ((_iconPos[i] - _iconAnchor[i]).magnitude > 18f) Leader(i, _iconAnchor[i], _iconPos[i]);
            }
        }

        /// <summary>Pushes overlapping icons apart (and back inside the screen) so the receiver buttons never stack on top of each other.</summary>
        void SeparateIcons()
        {
            const float minDist = 100f, margin = 62f;
            Rect r = _iconLayer.rect;
            float xMin = r.xMin + margin, xMax = r.xMax - margin, yMin = r.yMin + margin + 120f, yMax = r.yMax - margin - 190f;
            int n = _iconPos.Count;
            for (int pass = 0; pass < 24; pass++)
            {
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    if (!_iconOk[i]) continue;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (!_iconOk[j]) continue;
                        Vector2 d = _iconPos[j] - _iconPos[i]; float len = d.magnitude;
                        if (len >= minDist) continue;
                        Vector2 dir = len > 0.5f ? d / len : new Vector2(Mathf.Cos(i * 1.7f + j), Mathf.Sin(i * 1.3f + j * 2f));
                        // prefer spreading sideways first (receivers are lined up across the field)
                        dir = new Vector2(dir.x * 1.0f, dir.y * 0.7f).normalized;
                        float push = (minDist - len) * 0.5f + 0.5f;
                        _iconPos[i] -= dir * push; _iconPos[j] += dir * push; moved = true;
                    }
                    Vector2 p = _iconPos[i];
                    p.x = Mathf.Clamp(p.x, xMin, xMax); p.y = Mathf.Clamp(p.y, yMin, yMax);
                    _iconPos[i] = p;
                }
                if (!moved) break;
            }
        }

        void Leader(int i, Vector2 a, Vector2 b)
        {
            while (_leaders.Count <= i)
            {
                var im = UiKit.Panel(_iconLayer, "Leader" + _leaders.Count, new Color(1f, 1f, 1f, 0.75f)); im.transform.SetAsFirstSibling();
                im.rectTransform.pivot = new Vector2(0f, 0.5f); _leaders.Add(im);
            }
            var l = _leaders[i]; l.gameObject.SetActive(true);
            Vector2 d = b - a;
            l.rectTransform.anchorMin = l.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            l.rectTransform.anchoredPosition = a; l.rectTransform.sizeDelta = new Vector2(d.magnitude, 4f);
            l.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        GameObject MakeReceiverIcon()
        {
            var r = UiKit.Rect(_iconLayer, "RecIcon"); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(84, 84);
            var ring = UiKit.Panel(r, "Ring", Color.white, Sp("ring_b")); ring.preserveAspect = true; UiKit.Stretch(ring.rectTransform);
            if (ring.sprite == null) ring.color = new Color(0.55f, 0.1f, 1f, 0.8f);
            var holder = UiKit.Rect(r, "Glyph"); UiKit.Stretch(holder);
            return r.gameObject;
        }

        readonly Dictionary<GameObject, string> _iconBtn = new Dictionary<GameObject, string>();
        void UpdateIconGlyph(GameObject go, Btn b, bool arrow)
        {
            string key = arrow ? "arrow" : b.ToString() + (MatchInput.GamepadActive ? "p" : "k");
            string cur;
            if (_iconBtn.TryGetValue(go, out cur) && cur == key && go.transform.GetChild(1).childCount > 0) return;
            _iconBtn[go] = key;
            var holder = go.transform.GetChild(1);
            for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);
            if (arrow)
            {
                var t = UiKit.Label(holder, ">", 56, Color.white, true, TextAlignmentOptions.Center, "Arrow"); UiKit.Stretch(t.rectTransform);
                return;
            }
            var icon = MakeButtonIcon(holder, b, 46f);
            UiKit.Anchor((RectTransform)icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, ((RectTransform)icon).sizeDelta);
        }
        // ---------------------------------------------------------------------------------------- coin toss
        RectTransform BeginModal(string name, string title, string sub)
        {
            if (_tossRoot != null) Destroy(_tossRoot);
            var root = UiKit.Rect(_root, name); UiKit.Stretch(root); _tossRoot = root.gameObject;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.03f, 0.06f, 0.88f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var t = UiKit.Label(root, title, 92, Color.white, true, TextAlignmentOptions.Center, "Title");
            UiKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1500, 120)); t.characterSpacing = 6f;
            var rule = UiKit.Panel(root, "Rule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 178), new Vector2(220, 7));
            var s = UiKit.Label(root, sub, 36, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "Sub");
            UiKit.Anchor(s.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 110), new Vector2(1500, 60)); s.characterSpacing = 2f;
            _tossRoot.transform.SetAsLastSibling(); if (_fade != null) _fade.transform.SetAsLastSibling();
            return root;
        }

        RectTransform ButtonColumn(Transform parent, float y)
        {
            var col = UiKit.Rect(parent, "Buttons"); UiKit.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(560, 300));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 16; v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            return col;
        }

        public void ShowCoinTossCall(string userTeam, string cpuTeam, Action<bool> onCallHeads)
        {
            var root = BeginModal("CoinToss", "COIN TOSS", userTeam.ToUpperInvariant() + "  VS  " + cpuTeam.ToUpperInvariant() + "  -  CALL IT");
            var col = ButtonColumn(root, -110);
            Button first = null;
            first = UiKit.MenuButton(col, "HEADS", () => { Destroy(_tossRoot); onCallHeads(true); }, 560, 88, 40);
            UiKit.MenuButton(col, "TAILS", () => { Destroy(_tossRoot); onCallHeads(false); }, 560, 88, 40);
            StartCoroutine(SelectLater(first.gameObject));
        }

        public void ShowCoinTossResult(bool resultHeads, bool userWon, string winnerName, Action<bool> onReceive)
        {
            var root = BeginModal("CoinTossResult", resultHeads ? "IT'S HEADS" : "IT'S TAILS", winnerName.ToUpperInvariant() + " WON THE TOSS");
            if (userWon)
            {
                var col = ButtonColumn(root, -110);
                var first = UiKit.MenuButton(col, "RECEIVE", () => { Destroy(_tossRoot); onReceive(true); }, 560, 88, 40);
                UiKit.MenuButton(col, "DEFER TO 2ND HALF", () => { Destroy(_tossRoot); onReceive(false); }, 560, 88, 36);
                StartCoroutine(SelectLater(first.gameObject));
            }
            else
            {
                var t = UiKit.Label(root, "ELECTS TO RECEIVE", 44, UiColors.Gold, true, TextAlignmentOptions.Center, "Elect");
                UiKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1200, 70)); t.characterSpacing = 4f;
                StartCoroutine(AutoContinue(onReceive, 2.6f));
            }
        }

        IEnumerator AutoContinue(Action<bool> cb, float seconds)
        {
            float t = 0f; while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            if (_tossRoot != null) Destroy(_tossRoot);
            cb(true);
        }

        IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(g);
        }

        // ---------------------------------------------------------------------------------------- final
        public void ShowFinal(FinalData d, Action onContinue)
        {
            if (_finalRoot != null) Destroy(_finalRoot);
            var root = UiKit.Rect(_root, "Final"); UiKit.Stretch(root); _finalRoot = root.gameObject;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.03f, 0.06f, 0.92f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var head = UiKit.Label(root, d.Headline, 100, d.Tie ? Color.white : (d.UserWon ? UiColors.Gold : Color.white), true, TextAlignmentOptions.Center, "Headline");
            UiKit.Anchor(head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(1600, 130)); head.characterSpacing = 8f;
            var rule = UiKit.Panel(root, "Rule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -226), new Vector2(240, 7));

            // team rows: crest, name, per-quarter points, total
            float y0 = -280f;
            Row(root, y0, d.AwayAbbr, d.AwayName, d.AwayCrest, d.AwayColor, d.AwayByQuarter, d.AwayScore, d.AwayScore > d.HomeScore);
            Row(root, y0 - 130f, d.HomeAbbr, d.HomeName, d.HomeCrest, d.HomeColor, d.HomeByQuarter, d.HomeScore, d.HomeScore > d.AwayScore);
            // header for quarters
            string[] qn = { "1", "2", "3", "4", "OT" };
            for (int i = 0; i < 5; i++)
            {
                var hl = UiKit.Label(root, qn[i], 26, UiColors.TextMuted, true, TextAlignmentOptions.Center, "QH" + i);
                UiKit.Anchor(hl.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-650f + 760f + i * 96f, y0 + 34f), new Vector2(90, 34));
            }

            // stats
            float sy = y0 - 300f;
            var title = UiKit.Label(root, "TEAM STATS", 34, UiColors.Gold, true, TextAlignmentOptions.Center, "StatsTitle");
            UiKit.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, sy + 40), new Vector2(600, 44)); title.characterSpacing = 4f;
            string[][] rows =
            {
                new[] { "TOTAL YARDS", d.AwayStats.TotalYards.ToString(), d.HomeStats.TotalYards.ToString() },
                new[] { "PASSING YARDS", d.AwayStats.PassYards.ToString(), d.HomeStats.PassYards.ToString() },
                new[] { "RUSHING YARDS", d.AwayStats.RushYards.ToString(), d.HomeStats.RushYards.ToString() },
                new[] { "FIRST DOWNS", d.AwayStats.FirstDowns.ToString(), d.HomeStats.FirstDowns.ToString() },
                new[] { "TURNOVERS", d.AwayStats.Turnovers.ToString(), d.HomeStats.Turnovers.ToString() },
                new[] { "PENALTIES", d.AwayStats.Penalties + " - " + d.AwayStats.PenaltyYards, d.HomeStats.Penalties + " - " + d.HomeStats.PenaltyYards },
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var bg = UiKit.Panel(root, "StatRow" + i, i % 2 == 0 ? UiColors.Row : new Color(0.14f, 0.15f, 0.2f, 1f));
                UiKit.Anchor(bg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, sy - i * 48f), new Vector2(900, 46));
                var a = UiKit.Label(bg.rectTransform, rows[i][1], 30, Color.white, true, TextAlignmentOptions.MidlineLeft, "A"); UiKit.Anchor(a.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(240, 48));
                var m = UiKit.Label(bg.rectTransform, rows[i][0], 26, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "M"); UiKit.Anchor(m.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 48)); m.characterSpacing = 2f;
                var h = UiKit.Label(bg.rectTransform, rows[i][2], 30, Color.white, true, TextAlignmentOptions.MidlineRight, "H"); UiKit.Anchor(h.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(240, 48));
            }
            var col = UiKit.Rect(root, "Buttons"); UiKit.Anchor(col, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(560, 100));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var btn = UiKit.MenuButton(col, "CONTINUE", () => { if (onContinue != null) onContinue(); }, 560, 84, 38);
            _finalRoot.transform.SetAsLastSibling(); if (_fade != null) _fade.transform.SetAsLastSibling();
            StartCoroutine(SelectLater(btn.gameObject));
        }

        void Row(RectTransform root, float y, string abbr, string name, string crest, Color color, int[] byQ, int total, bool winner)
        {
            var bg = UiKit.Panel(root, "TeamRow_" + abbr, new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f, 0.95f), Sp("sb_local"));
            if (bg.sprite != null) bg.color = color;
            UiKit.Anchor(bg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(1300, 120));
            var cr = UiKit.Panel(bg.rectTransform, "Crest", Color.white, Sp(crest)); cr.preserveAspect = true; cr.enabled = cr.sprite != null;
            UiKit.Anchor(cr.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(58, 0), new Vector2(96, 96));
            var n = UiKit.Label(bg.rectTransform, name.ToUpperInvariant(), 40, Color.white, true, TextAlignmentOptions.MidlineLeft, "Name");
            UiKit.Anchor(n.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(176, 0), new Vector2(520, 70)); n.characterSpacing = 2f;
            int ot = 0; for (int i = 5; i < 8; i++) ot += byQ[i];
            int[] vals = { byQ[1], byQ[2], byQ[3], byQ[4], ot };
            for (int i = 0; i < 5; i++)
            {
                var ql = UiKit.Label(bg.rectTransform, vals[i].ToString(), 36, Color.white, true, TextAlignmentOptions.Center, "Q" + i);
                UiKit.Anchor(ql.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(760f + i * 96f, 0), new Vector2(90, 60));
            }
            var t = UiKit.Label(bg.rectTransform, total.ToString(), 84, winner ? UiColors.Gold : Color.white, true, TextAlignmentOptions.MidlineRight, "Total");
            UiKit.Anchor(t.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(180, 100));
        }

        // ---------------------------------------------------------------------------------------- pause
        void BuildPause()
        {
            var c = UiKit.CreateCanvas("PauseCanvas", 60, transform);
            _pauseRoot = c.gameObject;
            var root = (RectTransform)c.transform;
            var scrim = UiKit.Panel(root, "Scrim", UiColors.Scrim); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var rail = UiKit.Panel(root, "Rail", new Color(0.05f, 0.06f, 0.1f, 0.94f));
            UiKit.Anchor(rail.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(860, 0)); rail.rectTransform.anchorMax = new Vector2(0, 1); rail.rectTransform.sizeDelta = new Vector2(860, 0);
            var edge = UiKit.Panel(root, "RailEdge", UiColors.AccentBlue);
            UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(860, 0), new Vector2(4, 0)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(4, 0);
            var title = UiKit.Label(root, "PAUSED", 120, Color.white, true, TextAlignmentOptions.BottomLeft, "Title");
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -80), new Vector2(700, 140)); title.characterSpacing = 4f;
            var rule = UiKit.Panel(root, "Rule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(108, -228), new Vector2(220, 8));
            var col = UiKit.Rect(root, "Items"); UiKit.Anchor(col, new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -270), new Vector2(720, 520));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 14; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            _pauseResume = UiKit.MenuButton(col, "RESUME", () => { if (ResumeRequested != null) ResumeRequested(); }, 720, 84, 38);
            _pauseCam = UiControls.Selector(col, "Camera", new[] { "Broadcast", "Behind player", "First person" }, 0, i => { if (CameraCycleRequested != null && _dir != null && _dir.Cams != null) { _dir.Cams.SetMode((CameraMode)i, false); PixSettings.DefaultCamera = (CameraMode)i; } }, 720, 84);
            _pauseLight = UiControls.Selector(col, "Lighting", new[] { "Night game", "Day game" }, 0, i => { if (LightingToggleRequested != null) LightingToggleRequested(); }, 720, 84);
            UiKit.MenuButton(col, "QUIT TO MENU", () => { if (QuitRequested != null) QuitRequested(); }, 720, 84, 38);
            var bar = UiKit.Panel(root, "Footer", UiColors.BgPanelDark); UiKit.Anchor(bar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 76)); bar.rectTransform.anchorMax = new Vector2(1, 0); bar.rectTransform.sizeDelta = new Vector2(0, 76);
            var row = UiKit.Rect(bar.rectTransform, "Prompts"); UiKit.Anchor(row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(100, 0), new Vector2(1100, 56));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 48; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            UiKit.FooterPrompt(row, "glyph_a", "SELECT", 48); UiKit.FooterPrompt(row, "glyph_menu", "RESUME", 48); UiKit.FooterPrompt(row, "glyph_analog_all", "NAVIGATE", 56);
            _pauseRoot.SetActive(false);
        }

        public void SetPaused(bool paused)
        {
            if (_pauseRoot == null) return;
            _pauseRoot.SetActive(paused);
            if (paused)
            {
                if (_dir != null && _dir.Cams != null) _pauseCam.SetOptions(_pauseCam.Options, (int)_dir.Cams.Mode);
                if (_dir != null && _dir.Lighting != null) _pauseLight.SetOptions(_pauseLight.Options, (int)_dir.Lighting.Current);
                StartCoroutine(SelectLater(_pauseResume.gameObject));
            }
        }
    }
}






