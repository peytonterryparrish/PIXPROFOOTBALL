using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// Themed frame of the settings screen (direction 16): one wide panel centred under the top bar (title, page counter, tabs, the page holder with the option rows in two columns, BACK). The pages themselves are
    /// built by the same code as Classic through the theme-aware control factories; the rows flow down the left column first, then the right one.
    /// </summary>
    public partial class SettingsPanel
    {
        GlassTabs _glassTabs;
        readonly System.Collections.Generic.List<GridLayoutGroup> _grids = new System.Collections.Generic.List<GridLayoutGroup>();

        public const float RowW = 760f, RowH = 64f, ColGap = 28f, CardW = 2f * RowW + ColGap + 2f * 28f + 24f, HolderH = 5f * RowH + 4f * 10f;

        void BuildGlassFrame(RectTransform root, out RectTransform col, out RectTransform holder)
        {
            var t = GlassTheme.T;
            // laid out for a 1080 high canvas: the block (card at its design position) is centred and shrinks on shorter canvases (4:3) so BACK never runs into the footer
            var host = UiKit.Rect(root, "FitHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(60f, 70f); host.offsetMax = new Vector2(-60f, -140f);
            var size = new Vector2(1800f, 784f);
            var block = UiKit.Rect(host, "Block"); block.anchorMin = block.anchorMax = new Vector2(0.5f, 1f); block.pivot = new Vector2(0.5f, 1f); block.anchoredPosition = Vector2.zero; block.sizeDelta = size;
            var fit = host.gameObject.AddComponent<FitScale>(); fit.Block = block; fit.DesignSize = size;
            var card = GlassKit.Panel(block, "SettingsCard", 1.15f, true);
            var cr = card.rectTransform;
            UiKit.Anchor(cr, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(CardW, 660f));
            var v = cr.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 22, 24); v.spacing = 12f; v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            float inner = CardW - 56f;

            // title row: SETTINGS on the left, "LB  PAGE 1 / 5  RB" on the right
            var titleRow = UiKit.Rect(cr, "TitleRow"); titleRow.sizeDelta = new Vector2(inner, 56f);
            var tle = titleRow.gameObject.AddComponent<LayoutElement>(); tle.preferredWidth = inner; tle.preferredHeight = 56f;
            var title = GlassKit.Text(titleRow, "SETTINGS", GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Title");
            GlassKit.Style(title, 42f, t.textPrimary, true, t.trackingTitle); UiKit.Stretch(title.rectTransform, 0f, 0f, 460f, 0f);
            _pageLabel = GlassKit.Eyebrow(titleRow, "", TextAlignmentOptions.MidlineRight, "PageLabel");
            GlassKit.Style(_pageLabel, 20f, t.textSecondary, true, 3f);
            UiKit.Anchor(_pageLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(440f, 36f));
            var rule = GlassKit.Rule(cr); var rle = rule.gameObject.GetComponent<LayoutElement>(); rle.preferredWidth = inner;

            // tabs (the same SetPage logic as Classic: LB / RB paging stays in Update)
            string[] tabNames = { "VIDEO", "GAMEPLAY", "CAMERA", "BROADCAST", "AUDIO" };
            _glassTabs = GlassKit.Tabs(cr, tabNames, 0, i => SetPage(i), 52f, 22f);
            var tabLe = _glassTabs.gameObject.AddComponent<LayoutElement>(); tabLe.preferredWidth = inner; tabLe.preferredHeight = 52f;
            foreach (var b in _glassTabs.Buttons) _tabs.Add(b);

            holder = UiKit.Rect(cr, "Pages"); holder.sizeDelta = new Vector2(inner, HolderH);
            var hle = holder.gameObject.AddComponent<LayoutElement>(); hle.preferredWidth = inner; hle.preferredHeight = HolderH;
            col = cr;
        }

        /// <summary>Themed page: a grid that fills column by column (two columns of option rows).</summary>
        void AddGlassPageGrid(RectTransform page)
        {
            var g = page.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(RowW, RowH); g.spacing = new Vector2(ColGap, 10f);
            g.startAxis = GridLayoutGroup.Axis.Vertical; g.startCorner = GridLayoutGroup.Corner.UpperLeft; g.childAlignment = TextAnchor.UpperLeft;
            g.constraint = GridLayoutGroup.Constraint.FixedRowCount; g.constraintCount = 5;
            _grids.Add(g);
        }

        /// <summary>Called once every page has its rows: each page uses as many rows per column as it needs for two balanced columns (at most five).</summary>
        void FinishGlassPages()
        {
            foreach (var g in _grids)
            {
                int n = 0;
                for (int i = 0; i < g.transform.childCount; i++)
                {
                    var le = g.transform.GetChild(i).GetComponent<LayoutElement>();
                    if (le != null && le.ignoreLayout) continue;
                    n++;
                }
                g.constraintCount = Mathf.Clamp((n + 1) / 2, 1, 5);
            }
        }
    }
}
