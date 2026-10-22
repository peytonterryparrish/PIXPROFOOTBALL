using System.Globalization;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Presentation
{
    public enum StatTableKind { Passer, Rusher, Receiver, Defender, Kicker, Returner }

    /// <summary>
    /// The columns of the slim player stat table (2K8 style: "# NAME TEAM  ATT YDS AVG LNG TD"): which stat columns to show for a player's game so far, picked by what the player did
    /// (same priority as <see cref="OverlayText.StatLine"/>). Pure data, no UI.
    /// </summary>
    public sealed class StatTable
    {
        public StatTableKind Kind;
        /// <summary>Column headers (stat columns only, e.g. ATT YDS AVG LNG TD).</summary>
        public string[] Headers;
        /// <summary>The matching values.</summary>
        public string[] Values;

        public static string Avg(int yards, int count)
        {
            return count <= 0 ? "-" : (yards / (float)count).ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>The stat columns for a player's game line, or null when there is nothing to show.</summary>
        public static StatTable For(StatLine s, Position pos)
        {
            if (s == null) return null;
            if (s.PassAtt > 0 && (pos == Position.QB || s.PassAtt >= 2))
                return Make(StatTableKind.Passer, new[] { "CMP", "ATT", "YDS", "TD", "INT" }, new[] { s.PassCmp.ToString(), s.PassAtt.ToString(), s.PassYds.ToString(), s.PassTd.ToString(), s.PassInt.ToString() });
            if (s.RushAtt > 0 && s.RushAtt >= s.Rec)
                return Make(StatTableKind.Rusher, new[] { "ATT", "YDS", "AVG", "LNG", "TD" }, new[] { s.RushAtt.ToString(), s.RushYds.ToString(), Avg(s.RushYds, s.RushAtt), s.RushLong.ToString(), s.RushTd.ToString() });
            if (s.Rec > 0)
                return Make(StatTableKind.Receiver, new[] { "REC", "YDS", "AVG", "LNG", "TD" }, new[] { s.Rec.ToString(), s.RecYds.ToString(), Avg(s.RecYds, s.Rec), s.RecLong.ToString(), s.RecTd.ToString() });
            if (s.Sacks > 0 || s.Interceptions > 0 || s.Tackles > 0)
                return Make(StatTableKind.Defender, new[] { "TKL", "SACK", "INT" }, new[] { s.Tackles.ToString(), s.Sacks.ToString(), s.Interceptions.ToString() });
            if (s.FgAtt > 0 || s.XpAtt > 0)
                return Make(StatTableKind.Kicker, new[] { "FG", "XP", "LNG" }, new[] { s.FgAtt > 0 ? s.FgMade + "/" + s.FgAtt : "-", s.XpMade + "/" + s.XpAtt, s.FgAtt > 0 ? s.FgLong.ToString() : "-" });
            if (s.KrAtt > 0)
                return Make(StatTableKind.Returner, new[] { "KR", "YDS", "AVG" }, new[] { s.KrAtt.ToString(), s.KrYds.ToString(), Avg(s.KrYds, s.KrAtt) });
            return null;
        }

        static StatTable Make(StatTableKind kind, string[] headers, string[] values) { return new StatTable { Kind = kind, Headers = headers, Values = values }; }
    }
}
