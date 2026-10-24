using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>v0.4: the quarterback favors a hot-routed receiver, and the test hooks the contact gallery relies on (forced tackle outcomes, open-field-only tackles) behave.</summary>
    public class ContactSimTests
    {
        static PlaySimulation Manual(string off, string offForm, string def, string defForm, int seed, PlaySimOptions opts = null)
        {
            opts = opts ?? new PlaySimOptions { AutoSnap = false, Penalties = false };
            return SimTestKit.RunNamed(off, offForm, def, defForm, 75, 75, seed, null, null, opts);
        }

        static void SnapAndRun(PlaySimulation sim)
        {
            sim.Tick(0.02f, new ControlInput { Snap = true }, -1);
            sim.RunToCompletion();
        }

        // ------------------------------------------------------------------------------------------------ hot route preference
        [Test]
        public void QuarterbackThrowsToTheHotRoutedReceiverMoreOftenThanToTheSameRouteWithoutTheFlag()
        {
            int hotThrows = 0, plainThrows = 0, hotTotal = 0, plainTotal = 0;
            for (int seed = 1; seed <= 300; seed++)
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    var sim = Manual("Slants", "Shotgun", "Cover 4 Quarters", "Nickel", seed);   // v0.9.0: quarters leaves the out open (cover 2 now squats the flat and the quick Out is covered)
                    Assert.IsTrue(sim.SetHotRoute(10, HotRoute.Out));
                    if (mode == 1) ((RouteAssignment)sim.Agents[10].Assign).IsHot = false;   // same route, same read order, no preference
                    SnapAndRun(sim);
                    var thr = sim.Log.First(SimEventType.Throw);
                    if (!thr.HasValue || thr.Value.Note == "away") continue;
                    if (mode == 0) { hotTotal++; if (thr.Value.AgentB == 10) hotThrows++; }
                    else { plainTotal++; if (thr.Value.AgentB == 10) plainThrows++; }
                }
            }
            Assert.Greater(hotTotal, 30); Assert.Greater(plainTotal, 30);
            float hotShare = hotThrows / (float)hotTotal, plainShare = plainThrows / (float)plainTotal;
            Assert.Greater(hotShare, plainShare + 0.035f, "hot " + hotShare.ToString("0.00") + " vs plain " + plainShare.ToString("0.00"));
        }

        [Test]
        public void IsHotReadIsTrueOnlyForFlaggedRoutes()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel", 3);
            Assert.IsFalse(OffenseAI.IsHotRead(sim.Agents[10]));
            sim.SetHotRoute(10, HotRoute.Fade);
            Assert.IsTrue(OffenseAI.IsHotRead(sim.Agents[10]));
            Assert.IsFalse(OffenseAI.IsHotRead(sim.Agents[9]));
            Assert.IsFalse(OffenseAI.IsHotRead(null));
        }

        // ------------------------------------------------------------------------------------------------ forced tackles (gallery hook)
        static SimEventType FirstContactEvent(PlaySimulation sim)
        {
            foreach (var e in sim.Log.Events)
                if (e.Type == SimEventType.Tackle || e.Type == SimEventType.BrokenTackle) return e.Type;
            return SimEventType.PlayEnd;
        }

        [Test]
        public void ForcedTackleTypesComeOutAsRequested()
        {
            foreach (TackleType want in new[] { TackleType.WrapUp, TackleType.BigHit, TackleType.DiveTackle, TackleType.Shoestring, TackleType.BrokenTackle, TackleType.Drag })
            {
                bool found = false;
                for (int seed = 1; seed <= 12 && !found; seed++)
                {
                    var opts = new PlaySimOptions { AutoSnap = true, Penalties = false, ForceTackle = want, ForceTackleAfter = 2f };
                    var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, seed, null, null, opts);
                    sim.RunToCompletion();
                    foreach (var e in sim.Log.Events)
                    {
                        if ((e.Type == SimEventType.Tackle || e.Type == SimEventType.BrokenTackle) && e.Tackle == want) { found = true; break; }
                    }
                }
                Assert.IsTrue(found, "forced " + want);
            }
        }

        [Test]
        public void ForceTackleAfterKeepsDefendersOffUntilTheCarrierIsPastTheLine()
        {
            var opts = new PlaySimOptions { AutoSnap = true, Penalties = false, ForceTackle = TackleType.WrapUp, ForceTackleAfter = 6f };
            var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 5, null, null, opts);
            sim.RunToCompletion();
            foreach (var e in sim.Log.Events)
                if (e.Type == SimEventType.Tackle && e.AgentA >= 0) Assert.GreaterOrEqual(e.Pos.Y, sim.LosY + 6f - 0.5f, "no tackle before the carrier was 6 yards past the line: " + e);      // v0.11.0: a runner who slides or dives himself down (AgentA -1) is not a defender
        }

        [Test]
        public void TacklesWithoutTheHooksAreUnchanged()
        {
            // two identical plays: the determinism digest must match and the hooks default to off
            var a = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 9, null, null, new PlaySimOptions { Penalties = false });
            var b = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 9, null, null, new PlaySimOptions { Penalties = false });
            a.RunToCompletion(); b.RunToCompletion();
            Assert.AreEqual(a.Log.Digest(), b.Log.Digest());
            Assert.IsFalse(new PlaySimOptions().ForceTackle.HasValue);
            Assert.AreEqual(0f, new PlaySimOptions().ForceTackleAfter);
        }
    }
}
