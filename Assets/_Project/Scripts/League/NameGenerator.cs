using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Generated identity for a fictional school.</summary>
    public sealed class SchoolIdentity
    {
        public string Name;
        public string ShortName;
        public string Mascot;
        public string Abbreviation;
        public string City;
        public string State;
    }

    /// <summary>
    /// Deterministic generator for person, city, team and school names. All output is fictional; the
    /// pools hold only generic given names / surnames and invented place names.
    /// </summary>
    public static class NameGenerator
    {
        public static readonly string[] FirstNames = Split(NameData.FirstRaw);
        public static readonly string[] LastNames = Split(NameData.LastRaw);
        public static IReadOnlyList<string> Cities { get { return NameData.ProCities; } }
        public static IReadOnlyList<string> Nicknames { get { return NameData.ProNicknames; } }
        public static IReadOnlyList<string> Mascots { get { return NameData.SchoolMascots; } }
        public static IReadOnlyList<string> BowlNames { get { return NameData.BowlNames; } }

        private static string[] Split(string raw)
        {
            var seen = new HashSet<string>();
            var list = new List<string>();
            var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) if (seen.Add(parts[i])) list.Add(parts[i]);
            return list.ToArray();
        }

        public static string FirstName(Rng rng) { return FirstNames[rng.Range(0, FirstNames.Length)]; }
        public static string LastName(Rng rng) { return LastNames[rng.Range(0, LastNames.Length)]; }

        public static string Hometown(Rng rng) { return NameData.Hometowns[rng.Range(0, NameData.Hometowns.Length)]; }

        private static HashSet<string> _blocked;
        private static HashSet<string> Blocked
        {
            get
            {
                if (_blocked == null) _blocked = new HashSet<string>(NameData.RealNameBlocklist, StringComparer.OrdinalIgnoreCase);
                return _blocked;
            }
        }

        /// <summary>True when "first last" is a real, well-known player or coach (hand-written blocklist, case-insensitive). The generator never returns such a name.</summary>
        public static bool IsRealPersonName(string first, string last)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last)) return false;
            return Blocked.Contains(first + " " + last);
        }

        /// <summary>The blocklist itself (lower case "first last"), for tests and tools.</summary>
        public static IReadOnlyList<string> RealNameBlocklist { get { return NameData.RealNameBlocklist; } }

        /// <summary>Unique "First Last" (unique within <paramref name="used"/>, which is updated; never a name on the real-name blocklist).</summary>
        public static void PersonName(Rng rng, HashSet<string> used, out string first, out string last)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                first = FirstName(rng);
                last = LastName(rng);
                if (IsRealPersonName(first, last)) continue;
                string key = first + " " + last;
                if (used == null || used.Add(key)) return;
            }
            for (int attempt = 0; attempt < 40; attempt++)
            {
                first = FirstName(rng);
                last = LastName(rng);
                if (!IsRealPersonName(first, last)) break;
            }
            first = FirstName(rng);
            last = LastName(rng);
            while (IsRealPersonName(first, last)) last = LastName(rng);
            if (used != null) used.Add(first + " " + last);
        }

        /// <summary>A plausible invented place name, e.g. "Dalmont", "Kenwick".</summary>
        public static string PlaceName(Rng rng)
        {
            string s = NameData.PlaceStart[rng.Range(0, NameData.PlaceStart.Length)]
                       + NameData.PlaceMiddle[rng.Range(0, NameData.PlaceMiddle.Length)]
                       + NameData.PlaceEnd[rng.Range(0, NameData.PlaceEnd.Length)];
            return Capitalize(s);
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Plain US city + nickname, avoiding combinations in <paramref name="used"/>.</summary>
        public static void ProTeamName(Rng rng, HashSet<string> used, out string city, out string nickname)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                city = NameData.ProCities[rng.Range(0, NameData.ProCities.Length)];
                nickname = NameData.ProNicknames[rng.Range(0, NameData.ProNicknames.Length)];
                if (used == null || used.Add(city + " " + nickname)) return;
            }
            city = NameData.ProCities[rng.Range(0, NameData.ProCities.Length)];
            nickname = NameData.ProNicknames[rng.Range(0, NameData.ProNicknames.Length)];
        }

        /// <summary>
        /// Generates a fictional school. Patterns: "&lt;Place&gt; State University", "University of &lt;Place&gt;",
        /// "&lt;Place&gt; Tech", "&lt;Geo&gt; &lt;Place&gt; College", "&lt;Direction&gt; &lt;Place&gt; University", "&lt;Place&gt; Polytechnic".
        /// </summary>
        public static SchoolIdentity SchoolName(Rng rng, HashSet<string> usedNames, HashSet<string> usedAbbrs, HashSet<string> usedMascotKeys)
        {
            string place = null, name = null, shortName = null;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                place = PlaceName(rng);
                int pattern = rng.Range(0, 100);
                if (pattern < 28) { name = place + " State University"; shortName = place + " State"; }
                else if (pattern < 52) { name = "University of " + place; shortName = place; }
                else if (pattern < 66) { name = place + " Tech"; shortName = place + " Tech"; }
                else if (pattern < 76)
                {
                    string geo = NameData.GeoWords[rng.Range(0, NameData.GeoWords.Length)];
                    name = geo + " " + place + " College"; shortName = geo + " " + place;
                }
                else if (pattern < 86)
                {
                    string dir = NameData.Directions[rng.Range(0, NameData.Directions.Length)];
                    name = dir + " " + place + " University"; shortName = dir + " " + place;
                }
                else if (pattern < 93) { name = place + " Polytechnic"; shortName = place + " Poly"; }
                else
                {
                    string geo = NameData.GeoWords[rng.Range(0, NameData.GeoWords.Length)];
                    name = "University of " + geo + " " + place; shortName = geo + " " + place;
                }
                if (usedNames.Add(name)) break;
                name = null;
            }
            if (name == null) { name = place + " Institute"; shortName = place + " Institute"; usedNames.Add(name); }

            string mascot = null;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                mascot = NameData.SchoolMascots[rng.Range(0, NameData.SchoolMascots.Length)];
                // allow mascots to repeat across the world but keep (mascot, first letter) combos mostly distinct
                string key = mascot + shortName[0];
                if (usedMascotKeys.Add(key)) break;
            }

            string abbr = MakeAbbreviation(shortName, usedAbbrs, rng);
            string stateAbbr = States[rng.Range(0, States.Length)];
            return new SchoolIdentity
            {
                Name = name, ShortName = shortName, Mascot = mascot, Abbreviation = abbr,
                City = place, State = stateAbbr
            };
        }

        private static readonly string[] States =
        {
            "AL", "AZ", "AR", "CA", "CO", "CT", "FL", "GA", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO",
            "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "SC", "SD", "TN", "TX", "UT", "VA", "WA", "WV", "WI", "WY"
        };

        public static string MakeAbbreviation(string shortName, HashSet<string> used, Rng rng)
        {
            var letters = new StringBuilder();
            var words = shortName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // candidates: initials, first three letters, first + consonants
            var candidates = new List<string>();
            var init = new StringBuilder();
            for (int i = 0; i < words.Length; i++) init.Append(char.ToUpperInvariant(words[i][0]));
            string first = words[0];
            candidates.Add(Pad(first.Substring(0, Math.Min(3, first.Length)).ToUpperInvariant()));
            if (init.Length >= 2) candidates.Add(Pad(init.ToString().ToUpperInvariant()));
            candidates.Add(Pad(char.ToUpperInvariant(first[0]) + ConsonantTail(first, 2)));
            if (words.Length > 1) candidates.Add(Pad(first.Substring(0, 2).ToUpperInvariant() + char.ToUpperInvariant(words[1][0])));
            candidates.Add(Pad(first.Substring(0, Math.Min(4, first.Length)).ToUpperInvariant()));
            for (int i = 0; i < candidates.Count; i++) if (used.Add(candidates[i])) return candidates[i];
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string c = "" + (char)('A' + rng.Range(0, 26)) + (char)('A' + rng.Range(0, 26)) + (char)('A' + rng.Range(0, 26));
                if (used.Add(c)) return c;
            }
            return candidates[0];
        }

        private static string Pad(string s)
        {
            if (s.Length > 4) s = s.Substring(0, 4);
            return s;
        }

        private static string ConsonantTail(string s, int n)
        {
            var sb = new StringBuilder();
            for (int i = 1; i < s.Length && sb.Length < n; i++)
            {
                char c = char.ToUpperInvariant(s[i]);
                if ("AEIOU".IndexOf(c) < 0) sb.Append(c);
            }
            while (sb.Length < n) sb.Append('X');
            return sb.ToString();
        }

        /// <summary>Random hex color "#RRGGBB" from the HSV space, saturated enough for a uniform.</summary>
        public static string RandomColor(Rng rng, float minValue = 0.35f)
        {
            float h = rng.NextFloat();
            float s = 0.55f + 0.4f * rng.NextFloat();
            float v = minValue + (1f - minValue) * rng.NextFloat();
            return HsvToHex(h, s, v);
        }

        public static string HsvToHex(float h, float s, float v)
        {
            float r, g, b;
            int i = (int)Math.Floor(h * 6f);
            float f = h * 6f - i;
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            switch (((i % 6) + 6) % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return "#" + ((int)(r * 255f)).ToString("X2") + ((int)(g * 255f)).ToString("X2") + ((int)(b * 255f)).ToString("X2");
        }
    }
}
