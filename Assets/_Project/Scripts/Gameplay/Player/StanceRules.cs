using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Gameplay
{
    /// <summary>The pre-snap body position of a player (v0.6). Chosen from his position, alignment and the formation, never from the clip pool.</summary>
    public enum StanceKind
    {
        /// <summary>No authored stance: stand in the locomotion idle (kick holders, kickers, long snappers until their clips exist).</summary>
        Upright,
        CenterSnap,         // C: low 3-point with the hand on the ball
        ThreePoint,         // OL / DL / TE on the line / FB: one hand down
        FourPoint,          // goal-line interior linemen: both hands down
        TwoPointLine,       // OL in a pass-heavy set, TE off the ball, wide-9 edge: upright but low hips
        BackTwoPoint,       // RB / FB standing: hands on thighs
        QbUnderCenter,      // QB: hands under the center
        QbShotgun,          // QB: shotgun / pistol ready position
        ReceiverSplit,      // WR / TE split out on the line: square-ish, outside foot back
        ReceiverSlot,       // WR / TE / RB off the line: two-point, slight stagger
        LinebackerTwoPoint, // LB: low two-point, hands loose
        CornerPress,        // CB within 3 yards: square, low, hands up
        CornerOff,          // CB off the ball: backpedal-ready, hips turned a little
        SafetyDeep,         // S: two-point, deep, weight on the toes
        Gunner,             // punt coverage gunner on the edge: two-point split
        KickoffRunner,      // kickoff coverage runner: upright athletic
        Returner,           // kick / punt returner: two-point, hands out
        Kicker,             // K / P: standing at the spot
        Holder              // v0.7.0: field goal / PAT holder: kneeling, hands out for the snap (the first frames of the Yan holder take, held)
    }

    /// <summary>Everything the view needs to pose a player before the snap.</summary>
    public struct StanceSpec
    {
        public StanceKind Kind;
        /// <summary>The stance's up foot / lead side seen from the player: true = the stance is the mirror image (left hand down, right foot back for a right tackle, left foot back for a left-side receiver ...).</summary>
        public bool Mirror;
        public bool IsLeftOfBall;
    }

    /// <summary>
    /// Pure rules for stances and facing (EditMode tested). The simulation always drives toward +Y; the offense faces +Y and the defense faces -Y before the snap, kick returners face the kicker, kickers face the kick.
    /// </summary>
    public static class StanceRules
    {
        /// <summary>Tolerance (degrees) for pre-snap facing checks.</summary>
        public const float FacingToleranceDeg = 20f;

        /// <summary>Simulation-frame direction (x lateral, y downfield) a player must face before the snap.</summary>
        public static Vec2 ExpectedPreSnapFacing(SimSide side) { return side == SimSide.Offense ? new Vec2(0f, 1f) : new Vec2(0f, -1f); }

        /// <summary>Angle in degrees between two simulation directions.</summary>
        public static float AngleDeg(Vec2 a, Vec2 b)
        {
            float la = a.Length, lb = b.Length;
            if (la < 1e-5f || lb < 1e-5f) return 180f;
            float d = (a.X * b.X + a.Y * b.Y) / (la * lb);
            if (d > 1f) d = 1f; else if (d < -1f) d = -1f;
            return (float)(System.Math.Acos(d) * 180.0 / System.Math.PI);
        }

        /// <summary>Yaw (degrees about +Y, 0 = +Z, positive toward +X) of a unity-space direction on the XZ plane.</summary>
        public static float YawOf(float x, float z) { return (float)(System.Math.Atan2(x, z) * 180.0 / System.Math.PI); }

        /// <summary>Smallest signed difference of two yaw angles (degrees, -180..180).</summary>
        public static float YawDelta(float a, float b)
        {
            float d = (a - b) % 360f;
            if (d > 180f) d -= 360f; else if (d < -180f) d += 360f;
            return d;
        }

        /// <summary>Stance for one agent. <paramref name="relX"/> is lateral yards from the ball (+ = the offense's right), <paramref name="depth"/> yards from the line of scrimmage (offense: negative = backfield; defense: positive = off the ball).</summary>
        public static StanceSpec For(Position pos, SimSide side, PlayKind kind, string unit, float relX, float depth, bool qbShotgun, bool returner)
        {
            var s = new StanceSpec { Kind = StanceKind.Upright, IsLeftOfBall = relX < 0f };
            // facing the field: the right-handed 3-point stance puts the right hand down and the left foot up; the left side of the formation is the natural side, the right side mirrors it
            s.Mirror = (relX > 0f) != (side == SimSide.Defense);   // inside hand down: offense right of the ball = left hand, defense (facing the other way) left of the ball = left hand
            string u = unit ?? "";
            if (returner) { s.Kind = StanceKind.Returner; return s; }
            if (kind == PlayKind.Kickoff)
            {
                if (pos == Position.K) s.Kind = StanceKind.Kicker;
                else if (side == SimSide.Offense) s.Kind = StanceKind.KickoffRunner;
                else s.Kind = StanceKind.KickoffRunner;
                return s;
            }
            if (PositionGroup.IsOffensiveLine(pos))
            {
                s.Kind = pos == Position.C ? StanceKind.CenterSnap : StanceKind.ThreePoint;
                return s;
            }
            if (PositionGroup.IsDefensiveLine(pos))
            {
                s.Kind = (u == "4-4-3" || u == "FieldGoalBlock") && depth < 1.1f && pos == Position.DT ? StanceKind.FourPoint : StanceKind.ThreePoint;
                if (System.Math.Abs(relX) > 6f) s.Kind = StanceKind.TwoPointLine;
                return s;
            }
            if (PositionGroup.IsLinebacker(pos)) { s.Kind = StanceKind.LinebackerTwoPoint; return s; }
            if (pos == Position.CB) { s.Kind = depth <= 3.2f ? StanceKind.CornerPress : StanceKind.CornerOff; return s; }
            if (PositionGroup.IsSafety(pos)) { s.Kind = StanceKind.SafetyDeep; return s; }
            switch (pos)
            {
                case Position.QB:
                    if (kind == PlayKind.FieldGoal) s.Kind = StanceKind.Holder;      // the holder kneels (v0.7.0)
                    else s.Kind = qbShotgun ? StanceKind.QbShotgun : StanceKind.QbUnderCenter;
                    break;
                case Position.RB:
                    s.Kind = StanceKind.BackTwoPoint;
                    break;
                case Position.FB:
                    s.Kind = u == "Punt" ? StanceKind.BackTwoPoint : (depth > -4.5f && depth < -2.5f ? StanceKind.ThreePoint : StanceKind.BackTwoPoint);
                    break;
                case Position.TE:
                    s.Kind = depth > -0.6f && System.Math.Abs(relX) < 6.5f ? StanceKind.ThreePoint : (depth > -0.6f ? StanceKind.ReceiverSplit : StanceKind.ReceiverSlot);
                    break;
                case Position.WR:
                    if (u == "Punt" && System.Math.Abs(relX) > 12f) s.Kind = StanceKind.Gunner;
                    else s.Kind = depth > -0.6f ? StanceKind.ReceiverSplit : StanceKind.ReceiverSlot;
                    break;
                case Position.K: case Position.P: s.Kind = StanceKind.Kicker; break;
            }
            // receivers: the outside foot is the back foot; the stance mirrors with the side of the field
            if (s.Kind == StanceKind.ReceiverSplit || s.Kind == StanceKind.ReceiverSlot || s.Kind == StanceKind.Gunner) s.Mirror = relX > 0f;
            return s;
        }


        /// <summary>The ArtLibrary clip key that poses a stance (the animation track may add dedicated keys; every kind has a fallback so something always plays).</summary>
        public static string[] ClipKeys(StanceKind kind) { return ClipKeys(kind, PlayerRole.QB); }

        /// <summary>v0.8: distinct per-position stance clips (centre, OL, DL 3 / 4-point, wide-9, TE, RB, LB, press / off corners, safeties, receivers); each list ends with the old shared keys as fallback.</summary>
        public static string[] ClipKeys(StanceKind kind, PlayerRole role)
        {
            switch (kind)
            {
                case StanceKind.CenterSnap: return new[] { ArtKeys.StanceCenter, ArtKeys.StanceOl3ptR, ArtKeys.DlStance };
                case StanceKind.ThreePoint:
                    if (role == PlayerRole.DL) return new[] { ArtKeys.StanceDl3pt, ArtKeys.DlStance };
                    if (role == PlayerRole.TE) return new[] { ArtKeys.StanceTe, ArtKeys.StanceOl3ptR, ArtKeys.DlStance };
                    if (role == PlayerRole.RB) return new[] { ArtKeys.StanceRb, ArtKeys.StanceHalfback };
                    return new[] { ArtKeys.StanceOl3ptR, ArtKeys.DlStance };
                case StanceKind.FourPoint: return new[] { ArtKeys.StanceDl4pt, ArtKeys.StanceDl3pt, ArtKeys.DlStance };
                case StanceKind.TwoPointLine:
                    if (role == PlayerRole.DL) return new[] { ArtKeys.StanceWide9, ArtKeys.StanceLb, ArtKeys.Stance2pt };
                    return new[] { ArtKeys.StanceTe, ArtKeys.StanceLb, ArtKeys.Stance2pt, ArtKeys.IdleAlt };
                case StanceKind.BackTwoPoint: return new[] { ArtKeys.PixStanceRb, ArtKeys.StanceRb, ArtKeys.StanceHalfback };
                case StanceKind.QbUnderCenter: return new[] { ArtKeys.PixStanceQbUnder, ArtKeys.QbStanceUnder };
                case StanceKind.QbShotgun: return new[] { ArtKeys.QbStanceShotgun };
                case StanceKind.ReceiverSplit: case StanceKind.ReceiverSlot: case StanceKind.Gunner: return new[] { ArtKeys.PixStanceWr, ArtKeys.StanceWr, ArtKeys.Stance2pt };
                case StanceKind.LinebackerTwoPoint: return new[] { ArtKeys.StanceLb, ArtKeys.IdleAlt };
                case StanceKind.CornerPress: return new[] { ArtKeys.PixStanceCbPress, ArtKeys.StanceCbPress, ArtKeys.StanceSafety };
                case StanceKind.CornerOff: return new[] { ArtKeys.PixStanceCbOff, ArtKeys.StanceCbOff, ArtKeys.StanceSafety };
                case StanceKind.SafetyDeep: return new[] { ArtKeys.StanceS, ArtKeys.StanceSafety };
                case StanceKind.KickoffRunner: return new[] { ArtKeys.StanceLb, ArtKeys.Stance2pt };
                case StanceKind.Returner: return new[] { ArtKeys.StanceS, ArtKeys.Stance2pt };
                case StanceKind.Kicker: return new[] { ArtKeys.StanceKicker };
                case StanceKind.Holder: return new[] { ArtKeys.KickHolder };
                default: return new string[0];
            }
        }
    }
}
