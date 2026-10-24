using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>What the hub needs from the main menu: the actions behind the tiles and the data of the art tiles.</summary>
    public sealed class HubContext
    {
        public Action PlayNow, Rematch, FreeRoam, Quit;
        /// <summary>Opens the career (franchise) flow.</summary>
        public Action Career;
        /// <summary>One line under the FRANCHISE tile (the open save, or what the mode is) and its badge ("RESUME" with a save).</summary>
        public string CareerLine, CareerBadge;
        /// <summary>Opens the settings screen on a page (0 video .. 4 audio).</summary>
        public Action<int> Settings;
        /// <summary>Told the normalised (-1..1) position of the focused tile so the stage camera can drift.</summary>
        public Action<Vector2> Parallax;
        /// <summary>The last exhibition setup (hero tile and QUICK REMATCH): visitor and home.</summary>
        public Sprite CrestAway, CrestHome; public string NameAway, NameHome; public Color ColorAway, ColorHome;
        public string RematchLine; public bool HasRematch = true;
        /// <summary>The team of the newest save (FRANCHISE art).</summary>
        public Sprite CareerCrest; public Color CareerColor = new Color(0.16f, 0.17f, 0.2f, 1f);
        /// <summary>The four best teams (POWER RANKINGS panel): crest and name.</summary>
        public List<Sprite> RankCrests = new List<Sprite>(); public List<string> RankNames = new List<string>();
        public IList<string> Headlines;
        /// <summary>Watch mode (v0.11): the captions say so.</summary>
        public bool WatchMode = true;
        public string WatchLine = "Controller input: Off (watch)";
    }

    /// <summary>
    /// The tile-hub main menu of direction 16 (layout after the mock: tab bar in the top bar, a hero PLAY NOW tile with mixed-size tiles around it, the focused tile ringed in gold, locked "COMING SOON"
    /// tiles for the features that do not exist yet, a news ticker along the bottom whose headlines come from the league data). Pages: HOME, PLAY, SETTINGS (LB / RB or click). Every selectable tile is reachable
    /// with the d-pad through explicit links built from the tile geometry. The plan of every page lives in <see cref="MenuHubPlan"/> (no college mode is planned anywhere).
    /// </summary>
    public sealed class MainMenuHub
    {
        public static readonly string[] TabNames = MenuHubPlan.TabNames;

        public RectTransform Root;
        public Button First, SettingsTile;
        public GlassTabs Tabs;
        public GlassTicker Ticker;
        readonly List<List<MenuTile>> _pages = new List<List<MenuTile>>();
        readonly List<GameObject> _pageRoots = new List<GameObject>();
        int _page;
        HubContext _ctx;

        public int Page { get { return _page; } }
        public IList<MenuTile> Tiles(int page) { return _pages[page]; }

        public static MainMenuHub Build(RectTransform canvas, GlassWidgets.NavBar nav, HubContext ctx)
        {
            var t = GlassTheme.T;
            var hub = new MainMenuHub { _ctx = ctx, Tabs = nav != null ? nav.Tabs : null };
            var menu = UiKit.Rect(canvas, "MainMenu"); UiKit.Stretch(menu); hub.Root = menu;

            // tile block: laid out for the 1920 x 1080 reference (content from y 140; clear of the 46 ticker and the 62 footer); the FitScale host shrinks it when the canvas is shorter (it never grows) and the block stays centred
            var host = UiKit.Rect(menu, "FitHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(60f, 156f); host.offsetMax = new Vector2(-60f, -140f);
            var block = UiKit.Rect(host, "Block");
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f); block.pivot = new Vector2(0.5f, 0.5f); block.anchoredPosition = Vector2.zero; block.sizeDelta = MenuHubPlan.BlockSize;
            var fs = host.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = MenuHubPlan.BlockSize;

            var input = hub.MakeInput();
            for (int p = 0; p < TabNames.Length; p++)
            {
                var pr = UiKit.Rect(block, "Page_" + TabNames[p]); UiKit.Stretch(pr);
                hub._pageRoots.Add(pr.gameObject);
                hub._pages.Add(hub.BuildPage(pr, p, input));
            }

            // ---- ticker along the bottom, full width, above the footer (46 high, gold NEWS block)
            var heads = ctx.Headlines != null && ctx.Headlines.Count > 0 ? ctx.Headlines : new List<string> { "PIX PRO FOOTBALL: the season opens tonight" };
            hub.Ticker = GlassKit.Ticker(menu, heads, "NEWS");

            hub.Link();
            hub.SetPage(0, false);
            return hub;
        }

        PlanInput MakeInput()
        {
            return new PlanInput { WatchMode = _ctx.WatchMode, CareerLine = _ctx.CareerLine, ResumeBadge = _ctx.CareerBadge, RematchLine = _ctx.RematchLine ?? "", HasRematch = _ctx.HasRematch, WatchLine = _ctx.WatchLine };
        }

        // ------------------------------------------------------------------ pages
        Action ActionOf(string id)
        {
            switch (id)
            {
                case "play": return _ctx.PlayNow;
                case "rematch": return _ctx.Rematch ?? _ctx.PlayNow;
                case "franchise": return _ctx.Career;
                case "freeroam": return _ctx.FreeRoam;
                case "settings": return () => _ctx.Settings(0);
                case "quit": return _ctx.Quit;
                default:
                    int sp = MenuHubPlan.SettingsPageOf(id);
                    if (sp >= 0) return () => _ctx.Settings(sp);
                    return null;
            }
        }

        TileArt ArtOf(string id)
        {
            var a = new TileArt();
            switch (id)
            {
                case "play": case "rematch":
                    a.CrestA = _ctx.CrestAway; a.CrestB = _ctx.CrestHome; a.ColorA = _ctx.ColorAway; a.ColorB = _ctx.ColorHome;
                    if (id == "play") { a.NameA = _ctx.NameAway; a.NameB = _ctx.NameHome; }
                    break;
                case "franchise": a.CrestA = _ctx.CareerCrest; a.ColorA = _ctx.CareerColor; break;
                case "rankings": a.Strip = _ctx.RankCrests; a.StripNames = _ctx.RankNames; break;
            }
            return a;
        }

        List<MenuTile> BuildPage(RectTransform root, int page, PlanInput input)
        {
            var plan = MenuHubPlan.Page(page, input);
            var tiles = new List<MenuTile>();
            for (int i = 0; i < plan.Count; i++)
            {
                var pt = plan[i];
                var tile = MenuTileBuilder.Build(root, pt, ArtOf(pt.Id), 0.04f * i, OnTileFocus, pt.Selectable ? ActionOf(pt.Id) : null);
                tiles.Add(tile);
                if (page == 0 && tile.Button != null && pt.Id == "play") First = tile.Button;
                if (page == 0 && tile.Button != null && pt.Id == "settings") SettingsTile = tile.Button;
            }
            return tiles;
        }

        void OnTileFocus(MenuTile tile)
        {
            if (_ctx.Parallax == null || tile == null) return;
            var s = MenuHubPlan.BlockSize;
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
                    var page = _pages[p];
                    Func<int, Selectable> at = k => k >= 0 ? page[k].Button : null;
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
