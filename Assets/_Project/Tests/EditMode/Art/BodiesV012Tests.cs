using System.Linq;
using NUnit.Framework;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.12.0 BODIES: pad size, chin cup size / material and helmet completeness of the 15 live roster prefabs (skipped when the licensed art is absent).</summary>
    public class BodiesV012Tests
    {
        const string Dir = "Assets/_Licensed/PhotorealPlayer/Prefabs";
        static readonly string[] Sizes = { "Small", "Medium", "Large" };
        static readonly string[] Bases = { "Std", "Stocky", "Athletic", "Tall", "Runner" };
        // measured after v0.12 (bind pose, LOD0): S 0.707 max, M 0.763, L 0.85; before: 0.847 / 0.892 / 0.939
        static readonly float[] PadWidthMax = { 0.74f, 0.80f, 0.87f };

        static GameObject Spawn(string size, string b)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/PIX_PhotoPlayer_" + size + (b == "Std" ? "" : "_" + b) + ".prefab");
            if (p == null) Assert.Ignore("roster prefab " + size + " " + b + " not present");
            return (GameObject)Object.Instantiate(p);
        }

        static Renderer Lod(PhotoPlayer pp, string key, int lod)
        {
            var part = pp.parts.FirstOrDefault(x => x != null && x.key == key);
            Assert.IsNotNull(part, key);
            return part.lods[Mathf.Min(lod, part.lods.Length - 1)];
        }

        static Mesh MeshOf(Renderer r) { var s = r as SkinnedMeshRenderer; return s != null ? s.sharedMesh : r.GetComponent<MeshFilter>().sharedMesh; }

        [Test]
        public void PadsAreAboutAFifthSmallerThanV011()
        {
            for (int si = 0; si < 3; si++)
                foreach (var b in Bases)
                {
                    var g = Spawn(Sizes[si], b);
                    try
                    {
                        var smr = (SkinnedMeshRenderer)Lod(g.GetComponent<PhotoPlayer>(), "Pads", 0);
                        var m = new Mesh(); smr.BakeMesh(m, true);
                        float lo = 9, hi = -9; foreach (var v in m.vertices) { float x = smr.transform.TransformPoint(v).x; lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
                        Object.DestroyImmediate(m);
                        Assert.LessOrEqual(hi - lo, PadWidthMax[si], Sizes[si] + " " + b + " pad width");
                        Assert.GreaterOrEqual(hi - lo, 0.55f, Sizes[si] + " " + b + " pads must stay a padded silhouette, not thin");
                    }
                    finally { Object.DestroyImmediate(g); }
                }
        }

        [Test]
        public void ChinCupIsSmallAndHasItsOwnMatteMaterial()
        {
            foreach (var s in Sizes)
            {
                var g = Spawn(s, "Std");
                try
                {
                    var pp = g.GetComponent<PhotoPlayer>(); pp.autoLod = false; pp.Init();
                    var r = Lod(pp, "Chinstrap", 0); var mesh = MeshOf(r);
                    var vs = mesh.vertices.Select(v => r.transform.TransformPoint(v)).ToArray();
                    float ymin = vs.Min(v => v.y), cut = ymin + 0.03f;
                    float zmax = vs.Max(v => v.z);
                    var low = vs.Where(v => v.y < cut && v.z > zmax - 0.04f).ToArray();   // the cup (front, lowest), not the straps that run back along the jaw
                    float width = low.Max(v => v.x) - low.Min(v => v.x);
                    Assert.LessOrEqual(width, 0.10f, s + " chin cup width (was 0.117 m)");
                    var cs = Lod(pp, "Chinstrap", 0).sharedMaterial; var hp = Lod(pp, "HelmetPad", 0).sharedMaterial;
                    Assert.AreNotSame(cs, hp, "the chin strap has its own material instance");
                    Assert.LessOrEqual(cs.GetFloat("_Smoothness"), 0.3f, "matte");
                    Assert.Less(cs.GetColor("_BaseColor").grayscale, 0.45f, "a neutral grey, not a white block");
                }
                finally { Object.DestroyImmediate(g); }
            }
        }

        [Test]
        public void EveryHelmetPartHasTrianglesAtEveryLodAndSitsOnTheHead()
        {
            foreach (var s in Sizes)
                foreach (var b in Bases)
                {
                    var g = Spawn(s, b);
                    try
                    {
                        var pp = g.GetComponent<PhotoPlayer>(); var head = pp.headBone;
                        foreach (var key in new[] { "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad" })
                            for (int l = 0; l < 4; l++)
                            {
                                var r = Lod(pp, key, l); var m = MeshOf(r);
                                Assert.IsNotNull(m, s + b + key + l);
                                Assert.Greater(m.triangles.Length / 3, 100, s + " " + b + " " + key + " LOD" + l);
                                Assert.LessOrEqual(Vector3.Distance(r.bounds.center, head.position), 0.45f, s + " " + b + " " + key + " LOD" + l + " away from the head");
                            }
                        Assert.GreaterOrEqual(MeshOf(Lod(pp, "Helmet_Shell", 0)).triangles.Length / 3, 11000, "shell LOD0 (was 8,999)");
                        Assert.GreaterOrEqual(MeshOf(Lod(pp, "FaceMask", 0)).triangles.Length / 3, 9000, "face mask LOD0 (was 6,500)");
                        Assert.GreaterOrEqual(MeshOf(Lod(pp, "FaceMask", 2)).triangles.Length / 3, 1800, "face mask LOD2 keeps its bars (was 1,430)");
                    }
                    finally { Object.DestroyImmediate(g); }
                }
        }
    }
}
