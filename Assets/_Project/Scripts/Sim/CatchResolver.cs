using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// Resolves who can reach a ball in the air from geometry: reach envelopes from height and Jumping, then a contested duel
    /// (Catching, Strength, timing and position vs coverage skill) with outcomes catch, drop, deflection, interception,
    /// hit-in-traffic fumbles, pass interference, and feet-in-bounds at the sideline.
    /// </summary>
    internal sealed class CatchResolver
    {
        private readonly PlaySimulation S;
        private readonly List<SimAgent> _near = new List<SimAgent>(8);

        /// <summary>v0.8.0: contested-catch duel points per point of man / zone coverage ability (the baked rating adds a quarter point more).</summary>
        public const float DuelCoveragePerPoint = 0.5f;

        public CatchResolver(PlaySimulation s) { S = s; }

        private static float Sigmoid(float x) { return 1f / (1f + (float)Math.Exp(-x)); }

        // =====================================================================================
        // Reach envelope
        // =====================================================================================

        /// <summary>Highest point (yards) a standing player can pull a ball down.</summary>
        public float StandReach(SimAgent a) { return a.Body.HeightYd * 1.28f; }

        /// <summary>Vertical gain (yards) of a full-effort leap: a Jumping rating of 30 gets a hop, 97 gets a leaper's extension.</summary>
        public float Leap(SimAgent a) { return 0.12f + 0.98f * SimMath.Rating01(a.R.Jumping); }

        /// <summary>Highest point reachable at the top of a jump.</summary>
        public float JumpReach(SimAgent a) { return StandReach(a) + Leap(a); }

        public float ReachTop(SimAgent a)
        {
            float top = a.Body.HeightYd * 1.28f;
            if (a.JumpTime > 0.05f)
            {
                float leap = Leap(a);
                // parabolic jump profile: strongest around the middle of the window
                float phase = 1f - Math.Abs(a.JumpTime / 0.62f * 2f - 1f);
                top += leap * SimMath.Clamp01(phase + 0.15f);
            }
            return top;
        }

        /// <summary>
        /// v0.9.0 (E-134): the height of the ball when it is at the receiver's hands, not where it first enters his catch envelope. A steep arc (a 40 yd pass comes down at 40-50 degrees) is a
        /// yard higher when it first touches the envelope than where it crosses the receiver, which used to turn good deep balls into 'too high' drops.
        /// </summary>
        public float HandZ(SimAgent a, BallSim ball)
        {
            Vec2 vh = ball.Vel.XY;
            float sp = vh.Length;
            if (sp < 1f) return ball.Pos.Z;
            Vec2 toR = a.Pos - ball.Pos.XY;
            float tcl = SimMath.Clamp(toR.Dot(vh * (1f / sp)) / sp, 0f, 0.18f);
            return Math.Max(0.2f, ball.Pos.Z + ball.Vel.Z * tcl - 0.5f * SimMath.Gravity * tcl * tcl);
        }

        public float ReachRadius(SimAgent a, Vec3 ball)
        {
            float r = a.Body.Radius + 0.50f;   // v0.9.0 (E-134): was 0.62 (a catch was awarded with the ball up to a yard from his hands)
            if (a.Body.Speed > 3f && ball.Z < 1.1f) r += 0.45f; // stretching / diving for low balls
            Vec2 toBall = ball.XY - a.Pos;
            if (toBall.LenSq() > 0.01f && a.Facing.Dot(toBall.Norm()) < -0.2f) r = a.Body.Radius + 0.30f; // ball behind the shoulders
            return r;
        }

        /// <summary>Does the ball (moving from prev to cur this step) pass through the agent's catch envelope?</summary>
        public bool Touches(SimAgent a, Vec3 prev, Vec3 cur, out float closest)
        {
            closest = float.MaxValue;
            float top = ReachTop(a);
            bool hit = false;
            for (int k = 0; k <= 2; k++)
            {
                float f = k * 0.5f;
                Vec3 p = new Vec3(prev.X + (cur.X - prev.X) * f, prev.Y + (cur.Y - prev.Y) * f, prev.Z + (cur.Z - prev.Z) * f);
                float d = (p.XY - a.Pos).Length;
                if (d < closest) closest = d;
                float hz = p.Z;
                if (p.Z > top && S.Ball.Vel.Z < 0f) { hz = HandZ(a, S.Ball); }
                if (p.Z < 0.12f || hz > top) continue;
                if (d <= ReachRadius(a, p)) hit = true;
            }
            return hit;
        }

        // =====================================================================================
        // Passes in flight
        // =====================================================================================

        private readonly HashSet<int> _tried = new HashSet<int>();
        private readonly Dictionary<int, float> _lock = new Dictionary<int, float>();

        
        private bool _dpiChecked;

        /// <summary>Accumulates how long a defender has had his body on the intended receiver while the ball is in the air (E-204).</summary>
        private void TrackContact()
        {
            if (S.TargetId < 0) return;
            SimAgent R = S.Agents[S.TargetId];
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.State == AgentState.Down || a.Assign is RushAssignment || a.Assign is BlitzAssignment) continue;
                if ((a.Pos - R.Pos).Length < a.Body.Radius + R.Body.Radius + 0.25f) { _contactTime += PlaySimulation.Dt; return; }
            }
        }

        public void ResetPass() { _tried.Clear(); _lock.Clear(); _dpiChecked = false; _pendCatch = null; _contactTime = 0f; }

        // ---- v0.9.0 (E-134): the catch is made when the ball is in the hands. The envelope test (0.5 yd past the body) only starts the catch; the receiver then gets the ball over the next 2-6 ticks, so the
        // view never has to hide a catch awarded up to a yard from the hands.
        private SimAgent _pendCatch, _pendDef;
        private bool _pendContested;
        private float _pendT;
        /// <summary>True while a catch has been decided and the ball is still travelling the last few inches to the receiver hands.</summary>
        public bool CatchPending { get { return _pendCatch != null; } }

        private bool StepPendingCatch()
        {
            if (_pendCatch == null) return false;
            SimAgent r = _pendCatch;
            BallSim ball = S.Ball;
            _pendT += PlaySimulation.Dt;
            Vec3 hand = new Vec3(r.Pos + r.Facing * 0.22f, SimMath.Clamp(ball.Pos.Z, 0.35f, S.Catches.ReachTop(r)));
            float gap = (ball.Pos - hand).Length;
            bool away = (ball.Pos.XY - r.Pos).Dot(ball.Vel.XY) > 0f && gap > 0.8f;   // it flew past him
            if (gap < 0.45f || _pendT > 0.12f || away || ball.GroundContactThisStep)
            {
                bool contested = _pendContested; SimAgent d = _pendDef;
                _pendCatch = null; _pendDef = null;
                FinishCatch(r, contested, d);
            }
            return true;
        }

        private float _contactTime;

        /// <summary>
        /// Defensive pass interference: as a catchable ball arrives at the intended receiver, a defender who is right on him and not playing
        /// the ball may grab him. Deep balls (long arrival) draw far more flags than quick throws.
        /// </summary>
        private void CheckInterference()
        {
            if (_dpiChecked || !S.Options.Penalties || !S.FoulOpen(SimSide.Defense) || S.TargetId < 0) return;
            BallSim ball = S.Ball;
            SimAgent R = S.Agents[S.TargetId];
            if (R.Body.FallenTime > 0.3f) return;
            float dr = (R.Pos - ball.Pos.XY).Length;
            if (dr > 2.4f) return;
            _dpiChecked = true;
            if (ball.Pos.Z > ReachTop(R) + 0.7f || ball.Pos.Z < 0.2f) return;   // uncatchable: no flag
            // v0.9.0 (E-204): a flag needs CONTACT: a defender whose body has been within a hand (the radii plus 0.25 yd) of the receiver for at least 0.15 s, and who is not simply playing the ball
            SimAgent D = null; float bd = 2.1f;
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.State == AgentState.Down || a.Body.FallenTime > 0.3f || a.Assign is RushAssignment || a.Assign is BlitzAssignment) continue;
                float d = (a.Pos - R.Pos).Length;
                if (d < bd && d < a.Body.Radius + R.Body.Radius + 0.80f) { bd = d; D = a; }
            }
            if (D == null || _contactTime < 0.06f) return;
            float air = Math.Max(0f, ball.Pos.Y - S.PassThrowY);
            float aw = SimMath.Rating01(D.R.Awareness);
            float cov = SimMath.Rating01(D.Assign is CoverageAssignment && ((CoverageAssignment)D.Assign).IsMan ? D.R.ManCoverage : D.R.ZoneCoverage);
            float p = 0.30f + 0.0185f * Math.Min(air, 34f);
            p *= (1.6f - aw) / 0.85f * (1.5f - cov) / 0.75f;
            Vec2 toBall = ball.Pos.XY - D.Pos;
            bool looking = toBall.LenSq() > 0.01f && D.Facing.Dot(toBall.Norm()) > 0.3f;
            Vec2 fromQ = (S.Agents[Math.Max(0, S.PasserId)].Pos - R.Pos).Norm();
            bool trailing = (D.Pos - R.Pos).Dot(fromQ) < 0.2f;
            p *= trailing ? 1.25f : 0.45f;
            p *= looking ? 0.6f : 1.35f;
            p *= SimMath.Clamp01((2.1f - bd) / 1.4f) * 0.8f + 0.2f;
            p = Math.Min(p * 1.9f, 0.19f);
            if (S.Rng.NextFloat() < p) { S.Flag(PenaltyType.DefensivePassInterference, D, ball.Pos.Y - S.LosY); return; }
            // the receiver pushes off the defender who is playing the ball
            if (!trailing && bd < 1.5f && S.Rng.NextFloat() < 0.05f * (1.4f - SimMath.Rating01(R.R.Awareness)))
                S.Flag(PenaltyType.OffensivePassInterference, R, ball.Pos.Y - S.LosY);
        }

        /// <summary>
        /// Whether the agent actually gets a hand up when the ball enters his reach: receivers and defensive backs track the ball,
        /// linemen and engaged defenders only occasionally bat it, and nobody re-touches a ball he just tipped.
        /// </summary>
        private bool Engages(SimAgent a)
        {
            float until;
            if (_lock.TryGetValue(a.Id, out until) && S.Time < until) return false;
            if (a.Side == SimSide.Offense) return true;
            if (a.Id == S.TargetId) return true;
            if (_tried.Contains(a.Id)) return true;
            // first time this defender can reach this ball
            float p;
            if (a.State == AgentState.Engaged || a.IsDefensiveLineman) p = 0.12f + 0.20f * SimMath.Rating01(a.R.Jumping) + (a.State == AgentState.Engaged ? 0f : 0.12f);
            else if (PositionGroup.IsLinebacker(a.PlayPos)) p = 0.30f + 0.25f * SimMath.Rating01(a.R.Awareness);
            else p = 0.72f;
            if (a.Facing.Dot((S.Ball.Pos.XY - a.Pos).Norm()) < -0.3f) p *= 0.5f;
            bool engage = S.Rng.NextFloat() < p;
            if (engage) _tried.Add(a.Id);
            else { _lock[a.Id] = S.Time + 0.3f; }   // he may get a hand up on a later look at the ball (no permanent blind spot)
            return engage;
        }

        public void CheckPass()
        {
            BallSim ball = S.Ball;
            if (ball.State != BallState.Pass) return;
            if (StepPendingCatch()) return;
            TrackContact();
            CheckInterference();
            _near.Clear();
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.Body.FallenTime > 0.35f || a.State == AgentState.Down) continue;
                if (a.Side == SimSide.Offense && !a.IsReceiverEligible) continue;
                if (i == S.PasserId && ball.FlightTime < 0.5f) continue;
                if (a.State == AgentState.Engaged && a.Side == SimSide.Offense) continue;
                float closest;
                if (Touches(a, ball.PrevPos, ball.Pos, out closest) && Engages(a)) _near.Add(a);
            }
            if (_near.Count == 0) return;
            Resolve(false);
        }

        public void CheckTipped()
        {
            BallSim ball = S.Ball;
            if (StepPendingCatch()) return;
            _near.Clear();
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.Body.FallenTime > 0.35f || a.State == AgentState.Down) continue;
                if (a.Side == SimSide.Offense && !a.IsReceiverEligible) continue;
                float closest;
                if (Touches(a, ball.PrevPos, ball.Pos, out closest) && Engages(a)) _near.Add(a);
            }
            if (_near.Count == 0) return;
            Resolve(true);
        }

        private void Resolve(bool tipDrill)
        {
            BallSim ball = S.Ball;
            Rng rng = S.Rng;
            SimAgent bestR = null, bestD = null;
            float rBall = float.MaxValue, dBall = float.MaxValue;
            for (int i = 0; i < _near.Count; i++)
            {
                SimAgent a = _near[i];
                float d = (a.Pos - ball.Pos.XY).Length;
                if (a.Side == SimSide.Offense) { if (d < rBall) { rBall = d; bestR = a; } }
                else { if (d < dBall) { dBall = d; bestD = a; } }
            }
            // include opponents that are almost in range so contested balls are decided together
            if (bestR != null && bestD == null)
            {
                for (int i = 0; i < 22; i++)
                {
                    SimAgent a = S.Agents[i];
                    if (a.Side != SimSide.Defense || a.Body.FallenTime > 0.35f || a.State == AgentState.Down) continue;
                    float d = (a.Pos - ball.Pos.XY).Length;
                    if (d < ReachRadius(a, ball.Pos) + 0.55f && ball.Pos.Z < ReachTop(a) + 0.4f && d < dBall && Engages(a)) { dBall = d; bestD = a; }
                }
            }
            if (bestD != null && bestR == null && !tipDrill)
            {
                // intended receiver nearly in range?
                SimAgent t = S.TargetId >= 0 ? S.Agents[S.TargetId] : null;
                if (t != null && t.Body.FallenTime <= 0.35f)
                {
                    float d = (t.Pos - ball.Pos.XY).Length;
                    if (d < ReachRadius(t, ball.Pos) + 0.55f && ball.Pos.Z < ReachTop(t) + 0.4f) { bestR = t; rBall = d; }
                }
            }

            if (tipDrill)
            {
                SimAgent first = null; float fd = float.MaxValue;
                for (int i = 0; i < _near.Count; i++)
                {
                    float d = (_near[i].Pos - ball.Pos.XY).Length;
                    if (d < fd) { fd = d; first = _near[i]; }
                }
                float skill = SimMath.Rating01(first.R.Catching);
                float p = (first.Side == SimSide.Offense ? 0.20f + 0.30f * skill : 0.05f + 0.15f * skill) - 0.06f * SimMath.Clamp01(ball.Vel.Length / 18f);
                if (rng.NextFloat() < p)
                {
                    if (first.Side == SimSide.Offense) Catch(first, false, null);
                    else Intercept(first);
                }
                else { _lock[first.Id] = S.Time + 0.5f; }
                return;
            }

            if (bestR != null && bestD == null)
            {
                // uncontested (or open) target
                float p = CatchProbability(bestR, null);
                if (rng.NextFloat() < p) Catch(bestR, false, null);
                else Drop(bestR);
                return;
            }
            if (bestR == null && bestD != null)
            {
                float skill = SimMath.Rating01(bestD.R.Catching);
                float hands = 0.05f + 0.32f * skill;
                bool dl = bestD.IsDefensiveLineman;
                if (dl) hands *= 0.6f;
                float speed = ball.Vel.Length;
                hands *= 1f - 0.25f * SimMath.Clamp01((speed - 20f) / 18f);
                if (bestD.Facing.Dot((ball.Pos.XY - bestD.Pos).Norm()) < -0.2f) hands *= 0.5f;
                // an interception needs the ball to be reachable and "thrown to" him: more likely when the receiver is already beaten
                hands *= 0.36f;
                if (rng.NextFloat() < hands) Intercept(bestD);
                else if (BallHawk(bestD)) Intercept(bestD);
                else Deflect(bestD, 1.0f);
                return;
            }

            // ---------------- contested ----------------
            SimAgent R = bestR, D = bestD;
            float rCov = D.Assign is CoverageAssignment && ((CoverageAssignment)D.Assign).IsMan ? D.R.ManCoverage : D.R.ZoneCoverage;
            float rScore = 0.36f * R.R.Catching + 0.18f * R.R.Jumping + 0.14f * R.R.Strength + 0.10f * R.R.Awareness + 0.12f * R.R.Agility + 0.10f * R.R.Speed;
            if (R.Fx != null) rScore += 0.5f * CatchBonus(R, true);   // v0.8.0 Sticky Hands, Fearless Middle, Sky Walker
            float dScore = 0.22f * D.R.Catching + 0.26f * rCov + 0.16f * D.R.Jumping + 0.12f * D.R.Awareness + 0.12f * D.R.Strength + 0.12f * D.R.Agility;
            if (D.Fx != null) dScore += DuelCoveragePerPoint * D.Fxv(D.Assign is CoverageAssignment && ((CoverageAssignment)D.Assign).IsMan ? SimHook.ManCoverage : SimHook.ZoneRange);   // v0.8.0 Shutdown Shadow, Range Rover, Sideline To Sideline: the coverage ability on top of the rating already baked in
            // position: who is closer to the ball, who is looking at it
            float pos = (dBall - rBall) * 9f;
            Vec2 bd = (ball.Pos.XY - D.Pos).Norm();
            Vec2 br = (ball.Pos.XY - R.Pos).Norm();
            float dLook = D.Facing.Dot(bd) > 0.2f ? 5f : -7f;
            float rLook = R.Facing.Dot(br) > 0.2f ? 4f : -5f;
            // timing: a receiver already underneath the ball (he is the target) has the edge on the catch point
            float timing = (R.Id == S.TargetId ? 4f : 0f);
            // up high the taller, longer-leaping player wins the ball
            float hi = SimMath.Clamp01((ball.Pos.Z - 2.0f) / 1.1f);
            float vertical = hi * ((R.Body.HeightYd - D.Body.HeightYd) * 45f + (Leap(R) - Leap(D)) * 22f);
            float margin = (rScore - dScore) + pos + rLook - dLook + timing + vertical + rng.Gaussian(0f, 9f);
            float pR = Sigmoid(margin / 11f);

            // pass interference: the defender grabs a receiver he is beaten by
            bool contact = (R.Pos - D.Pos).Length < 1.5f;
            if (S.Options.Penalties && contact && S.FoulOpen(SimSide.Offense))
            {
                if (margin < -12f)
                {
                    float pOpi = 0.035f * (1.2f - SimMath.Rating01(R.R.Awareness));
                    if (rng.NextFloat() < pOpi) S.Flag(PenaltyType.OffensivePassInterference, R, ball.Pos.Y - S.LosY);
                }
            }

            float u = rng.NextFloat();
            if (u < pR)
            {
                float p = CatchProbability(R, D);
                if (rng.NextFloat() < p) Catch(R, true, D);
                else Drop(R);
            }
            else
            {
                float pInt = 0.03f + 0.22f * SimMath.Rating01(D.R.Catching) + (margin < -14f ? 0.06f : 0f);
                pInt -= 0.10f * SimMath.Clamp01((ball.Vel.Length - 22f) / 16f);
                // a defender who is beaten (behind the receiver, relative to the passer) can only get a hand in; the pick needs position
                Vec2 towardPasser = (S.Agents[Math.Max(0, S.PasserId)].Pos - R.Pos).Norm();
                float front = (D.Pos - R.Pos).Dot(towardPasser);
                pInt *= SimMath.Clamp(0.30f + 0.55f * front, 0.12f, 1f) * 0.30f;
                if (rng.NextFloat() < pInt) Intercept(D);
                else if (BallHawk(D)) Intercept(D);
                else Deflect(D, 1.0f);
            }
        }

        // =====================================================================================
        // Probabilities and outcomes
        // =====================================================================================

        /// <summary>v0.8.0 catching points from abilities: Sticky Hands / Fearless Middle with a defender within a yard (or in the duel), Sky Walker on a contested ball.</summary>
        private float CatchBonus(SimAgent r, bool contested)
        {
            if (r.Fx == null) return 0f;
            float pts = 0f;
            bool traffic = contested;
            if (!traffic)
            {
                for (int i = 11; i < 22; i++)
                {
                    SimAgent d = S.Agents[i];
                    if (d.State == AgentState.Down || d.Body.FallenTime > 0.3f) continue;
                    if ((d.Pos - r.Pos).Length < 1.0f) { traffic = true; break; }
                }
            }
            if (traffic) pts += r.Fxv(SimHook.CatchInTraffic, S.IsOverMiddle(r) ? PlaySimulation.CtxOverMiddle : 0);
            if (contested) pts += r.Fxv(SimHook.ContestedCatch);
            return pts;
        }

        /// <summary>v0.8.0 Ball Hawk: a share of the passes this defender gets a hand on become interceptions.</summary>
        private bool BallHawk(SimAgent d)
        {
            if (d.Fx == null) return false;
            float p = d.Fxv(SimHook.InterceptionCatch) / 100f;
            return p > 0f && S.Rng.NextFloat() < p;
        }

        public float CatchProbability(SimAgent r, SimAgent contester)
        {
            BallSim ball = S.Ball;
            float skill = SimMath.Rating01(r.R.Catching + (r.Fx != null ? (int)Math.Round(CatchBonus(r, contester != null), MidpointRounding.AwayFromZero) : 0));
            float p = 0.755f + 0.17f * skill;
            float difficulty = 0f;
            float speed = ball.Vel.Length;
            difficulty += 0.18f * SimMath.Clamp01((speed - 22f) / 16f) * (1.25f - skill);
            float off = (r.Pos - ball.Pos.XY).Length;
            difficulty += 0.22f * SimMath.Clamp01((off - 0.7f) / 1.0f);
            float top = ReachTop(r);
            float bz = HandZ(r, ball);
            if (bz > top * 0.82f) difficulty += 0.12f;
            {
                // a ball up at the limit of what he can reach is a hard catch; the leaper and the tall receiver make it look easy
                float stand = StandReach(r);
                if (bz > stand * 0.9f)
                {
                    float over = SimMath.Clamp01((bz - stand * 0.9f) / Math.Max(0.2f, top - stand * 0.9f));
                    difficulty += 0.34f * over * (1.05f - 0.55f * SimMath.Rating01(r.R.Jumping));
                }
            }
            if (bz < 0.5f) difficulty += 0.12f;
            Vec2 toBall = ball.Pos.XY - r.Pos;
            if (toBall.LenSq() > 0.01f && r.Facing.Dot(toBall.Norm()) < 0f) difficulty += 0.14f;
            difficulty += 0.05f * SimMath.Clamp01(r.Speed / Math.Max(1f, r.Body.VMax));
            if (r.Body.StumbleTime > 0f) difficulty += 0.15f;
            if (contester != null) difficulty += 0.14f;
            p -= difficulty * (1.1f - 0.5f * skill);
            if (S.Options.BallSlickness > 0f) p -= 0.07f * S.Options.BallSlickness * (1.1f - 0.5f * skill);
            return SimMath.Clamp(p, 0.04f, 0.995f);
        }

        private bool InBoundsCatch(SimAgent r)
        {
            // the lead foot (toward his direction of travel / facing), not the body centre, is what has to come down in bounds
            Vec2 toe = r.Speed > 1.5f ? r.Vel.Norm() : r.Facing;
            float x = r.Pos.X + toe.X * 0.22f;
            if (x < 0f || x > FieldGeometry.Width) return false;
            if (r.Pos.Y > 110f || r.Pos.Y < -10f) return false;
            float d = FieldGeometry.DistanceToSideline(x);
            // one foot (pro) vs two feet (college); awareness lets receivers drag a toe
            float need = S.Rules.FeetInBounds >= 2 ? 0.46f : 0.14f;
            need -= 0.08f * SimMath.Rating01(r.R.Awareness);
            // moving along the line is harder than falling out of bounds
            return d >= need;
        }

        private void Catch(SimAgent r, bool contested, SimAgent d)
        {
            // the catch starts here; the ball is given to him when it reaches his hands (see StepPendingCatch)
            _pendCatch = r; _pendContested = contested; _pendDef = d; _pendT = 0f;
            StepPendingCatch();
        }

        private void FinishCatch(SimAgent r, bool contested, SimAgent d)
        {
            if (S.Phase != PlayPhase.Live) return;
            if (!InBoundsCatch(r))
            {
                // feet down out of bounds: incomplete
                S.LogEvent(SimEventType.Incomplete, S.PasserId, r.Id, new Vec3(r.Pos, 0f), TackleType.None, 1f, "feet");
                S.Ball.Kill();
                S.EndPlay(EndReason.Incomplete);
                return;
            }
            S.CompleteCatch(r);
            // catch in traffic: a defender arrives at the same moment
            if (contested && d != null)
            {
                float pFum = 0.03f * (1.5f - SimMath.Rating01(r.R.Carrying)) * (0.8f + d.R.Tackling / 200f);
                if (S.Options.BallSlickness > 0f) pFum *= 1f + 0.8f * S.Options.BallSlickness;
                if (S.Rng.NextFloat() < pFum && S.Phase == PlayPhase.Live)
                    S.Fumble(r, d, (r.Pos - d.Pos).Norm() * 1.2f);
            }
        }

        private void Intercept(SimAgent d)
        {
            S.CompleteInterception(d);
        }

        private void Drop(SimAgent r)
        {
            S.LogEvent(SimEventType.Drop, r.Id, -1, new Vec3(S.Ball.Pos.X, S.Ball.Pos.Y, S.Ball.Pos.Z));
            _lock[r.Id] = S.Time + 0.45f;
            Vec3 v = S.Ball.Vel * 0.15f;
            S.Ball.Deflect(new Vec3(v.X + S.Rng.Gaussian(0f, 1.2f), v.Y + S.Rng.Gaussian(0f, 1.2f), 1.5f));
            S.PassTipped = true;
            S.PlayAction(r, AgentAction.Catch, 0.4f);
        }

        private void Deflect(SimAgent d, float strength)
        {
            S.LogEvent(SimEventType.Deflection, d.Id, S.TargetId, new Vec3(S.Ball.Pos.X, S.Ball.Pos.Y, S.Ball.Pos.Z));
            _lock[d.Id] = S.Time + 0.4f;
            Rng rng = S.Rng;
            Vec3 v = S.Ball.Vel;
            Vec3 nv = new Vec3(v.X * 0.25f + rng.Gaussian(0f, 3.5f) * strength, v.Y * 0.25f + rng.Gaussian(0f, 3.5f) * strength, 2.5f + rng.NextFloat() * 4f);
            S.Ball.Deflect(nv);
            S.PassTipped = true;
            S.PlayAction(d, AgentAction.Jump, 0.4f);
        }

        // =====================================================================================
        // Pitches
        // =====================================================================================

        public void CheckPitch()
        {
            BallSim ball = S.Ball;
            if (ball.State != BallState.Pitch || ball.Intended < 0) return;
            SimAgent r = S.Agents[ball.Intended];
            float d = (r.Pos - ball.Pos.XY).Length;
            if (d < 1.15f && ball.Pos.Z < 2.3f && ball.Pos.Z > 0.2f)
            {
                float pFum = 0.010f * (1.5f - SimMath.Rating01(r.R.Catching));
                if (S.Options.BallSlickness > 0f) pFum *= 1f + 0.8f * S.Options.BallSlickness;
                S.GiveBall(r);
                S.RusherId = r.Id;
                S.LogEvent(SimEventType.Catch, r.Id, ball.Thrower, new Vec3(r.Pos, 0f), TackleType.None, 0f, "pitch");
                S.PlayAction(r, AgentAction.Catch, 0.3f);
                if (S.Rng.NextFloat() < pFum) S.Fumble(r, null, Vec2.Zero);
            }
        }
    }
}
