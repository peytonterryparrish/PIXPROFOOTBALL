using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public enum StatCategory
    {
        PassYds, PassTd, PassInt, PasserRating, RushYds, RushTd, RushAvg, Rec, RecYds, RecTd, Sacks, Interceptions, Tackles,
        FgMade, Points, Punts
    }

    [Serializable]
    public class LeaderEntry
    {
        public int Rank;
        public int PlayerId;
        public string Name;
        public int TeamId;
        public Position Pos;
        public float Value;
        public int Games;
    }

    [Serializable]
    public class HelmEntry
    {
        public int Rank;
        public int PlayerId;
        public string Name;
        public Position Pos;
        public int TeamId;
        public string TeamName;
        public float Score;
        public string Summary;
    }

    /// <summary>Weekly snapshot of the Golden Helm Trophy race.</summary>
    [Serializable]
    public class HelmSnapshot
    {
        public int Week;
        public List<HelmEntry> Candidates = new List<HelmEntry>();
    }

    public static class LeagueLeaders
    {
        public static float ValueOf(StatLine s, StatCategory c)
        {
            switch (c)
            {
                case StatCategory.PassYds: return s.PassYds;
                case StatCategory.PassTd: return s.PassTd;
                case StatCategory.PassInt: return s.PassInt;
                case StatCategory.PasserRating: return s.PasserRating;
                case StatCategory.RushYds: return s.RushYds;
                case StatCategory.RushTd: return s.RushTd;
                case StatCategory.RushAvg: return s.RushAtt > 0 ? (float)s.RushYds / s.RushAtt : 0f;
                case StatCategory.Rec: return s.Rec;
                case StatCategory.RecYds: return s.RecYds;
                case StatCategory.RecTd: return s.RecTd;
                case StatCategory.Sacks: return s.Sacks;
                case StatCategory.Interceptions: return s.Interceptions;
                case StatCategory.Tackles: return s.Tackles;
                case StatCategory.FgMade: return s.FgMade;
                case StatCategory.Points: return s.Points;
                case StatCategory.Punts: return s.Punts;
            }
            return 0f;
        }

        private static bool Qualifies(StatLine s, StatCategory c, int games)
        {
            switch (c)
            {
                case StatCategory.PasserRating: return s.PassAtt >= 14 * Math.Max(1, games);
                case StatCategory.RushAvg: return s.RushAtt >= 6 * Math.Max(1, games);
                default: return true;
            }
        }

        /// <summary>Top <paramref name="n"/> players for a category in the season (regular season stats).</summary>
        public static List<LeaderEntry> Top(Season s, StatCategory category, int n)
        {
            var list = new List<LeaderEntry>();
            int playedWeeks = 0;
            for (int i = 0; i < s.Games.Count; i++) if (s.Games[i].Played && s.Games[i].Kind == GameKind.Regular && s.Games[i].Week > playedWeeks) playedWeeks = s.Games[i].Week;
            foreach (Team t in s.AllTeams())
            {
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    SeasonStats st = p.FindStats(s.Year);
                    if (st == null) continue;
                    if (!Qualifies(st, category, Math.Max(1, playedWeeks - 1))) continue;
                    float v = ValueOf(st, category);
                    if (v <= 0f) continue;
                    list.Add(new LeaderEntry { PlayerId = p.Id, Name = p.Name, TeamId = t.Id, Pos = p.Position, Value = v, Games = st.Games });
                }
            }
            list.Sort((a, b) => b.Value.CompareTo(a.Value));   // QA-567: every board is most-first (the dead "ascending" flag is gone)
            if (list.Count > n) list.RemoveRange(n, list.Count - n);
            for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
            return list;
        }
    }

    public static class AwardsCalculator
    {
        public static float OffenseValue(StatLine s)
        {
            return s.PassYds * 0.05f + s.PassTd * 5f - s.PassInt * 3f + s.RushYds * 0.06f + s.RushTd * 6f
                   + s.RecYds * 0.06f + s.RecTd * 6f + s.Rec * 0.2f - s.FumblesLost * 2f - s.Sacked * 0.2f;
        }

        public static float DefenseValue(StatLine s)
        {
            return s.Sacks * 5f + s.Interceptions * 7f + s.IntTd * 6f + s.Tackles * 0.5f + s.TacklesForLoss * 1.0f
                   + s.ForcedFumbles * 3f + s.PassesDefended * 1.2f + s.FumbleRecoveries * 2f;
        }

        private static AwardWinner Best(List<AwardWinner> cands)
        {
            AwardWinner best = null;
            for (int i = 0; i < cands.Count; i++) if (best == null || cands[i].Score > best.Score) best = cands[i];
            return best;
        }

        public static void ComputePro(Season s, List<Team> teams)
        {
            s.Awards.Clear();
            AwardWinner mvp = null, mvp2 = null, opoy = null, dpoy = null, roy = null;
            double mvpScore = -1e9, mvp2Score = -1e9, opoyScore = -1e9, dpoyScore = -1e9, royScore = -1e9;
            foreach (Team t in teams)
            {
                int wins = s.WinsOf(t.Id);
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    SeasonStats st = p.FindStats(s.Year);
                    if (st == null || st.Games == 0) continue;
                    float off = OffenseValue(st), def = DefenseValue(st);
                    // QA-566: the MVP weighs team success heavily and lets a dominant defender (defence value is on a smaller scale) compete
                    float mv = Math.Max(off, def * 3.5f) + wins * 9.0f;
                    if (mv > mvpScore)
                    {
                        mvp2 = mvp; mvp2Score = mvpScore;
                        mvpScore = mv; mvp = Make("MVP", p, t, mv);
                    }
                    else if (mv > mvp2Score) { mvp2Score = mv; mvp2 = Make("MVP", p, t, mv); }
                    if (off > opoyScore) { opoyScore = off; opoy = Make("Offensive Player of the Year", p, t, off); }
                    if (def > dpoyScore) { dpoyScore = def; dpoy = Make("Defensive Player of the Year", p, t, def); }
                    // QA-565: this year's rookies were drafted in the offseason that ended LAST season (DraftYear == Year - 1); a generated first-season league has DraftYear == Year
                    if (p.Experience == 0 && p.DraftYear > 0 && p.DraftYear >= s.Year - 1)
                    {
                        float rv = Math.Max(off, def * 0.9f);
                        if (rv > royScore) { royScore = rv; roy = Make("Rookie of the Year", p, t, rv); }
                    }
                }
            }
            // the two top awards go to different players unless the runner-up is far behind (QA-566: MVP no longer equals OPOY every year)
            if (mvp != null && opoy != null && mvp2 != null && mvp.PlayerId == opoy.PlayerId && mvp2.Score >= mvp.Score * 0.9) mvp = mvp2;
            if (mvp != null) { s.Awards.Add(mvp); s.MvpId = mvp.PlayerId; }
            if (opoy != null) { s.Awards.Add(opoy); s.OpoyId = opoy.PlayerId; }
            if (dpoy != null) { s.Awards.Add(dpoy); s.DpoyId = dpoy.PlayerId; }
            if (roy != null) { s.Awards.Add(roy); s.RoyId = roy.PlayerId; }
        }

        private static AwardWinner Make(string award, Player p, Team t, float score)
        {
            return new AwardWinner { Award = award, PlayerId = p.Id, PlayerName = p.Name, TeamId = t.Id, Pos = p.Position, Score = score };
        }

        // ------------------------------------------------------------ Golden Helm Trophy (college player of the year)
        public const string GoldenHelmName = "Golden Helm Trophy";

        public static float CollegeValue(StatLine s)
        {
            float v = OffenseValue(s) + DefenseValue(s) * 0.8f;
            v += s.FgMade * 0.8f - (s.FgAtt - s.FgMade) * 0.5f;
            return v;
        }

        /// <summary>Current top candidates for the Golden Helm Trophy (stats through the games played so far plus team success).</summary>
        public static HelmSnapshot HelmCandidates(Season s, int week, int count = 10)
        {
            var snap = new HelmSnapshot { Week = week };
            PollSnapshot poll = s.Polls.Count > 0 ? s.Polls[s.Polls.Count - 1] : null;
            var all = new List<HelmEntry>();
            foreach (Team t in s.AllTeams())
            {
                int wins = s.WinsOf(t.Id), losses = s.LossesOf(t.Id);
                float team = wins * 2.2f - losses * 2.6f;
                PollEntry pe = poll != null ? poll.Find(t.Id) : null;
                if (pe != null) team += (26 - pe.Rank) * 0.35f;
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    SeasonStats st = p.FindStats(s.Year);
                    if (st == null || st.Games == 0) continue;
                    float v = CollegeValue(st);
                    if (v < 40f) continue;
                    float score = v + team;
                    all.Add(new HelmEntry
                    {
                        PlayerId = p.Id, Name = p.Name, Pos = p.Position, TeamId = t.Id, TeamName = s.NameOf(t.Id), Score = score,
                        Summary = Summarize(p.Position, st)
                    });
                }
            }
            all.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < all.Count && i < count; i++) { all[i].Rank = i + 1; snap.Candidates.Add(all[i]); }
            return snap;
        }

        private static string Summarize(Position pos, StatLine s)
        {
            if (s.PassAtt > 40) return s.PassCmp + "/" + s.PassAtt + ", " + s.PassYds + " yds, " + s.PassTd + " TD, " + s.PassInt + " INT; " + s.RushYds + " rush yds";
            if (s.RushAtt > 30) return s.RushAtt + " car, " + s.RushYds + " yds, " + s.RushTd + " TD";
            if (s.Rec > 8) return s.Rec + " rec, " + s.RecYds + " yds, " + s.RecTd + " TD";
            if (s.Tackles + s.Sacks > 15) return s.Tackles + " tkl, " + s.Sacks + " sacks, " + s.Interceptions + " INT";
            return s.Points + " pts";
        }
    }
}
