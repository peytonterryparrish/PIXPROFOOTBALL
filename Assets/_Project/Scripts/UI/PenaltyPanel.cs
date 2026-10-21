using System;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The flag panel: penalty name, team, player number, yardage and the referee's announcement, with ACCEPT / DECLINE cards showing the result of each
    /// (down, distance, spot). When the CPU decides the same panel is shown for a moment with the CPU's choice marked.
    /// </summary>
    public sealed class PenaltyPanel : MonoBehaviour
    {
        RectTransform _root;
        TextMeshProUGUI _flag, _headline, _meta, _referee, _timer;
        Image _accept, _decline, _acceptOutline, _declineOutline, _star;
        TextMeshProUGUI _accTitle, _accText, _decTitle, _decText, _recommend;
        RectTransform _prompts;
        Action<bool> _onDecision;
        string _sig = "";

        public static PenaltyPanel Create(Transform parent)
        {
            var r = UiKit.Rect(parent, "Penalty"); UiKit.Stretch(r);
            var p = r.gameObject.AddComponent<PenaltyPanel>(); p._root = r; p.Build(); r.gameObject.SetActive(false);
            return p;
        }

        void Build()
        {
            var scrim = UiKit.Panel(_root, "Scrim", new Color(0.02f, 0.03f, 0.06f, 0.72f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var box = UiKit.Panel(_root, "Box", new Color(0.07f, 0.08f, 0.11f, 1f)); UiKit.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1180, 700));
            var strip = UiKit.Panel(box.rectTransform, "Strip", new Color(1f, 0.918f, 0f, 1f)); UiKit.Anchor(strip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 74)); strip.rectTransform.anchorMax = new Vector2(1, 1); strip.rectTransform.sizeDelta = new Vector2(0, 74);
            _flag = UiKit.Label(strip.rectTransform, "FLAG ON THE PLAY", 46, Color.black, true, TextAlignmentOptions.Center, "Flag"); UiKit.Stretch(_flag.rectTransform); _flag.characterSpacing = 8f;
            _headline = UiKit.Label(box.rectTransform, "", 64, Color.white, true, TextAlignmentOptions.Center, "Headline"); UiKit.Anchor(_headline.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(1100, 84)); _headline.characterSpacing = 3f;
            _meta = UiKit.Label(box.rectTransform, "", 34, UiColors.Gold, true, TextAlignmentOptions.Center, "Meta"); UiKit.Anchor(_meta.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -180), new Vector2(1100, 50)); _meta.characterSpacing = 3f;
            _referee = UiKit.Label(box.rectTransform, "", 28, UiColors.TextSecondary, false, TextAlignmentOptions.Top, "Referee"); UiKit.Anchor(_referee.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -236), new Vector2(1080, 90));
            _referee.textWrappingMode = TextWrappingModes.Normal; _referee.fontStyle = FontStyles.Italic;
            // option cards
            BuildCard(box.rectTransform, true, out _accept, out _acceptOutline, out _accTitle, out _accText, -285f);
            BuildCard(box.rectTransform, false, out _decline, out _declineOutline, out _decTitle, out _decText, 285f);
            _recommend = UiKit.Label(box.rectTransform, "COACH SUGGESTS", 22, UiColors.Gold, true, TextAlignmentOptions.Center, "Recommend"); UiKit.Anchor(_recommend.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 112), new Vector2(400, 30)); _recommend.characterSpacing = 3f;
            _timer = UiKit.Label(box.rectTransform, "", 24, UiColors.TextMuted, true, TextAlignmentOptions.Center, "Timer"); UiKit.Anchor(_timer.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(600, 30));
            _prompts = UiKit.Rect(box.rectTransform, "Prompts"); UiKit.Anchor(_prompts, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 52), new Vector2(1000, 56));
        }

        void BuildCard(RectTransform parent, bool accept, out Image bg, out Image outline, out TextMeshProUGUI title, out TextMeshProUGUI text, float x)
        {
            outline = UiKit.Panel(parent, accept ? "AccOutline" : "DecOutline", UiColors.Gold);
            UiKit.Anchor(outline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, -70), new Vector2(540, 230));
            bg = UiKit.Panel(outline.rectTransform, "Bg", accept ? UiColors.AccentBlue : UiColors.Row); UiKit.Stretch(bg.rectTransform, 5, 5, 5, 5);
            title = UiKit.Label(bg.rectTransform, accept ? "ACCEPT" : "DECLINE", 44, Color.white, true, TextAlignmentOptions.TopLeft, "Title"); UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -16), new Vector2(480, 56)); title.characterSpacing = 4f;
            text = UiKit.Label(bg.rectTransform, "", 28, Color.white, false, TextAlignmentOptions.TopLeft, "Text"); UiKit.Anchor(text.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -78), new Vector2(490, 140));
            text.textWrappingMode = TextWrappingModes.Normal; text.lineSpacing = 8f;
        }

        public void Show(PenaltyPanelData d, Action<bool> onDecision)
        {
            _onDecision = onDecision; _sig = "";
            _root.gameObject.SetActive(true); _root.transform.SetAsLastSibling();
            Refresh(d);
        }

        public void Refresh(PenaltyPanelData d)
        {
            if (!_root.gameObject.activeSelf) return;
            string sig = d.Selected + "|" + d.Headline + "|" + d.UserDecides + MatchInput.GamepadActive;
            _headline.text = d.Headline.ToUpperInvariant();
            _meta.text = d.Team + (string.IsNullOrEmpty(d.Player) ? "" : "    " + d.Player) + "    " + d.Yards;
            _referee.text = "\"" + d.Referee + "\"";
            _flag.text = d.DeadBall ? "FLAG - DEAD BALL" : "FLAG ON THE PLAY";
            _accTitle.text = d.AcceptTitle; _accText.text = d.AcceptText; _decTitle.text = d.DeclineTitle; _decText.text = d.DeclineText;
            _acceptOutline.enabled = d.Selected == 0; _declineOutline.enabled = d.Selected == 1;
            _accept.color = d.Selected == 0 ? UiColors.AccentBlueHot : UiColors.AccentBlue; _decline.color = d.Selected == 1 ? UiColors.AccentBlueHot : UiColors.Row;
            _recommend.gameObject.SetActive(d.UserDecides);
            _recommend.text = d.Recommended ? "COACH SUGGESTS:  ACCEPT" : "COACH SUGGESTS:  DECLINE";
            _timer.text = d.UserDecides ? "DECIDES IN " + Mathf.CeilToInt(d.Seconds) : (d.Selected == 0 ? "CPU ACCEPTS THE PENALTY" : "CPU DECLINES THE PENALTY");
            if (sig != _sig)
            {
                _sig = sig;
                var hints = new System.Collections.Generic.List<HintPair>();
                if (d.UserDecides)
                {
                    hints.Add(new HintPair("glyph_dpad_left", "CHOOSE")); hints.Add(new HintPair("glyph_a", "CONFIRM")); hints.Add(new HintPair("glyph_x", "ACCEPT")); hints.Add(new HintPair("glyph_b", "DECLINE"));
                }
                UiPrompts.FillRow(_prompts, hints, 40f, 24f, 40f);
            }
        }

        public void Hide() { _root.gameObject.SetActive(false); _onDecision = null; }
        public bool IsOpen { get { return _root != null && _root.gameObject.activeSelf; } }
    }
}


