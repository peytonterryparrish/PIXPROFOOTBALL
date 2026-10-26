using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 BODIES (playtest note 9, "human proportions"): the 15 photoreal bodies have an upper arm of 0.24 to 0.26 m against a forearm of 0.25 to 0.27 m (ratio 0.9 to 1.05; a human's is about 1.25, an upper arm of 0.31 to 0.35 m on a 1.9 m man).
    /// The short upper arm made every arm pose stubby and was the reason a lineman could not put a hand on the turf without folding himself in half (shoulder to wrist 0.50 m).
    /// This import step lengthens the upper arm of every PHR_*.fbx (all 15 bases and sizes, every LOD, the pads, jersey sleeves, gloves) to <see cref="UpperToForearm"/> times the forearm:
    /// the elbow, forearm, hand and fingers move out along the upper arm axis, and every vertex that follows an arm bone is moved with them (stretched between the shoulder and the elbow, carried rigidly beyond it),
    /// then the bind poses of the moved bones are rebuilt. Legs, torso, head and every texture are untouched; Humanoid clips retarget by rotation so they need nothing.
    /// It runs as an AssetPostprocessor (so every checkout and every build gets it from the source FBX) and <see cref="ReimportAll"/> / the static check below re-imports the bodies once when <see cref="Version"/> changes.
    /// </summary>
    public sealed class PhotoArmProportions : AssetPostprocessor
    {
        /// <summary>Bump to re-import the bodies (the importer version hash and the one-time check below).</summary>
        public const int Version = 6;   // v0.13.0 BODIES-2: 6 = PhotoUnderCloth (skin under the jersey and the pads follow the cloth weights)
        /// <summary>Upper arm length over forearm length after the step (human 1.2 to 1.3).</summary>
        public static float UpperToForearm = 1.25f;
        /// <summary>On since v0.10.0 round 2: the import step, the 15 prefabs (SyncPrefabs) and the helper-bone driver (BodyHelperBones) go together; BodiesTests checks that every prefab bone sits where its mesh's bind pose says.</summary>
        public static bool Enabled = true;

        public override uint GetVersion() { return Enabled ? (uint)(Version * 31 + 1) : 0u; }

        void OnPostprocessModel(GameObject root)
        {
            if (!Enabled) return;
            string p = assetPath.Replace('\\', '/');
            if ((!p.Contains("/PhotorealPlayer/Meshes/PHR_") && !p.Contains("/_BodiesStage/Meshes/PHR_")) || !p.EndsWith(".fbx")) return;   // v0.13.0 BODIES-2: the private stage folder gets the same import step as the live bodies
            string report = Apply(root, UpperToForearm);
            if (report.Length > 0) Debug.Log("[PhotoArm] " + p + ": " + report);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static void Collect(Transform t, HashSet<Transform> into) { into.Add(t); for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), into); }

        /// <summary>Lengthens both upper arms of a model hierarchy; returns a one-line report ("" when nothing was done). Idempotent per import (it always starts from the source FBX).</summary>
        public static string Apply(GameObject root, float ratio)
        {
            var report = new System.Text.StringBuilder();
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (string side in new[] { "Left", "Right" })
            {
                var arm = FindDeep(root.transform, "rig:" + side + "Arm");
                var fore = FindDeep(root.transform, "rig:" + side + "ForeArm");
                var hand = FindDeep(root.transform, "rig:" + side + "Hand");
                if (arm == null || fore == null || hand == null) return "";
                Vector3 S = arm.position, E = fore.position, W = hand.position;
                float L = Vector3.Distance(S, E), F = Vector3.Distance(E, W);
                float want = F * ratio;
                if (L >= want - 1e-4f) continue;
                float d = want - L;
                Vector3 a = (E - S) / L;
                Vector3 T = a * d;
                var moved = new HashSet<Transform>(); Collect(fore, moved);
                var armBones = new HashSet<Transform>(moved); armBones.Add(arm);
                var done = new HashSet<Mesh>();
                foreach (var smr in smrs)
                {
                    var mesh = smr.sharedMesh; if (mesh == null || !done.Add(mesh)) continue;
                    var bones = smr.bones; if (bones == null || bones.Length == 0) continue;
                    var bw = mesh.boneWeights; var vs = mesh.vertices;
                    if (bw.Length != vs.Length) continue;
                    bool isArm0 = false;
                    var isArm = new bool[bones.Length]; for (int i = 0; i < bones.Length; i++) { isArm[i] = bones[i] != null && armBones.Contains(bones[i]); isArm0 |= isArm[i]; }
                    if (!isArm0) continue;
                    var tr = smr.transform; int changed = 0;
                    for (int v = 0; v < vs.Length; v++)
                    {
                        var w = bw[v];
                        float aw = 0f;
                        if (w.weight0 > 0f && isArm[w.boneIndex0]) aw += w.weight0;
                        if (w.weight1 > 0f && isArm[w.boneIndex1]) aw += w.weight1;
                        if (w.weight2 > 0f && isArm[w.boneIndex2]) aw += w.weight2;
                        if (w.weight3 > 0f && isArm[w.boneIndex3]) aw += w.weight3;
                        if (aw <= 0f) continue;
                        Vector3 pw = tr.TransformPoint(vs[v]);
                        float t = Mathf.Clamp01(Vector3.Dot(pw - S, a) / L);
                        // the shoulder end stays put, the elbow end moves by the whole extension: a smooth stretch of the upper arm, a rigid carry of the forearm and hand
                        float k = t * t * (3f - 2f * t) * 0.35f + t * 0.65f;
                        Vector3 shift = T * (k * Mathf.Clamp01(aw));
                        vs[v] += tr.InverseTransformVector(shift); changed++;
                    }
                    mesh.vertices = vs; mesh.RecalculateBounds();

                    report.Append(smr.name).Append(' ').Append(changed).Append("v; ");
                }
                // move the bones (the forearm carries the hand and the fingers)
                fore.position += T;
                foreach (var smr in smrs)
                {
                    var mesh = smr.sharedMesh; if (mesh == null) continue;
                    var bones = smr.bones; if (bones == null) continue;
                    var bp = mesh.bindposes; if (bp.Length != bones.Length) continue;
                    bool any = false;
                    for (int i = 0; i < bones.Length; i++)
                        if (bones[i] != null && moved.Contains(bones[i])) { bp[i] = bones[i].worldToLocalMatrix * smr.transform.localToWorldMatrix; any = true; }
                    if (any) mesh.bindposes = bp;
                }
                report.Insert(0, side + " upper arm " + L.ToString("0.000") + " -> " + want.ToString("0.000") + " m | ");
            }
            report.Append(AddHelpers(root, smrs));
            report.Append(PhotoUnderCloth.Apply(root, smrs));   // v0.13.0 BODIES-2
            return report.ToString();
        }

        // ------------------------------------------------------------------------------------------ helper bones (hip and shoulder)
        /// <summary>Half-rotation helper bones: one per hip and one per shoulder, parented to the driven bone's parent, at its joint. A runtime driver (BodyHelperBones) turns each by half of the driven bone's rotation.</summary>
        public static readonly string[] HelperNames = { "rig:LeftUpLegHelper", "rig:RightUpLegHelper", "rig:LeftArmHelper", "rig:RightArmHelper" };
        public static readonly string[] HelperDriven = { "rig:LeftUpLeg", "rig:RightUpLeg", "rig:LeftArm", "rig:RightArm" };
        static readonly string[][] HelperParents = { new[] { "rig:Hips" }, new[] { "rig:Hips" }, new[] { "rig:LeftShoulder", "rig:Spine2", "rig:Spine1" }, new[] { "rig:RightShoulder", "rig:Spine2", "rig:Spine1" } };
        /// <summary>Weight share of the helper at the middle of a transition zone (the pelvis / thigh and the chest / arm blends): at 0.8 the seat and the armpit move by half of the joint's rotation there instead of collapsing or stretching.</summary>
        public static float HelperPeakHip = 0.0f, HelperPeakArm = 0.0f;
        /// <summary>Weight smoothing across the pelvis / thigh and chest / arm transitions: the driven-bone share of every vertex within the radius is relaxed toward its neighbours (Iters rounds), widening the narrow source blend so the rotation is spread over more vertices.</summary>
        public static int GroupIters = 30; public static int SmoothIters = 40; public static float SmoothRadiusHip = 0.45f, SmoothRadiusArm = 0.33f;

        static string AddHelpers(GameObject root, SkinnedMeshRenderer[] smrs)
        {
            var rpt = new System.Text.StringBuilder();
            var helpers = new Transform[HelperNames.Length];
            for (int h = 0; h < helpers.Length; h++)
            {
                var driven = FindDeep(root.transform, HelperDriven[h]); if (driven == null || driven.parent == null) return "";
                var ex = FindDeep(root.transform, HelperNames[h]);
                if (ex == null)
                {
                    var go = new GameObject(HelperNames[h]); ex = go.transform; ex.SetParent(driven.parent, false);
                    ex.localPosition = driven.localPosition; ex.localRotation = driven.localRotation; ex.localScale = driven.localScale;
                }
                helpers[h] = ex;
            }
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh; if (mesh == null) continue;
                var bones = smr.bones; var bw = mesh.boneWeights; if (bones == null || bw.Length != mesh.vertexCount || bones.Length != mesh.bindposes.Length) continue;
                var newBones = new List<Transform>(bones); var newBp = new List<Matrix4x4>(mesh.bindposes);
                int changed = 0; bool smoothed = false;
                for (int h = 0; h < helpers.Length; h++)
                {
                    int drivenIdx = System.Array.IndexOf(bones, FindDeep(root.transform, HelperDriven[h])); if (drivenIdx < 0) continue;
                    var pidx = new List<int>(); foreach (string pn in HelperParents[h]) { int pi = System.Array.IndexOf(bones, FindDeep(root.transform, pn)); if (pi >= 0) pidx.Add(pi); }
                    if (pidx.Count == 0) continue;
                    if (SmoothIters > 0) { SmoothTransition(smr, mesh, bw, drivenIdx, pidx, FindDeep(root.transform, HelperDriven[h]).position, h < 2 ? SmoothRadiusHip : SmoothRadiusArm, SmoothIters); smoothed = true; }
                    int helperIdx = -1; int n = 0;
                    for (int v = 0; v < bw.Length; v++)
                    {
                        var e = new List<KeyValuePair<int, float>>(4);
                        var w = bw[v];
                        if (w.weight0 > 0f) e.Add(new KeyValuePair<int, float>(w.boneIndex0, w.weight0));
                        if (w.weight1 > 0f) e.Add(new KeyValuePair<int, float>(w.boneIndex1, w.weight1));
                        if (w.weight2 > 0f) e.Add(new KeyValuePair<int, float>(w.boneIndex2, w.weight2));
                        if (w.weight3 > 0f) e.Add(new KeyValuePair<int, float>(w.boneIndex3, w.weight3));
                        float wD = 0f, wP = 0f; foreach (var kv in e) { if (kv.Key == drivenIdx) wD += kv.Value; else if (pidx.Contains(kv.Key)) wP += kv.Value; }
                        if (wD < 0.03f || wP < 0.03f) continue;
                        float T = wD + wP, s = wD / T, hw = T * (h < 2 ? HelperPeakHip : HelperPeakArm) * 4f * s * (1f - s);
                        if (hw < 0.01f) continue;
                        if (helperIdx < 0) { helperIdx = newBones.Count; newBones.Add(helpers[h]); newBp.Add(helpers[h].worldToLocalMatrix * smr.transform.localToWorldMatrix); }
                        var res = new List<KeyValuePair<int, float>>(5);
                        foreach (var kv in e) res.Add(new KeyValuePair<int, float>(kv.Key, (kv.Key == drivenIdx || pidx.Contains(kv.Key)) ? kv.Value * (T - hw) / T : kv.Value));
                        res.Add(new KeyValuePair<int, float>(helperIdx, hw));
                        float total = 0f; foreach (var kv in e) total += kv.Value;
                        res.Sort((a2, b2) => b2.Value.CompareTo(a2.Value)); if (res.Count > 4) res.RemoveRange(4, res.Count - 4);
                        float sum = 0f; foreach (var kv in res) sum += kv.Value; float k = sum > 1e-6f ? total / sum : 1f;
                        var nb = new BoneWeight();
                        nb.boneIndex0 = res[0].Key; nb.weight0 = res[0].Value * k;
                        if (res.Count > 1) { nb.boneIndex1 = res[1].Key; nb.weight1 = res[1].Value * k; }
                        if (res.Count > 2) { nb.boneIndex2 = res[2].Key; nb.weight2 = res[2].Value * k; }
                        if (res.Count > 3) { nb.boneIndex3 = res[3].Key; nb.weight3 = res[3].Value * k; }
                        bw[v] = nb; n++;
                    }
                    changed += n;
                }
                if (GroupIters > 0)
                {
                    var hipsT = FindDeep(root.transform, "rig:Hips"); var lul = FindDeep(root.transform, "rig:LeftUpLeg"); var rul = FindDeep(root.transform, "rig:RightUpLeg");
                    if (hipsT != null && lul != null && rul != null)
                    {
                        var g = new List<int>(); foreach (var t in new[] { hipsT, lul, rul }) { int i = System.Array.IndexOf(bones, t); if (i >= 0) g.Add(i); }
                        Vector3 hp = hipsT.position, across = (rul.position - lul.position).normalized;
                        if (g.Count == 3) { SmoothGroup(smr, mesh, bw, g.ToArray(), p => (p - hp).magnitude < 0.34f && Mathf.Abs(Vector3.Dot(p - hp, across)) < 0.12f, GroupIters); smoothed = true; }
                    }
                    if (smr.name.StartsWith("Arms"))
                        foreach (string side in new[] { "Left", "Right" })
                        {
                            var arm = FindDeep(root.transform, "rig:" + side + "Arm"); var g = new List<int>();
                            foreach (string bn in new[] { "rig:" + side + "Arm", "rig:" + side + "Shoulder", "rig:Spine1", "rig:Spine2" }) { int i = System.Array.IndexOf(bones, FindDeep(root.transform, bn)); if (i >= 0) g.Add(i); }
                            if (arm != null && g.Count == 4) { Vector3 ap = arm.position; SmoothGroup(smr, mesh, bw, g.ToArray(), p => (p - ap).magnitude < 0.33f, GroupIters); smoothed = true; }
                        }
                }
                if (smoothed && newBones.Count == bones.Length) { mesh.boneWeights = bw; rpt.Append(smr.name).Append(" smoothed; "); }
                if (newBones.Count != bones.Length) { mesh.boneWeights = bw; mesh.bindposes = newBp.ToArray(); smr.bones = newBones.ToArray(); rpt.Append(smr.name).Append(" +").Append(newBones.Count - bones.Length).Append("b/").Append(changed).Append("v; "); }
            }
            return rpt.ToString();
        }

        /// <summary>
        /// Relaxes the share s = driven / (driven + parents) of every vertex within radius of the joint toward the mean of its welded neighbours (iters rounds, half-way each), keeping each vertex's other bones.
        /// Vertices that are not mostly on the driven bone or its parents (the other leg, the head) are neither moved nor used as neighbours, so the two legs never mix.
        /// </summary>
        static void SmoothTransition(SkinnedMeshRenderer smr, Mesh mesh, BoneWeight[] bw, int driven, List<int> parents, Vector3 joint, float radius, int iters)
        {
            int n = bw.Length; var vs = mesh.vertices; var tris = mesh.triangles;
            var weld = new Dictionary<Vector3Int, int>(); var rep = new int[n]; int nr = 0;
            var pos = new Vector3[n];
            for (int v = 0; v < n; v++)
            {
                pos[v] = smr.transform.TransformPoint(vs[v]);
                var key = new Vector3Int(Mathf.RoundToInt(pos[v].x * 2000f), Mathf.RoundToInt(pos[v].y * 2000f), Mathf.RoundToInt(pos[v].z * 2000f));
                int r; if (!weld.TryGetValue(key, out r)) { r = nr++; weld[key] = r; }
                rep[v] = r;
            }
            var nbr = new HashSet<int>[nr]; for (int i = 0; i < nr; i++) nbr[i] = new HashSet<int>();
            for (int t = 0; t + 2 < tris.Length; t += 3) for (int e = 0; e < 3; e++) { int a = rep[tris[t + e]], b = rep[tris[t + (e + 1) % 3]]; if (a != b) { nbr[a].Add(b); nbr[b].Add(a); } }
            var sum = new float[nr]; var cnt = new int[nr]; var valid = new bool[nr]; var zone = new bool[nr];
            Func<BoneWeight, int, float> wOf = (w, b) => (w.boneIndex0 == b ? w.weight0 : 0f) + (w.boneIndex1 == b ? w.weight1 : 0f) + (w.boneIndex2 == b ? w.weight2 : 0f) + (w.boneIndex3 == b ? w.weight3 : 0f);
            var tAcc = new float[nr]; var dAcc = new float[nr];
            for (int v = 0; v < n; v++)
            {
                float wD = wOf(bw[v], driven), wP = 0f; foreach (int p in parents) wP += wOf(bw[v], p);
                tAcc[rep[v]] += wD + wP; dAcc[rep[v]] += wD; cnt[rep[v]]++;
                if ((pos[v] - joint).magnitude < radius) zone[rep[v]] = true;
            }
            var s = new float[nr];
            for (int r = 0; r < nr; r++) { valid[r] = cnt[r] > 0 && tAcc[r] / cnt[r] >= 0.5f; s[r] = tAcc[r] > 1e-5f ? dAcc[r] / tAcc[r] : 0f; zone[r] &= valid[r]; }
            for (int it = 0; it < iters; it++)
            {
                var ns = (float[])s.Clone();
                for (int r = 0; r < nr; r++)
                {
                    if (!zone[r]) continue;
                    float m = 0f; int k = 0; foreach (int q in nbr[r]) if (valid[q]) { m += s[q]; k++; }
                    if (k > 0) ns[r] = Mathf.Lerp(s[r], m / k, 0.5f);
                }
                s = ns;
            }
            for (int v = 0; v < n; v++)
            {
                int r = rep[v]; if (!zone[r]) continue;
                var w = bw[v]; float wD = wOf(w, driven), wP = 0f; foreach (int p in parents) wP += wOf(w, p);
                float T = wD + wP; if (T < 1e-4f) continue;
                float nd = T * s[r], np = T - nd;
                var list = new List<KeyValuePair<int, float>>(5); float total = 0f;
                Action<int, float> add = (b, x) => { if (x > 1e-5f) { list.Add(new KeyValuePair<int, float>(b, x)); } };
                add(w.boneIndex0, w.weight0); add(w.boneIndex1, w.weight1); add(w.boneIndex2, w.weight2); add(w.boneIndex3, w.weight3);
                foreach (var kv in list) total += kv.Value;
                var res = new List<KeyValuePair<int, float>>(5); bool hasD = false;
                foreach (var kv in list)
                {
                    if (kv.Key == driven) { res.Add(new KeyValuePair<int, float>(kv.Key, nd)); hasD = true; }
                    else if (parents.Contains(kv.Key)) res.Add(new KeyValuePair<int, float>(kv.Key, wP > 1e-5f ? kv.Value * np / wP : 0f));
                    else res.Add(kv);
                }
                if (!hasD) res.Add(new KeyValuePair<int, float>(driven, nd));
                if (wP <= 1e-5f && np > 1e-5f) res.Add(new KeyValuePair<int, float>(parents[0], np));
                res.Sort((x, y) => y.Value.CompareTo(x.Value)); if (res.Count > 4) res.RemoveRange(4, res.Count - 4);
                float rs = 0f; foreach (var kv in res) rs += kv.Value; if (rs < 1e-6f) continue; float k2 = total / rs;
                var nb = new BoneWeight();
                nb.boneIndex0 = res[0].Key; nb.weight0 = res[0].Value * k2;
                if (res.Count > 1) { nb.boneIndex1 = res[1].Key; nb.weight1 = res[1].Value * k2; }
                if (res.Count > 2) { nb.boneIndex2 = res[2].Key; nb.weight2 = res[2].Value * k2; }
                if (res.Count > 3) { nb.boneIndex3 = res[3].Key; nb.weight3 = res[3].Value * k2; }
                bw[v] = nb;
            }
        }

        /// <summary>
        /// Relaxes the whole weight split between a group of bones (all of them at once, not one driven bone against its parents) over the vertices the zone test accepts: the shares of the group's bones are averaged with the welded neighbours' (iters rounds, half-way each)
        /// and each vertex keeps its own total on the group and its weights on every other bone. Used for the gluteal cleft (hips + both thighs) and the armpit of the bare-arm mesh (arm, shoulder, two spine bones), where the two-bone pass leaves a few edges that stretch.
        /// </summary>
        static void SmoothGroup(SkinnedMeshRenderer smr, Mesh mesh, BoneWeight[] bw, int[] group, Func<Vector3, bool> inZone, int iters)
        {
            int n = bw.Length, K = group.Length; var vs = mesh.vertices; var tris = mesh.triangles;
            var weld = new Dictionary<Vector3Int, int>(); var rep = new int[n]; int nr = 0; var pos = new Vector3[n];
            for (int v = 0; v < n; v++)
            {
                pos[v] = smr.transform.TransformPoint(vs[v]);
                var key = new Vector3Int(Mathf.RoundToInt(pos[v].x * 2000f), Mathf.RoundToInt(pos[v].y * 2000f), Mathf.RoundToInt(pos[v].z * 2000f));
                int r; if (!weld.TryGetValue(key, out r)) { r = nr++; weld[key] = r; }
                rep[v] = r;
            }
            var nbr = new HashSet<int>[nr]; for (int i = 0; i < nr; i++) nbr[i] = new HashSet<int>();
            for (int t = 0; t + 2 < tris.Length; t += 3) for (int e = 0; e < 3; e++) { int a = rep[tris[t + e]], b = rep[tris[t + (e + 1) % 3]]; if (a != b) { nbr[a].Add(b); nbr[b].Add(a); } }
            Func<BoneWeight, int, float> wOf = (w, b) => (w.boneIndex0 == b ? w.weight0 : 0f) + (w.boneIndex1 == b ? w.weight1 : 0f) + (w.boneIndex2 == b ? w.weight2 : 0f) + (w.boneIndex3 == b ? w.weight3 : 0f);
            var acc = new float[nr, K]; var tot = new float[nr]; var cnt = new int[nr]; var zone = new bool[nr];
            for (int v = 0; v < n; v++)
            {
                int r = rep[v]; cnt[r]++;
                for (int k = 0; k < K; k++) { float w = wOf(bw[v], group[k]); acc[r, k] += w; tot[r] += w; }
                if (inZone(pos[v])) zone[r] = true;
            }
            var s = new float[nr, K]; var valid = new bool[nr];
            for (int r = 0; r < nr; r++) { valid[r] = cnt[r] > 0 && tot[r] / cnt[r] >= 0.5f; zone[r] &= valid[r]; if (tot[r] > 1e-5f) for (int k = 0; k < K; k++) s[r, k] = acc[r, k] / tot[r]; }
            for (int it = 0; it < iters; it++)
            {
                var ns = (float[,])s.Clone();
                for (int r = 0; r < nr; r++)
                {
                    if (!zone[r]) continue;
                    int c = 0; var m = new float[K]; foreach (int q in nbr[r]) if (valid[q]) { c++; for (int k = 0; k < K; k++) m[k] += s[q, k]; }
                    if (c == 0) continue;
                    for (int k = 0; k < K; k++) ns[r, k] = Mathf.Lerp(s[r, k], m[k] / c, 0.5f);
                }
                s = ns;
            }
            for (int v = 0; v < n; v++)
            {
                int r = rep[v]; if (!zone[r]) continue;
                var w = bw[v]; float T = 0f; for (int k = 0; k < K; k++) T += wOf(w, group[k]); if (T < 1e-4f) continue;
                var list = new List<KeyValuePair<int, float>>(8); float total = 0f;
                Action<int, float> add = (b, x) => { if (x > 1e-5f) { list.Add(new KeyValuePair<int, float>(b, x)); total += x; } };
                add(w.boneIndex0, w.weight0); add(w.boneIndex1, w.weight1); add(w.boneIndex2, w.weight2); add(w.boneIndex3, w.weight3);
                var res = new List<KeyValuePair<int, float>>(8);
                foreach (var kv in list) if (System.Array.IndexOf(group, kv.Key) < 0) res.Add(kv);
                for (int k = 0; k < K; k++) if (T * s[r, k] > 1e-4f) res.Add(new KeyValuePair<int, float>(group[k], T * s[r, k]));
                res.Sort((x, y) => y.Value.CompareTo(x.Value)); if (res.Count > 4) res.RemoveRange(4, res.Count - 4);
                float rs = 0f; foreach (var kv in res) rs += kv.Value; if (rs < 1e-6f) continue; float k2 = total / rs;
                var nb = new BoneWeight();
                nb.boneIndex0 = res[0].Key; nb.weight0 = res[0].Value * k2;
                if (res.Count > 1) { nb.boneIndex1 = res[1].Key; nb.weight1 = res[1].Value * k2; }
                if (res.Count > 2) { nb.boneIndex2 = res[2].Key; nb.weight2 = res[2].Value * k2; }
                if (res.Count > 3) { nb.boneIndex3 = res[3].Key; nb.weight3 = res[3].Value * k2; }
                bw[v] = nb;
            }
        }

        // ------------------------------------------------------------------------------------------ the 15 prefabs follow the model
        public const string MeshDir = "Assets/_Licensed/PhotorealPlayer/Meshes", PrefabDir = "Assets/_Licensed/PhotorealPlayer/Prefabs";

        /// <summary>
        /// The Humanoid avatar of each model stores its own skeleton pose (HumanoidMapper wrote it from the source FBX), and an Animator writes that pose back into the bones whenever it binds: without this the lengthened elbow snapped back to the old
        /// position at runtime while the meshes stayed lengthened. This copies the rig bones' local positions of the re-imported model into each model's human description and re-imports the model with it. Idempotent (only the models that differ are touched).
        /// </summary>
        [MenuItem("PIX Football/Photoreal Player/Fix avatar skeletons of the re-imported bodies")]
        public static int FixAvatars()
        {
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets("PHR_ t:Model", new[] { MeshDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g); if (!path.EndsWith(".fbx")) continue;
                var mi = AssetImporter.GetAtPath(path) as ModelImporter; if (mi == null || mi.animationType != ModelImporterAnimationType.Human) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (model == null) continue;
                var map = new Dictionary<string, Transform>(); foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!map.ContainsKey(t.name)) map[t.name] = t;
                var d = mi.humanDescription; var sk = d.skeleton; bool dirty = false;
                for (int i = 0; i < sk.Length; i++)
                {
                    Transform t;
                    if (map.TryGetValue(sk[i].name, out t) && Vector3.Distance(sk[i].position, t.localPosition) > 1e-4f) { sk[i].position = t.localPosition; dirty = true; }
                }
                if (!dirty) continue;
                d.skeleton = sk; mi.humanDescription = d; mi.SaveAndReimport(); n++;
            }
            Debug.Log("[PhotoArm] avatar skeletons updated: " + n);
            return n;
        }
        /// <summary>
        /// The live prefabs are flat copies of the model, so their bones keep the positions they had when they were built. This makes every rig bone of a prefab (names starting with rig:) take its local position, rotation and scale from the re-imported
        /// model, adds the helper bones, re-binds every SkinnedMeshRenderer of the prefab to the bones of the model's renderer of the same name (by bone name), and puts the BodyHelperBones driver on the animator object. Idempotent.
        /// </summary>
        [MenuItem("PIX Football/Photoreal Player/Sync prefabs to the re-imported bodies")]
        public static void SyncPrefabs()
        {
            int done = 0;
            foreach (string pg in AssetDatabase.FindAssets("PIX_PhotoPlayer_ t:Prefab", new[] { PrefabDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(pg); if (!path.EndsWith(".prefab")) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(path).Substring("PIX_PhotoPlayer_".Length);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(MeshDir + "/PHR_" + name + ".fbx");
                if (model == null) { Debug.LogWarning("[PhotoArm] no model for " + path); continue; }
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var mt = new Dictionary<string, Transform>(); foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("rig:") && !mt.ContainsKey(t.name)) mt[t.name] = t;
                    var pt = new Dictionary<string, Transform>(); foreach (var t in contents.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("rig:") && !pt.ContainsKey(t.name)) pt[t.name] = t;
                    // parents first: the model's hierarchy order is root first
                    foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    {
                        if (!t.name.StartsWith("rig:")) continue;
                        Transform p;
                        if (!pt.TryGetValue(t.name, out p))
                        {
                            Transform par; if (t.parent == null || !pt.TryGetValue(t.parent.name, out par)) continue;
                            var go = new GameObject(t.name); p = go.transform; p.SetParent(par, false); pt[t.name] = p;
                        }
                        p.localPosition = t.localPosition; p.localRotation = t.localRotation; p.localScale = t.localScale;
                    }
                    var msm = new Dictionary<string, SkinnedMeshRenderer>(); foreach (var s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) msm[s.name] = s;
                    foreach (var s in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        SkinnedMeshRenderer ms; if (!msm.TryGetValue(s.name, out ms) || ms.bones == null || ms.bones.Length == 0) continue;
                        var nb = new Transform[ms.bones.Length]; bool ok = true;
                        for (int i = 0; i < nb.Length; i++) { Transform b; if (ms.bones[i] != null && pt.TryGetValue(ms.bones[i].name, out b)) nb[i] = b; else ok = false; }
                        if (!ok) { Debug.LogWarning("[PhotoArm] " + name + " " + s.name + ": a bone of the model is missing in the prefab"); continue; }
                        s.bones = nb;
                        if (ms.rootBone != null && pt.ContainsKey(ms.rootBone.name)) s.rootBone = pt[ms.rootBone.name];
                    }
                    var an = contents.GetComponentInChildren<Animator>(true);
                    if (an != null && an.gameObject.GetComponent<PixFootball.Gameplay.BodyHelperBones>() == null) an.gameObject.AddComponent<PixFootball.Gameplay.BodyHelperBones>();
                    PrefabUtility.SaveAsPrefabAsset(contents, path); done++;
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            Debug.Log("[PhotoArm] synced " + done + " prefabs");
        }

        // ------------------------------------------------------------------------------------------ one-time re-import
        [InitializeOnLoadMethod]
        static void CheckOnce()
        {
            if (!Enabled) return;
            if (Application.isBatchMode && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixSkipArmReimport") >= 0) return;
            EditorApplication.delayCall += () =>
            {
                string marker = "Library/PixArmProportions.v" + Version + ".txt";
                if (System.IO.File.Exists(marker)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                ReimportAll(); FixAvatars(); SyncPrefabs();
                try { System.IO.File.WriteAllText(marker, "done"); } catch { }
            };
        }

        /// <summary>Batch entry: re-import the bodies, sync the prefabs, measure the proportions (BodyProportions) and exit.</summary>
        public static void RunBatch() { ReimportAll(); FixAvatars(); SyncPrefabs(); BodyProportions.Run(); }

        [MenuItem("PIX Football/Photoreal Player/Re-import bodies (arm proportions)")]
        public static void ReimportAll()
        {
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets("PHR_ t:Model", new[] { "Assets/_Licensed/PhotorealPlayer/Meshes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(".fbx")) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); n++;
            }
            Debug.Log("[PhotoArm] re-imported " + n + " bodies");
        }
    }
}
