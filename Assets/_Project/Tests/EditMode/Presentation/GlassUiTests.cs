using NUnit.Framework;
using PixFootball.UI;
using PixFootball.UI.Glass;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>EditMode tests of the glass UI: token / theme loading, the theme switch and the pure navigation models.</summary>
    public class GlassUiTests
    {
        [TearDown] public void TearDown() { UiTheme.Force(null); GlassTheme.ResetCache(); }

        // ------------------------------------------------------------------ tokens
        [Test]
        public void TokensDefaultToThePackDesignTokens()
        {
            var t = new GlassTokens();
            AssertColor(t.surfaceTop, "#66666A"); AssertColor(t.surfaceMid, "#242426"); AssertColor(t.surfaceBottom, "#555558");
            Assert.AreEqual(0.48f, t.opacityTop, 1e-4); Assert.AreEqual(0.40f, t.opacityMid, 1e-4); Assert.AreEqual(0.38f, t.opacityBottom, 1e-4);
            AssertColor(t.accent, "#F49A52"); AssertColor(t.textPrimary, "#F5F5F5"); AssertColor(t.textSecondary, "#BFC1C4");
            AssertColor(t.edgeA, "#DBDBDE"); AssertColor(t.edgeB, "#77777B"); AssertColor(t.edgeC, "#E0E0E3");
            AssertColor(t.actionLeft, "#F3F3F3"); AssertColor(t.actionRight, "#C7C9CC");
        }

        static void AssertColor(Color c, string hex)
        {
            Assert.AreEqual(GlassTokens.Hex(hex).r, c.r, 1e-3, hex + " r"); Assert.AreEqual(GlassTokens.Hex(hex).g, c.g, 1e-3, hex + " g"); Assert.AreEqual(GlassTokens.Hex(hex).b, c.b, 1e-3, hex + " b");
        }

        [Test]
        public void TokensRoundTripThroughJson()
        {
            var t = new GlassTokens { cornerRadius = 21f, blurRadius = 33f, accent = new Color(0.1f, 0.2f, 0.3f, 1f) };
            var back = GlassTokens.FromJson(t.ToJson());
            Assert.AreEqual(21f, back.cornerRadius); Assert.AreEqual(33f, back.blurRadius);
            Assert.AreEqual(0.2f, back.accent.g, 1e-4);
            // untouched fields keep their defaults when the json is partial
            var partial = GlassTokens.FromJson("{\"cornerRadius\": 5}");
            Assert.AreEqual(5f, partial.cornerRadius); Assert.AreEqual(0.48f, partial.opacityTop, 1e-4);
            Assert.IsNotNull(GlassTokens.FromJson(""));
        }

        [Test]
        public void SurfaceGradientHitsTheThreeStops()
        {
            var t = new GlassTokens();
            Assert.AreEqual(t.opacityTop, t.SurfaceAt(0f).a, 1e-4);
            Assert.AreEqual(t.opacityMid, t.SurfaceAt(0.55f).a, 1e-4);
            Assert.AreEqual(t.opacityBottom, t.SurfaceAt(1f).a, 1e-4);
            Assert.AreEqual(t.surfaceMid.r, t.SurfaceAt(0.55f).r, 1e-4);
            Assert.AreEqual(t.surfaceTop.r, t.SurfaceAt(-3f).r, 1e-4);   // clamped
        }

        [Test]
        public void TextStaysReadableOnTheBrightestGlass()
        {
            // the pack's own contrast reference: white on the brightest glass plus sheen (#555658) is 6.75, grey 4.08 (large text)
            var t = new GlassTokens();
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textPrimary, GlassTokens.Hex("#555658")), 6.5f);
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textPrimary, GlassTokens.Hex("#1B1C1E")), 15f);
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textSecondary, GlassTokens.Hex("#1B1C1E")), 9f);
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textOnAction, t.actionRight), 8f, "dark type on the silver action surface");
        }

        [Test]
        public void ThemeAlwaysResolvesEvenWithoutTheAsset()
        {
            GlassTheme.ResetCache();
            Assert.IsNotNull(GlassTheme.Current);
            Assert.IsNotNull(GlassTheme.T);
            Assert.Greater(GlassTheme.T.motionBase, 0.1f); Assert.Less(GlassTheme.T.motionSlow, 0.21f);   // 120-200 ms motion range
            Assert.GreaterOrEqual(GlassTheme.T.motionFast, 0.12f - 1e-4f);
        }

        // ------------------------------------------------------------------ theme switch
        [Test]
        public void ThemeDefaultsToClassicUntilGlassIsComplete()
        {
            Assert.AreEqual(UiThemeKind.Classic, UiTheme.DefaultKind);
        }

        [Test]
        public void ForcedThemeOverridesAndReleases()
        {
            UiTheme.Force(UiThemeKind.Glass); Assert.IsTrue(UiTheme.IsGlass);
            UiTheme.Force(UiThemeKind.Classic); Assert.IsFalse(UiTheme.IsGlass);
            UiTheme.Force(null);
            Assert.AreEqual(UiThemeKind.Glass, UiTheme.FromIndex(0)); Assert.AreEqual(UiThemeKind.Classic, UiTheme.FromIndex(1)); Assert.AreEqual(UiThemeKind.Glass, UiTheme.FromIndex(7));
            Assert.AreEqual(2, UiTheme.Names.Length);
        }

        // ------------------------------------------------------------------ backdrop rules
        [Test]
        public void LowTierUsesTheFrostedFallbackAndHigherTiersTheLiveBlur()
        {
            Assert.IsFalse(GlassBackdrop.TierAllowsBlur(0));
            for (int i = 1; i <= 3; i++) Assert.IsTrue(GlassBackdrop.TierAllowsBlur(i));
        }

        [Test]
        public void ValidExtentIsTheViewportShareOfTheBlurTexture()
        {
            var e = GlassBackdrop.ValidExtent(new Vector2Int(960, 540), new Vector2Int(1280, 720));
            Assert.AreEqual(0.75f, e.x, 1e-4); Assert.AreEqual(0.75f, e.y, 1e-4);
            var full = GlassBackdrop.ValidExtent(new Vector2Int(1280, 720), new Vector2Int(1280, 720)); Assert.AreEqual(1f, full.x);
            var degenerate = GlassBackdrop.ValidExtent(new Vector2Int(10, 10), new Vector2Int(0, 0)); Assert.AreEqual(1f, degenerate.x);
        }

        // ------------------------------------------------------------------ flat-panel skin
        [Test]
        public void FlatPanelsBecomeTheRightGlassRole()
        {
            var big = new Vector2(300f, 100f);
            Assert.AreEqual(GlassSkinRole.Well, GlassImageSkin.Classify(new Color(0f, 0f, 0f, 1f), big, "DiagramBg"));
            Assert.AreEqual(GlassSkinRole.Glass, GlassImageSkin.Classify(new Color(0.10f, 0.11f, 0.15f, 0.97f), big, "Bg"));
            Assert.AreEqual(GlassSkinRole.Glass, GlassImageSkin.Classify(new Color(0.07f, 0.08f, 0.11f, 0.5f), big, "Plate"));
            Assert.AreEqual(GlassSkinRole.Selected, GlassImageSkin.Classify(GlassTheme.T.accent, big, "Row0"));
            Assert.AreEqual(GlassSkinRole.Solid, GlassImageSkin.Classify(new Color(0.8f, 0.1f, 0.1f, 1f), big, "TeamBar"));
            Assert.AreEqual(GlassSkinRole.Solid, GlassImageSkin.Classify(new Color(1f, 0.918f, 0f, 1f), big, "FlagStrip"));
            Assert.AreEqual(GlassSkinRole.Skip, GlassImageSkin.Classify(new Color(0.1f, 0.1f, 0.1f, 1f), new Vector2(300f, 8f), "Rail"), "thin bars stay as they are");
            Assert.AreEqual(GlassSkinRole.Skip, GlassImageSkin.Classify(new Color(0.02f, 0.03f, 0.06f, 0.9f), big, "Scrim"));
            Assert.AreEqual(GlassSkinRole.Skip, GlassImageSkin.Classify(new Color(1f, 1f, 1f, 0f), big, "Bg"));
        }

        // ------------------------------------------------------------------ prefabs and loading
        [Test]
        public void GlassPrefabsExistAndNeverReferenceLicensedArt()
        {
            const string dir = "Assets/_Project/UI/Glass/Prefabs";
            if (!System.IO.Directory.Exists(dir)) Assert.Ignore("prefabs not built yet (PIX Football/Glass UI/Build prefabs)");
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { dir });
            Assert.GreaterOrEqual(guids.Length, 24, "the component library");
            foreach (string g in guids)
                foreach (string dep in UnityEditor.AssetDatabase.GetDependencies(UnityEditor.AssetDatabase.GUIDToAssetPath(g), true))
                    Assert.IsFalse(dep.Contains("_Licensed"), UnityEditor.AssetDatabase.GUIDToAssetPath(g) + " references licensed art: " + dep);
        }

        [Test]
        public void LoadingScreenTitlesAndTips()
        {
            Assert.AreEqual("KICKOFF", GlassLoading.TitleFor(PixFootball.Gameplay.GameSession.MatchScene));
            Assert.AreEqual("FREE ROAM", GlassLoading.TitleFor(PixFootball.Gameplay.GameSession.StadiumScene));
            Assert.AreEqual("MAIN MENU", GlassLoading.TitleFor("MainMenu"));
            Assert.GreaterOrEqual(GlassLoading.Tips.Length, 4);
            foreach (var tip in GlassLoading.Tips) Assert.IsFalse(string.IsNullOrWhiteSpace(tip));
        }

        // ------------------------------------------------------------------ navigation models
        [Test]
        public void StepWrapsOrClamps()
        {
            Assert.AreEqual(0, GlassNav.Step(3, 1, 4, true)); Assert.AreEqual(3, GlassNav.Step(0, -1, 4, true));
            Assert.AreEqual(3, GlassNav.Step(3, 1, 4, false)); Assert.AreEqual(0, GlassNav.Step(0, -1, 4, false));
            Assert.AreEqual(0, GlassNav.Step(5, 1, 0, true));
        }

        [Test]
        public void GridMoveWalksTheThirtyTwoTeamGridAndStopsAtEdges()
        {
            int cols = 8, n = 32;
            Assert.AreEqual(1, GlassNav.GridMove(0, 1, 0, cols, n)); Assert.AreEqual(8, GlassNav.GridMove(0, 0, 1, cols, n));
            Assert.AreEqual(0, GlassNav.GridMove(0, -1, 0, cols, n), "left edge"); Assert.AreEqual(0, GlassNav.GridMove(0, 0, -1, cols, n), "top edge");
            Assert.AreEqual(31, GlassNav.GridMove(31, 1, 0, cols, n), "right edge of the last row"); Assert.AreEqual(31, GlassNav.GridMove(31, 0, 1, cols, n), "bottom edge");
            // every cell is reachable from cell 0 by right / down moves
            var seen = new bool[n];
            for (int y = 0; y < 4; y++) { int i = y * cols; for (int x = 0; x < cols; x++) { seen[i] = true; i = GlassNav.GridMove(i, 1, 0, cols, n); } }
            foreach (bool s in seen) Assert.IsTrue(s);
            // a short last row: moving down from a column that does not exist in it lands on the last cell
            Assert.AreEqual(29, GlassNav.GridMove(21, 0, 1, 8, 30));
            Assert.AreEqual(29, GlassNav.GridMove(23, 0, 1, 8, 30));
        }
    }
}
