using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.5.x MODELS QA: renders every player body (Small / Medium / Large) at LOD0..LOD3 in T-pose, A-pose and ~30 representative clips from front / side / back
    /// (orthographic, fixed scale, labelled contact sheets) and measures the silhouette (waist / chest / hip / thigh / calf circumference from cross-sections of the posed
    /// meshes, perpendicular to the bones) so thin or collapsed bodies show up as numbers. Output: Docs/QA/models/&lt;tag&gt;_*.png|jpg, &lt;tag&gt;_measurements.csv, &lt;tag&gt;_summary.md.
    /// Run WITHOUT -nographics. Command line: -qaTag before|after  -qaSizes Small,Medium,Large  -qaLods 0,1,2,3  -qaWhat poses,clips,extras,heads,measure
    /// Entry points: ModelQaRenderer.RunAll (everything), RunPoses, RunClips, RunExtras, RunHeads.
    /// </summary>
    public static class ModelQaRenderer
    {
        const string OutDir = "Docs/QA/models";            // text results (csv, summary): small, kept in the repo
        /// <summary>Images go to the D: drive (the user asked for large outputs there); -qaImgDir overrides.</summary>
        static string ImgDir { get { return Arg("-qaImgDir", Directory.Exists("D:/") ? "D:/PIXFootball/Renders/models" : OutDir); } }
        static string Tag = "run";
        static readonly string[] AllSizes = { "Small", "Medium", "Large" };

        // ------------------------------------------------------------------ clip list: key, variant, time fraction
        struct ClipSpec { public string key; public int variant; public float frac; public ClipSpec(string k, int v = 0, float f = 0.5f) { key = k; variant = v; frac = f; } }
        static readonly ClipSpec[] Clips =
        {
            new ClipSpec("idle"), new ClipSpec("stance_2pt"), new ClipSpec("stance_3pt"), new ClipSpec("dl_stance"), new ClipSpec("ol_stance"), new ClipSpec("qb_stance_shotgun"),
            new ClipSpec("run", 0, 0.3f), new ClipSpec("sprint", 0, 0.3f), new ClipSpec("backpedal", 0, 0.3f), new ClipSpec("shuffle_left", 0, 0.4f), new ClipSpec("cut_hard", 0, 0.5f),
            new ClipSpec("stiff_arm"), new ClipSpec("tackle_wrap"), new ClipSpec("tackle_big_hit"), new ClipSpec("tackle_dive"), new ClipSpec("be_tackled"),
            new ClipSpec("ol_pass_set"), new ClipSpec("ol_run_block"), new ClipSpec("ol_block_engage"), new ClipSpec("dl_rush_swim"), new ClipSpec("dl_bull_rush"),
            new ClipSpec("qb_throw", 0, 0.4f), new ClipSpec("qb_throw_lob_long", 0, 0.5f), new ClipSpec("qb_drop_5", 0, 0.4f), new ClipSpec("catch_high"), new ClipSpec("catch_low"),
            new ClipSpec("catch_dive"), new ClipSpec("celebrate_td"), new ClipSpec("fall"), new ClipSpec("qb_sack"), new ClipSpec("hurdle"), new ClipSpec("dive_forward"),
        };
        static readonly ClipSpec[] ExtraClips =
        {
            new ClipSpec("idle"), new ClipSpec("run", 0, 0.3f), new ClipSpec("sprint", 0, 0.3f), new ClipSpec("stance_2pt"), new ClipSpec("dl_stance"), new ClipSpec("catch_high"),
            new ClipSpec("tackle_wrap"), new ClipSpec("celebrate_td"), new ClipSpec("ref_idle"), new ClipSpec("ref_signal_td"), new ClipSpec("ref_run"), new ClipSpec("ref_spot_ball"),
            new ClipSpec("coach_clap"), new ClipSpec("coach_headset_idle"), new ClipSpec("coach_clipboard_idle"), new ClipSpec("cheer"),
        };

        // ------------------------------------------------------------------ command line
        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }
        static string[] Sizes() { return Arg("-qaSizes", string.Join(",", AllSizes)).Split(','); }
        static int[] Lods() { return Arg("-qaLods", "0,1,2,3").Split(',').Select(int.Parse).ToArray(); }

        [MenuItem("PIX Football/QA/Model QA - all")]
        public static void RunAll()
        {
            Tag = Arg("-qaTag", "run");
            Directory.CreateDirectory(OutDir);
            string what = Arg("-qaWhat", "poses,clips,extras,heads");
            var sb = new StringBuilder();
            var rows = new List<Row>();
            ArtLibrary.ResetCache();
            PlayerMeshLodTable.ResetCache();
            Studio();
            if (what.Contains("poses")) RunPoses(rows);
            if (what.Contains("clips")) RunClips(rows);
            if (what.Contains("dump")) DumpPrefabs();
            if (what.Contains("heads")) RunHeads();
            if (what.Contains("closeups")) CloseupsImpl();
            if (what.Contains("extras")) RunExtras();
            WriteMeasurements(rows);
            Debug.Log("[ModelQa] done tag=" + Tag + " rows=" + rows.Count);
        }
        public static void RunPoses() { Tag = Arg("-qaTag", "run"); Directory.CreateDirectory(OutDir); Studio(); var r = new List<Row>(); RunPoses(r); WriteMeasurements(r); }
        public static void RunClips() { Tag = Arg("-qaTag", "run"); Directory.CreateDirectory(OutDir); Studio(); var r = new List<Row>(); RunClips(r); WriteMeasurements(r); }
        public static void RunExtras() { Tag = Arg("-qaTag", "run"); Directory.CreateDirectory(OutDir); if (_cam == null) Studio(); ExtrasImpl(); }
        public static void RunHeads() { Tag = Arg("-qaTag", "run"); Directory.CreateDirectory(OutDir); if (_cam == null) Studio(); HeadsImpl(); }

        // ------------------------------------------------------------------ studio
        static Camera _cam; static Light _sun; static GameObject _floor;
        static RenderTexture _rt; static Texture2D _readTex; static int _rtW, _rtH;
        static readonly Dictionary<string, Material> Clay = new Dictionary<string, Material>();

        static void Studio()
        {
            AssetScreenshots.NewStudio(12.5f, out _sun);
            _cam = AssetScreenshots.MakeCamera(new Vector3(0, 1f, 10f), new Vector3(0, 1f, 0), 30f);
            _cam.orthographic = true;
            _cam.backgroundColor = new Color(0.52f, 0.54f, 0.58f); _cam.GetComponent<HDAdditionalCameraData>().clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; _cam.GetComponent<HDAdditionalCameraData>().backgroundColorHDR = new Color(0.42f, 0.44f, 0.48f);
            _sun.transform.SetParent(_cam.transform, false);
            _sun.transform.localRotation = Quaternion.Euler(32f, 24f, 0f);
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "QaFloor";
            _floor.transform.position = new Vector3(0, -0.02f, 0);
            _floor.transform.localScale = new Vector3(8f, 0.04f, 8f);
            _floor.GetComponent<Renderer>().sharedMaterial = SolidMat(new Color(0.22f, 0.3f, 0.2f));
            Clay.Clear();
        }

        static Material SolidMat(Color c, float smooth = 0.25f)
        {
            var m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            return m;
        }

        static Material ClayFor(string n)
        {
            string k = n.StartsWith("Torso") ? "torso" : n.StartsWith("Legs") ? "legs" : n.StartsWith("Arms") || n.StartsWith("Hands") ? "skin" : n.StartsWith("Head") ? "head" :
                n.StartsWith("Eyes") ? "eyes" : n.StartsWith("Shoes") ? "shoes" : n.StartsWith("Hair") ? "hair" : n.Contains("Helmet") ? "helmet" : n.StartsWith("FM") || n.Contains("Mask") ? "mask" : "other";
            Material m;
            if (!Clay.TryGetValue(k, out m))
            {
                Color c = k == "torso" ? new Color(0.15f, 0.4f, 0.75f) : k == "legs" ? new Color(0.88f, 0.88f, 0.9f) : k == "skin" ? new Color(0.8f, 0.55f, 0.42f) : k == "head" ? new Color(0.85f, 0.62f, 0.5f) :
                    k == "eyes" ? Color.white : k == "shoes" ? new Color(0.12f, 0.12f, 0.14f) : k == "hair" ? new Color(0.12f, 0.08f, 0.05f) : k == "helmet" ? new Color(0.2f, 0.25f, 0.55f) : k == "mask" ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.6f, 0.6f, 0.6f);
                Clay[k] = m = SolidMat(c);
            }
            return m;
        }

        // ------------------------------------------------------------------ subjects
        static string KeyFor(string size) { return size == "Small" ? "player_small" : size == "Large" ? "player_large" : size == "Medium" ? "player_medium" : size; }

        static GameObject Spawn(string prefabKey, int lod, bool clay)
        {
            var lib = ArtLibrary.Load();
            var prefab = lib != null ? lib.Prefab(prefabKey) : null;
            if (prefab == null) return null;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.transform.position = Vector3.zero; g.transform.rotation = Quaternion.identity;
            var anim = g.GetComponent<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;
                var pl = new PlayerLod(anim.GetBoneTransform(HumanBodyBones.Head));
                foreach (var smr in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string n = smr.name;
                    if (n.StartsWith("Torso") || n.StartsWith("Legs") || n.StartsWith("Arms") || n.StartsWith("Hands") || n.StartsWith("Shoes") || n.StartsWith("Head")) pl.AddSkinned(smr);
                    else if (n.StartsWith("Eyes")) pl.AddFarHidden(smr);
                }
                if (prefabKey.StartsWith("player")) pl.Apply(lod);
            }
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Hair Mesh")) r.gameObject.SetActive(false);
            if (clay)
                foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                {
                    var arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                    for (int i = 0; i < arr.Length; i++) arr[i] = ClayFor(r.name);
                    r.sharedMaterials = arr;
                }
            return g;
        }

        static void PoseClip(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponent<Animator>();
            if (anim == null || clip == null) return;
            PlayableGraph graph;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out graph);
            graph.Evaluate(0f); graph.Evaluate(t); graph.Destroy();
        }

        static void APose(GameObject go, float armDown = -0.55f)
        {
            var anim = go.GetComponent<Animator>();
            if (anim == null || anim.avatar == null) return;
            var h = new HumanPoseHandler(anim.avatar, anim.transform);
            var hp = new HumanPose(); h.GetHumanPose(ref hp);
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++) if (names[i] == "Left Arm Down-Up" || names[i] == "Right Arm Down-Up") hp.muscles[i] = armDown;
            h.SetHumanPose(ref hp); h.Dispose();
        }

        /// <summary>
        /// Replaces every skinned renderer by a static copy of its posed mesh. The skinning is done here on the CPU (linear blend skinning with the bone matrices x bind poses):
        /// SkinnedMeshRenderer.BakeMesh did not follow the Animator in batch mode with the cleaned clips (the baked bodies stayed in the bind pose while the rigid helmets moved).
        /// The copies are children of the root (which stands at the origin, identity, when this runs) in world coordinates.
        /// </summary>
        static void Bake(GameObject go)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled || smr.sharedMesh == null) continue;
                var src = smr.sharedMesh; var bones = smr.bones; var bind = src.bindposes; var bw = src.boneWeights;
                var mats = new Matrix4x4[bones.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;
                var v = src.vertices; var n = src.normals; var pv = new Vector3[v.Length]; var pn = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    var w = bw[i]; Vector3 p = Vector3.zero, nn = Vector3.zero;
                    Acc(mats, w.boneIndex0, w.weight0, v[i], n[i], ref p, ref nn); Acc(mats, w.boneIndex1, w.weight1, v[i], n[i], ref p, ref nn);
                    Acc(mats, w.boneIndex2, w.weight2, v[i], n[i], ref p, ref nn); Acc(mats, w.boneIndex3, w.weight3, v[i], n[i], ref p, ref nn);
                    pv[i] = p; pn[i] = nn.normalized;
                }
                var baked = new Mesh { name = "posed_" + src.name };
                if (v.Length > 65000) baked.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                baked.vertices = pv; baked.normals = pn; baked.uv = src.uv; baked.subMeshCount = src.subMeshCount;
                for (int s = 0; s < src.subMeshCount; s++) baked.SetTriangles(src.GetTriangles(s), s);
                baked.RecalculateBounds();
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                child.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
            }
        }
        static void Acc(Matrix4x4[] m, int bi, float w, Vector3 v, Vector3 n, ref Vector3 p, ref Vector3 nn)
        {
            if (w <= 0f || bi < 0 || bi >= m.Length) return;
            p += m[bi].MultiplyPoint3x4(v) * w; nn += m[bi].MultiplyVector(n) * w;
        }

        /// <summary>Yaw the character so its pelvis faces +Z and move the hips over the origin (x/z).</summary>
        static float LastYaw;
        static void Normalize(GameObject go)
        {
            var a = go.GetComponent<Animator>();
            var L = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position; var R = a.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;
            var fwd = Vector3.Cross(R - L, Vector3.up); fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f)
            {
                float yaw = Vector3.SignedAngle(fwd.normalized, Vector3.forward, Vector3.up); LastYaw = yaw;
                go.transform.RotateAround(a.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.up, yaw);
            }
            var hp = a.GetBoneTransform(HumanBodyBones.Hips).position;
            go.transform.position -= new Vector3(hp.x, 0f, hp.z);
        }

        // ------------------------------------------------------------------ measurements
        struct MeshData { public Vector3[] v; public int[] t; }
        class Row { public string body, lod, pose; public Dictionary<string, float> m = new Dictionary<string, float>(); }

        static List<MeshData> Collect(GameObject go, params string[] prefixes)
        {
            var list = new List<MeshData>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.StartsWith("baked_") || mf.sharedMesh == null) continue;
                string n = mf.name.Substring(6);
                if (prefixes.Length > 0 && !prefixes.Any(p => n.StartsWith(p))) continue;
                var l2w = mf.transform.localToWorldMatrix;
                var src = mf.sharedMesh.vertices; var w = new Vector3[src.Length];
                for (int i = 0; i < src.Length; i++) w[i] = l2w.MultiplyPoint3x4(src[i]);
                list.Add(new MeshData { v = w, t = mf.sharedMesh.triangles });
            }
            return list;
        }

        /// <summary>Cross-section of the meshes with the plane (p, n); points within maxR of the axis point. Returns the 2D points in the plane basis.</summary>
        static List<Vector2> Section(List<MeshData> ms, Vector3 p, Vector3 n, Vector3 axisPoint, float maxR)
        {
            n.Normalize();
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(n, u);
            var pts = new List<Vector2>();
            foreach (var m in ms)
            {
                var d = new float[m.v.Length];
                for (int i = 0; i < d.Length; i++) d[i] = Vector3.Dot(m.v[i] - p, n);
                for (int t = 0; t + 2 < m.t.Length; t += 3)
                {
                    int i0 = m.t[t], i1 = m.t[t + 1], i2 = m.t[t + 2];
                    float d0 = d[i0], d1 = d[i1], d2 = d[i2];
                    if ((d0 > 0 && d1 > 0 && d2 > 0) || (d0 < 0 && d1 < 0 && d2 < 0)) continue;
                    Edge(m.v[i0], m.v[i1], d0, d1, p, u, w, axisPoint, maxR, pts);
                    Edge(m.v[i1], m.v[i2], d1, d2, p, u, w, axisPoint, maxR, pts);
                    Edge(m.v[i2], m.v[i0], d2, d0, p, u, w, axisPoint, maxR, pts);
                }
            }
            return pts;
        }
        static void Edge(Vector3 a, Vector3 b, float da, float db, Vector3 p, Vector3 u, Vector3 w, Vector3 axis, float maxR, List<Vector2> pts)
        {
            if (da * db >= 0f) return;
            var q = a + (b - a) * (da / (da - db));
            if (maxR > 0f && (q - axis).magnitude > maxR) return;
            var r = q - p; pts.Add(new Vector2(Vector3.Dot(r, u), Vector3.Dot(r, w)));
        }
        static float Hull(List<Vector2> pts, out float width, out float depth)
        {
            width = depth = 0f;
            if (pts.Count < 3) return 0f;
            width = pts.Max(q => q.x) - pts.Min(q => q.x); depth = pts.Max(q => q.y) - pts.Min(q => q.y);
            var p = pts.Distinct().OrderBy(q => q.x).ThenBy(q => q.y).ToList();
            if (p.Count < 3) return 0f;
            Func<Vector2, Vector2, Vector2, float> cross = (o, a, b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var h = new List<Vector2>();
            foreach (var q in p) { while (h.Count >= 2 && cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lo = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--) { var q = p[i]; while (h.Count >= lo && cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1);
            float per = 0f; for (int i = 0; i < h.Count; i++) per += Vector2.Distance(h[i], h[(i + 1) % h.Count]);
            return per;
        }
        static float Circ(List<MeshData> ms, Vector3 c, Vector3 n, Vector3 axis, float maxR) { float w, d; return Hull(Section(ms, c, n, axis, maxR), out w, out d); }

        static Row Measure(GameObject go, string body, string lod, string pose)
        {
            var row = new Row { body = body, lod = lod, pose = pose };
            var a = go.GetComponent<Animator>();
            Func<HumanBodyBones, Vector3> P = b => { var t = a.GetBoneTransform(b); return t != null ? t.position : Vector3.zero; };
            var hips = P(HumanBodyBones.Hips); var chest = P(HumanBodyBones.Chest);
            if (chest == Vector3.zero) chest = P(HumanBodyBones.Spine);
            var up = (chest - hips).normalized;
            var torso = Collect(go, "Torso"); var legs = Collect(go, "Legs"); var shoes = Collect(go, "Shoes");
            if (torso.Count == 0 && legs.Count == 0) { torso = Collect(go); legs = torso; }
            float w, d;
            var wc = Vector3.Lerp(hips, chest, 0.35f);
            row.m["waist_c"] = Hull(Section(torso, wc, up, Vector3.zero, 0f), out w, out d); row.m["waist_w"] = w; row.m["waist_d"] = d;
            var cc = Vector3.Lerp(hips, chest, 0.8f);
            row.m["chest_c"] = Hull(Section(torso, cc, up, Vector3.zero, 0f), out w, out d); row.m["chest_w"] = w;
            var hc = (P(HumanBodyBones.LeftUpperLeg) + P(HumanBodyBones.RightUpperLeg)) * 0.5f;
            row.m["hip_c"] = Hull(Section(legs, hc, up, Vector3.zero, 0f), out w, out d); row.m["hip_w"] = w;
            foreach (var side in new[] { "L", "R" })
            {
                bool l = side == "L";
                var ul = P(l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg); var ll = P(l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg); var ft = P(l ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                float t1 = 0, t2 = 0, t3 = 0, c1 = 0, c2 = 0, c3 = 0;
                var ta = (ll - ul).normalized; var ca = (ft - ll).normalized;
                t1 = Circ(legs, Vector3.Lerp(ul, ll, 0.3f), ta, Vector3.Lerp(ul, ll, 0.3f), 0.14f); t2 = Circ(legs, Vector3.Lerp(ul, ll, 0.5f), ta, Vector3.Lerp(ul, ll, 0.5f), 0.14f); t3 = Circ(legs, Vector3.Lerp(ul, ll, 0.7f), ta, Vector3.Lerp(ul, ll, 0.7f), 0.14f);
                c1 = Circ(legs, Vector3.Lerp(ll, ft, 0.3f), ca, Vector3.Lerp(ll, ft, 0.3f), 0.12f); c2 = Circ(legs, Vector3.Lerp(ll, ft, 0.5f), ca, Vector3.Lerp(ll, ft, 0.5f), 0.12f); c3 = Circ(legs, Vector3.Lerp(ll, ft, 0.7f), ca, Vector3.Lerp(ll, ft, 0.7f), 0.12f);
                row.m["thigh" + side] = t2; row.m["thighMin" + side] = Mathf.Min(t1, t2, t3); row.m["calf" + side] = c2; row.m["calfMin" + side] = Mathf.Min(c1, c2, c3);
                row.m["legLen" + side] = Vector3.Distance(ul, ll) + Vector3.Distance(ll, ft);
            }
            float miny = 99f; foreach (var m in shoes) foreach (var v in m.v) miny = Mathf.Min(miny, v.y);
            row.m["yaw"] = LastYaw; row.m["footMinY"] = shoes.Count > 0 ? miny : float.NaN; row.m["hipY"] = hips.y;
            return row;
        }

        // ------------------------------------------------------------------ rendering
        enum View { Front, Side, Back }
        static void CameraFor(View v, float cy, float half)
        {
            _cam.orthographic = true; _cam.orthographicSize = half;
            Vector3 center = new Vector3(0, cy, 0);
            Vector3 dir = v == View.Front ? Vector3.forward : v == View.Side ? Vector3.right : Vector3.back;
            _cam.transform.position = center + dir * 12f;
            _cam.transform.LookAt(center);
        }

        static void EnsureRt(int w, int h)
        {
            if (_rt != null && _rtW == w && _rtH == h) return;
            if (_rt != null) _rt.Release();
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); _rtW = w; _rtH = h;
            _readTex = new Texture2D(w, h, TextureFormat.RGB24, false);
        }

        static Color32[] RenderCell(int w, int h)
        {
            EnsureRt(w, h);
            _cam.targetTexture = _rt; _cam.aspect = (float)w / h;
            for (int i = 0; i < 2; i++) _cam.Render();
            RenderTexture.active = _rt;
            _readTex.ReadPixels(new Rect(0, 0, w, h), 0, 0); _readTex.Apply(false);
            RenderTexture.active = null; _cam.targetTexture = null;
            return _readTex.GetPixels32();
        }

        class Sheet
        {
            public Texture2D tex; public int cols, rows, cw, ch;
            public Sheet(int cols, int rows, int cw, int ch)
            {
                this.cols = cols; this.rows = rows; this.cw = cw; this.ch = ch;
                tex = new Texture2D(cols * cw, rows * ch, TextureFormat.RGB24, false);
                var fill = new Color32[tex.width * tex.height]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(30, 30, 34, 255);
                tex.SetPixels32(fill);
            }
            public void Put(int index, Color32[] px, string label)
            {
                int cx = index % cols, cy = index / cols;
                int x = cx * cw, y = (rows - 1 - cy) * ch;
                tex.SetPixels32(x, y, cw, ch, px);
                if (!string.IsNullOrEmpty(label)) Text(tex, x + 3, y + ch - 12, label, 1);
                // thin cell border
                for (int i = 0; i < cw; i++) { tex.SetPixel(x + i, y, new Color32(20, 20, 24, 255)); }
                for (int j = 0; j < ch; j++) { tex.SetPixel(x, y + j, new Color32(20, 20, 24, 255)); }
            }
            public void Save(string name, bool jpg)
            {
                tex.Apply(false);
                Directory.CreateDirectory(ImgDir); string path = ImgDir + "/" + Tag + "_" + name + (jpg ? ".jpg" : ".png");
                File.WriteAllBytes(path, jpg ? tex.EncodeToJPG(90) : tex.EncodeToPNG());
                Debug.Log("[ModelQa] wrote " + path);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // 5x7 bitmap font
        static readonly Dictionary<char, string> Font = BuildFont();
        static Dictionary<char, string> BuildFont()
        {
            var f = new Dictionary<char, string>();
            string[] g =
            {
                "A 01110,10001,10001,11111,10001,10001,10001", "B 11110,10001,10001,11110,10001,10001,11110", "C 01110,10001,10000,10000,10000,10001,01110", "D 11110,10001,10001,10001,10001,10001,11110",
                "E 11111,10000,10000,11110,10000,10000,11111", "F 11111,10000,10000,11110,10000,10000,10000", "G 01110,10001,10000,10111,10001,10001,01111", "H 10001,10001,10001,11111,10001,10001,10001",
                "I 01110,00100,00100,00100,00100,00100,01110", "J 00111,00010,00010,00010,00010,10010,01100", "K 10001,10010,10100,11000,10100,10010,10001", "L 10000,10000,10000,10000,10000,10000,11111",
                "M 10001,11011,10101,10101,10001,10001,10001", "N 10001,11001,10101,10011,10001,10001,10001", "O 01110,10001,10001,10001,10001,10001,01110", "P 11110,10001,10001,11110,10000,10000,10000",
                "Q 01110,10001,10001,10001,10101,10010,01101", "R 11110,10001,10001,11110,10100,10010,10001", "S 01111,10000,10000,01110,00001,00001,11110", "T 11111,00100,00100,00100,00100,00100,00100",
                "U 10001,10001,10001,10001,10001,10001,01110", "V 10001,10001,10001,10001,10001,01010,00100", "W 10001,10001,10001,10101,10101,11011,10001", "X 10001,10001,01010,00100,01010,10001,10001",
                "Y 10001,10001,01010,00100,00100,00100,00100", "Z 11111,00001,00010,00100,01000,10000,11111",
                "0 01110,10001,10011,10101,11001,10001,01110", "1 00100,01100,00100,00100,00100,00100,01110", "2 01110,10001,00001,00010,00100,01000,11111", "3 11110,00001,00001,01110,00001,00001,11110",
                "4 00010,00110,01010,10010,11111,00010,00010", "5 11111,10000,11110,00001,00001,10001,01110", "6 00110,01000,10000,11110,10001,10001,01110", "7 11111,00001,00010,00100,01000,01000,01000",
                "8 01110,10001,10001,01110,10001,10001,01110", "9 01110,10001,10001,01111,00001,00010,01100",
                "_ 00000,00000,00000,00000,00000,00000,11111", "- 00000,00000,00000,11111,00000,00000,00000", ". 00000,00000,00000,00000,00000,01100,01100", "/ 00001,00001,00010,00100,01000,10000,10000",
                ": 00000,01100,01100,00000,01100,01100,00000", "= 00000,00000,11111,00000,11111,00000,00000", "+ 00000,00100,00100,11111,00100,00100,00000", "( 00010,00100,01000,01000,01000,00100,00010", ") 01000,00100,00010,00010,00010,00100,01000",
            };
            foreach (var s in g) f[s[0]] = s.Substring(2);
            return f;
        }
        static void Text(Texture2D t, int x, int y, string s, int scale)
        {
            s = s.ToUpperInvariant();
            int w = s.Length * 6 * scale + 2;
            for (int i = 0; i < w; i++) for (int j = -1; j < 8 * scale; j++) { int px = x + i - 1, py = y + j; if (px >= 0 && px < t.width && py >= 0 && py < t.height) t.SetPixel(px, py, new Color32(0, 0, 0, 255)); }
            int cx = x;
            foreach (char ch in s)
            {
                string gl; if (Font.TryGetValue(ch, out gl))
                {
                    var rows = gl.Split(',');
                    for (int r = 0; r < 7; r++) for (int c = 0; c < 5; c++)
                        if (rows[r][c] == '1')
                            for (int sy = 0; sy < scale; sy++) for (int sx = 0; sx < scale; sx++)
                            { int px = cx + c * scale + sx, py = y + (6 - r) * scale + sy; if (px >= 0 && px < t.width && py >= 0 && py < t.height) t.SetPixel(px, py, new Color32(255, 255, 80, 255)); }
                }
                cx += 6 * scale;
            }
        }

        // ------------------------------------------------------------------ T / A pose sheets
        static void RunPoses(List<Row> rows)
        {
            foreach (string size in Sizes())
                foreach (bool clay in new[] { true, false })
                    foreach (string pose in new[] { "T", "A" })
                    {
                        if (!clay && pose == "A") continue;
                        var lods = Lods();
                        var sheet = new Sheet(lods.Length, 3, 440, 560);
                        for (int li = 0; li < lods.Length; li++)
                        {
                            var g = Spawn(KeyFor(size), lods[li], clay);
                            if (g == null) continue;
                            if (pose == "A") APose(g);
                            if (clay && pose == "T") { var an = g.GetComponent<Animator>(); Debug.Log("[ModelQa] bones " + size + " LOD" + lods[li] + " hips " + an.GetBoneTransform(HumanBodyBones.Hips).position.ToString("F3") + " lfoot " + an.GetBoneTransform(HumanBodyBones.LeftFoot).position.ToString("F3") + " head " + an.GetBoneTransform(HumanBodyBones.Head).position.ToString("F3") + " lhand " + an.GetBoneTransform(HumanBodyBones.LeftHand).position.ToString("F3")); }
                            Bake(g); Normalize(g);
                            if (clay) { var r = Measure(g, size, "LOD" + lods[li], pose + "pose"); rows.Add(r); }
                            int vi = 0;
                            foreach (View v in new[] { View.Front, View.Side, View.Back })
                            {
                                CameraFor(v, 0.95f, 1.1f);
                                sheet.Put(vi * lods.Length + li, RenderCell(440, 560), size + " LOD" + lods[li] + " " + pose + " " + v);
                                vi++;
                            }
                            UnityEngine.Object.DestroyImmediate(g);
                        }
                        sheet.Save((clay ? "pose_" : "posemat_") + size + "_" + pose, false);
                    }
        }

        /// <summary>
        /// The clip of an ArtLibrary key. The v0.5 library clips point at the imported FBX takes; the animation track has since re-pointed keys to cleaned copies
        /// (Animations/Clean/*.anim) that are their own work in progress. For body QA the ORIGINAL take is used when it can be found (same clip name in an FBX under Animations/),
        /// so before / after sheets compare the bodies, not the clips; the cleaned clip is the fallback.
        /// </summary>
        public static bool UseOriginalTakes = true;
        static AnimationClip GetClip(ArtLibrary lib, string key, int variant)
        {
            if (lib == null || !lib.HasClip(key)) return null;
            var c = lib.ClipVariant(key, Mathf.Min(variant, lib.ClipVariantCount(key) - 1));
            if (c == null || !UseOriginalTakes) return c;
            string p = AssetDatabase.GetAssetPath(c);
            if (!p.Contains("/Animations/Clean/")) return c;
            foreach (string g in AssetDatabase.FindAssets(c.name + " t:Model", new[] { CharacterSetup.Root + "/Animations" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != c.name) continue;
                var orig = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                if (orig != null) return orig;
            }
            return c;
        }

        // ------------------------------------------------------------------ clip sheets
        static void RunClips(List<Row> rows)
        {
            var lib = ArtLibrary.Load();
            foreach (string size in Sizes())
                foreach (int lod in Lods())
                {
                    var views = lod == 0 ? new[] { View.Front, View.Side, View.Back } : new[] { View.Front, View.Side };
                    var sheets = views.Select(v => new Sheet(8, 4, 280, 420)).ToArray();
                    for (int ci = 0; ci < Clips.Length; ci++)
                    {
                        var spec = Clips[ci];
                        var clip = GetClip(lib, spec.key, spec.variant);
                        if (clip == null) { Debug.LogWarning("[ModelQa] missing clip key " + spec.key); continue; }
                        var g = Spawn(KeyFor(size), lod, true);
                        float t = Mathf.Min(clip.length * spec.frac, 4f);
                        PoseClip(g, clip, t);
                        Bake(g); Normalize(g);
                        rows.Add(Measure(g, size, "LOD" + lod, spec.key));
                        for (int vi = 0; vi < views.Length; vi++)
                        {
                            CameraFor(views[vi], 1.05f, 1.35f);
                            sheets[vi].Put(ci, RenderCell(280, 420), spec.key + " " + t.ToString("0.0", CultureInfo.InvariantCulture) + "S");
                        }
                        UnityEngine.Object.DestroyImmediate(g);
                    }
                    for (int vi = 0; vi < views.Length; vi++) sheets[vi].Save("clips_" + size + "_LOD" + lod + "_" + views[vi], true);
                }
        }

        // ------------------------------------------------------------------ head / helmet / neck close-ups
        static void HeadsImpl()
        {
            string[] subjects = { "player_small", "player_medium", "player_large", "coach_a", "coach_b", "coach_c", "referee" };
            var sheet = new Sheet(3, subjects.Length, 400, 400);
            for (int i = 0; i < subjects.Length; i++)
            {
                var g = Spawn(subjects[i], 0, false);
                if (g == null) continue;
                Bake(g); Normalize(g);
                var head = g.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                int vi = 0;
                foreach (View v in new[] { View.Front, View.Side, View.Back })
                {
                    CameraFor(v, head.position.y + 0.02f, 0.3f);
                    _cam.transform.position += new Vector3(0, 0, 0);
                    sheet.Put(i * 3 + vi, RenderCell(400, 400), subjects[i] + " " + v);
                    vi++;
                }
                UnityEngine.Object.DestroyImmediate(g);
            }
            sheet.Save("heads", false);
        }

        // ------------------------------------------------------------------ region close-ups (eyes, neck seam, shoulder pad, glove / hand, hem, knee, shoe)
        static void CameraAt(View v, Vector3 center, float half)
        {
            _cam.orthographic = true; _cam.orthographicSize = half;
            Vector3 dir = v == View.Front ? Vector3.forward : v == View.Side ? Vector3.right : Vector3.back;
            _cam.transform.position = center + dir * 12f; _cam.transform.LookAt(center);
        }

        public static void RunCloseups() { Tag = Arg("-qaTag", "run"); Directory.CreateDirectory(OutDir); if (_cam == null) Studio(); CloseupsImpl(); }

        static void CloseupsImpl()
        {
            foreach (string size in Sizes())
            {
                var g = Spawn(KeyFor(size), 0, false);
                if (g == null) continue;
                Bake(g); Normalize(g);
                var a = g.GetComponent<Animator>();
                Func<HumanBodyBones, Vector3> P = b => a.GetBoneTransform(b).position;
                var holder = a.GetBoneTransform(HumanBodyBones.Head).Find("Helmet");
                var regions = new List<KeyValuePair<string, Vector3>>
                {
                    new KeyValuePair<string, Vector3>("EYES", P(HumanBodyBones.Head) + new Vector3(0, 0.07f, 0.05f)),
                    new KeyValuePair<string, Vector3>("NECK", P(HumanBodyBones.Neck) + new Vector3(0, 0.02f, 0.02f)),
                    new KeyValuePair<string, Vector3>("SHOULDER", P(HumanBodyBones.LeftUpperArm) + new Vector3(-0.03f, 0.02f, 0f)),
                    new KeyValuePair<string, Vector3>("HAND", P(HumanBodyBones.LeftHand) + new Vector3(-0.06f, 0f, 0f)),
                    new KeyValuePair<string, Vector3>("HEM", P(HumanBodyBones.Hips) + new Vector3(0, 0.08f, 0.03f)),
                    new KeyValuePair<string, Vector3>("KNEE", P(HumanBodyBones.LeftLowerLeg) + new Vector3(0, 0.05f, 0.03f)),
                    new KeyValuePair<string, Vector3>("SHOE", P(HumanBodyBones.LeftFoot) + new Vector3(0, -0.02f, 0.06f)),
                };
                float[] half = { 0.11f, 0.16f, 0.2f, 0.16f, 0.28f, 0.24f, 0.2f };
                var sheet = new Sheet(regions.Count, 2, 300, 300);
                for (int i = 0; i < regions.Count; i++)
                {
                    if (holder != null) holder.gameObject.SetActive(regions[i].Key != "EYES");
                    int vi = 0;
                    foreach (View v in new[] { View.Front, View.Side })
                    {
                        CameraAt(v, regions[i].Value, half[i]);
                        sheet.Put(vi * regions.Count + i, RenderCell(300, 300), size + " " + regions[i].Key + " " + v);
                        vi++;
                    }
                }
                sheet.Save("closeups_" + size, false);
                UnityEngine.Object.DestroyImmediate(g);
            }
        }

        // ------------------------------------------------------------------ prefab dump (renderers, slots, shaders, blend shapes, avatar): evidence for the MDL log
        public static void DumpPrefabs()
        {
            Directory.CreateDirectory("Logs/model_qa");
            var sb = new StringBuilder();
            var lib = ArtLibrary.Load();
            foreach (string key in new[] { "player_small", "player_medium", "player_large", "referee", "coach_a", "coach_b", "coach_c" })
            {
                var p = lib != null ? lib.Prefab(key) : null; if (p == null) { sb.AppendLine(key + ": MISSING"); continue; }
                var a = p.GetComponent<Animator>();
                sb.AppendLine("== " + key + " (" + p.name + ") animator=" + (a != null) + " avatar=" + (a != null && a.avatar != null ? a.avatar.name + " valid=" + a.avatar.isValid + " human=" + a.avatar.isHuman : "none") + " scale=" + p.transform.localScale.ToString("F3"));
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    var smr = r as SkinnedMeshRenderer; var mf = r.GetComponent<MeshFilter>();
                    var mesh = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                    sb.Append("  " + r.name + (r.gameObject.activeSelf ? "" : " [inactive]") + " " + (smr != null ? "SMR" : "MR") + " mesh=" + (mesh != null ? mesh.name + " v" + mesh.vertexCount + " sub" + mesh.subMeshCount + " bs" + mesh.blendShapeCount : "none"));
                    if (smr != null) sb.Append(" bones=" + smr.bones.Length + " quality=" + smr.quality + " localBounds=" + smr.localBounds.size.ToString("F2") + " bsWeights=" + string.Join("/", Enumerable.Range(0, mesh != null ? mesh.blendShapeCount : 0).Select(i => smr.GetBlendShapeWeight(i)).Where(w => w != 0f).Select(w => w.ToString("F0"))));
                    sb.AppendLine(" shadow=" + r.shadowCastingMode + " slots=" + string.Join(" | ", r.sharedMaterials.Select(m => m != null ? m.name + "(" + (m.shader != null ? m.shader.name : "?") + ")" : "null")));
                }
            }
            File.WriteAllText("Logs/model_qa/prefab_dump.txt", sb.ToString());
            Debug.Log("[ModelQa] prefab dump written");
        }

        // ------------------------------------------------------------------ diagnostic: does a clip drive the skinned body?
        public static void DiagPose()
        {
            var sb = new StringBuilder();
            var lib = ArtLibrary.Load();
            foreach (string key in new[] { "idle", "sprint", "tackle_wrap" })
            {
                var clip = lib.ClipVariant(key, 0);
                sb.AppendLine("clip " + key + " -> " + clip.name + " path=" + AssetDatabase.GetAssetPath(clip) + " human=" + clip.isHumanMotion + " legacy=" + clip.legacy + " len=" + clip.length + " bindings=" + AnimationUtility.GetCurveBindings(clip).Length);
                foreach (string src in new[] { "prefab", "fbx" })
                {
                    GameObject g;
                    if (src == "prefab") g = (GameObject)PrefabUtility.InstantiatePrefab(lib.Prefab("player_medium"));
                    else g = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.BodyPaths[1]));
                    var a = g.GetComponent<Animator>();
                    var head0 = a.GetBoneTransform(HumanBodyBones.Head).position; var hand0 = a.GetBoneTransform(HumanBodyBones.LeftHand).position;
                    var smr = g.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name.StartsWith("Torso"));
                    var m0 = new Mesh(); smr.BakeMesh(m0, false); var b0 = m0.bounds;
                    PoseClip(g, clip, Mathf.Min(clip.length * 0.5f, 2f));
                    var head1 = a.GetBoneTransform(HumanBodyBones.Head).position; var hand1 = a.GetBoneTransform(HumanBodyBones.LeftHand).position;
                    var m1 = new Mesh(); smr.BakeMesh(m1, false); var b1 = m1.bounds;
                    // are the renderer's bones the animator's bones?
                    var hipsT = a.GetBoneTransform(HumanBodyBones.Hips);
                    int same = 0; foreach (var bt in smr.bones) if (bt != null && bt.IsChildOf(g.transform)) same++;
                    sb.AppendLine("  " + src + ": headMoved=" + Vector3.Distance(head0, head1).ToString("F3") + " handMoved=" + Vector3.Distance(hand0, hand1).ToString("F3") + " torsoBakeBoundsMoved=" + Vector3.Distance(b0.center, b1.center).ToString("F3") + " size " + b0.size.ToString("F2") + "->" + b1.size.ToString("F2") + " smr.bones in hierarchy " + same + "/" + smr.bones.Length + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "null") + " sharedMesh=" + smr.sharedMesh.name + " hips=" + hipsT.name);
                    UnityEngine.Object.DestroyImmediate(g);
                }
            }
            Directory.CreateDirectory("Logs/model_qa");
            File.WriteAllText("Logs/model_qa/diag_pose.txt", sb.ToString());
            Debug.Log("[ModelQa] diag\n" + sb);
        }

        public static void DiagPose2()
        {
            var sb = new StringBuilder();
            var lib = ArtLibrary.Load();
            var clean = lib.ClipVariant("sprint", 0);
            var motus = AssetDatabase.LoadAllAssetsAtPath(CharacterSetup.Root + "/Animations/Motus/SprintCycleJR_1.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            sb.AppendLine("clean=" + clean.name + " " + AssetDatabase.GetAssetPath(clean) + " motus=" + (motus != null ? motus.name + " human=" + motus.isHumanMotion : "null"));
            foreach (var clip in new[] { clean, motus })
            {
                if (clip == null) continue;
                foreach (string variant in new[] { "plain", "update0", "rebind", "sync" })
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(lib.Prefab("player_medium"));
                    var a = g.GetComponent<Animator>();
                    var smr = g.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name.StartsWith("Legs"));
                    var m0 = new Mesh(); smr.BakeMesh(m0, false); var v0 = m0.vertices;
                    if (variant == "rebind") a.Rebind();
                    PlayableGraph graph; PixFootball.EditorTools.AnimPlay.PlayClip(a, clip, out graph);
                    graph.Evaluate(0f); graph.Evaluate(clip.length * 0.3f);
                    if (variant == "update0") a.Update(0f);
                    if (variant == "sync") { Physics.SyncTransforms(); foreach (var t in g.GetComponentsInChildren<Transform>()) { var p = t.position; t.position = p; } }
                    var m1 = new Mesh(); smr.BakeMesh(m1, false); var v1 = m1.vertices;
                    float d = 0f; for (int i = 0; i < v0.Length; i++) d += (v0[i] - v1[i]).magnitude; d /= v0.Length;
                    sb.AppendLine("  " + clip.name + " " + variant + ": mean baked vertex change " + d.ToString("F4") + " m; thigh bone moved " + a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation.eulerAngles.ToString("F1"));
                    graph.Destroy();
                    UnityEngine.Object.DestroyImmediate(g);
                }
            }
            File.WriteAllText("Logs/model_qa/diag_pose2.txt", sb.ToString());
            Debug.Log("[ModelQa] diag2\n" + sb);
        }

        /// <summary>Which clip is played for a key (original FBX take or cleaned copy) and how far the legs are folded (hip-to-foot distance / leg length).</summary>
        public static void DiagCrumple()
        {
            var sb = new StringBuilder();
            var lib = ArtLibrary.Load();
            foreach (var spec in Clips)
            {
                if (!lib.HasClip(spec.key)) continue;
                var libClip = lib.ClipVariant(spec.key, Mathf.Min(spec.variant, lib.ClipVariantCount(spec.key) - 1));
                var orig = GetClip(lib, spec.key, spec.variant);
                foreach (var pair in new[] { new KeyValuePair<string, AnimationClip>("library", libClip), new KeyValuePair<string, AnimationClip>("resolved", orig) })
                {
                    var g = Spawn("player_medium", 0, true);
                    var a = g.GetComponent<Animator>();
                    float t = Mathf.Min(pair.Value.length * spec.frac, 4f);
                    PoseClip(g, pair.Value, t);
                    var hip = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position; var knee = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position; var foot = a.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                    float leg = Vector3.Distance(hip, knee) + Vector3.Distance(knee, foot);
                    sb.AppendLine(spec.key.PadRight(20) + pair.Key.PadRight(9) + " " + AssetDatabase.GetAssetPath(pair.Value).Replace("Assets/_Licensed/DraftFever/Animations/", "") + " [" + pair.Value.name + "] hip-foot/leg " + (Vector3.Distance(hip, foot) / leg).ToString("F2") + " head height " + a.GetBoneTransform(HumanBodyBones.Head).position.y.ToString("F2"));
                    UnityEngine.Object.DestroyImmediate(g);
                }
            }
            Directory.CreateDirectory("Logs/model_qa");
            File.WriteAllText("Logs/model_qa/diag_crumple.txt", sb.ToString());
            Debug.Log("[ModelQa] crumple diag\n" + sb);
        }

        // ------------------------------------------------------------------ referee / coaches
        static void ExtrasImpl()
        {
            var lib = ArtLibrary.Load();
            string[] subjects = { "referee", "coach_a", "coach_b", "coach_c" };
            foreach (string s in subjects)
            {
                var bindSheet = new Sheet(3, 1, 440, 560);
                var g0 = Spawn(s, 0, false);
                if (g0 == null) continue;
                Bake(g0); Normalize(g0);
                int vi = 0;
                foreach (View v in new[] { View.Front, View.Side, View.Back }) { CameraFor(v, 0.95f, 1.1f); bindSheet.Put(vi++, RenderCell(440, 560), s + " bind " + v); }
                UnityEngine.Object.DestroyImmediate(g0);
                bindSheet.Save("extra_" + s + "_bind", false);
                var sheet = new Sheet(8, 2, 280, 420);
                for (int ci = 0; ci < ExtraClips.Length; ci++)
                {
                    var spec = ExtraClips[ci];
                    var clip = GetClip(lib, spec.key, 0);
                    if (clip == null) continue;
                    var g = Spawn(s, 0, false);
                    float t = Mathf.Min(clip.length * spec.frac, 4f);
                    PoseClip(g, clip, t); Bake(g); Normalize(g);
                    CameraFor(View.Front, 1.05f, 1.35f);
                    sheet.Put(ci, RenderCell(280, 420), spec.key);
                    UnityEngine.Object.DestroyImmediate(g);
                }
                sheet.Save("extra_" + s + "_clips", true);
            }
        }

        // ------------------------------------------------------------------ CSV + summary
        static readonly string[] Cols = { "waist_c", "waist_w", "waist_d", "chest_c", "chest_w", "hip_c", "hip_w", "thighL", "thighR", "thighMinL", "thighMinR", "calfL", "calfR", "calfMinL", "calfMinR", "legLenL", "legLenR", "footMinY", "hipY", "yaw" };

        static void WriteMeasurements(List<Row> rows)
        {
            if (rows.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendLine("body,lod,pose," + string.Join(",", Cols));
            foreach (var r in rows)
                sb.AppendLine(r.body + "," + r.lod + "," + r.pose + "," + string.Join(",", Cols.Select(c => r.m.ContainsKey(c) ? r.m[c].ToString("0.000", CultureInfo.InvariantCulture) : "")));
            string csv = OutDir + "/" + Tag + "_measurements.csv";
            File.WriteAllText(csv, sb.ToString());

            var s = new StringBuilder();
            s.AppendLine("# Model QA measurements (" + Tag + ")");
            s.AppendLine();
            s.AppendLine("Circumferences are hull perimeters (m) of cross-sections perpendicular to the bones of the posed meshes. Ratios are relative to the same body's LOD0 T-pose.");
            s.AppendLine();
            Func<Row, float> thigh = r => (r.m["thighL"] + r.m["thighR"]) * 0.5f;
            Func<Row, float> calf = r => (r.m["calfL"] + r.m["calfR"]) * 0.5f;
            s.AppendLine("## T-pose (bind) by body and LOD");
            s.AppendLine("| body | LOD | waist | chest | hip | thigh | calf | waist w x d | ratio waist | ratio thigh | ratio calf |");
            s.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (string body in rows.Select(r => r.body).Distinct())
            {
                var t0 = rows.FirstOrDefault(r => r.body == body && r.lod == "LOD0" && r.pose == "Tpose");
                foreach (var r in rows.Where(r => r.body == body && r.pose == "Tpose"))
                {
                    if (t0 == null) continue;
                    s.AppendLine("| " + body + " | " + r.lod + " | " + F(r.m["waist_c"]) + " | " + F(r.m["chest_c"]) + " | " + F(r.m["hip_c"]) + " | " + F(thigh(r)) + " | " + F(calf(r)) + " | " + F(r.m["waist_w"]) + " x " + F(r.m["waist_d"]) +
                        " | " + F(r.m["waist_c"] / Mathf.Max(1e-4f, t0.m["waist_c"])) + " | " + F(thigh(r) / Mathf.Max(1e-4f, thigh(t0))) + " | " + F(calf(r) / Mathf.Max(1e-4f, calf(t0))) + " |");
                }
            }
            s.AppendLine();
            s.AppendLine("## Worst ratios over all poses (vs the LOD0 T-pose of the same body)");
            s.AppendLine("| body | LOD | min waist (pose) | min thigh (pose) | min calf (pose) | max thigh (pose) | max leg stretch | foot min Y range |");
            s.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (string body in rows.Select(r => r.body).Distinct())
            {
                var t0 = rows.FirstOrDefault(r => r.body == body && r.lod == "LOD0" && r.pose == "Tpose");
                if (t0 == null) continue;
                foreach (string lod in rows.Where(r => r.body == body).Select(r => r.lod).Distinct())
                {
                    var rs = rows.Where(r => r.body == body && r.lod == lod).ToList();
                    Func<Func<Row, float>, bool, Row> pick = (f, min) => min ? rs.OrderBy(f).First() : rs.OrderByDescending(f).First();
                    var w = pick(r => r.m["waist_c"] / t0.m["waist_c"], true); var th = pick(r => Mathf.Min(r.m["thighMinL"], r.m["thighMinR"]) / Mathf.Min(t0.m["thighMinL"], t0.m["thighMinR"]), true);
                    var ca = pick(r => Mathf.Min(r.m["calfMinL"], r.m["calfMinR"]) / Mathf.Min(t0.m["calfMinL"], t0.m["calfMinR"]), true); var tx = pick(r => thigh(r) / thigh(t0), false);
                    var ls = pick(r => Mathf.Max(r.m["legLenL"], r.m["legLenR"]) / Mathf.Max(t0.m["legLenL"], t0.m["legLenR"]), false);
                    var fy = rs.Where(r => !float.IsNaN(r.m["footMinY"])).ToList();
                    s.AppendLine("| " + body + " | " + lod + " | " + F(w.m["waist_c"] / t0.m["waist_c"]) + " (" + w.pose + ") | " + F(Mathf.Min(th.m["thighMinL"], th.m["thighMinR"]) / Mathf.Min(t0.m["thighMinL"], t0.m["thighMinR"])) + " (" + th.pose + ") | " +
                        F(Mathf.Min(ca.m["calfMinL"], ca.m["calfMinR"]) / Mathf.Min(t0.m["calfMinL"], t0.m["calfMinR"])) + " (" + ca.pose + ") | " + F(thigh(tx) / thigh(t0)) + " (" + tx.pose + ") | " +
                        F(Mathf.Max(ls.m["legLenL"], ls.m["legLenR"]) / Mathf.Max(t0.m["legLenL"], t0.m["legLenR"])) + " (" + ls.pose + ") | " + (fy.Count > 0 ? F(fy.Min(r => r.m["footMinY"])) + " .. " + F(fy.Max(r => r.m["footMinY"])) : "") + " |");
                }
            }
            File.WriteAllText(OutDir + "/" + Tag + "_summary.md", s.ToString());
            Debug.Log("[ModelQa] wrote " + csv);
        }
        static string F(float v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }
    }
}
