using System.Collections.Generic;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>One of the 32 league teams (data from Assets/_Project/Data/Pro/teams.csv).</summary>
    public class TeamInfo
    {
        public string Id, City, Nickname, Name, Conference, Division, PrimaryHex, SecondaryHex, TertiaryHex, LogoKey;
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
            var list = new List<TeamInfo>();
            var rt = PixRuntimeAssets.Load();
            string csv = rt != null && rt.teamsCsv != null ? rt.teamsCsv.text : null;
#if UNITY_EDITOR
            if (csv == null && System.IO.File.Exists("Assets/_Project/Data/Pro/teams.csv")) csv = System.IO.File.ReadAllText("Assets/_Project/Data/Pro/teams.csv");
#endif
            if (string.IsNullOrEmpty(csv))
            {
                list.Add(new TeamInfo { Id = DefaultTeamId, City = "Buffalo", Nickname = "Ironhorns", Name = "Buffalo Ironhorns", PrimaryHex = "#0C3CB4", SecondaryHex = "#FC0C24", TertiaryHex = "#FFFFFF", LogoKey = "ProV6/buffalo_ironhorns" });
                return list;
            }
            var lines = csv.Replace("\r", "").Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = Split(lines[i]);
                if (f.Count < 10) continue;
                list.Add(new TeamInfo { Id = f[0], City = f[1], Nickname = f[2], Name = f[3], Conference = f[4], Division = f[5], PrimaryHex = f[6], SecondaryHex = f[7], TertiaryHex = f[8], LogoKey = f[9] });
            }
            return list;
        }

        static List<string> Split(string line)
        {
            var r = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            foreach (char c in line)
            {
                if (c == '"') q = !q;
                else if (c == ',' && !q) { r.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            r.Add(sb.ToString());
            return r;
        }
    }
}
