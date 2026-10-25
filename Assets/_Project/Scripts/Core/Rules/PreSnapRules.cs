using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>One offensive player as the rules see him: where he stands relative to the ball (X lateral, Y downfield, so the backfield is negative) and what he is.</summary>
    public struct Aligned
    {
        public int Id;
        public Position Pos;
        public float X, Y;
        public Aligned(int id, Position pos, float x, float y) { Id = id; Pos = pos; X = x; Y = y; }
    }

    public enum AlignIssue { None, TooFewOnLine, TooManyInBackfield, IneligibleOnEndOfLine }

    /// <summary>Result of <see cref="PreSnapRules.Analyze"/>: who is on the line, who is eligible by position AND alignment, and what (if anything) makes the set illegal.</summary>
    public sealed class AlignReport
    {
        public int OnLine, Backfield;
        /// <summary>Per input index: on the line of scrimmage.</summary>
        public bool[] IsOnLine;
        /// <summary>Per input index: eligible to catch a forward pass (position AND alignment).</summary>
        public bool[] Eligible;
        /// <summary>Per input index: an eligible position on the line with a teammate on the line outside him (covered up, so ineligible).</summary>
        public bool[] Covered;
        /// <summary>Input indices of the outermost players on the line (left, right); -1 when the line is empty.</summary>
        public int LeftEnd = -1, RightEnd = -1;
        public AlignIssue Issue;
        /// <summary>The input index to blame for <see cref="Issue"/> (the man who lined up wrong), -1 when legal.</summary>
        public int Culprit = -1;
        public bool Legal { get { return Issue == AlignIssue.None; } }
        public override string ToString() { return Issue + " on line " + OnLine + " back " + Backfield; }
    }

    public enum MotionIssue { None, IllegalShift, IllegalMotion }

    /// <summary>One offensive player's movement at the moment of the snap.</summary>
    public struct MoveState
    {
        public int Id;
        /// <summary>Speed in yd/s.</summary>
        public float Speed;
        /// <summary>Velocity component toward the line of scrimmage (downfield for the offense), yd/s. Positive = coming forward.</summary>
        public float ForwardSpeed;
        /// <summary>Seconds this player has been standing still (0 while he moves).</summary>
        public float SettledFor;
        /// <summary>His last movement overlapped with another player's: he was part of a shift (two or more players changing place together).</summary>
        public bool JoinedShift;
        /// <summary>He is on the line of scrimmage (a lineman or receiver down in a stance): a movement is then a false start, not motion.</summary>
        public bool OnLine;
    }

    public sealed class MotionVerdict
    {
        public MotionIssue Issue;
        /// <summary>The input index to blame, -1 when legal.</summary>
        public int Culprit = -1;
        public override string ToString() { return Issue + " (" + Culprit + ")"; }
    }

    /// <summary>
    /// v0.12.0 (RULES, master prompt M7): pre-snap legality computed from the real alignment and movement, never from a formation's name or a dice roll. Pure and deterministic, no engine types
    /// (the headless lab and the EditMode tests call it directly). The same checks serve both rule sets ("Pro" and "College": the thresholds live in <see cref="RuleSet"/>).
    /// <list type="bullet">
    /// <item>on the line = hips within <see cref="RuleSet.OnLineDepthYards"/> of the line; at least <see cref="RuleSet.MinOnLine"/> on it, at most <see cref="RuleSet.MaxBackfield"/> off it;</item>
    /// <item>eligible = an eligible position (back, wide receiver, tight end) that is off the line, or on the line AND an end of it (a covered receiver is ineligible); linemen and the quarterback never;</item>
    /// <item>an ineligible position on an end of the line is an illegal formation (numbering);</item>
    /// <item>after a shift (two or more players moving together) everybody is still for <see cref="RuleSet.ShiftSetSeconds"/> before the snap;</item>
    /// <item>one man in motion, and he is not moving toward the line at the snap.</item>
    /// </list>
    /// </summary>
    public static class PreSnapRules
    {
        public static bool IsOnLine(float y, RuleSet rules) { return y >= -rules.OnLineDepthYards; }

        /// <summary>Alignment legality and eligibility of an offensive set (the 11 players, any order).</summary>
        public static AlignReport Analyze(IList<Aligned> offense, RuleSet rules)
        {
            if (rules == null) rules = RuleSet.Pro();
            int n = offense.Count;
            var r = new AlignReport { IsOnLine = new bool[n], Eligible = new bool[n], Covered = new bool[n] };
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                Aligned a = offense[i];
                if (IsOnLine(a.Y, rules))
                {
                    r.IsOnLine[i] = true; r.OnLine++;
                    if (a.X < minX) { minX = a.X; r.LeftEnd = i; }
                    if (a.X > maxX) { maxX = a.X; r.RightEnd = i; }
                }
                else r.Backfield++;
            }
            for (int i = 0; i < n; i++)
            {
                bool pos = PositionGroup.IsEligibleReceiver(offense[i].Pos);
                if (!pos) continue;
                if (!r.IsOnLine[i]) r.Eligible[i] = true;
                else if (i == r.LeftEnd || i == r.RightEnd) r.Eligible[i] = true;
                else r.Covered[i] = true;
            }
            // the verdict, most specific first
            if (r.OnLine < rules.MinOnLine)
            {
                r.Issue = AlignIssue.TooFewOnLine;
                r.Culprit = ClosestToLine(offense, r, rules);
            }
            else if (r.Backfield > rules.MaxBackfield)
            {
                r.Issue = AlignIssue.TooManyInBackfield;
                r.Culprit = ClosestToLine(offense, r, rules);
            }
            else
            {
                int bad = -1;
                if (r.LeftEnd >= 0 && !PositionGroup.IsEligibleReceiver(offense[r.LeftEnd].Pos)) bad = r.LeftEnd;
                else if (r.RightEnd >= 0 && !PositionGroup.IsEligibleReceiver(offense[r.RightEnd].Pos)) bad = r.RightEnd;
                if (bad >= 0) { r.Issue = AlignIssue.IneligibleOnEndOfLine; r.Culprit = bad; }
            }
            return r;
        }

        /// <summary>The off-the-line skill player nearest to the line: the one who should have been on it when the line is short.</summary>
        static int ClosestToLine(IList<Aligned> offense, AlignReport r, RuleSet rules)
        {
            int best = -1; float by = float.MinValue;
            for (int i = 0; i < offense.Count; i++)
            {
                if (r.IsOnLine[i] || offense[i].Pos == Position.QB) continue;
                if (offense[i].Y > by) { by = offense[i].Y; best = i; }
            }
            return best;
        }

        /// <summary>Movement legality at the snap: shift without the set, more than one man in motion, motion toward the line.</summary>
        public static MotionVerdict CheckMovement(IList<MoveState> players, RuleSet rules)
        {
            if (rules == null) rules = RuleSet.Pro();
            var v = new MotionVerdict();
            int movers = 0, firstMover = -1, shiftMovers = 0, unsetShift = -1;
            for (int i = 0; i < players.Count; i++)
            {
                MoveState p = players[i];
                if (p.OnLine) continue;      // a lineman who moves has jumped: that is a false start (the early-mover model), not motion
                if (p.Speed > rules.MovingSpeed)
                {
                    movers++; if (firstMover < 0) firstMover = i;
                    if (p.JoinedShift) shiftMovers++;
                }
                else if (p.JoinedShift && p.SettledFor < rules.ShiftSetSeconds && unsetShift < 0) unsetShift = i;
            }
            if (movers >= 2 || movers > rules.MaxMenInMotion)
            {
                v.Culprit = firstMover;
                v.Issue = shiftMovers >= 2 ? MotionIssue.IllegalShift : MotionIssue.IllegalMotion;
                return v;
            }
            if (movers == 1)
            {
                MoveState m = players[firstMover];
                if (rules.MotionForwardTolerance >= 0f && m.ForwardSpeed > rules.MotionForwardTolerance) { v.Issue = MotionIssue.IllegalMotion; v.Culprit = firstMover; return v; }
                if (m.JoinedShift) { v.Issue = MotionIssue.IllegalShift; v.Culprit = firstMover; return v; }   // still shifting when the ball is snapped
            }
            if (unsetShift >= 0) { v.Issue = MotionIssue.IllegalShift; v.Culprit = unsetShift; }
            return v;
        }

        /// <summary>
        /// A defender is offside when any part of him is in the neutral zone at the snap: his front edge (<paramref name="bodyRadius"/> ahead of his centre) is closer to the line of scrimmage
        /// than the length of the ball. <paramref name="depth"/> is his centre's distance past the line of scrimmage (positive = on his side of it).
        /// </summary>
        public static bool InNeutralZone(float depth, float bodyRadius, RuleSet rules)
        {
            if (rules == null) rules = RuleSet.Pro();
            return depth - bodyRadius < rules.NeutralZoneYards;
        }
    }
}
