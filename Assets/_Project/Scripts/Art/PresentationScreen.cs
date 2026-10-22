using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.Art
{
    /// <summary>
    /// v0.7 presentation sets (jumbotrons, ribbon boards, the news-studio video wall and desk screen): puts a texture on the emissive screens of a prefab through a property block
    /// (no material copies). Feed it the idle card, a score card, a replay render texture or the output of <see cref="ScreenFeed"/>.
    /// </summary>
    public sealed class PresentationScreen : MonoBehaviour
    {
        static readonly int EmissiveMap = Shader.PropertyToID("_EmissiveColorMap"), EmissiveColor = Shader.PropertyToID("_EmissiveColor");

        [Tooltip("The renderers that carry the screen material (the PIX_*Screen slots).")]
        public Renderer[] screens = new Renderer[0];
        [Tooltip("Shown while no source is set.")]
        public Texture idle;
        [Tooltip("Emission of the picture in nits (stadium screens: thousands in daylight, a studio: a few hundred).")]
        public float nits = 2500f;
        [Tooltip("Index of the material slot of each renderer that is the screen (the others keep their material).")]
        public int slot = -1;

        [Tooltip("Scale the emission with the scene exposure (set by the weather system) so a stadium screen reads the same in daylight and at night.")]
        public bool followExposure = true;
        [Tooltip("Picture brightness relative to a paper-white surface when followExposure is on.")]
        public float exposureRelative = 0.6f;

        /// <summary>Fixed exposure (EV100) of the scene, written by the weather system at every preset change.</summary>
        public static float SceneExposureEv = 13.7f;
        static readonly System.Collections.Generic.List<PresentationScreen> All = new System.Collections.Generic.List<PresentationScreen>();
        /// <summary>Re-applies every screen (after an exposure change).</summary>
        public static void RefreshAll() { foreach (var s in All) if (s != null) s.Apply(); }

        Texture _source;
        MaterialPropertyBlock _mpb;

        public Texture Source { get { return _source; } }

        void OnEnable() { All.Add(this); Apply(); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Shows <paramref name="t"/> on every screen (null = the idle card).</summary>
        public void SetSource(Texture t) { _source = t; Apply(); }

        public void SetBrightness(float newNits) { nits = newNits; Apply(); }

        public void Apply()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            Texture tex = _source != null ? _source : idle;
            foreach (var r in screens)
            {
                if (r == null) continue;
                int s = ScreenSlot(r);
                if (s < 0) continue;
                r.GetPropertyBlock(_mpb, s);
                if (tex != null) _mpb.SetTexture(EmissiveMap, tex);
                float n = followExposure ? 1.2f * Mathf.Pow(2f, SceneExposureEv) * exposureRelative : nits;
                _mpb.SetColor(EmissiveColor, Color.white * n);
                r.SetPropertyBlock(_mpb, s);
            }
        }

        int ScreenSlot(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (slot >= 0) return slot < mats.Length ? slot : -1;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.Contains("Screen")) return i;
            return mats.Length == 1 ? 0 : -1;
        }
    }

    /// <summary>
    /// Renders a camera into a small render texture a few times per second (a live feed for a jumbotron / studio wall). Off by default: it costs a second view of the scene,
    /// so turn it on per quality tier. The camera is rendered by hand (never enabled), at <see cref="hz"/> frames per second.
    /// </summary>
    public sealed class ScreenFeed : MonoBehaviour
    {
        public Camera feedCamera;
        public PresentationScreen target;
        public int width = 640, height = 360;
        public float hz = 12f;
        RenderTexture _rt;
        float _next;

        void OnEnable()
        {
            if (feedCamera == null) return;
            if (_rt == null) { _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "PIX_ScreenFeed" }; _rt.Create(); }
            feedCamera.enabled = false; feedCamera.targetTexture = _rt;
            if (target != null) target.SetSource(_rt);
        }

        void OnDisable() { if (target != null) target.SetSource(null); if (feedCamera != null) feedCamera.targetTexture = null; }

        void OnDestroy() { if (_rt != null) { _rt.Release(); Destroy(_rt); } }

        void Update()
        {
            if (feedCamera == null || _rt == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / Mathf.Max(1f, hz);
            feedCamera.Render();
        }
    }

    /// <summary>Team dressing of the walk-out tunnel: the inflatable canvas takes the team colour, the logo plates the team crest.</summary>
    public sealed class TunnelDressing : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), BaseMap = Shader.PropertyToID("_BaseColorMap");
        public Renderer canvas;
        public Renderer[] logoPlates = new Renderer[0];
        MaterialPropertyBlock _mpb;

        public void Dress(Color canvasColor, Texture logo)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (canvas != null) { canvas.GetPropertyBlock(_mpb); _mpb.SetColor(BaseColor, canvasColor); canvas.SetPropertyBlock(_mpb); }
            foreach (var r in logoPlates)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                if (logo != null) _mpb.SetTexture(BaseMap, logo);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
