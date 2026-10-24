using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    public static partial class CrowdBuilder
    {
        public const string FansPath = "Assets/_Licensed/Resources/PixCrowdFans.bytes";
        public const string FansCollegePath = "Assets/_Licensed/Resources/PixCrowdFansCollege.bytes";

        /// <summary>
        /// v0.11.0 AUDIO+CROWD: bakes every fan of the chunk meshes (root, half-width vector, height vector, atlas cell, a stable random phase and a visitor-section flag) into one small binary
        /// asset per stadium, because at runtime the chunk meshes are static-batched and no longer readable. The GPU crowd animation (Presentation/Crowd/CrowdAnimator) builds its meshes from it.
        /// Layout: int fanCount, then 16 floats per fan (root xyz, right xyz, up xyz, u0, u1, vBottom, vTop, phase, team 0/1, angle). Mesh-local (the chunk objects sit at identity under the crowd root).
        /// Menu: PIX Football/Assets/Bake Crowd Fan Data. Run after Build Crowd Billboards.
        /// </summary>
        [MenuItem("PIX Football/Assets/Bake Crowd Fan Data")]
        public static void BakeFanData()
        {
            Bake(MeshListPath, FansPath);
            Bake(Dir + "/crowd_meshes_college.txt", FansCollegePath);
            AssetDatabase.Refresh();
        }

        static void Bake(string list, string outPath)
        {
            if (!File.Exists(Path.GetFullPath(list))) return;
            var data = new List<float>(); int n = 0;
            var rnd = new System.Random(4242);
            var vtx = new List<Vector3>(); var uvs = new List<Vector2>();
            foreach (string p in File.ReadAllLines(Path.GetFullPath(list)))
            {
                var m = AssetDatabase.LoadAssetAtPath<Mesh>(p.Trim());
                if (m == null || !m.isReadable) { if (m != null) Debug.LogWarning("[PIX] crowd mesh not readable: " + p); continue; }
                vtx.Clear(); uvs.Clear(); m.GetVertices(vtx); m.GetUVs(0, uvs);
                for (int v = 0; v + 3 < vtx.Count; v += 4)
                {
                    Vector3 p0 = vtx[v], p1 = vtx[v + 1], p2 = vtx[v + 2];
                    Vector3 root = (p0 + p1) * 0.5f, right = (p0 - p1) * 0.5f, up = p2 - p1;
                    float ang = Mathf.Atan2(root.z, root.x);
                    double rr = rnd.NextDouble();
                    // the visitors sit behind the +z end zone and in its corners, everybody else is home
                    float away = root.z > 20f ? (rr < 0.82 ? 1f : 0f) : (root.z > 6f ? (rr < 0.35 ? 1f : 0f) : (rr < 0.08 ? 1f : 0f));
                    data.AddRange(new[] { root.x, root.y, root.z, right.x, right.y, right.z, up.x, up.y, up.z, uvs[v].x, uvs[v + 1].x, uvs[v].y, uvs[v + 3].y, (float)rnd.NextDouble(), away, ang });
                    n++;
                }
            }
            var bytes = new byte[4 + data.Count * 4];
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(n), 0, bytes, 0, 4);
            System.Buffer.BlockCopy(data.ToArray(), 0, bytes, 4, data.Count * 4);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllBytes(Path.GetFullPath(outPath), bytes);
            Debug.Log("[PIX] crowd fan data: " + n + " fans -> " + outPath);
        }
    }
}
