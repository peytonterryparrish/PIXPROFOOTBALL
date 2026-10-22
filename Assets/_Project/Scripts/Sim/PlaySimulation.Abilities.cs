using System;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.8.0 career layer inside the play simulation: the player's <see cref="AbilityCatalog"/> abilities, his morale
    /// (<see cref="MoraleEngine.RatingModifier"/>, +/-2) and the team's leadership. Everything is additive: a player with no abilities and a neutral
    /// morale (70) changes nothing, and no random number is drawn on his account.
    /// <list type="bullet">
    /// <item>Rating-point hooks whose context is known at the snap (third down, two-minute drill, close game late, kick return, man coverage, blitz, pass or run play) are baked into <see cref="SimAgent.R"/> when the agent is built.</item>
    /// <item>Hooks whose context changes during the play (open field, deep route, under pressure, over the middle, at the line) are asked at the use site through <see cref="SimAgent.Fxv"/>, and the speed hooks update the body every tick (<see cref="UpdateAbilityBodies"/>).</item>
    /// </list>
    /// </summary>
    public sealed partial class PlaySimulation
    {
        private int _playCtx;

        /// <summary>Stop On A Dime: braking and lateral grip gained per percent of CutSpeedLoss (tier 3: -30 percent speed lost in a cut, +12 percent grip).</summary>
        public const float CutGripPerPercent = 0.4f;
        /// <summary>Rocket Arm: yards per second added to a flat or touch throw per point of ThrowPower ability (tier 3: +6 points, +0.72 yd/s).</summary>
        public const float ThrowVelocityPerPoint = 0.12f;
        /// <summary>The speed hooks are written as rating points (a tier-3 Burner is +3 Speed, about 1 percent of top speed); the simulation counts each point this many times so the ability can be felt on the field.</summary>
        public const float SpeedGain = 2f, ReturnSpeedGain = 4f, PullSpeedGain = 3f;
        /// <summary>RouteSeparation (percent) to a receiver's speed while he runs his route: tier 3 Crisp Routes (9) is 2.7 percent quicker through his stem and break, Burner (12) on a vertical route 3.6 percent.</summary>
        public const float RouteSeparationSpeedPerPercent = 0.003f;
        /// <summary>Crisp Routes: after a plant at a sharp break (a turn above about 45 degrees) the receiver gets this many seconds of burst, worth this share of top speed and of acceleration time per percent of RouteSeparation.</summary>
        public const float BreakBurstSeconds = 0.55f, BreakBurstSpeedPerPercent = 0.03f, BreakBurstTauPerPercent = 0.06f;
        /// <summary>The pocket pressure (0-1) from which a throw counts as made under pressure (Cool Under Fire).</summary>
        public const float UnderPressureAt = 0.25f;
        private readonly float[] _leaderPoints = new float[2];

        /// <summary>Context bits of the current down for every agent of the play (see <see cref="AbilityFx.StaticContexts"/>).</summary>
        internal int PlayContext { get { return _playCtx; } }

        private void ComputePlayContexts(Player[] offenseRoster, Player[] defenseRoster)
        {
            int m = 0;
            GameState gs = State;
            if (Kind == PlayKind.Scrimmage && gs.Phase == GamePhase.Scrimmage)
            {
                if (gs.Down >= 3) m |= AbilityFx.Bit(AbilityContext.ThirdFourthDown);
                if (gs.BallSpot >= 80) m |= AbilityFx.Bit(AbilityContext.RedZone);
                if (gs.Distance <= 2) m |= AbilityFx.Bit(AbilityContext.ShortYardage);
            }
            if (gs.Phase != GamePhase.Kickoff && gs.InLastTwoMinutesOfHalf) m |= AbilityFx.Bit(AbilityContext.TwoMinute);
            if (gs.Quarter >= 4 && Math.Abs(gs.HomeScore - gs.AwayScore) <= 8) m |= AbilityFx.Bit(AbilityContext.CloseGameLate);
            if (Kind == PlayKind.Kickoff || Kind == PlayKind.Punt) m |= AbilityFx.Bit(AbilityContext.Return);
            _playCtx = m;
            _leaderPoints[0] = LeadershipOf(offenseRoster);
            _leaderPoints[1] = LeadershipOf(defenseRoster);
        }

        /// <summary>
        /// Team leadership on the field: the sum of the Locker Room Leader effects (1 to 3 each, capped at 4.5), worth a third of a rating point each for every teammate:
        /// one tier-2 or tier-3 leader lifts his unit by a point, two tier-3 leaders by 1.5 (rounded to 2), a lone tier-1 leader is a morale story only. (A whole point
        /// for every leader inflated the league's scoring by about 0.4 a team.)
        /// </summary>
        private static float LeadershipOf(Player[] roster)
        {
            if (roster == null) return 0f;
            float lead = 0f;
            for (int i = 0; i < roster.Length; i++)
            {
                Player p = roster[i];
                if (p == null || p.Abilities == null || p.Abilities.Count == 0) continue;
                AbilityFx fx = AbilityFx.For(p);
                if (fx != null) lead += fx.Sum(SimHook.Leadership, 0);
            }
            return Math.Min(4.5f, lead) * LeadershipPointsPerEffect;
        }

        /// <summary>Rating points a teammate gains per point of Locker Room Leader effect on the field.</summary>
        public const float LeadershipPointsPerEffect = 0.34f;

        /// <summary>Builds the agent's ratings (morale, leadership and the abilities whose context is known now) and its ability lookup.</summary>
        private void BakeAbilities(SimAgent a, Player player, SimSide side, Assignment assign)
        {
            a.Fx = null; a.CtxMask = 0; a.PlayerModPoints = 0;
            if (Options != null && !Options.PlayerModifiers) return;

            // ---- morale + leadership: the same +/- points on every rating (Injury is a durability, not a skill) ----
            float m = MoraleEngine.RatingModifier(player) + _leaderPoints[(int)side];
            int mod = (int)Math.Round(m, MidpointRounding.AwayFromZero);
            Ratings r = a.R;
            if (mod != 0)
            {
                for (int k = 0; k < Ratings.KindCount; k++)
                {
                    var kind = (RatingKind)k;
                    if (kind == RatingKind.Injury) continue;
                    r.Set(kind, r.Get(kind) + mod);
                }
                a.PlayerModPoints = mod;
            }

            // ---- abilities ----
            AbilityFx fx = AbilityFx.For(player);
            if (fx != null)
            {
                int ctx = _playCtx;
                var cov = assign as CoverageAssignment;
                if (cov != null && cov.IsMan) ctx |= AbilityFx.Bit(AbilityContext.ManToMan);
                if (assign is BlitzAssignment) ctx |= AbilityFx.Bit(AbilityContext.Blitz);
                a.Fx = fx; a.CtxMask = ctx;
                bool pass = OffPlay != null && OffPlay.IsPass, run = OffPlay != null && OffPlay.IsRun;
                Bump(ref r, RatingKind.ThrowPower, fx.Sum(SimHook.ThrowPower, ctx));
                Bump(ref r, RatingKind.ThrowAccuracy, fx.Sum(SimHook.ThrowAccuracy, ctx));
                Bump(ref r, RatingKind.BreakTackle, fx.Sum(SimHook.BreakTackle, ctx));
                Bump(ref r, RatingKind.RunBlocking, fx.Sum(SimHook.RunBlock, ctx));
                Bump(ref r, RatingKind.PassBlocking, fx.Sum(SimHook.PassBlock, ctx));
                Bump(ref r, RatingKind.ManCoverage, fx.Sum(SimHook.ManCoverage, ctx));
                Bump(ref r, RatingKind.ZoneCoverage, fx.Sum(SimHook.ZoneRange, ctx));
                Bump(ref r, RatingKind.KickPower, fx.Sum(SimHook.KickPower, ctx));
                Bump(ref r, RatingKind.KickAccuracy, fx.Sum(SimHook.KickAccuracyPressure, ctx));
                Bump(ref r, RatingKind.Elusiveness, fx.Sum(SimHook.ReturnElusiveness, ctx));
                if (pass) Bump(ref r, RatingKind.BlockShedding, fx.Sum(SimHook.PassRush, ctx));
                if (run)
                {
                    float stop = fx.Sum(SimHook.RunStop, ctx);
                    Bump(ref r, RatingKind.BlockShedding, stop);
                    Bump(ref r, RatingKind.Tackling, stop);
                }
                float clutch = fx.Sum(SimHook.Clutch, ctx);
                if (clutch != 0f)
                    for (int k = 0; k < Ratings.KindCount; k++)
                    {
                        var kind = (RatingKind)k;
                        if (kind == RatingKind.Injury || kind == RatingKind.Stamina) continue;
                        Bump(ref r, kind, clutch);
                    }
            }
            a.R = r;
        }

        private static void Bump(ref Ratings r, RatingKind kind, float points)
        {
            if (points == 0f) return;
            r.Set(kind, r.Get(kind) + (int)Math.Round(points, MidpointRounding.AwayFromZero));
        }

        /// <summary>The ability multipliers that never change during a play: cutting and tiring.</summary>
        private static void ApplyStaticBodyAbilities(SimAgent a)
        {
            if (a.Fx == null) return;
            AgentBody b = a.Body;
            float cut = a.Fxv(SimHook.CutSpeedLoss);
            if (cut != 0f)
            {
                // Stop On A Dime: the plant costs less speed, and he also turns with more lateral grip
                b.CutBleed = SimMath.Clamp(1f + cut / 100f, 0.2f, 2f);
                float grip = SimMath.Clamp(1f - cut / 100f * CutGripPerPercent, 0.7f, 1.5f);
                b.LatBase *= grip;
            }
            float fat = a.Fxv(SimHook.FatigueRate);
            if (fat != 0f) b.DrainScale = SimMath.Clamp(1f + fat / 100f, 0.2f, 2f);
        }

        /// <summary>
        /// Every tick: the speed and burst abilities whose context is live right now (a back in the open field, a burner on a deep route, a scrambling
        /// quarterback, a returner with the ball, a pulling lineman, a blitzer after his timing, a receiver after the catch).
        /// </summary>
        private void UpdateAbilityBodies()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                if (a.Fx == null) continue;
                AbilityFx fx = a.Fx;
                AgentBody b = a.Body;
                int dyn = 0;
                float dirY = a.Side == SimSide.Offense ? 1f : -1f;
                if (a.HasBall && (Kind != PlayKind.Scrimmage || (a.Pos.Y - LosY) * dirY >= 4f)) dyn |= AbilityFx.Bit(AbilityContext.OpenField);
                var ra = a.Assign as RouteAssignment;
                if (ra != null && !a.HasBall && !a.RouteDone && OffenseAI.ContinuesDownfield(ra.Route)) dyn |= AbilityFx.Bit(AbilityContext.Deep);
                int mask = a.CtxMask | dyn;
                float speedPts = fx.Sum(SimHook.TopSpeed, mask) * SpeedGain;
                float accPts = fx.Sum(SimHook.Acceleration, mask) * SpeedGain;
                float mul = 1f;
                if (a.Slot == 0 && a.Side == SimSide.Offense && (a.QbState == QbPhase.Scramble || (a.HasBall && QbHasCrossedLos))) speedPts += fx.Sum(SimHook.ScrambleSpeed, mask) * SpeedGain;
                if (a.HasBall && (Kind == PlayKind.Kickoff || Kind == PlayKind.Punt)) speedPts += fx.Sum(SimHook.ReturnSpeed, mask) * ReturnSpeedGain;
                var ba = a.Assign as BlockAssignment;
                if (ba != null && ba.Block == BlockKind.Pull && a.PullStage < 99) speedPts += fx.Sum(SimHook.PullSpeed, mask) * PullSpeedGain;
                if (a.Assign is BlitzAssignment)
                {
                    var bz = (BlitzAssignment)a.Assign;
                    if (Time >= bz.Delay) mul *= 1f + fx.Sum(SimHook.BlitzTiming, mask) * 0.002f;
                }
                float tauMul = 1f;
                if (ra != null && !a.HasBall && !a.RouteDone)
                {
                    float sep = fx.Sum(SimHook.RouteSeparation, mask);
                    mul *= 1f + sep * RouteSeparationSpeedPerPercent;   // Crisp Routes, Burner: a sharper release
                    if (Time < a.BreakBurstUntil) { mul *= 1f + sep * BreakBurstSpeedPerPercent; tauMul = Math.Max(0.5f, 1f - sep * BreakBurstTauPerPercent); }   // and a burst out of every hard break
                }
                if (a.HasBall && PassCompleted && a.Id == CatcherId) mul *= 1f + fx.Sum(SimHook.YardsAfterCatch, mask) * 0.003f;
                if (speedPts != 0f)
                {
                    int s0 = a.R.Speed;
                    int s1 = SimMath.Clamp(s0 + (int)Math.Round(speedPts, MidpointRounding.AwayFromZero), 0, 99);
                    mul *= Physique.VMaxForSpeed(s1) / Physique.VMaxForSpeed(s0);
                }
                b.AbilityVMax = mul;
                if (accPts != 0f)
                {
                    int c0 = a.R.Acceleration;
                    int c1 = SimMath.Clamp(c0 + (int)Math.Round(accPts, MidpointRounding.AwayFromZero), 0, 99);
                    b.AbilityTau = Physique.TauFor(c1) / Physique.TauFor(c0);
                }
                else b.AbilityTau = 1f;
                b.AbilityTau *= tauMul;
            }
        }

        /// <summary>Diagnostics of the last forward pass (tests and tuning): how far the ball was aimed from where the passer meant it, the throw distance, the pocket pressure and the ball speed at release.</summary>
        public float LastThrowAimError { get; private set; }
        public float LastThrowDistance { get; private set; }
        public float LastThrowPressure { get; private set; }
        public float LastThrowSpeed { get; private set; }
        /// <summary>v0.9.0 (E-083 / E-103): the style of the last forward pass (Bullet / Touch / Lob); the animation layer picks the throw take (a lob over 50 yd is the hail mary take) from it and from <see cref="LastThrowDistance"/>.</summary>
        public ThrowStyle LastThrowStyle { get; internal set; }
        internal void NoteThrow(float error, float distance, float pressure, float speed) { LastThrowAimError = error; LastThrowDistance = distance; LastThrowPressure = pressure; LastThrowSpeed = speed; }

        // ---- shared dynamic contexts ----
        internal static readonly int CtxUnderPressure = AbilityFx.Bit(AbilityContext.UnderPressure);
        internal static readonly int CtxOverMiddle = AbilityFx.Bit(AbilityContext.OverMiddle);
        internal static readonly int CtxOpenField = AbilityFx.Bit(AbilityContext.OpenField);
        internal static readonly int CtxDeep = AbilityFx.Bit(AbilityContext.Deep);
        internal static readonly int CtxLineOfScrimmage = AbilityFx.Bit(AbilityContext.LineOfScrimmage);

        /// <summary>The receiver is running a vertical route (his Deep ability context).</summary>
        internal static bool OnDeepRoute(SimAgent r)
        {
            var ra = r.Assign as RouteAssignment;
            return ra != null && !r.RouteDone && OffenseAI.ContinuesDownfield(ra.Route);
        }

        /// <summary>The receiver is between the hash-side numbers, past the line (his Over The Middle ability context).</summary>
        internal bool IsOverMiddle(SimAgent r)
        {
            return Math.Abs(r.Pos.X - FieldGeometry.CenterX) < 10f && r.Pos.Y > LosY + 1.5f;
        }
    }
}
