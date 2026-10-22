using PixFootball.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// On-field coin toss support: while the backdrop is on, the toss modal keeps the field visible (transparent scrim, small title at the top,
    /// buttons in the lower third) and a 2K8-style caption strip ("Rollers choose tails.") runs along the bottom of the screen.
    /// </summary>
    public partial class MatchHud
    {
        bool _tossOnField;
        RectTransform _tossCaption;
        TextMeshProUGUI _tossCaptionText;
        Image _tossBand;

        /// <summary>True: the coin toss modal lets the field show through (referee and captains are on screen). False (default): the full-screen opaque modal.</summary>
        public void SetTossBackdrop(bool onField)
        {
            _tossOnField = onField;
            ApplyTossLayout(); // re-lay out a modal that is already up
        }

        /// <summary>The slim caption strip over the field while the toss is on; an empty string hides it. Independent of the toss modal (it lives under the HUD root).</summary>
        public void SetTossCaption(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (!show && _tossCaption == null) return;
            if (_tossCaption == null) BuildTossCaption();
            if (_tossCaption == null) return;
            _tossCaptionText.text = show ? text : "";
            _tossCaption.gameObject.SetActive(show);
            if (show)
            {
                // above the HUD chrome, below the toss modal (buttons) and the fade
                _tossCaption.SetAsLastSibling();
                if (_tossRoot != null) _tossRoot.transform.SetAsLastSibling();
                if (_fade != null) _fade.transform.SetAsLastSibling();
            }
        }

        void BuildTossCaption()
        {
            if (_root == null) return;
            if (_glassHud) { BuildTossCaptionGlass(); return; }
            var bar = UiKit.Rect(_root, "TossCaption");
            UiKit.Anchor(bar, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 40), new Vector2(0, 64));
            bar.anchorMax = new Vector2(1, 0); bar.sizeDelta = new Vector2(0, 64); // full width, 64 high, 40 above the bottom edge
            var bg = bar.gameObject.AddComponent<Image>(); bg.color = new Color(0.04f, 0.05f, 0.09f, 0.82f); bg.raycastTarget = false;
            var accent = UiKit.Panel(bar, "Accent", UiColors.Gold);
            UiKit.Anchor(accent.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(10, 0));
            accent.rectTransform.anchorMax = new Vector2(0, 1); accent.rectTransform.sizeDelta = new Vector2(10, 0);
            var rail = UiKit.Panel(bar, "Rail", new Color(1f, 1f, 1f, 0.85f));
            UiKit.Anchor(rail.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 3));
            rail.rectTransform.anchorMax = new Vector2(1, 1); rail.rectTransform.sizeDelta = new Vector2(0, 3);
            _tossCaptionText = UiKit.Label(bar, "", 38, Color.white, true, TextAlignmentOptions.MidlineLeft, "Text");
            UiKit.Stretch(_tossCaptionText.rectTransform, 44, 0, 44, 0); _tossCaptionText.characterSpacing = 1.5f;
            _tossCaption = bar;
            bar.gameObject.SetActive(false);
        }

        /// <summary>Positions the toss modal's pieces for the current backdrop mode. With the backdrop off every value equals the original full-screen layout.</summary>
        void ApplyTossLayout()
        {
            if (_tossRoot == null) return;
            bool f = _tossOnField;
            var root = (RectTransform)_tossRoot.transform;
            var scrim = root.Find("Scrim") as RectTransform;
            if (scrim != null) { var im = scrim.GetComponent<Image>(); if (im != null) im.color = f ? new Color(0.02f, 0.03f, 0.06f, 0f) : new Color(0.02f, 0.03f, 0.06f, 0.88f); }
            // a soft dark band behind the small title so it reads over bright crowd and grass
            var bandT = root.Find("TopBand") as RectTransform;
            if (f && bandT == null)
            {
                _tossBand = UiKit.Panel(root, "TopBand", new Color(0.02f, 0.03f, 0.06f, 0.5f));
                UiKit.Anchor(_tossBand.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 240));
                _tossBand.rectTransform.anchorMax = new Vector2(1, 1); _tossBand.rectTransform.sizeDelta = new Vector2(0, 240);
                _tossBand.rectTransform.SetSiblingIndex(scrim != null ? scrim.GetSiblingIndex() + 1 : 0);
                bandT = _tossBand.rectTransform;
            }
            if (bandT != null) bandT.gameObject.SetActive(f);
            var title = root.Find("Title") as RectTransform;
            if (title != null)
            {
                var tt = title.GetComponent<TextMeshProUGUI>(); if (tt != null) tt.fontSize = f ? 64f : 92f;
                UiKit.Anchor(title, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, f ? 430f : 250f), new Vector2(1500, f ? 90f : 120f));
            }
            var rule = root.Find("Rule") as RectTransform;
            if (rule != null) UiKit.Anchor(rule, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, f ? 384f : 178f), new Vector2(f ? 160f : 220f, f ? 5f : 7f));
            var sub = root.Find("Sub") as RectTransform;
            if (sub != null)
            {
                var st = sub.GetComponent<TextMeshProUGUI>(); if (st != null) st.fontSize = f ? 30f : 36f;
                UiKit.Anchor(sub, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, f ? 340f : 110f), new Vector2(1500, f ? 50f : 60f));
            }
            var buttons = root.Find("Buttons") as RectTransform;
            if (buttons != null) UiKit.Anchor(buttons, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, f ? -330f : -110f), new Vector2(560, 300));
            var elect = root.Find("Elect") as RectTransform;
            if (elect != null) UiKit.Anchor(elect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, f ? -300f : -20f), new Vector2(1200, 70));
        }
    }
}
