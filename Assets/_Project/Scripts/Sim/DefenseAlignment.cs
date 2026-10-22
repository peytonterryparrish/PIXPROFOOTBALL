using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Coverage-specific pre-snap alignment. The formations only fix where the base personnel stand; a real defense lines up for the call:
    /// press corners on the line, off corners eight yards away with inside leverage, Cover 2 safeties on the half-field hashes, Cover 3 / 1 free
    /// safeties in the middle of the field, quarters safeties over the inside receivers. Applied after the men are matched.
    /// </summary>
    internal static class DefenseAlignment
    {
        public const float PressDepth = 1.15f, OffCornerDepth = 6.4f, SlotOffDepth = 4.8f, DeepHalfDepth = 10.5f, DeepMiddleDepth = 12.0f, QuarterDepth = 9.0f, DeepHalfX = 9.0f;

        public static void Apply(PlaySimulation s)
        {
            if (s.Kind != PlayKind.Scrimmage || s.DefPlay == null) return;
            bool userPress = s.DefAdjust != null && s.DefAdjust.Press != PressMode.Auto;
            for (int i = 0; i < 11; i++)
            {
                SimAgent a = s.Agents[11 + i];
                var cov = a.Assign as CoverageAssignment;
                if (cov == null) continue;
                Vec2 rel = Vec2.Zero; bool set = false;
                float ballX = s.BallX0;
                if (cov.IsMan && a.CoverTarget >= 0)
                {
                    SimAgent r = s.Agents[a.CoverTarget];
                    float rx = r.StartPos.X - ballX;
                    float inside = rx > 0.5f ? -1f : (rx < -0.5f ? 1f : 0f);
                    bool onLine = r.StartPos.Y > s.LosY - 0.8f;
                    if (a.PlayPos == Position.CB)
                    {
                        if (userPress) continue;
                        bool slot = Math.Abs(rx) < 14f && Math.Abs(rx) > 4f;
                        if (cov.Press && onLine) { rel = new Vec2(rx + inside * 0.35f, PressDepth); set = true; }
                        else if (cov.Press) { rel = new Vec2(rx + inside * 0.4f, 1.9f); set = true; }
                        else { rel = new Vec2(rx + inside * 0.7f, slot ? SlotOffDepth : OffCornerDepth); set = true; }
                    }
                    else if (PositionGroup.IsSafety(a.PlayPos))
                    {
                        // a safety on a tight end or back shades over to him but keeps his depth
                        rel = new Vec2(rx + inside * 0.4f, a.StartPos.Y - s.LosY); set = true;
                    }
                }
                else if (!cov.IsMan && cov.Zone != null)
                {
                    ZoneRegion z = cov.Zone;
                    switch (z.Kind)
                    {
                        case ZoneKind.DeepHalf:
                            rel = new Vec2(Math.Sign(z.CenterX) * Math.Min(DeepHalfX, Math.Abs(z.CenterX) * 0.68f), DeepHalfDepth); set = true; break;
                        case ZoneKind.DeepMiddle:
                            rel = new Vec2(0f, DeepMiddleDepth); set = true; break;
                        case ZoneKind.DeepThird:
                            if (Math.Abs(z.CenterX) < 1f) { rel = new Vec2(0f, DeepMiddleDepth); set = true; }
                            else if (a.PlayPos == Position.CB && !userPress) { rel = new Vec2(a.StartPos.X - ballX, cov.Press ? 2.0f : 7.5f); set = true; }
                            break;
                        case ZoneKind.Quarter:
                            if (PositionGroup.IsSafety(a.PlayPos)) { rel = new Vec2(Math.Sign(z.CenterX) * 7.6f, QuarterDepth); set = true; }
                            else if (a.PlayPos == Position.CB && !userPress) { rel = new Vec2(a.StartPos.X - ballX, cov.Press ? 1.8f : 7.0f); set = true; }
                            break;
                        case ZoneKind.Flat:
                            if (a.PlayPos == Position.CB && !userPress) { rel = new Vec2(a.StartPos.X - ballX, cov.Press ? PressDepth + 0.3f : 5.8f); set = true; }
                            break;
                    }
                }
                if (!set) continue;
                Vec2 p = new Vec2(SimMath.Clamp(ballX + rel.X, 0.8f, FieldGeometry.Width - 0.8f), s.LosY + rel.Y);
                a.Body.Pos = p; a.StartPos = p; a.Body.Vel = Vec2.Zero;
                a.ClearHistory(p);
            }
        }
    }
}
