using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Pure navigation helpers shared by the glass controls (EditMode tested): wrap / clamp index stepping, bumper paging and column grid movement for the crest grid.
    /// </summary>
    public static class GlassNav
    {
        public static int Step(int index, int dir, int count, bool wrap)
        {
            if (count <= 0) return 0;
            int n = index + dir;
            return wrap ? ((n % count) + count) % count : Mathf.Clamp(n, 0, count - 1);
        }

        /// <summary>Moves inside a grid that is filled row by row (<paramref name="cols"/> per row, <paramref name="count"/> cells): returns the new index, or the same one at an edge.</summary>
        public static int GridMove(int index, int dx, int dy, int cols, int count)
        {
            if (count <= 0 || cols <= 0) return 0;
            int x = index % cols, y = index / cols;
            int rows = (count + cols - 1) / cols;
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) return index;
            int ni = ny * cols + nx;
            return ni < count ? ni : (dy != 0 ? count - 1 : index);
        }
    }




}
