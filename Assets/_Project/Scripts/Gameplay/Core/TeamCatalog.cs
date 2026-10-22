using System.Collections.Generic;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>One of the 32 league teams (data from Assets/_Project/Data/Pro/teams.csv).</summary>
    public class TeamInfo
    {
        public string Id, City, Nickname, Name, Conference, Division, PrimaryHex, SecondaryHex, TertiaryHex, LogoKey;
        /// <summary>Fixed three-letter code from the abbr column ("" for an old file without it).</summary>
        public string Abbr = "";
        public TeamColors Colors { get { return TeamColors.FromHex(PrimaryHex, SecondaryHex, TertiaryHex); } }
        public Color PrimaryUi { get { Color c; ColorUtility.TryParseHtmlString(PrimaryHex, out c); return c; } }
        public Color SecondaryUi { get { Color c; ColorUtility.TryParseHtmlString(SecondaryHex, out c); return c; } }
    }

    public static class TeamCatalog
    {
        static List<TeamInfo> _teams;
        public const string DefaultTeamId = "buffalo_ironhorns";

        public static IReadOnlyList<TeamInfo> Teams
        {
            get { if (_teams == null) _teams = Load(); return _teams; }
        }

        public static TeamInfo ById(string id)
        {
            foreach (var t in Teams) if (t.Id == id) return t;
            return Teams.Count > 0 ? Teams[0] : null;
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < Teams.Count; i++) if (Teams[i].Id == id) return i;
            return 0;
        }

        public static string SavedTeamId
        {
            get { return PlayerPrefs.GetString("pix.team", DefaultTeamId); }
            set { PlayerPrefs.SetString("pix.team", value); }
        }

        static List<TeamInfo> Load()
        {
            var rt = PixRuntimeAssets.Load();
            string csv = rt != null && rt.teamsCsv != null ? rt.teamsCsv.text : null;
#if UNITY_EDITOR
            if (csv == null && System.IO.File.Exists("Assets/_Project/Data/Pro/teams.csv")) csv = System.IO.File.ReadAllText("Assets/_Project/Data/Pro/teams.csv");
#endif
            if (string.IsNullOrEmpty(csv)) Debug.LogError("TeamCatalog: teams.csv is missing from PixRuntimeAssets (a damaged build): falling back to ONE built-in team. Re-run the build / asset setup.");
            return Parse(csv);
        }

        /// <summary>Parses teams.csv (BOM and quotes handled by the one shared CSV reader). Missing / empty text gives the single built-in fallback team.</summary>
        public static List<TeamInfo> Parse(string csv)
        {
            var list = new List<TeamInfo>();
            var rows = string.IsNullOrEmpty(csv) ? new List<string[]>() : PixFootball.League.CollegeData.ParseCsv(csv);
            if (rows.Count > 1)
            {
                var h = new Dictionary<string, int>();
                for (int i = 0; i < rows[0].Length; i++) h[rows[0][i].Trim().ToLowerInvariant()] = i;
                for (int i = 1; i < rows.Count; i++)
                {
                    string[] f = rows[i];
                    if (f.Length < 10) continue;
                    list.Add(new TeamInfo
                    {
                        Id = Col(f, h, "id", 0), City = Col(f, h, "city", 1), Nickname = Col(f, h, "nickname", 2), Name = Col(f, h, "name", 3), Conference = Col(f, h, "conference", 4), Division = Col(f, h, "division", 5),
                        PrimaryHex = Col(f, h, "primary", 6), SecondaryHex = Col(f, h, "secondary", 7), TertiaryHex = Col(f, h, "tertiary", 8), LogoKey = Col(f, h, "logo", 9), Abbr = Col(f, h, "abbr", -1)
                    });
                }
            }
            if (list.Count == 0)
                list.Add(new TeamInfo { Id = DefaultTeamId, City = "Buffalo", Nickname = "Ironhorns", Name = "Buffalo Ironhorns", PrimaryHex = "#0C3CB4", SecondaryHex = "#FC0C24", TertiaryHex = "#FFFFFF", LogoKey = "ProV6/buffalo_ironhorns", Abbr = "BUF" });
            return list;
        }

        static string Col(string[] row, Dictionary<string, int> h, string name, int fallbackIndex)
        {
            int i;
            if (!h.TryGetValue(name, out i)) i = fallbackIndex;
            return i >= 0 && i < row.Length ? row[i].Trim() : "";
        }
    }
}
