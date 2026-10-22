using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.7.0 Track A: the presentation reads ahead of the simulation where a clip has to LAND on an event, never changing the simulation:
    ///  - the intended receiver starts his catch when the ball is about to arrive (hands together on the frame the simulation completes the catch),
    ///  - the quarterback and the back start the hand-off take when they are about to meet (exchange frame on the simulated exchange),
    ///  - the hints the quarterback flow needs (state, drop-back distance, wind-up time, release already happened).
    /// </summary>
    public sealed partial class PlayStage
    {
        float _flowEta;

        static bool Loco(AgentAction a)
        {
            return a == AgentAction.Idle || a == AgentAction.Run || a == AgentAction.Sprint || a == AgentAction.Route || a == AgentAction.Shuffle || a == AgentAction.Backpedal || a == AgentAction.Carry;
        }

        /// <summary>Seconds before the ball reaches the receiver at which his catch take starts; the catch moment of the take is about 0.35 s into it.</summary>
        public const float CatchReachSeconds = 0.34f;

        AgentAction FlowAction(int i, SimAgent a, SimAgentView v)
        {
            _flowEta = -1f;
            AgentAction act = a.Action;
            if (Sim.Phase != PlayPhase.Live || SimAgentView.DebugNoFlow) return act;
            // ---- the kicker: the clip starts at the approach and its contact frame is the ball launch
            if (act == AgentAction.Kick && Sim.KickContactTime > 0f) _flowEta = Mathf.Max(0.05f, Sim.KickContactTime - Sim.Time);
            // ---- the intended receiver reaches for the pass
            var b = Sim.Ball;
            if (b.State == BallState.Pass && b.Intended == i && a.Side == SimSide.Offense && Loco(act) && !a.IsDown)
            {
                float speed = b.Vel.XY.Length;
                if (speed > 3f)
                {
                    Vec2 to = a.Pos - b.Pos.XY; float d = to.Length;
                    bool closing = (b.Vel.XY.X * to.X + b.Vel.XY.Y * to.Y) > 0f;
                    float eta = d / Mathf.Max(3f, speed + (closing ? Mathf.Max(0f, -a.Vel.Dot(b.Vel.XY.Norm())) : 0f));
                    if (closing && eta <= CatchReachSeconds && b.Pos.Z < 3.2f) { _flowEta = eta; return AgentAction.Catch; }
                }
            }
            // ---- the exchange: both players start the hand-off take when they are about to meet
            if (Sim.Kind == PlayKind.Scrimmage && a.Side == SimSide.Offense && b.State == BallState.Held && b.Holder == 0 && Sim.QbPlanIsHandoff && Sim.Agents[0].QbState == QbPhase.Mesh)
            {
                int tgt = Sim.QbHandoffTargetId;
                if (tgt > 0 && (i == 0 || i == tgt) && Loco(act))
                {
                    Vec2 d2 = Sim.Agents[tgt].Pos - Sim.Agents[0].Pos; float dist = d2.Length;
                    if (dist < 3.4f)
                    {
                        float closeSpeed = Mathf.Max(1.2f, (Sim.Agents[0].Vel - Sim.Agents[tgt].Vel).Dot(d2.Norm()));
                        _flowEta = Mathf.Max(0.05f, (dist - 1.55f) / closeSpeed);
                        return AgentAction.Handoff;
                    }
                }
            }
            return act;
        }

        readonly int[] _celebSide = new int[22];

        /// <summary>The hand that holds the ball during a touchdown celebration: the arm that reaches out farther from the chest early in the take (the Heisman pose, the raised ball), then locked so it never swaps hands.</summary>
        int CelebrateHand(SimAgent a, SimAgentView v)
        {
            if (a.ActionTime < 0.03f) _celebSide[a.Id] = 0;
            if (_celebSide[a.Id] != 0) return _celebSide[a.Id];
            var av = v.Avatar;
            int side = _carrySide[a.Id] >= 0 ? 1 : -1;
            if (av != null && av.RightHand != null && av.LeftHand != null && av.Chest != null && a.ActionTime > 0.35f)
            {
                float r = (av.RightHand.position - av.Chest.position).sqrMagnitude, l = (av.LeftHand.position - av.Chest.position).sqrMagnitude;
                side = r >= l ? 1 : -1; _celebSide[a.Id] = side;
            }
            return side;
        }

        void AddFlowHints(ref AnimHints h, SimAgent a, SimAgentView v)
        {
            h.Speed = a.Speed * PlayFrame.YardsToMeters;
            h.BallReleased = Sim.PassIsThrown;
            h.Whistled = Sim.Phase == PlayPhase.Dead;
            if (a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage)
            {
                h.IsQb = true; h.Qb = a.QbState; h.PrevScramble = a.PrevQbState == QbPhase.Scramble;
                h.DropSteps = Sim.QbDropSteps; h.Rolling = Sim.QbRolling; h.WindupSeconds = a.WindupLeft;
                Vector3 dw = Frame.ToUnity(Sim.QbDropPoint), wp = v.transform.position;
                h.DropRemaining = new Vector2(dw.x - wp.x, dw.z - wp.z).magnitude;
                for (int j = 11; j < 22; j++)
                {
                    var d = Sim.Agents[j]; if (d.IsDown) continue;
                    if ((d.Pos - a.Pos).Length < 3.2f) { h.Pressure = true; break; }
                }
            }
        }
    }
}
