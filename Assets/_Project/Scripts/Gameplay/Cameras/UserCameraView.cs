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
                if (b.State != BallState.Held)
                {
                    focus = frame.ToUnity(b.Pos); fvel = frame.VelToUnity(b.Vel.XY) * 0.35f; follow = true;
                    if (fvel.magnitude > 12f) fvel = fvel.normalized * 12f;
                }
            }
            c.Focus = focus; c.FocusVelocity = fvel; c.Follow = follow;
            return c;
        }
    }
}
