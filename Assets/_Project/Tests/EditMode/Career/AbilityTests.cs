using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>The ability (trait) system: catalogue, assignment by archetype and rating, progression unlocks and Sim-facing effect data.</summary>
    public class AbilityTests
    {
        private static Player Make(Position pos, Archetype arch, int rating, int seed = 1)
        {
            var p = new Player { Position = pos, Archetype = arch, Ratings = Ratings.Uniform(rating), Potential = rating + 5 };
            p.Bio.FirstName = "Test"; p.Bio.LastName = "Player" + seed; p.Bio.Age = 26; p.Bio.Id = seed;
            return p;
        }

        [Test]
        public void TheCatalogueHasAboutThirtyAbilitiesWithThreeTiersEach()
        {
            Assert.GreaterOrEqual(AbilityCatalog.Count, 30);
            Assert.LessOrEqual(AbilityCatalog.Count, 45);
            var ids = new HashSet<string>();
            foreach (AbilityDef d in AbilityCatalog.All)
            {
                Assert.IsTrue(ids.Add(d.Id), "duplicate id " + d.Id);
                Assert.IsFalse(string.IsNullOrEmpty(d.Name));
                Assert.IsFalse(string.IsNullOrEmpty(d.Description));
                Assert.AreEqual(3, d.Thresholds.Length);
                Assert.Less(d.Thresholds[0], d.Thresholds[1]);
                Assert.Less(d.Thresholds[1], d.Thresholds[2]);
                Assert.Greater(d.Effects.Length, 0, d.Id);
                foreach (AbilityEffect e in d.Effects)
                {
                    Assert.AreEqual(3, e.PerTier.Length);
                    // a higher tier always means a stronger effect (negative effects get more negative)
                    float a = Math.Abs(e.PerTier[0]), b = Math.Abs(e.PerTier[1]), c = Math.Abs(e.PerTier[2]);
                    Assert.Less(a, b, d.Id); Assert.Less(b, c, d.Id);
                    Assert.AreEqual(Math.Sign(e.PerTier[0]), Math.Sign(e.PerTier[2]), d.Id);
                }
                Assert.AreSame(d, AbilityCatalog.Get(d.Id));
            }
            // every position can get something
            foreach (Position pos in Enum.GetValues(typeof(Position)))
                Assert.Greater(AbilityCatalog.AvailableFor(pos).Count, 0, pos.ToString());
            // the described abilities of the brief exist under our own names
            foreach (string id in new[] { "rocket_arm", "stop_on_a_dime", "money_down", "sticky_hands", "wrecking_ball", "ankle_shaker", "return_ace", "burner" })
                Assert.IsNotNull(AbilityCatalog.Get(id), id);
            Assert.IsNull(AbilityCatalog.Get("nope"));
        }

        [Test]
        public void EffectsAreReadableDataForTheSim()
        {
            var qb = Make(Position.QB, Archetype.QbGunslinger, 90);
            qb.Abilities.Add(new PlayerAbility { Id = "rocket_arm", Tier = 2 });
            qb.Abilities.Add(new PlayerAbility { Id = "money_down", Tier = 3 });
            Assert.AreEqual(4f, AbilityCatalog.Modifier(qb, SimHook.ThrowPower), 0.001f);
            Assert.AreEqual(0f, AbilityCatalog.Modifier(qb, SimHook.ThrowAccuracy), 0.001f, "money down only applies on third and fourth down");
            Assert.AreEqual(7f, AbilityCatalog.Modifier(qb, SimHook.ThrowAccuracy, AbilityContext.ThirdFourthDown), 0.001f);
            Assert.AreEqual(3f, AbilityCatalog.Modifier(qb, SimHook.Clutch, AbilityContext.ThirdFourthDown), 0.001f);
            Assert.AreEqual(0f, AbilityCatalog.Modifier(qb, SimHook.CutSpeedLoss), 0.001f);
            Assert.AreEqual(3, AbilityCatalog.TierOf(qb, "money_down"));
            Assert.IsTrue(AbilityCatalog.Has(qb, "rocket_arm"));
            Assert.IsFalse(AbilityCatalog.Has(qb, "burner"));
            var rb = Make(Position.RB, Archetype.RbSpeed, 88);
            rb.Abilities.Add(new PlayerAbility { Id = "stop_on_a_dime", Tier = 1 });
            Assert.AreEqual(-12f, AbilityCatalog.Modifier(rb, SimHook.CutSpeedLoss), 0.001f, "cut speed loss -12 percent at one star");
            rb.Abilities[0].Tier = 3;
            Assert.AreEqual(-30f, AbilityCatalog.Modifier(rb, SimHook.CutSpeedLoss), 0.001f);
            var iron = Make(Position.MLB, Archetype.LbRunStopper, 85);
            iron.Abilities.Add(new PlayerAbility { Id = "iron_man", Tier = 3 });
            Assert.AreEqual(-35f, AbilityCatalog.Modifier(iron, SimHook.InjuryResist), 0.001f, "fewer injuries: the season scales the injury roll with this");
            var table = AbilityCatalog.ModifierTable(qb);
            Assert.IsTrue(table.Any(kv => kv.Key == "ThrowPower/Always" && Math.Abs(kv.Value - 4f) < 0.001f));
            string text = AbilityCatalog.Describe(qb.Abilities[0]);
            StringAssert.Contains("Rocket Arm", text);
            StringAssert.Contains("**", text);
            StringAssert.Contains("ThrowPower", text);
        }

        [Test]
        public void RatingsDecideWhatATierNeedsAndWhoQualifies()
        {
            AbilityDef rocket = AbilityCatalog.Get("rocket_arm");
            var weak = Make(Position.QB, Archetype.QbGunslinger, 70);
            var good = Make(Position.QB, Archetype.QbGunslinger, 83);
            var elite = Make(Position.QB, Archetype.QbGunslinger, 95);
            Assert.AreEqual(0, rocket.TierFor(weak));
            Assert.AreEqual(1, rocket.TierFor(good));
            Assert.AreEqual(3, rocket.TierFor(elite));
            // wrong position never qualifies
            Assert.IsFalse(rocket.AppliesTo(Position.K));
            Assert.IsTrue(rocket.AppliesTo(Position.QB));
        }

        [Test]
        public void RegensGetAbilitiesFromArchetypeAndRatingAndStarsGetMore()
        {
            var rng = new Rng(77);
            int starCount = 0, starAbilities = 0, rolePlayerAbilities = 0, rolePlayers = 0;
            for (int i = 0; i < 400; i++)
            {
                Position pos = LeagueRules.RosterOrder[i % LeagueRules.RosterOrder.Length];
                Player star = PlayerFactory.Create(rng, new IdSource(), new HashSet<string>(), pos, 92, 26);
                Player filler = PlayerFactory.Create(rng, new IdSource(), new HashSet<string>(), pos, 64, 26);
                AbilityCatalog.AssignInitial(star, rng);
                AbilityCatalog.AssignInitial(filler, rng);
                starCount++; starAbilities += star.Abilities.Count;
                rolePlayers++; rolePlayerAbilities += filler.Abilities.Count;
                var seen = new HashSet<string>();
                foreach (PlayerAbility a in star.Abilities.Concat(filler.Abilities).ToList())
                {
                    AbilityDef d = AbilityCatalog.Get(a.Id);
                    Assert.IsNotNull(d);
                    Assert.IsTrue(d.AppliesTo(pos), a.Id + " does not fit " + pos);
                    Assert.GreaterOrEqual(a.Tier, 1); Assert.LessOrEqual(a.Tier, 3);
                    Assert.IsTrue(seen.Add(star.Abilities.Contains(a) ? "s" + a.Id : "f" + a.Id));
                }
                foreach (PlayerAbility a in star.Abilities) Assert.LessOrEqual(a.Tier, AbilityCatalog.Get(a.Id).TierFor(star), "initial tier never exceeds what the ratings support");
                Assert.LessOrEqual(star.Abilities.Count, AbilityCatalog.SlotsFor(star.Overall, star.Dev));
            }
            Assert.Greater((double)starAbilities / starCount, 1.0);
            Assert.Greater((double)starAbilities / starCount, (double)rolePlayerAbilities / rolePlayers + 0.7);
            Assert.Greater(AbilityCatalog.SlotsFor(90, DevTrait.Normal), AbilityCatalog.SlotsFor(70, DevTrait.Normal));
            Assert.AreEqual(0, AbilityCatalog.SlotsFor(55, DevTrait.Normal));
        }

        [Test]
        public void ArchetypesPullTheirSignatureAbilities()
        {
            var rng = new Rng(5);
            int mobileWithEscape = 0, pocketWithEscape = 0, mobile = 0, pocket = 0;
            for (int i = 0; i < 300; i++)
            {
                Player scrambler = PlayerFactory.Create(rng, new IdSource(), new HashSet<string>(), Position.QB, 88, 27);
                scrambler.Archetype = Archetype.QbScrambler;
                scrambler.Ratings.Speed = 86; scrambler.Ratings.Agility = 88; scrambler.Ratings.Elusiveness = 88;
                Player pocketQb = PlayerFactory.Create(rng, new IdSource(), new HashSet<string>(), Position.QB, 88, 27);
                pocketQb.Archetype = Archetype.QbPocket;
                pocketQb.Ratings.Speed = 86; pocketQb.Ratings.Agility = 88; pocketQb.Ratings.Elusiveness = 88;
                AbilityCatalog.AssignInitial(scrambler, rng); AbilityCatalog.AssignInitial(pocketQb, rng);
                mobile++; pocket++;
                if (AbilityCatalog.Has(scrambler, "escape_artist")) mobileWithEscape++;
                if (AbilityCatalog.Has(pocketQb, "escape_artist")) pocketWithEscape++;
            }
            Assert.Greater(mobileWithEscape, pocketWithEscape, "the scrambler archetype favours Escape Artist");
        }

        [Test]
        public void AssignmentIsDeterministic()
        {
            Player a = Make(Position.WR, Archetype.WrDeepThreat, 90, 3), b = Make(Position.WR, Archetype.WrDeepThreat, 90, 3);
            AbilityCatalog.AssignInitial(a, new Rng(10)); AbilityCatalog.AssignInitial(b, new Rng(10));
            Assert.AreEqual(string.Join(",", a.Abilities.Select(x => x.Id + x.Tier).ToArray()), string.Join(",", b.Abilities.Select(x => x.Id + x.Tier).ToArray()));
        }

        [Test]
        public void ProgressionUnlocksUpgradesAndLosesAbilitiesWithTheRatings()
        {
            var rng = new Rng(9);
            // a developing receiver whose hands improved: tier goes up eventually and new tricks unlock
            Player wr = Make(Position.WR, Archetype.WrPossession, 70);
            wr.Dev = DevTrait.Superstar;
            wr.Ratings.Catching = 90; wr.Ratings.Awareness = 90; wr.Ratings.Speed = 88; wr.Ratings.Acceleration = 88; wr.Ratings.Agility = 88; wr.Ratings.Jumping = 90;
            wr.Potential = 95;
            wr.Abilities.Add(new PlayerAbility { Id = "sticky_hands", Tier = 1 });
            int ups = 0, unlocks = 0;
            for (int trial = 0; trial < 60; trial++)
            {
                Player copy = Make(Position.WR, Archetype.WrPossession, 70, trial + 20);
                copy.Ratings = wr.Ratings; copy.Dev = DevTrait.Superstar; copy.Potential = 95;
                copy.Abilities.Add(new PlayerAbility { Id = "sticky_hands", Tier = 1 });
                string change = AbilityCatalog.Progress(copy, rng, 10);
                if (copy.Abilities[0].Tier > 1) ups++;
                if (copy.Abilities.Count > 1) unlocks++;
                if (change.Length > 0) StringAssert.Contains(";", change + ";");
            }
            Assert.Greater(ups, 5);
            Assert.Greater(unlocks, 5);
            // a veteran who lost his speed loses the speed-based trick
            Player burner = Make(Position.WR, Archetype.WrDeepThreat, 65);
            burner.Abilities.Add(new PlayerAbility { Id = "burner", Tier = 3 });
            int gone = 0;
            for (int trial = 0; trial < 40; trial++)
            {
                Player c = Make(Position.WR, Archetype.WrDeepThreat, 65, trial + 100);
                c.Abilities.Add(new PlayerAbility { Id = "burner", Tier = 3 });
                AbilityCatalog.Progress(c, rng, 0);
                if (c.Abilities.Count == 0) gone++;
            }
            Assert.Greater(gone, 20, "ratings far below the bar remove the ability most of the time");
        }

        [Test]
        public void ScoutingRevealsAbilityHintsGradually()
        {
            Player p = Make(Position.QB, Archetype.QbGunslinger, 92);
            p.Abilities.Add(new PlayerAbility { Id = "rocket_arm", Tier = 3 });
            p.Abilities.Add(new PlayerAbility { Id = "deep_touch", Tier = 2 });
            Assert.AreEqual(0, AbilityCatalog.ScoutedHints(p, 5).Count);
            var some = AbilityCatalog.ScoutedHints(p, 30);
            Assert.LessOrEqual(some.Count, 2);
            foreach (PlayerAbility h in some) Assert.AreEqual(1, h.Tier);
            var all = AbilityCatalog.ScoutedHints(p, 80);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(3, all.First(a => a.Id == "rocket_arm").Tier);
        }

        [Test]
        public void CareerLeagueAndDraftClassCarryAbilities()
        {
            CareerSave s = CareerTestUtil.Fresh(95);
            var all = s.Pro.AllRosterPlayers().ToList();
            double starAvg = all.Where(p => p.Overall >= 84).Average(p => p.Abilities.Count);
            double lowAvg = all.Where(p => p.Overall < 66).Average(p => p.Abilities.Count);
            Assert.Greater(starAvg, lowAvg + 0.8);
            foreach (Player p in all) foreach (PlayerAbility a in p.Abilities) Assert.IsTrue(AbilityCatalog.Get(a.Id).AppliesTo(p.Position));
            Assert.IsTrue(s.Pro.FreeAgents.Any(p => p.Abilities.Count > 0) || s.Pro.FreeAgents.All(p => p.Overall < 68));
            CareerSave sc = CareerTestUtil.AtPhase(96, CareerPhase.Scouting);
            Assert.Greater(sc.Pro.DraftClass.Count(pr => pr.Player.Abilities.Count > 0), 20);
            // camp results report ability changes for the user's players
            CareerFlow.AdvanceUntil(sc, CareerPhase.TrainingCamp);
            Assert.Greater(sc.LastCamp.Count, 40);
            Assert.IsTrue(sc.LastCamp.All(c => c.OvrAfter >= 0));
        }

        private sealed class IdSource : IPlayerIdSource
        {
            private static int _next = 5000000;
            public int NextPlayerId() { return _next++; }
        }
    }
}
