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
        struct Tier { public float LodBias; public bool Fog, Ssao, Ssr, ContactShadows, MotionBlur, SpotShadows; public float ShadowDistance; }
        static readonly Tier[] Tiers =
        {
            new Tier { LodBias = 0.45f, Fog = false, Ssao = false, Ssr = false, ContactShadows = false, MotionBlur = false, SpotShadows = false, ShadowDistance = 60f },
            new Tier { LodBias = 0.7f, Fog = true, Ssao = true, Ssr = false, ContactShadows = false, MotionBlur = false, SpotShadows = false, ShadowDistance = 90f },
            new Tier { LodBias = 1f, Fog = true, Ssao = true, Ssr = true, ContactShadows = true, MotionBlur = true, SpotShadows = true, ShadowDistance = 120f },
            new Tier { LodBias = 1.4f, Fog = true, Ssao = true, Ssr = true, ContactShadows = true, MotionBlur = true, SpotShadows = true, ShadowDistance = 150f },
        };

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

        /// <summary>Test / command line override that is not stored in the player settings (autotest: -pixTier, -pixRenderScale).</summary>
        public static void Override(int tier, float scale)
        {
            if (tier >= 0) { _tierOverride = Mathf.Clamp(tier, 0, Names.Length - 1); QualitySettings.SetQualityLevel(QualityLevels[_tierOverride], true); }
            if (scale > 0f) _scaleOverride = Mathf.Clamp(scale, 0.5f, 1f);
            ApplyScene();
        }

        /// <summary>Stores the tier, switches the Unity quality level and applies the runtime settings to the loaded scene.</summary>
        public static void Select(int tier)
        {
            tier = Mathf.Clamp(tier, 0, Names.Length - 1);
            PixSettings.Quality = QualityLevels[tier];
            PixSettings.Save();
            QualitySettings.SetQualityLevel(QualityLevels[tier], true);
            ApplyScene();
        }

        static bool _scalerHooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => ApplyScene();
            ApplyScene();
        }

        /// <summary>Applies the stored tier and render scale to whatever is loaded now (call after a scene loads and when the settings change).</summary>
        public static void ApplyScene()
        {
            int t = Current;
            var tier = Tiers[Mathf.Clamp(t, 0, Tiers.Length - 1)];
            PlayerLod.Bias = tier.LodBias;
            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, tier.ShadowDistance);
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                var p = v.profile; if (p == null) continue;
                Fog fog; if (p.TryGet(out fog) && !tier.Fog) fog.enableVolumetricFog.value = false;
                Set<ScreenSpaceAmbientOcclusion>(p, tier.Ssao); Set<ScreenSpaceReflection>(p, tier.Ssr); Set<ContactShadows>(p, tier.ContactShadows); Set<MotionBlur>(p, tier.MotionBlur);
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Spot && l.shadows != LightShadows.None && !tier.SpotShadows) l.shadows = LightShadows.None;
            ApplyRenderScale();
        }

        static void Set<T>(VolumeProfile p, bool on) where T : VolumeComponent
        {
            T c; if (p.TryGet(out c) && !on) c.active = false;
        }

        /// <summary>Render scale through HDRP dynamic resolution (hardware): 100% leaves dynamic resolution off.</summary>
        public static void ApplyRenderScale()
        {
            float scale = Mathf.Clamp(Scale, 0.5f, 1f);
            if (!_scalerHooked)
            {
                _scalerHooked = true;
                DynamicResolutionHandler.SetDynamicResScaler(() => Mathf.Clamp(Scale, 0.5f, 1f) * 100f, DynamicResScalePolicyType.ReturnsPercentage);
            }
            foreach (var cam in Object.FindObjectsByType<HDAdditionalCameraData>(FindObjectsSortMode.None))
                cam.allowDynamicResolution = scale < 0.999f;
        }
    }
}
