using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// v0.11 HUD measurement: the screen rectangle of every visible HUD element group (the direct children of each overlay canvas that currently draw something), the pairs that overlap and the
    /// groups that leave the title-safe area. Used by the watch probe (per resolution, before / after) and by the HUD layout tests.
    /// </summary>
    public static class HudBounds
    {
        public struct Group
        {
            public string Canvas, Name; public Rect Px; public bool Backdrop;
            public override string ToString() { return Canvas + "/" + Name; }
        }

        /// <summary>Fraction of each screen edge that is kept clear (title safe, 3 %).</summary>
        public const float SafeInset = 0.03f;

        /// <summary>Visible groups of one root: every direct child that has at least one drawn graphic under it.</summary>
        public static void Collect(Transform root, string canvasName, List<Group> into, float minAlpha = 0.04f)
        {
            if (root == null) return;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                // a full-canvas container (the KickHud layer, the receiver icon layer) is not one element: measure what is inside it
                var crt = child as RectTransform; var prt = root as RectTransform;
                if (crt != null && prt != null && child.childCount > 1 && crt.rect.width * crt.rect.height > 0.5f * prt.rect.width * prt.rect.height) { Collect(child, canvasName + "/" + child.name, into, minAlpha); continue; }
                bool any = false; Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
                var gs = child.GetComponentsInChildren<Graphic>(false);
                var corners = new Vector3[4];
                foreach (var g in gs)
                {
                    if (g == null || !g.enabled || !g.gameObject.activeInHierarchy || g.canvasRenderer == null || g.canvasRenderer.cull) continue;
                    float a = g.color.a * g.canvasRenderer.GetInheritedAlpha();
                    if (a < minAlpha) continue;
                    var tmp = g as TMP_Text;
                    if (tmp != null && string.IsNullOrEmpty(tmp.text)) continue;
                    var img = g as Image;
                    if (img != null && img.sprite == null && img.color.a < minAlpha) continue;
                    var rt = g.rectTransform;
                    if (rt.rect.width < 1.5f || rt.rect.height < 1.5f) continue;
                    Rect r;
                    if (tmp != null)
                    {
                        // the text's own drawn extent, not its (often wide) box
                        Bounds b = tmp.textBounds;
                        if (b.size.x < 1f) continue;
                        Vector3 c0 = tmp.transform.TransformPoint(b.min), c1 = tmp.transform.TransformPoint(b.max);
                        r = Rect.MinMaxRect(Mathf.Min(c0.x, c1.x), Mathf.Min(c0.y, c1.y), Mathf.Max(c0.x, c1.x), Mathf.Max(c0.y, c1.y));
                    }
                    else { rt.GetWorldCorners(corners); r = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y); }
                    if (r.width < 1f || r.height < 1f) continue;
                    any = true;
                    mn = Vector2.Min(mn, r.min); mx = Vector2.Max(mx, r.max);
                }
                if (!any) continue;
                var grp = new Group { Canvas = canvasName, Name = child.name, Px = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y) };
                grp.Backdrop = grp.Px.width * grp.Px.height > 0.55f * Screen.width * Screen.height;
                into.Add(grp);
            }
        }

        /// <summary>Every enabled screen-space overlay root canvas in the scene.</summary>
        public static List<Group> MeasureAll()
        {
            var list = new List<Group>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || !c.enabled || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                Collect(c.transform, c.name, list);
            }
            return list;
        }

        public static float OverlapArea(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        public static bool OutsideSafe(Rect r, int w, int h)
        {
            float ix = w * SafeInset, iy = h * SafeInset;
            return r.xMin < ix - 1f || r.xMax > w - ix + 1f || r.yMin < iy - 1f || r.yMax > h - iy + 1f;
        }

        /// <summary>Overlapping pairs among the non-backdrop groups (the smaller one is covered by more than <paramref name="minFraction"/>).</summary>
        public static List<string> Overlaps(List<Group> groups, float minFraction = 0.02f)
        {
            var res = new List<string>();
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                {
                    if (groups[i].Backdrop || groups[j].Backdrop) continue;
                    float ov = OverlapArea(groups[i].Px, groups[j].Px);
                    float small = Mathf.Min(groups[i].Px.width * groups[i].Px.height, groups[j].Px.width * groups[j].Px.height);
                    if (small > 1f && ov / small > minFraction) res.Add(groups[i] + " x " + groups[j] + " (" + Mathf.RoundToInt(100f * ov / small) + "% of the smaller)");
                }
            return res;
        }

        /// <summary>One text block: each group with its rect in 1080p-equivalent units, the overlaps and the safe-area violations.</summary>
        public static string Report(string key, List<Group> groups, int w, int h)
        {
            var sb = new StringBuilder();
            float u = 1080f / h;   // px -> units of the 1080-high reference
            sb.AppendLine("## " + key + "  " + w + "x" + h);
            foreach (var g in groups)
                sb.AppendLine("  " + (g.Backdrop ? "[layer] " : "") + g + "  x " + (g.Px.xMin * u).ToString("0") + "  y " + ((h - g.Px.yMax) * u).ToString("0") + "  w " + (g.Px.width * u).ToString("0") + "  h " + (g.Px.height * u).ToString("0")
                              + "  centre " + ((g.Px.center.x / w) * 100f).ToString("0") + "% " + ((1f - g.Px.center.y / h) * 100f).ToString("0") + "%" + (!g.Backdrop && OutsideSafe(g.Px, w, h) ? "  OUTSIDE SAFE AREA" : ""));
            foreach (var o in Overlaps(groups)) sb.AppendLine("  OVERLAP " + o);
            return sb.ToString();
        }
    }
}
