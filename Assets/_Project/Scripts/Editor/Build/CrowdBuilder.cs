using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds the stadium crowd: seated fans are baked (CPU rasteriser) from the licensed crowd characters into a billboard atlas
    /// (3 body types x 8 clothing palettes x 2 poses) and placed on every seat anchor of the stadium as chunked billboard meshes
    /// (about 25k seats, a handful of draw calls).
    /// </summary>
    public static partial class CrowdBuilder
    {
        public const string Dir = "Assets/_Licensed/PIX/Crowd";
        public const string AtlasPath = Dir + "/crowd_atlas.png";
        public const string BillboardMaterialPath = Dir + "/PIX_CrowdBillboard.mat";
        public const string MeshListPath = Dir + "/crowd_meshes.txt";
        const string Root = CharacterSetup.Root;
        const int Cols = 8, CellW = 160, CellH = 256, Super = 2;
        const int Palettes = 8, Poses = 2, Bodies = 3;
        const float WindowW = 1.0f, WindowH = 1.95f;
        public const float QuadHeight = WindowH, QuadWidth = WindowH * CellW / CellH;

        static readonly string[][] BodyDefs =
        {
            new[] { "Male01", Root + "/Characters/Crowd/Male1/FBX/CrowdMale01.fbx", Root + "/Characters/Crowd/Male1/Texture/C_male_BaseColorMask.png" },
            new[] { "MaleLarge", Root + "/Characters/Crowd/Male2/FBX/CrowdMaleLarge.fbx", Root + "/Characters/Crowd/Male2/Texture/C_maleLarge_BaseColorMask.png" },
            new[] { "Female01", Root + "/Characters/Crowd/Girl/FBX/CrowdFemale01.fbx", Root + "/Characters/Crowd/Girl/Texture/C_female_BaseColorMask.png" },
        };

        // primary (shirt/pants), secondary, tertiary, accent per palette (sRGB)
        static readonly Color[][] PaletteSet =
        {
            new[] { C("#12306B"), C("#E9EDF2"), C("#FFC837"), C("#222222") },   // home navy/gold
            new[] { C("#B01E2A"), C("#F2F2F2"), C("#222222"), C("#555555") },   // away crimson
            new[] { C("#E9EDF2"), C("#12306B"), C("#FFC837"), C("#333333") },   // white tops
            new[] { C("#1E1E24"), C("#8A8F99"), C("#FFC837"), C("#444444") },   // black
            new[] { C("#2D5ABC"), C("#E9EDF2"), C("#1A1A1A"), C("#222222") },   // blue
            new[] { C("#3C3F48"), C("#B0B4BC"), C("#12306B"), C("#222222") },   // grey
            new[] { C("#FFC837"), C("#12306B"), C("#FFFFFF"), C("#222222") },   // gold
            new[] { C("#B01E2A"), C("#12306B"), C("#E9EDF2"), C("#222222") },   // mixed
        };

        static Color C(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }

        // ------------------------------------------------------------------ pose
        static Transform Bone(Animator a, HumanBodyBones b) { return a != null && a.isHuman ? a.GetBoneTransform(b) : null; }

        static void Rot(Transform t, Vector3 axis, float deg) { if (t != null) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation; }

        static void PoseSeated(GameObject go, bool cheer)
        {
            var a = go.GetComponent<Animator>();
            if (a == null || !a.isHuman) { Debug.LogWarning("[PIX] crowd prefab is not humanoid: " + go.name); return; }
            Vector3 X = Vector3.right, Z = Vector3.forward;
            Rot(Bone(a, HumanBodyBones.LeftUpperLeg), X, -85f); Rot(Bone(a, HumanBodyBones.RightUpperLeg), X, -85f);
            Rot(Bone(a, HumanBodyBones.LeftLowerLeg), X, 85f); Rot(Bone(a, HumanBodyBones.RightLowerLeg), X, 85f);
            Rot(Bone(a, HumanBodyBones.LeftFoot), X, -80f); Rot(Bone(a, HumanBodyBones.RightFoot), X, -80f);
            var hips = Bone(a, HumanBodyBones.Hips);
            foreach (var pair in new[] { new KeyValuePair<HumanBodyBones, HumanBodyBones>(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), new KeyValuePair<HumanBodyBones, HumanBodyBones>(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm) })
            {
                var ua = Bone(a, pair.Key); var la = Bone(a, pair.Value);
                if (ua == null) continue;
                float side = ua.position.x > (hips != null ? hips.position.x : 0f) ? 1f : -1f;
                if (cheer) { Rot(ua, Z, side * 58f); Rot(la, Z, side * 18f); }
                else { Rot(ua, Z, -side * 68f); Rot(la, X, -70f); }
            }
        }

        // ------------------------------------------------------------------ rasteriser
        sealed class Cell
        {
            public int W, H; public Color32[] Px; public float[] Z;
            public Cell(int w, int h) { W = w; H = h; Px = new Color32[w * h]; Z = new float[w * h]; for (int i = 0; i < Z.Length; i++) Z[i] = float.NegativeInfinity; }
        }

        static Color32 SampleTex(PixelCanvas t, Vector2 uv)
        {
            float x = (uv.x - Mathf.Floor(uv.x)) * t.W - 0.5f, y = (uv.y - Mathf.Floor(uv.y)) * t.H - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0;
            Color a = GetWrap(t, x0, y0), b = GetWrap(t, x0 + 1, y0), c = GetWrap(t, x0, y0 + 1), d = GetWrap(t, x0 + 1, y0 + 1);
            return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);
        }

        static Color32 GetWrap(PixelCanvas t, int x, int y)
        {
            x = ((x % t.W) + t.W) % t.W; y = ((y % t.H) + t.H) % t.H;
            return t.Px[y * t.W + x];
        }

        static void Raster(Cell cell, Vector3[] v, Vector2[] uv, Vector3[] n, int[] tris, PixelCanvas tex)
        {
            Vector3 L = new Vector3(0.35f, 0.55f, 0.75f).normalized;
            for (int i = 0; i < tris.Length; i += 3)
            {
                int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                Vector3 p0 = v[i0], p1 = v[i1], p2 = v[i2];
                float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)), maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x));
                float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)), maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX)), x1 = Mathf.Min(cell.W - 1, Mathf.CeilToInt(maxX));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(cell.H - 1, Mathf.CeilToInt(maxY));
                float den = (p1.y - p2.y) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.y - p2.y);
                if (Mathf.Abs(den) < 1e-8f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((p1.y - p2.y) * (px - p2.x) + (p2.x - p1.x) * (py - p2.y)) / den;
                        float w1 = ((p2.y - p0.y) * (px - p2.x) + (p0.x - p2.x) * (py - p2.y)) / den;
                        float w2 = 1f - w0 - w1;
                        if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;
                        float z = w0 * p0.z + w1 * p1.z + w2 * p2.z;
                        int idx = y * cell.W + x;
                        if (z <= cell.Z[idx]) continue;
                        Vector2 tuv = w0 * uv[i0] + w1 * uv[i1] + w2 * uv[i2];
                        Color32 tc = SampleTex(tex, tuv);
                        Vector3 nn = (w0 * n[i0] + w1 * n[i1] + w2 * n[i2]).normalized;
                        float shade = 0.5f + 0.5f * Mathf.Clamp01(Vector3.Dot(nn, L) * 0.6f + 0.55f);
                        cell.Z[idx] = z;
                        cell.Px[idx] = new Color32((byte)Mathf.Min(255, tc.r * shade), (byte)Mathf.Min(255, tc.g * shade), (byte)Mathf.Min(255, tc.b * shade), 255);
                    }
            }
        }

        static PixelCanvas Recolor(PixelCanvas src, Color[] pal)
        {
            var dst = new PixelCanvas(src.W, src.H);
            var red = pal[0].linear; var green = pal[1].linear; var blue = pal[2].linear; var mag = pal[3].linear;
            Color r = pal[0], g = pal[1], b = pal[2], m = pal[3];
            for (int i = 0; i < src.Px.Length; i++)
            {
                var c = src.Px[i];
                Color target = Color.clear; bool hit = false;
                if (Dist(c, 217, 91, 91) < 40) { target = r; hit = true; }
                else if (Dist(c, 51, 204, 51) < 55) { target = g; hit = true; }
                else if (Dist(c, 45, 65, 155) < 50) { target = b; hit = true; }
                else if (Dist(c, 204, 68, 204) < 55) { target = m; hit = true; }
                if (hit)
                {
                    float lum = 0.75f + 0.5f * (c.r / 255f * 0.3f + c.g / 255f * 0.5f + c.b / 255f * 0.2f);
                    dst.Px[i] = new Color32((byte)Mathf.Min(255, target.r * lum * 255f), (byte)Mathf.Min(255, target.g * lum * 255f), (byte)Mathf.Min(255, target.b * lum * 255f), 255);
                }
                else dst.Px[i] = c;
            }
            return dst;
        }

        static float Dist(Color32 c, int r, int g, int b) { return Mathf.Abs(c.r - r) + Mathf.Abs(c.g - g) + Mathf.Abs(c.b - b); }

        // ------------------------------------------------------------------ atlas
        public static int CellCount { get { return Bodies * Palettes * Poses; } }

        static void BakeAtlas()
        {
            int rows = Mathf.CeilToInt(CellCount / (float)Cols);
            var atlas = new PixelCanvas(Cols * CellW, rows * CellH);
            atlas.Clear(new Color32(0, 0, 0, 0));
            int cellIndex = 0;
            for (int b = 0; b < Bodies; b++)
            {
                var def = BodyDefs[b];
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(def[1]);
                if (fbx == null || !File.Exists(Path.GetFullPath(def[2]))) { Debug.LogWarning("[PIX] crowd body missing: " + def[0]); cellIndex += Palettes * Poses; continue; }
                var mask = PixelCanvas.Load(Path.GetFullPath(def[2]));
                for (int pose = 0; pose < Poses; pose++)
                {
                    var inst = (GameObject)Object.Instantiate(fbx);
                    inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
                    var anim = inst.GetComponent<Animator>(); if (anim != null) anim.enabled = false;
                    PoseSeated(inst, pose == 1);
                    // bake all skinned/mesh renderers into world space
                    var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var nrm = new List<Vector3>(); var tri = new List<int>();
                    foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var baked = new Mesh(); smr.BakeMesh(baked, true);
                        var mtx = smr.transform.localToWorldMatrix;
                        // BakeMesh(useScale) already includes scale: use rotation+position only
                        mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        int baseIdx = verts.Count;
                        var bv = baked.vertices; var bn = baked.normals; var bu = baked.uv;
                        for (int i = 0; i < bv.Length; i++) { verts.Add(mtx.MultiplyPoint3x4(bv[i])); nrm.Add(mtx.MultiplyVector(bn.Length > i ? bn[i] : Vector3.forward)); uvs.Add(bu.Length > i ? bu[i] : Vector2.zero); }
                        for (int s = 0; s < baked.subMeshCount; s++) foreach (int t in baked.GetTriangles(s)) tri.Add(baseIdx + t);
                        Object.DestroyImmediate(baked);
                    }
                    if (verts.Count == 0) { Object.DestroyImmediate(inst); continue; }
                    float minY = verts.Min(v => v.y);
                    float cx = 0.5f * (verts.Min(v => v.x) + verts.Max(v => v.x));
                    // world -> cell pixels (orthographic from +Z looking at the character: screen right = -X)
                    float ppm = CellH * Super / WindowH;
                    var sv = new Vector3[verts.Count];
                    for (int i = 0; i < sv.Length; i++) sv[i] = new Vector3((CellW * Super) * 0.5f - (verts[i].x - cx) * ppm, (verts[i].y - minY) * ppm + 2f, verts[i].z);
                    var nv = nrm.ToArray(); var uva = uvs.ToArray(); var tria = tri.ToArray();
                    for (int pal = 0; pal < Palettes; pal++)
                    {
                        var tex = Recolor(mask, PaletteSet[pal]);
                        var cell = new Cell(CellW * Super, CellH * Super);
                        Raster(cell, sv, uva, nv, tria, tex);
                        // downsample into the atlas with alpha coverage
                        int ci = cellIndex++;
                        int ax = (ci % Cols) * CellW, ay = (ci / Cols) * CellH; // top-origin in atlas, cell row 0 = top
                        for (int y = 0; y < CellH; y++)
                            for (int x = 0; x < CellW; x++)
                            {
                                float r = 0, g = 0, bl = 0, a = 0;
                                for (int sy = 0; sy < Super; sy++)
                                    for (int sx = 0; sx < Super; sx++)
                                    {
                                        var p = cell.Px[(y * Super + sy) * cell.W + x * Super + sx];
                                        if (p.a > 0) { r += p.r; g += p.g; bl += p.b; a += 1f; }
                                    }
                                int n = Super * Super;
                                if (a > 0) atlas.Set(ax + x, ay + (CellH - 1 - y), new Color32((byte)(r / a), (byte)(g / a), (byte)(bl / a), (byte)(255f * a / n)));
                            }
                    }
                    Object.DestroyImmediate(inst);
                }
            }
            Dilate(atlas, 3);
            MaterialFactory.EnsureFolder(Dir);
            atlas.Save(Path.GetFullPath(AtlasPath));
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.Compressed; ti.anisoLevel = 4;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
            Debug.Log("[PIX] crowd atlas " + atlas.W + "x" + atlas.H + " cells=" + CellCount);
        }

        static void Dilate(PixelCanvas c, int iterations)
        {
            for (int it = 0; it < iterations; it++)
            {
                var copy = (Color32[])c.Px.Clone();
                for (int y = 0; y < c.H; y++)
                    for (int x = 0; x < c.W; x++)
                    {
                        int i = y * c.W + x;
                        if (copy[i].a > 0) continue;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy; if (nx < 0 || ny < 0 || nx >= c.W || ny >= c.H) continue;
                                var q = copy[ny * c.W + nx];
                                if (q.a > 0) { c.Px[i] = new Color32(q.r, q.g, q.b, 0); goto next; }
                            }
                        next:;
                    }
            }
        }

        // ------------------------------------------------------------------ seats + meshes
        public struct Seat { public Vector3 Pos; public float Yaw; }

        /// <summary>Seat anchors from the licensed SeatPositions meshes (authoring pitch along X, rotated 90 degrees about Y into the stadium frame).</summary>
        public static List<Seat> LoadSeats()
        {
            var list = new List<Seat>();
            var seen = new HashSet<long>();
            for (int i = 1; i <= 6; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Props/SeatPositions/Regular/0" + i + ".fbx");
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh; if (m == null || m.vertexCount == 0) continue;
                    Vector3 c = m.bounds.center;
                    Vector3 w = new Vector3(c.z, c.y, -c.x); // Quaternion.Euler(0,90,0) * c
                    long key = ((long)Mathf.RoundToInt(w.x * 20f) << 40) ^ ((long)Mathf.RoundToInt(w.y * 20f) << 20) ^ (long)Mathf.RoundToInt(w.z * 20f);
                    if (!seen.Add(key)) continue;
                    // face the nearest point of the pitch rectangle
                    float tx = Mathf.Clamp(w.x, -24.4f, 24.4f), tz = Mathf.Clamp(w.z, -54.9f, 54.9f);
                    Vector3 d = new Vector3(tx - w.x, 0f, tz - w.z);
                    float yaw = d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : 0f;
                    list.Add(new Seat { Pos = w, Yaw = yaw });
                }
            }
            return list;
        }

        static void BuildMeshes(float fill)
        {
            var seats = LoadSeats();
            Debug.Log("[PIX] seat anchors: " + seats.Count);
            var rnd = new System.Random(20260101);
            // spatial chunks (40 m grid) so frustum culling works
            var chunks = new Dictionary<long, List<Seat>>();
            foreach (var s in seats)
            {
                if (rnd.NextDouble() > fill) continue;
                long key = ((long)Mathf.FloorToInt(s.Pos.x / 18f) << 20) ^ (long)(Mathf.FloorToInt(s.Pos.z / 18f) & 0xFFFFF) ^ ((long)Mathf.FloorToInt(s.Pos.y / 12f) << 44);
                List<Seat> l; if (!chunks.TryGetValue(key, out l)) chunks[key] = l = new List<Seat>();
                l.Add(s);
            }
            // clean old meshes
            if (AssetDatabase.IsValidFolder(Dir + "/Meshes")) AssetDatabase.DeleteAsset(Dir + "/Meshes");
            AssetDatabase.CreateFolder(Dir, "Meshes");
            var names = new List<string>();
            int rows = Mathf.CeilToInt(CellCount / (float)Cols);
            int idx = 0, total = 0;
            foreach (var kv in chunks)
            {
                var l = kv.Value;
                for (int start = 0; start < l.Count; start += 12000)
                {
                    int n = Mathf.Min(12000, l.Count - start);
                    var verts = new Vector3[n * 4]; var norms = new Vector3[n * 4]; var uvs = new Vector2[n * 4]; var tris = new int[n * 6];
                    for (int i = 0; i < n; i++)
                    {
                        var s = l[start + i];
                        Quaternion q = Quaternion.Euler(0f, s.Yaw, 0f);
                        float sc = 0.94f + 0.14f * (float)rnd.NextDouble();
                        float h = QuadHeight * sc, w = QuadWidth * sc;
                        Vector3 baseP = s.Pos + new Vector3(0f, -0.46f, 0f);
                        Vector3 up = Vector3.up * h;
                        Vector3 r = q * Vector3.right * (w * 0.5f);
                        int v = i * 4;
                        Vector3 nrm = (q * Vector3.forward + Vector3.up * 0.12f).normalized;
                        for (int k = 0; k < 4; k++) norms[v + k] = nrm;
                        int body = rnd.Next(0, Bodies), pal = rnd.Next(0, Palettes), pose = rnd.NextDouble() < 0.14 ? 1 : 0;
                        int ci = body * Palettes * Poses + pose * Palettes + pal;
                        float u0 = (ci % Cols) / (float)Cols, u1 = u0 + 1f / Cols;
                        int row = ci / Cols;
                        float v1 = 1f - row / (float)rows, v0 = v1 - 1f / rows;
                        // the viewer stands in front of the fan: the image left edge is the fan local +X side
                        verts[v + 0] = baseP + r; verts[v + 1] = baseP - r; verts[v + 2] = baseP + up - r; verts[v + 3] = baseP + up + r;
                        uvs[v + 0] = new Vector2(u0, v0); uvs[v + 1] = new Vector2(u1, v0); uvs[v + 2] = new Vector2(u1, v1); uvs[v + 3] = new Vector2(u0, v1);
                        int t = i * 6;
                        tris[t + 0] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1; tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
                    }
                    var mesh = new Mesh { name = "Crowd_" + idx, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.triangles = tris;
                    mesh.RecalculateBounds();
                    string path = Dir + "/Meshes/Crowd_" + idx + ".asset";
                    AssetDatabase.CreateAsset(mesh, path);
                    names.Add(path); idx++; total += n;
                }
            }
            File.WriteAllLines(Path.GetFullPath(MeshListPath), names.ToArray());
            Debug.Log("[PIX] crowd meshes: " + names.Count + " chunks, " + total + " fans");
        }

        static void BuildMaterial()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var m = AssetDatabase.LoadAssetAtPath<Material>(BillboardMaterialPath);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, BillboardMaterialPath); }
            m.shader = Shader.Find("HDRP/Lit");
            m.SetTexture("_BaseColorMap", atlas);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.45f);
            m.SetFloat("_DoubleSidedEnable", 1f); m.SetFloat("_CullMode", 0f); m.SetFloat("_CullModeForward", 0f);
            m.SetFloat("_DoubleSidedNormalMode", 2f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
        }

        [MenuItem("PIX Football/Assets/Build Crowd Billboards")]
        public static void BuildAll()
        {
            MaterialFactory.EnsureFolder(Dir);
            BakeAtlas();
            BuildMaterial();
            BuildMeshes(0.9f);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void BuildMeshesOnly() { BuildMaterial(); BuildMeshes(0.9f); AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
    }
}
