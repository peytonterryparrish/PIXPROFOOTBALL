using System;
using System.Collections.Generic;
using System.Text;

namespace PixFootball.League
{
    /// <summary>One row of Data/College/schools.csv.</summary>
    [Serializable]
    public sealed class SchoolDef
    {
        public string Id;
        /// <summary>Full display identity, e.g. "Amber Falls Skyhawks".</summary>
        public string Name;
        public string Conference;
        public string ConferenceId;
        public string Logo;

        public bool IsIndependent { get { return string.IsNullOrEmpty(ConferenceId) || ConferenceId == "independent"; } }

        /// <summary>Last word of the name ("Skyhawks").</summary>
        public string Nickname
        {
            get
            {
                int i = Name.LastIndexOf(' ');
                return i < 0 ? Name : Name.Substring(i + 1);
            }
        }

        /// <summary>Everything before the nickname ("Amber Falls").</summary>
        public string Place
        {
            get
            {
                int i = Name.LastIndexOf(' ');
                return i < 0 ? Name : Name.Substring(0, i);
            }
        }
    }

    /// <summary>One row of Data/College/conferences_bowls.csv.</summary>
    [Serializable]
    public sealed class ConfBowlDef
    {
        public string Id;
        public string Name;
        /// <summary>conference, bowl, playoff or national_championship.</summary>
        public string Category;
        /// <summary>conference, bowl, cfp, playoff or national_championship.</summary>
        public string Tier;
        public string Logo;

        public bool IsConference { get { return Category == "conference"; } }
        public bool IsBowl { get { return Category == "bowl"; } }
        public bool IsPlayoffBowl { get { return Category == "bowl" && Tier == "cfp"; } }
    }

    /// <summary>Minimal CSV reader (quoted fields, BOM) for the committed college data files.</summary>
    public static class CollegeData
    {
        public static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '﻿') text = text.Substring(1);
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else if (c == '\r') { }
                else if (c == '\n')
                {
                    fields.Add(sb.ToString()); sb.Length = 0;
                    if (!(fields.Count == 1 && fields[0].Length == 0)) rows.Add(fields.ToArray());
                    fields.Clear();
                }
                else sb.Append(c);
            }
            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                if (!(fields.Count == 1 && fields[0].Length == 0)) rows.Add(fields.ToArray());
            }
            return rows;
        }

        private static Dictionary<string, int> Header(string[] row)
        {
            var map = new Dictionary<string, int>();
            for (int i = 0; i < row.Length; i++) map[row[i].Trim().ToLowerInvariant()] = i;
            return map;
        }

        private static string Get(string[] row, Dictionary<string, int> h, string col)
        {
            int i;
            return h.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : "";
        }

        public static List<SchoolDef> ParseSchools(string csvText)
        {
            var rows = ParseCsv(csvText);
            var list = new List<SchoolDef>();
            if (rows.Count == 0) return list;
            var h = Header(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                string name = Get(r, h, "name");
                if (name.Length == 0) continue;
                list.Add(new SchoolDef
                {
                    Id = Get(r, h, "id"), Name = name, Conference = Get(r, h, "conference"),
                    ConferenceId = Get(r, h, "conference_id"), Logo = Get(r, h, "logo")
                });
            }
            return list;
        }

        public static List<ConfBowlDef> ParseConfBowls(string csvText)
        {
            var rows = ParseCsv(csvText);
            var list = new List<ConfBowlDef>();
            if (rows.Count == 0) return list;
            var h = Header(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                string name = Get(r, h, "name");
                if (name.Length == 0) continue;
                list.Add(new ConfBowlDef
                {
                    Id = Get(r, h, "id"), Name = name, Category = Get(r, h, "category"), Tier = Get(r, h, "tier"), Logo = Get(r, h, "logo")
                });
            }
            return list;
        }
    }
}
