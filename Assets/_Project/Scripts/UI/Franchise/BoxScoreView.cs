using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    /// <summary>Pure box score text (EditMode tested): the lines of the team comparison and the top performers of a <see cref="GameResult"/>.</summary>
    public static class BoxScoreModel
    {
        public sealed class StatRow { public string Away, Label, Home; }

        public static string Clock(int seconds) { return (seconds / 60) + ":" + (seconds % 60).ToString("00"); }

        public static List<StatRow> TeamRows(GameResult r)
        {
            var a = r.Away; var h = r.Home;
            var l = new List<StatRow>();
            Action<string, string, string> add = (x, lab, y) => l.Add(new StatRow { Away = x, Label = lab, Home = y });
            add(a.TotalYards.ToString(), "TOTAL YARDS", h.TotalYards.ToString());
            add(a.PassCmp + "/" + a.PassAtt + "  " + a.PassYds, "PASSING", h.PassCmp + "/" + h.PassAtt + "  " + h.PassYds);
            add(a.RushAtt + "  " + a.RushYds, "RUSHING", h.RushAtt + "  " + h.RushYds);
            add(a.FirstDowns.ToString(), "FIRST DOWNS", h.FirstDowns.ToString());
            add(a.ThirdDownConv + "/" + a.ThirdDownAtt, "THIRD DOWNS", h.ThirdDownConv + "/" + h.ThirdDownAtt);
            add(a.Giveaways.ToString(), "TURNOVERS", h.Giveaways.ToString());
            add(a.Sacked + "-" + a.SackYdsLost, "SACKED", h.Sacked + "-" + h.SackYdsLost);
            add(a.Penalties + "-" + a.PenaltyYds, "PENALTIES", h.Penalties + "-" + h.PenaltyYds);
            if (a.TimeOfPossessionSeconds + h.TimeOfPossessionSeconds > 0) add(Clock(a.TimeOfPossessionSeconds), "TIME OF POSS.", Clock(h.TimeOfPossessionSeconds));
            return l;
        }

        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int i = name.IndexOf(' ');
            return i > 0 ? name.Substring(0, 1) + ". " + name.Substring(i + 1) : name;
        }

        /// <summary>The passer, rusher, receiver and defender of a team with the biggest game, as "N. Name   line" strings (empty list entries are skipped).</summary>
        public static List<string> Performers(GameResult r, int teamId)
        {
            PlayerGameLine pass = null, rush = null, rec = null, def = null;
            foreach (var l in r.PlayerLines)
            {
                if (l.TeamId != teamId) continue;
                var s = l.Stats;
                if (s.PassAtt > 0 && (pass == null || s.PassYds > pass.Stats.PassYds)) pass = l;
                if (s.RushAtt > 0 && (rush == null || s.RushYds > rush.Stats.RushYds)) rush = l;
                if (s.Rec > 0 && (rec == null || s.RecYds > rec.Stats.RecYds)) rec = l;
                float dv = s.Tackles + s.Sacks * 4f + s.Interceptions * 5f + s.ForcedFumbles * 3f;
                if (dv > 0 && (def == null || dv > def.Stats.Tackles + def.Stats.Sacks * 4f + def.Stats.Interceptions * 5f + def.Stats.ForcedFumbles * 3f)) def = l;
            }
            var o = new List<string>();
            if (pass != null) o.Add("PASS   " + Short(pass.Name) + "   " + pass.Stats.PassCmp + "/" + pass.Stats.PassAtt + ", " + pass.Stats.PassYds + " yds, " + pass.Stats.PassTd + " TD" + (pass.Stats.PassInt > 0 ? ", " + pass.Stats.PassInt + " INT" : ""));
            if (rush != null) o.Add("RUSH   " + Short(rush.Name) + "   " + rush.Stats.RushAtt + " car, " + rush.Stats.RushYds + " yds" + (rush.Stats.RushTd > 0 ? ", " + rush.Stats.RushTd + " TD" : ""));
            if (rec != null) o.Add("REC   " + Short(rec.Name) + "   " + rec.Stats.Rec + " rec, " + rec.Stats.RecYds + " yds" + (rec.Stats.RecTd > 0 ? ", " + rec.Stats.RecTd + " TD" : ""));
            if (def != null) o.Add("DEF   " + Short(def.Name) + "   " + def.Stats.Tackles + " tkl" + (def.Stats.Sacks > 0 ? ", " + def.Stats.Sacks + " sack" : "") + (def.Stats.Interceptions > 0 ? ", " + def.Stats.Interceptions + " int" : ""));
            return o;
        }

        /// <summary>Points by quarter of one side, with the overtime column and the total ("7  10  0  14  -  31").</summary>
        public static string[] LineScore(TeamBoxScore b)
        {
            var l = new List<string>();
            for (int i = 0; i < 4; i++) l.Add(i < b.ByQuarter.Count ? b.ByQuarter[i].ToString() : "0");
            l.Add(b.OvertimeScore > 0 ? b.OvertimeScore.ToString() : "-");
            l.Add(b.Score.ToString());
            return l.ToArray();
        }
    }

    /// <summary>The box score modal: a final-score header, the line score, the team comparison and the top performers. B or CONTINUE closes it.</summary>
    public sealed class BoxScoreView : MonoBehaviour, ICancelHandler
    {
        Action _closed; bool _done; GameObject _returnTo;

        public static BoxScoreView Show(FranchiseUi shell, CareerSave save, GameResult r, string kicker, string closeLabel, Action onClosed)
        {
            var t = GlassTheme.T;
            var root = UiKit.Rect(shell.Root, "BoxScore"); UiKit.Stretch(root);
            var v = root.gameObject.AddComponent<BoxScoreView>(); v._closed = onClosed;
            v._returnTo = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.8f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;

            var host = UiKit.Rect(root, "Host"); host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(t.safeMarginX, 120f); host.offsetMax = new Vector2(-t.safeMarginX, -80f);
            var size = new Vector2(1600f, 880f);
            var block = UiKit.Rect(host, "Block"); block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f); block.pivot = new Vector2(0.5f, 0.5f); block.anchoredPosition = Vector2.zero; block.sizeDelta = size;
            var fs = host.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = size;

            var box = GlassKit.Panel(block, "Box", 1.7f, true, t.cornerRadiusLarge); Lay.Place(box.rectTransform, 0f, 0f, size.x, size.y); box.SetFocus(0f);
            var br = box.rectTransform;
            LeagueTeam home = save.Pro.GetTeam(r.HomeTeamId), away = save.Pro.GetTeam(r.AwayTeamId);
            Lay.Text(br, kicker.ToUpperInvariant(), 20f, t.accent, 40f, 24f, 700f, 30f, true, TextAlignmentOptions.MidlineLeft, 4f, "Kicker");

            // score header
            float cy = 66f;
            TeamUi.CrestImage(br, away, 120f, 120f, cy, "CrestAway"); TeamUi.CrestImage(br, home, 120f, size.x - 240f, cy, "CrestHome");
            Lay.Text(br, away.City.ToUpperInvariant() + " " + away.Nickname.ToUpperInvariant(), 26f, t.textSecondary, 260f, cy + 6f, 480f, 34f, true, TextAlignmentOptions.MidlineLeft, 2f, "AwayName");
            Lay.Text(br, home.City.ToUpperInvariant() + " " + home.Nickname.ToUpperInvariant(), 26f, t.textSecondary, size.x - 740f, cy + 6f, 480f, 34f, true, TextAlignmentOptions.MidlineRight, 2f, "HomeName");
            bool awayWon = r.AwayScore > r.HomeScore, homeWon = r.HomeScore > r.AwayScore;
            Lay.Text(br, r.AwayScore.ToString(), 84f, awayWon ? t.textPrimary : t.textSecondary, 260f, cy + 36f, 300f, 100f, true, TextAlignmentOptions.MidlineLeft, 0f, "AwayScore");
            Lay.Text(br, r.HomeScore.ToString(), 84f, homeWon ? t.textPrimary : t.textSecondary, size.x - 560f, cy + 36f, 300f, 100f, true, TextAlignmentOptions.MidlineRight, 0f, "HomeScore");
            Lay.Text(br, r.Overtime ? "FINAL / OT" : "FINAL", 34f, t.accent, size.x * 0.5f - 160f, cy + 50f, 320f, 56f, true, TextAlignmentOptions.Center, 6f, "Final");

            // line score
            float ly = 218f; float lx = 560f, colW = 92f;
            string[] heads = { "1", "2", "3", "4", "OT", "T" };
            for (int i = 0; i < heads.Length; i++) Lay.Text(br, heads[i], 20f, t.textSecondary, lx + 190f + i * colW, ly, colW, 30f, true, TextAlignmentOptions.Center, 2f, "LH" + i);
            for (int side = 0; side < 2; side++)
            {
                var team = side == 0 ? away : home; var vals = BoxScoreModel.LineScore(side == 0 ? r.Away : r.Home);
                float y = ly + 34f + side * 40f;
                Lay.Text(br, TeamUi.Abbr(team), 26f, t.textPrimary, lx, y, 170f, 36f, true, TextAlignmentOptions.MidlineLeft, 2f, "LT" + side);
                for (int i = 0; i < vals.Length; i++) Lay.Text(br, vals[i], 26f, i == 5 ? t.textPrimary : t.textSecondary, lx + 190f + i * colW, y, colW, 36f, i == 5, TextAlignmentOptions.Center, 0f, "LV" + side + i);
            }
            var rule = GlassKit.Rule(br); Lay.Place(rule.rectTransform, 40f, 338f, size.x - 80f, 1.4f);

            // left: team stats
            float sy = 356f; float cw = 740f;
            Lay.Caps(br, "TEAM COMPARISON", 40f, sy, cw, 28f, t.textSecondary);
            var rows = BoxScoreModel.TeamRows(r);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = GlassWidgets.CompareRow(br, rows[i].Away, rows[i].Label, rows[i].Home, cw, 40f, (i & 1) == 0);
                Lay.Unlayout(row.gameObject); Lay.Place(row, 40f, sy + 36f + i * 40f, cw, 40f);
            }
            // right: top performers
            float px = 860f;
            for (int side = 0; side < 2; side++)
            {
                var team = side == 0 ? away : home;
                var perf = BoxScoreModel.Performers(r, team.Id);
                float y = sy + side * 218f;
                Lay.Caps(br, TeamUi.Abbr(team) + " LEADERS", px, y, 700f, 28f, t.textSecondary);
                if (perf.Count == 0) Lay.Text(br, "No individual lines were recorded.", 21f, t.textMuted, px, y + 34f, 700f, 32f, false, TextAlignmentOptions.MidlineLeft, 0f, "None");
                for (int i = 0; i < perf.Count && i < 4; i++) Lay.Text(br, perf[i], 21f, t.textPrimary, px, y + 34f + i * 40f, 700f, 36f, false, TextAlignmentOptions.MidlineLeft, 0f, "Perf" + side + i);
            }

            var btn = GlassKit.Button(br, closeLabel, () => v.Close(), GlassButtonStyle.Primary, 420f, 70f, null, "Close");
            Lay.Place((RectTransform)btn.transform, size.x * 0.5f - 210f, size.y - 90f, 420f, 70f);
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            root.transform.SetAsLastSibling();
            ActionSheet.AnyOpen = true;
            v.StartCoroutine(v.SelectLater(btn.gameObject));
            return v;
        }

        IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) { UiAudio.Mute(0.2f); EventSystem.current.SetSelectedGameObject(g); }
        }

        public void OnCancel(BaseEventData e) { Close(); }

        public void Close()
        {
            if (_done) return; _done = true;
            ActionSheet.AnyOpen = false;
            var back = _returnTo; var cl = _closed;
            Destroy(gameObject);
            if (back != null && EventSystem.current != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
            if (cl != null) cl();
        }

        void OnDestroy() { if (!_done) ActionSheet.AnyOpen = false; }
    }
}
