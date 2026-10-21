using System.Collections.Generic;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds simplified LOD1 / LOD2 meshes of the skinned player body parts by vertex clustering (positions, normals, uvs and bone weights are averaged per grid cell; the grid is refined
    /// until the vertex budget is met) and stores them in PixMeshLods.asset. Menu: PIX Football/Assets/Build Player Mesh LODs.
    /// </summary>
    public static class PlayerMeshLodBuilder
    {
        static readonly string[] Parts = { "Torso", "Legs", "Arms", "Hands", "Shoes", "Head" };
        const float Lod1 = 0.38f, Lod2 = 0.12f;

        [MenuItem("PIX Football/Assets/Build Player Mesh LODs")]
        public static void Run()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var table = AssetDatabase.LoadAssetAtPath<PlayerMeshLodTable>(PlayerMeshLodTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<PlayerMeshLodTable>(); MaterialFactory.EnsureFolder("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, PlayerMeshLodTable.EditorPath); }
            // drop the old sub assets
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(PlayerMeshLodTable.EditorPath)) if (o is Mesh) Object.DestroyImmediate(o, true);
            table.entries.Clear();
            var seen = new HashSet<string>();
            foreach (string key in new[] { "player_small", "player_medium", "player_large" })
            {
                var prefab = lib.Prefab(key);
                foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!Parts.Any(p => smr.name.StartsWith(p)) || smr.sharedMesh == null) continue;
                    string k = PlayerMeshLodTable.KeyFor(smr.sharedMesh);
                    if (!seen.Add(k)) continue;
                    var src = smr.sharedMesh;
                    var l1 = Decimate(src, Mathf.Max(200, Mathf.RoundToInt(src.vertexCount * Lod1)), src.name + "_LOD1");
                    var l2 = Decimate(src, Mathf.Max(90, Mathf.RoundToInt(src.vertexCount * Lod2)), src.name + "_LOD2");
                    AssetDatabase.AddObjectToAsset(l1, table); AssetDatabase.AddObjectToAsset(l2, table);
                    table.entries.Add(new PlayerMeshLodTable.Entry { key = k, lod1 = l1, lod2 = l2 });
                    Debug.Log("[MeshLod] " + k + " -> " + l1.vertexCount + " / " + l2.vertexCount + " verts, tris " + src.triangles.Length / 3 + " -> " + l1.triangles.Length / 3 + " / " + l2.triangles.Length / 3);
                }
            }
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            PlayerMeshLodTable.ResetCache();
        }

        static Mesh Decimate(Mesh src, int target, string name)
        {
            var verts = src.vertices; var norms = src.normals; var uvs = src.uv; var tris = src.triangles; var bws = src.boneWeights;
            var b = src.bounds;
            float lo = 0.001f, hi = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            int[] map = null; int count = 0;
            for (int it = 0; it < 22; it++)
            {
                float cell = Mathf.Sqrt(lo * hi);
                map = Cluster(verts, b.min, cell, out count);
                if (count > target) lo = cell; else hi = cell;
            }
            map = Cluster(verts, b.min, hi, out count);
            var pos = new Vector3[count]; var nrm = new Vector3[count]; var uv = new Vector2[count]; var cnt = new int[count];
            var weights = new Dictionary<int, float>[count];
            for (int i = 0; i < verts.Length; i++)
            {
                int c = map[i];
                pos[c] += verts[i]; if (norms.Length > i) nrm[c] += norms[i]; if (uvs.Length > i) uv[c] += uvs[i]; cnt[c]++;
                if (bws.Length > i)
                {
                    var d = weights[c] ?? (weights[c] = new Dictionary<int, float>());
                    Add(d, bws[i].boneIndex0, bws[i].weight0); Add(d, bws[i].boneIndex1, bws[i].weight1); Add(d, bws[i].boneIndex2, bws[i].weight2); Add(d, bws[i].boneIndex3, bws[i].weight3);
                }
            }
            var outW = new BoneWeight[count];
            for (int c = 0; c < count; c++)
            {
                pos[c] /= cnt[c]; nrm[c] = nrm[c].sqrMagnitude > 1e-8f ? nrm[c].normalized : Vector3.up; uv[c] /= cnt[c];
                if (weights[c] != null)
                {
                    var top = weights[c].OrderByDescending(kv => kv.Value).Take(4).ToArray();
                    float sum = top.Sum(kv => kv.Value); if (sum < 1e-6f) sum = 1f;
                    var w = new BoneWeight();
                    if (top.Length > 0) { w.boneIndex0 = top[0].Key; w.weight0 = top[0].Value / sum; }
                    if (top.Length > 1) { w.boneIndex1 = top[1].Key; w.weight1 = top[1].Value / sum; }
                    if (top.Length > 2) { w.boneIndex2 = top[2].Key; w.weight2 = top[2].Value / sum; }
                    if (top.Length > 3) { w.boneIndex3 = top[3].Key; w.weight3 = top[3].Value / sum; }
                    outW[c] = w;
                }
            }
            var ntris = new List<int>(tris.Length / 3);
            var seenTri = new HashSet<long>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = map[tris[t]], bb = map[tris[t + 1]], cc = map[tris[t + 2]];
                if (a == bb || bb == cc || a == cc) continue;
                // one copy of every distinct triangle (winding preserved)
                int mn = Mathf.Min(a, Mathf.Min(bb, cc)), mx = Mathf.Max(a, Mathf.Max(bb, cc)), md = a + bb + cc - mn - mx;
                long key = ((long)mn * 1000003L + md) * 1000003L + mx;
                if (!seenTri.Add(key)) continue;
                ntris.Add(a); ntris.Add(bb); ntris.Add(cc);
            }
            var m = new Mesh { name = name };
            m.vertices = pos; m.normals = nrm; m.uv = uv; m.boneWeights = outW; m.bindposes = src.bindposes;
            m.SetTriangles(ntris, 0);
            m.bounds = src.bounds;
            m.RecalculateTangents();
            return m;
        }

        static void Add(Dictionary<int, float> d, int bone, float w) { if (w <= 0f) return; float cur; d.TryGetValue(bone, out cur); d[bone] = cur + w; }

        static int[] Cluster(Vector3[] v, Vector3 min, float cell, out int count)
        {
            var map = new int[v.Length]; var ids = new Dictionary<long, int>();
            for (int i = 0; i < v.Length; i++)
            {
                long x = (long)((v[i].x - min.x) / cell), y = (long)((v[i].y - min.y) / cell), z = (long)((v[i].z - min.z) / cell);
                long key = (x * 4093L + y) * 4093L + z;
                int id; if (!ids.TryGetValue(key, out id)) { id = ids.Count; ids[key] = id; }
                map[i] = id;
            }
            count = ids.Count;
            return map;
        }
    }
}
