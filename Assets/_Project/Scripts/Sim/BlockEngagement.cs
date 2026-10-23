using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    public enum RushMove { None, Bull, Swim, Rip, Spin, Speed, Disengage }

    /// <summary>
    /// A blocker (or two) locked with a defender. Each tick a leverage contest moves <see cref="Lev"/> between -1 (defender wins,
    /// sheds) and +1 (blockers win, pancake). Position, momentum and mass feed the contest; the pair drifts as a unit.
    /// </summary>
    public sealed class BlockEngagement
    {
        public SimAgent Defender;
        public readonly List<SimAgent> Blockers = new List<SimAgent>(2);
        /// <summary>-1 defender dominant .. +1 blockers dominant.</summary>
        public float Lev;
        public bool RunMode;
        public float Age;
        public RushMove Move;
        /// <summary>The previous pass-rush move of this lock-up (set up the counter, E-175).</summary>
        public RushMove LastMove;
        public float MoveTime;
        public float MoveCooldown;
        public bool IsShedding;
        public float ShedLeft;
        public float DominantTime;
        public float HighLevTime;
        public bool Finished;
        public RushMove UserMove;
        public int UserSide;

        public SimAgent Primary { get { return Blockers.Count > 0 ? Blockers[0] : null; } }

        public void UserHint(SimAgent a, ControlInput input)
        {
            if (a != Defender) return;
            if (MoveCooldown > 0f) return;
            if (input.Spin || input.JukeLeft || input.JukeRight || input.Dive || input.Tackle || input.Jump || input.StiffArm || input.Truck)
            {
                UserMove = input.Spin ? RushMove.Spin : (input.StiffArm || input.Truck ? RushMove.Bull : (input.JukeLeft || input.JukeRight ? RushMove.Swim : RushMove.Rip));
                UserSide = input.JukeLeft ? -1 : (input.JukeRight ? 1 : (input.Move.X < -0.2f ? -1 : (input.Move.X > 0.2f ? 1 : 0)));
            }
        }
    }

    /// <summary>Owns all block engagements and drives non-engaged blockers (targets, pass sets, pulls, leads, stalks, escorts).</summary>
    internal sealed partial class BlockingSystem
    {
        private readonly PlaySimulation S;
        public readonly List<BlockEngagement> Active = new List<BlockEngagement>();
        private readonly List<BlockEngagement> _remove = new List<BlockEngagement>();

        /// <summary>Overrides who is blocking (kicks); null means "the side with the ball".</summary>
        public SimSide? BlockerSideOverride;
        public bool ForcePassMode;
        public bool HandedOff;

        public BlockingSystem(PlaySimulation s) { S = s; }

        public SimSide BlockerSide { get { return BlockerSideOverride.HasValue ? BlockerSideOverride.Value : S.BallSide; } }

        public bool PassMode
        {
            get
            {
                if (ForcePassMode) return true;
                if (S.BallSide != SimSide.Offense) return false;
                // a draw shows pass protection until the exchange: the rushers get upfield past the blockers and the back follows them
                return S.OffAI.IsPassPlay && !S.PassCompleted && !S.QbHasCrossedLos;
            }
        }

        // =====================================================================================
        // Engagement lifecycle
        // =====================================================================================

        public void Release(SimAgent a)
        {
            BlockEngagement e = a.Engagement;
            if (e == null) { if (a.State == AgentState.Engaged) a.State = AgentState.Normal; return; }
            if (a == e.Defender) { End(e); return; }
            e.Blockers.Remove(a);
            a.Engagement = null;
            if (a.State == AgentState.Engaged) a.State = AgentState.Normal;
            if (e.Blockers.Count == 0) End(e);
        }

        public void ReleaseAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--) End(Active[i]);
        }

        private void End(BlockEngagement e)
        {
            if (e.Finished) return;
            e.Finished = true;
            if (e.Defender != null)
            {
                e.Defender.Engagement = null;
                if (e.Defender.State == AgentState.Engaged) e.Defender.State = AgentState.Normal;
            }
            for (int i = 0; i < e.Blockers.Count; i++)
            {
                e.Blockers[i].Engagement = null;
                if (e.Blockers[i].State == AgentState.Engaged) e.Blockers[i].State = AgentState.Normal;
            }
            Active.Remove(e);
        }

        private bool CanBlock(SimAgent b)
        {
            if (S.Kind == PlayKind.Kickoff && S.Specials.IsOnsideKick && S.Specials.KickAway) return false;
            if (b.Side != BlockerSide) return false;
            if (b.HasBall || b.State != AgentState.Normal || b.Body.FallenTime > 0f) return false;
            if (b.Engagement != null) return false;
            if (b.Body.StumbleTime > 0.25f) return false;
            if (S.Kind == PlayKind.Scrimmage && b.Side == SimSide.Offense && b.Slot == 0) return false; // the QB never blocks
            if (S.Kind != PlayKind.Scrimmage && b.Side == SimSide.Offense && S.Specials.IsKickerOrHolder(b)) return false;
            if (b.Assign is RouteAssignment && S.BallSide == SimSide.Offense && S.Kind == PlayKind.Scrimmage) return false;
            if (b.Assign is CarryAssignment && S.BallSide == SimSide.Offense && S.Kind == PlayKind.Scrimmage && !(((CarryAssignment)b.Assign).IsFake && b.PullStage >= 99)) return false;
            if (b.Assign is ReturnerAssignment || b.Assign is LaneAssignment) return false;
            if ((S.Kind == PlayKind.Punt || S.Kind == PlayKind.Kickoff) && S.Specials.KickAway && b.Side == SimSide.Offense) return false; // the coverage unit is running to the ball
            return true;
        }

        private bool CanBeBlocked(SimAgent d)
        {
            if (d.Side == BlockerSide) return false;
            if (d.State == AgentState.Down || d.Body.FallenTime > 0.2f || d.State == AgentState.Tackling) return false;
            if (d.HasBall) return false;
            return true;
        }

        // =====================================================================================
        // Per-tick update
        // =====================================================================================

        public void Update()
        {
            if (S.Phase != PlayPhase.Live) return;
            ReleaseScreenLine();
            DetectNewEngagements();

            _remove.Clear();
            for (int i = 0; i < Active.Count; i++)
            {
                BlockEngagement e = Active[i];
                UpdateEngagement(e);
                if (e.Finished) _remove.Add(e);
            }
            // (End() removes from Active while iterating over a copy)
            for (int i = 0; i < _remove.Count; i++) Active.Remove(_remove[i]);
        }

        private void DetectNewEngagements()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent b = S.Agents[i];
                if (!CanBlock(b)) continue;
                // the first step: nobody locks up on the snap tick, linemen fire out of their stances for about a tenth of a second
                if (S.Kind == PlayKind.Scrimmage && S.Time < 0.09f + 0.06f * (1f - b.Body.Agility01)) continue;
                // a puller is off the line before he can lock up with whoever happens to be across from him
                var pba = b.Assign as BlockAssignment;
                if (pba != null && pba.Block == BlockKind.Pull && pba.Path.Count > 0 && b.PullStage < 1 && (b.Pos - b.StartPos).Length < 1.5f && S.Kind == PlayKind.Scrimmage) continue;
                SimAgent best = null; float bestScore = float.MaxValue;
                for (int j = 0; j < 22; j++)
                {
                    SimAgent d = S.Agents[j];
                    if (d.Side == b.Side) continue;
                    if (!CanBeBlocked(d)) continue;
                    if (d.Engagement != null && d.Engagement.Blockers.Count >= 2) continue;
                    if (d.NoEngageId == b.Id && S.Time < d.NoEngageUntil) continue;
                    if (b.NoEngageId == d.Id && S.Time < b.NoEngageUntil) continue;
                    if (S.Tackles.InTackleLock(b, d)) continue;
                    Vec2 rel = d.Pos - b.Pos;
                    float dist = rel.Length;
                    float contact = b.Body.Radius + d.Body.Radius + (S.Kind == PlayKind.Scrimmage ? 0.14f : 0.34f);   // v0.9.0 (E-166): the hands meet at 0.14 yd past the bodies (was 0.34), so the get-off closes a real gap first
                    if (dist > contact) continue;
                    // must be roughly in front of the blocker (or his assigned man), unless pass blocking
                    bool assigned = b.BlockTargetId == d.Id;
                    float facing = b.Facing.Dot(rel.Norm());
                    if (!assigned && facing < -0.25f && !PassMode) continue;
                    // v0.9.0 (E-182): a free blocker joins an engaged defender only when his own man is gone (no target, down, locked with someone else, beaten) and the defender is next to him
                    if (d.Engagement != null && !assigned)
                    {
                        SimAgent bt = b.BlockTargetId >= 0 ? S.Agents[b.BlockTargetId] : null;
                        bool mineGone = bt == null || bt.State == AgentState.Down || bt.Body.FallenTime > 0.3f || (bt.Engagement != null && !bt.Engagement.Blockers.Contains(b)) || b.BeatenTime > 0f;
                        if (!mineGone || Math.Abs(d.Pos.X - b.Pos.X) > 2.2f) continue;
                    }
                    // an engaged defender may be doubled only by an adjacent free lineman or his assigned blocker
                    float score = dist - (assigned ? 0.6f : 0f);
                    if (d.Engagement != null) score += 0.3f;
                    if (score < bestScore) { bestScore = score; best = d; }
                }
                if (best != null) TryEngage(b, best);
            }
        }

        private void TryEngage(SimAgent b, SimAgent d)
        {
            // edge rushers can slip contact on first touch with speed/agility (the "dip")
            if (d.Engagement == null && PassMode && (d.Assign is RushAssignment || d.Assign is BlitzAssignment))
            {
                float edge = (d.Body.Agility01 * 0.5f + SimMath.Rating01(d.R.Speed) * 0.3f + SimMath.Rating01(d.R.Acceleration) * 0.2f)
                             - (b.Body.Agility01 * 0.5f + SimMath.Rating01(b.R.PassBlocking) * 0.5f);
                float pSlip = SimMath.Clamp(0.05f + edge * 0.5f, 0f, 0.28f);
                if (d.Speed > 3.5f && S.Rng.NextFloat() < pSlip * 0.25f)
                {
                    d.NoEngageId = b.Id; d.NoEngageUntil = S.Time + 0.5f;
                    b.NoEngageId = d.Id; b.NoEngageUntil = S.Time + 0.5f;
                    b.BeatenTime = 0.5f; b.BeatenBy = d.Id;
                    S.LogEvent(SimEventType.BlockLoss, b.Id, d.Id, TackleType.None, 0f, "slip");
                    return;
                }
            }
            MaybeBlockInBack(b, d);
            BlockEngagement e = d.Engagement;
            if (e == null)
            {
                e = new BlockEngagement { Defender = d, RunMode = !PassMode };
                e.MoveCooldown = PassMode ? 0.70f + S.Rng.NextFloat() * 0.70f : 0.25f + S.Rng.NextFloat() * 0.45f;   // v0.9.0 (E-385): the first rush move comes 0.7-1.4 s after contact (was 0.46-1.0)
                if (!PassMode) e.Lev = SimMath.Clamp(S.Rng.Gaussian(0.10f, 0.34f), -0.9f, 0.9f); // the first step decides a lot against the run
                else if (S.Kind == PlayKind.Scrimmage) e.Lev = SimMath.Clamp(S.Rng.Gaussian(0.03f, 0.12f), -0.2f, 0.4f); // the set: the blocker is square and balanced for the first beat
                d.Engagement = e;
                d.State = AgentState.Engaged;
                d.StateTime = 0f;
                Active.Add(e);
            }
            e.Blockers.Add(b);
            b.Engagement = e;
            b.State = AgentState.Engaged;
            b.StateTime = 0f;
            b.BlockTargetId = d.Id;
        }


        // =====================================================================================
        // Engagement dynamics
        // =====================================================================================

        private static float MassScore(SimAgent a) { return SimMath.Clamp01((a.Body.Mass - 70f) / 100f) * 100f; }

        private float BlockerScore(SimAgent b, bool run)
        {
            float skill = run ? b.R.RunBlocking : b.R.PassBlocking;
            if (b.Fx != null && b.Pos.Y > S.LosY + 2.5f && run) skill += b.FxOnly(SimHook.RunBlock, PlaySimulation.CtxOpenField);   // v0.8.0 Lead Puller: his blocks downfield
            if (b.PlayPos == Position.WR || b.PlayPos == Position.CB || b.PlayPos == Position.FS || b.PlayPos == Position.SS) skill = Math.Max(skill, 35f);
            else if (b.PlayPos == Position.TE || b.PlayPos == Position.FB || b.PlayPos == Position.RB) skill = Math.Max(skill, 42f);   // tight ends and backs block better than receivers do
            if (!PositionGroup.IsOffensiveLine(b.PlayPos) && (S.Kind != PlayKind.Scrimmage || b.Side == SimSide.Defense)) skill = Math.Max(skill, 45f);
            return 0.46f * skill + 0.30f * b.R.Strength + 0.09f * b.R.Awareness + 0.15f * MassScore(b) - 8f * (1f - b.Body.Energy);   // v0.9.0 (E-355): a gassed blocker loses up to 8 points
        }

        private float DefenderScore(SimAgent d)
        {
            float shed = d.R.BlockShedding;
            // skill players (gunners, corners, safeties) get off blocks with speed and quickness rather than technique
            if (!d.IsDefensiveLineman && !PositionGroup.IsLinebacker(d.PlayPos)) shed = Math.Max(shed, 0.75f * (d.R.Agility + d.R.Speed) * 0.5f);
            return 0.46f * shed + 0.30f * d.R.Strength + 0.09f * d.R.Awareness + 0.15f * MassScore(d) - 8f * (1f - d.Body.Energy);
        }

        private void UpdateEngagement(BlockEngagement e)
        {
            float dt = PlaySimulation.Dt;
            SimAgent d = e.Defender;
            if (e.Finished) return;
            // invalid participants
            for (int i = e.Blockers.Count - 1; i >= 0; i--)
            {
                SimAgent b = e.Blockers[i];
                if (b.State == AgentState.Down || b.Body.FallenTime > 0f || b.HasBall)
                {
                    e.Blockers.RemoveAt(i);
                    b.Engagement = null;
                    if (b.State == AgentState.Engaged) b.State = AgentState.Normal;
                }
            }
            if (e.Blockers.Count == 0 || d.State == AgentState.Down || d.Body.FallenTime > 0f || d.HasBall)
            {
                End(e);
                return;
            }
            e.Age += dt;
            if (e.MoveCooldown > 0f) e.MoveCooldown -= dt;
            if (e.MoveTime > 0f) { e.MoveTime -= dt; if (e.MoveTime <= 0f) e.Move = RushMove.None; }

            bool run = e.RunMode;
            // the mode follows the play: a pass play that turns into a scramble or handoff becomes run-mode
            run = !PassMode;
            e.RunMode = run;

            float bScore = BlockerScore(e.Blockers[0], run);
            if (e.Blockers.Count > 1) bScore += 0.62f * BlockerScore(e.Blockers[1], run);
            float dScore = DefenderScore(d);
            float gap = (bScore - dScore) / 100f;

            // momentum: a defender arriving fast against a flat-footed blocker wins the first beat
            Vec2 n0 = (d.Pos - e.Blockers[0].Pos).Norm();
            float closing = (e.Blockers[0].Vel - d.Vel).Dot(n0);
            float momentum = SimMath.Clamp(-closing * 0.012f, -0.05f, 0.05f) * (e.Age < 0.35f ? 1f : 0f);

            // v0.9.0 (E-178): the run bias was 0.30 (+0.22 against linebackers), an equilibrium Lev of +1.12 for equal ratings: an even matchup was a pancake in about 3 s. 0.14 / +0.10 keeps the run game honest.
            float bias = run ? 0.20f : -0.22f + SimTuning.PassBias;
            if (run && !d.IsDefensiveLineman && b0Side(e) == SimSide.Offense) bias += 0.12f; // climbing blockers get angle and leverage on linebackers who are already moving downhill
            // v0.9.0 (E-180): the contest has geometry: a blocker whose man has gotten a shoulder past him (reached / hooked) loses ground, a blocker square in front of his man gains it
            if (run)
            {
                Vec2 drv = BlockDriveDir(e.Blockers[0], d);
                float sep = Math.Max(0.3f, e.Blockers[0].Body.Radius + d.Body.Radius);
                float geo = SimMath.Clamp((d.Pos - e.Blockers[0].Pos).Dot(drv) / sep, -1f, 1f);
                bias += 0.16f * (geo - 0.75f);
            }
            if (e.Age < 1.0f && !run) bias += 0.04f; // initial set favors the blocker
            float noise = S.Rng.Gaussian(0f, 1f) * 0.62f * (float)Math.Sqrt(dt);
            float kGain = run ? 1.05f : 1.55f;
            float drift = (kGain * (gap + bias + momentum) - 0.28f * e.Lev) * dt;
            // a fast, light edge rusher leans on speed rather than strength
            // the coverage unit sheds the wall once the kick is away: gunners and lane runners beat their blockers and run to the ball
            if (S.Kind != PlayKind.Scrimmage && S.Specials.KickAway && d.Side == SimSide.Offense && d.Assign is LaneAssignment && ((LaneAssignment)d.Assign).Gunner) drift -= 0.2f * dt;
            if (S.Kind == PlayKind.Kickoff && S.Specials.KickAway && d.Side == SimSide.Offense) drift -= 0.30f * dt;
            // v0.9.0: a receiver's or back's stalk block on a defensive back or linebacker is a 2-3 s job, not a lock for the rest of the run: after 2 s the defender works free (the safeties used to stay locked 30 yd upfield and a run that beat the front went 65 yd)
            if (run && d.Side == SimSide.Defense && e.Age > 1.6f && e.Blockers.Count > 0 && !PositionGroup.IsOffensiveLine(e.Blockers[0].PlayPos) && !d.IsDefensiveLineman && S.Kind == PlayKind.Scrimmage)
                drift -= (1.1f + 0.5f * SimMath.Rating01(d.R.BlockShedding)) * dt;
            e.Lev += drift + noise;

            // ---- defender moves ----
            if (!e.IsShedding)
            {
                if (e.UserMove != RushMove.None && e.MoveCooldown <= 0f) { ApplyMove(e, e.UserMove, e.UserSide, true); e.UserMove = RushMove.None; }
                else if (e.MoveCooldown <= 0f && !d.ControlledByUser) ChooseMove(e, run);
            }
            e.Lev = SimMath.Clamp(e.Lev, -1.05f, 1.05f);
            // v0.7 commit window: a lock-up lasts at least GamePace.MinEngageSeconds before it can be won or lost, so the engage animation always plays out
            if (e.Age < (run ? GamePace.MinEngageSeconds : GamePace.MinEngagePassSeconds)) e.Lev = SimMath.Clamp(e.Lev, -0.92f, 0.92f);
            // a blocker who is losing his man grabs him
            if (!e.IsShedding && e.Lev < -0.45f && e.Blockers.Count > 0) MaybeHolding(e, e.Blockers[0], 0.0085f * (-e.Lev));

            // ---- outcomes ----
            if (!e.IsShedding && e.Lev <= -1f)
            {
                e.IsShedding = true;
                e.ShedLeft = 0.14f + (1f - SimMath.Rating01(d.R.BlockShedding)) * 0.20f;
                SimAgent b0 = e.Blockers[0];
                S.LogEvent(SimEventType.BlockLoss, b0.Id, d.Id, TackleType.None, e.Age, e.Move.ToString());
                S.LogEvent(SimEventType.Shed, d.Id, b0.Id, TackleType.None, e.Age, e.Move.ToString());
                MaybeHolding(e, b0, 0.8f);
            }
            if (e.IsShedding)
            {
                e.ShedLeft -= dt;
                if (e.ShedLeft <= 0f) { CompleteShed(e); ApplyPairMotion(e); return; }
            }

            if (e.Lev >= (run ? 1f : 0.85f))
            {
                e.HighLevTime += dt;
                // v0.9.0 (E-178 / E-179): a run pancake needs the lead for 0.5 s; the pass-mode pancake was 84 percent per second once the lead held (0.03 per tick), now about 3.5 percent per second
                bool pancake = run ? e.HighLevTime > 0.3f && gap > 0.10f : (e.HighLevTime > 0.9f && S.Rng.NextFloat() < 0.0006f);
                if (pancake)
                {
                    SimAgent b0 = e.Blockers[0];
                    S.LogEvent(SimEventType.Pancake, b0.Id, d.Id);
                    S.LogEvent(SimEventType.BlockWin, b0.Id, d.Id);
                    d.Body.Fall(0.9f + S.Rng.NextFloat() * 0.5f);
                    d.NoEngageId = b0.Id; d.NoEngageUntil = S.Time + 1.5f;
                    b0.NoEngageId = d.Id; b0.NoEngageUntil = S.Time + 1.0f;
                    End(e);
                    return;
                }
            }
            else e.HighLevTime = 0f;

            // combo blocks: once the double team has the man controlled, one blocker climbs to the second level
            // v0.9.0 (E-181): the double team holds for 0.8 s; one man peels only when a linebacker is really coming (within 2.5 yd)
            if (e.Blockers.Count == 2 && run && e.Lev > -0.1f && e.Age > 0.8f)
            {
                SimAgent peel = PickPeel(e);
                if (peel != null)
                {
                    peel.NoEngageId = d.Id; peel.NoEngageUntil = S.Time + 1.5f;
                    peel.BlockTargetId = -1;
                    peel.ReactionClock = 0f;
                    e.Blockers.Remove(peel);
                    peel.Engagement = null;
                    peel.State = AgentState.Normal;
                }
            }
            ApplyPairMotion(e);
        }

        private static SimSide b0Side(BlockEngagement e) { return e.Blockers.Count > 0 ? e.Blockers[0].Side : SimSide.Offense; }

        private SimAgent PickPeel(BlockEngagement e)
        {
            // the blocker closest to an uncovered linebacker/safety climbs to him
            SimAgent best = null; float bd = 2.5f;
            for (int i = 0; i < e.Blockers.Count; i++)
            {
                SimAgent b = e.Blockers[i];
                for (int j = 0; j < 22; j++)
                {
                    SimAgent t = S.Agents[j];
                    if (t.Side == b.Side || t.Engagement != null || t.State == AgentState.Down) continue;
                    if (!(PositionGroup.IsLinebacker(t.PlayPos) || PositionGroup.IsSafety(t.PlayPos))) continue;
                    float d = (t.Pos - b.Pos).Length;
                    if (d < bd) { bd = d; best = b; }
                }
            }
            return best;
        }

        private void MaybeBlockInBack(SimAgent b, SimAgent d)
        {
            if (!S.Options.Penalties || b.Side != SimSide.Offense || S.Kind != PlayKind.Scrimmage || !S.FoulOpen(SimSide.Offense) || S.BallSide != SimSide.Offense) return;
            if (d.Pos.Y - S.LosY < 3f) return;
            Vec2 toD = (d.Pos - b.Pos).Norm();
            if (d.Facing.Dot(toD) < 0.55f || d.Speed < 2.5f) return;
            if (S.Rng.NextFloat() < 0.04f) S.Flag(PenaltyType.IllegalBlockInBack, b, d.Pos.Y - S.LosY);
        }

        private void MaybeHolding(BlockEngagement e, SimAgent blocker, float scale = 1f)
        {
            if (!S.Options.Penalties || blocker.Side != SimSide.Offense || S.Kind != PlayKind.Scrimmage) return;
            if (!S.FoulOpen(SimSide.Offense)) return;
            if (S.BallSide != SimSide.Offense) return;
            Vec2 focus = S.Carrier != null ? S.Carrier.Pos : S.Qb.Pos;
            float near = (e.Defender.Pos - focus).Length;
            if (near > 7f) return;
            // v0.10.0: 0.062 -> 0.0545. The passer holds the ball a beat longer now (covered quarterbacks, the sack work), so the line loses more blocks per play and was drawing 114 holds in 3,500 plays (91 before);
            // the per-tick chance is scaled back to keep the overall flag rate at about 10 percent with holding still leading
            float p = scale * 0.0545f * (1.25f - SimMath.Rating01(blocker.R.Awareness)) * (1.1f - 0.5f * SimMath.Rating01(blocker.R.PassBlocking));
            if (S.Rng.NextFloat() < p)
                S.Flag(PenaltyType.OffensiveHolding, blocker, blocker.Pos.Y - S.LosY);
        }

        private void CompleteShed(BlockEngagement e)
        {
            SimAgent d = e.Defender;
            for (int i = 0; i < e.Blockers.Count; i++)
            {
                SimAgent b = e.Blockers[i];
                b.BeatenTime = 0.9f; b.BeatenBy = d.Id;
                b.NoEngageId = d.Id; b.NoEngageUntil = S.Time + 1.1f;
                d.NoEngageId = b.Id; d.NoEngageUntil = S.Time + 1.1f;
            }
            d.ShedBurst = 0.7f;
            // a clean shed gives the defender a shot of acceleration downhill
            Vec2 goal = DefenderGoalDir(e);
            d.Body.Knock(goal * 1.6f);
            End(e);
        }

        // =====================================================================================
        // Pass-rush moves
        // =====================================================================================

        private void ChooseMove(BlockEngagement e, bool run)
        {
            SimAgent d = e.Defender;
            SimAgent b = e.Blockers[0];
            if (run)
            {
                e.MoveCooldown = 0.55f + S.Rng.NextFloat() * 0.5f;
                float off = (0.6f * d.R.BlockShedding + 0.4f * d.R.Strength) - (0.6f * b.R.RunBlocking + 0.4f * b.R.Strength);
                if (e.Blockers.Count > 1) off -= 12f;
                float p = Sigmoid(off / 12f);
                e.Move = RushMove.Disengage; e.MoveTime = 0.35f;
                if (S.Rng.NextFloat() < p) e.Lev -= 0.22f + 0.15f * S.Rng.NextFloat();
                else e.Lev += 0.05f;
                return;
            }
            // pass rush: choose a move that fits the rusher's tools
            float str = d.R.Strength, agi = d.R.Agility, spd = d.R.Speed;
            // v0.9.0 (E-175): the move comes from the man and the matchup: a 300 lb tackle leans on the bull rush and rarely spins, an edge man wins with speed from the outside shoulder, and a move that failed is followed by its counter
            float massF = SimMath.Clamp01((d.Body.Mass - 100f) / 55f);
            bool edge = (d.Assign is RushAssignment && ((RushAssignment)d.Assign).Contain) || (d.Assign is BlitzAssignment && ((BlitzAssignment)d.Assign).Edge);
            float wBull = (0.7f + Math.Max(0f, (str - agi) / 40f) + (e.Lev > 0.2f ? 0.4f : 0f)) * (1f + 0.7f * massF);
            float wSwim = (1.0f + Math.Max(0f, (agi - 60f) / 40f)) * (1f - 0.35f * massF);
            float wRip = 0.9f + Math.Max(0f, (str - 60f) / 50f);
            float wSpin = (0.4f + Math.Max(0f, (agi - 75f) / 30f)) * (1f - 0.85f * massF);
            float wSpeed = edge ? 0.9f + Math.Max(0f, (spd - 70f) / 25f) : 0.05f;
            switch (e.LastMove)
            {
                case RushMove.Bull: wRip *= 1.8f; wSwim *= 1.3f; break;        // power set up the finesse counter
                case RushMove.Speed: wSpin *= 2.4f; wRip *= 1.4f; break;       // the speed rush turns inside
                case RushMove.Swim: case RushMove.Rip: wBull *= 1.5f; break;   // finesse set up the bull
                case RushMove.Spin: wSpeed *= 1.5f; break;
            }
            float total = wBull + wSwim + wRip + wSpin + wSpeed;
            float roll = S.Rng.NextFloat() * total;
            RushMove m;
            if ((roll -= wBull) < 0f) m = RushMove.Bull;
            else if ((roll -= wSwim) < 0f) m = RushMove.Swim;
            else if ((roll -= wRip) < 0f) m = RushMove.Rip;
            else if ((roll -= wSpin) < 0f) m = RushMove.Spin;
            else m = RushMove.Speed;
            e.LastMove = m;
            ApplyMove(e, m, 0, false);
        }

        private void ApplyMove(BlockEngagement e, RushMove m, int side, bool user)
        {
            SimAgent d = e.Defender;
            SimAgent b = e.Blockers[0];
            e.Move = m;
            e.MoveTime = 0.45f;
            e.MoveCooldown = 0.85f + S.Rng.NextFloat() * 0.55f;
            float skill, counter, win, lose;
            switch (m)
            {
                case RushMove.Bull:
                    skill = 0.6f * d.R.Strength + 0.4f * d.R.BlockShedding + MassScore(d) * 0.1f;
                    counter = 0.6f * b.R.Strength + 0.4f * b.R.PassBlocking + MassScore(b) * 0.1f;
                    win = 0.34f; lose = 0.08f; break;
                case RushMove.Swim:
                    skill = 0.5f * d.R.BlockShedding + 0.3f * d.R.Agility + 0.2f * d.R.Acceleration;
                    counter = 0.5f * b.R.PassBlocking + 0.3f * b.R.Agility + 0.2f * b.R.Awareness;
                    win = 0.42f; lose = 0.10f; break;
                case RushMove.Rip:
                    skill = 0.5f * d.R.BlockShedding + 0.3f * d.R.Strength + 0.2f * d.R.Agility;
                    counter = 0.5f * b.R.PassBlocking + 0.3f * b.R.Strength + 0.2f * b.R.Awareness;
                    win = 0.40f; lose = 0.09f; break;
                case RushMove.Spin:
                    skill = 0.4f * d.R.BlockShedding + 0.4f * d.R.Agility + 0.2f * d.R.Acceleration;
                    counter = 0.5f * b.R.PassBlocking + 0.5f * b.R.Agility;
                    win = 0.55f; lose = 0.22f; break;
                default:
                    skill = 0.5f * d.R.Speed + 0.3f * d.R.Acceleration + 0.2f * d.R.BlockShedding;
                    counter = 0.4f * b.R.Agility + 0.3f * b.R.PassBlocking + 0.3f * b.R.Speed;
                    win = 0.38f; lose = 0.08f; break;
            }
            counter += 14f; // technique, help from the quarterback side, and the set of the pocket
            if (e.Blockers.Count > 1) counter += 14f;
            if (d.Fx != null && !e.RunMode && S.OffAI.PocketPressure(S.Qb) >= 0.3f) skill += d.FxOnly(SimHook.PassRush, PlaySimulation.CtxUnderPressure);   // v0.8.0 Bull Rusher: once the pocket is collapsing his moves hit harder
            float p = Sigmoid((skill - counter) / 11f);
            if (m == RushMove.Bull && b.Fx != null) p *= Math.Max(0f, 1f - b.Fxv(SimHook.BullRushResist) / 100f);   // v0.8.0 Immovable Anchor
            if (S.Rng.NextFloat() < p) e.Lev -= win * (0.8f + 0.4f * S.Rng.NextFloat());
            else e.Lev += lose;
            if (side != 0) e.UserSide = side;
        }

        private static float Sigmoid(float x) { return 1f / (1f + (float)Math.Exp(-x)); }

        // =====================================================================================
        // Pair motion
        // =====================================================================================

        private Vec2 DefenderGoalDir(BlockEngagement e)
        {
            SimAgent d = e.Defender;
            // toward the passer in pass mode, otherwise toward the ball carrier / football
            if (!e.RunMode)
            {
                Vec2 to = S.Qb.Pos - d.Pos;
                if (to.LenSq() > 0.04f) return to.Norm();
            }
            SimAgent c = S.Carrier;
            Vec2 target = c != null ? c.Pos : S.Ball.Pos.XY;
            Vec2 dirv = target - d.Pos;
            if (dirv.LenSq() < 0.04f) return new Vec2(0f, d.Side == SimSide.Defense ? -1f : 1f);
            return dirv.Norm();
        }

        private void ApplyPairMotion(BlockEngagement e)
        {
            if (e.Finished) return;
            SimAgent d = e.Defender;
            SimAgent b0 = e.Blockers[0];
            Vec2 toD = d.Pos - b0.Pos;
            float dist = toD.Length;
            Vec2 n = dist > 1e-3f ? toD * (1f / dist) : new Vec2(0f, 1f);
            Vec2 dd = DefenderGoalDir(e);
            Vec2 db;
            if (!e.RunMode) db = (dd * -1f); // push the rusher back / mirror
            else db = BlockDriveDir(b0, d);

            float strRatio = (float)Math.Sqrt(Math.Max(0.3f, e.Blockers[0].R.Strength + 10f) / Math.Max(10f, d.R.Strength + 10f));
            float pmax = e.RunMode ? 2.8f : 1.5f;   // a pass-protection pair never walks faster than 1.5 yd/s
            float lev = e.Lev;
            Vec2 v = Vec2.Zero;
            if (e.RunMode)
            {
                float f = SimMath.Clamp(0.40f + 1.1f * lev, -0.8f, 1.2f);
                if (f >= 0f) v = db * (f * pmax * Math.Min(1.4f, strRatio));
                else v = dd * (-f * pmax * Math.Min(1.4f, 1f / strRatio));
            }
            else v = PassProVelocity(e, b0, d, dd, db, pmax, strRatio);
            if (e.RunMode && b0.Side == SimSide.Offense && S.OffPlay.Category == PlayCategory.Draw && !S.QbHasCrossedLos && S.Time < 2.6f && lev > -0.55f)
            {
                // the draw: the line shows pass protection and rides the rushers up and past the back's lane
                var dca = FindCarryAssignment();
                float laneX = dca != null && dca.Path.Count > 1 ? S.Rel(dca.Path[1]).X : S.BallX0;
                float away = d.Pos.X >= laneX ? 1f : -1f;
                float ramp = SimMath.Clamp01((S.Time - 0.2f) / 0.45f);
                v = new Vec2(away * 1.5f, -0.5f) * ramp + v * (1f - 0.6f * ramp);
            }
            // lateral arc for finesse / speed moves
            if (e.Move == RushMove.Swim || e.Move == RushMove.Rip || e.Move == RushMove.Speed || e.Move == RushMove.Spin)
            {
                Vec2 lat = dd.Perp();
                float sgn = e.UserSide != 0 ? e.UserSide : (d.Pos.X < b0.Pos.X ? -1f : 1f);
                v = v + lat * (sgn * 1.4f) * (lev <= 0.2f ? 1f : 0.3f);
            }
            if (e.Move == RushMove.Bull) v = v + db * (-0.4f);
            float spd = v.Length;
            float cap = Math.Min(d.Body.EffectiveVMax, b0.Body.EffectiveVMax) * 0.45f;
            if (spd > cap) v = v * (cap / spd);

            float s0 = b0.Body.Radius + d.Body.Radius + 0.03f;
            float err = dist - s0;
            Vec2 spring = n * (err * 6f);
            d.Want = v - spring * 0.5f;
            b0.Want = v + spring * 0.5f;
            d.HasFace = true; d.FaceDir = n * -1f;
            b0.HasFace = true; b0.FaceDir = n;
            d.Intent = AgentAction.Block;
            b0.Intent = AgentAction.Block;
            if (e.Blockers.Count > 1)
            {
                SimAgent b1 = e.Blockers[1];
                // second blocker takes the opposite shoulder
                float side = (b1.Pos - d.Pos).Cross(n) >= 0f ? 1f : -1f;
                Vec2 post = d.Pos + n.Rotate(side * 1.9f) * -1f * s0;
                Vec2 toPost = post - b1.Pos;
                b1.Want = v + toPost * 6f;
                b1.HasFace = true; b1.FaceDir = (d.Pos - b1.Pos).Norm();
                b1.Intent = AgentAction.Block;
            }
        }

        private Vec2 BlockDriveDir(SimAgent b, SimAgent d)
        {
            // run block: push the man away from the play's running lane, moving upfield
            SimAgent c = S.Carrier;
            Vec2 lane;
            var ca = FindCarryAssignment();
            if (ca != null && ca.Path.Count > 0)
            {
                var hole = HolePoint(ca);
                lane = S.Rel(hole);
            }
            else lane = c != null ? c.Pos : new Vec2(S.BallX0, S.LosY + 3f);
            float up = b.Side == SimSide.Offense ? 1f : -1f;
            Vec2 away = d.Pos - lane;
            away = new Vec2(away.X, 0f);
            Vec2 lateral = away.LenSq() > 0.01f ? away.Norm() : new Vec2(0f, 0f);
            Vec2 drive = new Vec2(lateral.X * 0.55f, up * 0.85f).Norm();
            return drive;
        }

        private CarryAssignment FindCarryAssignment()
        {
            for (int i = 1; i < 11; i++)
            {
                var ca = S.Agents[i].Assign as CarryAssignment;
                if (ca != null && !ca.IsFake) return ca;
            }
            return null;
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class BlockingSystem
    {
        /// <summary>The designed point where the run crosses the line of scrimmage (relative to the ball): the first path point at or past the line.</summary>
        private static Vec2 HolePoint(CarryAssignment ca)
        {
            for (int i = 0; i < ca.Path.Count; i++)
                if (ca.Path[i].Y > -0.8f) return ca.Path[i];
            return ca.Path[ca.Path.Count - 1];
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class BlockingSystem
    {
        /// <summary>Retreat speed (yd/s, toward the passer) of a pass-blocking pair at equilibrium: the kick-slide that gives a clean, cupped pocket.</summary>
        public const float PocketRetreat = 0.95f;

        /// <summary>
        /// Pass-protection pair motion. Blockers do not drive upfield: they kick-slide back at a steady pace (tackles the most, the center the
        /// least, so the line bows into a cup around the passer), win by holding their depth and slowing the retreat, and lose by being walked
        /// back toward the quarterback. Lateral spring keeps the splits so guards do not collapse into the center.
        /// </summary>
        private Vec2 PassProVelocity(BlockEngagement e, SimAgent b0, SimAgent d, Vec2 dd, Vec2 db, float pmax, float strRatio)
        {
            float lev = e.Lev;
            bool protector = b0.Side == SimSide.Offense && S.Kind == PlayKind.Scrimmage;
            Vec2 v;
            if (lev > 0f) v = db * (lev * (protector ? 0.40f : pmax) * Math.Min(1.4f, strRatio));
            else v = dd * (-lev * pmax * Math.Min(1.4f, 1f / strRatio));
            if (!protector) return v;
            // slot factor: tackles kick out and back furthest, the center sits closest to the line
            float slot = 1.0f;
            if (b0.Slot == 2 || b0.Slot == 6) slot = 1.25f; else if (b0.Slot == 4) slot = 0.75f; else if (!PositionGroup.IsOffensiveLine(b0.PlayPos)) slot = 1.0f;
            float retreat = PocketRetreat * slot * (1f - 0.55f * SimMath.Clamp(lev, -1f, 1f));
            if (S.Time > 3.4f) retreat *= 0.6f;
            // never walk into the passer's lap
            Vec2 q = S.Qb.Pos;
            if (b0.Pos.Y < q.Y + 2.4f) retreat = 0f;
            // the retreat is straight back with only a hint toward the passer, so tackles do not slide into the guards
            Vec2 back = new Vec2(SimMath.Clamp((q.X - b0.Pos.X) * 0.05f, -0.2f, 0.2f), -1f).Norm();
            v = v + back * retreat;
            // keep the splits: lateral spring to the blocker's own slot unless he is being beaten
            if (lev > -0.4f && PositionGroup.IsOffensiveLine(b0.PlayPos))
            {
                float k = SimMath.Clamp((b0.StartPos.X - b0.Pos.X) * 1.1f, -0.9f, 0.9f);
                v = new Vec2(v.X + k, v.Y);
            }
            return v;
        }
    }
}
