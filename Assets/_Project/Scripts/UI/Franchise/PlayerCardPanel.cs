using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    /// <summary>Position filters of the roster-style lists (pure, tested): ALL then one entry per group.</summary>
    public static class PosFilter
    {
        public static readonly string[] Names = { "ALL", "QB", "RB", "WR", "TE", "OL", "DL", "LB", "DB", "K / P" };

        public static bool Match(int filter, Position p)
        {
            if (filter <= 0) return true;
            var b = PositionBuckets.Of(p);
            switch (filter)
            {
                case 1: return b == PositionBucket.QB;
                case 2: return b == PositionBucket.RB || b == PositionBucket.FB;
                case 3: return b == PositionBucket.WR;
                case 4: return b == PositionBucket.TE;
                case 5: return b == PositionBucket.OL;
                case 6: return b == PositionBucket.DL;
                case 7: return b == PositionBucket.LB;
                case 8: return b == PositionBucket.CB || b == PositionBucket.S;
                case 9: return b == PositionBucket.K || b == PositionBucket.P;
            }
            return true;
        }
    }

    /// <summary>Text of a player card (pure, tested): rating labels, short ability lines, contract lines, a career line.</summary>
    public static class CardText
    {
        public static string RatingLabel(RatingKind k)
        {
            switch (k)
            {
                case RatingKind.Speed: return "SPD"; case RatingKind.Acceleration: return "ACC"; case RatingKind.Agility: return "AGI"; case RatingKind.Strength: return "STR";
                case RatingKind.Awareness: return "AWR"; case RatingKind.Catching: return "CTH"; case RatingKind.Carrying: return "CAR"; case RatingKind.ThrowPower: return "THP";
                case RatingKind.ThrowAccuracy: return "THA"; case RatingKind.Tackling: return "TAK"; case RatingKind.BlockShedding: return "BSH"; case RatingKind.RunBlocking: return "RBK";
                case RatingKind.PassBlocking: return "PBK"; case RatingKind.ManCoverage: return "MCV"; case RatingKind.ZoneCoverage: return "ZCV"; case RatingKind.Jumping: return "JMP";
                case RatingKind.Stamina: return "STA"; case RatingKind.Injury: return "INJ"; case RatingKind.KickPower: return "KPW"; case RatingKind.KickAccuracy: return "KAC";
                case RatingKind.BreakTackle: return "BTK"; default: return "ELU";
            }
        }

        public static string Archetype(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder();
            // "QbPocket" -> "Pocket", "WrDeepThreat" -> "Deep Threat" (the two letter position prefix goes, camel case gets spaces)
            int start = raw.Length > 2 && char.IsUpper(raw[0]) && char.IsLower(raw[1]) && char.IsUpper(raw[2]) ? 2 : 0;
            for (int i = start; i < raw.Length; i++) { if (i > start && char.IsUpper(raw[i])) sb.Append(' '); sb.Append(raw[i]); }
            return sb.ToString();
        }

        public static string AbilityLine(AbilityView a) { return a.Stars + " " + a.Name; }

        public static string Contract(PlayerCard c)
        {
            if (c.YearsLeft <= 0) return "No contract";
            return Fmt.Years(c.YearsLeft) + " left  -  " + Fmt.MoneyShort(c.Aav) + " per year";
        }
    }

    /// <summary>
    /// The player card of the franchise screens: a photoreal portrait (a live render of the player's appearance in his team colours, one small RenderTexture while the card is on screen; the crest when the
    /// portrait stage is not available), name, ratings, abilities with stars, contract, morale and the last season's line. Fed by <see cref="CareerViews.Card"/>.
    /// </summary>
    public sealed class PlayerCardPanel
    {
        public RectTransform Root;
        public float Width, Height;
        RectTransform _host;
        RawImage _portrait; Image _crestFallback;
        GlassPortraitStage _stage;
        StackPanel _stack;
        int _shownId = -1;
        public bool UsePortrait = true;
        static readonly List<GlassPortraitStage> Stages = new List<GlassPortraitStage>();
        /// <summary>Edit-mode tools: cameras do not run by themselves, render every open portrait now.</summary>
        public static void RenderAll() { foreach (var s in Stages) if (s != null) s.RenderNow(); }

        public static PlayerCardPanel Create(RectTransform parent, float x, float y, float w, float h, bool portrait = true)
        {
            var t = GlassTheme.T;
            var p = new PlayerCardPanel { Width = w, Height = h, UsePortrait = portrait };
            var card = GlassKit.Panel(parent, "PlayerCard", 1.15f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, x, y, w, h);
            p.Root = card.rectTransform;
            p._host = Lay.Group(p.Root, "Dynamic", 0f, 0f, w, h);
            return p;
        }

        public void Hide() { Root.gameObject.SetActive(false); DisposeStage(); }
        public void ShowPanel() { Root.gameObject.SetActive(true); }

        public void DisposeStage()
        {
            if (_stage != null) { Stages.Remove(_stage); try { _stage.Dispose(); } catch (Exception) { } _stage = null; }
            _shownId = -1;
        }

        public void RenderPortraitNow() { if (_stage != null) _stage.RenderNow(); }

        /// <summary>Shows the card of a player (any player of the save: roster, free agent, waiver, other team).</summary>
        public void Show(CareerSave save, int playerId)
        {
            var c = CareerViews.Card(save, playerId);
            Fill(save, c);
        }

        public void Fill(CareerSave save, PlayerCard c)
        {
            for (int i = _host.childCount - 1; i >= 0; i--) UiKit.Kill(_host.GetChild(i).gameObject);
            _portrait = null; _crestFallback = null; _stack = null;
            var t = GlassTheme.T;
            if (c == null) { Lay.Text(_host, "Select a player.", 24f, t.textMuted, 28f, 28f, Width - 56f, 40f, false); return; }
            LeagueTeam team = save.TeamOf(c.Id) >= 0 ? save.Pro.GetTeam(save.TeamOf(c.Id)) : null;
            float pw = 188f, ph = 224f, pad = 22f;

            // ---- portrait
            var well = GlassKit.Well(_host, "PortraitWell", t.cornerRadius); Lay.Place(well.rectTransform, pad, pad, pw, ph);
            if (team != null) { var wash = GlassKit.Solid(well.rectTransform, "Wash", TeamUi.Tint(team, 0.20f), t.cornerRadius); UiKit.Stretch(wash.rectTransform, 2f, 2f, 2f, 2f); }
            bool live = UsePortrait && TryPortrait(save, c, team, well.rectTransform, pw, ph);
            if (!live)
            {
                _crestFallback = TeamUi.CrestImage(well.rectTransform, team, 120f, (pw - 120f) * 0.5f, 60f, "CrestFallback");
                Lay.Text(well.rectTransform, c.Jersey > 0 ? "#" + c.Jersey : "", 44f, t.textSecondary, 0f, 190f, pw, 54f, true, TextAlignmentOptions.Center, 0f, "Jersey");
            }
            else if (c.Jersey > 0) Lay.Text(well.rectTransform, "#" + c.Jersey, 26f, t.textPrimary, 10f, ph - 40f, 90f, 32f, true, TextAlignmentOptions.MidlineLeft, 0f, "JerseyTag");

            // ---- identity
            float x = pad + pw + 22f, w = Width - x - pad;
            int sp = c.Name.IndexOf(' ');
            string first = sp > 0 ? c.Name.Substring(0, sp) : "", last = sp > 0 ? c.Name.Substring(sp + 1) : c.Name;
            Lay.Caps(_host, first, x, pad - 2f, w, 26f, t.textSecondary, 20f);
            Lay.Text(_host, last.ToUpperInvariant(), 40f, t.textPrimary, x, pad + 22f, w, 52f, true, TextAlignmentOptions.MidlineLeft, 1f, "Last");
            Lay.Text(_host, c.Pos + "  -  " + CardText.Archetype(c.Archetype), 22f, t.textSecondary, x, pad + 76f, w, 32f, false, TextAlignmentOptions.MidlineLeft, 0f, "Pos");
            var ovr = GlassKit.Surface(_host, "OvrBox", GlassKind.Glass, t.cornerRadiusSmall, false, 1f); ovr.SetFocus(0.9f); ovr.GlowScale = 0.2f; Lay.Place(ovr.rectTransform, x, pad + 108f, 124f, 78f);
            Lay.Text(ovr.rectTransform, c.Overall.ToString(), 44f, t.accent, 0f, 0f, 124f, 54f, true, TextAlignmentOptions.Center, 0f, "Ovr");
            Lay.Caps(ovr.rectTransform, "OVERALL", 0f, 52f, 124f, 24f, null, 20f, TextAlignmentOptions.Center);
            var pot = GlassKit.Well(_host, "PotBox", t.cornerRadiusSmall); Lay.Place(pot.rectTransform, x + 136f, pad + 108f, 146f, 78f);
            Lay.Text(pot.rectTransform, c.Potential.ToString(), 36f, t.textPrimary, 0f, 2f, 146f, 50f, true, TextAlignmentOptions.Center, 0f, "Pot");
            Lay.Caps(pot.rectTransform, "POTENTIAL", 0f, 52f, 146f, 24f, null, 20f, TextAlignmentOptions.Center);
            Lay.Text(_host, c.Age + " yrs   " + c.Height + "   " + c.WeightLb + " lb", 20f, t.textSecondary, x, pad + 190f, w, 26f, false, TextAlignmentOptions.MidlineLeft, 0f, "Body");
            Lay.Text(_host, (c.College ?? "") + (string.IsNullOrEmpty(c.Draft) ? "" : "   -   " + c.Draft), 19f, t.textMuted, x, pad + 216f, w, 26f, false, TextAlignmentOptions.MidlineLeft, 0f, "College");

            // ---- the rest as a stack
            float y0 = pad + ph + 26f;
            _stack = StackPanel.Create(_host, "Stack", pad, y0, Width - pad * 2f, Height - y0 - 14f);
            var s = _stack;
            // status line
            if (c.Injured || c.Status != RosterStatus.Active) s.Line("Status", Fmt.Status(c.Status) + (c.Injured ? "  -  " + c.InjuryType + ", " + c.InjuryWeeks + " wk" : ""), c.Injured ? t.danger : t.textPrimary, 28f, 20f);
            // ratings: two columns of bars
            float colW = (s.Width - 20f) * 0.5f;
            bool compact = Height < 640f;
            int n = Math.Min(compact ? 6 : 8, c.Ratings.Count);
            for (int i = 0; i < n; i++)
            {
                var r = c.Ratings[i];
                float cx = (i % 2) * (colW + 20f), cy = s.Y + (i / 2) * 32f;
                Lay.Text(s.Root, CardText.RatingLabel(r.Kind), 17f, t.textSecondary, cx, cy, 52f, 30f, true, TextAlignmentOptions.MidlineLeft, 1.5f, "RL");
                var bar = GlassKit.ProgressBar(s.Root, Mathf.Clamp01(r.Value / 99f), colW - 112f, 8f, r.Value >= 85, "RB"); Lay.Unlayout(bar.gameObject); Lay.Place((RectTransform)bar.transform, cx + 56f, cy + 11f, colW - 112f, 8f);
                Lay.Text(s.Root, r.Value.ToString(), 21f, Fmt.OvrColor(r.Value), cx + colW - 50f, cy, 50f, 30f, true, TextAlignmentOptions.MidlineRight, 0f, "RV");
            }
            s.Gap(((n + 1) / 2) * 32f + 6f);
            // abilities
            s.Caps("ABILITIES");
            if (c.Abilities.Count == 0) s.Para("No special abilities yet.", 19f, t.textMuted);
            for (int i = 0; i < c.Abilities.Count && i < (compact ? 2 : 3); i++)
            {
                var a = c.Abilities[i];
                Lay.Text(s.Root, a.Stars, 22f, t.accent, 0f, s.Y, 72f, 30f, true, TextAlignmentOptions.MidlineLeft, 0f, "Stars");
                var nm = Lay.Text(s.Root, a.Name, 21f, t.textPrimary, 76f, s.Y, s.Width - 76f, 30f, true, TextAlignmentOptions.MidlineLeft, 0f, "AbName", false);
                float nw = nm.GetPreferredValues(a.Name).x + 16f;
                if (s.Width - 76f - nw > 150f) Lay.Text(s.Root, FirstEffect(a), 17f, t.textSecondary, 76f + nw, s.Y, s.Width - 76f - nw, 30f, false, TextAlignmentOptions.MidlineRight, 0f, "AbFx");
                s.Gap(30f);
            }
            if (!compact && c.Abilities.Count > 3) { s.Para("+ " + (c.Abilities.Count - 3) + " more", 18f, t.textMuted); }
            s.Gap(4f);
            // contract
            s.Caps("CONTRACT");
            s.Line("Deal", CardText.Contract(c), null, 30f, 20f);
            if (s.Fits(30f)) s.Line("Cap hit / dead if cut", Fmt.MoneyShort(c.CapHit) + " / " + Fmt.MoneyShort(c.DeadIfReleased), null, 30f, 20f);
            if (c.Morale != null && s.Fits(30f)) s.Line("Morale", c.Morale.Morale + "  -  " + c.Morale.Mood, null, 30f, 20f);
            if (c.Seasons.Count > 0 && s.Fits(30f))
            {
                var last2 = c.Seasons[c.Seasons.Count - 1];
                s.Line(last2.Year + " " + last2.Team, Fmt.Trim(last2.Line, 38), t.textSecondary, 30f, 19f);
            }
            _shownId = c.Id;
        }

        static string FirstEffect(AbilityView a)
        {
            if (string.IsNullOrEmpty(a.Effects)) return "";
            int nl = a.Effects.IndexOf('\n'); string s = nl > 0 ? a.Effects.Substring(0, nl) : a.Effects;
            int dash = s.IndexOf(" - "); if (dash >= 0 && dash < 40) s = s.Substring(dash + 3);
            return Fmt.Trim(s, 34);
        }

        bool TryPortrait(CareerSave save, PlayerCard c, LeagueTeam team, RectTransform well, float w, float h)
        {
            try
            {
                if (_stage == null)
                {
                    var colors = TeamColorsOf(team);
                    _stage = GlassPortraitStage.Create(colors, 392, 504, PlayerSize.Medium, 195f);
                    _stage.TurnSpeed = 0f; Stages.Add(_stage);
                }
                if (team != null) { var cl = TeamColorsOf(team); _stage.View.Dress(cl, cl); }
                _stage.View.SetIdentity(c.Id, c.Pos);
                var go = UiKit.Rect(well, "Portrait"); UiKit.Stretch(go, 3f, 3f, 3f, 3f);
                _portrait = go.gameObject.AddComponent<RawImage>(); _portrait.texture = _stage.Target; _portrait.raycastTarget = false;
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[PIX] portrait unavailable: " + e.Message); DisposeStage(); return false; }
        }

        static TeamColors TeamColorsOf(LeagueTeam t)
        {
            if (t == null) return TeamColors.Home;
            return TeamColors.FromHex(t.PrimaryColor, t.SecondaryColor, t.TertiaryColor);
        }
    }
}
