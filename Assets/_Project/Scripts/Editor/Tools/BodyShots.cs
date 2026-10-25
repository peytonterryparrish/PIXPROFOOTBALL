using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12.0 BODIES: renders every roster body (15 prefabs) in a fixed studio (front / 3-4 / back / shoulder / helmet / arms-up close-ups) and measures what the owner sees on the pads and helmets:
    ///  * pad size at the bind pose (width x depth x height of the Pads part, its volume, the jersey width at the same height),
    ///  * helmet completeness (shell / face mask / chinstrap / helmet pad / visor present with triangles at all four LODs, parts that are far from the head bone),
    ///  * clipping across 8 clips x 3 times: pad vertices that come out through the jersey, arm-skin vertices that come out through jersey / pads where the cloth covered them at rest.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.BodyShots.Run -pixBodyShotsOut dir [-pixBodyShotsTag tag] [-pixBodyShotsWhat shots,metrics] [-pixBodyShotsOnly photo_player_large]
    /// QA only; nothing here ships.
    /// </summary>
    public static class BodyShots
    {
        static readonly string[] Sizes = { "small", "medium", "large" };
        static readonly string[] Bases = { "", "stocky", "athletic", "tall", "runner" };
        static readonly string[] PoseClips = { "catch_high", "qb_throw", "tackle_wrap", "ol_block_engage", "dl_rush_swim", "sprint", "stance_3pt", "celebrate_td" };
        static readonly float[] PoseTimes = { 0.25f, 0.5f, 0.75f };

        static Camera _cam;

        static string Arg(string name, string def) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return def; }
        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        public static IEnumerable<string> Keys()
        {
            foreach (var b in Bases) foreach (var s in Sizes) yield return "photo_player_" + s + (b == "" ? "" : "_" + b);
        }

        public static void Run()
        {
            string outDir = Arg("-pixBodyShotsOut", "D:/PIXFootball/Renders/v0120_bodies/run");
            string tag = Arg("-pixBodyShotsTag", "run");
            string what = Arg("-pixBodyShotsWhat", "shots,metrics");
            string only = Arg("-pixBodyShotsOnly", "");
            Directory.CreateDirectory(outDir);
            try
            {
                ArtLibrary.ResetCache(); PhotoPlayerLibrary.ResetCache();
                var keys = Keys().Where(k => only == "" || k.StartsWith(only)).ToList();
                if (what.Contains("metrics")) Metrics(keys, outDir, tag);
                if (what.Contains("shots")) Shots(keys, outDir, tag);
            }
            catch (Exception e) { Debug.LogError("[BodyShots] " + e); }
            Debug.Log("[BodyShots] done");
            EditorApplication.Exit(0);
        }

        /// <summary>photo_player_medium_runner -> PIX_PhotoPlayer_Medium_Runner.prefab (the staging prefab name).</summary>
        public static string StagePrefabName(string key)
        {
            var p = key.Replace("photo_player_", "").Split('_'); string s = char.ToUpper(p[0][0]) + p[0].Substring(1);
            return "PIX_PhotoPlayer_" + s + (p.Length > 1 ? "_" + char.ToUpper(p[1][0]) + p[1].Substring(1) : "") + ".prefab";
        }

        // ------------------------------------------------------------------------------------------------ spawn
        static GameObject Spawn(string key, int number, bool helmet, int seed)
        {
            var lib = ArtLibrary.Load();
            var prefab = lib != null ? lib.Prefab(key) : null;
            if (Arg("-pixBodyShotsStage", "0") == "1") prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_BodiesStage/Prefabs/" + StagePrefabName(key));
            if (prefab == null) return null;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.transform.position = Vector3.zero; g.transform.rotation = Quaternion.identity;
            var pp = g.GetComponent<PhotoPlayer>();
            pp.autoLod = false; pp.Init();
            var kit = PhotoPlayerQa.Kits[1];
            pp.SetTeamColors(kit.p, kit.s, kit.t);
            if (Arg("-pixBodyShotsStage", "0") == "1") { PhotoBodiesStage.ApplyMaterialNumbers(pp.HelmetMaterial, pp.JerseyMaterial, pp.PantsMaterial); var tm = AssetDatabase.LoadAssetAtPath<Texture2D>(PhotoBodiesStage.TexDir + "/jersey_Team.png"); if (tm != null && pp.JerseyMaterial != null) pp.JerseyMaterial.SetTexture("_PixColorMask", tm); }
            PhotoBody size = key.Contains("small") ? PhotoBody.Small : key.Contains("large") ? PhotoBody.Large : PhotoBody.Medium;
            pp.ApplyAppearance(PlayerAppearance.FromSeed(seed, size, size == PhotoBody.Large ? PlayerAppearance.RoleLine : PlayerAppearance.RoleSkill));
            var look = new JerseyLook { Number = number, HasNumber = true, Fill = kit.t, Trim = kit.s, Name = "ALDER", Wordmark = kit.wordmark, Dirt = default(DirtState) };
            var jm = pp.JerseyMaterial;
            if (jm != null) { PixLook.ApplyJersey(jm, look); pp.ApplyLayout(jm); }
            if (!helmet) foreach (var k in new[] { "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Visor" }) pp.SetPartVisible(k, false);
            foreach (var hk in Arg("-pixBodyShotsHide", "").Split(',')) if (hk != "") pp.SetPartVisible(hk, false);
            pp.SetLevel(0);
            return g;
        }

        // ------------------------------------------------------------------------------------------------ shots
        static void Studio()
        {
            Light sun; AssetScreenshots.NewStudio(12.5f, out sun);
            _cam = AssetScreenshots.MakeCamera(new Vector3(0, 1f, 8f), new Vector3(0, 1f, 0), 22f);
            _cam.backgroundColor = new Color(0.30f, 0.33f, 0.38f);
            var hd = _cam.GetComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = new Color(0.22f, 0.25f, 0.30f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "QaFloor"; floor.transform.position = new Vector3(0, -0.02f, 0); floor.transform.localScale = new Vector3(14f, 0.04f, 14f);
            var fm = new Material(Shader.Find("HDRP/Lit")); fm.SetColor("_BaseColor", new Color(0.14f, 0.2f, 0.13f)); fm.SetFloat("_Smoothness", 0.15f);
            floor.GetComponent<Renderer>().sharedMaterial = fm;
        }

        static Texture2D Shot(Vector3 camPos, Vector3 look, float fov, int w, int h)
        {
            _cam.transform.position = camPos; _cam.transform.LookAt(look); _cam.fieldOfView = fov; _cam.aspect = (float)w / h;
            return AssetScreenshots.Render(_cam, w, h);
        }

        static void BakeStatic(GameObject go)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, false);
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(smr.transform, false);
                var ls = smr.transform.lossyScale; child.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                var mr = child.AddComponent<MeshRenderer>(); mr.sharedMaterials = smr.sharedMaterials; mr.shadowCastingMode = smr.shadowCastingMode;
                smr.enabled = false;
            }
        }

        static void Pose(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || clip == null) return;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(anim.gameObject, clip, t);
        }

        /// <summary>Per body, left to right: front, 3/4, back, shoulder close-up (3/4), helmet front, helmet 3/4, arms-up 3/4 (catch_high), side. Cell 360 x 520. One PNG per body + the index.</summary>
        static void Shots(List<string> keys, string outDir, string tag)
        {
            Studio();
            var lib = ArtLibrary.Load();
            int sc = int.Parse(Arg("-pixBodyShotsScale", "1")), W = 360 * sc, H = 520 * sc;
            int[] numbers = { 12, 17, 21, 43, 56, 69, 82, 95, 26, 39, 52, 65, 78, 91, 4 };
            for (int bi = 0; bi < keys.Count; bi++)
            {
                var cells = new List<Texture2D>();
                string key = keys[bi];
                var g = Spawn(key, numbers[bi % numbers.Length], true, 1000 + bi * 37);
                if (g == null) { Debug.LogWarning("[BodyShots] no prefab " + key); continue; }
                var an = g.GetComponent<Animator>();
                var headPos = an.GetBoneTransform(HumanBodyBones.Head).position;
                var chestPos = an.GetBoneTransform(HumanBodyBones.UpperChest) != null ? an.GetBoneTransform(HumanBodyBones.UpperChest).position : an.GetBoneTransform(HumanBodyBones.Chest).position;
                BakeStatic(g);
                Vector3 full = new Vector3(0, 1.05f, 7.5f), fullL = new Vector3(0, 0.95f, 0);
                cells.Add(Shot(full, fullL, 22f, W, H));
                g.transform.RotateAround(Vector3.zero, Vector3.up, 38f); cells.Add(Shot(full, fullL, 22f, W, H));
                g.transform.RotateAround(Vector3.zero, Vector3.up, 142f); cells.Add(Shot(full, fullL, 22f, W, H));
                g.transform.rotation = Quaternion.identity; g.transform.RotateAround(Vector3.zero, Vector3.up, 32f);
                cells.Add(Shot(new Vector3(0.1f, chestPos.y + 0.1f, 2.1f), new Vector3(0.0f, chestPos.y - 0.02f, 0), 24f, W, H));
                g.transform.rotation = Quaternion.identity;
                cells.Add(Shot(new Vector3(0, headPos.y + 0.03f, 1.7f), new Vector3(0, headPos.y + 0.03f, 0), 16f, W, H));
                g.transform.RotateAround(Vector3.zero, Vector3.up, 55f);
                cells.Add(Shot(new Vector3(0, headPos.y + 0.03f, 1.7f), new Vector3(0, headPos.y + 0.03f, 0), 16f, W, H));
                g.transform.rotation = Quaternion.identity; g.transform.RotateAround(Vector3.zero, Vector3.up, 90f);
                cells.Add(Shot(full, fullL, 22f, W, H));
                UnityEngine.Object.DestroyImmediate(g);
                // arms up (catch_high mid clip): the pad / sleeve / skin stress pose
                var g2 = Spawn(key, numbers[bi % numbers.Length], true, 1000 + bi * 37);
                var clip = lib.ClipVariant("catch_high", 0);
                Pose(g2, clip, clip != null ? clip.length * 0.5f : 0f);
                var hip = g2.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position;
                g2.transform.position -= new Vector3(hip.x, 0, hip.z);
                BakeStatic(g2);
                g2.transform.RotateAround(Vector3.zero, Vector3.up, 30f);
                cells.Add(Shot(new Vector3(0, 1.7f, 3.3f), new Vector3(0, 1.65f, 0), 24f, W, H));
                UnityEngine.Object.DestroyImmediate(g2);
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                // helmet 3/4 at LOD1 / LOD2 / LOD3 (the match shows LOD1-2 almost all the time)
                for (int lv = 1; lv <= 3; lv++)
                {
                    var g3 = Spawn(key, numbers[bi % numbers.Length], true, 1000 + bi * 37);
                    g3.GetComponent<PhotoPlayer>().SetLevel(lv);
                    var hp3 = g3.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head).position;
                    BakeStatic(g3);
                    g3.transform.RotateAround(Vector3.zero, Vector3.up, 55f);
                    cells.Add(Shot(new Vector3(0, hp3.y + 0.03f, 1.7f), new Vector3(0, hp3.y + 0.03f, 0), 16f, W, H));
                    UnityEngine.Object.DestroyImmediate(g3);
                }
                // reorder: the arms-up cell goes before the side view
                var t = cells[6]; cells[6] = cells[7]; cells[7] = t;
                if (Arg("-pixBodyShotsCells", "0") == "1") for (int ci = 0; ci < cells.Count; ci++) File.WriteAllBytes(outDir + "/" + tag + "_" + key.Replace("photo_player_", "") + "_c" + ci + ".png", cells[ci].EncodeToPNG());
                File.WriteAllBytes(outDir + "/" + tag + "_" + key.Replace("photo_player_", "") + ".png", Row(cells.ToArray(), W, H).EncodeToPNG());
                Debug.Log("[BodyShots] shots " + key);
            }
        }

        static Texture2D Row(Texture2D[] cells, int cw, int ch)
        {
            var t = new Texture2D(cw * cells.Length, ch, TextureFormat.RGB24, false);
            for (int i = 0; i < cells.Length; i++) { t.SetPixels32(i * cw, 0, cw, ch, cells[i].GetPixels32()); UnityEngine.Object.DestroyImmediate(cells[i]); }
            t.Apply(false);
            return t;
        }

        // ------------------------------------------------------------------------------------------------ contact sheets
        static Texture2D Load(string path) { if (!File.Exists(path)) return null; var t = new Texture2D(2, 2, TextureFormat.RGB24, false); t.LoadImage(File.ReadAllBytes(path)); return t; }

        static Texture2D Fit(Texture2D src, int w, int h)
        {
            var o = new Texture2D(w, h, TextureFormat.RGB24, false); var fill = new Color32[w * h]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(70, 76, 90, 255); o.SetPixels32(fill);
            if (src != null)
            {
                float s = Mathf.Min((float)w / src.width, (float)h / src.height); int nw = Mathf.RoundToInt(src.width * s), nh = Mathf.RoundToInt(src.height * s), ox = (w - nw) / 2, oy = (h - nh) / 2;
                for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++) o.SetPixel(ox + x, oy + y, src.GetPixelBilinear((x + 0.5f) / nw, (y + 0.5f) / nh));
            }
            o.Apply(false); return o;
        }

        /// <summary>Contact sheets: R1 + the 15 bodies, one cell each (cell = the c<n> image of every body, n = view index), 4 columns. -pixBodyShotsSheet "dirAfter,tagAfter,cellIndex,r1image,outPng"; with a before dir: "dirBefore,tagBefore" pairs left of each cell.</summary>
        public static void Sheet()
        {
            var a = Arg("-pixBodyShotsSheet", "").Split(',');
            string dir = a[0], tag = a[1]; int c = int.Parse(a[2]); string r1 = a[3], outPng = a[4];
            int cw = int.Parse(Arg("-pixBodyShotsCw", "360")), ch = int.Parse(Arg("-pixBodyShotsCh", "520")), cols = int.Parse(Arg("-pixBodyShotsCols", "4"));
            string beforeDir = Arg("-pixBodyShotsBeforeDir", ""), beforeTag = Arg("-pixBodyShotsBeforeTag", "before");
            var cells = new List<Texture2D>();
            if (r1 != "") cells.Add(Fit(Load(r1), cw, ch));
            foreach (string key in Keys())
            {
                string n = key.Replace("photo_player_", "");
                if (beforeDir != "") cells.Add(Fit(Load(beforeDir + "/" + beforeTag + "_" + n + "_c" + c + ".png"), cw, ch));
                cells.Add(Fit(Load(dir + "/" + tag + "_" + n + "_c" + c + ".png"), cw, ch));
            }
            int rows = (cells.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * cw, rows * ch, TextureFormat.RGB24, false);
            for (int i = 0; i < cells.Count; i++) { int cx = i % cols, cy = i / cols; sheet.SetPixels32(cx * cw, (rows - 1 - cy) * ch, cw, ch, cells[i].GetPixels32()); }
            File.WriteAllBytes(outPng, sheet.EncodeToPNG());
            Debug.Log("[BodyShots] sheet " + outPng + " cells " + cells.Count);
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------------------------------------ metrics
        sealed class Cloud
        {
            public Vector3[] v; public Vector3[] n; public int[] tris;
            Dictionary<long, List<int>> _grid; const float Cell = 0.03f;
            static long Key(int x, int y, int z) { return ((long)(x + 4096) << 40) | ((long)(y + 4096) << 20) | (long)(z + 4096); }
            static int C(float f) { return Mathf.FloorToInt(f / Cell); }
            public void Index()
            {
                _grid = new Dictionary<long, List<int>>();
                for (int i = 0; i < v.Length; i++)
                {
                    long k = Key(C(v[i].x), C(v[i].y), C(v[i].z)); List<int> l; if (!_grid.TryGetValue(k, out l)) { l = new List<int>(); _grid[k] = l; }
                    l.Add(i);
                }
            }
            /// <summary>Signed distance of q to the nearest vertex' tangent plane (positive = outside along the vertex normal) and the distance to that vertex; -1 when nothing within r.</summary>
            public bool Signed(Vector3 q, float r, out float sd, out float dist)
            {
                int cr = Mathf.CeilToInt(r / Cell), cx = C(q.x), cy = C(q.y), cz = C(q.z);
                float best = r * r; int bi = -1;
                for (int x = -cr; x <= cr; x++) for (int y = -cr; y <= cr; y++) for (int z = -cr; z <= cr; z++)
                {
                    List<int> l; if (!_grid.TryGetValue(Key(cx + x, cy + y, cz + z), out l)) continue;
                    foreach (int i in l) { float d = (v[i] - q).sqrMagnitude; if (d < best) { best = d; bi = i; } }
                }
                if (bi < 0) { sd = 0; dist = r; return false; }
                sd = Vector3.Dot(q - v[bi], n[bi]); dist = Mathf.Sqrt(best);
                return true;
            }
        }

        static Cloud Bake(SkinnedMeshRenderer smr, bool index)
        {
            var m = new Mesh(); smr.BakeMesh(m, true);
            var vs = m.vertices; var ns = m.normals;
            var c = new Cloud { v = new Vector3[vs.Length], n = new Vector3[vs.Length], tris = m.triangles };
            for (int i = 0; i < vs.Length; i++) { c.v[i] = smr.transform.TransformPoint(vs[i]); c.n[i] = smr.transform.TransformDirection(ns[i]).normalized; }
            UnityEngine.Object.DestroyImmediate(m);
            if (index) c.Index();
            return c;
        }

        static SkinnedMeshRenderer Lod(PhotoPlayer pp, string key, int lod)
        {
            var p = pp.parts.FirstOrDefault(x => x != null && x.key == key);
            if (p == null || p.lods == null || lod >= p.lods.Length) return null;
            return p.lods[lod] as SkinnedMeshRenderer;
        }

        static float Volume(Cloud c)
        {
            double vol = 0; var t = c.tris;
            for (int i = 0; i < t.Length; i += 3) vol += Vector3.Dot(c.v[t[i]], Vector3.Cross(c.v[t[i + 1]], c.v[t[i + 2]])) / 6.0;
            return (float)Math.Abs(vol);
        }

        sealed class Pose1 { public bool[] padOut; public bool[] skinCovered; }

        static void Metrics(List<string> keys, string outDir, string tag)
        {
            var lib = ArtLibrary.Load();
            var csv = new StringBuilder("body,padWidthM,padDepthM,padHeightM,padVolumeL,jerseyWidthAtPadM,padOverJersey,padTris0,padVerts0,helmetMissing,padPokeRest,padPokeNew_mean,padPokeNew_max,skinPokeNew_mean,skinPokeNew_max,skinZoneVerts,poses\n");
            var txt = new StringBuilder("# BodyShots metrics (" + tag + ")\n");
            foreach (string key in keys)
            {
                var g = Spawn(key, 12, true, 7);
                if (g == null) { txt.AppendLine(key + ": prefab missing"); continue; }
                var pp = g.GetComponent<PhotoPlayer>(); var an = g.GetComponent<Animator>();
                var headB = an.GetBoneTransform(HumanBodyBones.Head);
                // ---- helmet completeness (every LOD): renderer, mesh, triangles, bounds near the head
                var missing = new List<string>();
                foreach (var pk in new[] { "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad" })
                {
                    var part = pp.parts.FirstOrDefault(x => x != null && x.key == pk);
                    if (part == null || part.lods == null || part.lods.Length == 0) { missing.Add(pk + ":part"); continue; }
                    for (int l = 0; l < 4; l++)
                    {
                        var r = part.lods[Mathf.Min(l, part.lods.Length - 1)];
                        var smr = r as SkinnedMeshRenderer; Mesh mesh = smr != null ? smr.sharedMesh : (r != null && r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                        if (r == null || mesh == null || mesh.triangles.Length == 0) { missing.Add(pk + ":LOD" + l + ":empty"); continue; }
                        float d = Vector3.Distance(r.bounds.center, headB.position);
                        if (d > 0.45f) missing.Add(pk + ":LOD" + l + ":far" + F(d));
                    }
                    txt.AppendLine(string.Format("  {0} {1} lods={2} tris=[{3}]", key, pk, part.lods.Length, string.Join(",", part.lods.Select(r => { var s = r as SkinnedMeshRenderer; Mesh mm = s != null ? s.sharedMesh : (r != null && r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null); return mm != null ? (mm.triangles.Length / 3).ToString() : "null"; }).ToArray())));
                }
                // ---- pad size at bind pose
                var pads = Lod(pp, "Pads", 0); var jer = Lod(pp, "Jersey", 0); var arms = Lod(pp, "Arms", 0);
                float pw = 0, pd = 0, ph = 0, pvol = 0, jw = 0; int pTris = 0, pVerts = 0;
                Vector3 across = an.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - an.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position; across.y = 0;
                Vector3 fwd = Vector3.Cross(across.normalized, Vector3.up), right = Vector3.Cross(Vector3.up, fwd);
                Cloud padRest = null, jerRest = null, armRest = null;
                if (pads != null && jer != null && arms != null)
                {
                    padRest = Bake(pads, false); jerRest = Bake(jer, true); armRest = Bake(arms, false);
                    Func<Vector3[], Func<Vector3, float>, Vector2> ext = (vs, f) => { float lo = 9, hi = -9; foreach (var v in vs) { float a = f(v); lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a); } return new Vector2(lo, hi); };
                    var ex = ext(padRest.v, v => Vector3.Dot(v, right)); pw = ex.y - ex.x;
                    ex = ext(padRest.v, v => Vector3.Dot(v, fwd)); pd = ex.y - ex.x;
                    ex = ext(padRest.v, v => v.y); ph = ex.y - ex.x;
                    pvol = Volume(padRest) * 1000f; pTris = padRest.tris.Length / 3; pVerts = padRest.v.Length;
                    float ymid = (ext(padRest.v, v => v.y).x + ext(padRest.v, v => v.y).y) * 0.5f; float lo2 = 9, hi2 = -9;
                    foreach (var v in jerRest.v) if (Mathf.Abs(v.y - ymid) < 0.02f) { float a = Vector3.Dot(v, right); lo2 = Mathf.Min(lo2, a); hi2 = Mathf.Max(hi2, a); }
                    jw = hi2 - lo2;
                }
                // ---- clipping across poses
                string perClip = ""; int zoneVerts = 0, nPoses = 0; var padNew = new List<int>(); var skinNew = new List<int>(); int padPokeRest = 0;
                if (padRest != null)
                {
                    var lua = an.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; var rua = an.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                    var zone = new List<int>();
                    for (int i = 0; i < armRest.v.Length; i++) if (Vector3.Distance(armRest.v[i], lua) < 0.28f || Vector3.Distance(armRest.v[i], rua) < 0.28f) zone.Add(i);
                    zoneVerts = zone.Count;
                    txt.AppendLine("   debug: armsVerts " + armRest.v.Length + " jerseyVerts " + jerRest.v.Length + " padVerts " + padRest.v.Length + " lua " + lua.ToString("0.000") + " armsMesh=" + arms.sharedMesh.name + " guid-path " + AssetDatabase.GetAssetPath(arms.sharedMesh));
                    Func<Cloud, Cloud, Cloud, bool[]> padOut = (P, J, _x) => { var o = new bool[P.v.Length]; for (int i = 0; i < P.v.Length; i++) { float sd, dist; if (J.Signed(P.v[i], 0.06f, out sd, out dist) && sd > 0.004f) o[i] = true; } return o; };
                    Func<Cloud, Cloud, Cloud, float[]> skinCov = (A, J, P) => { var o = new float[zone.Count]; for (int k = 0; k < zone.Count; k++) { float sd, dist; var q = A.v[zone[k]]; float s = 1f; if (J.Signed(q, 0.08f, out sd, out dist)) s = Mathf.Min(s, sd); if (P.Signed(q, 0.08f, out sd, out dist)) s = Mathf.Min(s, sd); o[k] = s; } return o; };
                    padRest.Index();
                    var restPadOut = padOut(padRest, jerRest, null); padPokeRest = restPadOut.Count(b => b);
                    var restSkin = skinCov(armRest, jerRest, padRest);
                    foreach (string ck in PoseClips)
                    {
                        var clip = lib.ClipVariant(ck, 0); if (clip == null) continue;
                        foreach (float tf in PoseTimes)
                        {
                            Pose(g, clip, clip.length * tf);
                            var P = Bake(pads, true); var J = Bake(jer, true); var A = Bake(arms, false);
                            var po = padOut(P, J, null); int pn = 0; for (int i = 0; i < po.Length; i++) if (po[i] && !restPadOut[i]) pn++;
                            var sc = skinCov(A, J, P); int sn = 0; for (int k = 0; k < sc.Length; k++) if (restSkin[k] < -0.006f && sc[k] > 0.002f) sn++;
                            padNew.Add(pn); skinNew.Add(sn); nPoses++; perClip += " " + ck + "@" + tf.ToString("0.00", CultureInfo.InvariantCulture) + "=" + pn + "/" + sn;
                        }
                    }
                    if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                }
                csv.AppendLine(string.Join(",", new[] { key, F(pw), F(pd), F(ph), F(pvol), F(jw), F(jw > 0 ? pw / jw : 0), pTris.ToString(), pVerts.ToString(), missing.Count.ToString(), padPokeRest.ToString(),
                    F(padNew.Count > 0 ? (float)padNew.Average() : 0), (padNew.Count > 0 ? padNew.Max() : 0).ToString(), F(skinNew.Count > 0 ? (float)skinNew.Average() : 0), (skinNew.Count > 0 ? skinNew.Max() : 0).ToString(), zoneVerts.ToString(), nPoses.ToString() }));
                txt.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-28} pads {1:0.000} x {2:0.000} x {3:0.000} m  vol {4:0.0} L  ({5} tris)  jersey width at pads {6:0.000}  | helmet missing: {7} | pad verts out of jersey at rest {8}, new in poses mean {9:0.0} max {10} | skin verts newly exposed mean {11:0.0} max {12} (zone {13})",
                    key, pw, pd, ph, pvol, pTris, jw, missing.Count == 0 ? "none" : string.Join(";", missing.ToArray()), padPokeRest, padNew.Count > 0 ? padNew.Average() : 0, padNew.Count > 0 ? padNew.Max() : 0, skinNew.Count > 0 ? skinNew.Average() : 0, skinNew.Count > 0 ? skinNew.Max() : 0, zoneVerts));
                txt.AppendLine("   per pose (pad/skin): " + perClip);
                UnityEngine.Object.DestroyImmediate(g);
                Debug.Log("[BodyShots] metrics " + key);
            }
            File.WriteAllText(outDir + "/" + tag + "_metrics.csv", csv.ToString());
            File.WriteAllText(outDir + "/" + tag + "_metrics.txt", txt.ToString());
        }
    }
}
