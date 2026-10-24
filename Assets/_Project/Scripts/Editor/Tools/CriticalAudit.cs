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
    /// v0.11.0 CRITICAL: static audit of the 15 photoreal body prefabs (parts, LOD renderers, null meshes / bones, skinned-mesh bounds against the skeleton, avatar). Output: Logs/critical_prefabs.txt (or -pixCriticalOut file).
    /// Run: Unity -batchmode -nographics -projectPath ... -executeMethod PixFootball.EditorTools.CriticalAudit.Prefabs -quit
    /// </summary>
    public static class CriticalAudit
    {
        static string Arg(string name, string def) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return def; }

        /// <summary>The ArtLibrary rebuild (ArtLibraryBuilder) only knows the 3 base photo prefabs: puts the 12 roster prefabs (photo_player_{size}_{base}) back (prefabs only, no clips). Idempotent. Run before a build.</summary>
        public static void EnsureRoster()
        {
            PhotoRosterV2.RegisterArtLibrary("Assets/_Licensed/PhotorealPlayer/NoClipsHere");
            ArtLibrary.ResetCache();
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(ArtLibrary.EditorAssetPath);
            Debug.Log("[CriticalAudit] photo prefab keys: " + string.Join(", ", lib.PrefabKeys.Where(k => k.StartsWith("photo_player_")).OrderBy(k => k).ToArray()));
        }

        public static void Prefabs()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pixEnsureRoster") >= 0) EnsureRoster();
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(ArtLibrary.EditorAssetPath);
            var sb = new StringBuilder();
            int problems = 0;
            foreach (var key in lib.PrefabKeys.Where(k => k.StartsWith("photo_player_")).OrderBy(k => k))
            {
                var prefab = lib.Prefab(key);
                if (prefab == null) { sb.AppendLine(key + ": PREFAB NULL"); problems++; continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var an = go.GetComponent<Animator>(); var pp = go.GetComponent<PhotoPlayer>();
                sb.AppendLine("== " + key + "  avatar=" + (an != null && an.avatar != null ? (an.avatar.isValid ? (an.avatar.isHuman ? "human" : "generic") : "INVALID") : "NULL") + "  parts=" + (pp != null ? pp.parts.Length : -1));
                if (an == null || an.avatar == null || !an.avatar.isHuman) problems++;
                var bones = new List<Transform>();
                if (an != null && an.isHuman) foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones))) { if (hb == HumanBodyBones.LastBone) continue; var t = an.GetBoneTransform(hb); if (t != null) bones.Add(t); }
                Bounds sk = bones.Count > 0 ? new Bounds(bones[0].position, Vector3.zero) : new Bounds();
                foreach (var b in bones) sk.Encapsulate(b.position);
                sb.AppendLine("   skeleton bounds (bind): " + sk.min.ToString("0.00") + " .. " + sk.max.ToString("0.00") + "  bones=" + bones.Count);
                if (pp != null)
                {
                    foreach (var part in pp.parts)
                    {
                        if (part == null) { sb.AppendLine("   NULL PART"); problems++; continue; }
                        var line = new StringBuilder("   " + part.key.PadRight(13) + " lods=" + (part.lods != null ? part.lods.Length : -1) + " hideBeyond=" + part.hideBeyond + " ");
                        if (part.lods == null) { sb.AppendLine(line.ToString() + "LODS NULL"); problems++; continue; }
                        for (int i = 0; i < part.lods.Length; i++)
                        {
                            var r = part.lods[i];
                            if (r == null) { line.Append(" [" + i + ":NULL]"); problems++; continue; }
                            var smr = r as SkinnedMeshRenderer;
                            Mesh m = smr != null ? smr.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                            if (m == null) { line.Append(" [" + i + ":NOMESH " + r.name + "]"); problems++; continue; }
                            line.Append(" [" + i + ":" + (m.vertexCount / 1000f).ToString("0.0") + "k");
                            if (smr != null)
                            {
                                int nullBones = smr.bones.Count(b => b == null);
                                line.Append(" b" + smr.bones.Length + (nullBones > 0 ? "!NULL" + nullBones : "") + " root=" + (smr.rootBone != null ? smr.rootBone.name : "NULL"));
                                if (nullBones > 0 || smr.rootBone == null) problems++;
                                if (m.bindposes.Length != smr.bones.Length) { line.Append(" BINDPOSE-MISMATCH " + m.bindposes.Length); problems++; }
                                var lb = smr.localBounds; var wb = smr.bounds;
                                int outside = 0; foreach (var bt in bones) if (!wb.Contains(bt.position)) outside++;
                                line.Append(" lb=" + lb.size.ToString("0.00") + " c=" + lb.center.ToString("0.00") + " skelOutside=" + outside);
                                if (lb.size.x < 0.05f || lb.size.y < 0.05f) { line.Append(" TINY-BOUNDS"); problems++; }
                                var baked = new Mesh(); smr.BakeMesh(baked);
                                line.Append(" baked=" + baked.bounds.size.ToString("0.00"));
                                UnityEngine.Object.DestroyImmediate(baked);
                            }
                            line.Append(" act=" + r.gameObject.activeSelf + "]");
                        }
                        sb.AppendLine(line.ToString());
                    }
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            sb.AppendLine("PROBLEMS: " + problems);
            string outp = Arg("-pixCriticalOut", "Logs/critical_prefabs.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outp)));
            File.WriteAllText(outp, sb.ToString());
            Debug.Log("[CriticalAudit] prefab audit written, problems=" + problems);
        }
    }
}
