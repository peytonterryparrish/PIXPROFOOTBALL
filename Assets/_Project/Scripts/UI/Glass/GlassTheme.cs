using TMPro;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The glass theme asset: tokens + the two TMP fonts (the DejaVu Sans pair bundled with the art pack, free licence) + the shared glass material. Every glass component reads its numbers from
    /// <see cref="Current"/>; a missing asset falls back to the built-in defaults so nothing breaks (the committed asset lives at Assets/_Project/UI/Glass/Resources/PixGlass/GlassTheme.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "PIX/Glass Theme", fileName = "GlassTheme")]
    public class GlassTheme : ScriptableObject
    {
        public const string ResourcesName = "PixGlass/GlassTheme";
        public const string EditorPath = "Assets/_Project/UI/Glass/Resources/PixGlass/GlassTheme.asset";
        public const string ShaderName = "PIX/UI/Glass";

        public GlassTokens tokens = new GlassTokens();
        public TMP_FontAsset fontTitle, fontBody;
        public Material material;

        static GlassTheme _cur;
        Material _runtimeMaterial;

        public static GlassTheme Current
        {
            get
            {
                if (_cur == null)
                {
                    _cur = Resources.Load<GlassTheme>(ResourcesName);
                    if (_cur == null) { _cur = CreateInstance<GlassTheme>(); _cur.hideFlags = HideFlags.DontSave; }
                }
                return _cur;
            }
        }

        public static GlassTokens T { get { return Current.tokens; } }

        /// <summary>Forgets the cached theme (tests, and after the asset was rebuilt).</summary>
        public static void ResetCache() { _cur = null; Shader.SetGlobalFloat("_PixGlassBackdropOn", 0f); }

        /// <summary>Heading / button / number font (DejaVu Sans Bold); falls back to the classic title font when the glass fonts were not built.</summary>
        public TMP_FontAsset Title { get { return fontTitle != null ? fontTitle : UiKit.ClassicTitle; } }
        /// <summary>Body font (DejaVu Sans).</summary>
        public TMP_FontAsset Body { get { return fontBody != null ? fontBody : UiKit.ClassicBody; } }

        /// <summary>The one shared material every glass surface draws with (batches into one draw call per canvas run).</summary>
        public Material GlassMaterial
        {
            get
            {
                if (material != null) { Apply(material); return material; }
                if (_runtimeMaterial == null)
                {
                    var sh = Shader.Find(ShaderName);
                    if (sh == null) return null;
                    _runtimeMaterial = new Material(sh) { name = "PixGlassUI (runtime)", hideFlags = HideFlags.DontSave };
                }
                Apply(_runtimeMaterial);
                return _runtimeMaterial;
            }
        }

        /// <summary>Copies the tokens into the shader properties.</summary>
        public void Apply(Material m)
        {
            if (m == null) return;
            var t = tokens;
            // Vectors, not Colors: the shader composes in gamma space like the pack art, so the sRGB values must reach it unconverted (a Color property would be linearised)
            m.SetVector("_SurfTop", new Vector4(t.surfaceTop.r, t.surfaceTop.g, t.surfaceTop.b, t.opacityTop));
            m.SetVector("_SurfMid", new Vector4(t.surfaceMid.r, t.surfaceMid.g, t.surfaceMid.b, t.opacityMid));
            m.SetVector("_SurfBot", new Vector4(t.surfaceBottom.r, t.surfaceBottom.g, t.surfaceBottom.b, t.opacityBottom));
            m.SetVector("_EdgeA", t.edgeA); m.SetVector("_EdgeB", t.edgeB); m.SetVector("_EdgeC", t.edgeC);
            m.SetVector("_ActionA", t.actionLeft); m.SetVector("_ActionB", t.actionRight);
            m.SetVector("_Accent", t.accent);
            m.SetVector("_FrostLow", t.frostDark); m.SetVector("_FrostHigh", t.frostLight);
            m.SetFloat("_Dim", t.backdropDim); m.SetFloat("_Desat", t.backdropDesaturate);
            m.SetFloat("_Refraction", t.refraction); m.SetFloat("_EdgeAlpha", t.edgeOpacity);
        }
    }
}
