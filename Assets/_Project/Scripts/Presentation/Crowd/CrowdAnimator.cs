using System.Collections.Generic;
using System.Runtime.InteropServices;
using PixFootball.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.Presentation
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD: animates the baked crowd billboards on the GPU. The ~228 chunk meshes are merged into a few sector meshes whose vertex buffers a compute shader
    /// (Resources/PixCrowdSway.compute) rewrites every frame, one thread per fan: idle sway / breathing, arms-up pose swap, hopping, standing and slumping per team section, with a wave round the bowl.
    /// The material, lighting and shadows are untouched (the same HDRP Lit cutout quads); cost is one dispatch per visible sector. Build() returns false (and the old per-chunk pose swap in
    /// <see cref="CrowdReaction"/> is used) when compute shaders or readable meshes are missing. <c>-pixNoCrowdAnim</c> turns it off.
    /// </summary>
    public sealed class CrowdAnimator : MonoBehaviour
    {
        static readonly int Sectors = FlagInt("-pixCrowdSectors", 24);
        static readonly bool NoDispatch = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixCrowdNoDispatch") >= 0;
        static int FlagInt(string name, int def) { var a = System.Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) { int v; if (int.TryParse(a[i + 1], out v)) return Mathf.Clamp(v, 1, 256); } return def; }
        int _frame;

        [StructLayout(LayoutKind.Sequential)]
        struct FanData
        {
            public Vector3 Root, Right, Up;
            public float U0, U1, Vb, Vt, Phase, Team, Angle;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Vert { public Vector3 P, N; public Vector2 UV; }

        sealed class Sector
        {
            public Mesh Mesh; public GraphicsBuffer VB; public ComputeBuffer Fans; public int Count; public MeshRenderer Renderer; public GameObject Go;
        }

        readonly List<Sector> _sectors = new List<Sector>();
        readonly List<MeshRenderer> _hidden = new List<MeshRenderer>();
        ComputeShader _cs; int _kernel;
        float _rowStep = 1f / 6f;
        public bool Active { get; private set; }
        public int FanCount { get; private set; }
        public int SectorCount { get { return _sectors.Count; } }
        public int Dispatches { get; private set; }
        public string Info { get; private set; } = "not built";
        public Vector4 Home, Away;
        float _clock, _waveClock;

        /// <summary>Merges the crowd chunks under `root` into animated sector meshes. False = animation unavailable (nothing was changed).</summary>
        public bool Build(Transform root)
        {
            if (Active) return true;
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-pixNoCrowdAnim") { Info = "disabled by -pixNoCrowdAnim"; return false; }
            if (!SystemInfo.supportsComputeShaders) { Info = "no compute shaders"; return false; }
            _cs = Resources.Load<ComputeShader>("PixCrowdSway");
            if (_cs == null) { Info = "PixCrowdSway.compute missing"; return false; }
            _kernel = _cs.FindKernel("Sway");
            var renderers = new List<MeshRenderer>(root.GetComponentsInChildren<MeshRenderer>());
            if (renderers.Count == 0) { Info = "no crowd chunks"; return false; }
            Material mat = renderers[0].sharedMaterial;
            var tex = mat != null ? mat.GetTexture("_BaseColorMap") : null;
            if (tex != null) _rowStep = 256f / Mathf.Max(256f, tex.height);
            // the baked per-fan data of this stadium (the chunk meshes are static-batched at runtime and cannot be read back)
            int want = 0; var info = root.GetComponent<CrowdInfo>(); if (info != null) want = info.FanCount;
            float[] raw = null; int count = 0;
            foreach (string res in new[] { "PixCrowdFans", "PixCrowdFansCollege" })
            {
                var ta = Resources.Load<TextAsset>(res); if (ta == null) continue;
                var b = ta.bytes; int c = System.BitConverter.ToInt32(b, 0);
                if (want > 0 && c != want) continue;
                raw = new float[c * 16]; System.Buffer.BlockCopy(b, 4, raw, 0, c * 16 * 4); count = c; break;
            }
            if (raw == null) { Info = "no baked fan data for " + want + " fans (PIX Football > Assets > Bake Crowd Fan Data)"; return false; }
            var perSector = new List<FanData>[Sectors]; var verts = new List<Vert>[Sectors];
            for (int i = 0; i < Sectors; i++) { perSector[i] = new List<FanData>(); verts[i] = new List<Vert>(); }
            var m4 = root.localToWorldMatrix;
            for (int f = 0; f < count; f++)
            {
                int o = f * 16;
                Vector3 r0 = m4.MultiplyPoint3x4(new Vector3(raw[o], raw[o + 1], raw[o + 2]));
                Vector3 rt = m4.MultiplyVector(new Vector3(raw[o + 3], raw[o + 4], raw[o + 5])), up = m4.MultiplyVector(new Vector3(raw[o + 6], raw[o + 7], raw[o + 8]));
                float ang = Mathf.Atan2(r0.z, r0.x);
                int sec = Mathf.Clamp(Mathf.FloorToInt((ang + Mathf.PI) / (2f * Mathf.PI) * Sectors), 0, Sectors - 1);
                var fan = new FanData { Root = r0, Right = rt, Up = up, U0 = raw[o + 9], U1 = raw[o + 10], Vb = raw[o + 11], Vt = raw[o + 12], Phase = raw[o + 13], Team = raw[o + 14], Angle = ang };
                perSector[sec].Add(fan);
                Vector3 fwd = Vector3.Cross(rt.normalized, Vector3.up).normalized;
                Vector3 nrm = (fwd + Vector3.up * 0.12f).normalized;
                verts[sec].Add(new Vert { P = r0 + rt, N = nrm, UV = new Vector2(fan.U0, fan.Vb) });
                verts[sec].Add(new Vert { P = r0 - rt, N = nrm, UV = new Vector2(fan.U1, fan.Vb) });
                verts[sec].Add(new Vert { P = r0 - rt + up, N = nrm, UV = new Vector2(fan.U1, fan.Vt) });
                verts[sec].Add(new Vert { P = r0 + rt + up, N = nrm, UV = new Vector2(fan.U0, fan.Vt) });
            }
            var attrs = new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            };
            var first = renderers[0];
            for (int s = 0; s < Sectors; s++)
            {
                int n = perSector[s].Count; if (n == 0) continue;
                var mesh = new Mesh { name = "CrowdSector" + s, indexFormat = IndexFormat.UInt32 };
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.SetVertexBufferParams(n * 4, attrs);
                mesh.SetVertexBufferData(verts[s], 0, 0, n * 4, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                var idx = new int[n * 6];
                for (int i = 0; i < n; i++) { int v = i * 4, t = i * 6; idx[t] = v; idx[t + 1] = v + 2; idx[t + 2] = v + 1; idx[t + 3] = v; idx[t + 4] = v + 3; idx[t + 5] = v + 2; }
                mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt32);
                mesh.SetIndexBufferData(idx, 0, 0, idx.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length), MeshUpdateFlags.DontRecalculateBounds);
                Bounds b = new Bounds(verts[s][0].P, Vector3.zero); foreach (var vv in verts[s]) b.Encapsulate(vv.P);
                b.Expand(new Vector3(1.5f, 1.5f, 1.5f)); mesh.bounds = b;
                var go = new GameObject("CrowdSector" + s); go.layer = first.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = first.receiveShadows; mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                var sec = new Sector { Mesh = mesh, Count = n, Renderer = mr, Go = go };
                sec.Fans = new ComputeBuffer(n, Marshal.SizeOf<FanData>()); sec.Fans.SetData(perSector[s]);
                sec.VB = mesh.GetVertexBuffer(0);
                _sectors.Add(sec); FanCount += n;
            }
            if (_sectors.Count == 0) { Info = "no fans"; return false; }
            foreach (var r in renderers) { r.enabled = false; _hidden.Add(r); }
            Active = true;
            Info = _sectors.Count + " sector meshes, " + FanCount + " fans, vertex stride " + _sectors[0].Mesh.GetVertexBufferStride(0) + " B";
            return true;
        }

        void OnDestroy()
        {
            foreach (var s in _sectors) { if (s.Fans != null) s.Fans.Release(); if (s.VB != null) s.VB.Dispose(); if (s.Mesh != null) Destroy(s.Mesh); if (s.Go != null) Destroy(s.Go); }
            _sectors.Clear();
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            Active = false;
        }

        /// <summary>Advance the animation clock (not while paused) and rewrite the visible sectors.</summary>
        public void Tick(float dt)
        {
            if (!Active) return;
            _clock += dt; _waveClock += dt * (0.4f + Mathf.Max(Home.x, Away.x));
            _cs.SetFloat("_Time", _clock); _cs.SetFloat("_WaveClock", _waveClock); _cs.SetFloat("_RowStep", _rowStep);
            _cs.SetVector("_Home", Home); _cs.SetVector("_Away", Away);
            Dispatches = 0;
            if (NoDispatch) return;
            _frame++;
            // a quiet crowd only sways by a centimetre: rewrite each sector at about 10 Hz; while a section cheers, stands or slumps, at 30 Hz
            bool lively = Home.x > 0.03f || Away.x > 0.03f || Home.y > 0.02f || Away.y > 0.02f || Home.z > 0.02f || Away.z > 0.02f;
            int period = lively ? 2 : 6;
            for (int si = 0; si < _sectors.Count; si++)
            {
                var s = _sectors[si];
                if (!s.Renderer.isVisible && _clock > 1f) continue;
                if (((si + _frame) % period) != 0 && _clock > 1f) continue;      // staggered: every sector is rewritten every `period` frames
                _cs.SetBuffer(_kernel, "_Fans", s.Fans); _cs.SetBuffer(_kernel, "_VB", s.VB);
                _cs.SetInt("_Count", s.Count); _cs.SetInt("_Stride", 32); _cs.SetInt("_UvOffset", 24);
                _cs.Dispatch(_kernel, (s.Count + 63) / 64, 1, 1);
                Dispatches++;
            }
        }
    }
}
