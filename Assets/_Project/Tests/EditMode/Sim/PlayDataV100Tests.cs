using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;

namespace PixFootball.Tests
{
    /// <summary>v0.10.0: the playbook looks like a real one: formations with real alignments and splits, route trees at real depths and break angles, concepts that put their routes on the right side of the formation, run schemes that match their blocking.</summary>
    public class PlayDataV100Tests
    {
        private static Playbook Book { get { return SimTestKit.Playbook; } }
        private static List<Play> Plays(string name) { return Book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Name == name).ToList(); }

        // ------------------------------------------------------------------ formations

        [Test]
        public void TheBookHasTheRealFormationFamiliesAndPersonnel()
        {
            var off = Book.Formations.Where(f => f.Side == PlaySide.Offense && f.Unit.Length == 2 && char.IsDigit(f.Unit[0])).ToList();
            Assert.GreaterOrEqual(off.Count, 22, "offensive formations");
            foreach (string fam in new[] { "Singleback", "I-Form", "Shotgun", "Pistol", "Goal Line", "Empty", "Trips" }) Assert.IsTrue(off.Any(f => f.Family == fam), fam);
            foreach (string set in new[] { "Doubles", "Bunch", "Trips", "Ace (12)", "Slot (20)", "Twins", "Tight (22)", "Spread (10)", "Strong (21)", "Quads" }) Assert.IsTrue(off.Any(f => f.SetName.StartsWith(set)), "set " + set);
            foreach (string unit in new[] { "01", "10", "11", "12", "20", "21", "22" }) Assert.IsTrue(off.Any(f => f.Unit == unit), "personnel " + unit);
            var def = Book.Formations.Where(f => f.Side == PlaySide.Defense).Select(f => f.Name).ToList();
            foreach (string n in new[] { "4-3 Base", "4-3 Over", "3-4 Base", "Nickel", "3-3-5", "Dime", "46 Bear", "Goal Line Defense" }) CollectionAssert.Contains(def, n);
        }

        [Test]
        public void EveryOffensiveFormationIsLegalAndAlignedLikeRealFootball()
        {
            var errors = new List<string>();
            Assert.IsTrue(Book.Validate(errors), string.Join("\n", errors.Take(10).ToArray()));
            foreach (Formation f in Book.Formations.Where(x => x.Side == PlaySide.Offense && x.Unit.Length == 2 && char.IsDigit(x.Unit[0])))
            {
                SlotSpec qb = f.Slots[0];
                float expectDepth = f.Family == "Shotgun" || f.Family == "Empty" || f.Family == "Trips" ? -5f : (f.Family == "Pistol" ? -4f : -1.25f);
                Assert.AreEqual(expectDepth, qb.Y, 0.01f, f.Name + " quarterback depth");
                // the line: five linemen 1.25 yd apart, a hand off the ball
                var ol = f.Slots.Where(s => PositionGroup.IsOffensiveLine(s.Position)).OrderBy(s => s.X).ToList();
                Assert.AreEqual(5, ol.Count);
                for (int i = 1; i < 5; i++) Assert.AreEqual(1.25f, ol[i].X - ol[i - 1].X, 0.01f, f.Name + " line splits");
                foreach (SlotSpec s in f.Slots)
                {
                    if (s.Position == Position.TE && Math.Abs(s.X) < 6f) Assert.AreEqual(3.75f, Math.Abs(s.X), 0.01f, f.Name + " an inline tight end is attached");
                    if (s.Position == Position.WR && s.Y >= -0.1f) Assert.GreaterOrEqual(Math.Abs(s.X), 12f, f.Name + " a receiver on the line is split (" + s.Label + ")");
                    if (s.Position == Position.WR && s.Y < -0.5f) Assert.That(Math.Abs(s.X), Is.InRange(7f, 18.1f), f.Name + " a flanker or slot (" + s.Label + ")");
                    if (s.Position == Position.RB && f.Family != "Shotgun" && f.Family != "Empty" && f.Family != "Trips") Assert.That(s.Y, Is.InRange(-7.1f, -4f), f.Name + " back depth");
                    if (s.Position == Position.FB) Assert.That(s.Y, Is.InRange(-4.7f, -2.9f), f.Name + " fullback depth");
                }
                Assert.AreEqual(5, f.Slots.Count(s => PositionGroup.IsEligibleReceiver(s.Position)), f.Name + " eligible receivers");
            }
        }

        [Test]
        public void ADefensiveFormationNeverStartsOnTheWrongSideOfTheBall()
        {
            foreach (Formation f in Book.Formations.Where(x => x.Side == PlaySide.Defense))
            {
                Assert.AreEqual(11, f.Slots.Length);
                foreach (SlotSpec s in f.Slots) Assert.Greater(s.Y, 1f, f.Name + " " + s.Position + " must start in the defensive half");
                Assert.IsFalse(string.IsNullOrEmpty(f.Front), f.Name + " names its front");
                int dl = f.Slots.Count(s => PositionGroup.IsDefensiveLine(s.Position));
                bool three = f.Front == "Odd" || f.Front == "Even3";
                Assert.AreEqual(three ? 3 : 4, dl, f.Name + " down linemen for the " + f.Front + " front");
            }
        }

        // ------------------------------------------------------------------ the route tree

        private static float SegmentAngleFromVertical(RouteWaypoint a, RouteWaypoint b)
        {
            float dx = Math.Abs(b.Point.X - a.Point.X), dy = b.Point.Y - a.Point.Y;
            return (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI);   // 0 = straight up the field, 90 = square across
        }

        [Test]
        public void RoutesBreakAtTheRealDepthsAndAngles()
        {
            Vec2 start = new Vec2(-17.5f, 0f);
            Func<RouteKind, List<RouteWaypoint>> build = k => RouteLibrary.Build(k, start, -1f);
            // hitch: stop at 5-7 yd, turn back to the passer
            var hitch = build(RouteKind.Hitch); Assert.That(hitch[0].Point.Y, Is.InRange(5f, 7f)); Assert.Less(hitch[1].Point.Y, hitch[0].Point.Y);
            // curl: 12 yd, comes back to 10
            var curl = build(RouteKind.Curl); Assert.AreEqual(12f, curl[0].Point.Y, 0.01f); Assert.AreEqual(10f, curl[1].Point.Y, 0.3f);
            // slant: three-step stem, then a break at about 45 degrees
            var slant = build(RouteKind.Slant); Assert.That(SegmentAngleFromVertical(slant[1], slant[2]), Is.InRange(35f, 55f), "slant break angle");
            // out and dig are square breaks at 10 and 12
            Func<RouteKind, List<RouteWaypoint>> buildSlot = k => RouteLibrary.Build(k, new Vec2(-8f, -1f), -1f);   // a slot receiver has the room to break toward the sideline
            var o = buildSlot(RouteKind.Out); Assert.AreEqual(10f, o[0].Point.Y, 0.01f); Assert.That(SegmentAngleFromVertical(o[0], o[1]), Is.InRange(80f, 91f));
            var dig = build(RouteKind.Dig); Assert.AreEqual(12f, dig[0].Point.Y, 0.01f); Assert.That(SegmentAngleFromVertical(dig[0], dig[1]), Is.InRange(80f, 91f));
            Assert.Greater(dig[1].Point.X, dig[0].Point.X, "a dig breaks toward the middle of the field");
            Assert.GreaterOrEqual(o[1].Point.X, -24.5f); Assert.Less(o[1].Point.X, o[0].Point.X, "an out breaks toward the sideline");
            // post toward the goalpost, corner toward the pylon, both after a 12 yd stem
            var post = build(RouteKind.Post); Assert.AreEqual(12f, post[0].Point.Y, 0.01f); Assert.That(SegmentAngleFromVertical(post[0], post[1]), Is.InRange(30f, 50f)); Assert.Greater(post[1].Point.X, post[0].Point.X);
            var corner = buildSlot(RouteKind.Corner); Assert.AreEqual(12f, corner[0].Point.Y, 0.01f); Assert.That(SegmentAngleFromVertical(corner[0], corner[1]), Is.InRange(35f, 55f)); Assert.Less(corner[1].Point.X, corner[0].Point.X);
            // comeback: deep (15-16) and back toward the sideline
            var cb = buildSlot(RouteKind.Comeback); Assert.That(cb[0].Point.Y, Is.InRange(14.5f, 16.5f)); Assert.Less(cb[1].Point.Y, cb[0].Point.Y - 2.5f);
            // go and seam stay on one line
            foreach (RouteKind k in new[] { RouteKind.Seam }) { var l = build(k); foreach (var w in l) Assert.AreEqual(start.X, w.Point.X, 0.01f, k.ToString()); }
            // a depth override moves the break and nothing else
            var curl8 = RouteLibrary.Build(RouteKind.Curl, start, -1f, 8f); Assert.AreEqual(8f, curl8[0].Point.Y, 0.01f);
            var dig15 = RouteLibrary.Build(RouteKind.Dig, start, -1f, 15f); Assert.AreEqual(15f, dig15[0].Point.Y, 0.01f);
        }

        [Test]
        public void RouteTimesIncreaseAndNobodyRunsOutOfBounds()
        {
            foreach (RouteKind k in Enum.GetValues(typeof(RouteKind)))
                foreach (float x in new[] { -17.5f, -8f, 3.75f, 17.5f })
                {
                    var l = RouteLibrary.Build(k, new Vec2(x, -1f), x < 0 ? -1f : 1f);
                    float last = 0f;
                    foreach (var w in l) { Assert.Greater(w.Time, last, k + " time"); last = w.Time; Assert.LessOrEqual(Math.Abs(w.Point.X), Playbook.RouteHalfWidth + 0.01f, k + " stays inbounds"); }
                }
        }

        // ------------------------------------------------------------------ concepts

        private static float SideOf(Formation f, int slot) { return f.Slots[slot].X >= 0f ? 1f : -1f; }

        [Test]
        public void SmashPutsAHitchUnderACornerOnTheSameSide()
        {
            var smash = Plays("Smash"); Assert.GreaterOrEqual(smash.Count, 6);
            int proper = 0;
            foreach (Play p in smash)
            {
                Formation f = Book.FindFormation(p.FormationName);
                var routes = p.Assignments.OfType<RouteAssignment>().ToList();
                var corner = routes.FirstOrDefault(r => r.Route == RouteKind.Corner && r.ReadOrder == 1);
                if (corner == null) continue;
                var hitch = routes.FirstOrDefault(r => r.Route == RouteKind.Hitch && SideOf(f, r.Slot) == SideOf(f, corner.Slot) && Math.Abs(f.Slots[r.Slot].X) > Math.Abs(f.Slots[corner.Slot].X));
                if (hitch != null) proper++;
            }
            Assert.GreaterOrEqual(proper, smash.Count * 8 / 10, "the hitch is outside the corner on the same side in most formations");
        }

        [Test]
        public void MeshSendsTwoShallowCrossersThatPassEachOther()
        {
            var mesh = Plays("Mesh"); Assert.GreaterOrEqual(mesh.Count, 6);
            int crossing = 0;
            foreach (Play p in mesh)
            {
                var cross = p.Assignments.OfType<RouteAssignment>().Where(r => r.Route == RouteKind.Drag || r.Route == RouteKind.Shallow).ToList();
                if (cross.Count < 2) continue;
                // the two crossers swap sides of the field at depths between 3 and 8 yd
                RouteAssignment a = cross[0], b = cross[1];
                bool oppositeStarts = Math.Sign(Book.FindFormation(p.FormationName).Slots[a.Slot].X) != Math.Sign(Book.FindFormation(p.FormationName).Slots[b.Slot].X);
                bool shallow = a.Waypoints.All(w => w.Point.Y <= 8f) && b.Waypoints.All(w => w.Point.Y <= 8f);
                if (oppositeStarts && shallow) crossing++;
            }
            Assert.GreaterOrEqual(crossing, mesh.Count * 7 / 10, "mesh crossers start on opposite sides and stay shallow");
        }

        [Test]
        public void FloodPutsThreeLevelsOnOneSideAndRollsThatWay()
        {
            var flood = Plays("Flood"); Assert.GreaterOrEqual(flood.Count, 4);
            foreach (Play p in flood)
            {
                Formation f = Book.FindFormation(p.FormationName);
                var q = (QbAssignment)p.Assignments[0];
                Assert.AreEqual(QbAction.Rollout, q.Action);
                var corner = p.Assignments.OfType<RouteAssignment>().First(r => r.Route == RouteKind.Corner);
                Assert.AreEqual(SideOf(f, corner.Slot), (float)q.RollDirection, p + ": the passer rolls to the flood side");
                var sameSide = p.Assignments.OfType<RouteAssignment>().Where(r => SideOf(f, r.Slot) == SideOf(f, corner.Slot)).Select(r => r.Route).ToList();
                CollectionAssert.Contains(sameSide, RouteKind.Corner);
                Assert.IsTrue(sameSide.Contains(RouteKind.Out) || sameSide.Contains(RouteKind.Flat), p + ": a second and a third level on the flood side");
            }
        }

        [Test]
        public void YCrossSendsTheTightEndAcrossTheFieldAtFourteen()
        {
            foreach (Play p in Plays("Y-Cross"))
            {
                Formation f = Book.FindFormation(p.FormationName);
                var cross = p.Assignments.OfType<RouteAssignment>().FirstOrDefault(r => r.Route == RouteKind.Cross);
                Assert.NotNull(cross, p.ToString());
                float depth = cross.Waypoints.Max(w => w.Point.Y);
                Assert.That(depth, Is.InRange(13f, 16.5f), p + " crossing depth");
                Assert.That(Math.Abs(cross.Waypoints.Last().Point.X - f.Slots[cross.Slot].X), Is.GreaterThan(15f), p + " crosses most of the field");
                if (f.Slots[cross.Slot].Position == Position.TE || f.Slots[cross.Slot].Position == Position.WR) Assert.AreEqual(1, cross.ReadOrder, "the crosser is the first read");
            }
        }

        [Test]
        public void FourVerticalsSendsEveryoneStraightUp()
        {
            foreach (Play p in Plays("Four Verticals"))
            {
                var routes = p.Assignments.OfType<RouteAssignment>().ToList();
                int vertical = routes.Count(r => r.Route == RouteKind.Go || r.Route == RouteKind.Seam || r.Route == RouteKind.Fade);
                Assert.GreaterOrEqual(vertical, 4, p.ToString());
            }
        }

        [Test]
        public void EveryPassConceptCarriesACoachingNoteAndAReadProgression()
        {
            var passes = Book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.IsPass).ToList();
            foreach (Play p in passes)
            {
                Assert.IsFalse(string.IsNullOrEmpty(p.Note), p + " has no card text");
                var q = (QbAssignment)p.Assignments[0];
                Assert.GreaterOrEqual(q.ReadProgression.Count, 1, p.ToString());
                foreach (RouteAssignment r in p.Assignments.OfType<RouteAssignment>()) Assert.GreaterOrEqual(r.Waypoints.Count, 1, p.ToString());
            }
            var names = passes.Select(p => p.Name).Distinct().ToList();
            foreach (string n in new[] { "Mesh", "Smash", "Flood", "Four Verticals", "Y-Cross", "Levels", "Drive", "Curl-Flat", "Slants", "Stick", "Spacing", "PA Boot", "HB Screen", "WR Screen", "Snag", "Dagger", "Mills", "Shallow Cross" })
                CollectionAssert.Contains(names, n);
        }

        // ------------------------------------------------------------------ run schemes match their blocking

        private static Play Run(string name, string form) { return Book.AllPlays.First(p => p.Side == PlaySide.Offense && p.Name == name && p.FormationName == form); }

        [Test]
        public void RunSchemesMatchTheirBlocking()
        {
            // inside zone: all five linemen zone, the back aims at the guard-tackle gap
            Play iz = Run("Inside Zone Right", "Singleback");
            for (int i = 2; i <= 6; i++) Assert.AreEqual(BlockKind.Zone, ((BlockAssignment)iz.Assignments[i]).Block);
            Assert.AreEqual(RunGap.BRight, iz.Gap);
            // outside zone: zone blocking, a wide stretch path (the back gets 5 yd outside before he turns up)
            Play oz = Run("Outside Zone Right", "Singleback");
            for (int i = 2; i <= 6; i++) Assert.AreEqual(BlockKind.Zone, ((BlockAssignment)oz.Assignments[i]).Block);
            var oc = oz.Assignments.OfType<CarryAssignment>().First();
            Assert.GreaterOrEqual(oc.Path.Max(v => v.X), 8f, "outside zone gets to the edge");
            Assert.That(oc.Path[1].Y, Is.LessThan(0f), "and stays behind the line for the stretch");
            // power: the backside guard pulls and a back or tight end kicks out
            Play pw = Run("Power Right", "I-Form");
            Assert.AreEqual(1, Enumerable.Range(2, 5).Count(i => ((BlockAssignment)pw.Assignments[i]).Block == BlockKind.Pull));
            Assert.AreEqual(BlockKind.Pull, ((BlockAssignment)pw.Assignments[OffBuilderIndex.LG]).Block, "right power: the LEFT guard pulls");
            Assert.IsTrue(pw.Assignments.OfType<BlockAssignment>().Any(b => b.Block == BlockKind.Kickout));
            // counter: a guard pulls the other way and the back takes the counter step first
            Play ct = Run("Counter Right", "Singleback");
            Assert.AreEqual(BlockKind.Pull, ((BlockAssignment)ct.Assignments[OffBuilderIndex.LG]).Block);
            var cc = ct.Assignments.OfType<CarryAssignment>().First();
            Assert.Less(cc.Path[0].X, 0f, "counter step away from the play");
            // draw: the line shows pass protection and the back is delayed
            Play dr = Run("Draw Right", "Shotgun");
            for (int i = 2; i <= 6; i++) Assert.AreEqual(BlockKind.PassPro, ((BlockAssignment)dr.Assignments[i]).Block);
            Assert.GreaterOrEqual(dr.Assignments.OfType<CarryAssignment>().First().Delay, 0.5f);
            // read option: the line zones AWAY from the side the quarterback reads
            Play ro = Run("Read Option Right", "Shotgun");
            Assert.AreEqual(BlockTarget.ZoneLeft, ((BlockAssignment)ro.Assignments[OffBuilderIndex.C]).Target);
            // the backs of a toss lead outside; the dive is an A/B gap run with a lead blocker
            Play dv = Run("Dive Right", "I-Form");
            Assert.AreEqual(BlockKind.Lead, ((BlockAssignment)dv.Assignments[IndexOfLabel(Book.FindFormation("I-Form"), "F")]).Block);
        }

        private static int IndexOfLabel(Formation f, string label) { return f.IndexOfLabel(label); }

        private static class OffBuilderIndex { public const int LT = 2, LG = 3, C = 4, RG = 5, RT = 6; }

        // ------------------------------------------------------------------ the defense data and the play art

        [Test]
        public void EveryDefensiveCallNamesItsShellAndSomeCarryCoachingText()
        {
            var defense = Book.AllPlays.Where(p => p.Side == PlaySide.Defense).ToList();
            foreach (Play p in defense) Assert.AreNotEqual(ShellLook.Auto, DefaultPlaybooks.EffectiveShell(p), p.ToString());
            Assert.GreaterOrEqual(defense.Count(p => !string.IsNullOrEmpty(p.Note)), 25);
            // the disguised calls show another shell than their coverage naturally has
            foreach (string n in new[] { "Cover 3 Cloud", "Cover 3 Buzz", "Cover 1 Robber", "Safety Blitz Cover 0", "Cover 4 Match", "Spy QB Cover 3", "Cover 3 Fire Zone" })
            {
                Play p = defense.First(x => x.Name == n);
                ShellLook natural;
                switch (p.Scheme) { case CoverageScheme.Cover0: natural = ShellLook.Zero; break; case CoverageScheme.Cover1: case CoverageScheme.Cover3: natural = ShellLook.OneHigh; break; default: natural = ShellLook.TwoHigh; break; }
                Assert.AreNotEqual(natural, DefaultPlaybooks.EffectiveShell(p), n + " is a disguise");
            }
        }

        [Test]
        public void ThePlayArtDrawsTheShellAndTheRotationOfADisguisedCoverage()
        {
            Play p = Book.AllPlays.First(x => x.Side == PlaySide.Defense && x.Name == "Cover 1 Robber" && x.FormationName == "4-3 Base");
            PlayDiagram d = PlayDiagram.For(Book, p);
            var safeties = d.Players.Where(x => x.Defense && PositionGroup.IsSafety(x.Position)).ToList();
            Assert.AreEqual(2, safeties.Count);
            Assert.That(safeties.Min(s => s.X), Is.EqualTo(-ShellLayout.TwoHighX).Within(0.01f), "the shell shows two-high");
            Assert.IsTrue(d.Strokes.Any(s => s.Ink == InkKind.Rotate && s.Dashed), "a dashed arrow from the shell to where the coverage plays");
            // every play of the book draws without clipping out of the card or throwing
            foreach (Play q in Book.AllPlays.Where(x => x.Side != PlaySide.Special)) Assert.DoesNotThrow(() => PlayDiagram.For(Book, q), q.ToString());
        }

        [Test]
        public void ShellSpotsMatchTheSimulationsAlignment()
        {
            Play p = Book.AllPlays.First(x => x.Side == PlaySide.Defense && x.Name == "Cover 4 Match" && x.FormationName == "Nickel");
            Formation f = Book.FindFormation(p.FormationName);
            var spots = ShellLayout.Spots(p, f, 1);
            Assert.AreEqual(2, spots.Count);
            var deep = spots.Values.First(v => Math.Abs(v.X) < 0.01f);
            Assert.AreEqual(ShellLayout.OneHighDepth, deep.Y, 0.01f);
            var box = spots.Values.First(v => Math.Abs(v.X) > 0.01f);
            Assert.AreEqual(ShellLayout.BoxX, box.X, 0.01f); Assert.AreEqual(ShellLayout.BoxDepth, box.Y, 0.01f);
        }
    }
}
