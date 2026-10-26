using NUnit.Framework;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 QB-2: the path helpers of a fitted take (distance, time at a distance, drop steps from the name) and the no-repeat bag over fitted rows.</summary>
    public class QbFitTests
    {
        static QbClipTable.Entry Row(string clip)
        {
            // a take that backs up 3 m in 2 s at a steady pace: 15 Hz samples of z = -1.5 t, from clip second 0.5
            var e = new QbClipTable.Entry { clip = clip, a = 0.5f, b = 2.5f, mvA = 0.5f, mvB = 2.5f };
            int n = 31; e.path = new float[n * 2];
            for (int i = 0; i < n; i++) { e.path[2 * i] = 0f; e.path[2 * i + 1] = -1.5f * (i / QbClipTable.PathRate); }
            return e;
        }

        [Test] public void PathLenIsTheStraightDistance() { var e = Row("x"); Assert.AreEqual(3f, e.PathLen(0.5f, 2.5f), 0.02f); Assert.AreEqual(1.5f, e.PathLen(1.5f, 2.5f), 0.02f); }

        [Test] public void TimeAtDistFindsTheMoment() { var e = Row("x"); Assert.AreEqual(1.5f, e.TimeAtDist(0.5f, 1.5f), 0.04f); Assert.AreEqual(e.b, e.TimeAtDist(0.5f, 9f), 1e-4f); }

        [Test] public void PathAtClampsOutsideTheWindow() { var e = Row("x"); Assert.AreEqual(0f, e.PathAt(0f).magnitude, 1e-4f); Assert.AreEqual(3f, e.PathAt(9f).magnitude, 0.02f); }

        [Test]
        public void StepsAreReadFromTheName()
        {
            Assert.AreEqual(5, Row("QB_undercenter_catch_to_ 5_step_dropback_002").Steps);
            Assert.AreEqual(7, Row("YAN__QB_7StepDropback_Walk").Steps);
            Assert.AreEqual(3, Row("YAN__QB_undercener_catch_to_3_step_dropback_002").Steps);
            Assert.AreEqual(1, Row("BGS__QB_Snap_1StepDropback").Steps);
            Assert.AreEqual(0, Row("BONES_QB_pass_bullet_001_s01").Steps);
        }

        [Test]
        public void TheBagDrawsEveryFittedRowOnceBeforeARepeat()
        {
            QbMc.ResetBags();
            var pool = new System.Collections.Generic.List<QbClipTable.Entry> { Row("a"), Row("b"), Row("c") };
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 3; i++) seen.Add(QbMc.Pick("fitTest", pool, 100 + i).clip);
            Assert.AreEqual(3, seen.Count);
        }
    }
}
