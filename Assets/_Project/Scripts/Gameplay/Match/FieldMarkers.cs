using PixFootball.Art;
using PixFootball.Core;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Broadcast overlays drawn on the grass (flat strips under the players, never over them): the blue line of scrimmage, the yellow
    /// first-down line, and the violet ring under the player the user controls.
    /// </summary>
    public sealed class FieldMarkers : MonoBehaviour
    {
        const float LineY = 0.03f, WidthM = 48.76f, RingSize = 2.5f;
        Transform _los, _fd, _ring;
        Renderer _losR, _fdR, _ringR;
        Transform _follow;
        float _pulse;

        public static FieldMarkers Create(Transform parent)
        {
            var go = new GameObject("FieldMarkers");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<FieldMarkers>();
            var rt = PixRuntimeAssets.Load();
            m._los = Strip(go.transform, "LineOfScrimmage", rt != null ? rt.lineLos : null, WidthM, 0.28f, out m._losR);
            m._fd = Strip(go.transform, "FirstDownLine", rt != null ? rt.lineFirstDown : null, WidthM, 0.30f, out m._fdR);
            m._ring = Strip(go.transform, "UserRing", rt != null ? rt.ringMarker : null, RingSize, RingSize, out m._ringR);
            m._ring.localPosition = new Vector3(0f, LineY + 0.01f, 0f);
            m.BuildMarkerLook(rt != null ? rt.ringMarker : null);
            m.HideAll();
            return m;
        }

        static Transform Strip(Transform parent, string name, Material mat, float w, float d, out Renderer r)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name; q.transform.SetParent(parent, false);
            Destroy(q.GetComponent<Collider>());
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(w, d, 1f);
            r = q.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return q.transform;
        }

        GameObject _collegeHashes;

        /// <summary>College rules put the hash marks 40 feet apart (the stadium is painted with the pro 18.5-foot hashes): draw the college ticks on every yard line.</summary>
        public void SetHashMarks(RuleSet rules)
        {
            bool college = rules != null && rules.HashWidthFeet > 30f;
            if (!college) { if (_collegeHashes != null) _collegeHashes.SetActive(false); return; }
            if (_collegeHashes == null)
            {
                var go = new GameObject("CollegeHashes"); go.transform.SetParent(transform, false); _collegeHashes = go;
                var verts = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>();
                float hx = FieldGeometry.HashOffsetFromCenter(rules) * PlayFrame.YardsToMeters;
                for (int yd = 0; yd <= 100; yd++)
                {
                    float z = (-50f + yd) * PlayFrame.YardsToMeters;
                    foreach (float sx in new[] { -hx, hx })
                    {
                        int i = verts.Count;
                        verts.Add(new Vector3(sx - 0.34f, LineY + 0.003f, z - 0.07f)); verts.Add(new Vector3(sx + 0.34f, LineY + 0.003f, z - 0.07f));
                        verts.Add(new Vector3(sx + 0.34f, LineY + 0.003f, z + 0.07f)); verts.Add(new Vector3(sx - 0.34f, LineY + 0.003f, z + 0.07f));
                        tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
                    }
                }
                var mesh = new Mesh { name = "CollegeHashes" }; mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var rt = PixRuntimeAssets.Load();
                if (rt != null && rt.hashMark != null) r.sharedMaterial = rt.hashMark; else if (rt != null) r.sharedMaterial = rt.lineFirstDown;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            _collegeHashes.SetActive(true);
        }

        // ------------------------------------------------------------------------------------------ v0.6 markers: teal ring under the user, gold stars under the eligible receivers (All-Pro 2K8 style)
        readonly System.Collections.Generic.List<Transform> _starQuads = new System.Collections.Generic.List<Transform>();
        readonly System.Collections.Generic.List<Transform> _starFollow = new System.Collections.Generic.List<Transform>();
        Material _starMat, _ringMat;

        /// <summary>Procedural marker texture: a bright ring (teal, for the user) or a five-point star (gold with a white rim, for receivers).</summary>
        static Texture2D MakeMarkerTexture(bool star, Color fill, Color rim)
        {
            const int N = 192;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = star ? "PIX_StarMarker" : "PIX_RingMarker", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    Color c = Color.clear; float a = 0f;
                    if (!star)
                    {
                        // C-001 (v0.7 note 1): a thick bright band with a near-black outline on both edges, so the user's ring reads on grass, paint and shadow from every camera
                        float core = Mathf.SmoothStep(0.70f, 0.74f, r) * (1f - Mathf.SmoothStep(0.92f, 0.95f, r));
                        float outline = Mathf.SmoothStep(0.64f, 0.68f, r) * (1f - Mathf.SmoothStep(0.97f, 0.995f, r));
                        float inner = Mathf.SmoothStep(0.48f, 0.50f, r) * (1f - Mathf.SmoothStep(0.52f, 0.54f, r));
                        a = Mathf.Max(outline, inner * 0.9f);
                        Color body = Color.Lerp(fill, rim, Mathf.SmoothStep(0.72f, 0.93f, r));
                        c = Color.Lerp(new Color(0.02f, 0.03f, 0.05f), body, core);
                        if (inner > core) c = rim;
                    }
                    else
                    {
                        // five-point star: radius as a function of angle between an inner and an outer radius
                        float ang = Mathf.Atan2(v, u) - Mathf.PI * 0.5f;
                        float k = Mathf.Abs(Mathf.Repeat(ang, Mathf.PI * 2f / 5f) / (Mathf.PI * 2f / 5f) - 0.5f) * 2f;   // 1 at the inner notch, 0 at the tip
                        float edge = Mathf.Lerp(0.98f, 0.42f, Mathf.SmoothStep(0f, 1f, k));
                        float inside = 1f - Mathf.SmoothStep(edge - 0.02f, edge, r);
                        float core = 1f - Mathf.SmoothStep(edge - 0.16f, edge - 0.13f, r);
                        a = inside;
                        c = Color.Lerp(rim, fill, core);
                    }
                    px[y * N + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(a) * 255f));
                }
            t.SetPixels32(px); t.Apply(true, false);
            return t;
        }

        Material MarkerMaterial(Material template, Texture2D tex)
        {
            if (template == null) return null;
            var m = new Material(template);
            m.SetTexture("_UnlitColorMap", tex); m.SetColor("_UnlitColor", Color.white);
            m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.3f);
            return m;
        }

        void BuildMarkerLook(Material template)
        {
            if (template == null) return;
            _ringMat = MarkerMaterial(template, MakeMarkerTexture(false, new Color(0.15f, 0.95f, 1f), new Color(1f, 1f, 1f)));
            _starMat = MarkerMaterial(template, MakeMarkerTexture(true, new Color(1f, 0.80f, 0.12f), Color.white));
            if (_ringMat != null && _ringR != null) _ringR.sharedMaterial = _ringMat;
        }

        /// <summary>Gold stars under the given players (the eligible receivers before the snap); null / empty hides them. Stars follow their player.</summary>
        public void SetStars(System.Collections.Generic.IList<Transform> players)
        {
            _starFollow.Clear();
            if (players != null && PixSettings.PreSnapStars) foreach (var p in players) if (p != null) _starFollow.Add(p);
            while (_starQuads.Count < _starFollow.Count)
            {
                var q = Strip(transform, "ReceiverStar", _starMat, 1.15f, 1.15f, out Renderer r);
                _starQuads.Add(q);
            }
            for (int i = 0; i < _starQuads.Count; i++) _starQuads[i].gameObject.SetActive(i < _starFollow.Count);
        }

        void UpdateStars()
        {
            for (int i = 0; i < _starFollow.Count && i < _starQuads.Count; i++)
            {
                if (_starFollow[i] == null) { _starQuads[i].gameObject.SetActive(false); continue; }
                Vector3 p = _starFollow[i].position; p.y = LineY + 0.011f;
                _starQuads[i].position = p;
                _starQuads[i].rotation = Quaternion.Euler(90f, 0f, 0f);
                float s = 1.15f + 0.05f * Mathf.Sin(Time.time * 3f + i);
                _starQuads[i].localScale = new Vector3(s, s, 1f);
            }
        }

        public void HideAll() { _los.gameObject.SetActive(false); _fd.gameObject.SetActive(false); _ring.gameObject.SetActive(false); SetStars(null); }

        /// <summary>Spots are 0..100 from the offense's own goal line.</summary>
        public void SetLines(PlayFrame frame, float losSpot, float lineToGainSpot, bool showLos, bool showFirstDown)
        {
            _los.gameObject.SetActive(showLos);
            _los.position = new Vector3(0f, LineY, frame.SpotToZ(losSpot));
            bool fd = showFirstDown && lineToGainSpot < 99.5f && lineToGainSpot > 0.5f;
            _fd.gameObject.SetActive(fd);
            _fd.position = new Vector3(0f, LineY + 0.004f, frame.SpotToZ(lineToGainSpot));
        }

        public void SetRing(Transform follow)
        {
            _follow = follow;
            _ring.gameObject.SetActive(follow != null);
        }

        void LateUpdate()
        {
            UpdateStars();
            if (_follow == null || !_ring.gameObject.activeSelf) return;
            _pulse += Time.deltaTime;
            Vector3 p = _follow.position; p.y = LineY + 0.012f;
            _ring.position = p;
            float s = RingSize + 0.1f * Mathf.Sin(_pulse * 4f);
            _ring.localScale = new Vector3(s, s, 1f);
            _ring.rotation = Quaternion.Euler(90f, _pulse * 40f, 0f);
        }
    }
}

