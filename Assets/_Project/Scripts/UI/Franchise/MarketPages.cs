using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    // ====================================================================================================== FREE AGENCY
    /// <summary>The offer window for one free agent: what he asks, how he judges the offer (money, contender, role, mood), the editor and the buttons. Outside the bidding the same window signs him on the spot.</summary>
    public static class FaOfferSheet
    {
        public static void Show(FranchiseUi shell, CareerSave save, Player p, Action changed)
        {
            var t = GlassTheme.T;
            var u = save.UserTeam;
            bool bidding = save.Phase == CareerPhase.FreeAgency;
            var size = new Vector2(1240f, 700f);
            var m = ModalView.Create(shell, size, null);
            var ask = FreeAgency.Ask(save, p);
            var existing = FreeAgency.OfferOf(save, u.Id, p.Id);
            Lay.Caps(m.Box, bidding ? "FREE AGENCY  -  DAY " + save.FaDay + " OF " + save.Settings.FreeAgencyDays : "STREET SIGNING", 44f, 24f, 700f, 28f, t.accent, 18f);
            Lay.Text(m.Box, p.Name.ToUpperInvariant(), 44f, t.textPrimary, 44f, 52f, 760f, 60f, true, TextAlignmentOptions.MidlineLeft, 2f, "Name");
            Lay.Text(m.Box, p.Position + "  -  " + p.Overall + " OVR  -  " + p.Age + " yrs  -  asks " + Fmt.MoneyShort(ask.Aav) + " x " + ask.Years + "  -  " + FreeAgency.PriorityOf(p).ToString().ToUpperInvariant() + " first", 22f, t.textSecondary, 44f, 112f, size.x - 88f, 32f, false);
            var rule = GlassKit.Rule(m.Box); Lay.Place(rule.rectTransform, 44f, 156f, size.x - 88f, 1.4f);

            var editor = OfferEditor.Build(m.Box, 660f, 190f, 536f, 56f);
            var evalHost = Lay.Group(m.Box, "Eval", 44f, 176f, 580f, 380f);
            var verdict = Lay.Text(m.Box, "", 22f, t.textPrimary, 660f, 408f, 536f, 110f, true, TextAlignmentOptions.TopLeft, 0f, "Verdict", false); verdict.textWrappingMode = TextWrappingModes.Normal;
            long minSal = CapMath.MinSalary(save, save.Year + 1);
            editor.Set(existing != null ? existing.Aav : ask.Aav, existing != null ? existing.Years : ask.Years, existing != null ? existing.BonusPct : ask.BonusPct, minSal);

            Action refresh = () =>
            {
                for (int i = evalHost.childCount - 1; i >= 0; i--) UiKit.Kill(evalHost.GetChild(i).gameObject);
                var contract = CapMath.Veteran(save, editor.Aav, editor.Years, editor.BonusPct, save.Year + 1);
                var sp = StackPanel.Create(evalHost, "S", 0f, 0f, 580f, 380f);
                if (bidding)
                {
                    var ev = FreeAgency.Evaluate(save, p, u.Id, editor.Aav, editor.Years, editor.BonusPct);
                    sp.Caps("HOW HE SEES YOUR OFFER");
                    sp.Bar("Money", ev.Money, Mathf.RoundToInt(ev.Money * 100f).ToString(), false, 34f);
                    sp.Bar("Contender", ev.Contender, Mathf.RoundToInt(ev.Contender * 100f).ToString(), false, 34f);
                    sp.Bar("Role", ev.Role, Mathf.RoundToInt(ev.Role * 100f).ToString(), false, 34f);
                    sp.Bar("Mood", ev.Mood, Mathf.RoundToInt(ev.Mood * 100f).ToString(), false, 34f);
                    sp.Rule();
                    sp.Line("Score / needed today", ev.Score.ToString("0.00") + " / " + ev.Threshold.ToString("0.00"), ev.WillSign ? t.success : t.warning, 36f, 22f);
                    verdict.text = ev.WillSign ? "He would sign this offer today." : ev.Reason;
                    verdict.color = ev.WillSign ? t.success : t.warning;
                }
                else
                {
                    sp.Caps("STREET SIGNING");
                    sp.Para("The bidding is closed. He signs right now if you pay at least 95 percent of his ask.", 22f);
                    verdict.text = editor.Aav >= ask.Aav * 0.95 ? "He will sign." : "He wants at least " + Fmt.MoneyShort((long)(ask.Aav * 0.95)) + ".";
                    verdict.color = editor.Aav >= ask.Aav * 0.95 ? t.success : t.warning;
                }
                sp.Gap(6f);
                sp.Line("First-year cap hit", Fmt.MoneyShort(contract.CapHit), contract.CapHit > u.SalaryCap - u.CapUsed ? t.danger : t.textPrimary, 34f, 22f);
                sp.Line("Your cap space", Fmt.MoneyShort(u.SalaryCap - u.CapUsed), null, 34f, 22f);
            };
            editor.Changed = refresh; refresh();

            var buttons = new List<Selectable>();
            if (bidding)
            {
                var place = GlassKit.Button(m.Box, existing != null ? "UPDATE OFFER" : "PLACE OFFER", () =>
                {
                    var r = FreeAgency.PlaceOffer(save, p.Id, editor.Aav, editor.Years, editor.BonusPct);
                    shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info");
                    if (r.Ok) { m.Close(); if (changed != null) changed(); }
                }, GlassButtonStyle.Primary, 400f, 66f, null, "Place", 26f);
                Lay.Place((RectTransform)place.transform, 660f, size.y - 100f, 270f, 66f); buttons.Add(place);
                if (existing != null)
                {
                    var wd = GlassKit.Button(m.Box, "WITHDRAW", () => { var r = FreeAgency.WithdrawOffer(save, p.Id); shell.Toast(r.Message); m.Close(); if (changed != null) changed(); }, GlassButtonStyle.Secondary, 250f, 66f, null, "Withdraw", 22f);
                    Lay.Place((RectTransform)wd.transform, 942f, size.y - 100f, 250f, 66f); buttons.Add(wd);
                }
            }
            else
            {
                var sign = GlassKit.Button(m.Box, "SIGN NOW", () =>
                {
                    var r = FreeAgency.SignNow(save, u, p.Id, editor.Aav, editor.Years);
                    shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info");
                    if (r.Ok) { m.Close(); if (changed != null) changed(); }
                }, GlassButtonStyle.Primary, 270f, 66f, null, "Sign", 26f);
                Lay.Place((RectTransform)sign.transform, 660f, size.y - 100f, 270f, 66f); buttons.Add(sign);
                if (p.Experience <= RosterRules.PracticeSquadMaxExperience)
                {
                    var ps = GlassKit.Button(m.Box, "SQUAD", () =>
                    {
                        var r = FreeAgency.SignNow(save, u, p.Id, minSal, 1, true);
                        shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info");
                        if (r.Ok) { m.Close(); if (changed != null) changed(); }
                    }, GlassButtonStyle.Secondary, 250f, 66f, null, "Squad", 22f);
                    Lay.Place((RectTransform)ps.transform, 942f, size.y - 100f, 250f, 66f); buttons.Add(ps);
                }
            }
            var cancel = GlassKit.Button(m.Box, "CANCEL", () => m.Close(), GlassButtonStyle.Secondary, 270f, 66f, null, "Cancel", 22f);
            Lay.Place((RectTransform)cancel.transform, 44f, size.y - 100f, 270f, 66f); buttons.Add(cancel);
            // navigation: the three steppers, then the buttons in a row
            var sels = new List<Selectable>(editor.Selectables());
            NavWire.Column(sels, null, buttons[0]);
            for (int i = 0; i < buttons.Count; i++) NavWire.Set(buttons[i], sels[2], null, i > 0 ? buttons[i - 1] : null, i + 1 < buttons.Count ? buttons[i + 1] : null);
            m.SelectFirst(editor.AavSel);
        }
    }

    /// <summary>OFFICE &gt; FREE AGENCY: the market (position and rating filters), the standing offers, the day button and the results of every day.</summary>
    public sealed class FreeAgencyPage : FranchisePage
    {
        GlassTable _table; PlayerCardPanel _card; OptionSelector _posSel, _ovrSel; TextMeshProUGUI _info; Button _day, _skip;
        List<Player> _pool = new List<Player>();
        readonly int[] _ovrChoices = { 0, 60, 65, 70, 75, 80 };
        int _sortCol = -1; bool _desc = true;

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            _posSel = GlassKit.Stepper(root, "Position", PosFilter.Names, 0, i => Refresh(), 340f, 56f); Lay.Place((RectTransform)_posSel.transform, 0f, 0f, 340f, 56f);
            _ovrSel = GlassKit.Stepper(root, "Min OVR", new[] { "Any", "60+", "65+", "70+", "75+", "80+" }, 0, i => Refresh(), 340f, 56f); Lay.Place((RectTransform)_ovrSel.transform, 354f, 0f, 340f, 56f);
            _info = Lay.Text(root, "", 20f, t.textPrimary, 706f, 0f, 380f, 56f, false, TextAlignmentOptions.MidlineRight, 0f, "Info"); Lay.Under(_info, 10f, 0f);
            var cols = new List<TableColumn>
            {
                new TableColumn("NAME", 2.5f, i => _pool[i].Name),
                new TableColumn("POS", 0.8f, i => _pool[i].Position.ToString()),
                new TableColumn("OVR", 0.8f, i => _pool[i].Overall.ToString(), true, i => Fmt.OvrColor(_pool[i].Overall)),
                new TableColumn("AGE", 0.6f, i => _pool[i].Age.ToString(), true),
                new TableColumn("ASK", 1.2f, i => Fmt.MoneyShort(FreeAgency.Ask(Save, _pool[i]).Aav), true),
                new TableColumn("WANTS", 1.2f, i => FreeAgency.PriorityOf(_pool[i]).ToString().ToUpperInvariant(), true, null, false),
                new TableColumn("YOUR OFFER", 1.5f, i => OfferText(_pool[i]), true, i => FreeAgency.OfferOf(Save, Save.UserTeamId, _pool[i].Id) != null ? (Color?)GlassTheme.T.accent : null, false)
            };
            _table = GlassTable.Create(root, "PoolTable", 1080f, 598f, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 68f, 1080f, 598f);
            _table.EmptyText = "No free agents match.";
            _table.CursorChanged = i => ShowCard();
            _table.Activated = i => FaOfferSheet.Show(Shell, Save, _pool[i], () => Ctl.Touch());
            _table.HeaderClicked = SortBy; _table.SortRequested = SortBy;
            _card = PlayerCardPanel.Create(root, 1096f, 68f, 632f, 598f);
            _day = GlassKit.Button(root, "ADVANCE DAY", AdvanceDay, GlassButtonStyle.Primary, 280f, 56f, null, "Day", 22f); Lay.Place((RectTransform)_day.transform, 1728f - 280f, 0f, 280f, 56f);
            _skip = GlassKit.Button(root, "SKIP TO THE END", SkipAll, GlassButtonStyle.Secondary, 280f, 56f, null, "Skip", 20f); Lay.Place((RectTransform)_skip.transform, 1728f - 280f * 2f - 14f, 0f, 280f, 56f);
        }

        string OfferText(Player p)
        {
            var o = FreeAgency.OfferOf(Save, Save.UserTeamId, p.Id);
            return o == null ? "" : Fmt.MoneyShort(o.Aav) + " x " + o.Years;
        }

        public override Selectable Entry { get { return _table; } }
        public override PromptItem[] Prompts() { return new[] { PromptFooter.Item("glyph_a", "OFFER"), PromptFooter.Item("glyph_y", "SORT") }; }
        public override void OnHide() { if (_card != null) _card.DisposeStage(); }

        void SortBy(int col)
        {
            if (col == _sortCol) _desc = !_desc; else { _sortCol = col; _desc = col != 0 && col != 1; }
            Refresh();
        }

        public override void Refresh()
        {
            if (!Built) return;
            int keep = _pool.Count > 0 && _table.Cursor < _pool.Count ? _pool[_table.Cursor].Id : -1;
            var all = FreeAgency.Pool(Save, false, PositionBucket.QB, _ovrChoices[_ovrSel.Index]);
            _pool = all.FindAll(p => PosFilter.Match(_posSel.Index, p.Position));
            if (_sortCol >= 0)
            {
                Comparison<Player> c;
                switch (_sortCol)
                {
                    case 0: c = (a, b) => string.CompareOrdinal(a.Name, b.Name); break;
                    case 1: c = (a, b) => RosterSort.PosIndex(a.Position).CompareTo(RosterSort.PosIndex(b.Position)); break;
                    case 2: c = (a, b) => a.Overall.CompareTo(b.Overall); break;
                    case 3: c = (a, b) => a.Age.CompareTo(b.Age); break;
                    default: c = (a, b) => FreeAgency.Ask(Save, a).Aav.CompareTo(FreeAgency.Ask(Save, b).Aav); break;
                }
                bool d = _desc;
                _pool.Sort((a, b) => d ? c(b, a) : c(a, b));
            }
            _table.SetCount(_pool.Count); _table.SetSort(_sortCol, _desc);
            int at = _pool.FindIndex(p => p.Id == keep); if (at >= 0) _table.SetCursor(at, false);
            bool bidding = Save.Phase == CareerPhase.FreeAgency;
            int offers = Save.FaOffers.FindAll(o => o.TeamId == Save.UserTeamId).Count;
            _info.text = bidding ? (FreeAgency.IsDone(Save) ? "Bidding is over" : "Day " + Save.FaDay + " of " + Save.Settings.FreeAgencyDays) + "   -   " + Fmt.Plural(offers, "offer", "offers") + " out" : "Street signings only";
            _day.interactable = bidding && !FreeAgency.IsDone(Save); _skip.interactable = bidding && !FreeAgency.IsDone(Save);
            ShowCard();
        }

        void ShowCard()
        {
            if (_pool.Count == 0) { _card.Fill(Save, null); return; }
            _card.Show(Save, _pool[Mathf.Clamp(_table.Cursor, 0, _pool.Count - 1)].Id);
        }

        void AdvanceDay()
        {
            Shell.Busy("Free agency day", () =>
            {
                var r = Ctl.Advance();
                if (!r.Ok) { Shell.Toast(Ctl.Message); return; }
                var d = r.FreeAgencyDay;
                if (d == null) { Shell.Toast(Ctl.Message); return; }
                var lines = new List<string>();
                if (d.UserNotes.Count > 0) { lines.Add("#YOUR OFFERS"); lines.AddRange(d.UserNotes); }
                lines.Add("#BIGGEST SIGNINGS OF THE DAY");
                var best = new List<FaSigning>(d.Signings); best.Sort((a, b) => b.Ovr.CompareTo(a.Ovr));
                for (int i = 0; i < best.Count && i < 10; i++) lines.Add(best[i].PlayerName + " (" + best[i].Pos + ", " + best[i].Ovr + " OVR) signs with " + CareerText.TeamNick(Save, best[i].TeamId) + ": " + Fmt.MoneyShort(best[i].Aav) + " x " + best[i].Years);
                if (best.Count == 0) lines.Add("Nobody signed today.");
                InfoModal.Show(Shell, "Day " + d.Day + " results", "Free agency", lines);
            });
        }

        void SkipAll()
        {
            Shell.Confirm("Skip the rest of free agency?", "The remaining days play out now with your standing offers.", "SKIP", () => Shell.Busy("Free agency", () => { CareerFlow.SkipFreeAgency(Save); Ctl.Autosave(); Shell.Toast("Free agency bidding is over."); Ctl.Touch(); }));
        }
    }

    // ====================================================================================================== TRADES
    /// <summary>OFFICE &gt; TRADES: build a deal with any team from your players and picks against theirs, watch the value meter, ask what it takes, propose; the CPU offers wait in the inbox until the deadline.</summary>
    public sealed class TradesPage : FranchisePage
    {
        public static int Wanted;
        sealed class Asset { public bool IsPick; public int Id; public string Name, Kind; public int Ovr; public float Value; public bool Selected, Blocked; }

        OptionSelector _viewSel, _partnerSel; TextMeshProUGUI _window;
        GlassTable _mine, _theirs, _inbox;
        RectTransform _summary, _inboxDetail;
        Button _propose, _ask, _clear, _accept, _reject;
        List<Asset> _a = new List<Asset>(), _b = new List<Asset>();
        List<TradeOffer> _offers = new List<TradeOffer>();
        List<LeagueTeam> _partners = new List<LeagueTeam>();
        TradeProposal _tp = new TradeProposal();
        TradeEvaluation _ev;
        bool Inbox { get { return _viewSel.Index == 1; } }
        LeagueTeam Partner { get { return _partners[Mathf.Clamp(_partnerSel.Index, 0, _partners.Count - 1)]; } }

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            _viewSel = GlassKit.Stepper(root, "View", new[] { "New deal", "Inbox" }, 0, i => SwitchView(), 420f, 56f); Lay.Place((RectTransform)_viewSel.transform, 0f, 0f, 420f, 56f);
            _partners = new List<LeagueTeam>();
            foreach (var tm in Save.Pro.Teams) if (tm.Id != Save.UserTeamId) _partners.Add(tm);
            var names = new string[_partners.Count];
            for (int i = 0; i < names.Length; i++) names[i] = _partners[i].City + " " + _partners[i].Nickname + "  (" + _partners[i].TeamOverall() + ")";
            _partnerSel = GlassKit.Stepper(root, "Trade with", names, 0, i => { _tp = new TradeProposal(); RebuildAssets(); }, 640f, 56f); Lay.Place((RectTransform)_partnerSel.transform, 436f, 0f, 640f, 56f);
            _window = Lay.Text(root, "", 20f, t.textSecondary, 1092f, 0f, 636f, 56f, true, TextAlignmentOptions.MidlineRight, 0f, "Window"); Lay.Under(_window, 12f, 0f);

            var cols = new Func<List<Asset>, List<TableColumn>>(list => new List<TableColumn>
            {
                new TableColumn("", 0.45f, i => list[i].Selected ? "YES" : "", false, i => GlassTheme.T.accent, false),
                new TableColumn("ASSET", 2.6f, i => list[i].Name, false, null, false),
                new TableColumn("POS", 0.8f, i => list[i].Kind, false, null, false),
                new TableColumn("OVR", 0.7f, i => list[i].IsPick ? "" : list[i].Ovr.ToString(), true, null, false),
                new TableColumn("VALUE", 0.9f, i => Mathf.RoundToInt(list[i].Value).ToString(), true, null, false)
            });
            _mine = GlassTable.Create(root, "MyAssets", 560f, 598f, cols(_a)); Lay.Place((RectTransform)_mine.transform, 0f, 68f, 560f, 598f);
            _theirs = GlassTable.Create(root, "TheirAssets", 560f, 598f, cols(_b)); Lay.Place((RectTransform)_theirs.transform, 576f, 68f, 560f, 598f);
            foreach (var tb in new[] { _mine, _theirs }) { tb.HandleSortKey = false; }
            _mine.RowTint = i => _a[i].Blocked ? (Color?)GlassTheme.T.textMuted : null; _theirs.RowTint = i => _b[i].Blocked ? (Color?)GlassTheme.T.textMuted : null;
            _mine.Activated = i => Toggle(_a, i, true); _theirs.Activated = i => Toggle(_b, i, false);
            _mine.AltAction = i => Shell.Toast(Describe(_a[i])); _theirs.AltAction = i => Shell.Toast(Describe(_b[i]));
            _mine.EmptyText = "Nothing to trade."; _theirs.EmptyText = "Nothing to trade.";

            var sc = GlassKit.Panel(root, "SummaryCard", 1.2f, true, t.cornerRadiusLarge); Lay.Place(sc.rectTransform, 1152f, 68f, 576f, 598f);
            _summary = Lay.Group(sc.rectTransform, "Summary", 24f, 18f, 528f, 380f);
            _propose = GlassKit.Button(sc.rectTransform, "PROPOSE TRADE", Propose, GlassButtonStyle.Primary, 528f, 62f, null, "Propose", 24f); Lay.Place((RectTransform)_propose.transform, 24f, 410f, 528f, 62f);
            _ask = GlassKit.Button(sc.rectTransform, "WHAT WOULD IT TAKE?", AskTake, GlassButtonStyle.Secondary, 528f, 56f, null, "Ask", 22f); Lay.Place((RectTransform)_ask.transform, 24f, 482f, 528f, 56f);
            _clear = GlassKit.Button(sc.rectTransform, "CLEAR THE DEAL", () => { _tp = new TradeProposal(); RebuildAssets(); }, GlassButtonStyle.Secondary, 528f, 56f, null, "Clear", 22f); Lay.Place((RectTransform)_clear.transform, 24f, 546f - 4f, 528f, 48f);

            // inbox
            var icols = new List<TableColumn>
            {
                new TableColumn("FROM", 1.9f, i => CareerText.TeamName(Save, _offers[i].FromTeamId), false, null, false),
                new TableColumn("THEY SEND", 2.6f, i => Summarize(_offers[i].Proposal.FromA), false, null, false),
                new TableColumn("THEY WANT", 2.6f, i => Summarize(_offers[i].Proposal.FromB), false, null, false),
                new TableColumn("EXPIRES", 0.9f, i => "wk " + _offers[i].ExpiresWeek, true, null, false)
            };
            _inbox = GlassTable.Create(root, "Inbox", 1120f, 598f, icols); Lay.Place((RectTransform)_inbox.transform, 0f, 68f, 1120f, 598f);
            _inbox.EmptyText = "No offers right now. CPU teams make offers before the deadline."; _inbox.HandleSortKey = false;
            _inbox.CursorChanged = i => ShowOffer();
            var dc = GlassKit.Panel(root, "OfferCard", 1.2f, true, t.cornerRadiusLarge); Lay.Place(dc.rectTransform, 1136f, 68f, 592f, 598f);
            _inboxDetail = Lay.Group(dc.rectTransform, "Detail", 24f, 18f, 544f, 400f);
            _accept = GlassKit.Button(dc.rectTransform, "ACCEPT", () => AcceptOffer(true), GlassButtonStyle.Primary, 260f, 62f, null, "Accept", 24f); Lay.Place((RectTransform)_accept.transform, 24f, 510f, 260f, 62f);
            _reject = GlassKit.Button(dc.rectTransform, "REJECT", () => AcceptOffer(false), GlassButtonStyle.Secondary, 260f, 62f, null, "Reject", 24f); Lay.Place((RectTransform)_reject.transform, 308f, 510f, 260f, 62f);
            _inbox.Activated = i => AcceptOffer(true);
            _inbox.gameObject.SetActive(false); dc.gameObject.SetActive(false); _inboxCard = dc.gameObject;
        }

        GameObject _inboxCard;

        string Describe(Asset a)
        {
            if (a.IsPick) return a.Name + ": worth " + Mathf.RoundToInt(a.Value) + " trade points.";
            return a.Name + ": " + a.Kind + ", " + a.Ovr + " OVR, worth " + Mathf.RoundToInt(a.Value) + " trade points" + (a.Blocked ? " (cannot be traded now)" : "") + ".";
        }

        string Summarize(List<TradeAsset> l)
        {
            var parts = new List<string>();
            foreach (var a in l) parts.Add(AssetName(a));
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts.ToArray());
        }

        string AssetName(TradeAsset a)
        {
            if (a.IsPick) { var k = TradeEngine.FindPick(Save, a.PickId); return k != null ? TradeEngine.PickLabel(Save, k) : "pick"; }
            var p = Save.FindPlayer(a.PlayerId); return p != null ? p.Name + " (" + p.Overall + ")" : "player";
        }

        public override Selectable Entry { get { return _viewSel; } }
        public override PromptItem[] Prompts() { return Inbox ? new[] { PromptFooter.Item("glyph_a", "ACCEPT") } : new[] { PromptFooter.Item("glyph_a", "ADD / REMOVE"), PromptFooter.Item("glyph_x", "VALUE") }; }

        public override void OnShow()
        {
            if (Wanted > 0) { _tp = new TradeProposal(); _tp.TeamA = Save.UserTeamId; _tp.FromA.Add(TradeAsset.OfPlayer(Wanted)); Wanted = 0; }
            Refresh();
        }

        void SwitchView()
        {
            Refresh(); Shell.UpdateFooter();
        }

        public override void Refresh()
        {
            if (!Built) return;
            var t = GlassTheme.T;
            _offers = TradeEngine.PendingOffers(Save);
            var items = new[] { "New deal", "Inbox (" + _offers.Count + ")" };
            _viewSel.SetOptions(items, _viewSel.Index);
            var w = TradeEngine.Window(Save);
            _window.text = w == TradeWindow.Open ? "Trade window open until week " + Save.Settings.TradeDeadlineWeek : w == TradeWindow.ClosedDeadline ? "The trade deadline has passed" : "Trades are closed during the postseason";
            _window.color = w == TradeWindow.Open ? t.textSecondary : t.warning;
            bool inbox = Inbox;
            _mine.gameObject.SetActive(!inbox); _theirs.gameObject.SetActive(!inbox); _summary.parent.gameObject.SetActive(!inbox); _partnerSel.gameObject.SetActive(!inbox);
            _inbox.gameObject.SetActive(inbox); _inboxCard.SetActive(inbox);
            if (inbox) { _inbox.SetCount(_offers.Count); ShowOffer(); }
            else RebuildAssets();
        }

        // ---------------------------------------------------------------- building a deal
        void RebuildAssets()
        {
            var u = Ctl.User; var p = Partner;
            _tp.TeamA = u.Id; _tp.TeamB = p.Id;
            BuildAssets(u, _a, _tp.FromA); BuildAssets(p, _b, _tp.FromB);
            _mine.SetCount(_a.Count); _theirs.SetCount(_b.Count);
            Evaluate();
        }

        void BuildAssets(LeagueTeam team, List<Asset> into, List<TradeAsset> chosen)
        {
            into.Clear();
            foreach (var pl in team.Roster)
            {
                bool sel = chosen.Exists(c => !c.IsPick && c.PlayerId == pl.Id);
                into.Add(new Asset { Id = pl.Id, Name = pl.Name, Kind = pl.Position.ToString(), Ovr = pl.Overall, Value = TradeEngine.PlayerValue(Save, pl), Selected = sel, Blocked = pl.Contract.YearsLeft <= 1 });
            }
            foreach (var k in TradeEngine.PicksOf(Save, team.Id))
            {
                bool sel = chosen.Exists(c => c.IsPick && c.PickId == k.Id);
                into.Add(new Asset { IsPick = true, Id = k.Id, Name = TradeEngine.PickLabel(Save, k), Kind = "R" + k.Round, Value = TradeEngine.PickValue(Save, k), Selected = sel });
            }
            into.Sort((x, y) => y.Value.CompareTo(x.Value));
        }

        void Toggle(List<Asset> list, int i, bool mine)
        {
            var a = list[i];
            var chosen = mine ? _tp.FromA : _tp.FromB;
            if (a.Selected) chosen.RemoveAll(c => a.IsPick ? (c.IsPick && c.PickId == a.Id) : (!c.IsPick && c.PlayerId == a.Id));
            else chosen.Add(a.IsPick ? TradeAsset.OfPick(a.Id) : TradeAsset.OfPlayer(a.Id));
            a.Selected = !a.Selected;
            _mine.Refresh(); _theirs.Refresh();
            Evaluate();
        }

        void Evaluate()
        {
            var t = GlassTheme.T;
            for (int i = _summary.childCount - 1; i >= 0; i--) UiKit.Kill(_summary.GetChild(i).gameObject);
            bool any = _tp.FromA.Count + _tp.FromB.Count > 0;
            _ev = any ? TradeEngine.Evaluate(Save, _tp) : null;
            var sp = StackPanel.Create(_summary, "S", 0f, 0f, 528f, 380f);
            sp.Caps("YOU SEND");
            if (_tp.FromA.Count == 0) sp.Para("nothing yet", 20f, t.textMuted);
            foreach (var a in _tp.FromA) if (sp.Fits(30f)) sp.Line(AssetName(a), Mathf.RoundToInt(TradeEngine.AssetValue(Save, a)).ToString(), null, 28f, 20f);
            sp.Gap(4f); sp.Caps("YOU GET");
            if (_tp.FromB.Count == 0) sp.Para("nothing yet", 20f, t.textMuted);
            foreach (var a in _tp.FromB) if (sp.Fits(30f)) sp.Line(AssetName(a), Mathf.RoundToInt(TradeEngine.AssetValue(Save, a)).ToString(), null, 28f, 20f);
            if (_ev != null)
            {
                float give = _ev.AGives, get = _ev.AReceives, max = Mathf.Max(1f, Mathf.Max(give, get));
                sp.Gap(6f);
                sp.Bar("You give", give / max, Mathf.RoundToInt(give).ToString(), false, 30f);
                sp.Bar("You get", get / max, Mathf.RoundToInt(get).ToString(), true, 30f);
                var msg = _ev.Legal ? _ev.Message : (_ev.Problems.Count > 0 ? _ev.Problems[0] : "Not a legal trade.");
                sp.Para(msg, 19f, _ev.Legal && _ev.CpuAccepts ? t.success : t.warning, 56f);
            }
            _propose.interactable = _ev != null && _ev.Legal && _ev.CpuAccepts && TradeEngine.IsOpen(Save);
            _ask.interactable = _tp.FromB.Count > 0 && TradeEngine.IsOpen(Save);
            _clear.interactable = any;
        }

        void Propose()
        {
            var r = TradeEngine.Propose(Save, _tp);
            Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info");
            if (r.Ok) { _tp = new TradeProposal(); Ctl.Touch(); }
            else Evaluate();
        }

        void AskTake()
        {
            var counter = TradeEngine.AskWhatItTakes(Save, _tp);
            if (counter == null) { Shell.Toast("They would not make that deal at any price."); return; }
            _tp = counter; _tp.TeamA = Save.UserTeamId; _tp.TeamB = Partner.Id;
            RebuildAssets();
            Shell.Toast("Here is what it takes.");
        }

        // ---------------------------------------------------------------- the inbox
        void ShowOffer()
        {
            var t = GlassTheme.T;
            for (int i = _inboxDetail.childCount - 1; i >= 0; i--) UiKit.Kill(_inboxDetail.GetChild(i).gameObject);
            bool any = _offers.Count > 0;
            _accept.interactable = any && TradeEngine.IsOpen(Save); _reject.interactable = any;
            if (!any) return;
            var o = _offers[Mathf.Clamp(_inbox.Cursor, 0, _offers.Count - 1)];
            var sp = StackPanel.Create(_inboxDetail, "S", 0f, 0f, 544f, 400f);
            sp.Heading(CareerText.TeamName(Save, o.FromTeamId), t.textPrimary, 30f);
            sp.Para(o.Message, 21f, t.textSecondary, 90f);
            sp.Gap(6f); sp.Caps("THEY SEND");
            foreach (var a in o.Proposal.FromA) if (sp.Fits(30f)) sp.Line(AssetName(a), Mathf.RoundToInt(TradeEngine.AssetValue(Save, a)).ToString(), null, 28f, 20f);
            sp.Gap(4f); sp.Caps("THEY WANT");
            foreach (var a in o.Proposal.FromB) if (sp.Fits(30f)) sp.Line(AssetName(a), Mathf.RoundToInt(TradeEngine.AssetValue(Save, a)).ToString(), null, 28f, 20f);
        }

        void AcceptOffer(bool accept)
        {
            if (_offers.Count == 0) return;
            var o = _offers[Mathf.Clamp(_inbox.Cursor, 0, _offers.Count - 1)];
            var r = accept ? TradeEngine.AcceptOffer(Save, o.Id) : TradeEngine.RejectOffer(Save, o.Id);
            Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info"); Ctl.Touch();
        }
    }
}
