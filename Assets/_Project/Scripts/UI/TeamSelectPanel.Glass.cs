using System;
using System.Collections.Generic;
using System.Linq;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The themed team select (direction 16): the HOME and AWAY team tiles on the left (crest, colours, an OVR / OFF / DEF stat panel with bars), summary, KICK OFF and BACK under them, a 8 x 4 grid of crest
    /// tiles (all 32 teams) on the right with a second MATCH OPTIONS tab (LB / RB). A on a tile assigns the team to the active slot (HOME first, then AWAY); X, or A on a team tile, switches the active slot.
    /// Every control is reachable with the d-pad / stick (explicit grid navigation). v0.11 watch mode: no "You play as" row and no YOU chips (both teams are played by the CPU).
    /// </summary>
    public partial class TeamSelectPanel
    {
        const int GridCols = 8;
        /// <summary>Design size of the screen block (the hub's block: x 60 .. 1860, content from y 140).</summary>
        public static readonly Vector2 DesignSize = new Vector2(1800f, 784f);
        const float LeftW = 620f, RightX = 664f, RightW = 1136f;

        bool _glass;
        int _slot;                      // 0 = HOME, 1 = AWAY: the slot the next tile click fills
        sealed class SlotCard
        {
            public Button Button; public GlassGraphic Surface, Wash; public Image Crest; public TextMeshProUGUI Role, City, Nick, You;
            public GlassGraphic RoleChip, YouChip; public Image[] Chips = new Image[3];
            public GlassBar[] Bars = new GlassBar[3]; public TextMeshProUGUI[] BarValues = new TextMeshProUGUI[3];
        }
        SlotCard _gHome, _gAway;
        readonly List<GlassCrestTile> _tiles = new List<GlassCrestTile>();
        GlassTabs _gTabs;
        GameObject _gridPane, _optionsPane;
        TextMeshProUGUI _hoverName, _gridTitle;
        Button _kickBtn, _backBtn;
        MatchupCard _matchup; GameObject _glassHost; string _lastHome;
        readonly List<OptionSelector> _options = new List<OptionSelector>();
        static readonly string[] StatCaps = { "OVR", "OFF", "DEF" };
        TextMeshProUGUI _watchNote;

        void MakeGlass(RectTransform root)
        {
            _glass = true;
            var t = GlassTheme.T;
            // host + design block: centred, shrinks on 4:3 like the Classic panels (and stays centred on 21:9)
            var host = UiKit.Rect(root, "GlassHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(60f, 70f); host.offsetMax = new Vector2(-60f, -140f);
            _glassHost = host.gameObject;
            var block = UiKit.Rect(host, "Block");
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 1f); block.pivot = new Vector2(0.5f, 1f); block.anchoredPosition = Vector2.zero; block.sizeDelta = DesignSize;
            var fit = host.gameObject.AddComponent<FitScale>(); fit.Block = block; fit.DesignSize = DesignSize;

            // ---- left: the two team tiles, summary, KICK OFF, BACK
            _gHome = MakeSlotCard(block, 0, 0f);
            _gAway = MakeSlotCard(block, 1, 252f);
            var sumCard = GlassKit.Panel(block, "SummaryCard", 1.2f, false, t.cornerRadius); UiKit.Anchor(sumCard.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -504f), new Vector2(LeftW, 84f));
            _summary = GlassKit.Eyebrow(sumCard.rectTransform, "", TextAlignmentOptions.MidlineLeft, "Summary");
            GlassKit.Style(_summary, 20f, t.textSecondary, true, 1f); _summary.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Stretch(_summary.rectTransform, 20f, 6f, 20f, 6f);
            _kickBtn = GlassKit.Button(block, "KICK OFF", KickOff, GlassButtonStyle.Primary, LeftW, 80f, "ui_arrow_right", "KickOff");
            UiKit.Anchor((RectTransform)_kickBtn.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -604f), new Vector2(LeftW, 80f));
            _backBtn = GlassKit.Button(block, "BACK", () => { if (OnBack != null) OnBack(); }, GlassButtonStyle.Secondary, LeftW, 64f, null, "Back");
            UiKit.Anchor((RectTransform)_backBtn.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -700f), new Vector2(LeftW, 64f));

            // ---- right: tabs + grid / options
            _gTabs = GlassKit.Tabs(block, new[] { "TEAMS", "MATCH OPTIONS" }, 0, i => ShowPane(i), 52f, 22f, "Tabs");
            _gTabs.BumperPaging = true;
            var tr = (RectTransform)_gTabs.transform; tr.anchorMin = tr.anchorMax = new Vector2(0f, 1f); tr.pivot = new Vector2(0f, 1f); tr.anchoredPosition = new Vector2(RightX, 0f);
            var tfit = tr.gameObject.AddComponent<ContentSizeFitter>(); tfit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; tfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _gridTitle = GlassKit.Eyebrow(block, "", TextAlignmentOptions.MidlineRight, "GridTitle"); GlassKit.Style(_gridTitle, 20f, t.accent, true, 3f);
            UiKit.Anchor(_gridTitle.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -8f), new Vector2(560f, 36f));

            _gridPane = UiKit.Rect(block, "GridPane").gameObject;
            var gp = (RectTransform)_gridPane.transform; gp.anchorMin = gp.anchorMax = new Vector2(0f, 1f); gp.pivot = new Vector2(0f, 1f); gp.anchoredPosition = new Vector2(RightX, -76f); gp.sizeDelta = new Vector2(RightW, 700f);
            BuildTileGrid(gp);
            _hoverName = GlassKit.Text(gp, "", GlassText.Heading, TextAlignmentOptions.MidlineLeft, "HoverName"); GlassKit.Style(_hoverName, 30f, t.textPrimary, true, 3f);
            UiKit.Anchor(_hoverName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -548f), new Vector2(RightW - 16f, 44f));

            _optionsPane = UiKit.Rect(block, "OptionsPane").gameObject;
            var op = (RectTransform)_optionsPane.transform; op.anchorMin = op.anchorMax = new Vector2(0f, 1f); op.pivot = new Vector2(0f, 1f); op.anchoredPosition = new Vector2(RightX, -76f); op.sizeDelta = new Vector2(RightW, 620f);
            BuildOptions(op);

            _slot = 0;
            RefreshGlass();
            ShowPane(0);
            ApplyGlassNavigation();
            First = _tiles.Count > 0 ? (Selectable)_tiles[Mathf.Clamp(TeamCatalog.IndexOf(_cfg.HomeId), 0, _tiles.Count - 1)].Button : _kickBtn;

            // the pre-game matchup card sits between KICK OFF and the game (one more A starts it)
            _matchup = MatchupCard.Build(root, StartGame, HideMatchup);
            _matchup.Root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ matchup card
        /// <summary>Opens the matchup card of the current setup right away (the hub's QUICK REMATCH tile). B returns to the team select.</summary>
        public void OpenMatchup()
        {
            if (!_glass || _matchup == null) return;
            if (_cfg.HomeId == _cfg.AwayId) { KickOff(); return; }
            ShowMatchup();
        }

        void ShowMatchup()
        {
            _matchup.Fill(_cfg);
            _glassHost.SetActive(false); _matchup.Root.gameObject.SetActive(true);
            if (OnScreen != null) OnScreen("MATCHUP");
            StartCoroutine(SelectLater(_matchup.Kick.gameObject));
        }

        void HideMatchup()
        {
            if (_matchup == null || !_matchup.Visible) return;
            _matchup.Root.gameObject.SetActive(false); _glassHost.SetActive(true);
            if (OnScreen != null) OnScreen("TEAM SELECT");
            StartCoroutine(SelectLater(_kickBtn.gameObject));
        }

        System.Collections.IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null && g.activeInHierarchy) { UiAudio.Mute(0.2f); EventSystem.current.SetSelectedGameObject(g); }
        }

        void OnEnable()
        {
            if (_glass) RefreshWatchRows();
        }

        void OnDisable()
        {
            if (_glass && _matchup != null && _matchup.Visible) { _matchup.Root.gameObject.SetActive(false); if (_glassHost != null) _glassHost.SetActive(true); }
        }

        /// <summary>Watch mode (v0.11): the user does not play a side, so the row and the YOU chips are hidden (they come back when Settings turns Controller input on).</summary>
        void RefreshWatchRows()
        {
            if (_sideSel != null) _sideSel.gameObject.SetActive(!WatchMode.Enabled);
            if (_watchNote != null) _watchNote.gameObject.SetActive(WatchMode.Enabled);
            RefreshGlass();
            ApplyGlassNavigation();
        }

        // ------------------------------------------------------------------ team tiles
        SlotCard MakeSlotCard(RectTransform block, int slot, float y)
        {
            var t = GlassTheme.T;
            var c = new SlotCard();
            var g = GlassKit.Panel(block, slot == 0 ? "HomeCard" : "AwayCard", 1.1f, true);
            var r = g.rectTransform; UiKit.Anchor(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -y), new Vector2(LeftW, 236f));
            g.raycastTarget = true; c.Surface = g;
            c.Button = r.gameObject.AddComponent<Button>(); c.Button.targetGraphic = g; c.Button.transition = Selectable.Transition.None;
            int s = slot;
            c.Button.onClick.AddListener(() => { UiAudio.Click(); SetSlot(s); });
            var f = r.gameObject.AddComponent<GlassFocusable>(); f.Surface = g; f.FocusScale = 1.01f; f.PlaySound = true;

            c.Wash = GlassKit.Solid(r, "Wash", new Color(1f, 1f, 1f, 0.5f), 0f); UiKit.Anchor(c.Wash.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(5f, 190f));
            c.RoleChip = GlassKit.Surface(r, "RoleChip", GlassKind.Glass, 0f, false, 1.3f); UiKit.Anchor(c.RoleChip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -16f), new Vector2(104f, 34f));
            c.Role = GlassKit.Eyebrow(c.RoleChip.rectTransform, slot == 0 ? "HOME" : "AWAY", TextAlignmentOptions.Center, "Role"); GlassKit.Style(c.Role, 20f, t.textPrimary, true, 2.5f); UiKit.Stretch(c.Role.rectTransform);
            c.YouChip = GlassKit.Surface(r, "YouChip", GlassKind.Glass, 0f, false, 1.3f); UiKit.Anchor(c.YouChip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(144f, -16f), new Vector2(84f, 34f));
            c.You = GlassKit.Eyebrow(c.YouChip.rectTransform, "YOU", TextAlignmentOptions.Center, "You"); GlassKit.Style(c.You, 20f, t.accent, true, 2.5f); UiKit.Stretch(c.You.rectTransform);

            c.Crest = UiKit.Panel(r, "Crest", Color.white); c.Crest.preserveAspect = true;
            UiKit.Anchor(c.Crest.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, -22f), new Vector2(124f, 124f));
            c.City = GlassKit.Eyebrow(r, "", TextAlignmentOptions.MidlineLeft, "City"); GlassKit.Style(c.City, 20f, t.textSecondary, true, 3f);
            UiKit.Anchor(c.City.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, -64f), new Vector2(170f, 30f)); UiKit.Fit(c.City, 20f);
            c.Nick = GlassKit.Text(r, "", GlassText.Title, TextAlignmentOptions.TopLeft, "Nick"); GlassKit.Style(c.Nick, 36f, t.textPrimary, true, 1f);
            UiKit.Anchor(c.Nick.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, -94f), new Vector2(206f, 56f)); UiKit.Fit(c.Nick, 22f);
            c.Nick.textWrappingMode = TextWrappingModes.NoWrap;
            for (int i = 0; i < 3; i++)
            {
                c.Chips[i] = GlassKit.Bar(r, "Chip" + i, Color.white, 34f, 12f);
                UiKit.Anchor(c.Chips[i].rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f + i * 42f, 24f), new Vector2(34f, 12f));
            }
            // the stat panel: OVR / OFF / DEF with bars
            for (int i = 0; i < 3; i++)
            {
                var bar = GlassWidgets.RatingBar(r, StatCaps[i], 0.5f, "", 200f, true, 40f);
                var row = (RectTransform)bar.transform.parent;
                var le = row.GetComponent<LayoutElement>(); if (le != null) UnityEngine.Object.Destroy(le);
                UiKit.Anchor(row, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -70f - i * 50f), new Vector2(200f, 40f));
                c.Bars[i] = bar; var vt = row.Find("Value"); c.BarValues[i] = vt != null ? vt.GetComponent<TextMeshProUGUI>() : null;
            }
            return c;
        }

        void FillSlot(SlotCard c, string id, bool you, bool active)
        {
            var t = TeamCatalog.ById(id);
            if (t == null) return;
            var sp = UiKit.Sprite("crest_" + t.Id); c.Crest.sprite = sp; c.Crest.enabled = sp != null;
            c.City.text = t.City.ToUpperInvariant(); c.Nick.text = t.Nickname.ToUpperInvariant();
            Color p = t.PrimaryUi, s = t.SecondaryUi, tt;
            if (!ColorUtility.TryParseHtmlString(t.TertiaryHex ?? "", out tt)) tt = s;
            c.Wash.color = p;
            c.Chips[0].color = p; c.Chips[1].color = s; c.Chips[2].color = tt;
            var rt = MenuHubModel.Ratings(MatchData.TeamByKey(id));
            int[] vals = { rt.Ovr, rt.Off, rt.Def };
            for (int i = 0; i < 3; i++)
            {
                if (c.Bars[i] != null) c.Bars[i].Set(Mathf.Clamp01((vals[i] - 45f) / 55f));
                if (c.BarValues[i] != null) c.BarValues[i].text = vals[i].ToString();
            }
            c.YouChip.gameObject.SetActive(you && !WatchMode.Enabled);
            c.RoleChip.SetFocus(active ? 0.9f : 0f);
            c.Role.color = active ? GlassTheme.T.accent : GlassTheme.T.textPrimary;
        }

        // ------------------------------------------------------------------ grid
        void BuildTileGrid(RectTransform parent)
        {
            var grid = UiKit.Rect(parent, "Grid"); grid.anchorMin = grid.anchorMax = new Vector2(0f, 1f); grid.pivot = new Vector2(0f, 1f); grid.anchoredPosition = Vector2.zero; grid.sizeDelta = new Vector2(RightW, 530f);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(132f, 124f); gl.spacing = new Vector2(10f, 10f); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = GridCols; gl.startCorner = GridLayoutGroup.Corner.UpperLeft; gl.childAlignment = TextAnchor.UpperLeft;
            for (int i = 0; i < _teams.Count; i++)
            {
                int idx = i; var tm = _teams[i];
                var tile = GlassWidgets.MakeCrestTile(grid, i, UiKit.Sprite("crest_" + tm.Id), tm.Abbr, string.IsNullOrEmpty(tm.Abbr) ? tm.Nickname.Substring(0, System.Math.Min(3, tm.Nickname.Length)).ToUpperInvariant() : tm.Abbr, tm.PrimaryUi, () => AssignTile(idx), 132f, 124f);
                var le = tile.GetComponent<LayoutElement>(); if (le != null) UnityEngine.Object.Destroy(le);
                var sel = tile.gameObject.AddComponent<TileFocus>(); sel.Owner = this; sel.Index = i;
                _tiles.Add(tile);
            }
        }

        sealed class TileFocus : MonoBehaviour, ISelectHandler
        {
            public TeamSelectPanel Owner; public int Index;
            public void OnSelect(BaseEventData e) { if (Owner != null) Owner.OnTileFocus(Index); }
        }

        void OnTileFocus(int i)
        {
            if (_hoverName != null && i >= 0 && i < _teams.Count) _hoverName.text = _teams[i].Name.ToUpperInvariant();
        }

        void AssignTile(int i)
        {
            string id = _teams[i].Id;
            if (_slot == 0) { if (_cfg.AwayId == id) _cfg.AwayId = _cfg.HomeId; _cfg.HomeId = id; }
            else { if (_cfg.HomeId == id) _cfg.HomeId = _cfg.AwayId; _cfg.AwayId = id; }
            SetSlot(1 - _slot);   // HOME first, then AWAY
        }

        void SetSlot(int slot) { _slot = slot; RefreshGlass(); }

        // ------------------------------------------------------------------ options
        void BuildOptions(RectTransform parent)
        {
            var t = GlassTheme.T;
            var card = GlassWidgets.Card(parent, "MATCH OPTIONS", RightW, 600f, "OptionsCard", true, 1.1f);
            UiKit.Stretch(card.Root);
            var col = GlassKit.Column(card.Body, "Rows", 10f); UiKit.Stretch((RectTransform)col.transform);
            const float w = 1060f, h = 64f;
            int qi = Array.IndexOf(MatchConfig.QuarterChoices, _cfg.QuarterMinutes); if (qi < 0) qi = 2;
            _quarterSel = UiControls.Selector(col.transform, "Quarter length", MatchConfig.QuarterChoices.Select(m => m + " min").ToArray(), qi, i => { _cfg.QuarterMinutes = MatchConfig.QuarterChoices[i]; RefreshGlass(); }, w, h);
            _rulesSel = UiControls.Selector(col.transform, "Rules", new[] { "Pro", "College" }, _cfg.CollegeRules ? 1 : 0, i => { _cfg.CollegeRules = i == 1; RefreshGlass(); }, w, h);
            _diffSel = UiControls.Selector(col.transform, "Difficulty", new[] { "Rookie", "Pro", "All-Pro" }, (int)_cfg.Difficulty, i => { _cfg.Difficulty = (Difficulty)i; RefreshGlass(); }, w, h);
            _sideSel = UiControls.Selector(col.transform, "You play as", new[] { "Home team", "Away team" }, _cfg.UserTeam == PixFootball.Core.Team.Home ? 0 : 1, i => { _cfg.UserTeam = i == 0 ? PixFootball.Core.Team.Home : PixFootball.Core.Team.Away; RefreshGlass(); }, w, h);
            _weatherSel = UiControls.Selector(col.transform, "Weather", PixFootball.Core.WeatherPresets.WeatherChoiceNames, (int)_cfg.Weather, i => { _cfg.Weather = (PixFootball.Core.WeatherChoice)i; RefreshGlass(); }, w, h);
            _timeSel = UiControls.Selector(col.transform, "Time of day", PixFootball.Core.WeatherPresets.TimeChoiceNames, (int)_cfg.Time, i => { _cfg.Time = (PixFootball.Core.TimeChoice)i; RefreshGlass(); }, w, h);
            _options.AddRange(new[] { _quarterSel, _rulesSel, _diffSel, _sideSel, _weatherSel, _timeSel });
            _sideSel.gameObject.SetActive(!WatchMode.Enabled);
            // watch mode (v0.11): both teams are played by the CPU; say so and where it changes
            _watchNote = GlassKit.Text(col.transform, "WATCH MODE: the CPU plays both teams. Settings, Gameplay, Controller input turns player control back on.", GlassText.Body, TextAlignmentOptions.TopLeft, "WatchNote");
            GlassKit.Style(_watchNote, 22f, t.textMuted, false, 0f); _watchNote.textWrappingMode = TextWrappingModes.Normal;
            var wle = _watchNote.gameObject.AddComponent<LayoutElement>(); wle.preferredWidth = w; wle.preferredHeight = 64f;
            _watchNote.gameObject.SetActive(WatchMode.Enabled);
        }

        /// <summary>The option rows that can be reached right now (watch mode hides "You play as").</summary>
        List<OptionSelector> LiveOptions() { return _options.Where(o => o != null && o.gameObject.activeSelf).ToList(); }

        void ShowPane(int i)
        {
            if (_gridPane != null) _gridPane.SetActive(i == 0);
            if (_optionsPane != null) _optionsPane.SetActive(i == 1);
            if (_gridTitle != null) _gridTitle.text = i == 0 ? (_slot == 0 ? "PICK THE HOME TEAM" : "PICK THE AWAY TEAM") : "";
            if (EventSystem.current != null && isActiveAndEnabled)
            {
                var live = LiveOptions();
                if (i == 1 && live.Count > 0) EventSystem.current.SetSelectedGameObject(live[0].gameObject);
                else if (i == 0 && _tiles.Count > 0 && EventSystem.current.currentSelectedGameObject != null && !_gridPane.transform.IsChildOf(EventSystem.current.currentSelectedGameObject.transform) && !EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_gridPane.transform))
                    EventSystem.current.SetSelectedGameObject(_tiles[Mathf.Clamp(_slot == 0 ? TeamCatalog.IndexOf(_cfg.HomeId) : TeamCatalog.IndexOf(_cfg.AwayId), 0, _tiles.Count - 1)].Button.gameObject);
            }
            ApplyGlassNavigation();
        }

        // ------------------------------------------------------------------ refresh
        void RefreshGlass()
        {
            if (_gHome == null || _cfg == null) return;
            bool userHome = _cfg.UserTeam == PixFootball.Core.Team.Home;
            FillSlot(_gHome, _cfg.HomeId, userHome, _slot == 0); FillSlot(_gAway, _cfg.AwayId, !userHome, _slot == 1);
            if (_lastHome != _cfg.HomeId) { _lastHome = _cfg.HomeId; if (OnHomeChanged != null) OnHomeChanged(_cfg.HomeId); }
            _gHome.Surface.SetFocus(_slot == 0 ? 0.55f : 0f); _gAway.Surface.SetFocus(_slot == 1 ? 0.55f : 0f);
            _summary.text = SummaryText(_cfg, _cfg.HomeId == _cfg.AwayId);
            for (int i = 0; i < _tiles.Count; i++)
            {
                string id = _teams[i].Id;
                if (id == _cfg.HomeId) _tiles[i].SetRole("HOME", _slot == 0);
                else if (id == _cfg.AwayId) _tiles[i].SetRole("AWAY", _slot == 1);
                else _tiles[i].SetRole(null, false);
            }
            if (_gridTitle != null && _gridPane != null && _gridPane.activeSelf) _gridTitle.text = _slot == 0 ? "PICK THE HOME TEAM" : "PICK THE AWAY TEAM";
        }

        // ------------------------------------------------------------------ input and navigation
        void Update()
        {
            if (!_glass || UiDialog.AnyOpen || (_matchup != null && _matchup.Visible)) return;
            bool swap = false;
            var pad = UnityEngine.InputSystem.Gamepad.current; if (pad != null && pad.buttonWest.wasPressedThisFrame) swap = true;
            var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && kb.cKey.wasPressedThisFrame) swap = true;
            if (swap && _gridPane != null && _gridPane.activeInHierarchy) { UiAudio.Move(); SetSlot(1 - _slot); }
        }

        /// <summary>Explicit navigation: the crest grid moves by <see cref="GlassNav.GridMove"/>, the grid's top edge goes to the tabs / cards, its bottom edge to KICK OFF.</summary>
        void ApplyGlassNavigation()
        {
            if (_tiles.Count == 0 || _kickBtn == null) return;
            var tabs = _gTabs != null ? _gTabs.Buttons : new Button[0];
            Button tab0 = tabs.Length > 0 ? tabs[0] : null;
            for (int i = 0; i < _tiles.Count; i++)
            {
                var b = _tiles[i].Button;
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                int up = GlassNav.GridMove(i, 0, -1, GridCols, _tiles.Count), down = GlassNav.GridMove(i, 0, 1, GridCols, _tiles.Count);
                int left = GlassNav.GridMove(i, -1, 0, GridCols, _tiles.Count), right = GlassNav.GridMove(i, 1, 0, GridCols, _tiles.Count);
                nav.selectOnUp = up != i ? _tiles[up].Button : tab0;
                nav.selectOnDown = down != i ? _tiles[down].Button : _kickBtn;
                nav.selectOnLeft = left != i ? _tiles[left].Button : (i / GridCols < 2 ? _gHome.Button : _gAway.Button);
                nav.selectOnRight = right != i ? _tiles[right].Button : null;
                b.navigation = nav;
            }
            SetNav(_gHome.Button, up: null, down: _gAway.Button, left: null, right: _tiles[0].Button);
            SetNav(_gAway.Button, up: _gHome.Button, down: _kickBtn, left: null, right: _tiles[GridCols].Button);
            SetNav(_kickBtn, up: _gAway.Button, down: _backBtn, left: null, right: _tiles[_tiles.Count - GridCols].Button);
            SetNav(_backBtn, up: _kickBtn, down: null, left: null, right: _tiles[_tiles.Count - 1].Button);
            // MATCH OPTIONS page: the left column reaches the option rows, the rows reach KICK OFF below, the first tab goes back to the slot cards
            var live = LiveOptions();
            if (_optionsPane != null && _optionsPane.activeSelf && live.Count > 0)
            {
                Selectable o0 = live[0], oLast = live[live.Count - 1];
                SetNav(_gHome.Button, up: null, down: _gAway.Button, left: null, right: o0);
                SetNav(_gAway.Button, up: _gHome.Button, down: _kickBtn, left: null, right: o0);
                SetNav(_kickBtn, up: _gAway.Button, down: _backBtn, left: null, right: oLast);
                SetNav(_backBtn, up: _kickBtn, down: null, left: null, right: oLast);
                for (int k = 0; k < live.Count; k++)
                {
                    var n = live[k].navigation; n.mode = Navigation.Mode.Explicit;
                    n.selectOnUp = k > 0 ? (Selectable)live[k - 1] : (tabs.Length > 1 ? tabs[1] : null);
                    n.selectOnDown = k + 1 < live.Count ? (Selectable)live[k + 1] : _kickBtn;
                    n.selectOnLeft = null; n.selectOnRight = null;
                    live[k].navigation = n;
                }
            }
            for (int k = 0; k < tabs.Length; k++)
                SetNav(tabs[k], up: null, down: k == 0 && _tiles.Count > 0 ? _tiles[0].Button : (k == 1 && live.Count > 0 ? (Selectable)live[0] : null), left: k > 0 ? tabs[k - 1] : (Selectable)_gHome.Button, right: k + 1 < tabs.Length ? tabs[k + 1] : null);
        }

        static void SetNav(Selectable s, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            if (s == null) return;
            s.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right };
        }
    }
}
