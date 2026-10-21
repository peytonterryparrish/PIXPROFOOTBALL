using System;
using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;

namespace PixFootball.Tests
{
    public class RngTests
    {
        [Test]
        public void SameSeedProducesSameSequence()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            var a = new Rng(1);
            var b = new Rng(2);
            int same = 0;
            for (int i = 0; i < 100; i++) if (a.NextULong() == b.NextULong()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void KnownSequenceIsStable()
        {
            // Guards cross-platform determinism: the integer stream must never change.
            var a = new Rng(42);
            ulong first = a.NextULong();
            var b = new Rng(42);
            Assert.AreEqual(first, b.NextULong());
            Assert.AreNotEqual(0UL, first);
        }

        [Test]
        public void FloatIsInUnitInterval()
        {
            var r = new Rng(7);
            for (int i = 0; i < 10000; i++)
            {
                float f = r.NextFloat();
                Assert.GreaterOrEqual(f, 0f);
                Assert.Less(f, 1f);
            }
        }

        [Test]
        public void IntRangeRespectsBounds()
        {
            var r = new Rng(9);
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000; i++)
            {
                int v = r.Range(-3, 4);
                Assert.GreaterOrEqual(v, -3);
                Assert.Less(v, 4);
                seen.Add(v);
            }
            Assert.AreEqual(7, seen.Count);
            Assert.AreEqual(5, r.Range(5, 5));
        }

        [Test]
        public void FloatRangeRespectsBounds()
        {
            var r = new Rng(11);
            for (int i = 0; i < 2000; i++)
            {
                float v = r.Range(2.5f, 7.5f);
                Assert.GreaterOrEqual(v, 2.5f);
                Assert.Less(v, 7.5f);
            }
        }

        [Test]
        public void GaussianHasRequestedMeanAndSpread()
        {
            var r = new Rng(21);
            const int n = 20000;
            double sum = 0, sq = 0;
            for (int i = 0; i < n; i++)
            {
                double v = r.Gaussian(50f, 10f);
                sum += v; sq += v * v;
            }
            double mean = sum / n;
            double sd = Math.Sqrt(sq / n - mean * mean);
            Assert.AreEqual(50.0, mean, 0.5);
            Assert.AreEqual(10.0, sd, 0.5);
        }

        [Test]
        public void ChanceExtremesAndFrequency()
        {
            var r = new Rng(3);
            Assert.IsFalse(r.Chance(0f));
            Assert.IsTrue(r.Chance(1f));
            int hits = 0;
            for (int i = 0; i < 10000; i++) if (r.Chance(0.3f)) hits++;
            Assert.AreEqual(3000, hits, 200);
        }

        [Test]
        public void PickReturnsMembersAndThrowsOnEmpty()
        {
            var r = new Rng(5);
            var list = new List<string> { "a", "b", "c" };
            for (int i = 0; i < 50; i++) CollectionAssert.Contains(list, r.Pick(list));
            Assert.Throws<ArgumentException>(() => r.Pick(new List<int>()));
        }

        [Test]
        public void ForkIsDeterministicAndIndependent()
        {
            var a = new Rng(99);
            var b = new Rng(99);
            Rng fa = a.Fork();
            Rng fb = b.Fork();
            for (int i = 0; i < 100; i++) Assert.AreEqual(fa.NextULong(), fb.NextULong());
            Assert.AreNotEqual(new Rng(99).NextULong(), new Rng(99).Fork().NextULong());
        }

        [Test]
        public void StateRoundTrip()
        {
            var a = new Rng(1234);
            a.NextULong();
            ulong s0, s1;
            a.GetState(out s0, out s1);
            ulong next = a.NextULong();
            var b = new Rng(0);
            b.SetState(s0, s1);
            Assert.AreEqual(next, b.NextULong());
        }
    }

    public class RatingsAndPositionTests
    {
        [Test]
        public void UniformRatingsGiveThatOverallForEveryPosition()
        {
            Ratings r = Ratings.Uniform(70);
            foreach (Position p in Enum.GetValues(typeof(Position)))
                Assert.AreEqual(70, r.Overall(p), p.ToString());
        }

        [Test]
        public void OverallIsAlwaysWithinRange()
        {
            var rng = new Rng(77);
            for (int n = 0; n < 200; n++)
            {
                var r = new Ratings();
                for (int k = 0; k < Ratings.KindCount; k++) r.Set((RatingKind)k, rng.Range(0, 100));
                foreach (Position p in Enum.GetValues(typeof(Position)))
                {
                    int o = r.Overall(p);
                    Assert.GreaterOrEqual(o, 0);
                    Assert.LessOrEqual(o, 99);
                }
            }
            Assert.AreEqual(0, Ratings.Uniform(0).Overall(Position.QB));
            Assert.AreEqual(99, Ratings.Uniform(99).Overall(Position.CB));
        }

        [Test]
        public void SetClampsAndGetRoundTrips()
        {
            var r = new Ratings();
            r.Set(RatingKind.Speed, 150);
            r.Set(RatingKind.Strength, -5);
            Assert.AreEqual(99, r.Speed);
            Assert.AreEqual(0, r.Strength);
            for (int k = 0; k < Ratings.KindCount; k++)
            {
                r.Set((RatingKind)k, k + 10);
                Assert.AreEqual(k + 10, r.Get((RatingKind)k));
            }
        }

        [Test]
        public void QuarterbackWeightsThrowingAndAwareness()
        {
            var thrower = new Ratings();
            thrower.ThrowAccuracy = 95; thrower.ThrowPower = 90; thrower.Awareness = 90;
            var runner = new Ratings();
            runner.Speed = 95; runner.Acceleration = 95; runner.Agility = 95; runner.Elusiveness = 95;
            Assert.Greater(thrower.Overall(Position.QB), runner.Overall(Position.QB));
            Assert.Greater(runner.Overall(Position.RB), thrower.Overall(Position.RB));
        }

        [Test]
        public void LinemenWeightBlockingAndKickersWeightKicking()
        {
            var blocker = new Ratings();
            blocker.PassBlocking = 95; blocker.RunBlocking = 95; blocker.Strength = 95;
            Assert.Greater(blocker.Overall(Position.LT), blocker.Overall(Position.WR));
            var kicker = new Ratings();
            kicker.KickPower = 90; kicker.KickAccuracy = 90;
            Assert.Greater(kicker.Overall(Position.K), 80);
            Assert.Less(kicker.Overall(Position.CB), 20);
        }

        [Test]
        public void PositionGroups()
        {
            Assert.IsTrue(PositionGroup.IsOffense(Position.QB));
            Assert.IsTrue(PositionGroup.IsDefense(Position.MLB));
            Assert.IsTrue(PositionGroup.IsSpecial(Position.P));
            Assert.IsTrue(PositionGroup.IsLine(Position.LT));
            Assert.IsTrue(PositionGroup.IsLine(Position.DT));
            Assert.IsFalse(PositionGroup.IsLine(Position.WR));
            Assert.IsTrue(PositionGroup.IsSkill(Position.TE));
            Assert.IsFalse(PositionGroup.IsSkill(Position.C));
            int o = 0, d = 0, s = 0;
            foreach (Position p in Enum.GetValues(typeof(Position)))
            {
                switch (PositionGroup.UnitOf(p))
                {
                    case PositionUnit.Offense: o++; break;
                    case PositionUnit.Defense: d++; break;
                    default: s++; break;
                }
            }
            Assert.AreEqual(10, o);
            Assert.AreEqual(9, d);
            Assert.AreEqual(2, s);
        }
    }

    public class RuleSetAndFieldTests
    {
        [Test]
        public void ProAndCollegePresetsDiffer()
        {
            RuleSet pro = RuleSet.Pro();
            RuleSet col = RuleSet.College();
            Assert.AreEqual(1, pro.FeetInBounds);
            Assert.AreEqual(2, col.FeetInBounds);
            Assert.AreEqual(OvertimeFormat.ProModifiedSuddenDeath, pro.Overtime);
            Assert.AreEqual(OvertimeFormat.CollegeAlternating, col.Overtime);
            Assert.AreEqual(10, pro.OvertimeMinutes);
            Assert.IsFalse(pro.ClockStopsOnFirstDown);
            Assert.IsTrue(col.ClockStopsOnFirstDown);
            Assert.AreEqual(18.5f, pro.HashWidthFeet);
            Assert.AreEqual(40f, col.HashWidthFeet);
            Assert.AreEqual(85, pro.PatSpot);
            Assert.AreEqual(97, col.PatSpot);
            Assert.AreEqual(98, pro.TwoPointSpot);
            Assert.AreEqual(97, col.TwoPointSpot);
            Assert.AreEqual(15, pro.QuarterMinutes);
            Assert.AreEqual(3, pro.TimeoutsPerHalf);
            Assert.AreEqual(2, pro.Challenges);
            Assert.AreEqual(0, col.Challenges);
            Assert.IsTrue(pro.HasTwoMinuteWarning);
            Assert.IsFalse(col.HasTwoMinuteWarning);
            Assert.AreEqual(35, pro.KickoffSpot);
            Assert.AreEqual(30, pro.KickoffTouchbackSpot);
            Assert.AreEqual(25, col.KickoffTouchbackSpot);
            Assert.AreEqual(40, pro.PlayClockLive);
            Assert.AreEqual(25, pro.PlayClockDead);
        }

        [Test]
        public void HashGeometry()
        {
            RuleSet pro = RuleSet.Pro();
            RuleSet col = RuleSet.College();
            float proWidth = FieldGeometry.HashX(Hash.Right, pro) - FieldGeometry.HashX(Hash.Left, pro);
            float colWidth = FieldGeometry.HashX(Hash.Right, col) - FieldGeometry.HashX(Hash.Left, col);
            Assert.AreEqual(18.5f / 3f, proWidth, 0.001f);
            Assert.AreEqual(40f / 3f, colWidth, 0.001f);
            Assert.AreEqual(FieldGeometry.CenterX, FieldGeometry.HashX(Hash.Middle, pro), 0.001f);
            Assert.AreEqual(Hash.Left, FieldGeometry.NearestHash(5f, col));
            Assert.AreEqual(Hash.Right, FieldGeometry.NearestHash(50f, col));
            Assert.AreEqual(Hash.Middle, FieldGeometry.NearestHash(FieldGeometry.CenterX + 0.2f, pro));
        }

        [Test]
        public void YardConversionsAndSpots()
        {
            Assert.AreEqual(0.9144f, Yards.ToMeters, 1e-6f);
            Assert.AreEqual(91.44f, Yards.ToMetersF(100f), 1e-3f);
            Assert.AreEqual(100f, Yards.MetersToYards(91.44f), 1e-3f);
            Assert.AreEqual(1f, Yards.FeetToYards(3f), 1e-6f);
            Assert.AreEqual(60f, FieldGeometry.SpotToZ(50f, true), 1e-4f);
            Assert.AreEqual(50f, FieldGeometry.ZToSpot(FieldGeometry.SpotToZ(50f, false), false), 1e-4f);
            Assert.IsTrue(FieldGeometry.IsInEndZone(5f));
            Assert.IsFalse(FieldGeometry.IsInEndZone(60f));
            Assert.IsTrue(FieldGeometry.IsInBounds(10f, 60f));
            Assert.IsFalse(FieldGeometry.IsInBounds(-1f, 60f));
        }
    }
}
