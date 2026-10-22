using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>The college consistency checks that run inside <see cref="CareerValidator"/>.</summary>
    public class CollegeValidatorTests
    {
        [Test]
        public void TheCollegeChecksPassInEveryPhaseOfTheYear()
        {
            CareerSave s = CollegeTestUtil.Career(111);
            var seen = new HashSet<CareerPhase>();
            int guard = 0;
            while (guard++ < 60 && !(s.Year == s.StartYear + 1 && s.Phase == CareerPhase.Preseason))
            {
                if (seen.Add(s.Phase) || s.Phase == CareerPhase.Draft && s.Draft.Current > 5)
                    Assert.AreEqual(0, CollegeValidator.Check(s).Count, s.Phase + ": " + string.Join("; ", CollegeValidator.Check(s).Take(5).ToArray()));
                if (s.Phase == CareerPhase.Draft && s.Draft.Current == 0) CareerDraft.SimToEnd(s, s.NewRng().Fork());   // picks made, the college not yet turned over
                if (s.Phase == CareerPhase.Draft && s.Draft.Finished) Assert.AreEqual(0, CollegeValidator.Check(s).Count, "a finished draft before the college turns over: " + string.Join("; ", CollegeValidator.Check(s).Take(4).ToArray()));
                CareerFlow.AutoResolveUser(s);
                Assert.IsTrue(CareerFlow.Advance(s).Ok);
            }
            Assert.AreEqual(12, seen.Count);
            CareerTestUtil.AssertClean(s, false);
        }

        [Test]
        public void DamageToTheCollegeWorldIsReported()
        {
            CareerSave s = CollegeTestUtil.Career(112);
            Assert.AreEqual(0, CollegeValidator.Check(s).Count);
            School sc = s.College.Schools[3];
            Player gone = sc.Team.Roster[10];
            sc.Team.RemovePlayer(gone);
            Assert.IsTrue(CollegeValidator.Check(s).Any(p => p.Contains("roster of 84")));
            sc.Team.AddPlayer(gone);
            Assert.AreEqual(0, CollegeValidator.Check(s).Count);
            // a collegian who is also on a pro roster
            Player clone = s.College.Schools[4].Team.Roster[2];
            s.Pro.FreeAgents.Add(clone);
            Assert.IsTrue(CollegeValidator.Check(s).Any(p => p.Contains("is also a pro")));
            s.Pro.FreeAgents.Remove(clone);
            // a scouting department that is too small
            var scouts = CollegeScouting.TeamScouts(s, 7);
            s.Net.Scouts.Remove(scouts[2]); s.Net.Scouts.Remove(scouts[1]);
            Assert.IsTrue(CollegeValidator.Check(s).Any(p => p.Contains("team 7 has")));
            // and the checks are part of the career validator
            Assert.IsTrue(CareerValidator.Check(s, false).Any(p => p.Contains("team 7 has")));
        }
    }
}
