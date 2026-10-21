using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    public static class TmpSetup
    {
        [MenuItem("PIX Football/Setup/Import TMP Essentials")]
        public static void ImportEssentials()
        {
            if (Directory.Exists("Assets/TextMesh Pro/Resources")) { Debug.Log("[PIX] TMP essentials already present"); if (Application.isBatchMode) EditorApplication.Exit(0); return; }
            string pkg = Path.GetFullPath("Library/PackageCache");
            foreach (var d in Directory.GetDirectories(pkg, "com.unity.ugui@*"))
            {
                string f = Path.Combine(d, "Package Resources", "TMP Essential Resources.unitypackage");
                if (File.Exists(f)) { AssetDatabase.importPackageCompleted += _ => EditorApplication.Exit(0); AssetDatabase.importPackageFailed += (a, b) => EditorApplication.Exit(1); AssetDatabase.ImportPackage(f, false); Debug.Log("[PIX] importing TMP essentials from " + f); return; }
            }
            Debug.LogError("[PIX] TMP essential resources package not found");
        }

        [MenuItem("PIX Football/Diagnostics/Dump Seats")]
        public static void DumpSeats()
        {
            var sb = new StringBuilder();
            foreach (string kind in new[] { "Regular", "College" })
                for (int i = 1; i <= 6; i++)
                {
                    string p = "Assets/_Licensed/DraftFever/Props/SeatPositions/" + kind + "/0" + i + ".fbx";
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (go == null) { sb.AppendLine("missing " + p); continue; }
                    var ts = go.GetComponentsInChildren<Transform>(true);
                    var min = new Vector3(1e9f, 1e9f, 1e9f); var max = -min;
                    foreach (var t in ts) { var w = t.position; min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
                    sb.AppendLine(p + " transforms=" + ts.Length + " rootScale=" + go.transform.localScale + " rootRot=" + go.transform.rotation.eulerAngles + " min=" + min + " max=" + max);
                    int n = 0;
                    foreach (var t in ts) { if (n++ > 4) break; sb.AppendLine("   " + t.name + " pos=" + t.position + " rot=" + t.rotation.eulerAngles + " scale=" + t.lossyScale + " parent=" + (t.parent != null ? t.parent.name : "-") + " mf=" + (t.GetComponent<MeshFilter>() != null)); }
                }
            File.WriteAllText("Logs/seats_dump.txt", sb.ToString());
        }
        public static void Run() { ImportEssentials(); }
    }
}
namespace PixFootball.EditorTools
{
    public static class SeatDiag
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= 6; i++)
            {
                string p = "Assets/_Licensed/DraftFever/Props/SeatPositions/Regular/0" + i + ".fbx";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                var min = new Vector3(1e9f, 1e9f, 1e9f); var max = -min; int n = 0; Vector3 sizeSum = Vector3.zero; int vsum = 0;
                foreach (var mf in mfs)
                {
                    var m = mf.sharedMesh; if (m == null) continue;
                    var c = m.bounds.center; min = Vector3.Min(min, c); max = Vector3.Max(max, c); sizeSum += m.bounds.size; vsum += m.vertexCount; n++;
                    if (n <= 3) sb.AppendLine("  " + mf.name + " center=" + c + " size=" + m.bounds.size + " verts=" + m.vertexCount + " tris=" + m.triangles.Length / 3);
                }
                sb.AppendLine(p + " meshes=" + n + " centerMin=" + min + " centerMax=" + max + " avgSize=" + sizeSum / Mathf.Max(1, n) + " avgVerts=" + vsum / Mathf.Max(1, n));
            }
            File.WriteAllText("Logs/seats_dump2.txt", sb.ToString());
        }
    }
}
