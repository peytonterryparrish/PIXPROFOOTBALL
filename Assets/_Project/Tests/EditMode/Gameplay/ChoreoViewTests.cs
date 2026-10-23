using NUnit.Framework;
using PixFootball.Art;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.10.0 CHOREO: the view side of the sequence planner (booked sessions are drawn where the simulation has the bodies; library mapping is safe without tables).</summary>
    public class ChoreoViewTests
    {
        static ContactTable.Pair MakePair()
        {
            int n = 61;
            var p = new ContactTable.Pair { key = "contact_tackle", clipA = "TackleWrapFrontCHighDmo_A", clipB = "TackleWrapFrontCHighDmo_B", lengthA = 3f, lengthB = 3f, step = 0.05f, tContact = 0.6f, distance = 0.55f, a = new float[n * ContactTable.Stride], b = new float[n * ContactTable.Stride] };
            for (int i = 0; i < n; i++) { p.a[i * ContactTable.Stride + 1] = i * 0.05f; p.b[i * ContactTable.Stride + 1] = 1.2f; }
            return p;
        }

        [Test]
        public void ABookedSessionDrawsTheBodiesWhereTheSimulationHasThem()
        {
            var bk = new ChoreoBooking { Kind = ChoreoKind.Tackle, A = 1, B = 2, StartT = 0.3f, StartSim = 1f, Rate = 0.85f, OwnsA = true, OwnsB = true };
            var s = ContactSession.CreateBooked(MakePair(), ContactKind.Wrap, 1, 2, bk, bk.ClipTime(1.2f));
            Assert.IsTrue(s.Booked);
            Assert.AreEqual(0.3f + 0.2f * 0.85f, s.Start, 1e-4f, "the session starts at the booking's clip time");
            ContactActorPose a, b;
            s.Evaluate(new Vector2(3f, 4f), 33f, new Vector2(5f, 6f), 44f, out a, out b);
            Assert.AreEqual(3f, a.Root.x, 1e-6f); Assert.AreEqual(4f, a.Root.y, 1e-6f);
            Assert.AreEqual(5f, b.Root.x, 1e-6f); Assert.AreEqual(6f, b.Root.y, 1e-6f);
            Assert.AreEqual(33f, a.Yaw, 1e-6f); Assert.AreEqual(44f, b.Yaw, 1e-6f);
            Assert.AreEqual(1f, a.Warp, 1e-6f);
            // time passes: still no warp, no correction back to the simulation
            s.Advance(0.5f, 0.85f);
            s.Evaluate(new Vector2(9f, 9f), 0f, new Vector2(8f, 8f), 0f, out a, out b);
            Assert.AreEqual(9f, a.Root.x, 1e-6f); Assert.AreEqual(8f, b.Root.y, 1e-6f);
        }

        [Test]
        public void TheLibraryIsAbsentWithoutTheMeasuredTables()
        {
            // the library is built from PixContact / PixChoreo / the ArtLibrary; whatever it returns, asking twice gives the same object and never throws
            var a = GameChoreoLibrary.Shared; var b = GameChoreoLibrary.Shared;
            Assert.AreSame(a, b);
            if (a != null) { Assert.Greater(a.Pairs.Count, 0); foreach (var p in a.Pairs) { Assert.IsNotNull(p.A); Assert.IsNotNull(p.B); Assert.Greater(p.A.Count, 1); } }
        }

        [Test]
        public void AClipPlanKeepsTheDefaultMaskSpeedUnlessAskedOtherwise()
        {
            var plan = new ClipPlan();
            Assert.AreEqual(0f, plan.MaskSpeed);
            Assert.AreEqual(2f, ActionAnimator.UpperBodySpeed);
        }
    }
}
