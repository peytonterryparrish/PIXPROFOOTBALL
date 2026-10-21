using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Presentation;
using UnityEngine;

namespace PixFootball.Tests
{
    public class DirectorTests
    {
        static PlayFacts Facts(Headline h, PlayType t, int yards, bool td = false, bool turnover = false)
        {
            return new PlayFacts { Headline = h, Type = t, Yards = yards, Touchdown = td, Turnover = turnover, Quarter = 2, ClockSeconds = 600f, ScoreMargin = 10 };
        }

        // ------------------------------------------------------------------------------------------- excitement scoring
        [Test]
        public void ExcitementRanksTouchdownsAndTurnoversAboveRoutinePlays()
        {
            float td = HighlightScorer.Score(Facts(Headline.Touchdown, PlayType.Pass, 35, true));
            float pick = HighlightScorer.Score(Facts(Headline.Interception, PlayType.Interception, 12, false, true));
            float fumble = HighlightScorer.Score(Facts(Headline.FumbleLost, PlayType.Fumble, 4, false, true));
            float safety = HighlightScorer.Score(Facts(Headline.Safety, PlayType.Sack, -3));
            float sack = HighlightScorer.Score(Facts(Headline.Sack, PlayType.Sack, -7));
            float fg = HighlightScorer.Score(Facts(Headline.FieldGoal, PlayType.FieldGoal, 31));
            float first = HighlightScorer.Score(Facts(Headline.FirstDown, PlayType.Run, 6));
            float punt = HighlightScorer.Score(Facts(Headline.Punt, PlayType.Punt, 44));
            float inc = HighlightScorer.Score(Facts(Headline.Incomplete, PlayType.Incomplete, 0));
            Assert.Greater(td, pick); Assert.Greater(pick, sack); Assert.Greater(fumble, sack); Assert.Greater(safety, sack);
            Assert.Greater(sack, fg); Assert.Greater(fg, first); Assert.Greater(first, punt); Assert.Greater(punt, inc);
            Assert.LessOrEqual(td, 100f); Assert.Greater(inc, 0f);
        }

        [Test]
        public void LongGainsBigHitsAndClutchTimeAddExcitement()
        {
            float short5 = HighlightScorer.Score(Facts(Headline.None, PlayType.Run, 5));
            float run24 = HighlightScorer.Score(Facts(Headline.None, PlayType.Run, 24));
            float run45 = HighlightScorer.Score(Facts(Headline.None, PlayType.Run, 45));
            Assert.Greater(run24, short5); Assert.Greater(run45, run24);
            Assert.GreaterOrEqual(run24, HighlightScorer.FullThreshold);
            var hit = Facts(Headline.None, PlayType.Run, 3); hit.BigHit = true; hit.Impact = 0.9f;
            Assert.GreaterOrEqual(HighlightScorer.Score(hit), HighlightScorer.FullThreshold);
            var calm = Facts(Headline.Touchdown, PlayType.Run, 5, true); var clutch = calm; clutch.Quarter = 4; clutch.ClockSeconds = 90f; clutch.ScoreMargin = 3;
            Assert.Greater(HighlightScorer.Score(clutch), HighlightScorer.Score(calm));
            var ot = calm; ot.Overtime = true; Assert.Greater(HighlightScorer.Score(ot), HighlightScorer.Score(calm));
            var conv = Facts(Headline.FirstDown, PlayType.Pass, 9); conv.ThirdOrFourthDownConversion = true;
            Assert.Greater(HighlightScorer.Score(conv), HighlightScorer.Score(Facts(Headline.FirstDown, PlayType.Pass, 9)));
        }

        [Test]
        public void AutoReplayFollowsTheCinematicsSetting()
        {
            Assert.IsFalse(HighlightScorer.WantsAutoReplay(100f, 2), "Off never replays");
            Assert.IsTrue(HighlightScorer.WantsAutoReplay(50f, 0)); Assert.IsFalse(HighlightScorer.WantsAutoReplay(50f, 1));
            Assert.IsTrue(HighlightScorer.WantsAutoReplay(85f, 1));
            Assert.IsFalse(HighlightScorer.WantsAutoReplay(10f, 0));
            Assert.AreEqual("42-YARD TOUCHDOWN", HighlightScorer.Title(Facts(Headline.Touchdown, PlayType.Pass, 42, true)));
            Assert.AreEqual("INTERCEPTION", HighlightScorer.Title(Facts(Headline.Interception, PlayType.Interception, 10, false, true)));
            Assert.AreEqual("BIG HIT", HighlightScorer.Title(new PlayFacts { Headline = Headline.None, Type = PlayType.Run, Yards = 2, BigHit = true }));
        }

        // ------------------------------------------------------------------------------------------- camera director rules
        static DirectorContext Ctx(int cine = 0) { return new DirectorContext { Cinematics = cine, PreSnapSeconds = 20f }; }

        [Test]
        public void CinematicsOffRunsNoShots()
        {
            var c = Ctx(2); c.GameStart = true; c.FirstPlayOfDrive = true; c.Touchdown = true; c.Turnover = true; c.BigPlay = true;
            Assert.AreEqual(0, ShotRules.PreSnap(c).Count); Assert.AreEqual(0, ShotRules.PostPlay(c).Count);
        }

        [Test]
        public void ShotsNeverRunIntoTheSnap()
        {
            foreach (float room in new[] { 0f, 0.9f, 1.4f, 1.7f, 2.5f, 3.4f, 12f })
            {
                var c = Ctx(); c.PreSnapSeconds = room; c.FirstPlayOfDrive = true; c.DriveIndex = 2;
                float total = ShotRules.Total(ShotRules.PreSnap(c));
                Assert.LessOrEqual(total, System.Math.Max(0f, room - ShotRules.MinLiveBeforeSnap) + 1e-4f, "room " + room);
                Assert.LessOrEqual(total, ShotRules.MaxPreSnapShot + 1e-4f);
            }
            var tight = Ctx(); tight.PreSnapSeconds = 1.2f; tight.GameStart = true;
            Assert.AreEqual(0, ShotRules.PreSnap(tight).Count, "too little time before the snap: no shot at all");
        }

        [Test]
        public void EstablishingShotsOpenTheGameAndDrives()
        {
            var start = Ctx(); start.GameStart = true; start.Kickoff = true;
            Assert.AreEqual(ShotKind.SkySwoop, ShotRules.PreSnap(start)[0].Kind);
            var kinds = new HashSet<ShotKind>();
            for (int drive = 0; drive < 3; drive++) { var c = Ctx(); c.FirstPlayOfDrive = true; c.DriveIndex = drive; var l = ShotRules.PreSnap(c); Assert.AreEqual(1, l.Count); kinds.Add(l[0].Kind); }
            CollectionAssert.AreEquivalent(new[] { ShotKind.SkySwoop, ShotKind.SidelineQb, ShotKind.EndZone }, kinds);
            Assert.AreEqual(0, ShotRules.PreSnap(Ctx()).Count, "a normal snap gets no establishing shot");
            var minimal = Ctx(1); minimal.FirstPlayOfDrive = true; Assert.AreEqual(0, ShotRules.PreSnap(minimal).Count);
            var half = Ctx(1); half.FirstPlayOfHalf = true; Assert.AreEqual(ShotKind.SkySwoop, ShotRules.PreSnap(half)[0].Kind);
        }

        [Test]
        public void PostPlayCutsFollowTheOutcome()
        {
            var td = Ctx(); td.Touchdown = true; td.Score = true; td.HomeBenefits = true; td.CarrierKnown = true;
            var l = ShotRules.PostPlay(td);
            Assert.AreEqual(ShotKind.Celebration, l[0].Kind); Assert.AreEqual(ShotKind.CrowdShot, l[1].Kind);
            td.HomeBenefits = false; Assert.AreEqual(ShotKind.CoachReaction, ShotRules.PostPlay(td)[1].Kind);
            var to = Ctx(); to.Turnover = true; to.CarrierKnown = true;
            var tl = ShotRules.PostPlay(to); Assert.AreEqual(ShotKind.TightCarrier, tl[0].Kind); Assert.IsTrue(tl.Any(s => s.Kind == ShotKind.CoachReaction));
            var big = Ctx(); big.BigPlay = true; big.CarrierKnown = true; Assert.AreEqual(ShotKind.TightCarrier, ShotRules.PostPlay(big)[0].Kind);
            var inc = Ctx(); inc.Headline = Headline.Incomplete; Assert.AreEqual(0, ShotRules.PostPlay(inc).Count);
            var minimal = Ctx(1); minimal.Touchdown = true; minimal.HomeBenefits = true; minimal.CarrierKnown = true;
            var ml = ShotRules.PostPlay(minimal); Assert.AreEqual(1, ml.Count); Assert.AreEqual(ShotKind.Celebration, ml[0].Kind);
            var routine = Ctx(); routine.Headline = Headline.FirstDown; routine.CarrierKnown = true; routine.PlaysSinceCrowd = 9;
            Assert.IsTrue(ShotRules.PostPlay(routine).Any(s => s.Kind == ShotKind.CrowdShot), "a crowd shot every so often");
        }

        [Test]
        public void PostPlayShotsStayWithinTheBudget()
        {
            var c = Ctx(); c.Touchdown = true; c.Score = true; c.HomeBenefits = true; c.CarrierKnown = true; c.PlaysSinceCrowd = 20; c.Turnover = true; c.BigPlay = true;
            Assert.LessOrEqual(ShotRules.Total(ShotRules.PostPlay(c)), ShotRules.MaxPostPlayTotal + 1e-4f);
        }

        // ------------------------------------------------------------------------------------------- shot geometry
        [Test]
        public void ShotPosesAreFiniteAndLookAtTheirSubject()
        {
            var ctx = new ShotContext { Los = new Vector3(2f, 0f, -10f), Downfield = 1f, LiveSide = 1f, Subject = () => new Vector3(1f, 0f, -12f), Second = () => new Vector3(2f, 0f, -11f) };
            foreach (ShotKind k in System.Enum.GetValues(typeof(ShotKind)))
            {
                if (k == ShotKind.None) continue;
                for (float u = 0f; u <= 1f; u += 0.25f)
                {
                    var p = ShotLibrary.Evaluate(k, u, ctx);
                    Assert.IsFalse(float.IsNaN(p.Pos.x + p.Pos.y + p.Pos.z + p.Look.x + p.Look.y + p.Look.z), k + " produced NaN");
                    Assert.Greater(p.Fov, 10f); Assert.Less(p.Fov, 80f);
                    Assert.Greater(p.Pos.y, 0.5f, k + " is above ground");
                    Assert.Greater((p.Look - p.Pos).magnitude, 1f);
                }
            }
        }

        [Test]
        public void ReplayAnglesAreFiniteForEveryPreset()
        {
            var clip = new ReplayClip { TowardsPlusZ = true };
            for (int s = 0; s < 3; s++) clip.Layouts[s] = new ReplayLayout { BoneCount = 1, PosBones = new int[0] };
            for (int i = 0; i < 22; i++) clip.Agents[i] = new ReplayAgent { Size = 1 };
            clip.BuildOffsets();
            for (int f = 0; f < 20; f++) { var fr = new ReplayFrame(clip.RotCount, clip.PosCount) { T = f * 0.1f, BallVisible = true, BallPos = new Vector3(f * 0.3f, 1f, f) }; for (int a = 0; a < 22; a++) fr.Pos[a] = new Vector3(a - 11, 0f, f + a * 0.2f); clip.Frames.Add(fr); }
            var v = new ReplayView();
            foreach (ReplayAngle angle in System.Enum.GetValues(typeof(ReplayAngle)))
                foreach (int target in new[] { -1, 0, 7, 15 })
                {
                    v.Reset(clip, target, angle);
                    for (float t = 0f; t < 1.9f; t += 0.3f)
                    {
                        var p = v.Compute(clip, t, 0.016f);
                        Assert.IsFalse(float.IsNaN(p.Pos.x + p.Pos.y + p.Pos.z), angle + "/" + target);
                        Assert.Greater(p.Pos.y, 0.3f);
                        if (p.Focus > 0f) Assert.Greater(p.Focus, 0.5f);
                    }
                }
            // switching a preset to the free camera keeps the view (no jump)
            v.Reset(clip, 0, ReplayAngle.High); var before = v.Compute(clip, 0.5f, 0.016f);
            v.ToFree(); var after = v.Compute(clip, 0.5f, 0.016f);
            Assert.Less((before.Pos - after.Pos).magnitude, 2.5f);
        }
    }
}
