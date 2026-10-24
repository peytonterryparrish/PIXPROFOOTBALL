using System.Collections.Generic;
using PixFootball.UI;
using PixFootball.UI.Glass;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds the glass component prefabs under Assets/_Project/UI/Glass/Prefabs from the same factory code the screens use (GlassKit / GlassWidgets), so a prefab and a runtime-built element are identical.
    /// Rules: licensed sprites are never referenced (icons keep their key in GlassIconKey and re-resolve at runtime, other sprites that are not project assets are cleared) and runtime click handlers are not serialised
    /// (a prefab is a visual template; callers wire behaviour). Menu: PIX Football/Glass UI/Build prefabs. Batch: -executeMethod PixFootball.EditorTools.GlassPrefabBuilder.BuildAll.
    /// </summary>
    public static partial class GlassPrefabBuilder
    {
        public const string Dir = "Assets/_Project/UI/Glass/Prefabs";

        [MenuItem("PIX Football/Glass UI/Build prefabs")]
        public static void BuildAll() { Build(UiThemeKind.Glass, "Glass", Dir); }

        /// <summary>The Direction 16 set: the same components drawn by the Tile look plus the tile-only ones (tab bar, tile, list row, play card, ticker), saved as Tile*.prefab under Prefabs/Tile.</summary>
        [MenuItem("PIX Football/Glass UI/Build tile prefabs")]
        public static void BuildTile() { Build(UiThemeKind.Tile, "Tile", Dir + "/Tile"); }

        static void Build(UiThemeKind kind, string prefix, string dir)
        {
            UiTheme.Force(kind);
            GlassTheme.ResetCache();
            MaterialFactory.EnsureFolder(Dir); MaterialFactory.EnsureFolder(dir);
            var canvas = UiKit.CreateCanvas("PrefabBuildCanvas", 0);
            var host = (RectTransform)new GameObject("Host", typeof(RectTransform)).transform;
            host.SetParent(canvas.transform, false);
            host.sizeDelta = new Vector2(1920f, 1080f);
            int n = 0;
            try
            {
                var items = new List<KeyValuePair<string, System.Func<Transform, GameObject>>>
                {
                    New("GlassPanel", p => GlassKit.Panel(p, "GlassPanel", 1f, true).gameObject),
                    New("GlassWell", p => GlassKit.Well(p, "GlassWell").gameObject),
                    New("GlassCard", p => GlassWidgets.Card(p, "CARD TITLE", 560f, 360f, "GlassCard").Root.gameObject),
                    New("GlassButtonPrimary", p => GlassKit.Button(p, "PRIMARY", null, GlassButtonStyle.Primary, 320f, 64f).gameObject),
                    New("GlassButtonSecondary", p => GlassKit.Button(p, "SECONDARY", null, GlassButtonStyle.Secondary, 320f, 64f).gameObject),
                    New("GlassButtonAccent", p => GlassKit.Button(p, "ACCENT", null, GlassButtonStyle.Accent, 320f, 64f).gameObject),
                    New("GlassButtonGhost", p => GlassKit.Button(p, "GHOST", null, GlassButtonStyle.Ghost, 320f, 64f).gameObject),
                    New("GlassMenuItem", p => GlassKit.MenuItem(p, "MENU ITEM", "ui_arrow_right", null, 560f, 88f).gameObject),
                    New("GlassListRow", p => GlassWidgets.CompareRow(p, "74", "STAT LABEL", "82", 720f, 56f, false).gameObject),
                    New("GlassTabs", p => GlassKit.Tabs(p, new[] { "TAB ONE", "TAB TWO", "TAB THREE" }, 0, null).gameObject),
                    New("GlassNavBar", p => GlassWidgets.TopBar(p, "SECTION", new[] { "ONE", "TWO" }, 0, null, true).Root.gameObject),
                    New("GlassControllerFooter", p => GlassWidgets.BottomBar(p, "v0").Root.gameObject),
                    New("GlassToggleRow", p => GlassKit.ToggleRow(p, "TOGGLE", true, null).gameObject),
                    New("GlassSliderRow", p => GlassKit.SliderRow(p, "SLIDER", 0.5f, null).gameObject),
                    New("GlassStepper", p => GlassKit.Stepper(p, "STEPPER", new[] { "LOW", "MEDIUM", "HIGH" }, 1, null).gameObject),
                    New("GlassDropdown", p => GlassWidgets.Dropdown(p, "DROPDOWN", new[] { "ONE", "TWO", "THREE" }, 0, null).gameObject),
                    New("GlassModal", p => { var w = UiKit.Rect(p, "GlassModal"); UiKit.Stretch(w); GlassWidgets.BuildDialog(w, "Title", "Message text goes here.", "CONFIRM", "CANCEL", null, null); return w.gameObject; }),
                    New("GlassToast", p => GlassToast.Create(p).gameObject),
                    New("GlassTooltipHost", p => { var b = GlassKit.Button(p, "HOVER ME", null, GlassButtonStyle.Secondary, 320f, 64f); GlassTooltip.Attach(b, "Tooltip text"); return b.gameObject; }),
                    New("GlassProgressBar", p => GlassKit.ProgressBar(p, 0.6f, 420f, 10f, true, "GlassProgressBar").gameObject),
                    New("GlassRatingBar", p => GlassWidgets.RatingBar(p, "RATING", 0.7f, "70", 400f).gameObject),
                    New("GlassStatTile", p => GlassWidgets.StatTile(p, "24", "CAPTION", 150f, 96f).gameObject),
                    New("GlassChip", p => GlassWidgets.Chip(p, "CHIP", false).gameObject),
                    New("GlassPlayerCard", p => GlassPlayerCard.Create(p, "First", "Last", "QB", 88, new[] { "SPD", "THP", "ACC" }, new[] { 80, 90, 85 }).Root.gameObject),
                    New("GlassCrestTile", p => GlassWidgets.MakeCrestTile(p, 0, null, "ABC", "TEAM", new Color(0.8f, 0.2f, 0.2f), null).gameObject),
                };
                if (kind == UiThemeKind.Tile)
                {
                    items.Add(New("TileTabBar", p => GlassKit.TabBar(p, new[] { "HOME", "PLAY", "SHARE" }, 0, null).Bar.gameObject));
                    items.Add(New("TileStripTabs", p => GlassKit.StripTabs(p, new[] { "SUGGESTIONS", "FORMATION" }, 0, null).gameObject));
                    items.Add(New("TileHubTile", p => { var b = UiKit.Rect(p, "TileHost"); b.sizeDelta = new Vector2(380f, 292f); return GlassKit.Tile(b, new HubTileSpec { Title = "Franchise", Subtitle = "Season 3", Badge = "RESUME", IconKey = "fb_franchise_hub", Flat = true, Pos = Vector2.zero, Size = TileMetrics.Standard }).Cell.gameObject; }));
                    items.Add(New("TileListRow", p => GlassKit.ListRow(p, "List row", "Sub line", 480f, 124f, null, true).gameObject));
                    items.Add(New("TilePlayCard", p => GlassKit.PlayCard(p, "Play name", TagKind.Pass, "Formation").Root.gameObject));
                    items.Add(New("TileTagChip", p => GlassKit.TagChip(p, "PASS", TagKind.Pass).gameObject));
                    items.Add(New("TileTicker", p => { var h = UiKit.Rect(p, "TickerHost"); h.sizeDelta = new Vector2(1920f, 46f); var tk = GlassTicker.Build(h, "NEWS", 46f, 20f); tk.SetHeadlines(new[] { "Headline one", "Headline two" }); return h.gameObject; }));
                    items.Add(New("TileModal", p => { var w = UiKit.Rect(p, "TileModal"); UiKit.Stretch(w); GlassWidgets.Modal(w, "MODAL", 900f, 440f); return w.gameObject; }));
                    items.Add(New("TileTableBlock", p => GlassKit.TableBlock(p, "GROUP", new[] { "", "W", "L" }, new[] { new[] { "1  TEAM", "5", "1" }, new[] { "2  TEAM", "4", "2" } }, new[] { 3f, 1f, 1f }, 600f, 0).gameObject));
                }
                foreach (var kv in items)
                {
                    var go = kv.Value(host);
                    if (go == null) { Debug.LogWarning("[GlassPrefabs] skipped " + kv.Key); continue; }
                    string key = kv.Key.StartsWith("Glass") ? prefix + kv.Key.Substring(5) : kv.Key;
                    go.name = key;
                    Sanitize(go);
                    PrefabUtility.SaveAsPrefabAsset(go, dir + "/" + key + ".prefab");
                    n++;
                    Object.DestroyImmediate(go);
                    // helpers may leave siblings (shades, scrims) under the host: clear it between items
                    for (int i = host.childCount - 1; i >= 0; i--) Object.DestroyImmediate(host.GetChild(i).gameObject);
                }
            }
            finally
            {
                Object.DestroyImmediate(canvas.gameObject);
                UiTheme.Force(null);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GlassPrefabs] saved " + n + " prefabs to " + dir);
        }

        static KeyValuePair<string, System.Func<Transform, GameObject>> New(string name, System.Func<Transform, GameObject> f)
        {
            return new KeyValuePair<string, System.Func<Transform, GameObject>>(name, f);
        }

        /// <summary>Clears sprites that are not committed project assets (runtime gradients, licensed icons re-resolved by GlassIconKey) and licensed raw textures.</summary>
        static void Sanitize(GameObject root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null) continue;
                string path = AssetDatabase.GetAssetPath(img.sprite);
                bool licensed = !string.IsNullOrEmpty(path) && path.Contains("_Licensed");
                if (licensed || string.IsNullOrEmpty(path) || !AssetDatabase.Contains(img.sprite)) { img.sprite = null; if (img.color == Color.white) img.color = new Color(0.08f, 0.085f, 0.1f, 1f); }   // a runtime gradient is a plain dark plate in the prefab
            }
            foreach (var raw in root.GetComponentsInChildren<RawImage>(true)) raw.texture = null;
        }
    }
}
