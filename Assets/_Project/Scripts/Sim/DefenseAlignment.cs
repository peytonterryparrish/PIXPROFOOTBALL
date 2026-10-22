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

        /// <summary>Half a lineman's shoulder width: how far an "outside shade" / "inside shade" sits from the head-up spot (yards).</summary>
        public const float ShadeYards = 0.6f;

        /// <summary>
        /// OPEN (B-013): the technique alignment is implemented and tested but OFF by default. With it ON the pass protection collapses (8.7 sacks a game; the slide-protection pairing in
        /// <c>BlockerAI.AssignPassPro</c> sends the tackle to the guard's man) and, once that is patched, the shaded front stops the run (4.0 yards a carry, 7.9 yards an attempt),
        /// so the blocking schemes and the run game need a calibration pass of their own before it can ship.
        /// </summary>
        public static bool UseTechniques = true;   // v0.9.0 (E-061): ON. The slide-protection pairing is fixed (BlockerAI.AssignPassPro uses the monotone match) and the run / pass blocking was recalibrated (block bias, engagement geometry, pass set, DL get-off) with it

        /// <summary>Called by the simulation setup: aligns the line by technique when <see cref="UseTechniques"/> is on.</summary>
        public static void AlignLineIfEnabled(PlaySimulation s) { if (UseTechniques) AlignLine(s); }

        /// <summary>
        /// v0.7 alignment techniques (Docs/QA/REFERENCE_Stances.md): the defensive line lines up relative to the ACTUAL offensive linemen, not at fixed x offsets.
        /// Even techniques are head-up (0 center, 2 guard, 4 tackle, 6 tight end), odd are the outside shade (1 center, 3 guard, 5 tackle, 7 tight end), "i" the inside shade.
        /// A 4-3 under is a 1-tech nose to the strong side, a 3-tech tackle to the weak side, a 5-tech weak end and a 7-tech strong end (9-tech when there is no tight end);
        /// a 3-4 is a 0-tech nose with a 4-tech strong end and a 5-tech weak end. Strong side = the side of the tight end (right when there is none).
        /// Returns the technique number given to each lineman by slot (left to right) for tests and the film room.
        /// </summary>
        public static int[] AlignLine(PlaySimulation s)
        {
            string name = s.DefForm != null ? s.DefForm.Name : "";
            bool four = name == "4-3 Base" || name == "Nickel" || name == "Dime";
            bool three = name == "3-4 Base";
            if (!four && !three) return null;
            var dl = new System.Collections.Generic.List<SimAgent>();
            for (int i = 11; i < 22; i++) if (PositionGroup.IsDefensiveLine(s.Agents[i].PlayPos)) dl.Add(s.Agents[i]);
            if (dl.Count != (four ? 4 : 3)) return null;
            dl.Sort((a, b) => a.StartPos.X.CompareTo(b.StartPos.X));
            // the offensive front, left to right
            var ol = new System.Collections.Generic.List<SimAgent>();
            SimAgent te = null; float teDist = 99f;
            for (int i = 0; i < 11; i++)
            {
                SimAgent a = s.Agents[i];
                if (a.StartPos.Y < s.LosY - 1.2f) continue;
                if (PositionGroup.IsOffensiveLine(a.PlayPos)) ol.Add(a);
                else if (a.PlayPos == Position.TE) { float d = Math.Abs(a.StartPos.X - s.BallX0); if (d < teDist) { teDist = d; te = a; } }
            }
            if (ol.Count != 5) return null;
            ol.Sort((a, b) => a.StartPos.X.CompareTo(b.StartPos.X));
            float lt = ol[0].StartPos.X, lg = ol[1].StartPos.X, c = ol[2].StartPos.X, rg = ol[3].StartPos.X, rt = ol[4].StartPos.X;
            if (te != null && (te.StartPos.X < lt - 0.5f || te.StartPos.X > rt + 0.5f)) { } else te = null;   // only a tight end on the end of the line counts
            int strong = te != null ? (te.StartPos.X >= c ? 1 : -1) : 1;
            float sh = ShadeYards;
            float[] x; int[] tech;
            if (four)
            {
                // weak DE, weak DT, strong DT, strong DE (left to right when strong is right); mirrored when strong is left
                float strongEnd = te != null ? te.StartPos.X + strong * sh : (strong > 0 ? rt : lt) + strong * sh;   // 7-tech outside the tight end, else the 5-tech (a 9 would be wider still)
                float weakEnd = (strong > 0 ? lt : rt) - strong * sh;                                              // 5-tech outside the weak tackle
                float weakDt = (strong > 0 ? lg : rg) - strong * sh;                                              // 3-tech: outside shade of the weak guard
                float strongNose = c + strong * sh;                                                               // 1-tech: outside shade of the center toward the strong side
                if (strong > 0) { x = new[] { weakEnd, weakDt, strongNose, strongEnd }; tech = new[] { 5, 3, 1, te != null ? 7 : 5 }; }
                else { x = new[] { strongEnd, strongNose, weakDt, weakEnd }; tech = new[] { te != null ? 7 : 5, 1, 3, 5 }; }
            }
            else
            {
                float strongEnd = (strong > 0 ? rt : lt);                                      // 4-tech: head-up on the strong tackle
                float weakEnd = (strong > 0 ? lt : rt) - strong * sh;                          // 5-tech: outside shade of the weak tackle
                if (strong > 0) { x = new[] { weakEnd, c, strongEnd }; tech = new[] { 5, 0, 4 }; }
                else { x = new[] { strongEnd, c, weakEnd }; tech = new[] { 4, 0, 5 }; }
            }
            // v0.9.0: the user's line shift (pinch / spread) still applies on top of the technique alignment (it used to be overwritten, so a pinch moved nobody)
            float shift = s.DefAdjust != null ? (s.DefAdjust.Line == LineShift.Pinch ? 0.70f : (s.DefAdjust.Line == LineShift.Spread ? 1.28f : 1f)) : 1f;
            for (int i = 0; i < dl.Count; i++)
            {
                SimAgent a = dl[i];
                if (shift != 1f) x[i] = s.BallX0 + (x[i] - s.BallX0) * shift;
                Vec2 p = new Vec2(SimMath.Clamp(x[i], 0.8f, FieldGeometry.Width - 0.8f), a.StartPos.Y);
                a.Body.Pos = p; a.StartPos = p; a.Body.Vel = Vec2.Zero;
                a.ClearHistory(p);
            }
            return tech;
        }

        /// <summary>
        /// v0.9.0 (E-330): the strength of the offensive formation: +1 when the tight end (or, with none, the heavier receiver side) is to the right, -1 to the left. The linebackers and the strong safety
        /// shift to it before the coverage alignment is applied (the strong safety used to stand at x = +7 whichever side the tight end or the trips were on).
        /// </summary>
        public static int StrengthOf(PlaySimulation s)
        {
            float te = 0f, wide = 0f;
            for (int i = 1; i < 11; i++)
            {
                SimAgent a = s.Agents[i];
                if (!a.IsReceiverEligible) continue;
                float rx = a.StartPos.X - s.BallX0;
                if (a.PlayPos == Position.TE) te += Math.Sign(rx) * 1.5f;
                else if (a.PlayPos == Position.WR) wide += Math.Sign(rx);
                else if (a.PlayPos == Position.RB || a.PlayPos == Position.FB) wide += Math.Sign(rx) * 0.4f;
            }
            float st = Math.Abs(te) > 0.1f ? te : wide;
            return st < 0f ? -1 : 1;
        }

        private static void ShiftToStrength(PlaySimulation s)
        {
            int strong = StrengthOf(s);
            int delta = strong - s.StrengthApplied;   // (idempotent: Apply runs again after every pre-snap change; only the change of strength moves the backers)
            s.StrengthApplied = strong;
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = s.Agents[i];
                if (a.Assign is RushAssignment) continue;
                Position p = a.PlayPos;
                float nx = a.Pos.X;
                if (PositionGroup.IsLinebacker(p)) nx = a.Pos.X + delta * 1.2f;
                else if (p == Position.SS) nx = s.BallX0 + strong * Math.Max(4f, Math.Abs(a.Pos.X - s.BallX0));
                else continue;
                Vec2 np = new Vec2(SimMath.Clamp(nx, 0.8f, FieldGeometry.Width - 0.8f), a.Pos.Y);
                a.Body.Pos = np; a.StartPos = np; a.ClearHistory(np);
            }
        }

        public static void Apply(PlaySimulation s)
        {
            if (s.Kind != PlayKind.Scrimmage || s.DefPlay == null) return;
            ShiftToStrength(s);

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
                        // a safety on a tight end or back shades over to him; v0.9.0 (E-202) man safeties play 6.5 yd off a tight end or back and 8.5 yd off a receiver (not their 9-12 yd formation depth)
                        bool backOrTe = r.PlayPos == Position.TE || r.PlayPos == Position.RB || r.PlayPos == Position.FB;
                        rel = new Vec2(rx + inside * 0.4f, backOrTe ? 6.5f : 8.5f); set = true;
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
