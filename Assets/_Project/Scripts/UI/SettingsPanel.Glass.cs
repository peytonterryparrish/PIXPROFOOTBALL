using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Glass frame of the settings screen: one dense glass card (title, page counter, underline tabs, the page holder, BACK). The pages themselves are built by the same code as Classic through the theme-aware control factories.</summary>
    public partial class SettingsPanel
    {
        GlassTabs _glassTabs;

        void BuildGlassFrame(RectTransform root, out RectTransform col, out RectTransform holder)
        {
            var t = GlassTheme.T;
            // laid out for a 1080 high canvas: the block (card at its design position) shrinks about the top-left corner on shorter canvases (21:9, 4:3) so BACK never runs into the footer
            var block = UiKit.Rect(root, "Block"); block.anchorMin = block.anchorMax = new Vector2(0f, 1f); block.pivot = new Vector2(0f, 1f); block.anchoredPosition = Vector2.zero; block.sizeDelta = new Vector2(t.safeMarginX + 860f, 150f + 772f);
            var fit = root.gameObject.AddComponent<FitScale>(); fit.Block = block; fit.DesignSize = new Vector2(t.safeMarginX + 860f, 150f + 772f + 130f);
            var card = GlassKit.Panel(block, "SettingsCard", 1.15f, true);
            var cr = card.rectTransform;
            UiKit.Anchor(cr, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(t.safeMarginX, -150f), new Vector2(860f, 772f));
            var v = cr.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 22, 24); v.spacing = 12f; v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;

            // title row: SETTINGS on the left, "LB  PAGE 1 / 5  RB" on the right
            var titleRow = UiKit.Rect(cr, "TitleRow"); titleRow.sizeDelta = new Vector2(804f, 56f);
            var tle = titleRow.gameObject.AddComponent<LayoutElement>(); tle.preferredWidth = 804f; tle.preferredHeight = 56f;
            var title = GlassKit.Text(titleRow, "SETTINGS", GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Title");
            GlassKit.Style(title, 42f, t.textPrimary, true, t.trackingTitle); UiKit.Stretch(title.rectTransform, 0f, 0f, 300f, 0f);
            _pageLabel = GlassKit.Eyebrow(titleRow, "", TextAlignmentOptions.MidlineRight, "PageLabel");
            GlassKit.Style(_pageLabel, 20f, t.textSecondary, true, 3f);
            UiKit.Anchor(_pageLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(420f, 36f));
            var rule = GlassKit.Rule(cr); var rle = rule.gameObject.GetComponent<LayoutElement>(); rle.preferredWidth = 804f;

            // tabs (the same SetPage logic as Classic: LB / RB paging stays in Update)
            string[] tabNames = { "VIDEO", "GAMEPLAY", "CAMERA", "BROADCAST", "AUDIO" };
            _glassTabs = GlassKit.Tabs(cr, tabNames, 0, i => SetPage(i), 52f, 22f);
            var tabLe = _glassTabs.gameObject.AddComponent<LayoutElement>(); tabLe.preferredWidth = 804f; tabLe.preferredHeight = 52f;
            foreach (var b in _glassTabs.Buttons) _tabs.Add(b);

            holder = UiKit.Rect(cr, "Pages"); holder.sizeDelta = new Vector2(804f, 500f);
            var hle = holder.gameObject.AddComponent<LayoutElement>(); hle.preferredWidth = 804f; hle.preferredHeight = 500f;
            col = cr;
        }
    }
}
