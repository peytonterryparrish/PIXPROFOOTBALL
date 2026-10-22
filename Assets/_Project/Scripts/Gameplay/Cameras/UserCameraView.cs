using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.6: works out what the user camera should look at from the simulation (shared by the match director and the camera gallery).</summary>
    public static class UserCameraView
    {
        public static UserCameraInput Build(PlaySimulation sim, PlayFrame frame, SimSide userSide)
        {
            var c = new UserCameraInput();
            Vector3 offDir = frame.DirToUnity(new Vec2(0f, 1f));
            bool userDef = userSide == SimSide.Defense;
            c.ViewDir = userDef ? -offDir : offDir; c.Defense = userDef;
            bool pre = sim.Phase == PlayPhase.PreSnap;
            c.PreSnap = pre; c.SnapTime = pre ? 0f : sim.Time;
            Vector3 los = frame.ToUnity(new Vec2(sim.BallX0, sim.LosY));
            var b = sim.Ball;
            bool scrim = sim.Kind == PlayKind.Scrimmage || sim.Kind == PlayKind.Kneel || sim.Kind == PlayKind.Spike;
            if (!scrim && userDef)
            {
                // the user returns the kick: stand behind the returner looking at the kicker
                for (int i = 11; i < 22; i++) if (sim.Agents[i].ReturnerRole) { los = frame.ToUnity(sim.Agents[i].Pos); break; }
            }
            else if (!scrim) los = frame.ToUnity(sim.Agents[0].Pos);   // kicking: behind the kicker
            if (!scrim && !userDef) c.KickKind = sim.Kind == PlayKind.Kickoff ? 1 : (sim.Kind == PlayKind.FieldGoal ? 2 : (sim.Kind == PlayKind.Punt ? 3 : 0));   // GFX-395
            c.Los = los;
            Vector3 focus = los, fvel = Vector3.zero; bool follow = false;
            if (!pre)
            {
                if (b.State == BallState.Held && b.Holder >= 0)
                {
                    var h = sim.Agents[b.Holder];
                    bool pocket = scrim && h.Side == SimSide.Offense && h.Slot == 0 && !sim.PassIsThrown && (h.QbState == QbPhase.Set || h.QbState == QbPhase.Windup || h.QbState == QbPhase.Dropback || h.QbState == QbPhase.WaitSnap || h.QbState == QbPhase.Fake || h.QbState == QbPhase.Mesh);
                    focus = frame.ToUnity(h.Pos); fvel = frame.VelToUnity(h.Vel);
                    follow = !pocket && !(sim.Kind == PlayKind.FieldGoal || sim.Kind == PlayKind.Punt) || (!scrim && h.Slot != 0);
                    if (pocket) focus = frame.ToUnity(sim.Agents[0].Pos);
                }
                else if (scrim) { focus = frame.ToUnity(sim.Agents[0].Pos); follow = b.State != BallState.Held; }
                // v0.9.0 CONNECT E-391: the snap flight (centre to quarterback / holder, about 0.4 s) keeps the pocket framing on the quarterback; it used to lurch to the carrier preset and back at every snap
                bool snapFlight = b.State == BallState.Snap && (scrim || sim.Kind == PlayKind.FieldGoal || sim.Kind == PlayKind.Punt);
                if (snapFlight) { focus = frame.ToUnity(sim.Agents[0].Pos); fvel = Vector3.zero; follow = false; }
                else if (b.State != BallState.Held)
                {
                    focus = frame.ToUnity(b.Pos); follow = true;
                    bool kicked = b.State == BallState.Kick;   // GFX-395: a kick travels 25-35 m/s, the old clamp of 12 m/s let it outrun the camera: lead = 0.6 x the ball velocity, up to 30 m/s
                    fvel = frame.VelToUnity(b.Vel.XY) * (kicked ? 0.6f : 0.35f);
                    float cap = kicked ? 30f : 12f;
                    if (fvel.magnitude > cap) fvel = fvel.normalized * cap;
                    c.BallAirborne = focus.y > 1.2f;   // v0.9.0 CONNECT E-390: Focus.y carries the ball's height for UserCameraMath
                }
                // v0.9.0 CONNECT E-392: after a turnover on a scrimmage play (the defense now holds the ball, the user is the offense) the returner runs back toward the old offense's backfield.
                // Flip the view to his travel direction so the camera stays behind him instead of framing him from the front; the rig turns the camera around smoothly (MatchCameraRig).
                if (scrim && !userDef && sim.BallSide == SimSide.Defense && follow) c.ViewDir = -offDir;
            }
            c.Focus = focus; c.FocusVelocity = fvel; c.Follow = follow;
            return c;
        }
    }
}
