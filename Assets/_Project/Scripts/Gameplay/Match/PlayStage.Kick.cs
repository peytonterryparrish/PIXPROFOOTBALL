using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.10.0 QB (note 7): the stage side of the kick event: tells the kicker's view where the ball will be at contact and when (the simulated contact time), so the foot and the ball meet on the same frame.</summary>
    public sealed partial class PlayStage
    {
        void KickTarget(int i, SimAgent a, SimAgentView v)
        {
            if (v == null) return;
            bool kickPlay = Sim.Kind == PlayKind.Kickoff || Sim.Kind == PlayKind.Punt || Sim.Kind == PlayKind.FieldGoal;
            bool isKicker = kickPlay && a.Side == SimSide.Offense && (a.PlayPos == Position.K || (a.PlayPos == Position.P && Sim.Kind == PlayKind.Punt));
            if (!isKicker || a.Action != AgentAction.Kick || Sim.Phase != PlayPhase.Live) { v.SetKickTarget(false, Vector3.zero, Vector3.forward, 0f); return; }
            Vec3 at;
            switch (Sim.Kind)
            {
                case PlayKind.Kickoff: at = new Vec3(Sim.BallX0, Sim.LosY, KickGeometry.TeeHeightYd); break;
                case PlayKind.FieldGoal: { var hd = Sim.KickHolderId >= 0 ? Sim.Agents[Sim.KickHolderId] : Sim.Agents[0]; at = new Vec3(hd.Pos.X + KickGeometry.FgSpotSideYd, hd.Pos.Y + KickGeometry.FgSpotAheadYd, KickGeometry.HolderSpotHeightYd); break; }
                default: at = new Vec3(a.Pos + new Vec2(KickGeometry.PuntContactAsideYd, KickGeometry.PuntContactAheadYd), KickGeometry.PuntContactHeightYd); break;
            }
            float eta = Sim.KickContactTime > 0f ? Sim.KickContactTime - Sim.Time : -1f;
            v.SetKickStandoff(Sim.Kind == PlayKind.Kickoff ? 0.13f : (Sim.Kind == PlayKind.FieldGoal ? 0.19f : 0.17f));
            v.SetKickTarget(true, Frame.ToUnity(at), Frame.DirToUnity(new Vec2(0f, 1f)), eta);
        }
    }
}
