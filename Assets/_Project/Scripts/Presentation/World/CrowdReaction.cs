using System.Collections.Generic;
using PixFootball.Gameplay;
using PixFootball.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.Presentation
{
    /// <summary>
    /// The stadium crowd reacts to big plays. The fans are baked billboards in a handful of chunks (two poses per fan in the atlas: seated, and arms up), so a reaction swaps the pose by
    /// scrolling the chunk's atlas window one row (every fan keeps his body and colours), wave-like around the bowl: each chunk cheers a little after the one before it, and while the
    /// excitement lasts it keeps flickering between the two poses (jumping up and down). Camera flashes pop in the stands: small white stars anchored to seats the camera can see, drawn as
    /// screen-space sprites (pooled, so they stay crisp and cost nothing). Both fade out with the excitement.
    /// </summary>
    public sealed class CrowdReaction : MonoBehaviour
    {
        sealed class Chunk { public Material Mat; public float Angle, Phase; public bool Up; }
        sealed class Flash { public RectTransform Rect; public Image Img; public Vector3 World; public float Age, Life, Size; public bool Alive; }

        readonly List<Chunk> _chunks = new List<Chunk>();
        readonly List<Vector3> _spots = new List<Vector3>();
        readonly List<Flash> _flashes = new List<Flash>();
        Canvas _canvas; Sprite _star;
        float _rows = 6f;
        float _excite, _waveClock;
        float _flashAcc;
        bool _built;
        public float Excitement { get { return _excite; } }
        public int ChunkCount { get { return _chunks.Count; } }
        public int FlashCount { get; private set; }
        public int WaveCount { get; private set; }
        public string FlashInfo { get; private set; } = "not built";
        public int ActiveFlashes { get { int n = 0; foreach (var f in _flashes) if (f.Alive) n++; return n; } }

        public void Build()
        {
            if (_built) return;
            _built = true;
            var info = FindAnyObjectByType<CrowdInfo>();
            if (info != null)
            {
                foreach (var r in info.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mat = r.material;     // one instance per chunk (the atlas window is per chunk)
                    var tex = mat != null ? mat.GetTexture("_BaseColorMap") as Texture2D : null;
                    if (tex != null && tex.wrapMode != TextureWrapMode.Repeat) tex.wrapMode = TextureWrapMode.Repeat;
                    if (tex != null) _rows = Mathf.Max(1f, tex.height / 256f);
                    Vector3 c = r.bounds.center;
                    _chunks.Add(new Chunk { Mat = mat, Angle = Mathf.Atan2(c.z, c.x), Phase = Random.value });
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh != null && mesh.isReadable)
                    {
                        var v = mesh.vertices;
                        for (int i = 0; i < v.Length && _spots.Count < 900; i += 4 * 40) _spots.Add(r.transform.TransformPoint(v[i]) + Vector3.up * 1.4f);
                    }
                    else _spots.Add(c);
                }
            }
            BuildFlashes();
        }

        void OnDestroy() { if (_star != null) { if (_star.texture != null) Destroy(_star.texture); Destroy(_star); } }   // QA-856

        void BuildFlashes()
        {
            _canvas = UiKit.CreateCanvas("CameraFlashCanvas", 4, transform);
            var gr = _canvas.GetComponent<GraphicRaycaster>(); if (gr != null) gr.enabled = false;
            _star = MakeStar();
            var root = (RectTransform)_canvas.transform;
            for (int i = 0; i < 70; i++)
            {
                var r = UiKit.Rect(root, "Flash" + i);
                r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(0.5f, 0.5f);
                var img = r.gameObject.AddComponent<Image>(); img.sprite = _star; img.raycastTarget = false; img.color = new Color(1f, 1f, 0.96f, 0f);
                r.gameObject.SetActive(false);
                _flashes.Add(new Flash { Rect = r, Img = img });
            }
            FlashInfo = "screen-space stars (pool of " + _flashes.Count + ") on " + _spots.Count + " seat anchors";
        }

        /// <summary>A soft white star: bright centre with four thin rays.</summary>
        static Sprite MakeStar()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "PIX_FlashStar" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - r * 1.6f); core *= core;
                    float rays = Mathf.Clamp01(1f - Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy)) * 14f) * Mathf.Clamp01(1f - r);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(core + rays * 0.9f)));
                }
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>A big play: strength 0..1 (touchdown 1, turnover 0.9, field goal 0.7, long gain 0.6).</summary>
        public void Excite(float strength)
        {
            Build();
            _excite = Mathf.Max(_excite, Mathf.Clamp01(strength));
            WaveCount++;
        }

        public void Calm() { _excite = 0f; }

        /// <summary>A seat the main camera can see (a few tries), so the flashes are where the viewer is looking.</summary>
        bool PickSpot(Camera cam, out Vector3 spot)
        {
            spot = Vector3.zero;
            if (_spots.Count == 0 || cam == null) return false;
            for (int i = 0; i < 16; i++)
            {
                var s = _spots[Random.Range(0, _spots.Count)];
                Vector3 v = cam.WorldToViewportPoint(s);
                if (v.z > 6f && v.x > 0.03f && v.x < 0.97f && v.y > 0.05f && v.y < 0.97f) { spot = s; return true; }
            }
            return false;
        }

        void Update()
        {
            if (!_built) return;
            // the crowd freezes with the pause menu (replays and shows run on unscaled time with the game paused: they keep it moving)
            if (GameSession.Paused && !(BroadcastDirector.Instance != null && BroadcastDirector.Instance.Busy)) return;
            float dt = Time.unscaledDeltaTime;
            var cam = Camera.main;
            if (_excite > 0.001f)
            {
                _waveClock += dt;
                _excite = Mathf.Max(0f, _excite - dt * 0.16f);
                float e = Mathf.Clamp01(_excite * 1.6f);
                for (int i = 0; i < _chunks.Count; i++)
                {
                    var c = _chunks[i];
                    // a wave that travels around the bowl twice a revolution plus a per-chunk jumping flicker
                    float wave = Mathf.Sin((_waveClock * 0.9f - c.Angle / (2f * Mathf.PI) * 2f) * Mathf.PI * 2f);
                    float jump = Mathf.Sin((_waveClock * 3.4f + c.Phase) * Mathf.PI * 2f);
                    bool up = e > 0.1f && (0.55f + 0.45f * wave + 0.25f * jump) * e > 0.34f;
                    if (up != c.Up) { c.Up = up; c.Mat.SetTextureOffset("_BaseColorMap", new Vector2(0f, up ? -1f / _rows : 0f)); }
                }
                _flashAcc += dt * (6f + 40f * e);
                while (_flashAcc >= 1f)
                {
                    _flashAcc -= 1f;
                    Vector3 s;
                    if (!PickSpot(cam, out s)) { _flashAcc = 0f; break; }
                    foreach (var f in _flashes)
                        if (!f.Alive) { f.Alive = true; f.World = s + Random.insideUnitSphere * 1.5f; f.Age = 0f; f.Life = Random.Range(0.18f, 0.38f); f.Size = Random.Range(0.8f, 1.5f); f.Rect.gameObject.SetActive(true); FlashCount++; break; }
                }
            }
            else
            {
                for (int i = 0; i < _chunks.Count; i++) { var c = _chunks[i]; if (c.Up) { c.Up = false; c.Mat.SetTextureOffset("_BaseColorMap", Vector2.zero); } }
            }
            // flashes: a quick pop and fade, anchored to their seat while the camera moves
            float scale = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
            foreach (var f in _flashes)
            {
                if (!f.Alive) continue;
                f.Age += dt;
                if (f.Age >= f.Life || cam == null) { f.Alive = false; f.Rect.gameObject.SetActive(false); continue; }
                Vector3 sp = cam.WorldToScreenPoint(f.World);
                if (sp.z < 3f) { f.Alive = false; f.Rect.gameObject.SetActive(false); continue; }
                float u = f.Age / f.Life;
                float k = u < 0.25f ? u / 0.25f : 1f - (u - 0.25f) / 0.75f;
                float px = Mathf.Clamp(f.Size * 2400f / Mathf.Max(8f, sp.z), 18f, 120f);   // bigger when closer
                f.Rect.anchoredPosition = new Vector2(sp.x, sp.y) / scale;
                f.Rect.sizeDelta = new Vector2(px, px) * (0.7f + 0.5f * k);
                f.Img.color = new Color(1f, 1f, 0.96f, Mathf.Clamp01(k));
            }
        }
    }
}
