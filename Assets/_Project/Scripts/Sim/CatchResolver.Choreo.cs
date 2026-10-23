using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.10.0 CHOREO: the contested catch decided ahead of the arrival. The duel of a receiver and the defender on him is rolled by the choreographer a few tenths of a second before the ball gets there
    /// (<see cref="PreRoll"/>: the same defender-engages roll, the same margin, the same draws in the same order as <see cref="Resolve"/> makes, only earlier, with the geometry forecast for the arrival), so the take that
    /// is booked for the two bodies is the take of what really happens. When the ball arrives <see cref="TryPreDuel"/> plays that outcome instead of rolling again (the draws are not made twice: the random stream
    /// is consumed as often as before, the outcome distribution is the formula's).
    /// </summary>
    internal sealed partial class CatchResolver
    {
        public enum DuelOutcome { None, Catch, Drop, Intercept, Deflect }
        struct PreDuel { public int R, D; public DuelOutcome Out; public float Margin; }
        readonly Dictionary<int, PreDuel> _pre = new Dictionary<int, PreDuel>();

        void ResetPre() { _pre.Clear(); }

        /// <summary>The outcome rolled for this receiver and defender (None: no duel forecast, or a different pair).</summary>
        public DuelOutcome PreRolled(int receiver, int defender)
        {
            PreDuel pd; return _pre.TryGetValue(receiver, out pd) && pd.D == defender ? pd.Out : DuelOutcome.None;
        }

        /// <summary>
        /// Rolls the duel of receiver <paramref name="R"/> and defender <paramref name="D"/> for the ball arriving at <paramref name="ballPos"/> with velocity <paramref name="ballVel"/>, the two bodies
        /// <paramref name="rBall"/> / <paramref name="dBall"/> yards from it at that moment. Returns None (and rolls nothing but the defender's own engage roll, which the real path makes too) when the defender
        /// does not go for the ball.
        /// </summary>
        public DuelOutcome PreRoll(SimAgent R, SimAgent D, float rBall, float dBall, Vec3 ballPos, Vec3 ballVel)
        {
            PreDuel pd;
            if (_pre.TryGetValue(R.Id, out pd)) return pd.D == D.Id ? pd.Out : DuelOutcome.None;
            if (!Engages(D)) return DuelOutcome.None;
            Rng rng = S.Rng;
            float rCov = D.Assign is CoverageAssignment && ((CoverageAssignment)D.Assign).IsMan ? D.R.ManCoverage : D.R.ZoneCoverage;
            float rScore = 0.36f * R.R.Catching + 0.18f * R.R.Jumping + 0.14f * R.R.Strength + 0.10f * R.R.Awareness + 0.12f * R.R.Agility + 0.10f * R.R.Speed;
            if (R.Fx != null) rScore += 0.5f * CatchBonus(R, true);
            float dScore = 0.22f * D.R.Catching + 0.26f * rCov + 0.16f * D.R.Jumping + 0.12f * D.R.Awareness + 0.12f * D.R.Strength + 0.12f * D.R.Agility;
            if (D.Fx != null) dScore += DuelCoveragePerPoint * D.Fxv(D.Assign is CoverageAssignment && ((CoverageAssignment)D.Assign).IsMan ? SimHook.ManCoverage : SimHook.ZoneRange);
            float pos = (dBall - rBall) * 9f;
            Vec2 bd = (ballPos.XY - D.Pos).Norm();
            Vec2 br = (ballPos.XY - R.Pos).Norm();
            float dLook = D.Facing.Dot(bd) > 0.2f ? 5f : -7f;
            float rLook = R.Facing.Dot(br) > 0.2f ? 4f : -5f;
            float timing = (R.Id == S.TargetId ? 4f : 0f);
            float hi = SimMath.Clamp01((ballPos.Z - 2.0f) / 1.1f);
            float vertical = hi * ((R.Body.HeightYd - D.Body.HeightYd) * 45f + (Leap(R) - Leap(D)) * 22f);
            float margin = (rScore - dScore) + pos + rLook - dLook + timing + vertical + rng.Gaussian(0f, 9f);
            float pR = Sigmoid(margin / 11f);
            DuelOutcome o;
            float u = rng.NextFloat();
            if (u < pR)
            {
                float p = CatchProbability(R, D);
                o = rng.NextFloat() < p ? DuelOutcome.Catch : DuelOutcome.Drop;
            }
            else
            {
                float pInt = 0.03f + 0.22f * SimMath.Rating01(D.R.Catching) + (margin < -14f ? 0.06f : 0f);
                pInt -= 0.10f * SimMath.Clamp01((ballVel.Length - 22f) / 16f);
                Vec2 towardPasser = (S.Agents[Math.Max(0, S.PasserId)].Pos - R.Pos).Norm();
                float front = (D.Pos - R.Pos).Dot(towardPasser);
                pInt *= SimMath.Clamp(0.30f + 0.55f * front, 0.12f, 1f) * 0.30f;
                if (rng.NextFloat() < pInt) o = DuelOutcome.Intercept;
                else if (BallHawk(D)) o = DuelOutcome.Intercept;
                else o = DuelOutcome.Deflect;
            }
            _pre[R.Id] = new PreDuel { R = R.Id, D = D.Id, Out = o, Margin = margin };
            return o;
        }

        /// <summary>The ball has arrived and the duel is between the pair that was rolled: play the rolled outcome. False when there is none (the normal roll runs).</summary>
        bool TryPreDuel(SimAgent R, SimAgent D)
        {
            PreDuel pd;
            if (!_pre.TryGetValue(R.Id, out pd) || pd.D != D.Id) return false;
            _pre.Remove(R.Id);
            bool contact = (R.Pos - D.Pos).Length < 1.5f;
            if (S.Options.Penalties && contact && S.FoulOpen(SimSide.Offense) && pd.Margin < -12f)
            {
                float pOpi = 0.035f * (1.2f - SimMath.Rating01(R.R.Awareness));
                if (S.Rng.NextFloat() < pOpi) S.Flag(PenaltyType.OffensivePassInterference, R, S.Ball.Pos.Y - S.LosY);
            }
            switch (pd.Out)
            {
                case DuelOutcome.Catch: Catch(R, true, D); break;
                case DuelOutcome.Drop: Drop(R); break;
                case DuelOutcome.Intercept: Intercept(D); break;
                default: Deflect(D, 1.0f); break;
            }
            return true;
        }
    }
}
