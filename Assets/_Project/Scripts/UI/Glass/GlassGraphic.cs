using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>How a glass surface is filled.</summary>
    public enum GlassKind { Glass = 0, Action = 1, Solid = 2, Well = 3, Accent = 4 }

    /// <summary>
    /// A rounded-rectangle UI graphic drawn by the PIX/UI/Glass shader: frosted backdrop refraction, graphite tint gradient, thin silver edge, diagonal reflection and subtle noise, all analytic
    /// (crisp at any resolution). One quad per surface; the rounded shape, the edge and the focus glow are computed per pixel from the per-vertex data (see the shader header).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class GlassGraphic : MaskableGraphic
    {
        public GlassKind Kind = GlassKind.Glass;
        [Tooltip("Corner radius in canvas units; negative = the theme's default. Capped at half of the smaller side (a pill).")]
        public float Radius = -1f;
        [Tooltip("Edge width in canvas units; negative = the theme's default; 0 = no edge.")]
        public float EdgeWidth = -1f;
        [Range(0f, 3f)] public float Density = 1f;
        [Range(0f, 1f)] public float Sheen = 1f;
        [Range(0f, 1f)] public float Focus;
        [Range(0f, 1f)] public float Shadow;
        [Range(-0.3f, 0.5f)] public float Brighten;
        [Range(0f, 1f)] public float Backdrop = 1f;
        /// <summary>Extra quad margin for the glow / shadow outside the rect.</summary>
        public float Pad = 0f;
        /// <summary>Strength of the outer orange glow when focused (1 = full; chips and static accents use less).</summary>
        [Range(0f, 1f)] public float GlowScale = 1f;

        const int Channels = (int)(AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3);

        public override Texture mainTexture { get { return Texture2D.whiteTexture; } }

        public override Material defaultMaterial
        {
            get
            {
                var m = GlassTheme.Current.GlassMaterial;
                return m != null ? m : base.defaultMaterial;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            GlassBackdrop.AddUser();
            EnsureChannels();
        }

        protected override void OnDisable()
        {
            GlassBackdrop.RemoveUser();
            base.OnDisable();
        }

        protected override void OnTransformParentChanged() { base.OnTransformParentChanged(); EnsureChannels(); }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); EnsureChannels(); }

        /// <summary>The shader reads texcoord 1..3: the (root) canvas must forward them.</summary>
        void EnsureChannels()
        {
            var c = canvas;
            if (c == null) return;
            c = c.rootCanvas;
            if (((int)c.additionalShaderChannels & Channels) != Channels) c.additionalShaderChannels |= (AdditionalCanvasShaderChannels)Channels;
        }

        public static void Prepare(Canvas c)
        {
            if (c == null) return;
            c.additionalShaderChannels |= (AdditionalCanvasShaderChannels)Channels;
        }

        public void SetFocus(float f) { f = Mathf.Clamp01(f); if (Mathf.Abs(f - Focus) < 0.002f) return; Focus = f; SetVerticesDirty(); }
        public void SetBrighten(float b) { if (Mathf.Abs(b - Brighten) < 0.002f) return; Brighten = b; SetVerticesDirty(); }
        public void SetKind(GlassKind k) { if (Kind == k) return; Kind = k; SetVerticesDirty(); }
        public void SetDensity(float d) { if (Mathf.Abs(d - Density) < 0.002f) return; Density = d; SetVerticesDirty(); }

        /// <summary>The quad margin actually used: room for the focus glow and the shadow when either can show.</summary>
        float EffectivePad { get { return Mathf.Max(Pad, (Shadow > 0.001f ? 26f : 0f), (Focus > 0.001f ? 28f : 0f)); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            if (r.width < 0.5f || r.height < 0.5f) return;
            var t = GlassTheme.T;
            float radius = Radius >= 0f ? Radius : t.cornerRadius;
            float edge = EdgeWidth >= 0f ? EdgeWidth : t.edgeWidth;
            float pad = EffectivePad;
            Vector2 c = r.center, half = r.size * 0.5f;
            var col = (Color32)color;

            var uv0Base = new Vector4(0, 0, half.x, half.y);
            var uv1 = new Vector4(radius, edge, Focus, Shadow);
            var uv2 = new Vector4(t.sheen * Sheen, t.noise, Backdrop, (float)(int)Kind);
            var uv3 = new Vector4(Density, Brighten, GlowScale, 0f);

            float x0 = r.xMin - pad, x1 = r.xMax + pad, y0 = r.yMin - pad, y1 = r.yMax + pad;
            AddVert(vh, x0, y0, c, half, col, uv1, uv2, uv3);
            AddVert(vh, x0, y1, c, half, col, uv1, uv2, uv3);
            AddVert(vh, x1, y1, c, half, col, uv1, uv2, uv3);
            AddVert(vh, x1, y0, c, half, col, uv1, uv2, uv3);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }

        static void AddVert(VertexHelper vh, float x, float y, Vector2 centre, Vector2 half, Color32 col, Vector4 uv1, Vector4 uv2, Vector4 uv3)
        {
            var v = UIVertex.simpleVert;
            v.position = new Vector3(x, y, 0f);
            v.color = col;
            v.uv0 = new Vector4(x - centre.x, y - centre.y, half.x, half.y);
            v.uv1 = uv1; v.uv2 = uv2; v.uv3 = uv3;
            vh.AddVert(v);
        }

#if UNITY_EDITOR
        protected override void OnValidate() { base.OnValidate(); SetVerticesDirty(); }
#endif
    }
}
