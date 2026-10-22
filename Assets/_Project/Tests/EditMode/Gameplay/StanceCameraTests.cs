using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.6: facing / stance rules (every formation, both directions), the clip facing table maths and the user camera presets.</summary>
    public class StanceCameraTests
    {
        static readonly Playbook Book = DefaultPlaybooks.Create();
        static readonly RuleSet Rules = RuleSet.Pro();

        static GameState Scrim(int spot = 35, int down = 1, int dist = 10)
        {
            var gs = new GameState(Rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = spot; gs.Down = down; gs.Distance = dist; gs.Possession = Core.Team.Home;
            return gs;
        }

        static PlaySimulation Sim(Play off, Play def, GameState gs, int seed = 5)
        {
            var sim = new PlaySimulation();
            sim.Setup(gs, off, def, null, null, Rules, new Rng(seed), new PlaySimOptions { Playbook = Book, Penalties = false });
            return sim;
        }

        static Play OffPlay(string formation) { return Book.AllPlays.First(p => p.Side == PlaySide.Offense && p.FormationName == formation && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike); }
        static Play DefPlay(string formation) { return Book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == formation); }
        static Play Special(string name) { return Book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == name); }

        static IEnumerable<PlaySimulation> AllSetups()
        {
            var off = Book.Formations.Where(f => f.Side == PlaySide.Offense).ToList();
            var def = Book.Formations.Where(f => f.Side == PlaySide.Defense).ToList();
            for (int i = 0; i < off.Count; i++) yield return Sim(OffPlay(off[i].Name), DefPlay(def[i % def.Count].Name), Scrim(off[i].Name == "Goal Line" ? 96 : 35));
            for (int j = 0; j < def.Count; j++) yield return Sim(OffPlay(off[(j * 3 + 1) % off.Count].Name), DefPlay(def[j].Name), Scrim(35));
            yield return Sim(Special("Kickoff Deep Middle"), Special("Kick Return Middle Wedge"), new GameState(Rules, Core.Team.Away));
            yield return Sim(Special("Onside Kick Short"), Special("Kick Return Left"), new GameState(Rules, Core.Team.Away));
            yield return Sim(Special("Punt Standard"), Special("Punt Return Set"), Scrim(30, 4, 8));
            yield return Sim(Special("Field Goal"), Special("Field Goal Block Middle"), Scrim(80, 4, 5));
        }

        // ------------------------------------------------------------------------------------------------- facing
        [Test]
        public void EveryFormationFacesTheOpponentBeforeTheSnap()
        {
            int n = 0;
            foreach (var sim in AllSetups())
            {
                n++;
                foreach (var a in sim.Agents)
                {
                    Vec2 exp = StanceRules.ExpectedPreSnapFacing(a.Side);
                    float ang = StanceRules.AngleDeg(a.Facing, exp);
                    Assert.LessOrEqual(ang, StanceRules.FacingToleranceDeg, sim.OffPlay.Name + " vs " + sim.DefPlay.Name + ": " + a + " faces " + a.Facing.X + "," + a.Facing.Y);
                }
            }
            Assert.Greater(n, 20);
        }

        [Test]
        public void PlayFrameMapsFacingToTheRightWorldYawInBothDirections()
        {
            var plus = new PlayFrame(true); var minus = new PlayFrame(false);
            Vector3 o1 = plus.DirToUnity(new Vec2(0f, 1f)), o2 = minus.DirToUnity(new Vec2(0f, 1f));
            Assert.AreEqual(plus.DownfieldYaw, StanceRules.YawOf(o1.x, o1.z), 0.01f);
            Assert.AreEqual(180f, Mathf.Abs(StanceRules.YawOf(o2.x, o2.z)), 0.01f);
            Assert.AreEqual(minus.DownfieldYaw, Mathf.Abs(StanceRules.YawOf(o2.x, o2.z)), 0.01f);
            // the defense faces the other way in both frames
            Vector3 d1 = plus.DirToUnity(new Vec2(0f, -1f)), d2 = minus.DirToUnity(new Vec2(0f, -1f));
            Assert.AreEqual(180f, Mathf.Abs(StanceRules.YawOf(d1.x, d1.z)), 0.01f);
            Assert.AreEqual(0f, StanceRules.YawOf(d2.x, d2.z), 0.01f);
            // handedness is kept: the offense's right (+X in the simulation) is the right hand of a player facing downfield
            foreach (var f in new[] { plus, minus })
            {
                Vector3 fwd = f.DirToUnity(new Vec2(0f, 1f)), right = f.DirToUnity(new Vec2(1f, 0f));
                Assert.AreEqual(1f, Vector3.Dot(Vector3.Cross(Vector3.up, fwd), right), 0.001f);
            }
        }

        [Test]
        public void YawMathWrapsCorrectly()
        {
            Assert.AreEqual(20f, StanceRules.YawDelta(10f, -10f), 0.001f);
            Assert.AreEqual(-20f, StanceRules.YawDelta(170f, -170f), 0.001f);
            Assert.AreEqual(180f, Mathf.Abs(StanceRules.YawDelta(90f, -90f)), 0.001f);
            Assert.AreEqual(90f, StanceRules.AngleDeg(new Vec2(1f, 0f), new Vec2(0f, 1f)), 0.01f);
            Assert.AreEqual(0f, ClipFacingTable.MeanAngle(359f, 1f, 0f), 0.01f);
        }

        // ------------------------------------------------------------------------------------------------- stances
        [Test]
        public void LinemenAndSkillPlayersGetTheRightStance()
        {
            Assert.AreEqual(StanceKind.CenterSnap, StanceRules.For(Position.C, SimSide.Offense, PlayKind.Scrimmage, "11", 0f, 0f, false, false).Kind);
            foreach (var p in new[] { Position.LT, Position.LG, Position.RG, Position.RT })
                Assert.AreEqual(StanceKind.ThreePoint, StanceRules.For(p, SimSide.Offense, PlayKind.Scrimmage, "11", p == Position.LT || p == Position.LG ? -2f : 2f, 0f, false, false).Kind, p.ToString());
            foreach (var p in new[] { Position.LE, Position.DT, Position.RE })
                Assert.AreEqual(StanceKind.ThreePoint, StanceRules.For(p, SimSide.Defense, PlayKind.Scrimmage, "4-3", 1.5f, 1.2f, false, false).Kind, p.ToString());
            Assert.AreEqual(StanceKind.QbUnderCenter, StanceRules.For(Position.QB, SimSide.Offense, PlayKind.Scrimmage, "11", 0f, -1f, false, false).Kind);
            Assert.AreEqual(StanceKind.QbShotgun, StanceRules.For(Position.QB, SimSide.Offense, PlayKind.Scrimmage, "11", 0f, -5f, true, false).Kind);
            Assert.AreEqual(StanceKind.LinebackerTwoPoint, StanceRules.For(Position.MLB, SimSide.Defense, PlayKind.Scrimmage, "4-3", 0f, 4.5f, false, false).Kind);
            Assert.AreEqual(StanceKind.SafetyDeep, StanceRules.For(Position.FS, SimSide.Defense, PlayKind.Scrimmage, "4-3", -5f, 12f, false, false).Kind);
            Assert.AreEqual(StanceKind.CornerOff, StanceRules.For(Position.CB, SimSide.Defense, PlayKind.Scrimmage, "4-3", -17f, 7f, false, false).Kind);
            Assert.AreEqual(StanceKind.CornerPress, StanceRules.For(Position.CB, SimSide.Defense, PlayKind.Scrimmage, "4-3", -17f, 1.0f, false, false).Kind);      // v0.9.0 (E-190): press only within about 1.2 yd
            Assert.AreEqual(StanceKind.CornerOff, StanceRules.For(Position.CB, SimSide.Defense, PlayKind.Scrimmage, "4-3", -17f, 2.0f, false, false).Kind);
            Assert.AreEqual(StanceKind.ReceiverSplit, StanceRules.For(Position.WR, SimSide.Offense, PlayKind.Scrimmage, "11", -17.5f, 0f, false, false).Kind);
            Assert.AreEqual(StanceKind.ReceiverSlot, StanceRules.For(Position.WR, SimSide.Offense, PlayKind.Scrimmage, "11", -8f, -1f, false, false).Kind);
            Assert.AreEqual(StanceKind.ThreePoint, StanceRules.For(Position.TE, SimSide.Offense, PlayKind.Scrimmage, "11", 4.5f, 0f, false, false).Kind);
            Assert.AreEqual(StanceKind.BackTwoPoint, StanceRules.For(Position.RB, SimSide.Offense, PlayKind.Scrimmage, "11", 0f, -6f, false, false).Kind);
            Assert.AreEqual(StanceKind.Returner, StanceRules.For(Position.RB, SimSide.Defense, PlayKind.Kickoff, "KickoffReturn", 0f, 58f, false, true).Kind);
            Assert.AreEqual(StanceKind.Kicker, StanceRules.For(Position.K, SimSide.Offense, PlayKind.Kickoff, "Kickoff", 0f, -5f, false, false).Kind);
            Assert.AreEqual(StanceKind.Gunner, StanceRules.For(Position.WR, SimSide.Offense, PlayKind.Punt, "Punt", -20f, 0f, false, false).Kind);
        }

        [Test]
        public void StancesMirrorAcrossTheBall()
        {
            // inside hand down: the offense's left tackle puts his right hand down, the right tackle his left hand down; the defense (facing the other way) mirrors it
            var lt = StanceRules.For(Position.LT, SimSide.Offense, PlayKind.Scrimmage, "11", -3f, 0f, false, false);
            var rt = StanceRules.For(Position.RT, SimSide.Offense, PlayKind.Scrimmage, "11", 3f, 0f, false, false);
            Assert.IsFalse(lt.Mirror); Assert.IsTrue(rt.Mirror);
            var le = StanceRules.For(Position.LE, SimSide.Defense, PlayKind.Scrimmage, "4-3", -4.5f, 1.2f, false, false);
            var re = StanceRules.For(Position.RE, SimSide.Defense, PlayKind.Scrimmage, "4-3", 4.5f, 1.2f, false, false);
            Assert.IsTrue(le.Mirror); Assert.IsFalse(re.Mirror);
            Assert.IsTrue(lt.IsLeftOfBall); Assert.IsFalse(rt.IsLeftOfBall);
        }

        [Test]
        public void EveryStanceHasAClipKeyOrIsStandingIdle()
        {
            foreach (StanceKind k in System.Enum.GetValues(typeof(StanceKind)))
            {
                var keys = StanceRules.ClipKeys(k);
                if (k == StanceKind.Upright) { Assert.AreEqual(0, keys.Length, k.ToString()); continue; }
                Assert.Greater(keys.Length, 0, k.ToString());
            }
        }

        [Test]
        public void EveryAgentInEveryFormationGetsAStanceKind()
        {
            foreach (var sim in AllSetups())
                foreach (var a in sim.Agents)
                {
                    var spec = StanceRules.For(a.PlayPos, a.Side, sim.Kind, a.Side == SimSide.Offense ? sim.OffForm.Unit : sim.DefForm.Unit, a.StartPos.X - sim.BallX0, a.StartPos.Y - sim.LosY, sim.OffForm.Slots[0].Y < -2.5f, a.ReturnerRole);
                    if (PositionGroup.IsOffensiveLine(a.PlayPos) && sim.Kind == PlayKind.Scrimmage) Assert.IsTrue(spec.Kind == StanceKind.CenterSnap || spec.Kind == StanceKind.ThreePoint, a.ToString());
                    if (PositionGroup.IsDefensiveLine(a.PlayPos) && sim.Kind == PlayKind.Scrimmage) Assert.IsTrue(spec.Kind == StanceKind.ThreePoint || spec.Kind == StanceKind.FourPoint || spec.Kind == StanceKind.TwoPointLine, a.ToString());
                    if (a.PlayPos == Position.QB && sim.Kind == PlayKind.Scrimmage && a.Side == SimSide.Offense) Assert.IsTrue(spec.Kind == StanceKind.QbShotgun || spec.Kind == StanceKind.QbUnderCenter, a.ToString());
                }
        }

        // ------------------------------------------------------------------------------------------------- clip facing table
        [Test]
        public void ClipFacingOffsetsTurnTheBodyBackToForward()
        {
            Assert.AreEqual(-130f, ClipFacingTable.OffsetOf(130f, 5f), 0.01f);      // Motus line stance
            Assert.AreEqual(90f, ClipFacingTable.OffsetOf(-90f, 20f), 0.01f);       // BoNES studio take
            Assert.AreEqual(0f, ClipFacingTable.OffsetOf(4f, 3f), 0.01f);           // already forward (noise)
            Assert.AreEqual(0f, ClipFacingTable.OffsetOf(170f, 120f), 0.01f);       // a spin: no single facing
            Assert.AreEqual(-30f, ClipFacingTable.OffsetOf(-330f, 0f), 0.01f);        // wraps (-330 == +30)
        }

        // ------------------------------------------------------------------------------------------------- user camera
        static UserCameraInput Pre(Vector3 dir, Vector3 los, bool defense = false) { return new UserCameraInput { ViewDir = dir, Los = los, Focus = los, PreSnap = true, Defense = defense }; }

        [Test]
        public void ClassicCameraSitsBehindTheOffenseHighAndPitchedDown()
        {
            foreach (var dir in new[] { Vector3.forward, Vector3.back })
            {
                Vector3 los = new Vector3(2f, 0f, 10f * dir.z);
                var p = UserCameraMath.Evaluate(Pre(dir, los), UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
                float behind = Vector3.Dot(p.Position - los, -dir);
                Assert.GreaterOrEqual(behind, 8f); Assert.LessOrEqual(behind, 18f, "8-18 m behind the line of scrimmage");
                Assert.GreaterOrEqual(p.Position.y, 6f); Assert.LessOrEqual(p.Position.y, 10f);
                Assert.GreaterOrEqual(p.PitchDegrees, 20f); Assert.LessOrEqual(p.PitchDegrees, 36f);
                Vector3 look = p.Rotation * Vector3.forward; look.y = 0f;
                Assert.AreEqual(1f, Vector3.Dot(look.normalized, dir), 0.001f, "looks downfield");
                Assert.Less(p.Fov, 50f);
            }
        }

        [Test]
        public void CameraPullsBackAndUpAtTheSnapAndFollowsTheCarrierFromBehind()
        {
            Vector3 d = Vector3.forward, los = new Vector3(0f, 0f, 0f);
            var pre = UserCameraMath.Evaluate(Pre(d, los), UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            var live = UserCameraMath.Evaluate(new UserCameraInput { ViewDir = d, Los = los, Focus = los, SnapTime = 2f }, UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            Assert.Greater(live.Position.y, pre.Position.y + 1f, "rises at the snap");
            Assert.Greater(Vector3.Dot(live.Position - pre.Position, -d), 2f, "pulls back at the snap");
            var noZoom = UserCameraMath.Evaluate(new UserCameraInput { ViewDir = d, Los = los, Focus = los, SnapTime = 2f }, UserCameraPreset.Classic(), 0f, 0f, 0f, 0f);
            Assert.AreEqual(pre.Position.y, noZoom.Position.y, 0.001f, "zoom-out 0 keeps the pre-snap framing");
            // the carrier: camera stays behind him along the view direction
            Vector3 carrier = new Vector3(3f, 0f, 22f);
            var f = UserCameraMath.Evaluate(new UserCameraInput { ViewDir = d, Los = los, Focus = carrier, FocusVelocity = new Vector3(0f, 0f, 7f), Follow = true, SnapTime = 4f }, UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            Assert.Less(f.Position.z, carrier.z - 6f);
            Assert.Greater(f.LookAt.z, carrier.z);
            Assert.AreEqual(3f, f.Position.x, 0.5f, "stays lined up with the carrier");
        }

        [Test]
        public void DefenseCameraSitsBehindTheDefenseLookingAtTheOffense()
        {
            // offense attacks +Z, so the defense is on the +Z side of the ball and looks toward -Z
            Vector3 view = Vector3.back, los = new Vector3(0f, 0f, 0f);
            var p = UserCameraMath.Evaluate(Pre(view, los, true), UserCameraPreset.Madden(), 0f, 0f, 0f, 1f);
            Assert.Greater(p.Position.z, los.z + 6f, "behind the defense (deeper than the line of scrimmage on the defense's side)");
            Vector3 look = p.Rotation * Vector3.forward; look.y = 0f;
            Assert.Less(look.z, -0.9f, "looks at the offense");
        }

        [Test]
        public void CameraPresetsDifferInHeightAndDistanceAndTrimsApply()
        {
            Vector3 los = Vector3.zero;
            var c = UserCameraMath.Evaluate(Pre(Vector3.forward, los), UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            var m = UserCameraMath.Evaluate(Pre(Vector3.forward, los), UserCameraPreset.Madden(), 0f, 0f, 0f, 1f);
            var w = UserCameraMath.Evaluate(Pre(Vector3.forward, los), UserCameraPreset.Wide(), 0f, 0f, 0f, 1f);
            Assert.Less(m.Position.y, c.Position.y); Assert.Greater(w.Position.y, c.Position.y);
            Assert.Less(Mathf.Abs(m.Position.z), Mathf.Abs(c.Position.z)); Assert.Greater(Mathf.Abs(w.Position.z), Mathf.Abs(c.Position.z));
            var t = UserCameraMath.Evaluate(Pre(Vector3.forward, los), UserCameraPreset.Classic(), 2f, 3f, 5f, 1f);
            Assert.AreEqual(c.Position.y + 2f, t.Position.y, 0.001f);
            Assert.AreEqual(c.Position.z - 3f, t.Position.z, 0.001f);
            Assert.AreEqual(c.Fov + 5f, t.Fov, 0.001f);
        }

        [Test]
        public void CameraPoseIsMirrorSymmetricBetweenTheTwoEndZones()
        {
            var a = UserCameraMath.Evaluate(Pre(Vector3.forward, new Vector3(3f, 0f, 12f)), UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            var b = UserCameraMath.Evaluate(Pre(Vector3.back, new Vector3(-3f, 0f, -12f)), UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            Assert.AreEqual(a.Position.x, -b.Position.x, 0.001f); Assert.AreEqual(a.Position.z, -b.Position.z, 0.001f); Assert.AreEqual(a.Position.y, b.Position.y, 0.001f);
            Assert.AreEqual(a.PitchDegrees, b.PitchDegrees, 0.01f);
        }

        // ------------------------------------------------------------------------------------------------- formation look
        [Test]
        public void LinemenLineUpTightAcrossTheNeutralZone()
        {
            foreach (var f in Book.Formations.Where(f => f.Side == PlaySide.Offense))
            {
                var ol = f.Slots.Where(s => PositionGroup.IsOffensiveLine(s.Position)).OrderBy(s => s.X).ToArray();
                for (int i = 1; i < ol.Length; i++) Assert.LessOrEqual(ol[i].X - ol[i - 1].X, 1.3f, f.Name + " OL splits");
            }
            foreach (var f in Book.Formations.Where(f => f.Side == PlaySide.Defense))
            {
                var dl = f.Slots.Where(s => PositionGroup.IsDefensiveLine(s.Position)).ToArray();
                foreach (var s in dl) Assert.LessOrEqual(s.Y, 1.5f, f.Name + " DL nose to nose");   // v0.9.0 (E-166 / B-013): 1.1 -> 1.5: the line sits 1.45 yd ahead of the ball (the offensive line is at -0.15) so the bodies are a hand apart (0.7 yd) at the snap instead of overlapping
            }
        }
    }
}
