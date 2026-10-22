using PixFootball.Core;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The 2K8-style scorebug: a full-width solid strip along the bottom of the screen (away block, home block, down and distance, ball spot, quarter, clock, play clock).
    /// Both this layout and the v0.5 top-left block are built; PixSettings.Scorebug picks which one is shown (re-read in SetScore, SetChrome and from the pause menu).
    /// </summary>
    public partial class MatchHud
    {
        const float BarHeight = 72f;

        RectTransform _bottomRoot, _flagStripB;
        TextMeshProUGUI _flagTextB;
        Block _bAway, _bHome;
        TextMeshProUGUI _bQuarter, _bClock, _bPlayClock, _bDown, _bSpot, _bCam;
        Image _bPlayClockBg;
        KickHud _kick;
        OptionSelector _pauseSb;
        bool _bottomActive;
        int _appliedStyle = -1;
        float _nextStyleCheck, _appliedScale = -1f;
        ScoreSnapshot _lastSnap;

        /// <summary>The wind indicator (top right): <paramref name="degreesFromNorth"/> is the heading the wind blows toward, clockwise from the compass N; 0 mph = calm.</summary>
        public void SetWind(float degreesFromNorth, float mph) { if (_kick != null) _kick.SetWind(degreesFromNorth, mph); }

        /// <summary>The kick meter (bottom centre): marker positions 0..1 along each gauge (0.5 = centre / perfect) plus the verdict text.</summary>
        public void SetKickMeter(bool visible, float timing01, string timingLabel, float aim01, string aimLabel) { if (_kick != null) _kick.SetKickMeter(visible, timing01, timingLabel, aim01, aimLabel); }

        // ---------------------------------------------------------------------------------------- build
        RectTransform BarCell(RectTransform row, string name, float width, bool flexible = false)
        {
            var r = UiKit.Rect(row, name);
            var le = r.gameObject.AddComponent<LayoutElement>();
            if (flexible) le.flexibleWidth = 1f; else { le.preferredWidth = width; le.minWidth = width; }
            return r;
        }

        Block MakeBarBlock(RectTransform row, string name, bool local)
        {
            var cell = BarCell(row, name, 340f);
            var b = new Block { Solid = true };
            b.Bg = cell.gameObject.AddComponent<Image>(); b.Bg.color = local ? new Color(0.1f, 0.3f, 0.7f, 1f) : new Color(0.7f, 0.15f, 0.15f, 1f); b.Bg.raycastTarget = false;
            b.Crest = UiKit.Panel(cell, "Crest", Color.white); b.Crest.preserveAspect = true;
            UiKit.Anchor(b.Crest.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(54, 54));
            b.Abbr = UiKit.Label(cell, "ABC", 42, Color.white, true, TextAlignmentOptions.MidlineLeft, "Abbr");
            UiKit.Anchor(b.Abbr.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(82, 6), new Vector2(130, 52)); b.Abbr.characterSpacing = 2f;
            b.Score = UiKit.Label(cell, "0", 54, Color.white, true, TextAlignmentOptions.MidlineRight, "Score");
            UiKit.Anchor(b.Score.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 2), new Vector2(100, 66));
            var poss = UiKit.Panel(cell, "Poss", UiColors.Gold, Sp("sb_arrow")); poss.preserveAspect = true;
            UiKit.Anchor(poss.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(214, 2), new Vector2(30, 30)); b.Poss = poss.gameObject;
            b.Pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(cell, "TO" + i, UiColors.Gold);
                UiKit.Anchor(p.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(84 + i * 22, 7), new Vector2(17, 6));
                b.Pips[i] = p;
            }
            return b;
        }

        TextMeshProUGUI BarLabel(RectTransform cell, string text, float size, Color color, TextAlignmentOptions align, string name, bool auto = false)
        {
            var t = UiKit.Label(cell, text, size, color, true, align, name);
            UiKit.Stretch(t.rectTransform, 8, 0, 8, 0);
            if (auto) { t.enableAutoSizing = true; t.fontSizeMin = 18; t.fontSizeMax = size; }
            return t;
        }

        void BuildBottomBar()
        {
            if (_glassHud) { BuildBottomBarGlass(); return; }
            var root = UiKit.Rect(_root, "BottomBar");
            root.anchorMin = new Vector2(0, 0); root.anchorMax = new Vector2(1, 0); root.pivot = new Vector2(0.5f, 0); root.anchoredPosition = Vector2.zero; root.sizeDelta = new Vector2(0, BarHeight);
            _bottomRoot = root;
            var bg = UiKit.Panel(root, "Bg", new Color(0.04f, 0.05f, 0.08f, 0.97f)); UiKit.Stretch(bg.rectTransform);
            var line = UiKit.Panel(root, "TopLine", UiColors.Gold);
            line.rectTransform.anchorMin = new Vector2(0, 1); line.rectTransform.anchorMax = new Vector2(1, 1); line.rectTransform.pivot = new Vector2(0.5f, 0); line.rectTransform.anchoredPosition = Vector2.zero; line.rectTransform.sizeDelta = new Vector2(0, 3);

            var row = UiKit.Rect(root, "Row"); UiKit.Stretch(row);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 3; h.padding = new RectOffset(0, 24, 0, 0); h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            _bAway = MakeBarBlock(row, "Away", false);
            _bHome = MakeBarBlock(row, "Home", true);

            var dd = BarCell(row, "DownCell", 220f);
            var ddBg = dd.gameObject.AddComponent<Image>(); ddBg.color = new Color(0.11f, 0.12f, 0.18f, 1f); ddBg.raycastTarget = false;
            _bDown = BarLabel(dd, "1ST & 10", 36, UiColors.Gold, TextAlignmentOptions.Center, "Down", true); _bDown.characterSpacing = 2f;
            var sp = BarCell(row, "SpotCell", 300f);
            var spBg = sp.gameObject.AddComponent<Image>(); spBg.color = new Color(0.09f, 0.1f, 0.15f, 1f); spBg.raycastTarget = false;
            _bSpot = BarLabel(sp, "BALL ON BUF 25", 32, Color.white, TextAlignmentOptions.Center, "Spot", true);

            BarCell(row, "Spacer", 0f, true);

            var q = BarCell(row, "QuarterCell", 100f);
            _bQuarter = BarLabel(q, "1ST", 38, UiColors.Gold, TextAlignmentOptions.Center, "Quarter");
            var c = BarCell(row, "ClockCell", 176f);
            _bClock = BarLabel(c, "5:00", 58, Color.white, TextAlignmentOptions.Center, "Clock");
            var pc = BarCell(row, "PlayClockCell", 96f);
            _bPlayClockBg = UiKit.Panel(pc, "Box", new Color(1f, 1f, 1f, 0.12f)); UiKit.Stretch(_bPlayClockBg.rectTransform, 6, 12, 6, 12);
            _bPlayClock = UiKit.Label(_bPlayClockBg.rectTransform, "25", 40, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "PlayClock"); UiKit.Stretch(_bPlayClock.rectTransform);
            var cam = BarCell(row, "CamCell", 150f);
            _bCam = BarLabel(cam, "BROADCAST", 18, UiColors.TextMuted, TextAlignmentOptions.MidlineRight, "Cam");

            // penalty flag strip: hangs just above the hint bar (the hint bar itself sits above the bar)
            _flagStripB = UiKit.Rect(_root, "FlagStripBottom");
            var fimg = _flagStripB.gameObject.AddComponent<Image>(); fimg.color = new Color(1f, 0.918f, 0f, 1f); fimg.raycastTarget = false;
            _flagTextB = UiKit.Label(_flagStripB, "", 28, Color.black, true, TextAlignmentOptions.MidlineLeft, "FlagText"); UiKit.Stretch(_flagTextB.rectTransform, 24, 0, 24, 0); _flagTextB.characterSpacing = 2f;
            _flagStripB.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------- fill
        void FillBottom(ScoreSnapshot s)
        {
            _lastSnap = s;
            if (_bottomRoot == null) return;
            if (Time.unscaledTime >= _nextStyleCheck) { _nextStyleCheck = Time.unscaledTime + 0.5f; ApplyScorebugLayout(); }
            if (_bottomActive) FillBottomNow(s);
        }

        void FillBottomNow(ScoreSnapshot s)
        {
            FillBar(_bHome, s.HomeAbbr, s.HomeCrest, s.HomeColor, s.HomeScore, s.HomeTimeouts, s.PossessionHome);
            FillBar(_bAway, s.AwayAbbr, s.AwayCrest, s.AwayColor, s.AwayScore, s.AwayTimeouts, !s.PossessionHome);
            _bQuarter.text = s.Quarter; _bClock.text = s.Clock;
            _bClock.color = s.TwoMinute ? UiColors.Orange : Color.white;
            _bDown.text = s.DownDistance; _bSpot.text = s.Spot;
            bool low = s.PlayClock >= 0 && s.PlayClock <= 5;
            _bPlayClock.text = s.PlayClock >= 0 ? s.PlayClock.ToString() : "--";
            _bPlayClock.color = low ? Color.white : UiColors.TextSecondary;
            _bPlayClockBg.color = low ? UiColors.Danger : new Color(1f, 1f, 1f, 0.12f);
        }

        void FillBar(Block b, string abbr, string crest, Color color, int score, int timeouts, bool poss)
        {
            b.Abbr.text = abbr; b.Score.text = score.ToString();
            var sp = Sp(crest); b.Crest.sprite = sp; b.Crest.enabled = sp != null;
            b.Bg.color = new Color(color.r, color.g, color.b, 1f);
            Color fg = _glassHud ? Color.white : Readable(color);
            b.Abbr.color = fg; b.Score.color = fg;
            var arrow = b.Poss.GetComponent<Image>(); if (arrow != null) arrow.color = fg;
            b.Poss.SetActive(poss);
            for (int i = 0; i < 3; i++) b.Pips[i].color = i < timeouts ? fg : new Color(fg.r, fg.g, fg.b, 0.25f);
        }

        void UpdateFlag(bool on, string text)
        {
            bool vis = on && _chromeVisible;
            if (_flagStrip != null) _flagStrip.gameObject.SetActive(vis && !_bottomActive);
            if (_flagStripB != null) _flagStripB.gameObject.SetActive(vis && _bottomActive);
            if (on && text != null)
            {
                if (_flagText != null) _flagText.text = text;
                if (_flagTextB != null) _flagTextB.text = text;
            }
        }

        // ---------------------------------------------------------------------------------------- layout selection
        /// <summary>Shows the scorebug layout chosen in PixSettings.Scorebug (and nothing while full-screen panels own the screen) and re-seats the hint bar, toast and kick meter around it.</summary>
        void ApplyScorebugLayout()
        {
            if (_bottomRoot == null) return;
            int style = (int)PixSettings.Scorebug;
            bool bottom = style == (int)ScorebugStyle.Bottom;
            bool changed = style != _appliedStyle;
            _appliedStyle = style; _bottomActive = bottom;
            if (_scorebugRoot != null) _scorebugRoot.gameObject.SetActive(_chromeVisible && !bottom);
            _bottomRoot.gameObject.SetActive(_chromeVisible && bottom);
            if (changed)
            {
                UpdateFlag(false, null);
                ApplyScorebugScale();
                if (bottom && _lastSnap != null) FillBottomNow(_lastSnap);
            }
        }

        /// <summary>Minimal-HUD / replay scaling for whichever scorebug is active, then everything stacked above the bottom bar.</summary>
        void ApplyScorebugScale()
        {
            float s = _hudMinimal && !_replayMode ? 0.66f : 1f;
            if (_scorebugRoot != null) _scorebugRoot.localScale = Vector3.one * s;
            if (_bottomRoot != null) _bottomRoot.localScale = Vector3.one * s;
            if (_bottomRoot == null) return;
            _appliedScale = s;
            float inset = _bottomActive ? BarHeight * s : 0f;
            if (_hintBar != null) ((RectTransform)_hintBar.transform).anchoredPosition = new Vector2(0f, _bottomActive ? inset + 14f : 36f);
            if (_toast != null) _toast.anchoredPosition = new Vector2(0f, _bottomActive ? inset + 128f : 150f);
            if (_kick != null) _kick.SetBaseY(_bottomActive ? inset + 84f : 106f);
            if (_preSnap != null) _preSnap.SetBottomInset(inset);
            if (_flagStripB != null)
            {
                UiKit.Anchor(_flagStripB, new Vector2(0, 0), new Vector2(0, 0), new Vector2(60f, inset + 78f), new Vector2(1170, 44));
            }
        }
    }
}
