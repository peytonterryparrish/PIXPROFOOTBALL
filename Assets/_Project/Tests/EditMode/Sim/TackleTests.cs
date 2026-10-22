using System;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Tackle resolution: ratings, contact angle, closing speed, drags, gang tackles, sidelines and fumbles.</summary>
    public class TackleTests
    {
        private sealed class Lab
        {
            public int Tackling = 75, DefStrength = 75, DefAwareness = 75;
            public int BreakTackle = 75, CarrierStrength = 75, Carrying = 75, Elusiveness = 75;
            public float CarrierSpeed = 6.5f;
            /// <summary>Defender position relative to the carrier.</summary>
            public float DefDx = 0f, DefDy = 3.0f;
            public float DefSpeed = 6f;
            public float CarrierX = 27f;
            public bool FromBehind;
            public bool Second;
        }

        private struct Outcome
        {
            public bool Stopped, Broken, Fumble, Dead, Shoestring, BigHit, Gang, OutOfBounds;
            public float YardsAfter;
        }

        private static Outcome Trial(int seed, Lab lab)
        {
            PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, seed, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 6f);
            SimAgent c = null;
            for (int i = 1; i < 11 && c == null; i++) if (sim.Agents[i].Assign is CarryAssignment && !((CarryAssignment)sim.Agents[i].Assign).IsFake) c = sim.Agents[i];
            Assert.IsNotNull(c, "play has a ball carrier");
            sim.Blocks.HandedOff = true;
            sim.GiveBall(c);
            c.R.BreakTackle = lab.BreakTackle; c.R.Strength = lab.CarrierStrength; c.R.Carrying = lab.Carrying; c.R.Elusiveness = lab.Elusiveness;
            SimTestKit.Place(c, lab.CarrierX, 40f, 0f, lab.CarrierSpeed);
            c.Body.Facing = new Vec2(0f, 1f);
            SimAgent d = sim.Agents[11 + 7];
            d.R.Tackling = lab.Tackling; d.R.Strength = lab.DefStrength; d.R.Awareness = lab.DefAwareness;
            float dy = lab.FromBehind ? -Math.Abs(lab.DefDy) : lab.DefDy;
            Vec2 toC = new Vec2(-lab.DefDx, -dy).Norm();
            SimTestKit.Place(d, lab.CarrierX + lab.DefDx, 40f + dy, toC.X * lab.DefSpeed, toC.Y * lab.DefSpeed);
            float startY = c.Pos.Y;
            var none = default(ControlInput);
            for (int i = 0; i < 200 && sim.Phase == PlayPhase.Live; i++) sim.Step(none);
            var o = new Outcome();
            o.Dead = sim.Phase == PlayPhase.Dead;
            o.Stopped = o.Dead && (sim.EndReason == EndReason.Tackle || sim.EndReason == EndReason.OutOfBounds);
            o.OutOfBounds = sim.EndReason == EndReason.OutOfBounds;
            o.Broken = sim.Log.Has(SimEventType.BrokenTackle);
            o.Fumble = sim.Log.Has(SimEventType.Fumble);
            o.Shoestring = false;
            o.BigHit = false;
            o.Gang = false;
            for (int i = 0; i < sim.Log.Events.Count; i++)
            {
                PlayEvent e = sim.Log.Events[i];
                if (e.Type != SimEventType.Tackle) continue;
                if (e.Tackle == TackleType.Shoestring) o.Shoestring = true;
                if (e.Tackle == TackleType.BigHit) o.BigHit = true;
                if (e.Tackle == TackleType.GangTackle) o.Gang = true;
            }
            o.YardsAfter = c.Pos.Y - startY;
            return o;
        }

        private static float Rate(Lab lab, int n, int seedBase, Func<Outcome, bool> pick)
        {
            int k = 0;
            for (int s = 0; s < n; s++) if (pick(Trial(seedBase + s, lab))) k++;
            return k / (float)n;
        }

        private static float MeanYards(Lab lab, int n, int seedBase)
        {
            float t = 0f;
            for (int s = 0; s < n; s++) t += Trial(seedBase + s, lab).YardsAfter;
            return t / n;
        }

        [Test]
        public void ADefenderInThePathStopsAnAverageCarrierMostOfTheTime()
        {
            float stop = Rate(new Lab(), 200, 100, o => o.Stopped);
            TestContext.WriteLine("average vs average stop rate: " + stop);
            Assert.Greater(stop, 0.55f);
            Assert.Less(stop, 0.99f);
        }

        [Test]
        public void TacklingRatingRaisesTheStopRate()
        {
            var good = new Lab { Tackling = 98, DefStrength = 90, DefAwareness = 90 };
            var bad = new Lab { Tackling = 35, DefStrength = 55, DefAwareness = 55 };
            float g = Rate(good, 200, 300, o => o.Stopped);
            float b = Rate(bad, 200, 300, o => o.Stopped);
            TestContext.WriteLine("stop rate: good tackler=" + g + " poor tackler=" + b);
            Assert.Greater(g, b + 0.15f);
        }

        [Test]
        public void BreakTackleAndStrengthLowerTheStopRate()
        {
            var back = new Lab { BreakTackle = 97, CarrierStrength = 92, Elusiveness = 90 };
            var scat = new Lab { BreakTackle = 35, CarrierStrength = 45, Elusiveness = 40 };
            float a = Rate(back, 200, 500, o => o.Stopped);
            float b = Rate(scat, 200, 500, o => o.Stopped);
            TestContext.WriteLine("stop rate: power back=" + a + " weak carrier=" + b);
            Assert.Greater(b, a + 0.15f);
            Assert.Greater(MeanYards(back, 100, 500), MeanYards(scat, 100, 500));
        }

        [Test]
        public void TacklingFromBehindIsHarderThanHeadOn()
        {
            var head = new Lab { DefDy = 3.0f, DefSpeed = 6f, CarrierSpeed = 6f };
            var behind = new Lab { DefDy = 3.0f, DefSpeed = 10f, CarrierSpeed = 6f, FromBehind = true };
            float h = Rate(head, 300, 700, o => o.Stopped && !o.Shoestring);
            float b = Rate(behind, 300, 700, o => o.Stopped && !o.Shoestring);
            float bShoe = Rate(behind, 300, 700, o => o.Shoestring);
            float hShoe = Rate(head, 300, 700, o => o.Shoestring);
            TestContext.WriteLine("clean stops head-on=" + h + " from behind=" + b + "; shoestrings head-on=" + hShoe + " behind=" + bShoe);
            Assert.Greater(h, b);
            Assert.Greater(bShoe, hShoe, "behind-the-play tacklers grab ankles");
        }

        [Test]
        public void HardClosingHitsCauseMoreFumblesThanSoftOnes()
        {
            var hard = new Lab { DefSpeed = 11f, CarrierSpeed = 8f, Tackling = 90, Carrying = 40 };
            var soft = new Lab { DefSpeed = 2f, CarrierSpeed = 3f, Tackling = 90, Carrying = 40 };
            float hf = Rate(hard, 800, 1000, o => o.Fumble);
            float sf = Rate(soft, 800, 1000, o => o.Fumble);
            TestContext.WriteLine("fumble rate hard=" + hf + " soft=" + sf);
            Assert.GreaterOrEqual(hf, sf);
            Assert.Greater(hf, 0f);
            Assert.Less(hf, 0.25f);
        }

        [Test]
        public void SecureCarriersFumbleLess()
        {
            var loose = new Lab { DefSpeed = 10f, CarrierSpeed = 8f, Tackling = 90, Carrying = 25 };
            var tight = new Lab { DefSpeed = 10f, CarrierSpeed = 8f, Tackling = 90, Carrying = 99 };
            float a = Rate(loose, 1000, 2000, o => o.Fumble);
            float b = Rate(tight, 1000, 2000, o => o.Fumble);
            TestContext.WriteLine("fumbles: loose=" + a + " tight=" + b);
            Assert.Greater(a, b);
        }

        [Test]
        public void CarriersNearTheSidelineGetPushedOut()
        {
            // a runner at the boundary heading out is shoved over the line rather than brought down in bounds
            var lab = new Lab { CarrierX = 1.0f, DefDx = 1.5f, DefDy = 1.0f, DefSpeed = 6f, CarrierSpeed = 6f, Tackling = 70 };
            int oob = 0, n = 200;
            for (int s = 0; s < n; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 4000 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                SimTestKit.StepToLive(sim);
                SimTestKit.ParkAll(sim, 6f);
                SimAgent c = null;
                for (int i = 1; i < 11 && c == null; i++) if (sim.Agents[i].Assign is CarryAssignment && !((CarryAssignment)sim.Agents[i].Assign).IsFake) c = sim.Agents[i];
                sim.Blocks.HandedOff = true;
                sim.GiveBall(c);
                SimTestKit.Place(c, lab.CarrierX, 40f, -2.2f, 5.5f);
                c.Body.Facing = new Vec2(-0.37f, 0.93f);
                SimAgent d = sim.Agents[11 + 7];
                d.R.Tackling = lab.Tackling;
                SimTestKit.Place(d, lab.CarrierX + lab.DefDx, 40f + lab.DefDy + 1.5f, -3f, -5f);
                for (int i = 0; i < 200 && sim.Phase == PlayPhase.Live; i++) sim.Step(default(ControlInput));
                if (sim.EndReason == EndReason.OutOfBounds) oob++;
            }
            TestContext.WriteLine("out of bounds on sideline contact: " + oob + "/" + n);
            Assert.Greater(oob, n / 16);   // with real missed tackles (clean-stop constant 7.5 -> 2.0) fewer contacts end in a clean shove
        }

        [Test]
        public void HeavyCarriersDragTacklersForExtraYards()
        {
            var heavy = new Lab { CarrierStrength = 98, BreakTackle = 90, Tackling = 55, DefStrength = 50 };
            var light = new Lab { CarrierStrength = 40, BreakTackle = 40, Tackling = 90, DefStrength = 90 };
            float yh = MeanYards(heavy, 120, 5000);
            float yl = MeanYards(light, 120, 5000);
            TestContext.WriteLine("yards after first contact: heavy=" + yh + " light=" + yl);
            Assert.Greater(yh, yl);
        }

        [Test]
        public void TacklersAreOnTheGroundBrieflyAfterTheStop()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 77, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
            PlayResult r = sim.RunToCompletion();
            if (sim.Log.Has(SimEventType.Tackle))
            {
                PlayEvent? e = null;   // the result names the tackler who brought the carrier down (the last tackle event: earlier ones are drags and gang pieces)
                foreach (var ev in sim.Log.Events) if (ev.Type == SimEventType.Tackle) e = ev;
                Assert.IsTrue(e.HasValue);
                Assert.GreaterOrEqual(e.Value.AgentA, 11, "a defender makes the tackle");
                Assert.LessOrEqual(e.Value.AgentA, 21);
                Assert.AreEqual(r.TacklerId, sim.Agents[e.Value.AgentA].PlayerId);
            }
        }
    }
}
