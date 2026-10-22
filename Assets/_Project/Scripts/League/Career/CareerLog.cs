using System;
using System.Collections.Generic;
using System.Globalization;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Formatting helpers shared by the career text (news, transactions, UI labels).</summary>
    public static class CareerText
    {
        public static string Money(long dollars)
        {
            double m = dollars / 1000000.0;
            if (Math.Abs(m) >= 100) return "$" + m.ToString("F0", CultureInfo.InvariantCulture) + "M";
            if (Math.Abs(m) >= 1) return "$" + m.ToString("F1", CultureInfo.InvariantCulture) + "M";
            return "$" + (dollars / 1000.0).ToString("F0", CultureInfo.InvariantCulture) + "K";
        }

        public static string Ordinal(int n)
        {
            int r = n % 100;
            if (r >= 11 && r <= 13) return n + "th";
            switch (n % 10) { case 1: return n + "st"; case 2: return n + "nd"; case 3: return n + "rd"; }
            return n + "th";
        }

        public static string TeamName(CareerSave s, int teamId)
        {
            Team t = s.Pro != null ? s.Pro.GetTeam(teamId) : null;
            return t != null ? t.FullName : "Team " + teamId;
        }

        public static string TeamNick(CareerSave s, int teamId)
        {
            Team t = s.Pro != null ? s.Pro.GetTeam(teamId) : null;
            return t != null ? t.Nickname : "Team " + teamId;
        }

        public static string PlayerLabel(Player p) { return p.Position + " " + p.Name; }

        public static string PhaseLabel(CareerSave s)
        {
            Season season = s.Season;
            switch (s.Phase)
            {
                case CareerPhase.Preseason: return "Preseason " + s.Year;
                case CareerPhase.RegularSeason: return "Week " + (season != null ? season.Week : 0) + " of " + (season != null ? season.RegularWeeks : 18);
                case CareerPhase.Playoffs: return season != null && season.PostseasonStage >= 1 ? PlayoffRoundName(season.PostseasonStage) : "Playoffs";
                case CareerPhase.PixBowl: return "PIX Bowl " + s.Year;
                case CareerPhase.Awards: return "Season awards " + s.Year;
                case CareerPhase.Retirements: return "Retirements " + s.Year;
                case CareerPhase.ReSign: return "Re-sign window " + (s.Year + 1);
                case CareerPhase.Scouting: return "Combine and scouting " + s.Year;
                case CareerPhase.Draft: return "The Draft " + s.Year;
                case CareerPhase.RookieSigning: return "Rookie signing " + s.Year;
                case CareerPhase.FreeAgency: return "Free agency day " + s.FaDay + " of " + s.Settings.FreeAgencyDays;
                case CareerPhase.TrainingCamp: return "Training camp " + (s.Year + 1);
            }
            return s.Phase.ToString();
        }

        public static string PlayoffRoundName(int stage)
        {
            switch (stage)
            {
                case 1: return "Wild Card round";
                case 2: return "Divisional round";
                case 3: return "Conference championships";
                default: return "PIX Bowl";
            }
        }
    }

    /// <summary>The transaction log (signings, cuts, trades, ...).</summary>
    public static class Ledger
    {
        public const int MaxTransactions = 4000;

        public static TransactionRecord Tx(CareerSave s, TransactionKind kind, int teamId, Player p, long amount, string text, int otherTeam = -1)
        {
            var t = new TransactionRecord
            {
                Id = s.NextId(), Year = s.Year, Week = s.Season != null ? s.Season.Week : 0, Phase = s.Phase, Kind = kind, TeamId = teamId,
                OtherTeamId = otherTeam, PlayerId = p != null ? p.Id : 0, PlayerName = p != null ? p.Name : "", Amount = amount, Text = text ?? ""
            };
            s.Transactions.Add(t);
            if (s.Transactions.Count > MaxTransactions) s.Transactions.RemoveRange(0, s.Transactions.Count - MaxTransactions);
            return t;
        }

        public static List<TransactionRecord> ForTeam(CareerSave s, int teamId, int max = 50)
        {
            var l = new List<TransactionRecord>();
            for (int i = s.Transactions.Count - 1; i >= 0 && l.Count < max; i--)
                if (s.Transactions[i].TeamId == teamId || s.Transactions[i].OtherTeamId == teamId) l.Add(s.Transactions[i]);
            return l;
        }
    }

    /// <summary>
    /// The news / headline generator. Everything noteworthy that happens in the league is posted here; the main-menu ticker and the
    /// News tab of the franchise hub read from <see cref="CareerSave.News"/> through <see cref="Ticker"/> and <see cref="Feed"/>.
    /// </summary>
    public static class NewsWriter
    {
        public const int MaxItems = 900;

        public static NewsItem Post(CareerSave s, NewsKind kind, string headline, string body, int priority, int team1 = -1, int team2 = -1, int player1 = 0, int player2 = 0)
        {
            var n = new NewsItem
            {
                Id = s.NextId(), Year = s.Year, Week = s.Season != null ? s.Season.Week : 0, Phase = s.Phase, Kind = kind,
                Headline = headline, Body = body ?? "", Priority = Math.Max(1, Math.Min(5, priority))
            };
            if (team1 >= 0) n.TeamIds.Add(team1);
            if (team2 >= 0 && team2 != team1) n.TeamIds.Add(team2);
            if (player1 != 0) n.PlayerIds.Add(player1);
            if (player2 != 0) n.PlayerIds.Add(player2);
            n.Mine = n.TeamIds.Contains(s.UserTeamId);
            if (n.Mine) n.Priority = Math.Min(5, n.Priority + 1);
            s.News.Add(n);
            if (s.News.Count > MaxItems) Trim(s);
            return n;
        }

        private static void Trim(CareerSave s)
        {
            // drop the oldest low-priority items first
            int remove = s.News.Count - MaxItems + 100;
            for (int i = 0; i < s.News.Count && remove > 0; )
            {
                if (s.News[i].Priority <= 2 && !s.News[i].Mine) { s.News.RemoveAt(i); remove--; }
                else i++;
            }
            if (s.News.Count > MaxItems) s.News.RemoveRange(0, s.News.Count - MaxItems);
        }

        /// <summary>The headlines for the scrolling ticker: newest first, important and user-team items preferred.</summary>
        public static List<NewsItem> Ticker(CareerSave s, int count = 12)
        {
            var l = new List<NewsItem>();
            int cutoffYear = s.Year;
            for (int i = s.News.Count - 1; i >= 0 && l.Count < count * 4; i--)
            {
                NewsItem n = s.News[i];
                if (n.Priority >= 3 || n.Mine) l.Add(n);
            }
            if (l.Count < count)
                for (int i = s.News.Count - 1; i >= 0 && l.Count < count; i--) if (!l.Contains(s.News[i])) l.Add(s.News[i]);
            if (l.Count > count) l.RemoveRange(count, l.Count - count);
            return l;
        }

        /// <summary>The news tab: newest first with optional filters (kind, only the user's team).</summary>
        public static List<NewsItem> Feed(CareerSave s, int max = 50, bool onlyMine = false, bool hasKind = false, NewsKind kind = NewsKind.Signing)
        {
            var l = new List<NewsItem>();
            for (int i = s.News.Count - 1; i >= 0 && l.Count < max; i--)
            {
                NewsItem n = s.News[i];
                if (onlyMine && !n.Mine) continue;
                if (hasKind && n.Kind != kind) continue;
                l.Add(n);
            }
            return l;
        }

        // ---------------------------------------------------------------- templates (deterministic from the item id so replays read the same)
        private static string Pick(CareerSave s, params string[] variants)
        {
            return variants[Math.Abs(s.NextEntityId * 31 + variants.Length) % variants.Length];
        }

        public static void Signing(CareerSave s, Team team, Player p, long aav, int years, int minPriorityOvr = 78, bool reSign = false)
        {
            int ovr = p.Overall;
            bool notable = ovr >= minPriorityOvr || team.Id == s.UserTeamId;
            if (!notable) return;
            string money = CareerText.Money(aav);
            int pr = ovr >= 88 ? 4 : (ovr >= 80 ? 3 : 2);
            if (reSign)
                Post(s, NewsKind.ReSigning, Pick(s, team.Nickname + " keep " + p.Name + " with " + years + "-year deal", p.Name + " re-signs with the " + team.Nickname, team.Nickname + " lock up " + CareerText.PlayerLabel(p)),
                    p.Name + " (" + p.Position + ", " + ovr + " OVR) agreed to " + years + " years at " + money + " per year.", pr, team.Id, -1, p.Id);
            else
                Post(s, NewsKind.Signing, Pick(s, p.Name + " signs with " + team.Nickname + " (" + years + " yrs, " + money + "/yr)", team.Nickname + " land " + CareerText.PlayerLabel(p), CareerText.PlayerLabel(p) + " agrees to join the " + team.Nickname),
                    p.Name + " (" + p.Position + ", " + ovr + " OVR, age " + p.Age + ") signs " + years + " years at " + money + " per year.", pr, team.Id, -1, p.Id);
        }

        public static void Injury(CareerSave s, Team team, Player p, string type, int weeks, bool starter)
        {
            if (!(team.Id == s.UserTeamId || (starter && p.Overall >= 80 && weeks >= 4))) return;
            string dur = weeks >= 20 ? "the season" : weeks + (weeks == 1 ? " week" : " weeks");
            Post(s, NewsKind.Injury, Pick(s, team.Nickname + " lose " + p.Name + " for " + dur, p.Name + " (" + type + ") out " + dur), CareerText.PlayerLabel(p) + " is out for " + dur + " (" + type + ").",
                p.Overall >= 85 ? 4 : (starter ? 3 : 2), team.Id, -1, p.Id);
        }

        public static void Retirement(CareerSave s, Team team, Player p, int seasonsRecorded)
        {
            int seasons = Math.Max(seasonsRecorded, p.Experience);
            if (p.Overall < 76 && seasons < 8 && team.Id != s.UserTeamId) return;
            Post(s, NewsKind.Retirement, p.Name + " retires after " + seasons + (seasons == 1 ? " season" : " seasons"), CareerText.PlayerLabel(p) + " (age " + p.Age + ", " + p.Overall + " OVR) is hanging up the cleats" + (team != null ? " as a member of the " + team.Nickname : "") + ".",
                p.Overall >= 88 ? 4 : 3, team != null ? team.Id : -1, -1, p.Id);
        }
    }
}
