using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>Pure geometric navigation for the mixed-size tile grid (EditMode tested): the nearest selectable tile in a direction, by centre distance with a cost on the sideways offset.</summary>
    public static class HubNav
    {
        public struct Cell
        {
            public Vector2 Center;     // canvas units, y up
            public Vector2 Size;
            public bool Selectable;
            public Cell(Vector2 center, Vector2 size, bool selectable) { Center = center; Size = size; Selectable = selectable; }
        }

        /// <summary>The index of the tile reached by moving from <paramref name="from"/> in <paramref name="dir"/> (a unit axis vector), or -1 when there is none. Locked tiles are skipped.</summary>
        public static int Neighbor(IList<Cell> cells, int from, Vector2 dir)
        {
            if (from < 0 || from >= cells.Count) return -1;
            var f = cells[from];
            int best = -1; float bestCost = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                if (i == from || !cells[i].Selectable) continue;
                Vector2 d = cells[i].Center - f.Center;
                float along = Vector2.Dot(d, dir);
                float half = (Mathf.Abs(dir.x) > 0.5f ? f.Size.x + cells[i].Size.x : f.Size.y + cells[i].Size.y) * 0.5f;
                if (along < half - 2f) continue;   // the target must lie beyond the source along the axis (no overlap)
                Vector2 perpV = d - dir * along;
                float perp = perpV.magnitude;
                // tiles whose extent overlaps the source across the movement axis are preferred (a wide hero tile is reachable from either of its neighbours)
                float overlapHalf = Mathf.Abs(dir.x) > 0.5f ? (f.Size.y + cells[i].Size.y) * 0.5f : (f.Size.x + cells[i].Size.x) * 0.5f;
                float cost = along + (perp <= overlapHalf * 0.5f ? perp * 0.6f : perp * 3f);
                if (cost < bestCost) { bestCost = cost; best = i; }
            }
            return best;
        }

        /// <summary>The four links (up, down, left, right) of every cell.</summary>
        public static int[][] Links(IList<Cell> cells)
        {
            var res = new int[cells.Count][];
            for (int i = 0; i < cells.Count; i++)
                res[i] = new[] { Neighbor(cells, i, Vector2.up), Neighbor(cells, i, Vector2.down), Neighbor(cells, i, Vector2.left), Neighbor(cells, i, Vector2.right) };
            return res;
        }
    }
}
