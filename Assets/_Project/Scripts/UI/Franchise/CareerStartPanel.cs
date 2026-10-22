using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    /// <summary>Pure rules of the career start screens (EditMode tested).</summary>
    public static class CareerStartModel
    {
        public static readonly string[] Difficulties = { "Rookie", "Pro", "All-Pro" };
        public static readonly int[] QuarterChoices = { 2, 3, 5, 8, 15 };

        public static string SlotLabel(CareerSlotMeta m)
        {
            return m.Valid ? "Slot " + m.Slot + " - " + m.TeamName + ", " + m.Year + " (replace)" : "Slot " + m.Slot + " - empty";
        }

        /// <summary>The first empty slot (or slot 1 when all are used: the screen then asks before replacing).</summary>
        public static int DefaultSlot(IList<CareerSlotMeta> slots)
        {
            for (int i = 0; i < slots.Count; i++) if (!slots[i].Valid) return slots[i].Slot;
            return slots.Count > 0 ? slots[0].Slot : 1;
        }

        public static CareerPrefs Prefs(int difficultyIndex, int quarterIndex)
        {
            return new CareerPrefs { Difficulty = Mathf.Clamp(difficultyIndex, 0, 2), QuarterMinutes = QuarterChoices[Mathf.Clamp(quarterIndex, 0, QuarterChoices.Length - 1)] };
        }

        public static string PrefsText(CareerPrefs p)
        {
            return Difficulties[Mathf.Clamp(p.Difficulty, 0, 2)] + " difficulty, " + p.QuarterMinutes + " minute quarters, pro rules";
        }

        /// <summary>The league seed of a new career: the standard league (the one the exhibition uses, so the ratings on the team cards are exact) or a fresh random one.</summary>
        public static int SeedFor(bool standard, int tick) { return standard ? MatchData.LeagueSeed : (tick & 0x3FFFFFFF) | 1; }
    }

    /// <summary>
    /// The career start flow inside the main menu canvas: a landing page (CONTINUE, NEW CAREER, LOAD CAREER), the new career page (crest grid like the team select, difficulty, quarter length, slot) and the
    /// load page (slot table with the details of the highlighted save, X deletes). It builds the franchise through <see cref="CareerSession"/> and hands the controller to the menu.
    /// </summary>
    public sealed class CareerStartPanel : MonoBehaviour
    {
        const int GridCols = 8;
        public RectTransform Root;
        public Selectable First;
        public Action<FranchiseController> Started;
        public Action Back;
        public Action<string> Section;
        public Action<IList<PromptItem>> Footer;

        RectTransform _block;
        GameObject _landing, _newPage, _loadPage;
        int _page;
        BusyOverlay _busy;

        // landing
        Button _continueBtn, _newBtn, _loadBtn, _backBtn;
        RectTransform _landingCard; CareerSlotMeta _latest;

        // new career
        readonly List<GlassCrestTile> _tiles = new List<GlassCrestTile>();
        IReadOnlyList<TeamInfo> _teams;
        int _team;
        OptionSelector _diff, _quarter, _league, _slotSel;
        Button _startBtn, _newBack;
        TextMeshProUGUI _hover;
        RectTransform _teamCard;
        List<CareerSlotMeta> _slots = new List<CareerSlotMeta>();

        // load
        GlassTable _table; RectTransform _loadCard; Button _loadBack;
        List<CareerSlotMeta> _loadRows = new List<CareerSlotMeta>();

        public static CareerStartPanel Build(RectTransform canvas)
        {
            var t = GlassTheme.T;
            var go = UiKit.Rect(canvas, "CareerStart"); UiKit.Stretch(go);
            var p = go.gameObject.AddComponent<CareerStartPanel>();
            p.Root = go;
            var host = UiKit.Rect(go, "FitHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(t.safeMarginX, 112f); host.offsetMax = new Vector2(-t.safeMarginX, -150f);
            var block = UiKit.Rect(host, "Block");
            block.anchorMin = block.anchorMax = new Vector2(0f, 1f); block.pivot = new Vector2(0f, 1f); block.anchoredPosition = Vector2.zero; block.sizeDelta = new Vector2(1728f, 820f);
            var fit = host.gameObject.AddComponent<FitScale>(); fit.Block = block; fit.DesignSize = new Vector2(1728f, 820f);
            p._block = block;
            p._teams = TeamCatalog.Teams;
            p._busy = BusyOverlay.Create(go);
            p.BuildLanding(); p.BuildNew(); p.BuildLoad();
            p.Show(0);
            return p;
        }

        // ================================================================== navigation between the pages
        public void Show(int page)
        {
            _page = page;
            _landing.SetActive(page == 0); _newPage.SetActive(page == 1); _loadPage.SetActive(page == 2);
            if (Section != null) Section(page == 0 ? "CAREER" : page == 1 ? "NEW CAREER" : "LOAD CAREER");
            Selectable first = null;
            if (page == 0) { RefreshLanding(); first = _continueBtn.interactable ? _continueBtn : _newBtn; }
            else if (page == 1) { RefreshNew(); first = _tiles[Mathf.Clamp(_team, 0, _tiles.Count - 1)].Button; }
            else { RefreshLoad(); first = _table; }
            First = first;
            SetFooter();
            if (isActiveAndEnabled && Application.isPlaying) StartCoroutine(SelectLater(first));
        }

        System.Collections.IEnumerator SelectLater(Selectable s)
        {
            yield return null;
            if (s != null && EventSystem.current != null && s.gameObject.activeInHierarchy) { UiAudio.Mute(0.2f); EventSystem.current.SetSelectedGameObject(s.gameObject); }
        }

        void SetFooter()
        {
            if (Footer == null) return;
            var l = new List<PromptItem> { PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_analog_all", "NAVIGATE") };
            if (_page == 2) l.Add(PromptFooter.Item("glyph_x", "DELETE"));
            Footer(l);
        }

        void OnEnable() { if (_block != null) Show(_page); }

        void Update()
        {
            if (UiDialog.AnyOpen || _busy.Visible) return;
            if (FranchiseInput.BackPressed() && Time.frameCount != _backFrame) { _backFrame = Time.frameCount; GoBack(); }
        }
        int _backFrame = -1;

        public void GoBack()
        {
            if (_page == 0) { if (Back != null) Back(); }
            else Show(0);
        }

        // ================================================================== landing
        void BuildLanding()
        {
            var t = GlassTheme.T;
            _landing = UiKit.Rect(_block, "Landing").gameObject; UiKit.Stretch((RectTransform)_landing.transform);
            var lr = (RectTransform)_landing.transform;
            Lay.Text(lr, "CAREER", 84f, t.textPrimary, 0f, 20f, 800f, 100f, true, TextAlignmentOptions.MidlineLeft, 8f, "Title");
            Lay.Caps(lr, "Build a dynasty. 32 teams, endless seasons.", 6f, 120f, 900f, 30f, t.accent, 20f);
            _continueBtn = GlassKit.MenuItem(lr, "CONTINUE", "ui_arrow_right", ContinueLatest, 640f, 96f, "Continue");
            Lay.Place((RectTransform)_continueBtn.transform, 0f, 190f, 640f, 96f);
            _newBtn = GlassKit.MenuItem(lr, "NEW CAREER", "fb_franchise_hub", () => Show(1), 640f, 96f, "New");
            Lay.Place((RectTransform)_newBtn.transform, 0f, 302f, 640f, 96f);
            _loadBtn = GlassKit.MenuItem(lr, "LOAD CAREER", "ui_roster", () => Show(2), 640f, 96f, "Load");
            Lay.Place((RectTransform)_loadBtn.transform, 0f, 414f, 640f, 96f);
            _backBtn = GlassKit.MenuItem(lr, "BACK", "ui_close", () => { if (Back != null) Back(); }, 640f, 96f, "Back");
            Lay.Place((RectTransform)_backBtn.transform, 0f, 560f, 640f, 96f);
            _landingCard = Lay.Group(lr, "LatestHost", 704f, 190f, 1024f, 466f);
        }

        void RefreshLanding()
        {
            var t = GlassTheme.T;
            _latest = CareerSession.Latest();
            bool any = _latest != null;
            _continueBtn.interactable = any;
            var list = new List<Selectable> { _continueBtn, _newBtn, _loadBtn, _backBtn };
            NavWire.Column(list);
            if (!any) NavWire.Set(_newBtn, null, _loadBtn);
            for (int i = _landingCard.childCount - 1; i >= 0; i--) UiKit.Kill(_landingCard.GetChild(i).gameObject);
            var card = GlassKit.Panel(_landingCard, "Card", 1.2f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 0f, 0f, 1024f, 466f);
            var cr = card.rectTransform;
            if (!any)
            {
                Lay.Caps(cr, "NO CAREER YET", 40f, 36f, 700f, 30f, t.accent, 20f);
                Lay.Text(cr, "Pick one of the 32 teams, set the difficulty and take over as general manager: roster, cap, free agency, trades, the draft and every game of the season.", 26f, t.textSecondary, 40f, 84f, 900f, 130f, false, TextAlignmentOptions.TopLeft, 0f, "Blurb", false).textWrappingMode = TextWrappingModes.Normal;
                return;
            }
            var m = _latest;
            var def = FindDef(m.TeamKey);
            Lay.Caps(cr, "LAST CAREER  -  SLOT " + m.Slot, 40f, 34f, 700f, 30f, t.accent, 20f);
            var crest = UiKit.Panel(cr, "Crest", Color.white, UiKit.Sprite("crest_" + m.TeamKey)); crest.preserveAspect = true; crest.enabled = crest.sprite != null; Lay.Place(crest.rectTransform, 40f, 86f, 190f, 190f);
            Lay.Text(cr, m.TeamName.ToUpperInvariant(), 46f, t.textPrimary, 260f, 90f, 720f, 64f, true, TextAlignmentOptions.MidlineLeft, 2f, "Team");
            Lay.Text(cr, m.Phase + "  -  " + m.Year + " season", 28f, t.textSecondary, 260f, 158f, 720f, 40f, false, TextAlignmentOptions.MidlineLeft, 0f, "Phase");
            Lay.Text(cr, "Record " + m.Record + "   -   season " + m.Seasons, 24f, t.textSecondary, 260f, 200f, 720f, 36f, false, TextAlignmentOptions.MidlineLeft, 0f, "Rec");
            Lay.Text(cr, CareerStartModel.PrefsText(m.Prefs), 22f, t.textMuted, 260f, 240f, 720f, 34f, false, TextAlignmentOptions.MidlineLeft, 0f, "Prefs");
            var rule = GlassKit.Rule(cr); Lay.Place(rule.rectTransform, 40f, 310f, 944f, 1.4f);
            Lay.Text(cr, "Saved " + m.SavedAt + "   -   " + (m.Bytes / 1048576f).ToString("0.0") + " MB", 22f, t.textMuted, 40f, 326f, 944f, 34f, false, TextAlignmentOptions.MidlineLeft, 0f, "Saved");
            Lay.Text(cr, "Every week and every phase saves by itself.", 22f, t.textSecondary, 40f, 366f, 944f, 34f, false, TextAlignmentOptions.MidlineLeft, 0f, "Auto");
        }

        static TeamInfo FindDef(string key) { foreach (var t in TeamCatalog.Teams) if (t.Id == key) return t; return null; }

        void ContinueLatest()
        {
            if (_latest == null) return;
            LoadSlot(_latest.Slot);
        }

        void LoadSlot(int slot)
        {
            string err = "";
            bool ok = false;
            _busy.Run(this, "Loading", "Reading the save", () => { ok = CareerSession.Load(slot, out err); FinishLoad(ok, err); });
        }

        void FinishLoad(bool ok, string err)
        {
            if (!ok) { UiDialog.Show(Root, "Could not load", err, "OK", "CLOSE", null, null); return; }
            if (Started != null) Started(FranchiseController.FromSession());
        }

        // ================================================================== new career
        void BuildNew()
        {
            var t = GlassTheme.T;
            _newPage = UiKit.Rect(_block, "NewCareer").gameObject; UiKit.Stretch((RectTransform)_newPage.transform);
            var nr = (RectTransform)_newPage.transform;
            // left column: team card, options, start
            _teamCard = Lay.Group(nr, "TeamCardHost", 0f, 0f, 620f, 250f);
            _diff = GlassKit.Stepper(nr, "Difficulty", CareerStartModel.Difficulties, 1, null, 620f, 58f); Lay.Place((RectTransform)_diff.transform, 0f, 266f, 620f, 58f);
            _quarter = GlassKit.Stepper(nr, "Quarter length", new[] { "2 min", "3 min", "5 min", "8 min", "15 min" }, 2, null, 620f, 58f); Lay.Place((RectTransform)_quarter.transform, 0f, 334f, 620f, 58f);
            _league = GlassKit.Stepper(nr, "League", new[] { "Standard", "Fresh (random)" }, 0, i => RefreshTeamCard(), 620f, 58f); Lay.Place((RectTransform)_league.transform, 0f, 402f, 620f, 58f);
            _slotSel = GlassKit.Stepper(nr, "Save slot", new[] { "Slot 1" }, 0, null, 620f, 58f); Lay.Place((RectTransform)_slotSel.transform, 0f, 470f, 620f, 58f);
            _startBtn = GlassKit.Button(nr, "START CAREER", TryStart, GlassButtonStyle.Primary, 620f, 80f, "ui_arrow_right", "Start");
            Lay.Place((RectTransform)_startBtn.transform, 0f, 600f, 620f, 80f);
            _newBack = GlassKit.Button(nr, "BACK", () => Show(0), GlassButtonStyle.Secondary, 620f, 64f, null, "Back");
            Lay.Place((RectTransform)_newBack.transform, 0f, 696f, 620f, 64f);

            // right: grid
            Lay.Caps(nr, "PICK YOUR TEAM", 664f, 0f, 400f, 36f, t.accent, 20f);
            _hover = Lay.Text(nr, "", 30f, t.textPrimary, 664f, 540f, 1000f, 44f, true, TextAlignmentOptions.MidlineLeft, 3f, "HoverName");
            var grid = UiKit.Rect(nr, "Grid"); Lay.Place(grid, 664f, 46f, 1064f, 520f);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(124f, 120f); gl.spacing = new Vector2(10f, 10f); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = GridCols; gl.startCorner = GridLayoutGroup.Corner.UpperLeft; gl.childAlignment = TextAnchor.UpperLeft;
            for (int i = 0; i < _teams.Count; i++)
            {
                int idx = i; var tm = _teams[i];
                string label = string.IsNullOrEmpty(tm.Abbr) ? tm.Nickname.Substring(0, Math.Min(3, tm.Nickname.Length)).ToUpperInvariant() : tm.Abbr;
                var tile = GlassWidgets.MakeCrestTile(grid, i, UiKit.Sprite("crest_" + tm.Id), tm.Abbr, label, tm.PrimaryUi, () => PickTeam(idx), 124f, 120f);
                Lay.Unlayout(tile.gameObject);
                var f = tile.gameObject.AddComponent<TileFocus>(); f.Owner = this; f.Index = i;
                _tiles.Add(tile);
            }
            _team = Mathf.Clamp(TeamCatalog.IndexOf(TeamCatalog.SavedTeamId), 0, _teams.Count - 1);
        }

        sealed class TileFocus : MonoBehaviour, ISelectHandler
        {
            public CareerStartPanel Owner; public int Index;
            public void OnSelect(BaseEventData e) { if (Owner != null) Owner.OnTileFocus(Index); }
        }

        void OnTileFocus(int i)
        {
            if (i < 0 || i >= _teams.Count) return;
            _hover.text = _teams[i].Name.ToUpperInvariant();
            _team = i; RefreshTeamCard();
        }

        void PickTeam(int i) { _team = i; RefreshTeamCard(); Focus(_startBtn); }

        static void Focus(Selectable s) { if (s != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(s.gameObject); }

        void RefreshNew()
        {
            _slots = CareerSession.Slots();
            var names = new string[_slots.Count];
            for (int i = 0; i < names.Length; i++) names[i] = CareerStartModel.SlotLabel(_slots[i]);
            _slotSel.SetOptions(names, Mathf.Clamp(CareerStartModel.DefaultSlot(_slots) - 1, 0, names.Length - 1));
            RefreshTeamCard();
            WireNew();
        }

        void RefreshTeamCard()
        {
            if (_teamCard == null) return;
            var t = GlassTheme.T;
            for (int i = _teamCard.childCount - 1; i >= 0; i--) UiKit.Kill(_teamCard.GetChild(i).gameObject);
            for (int i = 0; i < _tiles.Count; i++) _tiles[i].SetRole(i == _team ? "YOU" : null, i == _team);
            var info = _teams[Mathf.Clamp(_team, 0, _teams.Count - 1)];
            var card = GlassKit.Panel(_teamCard, "Card", 1.2f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 0f, 0f, 620f, 250f);
            var cr = card.rectTransform;
            var wash = GlassKit.Solid(cr, "Wash", new Color(info.PrimaryUi.r, info.PrimaryUi.g, info.PrimaryUi.b, 0.18f), t.cornerRadius); Lay.Place(wash.rectTransform, 12f, 12f, 596f, 226f);
            var crest = UiKit.Panel(cr, "Crest", Color.white, UiKit.Sprite("crest_" + info.Id)); crest.preserveAspect = true; crest.enabled = crest.sprite != null; Lay.Place(crest.rectTransform, 28f, 30f, 150f, 150f);
            Lay.Caps(cr, info.City, 200f, 30f, 390f, 26f, null, 18f);
            Lay.Text(cr, info.Nickname.ToUpperInvariant(), 44f, t.textPrimary, 200f, 54f, 400f, 58f, true, TextAlignmentOptions.MidlineLeft, 2f, "Nick");
            Lay.Text(cr, info.Conference + " " + info.Division, 22f, t.textSecondary, 200f, 112f, 400f, 32f, false, TextAlignmentOptions.MidlineLeft, 0f, "Div");
            var lg = MatchData.League;
            var team = lg.Teams.Find(x => x.Key == info.Id);
            if (team != null) Lay.Text(cr, MenuHubModel.VenueName(team) + (team.IsDome ? "  (dome)" : ""), 20f, t.textMuted, 200f, 142f, 400f, 30f, false, TextAlignmentOptions.MidlineLeft, 0f, "Venue");
            bool standard = _league == null || _league.Index == 0;
            string[] caps = { "OVR", "OFF", "DEF" };
            var hr = team != null ? MenuHubModel.Ratings(team) : default(HubRatings);
            int[] vals = { hr.Ovr, hr.Off, hr.Def };
            for (int i = 0; i < 3; i++)
            {
                var well = GlassKit.Well(cr, "St" + caps[i], t.cornerRadiusSmall); Lay.Place(well.rectTransform, 28f + i * 124f, 178f, 114f, 58f);
                Lay.Text(well.rectTransform, standard ? vals[i].ToString() : "--", 30f, t.textPrimary, 0f, 2f, 114f, 34f, true, TextAlignmentOptions.Center, 0f, "V");
                Lay.Caps(well.rectTransform, caps[i], 0f, 34f, 114f, 20f, null, 14f, TextAlignmentOptions.Center);
            }
            Lay.Text(cr, standard ? "Standard league ratings." : "A fresh random league:\nratings show when you start.", 18f, t.textMuted, 408f, 184f, 200f, 52f, false, TextAlignmentOptions.TopLeft, 0f, "Hint", false).textWrappingMode = TextWrappingModes.Normal;
        }

        void WireNew()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                var b = _tiles[i].Button;
                int up = GlassNav.GridMove(i, 0, -1, GridCols, _tiles.Count), down = GlassNav.GridMove(i, 0, 1, GridCols, _tiles.Count);
                int left = GlassNav.GridMove(i, -1, 0, GridCols, _tiles.Count), right = GlassNav.GridMove(i, 1, 0, GridCols, _tiles.Count);
                NavWire.Set(b, up != i ? _tiles[up].Button : null, down != i ? _tiles[down].Button : _startBtn, left != i ? _tiles[left].Button : (Selectable)_diff, right != i ? _tiles[right].Button : null);
            }
            NavWire.Column(new List<Selectable> { _diff, _quarter, _league, _slotSel, _startBtn, _newBack }, null, null, null, _tiles[0].Button);
            NavWire.Set(_diff, null, _quarter, null, _tiles[0].Button, true); NavWire.Set(_quarter, _diff, _league, null, _tiles[GridCols].Button);
            NavWire.Set(_league, _quarter, _slotSel, null, _tiles[GridCols * 2].Button); NavWire.Set(_slotSel, _league, _startBtn, null, _tiles[GridCols * 2].Button);
            NavWire.Set(_startBtn, _slotSel, _newBack, null, _tiles[GridCols * 3].Button); NavWire.Set(_newBack, _startBtn, null, null, _tiles[_tiles.Count - 1].Button);
        }

        void TryStart()
        {
            var meta = _slots[Mathf.Clamp(_slotSel.Index, 0, _slots.Count - 1)];
            if (meta.Valid) UiDialog.Show(Root, "Replace this career?", "Slot " + meta.Slot + " holds " + meta.TeamName + " (" + meta.Year + "). Starting a new career overwrites it.", "REPLACE", "KEEP IT", () => Create(meta.Slot), null);
            else Create(meta.Slot);
        }

        /// <summary>Creates the franchise with the screen's choices (also used by tests and tools).</summary>
        public void Create(int slot)
        {
            var info = _teams[Mathf.Clamp(_team, 0, _teams.Count - 1)];
            var prefs = CareerStartModel.Prefs(_diff.Index, _quarter.Index);
            int seed = CareerStartModel.SeedFor(_league.Index == 0, Environment.TickCount);
            var lg = MatchData.League;
            var tm = lg.Teams.Find(x => x.Key == info.Id);
            int userId = tm != null ? tm.Id : _team;
            TeamCatalog.SavedTeamId = info.Id;
            _busy.Run(this, "Building your league", "32 teams, rosters, contracts and the schedule", () =>
            {
                CareerSession.DeleteSlot(slot);
                CareerSession.StartNew(userId, seed, prefs, slot, info.Name + " Franchise");
                if (Started != null) Started(FranchiseController.FromSession());
            }, e => UiDialog.Show(Root, "Could not start", e.Message, "OK", "CLOSE", null, null));
        }

        // ================================================================== load
        void BuildLoad()
        {
            var t = GlassTheme.T;
            _loadPage = UiKit.Rect(_block, "Load").gameObject; UiKit.Stretch((RectTransform)_loadPage.transform);
            var lr = (RectTransform)_loadPage.transform;
            Lay.Caps(lr, "SAVED CAREERS", 0f, 0f, 400f, 36f, t.accent, 20f);
            var cols = new List<TableColumn>
            {
                new TableColumn("SLOT", 0.5f, i => _loadRows[i].Slot.ToString(), false, null, false),
                new TableColumn("TEAM", 2.2f, i => _loadRows[i].Valid ? _loadRows[i].TeamName : "Empty slot", false, i => _loadRows[i].Valid ? (Color?)null : GlassTheme.T.textMuted, false),
                new TableColumn("SEASON", 1.1f, i => _loadRows[i].Valid ? _loadRows[i].Year.ToString() : "", false, null, false),
                new TableColumn("PHASE", 2.0f, i => _loadRows[i].Valid ? _loadRows[i].Phase : "", false, null, false),
                new TableColumn("RECORD", 1f, i => _loadRows[i].Valid ? _loadRows[i].Record : "", true, null, false)
            };
            _table = GlassTable.Create(lr, "SlotTable", 1000f, 40f + 5 * 72f, cols, 72f);
            Lay.Place((RectTransform)_table.transform, 0f, 50f, 1000f, 40f + 5 * 72f);
            _table.EmptyText = "No saves yet.";
            _table.Activated = i => { if (_loadRows[i].Valid) LoadSlot(_loadRows[i].Slot); };
            _table.AltAction = i => { if (_loadRows[i].Valid) { var m = _loadRows[i]; UiDialog.Show(Root, "Delete this career?", m.TeamName + ", " + m.Year + ". This cannot be undone.", "DELETE", "KEEP IT", () => { CareerSession.DeleteSlot(m.Slot); RefreshLoad(); }, null); } };
            _table.CursorChanged = i => RefreshLoadCard();
            _loadCard = Lay.Group(lr, "Detail", 1024f, 50f, 704f, 520f);
            _loadBack = GlassKit.Button(lr, "BACK", () => Show(0), GlassButtonStyle.Secondary, 1000f, 64f, null, "Back");
            Lay.Place((RectTransform)_loadBack.transform, 0f, 50f + 40f + 5 * 72f + 24f, 1000f, 64f);
            NavWire.Set(_table, null, _loadBack, null, null); NavWire.Set(_loadBack, _table, null, null, null);
        }

        void RefreshLoad()
        {
            _loadRows = CareerSession.Slots();
            _table.SetCount(_loadRows.Count);
            RefreshLoadCard();
        }

        void RefreshLoadCard()
        {
            var t = GlassTheme.T;
            for (int i = _loadCard.childCount - 1; i >= 0; i--) UiKit.Kill(_loadCard.GetChild(i).gameObject);
            if (_loadRows.Count == 0) return;
            var m = _loadRows[Mathf.Clamp(_table.Cursor, 0, _loadRows.Count - 1)];
            var card = GlassKit.Panel(_loadCard, "Card", 1.2f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 0f, 0f, 704f, 520f);
            var cr = card.rectTransform;
            if (!m.Valid) { Lay.Caps(cr, "SLOT " + m.Slot, 32f, 30f, 400f, 28f, t.textSecondary, 18f); Lay.Text(cr, "Empty. Start a new career to fill it.", 26f, t.textMuted, 32f, 70f, 640f, 40f, false, TextAlignmentOptions.MidlineLeft, 0f, "Empty"); return; }
            var crest = UiKit.Panel(cr, "Crest", Color.white, UiKit.Sprite("crest_" + m.TeamKey)); crest.preserveAspect = true; crest.enabled = crest.sprite != null; Lay.Place(crest.rectTransform, 32f, 32f, 150f, 150f);
            Lay.Caps(cr, "SLOT " + m.Slot, 204f, 36f, 440f, 26f, t.accent, 18f);
            Lay.Text(cr, m.TeamName.ToUpperInvariant(), 34f, t.textPrimary, 204f, 62f, 470f, 50f, true, TextAlignmentOptions.MidlineLeft, 1f, "Team");
            Lay.Text(cr, m.Phase, 24f, t.textSecondary, 204f, 114f, 470f, 34f, false, TextAlignmentOptions.MidlineLeft, 0f, "Phase");
            Lay.Text(cr, "Season " + m.Seasons + " (" + m.Year + ")   record " + m.Record, 22f, t.textSecondary, 204f, 150f, 470f, 32f, false, TextAlignmentOptions.MidlineLeft, 0f, "Rec");
            var rule = GlassKit.Rule(cr); Lay.Place(rule.rectTransform, 32f, 214f, 640f, 1.4f);
            var sp = StackPanel.Create(cr, "Lines", 32f, 230f, 640f, 270f);
            sp.Line("Difficulty", CareerStartModel.Difficulties[Mathf.Clamp(m.Prefs.Difficulty, 0, 2)]);
            sp.Line("Quarter length", m.Prefs.QuarterMinutes + " minutes");
            sp.Line("Saved", m.SavedAt);
            sp.Line("File size", (m.Bytes / 1048576f).ToString("0.0") + " MB");
            sp.Gap(8f);
            sp.Para("A loads this career. X deletes it.", 20f, t.textMuted);
        }
    }
}
