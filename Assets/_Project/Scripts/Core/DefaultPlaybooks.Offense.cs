using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>Route templates as (outward, downfield) pairs. Downfield is absolute from the ball; outward is relative to the receiver's start x.</summary>
    public static class RouteLibrary
    {
        private static readonly Dictionary<RouteKind, float[]> Templates = new Dictionary<RouteKind, float[]>
        {
            { RouteKind.Go, new[] { 0f, 5f, 0f, 14f, 0f, 40f } },
            { RouteKind.Fade, new[] { 1f, 4f, 3f, 14f, 4f, 38f } },
            { RouteKind.Hitch, new[] { 0f, 6f, 0f, 4.5f } },
            { RouteKind.Curl, new[] { 0f, 12f, -1.5f, 10f } },
            { RouteKind.Comeback, new[] { 0f, 14f, 2.5f, 11f } },
            { RouteKind.Slant, new[] { 0f, 2f, -2.5f, 4.5f, -9f, 11f } },
            { RouteKind.Out, new[] { 0f, 9f, 8f, 9.5f } },
            { RouteKind.Dig, new[] { 0f, 12f, -12f, 12f } },
            { RouteKind.Post, new[] { 0f, 9f, -5f, 16f, -11f, 30f } },
            { RouteKind.Corner, new[] { 0f, 9f, 6f, 16f, 12f, 28f } },
            { RouteKind.Seam, new[] { 0f, 8f, 0f, 22f, 0f, 40f } },
            { RouteKind.Drag, new[] { -3f, 2f, -18f, 3f } },
            { RouteKind.Cross, new[] { 0f, 5f, -8f, 13f, -24f, 15f } },
            { RouteKind.Flat, new[] { 3f, 0.5f, 9f, 1.5f } },
            { RouteKind.Swing, new[] { 4f, -1f, 9f, 2f, 11f, 6f } },
            { RouteKind.Wheel, new[] { 3f, 0f, 9f, 2f, 11f, 12f, 10f, 32f } },
            { RouteKind.Stick, new[] { 0f, 5f, 1f, 5.5f } },
            { RouteKind.Texas, new[] { 3f, 1f, -3f, 5f, -5f, 11f } },
            { RouteKind.Bubble, new[] { 3f, -1f, 6f, 0f } },
            { RouteKind.ScreenRelease, new[] { -2f, -3f, -8f, -1f } },
            { RouteKind.Shallow, new[] { -4f, 3f, -20f, 4f } },
            { RouteKind.OutAndUp, new[] { 0f, 8f, 5f, 8.5f, 7f, 32f } },
            { RouteKind.Corner2, new[] { 0f, 12f, 8f, 22f, 14f, 36f } },
            { RouteKind.Delay, new[] { 0f, -1f, 4f, 2f, 8f, 6f } },
            { RouteKind.In, new[] { 0f, 6f, -8f, 6.5f } },
        };

        public const float RouteSpeed = 7f;

        public static List<RouteWaypoint> Build(RouteKind kind, Vec2 start, float sign)
        {
            float[] t = Templates[kind];
            var list = new List<RouteWaypoint>();
            Vec2 prev = start;
            Vec2 prevDir = Vec2.Zero;
            float time = 0.25f;
            for (int i = 0; i + 1 < t.Length; i += 2)
            {
                var p = new Vec2(start.X + sign * t[i], t[i + 1]);
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

        public OffBuilder(Formation f, bool isPass)
        {
            F = f; IsPass = isPass;
            Shotgun = f.Slots[0].Y <= -3f;
        }

        public static Vec2 P(float x, float y) { return new Vec2(x, y); }
        public int Idx(string label) { return F.IndexOfLabel(label); }
        public bool Has(string label) { return Idx(label) >= 0; }
        public float Sign(int slot) { return F.Slots[slot].X < 0f ? -1f : 1f; }

        public void SetQb(QbAction action, int steps, int roll = 0, int handoffSlot = -1, params Vec2[] runPath)
        {
            Qb = new QbAssignment { Slot = 0, Action = action, Shotgun = Shotgun, DropbackSteps = steps, RollDirection = roll, HandoffSlot = handoffSlot };
            Qb.RunPath.AddRange(runPath);
            A[0] = Qb;
        }

        public bool Route(string label, RouteKind kind, int read = 0)
        {
            int i = Idx(label);
            if (i < 0 || A[i] != null) return false;
            var ra = new RouteAssignment { Slot = i, Route = kind, ReadOrder = read };
            ra.Waypoints.AddRange(RouteLibrary.Build(kind, F.Slots[i].Offset, Sign(i)));
            A[i] = ra;
            RouteCount++;
            if (read > 0) _reads.Add(new KeyValuePair<int, int>(read, i));
            return true;
        }

        public bool Block(string label, BlockKind kind, BlockTarget target, RunGap gap = RunGap.None)
        {
            int i = Idx(label);
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
            int i = Idx(label);
            if (i < 0 || A[i] != null) return false;
            var c = new CarryAssignment { Slot = i, Style = style, Gap = gap, Delay = delay };
            c.Path.AddRange(path);
            A[i] = c;
            return true;
        }

        public bool FakeCarry(string label, CarryStyle style, RunGap gap, params Vec2[] path)
        {
            bool ok = Carry(label, style, gap, 0f, path);
            if (ok) ((CarryAssignment)A[Idx(label)]).IsFake = true;
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
                    BlockAt(i, BlockKind.Pull, BlockTarget.Playside, RunGap.None, P(sx * 0.35f, -1.3f), P(dir * 2.9f, -0.9f), P(dir * 5.2f, 1.3f));
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
                if (i == g) BlockAt(i, BlockKind.Pull, BlockTarget.EdgeDefender, RunGap.None, P(sx * 0.2f, -1.3f), P(dir * 2.5f, -1.2f), P(dir * 4.3f, 0.8f));
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
                if (i == puller) BlockAt(i, BlockKind.Pull, BlockTarget.Gap, RunGap.BRight, P(sx * 0.5f, -1f), P(dir * 1.6f, 0.3f));
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

        /// <summary>Applies a spec like "X=Slant*1 Z=Go*2 HB=@PassPro" of labeled route/block assignments.</summary>
        public void Spec(string spec)
        {
            foreach (string tok in spec.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = tok.IndexOf('=');
                string label = tok.Substring(0, eq);
                string what = tok.Substring(eq + 1);
                int read = 0;
                int star = what.IndexOf('*');
                if (star >= 0) { read = int.Parse(what.Substring(star + 1)); what = what.Substring(0, star); }
                if (what == "@PassPro") Block(label, BlockKind.PassPro, BlockTarget.BlitzPickup);
                else if (what == "@Chip") Block(label, BlockKind.Chip, BlockTarget.EdgeDefender);
                else if (what == "@Block") Block(label, BlockKind.Stalk, BlockTarget.Cornerback);
                else Route(label, (RouteKind)Enum.Parse(typeof(RouteKind), what), read);
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
                        A[i] = new RouteAssignment { Slot = i, Route = RouteKind.Hitch };
                        ((RouteAssignment)A[i]).Waypoints.AddRange(RouteLibrary.Build(RouteKind.Hitch, F.Slots[i].Offset, Sign(i)));
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
            _reads.Sort((a, b) => a.Key.CompareTo(b.Key));
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
                Tags = tags
            };
        }
    }

    public static partial class DefaultPlaybooks
    {
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
                S(Position.LT, -3f, 0f, "LT"), S(Position.LG, -1.5f, 0f, "LG"), S(Position.C, 0f, 0f, "C"),
                S(Position.RG, 1.5f, 0f, "RG"), S(Position.RT, 3f, 0f, "RT"),
                a, b, c, d
            };
        }

        public static List<Formation> OffensiveFormations()
        {
            var list = new List<Formation>();
            list.Add(new Formation(Singleback, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -1, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.TE, 4.5f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, -8f, -1, "H"))));
            list.Add(new Formation(IForm, PlaySide.Offense, "21", OffSlots(
                S(Position.QB, 0, -1, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.FB, 0, -4, "F"), S(Position.TE, 4.5f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"))));
            list.Add(new Formation(ShotgunF, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, 2f, -5, "HB"),
                S(Position.TE, 4.5f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, 10f, -1, "H"))));
            list.Add(new Formation(Pistol, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -4, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.TE, 4.5f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, -8f, -1, "H"))));
            list.Add(new Formation(GoalLine, PlaySide.Offense, "22", OffSlots(
                S(Position.QB, 0, -1, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.FB, 0, -3, "F"), S(Position.TE, 4.5f, 0, "Y"), S(Position.TE, -4.5f, 0, "Y2"), S(Position.WR, 12f, 0, "Z"))));
            list.Add(new Formation(Empty, PlaySide.Offense, "01", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.WR, -8f, -1, "W"),
                S(Position.TE, 4.5f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, 10f, -1, "H"))));
            list.Add(new Formation(Trips, PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, -2f, -5, "HB"),
                S(Position.TE, -4.5f, 0, "Y"), S(Position.WR, 18f, 0, "Z"), S(Position.WR, 12f, -1, "H"), S(Position.WR, 7.5f, -1, "W"))));

            // ---- v0.3 alignment / personnel variants (the second play-call screen). They reuse the labels of the base family so every concept builds. ----
            list.Add(new Formation("Shotgun Doubles", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, 2f, -5, "HB"),
                S(Position.TE, 4.5f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, -8f, -1, "H")), ShotgunF, "Doubles"));
            list.Add(new Formation("Shotgun Bunch", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -5, "QB"), S(Position.RB, -2f, -5, "HB"),
                S(Position.TE, 13f, -1.2f, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 14.5f, 0, "Z"), S(Position.WR, 16.5f, -1.2f, "H")), ShotgunF, "Bunch"));
            list.Add(new Formation("Singleback Ace", PlaySide.Offense, "12", OffSlots(
                S(Position.QB, 0, -1, "QB"), S(Position.RB, 0, -6, "HB"),
                S(Position.TE, 4.5f, 0, "Y"), S(Position.TE, -4.5f, 0, "Y2"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z")), Singleback, "Ace (12)"));
            list.Add(new Formation("I-Form Slot", PlaySide.Offense, "20", OffSlots(
                S(Position.QB, 0, -1, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.FB, 0, -4, "F"), S(Position.WR, 8f, -1, "H"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z")), IForm, "Slot (20)"));
            list.Add(new Formation("Pistol Trips", PlaySide.Offense, "11", OffSlots(
                S(Position.QB, 0, -4, "QB"), S(Position.RB, 0, -7, "HB"),
                S(Position.TE, 4.5f, 0, "Y"), S(Position.WR, -17.5f, 0, "X"), S(Position.WR, 17.5f, -1, "Z"), S(Position.WR, 10f, -1, "H")), Pistol, "Trips"));
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
                b.Block("Y", BlockKind.Zone, d > 0 ? BlockTarget.ZoneRight : BlockTarget.ZoneLeft);
                return b.Carry("HB", CarryStyle.InsideZone, d > 0 ? RunGap.BRight : RunGap.BLeft, 0f, P(d * 1.5f, -3f), P(d * 2.5f, 0.5f), P(d * 3.5f, 6f));
            });
            AddRun(pb, "Outside Zone", PlayCategory.OutsideRun, new[] { Singleback, IForm, Pistol, ShotgunF }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, d, OffBuilder.HB);
                b.OlZone(d);
                b.Block("F", BlockKind.Lead, BlockTarget.EdgeDefender);
                b.Block("Y", BlockKind.Zone, d > 0 ? BlockTarget.ZoneRight : BlockTarget.ZoneLeft);
                float oy = b.F.Slots[OffBuilder.HB].Y;
                return b.Carry("HB", CarryStyle.OutsideZone, d > 0 ? RunGap.DRight : RunGap.DLeft, 0f, P(d * 3f, Math.Min(-3f, oy + 1.8f)), P(d * 6.5f, -1.2f), P(d * 8.5f, 4f), P(d * 10f, 12f));
            });
            AddRun(pb, "Power", PlayCategory.InsideRun, new[] { IForm, Singleback, GoalLine, Pistol }, true, (b, d) =>
            {
                b.SetQb(QbAction.Handoff, 0, 0, OffBuilder.HB);
                b.OlPower(d);
                if (!b.Block("F", BlockKind.Kickout, BlockTarget.EdgeDefender)) b.Block("Y", BlockKind.Man, BlockTarget.Playside);
                return b.Carry("HB", CarryStyle.Power, d > 0 ? RunGap.CRight : RunGap.CLeft, 0f, P(d * 2.2f, -3.6f), P(d * 4.2f, -0.5f), P(d * 5f, 6f));
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
                b.Block("HB", BlockKind.Lead, BlockTarget.EdgeDefender);
                return b.Carry(lbl, CarryStyle.Sweep, d > 0 ? RunGap.DRight : RunGap.DLeft, 0f, P(d * 3f, yc), P(d * 9f, yc + 1.5f), P(d * 14f, 0.5f), P(d * 18f, 8f));
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

        private static void AddPass(Playbook pb, string name, PlayCategory cat, string[] formations, string spec,
            QbAction action = QbAction.QuickPass, int steps = 3, int roll = 0, bool screen = false, bool fakeRun = false)
        {
            foreach (string fn in Expand(formations))
            {
                Formation f = pb.FindFormation(fn);
                var b = new OffBuilder(f, true);
                int dropSteps = b.Shotgun ? steps : (steps <= 3 ? 3 : (steps <= 5 ? 5 : 7));
                b.SetQb(action, dropSteps, roll);
                if (fakeRun)
                    b.FakeCarry("HB", CarryStyle.InsideZone, RunGap.BRight, P(1.2f, Math.Min(-2.6f, b.F.Slots[OffBuilder.HB].Y + 3f)), P(1.8f, -1.2f));
                b.Spec(spec);
                if (b.RouteCount < 2) continue;
                if (screen) b.OlScreen(); else b.OlPassPro(roll >= 0 ? 1 : -1);
                AddPlay(pb, b.Finish(name, cat, "pass"));
            }
        }

        private static void AddPasses(Playbook pb)
        {
            string[] sg = { ShotgunF, Singleback };
            AddPass(pb, "Slants", PlayCategory.ShortPass, new[] { ShotgunF, Singleback, Trips, Pistol }, "X=Slant*1 Z=Slant*2 H=Slant*3 W=Slant*3 Y=Flat HB=Flat");
            AddPass(pb, "Quick Hitches", PlayCategory.ShortPass, new[] { ShotgunF, Pistol, Trips }, "X=Hitch*1 Z=Hitch*2 H=Hitch*3 W=Hitch*3 Y=Hitch HB=Swing");
            AddPass(pb, "Stick", PlayCategory.ShortPass, new[] { ShotgunF, Trips, Singleback }, "Y=Stick*1 X=Go H=Flat*2 Z=Out*3 W=Flat*2 HB=Swing");
            AddPass(pb, "Curl-Flat", PlayCategory.ShortPass, new[] { Singleback, ShotgunF, IForm }, "X=Curl*1 Z=Curl*2 HB=Flat*3 Y=Flat F=Flat H=Flat", QbAction.DropbackPass, 5);
            AddPass(pb, "Mesh", PlayCategory.ShortPass, new[] { ShotgunF, Empty, Pistol }, "X=Drag*1 Z=Shallow*2 H=Curl*3 W=Curl Y=Seam HB=Flat");
            AddPass(pb, "Snag", PlayCategory.ShortPass, new[] { Trips }, "Z=Corner*3 H=Curl*1 W=Flat*2 Y=Go HB=@PassPro");
            AddPass(pb, "Spacing", PlayCategory.ShortPass, new[] { ShotgunF, Empty }, "X=Hitch*2 Z=Hitch*3 H=Curl*1 W=Curl Y=Flat HB=Swing");
            AddPass(pb, "Double Outs", PlayCategory.ShortPass, sg, "X=Out*1 Z=Out*2 H=Flat*3 Y=Hitch HB=Swing");
            AddPass(pb, "Texas Route", PlayCategory.ShortPass, new[] { ShotgunF, Singleback, Pistol }, "HB=Texas*1 X=Go*2 Z=Hitch*3 H=Slant Y=Out");
            AddPass(pb, "Levels", PlayCategory.MediumPass, new[] { ShotgunF, Empty }, "X=Dig*1 H=Drag*2 W=Drag Z=Hitch Y=Seam HB=Swing", QbAction.DropbackPass, 5);
            AddPass(pb, "Smash", PlayCategory.MediumPass, new[] { ShotgunF, Singleback, Trips }, "Z=Corner*1 H=Hitch*2 W=Hitch X=Hitch*3 Y=Flat HB=Swing", QbAction.DropbackPass, 5);
            AddPass(pb, "Flood", PlayCategory.MediumPass, sg, "Z=Corner*1 H=Out*2 Y=Flat*3 X=Go HB=Flat", QbAction.Rollout, 5, 1);
            AddPass(pb, "Y-Cross", PlayCategory.MediumPass, sg, "Y=Cross*1 X=Post*2 Z=Go H=Curl*3 HB=Swing", QbAction.DropbackPass, 5);
            AddPass(pb, "Drive", PlayCategory.MediumPass, sg, "X=Dig*1 H=Drag*2 Z=Go Y=Seam HB=Swing", QbAction.DropbackPass, 5);
            AddPass(pb, "Deep Comebacks", PlayCategory.MediumPass, sg, "X=Comeback*1 Z=Comeback*2 H=Go*3 Y=Out HB=Flat", QbAction.DropbackPass, 5);
            AddPass(pb, "TE Seam", PlayCategory.MediumPass, new[] { Singleback, IForm, ShotgunF }, "Y=Seam*1 X=Hitch*2 Z=Out*3 HB=Flat F=Flat", QbAction.DropbackPass, 5);
            AddPass(pb, "Four Verticals", PlayCategory.DeepPass, new[] { ShotgunF, Empty, Trips }, "X=Go*1 Z=Go*2 H=Seam*3 W=Seam Y=Seam HB=Swing*4", QbAction.DropbackPass, 5);
            AddPass(pb, "Hail Mary", PlayCategory.DeepPass, new[] { ShotgunF, Empty }, "X=Go*1 Z=Go*2 H=Go*3 W=Go Y=Go HB=@PassPro", QbAction.HailMary, 5);
            AddPass(pb, "Post-Wheel", PlayCategory.DeepPass, sg, "X=Post*1 Z=Go*2 HB=Wheel*3 H=Out Y=Flat", QbAction.DropbackPass, 5);
            AddPass(pb, "Corner-Post", PlayCategory.DeepPass, sg, "X=Post*1 Z=Corner2*2 H=Dig*3 Y=Seam HB=@PassPro", QbAction.DropbackPass, 7);
            AddPass(pb, "Out-and-Up Shot", PlayCategory.DeepPass, new[] { Singleback, IForm }, "X=OutAndUp*1 Z=Go*2 Y=Drag*3 HB=@PassPro F=Flat*4", QbAction.DropbackPass, 7);
            AddPass(pb, "Wheel Route", PlayCategory.DeepPass, sg, "HB=Wheel*1 X=Go*2 Z=Hitch H=Out Y=Drag", QbAction.DropbackPass, 5);
            AddPass(pb, "Fade", PlayCategory.DeepPass, new[] { ShotgunF, Empty, GoalLine }, "Z=Fade*1 X=Fade*2 H=Slant*3 Y=Flat*3 Y2=Flat HB=@PassPro F=@PassPro");
            AddPass(pb, "PA Boot", PlayCategory.PlayAction, new[] { Singleback, IForm }, "Y=Drag*1 Z=Corner*2 HB=Flat*3 X=Post F=Flat", QbAction.Boot, 5, 1, false, true);
            AddPass(pb, "PA Crossers", PlayCategory.PlayAction, new[] { IForm, Singleback }, "Y=Cross*1 X=Post*2 Z=Dig*3", QbAction.PlayActionFake, 5, 0, false, true);
            AddPass(pb, "PA Deep Shot", PlayCategory.PlayAction, new[] { Singleback, IForm }, "X=Post*1 Z=Go*2 Y=Out*3 H=Hitch*4", QbAction.PlayActionFake, 5, 0, false, true);
            AddPass(pb, "HB Screen", PlayCategory.Screen, sg, "HB=ScreenRelease*1 X=Go Z=Go H=Go Y=@Block", QbAction.ScreenPass, 3, 0, true);
            AddPass(pb, "WR Screen", PlayCategory.Screen, new[] { ShotgunF, Trips }, "H=Bubble*1 W=Bubble*1 X=Go Z=@Block Y=@Block", QbAction.ScreenPass, 3, 0, true);
            AddPass(pb, "Slip Screen", PlayCategory.Screen, sg, "Y=ScreenRelease*1 X=Go Z=Go HB=Flat", QbAction.ScreenPass, 3, 0, true);
        }

        internal static void AddPlay(Playbook pb, Play p)
        {
            string setName = p.Side + " " + p.Category;
            PlaySet set = null;
            foreach (PlaySet s in pb.Sets) if (s.Name == setName) { set = s; break; }
            if (set == null) { set = new PlaySet(setName, p.Side); pb.Sets.Add(set); }
            set.Plays.Add(p);
        }
    }
}
