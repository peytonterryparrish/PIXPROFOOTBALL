using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>
    /// A generated college world definition (138 fictional schools in 10 conferences, 41 bowls, playoff and championship names) for runtimes that have no school data files:
    /// the career then still has a full college network. The same inputs always give the same schools.
    /// </summary>
    public static class CollegeFallback
    {
        public const int SchoolCount = 138;

        private static readonly string[] ConferenceIds =
        {
            "heartland_athletic_union", "tidewater_athletic_union", "prairie_twelve", "great_lakes_union", "continental_sports_league",
            "midland_athletic_league", "summit_range_alliance", "pacific_crest_union", "southern_heritage_league", "solar_coast_conference"
        };

        private static readonly string[] ConferenceNames =
        {
            "Heartland Athletic Union", "Tidewater Athletic Union", "Prairie Twelve", "Great Lakes Union", "Continental Sports League",
            "Midland Athletic League", "Summit Range Alliance", "Pacific Crest Union", "Southern Heritage League", "Solar Coast Conference"
        };

        private static readonly string[] CfpBowlNames =
        {
            "Briarfield Bowl", "Frostcane Bowl", "Tangelo Comet Bowl", "Linenfield Classic", "Emberfair Bowl", "Ridgegold Bowl"
        };

        private static string Slug(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = char.ToLowerInvariant(name[i]);
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        public static List<SchoolDef> Schools(int seed, int count = SchoolCount)
        {
            var rng = new Rng(unchecked((ulong)(uint)seed) ^ 0xC011E6EUL);
            var list = new List<SchoolDef>();
            var usedNames = new HashSet<string>();
            var usedAbbrs = new HashSet<string>();
            var usedMascots = new HashSet<string>();
            var usedShort = new HashSet<string>();
            var usedIds = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                SchoolIdentity id = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    id = NameGenerator.SchoolName(rng, usedNames, usedAbbrs, usedMascots);
                    if (usedShort.Add(id.ShortName + " " + id.Mascot)) break;
                }
                string name = id.ShortName + " " + id.Mascot;
                string key = Slug(name);
                int dup = 1;
                while (!usedIds.Add(key)) key = Slug(name) + "_" + (++dup);
                int conf = i * ConferenceIds.Length / count;
                list.Add(new SchoolDef { Id = key, Name = name, Conference = ConferenceNames[conf], ConferenceId = ConferenceIds[conf], Logo = "", ShortName = id.ShortName, State = id.State });
            }
            return list;
        }

        public static List<ConfBowlDef> Bowls()
        {
            var list = new List<ConfBowlDef>();
            for (int i = 0; i < ConferenceIds.Length; i++)
                list.Add(new ConfBowlDef { Id = ConferenceIds[i], Name = ConferenceNames[i], Category = "conference", Tier = "conference", Logo = "" });
            for (int i = 0; i < CfpBowlNames.Length; i++)
                list.Add(new ConfBowlDef { Id = Slug(CfpBowlNames[i]), Name = CfpBowlNames[i], Category = "bowl", Tier = "cfp", Logo = "" });
            var names = NameGenerator.BowlNames;
            for (int i = 0; i < names.Count && i < 35; i++)
                list.Add(new ConfBowlDef { Id = Slug(names[i]), Name = names[i], Category = "bowl", Tier = "bowl", Logo = "" });
            list.Add(new ConfBowlDef { Id = "gridiron_crown_playoff", Name = "Gridiron Crown Playoff", Category = "playoff", Tier = "playoff", Logo = "" });
            list.Add(new ConfBowlDef { Id = "gridiron_crown_championship", Name = "Gridiron Crown Championship", Category = "national_championship", Tier = "national_championship", Logo = "" });
            return list;
        }
    }
}
