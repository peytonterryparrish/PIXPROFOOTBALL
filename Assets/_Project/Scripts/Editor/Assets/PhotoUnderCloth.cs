using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 BODIES-2 (owner: "shoulder skin", Large arm skin 9.3 % of the shoulder zone newly outside the cloth): the import step above relaxes the skin weights of every mesh on its own (SmoothTransition /
    /// SmoothGroup), and the jersey cap / pads are weighted to the trunk (Spine2 0.64, clavicle 0.29) while the arm skin under them follows the arm (0.6-0.8), so with the arm raised the skin leaves the cloth.
    /// This runs LAST in <see cref="PhotoArmProportions.Apply"/>, per LOD, for the arm skin and the two compression sleeves:
    ///  * a skin vertex more than 2 mm under the jersey or the pads (within 0.36 m of a shoulder joint, the layer that covers it deepest) takes the skin weights of the 3 nearest vertices of that layer in proportion to its depth
    ///    (2 -> 8 mm) and is pushed up to 12 mm further under the cloth (hidden geometry: nothing visible moves at rest);
    ///  * that share is spread over the welded vertex graph of the skin (<see cref="SpreadHops"/> hops, about 1 cm each, falling linearly), so the weights turn from the cloth's to the arm's over a hand's width of skin instead of over
    ///    the few millimetres between "just inside" and "just outside" the cloth rim (that first version stretched the skin into spikes with the arm up);
    ///  * pad vertices within 5 cm of the jersey follow the jersey weights (the same rule as phr2_pads.pads_follow_jersey in Blender, redone here after the relaxation).
    /// Measured (BodyShots, 15 bodies, 8 poses x 3 times): arm skin newly outside the cloth 5.7 % of the shoulder zone -> 1.2 %; arm-up stretched edges +46 % (hidden skin under the cap turns with the cloth), see Docs/QA/v0130/bodies2.md.
    /// Switch: -pixNoUnderCloth restores the v0.12 weights. Cost: import time only.
    /// </summary>
    public static class PhotoUnderCloth
    {
        public static bool Enabled = true;
        public static float Push = 0.012f, ZoneNear = 0.28f, ZoneFar = 0.36f, DepthFrom = 0.002f, DepthTo = 0.008f, PadRadius = 0.05f;

        sealed class Grid
        {
            public Vector3[] p; Dictionary<long, List<int>> g = new Dictionary<long, List<int>>(); const float C = 0.03f;
            static long K(int x, int y, int z) { return ((long)(x + 4096) << 40) | ((long)(y + 4096) << 20) | (long)(z + 4096); }
            static int I(float f) { return Mathf.FloorToInt(f / C); }
            public Grid(Vector3[] pts) { p = pts; for (int i = 0; i < p.Length; i++) { long k = K(I(p[i].x), I(p[i].y), I(p[i].z)); List<int> l; if (!g.TryGetValue(k, out l)) { l = new List<int>(); g[k] = l; } l.Add(i); } }
            /// <summary>n nearest vertices within r (unsorted insertion, small n).</summary>
            public int Nearest(Vector3 q, float r, int n, int[] idx, float[] d2)
            {
                int cr = Mathf.CeilToInt(r / C), cx = I(q.x), cy = I(q.y), cz = I(q.z), cnt = 0; float r2 = r * r;
                for (int x = -cr; x <= cr; x++) for (int y = -cr; y <= cr; y++) for (int z = -cr; z <= cr; z++)
                {
                    List<int> l; if (!g.TryGetValue(K(cx + x, cy + y, cz + z), out l)) continue;
                    foreach (int i in l)
                    {
                        float d = (p[i] - q).sqrMagnitude; if (d > r2) continue;
                        int pos = cnt < n ? cnt : n; while (pos > 0 && d2[pos - 1] > d) { if (pos < n) { d2[pos] = d2[pos - 1]; idx[pos] = idx[pos - 1]; } pos--; }
                        if (pos < n) { d2[pos] = d; idx[pos] = i; if (cnt < n) cnt++; }
                    }
                }
                return cnt;
            }
        }

        static Transform FindDeep(Transform t, string name) { if (t.name == name) return t; for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; } return null; }
        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Share of the jersey's skin weights an arm vertex takes: 0 above 2 mm under the cloth, 1 below 8 mm, 0 beyond 0.36 m from the shoulder joint (full inside 0.28 m).</summary>
        public static float CoverAlpha(float depthInside, float distToJoint) { return Sm(DepthFrom, DepthTo, depthInside) * (1f - Sm(ZoneNear, ZoneFar, distToJoint)); }

        static void Add(Dictionary<Transform, float> d, Transform t, float w) { if (t == null || w <= 0f) return; float o; d.TryGetValue(t, out o); d[t] = o + w; }

        static Dictionary<Transform, float> Weights(SkinnedMeshRenderer smr, BoneWeight w)
        {
            var d = new Dictionary<Transform, float>(); var b = smr.bones;
            if (w.weight0 > 0f) Add(d, b[w.boneIndex0], w.weight0); if (w.weight1 > 0f) Add(d, b[w.boneIndex1], w.weight1);
            if (w.weight2 > 0f) Add(d, b[w.boneIndex2], w.weight2); if (w.weight3 > 0f) Add(d, b[w.boneIndex3], w.weight3);
            return d;
        }

        /// <summary>Writes the weights of vertex v (transform -> weight, top 4, normalised); bones missing from the renderer are appended (bind pose from the hierarchy).</summary>
        static void Store(SkinnedMeshRenderer smr, List<Transform> bones, List<Matrix4x4> bind, BoneWeight[] bw, int v, Dictionary<Transform, float> d)
        {
            var top = d.OrderByDescending(kv => kv.Value).Take(4).ToList(); float s = top.Sum(kv => kv.Value); if (s < 1e-6f) return;
            int[] id = new int[4]; float[] wt = new float[4];
            for (int i = 0; i < top.Count; i++)
            {
                int bi = bones.IndexOf(top[i].Key);
                if (bi < 0) { bi = bones.Count; bones.Add(top[i].Key); bind.Add(top[i].Key.worldToLocalMatrix * smr.transform.localToWorldMatrix); }
                id[i] = bi; wt[i] = top[i].Value / s;
            }
            var nb = new BoneWeight { boneIndex0 = id[0], weight0 = wt[0], boneIndex1 = id[1], weight1 = wt[1], boneIndex2 = id[2], weight2 = wt[2], boneIndex3 = id[3], weight3 = wt[3] };
            bw[v] = nb;
        }

        /// <summary>Spreads a 0..1 cover share outward over the welded vertex graph of a mesh: every vertex takes at least (best neighbour - 1 / hops), so the weights turn from the cloth's to the skin's over `hops` edges (about 1 cm each) instead of over the few millimetres between "just inside" and "just outside" the cloth rim, which stretched the skin into spikes with the arm up.</summary>
        public static float[] Spread(Vector3[] pos, int[] tris, float[] a, int hops)
        {
            int n = a.Length; var weld = new Dictionary<Vector3Int, int>(); var rep = new int[n]; int nr = 0;
            for (int v = 0; v < n; v++) { var key = new Vector3Int(Mathf.RoundToInt(pos[v].x * 2000f), Mathf.RoundToInt(pos[v].y * 2000f), Mathf.RoundToInt(pos[v].z * 2000f)); int r; if (!weld.TryGetValue(key, out r)) { r = nr++; weld[key] = r; } rep[v] = r; }
            var nbr = new List<int>[nr]; for (int i = 0; i < nr; i++) nbr[i] = new List<int>(6);
            for (int t = 0; t + 2 < tris.Length; t += 3) for (int e = 0; e < 3; e++) { int x = rep[tris[t + e]], y = rep[tris[t + (e + 1) % 3]]; if (x != y) { nbr[x].Add(y); nbr[y].Add(x); } }
            var s = new float[nr]; for (int v = 0; v < n; v++) s[rep[v]] = Mathf.Max(s[rep[v]], a[v]);
            float step = 1f / Mathf.Max(1, hops);
            for (int it = 0; it < hops; it++)
            {
                var ns = (float[])s.Clone();
                for (int r = 0; r < nr; r++) { float m = s[r]; foreach (int q in nbr[r]) m = Mathf.Max(m, s[q] - step); ns[r] = m; }
                s = ns;
            }
            var res = new float[n]; for (int v = 0; v < n; v++) res[v] = s[rep[v]]; return res;
        }

        /// <summary>Hops (about 1 cm each) over which the cloth weights fade out into the skin's own.</summary>
        public static int SpreadHops = 30;

        /// <summary>Under-layer parts that sit under the jersey / pads near the shoulder and must turn with the cloth: the arm skin and the two compression sleeves.</summary>
        static readonly string[] UnderParts = { "Arms", "SleeveL", "SleeveR" };

        public static string Apply(GameObject root, SkinnedMeshRenderer[] smrs)
        {
            if (!Enabled || Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoUnderCloth") >= 0) return "";   // switch: the v0.12 weights
            var rep = new System.Text.StringBuilder();
            var joints = new List<Vector3>();
            foreach (string side in new[] { "Left", "Right" }) { var a = FindDeep(root.transform, "rig:" + side + "Arm"); if (a != null) joints.Add(a.position); }
            if (joints.Count == 0) return "";
            int[] idx = new int[3]; float[] d2 = new float[3];
            for (int lod = 0; lod < 4; lod++)
            {
                var jer = smrs.FirstOrDefault(s => s.name == "Jersey_LOD" + lod);
                if (jer == null || jer.sharedMesh == null) continue;
                var jm = jer.sharedMesh; var jbw = jm.boneWeights; var jvs = jm.vertices; var jns = jm.normals;
                if (jbw.Length != jvs.Length || jns.Length != jvs.Length) continue;
                var jp = new Vector3[jvs.Length]; var jn = new Vector3[jvs.Length];
                for (int i = 0; i < jvs.Length; i++) { jp[i] = jer.transform.TransformPoint(jvs[i]); jn[i] = jer.transform.TransformDirection(jns[i]).normalized; }
                var jgrid = new Grid(jp);
                var jw = new Dictionary<Transform, float>[jvs.Length];
                Func<int, Dictionary<Transform, float>> jersey = i => jw[i] ?? (jw[i] = Weights(jer, jbw[i]));

                // ---- pads follow the jersey where they lie under it; the epaulet caps that stick out beyond the cloth keep their own (arm-following) weights
                var pads = smrs.FirstOrDefault(s => s.name == "Pads_LOD" + lod);
                Vector3[] pp = new Vector3[0], pn = new Vector3[0]; Dictionary<Transform, float>[] pw = new Dictionary<Transform, float>[0];
                if (pads != null && pads.sharedMesh != null)
                {
                    var pm = pads.sharedMesh; var pbw = pm.boneWeights; var pvs = pm.vertices; var pns = pm.normals;
                    if (pbw.Length == pvs.Length && pns.Length == pvs.Length)
                    {
                        var bones = pads.bones.ToList(); var bind = pm.bindposes.ToList(); int n0 = bones.Count, changed = 0;
                        for (int v = 0; v < pvs.Length; v++)
                        {
                            Vector3 p = pads.transform.TransformPoint(pvs[v]);
                            int c = jgrid.Nearest(p, PadRadius, 3, idx, d2); if (c == 0) continue;
                            var acc = new Dictionary<Transform, float>(); float ws = 0f;
                            for (int k = 0; k < c; k++)
                            {
                                float wk = 1f / (Mathf.Sqrt(d2[k]) + 0.004f); wk *= wk; ws += wk;
                                foreach (var kv in jersey(idx[k])) Add(acc, kv.Key, kv.Value * wk);
                            }
                            foreach (var key in acc.Keys.ToList()) acc[key] /= ws;
                            Store(pads, bones, bind, pbw, v, acc); changed++;
                        }
                        pm.boneWeights = pbw;
                        if (bones.Count != n0) { pm.bindposes = bind.ToArray(); pads.bones = bones.ToArray(); }
                        rep.Append(pads.name).Append(" follows jersey ").Append(changed).Append("v; ");
                        pp = new Vector3[pvs.Length]; pn = new Vector3[pvs.Length]; pw = new Dictionary<Transform, float>[pvs.Length];
                        for (int i = 0; i < pvs.Length; i++) { pp[i] = pads.transform.TransformPoint(pvs[i]); pn[i] = pads.transform.TransformDirection(pns[i]).normalized; pw[i] = Weights(pads, pbw[i]); }
                    }
                }

                // ---- the cloth = jersey and pads (a skin vertex counts as covered when EITHER layer covers it, like the metric; it takes the weights of the layer that covers it deepest)
                var pgrid = pp.Length > 0 ? new Grid(pp) : null;
                int[] idx2 = new int[3]; float[] e2 = new float[3];

                foreach (string part in UnderParts)
                {
                    var under = smrs.FirstOrDefault(s => s.name == part + "_LOD" + lod);
                    if (under == null || under.sharedMesh == null) continue;
                    var am = under.sharedMesh; var abw = am.boneWeights; var avs = am.vertices;
                    if (abw.Length != avs.Length) continue;
                    var bones = under.bones.ToList(); var bind = am.bindposes.ToList(); int n0 = bones.Count, changed = 0;
                    var wpos = new Vector3[avs.Length]; var raw = new float[avs.Length]; var usePadA = new bool[avs.Length]; var nrmA = new Vector3[avs.Length];
                    for (int v = 0; v < avs.Length; v++)
                    {
                        Vector3 p = wpos[v] = under.transform.TransformPoint(avs[v]);
                        float dj = joints.Min(j => Vector3.Distance(j, p)); if (dj > ZoneFar) continue;
                        int c = jgrid.Nearest(p, 0.08f, 3, idx, d2); int cp2 = pgrid != null ? pgrid.Nearest(p, 0.08f, 3, idx2, e2) : 0;
                        if (c == 0 && cp2 == 0) continue;
                        float dJ = c > 0 ? -Vector3.Dot(p - jp[idx[0]], jn[idx[0]]) : -1f, dP = cp2 > 0 ? -Vector3.Dot(p - pp[idx2[0]], pn[idx2[0]]) : -1f;
                        bool usePad = dP > dJ; float depth = usePad ? dP : dJ;
                        raw[v] = CoverAlpha(depth, dj); usePadA[v] = usePad; nrmA[v] = usePad ? pn[idx2[0]] : jn[idx[0]];
                    }
                    var spread = Spread(wpos, am.triangles, raw, SpreadHops);
                    for (int v = 0; v < avs.Length; v++)
                    {
                        float a = spread[v]; if (a < 0.02f) continue;
                        Vector3 p = wpos[v];
                        float dj = joints.Min(j => Vector3.Distance(j, p));
                        int c = jgrid.Nearest(p, 0.12f, 3, idx, d2); int cp2 = pgrid != null ? pgrid.Nearest(p, 0.12f, 3, idx2, e2) : 0;
                        if (c == 0 && cp2 == 0) continue;
                        bool usePad = cp2 > 0 && (c == 0 || usePadA[v] || raw[v] <= 0f && -Vector3.Dot(p - pp[idx2[0]], pn[idx2[0]]) > -Vector3.Dot(p - jp[idx[0]], jn[idx[0]]));
                        var acc = new Dictionary<Transform, float>(); float ws = 0f; var ow = Weights(under, abw[v]);
                        int cc = usePad ? cp2 : c; var ii = usePad ? idx2 : idx; var dd = usePad ? e2 : d2;
                        for (int k = 0; k < cc; k++)
                        {
                            float wk = 1f / (Mathf.Sqrt(dd[k]) + 0.004f); wk *= wk; ws += wk;
                            foreach (var kv in (usePad ? pw[ii[k]] : jersey(ii[k]))) Add(acc, kv.Key, kv.Value * wk);
                        }
                        var res = new Dictionary<Transform, float>();
                        foreach (var kv in ow) Add(res, kv.Key, kv.Value * (1f - a));
                        foreach (var kv in acc) Add(res, kv.Key, kv.Value / ws * a);
                        Store(under, bones, bind, abw, v, res);
                        if (raw[v] > 0f) avs[v] += under.transform.InverseTransformVector(-nrmA[v] * (Push * raw[v]));
                        changed++;
                    }
                    am.vertices = avs; am.RecalculateBounds(); am.boneWeights = abw;
                    if (bones.Count != n0) { am.bindposes = bind.ToArray(); under.bones = bones.ToArray(); }
                    rep.Append(under.name).Append(" under cloth ").Append(changed).Append("v; ");
                }
            }
            return rep.ToString();
        }
    }
}
