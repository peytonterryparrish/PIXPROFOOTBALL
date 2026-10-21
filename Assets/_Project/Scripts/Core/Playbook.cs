using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    public enum PlaySide { Offense, Defense, Special }

    public enum PlayCategory
    {
        // Offense
        InsideRun, OutsideRun, Draw, QbRun, ShortPass, MediumPass, DeepPass, PlayAction, Screen, Kneel, Spike,
        // Defense
        ZoneCoverage, ManCoverage, Blitz, GoalLine, Prevent,
        // Special teams
        Kickoff, Onside, Punt, FieldGoal, PointAfter, KickReturn, PuntReturn, FieldGoalBlock
    }

    public enum CoverageScheme
    {
        None, Cover0, Cover1, Cover2, Cover2Man, Cover3, Cover4, Cover6, Tampa2, Prevent
    }

    /// <summary>
    /// One of 11 positions in a formation. X = lateral yards from the ball (+ = offense's right),
    /// Y = yards downfield from the ball in the offense's direction (offense backfield negative, defense positive).
    /// </summary>
    public sealed class SlotSpec
    {
        public readonly Position Position;
        public readonly float X;
        public readonly float Y;
        /// <summary>Role label such as X, Z, H, Y, HB, F (offense) or a free label for other units.</summary>
        public readonly string Label;
        public SlotSpec(Position position, float x, float y, string label = "")
        {
            Position = position; X = x; Y = y; Label = label ?? "";
        }
        public Vec2 Offset { get { return new Vec2(X, Y); } }
    }

    public sealed class Formation
    {
        public const int SlotCount = 11;
        public readonly string Name;
        public readonly PlaySide Side;
        /// <summary>Special-teams unit name (Kickoff, KickoffReturn, Punt, PuntReturn, FieldGoal, FieldGoalBlock, Onside) or personnel grouping.</summary>
        public readonly string Unit;
        public readonly SlotSpec[] Slots;

        public Formation(string name, PlaySide side, string unit, SlotSpec[] slots)
        {
            Name = name; Side = side; Unit = unit ?? "";
            Slots = slots;
        }

        public int IndexOfLabel(string label)
        {
            for (int i = 0; i < Slots.Length; i++) if (Slots[i].Label == label) return i;
            return -1;
        }

        public int Count(Position p)
        {
            int n = 0;
            for (int i = 0; i < Slots.Length; i++) if (Slots[i].Position == p) n++;
            return n;
        }

        public override string ToString() { return Name; }
    }

    // ---------------------------------------------------------------- assignments

    public enum AssignmentKind { Route, Block, Carry, Qb, Coverage, Rush, Spy, Blitz, Kick, Lane, Returner }

    public abstract class Assignment
    {
        /// <summary>Index of the slot (0-10) this assignment belongs to.</summary>
        public int Slot;
        public abstract AssignmentKind Kind { get; }
    }

    public enum RouteKind
    {
        Go, Fade, Hitch, Curl, Comeback, Slant, Out, Dig, Post, Corner, Seam, Drag, Cross,
        Flat, Swing, Wheel, Stick, Texas, Bubble, ScreenRelease, Shallow, OutAndUp, Corner2, Delay
    }

    public readonly struct RouteWaypoint
    {
        /// <summary>Position in yards relative to the ball at the snap.</summary>
        public readonly Vec2 Point;
        /// <summary>Seconds after the snap the receiver should arrive here.</summary>
        public readonly float Time;
        /// <summary>True when the receiver makes a sharp cut at this waypoint.</summary>
        public readonly bool Cut;
        public RouteWaypoint(Vec2 point, float time, bool cut) { Point = point; Time = time; Cut = cut; }
    }

    public sealed class RouteAssignment : Assignment
    {
        public RouteKind Route;
        public List<RouteWaypoint> Waypoints = new List<RouteWaypoint>();
        /// <summary>Position in the QB's read progression (1 = first read, 0 = not a read).</summary>
        public int ReadOrder;
        public bool IsHot;
        public override AssignmentKind Kind { get { return AssignmentKind.Route; } }
    }

    public enum BlockKind { Zone, Man, Pull, PassPro, Lead, Seal, Chip, Stalk, Wall, Wedge, Kickout, Double }

    public enum BlockTarget
    {
        NearestDefender, ZoneLeft, ZoneRight, Mike, EdgeDefender, Backside, Playside, BlitzPickup,
        SlideLeft, SlideRight, Gap, Linebacker, Cornerback
    }

    public enum RunGap { None, ALeft, ARight, BLeft, BRight, CLeft, CRight, DLeft, DRight }

    public sealed class BlockAssignment : Assignment
    {
        public BlockKind Block;
        public BlockTarget Target;
        public RunGap Gap;
        /// <summary>Defensive slot for man blocks (-1 when not applicable).</summary>
        public int TargetSlot = -1;
        /// <summary>Path for pulling blockers (relative to the ball).</summary>
        public List<Vec2> Path = new List<Vec2>();
        public override AssignmentKind Kind { get { return AssignmentKind.Block; } }
    }

    public enum CarryStyle { InsideZone, OutsideZone, Power, Counter, Dive, Toss, Draw, Screen, Sneak, Sweep, Trap, Iso, Option, QbKeep }

    public sealed class CarryAssignment : Assignment
    {
        public CarryStyle Style;
        public RunGap Gap;
        public List<Vec2> Path = new List<Vec2>();
        /// <summary>Seconds after the snap before the carrier starts the path (draws, delays).</summary>
        public float Delay;
        /// <summary>Play-action fake: the runner sells the run but never receives the ball.</summary>
        public bool IsFake;
        public override AssignmentKind Kind { get { return AssignmentKind.Carry; } }
    }

    public enum QbAction
    {
        DropbackPass, QuickPass, Handoff, Toss, Keep, PlayActionFake, Rollout, Boot, Sneak, Kneel, Spike, ReadOption, ScreenPass, HailMary
    }

    public sealed class QbAssignment : Assignment
    {
        public QbAction Action;
        public bool Shotgun;
        /// <summary>Drop steps from center (3/5/7) or shotgun equivalents.</summary>
        public int DropbackSteps;
        /// <summary>Lateral roll direction: -1 left, 0 pocket, 1 right.</summary>
        public int RollDirection;
        /// <summary>Slots in read order (first = primary).</summary>
        public List<int> ReadProgression = new List<int>();
        public int HandoffSlot = -1;
        /// <summary>Path the QB runs on keepers, sneaks and option keeps (relative to the ball).</summary>
        public List<Vec2> RunPath = new List<Vec2>();
        public override AssignmentKind Kind { get { return AssignmentKind.Qb; } }
    }

    public enum ZoneKind { Flat, Hook, Curl, DeepThird, DeepHalf, Quarter, DeepMiddle, Robber, Buzz }

    /// <summary>Rectangular zone region relative to the ball. Depth/width in yards.</summary>
    public sealed class ZoneRegion
    {
        public ZoneKind Kind;
        public float CenterX;
        /// <summary>Depth (yards downfield of the ball) of the zone's anchor point.</summary>
        public float CenterY;
        public float Width;
        public float Height;
        public ZoneRegion(ZoneKind kind, float cx, float cy, float w, float h)
        {
            Kind = kind; CenterX = cx; CenterY = cy; Width = w; Height = h;
        }

        public static ZoneRegion FlatLeft() { return new ZoneRegion(ZoneKind.Flat, -17f, 4f, 12f, 7f); }
        public static ZoneRegion FlatRight() { return new ZoneRegion(ZoneKind.Flat, 17f, 4f, 12f, 7f); }
        public static ZoneRegion CurlLeft() { return new ZoneRegion(ZoneKind.Curl, -10f, 10f, 10f, 8f); }
        public static ZoneRegion CurlRight() { return new ZoneRegion(ZoneKind.Curl, 10f, 10f, 10f, 8f); }
        public static ZoneRegion HookLeft() { return new ZoneRegion(ZoneKind.Hook, -6f, 8f, 10f, 8f); }
        public static ZoneRegion HookMiddle() { return new ZoneRegion(ZoneKind.Hook, 0f, 9f, 10f, 8f); }
        public static ZoneRegion HookRight() { return new ZoneRegion(ZoneKind.Hook, 6f, 8f, 10f, 8f); }
        public static ZoneRegion DeepThirdLeft(float depth = 14f) { return new ZoneRegion(ZoneKind.DeepThird, -17.8f, depth, 17.8f, 30f); }
        public static ZoneRegion DeepThirdMiddle(float depth = 14f) { return new ZoneRegion(ZoneKind.DeepThird, 0f, depth, 17.8f, 30f); }
        public static ZoneRegion DeepThirdRight(float depth = 14f) { return new ZoneRegion(ZoneKind.DeepThird, 17.8f, depth, 17.8f, 30f); }
        public static ZoneRegion DeepHalfLeft(float depth = 14f) { return new ZoneRegion(ZoneKind.DeepHalf, -13.3f, depth, 26.7f, 30f); }
        public static ZoneRegion DeepHalfRight(float depth = 14f) { return new ZoneRegion(ZoneKind.DeepHalf, 13.3f, depth, 26.7f, 30f); }
        public static ZoneRegion DeepMiddle(float depth = 16f) { return new ZoneRegion(ZoneKind.DeepMiddle, 0f, depth, 20f, 30f); }
        public static ZoneRegion Robber() { return new ZoneRegion(ZoneKind.Robber, 0f, 7f, 10f, 6f); }
        public static ZoneRegion BuzzRight() { return new ZoneRegion(ZoneKind.Buzz, 12f, 8f, 10f, 8f); }
        public static ZoneRegion Quarter(int index, float depth = 13f)
        {
            float[] xs = { -20f, -6.7f, 6.7f, 20f };
            return new ZoneRegion(ZoneKind.Quarter, xs[index], depth, 13.3f, 30f);
        }
    }

    public sealed class CoverageAssignment : Assignment
    {
        public bool IsMan;
        /// <summary>Offense slot to cover in man (-1 for zone).</summary>
        public int TargetSlot = -1;
        public bool Press;
        public ZoneRegion Zone;
        public override AssignmentKind Kind { get { return AssignmentKind.Coverage; } }
    }

    public enum StuntKind { None, Twist, Loop, Slant, Pinch }

    public sealed class RushAssignment : Assignment
    {
        public RunGap Gap;
        public StuntKind Stunt;
        public int StuntPartnerSlot = -1;
        public bool Contain;
        public override AssignmentKind Kind { get { return AssignmentKind.Rush; } }
    }

    public sealed class SpyAssignment : Assignment
    {
        public int TargetSlot;
        public override AssignmentKind Kind { get { return AssignmentKind.Spy; } }
    }

    public sealed class BlitzAssignment : Assignment
    {
        public RunGap Gap;
        public bool Edge;
        /// <summary>Seconds after the snap before the blitzer commits (disguise).</summary>
        public float Delay;
        public override AssignmentKind Kind { get { return AssignmentKind.Blitz; } }
    }

    public enum KickKind { Deep, Squib, Onside, Punt, FieldGoal, ExtraPoint, DirectionalLeft, DirectionalRight, Rugby }
    public enum KickRole { Kicker, Holder, Snapper }

    public sealed class KickAssignment : Assignment
    {
        public KickKind KickType;
        public KickRole Role;
        /// <summary>Lateral aim point in yards from the ball.</summary>
        public float AimX;
        public override AssignmentKind Kind { get { return AssignmentKind.Kick; } }
    }

    public sealed class LaneAssignment : Assignment
    {
        public int Lane;
        public bool Gunner;
        public List<Vec2> Path = new List<Vec2>();
        public override AssignmentKind Kind { get { return AssignmentKind.Lane; } }
    }

    public sealed class ReturnerAssignment : Assignment
    {
        public bool FairCatchAllowed;
        public float ReturnLaneX;
        public List<Vec2> Path = new List<Vec2>();
        public override AssignmentKind Kind { get { return AssignmentKind.Returner; } }
    }

    // ---------------------------------------------------------------- play / sets / playbook

    public sealed class Play
    {
        public string Id;
        public string Name;
        public string FormationName;
        public PlaySide Side;
        public PlayCategory Category;
        public CoverageScheme Scheme;
        public Assignment[] Assignments = new Assignment[Formation.SlotCount];
        public string[] Tags = new string[0];
        /// <summary>Run gap for run plays (AI hint).</summary>
        public RunGap Gap;

        public bool IsPass
        {
            get
            {
                return Category == PlayCategory.ShortPass || Category == PlayCategory.MediumPass
                       || Category == PlayCategory.DeepPass || Category == PlayCategory.PlayAction
                       || Category == PlayCategory.Screen;
            }
        }

        public bool IsRun
        {
            get
            {
                return Category == PlayCategory.InsideRun || Category == PlayCategory.OutsideRun
                       || Category == PlayCategory.Draw || Category == PlayCategory.QbRun;
            }
        }

        /// <summary>Number of defenders rushing or blitzing the passer.</summary>
        public int PassRushers
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Assignments.Length; i++)
                    if (Assignments[i] is RushAssignment || Assignments[i] is BlitzAssignment) n++;
                return n;
            }
        }

        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Length; i++) if (Tags[i] == tag) return true;
            return false;
        }

        public T AssignmentAs<T>(int slot) where T : Assignment { return Assignments[slot] as T; }
        public override string ToString() { return Name + " (" + FormationName + ")"; }
    }

    public sealed class PlaySet
    {
        public string Name;
        public PlaySide Side;
        public List<Play> Plays = new List<Play>();
        public PlaySet(string name, PlaySide side) { Name = name; Side = side; }
    }

    public sealed class Playbook
    {
        public string Name;
        public List<Formation> Formations = new List<Formation>();
        public List<PlaySet> Sets = new List<PlaySet>();

        public Playbook(string name) { Name = name; }

        public IEnumerable<Play> AllPlays
        {
            get { foreach (PlaySet set in Sets) foreach (Play p in set.Plays) yield return p; }
        }

        public int PlayCount
        {
            get { int n = 0; foreach (PlaySet s in Sets) n += s.Plays.Count; return n; }
        }

        public Formation FindFormation(string name)
        {
            for (int i = 0; i < Formations.Count; i++) if (Formations[i].Name == name) return Formations[i];
            return null;
        }

        public Play FindPlay(string nameOrId)
        {
            foreach (Play p in AllPlays) if (p.Id == nameOrId || p.Name == nameOrId) return p;
            return null;
        }

        public List<Play> PlaysFor(string formationName)
        {
            var list = new List<Play>();
            foreach (Play p in AllPlays) if (p.FormationName == formationName) list.Add(p);
            return list;
        }

        public List<Play> PlaysBySide(PlaySide side)
        {
            var list = new List<Play>();
            foreach (Play p in AllPlays) if (p.Side == side) list.Add(p);
            return list;
        }

        public List<Play> PlaysByCategory(PlayCategory cat)
        {
            var list = new List<Play>();
            foreach (Play p in AllPlays) if (p.Category == cat) list.Add(p);
            return list;
        }

        public List<Formation> FormationsBySide(PlaySide side)
        {
            var list = new List<Formation>();
            foreach (Formation f in Formations) if (f.Side == side) list.Add(f);
            return list;
        }

        public void Merge(Playbook other)
        {
            foreach (Formation f in other.Formations) if (FindFormation(f.Name) == null) Formations.Add(f);
            foreach (PlaySet s in other.Sets) Sets.Add(s);
        }

        /// <summary>Structural validation. Returns true when no errors were found.</summary>
        public bool Validate(List<string> errors)
        {
            int before = errors.Count;
            var ids = new HashSet<string>();
            foreach (Formation f in Formations)
            {
                if (f.Slots == null || f.Slots.Length != Formation.SlotCount)
                    errors.Add("Formation " + f.Name + " must have exactly 11 slots.");
            }
            foreach (Play p in AllPlays)
            {
                string tag = "Play '" + p.Name + "' [" + p.FormationName + "]: ";
                if (!ids.Add(p.Id)) errors.Add(tag + "duplicate id " + p.Id);
                Formation f = FindFormation(p.FormationName);
                if (f == null) { errors.Add(tag + "unknown formation"); continue; }
                if (f.Side != p.Side) errors.Add(tag + "side does not match its formation");
                if (p.Assignments == null || p.Assignments.Length != Formation.SlotCount)
                {
                    errors.Add(tag + "must have exactly 11 assignments");
                    continue;
                }
                for (int i = 0; i < Formation.SlotCount; i++)
                {
                    Assignment a = p.Assignments[i];
                    if (a == null) { errors.Add(tag + "slot " + i + " has no assignment"); continue; }
                    if (a.Slot != i) errors.Add(tag + "slot " + i + " assignment reports slot " + a.Slot);
                    var cov = a as CoverageAssignment;
                    if (cov != null)
                    {
                        if (cov.IsMan && (cov.TargetSlot < 0 || cov.TargetSlot >= Formation.SlotCount))
                            errors.Add(tag + "slot " + i + " man target out of range");
                        if (!cov.IsMan && cov.Zone == null) errors.Add(tag + "slot " + i + " zone missing region");
                    }
                    var spy = a as SpyAssignment;
                    if (spy != null && (spy.TargetSlot < 0 || spy.TargetSlot >= Formation.SlotCount))
                        errors.Add(tag + "slot " + i + " spy target out of range");
                    var route = a as RouteAssignment;
                    if (route != null && route.Waypoints.Count == 0) errors.Add(tag + "slot " + i + " route has no waypoints");
                    var qb = a as QbAssignment;
                    if (qb != null)
                    {
                        foreach (int r in qb.ReadProgression)
                            if (r < 0 || r >= Formation.SlotCount) errors.Add(tag + "QB read slot out of range");
                    }
                    var rush = a as RushAssignment;
                    if (rush != null && rush.StuntPartnerSlot >= Formation.SlotCount)
                        errors.Add(tag + "stunt partner out of range");
                }
                ValidateSideAssignments(p, tag, errors);
            }
            return errors.Count == before;
        }

        private static void ValidateSideAssignments(Play p, string tag, List<string> errors)
        {
            if (p.Side == PlaySide.Offense)
            {
                int qbs = 0;
                foreach (Assignment a in p.Assignments)
                {
                    if (a is CoverageAssignment || a is RushAssignment || a is BlitzAssignment || a is SpyAssignment)
                        errors.Add(tag + "offensive play contains a defensive assignment");
                    if (a is QbAssignment) qbs++;
                }
                if (qbs != 1) errors.Add(tag + "offensive play needs exactly one QB assignment");
            }
            else if (p.Side == PlaySide.Defense)
            {
                foreach (Assignment a in p.Assignments)
                {
                    if (a is RouteAssignment || a is CarryAssignment || a is QbAssignment)
                        errors.Add(tag + "defensive play contains an offensive assignment");
                }
            }
        }
    }
}
