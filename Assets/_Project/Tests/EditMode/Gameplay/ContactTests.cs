using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Art;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.4 "Contact": paired-clip alignment math, tackle variant choice, ragdoll state machine, two-bone IK and lean math (all pure).</summary>
    public class ContactTests
    {
        const float Eps = 1e-3f;

        // ------------------------------------------------------------------------------------------------ alignment math
        [Test]
        public void RotateFollowsTheUnityYawConvention()
        {
            Vector2 r = ContactMath.Rotate(new Vector2(0f, 1f), 90f);   // +Z turned 90 degrees ends on +X
            Assert.AreEqual(1f, r.x, Eps); Assert.AreEqual(0f, r.y, Eps);
            Assert.AreEqual(90f, ContactMath.YawOf(new Vector2(1f, 0f)), Eps);
            Assert.AreEqual(0f, ContactMath.YawOf(new Vector2(0f, 3f)), Eps);
            var q = Quaternion.Euler(0f, 37f, 0f) * new Vector3(2f, 0f, 1f);
            Vector2 m = ContactMath.Rotate(new Vector2(2f, 1f), 37f);
            Assert.AreEqual(q.x, m.x, Eps); Assert.AreEqual(q.z, m.y, Eps);
        }

        [Test]
        public void AlignPutsTheAuthoredAnchorOnTheWorldAnchorWithTheWorldHeading()
        {
            Vector2 authored = new Vector2(1.3f, -2.1f); float authYaw = 25f;
            Vector2 world = new Vector2(40f, 12f); float worldYaw = -130f;
            var t = ContactMath.Align(authored, authYaw, world, worldYaw);
            Vector2 p = t.Apply(authored);
            Assert.AreEqual(world.x, p.x, Eps); Assert.AreEqual(world.y, p.y, Eps);
            Assert.AreEqual(worldYaw, Mathf.DeltaAngle(0f, authYaw + t.Yaw), Eps);
            var back = t.Inverse(p);
            Assert.AreEqual(authored.x, back.x, Eps); Assert.AreEqual(authored.y, back.y, Eps);
        }

        [Test]
        public void AlignPairAnchorsTheCarrierExactlyAndKeepsTheAuthoredDistance()
        {
            Vector2 authA = new Vector2(0.2f, -1.1f), authB = new Vector2(0f, 0f); float authYawB = 90f;   // A is the tackler behind the carrier B
            Vector2 worldA = new Vector2(10f, 3f), worldB = new Vector2(10.4f, 4.2f);
            var t = ContactMath.AlignPair(authA, authB, authYawB, worldA, worldB, 10f, 0.6f);
            Vector2 b = t.Apply(authB);
            Assert.AreEqual(worldB.x, b.x, Eps); Assert.AreEqual(worldB.y, b.y, Eps);
            Assert.AreEqual((authA - authB).magnitude, (t.Apply(authA) - t.Apply(authB)).magnitude, Eps, "a rigid transform keeps the distance");
            // axisWeight 1: the A to B axis matches the world axis exactly; 0: the carrier heading matches
            var axis = ContactMath.AlignPair(authA, authB, authYawB, worldA, worldB, 10f, 1f);
            Vector2 d = axis.Apply(authA) - axis.Apply(authB), w = worldA - worldB;
            Assert.AreEqual(ContactMath.YawOf(w), ContactMath.YawOf(d), 0.05f);
            var face = ContactMath.AlignPair(authA, authB, authYawB, worldA, worldB, 10f, 0f);
            Assert.AreEqual(10f, Mathf.DeltaAngle(0f, authYawB + face.Yaw), Eps);
        }

        [Test]
        public void BearingIsSignedFromTheObserversHeading()
        {
            Vector2 obs = Vector2.zero;
            Assert.AreEqual(0f, ContactMath.BearingDeg(new Vector2(0f, 5f), obs, 0f), Eps);
            Assert.AreEqual(90f, ContactMath.BearingDeg(new Vector2(5f, 0f), obs, 0f), Eps);
            Assert.AreEqual(-90f, ContactMath.BearingDeg(new Vector2(-5f, 0f), obs, 0f), Eps);
            Assert.AreEqual(180f, Mathf.Abs(ContactMath.BearingDeg(new Vector2(0f, -5f), obs, 0f)), Eps);
            Assert.AreEqual(0f, ContactMath.BearingDeg(new Vector2(5f, 0f), obs, 90f), Eps, "facing +X, the +X point is dead ahead");
        }

        [Test]
        public void WarpAndAuthorityWeightsAreMonotonicAndBounded()
        {
            float prev = -1f;
            for (float t = 0f; t <= 0.6f; t += 0.02f) { float w = ContactMath.WarpIn(t, 0.1f, 0.4f); Assert.GreaterOrEqual(w, prev); Assert.That(w, Is.InRange(0f, 1f)); prev = w; }
            Assert.AreEqual(0f, ContactMath.WarpIn(0.1f, 0.1f, 0.4f), Eps); Assert.AreEqual(1f, ContactMath.WarpIn(0.4f, 0.1f, 0.4f), Eps);
            prev = -1f;
            for (float t = 0f; t <= 6f; t += 0.1f) { float a = ContactMath.Authority(t, 0.4f, 5f); Assert.GreaterOrEqual(a, prev); prev = a; }
            Assert.AreEqual(0f, ContactMath.Authority(0.5f, 0.4f, 5f), Eps, "the animation drives the hit");
            Assert.AreEqual(1f, ContactMath.Authority(3f, 0.4f, 5f), Eps, "the simulation owns the end");
        }

        [Test]
        public void SamplingInterpolatesBetweenFrames()
        {
            var data = new float[] { 0f, 0f, 0f, 0f, 0f, 1f, 2f, 0f, 0f, 90f, 2f, 4f, 0f, 0f, 180f };   // 3 samples, stride 5
            Assert.AreEqual(0.5f, ContactMath.Sample(data, 5, 0, 0.1f, 0.05f), Eps);
            Assert.AreEqual(2f, ContactMath.Sample(data, 5, 0, 0.1f, 5f), Eps, "clamped to the last sample");
            Assert.AreEqual(45f, ContactMath.SampleYaw(data, 5, 4, 0.1f, 0.05f), Eps);
        }

        static ContactTable.Pair Pair(string clipA, string key, float bearing, float speedA, float closingB, int samples = 8)
        {
            var p = new ContactTable.Pair { key = key, clipA = clipA, clipB = clipA.Replace("_A", "_B"), lengthA = 2f, lengthB = 2f, step = 0.05f, tContact = 0.2f, bearing = bearing, speedA = speedA, closingB = closingB };
            p.a = new float[samples * ContactTable.Stride]; p.b = new float[samples * ContactTable.Stride];
            return p;
        }

        // ------------------------------------------------------------------------------------------------ session
        [Test]
        public void SessionWarpsFromTheSimulationOntoTheAnimationAndHandsBack()
        {
            // a carrier running +Z at 1 m per sample; the tackler comes from behind (-Z)
            var p = new ContactTable.Pair { key = ArtKeys.ContactTackle, clipA = "TackleWrapBackCHighDmo_A", clipB = "TackleWrapBackCHighDmo_B", lengthA = 3f, lengthB = 3f, step = 0.1f, tContact = 0.5f };
            int n = 31; p.a = new float[n * ContactTable.Stride]; p.b = new float[n * ContactTable.Stride];
            for (int i = 0; i < n; i++)
            {
                float z = i * 0.1f * 3f;   // carrier moves 3 m/s then
                p.a[i * 5 + 0] = 0f; p.a[i * 5 + 1] = z; p.a[i * 5 + 2] = 0f; p.a[i * 5 + 3] = z; p.a[i * 5 + 4] = 0f;
                p.b[i * 5 + 0] = 0f; p.b[i * 5 + 1] = z - 1.0f + Mathf.Min(0.5f, i * 0.1f); p.b[i * 5 + 2] = 0f; p.b[i * 5 + 3] = z - 1.0f + Mathf.Min(0.5f, i * 0.1f); p.b[i * 5 + 4] = 0f;
            }
            Vector2 simA = new Vector2(5f, 8f), simB = new Vector2(5f, 6.6f);
            var s = ContactSession.Create(p, ContactKind.Wrap, 1, 16, simA, simB, 0f, 0f, new Vector2(0f, 3f));
            Assert.AreEqual(0.32f, s.Start, 0.02f, "the take starts a lead-in before the contact");
            ContactActorPose a, b;
            s.Evaluate(simA, 0f, simB, 0f, out a, out b);
            Assert.AreEqual(0f, a.Warp, Eps, "no warp yet: the view is the simulation");
            Assert.AreEqual(simA.x, a.Root.x, Eps); Assert.AreEqual(simA.y, a.Root.y, Eps);
            // at the contact the carrier's hips are where the simulation predicted them and the tackler stands as authored (behind him)
            while (s.Time < p.tContact) s.Advance(0.01f);
            s.Evaluate(simA, 0f, simB, 0f, out a, out b);
            Assert.AreEqual(1f, a.Warp, 0.02f);
            Assert.Less(b.Root.y, a.Root.y, "the tackler is still behind the carrier");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, a.Yaw), 5f, "the carrier keeps his heading");
            // long after the hit the simulation owns the carrier again
            for (int i = 0; i < 300; i++) s.Advance(0.01f);
            s.Evaluate(simA, 0f, simB, 0f, out a, out b);
            Assert.AreEqual(simA.x, a.Root.x, 0.35f); Assert.AreEqual(simA.y, a.Root.y, 0.35f);
        }

        [Test]
        public void HeldSessionDoesNotAdvance()
        {
            var p = new ContactTable.Pair { key = ArtKeys.ContactTackle, clipA = "x_A", clipB = "x_B", lengthA = 3f, lengthB = 3f, step = 0.1f, tContact = 0.5f, a = new float[10 * 5], b = new float[10 * 5] };
            var s = ContactSession.Create(p, ContactKind.Wrap, 0, 1, Vector2.zero, new Vector2(0f, -1f), 0f, 0f, Vector2.zero);
            s.Advance(0.2f); float t = s.Time;
            s.Hold(true); s.Advance(1f);
            Assert.AreEqual(t, s.Time, 1e-5f);
            s.Hold(false); s.Advance(0.1f);
            Assert.Greater(s.Time, t);
        }

        // ------------------------------------------------------------------------------------------------ variant choice
        [Test]
        public void TackleKindFollowsTheSimulationOutcome()
        {
            Assert.AreEqual(ContactKind.Wrap, TackleVariants.KindFor(TackleType.WrapUp, false));
            Assert.AreEqual(ContactKind.Wrap, TackleVariants.KindFor(TackleType.GangTackle, false));
            Assert.AreEqual(ContactKind.Wrap, TackleVariants.KindFor(TackleType.Drag, false));
            Assert.AreEqual(ContactKind.Wrap, TackleVariants.KindFor(TackleType.PushOutOfBounds, false));
            Assert.AreEqual(ContactKind.BigHit, TackleVariants.KindFor(TackleType.BigHit, false));
            Assert.AreEqual(ContactKind.Dive, TackleVariants.KindFor(TackleType.DiveTackle, false));
            Assert.AreEqual(ContactKind.Dive, TackleVariants.KindFor(TackleType.Shoestring, false));
            Assert.AreEqual(ContactKind.Broken, TackleVariants.KindFor(TackleType.BrokenTackle, false));
            Assert.AreEqual(ContactKind.Sack, TackleVariants.KindFor(TackleType.WrapUp, true), "a sack always uses the sack takes");
        }

        [Test]
        public void ChooseMatchesTheKindAndThenTheContactAngleAndSpeed()
        {
            var list = new List<ContactTable.Pair>
            {
                Pair("TackleWrapBackCHighDmo_A", ArtKeys.ContactTackle, -165f, 3f, 2.5f),
                Pair("TackleWrapFrontCHighDmo_A", ArtKeys.ContactTackle, 5f, 1.5f, 2f),
                Pair("TackleWrapRightCHighDmo_A", ArtKeys.ContactTackle, 85f, 3f, 3f),
                Pair("TackleWrapBackCLowDmo_A", ArtKeys.ContactTackle, -170f, 3f, 3f),
                Pair("TackleBrokenBackCHighDmo_A", ArtKeys.ContactTackle, -160f, 4f, 4f),
                Pair("SackWrapFrontCHighDmo_A", ArtKeys.ContactSack, 85f, 0.3f, 3f),
                Pair("root|BigHit1_Success_003_A", ArtKeys.ContactBigHit, -60f, 4f, 3f),
            };
            Assert.AreEqual("TackleWrapBackCHighDmo_A", TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Wrap, Bearing = -170f, CarrierSpeed = 3f, Closing = 2.5f }).clipA);
            Assert.AreEqual("TackleWrapFrontCHighDmo_A", TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Wrap, Bearing = 10f, CarrierSpeed = 1.5f, Closing = 2f }).clipA);
            Assert.AreEqual("TackleWrapRightCHighDmo_A", TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Wrap, Bearing = 80f, CarrierSpeed = 3f, Closing = 3f }).clipA);
            Assert.AreEqual("TackleWrapBackCLowDmo_A", TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Dive, Bearing = -170f, CarrierSpeed = 3f, Closing = 3f }).clipA, "dives use the low wraps");
            Assert.AreEqual("TackleBrokenBackCHighDmo_A", TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Broken, Bearing = -150f, CarrierSpeed = 4f, Closing = 4f }).clipA);
            Assert.AreEqual("SackWrapFrontCHighDmo_A", TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Sack, Bearing = 0f, CarrierSpeed = 0f, Closing = 3f }).clipA);
            Assert.AreEqual("root|BigHit1_Success_003_A", TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.BigHit, Bearing = -40f, CarrierSpeed = 4f, Closing = 3f }).clipA);
            Assert.IsNull(TackleVariants.Choose(list, new ContactSituation { Kind = ContactKind.Contested }), "nothing of that kind in the table");
        }

        [Test]
        public void ChooseIsDeterministicForASeedAndVariesAcrossSeedsWhenTakesTie()
        {
            var list = new List<ContactTable.Pair>
            {
                Pair("TackleWrapBackCHighDmo_A", ArtKeys.ContactTackle, -160f, 3f, 2.5f),
                Pair("TackleWrapBackCHighOmo_A", ArtKeys.ContactTackle, -160f, 3f, 2.5f),
                Pair("TackleWrapBackRHighDmo_A", ArtKeys.ContactTackle, -160f, 3f, 2.5f),
            };
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 40; seed++)
            {
                var sit = new ContactSituation { Kind = ContactKind.Wrap, Bearing = -160f, CarrierSpeed = 3f, Closing = 2.5f, Seed = seed };
                var a = TackleVariants.Choose(list, sit); var b = TackleVariants.Choose(list, sit);
                Assert.AreSame(a, b);
                seen.Add(a.clipA);
            }
            Assert.GreaterOrEqual(seen.Count, 2, "equal takes are rotated by seed");
        }

        // ------------------------------------------------------------------------------------------------ ragdoll state machine
        [Test]
        public void RagdollMachineRunsAnimatedBlendInActiveRecoverAnimated()
        {
            var m = new RagdollStateMachine();
            Assert.AreEqual(RagdollPhase.Animated, m.Phase);
            Assert.IsTrue(m.Trigger(1f));
            Assert.AreEqual(RagdollPhase.BlendIn, m.Phase);
            float prev = 0f; var seen = new List<RagdollPhase> { m.Phase };
            for (int i = 0; i < 600 && m.Phase != RagdollPhase.Animated; i++)
            {
                m.Update(0.01f, 5f, false);   // never settles: the budget ends the activation
                if (m.Phase == RagdollPhase.BlendIn || m.Phase == RagdollPhase.Active) Assert.GreaterOrEqual(m.Weight, prev - 1e-5f);
                prev = m.Weight;
                if (seen[seen.Count - 1] != m.Phase) seen.Add(m.Phase);
            }
            CollectionAssert.AreEqual(new[] { RagdollPhase.BlendIn, RagdollPhase.Active, RagdollPhase.Recover, RagdollPhase.Animated }, seen);
            Assert.AreEqual(0f, m.Weight, 1e-5f);
        }

        [Test]
        public void RagdollWeightIsOneWhileActiveAndLastsBetweenTheMinimumAndTheBudget()
        {
            var big = new RagdollStateMachine(); big.Trigger(1f);
            var small = new RagdollStateMachine(); small.Trigger(0f);
            Assert.AreEqual(1.2f, big.ActiveBudget, 1e-4f); Assert.AreEqual(0.4f, small.ActiveBudget, 1e-4f);
            float tBig = ActiveTime(big, 9f, false), tSmall = ActiveTime(small, 9f, false);
            Assert.AreEqual(1.2f, tBig, 0.03f); Assert.AreEqual(0.4f, tSmall, 0.03f);
        }

        static float ActiveTime(RagdollStateMachine m, float speed, bool grounded)
        {
            float t = 0f;
            for (int i = 0; i < 1000; i++)
            {
                m.Update(0.005f, speed, grounded);
                if (m.Phase == RagdollPhase.Active) { Assert.AreEqual(1f, m.Weight, 1e-5f); t += 0.005f; }
                if (m.Phase == RagdollPhase.Recover) break;
            }
            return t;
        }

        [Test]
        public void RagdollEndsEarlyOnceGroundedAndSlowButNotBeforeTheMinimum()
        {
            var m = new RagdollStateMachine(); m.Trigger(1f);
            float t = ActiveTime(m, 0.1f, true);
            Assert.AreEqual(0.4f, t, 0.03f, "settles as soon as the minimum time is up");
            var air = new RagdollStateMachine(); air.Trigger(1f);
            Assert.AreEqual(1.2f, ActiveTime(air, 0.1f, false), 0.03f, "a body in the air keeps simulating");
        }

        [Test]
        public void RagdollRecoverRequestIsRaisedOnceAndRetriggerIsRefusedUntilDone()
        {
            var m = new RagdollStateMachine(); m.Trigger(0.5f);
            Assert.IsFalse(m.Trigger(1f), "already active");
            int requests = 0;
            for (int i = 0; i < 800 && m.Active; i++)
            {
                m.Update(0.005f, 0f, true);
                if (m.RecoverRequested) { requests++; m.ConsumeRecoverRequest(); }
                if (m.Phase == RagdollPhase.Recover) Assert.IsFalse(m.Trigger(1f));
            }
            Assert.AreEqual(1, requests);
            Assert.IsFalse(m.Active);
            Assert.IsTrue(m.Trigger(1f), "free again after the get-up blend");
            m.Cancel();
            Assert.AreEqual(RagdollPhase.Animated, m.Phase); Assert.AreEqual(0f, m.Weight);
        }

        [Test]
        public void RagdollFaceUpAndVelocityCap()
        {
            Assert.IsTrue(RagdollStateMachine.IsFaceUp(0.7f)); Assert.IsFalse(RagdollStateMachine.IsFaceUp(-0.2f));
            var v = RagdollStateMachine.CapVelocity(new Vector3(30f, 0f, 40f), 9f);
            Assert.AreEqual(9f, v.magnitude, 1e-3f);
            var small = RagdollStateMachine.CapVelocity(new Vector3(1f, 2f, 0f), 9f);
            Assert.AreEqual(new Vector3(1f, 2f, 0f), small);
        }

        // ------------------------------------------------------------------------------------------------ IK and lean
        [Test]
        public void TwoBoneIkReachesTheTargetKeepsBoneLengthsAndTheBendSide()
        {
            Vector3 root = new Vector3(0f, 1f, 0f), joint = new Vector3(0.1f, 0.5f, 0.2f), end = new Vector3(0f, 0.05f, 0f);
            float l1 = Vector3.Distance(root, joint), l2 = Vector3.Distance(joint, end);
            Vector3 target = new Vector3(0.25f, 0.25f, 0.1f);   // reachable
            Vector3 nj, ne;
            LimbIK.Solve(root, joint, end, target, joint - (root + end) * 0.5f, out nj, out ne);
            Assert.AreEqual(0f, Vector3.Distance(ne, target), 1e-4f);
            Assert.AreEqual(l1, Vector3.Distance(root, nj), 1e-4f); Assert.AreEqual(l2, Vector3.Distance(nj, ne), 1e-4f);
            // the knee stays on the same side of the leg's axis as before
            Vector3 axis = (ne - root).normalized;
            Vector3 before = (joint - root) - Vector3.Dot(joint - root, (end - root).normalized) * (end - root).normalized;
            Vector3 after = (nj - root) - Vector3.Dot(nj - root, axis) * axis;
            Assert.Greater(Vector3.Dot(before.normalized, after.normalized), 0.5f);
        }

        [Test]
        public void TwoBoneIkClampsAnUnreachableTargetToTheLimbsReach()
        {
            Vector3 root = Vector3.zero, joint = new Vector3(0f, -0.4f, 0.05f), end = new Vector3(0f, -0.8f, 0f);
            Vector3 nj, ne;
            LimbIK.Solve(root, joint, end, new Vector3(0f, -3f, 0f), Vector3.forward, out nj, out ne);
            Assert.LessOrEqual(ne.magnitude, LimbIK.Reach(root, joint, end) + 1e-4f);
            Assert.AreEqual(-1f, ne.normalized.y, 1e-3f, "still points at the target");
            // a target on the root itself must not produce NaNs
            LimbIK.Solve(root, joint, end, root, Vector3.forward, out nj, out ne);
            Assert.IsFalse(float.IsNaN(nj.x) || float.IsNaN(ne.y));
        }

        [Test]
        public void LeanBanksIntoTurnsAndIsBoundedAndFadesAtLowSpeed()
        {
            Assert.AreEqual(0f, LeanMath.BankDegrees(8f, 0.5f), 1e-4f, "a standing player does not bank");
            float right = LeanMath.BankDegrees(6f, 7f), left = LeanMath.BankDegrees(-6f, 7f);
            Assert.Greater(right, 5f); Assert.AreEqual(-right, left, 1e-4f);
            Assert.LessOrEqual(LeanMath.BankDegrees(60f, 9f), 17f + 1e-4f);
            Assert.Greater(LeanMath.BankDegrees(6f, 7f), LeanMath.BankDegrees(6f, 2f));
            Assert.Greater(LeanMath.PitchDegrees(5f, 4f), 0f);
            Assert.LessOrEqual(LeanMath.PitchDegrees(50f, 8f), 9f + 1e-4f);
            float s = 0f; for (int i = 0; i < 100; i++) s = LeanMath.Smooth(s, 10f, 0.02f, 0.1f);
            Assert.AreEqual(10f, s, 0.01f);
        }

        [Test]
        public void EnvelopeRampsInHoldsAndRampsOut()
        {
            Assert.AreEqual(0f, LeanMath.Envelope(0f, 0.5f, 0.1f, 0.1f), 1e-5f);
            Assert.AreEqual(1f, LeanMath.Envelope(0.25f, 0.5f, 0.1f, 0.1f), 1e-5f);
            Assert.AreEqual(0f, LeanMath.Envelope(0.5f, 0.5f, 0.1f, 0.1f), 1e-5f);
            Assert.AreEqual(0f, LeanMath.Envelope(0.6f, 0.5f, 0.1f, 0.1f), 1e-5f);
        }

        // ------------------------------------------------------------------------------------------------ performance LOD
        [Test]
        public void PlayerLodLevelStepsDownWithDistanceAndFollowsTheBias()
        {
            float bias = PlayerLod.Bias;
            try
            {
                PlayerLod.Bias = 1f;
                Assert.AreEqual(0, PlayerLod.LevelFor(3f)); Assert.AreEqual(1, PlayerLod.LevelFor(15f)); Assert.AreEqual(2, PlayerLod.LevelFor(60f));
                PlayerLod.Bias = 0.5f;
                Assert.AreEqual(1, PlayerLod.LevelFor(6f), "a lower tier drops detail sooner");
            }
            finally { PlayerLod.Bias = bias; }
        }
        [Test]
        public void QualityTiersMapOntoTheUnityQualityLevels()
        {
            Assert.AreEqual(4, QualityTiers.Names.Length);
            Assert.AreEqual(3, QualityTiers.TierOfQualityLevel(-1), "unset = Ultra, the v0.3 look");
            Assert.AreEqual(0, QualityTiers.TierOfQualityLevel(0));
            Assert.AreEqual(0, QualityTiers.TierOfQualityLevel(1));
            Assert.AreEqual(1, QualityTiers.TierOfQualityLevel(2));
            Assert.AreEqual(2, QualityTiers.TierOfQualityLevel(3));
            Assert.AreEqual(2, QualityTiers.TierOfQualityLevel(4));
            Assert.AreEqual(3, QualityTiers.TierOfQualityLevel(5));
            Assert.AreEqual(QualityTiers.RenderScales.Length, QualityTiers.RenderScaleNames.Length);
            foreach (float s in QualityTiers.RenderScales) Assert.That(s, Is.InRange(0.5f, 1f));
        }


        // ------------------------------------------------------------------------------------------------ the measured table (licensed art present only)
        [Test]
        public void ContactTableIsMeasuredAndConsistent()
        {
            ContactTable.ResetCache();
            var t = ContactTable.Load();
            if (t == null || t.pairs.Count == 0) Assert.Ignore("contact table not built (licensed art not present)");
            Assert.Greater(t.pairs.Count, 60);
            int tackles = 0, sacks = 0, bigs = 0;
            foreach (var p in t.pairs)
            {
                Assert.GreaterOrEqual(p.CountA, 2); Assert.GreaterOrEqual(p.CountB, 2);
                Assert.That(p.tContact, Is.InRange(0f, p.Length), p.clipA);
                Assert.That(p.distance, Is.InRange(0f, 3.5f), p.clipA + " hips at contact");
                Assert.That(p.bearing, Is.InRange(-180.01f, 180.01f));
                if (p.key == ArtKeys.ContactTackle) tackles++; else if (p.key == ArtKeys.ContactSack) sacks++; else if (p.key == ArtKeys.ContactBigHit) bigs++;
                foreach (float f in p.a) Assert.IsFalse(float.IsNaN(f) || float.IsInfinity(f));
            }
            Assert.Greater(tackles, 30); Assert.Greater(sacks, 10); Assert.Greater(bigs, 2);
            // every kind the director asks for has at least one take
            foreach (ContactKind k in new[] { ContactKind.Wrap, ContactKind.Dive, ContactKind.Broken, ContactKind.Sack, ContactKind.BigHit, ContactKind.Contested })
                Assert.IsNotNull(TackleVariants.Choose(t.pairs, new ContactSituation { Kind = k, Bearing = 0f, CarrierSpeed = 3f, Closing = 3f }), "no take for " + k);
        }
    }
}
