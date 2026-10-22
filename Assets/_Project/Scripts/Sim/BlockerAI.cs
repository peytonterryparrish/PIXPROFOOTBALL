using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    internal sealed partial class BlockingSystem
    {
        public Vec2 AnchorOverride;
        public bool HasAnchor;
        /// <summary>The point pass protection shields (the quarterback, punter, or holder).</summary>
        public Vec2 Anchor { get { return HasAnchor ? AnchorOverride : S.Qb.Pos; } }

        private readonly List<SimAgent> _tmpA = new List<SimAgent>();
        private readonly List<SimAgent> _tmpB = new List<SimAgent>();

        // =====================================================================================
        // Snap-time protection scheme
        // =====================================================================================

        public void OnSnap()
        {
            AssignPassPro();
            AssignRunBlocks();
            // the monotone assignment above holds for the first 0.3 s (blockers used to re-pick on the first tick and ignore it)
            if (S.Kind == PlayKind.Scrimmage)
                for (int i = 0; i < 22; i++) if (S.Agents[i].Side == BlockerSide && S.Agents[i].Assign is BlockAssignment && S.Agents[i].BlockTargetId >= 0) S.Agents[i].ReactionClock = 0.3f;
        }

        /// <summary>
        /// Run schemes: match the zone/man blockers to the defensive front with a monotone (non-crossing) minimum-distance
        /// assignment, first to the linemen and then the unmatched blockers to linebackers, so no one is left unblocked by accident.
        /// </summary>
        private void AssignRunBlocks()
        {
            if (S.Kind != PlayKind.Scrimmage) return;
            var blockers = new List<SimAgent>();
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.Side != BlockerSide) continue;
                var ba = a.Assign as BlockAssignment;
                if (ba == null) continue;
                bool ok = ba.Block == BlockKind.Zone || (ba.Block == BlockKind.Man && (ba.Target == BlockTarget.NearestDefender || ba.Target == BlockTarget.Playside)) || ba.Block == BlockKind.Wedge || ba.Block == BlockKind.Double;
                if (ok && (PositionGroup.IsOffensiveLine(a.PlayPos) || a.PlayPos == Position.TE)) blockers.Add(a);
            }
            if (blockers.Count == 0) return;
            int rd = RunDir();
            blockers.SortStable((p, q) => p.StartPos.X.CompareTo(q.StartPos.X));
            var front = new List<SimAgent>();
            var second = new List<SimAgent>();
            for (int i = 0; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.Side == BlockerSide) continue;
                float rel = Math.Abs(d.StartPos.Y - S.LosY);
                if (d.IsDefensiveLineman || (rel < 2.3f && PositionGroup.IsLinebacker(d.PlayPos))) front.Add(d);
                else if ((PositionGroup.IsLinebacker(d.PlayPos) || PositionGroup.IsSafety(d.PlayPos)) && rel < 9.5f) second.Add(d);
            }
            front.SortStable((p, q) => p.StartPos.X.CompareTo(q.StartPos.X));
            second.SortStable((p, q) => p.StartPos.X.CompareTo(q.StartPos.X));
            var matched = new HashSet<int>();
            MonotoneMatch(blockers, front, rd, matched);
            var rest = new List<SimAgent>();
            foreach (SimAgent b in blockers) if (!matched.Contains(b.Id)) rest.Add(b);
            if (rest.Count > 0 && second.Count > 0) MonotoneMatch(rest, second, rd, matched);
            // anyone still without a man doubles the nearest lineman on the playside
            foreach (SimAgent b in blockers)
            {
                if (matched.Contains(b.Id)) continue;
                SimAgent best = null; float bd = 99f;
                foreach (SimAgent d in front)
                {
                    float dd = Math.Abs(d.StartPos.X - (b.StartPos.X + rd * 1.0f));
                    if (dd < bd) { bd = dd; best = d; }
                }
                if (best != null) b.BlockTargetId = best.Id;
            }
        }

        private void MonotoneMatch(List<SimAgent> blockers, List<SimAgent> targets, int runDir, HashSet<int> matched)
        {
            int nb = blockers.Count, nt = targets.Count;
            if (nb == 0 || nt == 0) return;
            float shade = 0.7f * runDir;
            // dp[i, j]: min cost matching first i blockers using first j targets, with each target matched to at most one blocker (order preserving)
            int cap = Math.Min(nb, nt);
            float[,] dp = new float[nb + 1, nt + 1];
            int[,] choice = new int[nb + 1, nt + 1];
            const float skipBlocker = 2.2f; // cost of leaving a blocker without a target
            const float skipTarget = 6.0f;  // cost of leaving a defender unblocked
            for (int i = 0; i <= nb; i++)
                for (int j = 0; j <= nt; j++)
                {
                    if (i == 0 && j == 0) { dp[i, j] = 0f; continue; }
                    float best = float.MaxValue; int ch = 0;
                    if (i > 0 && j > 0)
                    {
                        float c = Math.Abs(targets[j - 1].StartPos.X - (blockers[i - 1].StartPos.X + shade));
                        float v = dp[i - 1, j - 1] + c;
                        if (v < best) { best = v; ch = 1; }
                    }
                    if (i > 0)
                    {
                        float v = dp[i - 1, j] + skipBlocker;
                        if (v < best) { best = v; ch = 2; }
                    }
                    if (j > 0)
                    {
                        float v = dp[i, j - 1] + skipTarget;
                        if (v < best) { best = v; ch = 3; }
                    }
                    dp[i, j] = best; choice[i, j] = ch;
                }
            int ii = nb, jj = nt;
            while (ii > 0 || jj > 0)
            {
                int ch = choice[ii, jj];
                if (ch == 1)
                {
                    blockers[ii - 1].BlockTargetId = targets[jj - 1].Id;
                    matched.Add(blockers[ii - 1].Id);
                    ii--; jj--;
                }
                else if (ch == 2) ii--;
                else if (ch == 3) jj--;
                else break;
            }
        }

        private struct Pair { public SimAgent B, R; public float Cost; }

        private static bool IsRusher(SimAgent d)
        {
            return d.Assign is RushAssignment || d.Assign is BlitzAssignment;
        }

        private void AssignPassPro()
        {
            var blockers = new List<SimAgent>();
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.Side != BlockerSide) continue;
                var ba = a.Assign as BlockAssignment;
                if (ba == null) continue;
                if (ba.Block == BlockKind.PassPro || ba.Block == BlockKind.Chip) blockers.Add(a);
            }
            if (blockers.Count == 0) return;
            var rushers = new List<SimAgent>();
            for (int i = 0; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.Side == BlockerSide) continue;
                if (IsRusher(d)) rushers.Add(d);
            }
            // v0.9.0 (E-176): the offensive front (the line plus a tight end on it) is matched to the defensive front with the SAME monotone (non-crossing) assignment the run blocks use, so a slide
            // protection or a technique alignment can never send a tackle to the guard's man (the cause of the 8.7-sack game with the technique alignment on); the leftover blockers and rushers
            // (a back, a blitzing linebacker or safety) are matched greedily below.
            if (S.Kind == PlayKind.Scrimmage && blockers.Count > 0 && blockers[0].Side == SimSide.Offense)
            {
                var line = new List<SimAgent>(); var front = new List<SimAgent>();
                int slideDir = 0;
                foreach (SimAgent b in blockers)
                {
                    bool onLine = b.StartPos.Y > S.LosY - 1.2f;
                    if (PositionGroup.IsOffensiveLine(b.PlayPos) || (b.PlayPos == Position.TE && onLine)) line.Add(b);
                    var ba0 = (BlockAssignment)b.Assign;
                    if (ba0.Target == BlockTarget.SlideLeft) slideDir = -1; else if (ba0.Target == BlockTarget.SlideRight) slideDir = 1;
                }
                foreach (SimAgent r in rushers)
                    if (r.IsDefensiveLineman || (PositionGroup.IsLinebacker(r.PlayPos) && r.StartPos.Y - S.LosY < 3.4f)) front.Add(r);
                line.SortStable((p, q) => p.StartPos.X.CompareTo(q.StartPos.X));
                front.SortStable((p, q) => p.StartPos.X.CompareTo(q.StartPos.X));
                var matchedPro = new HashSet<int>();
                MonotoneMatch(line, front, slideDir, matchedPro);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                var pairs = new List<Pair>();
                foreach (SimAgent b in blockers)
                {
                    bool ol = PositionGroup.IsOffensiveLine(b.PlayPos) || (b.Side == SimSide.Defense && PositionGroup.IsDefensiveLine(b.PlayPos)) || (b.Side == SimSide.Offense && S.Kind != PlayKind.Scrimmage && PositionGroup.IsOffensiveLine(b.PlayPos));
                    if ((pass == 0) != ol) continue;
                    if (b.BlockTargetId >= 0) continue;
                    foreach (SimAgent r in rushers)
                    {
                        float cost = Math.Abs(r.StartPos.X - b.StartPos.X) + 0.2f * Math.Abs(r.StartPos.Y - b.StartPos.Y);
                        if (ol && !(r.IsDefensiveLineman || PositionGroup.IsLinebacker(r.PlayPos))) cost += 3.5f;
                        if (!ol && r.IsDefensiveLineman) cost += 2.5f;
                        var ba = (BlockAssignment)b.Assign;
                        if (ba.Target == BlockTarget.SlideLeft) cost += (r.StartPos.X > b.StartPos.X) ? 0.8f : 0f;
                        if (ba.Target == BlockTarget.SlideRight) cost += (r.StartPos.X < b.StartPos.X) ? 0.8f : 0f;
                        pairs.Add(new Pair { B = b, R = r, Cost = cost });
                    }
                }
                pairs.SortStable((p, q) => p.Cost.CompareTo(q.Cost));
                var usedB = new HashSet<int>();
                var usedR = new HashSet<int>();
                foreach (SimAgent o in blockers) if (o.BlockTargetId >= 0) usedR.Add(o.BlockTargetId);
                foreach (Pair p in pairs)
                {
                    if (usedB.Contains(p.B.Id) || usedR.Contains(p.R.Id)) continue;
                    usedB.Add(p.B.Id); usedR.Add(p.R.Id);
                    p.B.BlockTargetId = p.R.Id;
                }
            }
        }

        // =====================================================================================
        // Blocker movement (not engaged)
        // =====================================================================================

        public void UpdateBlocker(SimAgent a)
        {
            var ba = a.Assign as BlockAssignment;
            if (ba == null) return;
            a.Intent = AgentAction.Block;
            float vmax = a.Body.EffectiveVMax;

            // beaten: scramble to recover onto the man who beat you
            if (a.BeatenTime > 0f && a.BeatenBy >= 0)
            {
                SimAgent bt = S.Agents[a.BeatenBy];
                Vec2 to = bt.Pos - a.Pos;
                a.Want = to.Norm() * (vmax * 0.8f);
                a.HasFace = true; a.FaceDir = to.Norm();
                return;
            }

            // delayed releases (chip blocks, screen walls)
            if (ba.Block == BlockKind.Chip && S.Time > 0.9f && a.PassProRelease < 0f)
            {
                // v0.9.0 (E-186): the back chips the edge man and leaves when the tackle has him (his lead above 0.3) or after 1.4 s, not at 0.9 s whatever the end is doing
                SimAgent edge = a.BlockTargetId >= 0 ? S.Agents[a.BlockTargetId] : null;
                bool edgeHeld = edge != null && edge.State == AgentState.Engaged && edge.Engagement != null && edge.Engagement.Lev > 0.3f && !edge.Engagement.Blockers.Contains(a);
                bool edgeFree = edge != null && edge.State != AgentState.Engaged && edge.State != AgentState.Down;
                if (edgeHeld || S.Time > 1.4f || edge == null || (!edgeFree && edge.State == AgentState.Down))
                {
                    a.PassProRelease = S.Time;
                    a.BlockTargetId = -1;
                }
            }

            a.ReactionClock -= PlaySimulation.Dt;
            SimAgent t = a.BlockTargetId >= 0 ? S.Agents[a.BlockTargetId] : null;
            bool valid = t != null && t.Side != a.Side && t.State != AgentState.Down && t.Body.FallenTime < 0.4f
                         && !(t.NoEngageId == a.Id && S.Time < t.NoEngageUntil)
                         && (t.Engagement == null || t.Engagement.Blockers.Count < 2 || t.Engagement.Blockers.Contains(a));
            if (a.ReactionClock <= 0f || !valid)
            {
                if (!valid || ba.Block != BlockKind.PassPro || a.ReactionClock <= 0f)
                {
                    SimAgent nt = PickTarget(a, ba, valid ? t : null);
                    a.BlockTargetId = nt != null ? nt.Id : -1;
                    t = nt;
                }
                // v0.9.0 (E-183): a blocker keeps his man for 0.6 s (he used to re-pick every 0.16-0.28 s and thrash between nearby defenders) unless the man is gone
                a.ReactionClock = (valid ? 0.6f : 0.2f) + (1f - SimMath.Rating01(a.R.Awareness)) * 0.12f;
            }

            switch (ba.Block)
            {
                case BlockKind.Pull: PullMove(a, ba, t); return;
                case BlockKind.Wall when S.OffPlay.Category == PlayCategory.Screen: ScreenWallMove(a, ba, t); return;
                case BlockKind.Lead: LeadMove(a, ba, t); return;
                case BlockKind.PassPro:
                case BlockKind.Chip:
                    PassProMove(a, ba, t); return;
                default:
                    RunBlockMove(a, ba, t); return;
            }
        }

        private void PassProMove(SimAgent a, BlockAssignment ba, SimAgent t)
        {
            float vmax = a.Body.EffectiveVMax;
            Vec2 anchor = Anchor;
            bool dirDown = a.Side == SimSide.Offense;
            if (t == null)
            {
                // hold the pocket line, a little off the ball; look for work
                Vec2 hold = new Vec2(a.StartPos.X, a.StartPos.Y + (dirDown ? -0.9f : 0.9f));
                Vec2 toHold = hold - a.Pos;
                a.Want = toHold.Norm() * Math.Min(vmax * 0.5f, toHold.Length * 4f);
                a.HasFace = true; a.FaceDir = new Vec2(0f, dirDown ? 1f : -1f);
                return;
            }
            Vec2 toAnchor = (anchor - t.Pos).Norm();
            Vec2 gp = t.Pos + toAnchor * (a.Body.Radius + t.Body.Radius + 0.28f);
            // v0.9.0 (E-177): the pass set. A pass blocker sets 0.4 yd behind the line and kick-slides back with the rusher's outside shoulder (3.5-4.5 yd/s at most), meeting him there; he no longer
            // chases the rusher up to a point 0.9 yd PAST the line, which met every rush at the line and never gave ground until locked
            bool isLine = PositionGroup.IsOffensiveLine(a.PlayPos) || (S.Kind == PlayKind.Scrimmage && a.Side == SimSide.Offense && a.StartPos.Y > S.LosY - 1.2f);
            float lim = dirDown ? S.LosY - (isLine ? 0.4f : 0.2f) : S.LosY + (isLine ? 0.4f : 0.2f);
            if (S.Kind != PlayKind.Scrimmage) lim = dirDown ? S.LosY + 0.9f : S.LosY - 0.9f;   // kick protection keeps its line
            if (dirDown && gp.Y > lim) gp = new Vec2(gp.X, lim);
            if (!dirDown && gp.Y < lim) gp = new Vec2(gp.X, lim);
            Vec2 to = gp - a.Pos;
            float dist = to.Length;
            float cap = S.Kind == PlayKind.Scrimmage ? Math.Min(vmax * 0.62f, 4.5f) : vmax * 0.82f;
            a.Want = to.Norm() * Math.Min(cap, Math.Max(1.4f, dist * 6f));
            a.HasFace = true; a.FaceDir = (t.Pos - a.Pos).Norm();
        }

        private void RunBlockMove(SimAgent a, BlockAssignment ba, SimAgent t)
        {
            float vmax = a.Body.EffectiveVMax;
            float up = a.Side == SimSide.Offense ? 1f : -1f;
            if (t == null)
            {
                // nothing to block: get downfield toward the ball carrier and find a body
                SimAgent c = S.Carrier;
                Vec2 goal;
                if (c != null && c != a) goal = c.Pos + new Vec2(0f, 4f * up);
                else goal = a.Pos + new Vec2(0f, 4f * up);
                // v0.9.0 (E-187): second-level work first: the nearest unblocked linebacker or safety within 7 yd on the play side, instead of a point 4 yd past the carrier that drags the line across the formation
                SimAgent work = null; float wd = 7f;
                int rdd = RunDir();
                for (int oi = (a.Side == SimSide.Offense ? 11 : 0), oe = oi + 11; oi < oe; oi++)
                {
                    SimAgent d = S.Agents[oi];
                    if (d.State == AgentState.Down || d.Body.FallenTime > 0.3f || d.HasBall || d.Engagement != null) continue;
                    if (!(PositionGroup.IsLinebacker(d.PlayPos) || PositionGroup.IsSafety(d.PlayPos))) continue;
                    if ((d.Pos.X - a.Pos.X) * rdd < -1.5f) continue;
                    if (TargetedByOthers(a, d) > 0) continue;
                    float dd = (d.Pos - a.Pos).Length;
                    if (dd < wd) { wd = dd; work = d; }
                }
                if (work != null) goal = work.Pos + work.Vel * 0.2f;
                else if (c != null && c != a) goal = new Vec2(a.Pos.X + (c.Pos.X - a.Pos.X) * 0.4f, goal.Y);
                Vec2 to = goal - a.Pos;
                a.Want = to.Norm() * (vmax * 0.8f);
                return;
            }
            // approach with leverage: take the shoulder on the side the play goes
            Vec2 drive = BlockDriveDirPublic(a, t);
            Vec2 gp = t.Pos - drive * (a.Body.Radius + t.Body.Radius - 0.05f) + t.Vel * 0.2f;
            Vec2 toT = gp - a.Pos;
            float dist = toT.Length;
            a.Want = toT.Norm() * Math.Min(vmax * 0.92f, Math.Max(2.2f, dist * 7f));
            a.HasFace = true; a.FaceDir = (t.Pos - a.Pos).Norm();
        }

        private Vec2 BlockDriveDirPublic(SimAgent b, SimAgent d)
        {
            // reuse the engagement drive logic through a temporary computation
            SimAgent c = S.Carrier;
            Vec2 lane = c != null ? c.Pos : new Vec2(S.BallX0, S.LosY + 3f);
            var ca = FindCarryAssignment();
            if (ca != null && ca.Path.Count > 0) lane = S.Rel(HolePoint(ca));
            float up = b.Side == SimSide.Offense ? 1f : -1f;
            float lat = d.Pos.X - lane.X;
            float lx = Math.Abs(lat) > 0.1f ? Math.Sign(lat) * 0.55f : 0f;
            return new Vec2(lx, up * 0.85f).Norm();
        }

        // ---------------------------------------------------------------- pulls and leads

        private void PullMove(SimAgent a, BlockAssignment ba, SimAgent t)
        {

            float vmax = a.Body.EffectiveVMax;
            if (ba.Path.Count > 0 && a.PullStage < ba.Path.Count)
            {
                Vec2 wp = S.Rel(ba.Path[a.PullStage]);
                // free to engage anyone who crosses the path (handled by engagement detection)
                if ((wp - a.Pos).Length < 1.3f) a.PullStage++;
                Vec2 to = wp - a.Pos;
                // v0.9.0 (E-184): the pull has a first step: the speed ramps from 0.6 to 0.95 of top over 0.5 s (and the lineman reacts to the snap like his teammates), he no longer runs from tick 1 at 0.95
                a.Want = to.Norm() * (vmax * (0.60f + 0.35f * SimMath.Clamp01(S.Time / 0.5f)));
                a.HasFace = false;
                return;
            }
            if (t != null)
            {
                Vec2 to = t.Pos + t.Vel * 0.2f - a.Pos;
                a.Want = to.Norm() * (vmax * 0.95f);
                a.HasFace = true; a.FaceDir = to.Norm();
            }
            else
            {
                float up = a.Side == SimSide.Offense ? 1f : -1f;
                a.Want = new Vec2(0f, up) * (vmax * 0.7f);
            }
        }

        private void LeadMove(SimAgent a, BlockAssignment ba, SimAgent t)
        {
            float vmax = a.Body.EffectiveVMax;
            var ca = FindCarryAssignment();
            Vec2 hole = new Vec2(S.BallX0, S.LosY + 1.5f);
            if (ca != null && ca.Path.Count > 0) hole = S.Rel(HolePoint(ca));
            if (t == null || (a.Pos - hole).Length > 5.5f && S.Time < 0.6f)
            {
                // lead to the hole first
                Vec2 hp = new Vec2(hole.X, hole.Y);
                Vec2 to = hp - a.Pos;
                a.Want = to.Norm() * (vmax * 0.95f);
                return;
            }
            Vec2 toT = t.Pos + t.Vel * 0.25f - a.Pos;
            a.Want = toT.Norm() * (vmax * 0.95f);
            a.HasFace = true; a.FaceDir = toT.Norm();
        }

        // =====================================================================================
        // Target selection
        // =====================================================================================

        private SimAgent PickTarget(SimAgent a, BlockAssignment ba, SimAgent current)
        {
            switch (ba.Block)
            {
                case BlockKind.PassPro: return PassProTarget(a, ba, current);
                case BlockKind.Chip: return S.Time < 0.9f && a.PassProRelease < 0f ? EdgeTarget(a, current) : PassProTarget(a, ba, current);
                case BlockKind.Zone: return ZoneTarget(a, ba, current);
                case BlockKind.Pull: return PullTarget(a, ba, current);
                case BlockKind.Lead: return LeadTarget(a, current);
                case BlockKind.Stalk: return StalkTarget(a, current);
                default: return ManTarget(a, ba, current);
            }
        }


        private int TargetedByOthers(SimAgent a, SimAgent d)
        {
            int n = 0;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = S.Agents[i];
                if (o == a || o.Side != a.Side) continue;
                if (o.BlockTargetId == d.Id) n++;
            }
            return n;
        }

        private SimAgent PassProTarget(SimAgent a, BlockAssignment ba, SimAgent current)
        {
            // stay with the assigned rusher while he remains a threat; a rusher who is already beaten behind the blocker, or is locked with a teammate, is released so a free rusher (an unblocked blitzer) gets picked up
            if (current != null && (current.Pos - a.Pos).Length < 7f)
            {
                bool takenByMate = current.Engagement != null && !current.Engagement.Blockers.Contains(a);
                bool pastMe = a.Side == SimSide.Offense && current.Pos.Y < a.Pos.Y - 0.8f;
                if (!takenByMate && !pastMe) return current;
            }
            Vec2 anchor = Anchor;
            SimAgent best = null; float bestScore = float.MaxValue;
            for (int oi408 = (a.Side == SimSide.Offense ? 11 : 0), oe408 = oi408 + 11; oi408 < oe408; oi408++)
            { SimAgent d = S.Agents[oi408]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                if (d.NoEngageId == a.Id && S.Time < d.NoEngageUntil) continue;
                bool rusher = IsRusher(d) || (d.Pos.Y < S.LosY + 5f && (anchor - d.Pos).Norm().Dot(d.Vel) > 1.5f);
                if (!rusher) continue;
                float dist = (d.Pos - a.Pos).Length;
                if (dist > 8f) continue;
                int others = TargetedByOthers(a, d);
                bool engagedByOther = d.Engagement != null;
                float score = dist + (others > 0 ? 3.5f : 0f) + (engagedByOther ? 1.2f : 0f);
                // blitz pickup backs prefer second-level rushers
                if (ba.Target == BlockTarget.BlitzPickup && d.IsDefensiveLineman) score += 2.0f;
                score += 0.35f * Math.Abs(d.Pos.X - a.StartPos.X);
                if (score < bestScore) { bestScore = score; best = d; }
            }
            if (best != null) return best;
            // help a teammate who is losing
            for (int oi425 = (a.Side == SimSide.Offense ? 11 : 0), oe425 = oi425 + 11; oi425 < oe425; oi425++)
            { SimAgent d = S.Agents[oi425]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                if (d.Engagement == null || d.Engagement.Blockers.Count >= 2 || d.Engagement.Lev > 0.15f) continue;
                if ((d.Pos - a.Pos).Length > 4.2f) continue;
                return d;
            }
            return null;
        }

        private SimAgent EdgeTarget(SimAgent a, SimAgent current)
        {
            SimAgent best = null; float bd = float.MaxValue;
            for (int oi437 = (a.Side == SimSide.Offense ? 11 : 0), oe437 = oi437 + 11; oi437 < oe437; oi437++)
            { SimAgent d = S.Agents[oi437]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                if (!(d.IsDefensiveLineman || PositionGroup.IsLinebacker(d.PlayPos))) continue;
                float dist = (d.Pos - a.Pos).Length;
                if (dist < bd && dist < 6f) { bd = dist; best = d; }
            }
            return best;
        }

        private int RunDir()
        {
            if (S.OffPlay.Category == PlayCategory.Screen)
            {
                SimAgent sr = S.OffAI.ScreenReceiver;
                if (sr != null && sr.Assign is RouteAssignment)
                {
                    var wps = ((RouteAssignment)sr.Assign).Waypoints;
                    if (wps.Count > 0)
                    {
                        float ex = wps[wps.Count - 1].Point.X - (sr.StartPos.X - S.BallX0);
                        if (Math.Abs(ex) > 0.5f) return ex > 0 ? 1 : -1;
                    }
                    return sr.StartPos.X >= S.BallX0 ? 1 : -1;
                }
            }
            var ca = FindCarryAssignment();
            if (ca != null && ca.Path.Count > 0)
            {
                Vec2 end = ca.Path[ca.Path.Count - 1];
                if (Math.Abs(end.X) > 0.5f) return end.X > 0 ? 1 : -1;
                return ca.Gap == RunGap.ARight || ca.Gap == RunGap.BRight || ca.Gap == RunGap.CRight || ca.Gap == RunGap.DRight ? 1 : -1;
            }
            var qa0 = S.OffPlay.Assignments[0] as QbAssignment;
            if (qa0 != null)
            {
                if (qa0.RollDirection != 0) return qa0.RollDirection > 0 ? 1 : -1;
                if (qa0.RunPath.Count > 0 && Math.Abs(qa0.RunPath[qa0.RunPath.Count - 1].X) > 0.5f) return qa0.RunPath[qa0.RunPath.Count - 1].X > 0 ? 1 : -1;
            }
            if (S.Qb != null && S.Qb.Speed > 1f) return S.Qb.Vel.X >= 0f ? 1 : -1;   // a scramble: toward the side the passer is running
            return 1;
        }

        private SimAgent ZoneTarget(SimAgent a, BlockAssignment ba, SimAgent current)
        {
            int dx = ba.Target == BlockTarget.ZoneLeft || ba.Target == BlockTarget.SlideLeft ? -1 : 1;
            float aimX = a.StartPos.X + dx * 1.6f;
            SimAgent best = null; float bestScore = float.MinValue;
            bool climb = current != null && current.Engagement != null && !current.Engagement.Blockers.Contains(a) && current.Engagement.Lev > 0.3f;
            for (int oi485 = (a.Side == SimSide.Offense ? 11 : 0), oe485 = oi485 + 11; oi485 < oe485; oi485++)
            { SimAgent d = S.Agents[oi485]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                float rel = d.Pos.Y - S.LosY;
                if (rel > (climb ? 9f : 7.5f) || rel < -1.5f) continue;
                if (climb && rel < 2.0f) continue;
                if (d.NoEngageId == a.Id && S.Time < d.NoEngageUntil) continue;
                float lateral = d.Pos.X - aimX;
                if (Math.Abs(lateral) > 5f) continue;
                float score = -Math.Abs(lateral) - 0.30f * rel + (d.IsDefensiveLineman ? 1.1f : 0f) + (PositionGroup.IsLinebacker(d.PlayPos) ? 0.5f : 0f);
                if (d.Engagement != null)
                {
                    if (d.Engagement.Blockers.Count >= 2) score -= 6f;
                    else score -= climb ? 3f : 0.9f;
                }
                int others = TargetedByOthers(a, d);
                score -= 1.1f * others;
                if (current != null && d == current) score += 3.0f;
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        private SimAgent ManTarget(SimAgent a, BlockAssignment ba, SimAgent current)
        {
            int rd = RunDir();
            SimAgent best = null; float bestScore = float.MinValue;
            Func<SimAgent, float> score = null;
            switch (ba.Target)
            {
                case BlockTarget.EdgeDefender:
                    score = d => (d.Pos.X - a.Pos.X) * rd * 0.6f - Math.Abs(d.Pos.Y - S.LosY) * 0.5f - (d.IsDefensiveLineman || PositionGroup.IsLinebacker(d.PlayPos) ? 0f : 6f) - Math.Abs(d.Pos.X - a.Pos.X - rd * 3f) * 0.3f;
                    break;
                case BlockTarget.Mike:
                    score = d => -(d.Pos - new Vec2(S.BallX0, S.LosY + 4.5f)).Length - (d.PlayPos == Position.MLB ? 0f : 3f);
                    break;
                case BlockTarget.Linebacker:
                    score = d => -(d.Pos - a.Pos).Length - (PositionGroup.IsLinebacker(d.PlayPos) ? 0f : 5f);
                    break;
                case BlockTarget.Cornerback:
                    score = d => -(d.Pos - a.Pos).Length - (d.PlayPos == Position.CB ? 0f : 5f);
                    break;
                case BlockTarget.Playside:
                    score = d => -Math.Abs(d.Pos.X - (a.StartPos.X + rd * 1.3f)) - 0.4f * Math.Abs(d.Pos.Y - S.LosY) + (d.IsDefensiveLineman ? 0.8f : 0f);
                    break;
                case BlockTarget.Backside:
                    score = d => -Math.Abs(d.Pos.X - (a.StartPos.X - rd * 1.3f)) - 0.4f * Math.Abs(d.Pos.Y - S.LosY) + (d.IsDefensiveLineman ? 0.8f : 0f);
                    break;
                default:
                    score = d => -(d.Pos - a.Pos).Length - (d.IsDefensiveLineman ? 0f : 1.5f) - Math.Abs(d.Pos.X - a.StartPos.X) * 0.35f;
                    break;
            }
            for (int oi536 = (a.Side == SimSide.Offense ? 11 : 0), oe536 = oi536 + 11; oi536 < oe536; oi536++)
            { SimAgent d = S.Agents[oi536]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                if (d.NoEngageId == a.Id && S.Time < d.NoEngageUntil) continue;
                if (current != null && d == current) { }
                if ((d.Pos - a.Pos).Length > 14f) continue;
                float sc = score(d) - 1.0f * TargetedByOthers(a, d);
                if (d.Engagement != null && d.Engagement.Blockers.Count >= 2) sc -= 6f;
                if (current != null && d == current) sc += 2.0f;   // v0.9.0 (E-183): stickier (was +1.0)
                if (sc > bestScore) { bestScore = sc; best = d; }
            }
            return best;
        }

        private SimAgent PullTarget(SimAgent a, BlockAssignment ba, SimAgent current)
        {
            var ba2 = ba;
            Vec2 focus = a.Pos;
            if (ba.Path.Count > 0) focus = S.Rel(ba.Path[ba.Path.Count - 1]);
            int rd = RunDir();
            SimAgent best = null; float bestScore = float.MinValue;
            for (int oi556 = (a.Side == SimSide.Offense ? 11 : 0), oe556 = oi556 + 11; oi556 < oe556; oi556++)
            { SimAgent d = S.Agents[oi556]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                if (d.NoEngageId == a.Id && S.Time < d.NoEngageUntil) continue;
                float dist = (d.Pos - focus).Length;
                if (dist > 7f) continue;
                float sc = -dist - 1.5f * TargetedByOthers(a, d) - (d.Engagement != null ? 2.5f : 0f);
                if (ba.Block == BlockKind.Pull && ba.Target == BlockTarget.EdgeDefender)
                    sc += (d.Pos.X - a.Pos.X) * rd * 0.4f + (d.IsDefensiveLineman || PositionGroup.IsLinebacker(d.PlayPos) ? 1f : -2f);
                else
                    sc += (PositionGroup.IsLinebacker(d.PlayPos) ? 1.2f : (d.IsDefensiveLineman ? 0.2f : 0.6f));
                if (current != null && d == current) sc += 1f;
                if (sc > bestScore) { bestScore = sc; best = d; }
            }
            return best;
        }

        private SimAgent LeadTarget(SimAgent a, SimAgent current)
        {
            var ca = FindCarryAssignment();
            Vec2 hole = new Vec2(S.BallX0, S.LosY + 1.5f);
            if (ca != null && ca.Path.Count > 0) hole = S.Rel(HolePoint(ca));
            SimAgent best = null; float bestScore = float.MinValue;
            for (int oi578 = (a.Side == SimSide.Offense ? 11 : 0), oe578 = oi578 + 11; oi578 < oe578; oi578++)
            { SimAgent d = S.Agents[oi578]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                float dist = (d.Pos - hole).Length;
                if (dist > 8f) continue;
                float sc = -dist - 2.0f * TargetedByOthers(a, d) - (d.Engagement != null ? 3.0f : 0f);
                sc += PositionGroup.IsLinebacker(d.PlayPos) ? 1.5f : (PositionGroup.IsDefensiveBack(d.PlayPos) ? 0.6f : 0f);
                if (d.Pos.Y < S.LosY - 0.5f && d.IsDefensiveLineman) sc += 0.5f;
                if (current != null && d == current) sc += 1.0f;
                if (sc > bestScore) { bestScore = sc; best = d; }
            }
            return best;
        }

        private SimAgent StalkTarget(SimAgent a, SimAgent current)
        {
            SimAgent best = null; float bd = float.MaxValue;
            for (int oi594 = (a.Side == SimSide.Offense ? 11 : 0), oe594 = oi594 + 11; oi594 < oe594; oi594++)
            { SimAgent d = S.Agents[oi594]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                if (d.CoverTarget == a.Id) return d;
                if (!(PositionGroup.IsDefensiveBack(d.PlayPos))) continue;
                float dist = (d.Pos - a.Pos).Length + TargetedByOthers(a, d) * 5f;
                if (dist < bd && dist < 18f) { bd = dist; best = d; }
            }
            return best;
        }

        // =====================================================================================
        // Escorts: blockers on the team that now has the ball (returns, turnovers)
        // =====================================================================================

        /// <summary>Return-team blocker before anyone has the ball: meet the nearest coverage man.</summary>
        public void UpdateReturnBlocker(SimAgent a)
        {
            float vmax = a.Body.EffectiveVMax;
            a.Intent = AgentAction.Block;
            if (S.Kind == PlayKind.Kickoff && S.Specials.KickAway && a.Side == SimSide.Defense && !S.Specials.CatchMade)
            {
                // the wall: hold a row 12-18 yards in front of the returner until the coverage arrives, then take the first man to reach it
                // v0.9.0 (E-344): the wall rows are relative to the kick spot (they were the absolute lines 78 and 84, which pushed a safety-kick return line 15-20 yd upfield)
                float row = a.StartPos.Y <= S.LosY + 44f ? S.LosY + 43f : S.LosY + 49f;
                Vec2 hold = new Vec2(a.StartPos.X + (FieldGeometry.CenterX - a.StartPos.X) * 0.18f, Math.Min(row, 94f));
                // v0.9.0 (E-343): a wedge blocker forms the wedge 5 yd in front of the returner (offsets -4, -1.3, 1.3, 4)
                var wba = a.Assign as BlockAssignment;
                if (wba != null && wba.Block == BlockKind.Wedge && S.Specials.ReturnerAgent != null)
                {
                    float[] wo = { -4f, -1.3f, 1.3f, 4f };
                    SimAgent rr = S.Specials.ReturnerAgent;
                    hold = new Vec2(rr.Pos.X + wo[Math.Max(0, Math.Min(3, a.Slot - 2))], Math.Min(rr.Pos.Y - 5f, 94f));   // (the returner faces the kicker: the kick comes from the low-Y end, so the wedge is toward LOS)
                }
                SimAgent near = null; float nd = 99f;
                for (int oi619 = (a.Side == SimSide.Offense ? 11 : 0), oe619 = oi619 + 11; oi619 < oe619; oi619++)
                { SimAgent d = S.Agents[oi619]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                    if (d.Assign is KickAssignment) continue;
                    float dd2 = (d.Pos - hold).Length;
                    if (dd2 < nd) { nd = dd2; near = d; }
                }
                if (near == null || nd > 7.5f)
                {
                    Vec2 toHold = hold - a.Pos;
                    a.Want = toHold.Norm() * Math.Min(vmax * 0.8f, toHold.Length * 2.5f);
                    a.HasFace = true; a.FaceDir = new Vec2(0f, -1f);
                    a.BlockTargetId = -1;
                    return;
                }
            }
            a.ReactionClock -= PlaySimulation.Dt;
            SimAgent t = a.BlockTargetId >= 0 ? S.Agents[a.BlockTargetId] : null;
            bool valid = t != null && t.Side != a.Side && t.State != AgentState.Down && t.Body.FallenTime < 0.4f
                         && (t.Engagement == null || t.Engagement.Blockers.Count < 2 || t.Engagement.Blockers.Contains(a))
                         && !(t.NoEngageId == a.Id && S.Time < t.NoEngageUntil);
            if (!valid || a.ReactionClock <= 0f)
            {
                SimAgent best = null; float bestScore = float.MinValue;
                for (int oi642 = (a.Side == SimSide.Offense ? 11 : 0), oe642 = oi642 + 11; oi642 < oe642; oi642++)
                { SimAgent d = S.Agents[oi642]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                    if (d.IsDefensiveLineman == false && d.Assign is KickAssignment) continue;
                    float dist = (d.Pos - a.Pos).Length;
                    float sc = -dist - 3.0f * TargetedByOthers(a, d) - (d.Engagement != null ? 2f : 0f) - Math.Abs(d.StartPos.X - a.StartPos.X) * 0.25f;
                    if (valid && t == d) sc += 1.5f;
                    if (sc > bestScore) { bestScore = sc; best = d; }
                }
                a.BlockTargetId = best != null ? best.Id : -1;
                t = best;
                a.ReactionClock = 0.25f;
            }
            if (t == null) { a.Want = Vec2.Zero; return; }
            Vec2 gp = t.Pos + t.Vel * 0.3f;
            Vec2 to = gp - a.Pos;
            float dd = to.Length;
            // do not sprint past the holding spot: fall back toward the blocking position until the coverage arrives
            float speedFrac = dd > 12f ? 0.55f : 0.9f;
            a.Want = to.Norm() * vmax * speedFrac;
            a.HasFace = true; a.FaceDir = to.Norm();
        }

        public void UpdateEscort(SimAgent a)
        {
            float vmax = a.Body.EffectiveVMax;
            SimAgent c = S.Carrier;
            a.Intent = AgentAction.Block;
            if (c == null) return;
            a.ReactionClock -= PlaySimulation.Dt;
            SimAgent t = a.BlockTargetId >= 0 ? S.Agents[a.BlockTargetId] : null;
            bool valid = t != null && t.Side != a.Side && t.State != AgentState.Down && t.Body.FallenTime < 0.4f
                         && (t.Engagement == null || t.Engagement.Blockers.Count < 2 || t.Engagement.Blockers.Contains(a))
                         && !(t.NoEngageId == a.Id && S.Time < t.NoEngageUntil);
            if (!valid || a.ReactionClock <= 0f)
            {
                float up = c.Side == SimSide.Offense ? 1f : -1f;
                SimAgent best = null; float bestScore = float.MinValue;
                for (int oi679 = (a.Side == SimSide.Offense ? 11 : 0), oe679 = oi679 + 11; oi679 < oe679; oi679++)
                { SimAgent d = S.Agents[oi679]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                    float threat = -(d.Pos - c.Pos).Length * 0.8f;
                    // opponents ahead of or level with the carrier are the dangerous ones
                    float ahead = (d.Pos.Y - c.Pos.Y) * up;
                    threat += ahead > -2f ? 2.5f : -3f;
                    float myDist = (d.Pos - a.Pos).Length;
                    float sc = threat - 0.45f * myDist - 2.5f * TargetedByOthers(a, d) - (d.Engagement != null ? 2f : 0f);
                    if (valid && t == d) sc += 1.5f;
                    if (sc > bestScore) { bestScore = sc; best = d; }
                }
                a.BlockTargetId = best != null ? best.Id : -1;
                t = best;
                a.ReactionClock = 0.2f;
            }
            if (t == null)
            {
                Vec2 goal = c.Pos + new Vec2(0f, (c.Side == SimSide.Offense ? 1f : -1f) * 3f);
                a.Want = (goal - a.Pos).Norm() * (vmax * 0.85f);
                return;
            }
            // take a spot between the threat and the carrier
            Vec2 toC = (c.Pos - t.Pos).Norm();
            Vec2 gp = t.Pos + toC * (a.Body.Radius + t.Body.Radius - 0.05f) + t.Vel * 0.15f;
            Vec2 to = gp - a.Pos;
            a.Want = to.Norm() * Math.Min(vmax * 0.95f, Math.Max(2.5f, to.Length * 6f));
            a.HasFace = true; a.FaceDir = (t.Pos - a.Pos).Norm();
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class BlockingSystem
    {
        private readonly BlockAssignment PickupAssign = new BlockAssignment { Block = BlockKind.PassPro, Target = BlockTarget.BlitzPickup };

        /// <summary>A play-action fake carrier who has sold the fake turns into a pass-protection back.</summary>
        public void FakeCarrierBlock(SimAgent a)
        {
            a.PullStage = 99; // marks "free to engage"
            a.Intent = AgentAction.Block;
            a.ReactionClock -= PlaySimulation.Dt;
            SimAgent t = a.BlockTargetId >= 0 ? S.Agents[a.BlockTargetId] : null;
            bool valid = t != null && t.Side != a.Side && t.State != AgentState.Down && t.Body.FallenTime < 0.4f
                         && (t.Engagement == null || t.Engagement.Blockers.Count < 2 || t.Engagement.Blockers.Contains(a));
            if (!valid || a.ReactionClock <= 0f)
            {
                SimAgent nt = PassProTarget(a, PickupAssign, valid ? t : null);
                a.BlockTargetId = nt != null ? nt.Id : -1;
                t = nt;
                a.ReactionClock = 0.2f;
            }
            PassProMove(a, PickupAssign, t);
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class BlockingSystem
    {
        public const float ScreenReleaseTime = 0.95f;
        private bool _screenReleased;

        /// <summary>Screen: the interior line sets in pass protection, then lets the rush go and sprints out to lead the screen.</summary>
        private void ScreenWallMove(SimAgent a, BlockAssignment ba, SimAgent t)
        {
            SimAgent sr = S.OffAI.ScreenReceiver;
            if (S.Time < ScreenReleaseTime)
            {
                if (t == null) t = PassProTarget(a, PickupAssign, null);
                if (t != null) a.BlockTargetId = t.Id;
                PassProMove(a, ba, t);
                return;
            }
            a.ReactionClock -= PlaySimulation.Dt;
            float vmax = a.Body.EffectiveVMax;
            SimAgent focus = S.Carrier != null && S.Carrier.Side == a.Side ? S.Carrier : sr;
            if (focus == null) { a.Want = Vec2.Zero; return; }
            if (t == null || a.ReactionClock <= 0f || t.State == AgentState.Down || t.NoEngageId == a.Id && S.Time < t.NoEngageUntil)
            {
                SimAgent best = null; float bs = float.MinValue;
                for (int oi760 = (a.Side == SimSide.Offense ? 11 : 0), oe760 = oi760 + 11; oi760 < oe760; oi760++)
                { SimAgent d = S.Agents[oi760]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.4f || d.HasBall) continue;
                    if (d.IsDefensiveLineman) continue;
                    if (d.NoEngageId == a.Id && S.Time < d.NoEngageUntil) continue;
                    float dist = (d.Pos - focus.Pos).Length;
                    if (dist > 12f) continue;
                    float sc = -dist - 0.3f * (d.Pos - a.Pos).Length - 3f * TargetedByOthers(a, d) - (d.Engagement != null ? 2f : 0f);
                    if (sc > bs) { bs = sc; best = d; }
                }
                a.BlockTargetId = best != null ? best.Id : -1;
                t = best;
                a.ReactionClock = 0.2f;
            }
            if (t == null)
            {
                Vec2 lead = focus.Pos + new Vec2(RunDir() * 3f, 3f);
                a.Want = (lead - a.Pos).Norm() * vmax * 0.9f;
                return;
            }
            Vec2 toF = (focus.Pos - t.Pos).Norm();
            Vec2 gp = t.Pos + toF * (a.Body.Radius + t.Body.Radius - 0.05f) + t.Vel * 0.15f;
            Vec2 to = gp - a.Pos;
            a.Want = to.Norm() * Math.Min(vmax * 0.95f, Math.Max(2.5f, to.Length * 6f));
            a.HasFace = true; a.FaceDir = (t.Pos - a.Pos).Norm();
        }

        /// <summary>Called each tick: frees the screen line from its protection engagements at the release time.</summary>
        private void ReleaseScreenLine()
        {
            if (_screenReleased || S.OffPlay.Category != PlayCategory.Screen || S.Time < ScreenReleaseTime) return;
            _screenReleased = true;
            for (int i = 0; i < 11; i++)
            {
                SimAgent a = S.Agents[i];
                var ba = a.Assign as BlockAssignment;
                if (ba == null || ba.Block != BlockKind.Wall) continue;
                SimAgent held = null;
                if (a.Engagement != null) held = a.Engagement.Defender;
                Release(a);
                if (held != null)
                {
                    held.NoEngageId = a.Id; held.NoEngageUntil = S.Time + 1.6f;
                    a.NoEngageId = held.Id; a.NoEngageUntil = S.Time + 1.6f;
                }
                a.BlockTargetId = -1;
                a.ReactionClock = 0f;
            }
        }
    }
}
