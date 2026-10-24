using TMPro;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The glass theme asset: tokens + the two TMP fonts + the shared surface material. Two assets exist: GlassTheme (v0.7: DejaVu Sans pair, frosted glass) and TileTheme (v0.11 Direction 16: Barlow Semi Condensed Bold / Medium,
    /// flat tiles). <see cref="Current"/> returns the one that matches <see cref="UiTheme.Current"/>; every glass component reads its numbers from it. A missing asset falls back to built-in defaults
    /// (<see cref="GlassTokens.Tile"/> for Tile) so nothing breaks (the committed assets live at Assets/_Project/UI/Glass/Resources/PixGlass/).
    /// </summary>
    [CreateAssetMenu(menuName = "PIX/Glass Theme", fileName = "GlassTheme")]
    public class GlassTheme : ScriptableObject
    {
        public const string ResourcesName = "PixGlass/GlassTheme";
        public const string TileResourcesName = "PixGlass/TileTheme";
        public const string EditorPath = "Assets/_Project/UI/Glass/Resources/PixGlass/GlassTheme.asset";
        public const string TileEditorPath = "Assets/_Project/UI/Glass/Resources/PixGlass/TileTheme.asset";
        public const string ShaderName = "PIX/UI/Glass";

        public GlassTokens tokens = new GlassTokens();
        public TMP_FontAsset fontTitle, fontBody;
        public Material material;

        static GlassTheme _cur;
        static bool _curTile;
        Material _runtimeMaterial;
        bool _applied;

        /// <summary>The theme asset of the active look (Tile or Glass; Classic reads the Glass one, as before).</summary>
        public static GlassTheme Current
        {
            get
            {
                bool tile = UiTheme.IsTile;
                if (_cur == null || _curTile != tile)
                {
                    _curTile = tile;
                    _cur = Resources.Load<GlassTheme>(tile ? TileResourcesName : ResourcesName);
                    if (_cur == null)
                    {
                        _cur = CreateInstance<GlassTheme>(); _cur.hideFlags = HideFlags.DontSave;
                        if (tile) _cur.tokens = GlassTokens.Tile();
                    }
                    if (tile) ResolveTileFonts(_cur);
                }
                return _cur;
            }
        }

        public static GlassTokens T { get { return Current.tokens; } }
        /// <summary>True when the active theme draws the flat Tile look.</summary>
        public static bool IsTile { get { return UiTheme.IsTile; } }

        /// <summary>Forgets the cached theme (tests, and after the asset was rebuilt).</summary>
        public static void ResetCache() { _cur = null; Shader.SetGlobalFloat("_PixGlassBackdropOn", 0f); }

        /// <summary>Barlow font assets are looked up by name when the asset does not hold them (Resources/PixGlass/Fonts or the licensed copy): a clone without them keeps the DejaVu fallback.</summary>
        static void ResolveTileFonts(GlassTheme th)
        {
            if (th.fontTitle != null && th.fontBody != null) return;
            if (th.fontTitle == null) th.fontTitle = FindFont("PIX_Barlow_Bold SDF");
            if (th.fontBody == null) th.fontBody = FindFont("PIX_Barlow_Medium SDF");
        }

        static TMP_FontAsset FindFont(string n)
        {
            var f = Resources.Load<TMP_FontAsset>("PixGlass/" + n);
            if (f != null) return f;
#if UNITY_EDITOR
            foreach (string dir in new[] { "Assets/_Project/UI/Glass/Fonts/", "Assets/_Licensed/PIX/Fonts/" })
            {
                f = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(dir + n + ".asset");
                if (f != null) return f;
            }
#endif
            return null;
        }

        /// <summary>Heading / button / number font (Barlow Bold in Tile, DejaVu Sans Bold in Glass); falls back to the classic title font when the glass fonts were not built.</summary>
        public TMP_FontAsset Title { get { return fontTitle != null ? fontTitle : UiKit.ClassicTitle; } }
        /// <summary>Body font (Barlow Medium in Tile, DejaVu Sans in Glass).</summary>
        public TMP_FontAsset Body { get { return fontBody != null ? fontBody : UiKit.ClassicBody; } }

        /// <summary>The one shared material every surface draws with (batches into one draw call per canvas run).</summary>
        public Material GlassMaterial
        {
            get
            {
                if (material != null) { if (!_applied) { Apply(material); _applied = true; } return material; }
                if (_runtimeMaterial == null)
                {
                    var sh = Shader.Find(ShaderName);
                    if (sh == null) return null;
                    _runtimeMaterial = new Material(sh) { name = tokens.flat ? "PixTileUI (runtime)" : "PixGlassUI (runtime)", hideFlags = HideFlags.DontSave };
                    _applied = false;
                }
                if (!_applied) { Apply(_runtimeMaterial); _applied = true; }
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
            // flat (Tile) branch
            m.SetFloat("_Flat", t.flat ? 1f : 0f);
            m.SetVector("_FlatFill", new Vector4(t.tileFill.r, t.tileFill.g, t.tileFill.b, 1f));
            m.SetVector("_PanelFill", new Vector4(t.panelFill.r, t.panelFill.g, t.panelFill.b, 1f));
            m.SetVector("_PanelHead", new Vector4(t.panelHead.r, t.panelHead.g, t.panelHead.b, 1f));
            m.SetVector("_RowFill", new Vector4(t.rowFill.r, t.rowFill.g, t.rowFill.b, 1f));
            m.SetVector("_RowFocus", new Vector4(t.rowFocus.r, t.rowFocus.g, t.rowFocus.b, 1f));
            m.SetVector("_TabOff", new Vector4(t.tabOff.r, t.tabOff.g, t.tabOff.b, 1f));
            m.SetVector("_GoldHi", new Vector4(t.goldHi.r, t.goldHi.g, t.goldHi.b, 1f));
            m.SetVector("_KeyA", new Vector4(t.glyphKeyTop.r, t.glyphKeyTop.g, t.glyphKeyTop.b, 1f)); m.SetVector("_KeyB", new Vector4(t.glyphKeyBottom.r, t.glyphKeyBottom.g, t.glyphKeyBottom.b, 1f));
            // x focus edge width, y outer ring width, z ring alpha, w glow alpha
            m.SetVector("_FocusA", new Vector4(t.focusEdge, t.ringWidth, t.ringAlpha, t.glowAlpha));
            // x glow radius, y sheen alpha, z shadow alpha, w unused
            m.SetVector("_FocusB", new Vector4(t.glowRadius, t.sheenAlpha, t.shadowAlpha, 0f));
        }

#if UNITY_EDITOR
        void OnValidate() { _applied = false; }
#endif
    }
}
