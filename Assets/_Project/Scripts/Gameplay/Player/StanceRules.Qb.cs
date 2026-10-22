using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Gameplay
{
    /// <summary>What the play says about one player's stance (v0.9.0 QBSTANCE): the audit found the line / corner / backer stances chosen by x and depth alone.</summary>
    public struct StanceContext
    {
        /// <summary>The offensive play is a pass (OL pass sets, 2-point offensive line in shotgun).</summary>
        public bool PassPlay;
        /// <summary>The offensive play is a run (OL stance tell: weight on the hand).</summary>
        public bool RunPlay;
        /// <summary>The corner's assignment is known: <see cref="Press"/> says whether he presses (a Cover 3 corner at 2 yd must not show a jam stance).</summary>
        public bool PressKnown, Press;
        /// <summary>The defender is an edge / contain rusher (RushAssignment.Contain, a blitzing edge backer).</summary>
        public bool Edge;
        /// <summary>Goal-line / short-yardage front: every defensive lineman has both hands down.</summary>
        public bool GoalLine;
    }

    public static partial class StanceRules
    {
        public static StanceSpec For(Position pos, SimSide side, PlayKind kind, string unit, float relX, float depth, bool qbShotgun, bool returner)
        {
            return For(pos, side, kind, unit, relX, depth, qbShotgun, returner, default(StanceContext));
        }

        /// <summary>Stance of one agent with the play context (the QBSTANCE rules over the basic position / depth rules).</summary>
        public static StanceSpec For(Position pos, SimSide side, PlayKind kind, string unit, float relX, float depth, bool qbShotgun, bool returner, in StanceContext ctx)
        {
            var s = ForBasic(pos, side, kind, unit, relX, depth, qbShotgun, returner);
            s.Defense = side == SimSide.Defense;
            if (returner) return s;
            if (kind == PlayKind.Kickoff)
            {
                // the kicking line runs from a staggered start, the receiving front line waits with its hands up
                if (pos == Position.K) s.Kind = StanceKind.Kicker;
                else s.Kind = side == SimSide.Offense ? StanceKind.KickoffRunner : StanceKind.KickoffReceive;
                return s;
            }
            if (kind == PlayKind.Punt || kind == PlayKind.FieldGoal)
            {
                if (side == SimSide.Offense)
                {
                    if (pos == Position.C) { s.Kind = StanceKind.LongSnapper; return s; }
                    if (pos == Position.P) { s.Kind = StanceKind.Punter; return s; }
                }
                else if (kind == PlayKind.Punt && s.Kind == StanceKind.KickoffRunner) s.Kind = StanceKind.KickoffReceive;
            }
            if (kind == PlayKind.Scrimmage)
            {
                if (PositionGroup.IsOffensiveLine(pos) && pos != Position.C)
                {
                    if (qbShotgun && ctx.PassPlay) s.Kind = StanceKind.OlTwoPoint;
                    s.Tell = ctx.PassPlay ? -1f : (ctx.RunPlay ? 1f : 0f);
                }
                else if (pos == Position.C) s.Tell = ctx.PassPlay ? -1f : (ctx.RunPlay ? 1f : 0f);
                else if (pos == Position.TE && s.Kind == StanceKind.ThreePoint) s.Tell = ctx.PassPlay ? -1f : (ctx.RunPlay ? 1f : 0f);
                if (PositionGroup.IsDefensiveLine(pos))
                {
                    bool wide = System.Math.Abs(relX) > 6f;
                    if (ctx.GoalLine && !wide) s.Kind = StanceKind.FourPoint;
                    else if (ctx.GoalLine && wide) s.Kind = StanceKind.ThreePoint;
                    else if (s.Kind == StanceKind.TwoPointLine && !wide) s.Kind = StanceKind.ThreePoint;
                    // the edge (a 7 / 9 technique, a contain rusher) angles his body toward the quarterback
                    bool edge = pos == Position.LE || pos == Position.RE || ctx.Edge;
                    if (edge && System.Math.Abs(relX) > 2.8f)
                    {
                        float tilt = wide ? 22f : 14f;
                        float toward = relX < 0f ? 1f : -1f;            // field frame: the way the nose points to the ball line's middle
                        s.Tilt = (side == SimSide.Defense ? -1f : 1f) * toward * tilt;
                    }
                }
                else if (pos == Position.CB)
                {
                    bool press = ctx.PressKnown ? (ctx.Press && depth <= 3.2f) : depth <= 1.2f;     // E-190 / E-067: press to 1.2 yd (the old 3.2 yd chose a jam for a soft corner)
                    s.Kind = press ? StanceKind.CornerPress : StanceKind.CornerOff;
                }
                else if ((pos == Position.LOLB || pos == Position.ROLB) && ctx.Edge && depth <= 2.5f) s.Kind = StanceKind.LinebackerBlitz;
            }
            return s;
        }
    }
}
