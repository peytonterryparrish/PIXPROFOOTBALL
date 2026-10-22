using System.Collections.Generic;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// The wear of the playing surface, kept for the whole game in one top-down render texture (2048 x 4096 over the 80 x 140 m pitch, about 3.5 cm per pixel):
    /// R = scuffed grass / soil (divots), G = mud, B = footprints cleared in snow, A = painted line wear. The turf and marking shaders (PIX/Lit modes 1 and 4) read it through
    /// the global <c>_PixWearTex</c> / <c>_PixWearRect</c>. Stamps (tackles, piles, footsteps) are queued and written by a compute shader in one pass per frame - one texture
    /// and a handful of tiny dispatches for the whole pitch instead of dozens of pooled decal projectors.
    /// </summary>
    public sealed class FieldWear : MonoBehaviour
    {
        public const float OriginX = -40f, OriginZ = -70f, SizeX = 80f, SizeZ = 140f;
        public const int Width = 2048, Height = 4096;
        public const int MaxStampsPerFrame = 96;

        public static FieldWear Instance { get; private set; }

        struct Stamp { public float X, Z, Radius; public Color Channels; }
        readonly List<Stamp> _queue = new List<Stamp>(256);
        RenderTexture _rt;
        ComputeShader _cs;
        int _kStamp, _kDecay;
        float _decayAcc;
        public int StampsDrawn { get; private set; }
        public int Divots { get; private set; }
        public RenderTexture Texture { get { return _rt; } }
        public bool Ready { get { return _rt != null && _cs != null; } }

        static readonly int Result = Shader.PropertyToID("_Result"), TexW = Shader.PropertyToID("_TexW"), TexH = Shader.PropertyToID("_TexH"), Origin = Shader.PropertyToID("_Origin"),
                            Center = Shader.PropertyToID("_Center"), Color4 = Shader.PropertyToID("_Color"), Decay4 = Shader.PropertyToID("_Decay4");

        public static FieldWear Create(Transform parent)
        {
            if (Instance != null) { Instance.Clear(); return Instance; }
            var go = new GameObject("FieldWear");
            if (parent != null) go.transform.SetParent(parent, false);
            var w = go.AddComponent<FieldWear>();
            w.Build();
            return w;
        }

        void Awake() { if (Instance == null) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; if (_rt != null) { _rt.Release(); Destroy(_rt); } }

        void Build()
        {
            Instance = this;
            var assets = PixLookAssets.Load();
            _cs = assets != null ? assets.wearCompute : null;
            if (_cs == null || !SystemInfo.supportsComputeShaders) { Debug.LogWarning("[PIX] field wear unavailable (no compute shader)"); _cs = null; return; }
            _kStamp = _cs.FindKernel("Stamp"); _kDecay = _cs.FindKernel("Decay");
            _rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "PIX_FieldWear", enableRandomWrite = true, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false };
            _rt.Create();
            Clear();
            Publish();
        }

        /// <summary>Binds the wear texture and its world rectangle for the shaders.</summary>
        public void Publish()
        {
            if (_rt != null) Shader.SetGlobalTexture(PixLook.PixWearTex, _rt);
            Shader.SetGlobalVector(PixLook.PixWearRect, new Vector4(OriginX, OriginZ, 1f / SizeX, 1f / SizeZ));
        }

        public void Clear()
        {
            _queue.Clear(); Divots = 0; StampsDrawn = 0;
            if (_rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = _rt; GL.Clear(false, true, new Color(0, 0, 0, 0)); RenderTexture.active = prev;
        }

        // ------------------------------------------------------------------------------------------------ stamps
        /// <summary>A tackle / pile scuff on the grass: soil in R (and mud in G in the wet), radius in metres.</summary>
        public void Divot(Vector3 world, float radius, float strength, float mud)
        {
            Queue(world.x, world.z, radius, new Color(strength, mud * strength, 0f, strength * 0.7f));
            Divots++;
        }

        /// <summary>One footstep: a small mark of wear, mud in the wet, a cleared print in snow.</summary>
        public void Footstep(Vector3 world, float radius, float wear, float mud, float snowPrint)
        {
            if (wear <= 0f && mud <= 0f && snowPrint <= 0f) return;
            Queue(world.x, world.z, radius, new Color(wear, mud, snowPrint, wear * 0.8f));
        }

        void Queue(float x, float z, float r, Color c)
        {
            if (_rt == null || _queue.Count >= 512) return;
            _queue.Add(new Stamp { X = x, Z = z, Radius = r, Channels = c });
        }

        void LateUpdate() { Flush(); }

        public void Flush()
        {
            if (!Ready || _queue.Count == 0) return;
            _cs.SetTexture(_kStamp, Result, _rt);
            _cs.SetInt(TexW, Width); _cs.SetInt(TexH, Height);
            float ppmX = Width / SizeX, ppmZ = Height / SizeZ;
            int n = Mathf.Min(_queue.Count, MaxStampsPerFrame);   // QA-719: bounded work per frame, the rest waits for the next one
            for (int qi = 0; qi < n; qi++)
            {
                var s = _queue[qi];
                float cx = (s.X - OriginX) * ppmX, cy = (s.Z - OriginZ) * ppmZ, rx = Mathf.Max(1.5f, s.Radius * ppmX), ry = Mathf.Max(1.5f, s.Radius * ppmZ);
                int x0 = Mathf.FloorToInt(cx - rx), y0 = Mathf.FloorToInt(cy - ry);
                int gx = Mathf.CeilToInt(rx * 2f / 8f) + 1, gy = Mathf.CeilToInt(ry * 2f / 8f) + 1;
                _cs.SetInts(Origin, x0, y0);
                _cs.SetVector(Center, new Vector4(cx, cy, rx, ry));
                _cs.SetVector(Color4, s.Channels);
                _cs.Dispatch(_kStamp, gx, gy, 1);
            }
            StampsDrawn += n;
            _queue.RemoveRange(0, n);
        }

        /// <summary>Snow slowly fills the footprints back in: subtracts from the B channel (call with the amount of cover gained).</summary>
        public void RefillSnowPrints(float amount)
        {
            if (!Ready || amount <= 0f) return;
            _decayAcc += amount;
            if (_decayAcc < 1f / 255f) return;
            Flush();
            _cs.SetTexture(_kDecay, Result, _rt);
            _cs.SetInt(TexW, Width); _cs.SetInt(TexH, Height);
            _cs.SetVector(Decay4, new Vector4(0f, 0f, _decayAcc, 0f));
            _cs.Dispatch(_kDecay, Width / 8, Height / 8, 1);
            _decayAcc = 0f;
        }
    }
}
