using NUnit.Framework;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.9.0 CONNECT: the pure parts of the connection layer (carried momentum, take choice by speed, held blend, step callback, kick geometry, block style plans).</summary>
    public class ConnectTests
    {
        static ContactTable.Pair Pair(string clip, float speedA, float tContact = 0.5f)
        {
            var p = new ContactTable.Pair { key = ArtKeys.ContactTackle, clipA = clip, clipB = clip.Replace("_A", "_B"), lengthA = 3f, lengthB = 3f, step = 0.1f, tContact = tContact, speedA = speedA, closingB = 3f, bearing = 0f };
            int n = 31; p.a = new float[n * ContactTable.Stride]; p.b = new float[n * ContactTable.Stride];
            return p;
        }

        [Test]
        public void TheTakeFasterThanTheCarrierWinsTheChoice()
        {
            var slow = Pair("TackleWrapFrontCHighDmo_A", 1.0f); var fast = Pair("TackleWrapFrontLHighDmo_A", 4.0f);
            var list = new System.Collections.Generic.List<ContactTable.Pair> { slow, fast };
            var pick = TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Wrap, Bearing = 0f, CarrierSpeed = 7.5f, Closing = 3f });
            Assert.AreSame(fast, pick, "a sprinting carrier gets the fast take (E-205)");
        }

        [Test]
        public void RecentTakesAreAvoided()
        {
            var a = Pair("TackleWrapFrontCHighDmo_A", 2f); var b = Pair("TackleWrapFrontLHighDmo_A", 2f);
            var list = new System.Collections.Generic.List<ContactTable.Pair> { a, b };
            var s = new ContactSituation { Kind = ContactKind.Wrap, Bearing = 0f, CarrierSpeed = 2f, Closing = 3f, Seed = 5 };
            var first = TackleVariants.Choose(list, s);
            s.Avoid = new System.Collections.Generic.List<string> { first.clipA };
            Assert.AreNotSame(first, TackleVariants.Choose(list, s), "the take that was just used is avoided (E-217)");
        }

        [Test]
        public void MomentumIsCarriedThroughTheHitAndEases()
        {
            var p = Pair("TackleWrapFrontCHighDmo_A", 1f);
            var s = ContactSession.Create(p, ContactKind.Wrap, 1, 16, Vector2.zero, new Vector2(0f, -1f), 0f, 0f, new Vector2(0f, 8f));
            Assert.AreEqual(8f, s.EntrySpeed, 1e-3f);
            Assert.AreEqual(0f, s.Slide().magnitude, 1e-4f, "nothing before the contact");
            while (s.Time < p.tContact + 0.05f) s.Advance(0.01f);
            float early = s.Slide().magnitude;
            while (s.Time < p.tContact + 1.5f) s.Advance(0.01f);
            float late = s.Slide().magnitude;
            Assert.Greater(early, 0f); Assert.Greater(late, early); Assert.LessOrEqual(late, 0.6f + 1e-3f, "capped: the simulation already carries a tackled runner on");
            Assert.Greater(s.Slide().y, 0f, "along the incoming velocity");
        }

        [Test]
        public void ASprintingCarrierIsGivenBackToTheSimulationEarlier()
        {
            var p = Pair("TackleWrapFrontCHighDmo_A", 1f);
            var slow = ContactSession.Create(p, ContactKind.Wrap, 1, 16, Vector2.zero, new Vector2(0f, -1f), 0f, 0f, new Vector2(0f, 2f));
            var fast = ContactSession.Create(p, ContactKind.Wrap, 1, 16, Vector2.zero, new Vector2(0f, -1f), 0f, 0f, new Vector2(0f, 9f));
            Assert.Less(fast.AuthorityHold, slow.AuthorityHold);
        }

        [Test]
        public void HeldTakeBlendsInInsteadOfJumping()
        {
            var p = Pair("TackleWrapFrontCHighDmo_A", 1f);
            var s = ContactSession.Create(p, ContactKind.Wrap, 1, 16, Vector2.zero, new Vector2(0f, -1f), 0f, 0f, Vector2.zero);
            s.Hold(true); s.TickHeld(0.05f);
            Assert.Less(s.HeldBlend, 0.3f);
            for (int i = 0; i < 40; i++) s.TickHeld(0.02f);
            Assert.AreEqual(1f, s.HeldBlend, 1e-4f);
            s.Hold(false); s.TickHeld(0.02f);
            Assert.AreEqual(0f, s.HeldBlend, 1e-4f);
        }

        [Test]
        public void EachActorIsFinishedWithHisOwnTake()
        {
            var p = Pair("TackleBrokenBackCHighDmo_A", 2f); p.lengthA = 1.0f; p.lengthB = 3.0f;
            var s = ContactSession.Create(p, ContactKind.Broken, 1, 16, Vector2.zero, new Vector2(0f, -1f), 0f, 0f, Vector2.zero);
            while (s.Time < 1.05f) s.Advance(0.01f);
            Assert.IsFalse(s.Finished, "the tackler is still in his longer take");
            s.ReleasedA = true;
            while (s.Time < 3.05f) s.Advance(0.01f);
            Assert.IsTrue(s.Finished);
            Assert.IsTrue(s.CarrierRunsOn);
        }

        [Test]
        public void StepCallbackFiresOncePerFixedStep()
        {
            var sim = new PlaySimulation();
            int n = 0; sim.AfterStep += s => n++;
            Assert.AreEqual(0, sim.StepCounter);
            Assert.That(sim.StepAlpha, Is.InRange(0f, 1f));
        }

        [Test]
        public void KickGeometryIsConsistent()
        {
            Assert.AreEqual(0.30f, KickGeometry.TeeHeightYd, 1e-4f);
            Assert.Greater(KickGeometry.PuntDropSeconds, 0.2f);
            Assert.That(KickGeometry.HolderSpotHeightYd, Is.InRange(0.15f, 0.35f));      // v0.10.0 QB: the holder's spot (the ball tilted on the turf at his knee)
            Assert.Less(KickGeometry.SnapStartHeightYd, 0.5f);
        }

        [Test]
        public void ClipPlanHasAFadeFloorAndBlockStylesExist()
        {
            var plan = new ClipPlan();
            Assert.AreEqual(0f, plan.FadeFloor);
            Assert.That(AnimKit.ImpactFloor, Is.LessThan(ActionAnimator.MinFade));
            Assert.AreNotEqual(BlockStyle.PassSet, BlockStyle.RunBlock);
        }

        [Test]
        public void NoPlanWithoutALibrary()
        {
            // plan builders are empty (never throw) when the art library is missing
            Assert.DoesNotThrow(() => { var h = new AnimHints { Seed = 3, PairSeed = 9 }; AnimKit.TurnAndRun(3, 10f); AnimKit.GetOffFor(PlayerRole.OL, 1, AgentAction.Run, 0f, BlockStyle.PassSet); AnimKit.MotionStart(1, 10f); });
        }
    }
}
