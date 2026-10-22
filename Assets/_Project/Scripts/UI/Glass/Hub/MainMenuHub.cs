using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>What the hub needs from the main menu: the actions behind the tiles and the data of the hero tile.</summary>
    public sealed class HubContext
    {
        public Action PlayNow, FreeRoam, Quit;
        /// <summary>v0.8: opens the career (franchise) flow; null keeps the tile locked.</summary>
        public Action Career;
        /// <summary>One line under the CAREER tile (the open save, or what the mode is).</summary>
        public string CareerSubtitle;
        /// <summary>Opens the settings screen on a page (0 video .. 4 audio).</summary>
        public Action<int> Settings;
        /// <summary>Told the normalised (-1..1) position of the focused tile so the stage camera can drift.</summary>
        public Action<Vector2> Parallax;
        public Sprite CrestAway, CrestHome; public string NameAway, NameHome;
        public IList<string> Headlines;
    }

    /// <summary>
    /// The tile-hub main menu of the glass theme (layout and flow after the console tile hubs): a tab strip in the top bar (HOME / PLAY / PRACTICE / OPTIONS, LB / RB or click), a large hero tile with
    /// mixed-size tiles around it, the focused tile glowing with the orange accent and growing a hair, locked "COMING SOON" tiles for the features of later versions, and a scrolling news ticker
    /// along the bottom whose headlines come from the league data. Every tile is reachable with the d-pad through explicit links built from the tile geometry.
    /// </summary>
    public sealed class MainMenuHub
    {
        public static readonly string[] TabNames = { "HOME", "PLAY", "PRACTICE", "OPTIONS" };

        public RectTransform Root;
        public Button First, SettingsTile;
        public GlassTabs Tabs;
        public GlassTicker Ticker;
        readonly List<List<HubTile>> _pages = new List<List<HubTile>>();
        readonly List<GameObject> _pageRoots = new List<GameObject>();
        int _page;
        HubContext _ctx;

        public int Page { get { return _page; } }
        public IList<HubTile> Tiles(int page) { return _pages[page]; }

        public static MainMenuHub Build(RectTransform canvas, GlassWidgets.NavBar nav, HubContext ctx)
        {
            var t = GlassTheme.T;
            var hub = new MainMenuHub { _ctx = ctx, Tabs = nav != null ? nav.Tabs : null };
            var menu = UiKit.Rect(canvas, "MainMenu"); UiKit.Stretch(menu); hub.Root = menu;

            // tile block: laid out for the 1920 x 1080 reference; the FitScale host shrinks it when the canvas is shorter (it never grows)
            var host = UiKit.Rect(menu, "FitHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(t.safeMarginX, 196f); host.offsetMax = new Vector2(-t.safeMarginX, -152f);
            var block = UiKit.Rect(host, "Block");
            block.anchorMin = block.anchorMax = new Vector2(0f, 0.5f); block.pivot = new Vector2(0f, 0.5f); block.anchoredPosition = Vector2.zero; block.sizeDelta = HubTileFactory.BlockSize;   // centred vertically between the tabs and the ticker
            var fs = host.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = HubTileFactory.BlockSize;

            for (int p = 0; p < TabNames.Length; p++)
            {
                var pr = UiKit.Rect(block, "Page_" + TabNames[p]); UiKit.Stretch(pr);
                hub._pageRoots.Add(pr.gameObject);
                hub._pages.Add(hub.BuildPage(pr, p));
            }

            // ---- ticker along the bottom (above the footer rule)
            var strip = UiKit.Rect(menu, "TickerHost");
            strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(1f, 0f); strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(t.safeMarginX, 124f); strip.offsetMax = new Vector2(-t.safeMarginX, 124f + 52f);
            hub.Ticker = GlassTicker.Build(strip, "LEAGUE WIRE", 52f, 24f);
            hub.Ticker.SetHeadlines(ctx.Headlines != null && ctx.Headlines.Count > 0 ? ctx.Headlines : new List<string> { "PIX Football: the season opens tonight" });

            hub.Link();
            hub.SetPage(0, false);
            return hub;
        }

        // ------------------------------------------------------------------ pages
        HubTileSpec Spec(string title, string sub, string icon, Action click, int col, int row, int cs = 1, int rs = 1, bool locked = false)
        {
            return new HubTileSpec { Title = title, Subtitle = sub, IconKey = icon, Click = click, Col = col, Row = row, ColSpan = cs, RowSpan = rs, Locked = locked };
        }

        HubTileSpec PlayHero(int rs)
        {
            return new HubTileSpec
            {
                Title = "PLAY NOW", Eyebrow = "EXHIBITION", Subtitle = "Pick two teams, the rules and the weather", IconKey = "fb_football_laces", Click = _ctx.PlayNow,
                Col = 0, Row = 0, ColSpan = 2, RowSpan = rs, Hero = true, CrestA = _ctx.CrestAway, CrestB = _ctx.CrestHome, NameA = _ctx.NameAway, NameB = _ctx.NameHome
            };
        }

        HubTileSpec CareerTile(int col, int row)
        {
            bool on = _ctx.Career != null;
            return Spec("CAREER", on ? (_ctx.CareerSubtitle ?? "Build a dynasty over endless seasons") : "", "fb_franchise_hub", _ctx.Career, col, row, 2, 1, !on);
        }

        List<HubTile> BuildPage(RectTransform root, int page)
        {
            var specs = new List<HubTileSpec>();
            switch (page)
            {
                case 0:
                    specs.Add(PlayHero(2));
                    specs.Add(Spec("FREE ROAM", "Run the empty stadium: moves, cameras, lights", "fb_field_compass", _ctx.FreeRoam, 2, 0, 2, 1));
                    specs.Add(Spec("SETTINGS", "Video, camera, audio", "ui_settings", () => _ctx.Settings(0), 2, 1));
                    specs.Add(Spec("QUIT", "Back to the desktop", "ui_close", _ctx.Quit, 3, 1));
                    specs.Add(CareerTile(0, 2));
                    specs.Add(Spec("COLLEGE NETWORK", "", "fb_scout_binoculars", null, 2, 2, 1, 1, true));
                    specs.Add(Spec("TROPHY ROOM", "", "ui_trophy", null, 3, 2, 1, 1, true));
                    break;
                case 1:
                    specs.Add(PlayHero(3));
                    specs.Add(CareerTile(2, 0));
                    specs.Add(Spec("COLLEGE NETWORK", "", "fb_scout_binoculars", null, 2, 1, 2, 1, true));
                    specs.Add(Spec("TROPHY ROOM", "", "ui_trophy", null, 2, 2, 2, 1, true));
                    break;
                case 2:
                    specs.Add(new HubTileSpec { Title = "FREE ROAM", Eyebrow = "PRACTICE", Subtitle = "Run the empty stadium", IconKey = "fb_field_compass", Click = _ctx.FreeRoam, Col = 0, Row = 0, ColSpan = 2, RowSpan = 3, Hero = true });
                    specs.Add(Spec("SKILLS TRAINER", "", "fb_agility_ladder", null, 2, 0, 2, 1, true));
                    specs.Add(Spec("PLAYBOOK LAB", "", "fb_route_playbook", null, 2, 1, 2, 1, true));
                    specs.Add(Spec("COMBINE", "", "fb_combine_measure", null, 2, 2, 2, 1, true));
                    break;
                default:
                    specs.Add(new HubTileSpec { Title = "ALL SETTINGS", Eyebrow = "OPTIONS", Subtitle = "Video, gameplay, camera, broadcast, audio", IconKey = "fb_tuning_sliders", Click = () => _ctx.Settings(0), Col = 0, Row = 0, ColSpan = 2, RowSpan = 2, Hero = true });
                    specs.Add(Spec("VIDEO", "Display and quality", "ui_eye", () => _ctx.Settings(0), 2, 0));
                    specs.Add(Spec("GAMEPLAY", "Rules and controls", "ui_controller", () => _ctx.Settings(1), 3, 0));
                    specs.Add(Spec("CAMERA", "Views and shake", "fb_film_reel", () => _ctx.Settings(2), 2, 1));
                    specs.Add(Spec("BROADCAST", "Overlays and shows", "fb_video_review", () => _ctx.Settings(3), 3, 1));
                    specs.Add(Spec("AUDIO", "Music, crowd, effects", "fb_audio_levels", () => _ctx.Settings(4), 0, 2, 2, 1));
                    specs.Add(Spec("QUIT", "Back to the desktop", "ui_close", _ctx.Quit, 2, 2, 2, 1));
                    break;
            }
            var tiles = new List<HubTile>();
            for (int i = 0; i < specs.Count; i++)
            {
                var tile = HubTileFactory.Build(root, specs[i], 0.03f * i, OnTileFocus);
                tiles.Add(tile);
                if (page == 0 && tile.Button != null && specs[i].Title == "PLAY NOW") First = tile.Button;
                if (page == 0 && tile.Button != null && specs[i].Title == "SETTINGS") SettingsTile = tile.Button;
            }
            return tiles;
        }

        void OnTileFocus(HubTile tile)
        {
            if (_ctx.Parallax == null || tile == null) return;
            var s = HubTileFactory.BlockSize;
            _ctx.Parallax(new Vector2((tile.Center.x / s.x - 0.5f) * 2f, (tile.Center.y / s.y - 0.5f) * 2f));
        }

        // ------------------------------------------------------------------ navigation
        /// <summary>Explicit links for every selectable tile of every page; the top row goes up to the tab of its page, the tab strip goes down to the first tile.</summary>
        void Link()
        {
            var tabs = Tabs != null ? Tabs.Buttons : new Button[0];
            for (int p = 0; p < _pages.Count; p++)
            {
                var cells = new List<HubNav.Cell>();
                foreach (var tl in _pages[p]) cells.Add(tl.NavCell);
                var links = HubNav.Links(cells);
                Button tab = p < tabs.Length ? tabs[p] : null;
                for (int i = 0; i < _pages[p].Count; i++)
                {
                    var b = _pages[p][i].Button; if (b == null) continue;
                    Func<int, Selectable> at = k => k >= 0 ? _pages[p][k].Button : null;
                    b.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = links[i][0] >= 0 ? at(links[i][0]) : tab, selectOnDown = at(links[i][1]), selectOnLeft = at(links[i][2]), selectOnRight = at(links[i][3]) };
                }
            }
        }

        /// <summary>The first selectable tile of the open page (the hero), where the pad lands.</summary>
        public Button FirstOfPage(int page)
        {
            foreach (var tl in _pages[page]) if (tl.Button != null) return tl.Button;
            return null;
        }

        /// <summary>Shows a page: the tiles of the page reveal one after another (GlassReveal stagger), the tab strip follows.</summary>
        public void SetPage(int i, bool notifyTabs = true)
        {
            i = Mathf.Clamp(i, 0, _pageRoots.Count - 1);
            _page = i;
            for (int k = 0; k < _pageRoots.Count; k++) _pageRoots[k].SetActive(k == i);
            if (Tabs != null)
            {
                Tabs.SetIndex(i, false);
                var tabs = Tabs.Buttons;
                for (int k = 0; k < tabs.Length; k++)
                {
                    var nv = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = FirstOfPage(i), selectOnLeft = k > 0 ? tabs[k - 1] : null, selectOnRight = k + 1 < tabs.Length ? tabs[k + 1] : null };
                    tabs[k].navigation = nv;
                }
            }
            if (_ctx.Parallax != null) _ctx.Parallax(Vector2.zero);
        }
    }
}
