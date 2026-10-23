using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.10.0 coverage rules. A defender leaves his assignment only by a rule: the ball is thrown (break on it), a receiver works into his zone (carry him, pass him off when he leaves), his man blocks (he is free: take the
    /// nearest uncovered runner, blitz in Cover 0, else rat the hook), the quarterback commits to run past the line (rally). Everything else holds the landmark, the man or the lane.
    /// </summary>
    internal sealed partial class DefenseAI
    {
        private readonly BlitzAssignment[] _freeBlitz = new BlitzAssignment[22];

        /// <summary>
        /// The quarterback has committed to run: he crosses the line, or he is scrambling and is within 0.75 yd of it. A scramble that is still behind the line is NOT a run yet: the rush chases him
        /// and the coverage stays on its receivers (it used to abandon the zones the moment the passer left the pocket).
        /// </summary>
        private bool ScrambleCommitted(SimAgent c)
        {
            return c.QbState == QbPhase.Scramble && c.Pos.Y > S.LosY - 0.75f;
        }

        /// <summary>A man defender whose man is blocking, during the 0.9 s he waits to see whether the man releases: he holds his depth and shades to his man (he does not follow him into the backfield).</summary>
        private void HoldOnBlocker(SimAgent a, SimAgent r)
        {
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            float y = Math.Max(S.LosY + 2.5f, Math.Min(a.StartPos.Y, S.LosY + 6f));
            Vec2 hold = new Vec2(a.StartPos.X + (r.Pos.X - a.StartPos.X) * 0.4f, y);
            Vec2 to = hold - a.Pos;
            a.Want = to.Norm() * Math.Min(vmax * 0.5f, to.Length * 3f);
            a.HasFace = true; a.FaceDir = (S.Qb.Pos - a.Pos).LenSq() > 0.5f ? (S.Qb.Pos - a.Pos).Norm() : new Vec2(0f, -1f);
        }

        /// <summary>
        /// The defender whose man stayed in to block. 1) He takes the nearest uncovered route runner who is working within 9 yd (the checkdown, the crosser). 2) In Cover 0 nobody is deep and the free
        /// man comes after the quarterback through his A gap. 3) Otherwise he is the rat: he sits in the hook (7 yd), mirrors the passer laterally, and goes where the ball is (the thrown-ball and scramble rules
        /// above take him from there).
        /// </summary>
        private void FreeDefenderUpdate(SimAgent a, CoverageAssignment cov)
        {
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            SimAgent qb = S.Qb;
            SimAgent pick = null; float pd = 9f;
            for (int oi = 1; oi < 11; oi++)
            {
                SimAgent o = S.Agents[oi];
                if (!o.IsReceiverEligible || !(o.Assign is RouteAssignment) || o.RouteDone || o.Body.FallenTime > 0f) continue;
                bool covered = false;
                for (int di = 11; di < 22; di++) if (di != a.Id && S.Agents[di].CoverTarget == o.Id) { covered = true; break; }
                if (covered) continue;
                float dd = (o.Pos - a.Pos).Length;
                if (dd < pd && o.Pos.Y > S.LosY - 2f) { pd = dd; pick = o; }
            }
            if (pick != null)
            {
                a.CoverTarget = pick.Id; a.IsFree = false; a.RuleMatches++;
                ManUpdate(a, cov);
                return;
            }
            bool cover0 = S.DefPlay != null && S.DefPlay.Scheme == CoverageScheme.Cover0;
            if (cover0 && S.Time < 3.0f)
            {
                if (_freeBlitz[a.Id] == null) _freeBlitz[a.Id] = new BlitzAssignment { Slot = a.Slot, Gap = a.Pos.X >= S.BallX0 ? RunGap.ARight : RunGap.ALeft, Edge = false, Delay = 0f };
                BlitzUpdate(a, _freeBlitz[a.Id]);
                return;
            }
            // the rat
            float lat = SimMath.Clamp(qb.Pos.X - S.BallX0, -5f, 5f);
            Vec2 hold = new Vec2(S.BallX0 + lat * 0.8f + (a.StartPos.X - S.BallX0) * 0.2f, S.LosY + 7f);
            a.Zone = ZoneRegion.HookMiddle(); a.Landmark = hold;
            Vec2 to = hold - a.Pos;
            float dist = to.Length;
            a.Want = dist < 0.25f ? Vec2.Zero : to.Norm() * Math.Min(vmax * 0.75f, Math.Max(1.0f, dist * 3.5f));
            a.HasFace = true; a.FaceDir = (qb.Pos - a.Pos).LenSq() > 0.5f ? (qb.Pos - a.Pos).Norm() : new Vec2(0f, -1f);
        }

        /// <summary>
        /// Lateral shade of a zone toward the ball: the underneath zones flow with the quarterback (a boot or a roll to one side takes the hooks and curls a yard or two over, the way a real zone drops),
        /// the deep zones shade less. Never more than 2.2 yd (an underneath zone) or 1.5 yd (a deep zone) from the landmark.
        /// </summary>
        private float ZoneShade(ZoneRegion z, Vec2 qbPos)
        {
            float k = IsDeep(z) ? 0.14f : 0.30f, cap = IsDeep(z) ? 1.5f : 2.2f;
            return SimMath.Clamp((qbPos.X - S.BallX0) * k, -cap, cap);
        }

        /// <summary>
        /// Quarters pattern-match (Cover 4, Cover 6's quarters side): the corner has the outermost receiver on his side and the safety the second one. If his receiver stems vertical (past 7 yd and fast)
        /// he is covered man-to-man from there; if he breaks short the corner passes him off and the safety, with nobody vertical of his own, helps over the outside receiver who is.
        /// Returns the receiver this defender should key, or null when his man did not go vertical (he sits and reads: the zone rules apply).
        /// </summary>
        private SimAgent QuartersMatch(SimAgent a, ZoneRegion z)
        {
            var mcov = a.Assign as CoverageAssignment;
            bool matchThird = mcov != null && mcov.Match && (z.Kind == ZoneKind.DeepThird || z.Kind == ZoneKind.DeepHalf) && Math.Abs(z.CenterX) > 12f;   // Cover 3 Match: the corners match like quarters
            if (z.Kind != ZoneKind.Quarter && !matchThird) return null;
            int side = z.CenterX < 0f ? -1 : 1;
            bool cornerSide = Math.Abs(z.CenterX) > 12f;
            SimAgent r1 = null, r2 = null;
            // outside-in ranking of the eligible receivers who lined up on his side at the snap
            var rx = new List<SimAgent>();
            for (int i = 1; i < 11; i++)
            {
                SimAgent r = S.Agents[i];
                if (!r.IsReceiverEligible || r.PlayPos == Position.RB || r.PlayPos == Position.FB) continue;
                if (Math.Sign(r.StartPos.X - S.BallX0) != side) continue;
                rx.Add(r);
            }
            rx.Sort((p, q) => Math.Abs(q.StartPos.X - S.BallX0).CompareTo(Math.Abs(p.StartPos.X - S.BallX0)));
            if (rx.Count > 0) r1 = rx[0];
            if (rx.Count > 1) r2 = rx[1];
            SimAgent mine = cornerSide ? r1 : (r2 ?? null);
            float vmax = 8f;
            if (mine != null && mine.Pos.Y - S.LosY > 7f && mine.Vel.Y > 0.5f * mine.Body.VMax) { if (a.ZoneTargetId != mine.Id) a.RuleMatches++; return mine; }
            // the safety with no vertical number two of his own helps over the vertical number one
            if (!cornerSide && r1 != null && r1.Pos.Y - S.LosY > 7f && r1.Vel.Y > 0.5f * r1.Body.VMax && Math.Abs(r1.Pos.X - a.Pos.X) < 12f) return r1;
            return null;
        }
    }
}

namespace PixFootball.Sim
{
    internal sealed partial class DefenseAI
    {
        // ---------------------------------------------------------------- play recognition: take away the primary read
        private SimAgent _bracketTarget;
        private SimAgent _bracketDefender;
        private bool _bracketChosen;

        /// <summary>Share of pass plays on which the defense has the concept diagnosed and takes the quarterback's first read away with a second defender (a bracket). Calibrated with the sack rate.</summary>
        internal static float BracketShare = 0.30f;

        /// <summary>
        /// The defense reads the call (it already reads run versus pass): on a share of pass plays the defender whose zone the quarterback's first read is running into, who is not that receiver's man,
        /// bracket the receiver (inside and over the top) until the ball is in the air. That is a coverage that took the primary read away: the quarterback has to go to his next read, hold, or take the sack.
        /// </summary>
        private void ChooseBracket()
        {
            _bracketChosen = true; _bracketTarget = null; _bracketDefender = null;
            if (S.Kind != PlayKind.Scrimmage || !S.OffPlay.IsPass || S.OffPlay.Category == PlayCategory.Screen || S.OffPlay.Category == PlayCategory.Kneel) return;
            var qa = S.OffPlay.Assignments[0] as QbAssignment;
            if (qa == null || qa.ReadProgression.Count == 0) return;
            if (S.DefPlay != null && S.DefPlay.Scheme == CoverageScheme.Cover0) return;   // nobody is free
            float roll = Hash01(S.OffPlay.Id.GetHashCode(), 3571 + (int)(S.State.ClockSeconds * 10f) + S.State.BallSpot * 31 + S.State.Down);
            if (roll >= BracketShare) return;
            SimAgent prim = S.Agents[qa.ReadProgression[0]];
            var ra = prim.Assign as RouteAssignment;
            if (ra == null || ra.Waypoints.Count == 0) return;
            // where the receiver is going to be at his break: the first sharp waypoint (or the end of a short route)
            Vec2 brk = S.RouteRel(ra.Waypoints[0].Point);
            for (int i = 0; i < ra.Waypoints.Count; i++) { if (ra.Waypoints[i].Cut) { brk = S.RouteRel(ra.Waypoints[Math.Max(0, i - 1)].Point); break; } brk = S.RouteRel(ra.Waypoints[i].Point); }
            SimAgent best = null; float bd = 99f;
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                var cov = d.Assign as CoverageAssignment;
                if (cov == null || d.CoverTarget == prim.Id) continue;
                if (cov.IsMan && d.CoverTarget >= 0) continue;                  // a man defender stays on his man
                if (cov.Zone == null && !d.IsFree) continue;
                float dd = (d.Landmark - brk).Length;
                if (!PositionGroup.IsDefensiveBack(d.PlayPos) && !PositionGroup.IsLinebacker(d.PlayPos)) continue;
                if (dd < bd) { bd = dd; best = d; }
            }
            if (best != null && bd < 11f) { _bracketTarget = prim; _bracketDefender = best; }
        }

        /// <summary>The first-read receiver this zone defender brackets right now, or null.</summary>
        private SimAgent BracketMatch(SimAgent a)
        {
            if (!_bracketChosen) ChooseBracket();
            if (_bracketDefender != a || _bracketTarget == null) return null;
            if (S.Time < 0.6f || _bracketTarget.State == AgentState.Down || _bracketTarget.RouteDone && S.Time > 3.5f) return null;
            if ((_bracketTarget.Pos - a.Pos).Length > 13f) return null;
            if (a.ZoneTargetId != _bracketTarget.Id) a.RuleMatches++;
            return _bracketTarget;
        }
    }
}
