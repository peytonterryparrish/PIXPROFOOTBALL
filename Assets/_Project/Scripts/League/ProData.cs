using System;
using System.Collections.Generic;
using System.Text;

namespace PixFootball.League
{
    /// <summary>One row of Data/Pro/teams.csv.</summary>
    [Serializable]
    public sealed class ProTeamDef
    {
        public string Id;
        public string City;
        public string Nickname;
        /// <summary>Full display identity, e.g. "Buffalo Ironhorns".</summary>
        public string Name;
        public string Conference;
        public string Division;
        public string Primary = "#000000";
        public string Secondary = "#FFFFFF";
        public string Tertiary = "#FFFFFF";
        public string Logo;
        /// <summary>Fixed 3-letter code from the abbr column (empty when the file has none: then it is derived).</summary>
        public string Abbr = "";
    }

    /// <summary>Loader for the committed pro-league team list.</summary>
    public static class ProData
    {
        public static List<ProTeamDef> ParseTeams(string csvText)
        {
            List<string[]> rows = CollegeData.ParseCsv(csvText);
            var list = new List<ProTeamDef>();
            if (rows.Count == 0) return list;
            var h = new Dictionary<string, int>();
            for (int i = 0; i < rows[0].Length; i++) h[rows[0][i].Trim().ToLowerInvariant()] = i;
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                string city = Get(r, h, "city");
                string nick = Get(r, h, "nickname");
                string name = Get(r, h, "name");
                if (name.Length == 0) name = (city + " " + nick).Trim();
                if (name.Length == 0) continue;
                list.Add(new ProTeamDef
                {
                    Id = Get(r, h, "id"), City = city, Nickname = nick, Name = name,
                    Conference = Get(r, h, "conference"), Division = Get(r, h, "division"),
                    Primary = NormalizeHex(Get(r, h, "primary"), "#000000"),
                    Secondary = NormalizeHex(Get(r, h, "secondary"), "#FFFFFF"),
                    Tertiary = NormalizeHex(Get(r, h, "tertiary"), "#FFFFFF"),
                    Logo = Get(r, h, "logo"), Abbr = CleanAbbr(Get(r, h, "abbr"))
                });
            }
            return list;
        }

        private static string Get(string[] row, Dictionary<string, int> h, string col)
        {
            int i;
            return h.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : "";
        }

        /// <summary>Returns "#RRGGBB" (upper case) for a valid 6-digit hex string, otherwise the fallback.</summary>
        public static string NormalizeHex(string s, string fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6) return fallback;
            for (int i = 0; i < 6; i++)
                if (!Uri.IsHexDigit(s[i])) return fallback;
            return "#" + s.ToUpperInvariant();
        }

        /// <summary>Preferred short codes for the official teams (keyed by team id). Anything else is derived.</summary>
        private static readonly Dictionary<string, string> Preferred = new Dictionary<string, string>
        {
            { "buffalo_ironhorns", "BUF" }, { "miami_reefrunners", "MIA" }, { "new_england_sentinels", "NEW" }, { "new_york_skybolts", "NYS" },
            { "baltimore_nightwings", "BAL" }, { "cincinnati_embercats", "CIN" }, { "cleveland_foundry", "CLE" }, { "pittsburgh_ironforge", "PIT" },
            { "houston_stampede", "HOU" }, { "indianapolis_bluechargers", "IND" }, { "jacksonville_goldclaws", "JAX" }, { "tennessee_stormguard", "TEN" },
            { "denver_ridgebacks", "DEN" }, { "kansas_city_firebirds", "KCF" }, { "las_vegas_blackjacks", "LVB" }, { "los_angeles_thunderhawks", "LAT" },
            { "dallas_outriders", "DAL" }, { "new_york_colossi", "NYC" }, { "philadelphia_talons", "PHI" }, { "washington_redforts", "WAS" },
            { "chicago_bruisers", "CHI" }, { "detroit_silvermanes", "DET" }, { "green_bay_lumberjacks", "GBL" }, { "minnesota_frostgiants", "MIN" },
            { "atlanta_redtails", "ATL" }, { "carolina_nightcats", "CAR" }, { "new_orleans_brassguard", "NOB" }, { "tampa_bay_corsairs", "TBC" },
            { "arizona_sunhawks", "ARI" }, { "los_angeles_goldhorns", "LAG" }, { "san_francisco_goldrush", "SFG" }, { "seattle_tidewolves", "SEA" }
        };

        /// <summary>
        /// Unique 2-3 letter codes, one per team in input order. Known official ids use their preferred code;
        /// anything else is derived from city initials / first letters, then city+nickname letters until unique.
        /// </summary>
        public static string[] DeriveAbbreviations(IReadOnlyList<ProTeamDef> teams)
        {
            var result = new string[teams.Count];
            var used = new HashSet<string>();
            for (int i = 0; i < teams.Count; i++)
            {
                string code;
                if (teams[i].Abbr != null && teams[i].Abbr.Length == 3 && used.Add(teams[i].Abbr)) { result[i] = teams[i].Abbr; continue; }
                if (!string.IsNullOrEmpty(teams[i].Id) && Preferred.TryGetValue(teams[i].Id, out code) && used.Add(code)) result[i] = code;
            }
            for (int i = 0; i < teams.Count; i++)
            {
                if (result[i] != null) continue;
                result[i] = Derive(teams[i], used);
            }
            return result;
        }

        private static string Derive(ProTeamDef t, HashSet<string> used)
        {
            string city = Letters(t.City), nick = Letters(t.Nickname);
            string[] words = (t.City ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cands = new List<string>();
            if (words.Length >= 2)
            {
                var ini = new StringBuilder();
                for (int i = 0; i < words.Length && ini.Length < 3; i++) { string w = Letters(words[i]); if (w.Length > 0) ini.Append(w[0]); }
                if (ini.Length == 2) { string last = Letters(words[words.Length - 1]); if (last.Length >= 2) cands.Add(ini.ToString() + last[1]); }
                cands.Add(ini.ToString());
            }
            if (city.Length >= 3) cands.Add(city.Substring(0, 3));
            if (city.Length >= 2 && nick.Length >= 1) cands.Add(city.Substring(0, 2) + nick[0]);
            if (city.Length >= 1 && nick.Length >= 2) cands.Add(city.Substring(0, 1) + nick.Substring(0, 2));
            if (city.Length >= 2 && nick.Length >= 1) cands.Add(city.Substring(0, 2) + nick[nick.Length - 1]);
            for (int i = 0; i < cands.Count; i++)
            {
                string c = cands[i].ToUpperInvariant();   // always exactly 3 letters (fixed-width scorebug and ticker)
                if (c.Length == 3 && used.Add(c)) return c;
            }
            string basis = (city + nick + "XXX").ToUpperInvariant();
            for (int a = 0; a < basis.Length; a++)
                for (int b = a + 1; b < basis.Length; b++)
                    for (int c = b + 1; c < basis.Length; c++)
                    {
                        string s = "" + basis[a] + basis[b] + basis[c];
                        if (used.Add(s)) return s;
                    }
            for (int n = 0; ; n++) { string s = "T" + n.ToString("00"); if (used.Add(s)) return s; }
        }

        /// <summary>The abbr column cleaned to exactly three upper-case letters, otherwise empty (then the code is derived).</summary>
        public static string CleanAbbr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim().ToUpperInvariant();
            if (s.Length != 3) return "";
            for (int i = 0; i < 3; i++) if (s[i] < 'A' || s[i] > 'Z') return "";
            return s;
        }

        private static string Letters(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) if (char.IsLetter(s[i])) sb.Append(s[i]);
            return sb.ToString();
        }
    }
}
