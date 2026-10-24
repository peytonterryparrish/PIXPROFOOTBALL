using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.UI;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.Tests
{
    /// <summary>EditMode tests of the Tile (Direction 16 "Tile Hub Modern") look: tokens against the spec, contrast, the theme switch, the no-blur rule and the component entry points.</summary>
    public class TileUiTests
    {
        Canvas _canvas;
        [SetUp] public void SetUp() { UiTheme.Force(UiThemeKind.Tile); GlassTheme.ResetCache(); }
        [TearDown]
        public void TearDown()
        {
            if (_canvas != null) Object.DestroyImmediate(_canvas.gameObject);
            UiTheme.Force(null); GlassTheme.ResetCache();
        }

        RectTransform Host()
        {
            _canvas = UiKit.CreateCanvas("TileTestCanvas", 0);
            var host = UiKit.Rect(_canvas.transform, "Host"); host.sizeDelta = new Vector2(1920f, 1080f);
            return host;
        }

        static void AssertColor(Color c, string hex) { var h = GlassTokens.Hex(hex); Assert.AreEqual(h.r, c.r, 1e-3, hex + " r"); Assert.AreEqual(h.g, c.g, 1e-3, hex + " g"); Assert.AreEqual(h.b, c.b, 1e-3, hex + " b"); }

        // ------------------------------------------------------------------ tokens
        [Test]
        public void TileTokensAreTheSpecNumbers()
        {
            var t = GlassTokens.Tile();
            Assert.IsTrue(t.flat);
            AssertColor(t.gold, "#F2A63A"); AssertColor(t.goldHi, "#FFCF7A"); AssertColor(t.onGold, "#17130A");
            AssertColor(t.bgTop, "#17181C"); AssertColor(t.bgBottom, "#101114"); AssertColor(t.tickerBg, "#0C0D10"); AssertColor(t.footerBg, "#0A0B0D");
            AssertColor(t.tileFill, "#25272C"); AssertColor(t.panelFill, "#202228"); AssertColor(t.panelHead, "#15171B"); AssertColor(t.rowFill, "#1A1C20"); AssertColor(t.rowFocus, "#272A31");
            AssertColor(t.tabOff, "#1D1F24"); AssertColor(t.border, "#34373E"); AssertColor(t.borderPanel, "#3A3D45"); AssertColor(t.hairline, "#2C2F35");
            AssertColor(t.tagPass, "#4FC3FF"); AssertColor(t.tagRun, "#FF8F3A"); AssertColor(t.good, "#7EE08F"); AssertColor(t.bad, "#FF7A6A");
            AssertColor(t.glyphA, "#2F9E4F"); AssertColor(t.glyphB, "#D2423B"); AssertColor(t.glyphX, "#2F6FD6"); AssertColor(t.glyphY, "#E5B021");
            AssertColor(t.accent, "#F2A63A"); AssertColor(t.textPrimary, "#FFFFFF"); AssertColor(t.textMuted, "#8D919B");
            Assert.AreEqual(0f, t.cornerRadius); Assert.AreEqual(2f, t.restEdge); Assert.AreEqual(4f, t.focusEdge); Assert.AreEqual(-14f, t.tabSkew);
            Assert.AreEqual(1.035f, t.tileScale, 1e-4); Assert.AreEqual(1.02f, t.heroScale, 1e-4); Assert.AreEqual(1.04f, t.cardScale, 1e-4);
            Assert.AreEqual(112f, t.topBarHeight); Assert.AreEqual(46f, t.tickerHeight); Assert.AreEqual(62f, t.footerHeight); Assert.AreEqual(96f, t.tickerSpeed);
            Assert.AreEqual(0.16f, t.tabSlide, 1e-4); Assert.AreEqual(0.20f, t.crossfade, 1e-4); Assert.AreEqual(24f, t.crossDrift);
        }

        [Test]
        public void TheGlassTokensAreUntouchedByTheTileFields()
        {
            var g = new GlassTokens();
            Assert.IsFalse(g.flat);
            AssertColor(g.accent, "#F49A52"); Assert.AreEqual(12f, g.cornerRadius);
        }

        [Test]
        public void NothingInTheTileTypeScaleIsBelowTwenty()
        {
            var t = GlassTokens.Tile();
            foreach (float s in new[] { t.sizeCaption, t.sizeLabel, t.sizeBody, t.sizeHeading, t.sizeTitle, t.sizeHero, t.sizeTab, t.sizeTabSmall, t.sizeTileTitle, t.sizeHeroTitle, t.sizePanelTitle, t.sizeGroupTitle, t.sizeSelectedPlay, t.sizeCardTitle, t.sizeRowTitle, t.sizeTableBody, t.sizeTableHead, t.sizeSub, t.sizeBadge, t.sizeLegend })
                Assert.GreaterOrEqual(s, 20f);
        }

        [Test]
        public void LetterSpacingConvertsFromCssPixelsToTmpHundredthsOfAnEm()
        {
            Assert.AreEqual(3f / 28f * 100f, GlassTokens.Em(3f, 28f), 1e-3);
            Assert.AreEqual(0f, GlassTokens.Em(3f, 0f));
        }

        // ------------------------------------------------------------------ contrast (SPEC acceptance: 7:1 white on tile, 4.5:1 muted on row, 7:1 onGold on gold)
        [Test]
        public void ContrastMeetsTheSpecFloors()
        {
            var t = GlassTokens.Tile();
            foreach (var bg in new[] { t.tileFlatA, t.tileFlatB, t.tileFill, t.panelFill, t.panelHead, t.rowFill, t.rowFocus, t.tabOff, t.footerBg, t.tickerBg })
                Assert.GreaterOrEqual(GlassTokens.Contrast(t.textPrimary, bg), 7f, "white on " + ColorUtility.ToHtmlStringRGB(bg));
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textMuted, t.rowFill), 4.5f, "muted on the row fill");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textMuted, t.footerBg), 4.5f, "muted on the footer");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textTab, t.tabOff), 4.5f, "idle tab text");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textSecondary, t.rowFill), 7f, "secondary on a row");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.onGold, t.gold), 7f, "dark on gold");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.onGold, t.goldHi), 7f, "dark on light gold");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.tagText, t.tagPass), 7f); Assert.GreaterOrEqual(GlassTokens.Contrast(t.tagText, t.tagRun), 7f);
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.goldHi, t.panelFill), 7f, "light gold values on a panel");
        }

        // ------------------------------------------------------------------ theme switch
        [Test]
        public void ThemeSwitchKeepsAllThreeLooksBuildable()
        {
            Assert.IsTrue(UiTheme.IsTile); Assert.IsTrue(UiTheme.IsGlass); Assert.IsTrue(GlassTheme.T.flat);
            UiTheme.Force(UiThemeKind.Glass); Assert.IsFalse(UiTheme.IsTile); Assert.IsTrue(UiTheme.IsGlass); Assert.IsFalse(GlassTheme.T.flat);
            UiTheme.Force(UiThemeKind.Classic); Assert.IsFalse(UiTheme.IsGlass); Assert.IsFalse(UiTheme.IsTile);
            UiTheme.Force(UiThemeKind.Tile); Assert.IsTrue(GlassTheme.T.flat);
            foreach (var kind in new[] { UiThemeKind.Tile, UiThemeKind.Glass })
            {
                UiTheme.Force(kind);
                var host = Host();
                Assert.IsNotNull(GlassKit.Button(host, "OK", null, GlassButtonStyle.Primary), kind + " button");
                Assert.IsNotNull(GlassKit.Tabs(host, new[] { "ONE", "TWO" }, 0, null), kind + " tabs");
                Assert.IsNotNull(GlassWidgets.TopBar(host, "SECTION", new[] { "A", "B" }, 0, null, true), kind + " top bar");
                Assert.IsNotNull(GlassWidgets.BottomBar(host, "x"), kind + " footer");
                Assert.IsNotNull(GlassKit.ToggleRow(host, "T", true, null), kind + " toggle");
                Object.DestroyImmediate(_canvas.gameObject); _canvas = null;
            }
        }

        [Test]
        public void ThemeNamesIndexEqualsTheEnumValue()
        {
            for (int i = 0; i < UiTheme.Names.Length; i++) Assert.AreEqual(i, (int)UiTheme.FromIndex(i));
            Assert.AreEqual(UiThemeKind.Tile, UiTheme.FromIndex(2));
            Assert.AreEqual((int)UiThemeKind.Tile, UiTheme.ParseName("TILE")); Assert.AreEqual((int)UiThemeKind.Glass, UiTheme.ParseName("glass")); Assert.AreEqual((int)UiThemeKind.Classic, UiTheme.ParseName("Classic")); Assert.AreEqual(-1, UiTheme.ParseName("x"));
        }

        [Test]
        public void TheTileLookNeverAsksForTheBlurPass()
        {
            var host = Host();
            GlassKit.Panel(host, "P", 1f, true);
            Assert.IsFalse(GlassBackdrop.WantBlur, "no blur pass while Tile is active, even with surfaces on screen");
        }

        [Test]
        public void UiColorsFollowTheTileTokens()
        {
            AssertColor(UiColors.Gold, "#F2A63A"); AssertColor(UiColors.TextPrimary, "#FFFFFF"); AssertColor(UiColors.Row, "#1A1C20");
            UiTheme.Force(UiThemeKind.Glass); AssertColor(UiColors.Gold, "#F49A52");
        }

        // ------------------------------------------------------------------ components
        [Test]
        public void TileSizesMatchTheSpec()
        {
            Assert.AreEqual(new Vector2(780f, 470f), TileMetrics.Hero); Assert.AreEqual(new Vector2(380f, 292f), TileMetrics.Standard);
            Assert.AreEqual(new Vector2(480f, 290f), TileMetrics.Wide); Assert.AreEqual(new Vector2(480f, 162f), TileMetrics.WideShort);
            Assert.AreEqual(new Vector2(230f, 288f), TileMetrics.HalfTall); Assert.AreEqual(new Vector2(240f, 238f), TileMetrics.Half);
            Assert.AreEqual(new Vector2(500f, 250f), TileMetrics.Large); Assert.AreEqual(new Vector2(500f, 252f), TileMetrics.LargeShort);
            Assert.AreEqual(108f, TileMetrics.BottomClear);
        }

        [Test]
        public void TabBarMarksTheActiveTabAndSlidesThePlate()
        {
            var host = Host();
            int changed = -1;
            var tabs = GlassKit.TabBar(host, new[] { "HOME", "PLAY", "SHARE" }, 0, i => changed = i, false, true);
            Assert.IsTrue(tabs.Tile); Assert.AreEqual(3, tabs.Buttons.Length);
            var t = GlassTheme.T;
            Assert.AreEqual(t.onGold.r, tabs.Labels[0].color.r, 1e-3); Assert.AreEqual(t.textTab.r, tabs.Labels[1].color.r, 1e-3);
            Assert.AreEqual(tabs.SlotX[0], tabs.Plate.rectTransform.anchoredPosition.x, 1e-3);
            tabs.SetIndex(2, true);
            Assert.AreEqual(2, changed); Assert.AreEqual(t.onGold.r, tabs.Labels[2].color.r, 1e-3);
            tabs.SnapPlate();
            Assert.AreEqual(tabs.SlotX[2], tabs.Plate.rectTransform.anchoredPosition.x, 1e-3);
            Assert.AreEqual(tabs.SlotW[2], tabs.Plate.rectTransform.sizeDelta.x, 1e-3);
            Assert.AreEqual(t.tabHeight, tabs.Plate.rectTransform.sizeDelta.y, 1e-3);
            Assert.AreEqual(14f, tabs.Plate.Skew, 1e-3);
        }

        [Test]
        public void TileHasBadgeLockedStateAndFocusableButton()
        {
            var host = Host();
            var open = GlassKit.Tile(host, new HubTileSpec { Title = "Franchise", Subtitle = "Season 3", Badge = "RESUME", Flat = true, Size = TileMetrics.Standard, Click = () => { } });
            Assert.IsNotNull(open.Button);
            var f = open.Button.GetComponent<GlassFocusable>();
            Assert.IsNotNull(f); Assert.AreEqual(GlassTheme.T.tileScale, f.FocusScale, 1e-4); Assert.AreSame(open.Cell, f.RaiseTarget);
            Assert.AreEqual(GlassKind.Frame, f.Surface.Kind);
            var hero = GlassKit.Tile(host, new HubTileSpec { Title = "Play Now", Hero = true, Size = TileMetrics.Hero, Click = () => { } });
            Assert.AreEqual(GlassTheme.T.heroScale, hero.Button.GetComponent<GlassFocusable>().FocusScale, 1e-4);
            var locked = GlassKit.Tile(host, new HubTileSpec { Title = "Online", Locked = true, Size = TileMetrics.Standard });
            Assert.IsNull(locked.Button, "locked tiles are not selectable");
            bool soon = false; foreach (var tx in locked.Cell.GetComponentsInChildren<TMP_Text>(true)) if (tx.text == "COMING SOON") soon = true;
            Assert.IsTrue(soon);
        }

        [Test]
        public void ListRowFocusSelectedAndTag()
        {
            var host = Host();
            var row = GlassKit.ListRow(host, "Slant", "Personnel: 1RB | 1TE | 3WR", 480f, 124f, null, true);
            Assert.AreEqual(GlassKind.Row, row.Surface.Kind); Assert.IsNotNull(row.Thumb);
            row.SetTag("PASS", TagKind.Pass); Assert.IsTrue(row.Tag.gameObject.activeSelf); Assert.AreEqual(GlassTheme.T.tagPass.r, row.Tag.color.r, 1e-3);
            row.SetTag("RUN", TagKind.Run); Assert.AreEqual(GlassTheme.T.tagRun.r, row.Tag.color.r, 1e-3);
            row.SetTag("", TagKind.Run); Assert.IsFalse(row.Tag.gameObject.activeSelf);
            Assert.IsFalse(row.Selected); row.Selected = true; Assert.IsTrue(row.Selected);
            row.Focus.ShowState(1f, true); Assert.AreEqual(1f, row.Surface.Focus, 1e-3);
            Assert.AreEqual(1f, row.Focus.transform.localScale.x, 1e-4, "rows do not grow");
        }

        [Test]
        public void TickerWrapsHidesWhenEmptyAndCostsNoAllocation()
        {
            var host = Host();
            var tk = GlassKit.Ticker(host, new List<string> { "One headline", "Another headline" });
            Assert.IsTrue(tk.Host.gameObject.activeSelf);
            for (int i = 0; i < 3000; i++) tk.Tick(0.016f);
            float x = tk.Strip.anchoredPosition.x;
            Assert.LessOrEqual(x, 0f); Assert.Greater(x, -2000f, "the strip wraps instead of running away");
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++) tk.Tick(0.016f);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - a0, "ticker update must not allocate");
            tk.SetHeadlines(new List<string>());
            Assert.IsFalse(tk.Host.gameObject.activeSelf, "hidden when empty");
            Assert.AreEqual(96f, tk.Speed);
        }

        [Test]
        public void FocusStepAllocatesNothing()
        {
            var host = Host();
            var b = GlassKit.Button(host, "GO", null, GlassButtonStyle.Secondary);
            var f = b.GetComponent<GlassFocusable>();
            f.ShowState(0.5f, true);
            for (int i = 0; i < 20; i++) f.Step(0.016f);
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++) f.Step(0.016f);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - a0);
        }

        [Test]
        public void FooterBuildsGlyphLegendAndRightText()
        {
            var host = Host();
            var foot = GlassWidgets.BottomBar(host, "WEEK 6");
            foot.Prompts.Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_lb", "PREV TAB"));
            foot.Prompts.ForceRebuild();
            Assert.AreEqual("WEEK 6", foot.Right.text);
            Assert.AreEqual(GlassTheme.T.footerHeight, foot.Root.sizeDelta.y, 1e-3);
            Assert.AreEqual(0f, foot.Root.sizeDelta.x, "full-bleed");
        }

        [Test]
        public void TextOfEveryTileComponentIsAtLeastTwentyUnits()
        {
            var host = Host();
            GlassWidgets.TopBar(host, "S", new[] { "HOME", "NEWS", "ACTIONS", "OWNER", "TEAM", "LEAGUE" }, 0, null, true);
            GlassKit.TabBar(host, new[] { "A", "B" }, 0, null, true, true);
            GlassKit.StripTabs(host, new[] { "SUGGESTIONS", "FORMATION" }, 0, null);
            GlassKit.Tile(host, new HubTileSpec { Title = "Online Head to Head", Subtitle = "7 people", Badge = "LIVE", Size = TileMetrics.Wide, Click = () => { } });
            GlassKit.ListRow(host, "Play", "Sub", 480f, 124f, null, true).SetTag("PASS", TagKind.Pass);
            GlassKit.PlayCard(host, "PA Cross", TagKind.Pass, "Singleback Ace");
            GlassKit.TableBlock(host, "Liberty East", new[] { "", "W" }, new[] { new[] { "1 TEAM", "5" } }, new[] { 3f, 1f }, 600f, 0);
            GlassKit.Stepper(host, "Quality", new[] { "Low", "High" }, 0, null); GlassKit.ToggleRow(host, "Rumble", true, null); GlassKit.SliderRow(host, "Volume", 0.5f, null); GlassWidgets.Dropdown(host, "Res", new[] { "a", "b" }, 0, null);
            GlassWidgets.Chip(host, "NEW", true); GlassWidgets.StatTile(host, "87", "OVR"); GlassWidgets.Card(host, "TITLE", 400f, 300f);
            GlassKit.Ticker(host, new List<string> { "x" });
            GlassWidgets.BottomBar(host, "r").Prompts.Set(PromptFooter.Item("glyph_a", "SELECT"));
            var bad = new List<string>();
            foreach (var tm in host.GetComponentsInChildren<TMP_Text>(true))
            {
                float low = tm.enableAutoSizing ? Mathf.Min(tm.fontSize, tm.fontSizeMin) : tm.fontSize;
                if (low < 19.99f && !string.IsNullOrEmpty(tm.text)) bad.Add(tm.transform.parent.name + "/" + tm.name + "=" + low);
            }
            Assert.IsEmpty(bad, "text below 20 canvas units: " + string.Join(", ", bad));
        }

        [Test]
        public void ModalFrameHasScrimBoxHeadAndBody()
        {
            var host = Host();
            var m = GlassWidgets.Modal(host, "BOX SCORE", 900f, 500f);
            Assert.IsNotNull(m.Scrim); Assert.IsNotNull(m.Box); Assert.IsNotNull(m.Body); Assert.AreEqual("BOX SCORE", m.Title.text);
            var d = GlassWidgets.BuildDialog(host, "Title", "Message", "YES", "NO", null, null);
            Assert.IsNotNull(d.Yes); Assert.IsNotNull(d.No); Assert.AreEqual("MESSAGE", d.Message.text.ToUpperInvariant());
        }

        [Test]
        public void TileTabsPageWithTheBumpersWhenAsked()
        {
            var host = Host();
            var tabs = GlassKit.TabBar(host, new[] { "A", "B", "C" }, 0, null, false, true);
            Assert.IsTrue(tabs.BumperPaging);
            var plain = GlassKit.TabBar(host, new[] { "A", "B" }, 0, null, false, false);
            Assert.IsFalse(plain.BumperPaging);
        }

        // ------------------------------------------------------------------ prefabs and licensing
        [Test]
        public void TilePrefabsExistAndNeverReferenceLicensedArt()
        {
            const string dir = "Assets/_Project/UI/Glass/Prefabs/Tile";
            if (!System.IO.Directory.Exists(dir)) Assert.Ignore("tile prefabs not built yet (PIX Football/Glass UI/Build tile prefabs)");
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { dir });
            Assert.GreaterOrEqual(guids.Length, 30, "the tile component library");
            foreach (string g in guids)
                foreach (string dep in UnityEditor.AssetDatabase.GetDependencies(UnityEditor.AssetDatabase.GUIDToAssetPath(g), true))
                {
                    Assert.IsFalse(dep.Contains("_Licensed"), UnityEditor.AssetDatabase.GUIDToAssetPath(g) + " references licensed art: " + dep);
                    Assert.IsFalse(dep.ToLowerInvariant().Contains("draftfever") || dep.ToLowerInvariant().Contains("logos"), UnityEditor.AssetDatabase.GUIDToAssetPath(g) + " references a logo: " + dep);
                }
        }

        [Test]
        public void TileThemeAssetUsesCommittedFontsOnly()
        {
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<GlassTheme>(GlassTheme.TileEditorPath);
            if (theme == null) Assert.Ignore("TileTheme asset not built yet (PIX Football/Glass UI/Build tile assets)");
            Assert.IsTrue(theme.tokens.flat); Assert.IsNotNull(theme.fontTitle); Assert.IsNotNull(theme.fontBody); Assert.IsNotNull(theme.material);
            foreach (string dep in UnityEditor.AssetDatabase.GetDependencies(GlassTheme.TileEditorPath, true)) Assert.IsFalse(dep.Contains("_Licensed"), "TileTheme references " + dep);
            StringAssert.Contains("Barlow", theme.fontTitle.name);
            Assert.IsTrue(System.IO.File.Exists("Assets/_Project/UI/Glass/Fonts/LICENSE-Barlow-OFL.txt"), "OFL text ships with the font");
        }

        [Test]
        public void BarlowCoversLatinAndTheFallbackTakesTheRest()
        {
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<GlassTheme>(GlassTheme.TileEditorPath);
            if (theme == null || theme.fontTitle == null) Assert.Ignore("fonts not built yet");
            int missing = 0;
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?-+/&%$#@()[]'\"•–—°éñ") if (!theme.fontTitle.HasCharacter(c, true, true)) missing++;
            Assert.AreEqual(0, missing);
        }
    }
}
