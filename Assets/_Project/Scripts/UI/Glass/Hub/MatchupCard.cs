using System;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The pre-game matchup card (the step between team select and kickoff; layout after the console game-day cards, art ours): the two big crests with "AT" between them (visitor left, home right),
    /// city and nickname, records, OVR / OFF / DEF tiles per team (the better value of each pair lights up), the kickoff time and venue in the middle and a strip of the match options on top.
    /// KICK OFF is the primary button; BACK returns to team select. Built inside the team-select panel.
    /// </summary>
    public sealed class MatchupCard
    {
        sealed class Side
        {
            public Image Crest; public GlassGraphic Wash;
            public TextMeshProUGUI Role, You, City, Nick, Record;
            public GlassGraphic YouChip;
            public TextMeshProUGUI[] Value = new TextMeshProUGUI[3];
        }

        public RectTransform Root;
        public Button Kick, Back;
        RectTransform _chips;
        Side _away, _home;
        TextMeshProUGUI _at, _time, _venue, _weather;

        const float ColW = 440f, MidW = 300f, CardH = 496f;
        public static readonly Vector2 DesignSize = new Vector2(1212f, 676f);

        public bool Visible { get { return Root != null && Root.gameObject.activeSelf; } }

        public static MatchupCard Build(RectTransform panelRoot, Action onKick, Action onBack)
        {
            var t = GlassTheme.T;
            var m = new MatchupCard();
            var host = UiKit.Rect(panelRoot, "MatchupHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(t.safeMarginX, 196f); host.offsetMax = new Vector2(-t.safeMarginX, -152f);
            var block = UiKit.Rect(host, "Block");
            block.anchorMin = block.anchorMax = new Vector2(0f, 0.5f); block.pivot = new Vector2(0f, 0.5f); block.anchoredPosition = Vector2.zero; block.sizeDelta = DesignSize;
            var fs = host.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = DesignSize;
            m.Root = host;

            // options strip
            m._chips = UiKit.Rect(block, "OptionChips");
            UiKit.Anchor(m._chips, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(DesignSize.x, 44f));
            Reveal(m._chips, 0f);

            // card
            var card = GlassKit.Panel(block, "Card", 1.2f, true, t.cornerRadiusLarge);
            UiKit.Anchor(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -64f), new Vector2(DesignSize.x, CardH));
            Reveal(card.rectTransform, 0.05f);
            var cr = card.rectTransform;
            m._away = BuildSide(cr, "Away", 16f, "AWAY");
            m._home = BuildSide(cr, "Home", DesignSize.x - 16f - ColW, "HOME");

            // centre: AT, kickoff, venue, weather
            float cx = DesignSize.x * 0.5f;
            m._at = GlassKit.Text(cr, "AT", GlassText.Title, TextAlignmentOptions.Center, "At"); GlassKit.Style(m._at, 64f, t.accent, true, 6f);
            UiKit.Anchor(m._at.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -44f), new Vector2(MidW - 24f, 80f));
            CenterCaption(cr, "KICKOFF", cx, -140f);
            m._time = GlassKit.Text(cr, "", GlassText.Heading, TextAlignmentOptions.Center, "Time"); GlassKit.Style(m._time, 44f, t.textPrimary, true, 1f);
            UiKit.Anchor(m._time.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -166f), new Vector2(MidW - 24f, 56f)); UiKit.Fit(m._time, 30f);
            CenterCaption(cr, "VENUE", cx, -244f);
            m._venue = GlassKit.Text(cr, "", GlassText.Body, TextAlignmentOptions.Top, "Venue"); GlassKit.Style(m._venue, 24f, t.textPrimary, true, 1f);
            m._venue.textWrappingMode = TextWrappingModes.Normal; m._venue.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.Anchor(m._venue.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -272f), new Vector2(MidW - 20f, 78f));
            CenterCaption(cr, "CONDITIONS", cx, -362f);
            m._weather = GlassKit.Text(cr, "", GlassText.Body, TextAlignmentOptions.Top, "Weather"); GlassKit.Style(m._weather, 24f, t.accent, true, 1f);
            UiKit.Anchor(m._weather.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -390f), new Vector2(MidW - 16f, 72f));
            m._weather.textWrappingMode = TextWrappingModes.Normal; UiKit.Fit(m._weather, 20f);
            // hairlines between the columns
            for (int k = 0; k < 2; k++)
            {
                var rl = GlassKit.Bar(cr, "Divider" + k, new Color(t.edgeA.r, t.edgeA.g, t.edgeA.b, 0.28f), 1.4f, CardH - 72f);
                UiKit.Anchor(rl.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx + (k == 0 ? -MidW * 0.5f : MidW * 0.5f), -36f), new Vector2(1.4f, CardH - 72f));
            }

            // buttons
            m.Kick = GlassKit.Button(block, "KICK OFF", onKick, GlassButtonStyle.Primary, 720f, 80f, "ui_arrow_right", "KickOff");
            UiKit.Anchor((RectTransform)m.Kick.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -584f), new Vector2(720f, 80f));
            m.Back = GlassKit.Button(block, "BACK", onBack, GlassButtonStyle.Secondary, 468f, 80f, null, "Back");
            UiKit.Anchor((RectTransform)m.Back.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(744f, -584f), new Vector2(468f, 80f));
            var kn = m.Kick.navigation; kn.mode = Navigation.Mode.Explicit; kn.selectOnRight = m.Back; kn.selectOnLeft = m.Back; m.Kick.navigation = kn;
            var bn = m.Back.navigation; bn.mode = Navigation.Mode.Explicit; bn.selectOnLeft = m.Kick; bn.selectOnRight = m.Kick; m.Back.navigation = bn;
            return m;
        }

        static void CenterCaption(RectTransform card, string text, float cx, float y)
        {
            var c = GlassKit.Eyebrow(card, text, TextAlignmentOptions.Center, "Cap_" + text); GlassKit.Style(c, 20f, GlassTheme.T.textSecondary, true, 4f);
            UiKit.Anchor(c.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, y), new Vector2(MidW - 24f, 26f));
        }

        static void Reveal(RectTransform r, float delay)
        {
            if (r == null) return;
            var cg = r.gameObject.GetComponent<CanvasGroup>(); if (cg == null) r.gameObject.AddComponent<CanvasGroup>();
            var rev = r.gameObject.AddComponent<GlassReveal>(); rev.Delay = delay; rev.FromOffset = new Vector2(0f, -16f);
        }

        static Side BuildSide(RectTransform card, string name, float x, string role)
        {
            var t = GlassTheme.T;
            var s = new Side();
            float cx = x + ColW * 0.5f;
            s.Wash = GlassKit.Solid(card, name + "Wash", new Color(1f, 1f, 1f, 0.14f), t.cornerRadius);
            UiKit.Anchor(s.Wash.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -16f), new Vector2(ColW, CardH - 32f));
            var chip = GlassKit.Surface(card, name + "RoleChip", GlassKind.Glass, 17f, false, 1.3f);
            UiKit.Anchor(chip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + 18f, -30f), new Vector2(104f, 36f));
            s.Role = GlassKit.Eyebrow(chip.rectTransform, role, TextAlignmentOptions.Center, "Role"); GlassKit.Style(s.Role, 20f, t.textPrimary, true, 3f); UiKit.Stretch(s.Role.rectTransform);
            s.YouChip = GlassKit.Surface(card, name + "YouChip", GlassKind.Glass, 17f, false, 1.3f); s.YouChip.SetFocus(0.85f); s.YouChip.GlowScale = 0.2f;
            UiKit.Anchor(s.YouChip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + 132f, -30f), new Vector2(84f, 36f));
            s.You = GlassKit.Eyebrow(s.YouChip.rectTransform, "YOU", TextAlignmentOptions.Center, "You"); GlassKit.Style(s.You, 20f, t.accent, true, 3f); UiKit.Stretch(s.You.rectTransform);
            s.Crest = UiKit.Panel(card, name + "Crest", Color.white); s.Crest.preserveAspect = true;
            UiKit.Anchor(s.Crest.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -62f), new Vector2(196f, 196f));
            s.City = GlassKit.Eyebrow(card, "", TextAlignmentOptions.Center, name + "City"); GlassKit.Style(s.City, 22f, t.textSecondary, true, 4f);
            UiKit.Anchor(s.City.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -266f), new Vector2(ColW - 24f, 30f)); UiKit.Fit(s.City, 20f);
            s.Nick = GlassKit.Text(card, "", GlassText.Title, TextAlignmentOptions.Center, name + "Nick"); GlassKit.Style(s.Nick, 50f, t.textPrimary, true, 1f);
            UiKit.Anchor(s.Nick.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -294f), new Vector2(ColW - 24f, 60f)); UiKit.Fit(s.Nick, 30f);
            s.Record = GlassKit.Text(card, "0-0", GlassText.Body, TextAlignmentOptions.Center, name + "Record"); GlassKit.Style(s.Record, 26f, t.textSecondary, true, 3f);
            UiKit.Anchor(s.Record.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -356f), new Vector2(ColW - 24f, 32f));
            string[] caps = { "OVR", "OFF", "DEF" };
            float tw = 128f, gap = 12f, x0 = cx - (3 * tw + 2 * gap) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                var well = GlassKit.Well(card, name + "Stat" + caps[i], t.cornerRadiusSmall);
                UiKit.Anchor(well.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x0 + i * (tw + gap), -398f), new Vector2(tw, 78f));
                s.Value[i] = GlassKit.Text(well.rectTransform, "", GlassText.Heading, TextAlignmentOptions.Center, "Value"); GlassKit.Style(s.Value[i], 40f, t.textPrimary, true);
                UiKit.Anchor(s.Value[i].rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(tw * 0.5f, -4f), new Vector2(tw - 8f, 48f));
                var cp = GlassKit.Eyebrow(well.rectTransform, caps[i], TextAlignmentOptions.Center, "Caption"); GlassKit.Style(cp, 20f, t.textSecondary, true, 3f);
                UiKit.Anchor(cp.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(tw * 0.5f, 4f), new Vector2(tw - 8f, 26f));
            }
            return s;
        }

        /// <summary>Fills the card from the chosen setup: visitor (AWAY) left, home right.</summary>
        public void Fill(MatchConfig cfg)
        {
            var t = GlassTheme.T;
            var homeInfo = TeamCatalog.ById(cfg.HomeId); var awayInfo = TeamCatalog.ById(cfg.AwayId);
            var homeLg = MatchData.TeamByKey(cfg.HomeId); var awayLg = MatchData.TeamByKey(cfg.AwayId);
            var hr = MenuHubModel.Ratings(homeLg); var ar = MenuHubModel.Ratings(awayLg);
            FillSide(_away, awayInfo, ar, hr, cfg.UserTeam == PixFootball.Core.Team.Away);
            FillSide(_home, homeInfo, hr, ar, cfg.UserTeam == PixFootball.Core.Team.Home);
            _time.text = MenuHubModel.KickoffTime(cfg.Time);
            _venue.text = MenuHubModel.VenueName(homeLg).ToUpperInvariant() + "\n" + (homeLg != null ? homeLg.City.ToUpperInvariant() : "");
            _weather.text = MenuHubModel.WeatherLine(cfg.Weather, cfg.Time, homeLg);

            // options strip
            for (int i = _chips.childCount - 1; i >= 0; i--) UiKit.Kill(_chips.GetChild(i).gameObject);
            string you = cfg.UserTeam == PixFootball.Core.Team.Home ? homeInfo.Abbr : awayInfo.Abbr;
            string[] opts = { cfg.QuarterMinutes + " MIN QUARTERS", cfg.CollegeRules ? "COLLEGE RULES" : "PRO RULES", "CPU " + (cfg.Difficulty == Difficulty.AllPro ? "ALL-PRO" : cfg.Difficulty.ToString().ToUpperInvariant()) };
            float cxp = 0f;
            foreach (var o in opts) cxp = AddChip(cxp, o, false);
            AddChip(cxp, "YOU: " + (string.IsNullOrEmpty(you) ? "HOME" : you), true);
        }

        /// <summary>One option chip at an explicit x (no layout group, so it is right in the edit-mode renders too); returns the x of the next chip.</summary>
        float AddChip(float x, string text, bool accent)
        {
            var t = GlassTheme.T;
            float w = text.Length * 16.5f + 40f, h = 44f;
            var g = GlassKit.Surface(_chips, "Chip_" + text, GlassKind.Glass, h * 0.5f, false, 1.1f);
            if (accent) { g.SetFocus(0.85f); g.GlowScale = 0.22f; }
            UiKit.Anchor(g.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(w, h));
            var tx = GlassKit.Eyebrow(g.rectTransform, text, TextAlignmentOptions.Center, "Text"); GlassKit.Style(tx, 20f, accent ? t.accent : t.textPrimary, true, 2f);
            UiKit.Stretch(tx.rectTransform, 6f, 0f, 6f, 0f);
            return x + w + 12f;
        }

        static void FillSide(Side s, TeamInfo info, HubRatings me, HubRatings other, bool user)
        {
            var t = GlassTheme.T;
            if (info == null) return;
            var sp = UiKit.Sprite("crest_" + info.Id); s.Crest.sprite = sp; s.Crest.enabled = sp != null;
            s.City.text = info.City.ToUpperInvariant(); s.Nick.text = info.Nickname.ToUpperInvariant();
            s.Record.text = "RECORD  0-0";
            Color p = info.PrimaryUi; p.a = 0.22f; s.Wash.color = p;
            s.YouChip.gameObject.SetActive(user);
            int[] mine = { me.Ovr, me.Off, me.Def }, theirs = { other.Ovr, other.Off, other.Def };
            for (int i = 0; i < 3; i++) { s.Value[i].text = mine[i].ToString(); s.Value[i].color = mine[i] > theirs[i] ? t.accent : t.textPrimary; }
        }
    }
}
