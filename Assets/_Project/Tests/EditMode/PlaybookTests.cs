using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    public class PlaybookTests
    {
        private static Playbook _book;
        private static Playbook Book { get { return _book ?? (_book = DefaultPlaybooks.Create()); } }

        private static string Concept(string name)
        {
            if (name.EndsWith(" Right")) return name.Substring(0, name.Length - 6);
            if (name.EndsWith(" Left")) return name.Substring(0, name.Length - 5);
            return name;
        }

        [Test]
        public void LeagueAssemblyCompiles()
        {
            Assert.IsFalse(string.IsNullOrEmpty(LeagueAssembly.Version));
        }

        [Test]
        public void PlaybookValidatesWithNoErrors()
        {
            var errors = new List<string>();
            bool ok = Book.Validate(errors);
            Assert.IsTrue(ok, string.Join("\n", errors.Take(20).ToArray()));
        }

        [Test]
        public void EveryPlayHasExactlyElevenValidAssignments()
        {
            foreach (Play p in Book.AllPlays)
            {
                Assert.AreEqual(11, p.Assignments.Length, p.ToString());
                for (int i = 0; i < 11; i++)
                {
                    Assert.IsNotNull(p.Assignments[i], p + " slot " + i);
                    Assert.AreEqual(i, p.Assignments[i].Slot, p + " slot " + i);
                    var cov = p.Assignments[i] as CoverageAssignment;
                    if (cov != null && cov.IsMan) Assert.That(cov.TargetSlot, Is.InRange(0, 10), p.ToString());
                }
            }
        }

        [Test]
        public void EveryFormationHasElevenSlots()
        {
            Assert.GreaterOrEqual(Book.Formations.Count, 19);
            foreach (Formation f in Book.Formations)
                Assert.AreEqual(11, f.Slots.Length, f.Name);
        }

        [Test]
        public void OffensiveFormationsAreLegal()
        {
            string[] expected = { "Singleback", "I-Form", "Shotgun", "Pistol", "Goal Line", "Empty", "Trips" };
            foreach (string n in expected)
            {
                Formation f = Book.FindFormation(n);
                Assert.IsNotNull(f, n);
                Assert.AreEqual(PlaySide.Offense, f.Side);
                Assert.AreEqual(1, f.Count(Position.QB), n);
                Assert.AreEqual(1, f.Count(Position.C), n);
                Assert.AreEqual(1, f.Count(Position.LT) + f.Count(Position.RT) - 1, n);
                Assert.AreEqual(1, f.Count(Position.LG), n);
                Assert.AreEqual(1, f.Count(Position.RG), n);
                foreach (SlotSpec s in f.Slots) Assert.IsTrue(PositionGroup.IsOffense(s.Position), n);
                int eligible = f.Slots.Count(s => PositionGroup.IsEligibleReceiver(s.Position));
                Assert.AreEqual(5, eligible, n);
                // seven men on the line of scrimmage at most is enough; at least five linemen are on it
                Assert.GreaterOrEqual(f.Slots.Count(s => s.Y >= -0.5f), 7, n);
            }
        }

        [Test]
        public void DefensiveFormationsAreLegal()
        {
            string[] expected = { "4-3 Base", "3-4 Base", "Nickel", "Dime", "Goal Line Defense" };
            foreach (string n in expected)
            {
                Formation f = Book.FindFormation(n);
                Assert.IsNotNull(f, n);
                Assert.AreEqual(PlaySide.Defense, f.Side);
                foreach (SlotSpec s in f.Slots) Assert.IsTrue(PositionGroup.IsDefense(s.Position), n);
            }
            Assert.AreEqual(4, Book.FindFormation("4-3 Base").Slots.Count(s => PositionGroup.IsDefensiveLine(s.Position)));
            Assert.AreEqual(3, Book.FindFormation("3-4 Base").Slots.Count(s => PositionGroup.IsDefensiveLine(s.Position)));
            Assert.AreEqual(3, Book.FindFormation("Nickel").Count(Position.CB));
            Assert.AreEqual(4, Book.FindFormation("Dime").Count(Position.CB));
        }

        [Test]
        public void SpecialTeamsUnitsExist()
        {
            string[] units = { "Kickoff", "Onside", "KickoffReturn", "Punt", "PuntReturn", "FieldGoal", "FieldGoalBlock" };
            foreach (string u in units)
            {
                Formation f = Book.Formations.FirstOrDefault(x => x.Unit == u);
                Assert.IsNotNull(f, u);
                Assert.AreEqual(PlaySide.Special, f.Side);
                Assert.IsTrue(Book.PlaysFor(f.Name).Count > 0, u);
            }
            Assert.IsNotNull(Book.FindPlay("Kickoff Deep Middle"));
            Assert.IsNotNull(Book.FindPlay("Onside Kick Short"));
            Assert.IsNotNull(Book.FindPlay("Punt Standard"));
            Assert.IsNotNull(Book.FindPlay("Field Goal"));
            Assert.IsNotNull(Book.FindPlay("Extra Point"));
            Assert.IsNotNull(Book.FindPlay("Punt Return Set"));
            Assert.IsNotNull(Book.FindPlay("Field Goal Block Middle"));
            Assert.IsNotNull(Book.FindPlay("Kick Return Middle Wedge"));
        }

        [Test]
        public void OffenseHasAtLeastFortyConcepts()
        {
            List<Play> plays = Book.PlaysBySide(PlaySide.Offense);
            int concepts = plays.Select(p => Concept(p.Name)).Distinct().Count();
            Assert.GreaterOrEqual(concepts, 40, "distinct offensive concepts");
            Assert.GreaterOrEqual(plays.Count, 40);
            string[] required = {
                "Inside Zone Right", "Outside Zone Left", "Power Right", "Counter Right", "Dive Right", "Toss Sweep Right",
                "Draw Right", "HB Screen", "Slants", "Four Verticals", "Mesh", "Smash", "Flood", "Curl-Flat", "PA Boot",
                "Y-Cross", "Stick", "Drive", "Hail Mary", "QB Sneak", "Kneel", "Spike" };
            foreach (string r in required) Assert.IsNotNull(Book.FindPlay(r), r);
        }

        [Test]
        public void DefenseHasAtLeastThirtyConceptsAcrossAllCoverages()
        {
            List<Play> plays = Book.PlaysBySide(PlaySide.Defense);
            Assert.GreaterOrEqual(plays.Select(p => p.Name).Distinct().Count(), 30);
            foreach (CoverageScheme s in new[] { CoverageScheme.Cover0, CoverageScheme.Cover1, CoverageScheme.Cover2,
                CoverageScheme.Cover3, CoverageScheme.Cover4, CoverageScheme.Cover6 })
                Assert.IsTrue(plays.Any(p => p.Scheme == s), s.ToString());
            Assert.IsTrue(plays.Any(p => p.PassRushers >= 6));
            Assert.IsTrue(plays.Any(p => p.Assignments.Any(a => a is SpyAssignment)));
            Assert.IsTrue(plays.Any(p => p.Assignments.Any(a => a is BlitzAssignment)));
            Assert.IsTrue(plays.Any(p => p.Assignments.Any(a => a is RushAssignment ra && ra.Stunt != StuntKind.None)));
            foreach (Formation f in Book.FormationsBySide(PlaySide.Defense))
                Assert.IsTrue(Book.PlaysFor(f.Name).Count >= 3, f.Name);
        }

        [Test]
        public void DefensivePlaysCoverEveryDefenderSensibly()
        {
            foreach (Play p in Book.PlaysBySide(PlaySide.Defense))
            {
                int rushers = p.PassRushers;
                Assert.That(rushers, Is.InRange(3, 8), p.ToString());
                int cover = p.Assignments.Count(a => a is CoverageAssignment);
                Assert.GreaterOrEqual(cover, 3, p.ToString());
                // man targets are unique
                var targets = p.Assignments.OfType<CoverageAssignment>().Where(c => c.IsMan).Select(c => c.TargetSlot).ToList();
                Assert.AreEqual(targets.Count, targets.Distinct().Count(), p.ToString());
            }
        }

        [Test]
        public void OffensivePlayShapes()
        {
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense))
            {
                Assert.AreEqual(1, p.Assignments.Count(a => a is QbAssignment), p.ToString());
                Assert.IsInstanceOf<QbAssignment>(p.Assignments[0], p.ToString());
                for (int i = 2; i <= 6; i++) Assert.IsInstanceOf<BlockAssignment>(p.Assignments[i], p + " OL " + i);
                if (p.IsRun && p.Category != PlayCategory.QbRun)
                    Assert.IsTrue(p.Assignments.Any(a => a is CarryAssignment), p.ToString());
                if (p.IsPass)
                {
                    var qb = (QbAssignment)p.Assignments[0];
                    Assert.GreaterOrEqual(qb.ReadProgression.Count, 1, p.ToString());
                    Assert.IsTrue(p.Assignments.Any(a => a is RouteAssignment), p.ToString());
                }
            }
        }

        [Test]
        public void RoutesHaveIncreasingTimesAndStartNearTheReceiver()
        {
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense))
            {
                Formation f = Book.FindFormation(p.FormationName);
                foreach (RouteAssignment r in p.Assignments.OfType<RouteAssignment>())
                {
                    float last = 0f;
                    foreach (RouteWaypoint w in r.Waypoints)
                    {
                        Assert.Greater(w.Time, last, p + " slot " + r.Slot);
                        last = w.Time;
                    }
                    Assert.IsTrue(Math.Abs(r.Waypoints[0].Point.X - f.Slots[r.Slot].X) < 25f, p.ToString());
                }
            }
        }

        [Test]
        public void ReadProgressionPointsAtRouteRunners()
        {
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense))
            {
                var qb = (QbAssignment)p.Assignments[0];
                foreach (int slot in qb.ReadProgression)
                    Assert.IsInstanceOf<RouteAssignment>(p.Assignments[slot], p.ToString());
            }
        }

        [Test]
        public void MirroredRunsMirrorTheCarryPath()
        {
            Play right = Book.FindPlay("Singleback.Inside Zone Right".ToLowerInvariant().Replace(' ', '_').Replace('-', '_'));
            Play left = Book.FindPlay("Singleback.Inside Zone Left".ToLowerInvariant().Replace(' ', '_').Replace('-', '_'));
            Assert.IsNotNull(right);
            Assert.IsNotNull(left);
            var cr = right.Assignments[1] as CarryAssignment;
            var cl = left.Assignments[1] as CarryAssignment;
            Assert.IsNotNull(cr);
            Assert.IsNotNull(cl);
            Assert.AreEqual(cr.Path.Count, cl.Path.Count);
            for (int i = 0; i < cr.Path.Count; i++) Assert.AreEqual(cr.Path[i].X, -cl.Path[i].X, 1e-4f);
        }

        [Test]
        public void PlayIdsAreUnique()
        {
            var ids = new HashSet<string>();
            foreach (Play p in Book.AllPlays) Assert.IsTrue(ids.Add(p.Id), p.Id);
        }

        [Test]
        public void PlaySetsGroupPlaysAndQueriesWork()
        {
            Assert.Greater(Book.Sets.Count, 10);
            Assert.AreEqual(Book.PlayCount, Book.AllPlays.Count());
            Assert.Greater(Book.PlaysByCategory(PlayCategory.Blitz).Count, 0);
            Assert.AreEqual(PlaySide.Offense, Book.PlaysFor("Shotgun").First().Side);
            Assert.IsNull(Book.FindPlay("does not exist"));
        }

        [Test]
        public void ValidatorCatchesBrokenPlays()
        {
            var pb = DefaultPlaybooks.Offense();
            Play p = pb.AllPlays.First();
            p.Assignments[3] = null;
            var errors = new List<string>();
            Assert.IsFalse(pb.Validate(errors));
            Assert.IsTrue(errors.Count > 0);

            var pb2 = DefaultPlaybooks.Defense();
            Play d = pb2.AllPlays.First(x => x.Assignments.Any(a => a is CoverageAssignment c && c.IsMan));
            ((CoverageAssignment)d.Assignments.First(a => a is CoverageAssignment c && c.IsMan)).TargetSlot = 11;
            errors.Clear();
            Assert.IsFalse(pb2.Validate(errors));
        }
    }

    public class SimulationFuzzTests
    {
        private static PlayResult RandomPlay(GameState g, Rng r)
        {
            switch (g.Phase)
            {
                case GamePhase.Kickoff:
                    if (r.Chance(0.08f)) return PlayResult.Onside(12, r.Chance(0.2f));
                    return PlayResult.Kickoff(r.Range(55, 75), r.Range(5, 35), false, r.Chance(0.3f));
                case GamePhase.PointAfter:
                    return g.RequiresTwoPoint ? PlayResult.TwoPointTry(r.Chance(0.45f)) : (r.Chance(0.15f) ? PlayResult.TwoPointTry(r.Chance(0.45f)) : PlayResult.Extra(r.Chance(0.95f)));
                default:
                    if (g.Down == 4)
                    {
                        if (g.BallSpot >= 60) return PlayResult.FieldGoal(r.Chance(0.8f));
                        if (g.BallSpot < 40 || r.Chance(0.9f)) return PlayResult.Punt(r.Range(35, 60), r.Range(0, 15), false, false, r.Chance(0.1f));
                    }
                    switch (r.Range(0, 9))
                    {
                        case 0: return PlayResult.Incomplete();
                        case 1: return PlayResult.Sack(r.Range(3, 10));
                        case 2: return PlayResult.Intercept(r.Range(5, 35), r.Range(0, 25));
                        case 3: return PlayResult.FumbleLost(r.Range(-3, 8), r.Range(0, 15));
                        case 4: return PlayResult.Pass(r.Range(0, 40), 6f, r.Chance(0.15f));
                        case 5: return PlayResult.KneelDown();
                        default: return PlayResult.Run(r.Range(-4, 15), 5f, r.Chance(0.1f));
                    }
            }
        }

        private static void RunGame(RuleSet rules, int seed)
        {
            var rng = new Rng(seed);
            var g = new GameState(rules);
            int plays = 0;
            while (g.Phase != GamePhase.Final && plays < 20000)
            {
                PlayResult p = RandomPlay(g, rng);
                if (rng.Chance(0.05f) && g.Phase == GamePhase.Scrimmage && p.Type != PlayType.FieldGoal && p.Type != PlayType.Punt)
                    p.WithPenalty(new PenaltyCall(rng.Chance(0.5f) ? PenaltyType.DefensiveHolding : PenaltyType.OffensiveHolding,
                        rng.Chance(0.5f) ? g.Possession : g.Possession.Opponent()));
                g.Apply(p);
                plays++;
                Assert.GreaterOrEqual(g.BallSpot, 0, "seed " + seed);
                Assert.LessOrEqual(g.BallSpot, 100, "seed " + seed);
                if (g.Phase == GamePhase.Scrimmage)
                {
                    Assert.That(g.Down, Is.InRange(1, 4), "seed " + seed);
                    Assert.GreaterOrEqual(g.Distance, 1, "seed " + seed);
                    Assert.GreaterOrEqual(g.ClockSeconds, 0f);
                }
                Assert.GreaterOrEqual(g.HomeScore, 0);
                Assert.GreaterOrEqual(g.AwayScore, 0);
            }
            Assert.AreEqual(GamePhase.Final, g.Phase, "game should finish (seed " + seed + ")");
        }

        [Test]
        public void ManyProGamesAlwaysFinishWithSaneState()
        {
            for (int s = 1; s <= 40; s++) RunGame(RuleSet.Pro(), s);
        }

        [Test]
        public void ManyCollegeGamesAlwaysFinishWithSaneState()
        {
            for (int s = 1; s <= 40; s++) RunGame(RuleSet.College(), 1000 + s);
        }

        [Test]
        public void SameSeedSameGame()
        {
            var a = new GameState(RuleSet.Pro());
            var b = new GameState(RuleSet.Pro());
            var ra = new Rng(5);
            var rb = new Rng(5);
            for (int i = 0; i < 300 && !a.IsFinal; i++)
            {
                a.Apply(RandomPlay(a, ra));
                b.Apply(RandomPlay(b, rb));
            }
            Assert.AreEqual(a.HomeScore, b.HomeScore);
            Assert.AreEqual(a.AwayScore, b.AwayScore);
            Assert.AreEqual(a.BallSpot, b.BallSpot);
            Assert.AreEqual(a.ClockSeconds, b.ClockSeconds);
            Assert.AreEqual(a.Quarter, b.Quarter);
        }
    }
}
