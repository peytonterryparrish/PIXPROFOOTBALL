using System;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// Ball-carrier moves (juke, spin, stiff-arm, truck, hurdle, dive) for the user and the AI. Each move has a duration,
    /// a cooldown and a rating-based contest against the nearest threatening defender.
    /// </summary>
    internal sealed class CarrierMoves
    {
        private readonly PlaySimulation S;

        public CarrierMoves(PlaySimulation s) { S = s; }

        private static float Sigmoid(float x) { return 1f / (1f + (float)Math.Exp(-x)); }

        // =====================================================================================
        // Timers
        // =====================================================================================

        /// <summary>Contest geometry of a carrier move: how far away a defender may be to be affected, the forward bias and the width.</summary>
        private static bool ContestParams(CarrierMove m, out float range, out float fwd, out float width)
        {
            switch (m)
            {
                case CarrierMove.JukeLeft: case CarrierMove.JukeRight: range = 2.8f; fwd = 0.2f; width = 0.9f; return true;
                case CarrierMove.Spin: range = 2.0f; fwd = -0.2f; width = 1.0f; return true;
                case CarrierMove.StiffArm: range = 2.2f; fwd = -0.1f; width = 1.0f; return true;
                case CarrierMove.Truck: range = 2.0f; fwd = 0.4f; width = 1.0f; return true;
                case CarrierMove.Hurdle: range = 2.5f; fwd = 0.3f; width = 1.0f; return true;
            }
            range = fwd = width = 0f; return false;
        }

        public const float DiveCarrierSpeed = 4.6f, DiveTacklerSpeed = 5.2f;

        public void Update()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.Move == CarrierMove.None) continue;
                a.MoveTime -= PlaySimulation.Dt;
                // v0.7: a dive is a committed lunge. The knock used to be braked away by the body in a few ticks (the lunge travelled under a yard, a sneak from the 1 scored 3 to 30 percent);
                // now the intent holds the lunge speed for the whole move, so the diver really covers ground
                if (a.Move == CarrierMove.Dive && a.MoveTime > 0f && a.State == AgentState.Normal && a.Body.FallenTime <= 0f)
                    a.Want = a.MoveDir * (a.HasBall ? DiveCarrierSpeed : DiveTacklerSpeed);
                // the contest is decided when a defender actually reaches the move (60% of the old range), not the instant the button is pressed
                float r, f, w;
                if (!a.MoveResolved && a.MoveTime > 0f && ContestParams(a.Move, out r, out f, out w)) Contest(a, r * 0.62f, f, w, a.Move);
                if (a.MoveTime <= 0f) EndMove(a);
            }
        }

        private void EndMove(SimAgent a)
        {
            CarrierMove m = a.Move;
            bool wasted = !a.MoveResolved && m != CarrierMove.Dive && m != CarrierMove.None;
            a.Move = CarrierMove.None;
            a.MoveTime = 0f;
            // a move thrown at nobody costs him a step and delays the next one
            if (wasted) { a.Body.Vel = a.Body.Vel * 0.88f; a.MoveCooldown += 0.35f; }
            if (m == CarrierMove.Dive)
            {
                a.Body.Fall(0.9f);
                if (a.HasBall && S.Phase == PlayPhase.Live)
                {
                    // the dive ends with the carrier down where he lands
                    a.State = AgentState.Down;
                    S.TacklerId = -1; S.LastTackleType = TackleType.None;   // down on his own dive: "runner down", nobody gets the tackle
                    S.LogEvent(SimEventType.Tackle, -1, a.Id, new Vec3(a.Pos, 0f), TackleType.None, 0f, "dive");
                    S.EndPlay(EndReason.Tackle);
                }
            }
            if (m == CarrierMove.Spin || m == CarrierMove.Truck || m == CarrierMove.Hurdle) a.Body.SpeedMultiplier = 1f;
        }

        // =====================================================================================
        // Executing a move
        // =====================================================================================

        private bool CanMove(SimAgent c)
        {
            return c.Move == CarrierMove.None && c.MoveCooldown <= 0f && c.State == AgentState.Normal && c.Body.FallenTime <= 0f
                   && c.Body.StumbleTime <= 0f;
        }

        public bool TryMove(SimAgent c, CarrierMove m)
        {
            if (!CanMove(c)) return false;

            Vec2 heading = c.Speed > 1.2f ? c.Vel.Norm() : c.Facing;
            c.MoveDir = heading;
            c.MoveResolved = false;
            c.Move = m;
            switch (m)
            {
                case CarrierMove.JukeLeft:
                case CarrierMove.JukeRight:
                {
                    float sgn = m == CarrierMove.JukeLeft ? 1f : -1f; // left of heading
                    Vec2 side = heading.Perp() * sgn;
                    c.MoveDir = side;
                    c.MoveTotal = c.MoveTime = 0.32f;
                    c.MoveCooldown = 0.7f;
                    float jukeV = 2.2f + 2.6f * SimMath.Rating01(c.R.Elusiveness) * 0.6f + 1.8f * c.Body.Agility01;
                    c.Body.Knock(side * jukeV);
                    c.Body.Vel = c.Body.Vel * 0.93f;
                    break;
                }
                case CarrierMove.Spin:
                {
                    c.MoveTotal = c.MoveTime = 0.45f;
                    c.MoveCooldown = 1.35f;
                    c.Body.SpeedMultiplier = 0.88f;
                    // spin away from the nearest threat (toward open grass), random only when nobody is near
                    float spinSgn = S.Rng.NextFloat() < 0.5f ? 1f : -1f;
                    { SimAgent nt = null; float nd = 4f; for (int q = 0; q < 22; q++) { SimAgent o = S.Agents[q]; if (o.Side == c.Side || o.State == AgentState.Down) continue; float dd = (o.Pos - c.Pos).Length; if (dd < nd) { nd = dd; nt = o; } } if (nt != null) spinSgn = (nt.Pos - c.Pos).Dot(heading.Perp()) > 0f ? -1f : 1f; }
                    Vec2 side = heading.Perp() * spinSgn;
                    c.Body.Knock(side * 1.2f);
                    break;
                }
                case CarrierMove.StiffArm:
                {
                    c.MoveTotal = c.MoveTime = 0.4f;
                    c.MoveCooldown = 1.0f;
                    break;
                }
                case CarrierMove.Truck:
                {
                    c.MoveTotal = c.MoveTime = 0.4f;
                    c.MoveCooldown = 1.3f;
                    c.Body.SpeedMultiplier = 0.9f;
                    break;
                }
                case CarrierMove.Hurdle:
                {
                    c.MoveTotal = c.MoveTime = 0.6f;
                    c.MoveCooldown = 1.2f;
                    c.Body.SpeedMultiplier = 0.95f;
                    break;
                }
                case CarrierMove.Dive:
                {
                    c.MoveTotal = c.MoveTime = c.HasBall ? 0.40f : 0.46f;
                    c.MoveCooldown = 2f;
                    Vec2 dd = heading;
                    SimAgent target = S.Carrier;
                    if (!c.HasBall && target != null && target.Side != c.Side)
                    {
                        // a tackler lunges AT the carrier (v0.7: the old lunge went along his own heading and the tackle was credited from 1-2 yd away)
                        Vec2 to = target.Pos + target.Vel * 0.12f - c.Pos;
                        if (to.LenSq() > 0.01f) { dd = to.Norm(); c.MoveDir = dd; }
                    }
                    c.Body.Knock(dd * (c.HasBall ? 4.2f : (c.IsDefensiveLineman ? 3.4f : 4.4f)));
                    if (c.IsDefensiveLineman) { float lc = c.Body.EffectiveVMax, ls = c.Body.Vel.Length; if (ls > lc) c.Body.Vel = c.Body.Vel * (lc / ls); }   // a lineman's lunge never takes him past his own top speed
                    break;
                }
            }
            S.LogEvent(SimEventType.CarrierMove, c.Id, -1, TackleType.None, (float)m);
            return true;
        }

        /// <summary>Finds the best target for a move and resolves the contest immediately.</summary>
        private void Contest(SimAgent c, float range, float forwardBias, float width, CarrierMove m)
        {
            Vec2 heading = c.MoveDir;
            if (m == CarrierMove.JukeLeft || m == CarrierMove.JukeRight) heading = (c.Speed > 1.2f ? c.Vel.Norm() : c.Facing);
            SimAgent target = null; float bd = float.MaxValue;
            for (int i = 0; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.Side == c.Side || d.State == AgentState.Down || d.State == AgentState.Engaged) continue;
                Vec2 rel = d.Pos - c.Pos;
                float dist = rel.Length;
                if (dist > range) continue;
                float ahead = rel.Norm().Dot(heading);
                if (ahead < forwardBias - 0.1f) continue;
                if (m == CarrierMove.StiffArm && Math.Abs(rel.Norm().Dot(heading.Perp())) < 0.2f && ahead > 0.8f) { /* straight ahead is fine */ }
                if (dist < bd) { bd = dist; target = d; }
            }
            if (target == null) return;
            SimAgent t = target;
            float win;
            float elu = 0.55f * c.R.Elusiveness + 0.45f * c.R.Agility;
            float defMove = 0.45f * t.R.Tackling + 0.30f * t.R.Agility + 0.25f * t.R.Awareness;
            switch (m)
            {
                case CarrierMove.JukeLeft:
                case CarrierMove.JukeRight:
                    win = Sigmoid((elu - defMove) / 15f - 0.90f);
                    break;
                case CarrierMove.Spin:
                    win = Sigmoid(((0.6f * c.R.Elusiveness + 0.25f * c.R.Agility + 0.15f * c.R.BreakTackle) - (0.55f * t.R.Tackling + 0.25f * t.R.Agility + 0.2f * t.R.Awareness)) / 14f - 1.00f);
                    break;
                case CarrierMove.StiffArm:
                    win = Sigmoid(((0.45f * c.R.Strength + 0.35f * c.R.BreakTackle + 0.2f * c.R.Carrying) - (0.5f * t.R.Tackling + 0.3f * t.R.Strength + 0.2f * t.R.Awareness)) / 12f - 0.6f);
                    break;
                case CarrierMove.Truck:
                {
                    float massTerm = (c.Body.Mass - t.Body.Mass) / 12f;
                    win = Sigmoid(((0.45f * c.R.Strength + 0.3f * c.R.BreakTackle + 0.25f * c.R.Carrying) - (0.5f * t.R.Tackling + 0.3f * t.R.Strength + 0.2f * t.R.Awareness)) / 11f + massTerm * 0.18f);
                    break;
                }
                case CarrierMove.Hurdle:
                {
                    bool low = t.Body.FallenTime > 0f || (t.Move == CarrierMove.Dive && t.MoveTime > 0f) || t.State == AgentState.Tackling;
                    win = low ? 0.35f + 0.55f * SimMath.Rating01(c.R.Jumping) : 0.02f + 0.12f * SimMath.Rating01(c.R.Jumping);
                    break;
                }
                default: return;
            }
            if (c.Fx != null)
            {
                // v0.8.0: Ankle Shaker adds to a juke, Whirlwind to a spin (percentage points of success)
                if (m == CarrierMove.JukeLeft || m == CarrierMove.JukeRight) win = SimMath.Clamp(win + c.Fxv(SimHook.JukeSuccess) / 100f, 0.02f, 0.97f);
                else if (m == CarrierMove.Spin) win = SimMath.Clamp(win + c.Fxv(SimHook.SpinSuccess) / 100f, 0.02f, 0.97f);
            }
            c.MoveResolved = true;
            bool success = S.Rng.NextFloat() < win;
            S.LogEvent(SimEventType.CarrierMove, c.Id, t.Id, TackleType.None, success ? 1f : 0f, m.ToString());
            if (success)
            {
                c.IgnoreAgent = t.Id; c.IgnoreUntil = S.Time + 0.65f;
                t.IgnoreAgent = c.Id; t.IgnoreUntil = S.Time + 0.65f;
                switch (m)
                {
                    case CarrierMove.JukeLeft: case CarrierMove.JukeRight:
                        t.Body.Stumble(0.45f); t.Body.Vel = t.Body.Vel * 0.55f; break;
                    case CarrierMove.Spin:
                        t.Body.Stumble(0.4f); t.Body.Vel = t.Body.Vel * 0.5f; break;
                    case CarrierMove.StiffArm:
                        t.Body.Stumble(0.55f); t.Body.Vel = t.Body.Vel * 0.5f;
                        c.Body.SpeedMultiplier = 1f; break;
                    case CarrierMove.Truck:
                        t.Body.Fall(0.9f + 0.4f * S.Rng.NextFloat());
                        S.LogEvent(SimEventType.BrokenTackle, t.Id, c.Id, TackleType.BrokenTackle);
                        break;
                    case CarrierMove.Hurdle:
                        break;
                }
            }
            else
            {
                // a failed truck/spin costs speed and risks the ball
                if (m == CarrierMove.Truck)
                {
                    c.Body.Vel = c.Body.Vel * 0.55f;
                    if (S.Rng.NextFloat() < 0.035f * (1.5f - SimMath.Rating01(c.R.Carrying)) * (1f + 0.8f * S.Options.BallSlickness) * Math.Max(0.1f, 1f + c.Fxv(SimHook.FumbleResist) / 100f)) S.Fumble(c, t, (t.Pos - c.Pos).Norm() * -1.5f);
                }
                else if (m == CarrierMove.Hurdle) c.Body.Stumble(0.5f);
                else if (m == CarrierMove.Spin) c.Body.Vel = c.Body.Vel * 0.85f;
            }
        }

        // =====================================================================================
        // User input
        // =====================================================================================

        public void UserMoves(SimAgent c, ControlInput input)
        {
            if (!c.HasBall) return;
            if (input.Protect) return;
            if (input.JukeLeft) TryMove(c, CarrierMove.JukeLeft);
            else if (input.JukeRight) TryMove(c, CarrierMove.JukeRight);
            else if (input.Spin) TryMove(c, CarrierMove.Spin);
            else if (input.StiffArm) TryMove(c, CarrierMove.StiffArm);
            else if (input.Truck) TryMove(c, CarrierMove.Truck);
            else if (input.Hurdle) TryMove(c, CarrierMove.Hurdle);
            else if (input.Dive) TryMove(c, CarrierMove.Dive);
        }

        // =====================================================================================
        // AI choice
        // =====================================================================================

        public void ConsiderAIMoves(SimAgent c)
        {
            if (!CanMove(c) || c.Slot == 0 && c.Side == SimSide.Offense && S.OffAI.IsPassPlay && !S.QbHasCrossedLos) return;
            float dirY = c.Side == SimSide.Offense ? 1f : -1f;
            Vec2 heading = c.Speed > 1.2f ? c.Vel.Norm() : c.Facing;
            SimAgent threat = null; float td = float.MaxValue;
            for (int i = 0; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.Side == c.Side || d.State == AgentState.Down || d.Body.FallenTime > 0.5f) continue;
                if (d.State == AgentState.Engaged) continue;
                if (c.IgnoreAgent == d.Id && S.Time < c.IgnoreUntil) continue;
                Vec2 rel = d.Pos - c.Pos;
                float dist = rel.Length;
                if (dist > 3.0f) continue;
                if (rel.Norm().Dot(heading) < -0.2f) continue;
                if (dist < td) { td = dist; threat = d; }
            }
            // goal-line dive
            float toGoal = dirY > 0 ? 100f - c.Pos.Y : c.Pos.Y;
            if (toGoal < 2.3f && toGoal > 0.6f && threat != null && td < 2.6f && c.Move == CarrierMove.None)
            {
                TryMove(c, CarrierMove.Dive);
                return;
            }
            if (threat == null) return;
            if (S.Rng.NextFloat() > 0.045f) return;

            float elu = (c.R.Elusiveness + c.R.Agility) * 0.5f / 99f;
            float pow = (c.R.Strength + c.R.BreakTackle) * 0.5f / 99f;
            float dist2 = td;
            Vec2 rel2 = threat.Pos - c.Pos;
            float side = Math.Abs(rel2.Norm().Dot(heading.Perp()));
            float ahead = rel2.Norm().Dot(heading);
            float wJuke = dist2 > 1.3f && ahead > 0.4f ? elu * 1.2f : 0f;
            float wSpin = dist2 < 2.2f && dist2 > 1.0f ? (c.R.Elusiveness / 99f) * 0.8f : 0f;
            float wStiff = dist2 < 2.1f && side > 0.35f ? pow * 1.0f : 0f;
            float wTruck = dist2 < 1.9f && ahead > 0.5f ? pow * 0.9f * SimMath.Clamp01((c.Body.Mass - threat.Body.Mass + 25f) / 50f) : 0f;
            bool low = threat.Body.FallenTime > 0f || threat.Move == CarrierMove.Dive;
            float wHurdle = low && dist2 < 2.4f ? 0.8f * SimMath.Rating01(c.R.Jumping) : 0f;
            float tot = wJuke + wSpin + wStiff + wTruck + wHurdle;
            if (tot < 0.05f) return;
            float roll = S.Rng.NextFloat() * tot;
            if ((roll -= wJuke) < 0f)
            {
                // juke toward the open side (away from the defender's lateral lean)
                float lat = rel2.Dot(heading.Perp());
                TryMove(c, lat > 0f ? CarrierMove.JukeRight : CarrierMove.JukeLeft);
            }
            else if ((roll -= wSpin) < 0f) TryMove(c, CarrierMove.Spin);
            else if ((roll -= wStiff) < 0f) TryMove(c, CarrierMove.StiffArm);
            else if ((roll -= wTruck) < 0f) TryMove(c, CarrierMove.Truck);
            else TryMove(c, CarrierMove.Hurdle);
        }
    }
}
