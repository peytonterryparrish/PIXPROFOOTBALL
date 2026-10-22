using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.4 graphics tiers (Low, Medium, High, Ultra) and the render scale option. A tier maps onto a Unity quality level (lod bias, shadow distance, skin weights, vsync) and applies the HDRP
    /// settings that cost the most in a match at runtime: the helmet / body level of detail, volumetric fog, ambient occlusion, reflections, contact shadows, motion blur and the
    /// shadow-casting stadium spot lights. Ultra is the v0.3 look. Render scale uses HDRP dynamic resolution (hardware) with the asset's upscale filter.
    /// </summary>
    public static class QualityTiers
    {
        public static readonly string[] Names = { "Low", "Medium", "High", "Ultra" };
        static readonly int[] QualityLevels = { 1, 2, 3, 5 };
        public static readonly float[] RenderScales = { 1f, 0.9f, 0.8f, 0.67f };
        public static readonly string[] RenderScaleNames = { "100% (native)", "90%", "80%", "67% (performance)" };

        /// <summary>Per tier: player level of detail bias, volumetric fog, SSAO, SSR, contact shadows, motion blur, spot light shadows, soft shadows.</summary>
        struct Tier { public float LodBias; public bool Fog, Ssao, Ssr, ContactShadows, MotionBlur, SpotShadows; public float ShadowDistance; public float FxScale; public bool Splashes, Breath, GrassBlades, Ssgi; }
        static readonly Tier[] Tiers =
        {
            new Tier { LodBias = 0.8f, Fog = false, Ssao = false, Ssr = false, ContactShadows = false, MotionBlur = false, SpotShadows = false, ShadowDistance = 60f, FxScale = 0.4f, Splashes = false, Breath = false, GrassBlades = false, Ssgi = false },
            new Tier { LodBias = 0.9f, Fog = true, Ssao = true, Ssr = false, ContactShadows = false, MotionBlur = false, SpotShadows = false, ShadowDistance = 90f, FxScale = 0.7f, Splashes = false, Breath = true, GrassBlades = false, Ssgi = false },
            new Tier { LodBias = 1.1f, Fog = true, Ssao = true, Ssr = true, ContactShadows = true, MotionBlur = true, SpotShadows = true, ShadowDistance = 120f, FxScale = 1f, Splashes = true, Breath = true, GrassBlades = true, Ssgi = false },
            new Tier { LodBias = 1.4f, Fog = true, Ssao = true, Ssr = true, ContactShadows = true, MotionBlur = true, SpotShadows = true, ShadowDistance = 150f, FxScale = 1.25f, Splashes = true, Breath = true, GrassBlades = true, Ssgi = true },
        };

        // ---- v0.6 look features by tier
        static Tier Cur { get { return Tiers[Mathf.Clamp(Current, 0, Tiers.Length - 1)]; } }
        /// <summary>Amount of weather particles (rain / snow) relative to High.</summary>
        public static float FxScale { get { return Cur.FxScale; } }
        public static bool WeatherSplashes { get { return Cur.Splashes; } }
        public static bool BreathVapor { get { return Cur.Breath; } }
        /// <summary>Short grass blades near the camera (High and Ultra).</summary>
        public static bool GrassBlades { get { return Cur.GrassBlades; } }
        public static bool Ssgi { get { return Cur.Ssgi; } }
        /// <summary>v0.9 (GFX-430): the tier's shadow distance (metres): the weather volume uses it for the HDRP maximum shadow distance (capped at 110, the range the cascades are tuned for).</summary>
        public static float ShadowDistance { get { return Cur.ShadowDistance; } }
        /// <summary>v0.9 (GFX-409/410): number of floodlights that cast player shadows (0 on Low / Medium, the two far rim lamps on High, four on Ultra).</summary>
        public static int FloodShadowCount { get { return Cur.SpotShadows ? (Current >= 3 ? 4 : 2) : 0; } }
        public static bool SpotShadows { get { return Cur.SpotShadows; } }

        public static int TierOfQualityLevel(int level)
        {
            if (level < 0) return Names.Length - 1;
            int best = 0;
            for (int i = 0; i < QualityLevels.Length; i++) if (QualityLevels[i] <= level) best = i;
            return best;
        }

        static int _tierOverride = -1; static float _scaleOverride = -1f;
        public static int Current { get { return _tierOverride >= 0 ? _tierOverride : TierOfQualityLevel(PixSettings.Quality >= 0 ? PixSettings.Quality : QualitySettings.GetQualityLevel()); } }
        static float Scale { get { return _scaleOverride > 0f ? _scaleOverride : PixSettings.RenderScale; } }
        /// <summary>The render scale in use (stored setting or the test override), 0.5 .. 1.</summary>
        public static float EffectiveRenderScale { get { return Mathf.Clamp(Scale, 0.5f, 1f); } }

        /// <summary>Test / command line override that is not stored in the player settings (autotest: -pixTier, -pixRenderScale).</summary>
        public static void Override(int tier, float scale)
        {
            if (tier >= 0) { _tierOverride = Mathf.Clamp(tier, 0, Names.Length - 1); QualitySettings.SetQualityLevel(QualityLevels[_tierOverride], true); }
            if (scale > 0f) _scaleOverride = Mathf.Clamp(scale, 0.5f, 1f);
            ApplyScene();
        }

        /// <summary>Drops the command line / test override so the stored tier applies again.</summary>
        public static void ClearOverride() { _tierOverride = -1; _scaleOverride = -1f; }

        /// <summary>Stores the tier, switches the Unity quality level and applies the runtime settings to the loaded scene.</summary>
        public static void Select(int tier)
        {
            tier = Mathf.Clamp(tier, 0, Names.Length - 1);
            PixSettings.Quality = QualityLevels[tier];
            PixSettings.Save();
            QualitySettings.SetQualityLevel(QualityLevels[tier], true);
            PixSettings.ApplyFrameSettings();
            ApplyScene();
        }

        static bool _scalerHooked, _sceneHooked;

        // what the tier switched off, so a higher tier can switch it back on (QA-812: a tier change used to be one-way)
        static readonly System.Collections.Generic.HashSet<VolumeComponent> _offByTier = new System.Collections.Generic.HashSet<VolumeComponent>();
        static readonly System.Collections.Generic.HashSet<Fog> _fogOffByTier = new System.Collections.Generic.HashSet<Fog>();
        static readonly System.Collections.Generic.Dictionary<Light, LightShadows> _lightShadowsBefore = new System.Collections.Generic.Dictionary<Light, LightShadows>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!_sceneHooked) { _sceneHooked = true; UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { _lightShadowsBefore.Clear(); ApplyScene(); }; }   // QA-740: subscribe once
            ApplyScene();
        }

        /// <summary>Applies the stored tier and render scale to whatever is loaded now (call after a scene loads and when the settings change).</summary>
        public static void ApplyScene()
        {
            int t = Current;
            var tier = Tiers[Mathf.Clamp(t, 0, Tiers.Length - 1)];
            PlayerLod.Bias = tier.LodBias;
            // v0.9: anisotropic filtering forced on from Medium (the turf stripes, the painted lines and the jersey weave shimmered / blurred at grazing angles; the import settings leave most textures at 1x)
            QualitySettings.anisotropicFiltering = t >= 1 ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            if (t >= 1) Texture.SetGlobalAnisotropicFilteringLimits(t >= 2 ? 16 : 8, t >= 2 ? 16 : 8);
            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, tier.ShadowDistance);
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                var p = v.profile; if (p == null) continue;
                Fog fog;
                if (p.TryGet(out fog))
                {
                    if (!tier.Fog) { if (fog.enableVolumetricFog.value) { fog.enableVolumetricFog.value = false; _fogOffByTier.Add(fog); } }
                    else if (_fogOffByTier.Remove(fog)) fog.enableVolumetricFog.value = true;
                }
                Set<ScreenSpaceAmbientOcclusion>(p, tier.Ssao); Set<ScreenSpaceReflection>(p, tier.Ssr); Set<ContactShadows>(p, tier.ContactShadows); Set<MotionBlur>(p, tier.MotionBlur);
                Set<GlobalIllumination>(p, tier.Ssgi);
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Spot) continue;
                if (l.name.StartsWith("Flood_")) continue;   // v0.9 (GFX-409): the floodlight rig is owned by WeatherSystem.RefreshTier (which lamps cast, per tier); this loop used to switch them off again right after
                if (!(tier.SpotShadows && Current >= 3)) { if (l.shadows != LightShadows.None) { _lightShadowsBefore[l] = l.shadows; l.shadows = LightShadows.None; } }
                else { LightShadows was; if (_lightShadowsBefore.TryGetValue(l, out was)) { l.shadows = was; _lightShadowsBefore.Remove(l); } }
            }
            var ws = WeatherSystem.Instance; if (ws != null) ws.RefreshTier();
            ApplyRenderScale();
        }

        static void Set<T>(VolumeProfile p, bool on) where T : VolumeComponent
        {
            T c; if (!p.TryGet(out c)) return;
            if (!on) { if (c.active) { c.active = false; _offByTier.Add(c); } }
            else if (_offByTier.Remove(c)) c.active = true;
        }

        /// <summary>Render scale through HDRP dynamic resolution (hardware): 100% leaves dynamic resolution off.</summary>
        public static void ApplyRenderScale()
        {
            if (!_scalerHooked)
            {
                _scalerHooked = true;
                DynamicResolutionHandler.SetDynamicResScaler(() => Mathf.Clamp(Scale, 0.5f, 1f) * 100f, DynamicResScalePolicyType.ReturnsPercentage);
            }
            // v0.9.1: the camera flags are set in one place (UpscalerControl): this loop switched dynamic resolution off at 100 percent after the upscaler had switched it on, so DLSS / FSR 2 never ran
            UpscalerControl.Apply();
        }
    }
}
