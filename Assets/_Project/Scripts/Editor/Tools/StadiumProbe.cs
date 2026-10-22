using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Track D probe: prints the hierarchy bounds of the stadium prefab (children, 3 levels deep) for the kit-bash placement.</summary>
    public static class StadiumProbe
    {
        public static void Dump()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Licensed/DraftFever/Prefabs/PIX_Stadium_Pro.prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var sb = new StringBuilder();
            Walk(inst.transform, 0, sb);
            Debug.Log("[StadiumProbe]\n" + sb);
            Object.DestroyImmediate(inst);
        }

        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds(); bool any = false; int tris = 0;
            foreach (var r in rs) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) tris += (int)(mf.sharedMesh.triangles.Length / 3); }
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(" | renderers ").Append(rs.Length).Append(" tris ").Append(tris);
            if (any) sb.Append(" | center ").Append(b.center.ToString("F1")).Append(" size ").Append(b.size.ToString("F1"));
            sb.Append('\n');
            if (depth < 3) foreach (Transform c in t) Walk(c, depth + 1, sb);
        }
    }
}
