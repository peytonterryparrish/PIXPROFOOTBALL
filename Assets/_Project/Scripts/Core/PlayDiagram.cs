using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>What a stroke of the play-art diagram means (and which colour the renderer gives it).</summary>
    public enum InkKind { Route, PrimaryRoute, HotRoute, Run, Fake, Block, Handoff, Dropback, Blitz, Rush, Man, Spy, Ghost, Kick }

    public sealed class DiagramStroke
    {
        public InkKind Ink;
        /// <summary>Points in yards relative to the ball (X lateral, Y downfield).</summary>
        public List<Vec2> Points = new List<Vec2>();
        public bool Dashed, Arrow, TEnd, Thick;
        public int Slot = -1;
        /// <summary>True when <see cref="PlayDiagram.ClampToViewport"/> pulled at least one point back to the card edge (the route really runs further; QA-549).</summary>
        public bool Clipped;
    }

    public sealed class DiagramZone
    {
        public ZoneKind Kind;
        public float CenterX, CenterY, Width, Height;
        public int Slot;
        public bool Deep;
    }

    public sealed class DiagramPlayer
    {
        public int Slot;
        public Position Position;
        public string Label;
        public float X, Y;
        public bool Defense;
        /// <summary>Faint stand-in for an offensive player drawn behind a defensive play (man coverage targets).</summary>
        public bool Ghost;
        public bool Highlight;
    }

    /// <summary>
    /// Geometry of one play's diagram (routes as lines with arrows, blocks as T's, run paths, QB action, defensive zone bubbles,
    /// blitz arrows), independent of any renderer. The UI draws it with a mesh; the editor atlas tool draws it into a PNG.
    /// </summary>
    public sealed class PlayDiagram
    {
        public readonly List<DiagramPlayer> Players = new List<DiagramPlayer>();
        public readonly List<DiagramStroke> Strokes = new List<DiagramStroke>();
        public readonly List<DiagramZone> Zones = new List<DiagramZone>();
        public PlaySide Side;
        public string Title = "";
        /// <summary>Suggested viewport (min x, min y, max x, max y) in yards.</summary>
        public float MinX = -24f, MinY = -9f, MaxX = 24f, MaxY = 22f;

        // ================================================================================================ builders
        public static PlayDiagram ForFormation(Formation f)
        {
            var d = new PlayDiagram { Side = f.Side, Title = f.Name };
            d.ApplyViewport(f.Side);
            for (int i = 0; i < f.Slots.Length; i++) d.AddPlayer(f, i, f.Side == PlaySide.Defense);
            d.SeparateChips();
            return d;
        }

        public static PlayDiagram For(Playbook book, Play play)
        {
            return For(book, play, null);
        }

        /// <summary>
        /// The diagram of <paramref name="play"/>. <paramref name="offense"/> is the offensive formation a defensive play is drawn against (man coverage
        /// lines point at ghosts of its receivers, QA-548); null uses the playbook's Singleback look.
        /// </summary>
        public static PlayDiagram For(Playbook book, Play play, Formation offense)
        {
            Formation f = PlayEditing.FormationFor(book, play);
            if (offense == null && play.Side == PlaySide.Defense) offense = book.FindFormation(DefaultPlaybooks.Singleback);
            return For(play, f, offense);
        }

        public static PlayDiagram For(Play play, Formation f)
        {
            return For(play, f, null);
        }

        public static PlayDiagram For(Play play, Formation f, Formation offense)
        {
            var d = new PlayDiagram { Side = play.Side, Title = play.Name };
            d.ApplyViewport(play.Side);
            if (f == null) return d;
            for (int i = 0; i < f.Slots.Length; i++) d.AddPlayer(f, i, play.Side == PlaySide.Defense);
            d.SeparateChips();
            if (play.Side == PlaySide.Offense) d.BuildOffense(play, f);
            else if (play.Side == PlaySide.Defense) d.BuildDefense(play, f, offense);
            else { d.BuildSpecial(play, f); d.FitToContent(); return d; }
            d.ClampToViewport();
            return d;
        }

        /// <summary>Number of strokes that <see cref="ClampToViewport"/> had to pull back to the card edge (QA-549: a non-zero count means a route is longer than the card shows).</summary>
        public int ClippedStrokes { get; private set; }

        /// <summary>Deep routes run off the top of the card: pull stray points back to the viewport edge and flag the strokes that were cut (<see cref="DiagramStroke.Clipped"/>).</summary>
        public void ClampToViewport()
        {
            float top = MaxY - 1.6f, side = MaxX - 1.0f;
            ClippedStrokes = 0;
            foreach (DiagramStroke s in Strokes)
            {
                for (int i = 0; i < s.Points.Count; i++)
                {
                    Vec2 p = s.Points[i];
                    var q = new Vec2(Math.Max(-side, Math.Min(side, p.X)), Math.Min(top, p.Y));
                    if (q.X != p.X || q.Y != p.Y) s.Clipped = true;
                    s.Points[i] = q;
                }
                if (s.Clipped) ClippedStrokes++;
            }
        }

        /// <summary>One line per clipped stroke ("Slant: slot 8 ..."), for the editor tool and logs.</summary>
        public List<string> ClipReport()
        {
            var l = new List<string>();
            foreach (DiagramStroke s in Strokes) if (s.Clipped) l.Add(Title + ": " + s.Ink + " stroke of slot " + s.Slot + " is clipped by the card");
            return l;
        }

        /// <summary>Viewport that frames every player and stroke (special teams units sit far apart).</summary>
        public void FitToContent()
        {
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            foreach (DiagramPlayer p in Players) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
            foreach (DiagramStroke s in Strokes) foreach (Vec2 p in s.Points) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
            if (x0 > x1) return;
            float w = Math.Max(x1 - x0, 30f), h = Math.Max(y1 - y0, 20f);
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            // keep the square aspect of a play card
            if (w < h) w = h; else h = w;
            w += 6f; h += 6f;
            MinX = cx - w * 0.5f; MaxX = cx + w * 0.5f; MinY = cy - h * 0.5f; MaxY = cy + h * 0.5f;
        }

        void ApplyViewport(PlaySide side)
        {
            Side = side;
            if (side == PlaySide.Defense) { MinX = -22f; MaxX = 22f; MinY = -5f; MaxY = 39f; }
            else if (side == PlaySide.Offense) { MinX = -22f; MaxX = 22f; MinY = -8.5f; MaxY = 35.5f; }
            else { MinX = -26f; MaxX = 26f; MinY = -17f; MaxY = 30f; }
        }

        void AddPlayer(Formation f, int slot, bool defense)
        {
            SlotSpec s = f.Slots[slot];
            Players.Add(new DiagramPlayer { Slot = slot, Position = s.Position, Label = LabelFor(f, slot), X = s.X, Y = s.Y, Defense = defense });
        }

        /// <summary>Chip label of a slot in its formation: like <see cref="LabelFor(SlotSpec)"/>, but the extra cornerbacks of a nickel / dime package read NB / DB instead of CB (QA-167).</summary>
        public static string LabelFor(Formation f, int slot)
        {
            SlotSpec s = f.Slots[slot];
            string l = LabelFor(s);
            if (f.Side != PlaySide.Defense || s.Position != Position.CB || !string.IsNullOrEmpty(s.Label)) return l;
            int count = 0, rank = 0;
            for (int i = 0; i < f.Slots.Length; i++)
            {
                if (f.Slots[i].Position != Position.CB) continue;
                count++;
                if (f.Slots[i].X < s.X || (f.Slots[i].X == s.X && i < slot)) rank++;
            }
            if (count < 3 || rank == 0 || rank == count - 1) return l;
            return count == 3 || rank == 1 ? "NB" : "DB";
        }

        const float OlChipRadius = 0.6f, ChipRadius = 1.05f;
        static float ChipRadiusOf(DiagramPlayer p) { return !p.Defense && PositionGroup.IsOffensiveLine(p.Position) ? OlChipRadius : ChipRadius; }

        /// <summary>
        /// Display-only: chips that would overlap (shotgun QB and back stand two yards apart, a tight end next to the tackle) are pushed sideways until they just
        /// clear each other, so no two bubbles draw on the same spot (QA-159). The formation data is not touched.
        /// </summary>
        void SeparateChips()
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int j = 1; j < Players.Count; j++)
                {
                    DiagramPlayer b = Players[j];
                    if (b.Ghost) continue;
                    bool bLine = !b.Defense && PositionGroup.IsOffensiveLine(b.Position);
                    for (int i = 0; i < j; i++)
                    {
                        DiagramPlayer a = Players[i];
                        if (a.Ghost) continue;
                        if (!a.Defense && PositionGroup.IsOffensiveLine(a.Position) && bLine) continue;   // the line is drawn as separate boxes already
                        if (Math.Abs(a.Y - b.Y) >= 1.6f) continue;
                        float need = ChipRadiusOf(a) + ChipRadiusOf(b) + 0.1f, dx = b.X - a.X;
                        if (Math.Abs(dx) >= need - 0.001f) continue;
                        b.X = a.X + (dx < 0f ? -need : need);
                        moved = true;
                    }
                }
                if (!moved) break;
            }
        }

        public static string LabelFor(SlotSpec s)
        {
            if (!string.IsNullOrEmpty(s.Label) && s.Position != Position.LT && s.Position != Position.LG && s.Position != Position.C && s.Position != Position.RG && s.Position != Position.RT) return s.Label;
            switch (s.Position)
            {
                case Position.QB: return "QB"; case Position.RB: return "HB"; case Position.FB: return "FB"; case Position.WR: return "WR"; case Position.TE: return "TE";
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT: return "";
                case Position.LE: case Position.RE: return "DE"; case Position.DT: return "DT";
                case Position.LOLB: case Position.ROLB: case Position.MLB: return "LB";
                case Position.CB: return "CB"; case Position.FS: return "FS"; case Position.SS: return "SS"; case Position.K: return "K"; case Position.P: return "P";
            }
            return "";
        }

        /// <summary>Where the chip of a slot is drawn (the separated position when two chips were pushed apart), so strokes start at the chip.</summary>
        Vec2 PosOf(Formation f, int slot)
        {
            if (slot >= 0 && slot < Players.Count && Players[slot].Slot == slot && !Players[slot].Ghost) return new Vec2(Players[slot].X, Players[slot].Y);
            return new Vec2(f.Slots[slot].X, f.Slots[slot].Y);
        }

        DiagramStroke Stroke(InkKind ink, int slot, bool arrow = false, bool dashed = false, bool tEnd = false, bool thick = false)
        {
            var s = new DiagramStroke { Ink = ink, Slot = slot, Arrow = arrow, Dashed = dashed, TEnd = tEnd, Thick = thick };
            Strokes.Add(s);
            return s;
        }

        // ================================================================================================ offense
        void BuildOffense(Play play, Formation f)
        {
            var qb = play.Assignments[0] as QbAssignment;
            int primary = qb != null && qb.ReadProgression.Count > 0 ? qb.ReadProgression[0] : -1;
            for (int i = 0; i < play.Assignments.Length; i++)
            {
                Vec2 start = PosOf(f, i);
                var r = play.Assignments[i] as RouteAssignment;
                if (r != null)
                {
                    var s = Stroke(r.IsHot ? InkKind.HotRoute : (i == primary ? InkKind.PrimaryRoute : InkKind.Route), i, true);
                    s.Points.Add(start);
                    foreach (RouteWaypoint w in r.Waypoints) s.Points.Add(w.Point);
                    continue;
                }
                var c = play.Assignments[i] as CarryAssignment;
                if (c != null)
                {
                    var s = Stroke(c.IsFake ? InkKind.Fake : InkKind.Run, i, true, c.IsFake, false, !c.IsFake);
                    s.Points.Add(start);
                    foreach (Vec2 p in c.Path) s.Points.Add(p);
                    if (c.Path.Count > 0 && !c.IsFake) Players[i].Highlight = true;
                    continue;
                }
                var b = play.Assignments[i] as BlockAssignment;
                if (b != null) { BlockStroke(play, f, i, b, start); continue; }
            }
            if (qb != null) QbStrokes(play, f, qb);
        }

        void BlockStroke(Play play, Formation f, int i, BlockAssignment b, Vec2 start)
        {
            var s = Stroke(InkKind.Block, i, false, false, true);
            s.Points.Add(start);
            bool pass = b.Block == BlockKind.PassPro || b.Block == BlockKind.Chip;
            float side = start.X < 0f ? -1f : 1f;
            if (b.Path != null && b.Path.Count > 0)
            {
                foreach (Vec2 p in b.Path) s.Points.Add(p);
                return;
            }
            switch (b.Block)
            {
                case BlockKind.PassPro:
                case BlockKind.Chip:
                    {
                        float dx = b.Target == BlockTarget.SlideLeft ? -1.1f : (b.Target == BlockTarget.SlideRight ? 1.1f : 0f);
                        if (start.Y < -2f) { s.Points.Add(new Vec2(start.X + dx, start.Y + 0.9f)); }
                        else s.Points.Add(new Vec2(start.X + dx, start.Y - 1.3f));
                        break;
                    }
                case BlockKind.Stalk:
                    s.Points.Add(new Vec2(start.X - side * 1.0f, start.Y + 2.4f));
                    break;
                case BlockKind.Lead:
                case BlockKind.Kickout:
                    s.Points.Add(new Vec2(start.X + side * 1.2f, start.Y + 2.8f));
                    break;
                default:
                    {
                        float dx = 0f;
                        if (b.Target == BlockTarget.ZoneRight) dx = 1.5f;
                        else if (b.Target == BlockTarget.ZoneLeft) dx = -1.5f;
                        else if (b.Target == BlockTarget.Playside && play.Gap == RunGap.None) dx = 1.5f * DirOfRun(play);   // QA-547: toward the run, whichever way it goes
                        else if (b.Target == BlockTarget.Backside || b.Target == BlockTarget.Playside)
                            dx = (b.Target == BlockTarget.Playside ? 1f : -1f) * 1.2f * DirOfRun(play);
                        else if (b.Target == BlockTarget.EdgeDefender) dx = side * 1.4f;
                        s.Points.Add(new Vec2(start.X + dx, start.Y + 1.6f));
                        break;
                    }
            }
        }

        /// <summary>+1 when the called run goes to the offense's right, -1 left, 0 for none (from the carry path).</summary>
        static int DirOfRun(Play play)
        {
            foreach (Assignment a in play.Assignments)
            {
                var c = a as CarryAssignment;
                if (c == null || c.IsFake || c.Path.Count == 0) continue;
                return c.Path[c.Path.Count - 1].X >= 0f ? 1 : -1;
            }
            var qb = play.Assignments.Length > 0 ? play.Assignments[0] as QbAssignment : null;
            if (qb != null && qb.RunPath.Count > 0) return qb.RunPath[qb.RunPath.Count - 1].X >= 0f ? 1 : -1;
            return 1;
        }

        void QbStrokes(Play play, Formation f, QbAssignment qb)
        {
            Vec2 q = PosOf(f, 0);
            switch (qb.Action)
            {
                case QbAction.Handoff:
                case QbAction.Toss:
                case QbAction.ReadOption:
                    if (qb.HandoffSlot >= 0)
                    {
                        Vec2 h = PosOf(f, qb.HandoffSlot);
                        var s = Stroke(InkKind.Handoff, 0, true, qb.Action == QbAction.Toss);
                        s.Points.Add(q); s.Points.Add(new Vec2((q.X + h.X) * 0.5f, (q.Y + h.Y) * 0.5f));
                        s.Points.Add(h);
                    }
                    if (qb.RunPath.Count > 0)
                    {
                        var s = Stroke(InkKind.Run, 0, true, true);
                        s.Points.Add(q); foreach (Vec2 p in qb.RunPath) s.Points.Add(p);
                    }
                    break;
                case QbAction.Keep:
                case QbAction.Sneak:
                    if (qb.RunPath.Count > 0)
                    {
                        var s = Stroke(InkKind.Run, 0, true, false, false, true);
                        s.Points.Add(q); foreach (Vec2 p in qb.RunPath) s.Points.Add(p);
                        Players[0].Highlight = true;
                    }
                    break;
                case QbAction.Kneel:
                case QbAction.Spike:
                    break;
                default:
                    {
                        float depth = f.Slots[0].Y <= -3f ? (qb.DropbackSteps >= 5 ? 1.8f : 1.0f) : (qb.DropbackSteps >= 7 ? 5.5f : qb.DropbackSteps >= 5 ? 4.2f : 2.8f);
                        var s = Stroke(InkKind.Dropback, 0, true, true);
                        s.Points.Add(q);
                        if (qb.Action == QbAction.Rollout || qb.Action == QbAction.Boot)
                        {
                            float dir = qb.RollDirection == 0 ? 1f : Math.Sign(qb.RollDirection);
                            s.Points.Add(new Vec2(q.X + dir * 3.2f, q.Y - 1.2f));
                            s.Points.Add(new Vec2(q.X + dir * 6.2f, q.Y - 0.6f));
                        }
                        else s.Points.Add(new Vec2(q.X, q.Y - depth));
                        break;
                    }
            }
        }

        // ================================================================================================ defense
        /// <summary>Position of the offensive player a man defender covers when no offensive formation is known (a standard Singleback look).</summary>
        static Vec2 FallbackGhost(int slot)
        {
            switch (slot)
            {
                case 1: return new Vec2(0f, -6f); case 7: return new Vec2(4.5f, 0f); case 8: return new Vec2(-17.5f, 0f);
                case 9: return new Vec2(17.5f, -1f); case 10: return new Vec2(-8f, -1f); default: return new Vec2(0f, -1f);
            }
        }

        void BuildDefense(Play play, Formation f, Formation offense)
        {
            // ghost offense (QA-548): one faint stand-in per offensive slot that a man defender is assigned to, placed where that slot lines up in the
            // offensive formation (a Singleback look when none is supplied), so the coverage lines point at the receivers they cover
            var targets = new List<int>();
            foreach (Assignment a in play.Assignments)
            {
                var c = a as CoverageAssignment;
                if (c != null && c.IsMan && c.TargetSlot >= 0 && !targets.Contains(c.TargetSlot)) targets.Add(c.TargetSlot);
            }
            var ghostAt = new Dictionary<int, Vec2>();
            foreach (int t in targets)
            {
                bool known = offense != null && t < offense.Slots.Length;
                Vec2 gp = known ? new Vec2(offense.Slots[t].X, offense.Slots[t].Y) : FallbackGhost(t);
                ghostAt[t] = gp;
                Players.Add(new DiagramPlayer { Slot = t, Position = known ? offense.Slots[t].Position : Position.WR, Label = "", X = gp.X, Y = gp.Y, Defense = false, Ghost = true });
            }

            for (int i = 0; i < play.Assignments.Length; i++)
            {
                Vec2 start = PosOf(f, i);
                var cv = play.Assignments[i] as CoverageAssignment;
                if (cv != null)
                {
                    if (!cv.IsMan && cv.Zone != null)
                    {
                        ZoneRegion z = cv.Zone;
                        Zones.Add(new DiagramZone { Kind = z.Kind, CenterX = z.CenterX, CenterY = z.CenterY, Width = z.Width, Height = Math.Min(z.Height, 12f), Slot = i, Deep = z.Kind == ZoneKind.DeepHalf || z.Kind == ZoneKind.DeepThird || z.Kind == ZoneKind.DeepMiddle || z.Kind == ZoneKind.Quarter });
                    }
                    else if (cv.IsMan)
                    {
                        Vec2 t;
                        if (!ghostAt.TryGetValue(cv.TargetSlot, out t)) t = new Vec2(start.X, start.Y - 4f);
                        var s = Stroke(InkKind.Man, i, true, true);
                        s.Points.Add(start); s.Points.Add(new Vec2(t.X, Math.Max(t.Y + 0.9f, 0.9f) * 0.5f + t.Y * 0.5f)); s.Points.Add(new Vec2(t.X, t.Y + 0.8f));
                    }
                    continue;
                }
                var bl = play.Assignments[i] as BlitzAssignment;
                if (bl != null)
                {
                    var s = Stroke(InkKind.Blitz, i, true, false, false, true);
                    float gx = Math.Abs(GapX(bl.Gap)) < 0.01f ? start.X : GapX(bl.Gap);
                    s.Points.Add(start); s.Points.Add(new Vec2((start.X + gx) * 0.5f, (start.Y + -1.2f) * 0.5f)); s.Points.Add(new Vec2(gx, -1.8f));
                    continue;
                }
                var ru = play.Assignments[i] as RushAssignment;
                if (ru != null)
                {
                    var s = Stroke(InkKind.Rush, i, true);
                    float gx = Math.Abs(GapX(ru.Gap)) < 0.01f ? start.X : GapX(ru.Gap);
                    s.Points.Add(start); s.Points.Add(new Vec2(gx, -1.2f));
                    continue;
                }
                var sp = play.Assignments[i] as SpyAssignment;
                if (sp != null)
                {
                    var s = Stroke(InkKind.Spy, i, true, true);
                    s.Points.Add(start); s.Points.Add(new Vec2(start.X * 0.4f, 2.2f));
                }
            }
        }

        static float GapX(RunGap g)
        {
            switch (g)
            {
                case RunGap.ALeft: return -0.75f; case RunGap.ARight: return 0.75f;
                case RunGap.BLeft: return -2.25f; case RunGap.BRight: return 2.25f;
                case RunGap.CLeft: return -3.75f; case RunGap.CRight: return 3.75f;
                case RunGap.DLeft: return -5.5f; case RunGap.DRight: return 5.5f;
                default: return 0f;
            }
        }

        // ================================================================================================ special teams
        void BuildSpecial(Play play, Formation f)
        {
            for (int i = 0; i < play.Assignments.Length; i++)
            {
                Vec2 start = PosOf(f, i);
                var k = play.Assignments[i] as KickAssignment;
                if (k != null)
                {
                    var s = Stroke(InkKind.Kick, i, true, true);
                    s.Points.Add(start);
                    float ax = k.AimX;
                    float depth = k.KickType == KickKind.Onside ? 11f : (k.KickType == KickKind.Squib ? 22f : (k.KickType == KickKind.FieldGoal || k.KickType == KickKind.ExtraPoint ? 14f : 28f));
                    s.Points.Add(new Vec2(start.X + ax * 0.5f, start.Y + depth * 0.55f));
                    s.Points.Add(new Vec2(start.X + ax, start.Y + depth));
                    continue;
                }
                var l = play.Assignments[i] as LaneAssignment;
                if (l != null)
                {
                    var s = Stroke(InkKind.Route, i, true);
                    s.Points.Add(start);
                    if (l.Path.Count > 0)
                    {
                        // coverage lanes run 40-60 yards: draw a straight lane of a readable length in the direction of the run
                        Vec2 last = l.Path[l.Path.Count - 1];
                        float dy = last.Y - start.Y, len = Math.Min(Math.Abs(dy), 14f), dir = dy >= 0f ? 1f : -1f;
                        s.Points.Add(new Vec2(last.X, start.Y + dir * len));
                    }                    continue;
                }
                var r = play.Assignments[i] as ReturnerAssignment;
                if (r != null && r.Path.Count > 0)
                {
                    var s = Stroke(InkKind.Run, i, true, false, false, true);
                    s.Points.Add(start); foreach (Vec2 p in r.Path) s.Points.Add(p);
                    continue;
                }
                var b = play.Assignments[i] as BlockAssignment;
                if (b != null) BlockStroke(play, f, i, b, start);
                var ru = play.Assignments[i] as RushAssignment;
                if (ru != null)
                {
                    var s = Stroke(InkKind.Rush, i, true);
                    s.Points.Add(start); s.Points.Add(new Vec2(start.X * 0.5f, -2.5f));
                }
            }
        }

        /// <summary>Longest straight run of the strokes, for sanity checks (play art tests).</summary>
        public float MaxExtentY()
        {
            float m = float.MinValue;
            foreach (DiagramStroke s in Strokes) foreach (Vec2 p in s.Points) if (p.Y > m) m = p.Y;
            return m;
        }
    }
}


