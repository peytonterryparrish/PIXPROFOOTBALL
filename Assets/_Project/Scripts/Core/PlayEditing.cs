using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>The seven hot routes a receiver can be switched to at the line (v0.3).</summary>
    public enum HotRoute { Slant, Fade, Curl, In, Out, Streak, Block }

    /// <summary>
    /// Copy-on-write helpers for the playbook data: plays are shared and immutable by convention, so every pre-snap change
    /// (flip, hot route, slide protection, audibles) works on clones.
    /// </summary>
    public static class PlayEditing
    {
        // ------------------------------------------------------------------------------------------------ cloning
        public static Assignment Clone(Assignment a)
        {
            if (a == null) return null;
            var r = a as RouteAssignment;
            if (r != null) return new RouteAssignment { Slot = r.Slot, Route = r.Route, Waypoints = new List<RouteWaypoint>(r.Waypoints), ReadOrder = r.ReadOrder, IsHot = r.IsHot };
            var b = a as BlockAssignment;
            if (b != null) return new BlockAssignment { Slot = b.Slot, Block = b.Block, Target = b.Target, Gap = b.Gap, TargetSlot = b.TargetSlot, Path = new List<Vec2>(b.Path) };
            var c = a as CarryAssignment;
            if (c != null) return new CarryAssignment { Slot = c.Slot, Style = c.Style, Gap = c.Gap, Path = new List<Vec2>(c.Path), Delay = c.Delay, IsFake = c.IsFake };
            var q = a as QbAssignment;
            if (q != null) return new QbAssignment { Slot = q.Slot, Action = q.Action, Shotgun = q.Shotgun, DropbackSteps = q.DropbackSteps, RollDirection = q.RollDirection, ReadProgression = new List<int>(q.ReadProgression), HandoffSlot = q.HandoffSlot, RunPath = new List<Vec2>(q.RunPath) };
            var cv = a as CoverageAssignment;
            if (cv != null) return new CoverageAssignment { Slot = cv.Slot, IsMan = cv.IsMan, TargetSlot = cv.TargetSlot, Press = cv.Press, Zone = cv.Zone == null ? null : new ZoneRegion(cv.Zone.Kind, cv.Zone.CenterX, cv.Zone.CenterY, cv.Zone.Width, cv.Zone.Height) };
            var ru = a as RushAssignment;
            if (ru != null) return new RushAssignment { Slot = ru.Slot, Gap = ru.Gap, Stunt = ru.Stunt, StuntPartnerSlot = ru.StuntPartnerSlot, Contain = ru.Contain };
            var sp = a as SpyAssignment;
            if (sp != null) return new SpyAssignment { Slot = sp.Slot, TargetSlot = sp.TargetSlot };
            var bl = a as BlitzAssignment;
            if (bl != null) return new BlitzAssignment { Slot = bl.Slot, Gap = bl.Gap, Edge = bl.Edge, Delay = bl.Delay };
            var k = a as KickAssignment;
            if (k != null) return new KickAssignment { Slot = k.Slot, KickType = k.KickType, Role = k.Role, AimX = k.AimX };
            var l = a as LaneAssignment;
            if (l != null) return new LaneAssignment { Slot = l.Slot, Lane = l.Lane, Gunner = l.Gunner, Path = new List<Vec2>(l.Path) };
            var rt = a as ReturnerAssignment;
            if (rt != null) return new ReturnerAssignment { Slot = rt.Slot, FairCatchAllowed = rt.FairCatchAllowed, ReturnLaneX = rt.ReturnLaneX, Path = new List<Vec2>(rt.Path) };
            throw new NotSupportedException(a.GetType().Name);
        }

        /// <summary>Shallow copy of a play with its own assignment array (assignments themselves are shared until replaced).</summary>
        public static Play CloneShallow(Play p)
        {
            return new Play
            {
                Id = p.Id, Name = p.Name, FormationName = p.FormationName, Side = p.Side, Category = p.Category, Scheme = p.Scheme,
                Assignments = (Assignment[])p.Assignments.Clone(), Tags = p.Tags, Gap = p.Gap, Mirrored = p.Mirrored
            };
        }

        public static Play CloneDeep(Play p)
        {
            Play c = CloneShallow(p);
            for (int i = 0; i < c.Assignments.Length; i++) c.Assignments[i] = Clone(c.Assignments[i]);
            return c;
        }

        // ------------------------------------------------------------------------------------------------ mirror
        private static int MapSlot(int i)
        {
            // the offensive line swaps sides; the skill players keep their index and move to the other side
            switch (i) { case 2: return 6; case 6: return 2; case 3: return 5; case 5: return 3; default: return i; }
        }

        private static RunGap MirrorGap(RunGap g)
        {
            switch (g)
            {
                case RunGap.ALeft: return RunGap.ARight; case RunGap.ARight: return RunGap.ALeft;
                case RunGap.BLeft: return RunGap.BRight; case RunGap.BRight: return RunGap.BLeft;
                case RunGap.CLeft: return RunGap.CRight; case RunGap.CRight: return RunGap.CLeft;
                case RunGap.DLeft: return RunGap.DRight; case RunGap.DRight: return RunGap.DLeft;
                default: return g;
            }
        }

        private static BlockTarget MirrorTarget(BlockTarget t)
        {
            switch (t)
            {
                case BlockTarget.ZoneLeft: return BlockTarget.ZoneRight; case BlockTarget.ZoneRight: return BlockTarget.ZoneLeft;
                case BlockTarget.SlideLeft: return BlockTarget.SlideRight; case BlockTarget.SlideRight: return BlockTarget.SlideLeft;
                default: return t;
            }
        }

        private static int MapRef(int slot) { return slot < 0 || slot > 10 ? slot : MapSlot(slot); }

        /// <summary>Mirror image of an offensive formation (skill positions move to the other side; the line is symmetric).</summary>
        public static Formation MirrorFormation(Formation f)
        {
            var slots = new SlotSpec[f.Slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                SlotSpec s = f.Slots[i];
                bool line = i >= 2 && i <= 6;
                slots[i] = line ? s : new SlotSpec(s.Position, -s.X, s.Y, s.Label);
            }
            return new Formation(f.Name, f.Side, f.Unit, slots, f.Family, f.SetName);
        }

        /// <summary>
        /// Flips an offensive play left to right: routes, run paths, blocking schemes and the line's roles all mirror, and the
        /// result is flagged <see cref="Play.Mirrored"/> (its formation is the mirrored image of the named one). Mirroring twice restores the original.
        /// </summary>
        public static Play Mirror(Play src)
        {
            if (src.Side != PlaySide.Offense) throw new ArgumentException("Only offensive plays can be flipped.");
            Play p = CloneShallow(src);
            p.Mirrored = !src.Mirrored;
            p.Gap = MirrorGap(src.Gap);
            for (int i = 0; i < src.Assignments.Length; i++)
            {
                int to = MapSlot(i);
                Assignment m = MirrorAssignment(src.Assignments[i], to);
                p.Assignments[to] = m;
            }
            return p;
        }

        private static Assignment MirrorAssignment(Assignment a, int newSlot)
        {
            Assignment c = Clone(a);
            c.Slot = newSlot;
            var r = c as RouteAssignment;
            if (r != null)
            {
                for (int i = 0; i < r.Waypoints.Count; i++) { RouteWaypoint w = r.Waypoints[i]; r.Waypoints[i] = new RouteWaypoint(w.Point.MirrorX(), w.Time, w.Cut); }
                return r;
            }
            var b = c as BlockAssignment;
            if (b != null)
            {
                b.Target = MirrorTarget(b.Target); b.Gap = MirrorGap(b.Gap); b.TargetSlot = MapRef(b.TargetSlot);
                for (int i = 0; i < b.Path.Count; i++) b.Path[i] = b.Path[i].MirrorX();
                return b;
            }
            var cr = c as CarryAssignment;
            if (cr != null)
            {
                cr.Gap = MirrorGap(cr.Gap);
                for (int i = 0; i < cr.Path.Count; i++) cr.Path[i] = cr.Path[i].MirrorX();
                return cr;
            }
            var q = c as QbAssignment;
            if (q != null)
            {
                q.RollDirection = -q.RollDirection;
                q.HandoffSlot = MapRef(q.HandoffSlot);
                for (int i = 0; i < q.ReadProgression.Count; i++) q.ReadProgression[i] = MapRef(q.ReadProgression[i]);
                for (int i = 0; i < q.RunPath.Count; i++) q.RunPath[i] = q.RunPath[i].MirrorX();
                return q;
            }
            return c;
        }

        /// <summary>The formation a play is drawn / played on: the playbook's, or its mirror image when the play was flipped.</summary>
        public static Formation FormationFor(Playbook book, Play play)
        {
            Formation f = book.FindFormation(play.FormationName);
            if (f != null && play.Mirrored && play.Side == PlaySide.Offense) return MirrorFormation(f);
            return f;
        }

        // ------------------------------------------------------------------------------------------------ hot routes
        public static string HotRouteName(HotRoute r)
        {
            switch (r)
            {
                case HotRoute.Streak: return "STREAK"; case HotRoute.Fade: return "FADE"; case HotRoute.Slant: return "SLANT";
                case HotRoute.Curl: return "CURL"; case HotRoute.In: return "IN"; case HotRoute.Out: return "OUT"; default: return "BLOCK";
            }
        }

        /// <summary>Whether this slot can be given a hot route (an eligible receiver who is not the intended ball carrier of the run).</summary>
        public static bool CanHotRoute(Play play, Formation f, int slot)
        {
            if (play == null || f == null || slot < 1 || slot > 10 || play.Side != PlaySide.Offense) return false;
            if (!PositionGroup.IsEligibleReceiver(f.Slots[slot].Position)) return false;
            var qb = play.Assignments[0] as QbAssignment;
            if (qb != null && (qb.Action == QbAction.Kneel || qb.Action == QbAction.Spike)) return false;
            var ca = play.Assignments[slot] as CarryAssignment;
            if (ca != null && !ca.IsFake) return false;
            if (qb != null && qb.HandoffSlot == slot) return false;
            return true;
        }

        /// <summary>The new assignment for a hot route (route with waypoints from the player's alignment, or a stalk / chip block).</summary>
        public static Assignment BuildHotRoute(Formation f, int slot, HotRoute hot)
        {
            SlotSpec s = f.Slots[slot];
            if (hot == HotRoute.Block)
            {
                bool back = s.Position == Position.RB || s.Position == Position.FB || s.Position == Position.TE;
                return new BlockAssignment { Slot = slot, Block = back ? BlockKind.Chip : BlockKind.Stalk, Target = back ? BlockTarget.BlitzPickup : BlockTarget.Cornerback };
            }
            RouteKind kind;
            switch (hot)
            {
                case HotRoute.Slant: kind = RouteKind.Slant; break;
                case HotRoute.Fade: kind = RouteKind.Fade; break;
                case HotRoute.Curl: kind = RouteKind.Curl; break;
                case HotRoute.In: kind = RouteKind.In; break;
                case HotRoute.Out: kind = RouteKind.Out; break;
                default: kind = RouteKind.Go; break;
            }
            float sign = s.X < 0f ? -1f : 1f;
            if (Math.Abs(s.X) < 0.5f) sign = 1f;
            var ra = new RouteAssignment { Slot = slot, Route = kind, IsHot = true };
            ra.Waypoints.AddRange(RouteLibrary.Build(kind, s.Offset, sign));
            return ra;
        }

        /// <summary>Returns a copy of the play where <paramref name="slot"/> runs the hot route and the quarterback reads him first.</summary>
        public static Play WithHotRoute(Play play, Formation f, int slot, HotRoute hot)
        {
            Play p = CloneShallow(play);
            p.Assignments[slot] = BuildHotRoute(f, slot, hot);
            var qb = Clone(play.Assignments[0]) as QbAssignment;
            if (qb != null)
            {
                qb.ReadProgression.Remove(slot);
                // QA-538: on a called run the quarterback has no progression to put him into (the route is only a decoy / outlet)
                bool runCall = qb.Action == QbAction.Handoff || qb.Action == QbAction.Toss || qb.Action == QbAction.Keep || qb.Action == QbAction.Sneak || qb.Action == QbAction.ReadOption;
                if (hot != HotRoute.Block && !runCall) qb.ReadProgression.Insert(0, slot);
                p.Assignments[0] = qb;
            }
            return p;
        }

        // ------------------------------------------------------------------------------------------------ protection
        /// <summary>Slide protection: every pass-blocking lineman slides left (-1) or right (+1); 0 restores man / BlitzPickup (nearest defender).</summary>
        public static Play WithSlide(Play play, int dir)
        {
            Play p = CloneShallow(play);
            for (int i = 2; i <= 6; i++)
            {
                var b = play.Assignments[i] as BlockAssignment;
                if (b == null || b.Block != BlockKind.PassPro) continue;
                var nb = (BlockAssignment)Clone(b);
                nb.Target = dir < 0 ? BlockTarget.SlideLeft : (dir > 0 ? BlockTarget.SlideRight : BlockTarget.NearestDefender);
                p.Assignments[i] = nb;
            }
            return p;
        }

        public static bool HasPassProtection(Play play)
        {
            for (int i = 2; i <= 6; i++) { var b = play.Assignments[i] as BlockAssignment; if (b != null && b.Block == BlockKind.PassPro) return true; }
            return false;
        }

        /// <summary>Current slide direction of the line: -1, 0 or 1 (by the center's neighbours).</summary>
        public static int SlideOf(Play play)
        {
            int l = 0, r = 0;
            for (int i = 2; i <= 6; i++)
            {
                var b = play.Assignments[i] as BlockAssignment;
                if (b == null || b.Block != BlockKind.PassPro) continue;
                if (b.Target == BlockTarget.SlideLeft) l++; else if (b.Target == BlockTarget.SlideRight) r++;
            }
            return l > r ? -1 : (r > l ? 1 : 0);
        }
    }
}
