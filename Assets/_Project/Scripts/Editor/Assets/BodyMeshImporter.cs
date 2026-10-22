using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Imports the .pixmesh files written by Tools/Blender/pix_player_fix.py (bulky padded LOD0 bodies + decimated LOD1..LOD3 with UV-border protection) and
    /// rebuilds the player prefabs' skinned meshes and the PlayerMeshLodTable from them. The original body FBXs, their Humanoid avatars and the bind pose are untouched:
    /// the new meshes are skinned to the very same bones (bone indices re-mapped by name onto the prefab renderer's bone list, original bind poses reused).
    /// Menu: PIX Football/Assets/Import Fixed Body Meshes. Batch: BodyMeshImporter.Run (all sizes) / Verify (no changes, original-vs-export check).
    /// </summary>
    public static class BodyMeshImporter
    {
        public const string Dir = "Assets/_Licensed/PIX/Meshes/Bodies";
        static readonly string[] Sizes = { "Small", "Medium", "Large" };
        const int Version = 1;
        /// <summary>Bones that a mesh weights but its renderer does not list (remapped to an ancestor): logged per import, an error when a vertex weight is bound to the fallback root (QA-946).</summary>
        public static int RemappedBones;
        static readonly string[] LodParts = { "Torso", "Legs", "Arms", "Hands", "Shoes", "Head" };

        class Entry { public string name; public int lod, nv, nt, nsub; public Vector3[] p, n; public Vector2[] uv; public int[] bi; public float[] bw; public int[] tris, sub; }

        static float[] Floats(BinaryReader r, int count) { var b = r.ReadBytes(count * 4); var f = new float[count]; Buffer.BlockCopy(b, 0, f, 0, b.Length); return f; }
        static int[] Ints(BinaryReader r, int count) { var b = r.ReadBytes(count * 4); var f = new int[count]; Buffer.BlockCopy(b, 0, f, 0, b.Length); return f; }

        static List<Entry> Read(string path, out string[] bones)
        {
            var list = new List<Entry>();
            using (var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8))
            {
                if (new string(r.ReadChars(4)) != "PIXM") throw new Exception("bad magic " + path);
                int version = r.ReadInt32(); if (version != Version) throw new Exception(path + ": .pixmesh version " + version + ", importer expects " + Version + " (re-run Tools/Blender/pix_player_fix.py)"); int count = r.ReadInt32();
                int nb = r.ReadInt32(); bones = new string[nb]; for (int i = 0; i < nb; i++) bones[i] = r.ReadString();
                for (int k = 0; k < count; k++)
                {
                    var e = new Entry { name = r.ReadString() };
                    e.lod = r.ReadInt32(); e.nv = r.ReadInt32(); e.nt = r.ReadInt32(); e.nsub = r.ReadInt32();
                    var p = Floats(r, e.nv * 3); var n = Floats(r, e.nv * 3); var uv = Floats(r, e.nv * 2);
                    e.p = new Vector3[e.nv]; e.n = new Vector3[e.nv]; e.uv = new Vector2[e.nv];
                    for (int i = 0; i < e.nv; i++) { e.p[i] = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]); e.n[i] = new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]); e.uv[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]); }
                    e.bi = Ints(r, e.nv * 4); e.bw = Floats(r, e.nv * 4);
                    e.tris = Ints(r, e.nt * 3); e.sub = Ints(r, e.nt);
                    list.Add(e);
                }
            }
            return list;
        }

        static Mesh Build(Entry e, SkinnedMeshRenderer smr, string[] bones, Matrix4x4[] bindposes, string meshName)
        {
            var w2l = smr.transform.worldToLocalMatrix; var l2wT = smr.transform.localToWorldMatrix.transpose;
            var names = smr.bones.Select(b => b != null ? b.name : "").ToArray();
            var map = new int[bones.Length];
            var usedBone = new bool[bones.Length];
            for (int k = 0; k < e.bi.Length; k++) if (e.bi[k] >= 0 && e.bw[k] > 0f) usedBone[e.bi[k]] = true;
            for (int i = 0; i < bones.Length; i++)
            {
                int idx = Array.IndexOf(names, bones[i]);
                if (idx < 0 && !usedBone[i]) { map[i] = 0; continue; }
                if (idx < 0)
                {
                    // a bone the renderer does not list: walk up to the first ancestor it does
                    var t = smr.transform.root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == bones[i]);
                    while (t != null && idx < 0) { idx = Array.IndexOf(names, t.name); t = t.parent; }
                    if (idx < 0) { idx = 0; Debug.LogError("[BodyMesh] " + meshName + ": bone '" + bones[i] + "' has no ancestor in the renderer's bone list, weights bound to bone 0"); }
                    else Debug.LogWarning("[BodyMesh] " + meshName + ": bone '" + bones[i] + "' is not in the renderer's bone list, remapped to '" + names[idx] + "'");
                    RemappedBones++;
                }
                map[i] = idx;
            }
            int unweighted = 0;
            var v = new Vector3[e.nv]; var n = new Vector3[e.nv]; var bws = new BoneWeight[e.nv];
            for (int i = 0; i < e.nv; i++)
            {
                v[i] = w2l.MultiplyPoint3x4(e.p[i]);
                n[i] = l2wT.MultiplyVector(e.n[i]).normalized;
                var w = new BoneWeight();
                // merge duplicates created by the fallback mapping
                var acc = new Dictionary<int, float>();
                for (int k = 0; k < 4; k++) { if (e.bi[i * 4 + k] < 0 || e.bw[i * 4 + k] <= 0f) continue; int b = map[e.bi[i * 4 + k]]; float cur; acc.TryGetValue(b, out cur); acc[b] = cur + e.bw[i * 4 + k]; }
                var top = acc.OrderByDescending(kv => kv.Value).Take(4).ToArray();
                if (top.Length == 0) { w.boneIndex0 = 0; w.weight0 = 1f; unweighted++; }
                else
                {
                    float sum = top.Sum(kv => kv.Value);
                    if (top.Length > 0) { w.boneIndex0 = top[0].Key; w.weight0 = top[0].Value / sum; }
                    if (top.Length > 1) { w.boneIndex1 = top[1].Key; w.weight1 = top[1].Value / sum; }
                    if (top.Length > 2) { w.boneIndex2 = top[2].Key; w.weight2 = top[2].Value / sum; }
                    if (top.Length > 3) { w.boneIndex3 = top[3].Key; w.weight3 = top[3].Value / sum; }
                }
                bws[i] = w;
            }
            if (unweighted > 0 && !meshName.StartsWith("Eyes")) Debug.LogError("[BodyMesh] " + meshName + ": " + unweighted + " of " + e.nv + " vertices have no skin weight in the .pixmesh (bound to bone 0): re-export with Tools/Blender/pix_player_fix.py");
            var m = new Mesh { name = meshName };
            if (e.nv > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.normals = n; m.uv = e.uv;
            m.subMeshCount = e.nsub;
            for (int s = 0; s < e.nsub; s++)
            {
                var tl = new List<int>();
                for (int t = 0; t < e.nt; t++) if (e.sub[t] == s) { tl.Add(e.tris[t * 3]); tl.Add(e.tris[t * 3 + 1]); tl.Add(e.tris[t * 3 + 2]); }
                m.SetTriangles(tl, s);
            }
            m.boneWeights = bws; m.bindposes = bindposes;
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Fraction of triangles whose geometric normal agrees with the average vertex normal (winding check).</summary>
        static float WindingAgreement(Mesh m)
        {
            var v = m.vertices; var n = m.normals; var t = m.triangles; int ok = 0, tot = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                var g = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                var a = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (g.sqrMagnitude < 1e-14f) continue;
                tot++; if (Vector3.Dot(g, a) > 0) ok++;
            }
            return tot == 0 ? 1f : (float)ok / tot;
        }

        static string PixmeshPath(string size) { return Dir + "/" + size + ".pixmesh"; }
        static string PrefabPath(string size) { return CharacterSetup.Root + "/Prefabs/PIX_Player_" + size + ".prefab"; }

        /// <summary>No changes: builds every LOD0 from the .pixmesh and compares it with the mesh the prefab uses now (bounds, vertex distance, winding).</summary>
        public static void Verify() { VerifyAll(); }

        /// <summary>Returns false when a rebuilt mesh disagrees with the original (winding below 0.98 or mean vertex distance above 4 mm): then the import must not run.</summary>
        public static bool VerifyAll()
        {
            bool ok = true; int checkedParts = 0;
            var sb = new StringBuilder();
            foreach (string size in Sizes)
            {
                if (!File.Exists(PixmeshPath(size))) continue;
                string[] bones; var entries = Read(PixmeshPath(size), out bones);
                var root = PrefabUtility.LoadPrefabContents(PrefabPath(size));
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var e = entries.FirstOrDefault(x => x.name == smr.name && x.lod == 0);
                    if (e == null || smr.sharedMesh == null) continue;
                    var m = Build(e, smr, bones, smr.sharedMesh.bindposes, "verify");
                    var o = smr.sharedMesh;
                    // mean nearest-vertex distance new -> old (sampled)
                    var ov = o.vertices; var nv = m.vertices; float sum = 0f; int cnt = 0;
                    for (int i = 0; i < nv.Length; i += Mathf.Max(1, nv.Length / 400)) { float best = 1e9f; for (int j = 0; j < ov.Length; j++) { float d = (ov[j] - nv[i]).sqrMagnitude; if (d < best) best = d; } sum += Mathf.Sqrt(best); cnt++; }
                    sb.AppendLine(size + " " + smr.name + ": verts " + o.vertexCount + " -> " + m.vertexCount + " subMeshes " + o.subMeshCount + " -> " + m.subMeshCount + " blendShapes " + o.blendShapeCount +
                        " bounds " + o.bounds.center.ToString("F3") + o.bounds.size.ToString("F3") + " -> " + m.bounds.center.ToString("F3") + m.bounds.size.ToString("F3") +
                        " meanNearest " + (sum / cnt * 1000f).ToString("F2") + "mm winding " + WindingAgreement(m).ToString("F3") + " (old " + WindingAgreement(o).ToString("F3") + ")");
                    if ((smr.name.StartsWith("Torso") || smr.name.StartsWith("Legs") || smr.name.StartsWith("Arms")) && (WindingAgreement(m) < 0.98f || sum / cnt > 0.004f)) ok = false;
                    checkedParts++;
                    UnityEngine.Object.DestroyImmediate(m);
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            Directory.CreateDirectory("Logs/model_qa");
            File.WriteAllText("Logs/model_qa/body_mesh_verify.txt", sb.ToString() + "OK=" + ok + " parts=" + checkedParts);
            Debug.Log("[BodyMesh] verify ok=" + ok + " parts=" + checkedParts + "\n" + sb);
            return ok && checkedParts > 0;
        }

        [MenuItem("PIX Football/Assets/Import Fixed Body Meshes")]
        public static void Run()
        {
            AssetDatabase.StartAssetEditing();
            var table = AssetDatabase.LoadAssetAtPath<PlayerMeshLodTable>(PlayerMeshLodTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<PlayerMeshLodTable>(); MaterialFactory.EnsureFolder("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, PlayerMeshLodTable.EditorPath); }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(PlayerMeshLodTable.EditorPath)) if (o is Mesh) UnityEngine.Object.DestroyImmediate(o, true);
            table.entries.Clear();
            var log = new StringBuilder();
            try
            {
                foreach (string size in Sizes)
                {
                    if (!File.Exists(PixmeshPath(size))) { Debug.LogWarning("[BodyMesh] missing " + PixmeshPath(size)); continue; }
                    string[] bones; var entries = Read(PixmeshPath(size), out bones);
                    string prefabPath = PrefabPath(size);
                    var root = PrefabUtility.LoadPrefabContents(prefabPath);
                    MaterialFactory.EnsureFolder(Dir + "/" + size);
                    foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var set = entries.Where(x => x.name == smr.name).OrderBy(x => x.lod).ToList();
                        if (set.Count == 0 || smr.sharedMesh == null) continue;
                        var bind = smr.sharedMesh.bindposes;
                        var meshes = new Mesh[4];
                        foreach (var e in set)
                        {
                            string nm = smr.name + "_" + size + "_LOD" + e.lod;
                            var m = Build(e, smr, bones, bind, nm);
                            string path = Dir + "/" + size + "/" + nm + ".asset";
                            AssetDatabase.DeleteAsset(path);
                            AssetDatabase.CreateAsset(m, path);
                            meshes[e.lod] = m;   // the created object IS the asset (LoadAssetAtPath returns null while asset editing is batched)
                            log.AppendLine(size + " " + nm + " verts " + m.vertexCount + " tris " + m.triangles.Length / 3 + " winding " + WindingAgreement(m).ToString("F3"));
                        }
                        for (int li = 1; li <= 3; li++) if (meshes[li] == null && LodParts.Any(p => smr.name.StartsWith(p))) Debug.LogError("[BodyMesh] " + size + " " + smr.name + " has no LOD" + li + " in the .pixmesh (QA-948)");
                        if (meshes[0] == null) continue;
                        smr.sharedMesh = meshes[0];
                        if (LodParts.Any(p => smr.name.StartsWith(p)) && meshes[1] != null && meshes[2] != null)
                            table.entries.Add(new PlayerMeshLodTable.Entry { key = PlayerMeshLodTable.KeyFor(meshes[0]), lod1 = meshes[1], lod2 = meshes[2], lod3 = meshes[3] });
                    }
                    // the Medium FBX ships two hidden hair-card renderers (5.7k + 5.9k vertices, 76 bones each, DefaultHDMaterial) that nothing ever shows
                    foreach (var hr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("Hair Mesh")).ToArray()) UnityEngine.Object.DestroyImmediate(hr.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PlayerMeshLodTable.ResetCache();
            Directory.CreateDirectory("Logs/model_qa");
            log.AppendLine("bones with weights that the renderer does not list (remapped to an ancestor): " + RemappedBones);
            File.WriteAllText("Logs/model_qa/body_mesh_import.txt", log.ToString());
            Debug.Log("[BodyMesh] imported\n" + log);
        }
    }
}
