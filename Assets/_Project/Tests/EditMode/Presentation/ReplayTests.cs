using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Presentation;
using PixFootball.Sim;
using UnityEngine;
using Team = PixFootball.Core.Team;

namespace PixFootball.Tests
{
    public class ReplayTests
    {
        static float AngleBetween(Quaternion a, Quaternion b) { return Quaternion.Angle(a, b); }

        [Test]
        public void QuaternionPackingStaysWithinAFractionOfADegree()
        {
            var rnd = new System.Random(7);
            float worst = 0f;
            for (int i = 0; i < 4000; i++)
            {
                var q = Random.rotation;
                if (i < 10) q = new[] { Quaternion.identity, new Quaternion(0, 0, 0, -1), new Quaternion(1, 0, 0, 0), new Quaternion(0, -1, 0, 0), Quaternion.Euler(90, 0, 0), Quaternion.Euler(0, 180, 0), Quaternion.Euler(0, 0, 180), Quaternion.Euler(-120, 33, 70), Quaternion.Euler(10, 10, 10), Quaternion.Euler(179, 0, 0) }[i];
                var r = QuatPack.Unpack(QuatPack.Pack(q));
                worst = Mathf.Max(worst, AngleBetween(q, r));
            }
            Assert.Less(worst, 0.35f, "worst packing error in degrees");
        }

        [Test]
        public void PackingIsDeterministicAndCompact()
        {
            var q = Quaternion.Euler(33f, -71f, 120f);
            Assert.AreEqual(QuatPack.Pack(q), QuatPack.Pack(q));
            Assert.AreEqual(4, sizeof(uint));
        }

        static ReplayClip MakeClip(int frames, float step, float t0 = -1f)
        {
            var clip = new ReplayClip();
            for (int s = 0; s < 3; s++) clip.Layouts[s] = new ReplayLayout { BoneCount = 3, PosBones = new[] { 0 } };
            for (int i = 0; i < 22; i++) clip.Agents[i] = new ReplayAgent { Size = (byte)(i % 3), Side = (byte)(i < 11 ? 0 : 1), Slot = (byte)i, Position = "WR", Name = "Test Player" + i, Number = i };
            clip.BuildOffsets();
            for (int f = 0; f < frames; f++)
            {
                var fr = new ReplayFrame(clip.RotCount, clip.PosCount) { T = t0 + f * step, BallPos = new Vector3(f * 0.5f, 1f, f), BallVisible = true };
                for (int a = 0; a < 22; a++) { fr.Pos[a] = new Vector3(a, 0f, f * (1f + a * 0.01f)); fr.Yaw[a] = f * 2f; }
                for (int k = 0; k < fr.Rot.Length; k++) fr.Rot[k] = QuatPack.Pack(Quaternion.Euler(f, k, 0));
                clip.Frames.Add(fr);
            }
            clip.EndTime = clip.Frames[clip.Frames.Count - 1].T;
            return clip;
        }

        [Test]
        public void LocateFindsTheFrameAndBlend()
        {
            var clip = MakeClip(31, 1f / 30f);
            int i; float a;
            clip.Locate(-5f, out i, out a); Assert.AreEqual(0, i); Assert.AreEqual(0f, a);
            clip.Locate(100f, out i, out a); Assert.AreEqual(30, i); Assert.AreEqual(0f, a);
            clip.Locate(-1f + 10f / 30f + 0.5f / 30f, out i, out a); Assert.AreEqual(10, i); Assert.AreEqual(0.5f, a, 0.01f);
            Assert.AreEqual(1f, clip.Duration, 0.001f);
        }

        [Test]
        public void SamplingInterpolatesPositionsAndYaw()
        {
            var clip = MakeClip(11, 0.1f, 0f);
            Vector3 p = clip.AgentPos(5, 0.25f);
            Assert.AreEqual(5f, p.x, 1e-4f);
            Assert.AreEqual(0.25f * 10f * (1f + 5 * 0.01f), p.z, 0.02f);   // frame index is time / 0.1, z grows by (1 + a*0.01) per frame
            Assert.AreEqual(5f, clip.AgentYaw(3, 0.25f), 0.01f);
            Vector3 b = clip.BallPosAt(0.5f); Assert.AreEqual(2.5f, b.x, 1e-3f);
        }

        [Test]
        public void SamplingTheSameClipTwiceGivesTheSamePose()
        {
            // playback is a pure function of (clip, time): scrubbing back and forth reproduces exactly the same samples
            var clip = MakeClip(40, 1f / 30f);
            var first = new List<Vector3>(); var second = new List<Vector3>();
            float[] times = { -0.9f, -0.2f, 0f, 0.37f, 0.9f, 0.37f, 0f, -0.2f };
            foreach (float t in times) first.Add(clip.AgentPos(7, t) + clip.BallPosAt(t));
            foreach (float t in times) second.Add(clip.AgentPos(7, t) + clip.BallPosAt(t));
            CollectionAssert.AreEqual(first, second);
            Assert.AreEqual(first[3], first[5]);
            Assert.AreEqual(first[2], first[6]);
        }

        [Test]
        public void RecordedPlaybackReproducesTheDeterministicSimulationPositions()
        {
            // record a headless play at 30 Hz exactly the way the recorder samples the view, then play it back: the sampled positions equal the simulation's at every recorded tick
            var a = RecordPlay(11); var b = RecordPlay(11); var c = RecordPlay(12);
            Assert.AreEqual(a.Frames.Count, b.Frames.Count);
            for (int i = 0; i < a.Frames.Count; i += 7) Assert.AreEqual(a.Frames[i].Pos[0], b.Frames[i].Pos[0]);
            Assert.AreEqual(a.AgentPos(0, a.Frames[10].T), a.Frames[10].Pos[0]);
            bool differs = a.Frames.Count != c.Frames.Count; for (int i = 0; i < Mathf.Min(a.Frames.Count, c.Frames.Count) && !differs; i++) differs |= a.Frames[i].Pos[3] != c.Frames[i].Pos[3];
            Assert.IsTrue(differs, "a different seed is a different play");
        }

        static ReplayClip RecordPlay(int seed)
        {
            var cfg = new MatchConfig { QuarterMinutes = 5, RunoffScale = 0.5f, CpuVsCpu = true, Seed = seed };
            var flow = new MatchFlow(cfg, MatchData.TeamByKey("buffalo_ironhorns"), MatchData.TeamByKey("miami_reefrunners"), Team.Away, new Rng(seed));
            flow.PrepareSnap();
            Play off, def; flow.CpuCalls(out off, out def);
            var sim = flow.CreateSim(off, def, true);
            var frame = PlayFrame.For(flow.State);
            var clip = new ReplayClip { TowardsPlusZ = frame.TowardsPlusZ };
            for (int s = 0; s < 3; s++) clip.Layouts[s] = new ReplayLayout { BoneCount = 1, PosBones = new int[0] };
            for (int i = 0; i < 22; i++) clip.Agents[i] = new ReplayAgent { Size = 1 };
            clip.BuildOffsets();
            float t = 0f, next = 0f; int guard = 0;
            while (sim.Phase != PlayPhase.Dead && guard++ < 60 * 40)
            {
                sim.Tick(1f / 60f, default(ControlInput), -1);
                t += 1f / 60f;
                if (t >= next)
                {
                    next += 1f / 30f;
                    var fr = new ReplayFrame(clip.RotCount, clip.PosCount) { T = t };
                    for (int i = 0; i < 22; i++) { fr.Pos[i] = frame.ToUnity(sim.Agents[i].Pos); fr.Yaw[i] = 0f; }
                    fr.BallPos = frame.ToUnity(sim.Ball.Pos);
                    clip.Frames.Add(fr);
                }
            }
            clip.EndTime = clip.Frames.Count > 0 ? clip.Frames[clip.Frames.Count - 1].T : 0f;
            return clip;
        }

        [Test]
        public void LibraryKeepsRecentPlaysAndTheMostExcitingOnes()
        {
            var lib = new ReplayLibrary { KeepRecent = 3, KeepHighlights = 2, MaxBytes = long.MaxValue };
            float[] scores = { 90f, 10f, 20f, 70f, 5f, 5f, 8f, 6f, 7f };
            for (int i = 0; i < scores.Length; i++) { var c = MakeClip(6, 0.1f); c.Excitement = scores[i]; c.Quarter = 1 + i / 5; lib.Add(c); }
            Assert.AreEqual(5, lib.Count);
            var ids = new List<float>(); foreach (var c in lib.Clips) ids.Add(c.Excitement);
            CollectionAssert.Contains(ids, 90f); CollectionAssert.Contains(ids, 70f);        // the best two survive
            CollectionAssert.Contains(ids, 7f); CollectionAssert.Contains(ids, 6f); CollectionAssert.Contains(ids, 8f);   // the three newest
            Assert.AreEqual(lib.Latest.Excitement, 7f);
            var top = lib.Top(2);
            Assert.AreEqual(2, top.Count); Assert.AreEqual(90f, top[0].Excitement); Assert.AreEqual(70f, top[1].Excitement);   // game order
        }

        [Test]
        public void LibraryHonoursTheMemoryBudget()
        {
            var lib = new ReplayLibrary { KeepRecent = 2, KeepHighlights = 20, MaxBytes = 3 * MakeClip(50, 0.03f).Bytes };
            for (int i = 0; i < 12; i++) { var c = MakeClip(50, 0.03f); c.Excitement = i; lib.Add(c); }
            Assert.LessOrEqual(lib.TotalBytes, lib.MaxBytes + 1);
            Assert.GreaterOrEqual(lib.Count, 2);
        }

        [Test]
        public void RecordingIsCheap()
        {
            // 22 players x about 100 animated transforms per sample: one frame is under 10 KB and a ten second play under 3 MB
            var clip = new ReplayClip();
            for (int s = 0; s < 3; s++) clip.Layouts[s] = new ReplayLayout { BoneCount = 100, PosBones = new[] { 0, 1 } };
            for (int i = 0; i < 22; i++) clip.Agents[i] = new ReplayAgent { Size = (byte)(i % 3) };
            clip.BuildOffsets();
            for (int f = 0; f < 300; f++) clip.Frames.Add(new ReplayFrame(clip.RotCount, clip.PosCount));
            Assert.Less(clip.Bytes / 300, 11000);
            Assert.Less(clip.Bytes, 3L * 1024 * 1024);
        }
    }
}
