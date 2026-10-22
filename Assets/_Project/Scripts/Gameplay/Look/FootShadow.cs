using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9 (GFX-411): a soft dark blob (about 0.9 m) on the turf under every player, on EVERY graphics tier. Low and Medium have no SSAO / contact shadows and the broadcast light is flat, so
    /// players floated; the blob grounds them at any distance and under any light (it is unlit, alpha only). One shared quad mesh, one radial-gradient texture and one material for all 22.
    /// The blob is a child of the pooled view root (the ground point), so it follows the player with no per-frame work.
    /// </summary>
    public static class FootShadow
    {
        public const string ChildName = "FootShadow";
        public static float Diameter = 0.95f, Alpha = 0.34f, Lift = 0.035f;
        static Mesh _mesh; static Material _mat; static Texture2D _tex;

        static Texture2D Gradient()
        {
            if (_tex != null) return _tex;
            const int n = 64;
            _tex = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "FootShadowGradient", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a); a = Mathf.Pow(a, 1.25f);   // smooth, denser core
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            _tex.SetPixels32(px); _tex.Apply(false, true);
            return _tex;
        }

        static Mesh Quad()
        {
            if (_mesh != null) return _mesh;
            _mesh = new Mesh { name = "FootShadowQuad", hideFlags = HideFlags.HideAndDontSave };
            _mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            _mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));
            return _mesh;
        }

        static Material Mat()
        {
            if (_mat != null) return _mat;
            var a = PixLook.Assets;
            var sh = a != null ? a.fxShader : null;
            if (sh == null) return null;
            _mat = new Material(sh) { name = "FootShadow", hideFlags = HideFlags.HideAndDontSave };
            _mat.SetTexture("_MainTex", Gradient());
            _mat.SetColor("_Color", new Color(0.01f, 0.012f, 0.01f, Alpha));
            _mat.SetFloat("_Intensity", 1f);
            _mat.SetFloat("_SrcBlend", 5f); _mat.SetFloat("_DstBlend", 10f); _mat.SetFloat("_ZTest", 4f);
            _mat.renderQueue = 2999;   // after the turf, before the other transparent effects
            return _mat;
        }

        /// <summary>Adds the blob under a view root (once); returns it, or null when the effect assets are missing.</summary>
        public static Transform Ensure(Transform view)
        {
            if (view == null) return null;
            var have = view.Find(ChildName); if (have != null) return have;
            var m = Mat(); if (m == null) return null;
            var go = new GameObject(ChildName);
            go.transform.SetParent(view, false);
            go.transform.localPosition = new Vector3(0f, Lift, 0f);
            go.transform.localScale = new Vector3(Diameter, 1f, Diameter);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        public static void SetEnabled(Transform view, bool on)
        {
            var c = view != null ? view.Find(ChildName) : null;
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>The blob colour follows the light: darker under the sun, lighter in overcast / dusk / night.</summary>
        public static void SetStrength(float k)
        {
            var m = Mat(); if (m == null) return;
            m.SetColor("_Color", new Color(0.01f, 0.012f, 0.01f, Mathf.Clamp01(Alpha * k)));
        }
    }
}
