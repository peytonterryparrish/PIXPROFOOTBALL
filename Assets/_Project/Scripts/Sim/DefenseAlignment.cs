using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Coverage-specific pre-snap alignment. The formations only fix where the base personnel stand; a real defense lines up for the call:
    /// press corners on the line, off corners eight yards away with inside leverage, Cover 2 safeties on the half-field hashes, Cover 3 / 1 free
    /// safeties in the middle of the field, quarters safeties over the inside receivers. Applied after the men are matched.
    /// v0.10.0: the line aligns by technique for every front (Under, Over, Even, Odd, Bear...), the man linebackers walk out onto their receivers, and a disguised coverage shows another shell.
    /// </summary>
    internal static class DefenseAlignment
    {
        public const float PressDepth = 1.15f, OffCornerDepth = 6.4f, SlotOffDepth = 4.8f, DeepHalfDepth = 10.5f, DeepMiddleDepth = 12.0f, QuarterDepth = 9.0f, DeepHalfX = 9.0f;

        /// <summary>The pre-snap depths of the shells (see <see cref="ShellLook"/>): two-high safeties on the hashes at 11, a one-high safety in the middle at 12.5 with the other down at 7, a zero shell at 5.5.</summary>
        public const float ShellTwoHighDepth = ShellLayout.TwoHighDepth, ShellTwoHighX = ShellLayout.TwoHighX, ShellOneHighDepth = ShellLayout.OneHighDepth, ShellBoxDepth = ShellLayout.BoxDepth, ShellZeroDepth = ShellLayout.ZeroDepth;

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

        /// <summary>The front of a formation: its <see cref="Formation.Front"/>, else by name ("4-3 Base", "Nickel", "Dime" = Under, "3-4 Base" = Odd).</summary>
        public static string FrontOf(Formation f)
        {
            if (f == null) return "";
            if (!string.IsNullOrEmpty(f.Front)) return f.Front;
            if (f.Name == "4-3 Base" || f.Name == "Nickel" || f.Name == "Dime") return "Under";
            if (f.Name == "3-4 Base") return "Odd";
            return "";
        }

        /// <summary>The gap a down lineman of this technique is responsible for, on the side of the ball he lines up on (+1 right, -1 left).</summary>
        public static RunGap GapOfTechnique(int tech, int side)
        {
            bool r = side >= 0;
            switch (tech)
            {
                case 0: case 1: case 2: return r ? RunGap.ARight : RunGap.ALeft;
                case 3: case 4: return r ? RunGap.BRight : RunGap.BLeft;
                case 5: return r ? RunGap.CRight : RunGap.CLeft;
                default: return r ? RunGap.DRight : RunGap.DLeft;   // 7 and 9: contain
            }
        }

        /// <summary>
        /// Alignment techniques (Docs/QA/REFERENCE_Stances.md): the defensive line lines up relative to the ACTUAL offensive linemen, not at fixed x offsets.
        /// Even techniques are head-up (0 center, 2 guard, 4 tackle, 6 tight end), odd are the outside shade (1 center, 3 guard, 5 tackle, 7 tight end), "i" the inside shade.
        /// Fronts (strong side = the side of the tight end, the right when there is none):
        ///   Under (4-3 / nickel / dime): 5 - 3 - 1 - 7 (weak end, weak tackle, strong nose, strong end), the nose on the strong side;
        ///   Over (4-3): 5 - 1 - 3 - 7, the three-technique on the strong side, the one-technique on the weak side;
        ///   Even (4): 5 - 3 - 3 - 7;
        ///   Bear (46): 5 - 2 - 2 - 7, the tackles head-up on the guards;
        ///   Odd (3-4): 5 - 0 - 4, a nose head-up on the center, the strong end head-up on the tackle;
        ///   Even3 (3-3-5): 5 - 0 - 7 (or 5).
        /// Returns the technique number given to each lineman by slot (left to right) for tests and the film room, and records each man's technique and gap on his agent.
        /// </summary>
        public static int[] AlignLine(PlaySimulation s)
        {
            string front = FrontOf(s.DefForm);
            if (front.Length == 0 || front == "Goal") return null;
            bool three = front == "Odd" || front == "Even3";
            var dl = new System.Collections.Generic.List<SimAgent>();
            for (int i = 11; i < 22; i++) if (PositionGroup.IsDefensiveLine(s.Agents[i].PlayPos)) dl.Add(s.Agents[i]);
            if (dl.Count != (three ? 3 : 4)) return null;
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
            float strongT = strong > 0 ? rt : lt, weakT = strong > 0 ? lt : rt, strongG = strong > 0 ? rg : lg, weakG = strong > 0 ? lg : rg;
            float strongEnd = te != null ? te.StartPos.X + strong * sh : strongT + strong * sh;   // 7-tech outside the tight end, else the 5-tech outside the tackle
            int strongEndTech = te != null ? 7 : 5;
            float weakEnd = weakT - strong * sh;                                                    // 5-tech: outside shade of the weak tackle
            float[] x; int[] tech;   // strong-right order, left to right (mirrored below when the strength is left)
            switch (front)
            {
                case "Over":
                    // weak end 5, weak tackle 1 (weak shade of the center), strong tackle 3 (outside shade of the strong guard), strong end 7 / 5
                    x = new[] { weakEnd, c - strong * sh, strongG + strong * sh, strongEnd }; tech = new[] { 5, 1, 3, strongEndTech }; break;
                case "Even":
                    x = new[] { weakEnd, weakG - strong * sh, strongG + strong * sh, strongEnd }; tech = new[] { 5, 3, 3, strongEndTech }; break;
                case "Bear":
                    x = new[] { weakEnd, weakG, strongG, strongEnd }; tech = new[] { 5, 2, 2, strongEndTech }; break;
                case "Odd":
                    x = new[] { weakEnd, c, strongT }; tech = new[] { 5, 0, 4 }; break;   // 4-tech: head-up on the strong tackle
                case "Even3":
                    x = new[] { weakEnd, c, strongEnd }; tech = new[] { 5, 0, strongEndTech }; break;
                default:   // Under
                    x = new[] { weakEnd, weakG - strong * sh, c + strong * sh, strongEnd }; tech = new[] { 5, 3, 1, strongEndTech }; break;
            }
            // x / tech were built strong-side-last; with the strength to the left the line reads left to right strong first
            if (strong < 0) { Array.Reverse(x); Array.Reverse(tech); }
            // v0.9.0: the user's line shift (pinch / spread) still applies on top of the technique alignment (it used to be overwritten, so a pinch moved nobody)
            float shift = s.DefAdjust != null ? (s.DefAdjust.Line == LineShift.Pinch ? 0.70f : (s.DefAdjust.Line == LineShift.Spread ? 1.28f : 1f)) : 1f;
            for (int i = 0; i < dl.Count; i++)
            {
                SimAgent a = dl[i];
                if (shift != 1f) x[i] = s.BallX0 + (x[i] - s.BallX0) * shift;
                Vec2 p = new Vec2(SimMath.Clamp(x[i], 0.8f, FieldGeometry.Width - 0.8f), a.StartPos.Y);
                a.Body.Pos = p; a.StartPos = p; a.Body.Vel = Vec2.Zero;
                a.ClearHistory(p);
                a.Technique = tech[i];
                int side = x[i] >= s.BallX0 + 0.05f ? 1 : (x[i] <= s.BallX0 - 0.05f ? -1 : strong);   // the nose gets the A gap on the strong side
                a.TechGap = GapOfTechnique(tech[i], side); a.HasTechGap = true;
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
                    bool backOrTe = r.PlayPos == Position.TE || r.PlayPos == Position.RB || r.PlayPos == Position.FB;
                    float formationDepth = a.StartPos.Y - s.LosY;
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
                        // a safety on a tight end or back shades over to him; v0.9.0 (E-202) man safeties play 6.5 yd off a tight end or back and 8.5 yd off a receiver (not their 9-12 yd formation depth);
                        // v0.10.0: a safety who is already walked up into the box (the 46's strong safety) stays down on his man
                        float depth = backOrTe ? 6.5f : 8.5f;
                        if (formationDepth <= 3.5f) depth = Math.Max(1.8f, formationDepth);
                        rel = new Vec2(rx + inside * 0.4f, depth); set = true;
                    }
                    else if (PositionGroup.IsLinebacker(a.PlayPos))
                    {
                        // v0.10.0: a linebacker in man walks out onto his receiver (he used to stand at his base spot 10-15 yd from a slot receiver and chase from there): head-up-inside on a back or a tight end,
                        // off and inside a slot receiver, never deeper than he stood
                        float depth = Math.Min(Math.Max(formationDepth, 3.0f), backOrTe ? 4.8f : 5.8f);
                        rel = new Vec2(rx + inside * (backOrTe ? 0.5f : 0.9f), depth); set = true;
                    }
                }
                else if (!cov.IsMan && cov.Zone != null)
                {
                    ZoneRegion z = cov.Zone;
                    if (!PositionGroup.IsDefensiveBack(a.PlayPos)) continue;   // v0.10.0: a linebacker who drops deep (the Tampa 2 Mike) lines up at his own spot; the deep alignments are for the secondary
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
                Place(s, a, new Vec2(ballX + rel.X, s.LosY + rel.Y));
            }
            ApplyShell(s);
            SeparateBackers(s);
        }

        /// <summary>v0.10.0: two linebackers or backs walked onto neighbouring receivers no longer stand on top of each other: defenders at the same depth keep at least 1.5 yd apart (the line is not touched).</summary>
        private static void SeparateBackers(PlaySimulation s)
        {
            const float need = 1.5f;
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int i = 11; i < 22; i++)
                {
                    SimAgent a = s.Agents[i];
                    if (PositionGroup.IsDefensiveLine(a.PlayPos)) continue;
                    for (int j = i + 1; j < 22; j++)
                    {
                        SimAgent b = s.Agents[j];
                        if (PositionGroup.IsDefensiveLine(b.PlayPos)) continue;
                        float dx = b.StartPos.X - a.StartPos.X;
                        if (Math.Abs(b.StartPos.Y - a.StartPos.Y) >= 1.2f || Math.Abs(dx) >= need - 0.01f) continue;
                        float push = (need - Math.Abs(dx)) * 0.5f; float dir = dx >= 0f ? 1f : -1f;
                        Place(s, a, new Vec2(a.StartPos.X - dir * push, a.StartPos.Y));
                        Place(s, b, new Vec2(b.StartPos.X + dir * push, b.StartPos.Y));
                        moved = true;
                    }
                }
                if (!moved) break;
            }
        }

        private static bool IsManDefender(SimAgent a) { var c = a.Assign as CoverageAssignment; return c != null && c.IsMan; }

        private static void Place(PlaySimulation s, SimAgent a, Vec2 want)
        {
            Vec2 p = new Vec2(SimMath.Clamp(want.X, 0.8f, FieldGeometry.Width - 0.8f), want.Y);
            a.Body.Pos = p; a.StartPos = p; a.Body.Vel = Vec2.Zero;
            a.ClearHistory(p);
        }

        /// <summary>
        /// v0.10.0: a coverage that names another shell (Cover 3 Sky from a two-high look, Cover 1 Robber from two-high, Cover 4 Match from one-high, Cover 0 Blitz Safety from two-high) puts its safeties where that
        /// shell shows them. The rotation to the true landmarks starts at the snap (the zone drop); nothing is faked after the ball moves. The safeties of a blitz keep their spot (they come from where they stand).
        /// </summary>
        public static void ApplyShell(PlaySimulation s)
        {
            ShellLook shell = s.DefPlay != null ? s.DefPlay.Shell : ShellLook.Auto;
            if (shell == ShellLook.Auto) return;
            var safeties = new System.Collections.Generic.List<SimAgent>();
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = s.Agents[i];
                if (!PositionGroup.IsSafety(a.PlayPos) || a.Assign is BlitzAssignment || a.Assign is RushAssignment || a.Assign is SpyAssignment) continue;
                safeties.Add(a);
            }
            if (safeties.Count == 0) return;
            safeties.Sort((p, q) => p.StartPos.X.CompareTo(q.StartPos.X));
            float bx = s.BallX0, los = s.LosY;
            switch (shell)
            {
                case ShellLook.TwoHigh:
                    if (safeties.Count >= 2)
                    {
                        // a safety who plays man comes down from the shell at his man's depth (a safety 11 yd deep cannot cover a slot receiver): the shell moves him to the hash, not back
                        SimAgent l = safeties[0], r = safeties[safeties.Count - 1];
                        float dl = IsManDefender(l) ? Math.Min(ShellTwoHighDepth, l.StartPos.Y - los) : ShellTwoHighDepth, dr = IsManDefender(r) ? Math.Min(ShellTwoHighDepth, r.StartPos.Y - los) : ShellTwoHighDepth;
                        Place(s, l, new Vec2(bx - ShellTwoHighX, los + dl));
                        Place(s, r, new Vec2(bx + ShellTwoHighX, los + dr));
                    }
                    else
                    {
                        // one safety left (the other one blitzes): he takes the hash away from the blitzer so the look is still two-high
                        float side = 0f;
                        for (int i = 11; i < 22; i++) { SimAgent o = s.Agents[i]; if (PositionGroup.IsSafety(o.PlayPos) && o != safeties[0]) side = o.StartPos.X >= bx ? -1f : 1f; }
                        Place(s, safeties[0], new Vec2(bx + (side == 0f ? 0f : side * ShellTwoHighX), los + ShellTwoHighDepth));
                    }
                    break;
                case ShellLook.OneHigh:
                {
                    // the deep-middle player (the free safety, else the deepest) stays in the middle, the other is down in the box on the strong side
                    SimAgent deep = null;
                    foreach (SimAgent a in safeties) { var cz = a.Assign as CoverageAssignment; if (cz != null && !cz.IsMan && cz.Zone != null && (cz.Zone.Kind == ZoneKind.DeepMiddle || cz.Zone.Kind == ZoneKind.DeepThird && Math.Abs(cz.Zone.CenterX) < 1f)) { deep = a; break; } }
                    if (deep == null) foreach (SimAgent a in safeties) if (a.PlayPos == Position.FS) { deep = a; break; }
                    if (deep == null) deep = safeties[safeties.Count - 1];
                    Place(s, deep, new Vec2(bx, los + ShellOneHighDepth));
                    int strong = s.StrengthApplied == 0 ? 1 : s.StrengthApplied;
                    foreach (SimAgent a in safeties) if (a != deep) Place(s, a, new Vec2(bx + strong * ShellLayout.BoxX, los + ShellBoxDepth));
                    break;
                }
                case ShellLook.Zero:
                    for (int k = 0; k < safeties.Count; k++) Place(s, safeties[k], new Vec2(bx + (safeties.Count == 1 ? 0f : (k == 0 ? -ShellLayout.ZeroX : ShellLayout.ZeroX)), los + ShellZeroDepth));
                    break;
            }
        }
    }
}
