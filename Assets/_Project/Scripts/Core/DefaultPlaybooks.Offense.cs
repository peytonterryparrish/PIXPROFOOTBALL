using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>
    /// Route templates as (outward, downfield) pairs. Downfield is absolute from the ball; outward is relative to the receiver's start x (negative = toward the middle of the field).
    /// v0.10.0: every break depth and angle follows the standard route tree (hitch 5-6, slant 3-step stem then a 45 degree break, curl 12 coming back to 10, out 10 at 90 degrees, dig 12 at 90 degrees,
    /// post 12 then toward the goalpost, corner 12 then toward the pylon, comeback 15-16 back to the sideline) and the depth can be overridden per concept ("S1=Curl@10").
    /// </summary>
    public static class RouteLibrary
    {
        /// <summary>The break depth a route runs when the concept does not name one (0 for routes that do not have a single break depth).</summary>
        public static float DefaultDepth(RouteKind k)
        {
            switch (k)
            {
                case RouteKind.Hitch: return 6f;
                case RouteKind.Curl: return 12f;
                case RouteKind.Comeback: return 15f;
                case RouteKind.Out: return 10f;
                case RouteKind.Dig: return 12f;
                case RouteKind.In: return 6f;
                case RouteKind.Post: return 12f;
                case RouteKind.Corner: return 12f;
                case RouteKind.Drag: return 5f;
                case RouteKind.Shallow: return 4f;
                case RouteKind.Cross: return 13f;
                case RouteKind.Stick: return 5.5f;
                case RouteKind.OutAndUp: return 8f;
                default: return 0f;
            }
        }

        /// <summary>The route's break angle in degrees away from the stem (0 = straight on, 90 = a square break, 180 = a turn back to the passer), measured on the first break of the template.</summary>
        public static float BreakAngle(RouteKind k)
        {
            float[] t = Shape(k, DefaultDepth(k));
            if (t.Length < 6) return t.Length >= 4 && (k == RouteKind.Hitch || k == RouteKind.Curl) ? 160f : (t.Length >= 4 ? AngleOf(0f, 1f, t[2] - t[0], t[3] - t[1]) : 0f);
            return AngleOf(t[0] - 0f, t[1] - 0f, t[2] - t[0], t[3] - t[1]);
        }

        private static float AngleOf(float ax, float ay, float bx, float by)
        {
            float la = (float)Math.Sqrt(ax * ax + ay * ay), lb = (float)Math.Sqrt(bx * bx + by * by);
            if (la < 1e-4f || lb < 1e-4f) return 0f;
            float c = (ax * bx + ay * by) / (la * lb);
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, c))) * 180.0 / Math.PI);
        }

        private static float[] Shape(RouteKind k, float d)
        {
            switch (k)
            {
                case RouteKind.Go: return new[] { 0f, 5f, 0f, 14f, 0f, 40f };
                case RouteKind.Fade: return new[] { 0.8f, 4f, 2.6f, 14f, 4.5f, 38f };
                case RouteKind.Hitch: return new[] { 0f, d, 0f, d - 1.2f };
                case RouteKind.Curl: return new[] { 0f, d, -1.2f, d - 2.2f };
                case RouteKind.Comeback: return new[] { 0f, d, 2.8f, d - 3.5f };
                case RouteKind.Slant: return new[] { 0f, 2.8f, -2f, 4.8f, -8.5f, 11f };
                case RouteKind.Out: return new[] { 0f, d, 8f, d + 0.3f };
                case RouteKind.Dig: return new[] { 0f, d, -13f, d + 0.3f };
                case RouteKind.Post: return new[] { 0f, d, -15f, d + 17f };
                case RouteKind.Corner: return new[] { 0f, d, 14f, d + 15f };
                case RouteKind.Seam: return new[] { 0f, 7f, 0f, 22f, 0f, 42f };
                case RouteKind.Drag: return new[] { -2.2f, d, -17f, d + 0.4f };
                case RouteKind.Cross: return new[] { -0.5f, 5f, -8f, d, -23f, d + 1.5f };
                case RouteKind.Flat: return new[] { 3f, 0.4f, 9f, 1.2f };
                case RouteKind.Swing: return new[] { 4f, -1f, 9f, 2f, 11f, 6f };
                case RouteKind.Wheel: return new[] { 3f, 0f, 9f, 2f, 11f, 12f, 10f, 32f };
                case RouteKind.Stick: return new[] { 0f, d, 1.2f, d + 0.2f };
                case RouteKind.Texas: return new[] { 3f, 1f, -3f, 5f, -5f, 11f };
                case RouteKind.Bubble: return new[] { 3f, -1f, 6f, 0f };
                case RouteKind.ScreenRelease: return new[] { -2f, -3f, -8f, -1f };
                case RouteKind.Shallow: return new[] { -3f, d, -22f, d + 0.4f };
                case RouteKind.OutAndUp: return new[] { 0f, d, 5f, d + 0.5f, 7f, 32f };
                case RouteKind.Corner2: return new[] { 0f, 12f, 8f, 22f, 14f, 36f };
                case RouteKind.Delay: return new[] { 0f, -1f, 4f, 2f, 8f, 6f };
                case RouteKind.In: return new[] { 0f, d, -8f, d + 0.5f };
                default: return new[] { 0f, 10f };
            }
        }

        public const float RouteSpeed = 5.6f;   // v0.9.0 (E-110): the QB's timing estimate of a route IS the receiver's average pace including the stand-still start (a 9 yd out takes about 1.9 s for a 75 WR, not the 1.5 s that 7 yd/s gave); the sim runs the stems at 88-98 percent of his top speed

        public static List<RouteWaypoint> Build(RouteKind kind, Vec2 start, float sign) { return Build(kind, start, sign, 0f); }

        /// <summary>The route from a start spot. <paramref name="depth"/> overrides the break depth of the routes that have one (0 = the default).</summary>
        public static List<RouteWaypoint> Build(RouteKind kind, Vec2 start, float sign, float depth)
        {
            float[] t = Shape(kind, depth > 0.5f ? depth : DefaultDepth(kind));
            var list = new List<RouteWaypoint>();
            Vec2 prev = start;
            Vec2 prevDir = Vec2.Zero;
            float time = 0.25f;
            for (int i = 0; i + 1 < t.Length; i += 2)
            {
                var p = new Vec2(Math.Max(-Playbook.RouteHalfWidth, Math.Min(Playbook.RouteHalfWidth, start.X + sign * t[i])), t[i + 1]);   // never out of bounds
                Vec2 d = p - prev;
                float len = d.Length;
                Vec2 dir = len > 0.001f ? d * (1f / len) : Vec2.Zero;
                bool cut = i > 0 && (prevDir.X * dir.X + prevDir.Y * dir.Y) < 0.8f;
                time += len / RouteSpeed + (cut ? 0.15f : 0f);
                list.Add(new RouteWaypoint(p, time, cut));
                prev = p;
                prevDir = dir;
            }
            return list;
        }
    }

    /// <summary>Which side of the formation a concept's "strong" roles (S1..S4) belong to: the side with more receivers, the side with more wide receivers, or the tight end's side.</summary>
    internal enum SideMode { Strong, Wr, Te }

    internal sealed class OffBuilder
    {
        public const int QB = 0, HB = 1, LT = 2, LG = 3, C = 4, RG = 5, RT = 6;
        public readonly Formation F;
        public readonly Assignment[] A = new Assignment[Formation.SlotCount];
        public readonly bool Shotgun;
        public readonly bool IsPass;
        public QbAssignment Qb;
        private readonly List<KeyValuePair<int, int>> _reads = new List<KeyValuePair<int, int>>();
        public int RouteCount;
        /// <summary>The side (+1 right, -1 left) with more receivers; with equal numbers the tight-end side, else the right.</summary>
        public readonly int StrongSign;
        private readonly List<int> _strong = new List<int>(), _weak = new List<int>();

        public OffBuilder(Formation f, bool isPass) : this(f, isPass, SideMode.Strong) { }

        public OffBuilder(Formation f, bool isPass, SideMode mode)
        {
            F = f; IsPass = isPass;
            Shotgun = f.Slots[0].Y <= -3f;
            // the receivers of the formation (wide receivers and tight ends who are not in the backfield), outside in on each side
            int nr = 0, nl = 0, wr = 0, wl = 0; int teSide = 0;
            for (int i = 1; i < Formation.SlotCount; i++)
            {
                SlotSpec s = f.Slots[i];
                if (!IsReceiverSlot(s)) continue;
                if (s.X > 0.5f) { nr++; if (s.Position == Position.WR) wr++; } else if (s.X < -0.5f) { nl++; if (s.Position == Position.WR) wl++; }
                if (s.Position == Position.TE && Math.Abs(s.X) < 6f && teSide == 0) teSide = s.X >= 0f ? 1 : -1;
            }
            StrongSign = nr > nl ? 1 : (nl > nr ? -1 : (teSide != 0 ? teSide : 1));
            if (mode == SideMode.Wr && wr != wl) StrongSign = wr > wl ? 1 : -1;            // the side with more wide receivers (a TE alone does not make a side)
            else if (mode == SideMode.Te && teSide != 0) StrongSign = teSide;               // the tight end's side
            for (int i = 1; i < Formation.SlotCount; i++)
            {
                SlotSpec s = f.Slots[i];
                if (!IsReceiverSlot(s)) continue;
                int sign = s.X >= 0f ? 1 : -1;
                (sign == StrongSign ? _strong : _weak).Add(i);
            }
            Comparison<int> outsideIn = (p, q) => Math.Abs(f.Slots[q].X).CompareTo(Math.Abs(f.Slots[p].X));
            _strong.Sort(outsideIn); _weak.Sort(outsideIn);
        }

        private static bool IsReceiverSlot(SlotSpec s)
        {
            if (s.Position != Position.WR && s.Position != Position.TE) return false;
            return s.Y > -2.2f || Math.Abs(s.X) > 5.5f;
        }

        public static Vec2 P(float x, float y) { return new Vec2(x, y); }
        public int Idx(string label) { return Role(label); }
        public bool Has(string label) { return Role(label) >= 0; }
        public float Sign(int slot) { return F.Slots[slot].X < 0f ? -1f : 1f; }

        /// <summary>
        /// Resolves a role token to a formation slot: S1..S4 (strong side, outside in), W1..W3 (weak side), TE (the inline tight end, strong side first), HB, F, or a formation label (X, Z, H, W, Y, Y2).
        /// Returns -1 when the formation has no such player.
        /// </summary>
        public int Role(string tok)
        {
            if (tok.Length == 2 && (tok[0] == 'S' || tok[0] == 'W') && char.IsDigit(tok[1]))
            {
                int rank = tok[1] - '1';
                List<int> l = tok[0] == 'S' ? _strong : _weak;
                return rank >= 0 && rank < l.Count ? l[rank] : -1;
            }
            if (tok == "WI") return _weak.Count > 0 ? _weak[_weak.Count - 1] : -1;   // the innermost weak-side receiver (a crosser)
            if (tok == "SI")                                                              // the innermost strong-side wide receiver (the slot), else the innermost strong-side receiver
            {
                for (int k = _strong.Count - 1; k >= 0; k--) if (F.Slots[_strong[k]].Position == Position.WR) return _strong[k];
                return _strong.Count > 0 ? _strong[_strong.Count - 1] : -1;
            }
            if (tok == "TE" || tok == "TE+")
            {
                int best = -1; float bd = 99f;
                for (int i = 1; i < Formation.SlotCount; i++)
                {
                    SlotSpec s = F.Slots[i];
                    if (s.Position != Position.TE || Math.Abs(s.X) > 6f) continue;
                    float d = (Math.Sign(s.X) == StrongSign ? 0f : 10f) + Math.Abs(s.X);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0 && tok == "TE+") return Role("SI");   // no inline tight end (10 personnel): the slot runs the tight end's route
                return best;
            }
            return F.IndexOfLabel(tok);
        }

        public void SetQb(QbAction action, int steps, int roll = 0, int handoffSlot = -1, params Vec2[] runPath)
        {
            Qb = new QbAssignment { Slot = 0, Action = action, Shotgun = Shotgun, DropbackSteps = steps, RollDirection = roll, HandoffSlot = handoffSlot };
            Qb.RunPath.AddRange(runPath);
            A[0] = Qb;
        }

        public bool Route(string label, RouteKind kind, int read = 0, float depth = 0f)
        {
            int i = Role(label);
            if (i < 0 || A[i] != null) return false;
            var ra = new RouteAssignment { Slot = i, Route = kind, ReadOrder = read };
            ra.Waypoints.AddRange(RouteLibrary.Build(kind, F.Slots[i].Offset, Sign(i), depth));
            A[i] = ra;
            RouteCount++;
            if (read > 0) _reads.Add(new KeyValuePair<int, int>(read, i));
            return true;
        }

        public bool Block(string label, BlockKind kind, BlockTarget target, RunGap gap = RunGap.None)
        {
            int i = Role(label);
            return i >= 0 && BlockAt(i, kind, target, gap);
        }

        public bool BlockAt(int i, BlockKind kind, BlockTarget target, RunGap gap = RunGap.None, params Vec2[] path)
        {
            if (A[i] != null) return false;
            var b = new BlockAssignment { Slot = i, Block = kind, Target = target, Gap = gap };
            b.Path.AddRange(path);
            A[i] = b;
            return true;
        }

        public bool Carry(string label, CarryStyle style, RunGap gap, float delay, params Vec2[] path)
        {
            int i = Role(label);
            if (i < 0 || A[i] != null) return false;
            var c = new CarryAssignment { Slot = i, Style = style, Gap = gap, Delay = delay };
            c.Path.AddRange(path);
            A[i] = c;
            return true;
        }

        public bool FakeCarry(string label, CarryStyle style, RunGap gap, params Vec2[] path)
        {
            bool ok = Carry(label, style, gap, 0f, path);
            if (ok) ((CarryAssignment)A[Role(label)]).IsFake = true;
            return ok;
        }

        // ----- offensive line schemes -----
        public void OlZone(int dir)
        {
            for (int i = LT; i <= RT; i++)
                BlockAt(i, BlockKind.Zone, dir > 0 ? BlockTarget.ZoneRight : BlockTarget.ZoneLeft);
        }

        public void OlPower(int dir)
        {
            // backside guard pulls to lead; the center and the playside line block down; the TE or fullback kicks out the end
            int puller = dir > 0 ? LG : RG;
            int playT = dir > 0 ? RT : LT;
            for (int i = LT; i <= RT; i++)
            {
                if (i == puller)
                {
                    float sx = F.Slots[i].X;
                    BlockAt(i, BlockKind.Pull, BlockTarget.Playside, RunGap.None, P(sx * 0.35f, -0.7f), P(dir * 2.9f, -0.7f), P(dir * 5.2f, 1.3f));
                }
                else if (i == C) BlockAt(i, BlockKind.Man, BlockTarget.Backside);
                else if (F.Slots[i].X * dir >= -0.1f) BlockAt(i, BlockKind.Man, BlockTarget.Playside);
                else BlockAt(i, BlockKind.Seal, BlockTarget.Backside);
            }
        }

        public void OlCounter(int dir)
        {
            // backside guard pulls and kicks out the edge; the backside tackle stays home and seals the end
            int g = dir > 0 ? LG : RG;
            int t = dir > 0 ? LT : RT;
            for (int i = LT; i <= RT; i++)
            {
                float sx = F.Slots[i].X;
                if (i == g) BlockAt(i, BlockKind.Pull, BlockTarget.EdgeDefender, RunGap.None, P(sx * 0.2f, -0.7f), P(dir * 2.5f, -0.9f), P(dir * 4.3f, 0.8f));
                else if (i == t) BlockAt(i, BlockKind.Seal, BlockTarget.Backside);
                else if (i == C) BlockAt(i, BlockKind.Man, BlockTarget.Backside);
                else BlockAt(i, BlockKind.Man, BlockTarget.Playside);
            }
        }

        public void OlTrap(int dir)
        {
            // the backside guard pulls to trap the penetrating tackle that the playside guard releases up to the linebacker
            int puller = dir > 0 ? LG : RG;
            int playG = dir > 0 ? RG : LG;
            for (int i = LT; i <= RT; i++)
            {
                float sx = F.Slots[i].X;
                if (i == puller) BlockAt(i, BlockKind.Pull, BlockTarget.Gap, dir > 0 ? RunGap.BRight : RunGap.BLeft, P(sx * 0.5f, -1f), P(dir * 1.6f, 0.3f));
                else if (i == playG) BlockAt(i, BlockKind.Man, BlockTarget.Mike);
                else if (i == C) BlockAt(i, BlockKind.Man, BlockTarget.Backside);
                else if (F.Slots[i].X * dir > 0f) BlockAt(i, BlockKind.Man, BlockTarget.Playside);
                else BlockAt(i, BlockKind.Seal, BlockTarget.Backside);
            }
        }

        public void OlPassPro(int slide)
        {
            for (int i = LT; i <= RT; i++)
                BlockAt(i, BlockKind.PassPro, slide < 0 ? BlockTarget.SlideLeft : (slide > 0 ? BlockTarget.SlideRight : BlockTarget.NearestDefender));
        }

        public void OlScreen()
        {
            BlockAt(LT, BlockKind.PassPro, BlockTarget.SlideRight);
            BlockAt(RT, BlockKind.PassPro, BlockTarget.SlideLeft);
            BlockAt(LG, BlockKind.Wall, BlockTarget.Playside);
            BlockAt(C, BlockKind.Wall, BlockTarget.Playside);
            BlockAt(RG, BlockKind.Wall, BlockTarget.Playside);
        }

        /// <summary>
        /// Applies a spec like "S1=Curl@10*1 S2=Flat*2 W1=Go HB=@PassPro" of role (or label) assigned routes and blocks. A role is S1..S4 / W1..W3 / TE / HB / F or a formation label;
        /// "@depth" overrides the break depth of the route, "*n" is the quarterback's read order.
        /// </summary>
        public void Spec(string spec)
        {
            foreach (string tok in spec.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = tok.IndexOf('=');
                if (eq <= 0) { DefaultPlaybooks.SkippedSpecTokens.Add(F.Name + ": malformed " + tok); continue; }
                string label = tok.Substring(0, eq);
                string what = tok.Substring(eq + 1);
                int read = 0;
                int star = what.IndexOf('*');
                if (star >= 0) { int.TryParse(what.Substring(star + 1), out read); what = what.Substring(0, star); }
                float depth = 0f;
                int at = what.StartsWith("@") ? -1 : what.IndexOf('@');
                if (at > 0) { float.TryParse(what.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out depth); what = what.Substring(0, at); }
                bool ok = true;
                if (Role(label) < 0) { DefaultPlaybooks.SkippedSpecTokens.Add(F.Name + ": no player for " + tok); continue; }
                if (what == "@PassPro") ok = Block(label, BlockKind.PassPro, BlockTarget.BlitzPickup);
                else if (what == "@Chip") ok = Block(label, BlockKind.Chip, BlockTarget.EdgeDefender);
                else if (what == "@Block") ok = Block(label, BlockKind.Stalk, BlockTarget.Cornerback);
                else
                {
                    RouteKind rk;
                    if (!Enum.TryParse(what, out rk)) { DefaultPlaybooks.SkippedSpecTokens.Add(F.Name + ": unknown route " + tok); continue; }
                    ok = Route(label, rk, read, depth);
                }
                if (!ok) DefaultPlaybooks.SkippedSpecTokens.Add(F.Name + ": skipped " + tok);   // the slot already has an assignment
            }
        }

        public Play Finish(string name, PlayCategory cat, params string[] tags)
        {
            if (Qb == null) throw new InvalidOperationException("QB assignment missing for " + name);
            for (int i = 1; i < Formation.SlotCount; i++)
            {
                if (A[i] != null) continue;
                Position pos = F.Slots[i].Position;
                if (PositionGroup.IsOffensiveLine(pos))
                {
                    if (IsPass) BlockAt(i, BlockKind.PassPro, BlockTarget.SlideRight);
                    else BlockAt(i, BlockKind.Zone, BlockTarget.ZoneRight);
                }
                else if (IsPass)
                {
                    if (pos == Position.WR)
                    {
                        // a receiver the concept does not use runs a clearing vertical when he is wide and a hitch when he is in the slot
                        RouteKind rk = Math.Abs(F.Slots[i].X) >= 14f ? RouteKind.Go : RouteKind.Hitch;
                        DefaultPlaybooks.DefaultedReceivers.Add(F.Name + " / " + name + ": " + (F.Slots[i].Label.Length > 0 ? F.Slots[i].Label : F.Slots[i].Position.ToString()) + " -> " + rk);
                        A[i] = new RouteAssignment { Slot = i, Route = rk };
                        ((RouteAssignment)A[i]).Waypoints.AddRange(RouteLibrary.Build(rk, F.Slots[i].Offset, Sign(i)));
                    }
                    else BlockAt(i, pos == Position.TE ? BlockKind.Chip : BlockKind.PassPro, BlockTarget.BlitzPickup);
                }
                else
                {
                    if (pos == Position.WR) BlockAt(i, BlockKind.Stalk, BlockTarget.Cornerback);
                    else if (pos == Position.TE) BlockAt(i, BlockKind.Man, BlockTarget.EdgeDefender);
                    else BlockAt(i, BlockKind.Lead, BlockTarget.Linebacker);
                }
            }
            _reads.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));   // total order: equal read numbers fall back to the slot
            foreach (var kv in _reads) Qb.ReadProgression.Add(kv.Value);
            return new Play
            {
                Id = DefaultPlaybooks.Slug(F.Name + "." + name),
                Name = name,
                FormationName = F.Name,
                Side = PlaySide.Offense,
                Category = cat,
                Scheme = CoverageScheme.None,
                Assignments = A,
                Tags = tags,
                Gap = CarryGap(A)
            };
        }

        /// <summary>The run gap of the first real (non-fake) carry, or None for a pass (QA-547: the play's own gap, used by the diagram's blockers).</summary>
        static RunGap CarryGap(Assignment[] a)
        {
            foreach (Assignment x in a)
            {
                var c = x as CarryAssignment;
                if (c != null && !c.IsFake) return c.Gap;
            }
            return RunGap.None;
        }
    }

    public static partial class DefaultPlaybooks
    {
        /// <summary>Spec tokens ("HB=Flat*3") that did not apply while the default playbook was built (no such player in the formation, slot already assigned, unknown route): diagnostics for the playbook tests.</summary>
        public static readonly System.Collections.Generic.List<string> SkippedSpecTokens = new System.Collections.Generic.List<string>();

        /// <summary>Receivers a pass concept did not name (they run the default clearing vertical or hitch): diagnostics for the playbook tests.</summary>
        public static readonly System.Collections.Generic.List<string> DefaultedReceivers = new System.Collections.Generic.List<string>();

        public const string Singleback = "Singleback", IForm = "I-Form", ShotgunF = "Shotgun", Pistol = "Pistol",
            GoalLine = "Goal Line", Empty = "Empty", Trips = "Trips";

        internal static string Slug(string s)
        {
            return s.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        }

        private static SlotSpec S(Position p, float x, float y, string label) { return new SlotSpec(p, x, y, label); }

        private static SlotSpec[] OffSlots(SlotSpec qb, SlotSpec s1, SlotSpec a, SlotSpec b, SlotSpec c, SlotSpec d)
        {
            return new[]
            {
                qb, s1,
                S(Position.LT, -2.5f, -0.15f, "LT"), S(Position.LG, -1.25f, -0.15f, "LG"), S(Position.C, 0f, -0.15f, "C"),
                S(Position.RG, 1.25f, -0.15f, "RG"), S(Position.RT, 2.5f, -0.15f, "RT"),
                a, b, c, d
            };
        }

        /// <summary>
        /// The offensive formations. Alignment numbers (yards from the ball): the line splits are 1.25 yd, a tight end is attached at 3.75, a slot receiver stands 8-12 yd out and a split end 17.5 (near the numbers),
        /// receivers on the line stand at 0 and flankers / slots one yard off it, a shotgun passer 5 yd back with the back beside him, a pistol passer 4 yd back with the back directly behind him,
        /// an under-center back 6-7 yd deep and a fullback 3-4 yd deep. Every set keeps its family's labels (X, Z, H, W, Y, Y2, HB, F) so concepts resolve their roles.
        /// </summary>
        public static List<Formation> OffensiveFormations()
        {
            var list = new List<Formation>();
            // ---- base families ----
            list.Add(new Formation(Singleback, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, -8f, -1, "H"))));
            list.Add(new Formation(IForm, PlaySide.Offense, "21", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.FB, 0, -4, "F"), S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"))));
            list.Add(new Formation(ShotgunF, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, 2f, -5, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, 10f, -1, "H"))));
            list.Add(new Formation(Pistol, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -4, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, -8f, -1, "H"))));
            list.Add(new Formation(GoalLine, PlaySide.Offense, "22", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.FB, 0, -3, "F"), S(Position.TE, 3.75f, 0, "Y"), S(Position.TE, -3.75f, 0, "Y2"), S(Position.WR, 12f, 0, "Z"))));
            list.Add(new Formation(Empty, PlaySide.Offense, "01", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.WR, -8f, -1, "W"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, 10f, -1, "H"))));
            list.Add(new Formation(Trips, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, -2f, -5, "HB"),
                S(Position.TE, -3.75f, 0, "Y"), S(Position.WR, 18f, 0, "Z"), S(Position.WR, 12f, -1, "H"), S(Position.WR, 7.5f, -1, "W"))));

            // ---- alignment / personnel variants (the second play-call screen) ----
            list.Add(new Formation("Shotgun Doubles", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, 2f, -5, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, -8f, -1, "H")), ShotgunF, "Doubles"));
            list.Add(new Formation("Shotgun Bunch", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, -2f, -5, "HB"),
                S(Position.TE, -3.75f, 0, "Y"), S(Position.WR, 14.5f, 0, "Z"), S(Position.WR, 16.5f, -1.2f, "H"), S(Position.WR, 12.5f, -1.2f, "W")), ShotgunF, "Bunch"));
            list.Add(new Formation("Shotgun Spread", PlaySide.Offense, "10", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, 2f, -5, "HB"),
                S(Position.WR, -17.5f, 0, "X"), S(Position.WR, -8f, -1, "H"), S(Position.WR, 8.5f, -1, "W"), S(Position.WR, 17.5f, 0, "Z")), ShotgunF, "Spread (10)"));
            list.Add(new Formation("Shotgun Ace", PlaySide.Offense, "12", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, 2f, -5, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.TE, -3.75f, 0, "Y2"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z")), ShotgunF, "Ace (12)"));
            list.Add(new Formation("Singleback Ace", PlaySide.Offense, "12", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.TE, -3.75f, 0, "Y2"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z")), Singleback, "Ace (12)"));
            list.Add(new Formation("Singleback Doubles", PlaySide.Offense, "10", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.WR, -17.5f, 0, "X"), S(Position.WR, -8f, -1, "H"), S(Position.WR, 8.5f, -1, "W"), S(Position.WR, 17.5f, 0, "Z")), Singleback, "Doubles (10)"));
            list.Add(new Formation("Singleback Trips", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.TE, -3.75f, 0, "Y"), S(Position.WR, 18f, 0, "Z"), S(Position.WR, 12.5f, -1, "H"), S(Position.WR, 7.5f, -1, "W")), Singleback, "Trips"));
            list.Add(new Formation("Singleback Bunch", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.TE, -3.75f, 0, "Y"), S(Position.WR, 14.5f, 0, "Z"), S(Position.WR, 16.5f, -1.2f, "H"), S(Position.WR, 12.5f, -1.2f, "W")), Singleback, "Bunch"));
            list.Add(new Formation("I-Form Slot", PlaySide.Offense, "20", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.FB, 0, -4, "F"), S(Position.WR, 8f, -1, "H"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, 0, "Z")), IForm, "Slot (20)"));   // 7 on the line (QA-510)
            list.Add(new Formation("I-Form Twins", PlaySide.Offense, "21", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.FB, 0, -4, "F"), S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, -10f, -1, "H")), IForm, "Twins"));
            list.Add(new Formation("I-Form Tight", PlaySide.Offense, "22", OffSlots(
                S(Position.QB, 0, -1.25f, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.FB, 0, -4, "F"), S(Position.TE, 3.75f, 0, "Y"), S(Position.TE, -3.75f, 0, "Y2"), S(Position.WR, 12.5f, -1, "Z")), IForm, "Tight (22)"));
            list.Add(new Formation("Pistol Trips", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -4, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, 10f, -1, "H")), Pistol, "Trips"));
            list.Add(new Formation("Pistol Ace", PlaySide.Offense, "12", OffSlots(
                S(Position.QB, 0, -4, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.TE, 3.75f, 0, "Y"), S(Position.TE, -3.75f, 0, "Y2"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z")), Pistol, "Ace (12)"));
            list.Add(new Formation("Pistol Strong", PlaySide.Offense, "21", OffSlots(
                S(Position.QB, 0, -4, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.FB, 2.6f, -4.6f, "F"), S(Position.TE, 3.75f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z")), Pistol, "Strong (21)"));
            list.Add(new Formation("Empty Quads", PlaySide.Offense, "01", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.WR, -17.5f, 0, "X"),
                S(Position.WR, 19f, 0, "Z"), S(Position.WR, 15.5f, -1, "H"), S(Position.WR, 12f, -1, "W"), S(Position.TE, 8.5f, -1, "Y")), Empty, "Quads"));
            return list;
        }

        /// <summary>The formation names plus every variant formation whose family is one of them (so each concept also builds for the variants).</summary>
        private static string[] Expand(string[] names)
        {
            var all = new List<string>(names);
            foreach (Formation v in OffensiveFormations())
                if (v.Family != v.Name && Array.IndexOf(names, v.Family) >= 0 && !all.Contains(v.Name)) all.Add(v.Name);
            return all.ToArray();
        }

        private static Vec2 P(float x, float y) { return new Vec2(x, y); }

        private delegate bool RunBuild(OffBuilder b, int dir);

        public static void AddOffense(Playbook pb)
        {
            foreach (Formation f in OffensiveFormations()) if (pb.FindFormation(f.Name) == null) pb.Formations.Add(f);
            AddRuns(pb);
            AddPasses(pb);
        }

        private static void AddRun(Playbook pb, string name, PlayCategory cat, string[] formations, bool twoWay, RunBuild build)
        {
            foreach (string fn in Expand(formations))
            {
                Formation f = pb.FindFormation(fn);
                int[] dirs = twoWay ? new[] { 1, -1 } : new[] { 1 };
                foreach (int dir in dirs)
                {
                    var b = new OffBuilder(f, false);
                    if (!build(b, dir)) continue;
                    string n = twoWay ? name + (dir > 0 ? " Right" : " Left") : name;
                    Play p = b.Finish(n, cat, "run");
                    AddPlay(pb, p);
                }
            }
        }

        private static void AddRuns(Playbook pb)
        {
            AddRun(pb, "Inside Zone", PlayCategory.InsideRun, new[] { Singleback, IForm, ShotgunF, Pistol }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, 0, OffBuilder.HB);
                b.OlZone(d);
                b.Block("F", BlockKind.Lead, BlockTarget.Linebacker);
                b.Block("TE", BlockKind.Zone, d > 0 ? BlockTarget.ZoneRight : BlockTarget.ZoneLeft);
                return b.Carry("HB", CarryStyle.InsideZone, d > 0 ? RunGap.BRight : RunGap.BLeft, 0f, P(d * 1.5f, -3f), P(d * 2.5f, 0.5f), P(d * 3.5f, 6f));
            });
            AddRun(pb, "Outside Zone", PlayCategory.OutsideRun, new[] { Singleback, IForm, Pistol, ShotgunF }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, d, OffBuilder.HB);
                b.OlZone(d);
                b.Block("F", BlockKind.Lead, BlockTarget.EdgeDefender);
                b.Block("TE", BlockKind.Zone, d > 0 ? BlockTarget.ZoneRight : BlockTarget.ZoneLeft);
                float oy = b.F.Slots[OffBuilder.HB].Y;
                return b.Carry("HB", CarryStyle.OutsideZone, d > 0 ? RunGap.DRight : RunGap.DLeft, 0f, P(d * 2.8f, Math.Min(-3f, oy + 1.8f)), P(d * 5.0f, -1.0f), P(d * 7.0f, 3.0f), P(d * 8.5f, 10f));
            });
            AddRun(pb, "Power", PlayCategory.InsideRun, new[] { IForm, Singleback, GoalLine, Pistol }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, 0, OffBuilder.HB);
                b.OlPower(d);
                if (!b.Block("F", BlockKind.Kickout, BlockTarget.EdgeDefender))
                {
                    // the kickout comes from the PLAYSIDE tight end or slot (the backside end used to cross the formation on Power Left)
                    foreach (string lbl in new[] { "Y", "Y2", "H" })
                    {
                        int ix = b.Idx(lbl);
                        if (ix >= 0 && b.F.Slots[ix].X * d > 0f && b.Block(lbl, BlockKind.Kickout, BlockTarget.EdgeDefender)) break;
                    }
                }
                return b.Carry("HB", CarryStyle.Power, d > 0 ? RunGap.CRight : RunGap.CLeft, 0f, P(d * 2.4f, -3.4f), P(d * 3.8f, -0.8f), P(d * 4.6f, 4f), P(d * 5.2f, 11f));
            });
            AddRun(pb, "Counter", PlayCategory.InsideRun, new[] { Singleback, Pistol, ShotgunF }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, 0, OffBuilder.HB);
                b.OlCounter(d);
                float cy = b.F.Slots[OffBuilder.HB].Y;
                return b.Carry("HB", CarryStyle.Counter, d > 0 ? RunGap.BRight : RunGap.BLeft, 0f, P(-d * 1.0f, cy + 0.4f), P(d * 2.5f, cy + 3f), P(d * 4.6f, 0.4f), P(d * 5.5f, 8f));
            });
            AddRun(pb, "Dive", PlayCategory.InsideRun, new[] { IForm, GoalLine, Pistol }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, 0, OffBuilder.HB);
                b.OlZone(d);
                b.Block("F", BlockKind.Lead, BlockTarget.Linebacker);
                return b.Carry("HB", CarryStyle.Dive, d > 0 ? RunGap.ARight : RunGap.ALeft, 0f, P(d * 0.8f, -3f), P(d * 1f, 2f), P(d * 1f, 8f));
            });
            AddRun(pb, "Toss Sweep", PlayCategory.OutsideRun, new[] { IForm, Singleback }, true, (b, d) =>
            {
                b.SetQb(QbAction.Toss, 0, d, OffBuilder.HB);
                b.OlZone(d);
                b.Block("F", BlockKind.Lead, BlockTarget.EdgeDefender);
                return b.Carry("HB", CarryStyle.Toss, d > 0 ? RunGap.DRight : RunGap.DLeft, 0f, P(d * 5f, -4.5f), P(d * 10f, -3f), P(d * 14f, 2f), P(d * 16f, 10f));
            });
            AddRun(pb, "Draw", PlayCategory.Draw, new[] { ShotgunF, Singleback, Pistol }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 3, 0, OffBuilder.HB);
                b.OlPassPro(0);
                float hy = b.F.Slots[OffBuilder.HB].Y;
                return b.Carry("HB", CarryStyle.Draw, d > 0 ? RunGap.ARight : RunGap.ALeft, 0.75f, P(d * 1.5f, Math.Min(-2f, hy + 2.5f)), P(d * 2f, 1f), P(d * 3f, 8f));
            });
            AddRun(pb, "Trap", PlayCategory.InsideRun, new[] { IForm, Singleback }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, 0, OffBuilder.HB);
                b.OlTrap(d);
                return b.Carry("HB", CarryStyle.Trap, d > 0 ? RunGap.BRight : RunGap.BLeft, 0f, P(d * 2.4f, -3f), P(d * 3f, 0f), P(d * 3.5f, 6f));
            });
            AddRun(pb, "Iso", PlayCategory.InsideRun, new[] { IForm, GoalLine }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, 0, OffBuilder.HB);
                b.OlZone(d);
                b.Block("F", BlockKind.Lead, BlockTarget.Mike);
                return b.Carry("HB", CarryStyle.Iso, d > 0 ? RunGap.BRight : RunGap.BLeft, 0f, P(d * 1f, -4f), P(d * 1.5f, 1f), P(d * 2f, 7f));
            });
            AddRun(pb, "QB Sneak", PlayCategory.QbRun, new[] { GoalLine, Singleback }, false, (b, d) =>
            {
                b.SetQb(QbAction.Sneak, 0, 0, -1, P(0f, 0.5f), P(0f, 2f));
                for (int i = OffBuilder.LT; i <= OffBuilder.RT; i++) b.BlockAt(i, BlockKind.Man, BlockTarget.NearestDefender);
                return true;
            });
            AddRun(pb, "QB Draw", PlayCategory.QbRun, new[] { ShotgunF }, true, (b, d) =>
            {
                b.SetQb(QbAction.Keep, 3, 0, -1, P(d * 1f, -3f), P(d * 2f, 3f), P(d * 3f, 10f));
                b.OlPassPro(0);
                b.Block("HB", BlockKind.Lead, BlockTarget.Linebacker);
                return true;
            });
            AddRun(pb, "Read Option", PlayCategory.QbRun, new[] { ShotgunF, Pistol }, true, (b, d) =>
            {
                b.SetQb(QbAction.ReadOption, 0, d, OffBuilder.HB, P(d * 3f, -2.5f), P(d * 6f, 0.5f), P(d * 9f, 6f), P(d * 11f, 14f));
                b.OlZone(-d);
                return b.Carry("HB", CarryStyle.Option, d > 0 ? RunGap.BLeft : RunGap.BRight, 0f, P(-d * 2.2f, -2.8f), P(-d * 3.2f, 0.5f), P(-d * 3.8f, 7f));
            });
            AddRun(pb, "Jet Sweep", PlayCategory.OutsideRun, new[] { ShotgunF, Pistol, Singleback, Trips, Empty }, true, (b, d) =>
            {
                // the motion man crosses the formation against the direction of the run and takes the ball on the move
                string lbl = null; float best = 99f;
                foreach (string l in new[] { "H", "W" })
                {
                    if (!b.Has(l)) continue;
                    float x0 = b.F.Slots[b.Idx(l)].X;
                    if (x0 * d < -2f && Math.Abs(x0) < best) { best = Math.Abs(x0); lbl = l; }
                }
                if (lbl == null) return false;
                int slot = b.Idx(lbl);
                float yc = Math.Min(-3f, b.F.Slots[0].Y + 0.8f);
                b.SetQb(QbAction.Toss, 0, d, slot);
                b.OlZone(d);
                b.Block("TE", BlockKind.Man, BlockTarget.EdgeDefender);   // v0.9.0 (E-156): the tight end takes the edge defender, the back leads outside of him
                b.Block("HB", BlockKind.Lead, BlockTarget.Linebacker);
                foreach (string wl in new[] { "X", "Z", "W", "H" })   // v0.9.0 (E-156): the playside receivers block the force players (the corner, then the nearest linebacker or safety)
                {
                    if (wl == lbl || !b.Has(wl)) continue;
                    float wx = b.F.Slots[b.Idx(wl)].X;
                    if (wx * d > 3f) b.Block(wl, BlockKind.Man, Math.Abs(wx) >= 9f ? BlockTarget.Cornerback : BlockTarget.Linebacker);
                }
                return b.Carry(lbl, CarryStyle.Sweep, d > 0 ? RunGap.DRight : RunGap.DLeft, 0f, P(d * 3f, yc), P(d * 8f, yc + 1.5f), P(d * 12f, 0.5f), P(d * 15f, 8f));
            });
            AddRun(pb, "Kneel", PlayCategory.Kneel, new[] { Singleback }, false, (b, d) =>
            {
                b.SetQb(QbAction.Kneel, 0);
                for (int i = OffBuilder.LT; i <= OffBuilder.RT; i++) b.BlockAt(i, BlockKind.Man, BlockTarget.NearestDefender);
                return true;
            });
            AddRun(pb, "Spike", PlayCategory.Spike, new[] { ShotgunF }, false, (b, d) =>
            {
                b.SetQb(QbAction.Spike, 0);
                for (int i = OffBuilder.LT; i <= OffBuilder.RT; i++) b.BlockAt(i, BlockKind.PassPro, BlockTarget.NearestDefender);
                return true;
            });
        }

        internal static void AddPlay(Playbook pb, Play p)
        {
            string setName = PlayCallModel.SetNameFor(p.Side, p.Category);
            PlaySet set = null;
            foreach (PlaySet s in pb.Sets) if (s.Name == setName) { set = s; break; }
            if (set == null) { set = new PlaySet(setName, p.Side); pb.Sets.Add(set); }
            set.Plays.Add(p);
        }
    }
}
