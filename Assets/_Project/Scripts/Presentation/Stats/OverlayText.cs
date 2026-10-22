using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Presentation
{
    public struct CompareRow
    {
        public string Label, Away, Home;
        public CompareRow(string label, string away, string home) { Label = label; Away = away; Home = home; }
    }

    /// <summary>Text for the TV overlays, built from the live box score (all pure and tested): player headings and stat lines, drive summaries, team comparison rows, leaders.</summary>
    public static class OverlayText
    {
        public static string Clock(int seconds)
        {
            seconds = Math.Max(0, seconds);
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>"J. DOE" from a full name.</summary>
        public static string ShortName(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            int sp = full.IndexOf(' ');
            if (sp <= 0 || sp >= full.Length - 1) return full.ToUpperInvariant();
            return (full[0] + ". " + full.Substring(sp + 1)).ToUpperInvariant();
        }

        /// <summary>"#12 J. DOE".</summary>
        public static string Heading(Player p)
        {
            if (p == null) return "";
            return "#" + p.Bio.JerseyNumber + " " + ShortName(p.Name);
        }

        public static string PositionLabel(Position p)
        {
            switch (p)
            {
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT: return "OL";
                case Position.LE: case Position.RE: return "DE";
                case Position.LOLB: case Position.ROLB: return "OLB";
                default: return p.ToString();
            }
        }

        /// <summary>Season-style stat line for a player's game so far, e.g. "18/24, 231 YDS, 2 TD" for a passer. Empty when there is nothing to show.</summary>
        public static string StatLine(StatLine s, Position pos)
        {
            if (s == null) return "";
            var parts = new List<string>();
            if (s.PassAtt > 0 && (pos == Position.QB || s.PassAtt >= 2))
            {
                parts.Add(s.PassCmp + "/" + s.PassAtt); parts.Add(s.PassYds + " YDS");
                if (s.PassTd > 0) parts.Add(s.PassTd + " TD");
                if (s.PassInt > 0) parts.Add(s.PassInt + " INT");
                return string.Join(", ", parts.ToArray());
            }
            if (s.RushAtt > 0 && s.RushAtt >= s.Rec)
            {
                parts.Add(s.RushAtt + " CAR"); parts.Add(s.RushYds + " YDS");
                if (s.RushTd > 0) parts.Add(s.RushTd + " TD");
                return string.Join(", ", parts.ToArray());
            }
            if (s.Rec > 0)
            {
                parts.Add(s.Rec + " REC"); parts.Add(s.RecYds + " YDS");
                if (s.RecTd > 0) parts.Add(s.RecTd + " TD");
                return string.Join(", ", parts.ToArray());
            }
            if (s.Sacks > 0 || s.Interceptions > 0 || s.Tackles > 0)
            {
                parts.Add(s.Tackles + " TKL");
                if (s.Sacks > 0) parts.Add(s.Sacks + (s.Sacks == 1 ? " SACK" : " SACKS"));
                if (s.Interceptions > 0) parts.Add(s.Interceptions + (s.Interceptions == 1 ? " INT" : " INTS"));
                return string.Join(", ", parts.ToArray());
            }
            if (s.FgAtt > 0) return s.FgMade + "/" + s.FgAtt + " FG" + (s.XpAtt > 0 ? ", " + s.XpMade + "/" + s.XpAtt + " XP" : "");
            if (s.XpAtt > 0) return s.XpMade + "/" + s.XpAtt + " XP";   // a kicker with only extra points (QA-843)
            if (s.KrAtt > 0) return s.KrAtt + " KR, " + s.KrYds + " YDS";
            return "";
        }

        /// <summary>Joins the non-empty parts with the separator, so a missing stat line never leaves a dangling " - " (QA-875).</summary>
        public static string JoinNonEmpty(string separator, params string[] parts)
        {
            var sb = new StringBuilder();
            if (parts == null) return "";
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                if (sb.Length > 0) sb.Append(separator);
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        /// <summary>The player-of-the-game sub line: "QB - MIAMI REEFRUNNERS - 5/6, 159 YDS, 2 TD", without a dangling separator when the stat line is empty.</summary>
        public static string PotgSubLine(Player p, string teamName, StatLine s)
        {
            return JoinNonEmpty(" - ", p != null ? PositionLabel(p.Position) : "", (teamName ?? "").ToUpperInvariant(), StatLine(s, p != null ? p.Position : Position.QB));
        }

        /// <summary>"#12 J. DOE - 18/24, 231 YDS, 2 TD" (hyphen so every font renders it).</summary>
        public static string PlayerLine(Player p, StatLine s)
        {
            string stat = StatLine(s, p != null ? p.Position : Position.QB);
            return stat.Length == 0 ? Heading(p) : Heading(p) + " - " + stat;
        }

        /// <summary>Drive summary headline: "9 PLAYS - 72 YARDS - 4:31".</summary>
        public static string DriveHeadline(DriveRecord d)
        {
            if (d == null) return "";
            return d.Plays + (d.Plays == 1 ? " PLAY" : " PLAYS") + " - " + d.Yards + (Math.Abs(d.Yards) == 1 ? " YARD" : " YARDS") + " - " + Clock(d.Seconds);
        }

        public static string DriveResult(DriveRecord d)
        {
            if (d == null) return "";
            switch (d.Result)
            {
                case "Touchdown": return "TOUCHDOWN";
                case "Field Goal": return "FIELD GOAL";
                case "Missed FG": return "MISSED FIELD GOAL";
                case "Punt": return "PUNT";
                case "Interception": return "INTERCEPTION";
                case "Fumble": return "FUMBLE";
                case "Downs": return "TURNOVER ON DOWNS";
                case "Safety": return "SAFETY";
                case "End of Half": return "END OF HALF";
                case "End of Regulation": return "END OF REGULATION";
                case "End of Game": return "END OF GAME";
                default: return (d.Result ?? "").ToUpperInvariant();
            }
        }

        /// <summary>Whether a finished drive deserves a summary card (a one-play drive that ended in a punt does not).</summary>
        public static bool WorthSummary(DriveRecord d) { return d != null && (d.Plays >= 3 || d.Points > 0 || d.Result == "Interception" || d.Result == "Fumble"); }

        /// <summary>"made/attempts" ("-" when there were no attempts). Not a percentage (QA-843).</summary>
        public static string Ratio(int made, int attempts) { return attempts <= 0 ? "-" : made + "/" + attempts; }

        /// <summary>Kept for old callers: same as <see cref="Ratio"/>.</summary>
        public static string Pct(int a, int b) { return Ratio(a, b); }

        /// <summary>Extra comparison rows that do not fit the standard card: 4TH DOWN, SACKS (taken, yards lost), PASSING (completions/attempts) and RED ZONE.</summary>
        public static List<CompareRow> CompareExtra(TeamBoxScore home, TeamBoxScore away)
        {
            var l = new List<CompareRow>();
            l.Add(new CompareRow("4TH DOWN", Ratio(away.FourthDownConv, away.FourthDownAtt), Ratio(home.FourthDownConv, home.FourthDownAtt)));
            l.Add(new CompareRow("COMP / ATT", Ratio(away.PassCmp, away.PassAtt), Ratio(home.PassCmp, home.PassAtt)));
            l.Add(new CompareRow("SACKED", away.Sacked + " - " + away.SackYdsLost, home.Sacked + " - " + home.SackYdsLost));
            l.Add(new CompareRow("RED ZONE", Ratio(away.RedZoneTds, away.RedZoneTrips), Ratio(home.RedZoneTds, home.RedZoneTrips)));
            return l;
        }

        /// <summary>Team comparison rows (away on the left, home on the right, like the scorebug).</summary>
        public static List<CompareRow> Compare(TeamBoxScore home, TeamBoxScore away)
        {
            var l = new List<CompareRow>();
            l.Add(new CompareRow("TOTAL YARDS", away.TotalYards.ToString(), home.TotalYards.ToString()));
            l.Add(new CompareRow("PASSING YARDS", away.NetPassYards.ToString(), home.NetPassYards.ToString()));
            l.Add(new CompareRow("RUSHING YARDS", away.RushYds.ToString(), home.RushYds.ToString()));
            l.Add(new CompareRow("FIRST DOWNS", away.FirstDowns.ToString(), home.FirstDowns.ToString()));
            l.Add(new CompareRow("3RD DOWN", Pct(away.ThirdDownConv, away.ThirdDownAtt), Pct(home.ThirdDownConv, home.ThirdDownAtt)));
            l.Add(new CompareRow("TURNOVERS", away.Giveaways.ToString(), home.Giveaways.ToString()));
            l.Add(new CompareRow("PENALTIES", away.Penalties + " - " + away.PenaltyYds, home.Penalties + " - " + home.PenaltyYds));
            l.Add(new CompareRow("TIME OF POSS.", Clock(away.TimeOfPossessionSeconds), Clock(home.TimeOfPossessionSeconds)));
            return l;
        }

        /// <summary>The compact comparison card between plays: three rows.</summary>
        public static List<CompareRow> CompareShort(TeamBoxScore home, TeamBoxScore away)
        {
            var l = new List<CompareRow>();
            l.Add(new CompareRow("TOTAL YARDS", away.TotalYards.ToString(), home.TotalYards.ToString()));
            l.Add(new CompareRow("FIRST DOWNS", away.FirstDowns.ToString(), home.FirstDowns.ToString()));
            l.Add(new CompareRow("TIME OF POSS.", Clock(away.TimeOfPossessionSeconds), Clock(home.TimeOfPossessionSeconds)));
            return l;
        }

        /// <summary>Leader line for the halftime / postgame show: "J. DOE  12/18  176 YDS  1 TD".</summary>
        public static string LeaderLine(PlayerGameLine l, string category)
        {
            var s = l.Stats;
            string name = ShortName(l.Name);
            switch (category)
            {
                case "pass": return name + "   " + s.PassCmp + "/" + s.PassAtt + "   " + s.PassYds + " YDS   " + s.PassTd + " TD" + (s.PassInt > 0 ? "   " + s.PassInt + " INT" : "");
                case "rush": return name + "   " + s.RushAtt + " CAR   " + s.RushYds + " YDS   " + s.RushTd + " TD";
                case "rec": return name + "   " + s.Rec + " REC   " + s.RecYds + " YDS   " + s.RecTd + " TD";
                default: return name + "   " + s.Tackles + " TKL   " + s.Sacks + " SCK" + (s.Interceptions > 0 ? "   " + s.Interceptions + " INT" : "");
            }
        }

        /// <summary>Down and distance for the "next play" card: "2ND & 7 - BUF 34".</summary>
        public static string DownAndDistance(int down, int distance, bool goalToGo)
        {
            string d = down == 1 ? "1ST" : down == 2 ? "2ND" : down == 3 ? "3RD" : "4TH";
            return d + " & " + (goalToGo ? "GOAL" : distance.ToString());
        }

        /// <summary>The temperature at kickoff for the stadium card: stable for a seed, 36 to 76 F by day, 31 to 71 F at night (5 degrees cooler).</summary>
        public static int KickoffTemperature(int seed, bool night, string city)
        {
            int ch = 17;
            if (city != null) foreach (char c in city) ch = unchecked(ch * 31 + c);
            int h = unchecked(seed * 73856093 ^ ch * 19349663);
            h = (h ^ (h >> 13)) & 0x7fffffff;
            int t = 36 + h % 41;
            return night ? Math.Max(30, t - 5) : t;
        }

        /// <summary>Same, but the match weather decides the range (QA-844): snow is always at or below freezing, rain and fog stay mild, clear and overcast keep the plain value.</summary>
        public static int KickoffTemperature(int seed, bool night, string city, WeatherKind weather)
        {
            int t = KickoffTemperature(seed, night, city);
            switch (weather)
            {
                case WeatherKind.Snow: return Math.Min(t, 24 + (t % 9));        // 24..32 F
                case WeatherKind.Rain: return Math.Max(40, Math.Min(t, 62));
                case WeatherKind.Fog: return Math.Max(36, Math.Min(t, 56));
                default: return t;
            }
        }
    }
}
