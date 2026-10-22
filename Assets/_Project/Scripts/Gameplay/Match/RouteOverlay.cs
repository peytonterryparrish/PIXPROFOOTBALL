using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.7 (playtest note 10): the offense's routes drawn on the turf while the user holds LT, from the simulation's own assignments (so it is right after an audible, a hot route, a flip or a
    /// motion): a dark outline ribbon with a team-colour glowing core and an arrowhead per receiver, the primary read thicker and white-hot, hot routes orange, a run path for the ball carrier.
    /// Flat meshes (like <see cref="FieldMarkers"/>) in the same unlit material as the first-down line; the read numbers and the button icons are drawn by the HUD from <see cref="Badges"/>.
    /// </summary>
    public sealed class RouteOverlay : MonoBehaviour
    {
        /// <summary>A label the HUD draws at a route's end: the read number (1 = first read), "RUN" for a carry, plus the route's colour class.</summary>
        public struct Badge { public Vector3 World; public int Read; public string Text; public bool Hot, Primary, Run; public int Agent; }

        const float OutlineY = 0.036f, CoreY = 0.042f, TopY = 0.048f;
        const float CoreW = 0.26f, OutlineW = 0.52f, PrimaryW = 0.34f, ArrowLen = 1.25f, ArrowHalf = 0.8f;

        readonly List<Badge> _badges = new List<Badge>();
        public IList<Badge> Badges { get { return _badges; } }
        public bool Shown { get; private set; }
        public int Signature { get; private set; } = int.MinValue;
        public int RouteCount { get; private set; }

        MeshFilter _outline, _team, _primary, _hot;
        Material _mOutline, _mTeam, _mPrimary, _mHot;
        Color _teamColor = Color.white;

        public static RouteOverlay Create(Transform parent)
        {
            var go = new GameObject("RouteOverlay");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<RouteOverlay>();
            r.Setup();
            go.SetActive(false);
            return r;
        }

        void Setup()
        {
            var rt = PixRuntimeAssets.Load();
            Material template = rt != null ? rt.lineFirstDown : null;
            _outline = Layer("Outline", template, new Color(0.02f, 0.03f, 0.05f, 1f), out _mOutline);
            _team = Layer("Team", template, Color.white, out _mTeam);
            _primary = Layer("Primary", template, new Color(1f, 0.97f, 0.82f, 1f), out _mPrimary);
            _hot = Layer("Hot", template, new Color(1f, 0.55f, 0.12f, 1f), out _mHot);
        }

        MeshFilter Layer(string name, Material template, Color color, out Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mat = template != null ? new Material(template) : null;
            if (mat != null) { mat.SetColor("_UnlitColor", color); mr.sharedMaterial = mat; }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mf;
        }

        /// <summary>A saturated, bright version of the team colour that reads on grass (dark and green kits get lifted).</summary>
        public static Color Visible(Color c)
        {
            float h, s, v; Color.RGBToHSV(c, out h, out s, out v);
            s = Mathf.Max(s, 0.65f); v = 1f;
            return Color.HSVToRGB(h, s, v);
        }

        void OnDestroy()
        {
            foreach (var m in new[] { _mOutline, _mTeam, _mPrimary, _mHot }) if (m != null) Destroy(m);
            foreach (var mf in new[] { _outline, _team, _primary, _hot }) if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }

        // ============================================================================================ signature
        /// <summary>A hash of everything the picture depends on (the receivers' waypoints, hot flags, start spots before the snap): the director rebuilds when it changes.</summary>
        public static int SignatureOf(PlaySimulation sim)
        {
            if (sim == null) return 0;
            unchecked
            {
                int h = 17 + (sim.OffPlay != null ? (sim.OffPlay.Id ?? "").GetHashCode() : 0) + (sim.OffPlay != null && sim.OffPlay.Mirrored ? 7 : 0);
                bool pre = sim.Phase == PlayPhase.PreSnap;
                for (int i = 0; i < 11; i++)
                {
                    var a = sim.Agents[i]; if (a == null || a.Assign == null) continue;
                    h = h * 31 + i;
                    if (pre) h = h * 31 + (int)(a.Pos.X * 8f) * 7 + (int)(a.Pos.Y * 8f);
                    var ra = a.Assign as RouteAssignment;
                    if (ra != null)
                    {
                        h = h * 31 + (ra.IsHot ? 1 : 0) + ra.ReadOrder * 3 + (int)ra.Route * 5;
                        foreach (var w in ra.Waypoints) h = h * 31 + (int)(w.Point.X * 8f) * 13 + (int)(w.Point.Y * 8f);
                        continue;
                    }
                    var ca = a.Assign as CarryAssignment;
                    if (ca != null) foreach (var p in ca.Path) h = h * 31 + (int)(p.X * 8f) * 13 + (int)(p.Y * 8f);
                }
                return h;
            }
        }

        // ============================================================================================ build
        public void Rebuild(PlaySimulation sim, PlayFrame frame, Color teamColor)
        {
            _badges.Clear(); RouteCount = 0;
            Signature = SignatureOf(sim);
            _teamColor = Visible(teamColor);
            var oL = new MeshData(); var tL = new MeshData(); var pL = new MeshData(); var hL = new MeshData();
            var qb = sim != null && sim.Agents.Length > 0 ? sim.Agents[0].Assign as QbAssignment : null;
            if (sim != null)
            {
                for (int i = 1; i < 11; i++)
                {
                    var a = sim.Agents[i]; if (a == null || a.Assign == null) continue;
                    var ra = a.Assign as RouteAssignment;
                    if (ra != null && ra.Waypoints.Count > 0)
                    {
                        int read = qb != null ? qb.ReadProgression.IndexOf(a.Slot) + 1 : 0; if (read <= 0) read = ra.ReadOrder;
                        bool primary = read == 1, hot = ra.IsHot;
                        var pts = new List<Vector3> { Flat(frame.ToUnity(a.Pos)) };
                        foreach (var w in ra.Waypoints) pts.Add(Flat(frame.ToUnity(sim.RouteRel(w.Point))));
                        Ribbon(oL, pts, OutlineW, OutlineY, true);
                        var target = hot ? hL : (primary ? pL : tL);
                        Ribbon(target, pts, primary || hot ? PrimaryW : CoreW, hot ? TopY : (primary ? TopY : CoreY), true);
                        _badges.Add(new Badge { World = pts[pts.Count - 1], Read = read, Text = read > 0 ? read.ToString() : "", Hot = hot, Primary = primary, Agent = i });
                        RouteCount++;
                        continue;
                    }
                    var ca = a.Assign as CarryAssignment;
                    if (ca != null && !ca.IsFake && ca.Path.Count > 0)
                    {
                        var pts = new List<Vector3> { Flat(frame.ToUnity(a.Pos)) };
                        foreach (var p in ca.Path) pts.Add(Flat(frame.ToUnity(sim.RouteRel(p))));
                        Ribbon(oL, pts, OutlineW, OutlineY, true);
                        Ribbon(pL, pts, PrimaryW, TopY, true);
                        _badges.Add(new Badge { World = pts[pts.Count - 1], Read = 0, Text = "RUN", Run = true, Primary = true, Agent = i });
                        RouteCount++;
                    }
                }
            }
            Apply(_outline, oL); Apply(_team, tL); Apply(_primary, pL); Apply(_hot, hL);
            if (_mTeam != null) _mTeam.SetColor("_UnlitColor", _teamColor * 1.25f);
        }

        static Vector3 Flat(Vector3 p) { p.y = 0f; return p; }

        public void SetShown(bool on)
        {
            if (on == Shown) return;
            Shown = on;
            gameObject.SetActive(on);
        }

        void Update()
        {
            // hot and primary routes breathe a little so they read as the important ones
            float k = 1.15f + 0.2f * Mathf.Sin(Time.unscaledTime * 5f);
            if (_mHot != null) _mHot.SetColor("_UnlitColor", new Color(1f, 0.55f, 0.12f, 1f) * k);
            if (_mPrimary != null) _mPrimary.SetColor("_UnlitColor", new Color(1f, 0.97f, 0.82f, 1f) * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 3.4f)));
        }

        // ============================================================================================ ribbon meshes
        sealed class MeshData { public readonly List<Vector3> V = new List<Vector3>(); public readonly List<int> T = new List<int>(); }

        static void Apply(MeshFilter mf, MeshData d)
        {
            var old = mf.sharedMesh;
            var m = new Mesh { name = "RouteRibbon" };
            m.SetVertices(d.V); m.SetTriangles(d.T, 0); m.RecalculateBounds();
            var n = new Vector3[d.V.Count]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            mf.sharedMesh = m;
            if (old != null) Destroy(old);
        }

        static void Quad(MeshData d, Vector3 a, Vector3 b, Vector3 c, Vector3 e)
        {
            int i = d.V.Count; d.V.Add(a); d.V.Add(b); d.V.Add(c); d.V.Add(e);
            // both windings: the material may be single-sided and the camera looks from above
            d.T.Add(i); d.T.Add(i + 1); d.T.Add(i + 2); d.T.Add(i); d.T.Add(i + 2); d.T.Add(i + 3);
            d.T.Add(i); d.T.Add(i + 2); d.T.Add(i + 1); d.T.Add(i); d.T.Add(i + 3); d.T.Add(i + 2);
        }

        static void Tri(MeshData d, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = d.V.Count; d.V.Add(a); d.V.Add(b); d.V.Add(c);
            d.T.Add(i); d.T.Add(i + 1); d.T.Add(i + 2); d.T.Add(i); d.T.Add(i + 2); d.T.Add(i + 1);
        }

        static void Disc(MeshData d, Vector3 c, float r)
        {
            const int N = 8;
            for (int k = 0; k < N; k++)
            {
                float a0 = k * Mathf.PI * 2f / N, a1 = (k + 1) * Mathf.PI * 2f / N;
                Tri(d, c, c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r);
            }
        }

        /// <summary>A flat ribbon along <paramref name="pts"/> of full width <paramref name="w"/> at height <paramref name="y"/> with round joints and (optionally) an arrowhead one width wider than the line.</summary>
        static void Ribbon(MeshData d, List<Vector3> pts, float w, float y, bool arrow)
        {
            if (pts.Count < 2) return;
            float half = w * 0.5f;
            // the arrowhead replaces the last piece of the line, so the shaft ends where the head begins
            Vector3 endDir = Vector3.zero; Vector3 tip = pts[pts.Count - 1];
            var line = new List<Vector3>(pts);
            if (arrow)
            {
                for (int i = pts.Count - 2; i >= 0; i--) { endDir = pts[pts.Count - 1] - pts[i]; endDir.y = 0f; if (endDir.sqrMagnitude > 0.01f) break; }
                if (endDir.sqrMagnitude > 0.0001f)
                {
                    endDir.Normalize();
                    float head = Mathf.Min(ArrowLen * (w > OutlineW - 0.01f ? 1.18f : 1f), Vector3.Distance(pts[pts.Count - 1], pts[pts.Count - 2]) * 0.9f + 0.001f);
                    line[line.Count - 1] = tip - endDir * head * 0.55f;
                }
                else arrow = false;
            }
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector3 a = line[i], b = line[i + 1]; a.y = y; b.y = y;
                Vector3 dir = b - a; dir.y = 0f; float len = dir.magnitude; if (len < 0.001f) continue;
                dir /= len; Vector3 n = new Vector3(-dir.z, 0f, dir.x) * half;
                Quad(d, a - n, a + n, b + n, b - n);
                if (i > 0) Disc(d, a, half);
            }
            if (arrow)
            {
                float k = w > OutlineW - 0.01f ? 1.18f : (w > CoreW + 0.01f ? 1.2f : 1.12f);
                float len = ArrowLen * k, wing = ArrowHalf * k;
                Vector3 t = tip; t.y = y; Vector3 side = new Vector3(-endDir.z, 0f, endDir.x);
                Vector3 baseC = t - endDir * len;
                Tri(d, t + endDir * (w > OutlineW - 0.01f ? 0.28f : 0f), baseC + side * wing, baseC - side * wing);
            }
            else { Vector3 e = pts[pts.Count - 1]; e.y = y; Disc(d, e, half); }
        }
    }
}
