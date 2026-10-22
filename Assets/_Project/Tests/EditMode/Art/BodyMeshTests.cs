using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.5.x MODELS: the player body meshes (bulky LOD0 from Tools/Blender/pix_player_fix.py + decimated LOD1..3) must keep their silhouette, weights and ordering.
    /// The art lives in the git-ignored Assets/_Licensed tree, so every test skips itself when the prefabs are not present.
    /// </summary>
    public class BodyMeshTests
    {
        static readonly string[] Sizes = { "Small", "Medium", "Large" };
        static readonly string[] Parts = { "Torso", "Legs", "Arms", "Hands", "Shoes", "Head" };

        static GameObject Prefab(string size)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Licensed/DraftFever/Prefabs/PIX_Player_" + size + ".prefab");
            if (p == null) Assert.Ignore("player prefabs not imported (Assets/_Licensed is git-ignored)");
            return p;
        }

        static IEnumerable<SkinnedMeshRenderer> BodyParts(GameObject prefab)
        {
            return prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => Parts.Any(p => s.name.StartsWith(p)) && s.sharedMesh != null);
        }

        static float Area(Mesh m)
        {
            var v = m.vertices; var t = m.triangles; float a = 0f;
            for (int i = 0; i < t.Length; i += 3) a += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            return a;
        }

        [Test]
        public void EveryBodyPartHasFourDecreasingLods()
        {
            var table = AssetDatabase.LoadAssetAtPath<PlayerMeshLodTable>(PlayerMeshLodTable.EditorPath);
            if (table == null) Assert.Ignore("no PixMeshLods table");
            foreach (string size in Sizes)
                foreach (var smr in BodyParts(Prefab(size)))
                {
                    var e = table.Find(smr.sharedMesh);
                    Assert.IsNotNull(e, size + " " + smr.name + " has no LOD entry (key " + PlayerMeshLodTable.KeyFor(smr.sharedMesh) + ")");
                    Assert.IsNotNull(e.lod1); Assert.IsNotNull(e.lod2); Assert.IsNotNull(e.lod3, size + " " + smr.name + " lod3");
                    Assert.Less(e.lod1.triangles.Length, smr.sharedMesh.triangles.Length, smr.name);
                    Assert.Less(e.lod2.triangles.Length, e.lod1.triangles.Length, size + " " + smr.name);
                    Assert.Less(e.lod3.triangles.Length, e.lod2.triangles.Length, size + " " + smr.name);
                }
        }

        [Test]
        public void LodsKeepTheSilhouetteOfTheBigParts()
        {
            var table = AssetDatabase.LoadAssetAtPath<PlayerMeshLodTable>(PlayerMeshLodTable.EditorPath);
            if (table == null) Assert.Ignore("no PixMeshLods table");
            foreach (string size in Sizes)
                foreach (var smr in BodyParts(Prefab(size)).Where(s => s.name.StartsWith("Torso") || s.name.StartsWith("Legs") || s.name.StartsWith("Arms")))
                {
                    var e = table.Find(smr.sharedMesh);
                    Assert.IsNotNull(e);
                    var b0 = smr.sharedMesh.bounds.size; float a0 = Area(smr.sharedMesh);
                    int lod = 1;
                    foreach (var m in new[] { e.lod1, e.lod2, e.lod3 })
                    {
                        var b = m.bounds.size;
                        for (int ax = 0; ax < 3; ax++) Assert.AreEqual(b0[ax], b[ax], b0[ax] * 0.10f, size + " " + smr.name + " LOD" + lod + " axis " + ax + " bounds");
                        Assert.AreEqual(a0, Area(m), a0 * 0.15f, size + " " + smr.name + " LOD" + lod + " surface area (clustered v0.5 LODs lost 20-30 percent)");
                        lod++;
                    }
                }
        }

        // ---- circumference check (QA-1053): the old clustered LODs passed a 10 percent bounds check while the waist was 18 percent thinner
        static float Hull(System.Collections.Generic.List<Vector2> pts)
        {
            if (pts.Count < 3) return 0f;
            var p = pts.Distinct().OrderBy(q => q.x).ThenBy(q => q.y).ToList(); if (p.Count < 3) return 0f;
            System.Func<Vector2, Vector2, Vector2, float> cross = (o, a, b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var h = new System.Collections.Generic.List<Vector2>();
            foreach (var q in p) { while (h.Count >= 2 && cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lo = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--) { var q = p[i]; while (h.Count >= lo && cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1);
            float per = 0f; for (int i = 0; i < h.Count; i++) per += Vector2.Distance(h[i], h[(i + 1) % h.Count]);
            return per;
        }

        /// <summary>Hull perimeter of the cross-section of the baked bind-pose mesh with the horizontal plane at y; points with x on the given side only (0 = all).</summary>
        static float Circ(Mesh baked, Matrix4x4 l2w, float y, int side)
        {
            var v = baked.vertices; var t = baked.triangles; var pts = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < v.Length; i++) v[i] = l2w.MultiplyPoint3x4(v[i]);
            for (int i = 0; i + 2 < t.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    var a = v[t[i + e]]; var b = v[t[i + (e + 1) % 3]];
                    if ((a.y - y) * (b.y - y) >= 0f) continue;
                    var q = a + (b - a) * ((y - a.y) / (b.y - a.y));
                    if (side != 0 && Mathf.Sign(q.x) != side) continue;
                    pts.Add(new Vector2(q.x, q.z));
                }
            return Hull(pts);
        }

        [Test]
        public void LodCircumferencesAtWaistHipAndThighStayWithinFewPercent()
        {
            var table = AssetDatabase.LoadAssetAtPath<PlayerMeshLodTable>(PlayerMeshLodTable.EditorPath);
            if (table == null) Assert.Ignore("no PixMeshLods table");
            foreach (string size in Sizes)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(size));
                try
                {
                    var anim = go.GetComponent<Animator>();
                    var hips = anim.GetBoneTransform(HumanBodyBones.Hips).position; var chest = anim.GetBoneTransform(HumanBodyBones.Chest).position;
                    var ul = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position; var ll = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position;
                    float yWaist = Mathf.Lerp(hips.y, chest.y, 0.35f), yHip = ul.y + 0.02f, yThigh = (ul.y + ll.y) * 0.5f; int legSide = (int)Mathf.Sign(ul.x);
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("Torso") || s.name.StartsWith("Legs")))
                    {
                        var e = table.Find(smr.sharedMesh); Assert.IsNotNull(e, size + " " + smr.name);
                        bool torso = smr.name.StartsWith("Torso");
                        float[] ys = torso ? new[] { yWaist } : new[] { yHip, yThigh }; int[] sides = torso ? new[] { 0 } : new[] { 0, legSide };
                        var l2w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        var full = new Mesh(); smr.sharedMesh = smr.sharedMesh; smr.BakeMesh(full, false);
                        var meshes = new[] { e.lod1, e.lod2, e.lod3 }; float[] tol = { 0.04f, 0.05f, 0.08f };
                        for (int k = 0; k < ys.Length; k++)
                        {
                            float c0 = Circ(full, l2w, ys[k], sides[k]); Assert.Greater(c0, 0.2f, size + " " + smr.name + " reference section");
                            for (int li = 0; li < 3; li++)
                            {
                                var keep = smr.sharedMesh; smr.sharedMesh = meshes[li]; var m = new Mesh(); smr.BakeMesh(m, false); smr.sharedMesh = keep;
                                float c = Circ(m, l2w, ys[k], sides[k]);
                                Assert.AreEqual(c0, c, c0 * tol[li], size + " " + smr.name + " LOD" + (li + 1) + " section " + k + " (v0.5 clustered LODs lost 12-21 percent here)");
                                Object.DestroyImmediate(m);
                            }
                        }
                        Object.DestroyImmediate(full);
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        [Test]
        public void SkinWeightsAreNormalisedAndIndicesValid()
        {
            var table = AssetDatabase.LoadAssetAtPath<PlayerMeshLodTable>(PlayerMeshLodTable.EditorPath);
            foreach (string size in Sizes)
                foreach (var smr in BodyParts(Prefab(size)))
                {
                    var meshes = new List<Mesh> { smr.sharedMesh };
                    var e = table != null ? table.Find(smr.sharedMesh) : null;
                    if (e != null) meshes.AddRange(new[] { e.lod1, e.lod2, e.lod3 });
                    foreach (var m in meshes)
                    {
                        if (m == null) continue;
                        var distinct = new System.Collections.Generic.HashSet<int>(); foreach (var bwv in m.boneWeights) distinct.Add(bwv.boneIndex0);
                        if (smr.name.StartsWith("Torso") || smr.name.StartsWith("Legs") || smr.name.StartsWith("Arms")) Assert.GreaterOrEqual(distinct.Count, 4, size + " " + m.name + ": the weights must spread over several bones (an export without weights binds everything to bone 0 and the body stays in the bind pose when animated)");
                        Assert.AreEqual(smr.bones.Length, m.bindposes.Length, size + " " + m.name + " bindposes vs renderer bones");
                        var w = m.boneWeights;
                        Assert.AreEqual(m.vertexCount, w.Length, size + " " + m.name);
                        for (int i = 0; i < w.Length; i++)
                        {
                            float s = w[i].weight0 + w[i].weight1 + w[i].weight2 + w[i].weight3;
                            if (Mathf.Abs(s - 1f) > 0.02f || w[i].boneIndex0 >= smr.bones.Length || w[i].boneIndex1 >= smr.bones.Length || w[i].boneIndex2 >= smr.bones.Length || w[i].boneIndex3 >= smr.bones.Length)
                                Assert.Fail(size + " " + m.name + " vertex " + i + " weight sum " + s);
                        }
                    }
                }
        }

        [Test]
        public void LinemenAreVisiblyWiderThanSkillPlayers()
        {
            float[] torsoW = new float[3], thighD = new float[3];
            for (int i = 0; i < 3; i++)
                foreach (var smr in BodyParts(Prefab(Sizes[i])))
                {
                    if (smr.name.StartsWith("Torso")) torsoW[i] = smr.sharedMesh.bounds.size.x;
                    if (smr.name.StartsWith("Legs")) thighD[i] = smr.sharedMesh.bounds.size.x;
                }
            Assert.Greater(torsoW[1], torsoW[0] * 1.05f, "Medium torso should be wider than Small");
            Assert.Greater(torsoW[2], torsoW[1] * 1.05f, "Large torso should be wider than Medium");
            Assert.Greater(thighD[2], thighD[0] * 1.05f, "Large legs wider than Small");
        }
    }
}
