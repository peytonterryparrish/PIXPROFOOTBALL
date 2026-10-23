using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 BODIES: controlled before / after of the hip and shoulder helper bones. The source FBX is copied unprocessed next to the re-imported (fixed) one, both are posed the same way
    /// (a deep hip flexion with the legs kept vertical; an arm raised overhead), the helper bones of the fixed one are driven exactly like BodyHelperBones does, the pants / jersey / pads are baked and measured:
    /// seat: the largest growth of a seat vertex's distance from the hip joint (pelvis frame) against the unposed body; shoulder: stretch of the edges around the shoulder joint against the unposed body.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.SkinFixCheck.Run -pixBodyProbeOut dir
    /// </summary>
    public static class SkinFixCheck
    {
        const string Dir = "Assets/_Licensed/PhotorealPlayer/Meshes/";

        static Transform Find(Transform t, string n) { if (t.name == n) return t; for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), n); if (r != null) return r; } return null; }

        public static void Run()
        {
            string outDir = BodyProbe.Arg("-pixBodyProbeOut", "D:/PIXFootball/Renders/bodies_v0100/final");
            var sb = new StringBuilder("# Hip and shoulder helper bones, controlled before / after (source FBX copy vs the re-imported body, same poses)\n\n");
            AssetDatabase.CreateFolder("Assets", "TempSkinCheck");
            foreach (string body in new[] { "PHR_Large_Stocky", "PHR_Medium_Runner" })
            {
                string copy = "Assets/TempSkinCheck/Orig_" + body + ".fbx";
                AssetDatabase.CopyAsset(Dir + body + ".fbx", copy);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceUpdate);
                var before = Measure(Inst(copy, false, 0, 0, 0), false);
                var cols = new List<Dictionary<string, float>>(); var names = new List<string>();
                int vi = 0;
                foreach (var vv in new[] { new Vector4(0f, 0f, 40f, 1.5f) })
                {
                    string c2 = "Assets/TempSkinCheck/V" + (vi++) + "_" + body + ".fbx";
                    AssetDatabase.CopyAsset(copy, c2); AssetDatabase.ImportAsset(c2, ImportAssetOptions.ForceUpdate);
                    DiagOn = false; Diag.AppendLine("=== " + body + " variant " + (vi - 1)); cols.Add(Measure(Inst(c2, true, vv.x, vv.y, (int)vv.z, vv.w), true)); DiagOn = false; names.Add("smooth " + (int)vv.z + " rad x" + vv.w);
                }
                sb.AppendLine("## " + body + "   (columns: no helpers | " + string.Join(" | ", names.ToArray()) + ")");
                foreach (var k in before.Keys.OrderBy(x => x)) { sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,-62} {1,7:0.000}", k, before[k])); foreach (var c in cols) sb.Append(string.Format(CultureInfo.InvariantCulture, " {0,7:0.000}", c.ContainsKey(k) ? c[k] : float.NaN)); sb.AppendLine(); }
                sb.AppendLine();
            }
            File.WriteAllText(outDir + "/skin_helper_before_after.txt", sb.ToString()); File.WriteAllText(outDir + "/skin_worst_edges.txt", Diag.ToString());
            AssetDatabase.DeleteAsset("Assets/TempSkinCheck");
            Debug.Log("[SkinFixCheck] done");
            EditorApplication.Exit(0);
        }

        public static bool DiagOn; public static StringBuilder Diag = new StringBuilder();
        static void Worst(Part p, Vector3[] vs, HashSet<int> set, string label, Vector3 joint)
        {
            var l = new List<KeyValuePair<float, int>>();
            for (int e = 0; e < p.A.Length; e++)
            {
                if (!set.Contains(p.A[e]) || !set.Contains(p.B[e])) continue;
                float l0 = (p.Rest[p.A[e]] - p.Rest[p.B[e]]).magnitude; if (l0 < 0.02f) continue;
                l.Add(new KeyValuePair<float, int>(p.Smr.transform.TransformVector(vs[p.A[e]] - vs[p.B[e]]).magnitude / l0, e));
            }
            l.Sort((x, y) => y.Key.CompareTo(x.Key));
            var bw = p.Smr.sharedMesh.boneWeights; var bones = p.Smr.bones;
            Func<int, string> W = v => { var w = bw[v]; return bones[w.boneIndex0].name.Replace("rig:", "") + ":" + w.weight0.ToString("0.00") + (w.weight1 > 0 ? " " + bones[w.boneIndex1].name.Replace("rig:", "") + ":" + w.weight1.ToString("0.00") : "") + (w.weight2 > 0 ? " " + bones[w.boneIndex2].name.Replace("rig:", "") + ":" + w.weight2.ToString("0.00") : ""); };
            Diag.AppendLine("-- " + label + " " + p.Smr.name + " edges " + l.Count);
            for (int i = 0; i < Math.Min(10, l.Count); i++)
            {
                int e = l[i].Value, a = p.A[e], b = p.B[e];
                Diag.AppendLine(string.Format(CultureInfo.InvariantCulture, "  x{0:0.00} rest {1:0.000} m, a {2:0.00} m from joint [{3}] | b {4:0.00} m [{5}]", l[i].Key, (p.Rest[a] - p.Rest[b]).magnitude, (p.Rest[a] - joint).magnitude, W(a), (p.Rest[b] - joint).magnitude, W(b)));
            }
        }

        sealed class Part { public SkinnedMeshRenderer Smr; public Mesh Baked; public Vector3[] Rest; public int[] A, B; public int[] Seat, Shoulder; }

        /// <summary>Instance of the (copied, so disposable) model at the origin; peak >= 0 runs the import step's arm lengthening and helper bones on it with that helper weight.</summary>
        static GameObject Inst(string path, bool on, float hip, float arm, int smooth, float rad = 1f)
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
            if (on) { PhotoArmProportions.HelperPeakHip = hip; PhotoArmProportions.HelperPeakArm = arm; PhotoArmProportions.SmoothIters = smooth; PhotoArmProportions.SmoothRadiusHip = 0.30f * rad; PhotoArmProportions.SmoothRadiusArm = 0.22f * rad; PhotoArmProportions.Apply(go, PhotoArmProportions.UpperToForearm); }
            return go;
        }

        static Dictionary<string, float> Measure(GameObject go, bool helpers)
        {
            var res = new Dictionary<string, float>();
            try
            {
                var hips = Find(go.transform, "rig:Hips"); var lul = Find(go.transform, "rig:LeftUpLeg"); var rul = Find(go.transform, "rig:RightUpLeg");
                var larm = Find(go.transform, "rig:LeftArm"); var lfore = Find(go.transform, "rig:LeftForeArm");
                Vector3 across = rul.position - lul.position; across.y = 0f; Vector3 fwd = Vector3.Cross(across.normalized, Vector3.up), right = Vector3.Cross(Vector3.up, fwd);
                var parts = new List<Part>();
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!(smr.name == "Pants_LOD0" || smr.name == "Jersey_LOD0" || smr.name == "Pads_LOD0" || smr.name == "Arms_LOD0")) continue;
                    var p = new Part { Smr = smr, Baked = new Mesh() };
                    smr.BakeMesh(p.Baked, true);
                    var vs = p.Baked.vertices; p.Rest = new Vector3[vs.Length];
                    for (int i = 0; i < vs.Length; i++) p.Rest[i] = smr.transform.TransformPoint(vs[i]);
                    var tris = p.Baked.triangles; var seen = new HashSet<long>(); var A = new List<int>(); var B = new List<int>();
                    for (int t = 0; t + 2 < tris.Length; t += 3) for (int e = 0; e < 3; e++) { int a = tris[t + e], b = tris[t + (e + 1) % 3]; if (a > b) { int x = a; a = b; b = x; } if (seen.Add(((long)a << 32) | (uint)b)) { A.Add(a); B.Add(b); } }
                    p.A = A.ToArray(); p.B = B.ToArray();
                    var seat = new List<int>(); var sh = new List<int>();
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3 d = p.Rest[i] - hips.position;
                        if (d.magnitude < 0.32f && Vector3.Dot(d, fwd) < 0.04f && d.y > -0.28f) seat.Add(i);
                        if ((p.Rest[i] - larm.position).magnitude < 0.26f) sh.Add(i);
                    }
                    p.Seat = seat.ToArray(); p.Shoulder = sh.ToArray(); parts.Add(p);
                }
                var drv = new[] { Tuple.Create("rig:LeftUpLegHelper", "rig:LeftUpLeg"), Tuple.Create("rig:RightUpLegHelper", "rig:RightUpLeg"), Tuple.Create("rig:LeftArmHelper", "rig:LeftArm"), Tuple.Create("rig:RightArmHelper", "rig:RightArm") };
                var hb = new List<Transform[]>(); var hbind = new List<Quaternion[]>();
                if (helpers) foreach (var d in drv) { var h = Find(go.transform, d.Item1); var t = Find(go.transform, d.Item2); if (h != null && t != null) { hb.Add(new[] { h, t }); hbind.Add(new[] { h.localRotation, t.localRotation }); } }
                Action drive = () => { for (int i = 0; i < hb.Count; i++) hb[i][0].localRotation = hbind[i][0] * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(hbind[i][1]) * hb[i][1].localRotation, 0.5f); };

                // ---- seat: pelvis pitched over the thighs
                var legRot = new[] { lul.rotation, rul.rotation };
                foreach (float theta in new[] { 70f, 100f })
                {
                    var saved = go.GetComponentsInChildren<Transform>(true).Select(t => new { t, p = t.localPosition, r = t.localRotation }).ToList();
                    hips.rotation = Quaternion.AngleAxis(theta, right) * hips.rotation; lul.rotation = legRot[0]; rul.rotation = legRot[1]; drive();
                    float worst = 0f, sumGrow = 0f; int n = 0;
                    foreach (var p in parts.Where(q => q.Smr.name == "Pants_LOD0"))
                    {
                        p.Smr.BakeMesh(p.Baked, true); var vs = p.Baked.vertices;
                        foreach (int i in p.Seat)
                        {
                            Vector3 now = p.Smr.transform.TransformPoint(vs[i]);
                            // pelvis frame: the hip joint centre of the nearer leg and the pelvis rotation
                            Vector3 restLocal = Quaternion.Inverse(Quaternion.identity) * (p.Rest[i] - hips.position);
                            float g = (Quaternion.Inverse(hips.rotation) * (now - hips.position)).magnitude / Mathf.Max(0.02f, restLocal.magnitude);
                            worst = Mathf.Max(worst, g); sumGrow += g; n++;
                        }
                    }
                    res["seat growth of distance from hips, " + theta + " deg flexion, worst"] = worst;
                    res["seat growth of distance from hips, " + theta + " deg flexion, mean"] = n > 0 ? sumGrow / n : 0f;
                    foreach (var p in parts.Where(q => q.Smr.name == "Pants_LOD0"))
                    {
                        p.Smr.BakeMesh(p.Baked, true); var vs = p.Baked.vertices; var set = new HashSet<int>(p.Seat); var ratios = new List<float>(); var vis = new List<float>();
                        for (int e = 0; e < p.A.Length; e++)
                        {
                            if (!set.Contains(p.A[e]) || !set.Contains(p.B[e])) continue;
                            float l0 = (p.Rest[p.A[e]] - p.Rest[p.B[e]]).magnitude; if (l0 < 0.004f) continue;
                            ratios.Add(p.Smr.transform.TransformVector(vs[p.A[e]] - vs[p.B[e]]).magnitude / l0);  if (l0 >= 0.02f) vis.Add(ratios[ratios.Count - 1]);
                        }
                        if (ratios.Count > 50) { ratios.Sort(); res["seat Pants edge stretch p95, " + theta + " deg flexion"] = ratios[(int)(ratios.Count * 0.95f)]; res["seat Pants edge stretch p99, " + theta + " deg flexion"] = ratios[(int)(ratios.Count * 0.99f)]; res["seat Pants edge shrink p01, " + theta + " deg flexion"] = ratios[(int)(ratios.Count * 0.01f)]; }
                        if (vis.Count > 30) { vis.Sort(); res["seat Pants VISIBLE edges (>=2cm) stretch p99, " + theta + " deg"] = vis[(int)(vis.Count * 0.99f)]; }
if (DiagOn) Worst(p, vs, set, "seat " + theta, hips.position);
                    }
                    foreach (var s in saved) { s.t.localPosition = s.p; s.t.localRotation = s.r; }
                }
                // ---- shoulder: left arm overhead
                foreach (float theta in new[] { 150f })
                {
                    var saved = go.GetComponentsInChildren<Transform>(true).Select(t => new { t, p = t.localPosition, r = t.localRotation }).ToList();
                    Vector3 handBefore = lfore.position;
                    larm.rotation = Quaternion.AngleAxis(theta, fwd) * larm.rotation; if (lfore.position.y < handBefore.y) larm.rotation = Quaternion.AngleAxis(-2f * theta, fwd) * larm.rotation;
                    drive();
                    foreach (var p in parts)
                    {
                        p.Smr.BakeMesh(p.Baked, true); var vs = p.Baked.vertices;
                        var set = new HashSet<int>(p.Shoulder); var ratios = new List<float>(); var vis = new List<float>();
                        for (int e = 0; e < p.A.Length; e++)
                        {
                            if (!set.Contains(p.A[e]) || !set.Contains(p.B[e])) continue;
                            float l0 = (p.Rest[p.A[e]] - p.Rest[p.B[e]]).magnitude; if (l0 < 0.004f) continue;
                            ratios.Add((p.Smr.transform.TransformVector(vs[p.A[e]] - vs[p.B[e]])).magnitude / l0);  if (l0 >= 0.02f) vis.Add(ratios[ratios.Count - 1]);
                        }
                        if (vis.Count > 30) { vis.Sort(); res["shoulder " + p.Smr.name.Replace("_LOD0", "") + " VISIBLE edges (>=2cm) stretch p99, " + theta + " deg"] = vis[(int)(vis.Count * 0.99f)]; }
if (DiagOn) Worst(p, vs, set, "shoulder " + theta, larm.position);
                        if (ratios.Count < 50) continue; ratios.Sort();
                        string nm = p.Smr.name.Replace("_LOD0", "");
                        res["shoulder " + nm + " edge stretch p95, arm " + theta + " deg overhead"] = ratios[(int)(ratios.Count * 0.95f)];
                        res["shoulder " + nm + " edge stretch p99, arm " + theta + " deg overhead"] = ratios[(int)(ratios.Count * 0.99f)];
                        res["shoulder " + nm + " edge shrink p01, arm " + theta + " deg overhead"] = ratios[(int)(ratios.Count * 0.01f)];
                    }
                    foreach (var s in saved) { s.t.localPosition = s.p; s.t.localRotation = s.r; }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            return res;
        }
    }
}
