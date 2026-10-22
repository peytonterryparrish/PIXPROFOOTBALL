using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using LeagueTeam = PixFootball.League.Team;
using LeaguePos = PixFootball.Core.Position;

namespace PixFootball.UI.Glass
{
    /// <summary>Team ratings shown on the matchup card: the overall plus two clearly derived unit ratings (average of the depth-chart starters).</summary>
    public struct HubRatings
    {
        public int Ovr, Off, Def;
        public override string ToString() { return Ovr + "/" + Off + "/" + Def; }
    }

    /// <summary>
    /// Pure logic of the tile-hub main menu and the pre-game matchup card (EditMode tested): the news ticker headlines built from the 32-team league data, OFF / DEF unit ratings,
    /// venue and kickoff text. Everything is deterministic (the league is built from a fixed seed) and uses only the fictional league data: no real names.
    /// </summary>
    public static class MenuHubModel
    {
        static readonly LeaguePos[] OffPositions = { LeaguePos.QB, LeaguePos.RB, LeaguePos.FB, LeaguePos.WR, LeaguePos.TE, LeaguePos.LT, LeaguePos.LG, LeaguePos.C, LeaguePos.RG, LeaguePos.RT };
        static readonly LeaguePos[] DefPositions = { LeaguePos.LE, LeaguePos.DT, LeaguePos.RE, LeaguePos.LOLB, LeaguePos.MLB, LeaguePos.ROLB, LeaguePos.CB, LeaguePos.FS, LeaguePos.SS };

        // ------------------------------------------------------------------ ratings
        /// <summary>Weighted average of the starters at <paramref name="positions"/> (depth chart order; the quarterback counts three times in the offense).</summary>
        public static int UnitRating(LeagueTeam t, LeaguePos[] positions)
        {
            if (t == null) return 0;
            double sum = 0, tot = 0;
            foreach (var p in positions)
            {
                List<int> ids;
                if (!t.Depth.TryGet(p, out ids)) continue;
                int n = LeagueRules.Starters(p);
                for (int s = 0; s < n && s < ids.Count; s++)
                {
                    var pl = t.GetPlayer(ids[s]);
                    if (pl == null) continue;
                    double w = p == LeaguePos.QB ? 3.0 : 1.0;
                    sum += w * pl.Ratings.Overall(p); tot += w;
                }
            }
            return tot <= 0 ? 0 : (int)Math.Round(sum / tot);
        }

        public static int OffRating(LeagueTeam t) { return UnitRating(t, OffPositions); }
        public static int DefRating(LeagueTeam t) { return UnitRating(t, DefPositions); }

        public static HubRatings Ratings(LeagueTeam t)
        {
            return t == null ? default(HubRatings) : new HubRatings { Ovr = t.TeamOverall(), Off = OffRating(t), Def = DefRating(t) };
        }

        /// <summary>Best starter at any of <paramref name="positions"/> across the league (ties go to the earlier team); <paramref name="ovr"/> is his rating at that slot.</summary>
        public static Player BestPlayer(ProLeague lg, out LeagueTeam team, out int ovr, params LeaguePos[] positions)
        {
            Player best = null; team = null; ovr = 0;
            foreach (var t in lg.Teams)
                foreach (var p in positions)
                {
                    List<int> ids;
                    if (!t.Depth.TryGet(p, out ids) || ids.Count == 0) continue;
                    var pl = t.GetPlayer(ids[0]);
                    if (pl == null) continue;
                    int o = pl.Ratings.Overall(p);
                    if (o > ovr) { ovr = o; best = pl; team = t; }
                }
            return best;
        }

        // ------------------------------------------------------------------ headlines
        /// <summary>8 to <paramref name="max"/> ticker headlines from the league (power rankings, best players by position, game of the week, tightest match, best units, mismatch, toughest division).</summary>
        public static List<string> Headlines(ProLeague lg, int max = 12)
        {
            var list = new List<string>();
            if (lg == null || lg.Teams.Count < 4) { list.Add("PIX Football season opens tonight"); return list; }
            int n = lg.Teams.Count;
            var ovr = new int[n]; var off = new int[n]; var def = new int[n];
            for (int i = 0; i < n; i++) { ovr[i] = lg.Teams[i].TeamOverall(); off[i] = OffRating(lg.Teams[i]); def[i] = DefRating(lg.Teams[i]); }
            Func<int[], bool, int> argBest = (key, wantMax) =>
            {
                int bi = 0;
                for (int i = 1; i < n; i++) if (wantMax ? key[i] > key[bi] : key[i] < key[bi]) bi = i;
                return bi;
            };
            int top = argBest(ovr, true), low = argBest(ovr, false);
            list.Add(lg.Teams[top].FullName + " top the power rankings at " + ovr[top] + " OVR");

            LeagueTeam bt; int bo;
            var qb = BestPlayer(lg, out bt, out bo, LeaguePos.QB);
            if (qb != null) list.Add(qb.Name + " (" + bt.Nickname + ") is the top-rated quarterback in the league at " + bo + " OVR");
            var wr = BestPlayer(lg, out bt, out bo, LeaguePos.WR);
            if (wr != null) list.Add(wr.Name + " (" + bt.Nickname + ") leads all receivers with a " + bo + " OVR");
            var dl = BestPlayer(lg, out bt, out bo, LeaguePos.LE, LeaguePos.DT, LeaguePos.RE);
            if (dl != null) list.Add(dl.Name + " (" + bt.Nickname + ") is the best pass rusher on the board at " + bo + " OVR");
            var cb = BestPlayer(lg, out bt, out bo, LeaguePos.CB);
            if (cb != null) list.Add(cb.Name + " (" + bt.Nickname + ") is the league's shutdown corner, " + bo + " OVR");

            // game of the week: the closest pair among the twelve best teams (higher combined rating wins ties)
            var order = new List<int>(); for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => ovr[a] != ovr[b] ? ovr[b].CompareTo(ovr[a]) : a.CompareTo(b));
            int topN = Math.Min(12, n);
            int ga = -1, gb = -1, gd = int.MaxValue, gs = -1;
            for (int x = 0; x < topN; x++)
                for (int y = x + 1; y < topN; y++)
                {
                    int a = order[x], b = order[y], d = Math.Abs(ovr[a] - ovr[b]), s = ovr[a] + ovr[b];
                    if (d < gd || (d == gd && s > gs)) { gd = d; gs = s; ga = a; gb = b; }
                }
            if (ga >= 0)
            {
                int home = Math.Min(ga, gb), away = Math.Max(ga, gb);
                list.Add("Game of the week: " + lg.Teams[away].FullName + " at " + lg.Teams[home].FullName + ", " + ovr[away] + " OVR against " + ovr[home]);
            }
            // tightest match elsewhere in the league
            int ta = -1, tb = -1, td = int.MaxValue, ts = -1;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    if (a == ga || a == gb || b == ga || b == gb) continue;
                    int d = Math.Abs(ovr[a] - ovr[b]), s = ovr[a] + ovr[b];
                    if (d < td || (d == td && s > ts)) { td = d; ts = s; ta = a; tb = b; }
                }
            if (ta >= 0) list.Add("Toss-up: " + lg.Teams[ta].FullName + " and " + lg.Teams[tb].FullName + " are " + (td == 0 ? "dead even" : "only " + td + " OVR apart"));

            int bOff = argBest(off, true), bDef = argBest(def, true);
            list.Add(lg.Teams[bOff].Nickname + " own the league's best offense, rated " + off[bOff] + " OFF");
            list.Add(lg.Teams[bDef].Nickname + " build the best defense in the league at " + def[bDef] + " DEF");

            if (top != low) list.Add("Mismatch alert: " + lg.Teams[top].Nickname + " (" + ovr[top] + ") could meet " + lg.Teams[low].Nickname + " (" + ovr[low] + ") on any given Sunday");
            list.Add(lg.Teams[low].FullName + " sit last in the power rankings at " + ovr[low] + " OVR");

            // toughest division by average overall
            string divName = null; double divBest = -1;
            foreach (var c in lg.Conferences)
                foreach (var d in c.Divisions)
                {
                    if (d.TeamIds.Count == 0) continue;
                    double s = 0; int k = 0;
                    foreach (int id in d.TeamIds) { var t = lg.GetTeam(id); if (t != null) { s += t.TeamOverall(); k++; } }
                    if (k > 0 && s / k > divBest) { divBest = s / k; divName = (string.IsNullOrEmpty(c.Abbreviation) ? c.Name : c.Abbreviation) + " " + d.Name; }
                }
            if (divName != null) list.Add("The " + divName + " is the toughest division, averaging " + (int)Math.Round(divBest) + " OVR");
            var rb = BestPlayer(lg, out bt, out bo, LeaguePos.RB);
            if (rb != null) list.Add(rb.Name + " (" + bt.Nickname + ") is the top running back, " + bo + " OVR");

            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        // ------------------------------------------------------------------ matchup card text
        /// <summary>The record shown under a team name (a new exhibition starts 0-0).</summary>
        public static string Record(LeagueTeam t) { return "0-0"; }

        public static string VenueName(LeagueTeam home)
        {
            return home == null || string.IsNullOrEmpty(home.StadiumName) ? "PIX Pro Stadium" : home.StadiumName;
        }

        /// <summary>"8:15 PM" for a night game, 1:05 PM by day, 5:40 PM at dusk; Random shows "TBD".</summary>
        public static string KickoffTime(TimeChoice t)
        {
            switch (t) { case TimeChoice.Day: return "1:05 PM"; case TimeChoice.Dusk: return "5:40 PM"; case TimeChoice.Night: return "8:15 PM"; default: return "TBD"; }
        }

        /// <summary>One-line venue text: kickoff time, stadium, city.</summary>
        public static string VenueLine(LeagueTeam home, TimeChoice time)
        {
            string city = home != null && !string.IsNullOrEmpty(home.City) ? home.City : "";
            return KickoffTime(time) + "  -  " + VenueName(home) + (city.Length > 0 ? ", " + city : "");
        }

        public static string WeatherLine(WeatherChoice w, TimeChoice t, LeagueTeam home)
        {
            if (home != null && home.IsDome && w != WeatherChoice.Random) return "DOME  -  CLIMATE CONTROLLED";
            return (WeatherPresets.WeatherChoiceNames[(int)w] + "  -  " + WeatherPresets.TimeChoiceNames[(int)t]).ToUpperInvariant();
        }
    }
}
