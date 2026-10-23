using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    internal enum DefVariant { None, Cloud, Buzz }

    internal sealed class DefConcept
    {
        public string Name;
        public PlayCategory Cat;
        public CoverageScheme Scheme;
        public string[] Formations;
        public string Blitz = "";
        public RunGap[] Gaps = new RunGap[0];
        public string Drop = "";
        public string Spy = "";
        public string Robber = "";
        public StuntKind Stunt = StuntKind.None;
        public bool Press;
        public bool Deep;
        public DefVariant Variant;
        public ShellLook Shell;
        public string Note = "";
        public bool Match;
    }

    internal sealed class DefBuilder
    {
        private readonly Formation _f;
        private readonly Assignment[] _a = new Assignment[Formation.SlotCount];
        private readonly List<int> _dl = new List<int>(), _lb = new List<int>(), _cb = new List<int>(), _s = new List<int>();

        public DefBuilder(Formation f)
        {
            _f = f;
            for (int i = 0; i < Formation.SlotCount; i++)
            {
                Position p = f.Slots[i].Position;
                if (PositionGroup.IsDefensiveLine(p)) _dl.Add(i);
                else if (PositionGroup.IsLinebacker(p)) _lb.Add(i);
                else if (p == Position.CB) _cb.Add(i);
                else _s.Add(i);
            }
            Comparison<int> byX = (x, y) => f.Slots[x].X.CompareTo(f.Slots[y].X);
            _dl.Sort(byX); _lb.Sort(byX); _cb.Sort(byX); _s.Sort(byX);
        }

        private float X(int slot) { return _f.Slots[slot].X; }
        private bool Free(int slot) { return slot >= 0 && _a[slot] == null; }

        private int Resolve(string tok)
        {
            switch (tok)
            {
                case "MLB": foreach (int i in _lb) if (_f.Slots[i].Position == Position.MLB) return i; return -1;
                case "LB0": return _lb.Count > 0 ? _lb[0] : -1;
                case "LB1": return _lb.Count > 1 ? _lb[1] : -1;
                case "LB2": return _lb.Count > 2 ? _lb[2] : -1;
                case "LB3": return _lb.Count > 3 ? _lb[3] : -1;
                case "CBL": return _cb.Count > 0 ? _cb[0] : -1;
                case "CBR": return _cb.Count > 1 ? _cb[_cb.Count - 1] : -1;
                case "NB": return _cb.Count > 2 ? _cb[1] : -1;
                case "SS": foreach (int i in _s) if (_f.Slots[i].Position == Position.SS) return i; return -1;
                case "FS": foreach (int i in _s) if (_f.Slots[i].Position == Position.FS) return i; return -1;
                case "DEL": return _dl.Count > 0 ? _dl[0] : -1;
                case "DER": return _dl.Count > 1 ? _dl[_dl.Count - 1] : -1;
                case "DT": return _dl.Count > 2 ? _dl[1] : -1;
                default: return -1;
            }
        }

        private List<int> ResolveAll(string toks)
        {
            var list = new List<int>();
            foreach (string t in toks.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int s = Resolve(t);
                if (s >= 0 && !list.Contains(s)) list.Add(s);
            }
            return list;
        }

        private static RunGap[] GapsFor(int n)
        {
            switch (n)
            {
                case 0: return new RunGap[0];
                case 1: return new[] { RunGap.ALeft };
                case 2: return new[] { RunGap.BLeft, RunGap.BRight };
                case 3: return new[] { RunGap.BLeft, RunGap.ALeft, RunGap.BRight };
                case 4: return new[] { RunGap.CLeft, RunGap.BLeft, RunGap.BRight, RunGap.CRight };
                default: return new[] { RunGap.DLeft, RunGap.BLeft, RunGap.ARight, RunGap.BRight, RunGap.DRight };
            }
        }

        private void Zone(int slot, ZoneRegion r, bool press = false)
        {
            _a[slot] = new CoverageAssignment { Slot = slot, IsMan = false, Zone = r, Press = press };
        }

        private void Man(int slot, int target, bool press)
        {
            _a[slot] = new CoverageAssignment { Slot = slot, IsMan = true, TargetSlot = target, Press = press };
        }

        private static ZoneRegion[] UnderPattern(int n)
        {
            switch (n)
            {
                case 1: return new[] { ZoneRegion.HookMiddle() };
                case 2: return new[] { ZoneRegion.HookLeft(), ZoneRegion.HookRight() };
                case 3: return new[] { ZoneRegion.HookLeft(), ZoneRegion.HookMiddle(), ZoneRegion.HookRight() };
                case 4: return new[] { ZoneRegion.FlatLeft(), ZoneRegion.HookLeft(), ZoneRegion.HookRight(), ZoneRegion.FlatRight() };
                default: return new[] { ZoneRegion.FlatLeft(), ZoneRegion.HookLeft(), ZoneRegion.HookMiddle(), ZoneRegion.HookRight(), ZoneRegion.FlatRight() };
            }
        }

        private void AllocUnder(List<int> defenders)
        {
            var distinct = new List<int>();
            foreach (int d in defenders) if (!distinct.Contains(d)) distinct.Add(d);
            defenders = distinct;
            defenders.Sort((p, q) => X(p).CompareTo(X(q)));
            // v0.9.0 (E-194 / E-203): the flats belong to the defensive backs (a nickel, a strong safety, a corner who is not deep) and the linebackers play hook / curl. The old pattern
            // gave the leftmost defender the left flat (17 yd from the middle linebacker's side), so a 3-4 outside backer had to cross 11 yd in 2 s.
            var backs = new List<int>(); var backers = new List<int>();
            foreach (int d in defenders) { if (PositionGroup.IsDefensiveBack(_f.Slots[d].Position)) backs.Add(d); else backers.Add(d); }
            bool flatL = false, flatR = false;
            foreach (int b in backs)
            {
                bool left = X(b) < 0f;
                if (left && !flatL) { Zone(b, ZoneRegion.FlatLeft()); flatL = true; }
                else if (!left && !flatR) { Zone(b, ZoneRegion.FlatRight()); flatR = true; }
                else if (!flatL) { Zone(b, ZoneRegion.FlatLeft()); flatL = true; }
                else if (!flatR) { Zone(b, ZoneRegion.FlatRight()); flatR = true; }
                else backers.Add(b);
            }
            backers.Sort((p, q) => X(p).CompareTo(X(q)));
            int n = backers.Count;
            // a side without a flat player is covered by the curl of the linebacker on it (depth 11); the middle of the field belongs to the hook zones
            ZoneRegion[] pat;
            switch (n)
            {
                case 0: pat = new ZoneRegion[0]; break;
                case 1: pat = new[] { ZoneRegion.HookMiddle() }; break;
                case 2: pat = new[] { flatL && !flatR ? ZoneRegion.HookMiddle() : ZoneRegion.HookLeft(), flatR && !flatL ? ZoneRegion.HookMiddle() : ZoneRegion.HookRight() }; if (!flatL && !flatR) pat = new[] { ZoneRegion.CurlLeft(), ZoneRegion.CurlRight() }; break;
                case 3: pat = new[] { flatL ? ZoneRegion.HookLeft() : ZoneRegion.CurlLeft(), ZoneRegion.HookMiddle(), flatR ? ZoneRegion.HookRight() : ZoneRegion.CurlRight() }; break;
                default: pat = new[] { ZoneRegion.CurlLeft(), ZoneRegion.HookLeft(), ZoneRegion.HookRight(), ZoneRegion.CurlRight() }; break;
            }
            for (int i = 0; i < n; i++) Zone(backers[i], i < pat.Length ? pat[i] : ZoneRegion.HookMiddle());
        }

        private int[] Prefs(int slot)
        {
            Position p = _f.Slots[slot].Position;
            if (p == Position.CB)
            {
                int idx = _cb.IndexOf(slot);
                if (idx == 0) return new[] { 8, 10, 7, 1, 9 };
                if (idx == _cb.Count - 1) return new[] { 9, 10, 7, 1, 8 };
                return new[] { 10, 7, 1, 8, 9 };
            }
            if (p == Position.SS) return new[] { 7, 10, 1, 8, 9 };
            if (p == Position.FS) return new[] { 10, 1, 7, 8, 9 };
            if (p == Position.MLB) return new[] { 1, 7, 10, 8, 9 };
            return new[] { 7, 1, 10, 8, 9 };
        }

        private List<int> FreeInPriority(bool includeFs)
        {
            var order = new List<int>();
            if (_cb.Count > 0) order.Add(_cb[0]);
            if (_cb.Count > 1) order.Add(_cb[_cb.Count - 1]);
            for (int i = 1; i < _cb.Count - 1; i++) order.Add(_cb[i]);
            foreach (int s in _s) if (_f.Slots[s].Position == Position.SS) order.Add(s);
            if (includeFs) foreach (int s in _s) if (_f.Slots[s].Position == Position.FS) order.Add(s);
            order.AddRange(_lb);
            order.RemoveAll(s => !Free(s));
            return order;
        }

        private void AllocMan(List<int> defenders, bool press)
        {
            var used = new HashSet<int>();
            var leftover = new List<int>();
            foreach (int d in defenders)
            {
                int target = -1;
                foreach (int t in Prefs(d)) if (!used.Contains(t)) { target = t; break; }
                if (target < 0) { leftover.Add(d); continue; }
                used.Add(target);
                bool isCb = _f.Slots[d].Position == Position.CB;
                Man(d, target, press && isCb);
            }
            if (leftover.Count > 0) AllocUnder(leftover);
        }

        private int FirstFree(params int[] cands)
        {
            foreach (int c in cands) if (Free(c)) return c;
            return -1;
        }

        public Play Build(DefConcept c)
        {
            List<int> blitzers = ResolveAll(c.Blitz);
            List<int> drops = ResolveAll(c.Drop);
            List<int> spies = ResolveAll(c.Spy);
            List<int> robbers = ResolveAll(c.Robber);
            foreach (int s in spies) _a[s] = new SpyAssignment { Slot = s, TargetSlot = 0 };

            // Defensive line
            var rushing = new List<int>();
            foreach (int d in _dl) if (!blitzers.Contains(d) && !drops.Contains(d) && Free(d)) rushing.Add(d);
            RunGap[] gaps = GapsFor(rushing.Count);
            for (int i = 0; i < rushing.Count; i++)
                _a[rushing[i]] = new RushAssignment { Slot = rushing[i], Gap = gaps[i], Contain = i == 0 || i == rushing.Count - 1 };
            if (c.Stunt == StuntKind.Pinch && rushing.Count >= 2)
            {
                // QA-532: a pinch squeezes the ends inside (B gaps); the old code swapped gaps like a twist
                ((RushAssignment)_a[rushing[0]]).Gap = RunGap.BLeft;
                ((RushAssignment)_a[rushing[rushing.Count - 1]]).Gap = RunGap.BRight;
                if (rushing.Count == 4) { ((RushAssignment)_a[rushing[1]]).Gap = RunGap.ALeft; ((RushAssignment)_a[rushing[2]]).Gap = RunGap.ARight; }
            }
            else if (c.Stunt != StuntKind.None && rushing.Count >= 2)
            {
                int a = rushing.Count >= 3 ? rushing[1] : rushing[0];
                int b = rushing.Count >= 3 ? rushing[2] : rushing[1];
                if (c.Stunt == StuntKind.Loop) { a = rushing[0]; b = rushing[1]; }
                var ra = (RushAssignment)_a[a]; var rb = (RushAssignment)_a[b];
                RunGap tmp = ra.Gap; ra.Gap = rb.Gap; rb.Gap = tmp;
                ra.Stunt = rb.Stunt = c.Stunt;
                ra.StuntPartnerSlot = b; rb.StuntPartnerSlot = a;
            }

            // Blitzers
            int gi = 0;
            foreach (int bl in blitzers)
            {
                if (!Free(bl)) continue;
                bool db = PositionGroup.IsDefensiveBack(_f.Slots[bl].Position);
                RunGap gap = gi < c.Gaps.Length ? c.Gaps[gi] : (X(bl) < 0 ? (db ? RunGap.DLeft : RunGap.ALeft) : (db ? RunGap.DRight : RunGap.ARight));
                gi++;
                _a[bl] = new BlitzAssignment { Slot = bl, Gap = gap, Edge = db || gap == RunGap.CLeft || gap == RunGap.CRight || gap == RunGap.DLeft || gap == RunGap.DRight, Delay = db ? 0.35f : 0.18f + 0.07f * (gi % 3) };   // v0.9.0 (E-358): a late bring (linebackers 0.18-0.32 s, defensive backs 0.35 s), not from the snap
            }

            foreach (int r in robbers) if (Free(r)) Zone(r, ZoneRegion.Robber());

            // Coverage
            var under = new List<int>();
            foreach (int d in drops) if (Free(d)) under.Add(d);
            float depth = c.Deep ? 20f : 14f;
            bool goalLine = _f.Unit == "4-4-3";
            if (goalLine) depth = 6f;   // QA-532: a goal-line safety plays the end zone, not 14 yards behind it
            switch (c.Scheme)
            {
                case CoverageScheme.Cover0:
                    AllocMan(FreeInPriority(true), false);
                    break;
                case CoverageScheme.Cover1:
                {
                    int fs = FirstFree(Resolve("FS"), Resolve("SS"));
                    if (fs >= 0) Zone(fs, goalLine ? ZoneRegion.DeepMiddle(depth) : ZoneRegion.DeepMiddle());
                    AllocMan(FreeInPriority(false), c.Press);
                    break;
                }
                case CoverageScheme.Cover2Man:
                {
                    DeepHalves(depth);
                    AllocMan(FreeInPriority(false), c.Press);
                    break;
                }
                case CoverageScheme.Cover2:
                case CoverageScheme.Tampa2:
                {
                    DeepHalves(depth);
                    if (c.Scheme == CoverageScheme.Tampa2)
                    {
                        int mike = Resolve("MLB");
                        if (Free(mike)) Zone(mike, ZoneRegion.DeepMiddle(18f));
                    }
                    if (_cb.Count > 0 && Free(_cb[0])) Zone(_cb[0], ZoneRegion.FlatLeft(), c.Press);
                    if (_cb.Count > 1 && Free(_cb[_cb.Count - 1])) Zone(_cb[_cb.Count - 1], ZoneRegion.FlatRight(), c.Press);
                    foreach (int s in Others()) under.Add(s);
                    AllocUnder(under);
                    break;
                }
                case CoverageScheme.Cover3:
                {
                    int left = FirstFree(Resolve("CBL"), _s.Count > 0 ? _s[0] : -1);
                    if (left >= 0) { Zone(left, ZoneRegion.DeepThirdLeft(depth)); if (c.Match) ((CoverageAssignment)_a[left]).Match = true; }
                    int right = FirstFree(Resolve("CBR"), _s.Count > 0 ? _s[_s.Count - 1] : -1);
                    if (right >= 0) { Zone(right, ZoneRegion.DeepThirdRight(depth)); if (c.Match) ((CoverageAssignment)_a[right]).Match = true; }
                    int mid = FirstFree(Resolve("FS"), Resolve("SS"));
                    if (mid >= 0) Zone(mid, ZoneRegion.DeepThirdMiddle(depth));
                    if (c.Variant == DefVariant.Cloud)
                    {
                        int cr = Resolve("CBR");
                        // Cloud: the corner takes the flat and the strong safety takes his deep third.
                        if (cr >= 0 && _a[cr] is CoverageAssignment) Zone(cr, ZoneRegion.FlatRight());
                        int strong = Resolve("SS");
                        if (Free(strong)) Zone(strong, ZoneRegion.DeepThirdRight(depth));
                    }
                    else if (c.Variant == DefVariant.Buzz)
                    {
                        int ss = Resolve("SS");
                        if (Free(ss)) Zone(ss, X(ss) < 0f ? ZoneRegion.BuzzLeft() : ZoneRegion.BuzzRight());   // QA-546: the buzz safety drops to the side he lines up on
                    }
                    foreach (int s in Others()) under.Add(s);
                    AllocUnder(under);
                    break;
                }
                case CoverageScheme.Cover4:
                case CoverageScheme.Prevent:
                {
                    float qd = c.Scheme == CoverageScheme.Prevent ? 22f : 13f;
                    if (_cb.Count > 0 && Free(_cb[0])) Zone(_cb[0], ZoneRegion.Quarter(0, qd));
                    if (_cb.Count > 1 && Free(_cb[_cb.Count - 1])) Zone(_cb[_cb.Count - 1], ZoneRegion.Quarter(3, qd));
                    var safeties = new List<int>();
                    foreach (int s in _s) if (Free(s)) safeties.Add(s);
                    if (safeties.Count >= 2)
                    {
                        Zone(safeties[0], ZoneRegion.Quarter(1, qd));
                        Zone(safeties[safeties.Count - 1], ZoneRegion.Quarter(2, qd));
                    }
                    else if (safeties.Count == 1) Zone(safeties[0], ZoneRegion.DeepMiddle(qd));
                    foreach (int s in Others()) under.Add(s);
                    AllocUnder(under);
                    break;
                }
                case CoverageScheme.Cover6:
                {
                    int cbl = Resolve("CBL"), cbr = Resolve("CBR");
                    var safeties = new List<int>();
                    foreach (int s in _s) if (Free(s)) safeties.Add(s);
                    if (Free(cbr)) Zone(cbr, ZoneRegion.Quarter(3));
                    if (safeties.Count > 0) Zone(safeties[safeties.Count - 1], ZoneRegion.Quarter(2));
                    if (safeties.Count > 1) Zone(safeties[0], ZoneRegion.DeepHalfLeft());
                    if (Free(cbl)) Zone(cbl, ZoneRegion.FlatLeft(), true);
                    foreach (int s in Others()) under.Add(s);
                    AllocUnder(under);
                    break;
                }
                default:
                    foreach (int s in Others()) under.Add(s);
                    AllocUnder(under);
                    break;
            }

            // Safety net
            for (int i = 0; i < Formation.SlotCount; i++)
                if (_a[i] == null) Zone(i, ZoneRegion.HookMiddle());

            return new Play
            {
                Id = DefaultPlaybooks.Slug(_f.Name + "." + c.Name),
                Name = c.Name,
                FormationName = _f.Name,
                Side = PlaySide.Defense,
                Category = c.Cat,
                Scheme = c.Scheme,
                Assignments = _a,
                Tags = new[] { "defense" },
                Shell = c.Shell,
                Note = c.Note
            };
        }

        private void DeepHalves(float depth)
        {
            var safeties = new List<int>();
            foreach (int s in _s) if (Free(s)) safeties.Add(s);
            if (safeties.Count >= 2)
            {
                Zone(safeties[0], ZoneRegion.DeepHalfLeft(depth));
                Zone(safeties[safeties.Count - 1], ZoneRegion.DeepHalfRight(depth));
            }
            else if (safeties.Count == 1) Zone(safeties[0], ZoneRegion.DeepMiddle(depth));
        }

        /// <summary>All unassigned non-lineman, non-corner-outside defenders that should drop into underneath zones.</summary>
        private List<int> Others()
        {
            var list = new List<int>();
            foreach (int s in _lb) if (Free(s)) list.Add(s);
            foreach (int s in _s) if (Free(s)) list.Add(s);
            for (int i = 1; i < _cb.Count - 1; i++) if (Free(_cb[i])) list.Add(_cb[i]);
            if (_cb.Count > 0 && Free(_cb[0])) list.Add(_cb[0]);
            if (_cb.Count > 1 && Free(_cb[_cb.Count - 1])) list.Add(_cb[_cb.Count - 1]);
            return list;
        }
    }

    public static partial class DefaultPlaybooks
    {
        public const string Base43 = "4-3 Base", Base34 = "3-4 Base", Nickel = "Nickel", Dime = "Dime", GoalLineD = "Goal Line Defense",
            Over43 = "4-3 Over", Nickel335 = "3-3-5", Bear46 = "46 Bear";

        /// <summary>
        /// The defensive formations. Alignments (yards from the ball): a line spot at 1.45 yd depth is a hand off the offensive line; the actual spots of the down linemen are set at the snap from the offense's real line by technique (0 / 1 / 2 / 3 / 4 / 5 / 7 / 9, see
        /// <c>DefenseAlignment.AlignLine</c> and <see cref="Formation.Front"/>); linebackers stand 4.5 yd deep (2-3 when they are walked up), corners 7 (press 1), safeties 9-12.
        /// </summary>
        public static List<Formation> DefensiveFormations()
        {
            Func<Position, float, float, SlotSpec> d = (p, x, y) => new SlotSpec(p, x, y, "");
            var list = new List<Formation>();
            list.Add(new Formation(Base43, PlaySide.Defense, "4-3", new[] {
                d(Position.LE, -3.75f, 1.45f), d(Position.DT, -1.25f, 1.45f), d(Position.DT, 1.25f, 1.45f), d(Position.RE, 3.75f, 1.45f),
                d(Position.LOLB, -6f, 4.5f), d(Position.MLB, 0f, 4.5f), d(Position.ROLB, 6f, 4.5f),
                d(Position.CB, -17f, 7f), d(Position.CB, 17f, 7f), d(Position.FS, -5f, 12f), d(Position.SS, 7f, 9f) }) { Front = "Under" });
            list.Add(new Formation(Base34, PlaySide.Defense, "3-4", new[] {
                d(Position.LE, -2.5f, 1.45f), d(Position.DT, 0f, 1.45f), d(Position.RE, 2.5f, 1.45f),
                d(Position.LOLB, -6f, 2f), d(Position.MLB, -2f, 4.5f), d(Position.MLB, 2f, 4.5f), d(Position.ROLB, 6f, 2f),
                d(Position.CB, -17f, 7f), d(Position.CB, 17f, 7f), d(Position.FS, -5f, 12f), d(Position.SS, 7f, 9f) }) { Front = "Odd" });
            list.Add(new Formation(Nickel, PlaySide.Defense, "4-2-5", new[] {
                d(Position.LE, -3.75f, 1.45f), d(Position.DT, -1.25f, 1.45f), d(Position.DT, 1.25f, 1.45f), d(Position.RE, 3.75f, 1.45f),
                d(Position.LOLB, -3.5f, 4.5f), d(Position.MLB, 3.5f, 4.5f),
                d(Position.CB, -17f, 7f), d(Position.CB, -9f, 5f), d(Position.CB, 17f, 7f), d(Position.FS, -5f, 12f), d(Position.SS, 7f, 9f) }) { Front = "Under" });
            list.Add(new Formation(Dime, PlaySide.Defense, "4-1-6", new[] {
                d(Position.LE, -3.75f, 1.45f), d(Position.DT, -1.25f, 1.45f), d(Position.DT, 1.25f, 1.45f), d(Position.RE, 3.75f, 1.45f),
                d(Position.MLB, 0f, 4.5f),
                d(Position.CB, -17f, 7f), d(Position.CB, -9f, 5f), d(Position.CB, 9f, 5f), d(Position.CB, 17f, 7f),
                d(Position.FS, -5f, 12f), d(Position.SS, 5f, 12f) }) { Front = "Under" });
            list.Add(new Formation(GoalLineD, PlaySide.Defense, "4-4-3", new[] {
                d(Position.LE, -3.75f, 1.45f), d(Position.DT, -1.25f, 1.45f), d(Position.DT, 1.25f, 1.45f), d(Position.RE, 3.75f, 1.45f),
                d(Position.LOLB, -6f, 2.5f), d(Position.MLB, -2f, 3.2f), d(Position.MLB, 2f, 3.2f), d(Position.ROLB, 6f, 2.5f),
                d(Position.CB, -14f, 3f), d(Position.CB, 14f, 3f), d(Position.FS, 0f, 6f) }) { Front = "Goal" });
            // ---- v0.10.0 fronts ----
            // 4-3 Over: the strong-side tackle is a 3-technique, the weak-side tackle a 1-technique, the strong-side linebacker walks up on the tight end
            list.Add(new Formation(Over43, PlaySide.Defense, "4-3", new[] {
                d(Position.LE, -3.75f, 1.45f), d(Position.DT, -1.25f, 1.45f), d(Position.DT, 1.25f, 1.45f), d(Position.RE, 3.75f, 1.45f),
                d(Position.LOLB, -4.5f, 4.5f), d(Position.MLB, 0.5f, 4.5f), d(Position.ROLB, 6.5f, 2.6f),
                d(Position.CB, -17f, 7f), d(Position.CB, 17f, 7f), d(Position.FS, -4f, 12f), d(Position.SS, 8f, 9f) }) { Front = "Over" });
            // 3-3-5: three down linemen (5 - 0 - 5), three linebackers, five defensive backs
            list.Add(new Formation(Nickel335, PlaySide.Defense, "3-3-5", new[] {
                d(Position.LE, -2.5f, 1.45f), d(Position.DT, 0f, 1.45f), d(Position.RE, 2.5f, 1.45f),
                d(Position.LOLB, -5f, 4.5f), d(Position.MLB, 0f, 4.5f), d(Position.ROLB, 5f, 4.5f),
                d(Position.CB, -17f, 7f), d(Position.CB, -9f, 5f), d(Position.CB, 17f, 7f), d(Position.FS, -5f, 12f), d(Position.SS, 7f, 9f) }) { Front = "Even3" });
            // 46 Bear: four linemen with the tackles head-up on the guards and eight men in the box (the strong safety walks down onto the tight end)
            list.Add(new Formation(Bear46, PlaySide.Defense, "4-6", new[] {
                d(Position.LE, -3.75f, 1.45f), d(Position.DT, -1.25f, 1.45f), d(Position.DT, 1.25f, 1.45f), d(Position.RE, 3.75f, 1.45f),
                d(Position.LOLB, -5f, 3.4f), d(Position.MLB, -0.7f, 4.0f), d(Position.ROLB, 3.5f, 3.2f),
                d(Position.CB, -17f, 7f), d(Position.CB, 17f, 7f), d(Position.FS, 0f, 11.5f), d(Position.SS, 7f, 2.0f) }) { Front = "Bear" });
            return list;
        }

        /// <summary>The shell a defensive play shows before the snap: the disguise the play names, or the look its coverage naturally has.</summary>
        public static ShellLook EffectiveShell(Play p)
        {
            if (p == null) return ShellLook.Auto;
            if (p.Shell != ShellLook.Auto) return p.Shell;
            switch (p.Scheme)
            {
                case CoverageScheme.Cover0: return ShellLook.Zero;
                case CoverageScheme.Cover1: case CoverageScheme.Cover3: return ShellLook.OneHigh;
                case CoverageScheme.None: return ShellLook.Auto;
                default: return ShellLook.TwoHigh;   // Cover 2, 2-man, Tampa 2, Cover 4, Cover 6, prevent
            }
        }

        private static DefConcept Def(string name, PlayCategory cat, CoverageScheme scheme, string[] formations, string blitz = "",
            RunGap[] gaps = null, string drop = "", string spy = "", string robber = "", StuntKind stunt = StuntKind.None,
            bool press = false, bool deep = false, DefVariant variant = DefVariant.None, ShellLook shell = ShellLook.Auto, string note = "", bool match = false)
        {
            return new DefConcept
            {
                Name = name, Cat = cat, Scheme = scheme, Formations = formations, Blitz = blitz,
                Gaps = gaps ?? new RunGap[0], Drop = drop, Spy = spy, Robber = robber, Stunt = stunt,
                Press = press, Deep = deep, Variant = variant, Shell = shell, Note = note, Match = match
            };
        }

        public static void AddDefense(Playbook pb)
        {
            foreach (Formation f in DefensiveFormations()) if (pb.FindFormation(f.Name) == null) pb.Formations.Add(f);
            var concepts = new List<DefConcept>();
            var Z = PlayCategory.ZoneCoverage; var M = PlayCategory.ManCoverage; var B = PlayCategory.Blitz; var G = PlayCategory.GoalLine;
            var T = ShellLook.TwoHigh; var O = ShellLook.OneHigh;
            concepts.Add(Def("Cover 0 Blitz", B, CoverageScheme.Cover0, new[] { Base43 }, "MLB LB2", new[] { RunGap.ALeft, RunGap.BRight }, note: "No deep help: everybody in man, two linebackers come"));
            concepts.Add(Def("Cover 0 Blitz", B, CoverageScheme.Cover0, new[] { Nickel }, "MLB LB0", new[] { RunGap.BRight, RunGap.BLeft }, note: "No deep help: everybody in man, two linebackers come"));   // QA-531: the nickel has only two linebackers (there is no LB2)
            concepts.Add(Def("3-4 Cover 0 Blitz", B, CoverageScheme.Cover0, new[] { Base34 }, "LB1 LB2 LB0", new[] { RunGap.ALeft, RunGap.ARight, RunGap.CLeft }));
            concepts.Add(Def("Cover 0 Double A-Gap", B, CoverageScheme.Cover0, new[] { Base43, Nickel }, "MLB LB0", new[] { RunGap.ARight, RunGap.ALeft }, note: "Two linebackers show in the A gaps and both come"));
            concepts.Add(Def("Cover 1 Robber", M, CoverageScheme.Cover1, new[] { Base43, Base34, Nickel }, robber: "MLB", shell: T, note: "Looks like two-high, then one safety rotates to the middle and the Mike robs the crossers"));
            concepts.Add(Def("Cover 1 Man Free", M, CoverageScheme.Cover1, new[] { Base43, Base34, Nickel, Dime, Nickel335, Bear46 }, note: "Man to man with a free safety in the middle"));
            concepts.Add(Def("Cover 1 Spy", M, CoverageScheme.Cover1, new[] { Nickel }, spy: "MLB", note: "Man to man, the Mike mirrors the quarterback"));
            concepts.Add(Def("Cover 2 Zone", Z, CoverageScheme.Cover2, new[] { Base43, Base34, Nickel, Nickel335 }, note: "Corners sit in the flats, two safeties split the deep field in halves"));
            concepts.Add(Def("Cover 2 Press", Z, CoverageScheme.Cover2, new[] { Nickel, Base43 }, press: true, note: "Corners jam at the line, then drop to the flats"));
            concepts.Add(Def("Cover 2 Man Under", M, CoverageScheme.Cover2Man, new[] { Base43, Nickel, Dime }, note: "Man underneath, two safeties over the top"));
            concepts.Add(Def("3-4 Cover 2 Man", M, CoverageScheme.Cover2Man, new[] { Base34 }, press: true));
            concepts.Add(Def("Tampa 2", Z, CoverageScheme.Tampa2, new[] { Base43 }, note: "Cover 2 where the Mike runs the deep middle"));
            concepts.Add(Def("Cover 3 Sky", Z, CoverageScheme.Cover3, new[] { Base43, Base34, Nickel, Nickel335, Bear46 }, note: "Three deep: the corners and the free safety take the thirds, the strong safety rolls down to the flat"));
            concepts.Add(Def("Cover 3 Cloud", Z, CoverageScheme.Cover3, new[] { Base43, Nickel }, variant: DefVariant.Cloud, shell: T, note: "The corner takes the flat, the strong safety takes his deep third"));
            concepts.Add(Def("Cover 3 Match", Z, CoverageScheme.Cover3, new[] { Nickel, Base43, Nickel335 }, shell: T, match: true, note: "Three deep from a quarters look: the corners match the outside receivers who go vertical and the safety plays the middle"));
            concepts.Add(Def("Cover 3 Buzz", Z, CoverageScheme.Cover3, new[] { Nickel, Base43 }, variant: DefVariant.Buzz, shell: T, note: "The strong safety buzzes down to the hook zone"));
            concepts.Add(Def("Cover 4 Quarters", Z, CoverageScheme.Cover4, new[] { Base43, Base34, Nickel, Dime, Nickel335 }, note: "Four deep quarters: the corner and the safety on each side match the receivers who go vertical"));   // QA-504: the 3-4 gets the shells and stunts the 4-3 has
            concepts.Add(Def("Cover 4 Match", Z, CoverageScheme.Cover4, new[] { Nickel }, press: true, shell: O, note: "Quarters from a one-high look, the corners press and then match"));
            concepts.Add(Def("Cover 6", Z, CoverageScheme.Cover6, new[] { Nickel, Base43, Base34, Nickel335 }, note: "Quarters to the strong side, a half to the weak side"));
            concepts.Add(Def("Cover 3 Fire Zone", B, CoverageScheme.Cover3, new[] { Base34 }, "LB0 LB3 LB1", drop: "DER", shell: T, note: "Five rush, three deep, the end drops into the flat"));   // QA-618: five rush, three deep, the right end drops
            concepts.Add(Def("Cover 3 Fire Zone", B, CoverageScheme.Cover3, new[] { Base43 }, "LB0 LB2", drop: "DER", shell: T, note: "Five rush, three deep, the end drops into the flat"));
            concepts.Add(Def("Zone Blitz Dog", B, CoverageScheme.Cover3, new[] { Base34 }, "LB0 LB1 LB3", new[] { RunGap.CLeft, RunGap.ALeft, RunGap.CRight }, drop: "DT", shell: T, note: "Three linebackers come, the nose drops to the hook"));   // QA-617: each blitzer has his own gap
            concepts.Add(Def("Corner Blitz Cover 1", B, CoverageScheme.Cover1, new[] { Nickel, Base43 }, "CBR", new[] { RunGap.DRight }, note: "The corner comes off the edge, the safety is in the middle"));
            concepts.Add(Def("Safety Blitz Cover 0", B, CoverageScheme.Cover0, new[] { Base43, Nickel, Bear46 }, "SS", new[] { RunGap.CRight }, shell: T, note: "Looks two-high, the strong safety comes off the edge and nobody is deep"));
            concepts.Add(Def("Mike Blitz Cover 1", B, CoverageScheme.Cover1, new[] { Base43 }, "MLB", new[] { RunGap.ARight }, note: "The Mike comes up the middle"));
            concepts.Add(Def("LB Cross Blitz Cover 1", B, CoverageScheme.Cover1, new[] { Base34 }, "LB1 LB2", new[] { RunGap.ARight, RunGap.BLeft }));   // QA-529: the two inside backers swap sides, so the blitz actually crosses
            concepts.Add(Def("Sam Blitz Cover 3", B, CoverageScheme.Cover3, new[] { Base43 }, "LB2", new[] { RunGap.CRight }));   // QA-530: Sam is the strong-side (right) backer
            concepts.Add(Def("Will Blitz Cover 3", B, CoverageScheme.Cover3, new[] { Base43 }, "LB0", new[] { RunGap.CLeft }));
            concepts.Add(Def("Spy QB Cover 3", Z, CoverageScheme.Cover3, new[] { Base43, Base34, Nickel }, spy: "MLB", shell: T, note: "Zone with the Mike glued to the quarterback"));
            concepts.Add(Def("Prevent Shell", PlayCategory.Prevent, CoverageScheme.Prevent, new[] { Dime, Nickel }, deep: true, note: "Everybody deep, nothing over the top"));
            concepts.Add(Def("Dime Cover 1 Blitz", B, CoverageScheme.Cover1, new[] { Dime }, "MLB", new[] { RunGap.ARight }));
            concepts.Add(Def("Dime Quarters Spy", Z, CoverageScheme.Cover4, new[] { Dime }, spy: "MLB"));
            concepts.Add(Def("Nickel Fire Zone", B, CoverageScheme.Cover3, new[] { Nickel, Nickel335 }, "NB MLB", new[] { RunGap.DLeft, RunGap.ARight }, shell: T));
            concepts.Add(Def("Twist Cover 3", Z, CoverageScheme.Cover3, new[] { Base43, Base34, Nickel }, stunt: StuntKind.Twist, shell: T, note: "Two linemen twist, three deep behind it"));
            concepts.Add(Def("Loop Stunt Cover 2", Z, CoverageScheme.Cover2, new[] { Base43, Nickel }, stunt: StuntKind.Loop));
            concepts.Add(Def("Double Edge Blitz Cover 2", B, CoverageScheme.Cover2, new[] { Nickel }, "CBL CBR", new[] { RunGap.DLeft, RunGap.DRight }));
            concepts.Add(Def("Goal Line Cover 0", G, CoverageScheme.Cover0, new[] { GoalLineD }, "LB0"));
            concepts.Add(Def("Goal Line Gap Control", G, CoverageScheme.Cover1, new[] { GoalLineD }));
            concepts.Add(Def("Goal Line Plug Cover 2", G, CoverageScheme.Cover2, new[] { GoalLineD }, stunt: StuntKind.Pinch));
            // the 4-3 Over runs every 4-3 call (same personnel, a different front)
            foreach (DefConcept c in concepts)
                if (Array.IndexOf(c.Formations, Base43) >= 0 && Array.IndexOf(c.Formations, Over43) < 0)
                { var fl = new List<string>(c.Formations); fl.Add(Over43); c.Formations = fl.ToArray(); }

            foreach (DefConcept c in concepts)
            {
                foreach (string fn in c.Formations)
                {
                    Formation f = pb.FindFormation(fn);
                    AddPlay(pb, new DefBuilder(f).Build(c));
                }
            }
        }
    }
}

namespace PixFootball.Core
{
    /// <summary>The pre-snap spots of the safeties in each shell (yards from the ball). One source for the sim's alignment (<c>DefenseAlignment.ApplyShell</c>) and the play diagram.</summary>
    public static class ShellLayout
    {
        public const float TwoHighDepth = 10.5f, TwoHighX = 9f, OneHighDepth = 12.5f, BoxDepth = 7f, BoxX = 5.5f, ZeroDepth = 5.5f, ZeroX = 4.5f;

        /// <summary>
        /// Where the safeties of a defensive play stand before the snap, by formation slot, for the shell the play shows. Safeties who blitz or spy keep their formation spot.
        /// <paramref name="strength"/> is the side (+1 / -1) the strong safety walks down to in a one-high shell.
        /// </summary>
        public static Dictionary<int, Vec2> Spots(Play play, Formation f, int strength)
        {
            var spots = new Dictionary<int, Vec2>();
            if (play == null || f == null || play.Side != PlaySide.Defense) return spots;
            ShellLook shell = DefaultPlaybooks.EffectiveShell(play);
            if (shell == ShellLook.Auto) return spots;
            var s = new List<int>();
            for (int i = 0; i < f.Slots.Length; i++)
            {
                if (!PositionGroup.IsSafety(f.Slots[i].Position)) continue;
                Assignment a = play.Assignments[i];
                if (a is BlitzAssignment || a is RushAssignment || a is SpyAssignment) continue;
                s.Add(i);
            }
            if (s.Count == 0) return spots;
            s.Sort((p, q) => f.Slots[p].X.CompareTo(f.Slots[q].X));
            if (strength == 0) strength = 1;
            switch (shell)
            {
                case ShellLook.TwoHigh:
                    if (s.Count >= 2) { spots[s[0]] = new Vec2(-TwoHighX, TwoHighDepth); spots[s[s.Count - 1]] = new Vec2(TwoHighX, TwoHighDepth); }
                    else spots[s[0]] = new Vec2(0f, TwoHighDepth + 1f);
                    break;
                case ShellLook.OneHigh:
                {
                    int deep = -1;
                    foreach (int i in s) { var c = play.Assignments[i] as CoverageAssignment; if (c != null && !c.IsMan && c.Zone != null && (c.Zone.Kind == ZoneKind.DeepMiddle || c.Zone.Kind == ZoneKind.DeepThird && Math.Abs(c.Zone.CenterX) < 1f)) { deep = i; break; } }
                    if (deep < 0) foreach (int i in s) if (f.Slots[i].Position == Position.FS) { deep = i; break; }
                    if (deep < 0) deep = s[s.Count - 1];
                    spots[deep] = new Vec2(0f, OneHighDepth);
                    foreach (int i in s) if (i != deep) spots[i] = new Vec2(strength * BoxX, BoxDepth);
                    break;
                }
                case ShellLook.Zero:
                    for (int k = 0; k < s.Count; k++) spots[s[k]] = new Vec2(s.Count == 1 ? 0f : (k == 0 ? -ZeroX : ZeroX), ZeroDepth);
                    break;
            }
            return spots;
        }
    }
}
