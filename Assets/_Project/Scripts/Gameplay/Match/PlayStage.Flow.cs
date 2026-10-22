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
        public const float CatchReachSeconds = 0.5f;      // v0.9.0 CONNECT E-375: the hands are formed by the catch frame even for short ETAs (was 0.34 s)
        /// <summary>v0.9.0 CONNECT E-133: highest ball (yards) that still gets the early reach (jump balls and deep fades were late at 3.2).</summary>
        public const float CatchReachHeightYd = 4.9f;
        int _bestDef = -1, _bestDefFrame = -1;

        /// <summary>The defender with the best play on the ball in the air (E-133 / E-189): smallest miss distance to the ball's line among those who can get there inside the reach window.</summary>
        void FindBestDefender()
        {
            if (_bestDefFrame == Time.frameCount) return;
            _bestDefFrame = Time.frameCount; _bestDef = -1;
            var b = Sim.Ball;
            if (b.State != BallState.Pass) return;
            Vec2 bv = b.Vel.XY; float sp = bv.Length; if (sp < 3f) return;
            Vec2 dirb = bv * (1f / sp);
            float best = 2.4f;
            for (int j = 0; j < 22; j++)
            {
                var d = Sim.Agents[j]; if (d.Side == SimSide.Offense || d.IsDown || !Loco(d.Action)) continue;
                Vec2 to = d.Pos - b.Pos.XY; float along = -(to.X * dirb.X + to.Y * dirb.Y);       // + = the ball still has to travel to him
                if (along <= 0f) continue;
                float perp = Mathf.Abs(to.X * dirb.Y - to.Y * dirb.X);
                float eta = along / Mathf.Max(3f, sp);
                if (eta > CatchReachSeconds || b.Pos.Z > CatchReachHeightYd) continue;
                if (perp < best) { best = perp; _bestDef = j; }
            }
        }

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
                    if (closing && eta <= CatchReachSeconds && b.Pos.Z < CatchReachHeightYd)
                    {
                        // E-122 / E-129: a duel in traffic is a pair take (the director starts it so its contact frame is the catch frame); no plain catch over it
                        if (Contacts != null && !SimAgentView.DebugNoConnect && Contacts.IsContestedCandidate(i)) { Contacts.WantsContestedCatch(i, eta); return act; }
                        _flowEta = eta; Magnet(i, eta); return AgentAction.Catch;
                    }
                }
            }
            // ---- v0.9.0 CONNECT E-133 / E-189: the defender with the best play on the ball reaches ahead too (the swat for a high ball, the interception reach at the body)
            FindBestDefender();
            if (i == _bestDef && a.Side == SimSide.Defense)
            {
                Vec2 bv2 = b.Vel.XY; float sp2 = bv2.Length; Vec2 to2 = a.Pos - b.Pos.XY;
                float along2 = -(to2.X * bv2.X + to2.Y * bv2.Y) / Mathf.Max(0.1f, sp2);
                _flowEta = Mathf.Max(0.05f, along2 / Mathf.Max(3f, sp2));
                Magnet(i, _flowEta);
                return b.Pos.Z > 2.6f ? AgentAction.Jump : AgentAction.Catch;
            }
            // ---- v0.9.0 CONNECT E-308 / E-372: the returner reaches for the kick before it arrives
            if (b.State == BallState.Kick && a.ReturnerRole && Loco(act) && !a.IsDown && b.Pos.Z < 5f)
            {
                float d3 = (a.Pos - b.Pos.XY).Length;
                float sxy = b.Vel.XY.Length;
                float vz = b.Vel.Z, z = Mathf.Max(0f, b.Pos.Z);
                float tfall = (vz + Mathf.Sqrt(Mathf.Max(0f, vz * vz + 2f * 10.7f * z))) / 10.7f;     // time until the ball is back at the turf
                float eta3 = Mathf.Min(d3 / Mathf.Max(2.5f, sxy), tfall > 0.02f ? tfall : 9f);
                if (d3 < 9f && eta3 <= CatchReachSeconds && (vz < 2f)) { _flowEta = Mathf.Max(0.05f, eta3); Magnet(i, _flowEta); return AgentAction.Catch; }
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
                        _flowEta = Mathf.Max(0.05f, (dist - 1.3f) / closeSpeed);      // v0.9.0 CONNECT E-247: the exchange happens at 1.3 yd (SIM tuned the 1.15 of this track after the outside runs missed it)
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
