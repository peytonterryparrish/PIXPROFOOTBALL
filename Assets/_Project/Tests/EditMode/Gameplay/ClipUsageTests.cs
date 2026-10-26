using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PixFootball.Gameplay;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 COVERAGE: the clip-usage recorder (plays, seconds, systems, first play, free while disabled).</summary>
    public class ClipUsageTests
    {
        bool _was;
        [SetUp] public void SetUp() { _was = ClipUsage.Enabled; ClipUsage.Reset(); ClipUsage.Enabled = true; }
        [TearDown] public void TearDown() { ClipUsage.Reset(); ClipUsage.Enabled = _was; }

        [Test]
        public void CountsPlaysSecondsAndDistinctClips()
        {
            var a = new AnimationClip { name = "a" }; var b = new AnimationClip { name = "b" };
            ClipUsage.Marker = 3;
            ClipUsage.Note(a, "Sys1"); ClipUsage.Hold(a, "Sys1", 0.5f); ClipUsage.Hold(a, "Sys1", 0.25f);
            ClipUsage.Marker = 7;
            ClipUsage.Note(a, "Sys1"); ClipUsage.Note(a, "Sys2", 1f); ClipUsage.Note(b, "Sys1");
            var rows = ClipUsage.Rows.ToList();
            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual(2, ClipUsage.DistinctClips);
            var a1 = rows.Single(r => r.Clip == a && r.System == "Sys1");
            Assert.AreEqual(2, a1.Plays); Assert.AreEqual(0.75f, a1.Seconds, 1e-4f); Assert.AreEqual(3, a1.FirstPlay);
            Assert.AreEqual(7, rows.Single(r => r.Clip == b).FirstPlay);
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
        }

        [Test]
        public void RecordsNothingWhileDisabledOrWithoutClip()
        {
            var a = new AnimationClip { name = "a" };
            ClipUsage.Enabled = false;
            ClipUsage.Note(a, "S"); ClipUsage.Hold(a, "S", 1f);
            ClipUsage.Enabled = true;
            ClipUsage.Note(null, "S"); ClipUsage.Hold(a, "S", 0f);
            Assert.AreEqual(0, ClipUsage.Rows.Count());
            Object.DestroyImmediate(a);
        }

        [Test]
        public void SystemIsTheCallingMethodWhenNotGiven()
        {
            var a = new AnimationClip { name = "a" };
            ClipUsage.Note(a);
            Assert.AreEqual("ClipUsageTests.SystemIsTheCallingMethodWhenNotGiven", ClipUsage.Rows.Single().System);
            Object.DestroyImmediate(a);
        }
    }
}
