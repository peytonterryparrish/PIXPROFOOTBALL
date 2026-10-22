using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>Ten seasons with the college world on: rosters valid, recruits keep every school at 85, draft classes of about 260 with a sensible talent spread, determinism, save and load.</summary>
    public class CollegeSoakTests
    {
        private sealed class YearLine
        {
            public int Year, ClassSize, Juniors, Rookies, Undrafted;
            public float ClassMean, ClassTop10, ClassMin, CollegeMean, CollegeTopTier, CollegeLowTier, FreshmanStars;
            public int BoxScores, Honors, History, Scouts;
            public long Fingerprint;
        }

        private static List<YearLine> _lines;
        private static CareerSave _save;
        private static long _jsonLength;

        private static void Run()
        {
            if (_lines != null) return;
            var lines = new List<YearLine>();
            CareerSave s = CollegeTestUtil.Career(2031, 9);
            for (int y = 0; y < 10; y++)
            {
                CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
                Assert.AreEqual(s.Year, s.College.Year, "the college year follows the pro year");
                CollegeTestUtil.AssertRostersFull(s);
                CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
                var cls = s.Pro.DraftClass;
                var line = new YearLine
                {
                    Year = s.Year, ClassSize = cls.Count, Juniors = cls.Count(p => p.DeclaredEarly), ClassMean = (float)cls.Average(p => p.Player.Overall), ClassTop10 = (float)cls.OrderBy(p => p.ConsensusRank).Take(10).Average(p => p.Player.Overall),
                    ClassMin = cls.Min(p => p.Player.Overall)
                };
                // valid class: unique ids, every prospect on his school's roster, ranks 1..n
                Assert.AreEqual(cls.Count, cls.Select(p => p.Id).Distinct().Count(), line.Year + " duplicate prospects");
                Assert.AreEqual(Enumerable.Range(1, cls.Count).ToList(), cls.Select(p => p.ConsensusRank).ToList());
                Assert.IsTrue(cls.All(p => s.College.GetSchool(p.SchoolId).Team.GetPlayer(p.Id) != null), line.Year + " prospect missing from his school");
                // the user spends points like a real session each year
                var top = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Take = 12 }).Rows;
                CollegeScouting.AttendCombine(s);
                foreach (ScoutingRow r in top.Take(6)) CollegeScouting.ScoutPlayer(s, r.PlayerId, 30);
                CollegeScouting.AddToBoard(s, top[0].PlayerId);
                CareerFlow.AdvanceUntil(s, CareerPhase.RookieSigning);
                line.Rookies = s.Pro.LastDraft.Count;
                line.Undrafted = s.Pro.FreeAgents.Count(p => p.DraftYear == s.Year && p.DraftRound == 0 && p.Experience == 0);
                // the college turned over: 85 everywhere, tiers kept, names unique, no duplicates between worlds
                Assert.AreEqual(s.Year + 1, s.College.Year);
                CollegeTestUtil.AssertRostersFull(s);
                var prestige = s.College.Schools.GroupBy(x => x.Prestige).ToDictionary(g => g.Key, g => g.Count());
                int[] expected = CollegeFactory.TierCountsFor(138);
                for (int t = 0; t < expected.Length; t++) Assert.AreEqual(expected[t], prestige.ContainsKey(6 - t) ? prestige[6 - t] : 0, "prestige tier " + (6 - t) + " in " + line.Year);
                var collegeNames = s.College.Schools.SelectMany(x => x.Team.Roster).Select(p => p.Name).ToList();
                Assert.AreEqual(collegeNames.Count, collegeNames.Distinct().Count(), "unique college names");
                var collegeIds = new HashSet<int>(s.College.Schools.SelectMany(x => x.Team.Roster).Select(p => p.Id));
                Assert.IsFalse(s.Pro.AllRosterPlayers().Any(p => collegeIds.Contains(p.Id)), "nobody is both a pro and a collegian");
                Assert.IsFalse(s.Pro.FreeAgents.Any(p => collegeIds.Contains(p.Id)));
                var ovrs = s.College.Schools.SelectMany(x => x.Team.Roster).Select(p => p.Overall).ToList();
                line.CollegeMean = (float)ovrs.Average();
                line.CollegeTopTier = (float)s.College.Schools.Where(x => x.Prestige >= 5).SelectMany(x => x.Team.Roster).Average(p => p.Overall);
                line.CollegeLowTier = (float)s.College.Schools.Where(x => x.Prestige <= 2).SelectMany(x => x.Team.Roster).Average(p => p.Overall);
                line.FreshmanStars = (float)s.College.Schools.SelectMany(x => x.Team.Roster).Where(p => p.Class == ClassYear.Freshman).Average(p => p.Stars);
                // network state stays bounded
                Assert.That(s.Net.Scouts.Count(x => x.TeamId >= 0), Is.InRange(32 * 3, 32 * 5));
                for (int t = 0; t < 32; t++) Assert.That(CollegeScouting.TeamScouts(s, t).Count, Is.InRange(3, 5));
                Assert.LessOrEqual(s.Net.Boxes.Count, 2 * 1000, "two seasons of box scores at most");
                Assert.IsTrue(s.Net.Honors.All(h => h.Year >= s.Year - 3));
                Assert.LessOrEqual(s.Net.History.Count, 10 * 138);
                Assert.IsTrue(s.Net.Board.All(e => e.PlayerId > 0 && (e.Starred || e.OnBoard || e.Note.Length > 0 || e.Tag.Length > 0)));
                line.BoxScores = s.Net.Boxes.Count; line.Honors = s.Net.Honors.Count; line.History = s.Net.History.Count; line.Scouts = s.Net.Scouts.Count;
                line.Fingerprint = CollegeTestUtil.Fingerprint(s);
                List<string> general = CareerValidator.Check(s, false);
                Assert.AreEqual(0, general.Count, "year " + line.Year + ": " + string.Join("; ", general.Take(6).ToArray()));
                lines.Add(line);
                CareerFlow.AdvanceUntil(s, CareerPhase.Preseason);
                if (y == 4) _jsonLength = JsonLength(s);
            }
            _lines = lines;
            _save = s;
        }

        private static long JsonLength(CareerSave s)
        {
            int len;
            CareerSaveTests.RoundTrip(s, out len);
            return len;
        }

        [Test]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void TenSeasonsKeepEverySchoolAtEightyFiveWithRecruits()
        {
            Run();
            Assert.AreEqual(10, _lines.Count);
            CollegeTestUtil.AssertRostersFull(_save);
            Assert.IsTrue(_lines.All(l => l.FreshmanStars > 1.3f && l.FreshmanStars < 2.3f), "recruits arrive with sensible star ratings");
            Assert.IsTrue(_lines.All(l => l.CollegeTopTier > l.CollegeLowTier + 2.5f), "elite programs stay ahead of the low ones: " + string.Join(", ", _lines.Select(l => (l.CollegeTopTier - l.CollegeLowTier).ToString("0.0")).ToArray()));
            Assert.Less(Math.Abs(_lines.Last().CollegeMean - _lines.First().CollegeMean), 4f, "the college talent level is stable over ten years");
        }

        [Test]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void DraftClassesStayAroundTwoSixtyWithASensibleSpread()
        {
            Run();
            foreach (YearLine l in _lines)
            {
                Assert.That(l.ClassSize, Is.InRange(250, 290), l.Year + " class size");
                Assert.That(l.ClassMean, Is.InRange(68f, 80f), l.Year + " class mean");
                Assert.GreaterOrEqual(l.ClassTop10, 76f, l.Year + " top ten");
                Assert.Less(l.ClassMin, l.ClassMean - 4f, l.Year + " spread");
                Assert.That(l.Juniors, Is.InRange(15, 160), l.Year + " early entrants");
                Assert.AreEqual(224, l.Rookies);
                Assert.Greater(l.Undrafted, 8, l.Year + " undrafted free agents");
            }
        }

        [Test]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void TheNetworkStateStaysBoundedAndSavesStayUsable()
        {
            Run();
            Assert.IsTrue(_lines.All(l => l.Honors > 200 && l.Honors < 4 * 340));
            Assert.IsTrue(_lines.All(l => l.History <= 1380));
            Assert.Less(_jsonLength, 60000000, "the save with a college world after five seasons is " + _jsonLength / 1000000.0 + " MB");
            Console.WriteLine("save size after five seasons: " + Math.Round(_jsonLength / 1000000.0, 1) + " MB");
            Console.WriteLine(string.Join("\n", _lines.Select(l => l.Year + ": class " + l.ClassSize + " (" + l.Juniors + " jr) mean " + l.ClassMean.ToString("0.0") + " top10 " + l.ClassTop10.ToString("0.0") + " min " + l.ClassMin + " | college " + l.CollegeMean.ToString("0.0") + " top " + l.CollegeTopTier.ToString("0.0") + " low " + l.CollegeLowTier.ToString("0.0") + " fr* " + l.FreshmanStars.ToString("0.00")).ToArray()));
        }

        [Test]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void TheCollegeSoakIsDeterministic()
        {
            long a = Mini(2032), b = Mini(2032), c = Mini(2033);
            Assert.AreEqual(a, b, "same seed, same college world and scouting after three seasons");
            Assert.AreNotEqual(a, c);
        }

        private static long Mini(int seed)
        {
            CareerSave s = CollegeTestUtil.Career(seed, 3);
            for (int y = 0; y < 3; y++)
            {
                CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
                CollegeScouting.AttendCombine(s);
                CollegeScouting.ScoutPlayer(s, s.Pro.DraftClass[2].Id, 40);
                CareerFlow.SimToNextSeason(s);
            }
            long h = CollegeTestUtil.Fingerprint(s);
            return h * 31 + CareerTestUtil.Fingerprint(s);
        }
    }
}
