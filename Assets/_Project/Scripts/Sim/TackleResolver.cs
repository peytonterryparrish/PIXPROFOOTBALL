using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Resolves defenders reaching the ball carrier: closing speed, contact angle, momentum ratio and Tackling vs
    /// BreakTackle/Strength/Carrying decide between wrap-ups, big hits (fumble risk), shoestrings, drags that grow
    /// into gang tackles, broken tackles, dives and pushes out of bounds.
    /// </summary>
    internal sealed class TackleResolver
    {
        private readonly PlaySimulation S;
        private readonly List<SimAgent> _draggers = new List<SimAgent>();
        private bool _qbHitLogged;

        public TackleResolver(PlaySimulation s) { S = s; }

        public bool InTackleLock(SimAgent a, SimAgent b)
        {
            if (a.State == AgentState.Tackling && b.HasBall) return true;
            if (b.State == AgentState.Tackling && a.HasBall) return true;
            return false;
        }

        public void Update()
        {
            // recover tacklers
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.State == AgentState.Tackling && a.StateTime > 0.75f && a.Body.FallenTime <= 0f)
                {
                    SimAgent c = S.Carrier;
                    if (c == null || c.DragMass <= 0f || !IsDragger(a)) a.State = AgentState.Normal;
                }
            }
            // the passer can still be hit for a moment after he lets go: a QbHit event (and, if it is late, roughing the passer)
            if (S.PassThrown && S.Time - S.PassReleaseTime < 0.8f && S.PasserId >= 0 && !_qbHitLogged)
            {
                SimAgent q = S.Agents[S.PasserId];
                if (!q.HasBall && q.State == AgentState.Normal)
                {
                    for (int i = 11; i < 22; i++)
                    {
                        SimAgent d = S.Agents[i];
                        if (d.State == AgentState.Engaged || d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                        if ((d.Pos - q.Pos).Length > d.Body.Radius + q.Body.Radius + 0.55f) continue;
                        _qbHitLogged = true;
                        S.LogEvent(SimEventType.QbHit, d.Id, q.Id, new Vec3(q.Pos, 0f), TackleType.None, S.Time - S.PassReleaseTime);
                        if (S.Options.Penalties && S.FoulOpen(SimSide.Defense) && S.Time - S.PassReleaseTime > 0.25f && S.Rng.NextFloat() < 0.30f * (1.5f - SimMath.Rating01(d.R.Awareness)))
                            S.Flag(PenaltyType.RoughingThePasser, d, q.Pos.Y - S.LosY);
                        q.Body.Stumble(0.25f);
                        break;
                    }
                }
            }
            SimAgent carrier = S.Carrier;
            if (carrier == null || carrier.State == AgentState.Down) return;
            UpdateDrag(carrier);
            if (S.Phase != PlayPhase.Live) return;
            if (_pendingDives.Count > 0)
            {
                UpdatePendingDives();
                if (S.Phase != PlayPhase.Live) return;
                carrier = S.Carrier;
                if (carrier == null || carrier.State == AgentState.Down) return;
            }

            for (int i = 0; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.Side == carrier.Side) continue;
                if (d.State == AgentState.Engaged || d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                if (d.State == AgentState.Tackling && !(d.Move == CarrierMove.Dive && d.MoveTime > 0f)) continue;
                if (d.TackleCooldown > 0f) continue;
                if (carrier.IgnoreAgent == d.Id && S.Time < carrier.IgnoreUntil) continue;
                if (d.IgnoreAgent == carrier.Id && S.Time < d.IgnoreUntil) continue;
                Vec2 rel = carrier.Pos - d.Pos;
                float dist = rel.Length;
                bool diving = d.Move == CarrierMove.Dive && d.MoveTime > 0f;
                float reach = d.Body.Radius + carrier.Body.Radius + 0.42f + 0.18f * SimMath.Rating01(d.R.Tackling);
                if (diving) { if (S.BodyBetween(d, carrier)) continue; reach += 1.25f; }
                if (carrier.Move == CarrierMove.StiffArm && carrier.MoveTime > 0f) reach -= 0.2f;
                if (dist > reach) continue;
                Attempt(d, carrier, diving);
                if (S.Phase != PlayPhase.Live) return;
                carrier = S.Carrier;
                if (carrier == null || carrier.State == AgentState.Down) return;
            }
        }

        /// <summary>
        /// Draggers hold on to the carrier. v0.7 fix: this runs BEFORE the bodies step. It used to be set inside <see cref="UpdateDrag"/>, which runs after the step,
        /// so the intent was wiped at the start of the next tick and the first tackler stood still while the carrier dragged away (the tackle was then credited from 3-5 yd away).
        /// </summary>
        public void ApplyDragHold()
        {
            if (_draggers.Count == 0) return;
            SimAgent c = S.Carrier;
            if (c == null || c.DragMass <= 0f) return;
            for (int i = 0; i < _draggers.Count; i++)
            {
                SimAgent g = _draggers[i];
                if (g.State != AgentState.Tackling || g.Body.FallenTime > 0f) continue;
                Vec2 holdDir = c.Speed > 0.3f ? c.Vel.Norm() : c.Facing;
                Vec2 hold = c.Pos - holdDir * (c.Body.Radius + g.Body.Radius - 0.05f);
                g.Want = (hold - g.Pos) * 8f + c.Vel;
                g.HasFace = true; g.FaceDir = (c.Pos - g.Pos).Norm();
                // he has his arms around him: a carrier who outruns the tackler's own legs pulls him along instead of leaving him standing (up to 3.6 yd/s of drag)
                Vec2 to = c.Pos - g.Pos;
                float gap = to.Length - g.Body.Radius - c.Body.Radius;
                if (gap > 0.45f) g.Body.Pos = g.Body.Pos + to.Norm() * Math.Min(gap - 0.45f, 0.06f);
            }
        }

        private bool IsDragger(SimAgent a)
        {
            for (int i = 0; i < _draggers.Count; i++) if (_draggers[i] == a) return true;
            return false;
        }

        // ---------------------------------------------------------------- the contest

        private void Attempt(SimAgent d, SimAgent c, bool diving)
        {
            d.TackleCooldown = 0.32f;
            Vec2 rel = c.Pos - d.Pos;
            float dist = Math.Max(0.05f, rel.Length);
            Vec2 toC = rel * (1f / dist);
            float closing = (d.Vel - c.Vel).Dot(toC);
            Vec2 cdir = c.Speed > 1.2f ? c.Vel.Norm() : c.Facing;
            float cosA = cdir.Dot((d.Pos - c.Pos).Norm());
            int category = cosA > 0.5f ? 0 : (cosA > -0.35f ? 1 : 2); // 0 head-on, 1 side, 2 from behind

            float idm = d.Body.Mass * Math.Max(0f, d.Vel.Dot(toC)) + 0.6f * d.Body.Mass;
            float icm = c.Body.Mass * Math.Max(0f, c.Vel.Dot(toC * -1f)) + 0.6f * c.Body.Mass;
            float ratio = idm / Math.Max(1f, icm);

            float tkEff = d.R.Tackling >= 75 ? 75f + (d.R.Tackling - 75f) * 1.45f : 75f + (d.R.Tackling - 75f) * 0.46f;
            float tackleSkill = 0.52f * tkEff + 0.16f * d.R.Strength + 0.12f * d.R.Awareness + 0.10f * d.R.Agility + 0.10f * d.R.Speed;
            float carrierSkill = 0.34f * c.R.BreakTackle + 0.22f * c.R.Strength + 0.14f * c.R.Carrying + 0.18f * c.R.Elusiveness + 0.12f * c.R.Agility;
            float mod = 0f;
            mod += category == 0 ? 5f : (category == 2 ? -14f : -3f);
            if (diving) mod -= 14f;
            mod += (d.Body.Mass - c.Body.Mass) / 40f * 5f;
            { float facing = d.Facing.Dot(toC); if (facing < 0.3f) mod -= 12f * SimMath.Clamp01((0.3f - facing) / 1.3f); }   // a defender running away from the carrier cannot make a full-strength tackle
            mod += (float)Math.Log(ratio) * 7f;
            mod -= 5f * SimMath.Clamp01(c.Speed / Math.Max(1f, c.Body.VMax));
            mod += Math.Min(6f, closing * 0.6f);
            if (c.Protecting) mod += 3f;
            if (c.Body.StumbleTime > 0f) mod += 10f;
            if (d.Body.StumbleTime > 0f) mod -= 9f;
            if (c.DragMass > 0f) mod += 10f + 22f * (c.DragMass / c.Body.Mass);
            if (c.Slot == 0 && c.Side == SimSide.Offense && S.OffAI.IsPassPlay && !S.PassThrown) mod += 12f; // passers rarely escape an unblocked rusher
            if (c.State == AgentState.Normal && c.Body.FallenTime > 0f) mod += 20f;

            float margin = tackleSkill - carrierSkill + mod + S.Rng.Gaussian(0f, 12f) + 2.0f;

            // sack bookkeeping
            bool sackCandidate = c.Slot == 0 && c.Side == SimSide.Offense && S.OffAI.IsPassPlay && !S.PassThrown && c.Pos.Y <= S.LosY + 0.01f && S.BallSide == SimSide.Offense;
            if (S.Options.ForceTackleAfter > 0f && !sackCandidate && c.Pos.Y < S.LosY + S.Options.ForceTackleAfter) return;   // test hook: open-field contact only
            if (S.Options.ForceTackle.HasValue) { ForceOutcome(d, c, S.Options.ForceTackle.Value, diving, closing, category, sackCandidate); return; }

            if (diving)
            {
                // v0.7 commit window: the lunge has been decided, but nobody goes down until the diver's hands are really on the carrier
                // (the old rule downed the runner when the lunge STARTED, up to 1.8 yd away: a phantom tackle)
                d.TackleCooldown = 1.0f;
                _pendingDives.Add(new PendingDive { D = d, C = c, Margin = margin, Closing = closing, Category = category, Sack = sackCandidate, Ratio = ratio, T0 = S.Time });
                return;
            }
            Resolve(d, c, diving, margin, closing, category, sackCandidate, ratio);
        }

        // ---------------------------------------------------------------- dive commit window

        private struct PendingDive { public SimAgent D, C; public float Margin, Closing, Ratio, T0; public int Category; public bool Sack; }
        private readonly List<PendingDive> _pendingDives = new List<PendingDive>();

        /// <summary>Longest a dive tackle may stay in the air before it counts as a whiff (seconds).</summary>
        public const float DiveCommitSeconds = GamePace.DiveCommitSeconds;
        /// <summary>Seconds between first contact of a tackle and the whistle: the carrier is wrapped up and goes down on screen before the play is called dead.</summary>
        public const float WrapSeconds = GamePace.TackleWrapSeconds;

        private void UpdatePendingDives()
        {
            for (int i = _pendingDives.Count - 1; i >= 0; i--)
            {
                PendingDive p = _pendingDives[i];
                SimAgent d = p.D, c = p.C;
                bool gone = S.Carrier != c || c.State == AgentState.Down || d.State == AgentState.Engaged || d.State == AgentState.Down;
                if (gone) { _pendingDives.RemoveAt(i); continue; }
                float gap = (c.Pos - d.Pos).Length - d.Body.Radius - c.Body.Radius;
                float armReach = 0.42f + 0.18f * SimMath.Rating01(d.R.Tackling);
                if (S.BodyBetween(d, c)) { _pendingDives.RemoveAt(i); continue; }
                if (gap <= armReach)
                {
                    _pendingDives.RemoveAt(i);
                    Resolve(d, c, true, p.Margin, p.Closing, p.Category, p.Sack, p.Ratio);
                    if (S.Phase != PlayPhase.Live) return;
                    continue;
                }
                if (S.Time - p.T0 > DiveCommitSeconds || (d.Move != CarrierMove.Dive && S.Time - p.T0 > 0.12f))
                {
                    // the lunge ended short: he lands on the grass with nothing in his hands
                    _pendingDives.RemoveAt(i);
                    BrokenTackle(d, c, true);
                }
            }
        }

        private void Resolve(SimAgent d, SimAgent c, bool diving, float margin, float closing, int category, bool sackCandidate, float ratio)
        {
            if (margin > 0f)
            {
                // clean stop
                bool big = margin > 31f && closing > 6.2f && category != 2 && !diving;   // QA-185: a big hit is a highlight, not every routine tackle
                TackleType type;
                if (c.DragMass > 0f) type = TackleType.GangTackle;
                else if (diving) type = TackleType.DiveTackle;
                else if (big) type = TackleType.BigHit;
                else type = TackleType.WrapUp;

                // heavy, fast carriers drag the first tackler a bit before going down
                if (type == TackleType.WrapUp && ratio < 0.85f && c.Speed > 3.2f && !sackCandidate && c.DragMass <= 0f && S.Rng.NextFloat() < 0.55f)
                {
                    StartDrag(d, c);
                    return;
                }
                BringDown(d, c, type, closing, category, sackCandidate);
                return;
            }
            if (margin > -12f)
            {
                if (category == 2 || diving)
                {
                    Shoestring(d, c, diving);
                }
                else if (c.DragMass <= 0f && !sackCandidate)
                {
                    StartDrag(d, c);
                }
                else
                {
                    BringDown(d, c, c.DragMass > 0f ? TackleType.GangTackle : TackleType.WrapUp, closing, category, sackCandidate);
                }
                return;
            }
            // broken tackle
            BrokenTackle(d, c, diving);
        }

        // ---------------------------------------------------------------- outcomes

        /// <summary>Test hook (<see cref="PlaySimOptions.ForceTackle"/>): resolves the contact to a chosen outcome instead of the rating contest.</summary>
        private void ForceOutcome(SimAgent d, SimAgent c, TackleType type, bool diving, float closing, int category, bool sack)
        {
            switch (type)
            {
                case TackleType.Drag:
                    if (c.DragMass <= 0f && !sack) StartDrag(d, c); else BringDown(d, c, TackleType.WrapUp, closing, category, sack);
                    break;
                case TackleType.Shoestring: Shoestring(d, c, diving); break;
                case TackleType.BrokenTackle: BrokenTackle(d, c, diving); break;
                case TackleType.PushOutOfBounds: FinishTackle(d, c, TackleType.PushOutOfBounds, false, false, closing); break;
                default: BringDown(d, c, type == TackleType.None ? TackleType.WrapUp : type, closing, category, sack); break;
            }
        }

        private void BringDown(SimAgent d, SimAgent c, TackleType type, float closing, int category, bool sack)
        {
            // push out of bounds
            float toLine = FieldGeometry.DistanceToSideline(c.Pos.X);
            float towardX = c.Pos.X < FieldGeometry.CenterX ? -c.Vel.X : c.Vel.X;
            if (type != TackleType.BigHit && !sack && toLine < 1.3f && towardX > 1.0f && c.Side == S.BallSide && S.Rng.NextFloat() < 0.45f + 0.5f * SimMath.Rating01(d.R.Tackling))
            {
                type = TackleType.PushOutOfBounds;
            }

            bool qbPass = sack;
            // fumble roll before the whistle
            float pFum = 0.0040f;
            if (type == TackleType.BigHit) pFum = 0.030f;
            else if (type == TackleType.GangTackle) pFum = 0.012f;
            else if (type == TackleType.DiveTackle) pFum = 0.006f;
            pFum *= 1.45f - SimMath.Rating01(c.R.Carrying);
            pFum *= 0.75f + d.R.Tackling / 200f + closing / 40f;
            if (c.Protecting) pFum *= 0.45f;
            if (qbPass) pFum = 0.05f * (1.55f - SimMath.Rating01(c.R.Carrying)) * (0.8f + d.R.Strength / 200f);
            if (c.Move == CarrierMove.Spin || c.Move == CarrierMove.Truck) pFum *= 1.6f;
            pFum *= 0.92f;   // QA-351: lost fumbles were 0.35 per team-game against about 0.5 in the NFL
            bool fumble = S.Rng.NextFloat() < pFum && !S.Options.ForceTackle.HasValue;

            FinishTackle(d, c, type, sack, fumble, closing);
        }

        private void FinishTackle(SimAgent d, SimAgent c, TackleType type, bool sack, bool fumble, float closing)
        {
            S.LastTackleType = type;
            S.TacklerId = d.Id;
            c.State = AgentState.Down;
            c.StateTime = 0f;
            Vec2 dirv = (c.Pos - d.Pos).Norm();
            // carrier falls forward a little with the contact
            float slide = SimMath.Clamp(0.12f * c.Speed + 0.08f * Math.Max(0f, closing), 0.1f, 1.2f);

            c.Body.Fall(1.1f);
            c.Body.Vel = c.Body.Vel * 0.55f + dirv * (slide * 0.8f);
            S.PlayAction(d, TackleAction(type), 0.8f);
            d.State = AgentState.Tackling;
            d.StateTime = 0f;
            d.Body.Vel = d.Body.Vel * 0.35f;
            d.TacklesMade++;
            if (type == TackleType.DiveTackle) d.Body.Fall(0.8f);
            S.Blocks.Release(c);

            MaybeFoul(d, c, type, sack, closing);
            if (type == TackleType.PushOutOfBounds)
            {
                c.Body.Vel = new Vec2(c.Pos.X < FieldGeometry.CenterX ? -3f : 3f, c.Body.Vel.Y * 0.3f);
                { float lineX = c.Pos.X < FieldGeometry.CenterX ? -0.15f : FieldGeometry.Width + 0.15f; c.Body.Pos = new Vec2(c.Pos.X + SimMath.Clamp(lineX - c.Pos.X, -0.45f, 0.45f), c.Pos.Y); }   // shoved over the line, never teleported more than a few tenths of a yard
                S.OutOfBounds = true;
                S.LogEvent(SimEventType.Tackle, d.Id, c.Id, new Vec3(c.Pos, 0f), type, closing);
                S.EndPlay(EndReason.OutOfBounds);
                return;
            }
            S.LogEvent(SimEventType.Tackle, d.Id, c.Id, new Vec3(c.Pos, 0f), type, closing);

            if (sack && fumble)
            {
                // a strip-sack is a sack first: credit it before the ball comes loose
                S.QbSack = true; S.SackerId = d.Id;
                S.LogEvent(SimEventType.Sack, d.Id, c.Id, new Vec3(c.Pos, 0f), type, S.LosY - c.Pos.Y);
            }
            if (fumble)
            {
                Vec2 push = dirv * (1.5f + closing * 0.35f);
                c.State = AgentState.Down;
                S.Fumble(c, d, push);
                // the ball is live; the carrier is down but the play continues
                return;
            }

            if (sack)
            {
                S.QbSack = true;
                S.SackerId = d.Id;
                S.LogEvent(SimEventType.Sack, d.Id, c.Id, new Vec3(c.Pos, 0f), type, S.LosY - c.Pos.Y);
                S.EndPlayAfter(c.Pos.Y <= 0.2f ? EndReason.Safety : EndReason.Sack, WrapSeconds);
                if (c.Pos.Y <= 0.2f) { S.SafetyScored = true; }
                return;
            }
            if (c.Side == SimSide.Offense && c.Pos.Y + 0.2f <= 0f && S.BallSide == SimSide.Offense)
            {
                S.SafetyScored = true;
                S.EndPlayAfter(EndReason.Safety, WrapSeconds);
                return;
            }
            S.EndPlayAfter(EndReason.Tackle, WrapSeconds);
        }

        /// <summary>Late hits, face masks and roughing: the defender's discipline decides whether a hard tackle draws a flag.</summary>
        private void MaybeFoul(SimAgent d, SimAgent c, TackleType type, bool sack, float closing)
        {
            if (!S.Options.Penalties || !S.FoulOpen(SimSide.Defense) || S.Kind != PlayKind.Scrimmage || c.Side != SimSide.Offense || S.BallSide != SimSide.Offense) return;
            float dis = 1.5f - SimMath.Rating01(d.R.Awareness);
            float spot = c.Pos.Y - S.LosY;
            bool qb = c.Slot == 0;
            if (qb && S.PassThrown)
            {
                // hitting the passer after he let go
                // only a hit that comes after the ball has left his hand (and before the play is a long way gone) can be roughing
                if (S.Time - S.PassReleaseTime > 0.25f && S.Time - S.PassReleaseTime < 1.2f && S.Rng.NextFloat() < 0.40f * dis) S.Flag(PenaltyType.RoughingThePasser, d, spot);
                return;
            }
            if (type == TackleType.PushOutOfBounds && S.Rng.NextFloat() < 0.05f * dis) { S.Flag(PenaltyType.UnnecessaryRoughness, d, spot); return; }
            if (sack && S.Rng.NextFloat() < 0.05f * dis) { S.Flag(PenaltyType.RoughingThePasser, d, spot); return; }
            if (type == TackleType.BigHit && S.Rng.NextFloat() < 0.014f * dis) { S.Flag(PenaltyType.UnnecessaryRoughness, d, spot); return; }
            if (S.Rng.NextFloat() < 0.0042f * dis) S.Flag(PenaltyType.FaceMask, d, spot);
        }

        private static AgentAction TackleAction(TackleType t)
        {
            switch (t)
            {
                case TackleType.BigHit: return AgentAction.TackleBigHit;
                case TackleType.DiveTackle: return AgentAction.TackleDive;
                case TackleType.GangTackle: return AgentAction.TackleGang;
                case TackleType.Shoestring: return AgentAction.TackleShoestring;
                default: return AgentAction.TackleWrapUp;
            }
        }

        private void StartDrag(SimAgent d, SimAgent c)
        {
            c.DragMass += d.Body.Mass;
            c.DragTime = 0f;
            c.DragTackler = d.Id;
            d.State = AgentState.Tackling;
            d.StateTime = 0f;
            d.DragTackler = -1;
            _draggers.Add(d);
            S.PlayAction(d, AgentAction.TackleWrapUp, 1.0f);
            S.LogEvent(SimEventType.Tackle, d.Id, c.Id, new Vec3(c.Pos, 0f), TackleType.Drag);
            S.LastTackleType = TackleType.Drag;
            S.TacklerId = d.Id;
            ApplyDragSlow(c);
        }

        private void ApplyDragSlow(SimAgent c)
        {
            float mc = c.Body.Mass;
            float strength = 0.55f + 0.45f * SimMath.Rating01(c.R.Strength);
            float f = mc * strength / (mc * strength + 0.75f * c.DragMass);
            c.Body.SpeedMultiplier = SimMath.Clamp(f, 0.12f, 1f);
        }

        private void UpdateDrag(SimAgent c)
        {
            if (c.DragMass <= 0f)
            {
                if (_draggers.Count > 0) _draggers.Clear();
                return;
            }
            c.DragTime += PlaySimulation.Dt;
            ApplyDragSlow(c);
            // draggers hang on to the carrier
            for (int i = _draggers.Count - 1; i >= 0; i--)
            {
                SimAgent g = _draggers[i];
                if (g.State != AgentState.Tackling || g.Body.FallenTime > 0f) { _draggers.RemoveAt(i); c.DragMass = Math.Max(0f, c.DragMass - g.Body.Mass); continue; }
                Vec2 holdDir = c.Speed > 0.3f ? c.Vel.Norm() : c.Facing;
                Vec2 hold = c.Pos - holdDir * (c.Body.Radius + g.Body.Radius - 0.05f);
                g.Want = (hold - g.Pos) * 8f + c.Vel;
                g.HasFace = true; g.FaceDir = (c.Pos - g.Pos).Norm();
            }
            if (c.DragMass <= 0f) { c.Body.SpeedMultiplier = 1f; return; }
            // break free?
            float pFree = 0.0006f * (c.R.BreakTackle + c.R.Strength * 0.5f) * 0.1f;   // per tick
            float limit = 0.9f + 0.7f * SimMath.Rating01(c.R.Strength);
            bool stopped = c.Speed < 1.5f;
            if (S.Rng.NextFloat() < pFree)
            {
                for (int i = 0; i < _draggers.Count; i++)
                {
                    _draggers[i].Body.Fall(0.8f);
                    _draggers[i].State = AgentState.Normal;
                    S.LogEvent(SimEventType.BrokenTackle, _draggers[i].Id, c.Id);
                }
                _draggers.Clear();
                c.DragMass = 0f;
                c.Body.SpeedMultiplier = 1f;
                c.Body.Stumble(0.25f);
                return;
            }
            if (stopped || c.DragTime > limit)
            {
                SimAgent lead = _draggers.Count > 0 ? _draggers[0] : S.Agents[c.DragTackler];
                // the tackler credited (and shown) is the one actually hanging on to him, not whoever grabbed him first and fell behind
                for (int di = 1; di < _draggers.Count; di++)
                    if ((_draggers[di].Pos - c.Pos).LenSq() < (lead.Pos - c.Pos).LenSq()) lead = _draggers[di];
                if ((lead.Pos - c.Pos).Length - lead.Body.Radius - c.Body.Radius > 0.7f)
                {
                    // nobody credited is actually touching him any more: the nearest defender on his feet makes the stop
                    SimAgent near = null; float nd = 99f;
                    for (int ni = 0; ni < 22; ni++)
                    {
                        SimAgent o = S.Agents[ni];
                        if (o.Side == c.Side || o.State == AgentState.Down || o.Body.FallenTime > 0.3f) continue;
                        float od = (o.Pos - c.Pos).Length - o.Body.Radius - c.Body.Radius;
                        if (od < nd) { nd = od; near = o; }
                    }
                    if (near != null && nd < 0.7f) lead = near;
                }
                TackleType type = _draggers.Count >= 2 ? TackleType.GangTackle : TackleType.Drag;
                c.Body.SpeedMultiplier = 1f;
                c.DragMass = 0f;
                _draggers.Clear();
                FinishTackle(lead, c, type == TackleType.Drag ? TackleType.WrapUp : type, false, false, 1f);
            }
        }

        private void Shoestring(SimAgent d, SimAgent c, bool diving)
        {
            d.Body.Fall(0.8f);
            d.State = AgentState.Tackling;
            d.StateTime = 0f;
            S.PlayAction(d, diving ? AgentAction.TackleDive : AgentAction.TackleShoestring, 0.8f);
            c.Body.Stumble(0.55f);
            c.Body.Vel = c.Body.Vel * 0.7f;
            S.LogEvent(SimEventType.Tackle, d.Id, c.Id, new Vec3(c.Pos, 0f), TackleType.Shoestring);
            S.LastTackleType = TackleType.Shoestring;
            c.IgnoreAgent = d.Id; c.IgnoreUntil = S.Time + 0.6f;
            d.TackleCooldown = 1.1f;   // the player who whiffed does not re-attempt on the next tick
            // a stumbling carrier can still go down on the next contact
            S.Blocks.Release(c);
        }

        private void BrokenTackle(SimAgent d, SimAgent c, bool diving)
        {
            d.Body.Fall(0.9f + 0.4f * S.Rng.NextFloat());
            d.State = AgentState.Tackling;
            d.StateTime = 0f;
            S.PlayAction(d, diving ? AgentAction.TackleDive : AgentAction.TackleWrapUp, 0.8f);
            c.IgnoreAgent = d.Id; c.IgnoreUntil = S.Time + 0.7f;
            d.TackleCooldown = 1.3f;
            c.Body.Vel = c.Body.Vel * 0.9f;
            S.LogEvent(SimEventType.BrokenTackle, d.Id, c.Id, new Vec3(c.Pos, 0f), TackleType.BrokenTackle);
            S.LastTackleType = TackleType.BrokenTackle;
        }
    }
}
