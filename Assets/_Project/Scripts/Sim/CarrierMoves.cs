using System;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// Ball-carrier moves (juke, spin, stiff-arm, truck, hurdle, dive) for the user and the AI. Each move has a duration,
    /// a cooldown and a rating-based contest against the nearest threatening defender.
    /// </summary>
    internal sealed partial class CarrierMoves
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
                {
                    // v0.9.0 (E-239): a tackler's lunge is at least 85 percent of the speed he dived at (and never under 5.2 yd/s), then slides down at 5 yd/s2, so a defender who dives at 9 yd/s no longer decelerates in mid-air
                    float lunge = a.HasBall ? DiveCarrierSpeed : Math.Max(DiveTacklerSpeed, 0.85f * a.MoveSpeed);
                    float elapsed = a.MoveTotal - a.MoveTime;
                    if (!a.HasBall) lunge = Math.Max(DiveTacklerSpeed * 0.7f, lunge - 5f * elapsed);
                    a.Want = a.MoveDir * lunge;
                }
                // v0.9.0 (E-152): a juke or spin is a steering target for the length of the move (a sideways component that builds and fades) with extra lateral grip, not a velocity impulse that bypassed the friction circle
                if ((a.Move == CarrierMove.JukeLeft || a.Move == CarrierMove.JukeRight || a.Move == CarrierMove.Spin) && a.MoveTime > 0f)
                {
                    float prog = 1f - a.MoveTime / Math.Max(0.05f, a.MoveTotal);
                    float lat = (float)Math.Sin(prog * Math.PI);
                    a.Want = a.Want * 0.92f + a.MoveDir * (a.MoveSpeed * lat);
                    a.Body.LatBoost = 1.45f;
                }
                else if (a.Body.LatBoost != 1f) a.Body.LatBoost = 1f;
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
            a.Body.LatBoost = 1f;
            if (m == CarrierMove.Slide)
            {
                // v0.9.0 (E-083 / E-093): the quarterback slid and nobody got there: he is down where he stops (feet first, no contact)
                a.Body.Fall(0.8f);
                if (a.HasBall && S.Phase == PlayPhase.Live)
                {
                    a.State = AgentState.Down;
                    S.TacklerId = -1; S.LastTackleType = TackleType.None;
                    S.LogEvent(SimEventType.Tackle, -1, a.Id, new Vec3(a.Pos, 0f), TackleType.None, 0f, "slide");
                    S.EndPlayAfter(EndReason.Tackle, 0.35f);
                }
                a.Body.SpeedMultiplier = 1f;
                return;
            }
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
                    c.MoveSpeed = jukeV * 1.15f;   // (E-152) the sideways steering speed at the middle of the move; the cut itself comes through the body model
                    c.Body.Vel = c.Body.Vel * 0.95f;
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
                    c.MoveDir = side; c.MoveSpeed = 2.0f;   // (E-152) a spin is steered the same way as a juke
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
                case CarrierMove.Slide:
                {
                    // v0.9.0 (E-083 / E-093): a quarterback gives himself up, feet first: 0.55 s of slide, no hit, no fumble
                    c.MoveTotal = c.MoveTime = 0.55f;
                    c.MoveCooldown = 3f;
                    c.Body.SpeedMultiplier = 0.55f;
                    break;
                }
                case CarrierMove.Dive:
                {
                    c.MoveSpeed = c.Speed;
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
            else if (input.Tackle && c.Slot == 0 && c.Side == SimSide.Offense && S.Kind == PlayKind.Scrimmage) TryMove(c, CarrierMove.Slide);   // v0.9.0 (E-083): the quarterback slides on the tackle button
        }

        // =====================================================================================
        // AI choice
        // =====================================================================================

        public void ConsiderAIMoves(SimAgent c)
        {
            if (StylePlanTick(c)) return;      // v0.11.0 RB: a move decided earlier is executed (or called off) here
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
            if (toGoal < (c.Slot == 0 ? 1.35f : 2.3f) && toGoal > 0.6f && threat != null && td < 2.6f && c.Move == CarrierMove.None)
            {
                TryMove(c, CarrierMove.Dive);
                return;
            }
            // v0.9.0 (E-153): a runner a yard short of the sticks with a tackler on him dives for the line to gain
            if (S.Kind == PlayKind.Scrimmage && c.Side == SimSide.Offense && S.BallSide == SimSide.Offense && threat != null && td < 2.6f && c.Slot != 0 && !S.FirstDownCrossed)
            {
                float toSticks = S.LineToGainY - c.Pos.Y;
                if (toSticks > 0.4f && toSticks < 1.5f && S.LineToGainY < 100f && c.Move == CarrierMove.None) { TryMove(c, CarrierMove.Dive); return; }
            }
            // v0.9.0 (E-083 / E-093): a quarterback who has run past the line slides before the hit (unless the touchdown or the sticks are inside two yards)
            if (c.Slot == 0 && c.Side == SimSide.Offense && S.Kind == PlayKind.Scrimmage && threat != null && td < 2.3f && c.Pos.Y > S.LosY + 0.5f && toGoal > 4f
                && !(S.LineToGainY - c.Pos.Y > 0.2f && S.LineToGainY - c.Pos.Y < 2f) && c.Move == CarrierMove.None)
            {
                Vec2 rel0 = threat.Pos - c.Pos;
                if ((c.Vel - threat.Vel).Dot(rel0.Norm()) > 2.0f) { TryMove(c, CarrierMove.Slide); return; }
            }
            if (threat == null) return;
            float ttcSel = 0.4f;
            // v0.9.0 (E-151): a move is decided once per threat, on the defender's commit (closing at over 3 yd/s, 0.20-0.45 s from contact), not on a 4.5 percent roll per tick at any distance
            {
                Vec2 relT = threat.Pos - c.Pos;
                float closeSp = (c.Vel - threat.Vel).Dot(relT.Norm());
                float ttc = Math.Max(0f, td - (c.Body.Radius + threat.Body.Radius + 0.35f)) / Math.Max(0.5f, closeSp);
                if (closeSp < 3.0f || ttc < 0.17f || ttc > StyleTtcMax || c.MoveThreatId == threat.Id) return;
                ttcSel = ttc;
                c.MoveThreatId = threat.Id;
                if (S.Rng.NextFloat() > StylePass) return;
            }

            float elu = (c.R.Elusiveness + c.R.Agility) * 0.5f / 99f;
            float pow = (c.R.Strength + c.R.BreakTackle) * 0.5f / 99f;
            float dist2 = td;
            Vec2 rel2 = threat.Pos - c.Pos;
            float side = Math.Abs(rel2.Norm().Dot(heading.Perp()));
            float ahead = rel2.Norm().Dot(heading);
            float wJuke = dist2 > 1.3f && ahead > 0.4f ? elu * 1.2f : 0f;
            float wSpin = dist2 < 2.4f && dist2 > 0.9f ? (c.R.Elusiveness / 99f) * 0.8f : 0f;
            float wStiff = dist2 < 2.1f && side > 0.35f ? pow * 1.0f : 0f;
            float wTruck = dist2 < 1.9f && ahead > 0.5f ? pow * 0.9f * SimMath.Clamp01((c.Body.Mass - threat.Body.Mass + 25f) / 50f) : 0f;
            bool low = threat.Body.FallenTime > 0f || threat.Move == CarrierMove.Dive;
            float wHurdle = low && dist2 < 2.4f ? 0.8f * SimMath.Rating01(c.R.Jumping) : 0f;
            StyleWeights(c, threat, ref wJuke, ref wSpin, ref wStiff, ref wTruck, ref wHurdle);      // v0.11.0 RB: leverage, situation and budget
            float tot = wJuke + wSpin + wStiff + wTruck + wHurdle;
            if (tot < 0.05f) return;
            float roll = S.Rng.NextFloat() * tot;
            if ((roll -= wJuke) < 0f)
            {
                // juke toward the open side (away from the defender's lateral lean)
                float lat = rel2.Dot(heading.Perp());
                StyleQueue(c, StyleJukeSide(c, threat, heading, lat), ttcSel, threat.Id);
            }
            else if ((roll -= wSpin) < 0f) StyleQueue(c, CarrierMove.Spin, ttcSel, threat.Id);
            else if ((roll -= wStiff) < 0f) StyleQueue(c, CarrierMove.StiffArm, ttcSel, threat.Id);
            else if ((roll -= wTruck) < 0f) StyleQueue(c, CarrierMove.Truck, ttcSel, threat.Id);
            else StyleQueue(c, CarrierMove.Hurdle, ttcSel, threat.Id);
        }
    }
}
