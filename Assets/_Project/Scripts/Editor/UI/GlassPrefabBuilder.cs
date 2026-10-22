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
    public static class GlassPrefabBuilder
    {
        public const string Dir = "Assets/_Project/UI/Glass/Prefabs";

        [MenuItem("PIX Football/Glass UI/Build prefabs")]
        public static void BuildAll()
        {
            UiTheme.Force(UiThemeKind.Glass);
            GlassTheme.ResetCache();
            MaterialFactory.EnsureFolder(Dir);
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
                foreach (var kv in items)
                {
                    var go = kv.Value(host);
                    if (go == null) { Debug.LogWarning("[GlassPrefabs] skipped " + kv.Key); continue; }
                    go.name = kv.Key;
                    Sanitize(go);
                    PrefabUtility.SaveAsPrefabAsset(go, Dir + "/" + kv.Key + ".prefab");
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
            Debug.Log("[GlassPrefabs] saved " + n + " prefabs to " + Dir);
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
                if (licensed || string.IsNullOrEmpty(path) || !AssetDatabase.Contains(img.sprite)) img.sprite = null;
            }
            foreach (var raw in root.GetComponentsInChildren<RawImage>(true)) raw.texture = null;
        }
    }
}
