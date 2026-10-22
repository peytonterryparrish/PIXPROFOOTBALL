using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.6 weather and time of day. Turns a <see cref="WeatherPreset"/> (Day / Dusk / Night x Clear / Overcast / Rain / Snow / Fog) into the sun / moon, the floodlight rig,
    /// a high priority HDRP volume (exposure, sky, fog, colour grade), the shader globals that drive wet and snowy surfaces, and the rain / snow / splash / breath particles.
    /// Snow accumulates on the field and the stands over the game (<see cref="WeatherClimate"/>); the wear texture keeps the footprints.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }

        public LightingController Lighting;
        public WeatherPreset Preset { get; private set; }
        public WeatherClimate Climate { get; private set; }
        public WeatherFx Fx { get; private set; }
        public bool Applied { get; private set; }
        /// <summary>Seconds of game time that pass per real second for snow build-up / drying (an accelerated clock).</summary>
        public float ClimateTimeScale = 6f;
        /// <summary>Night games: the saved lamps are 9.4 M cd (about 10,000 lux on the pitch, QA-971); at night they run at this fraction (about 2,000 lux, a real stadium) with the exposure lowered to match, so the light falls off into pools instead of lighting everything evenly.</summary>
        public const float NightFloodScale = 0.2f;
        /// <summary>Natural grass or artificial turf under this game (the turf shader reads it).</summary>
        public bool ArtificialTurf { get; private set; }

        Volume _volume; VolumeProfile _profile;
        readonly List<Light> _flood = new List<Light>(); readonly List<float> _floodBase = new List<float>();
        Renderer[] _turf = new Renderer[0];
        MaterialPropertyBlock _mpb;
        float _lastSnow;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; if (_profile != null) Destroy(_profile); if (_dramaProfile != null) Destroy(_dramaProfile); }

        public static WeatherSystem Ensure(LightingController lc)
        {
            var ws = Instance;
            if (ws == null)
            {
                var go = new GameObject("WeatherSystem");
                ws = go.AddComponent<WeatherSystem>();
                Instance = ws;   // edit-mode tools never run Awake
            }
            if (lc != null) ws.Lighting = lc;
            return ws;
        }

        /// <summary>Resolves the player's choice with the game seed (random weather / time), applies it and returns the preset.</summary>
        public WeatherPreset Choose(WeatherChoice w, TimeChoice t, int seed, bool artificialTurf)
        {
            var rng = new Rng(seed * 7919 + 13);
            var kind = WeatherPresets.Resolve(w, rng);
            var time = WeatherPresets.Resolve(t, rng);
            Apply(kind, time, artificialTurf);
            return Preset;
        }

        public void Apply(WeatherKind kind, TimeOfDay time, bool artificialTurf = false)
        {
            Preset = WeatherPresets.Get(kind, time);
            ArtificialTurf = artificialTurf;
            Climate = new WeatherClimate(Preset);
            _lastSnow = Climate.Snow;
            CollectLights();
            ApplyLights();
            BuildVolume();
            BuildDramaVolume();
            ApplyTurf();
            if (Fx == null && !Flag("-pixNoFx")) Fx = new WeatherFx(transform);
            if (Fx != null) Fx.Configure(Preset);
            Applied = true;
            PushGlobals();
            foreach (var probe in FindObjectsByType<HDAdditionalReflectionData>(FindObjectsSortMode.None)) probe.RequestRenderNextUpdate();
            QualityTiers.ApplyScene();
        }

        /// <summary>Old day / night toggle (pause menu, free roam): keeps the weather, switches the time of day.</summary>
        public void OnLightingPreset(LightingPreset p)
        {
            var kind = Applied ? Preset.Kind : WeatherKind.Clear;
            Apply(kind, p == LightingPreset.Night ? TimeOfDay.Night : TimeOfDay.Day, ArtificialTurf);
        }

        void CollectLights()
        {
            _flood.Clear(); _floodBase.Clear();
            if (Lighting != null && Lighting.FloodRoot != null)
            {
                foreach (var l in Lighting.FloodRoot.GetComponentsInChildren<Light>(true))
                {
                    _flood.Add(l);
                    var tag = l.GetComponent<FloodBase>();
                    if (tag == null) { tag = l.gameObject.AddComponent<FloodBase>(); tag.Intensity = l.intensity; }
                    _floodBase.Add(tag.Intensity);
                }
            }
            if (_turf.Length == 0)
            {
                var list = new List<Renderer>();
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r.name == "Turf" || (r.transform.parent != null && r.transform.parent.name == "Turf")) list.Add(r);
                }
                _turf = list.ToArray();
            }
        }

        sealed class FloodBase : MonoBehaviour { public float Intensity; }

        static Color C(Rgb c) { return new Color(c.R, c.G, c.B); }
        static readonly string[] _args = System.Environment.GetCommandLineArgs();
        static readonly bool NoWetShader = Has("-pixNoWetShader"), NoPuddles = Has("-pixNoPuddles");
        static readonly int LegacyId = Shader.PropertyToID("_PixLegacy");
        static bool Has(string name) { foreach (string a in _args) if (a == name) return true; return false; }
        public static bool Flag(string name) { return Has(name); }

        // ------------------------------------------------------------------------------------------------ lights
        void ApplyLights()
        {
            var p = Preset;
            bool night = p.Time == TimeOfDay.Night;
            if (Lighting != null)
            {
                if (Lighting.NightRoot != null) Lighting.NightRoot.SetActive(night);
                if (Lighting.DayRoot != null) Lighting.DayRoot.SetActive(!night);
                if (Lighting.FloodRoot != null) Lighting.FloodRoot.SetActive(p.FloodlightsOn && !Flag("-pixNoFlood"));
                var sun = night ? Lighting.NightSun : Lighting.DaySun;
                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.Euler(p.SunElevation, p.SunAzimuth, 0f);
                    sun.intensity = p.SunLux;
                    sun.color = C(p.SunColor);
                    var hd = sun.GetComponent<HDAdditionalLightData>();
                    if (hd != null)
                    {
                        hd.angularDiameter = Mathf.Lerp(1.4f, 6f, p.CloudCover);   // soft, short-edged player shadows even in clear weather (QA-300)                  // overcast skies give soft shadows
                        hd.shadowDimmer = Mathf.Lerp(1f, 0.45f, p.CloudCover);
                    }
                    RenderSettings.sun = sun;
                }
                Lighting.NoteTime(night ? LightingPreset.Night : LightingPreset.Day);
            }
            var cookie = PixLook.Assets != null ? PixLook.Assets.floodCookie : null;
            int tier = QualityTiers.Current;
            for (int i = 0; i < _flood.Count; i++)
            {
                var l = _flood[i]; if (l == null) continue;
                string n = l.name;
                // the far (east) lamps are the rim light on the players seen from the broadcast side, the near (west) lamps the key: bias the rig for a more dramatic split
                float bias = n.StartsWith("Flood_E") ? 1f + p.FloodRimBias : (n.StartsWith("Flood_W") ? 1f - p.FloodRimBias * 0.5f : 1f);
                l.intensity = _floodBase[i] * p.FloodlightScale * bias * (p.Time == TimeOfDay.Night ? NightFloodScale : 1f);
                if (cookie != null && l.type == LightType.Spot) l.cookie = cookie;
                var lhd = l.GetComponent<HDAdditionalLightData>();
                if (lhd != null) lhd.volumetricDimmer = p.FloodVolumetric;
                // player shadows from the rim lamps (long shadows toward the camera): one at High, two at Ultra
                bool shadow = (tier >= 2 && n == "Flood_E6") || (tier >= 3 && n == "Flood_E7");
                l.shadows = shadow ? LightShadows.Soft : LightShadows.None;
            }
        }

        // ------------------------------------------------------------------------------------------------ volume
        void BuildVolume()
        {
            var p = Preset;
            if (_volume == null)
            {
                var go = new GameObject("WeatherVolume");
                go.transform.SetParent(transform, false);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true; _volume.priority = 50f; _volume.weight = 1f;
            }
            if (_profile != null) Destroy(_profile);
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            // GFX-082: Volume.profile is an instantiated COPY of sharedProfile that QualityTiers.ApplyScene (which reads v.profile) creates on the first Apply and that the volume manager
            // then keeps using; assigning a new sharedProfile at a runtime preset switch never reached the renderer (the Night exposure stayed, Day Snow blew out white). Set both.
            _volume.sharedProfile = _profile; _volume.profile = _profile;

            var exp = _profile.Add<Exposure>(false); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(p.ExposureEV); _exposure = exp;
            PresentationScreen.SceneExposureEv = p.ExposureEV - p.PostExposure; PresentationScreen.RefreshAll();   // v0.7: the jumbotron / studio screens follow the exposure
            var env = _profile.Add<VisualEnvironment>(false);
            var ind = _profile.Add<IndirectLightingController>(false); ind.indirectDiffuseLightingMultiplier.Override(Mathf.Max(0.5f, p.AmbientMultiplier)); ind.reflectionLightingMultiplier.Override(Mathf.Lerp(1f, 0.8f, p.CloudCover));
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            if (p.PhysicallyBasedSky)
            {
                env.skyType.Override((int)SkyType.PhysicallyBased);
                var pbs = _profile.Add<PhysicallyBasedSky>(false);
                pbs.aerosolDensity.Override(Mathf.Lerp(0.012f, 0.09f, Mathf.Clamp01(p.CloudCover)));   // a thick atmosphere would also extinguish the sun light
                pbs.aerosolTint.Override(Color.Lerp(new Color(0.9f, 0.9f, 0.9f), new Color(0.72f, 0.75f, 0.8f), p.CloudCover));
                pbs.colorSaturation.Override(Mathf.Lerp(1f, 0.45f, p.CloudCover));
                pbs.groundTint.Override(Color.Lerp(new Color(0.12f, 0.10f, 0.09f), new Color(0.25f, 0.25f, 0.27f), p.CloudCover));
                pbs.exposure.Override(0f);
                pbs.multiplier.Override(p.SkyMultiplier);
                pbs.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
            }
            else
            {
                env.skyType.Override((int)SkyType.Gradient);
                var g = _profile.Add<GradientSky>(false);
                g.top.Override(C(p.SkyTop)); g.middle.Override(C(p.SkyMiddle)); g.bottom.Override(C(p.SkyBottom));
                g.gradientDiffusion.Override(2f); g.multiplier.Override(p.SkyMultiplier);
            }
            var fog = _profile.Add<Fog>(false);
            fog.enabled.Override(!Flag("-pixNoFog")); fog.enableVolumetricFog.Override(p.VolumetricFog && !Flag("-pixNoVolFog"));
            fog.meanFreePath.Override(p.FogMeanFreePath); fog.albedo.Override(C(p.FogAlbedo)); fog.anisotropy.Override(p.FogAnisotropy);
            fog.baseHeight.Override(0f); fog.maximumHeight.Override(p.FogMaxHeight);

            // ---- the broadcast grade: warm highlights, crisp contrast, saturated greens
            var ca = _profile.Add<ColorAdjustments>(false);
            ca.postExposure.Override(p.PostExposure); ca.contrast.Override(p.Contrast); ca.saturation.Override(p.Saturation); ca.colorFilter.Override(C(p.ColorFilter));
            var wb = _profile.Add<WhiteBalance>(false); wb.temperature.Override(p.Temperature); wb.tint.Override(p.Tint);
            var lgg = _profile.Add<LiftGammaGain>(false);
            lgg.lift.Override(new Vector4(1f + p.Lift.R, 1f + p.Lift.G, 1f + p.Lift.B, 0f));
            lgg.gamma.Override(new Vector4(1f + p.Gamma.R, 1f + p.Gamma.G, 1f + p.Gamma.B, 0f));
            lgg.gain.Override(new Vector4(1f + p.Gain.R, 1f + p.Gain.G, 1f + p.Gain.B, 0f));
            var curves = _profile.Add<ColorCurves>(false);
            float gb = Mathf.Clamp01(p.GreenBoost);
            var keys = new[] { new Keyframe(0.00f, 0.5f), new Keyframe(0.17f, 0.5f), new Keyframe(0.24f, 0.5f + 0.16f * gb), new Keyframe(0.33f, 0.5f + 0.24f * gb), new Keyframe(0.42f, 0.5f + 0.12f * gb), new Keyframe(0.52f, 0.5f), new Keyframe(1.00f, 0.5f) };
            curves.hueVsSat.Override(new TextureCurve(keys, 0.5f, true, new Vector2(0f, 1f)));
            var mbl = _profile.Add<MotionBlur>(false); mbl.intensity.Override(0f);   // live play stays sharp (QA-176 / QA-255); the drama volume adds a little
            var shs = _profile.Add<HDShadowSettings>(false); shs.maxShadowDistance.Override(90f); shs.cascadeShadowSplitCount.Override(QualityTiers.Current >= 2 ? 4 : 3);
            shs.cascadeShadowSplit0.Override(0.04f); shs.cascadeShadowSplit1.Override(0.12f); shs.cascadeShadowSplit2.Override(0.3f); shs.cascadeShadowBorder0.Override(0.15f); shs.cascadeShadowBorder1.Override(0.15f); shs.cascadeShadowBorder2.Override(0.15f);
            var bloom = _profile.Add<Bloom>(false); bloom.intensity.Override(p.Bloom); bloom.threshold.Override(0.9f); bloom.scatter.Override(0.6f);
            var vg = _profile.Add<Vignette>(false); vg.intensity.Override(p.Vignette); vg.smoothness.Override(0.55f);
            var fgr = _profile.Add<FilmGrain>(false); fgr.type.Override(FilmGrainLookup.Thin1); fgr.intensity.Override(p.FilmGrain); fgr.response.Override(0.8f);
            var smh = _profile.Add<ShadowsMidtonesHighlights>(false);
            smh.shadows.Override(new Vector4(p.ShadowTint.R, p.ShadowTint.G, p.ShadowTint.B, 0f)); smh.midtones.Override(new Vector4(p.MidtoneTint.R, p.MidtoneTint.G, p.MidtoneTint.B, 0f)); smh.highlights.Override(new Vector4(p.HighlightTint.R, p.HighlightTint.G, p.HighlightTint.B, 0f));
            var cab = _profile.Add<ChromaticAberration>(false); cab.intensity.Override(p.Chromatic);
            if (QualityTiers.Ssgi) { var gi = _profile.Add<GlobalIllumination>(false); gi.enable.Override(true); }
        }

        // ------------------------------------------------------------------------------------------------ drama volume
        Volume _drama; VolumeProfile _dramaProfile;
        /// <summary>0 = the clean live-play grade, 1 = the full cinematic grade (replays, cutaways, shows; a little at night). Set to -1 to follow the game.</summary>
        public float DramaOverride = -1f;
        public float DramaWeight { get; private set; }

        /// <summary>The extra grade blended in for cinematics: deeper contrast and vignette, stronger bloom on the lights, cooler shadows / warmer highlights, a little lens fringing, less fill.</summary>
        void BuildDramaVolume()
        {
            var p = Preset;
            if (_drama == null)
            {
                var go = new GameObject("DramaVolume");
                go.transform.SetParent(transform, false);
                _drama = go.AddComponent<Volume>();
                _drama.isGlobal = true; _drama.priority = 60f; _drama.weight = 0f;
            }
            if (_dramaProfile != null) Destroy(_dramaProfile);
            _dramaProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _drama.sharedProfile = _dramaProfile; _drama.profile = _dramaProfile;   // see BuildVolume (GFX-082)
            var ca = _dramaProfile.Add<ColorAdjustments>(false);
            ca.postExposure.Override(p.PostExposure - 0.3f); ca.contrast.Override(p.Contrast + 14f); ca.saturation.Override(p.Saturation + 5f); ca.colorFilter.Override(C(p.ColorFilter));
            var bloom = _dramaProfile.Add<Bloom>(false); bloom.intensity.Override(Mathf.Min(1f, p.Bloom + 0.14f)); bloom.threshold.Override(0.8f); bloom.scatter.Override(0.7f);
            var vg = _dramaProfile.Add<Vignette>(false); vg.intensity.Override(Mathf.Min(0.6f, p.Vignette + 0.14f)); vg.smoothness.Override(0.5f);
            var smh = _dramaProfile.Add<ShadowsMidtonesHighlights>(false);
            smh.shadows.Override(new Vector4(p.ShadowTint.R * 0.93f, p.ShadowTint.G * 0.96f, p.ShadowTint.B * 1.08f, 0f));
            smh.midtones.Override(new Vector4(p.MidtoneTint.R, p.MidtoneTint.G, p.MidtoneTint.B, 0f));
            smh.highlights.Override(new Vector4(p.HighlightTint.R * 1.04f, p.HighlightTint.G, p.HighlightTint.B * 0.93f, 0f));
            var cab = _dramaProfile.Add<ChromaticAberration>(false); cab.intensity.Override(Mathf.Min(1f, p.Chromatic + 0.07f));
            var ind = _dramaProfile.Add<IndirectLightingController>(false); ind.indirectDiffuseLightingMultiplier.Override(Mathf.Max(0.5f, p.AmbientMultiplier * 0.62f));
            DramaWeight = DramaTarget(); _drama.weight = PixLook.Legacy ? 0f : DramaWeight;
        }

        float DramaTarget()
        {
            if (DramaOverride >= 0f) return DramaOverride;
            float basis = Preset.Time == TimeOfDay.Night ? 0.25f : (Preset.Time == TimeOfDay.Dusk ? 0.15f : 0f);
            var md = MatchDirector.Instance;
            bool cine = md != null && ((md.Cams != null && md.Cams.Overridden) || (md.Broadcast != null && md.Broadcast.Busy));
            return cine ? 1f : basis;
        }

        // ------------------------------------------------------------------------------------------------ turf
        void ApplyTurf()
        {
            if (_turf == null || _turf.Length == 0) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            foreach (var r in _turf)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                // natural grass: strong 5-yard stripes and macro variation; artificial turf: subtle stripes, regular fibre tufts, glossier
                _mpb.SetVector(PixLook.PixTurf, ArtificialTurf ? new Vector4(5f, 0.04f, 0.25f, 1f) : new Vector4(5f, 0.13f, 1f, 0f));
                r.SetPropertyBlock(_mpb);
            }
        }

        // ------------------------------------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!Applied) return;
            float dt = Time.unscaledDeltaTime;
            Climate.Advance(dt * ClimateTimeScale);
            DramaWeight = Mathf.MoveTowards(DramaWeight, DramaTarget(), dt * 1.1f);
            if (_drama != null) _drama.weight = PixLook.Legacy ? 0f : DramaWeight;
            if (Climate.Snow > _lastSnow + 0.0005f)
            {
                var w = FieldWear.Instance;
                if (w != null) w.RefillSnowPrints((Climate.Snow - _lastSnow) * 2.2f);
                _lastSnow = Climate.Snow;
            }
            PushGlobals();
            if (Fx != null) Fx.Update(dt, Preset, Camera.main);
        }

        Exposure _exposure;

        /// <summary>v0.6.0 tuning hook: exposure offset (EV, positive = darker) as the snow cover builds; 0 by default (see SnowFullCoverEv).</summary>
        public static float SnowExposureOffset(float snow)
        {
            float s = Mathf.Clamp01(snow);
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.45f) / 0.45f));
            return SnowFullCoverEv * up;
        }
        public static float SnowFullCoverEv = 0f;   // tuning hook only (-pixSnowEv): the look gallery blows out on a runtime switch to snow regardless of exposure, a real snow match (cover 0.35 and up) is exposed fine, so no compensation ships

        public void PushGlobals()
        {
            if (_exposure != null && Preset.Kind == WeatherKind.Snow) _exposure.fixedExposure.value = Preset.ExposureEV + (Preset.Time == TimeOfDay.Night ? 0.4f : 1f) * SnowExposureOffset(Climate.Snow);
            if (Preset.Kind == 0 && Preset.SunLux == 0f) return;
            float wetShader = PixLook.Legacy || NoWetShader ? 0f : Climate.Wet;
            float snow = PixLook.Legacy || NoWetShader ? 0f : Climate.Snow;
            Shader.SetGlobalVector(PixLook.PixWeather, new Vector4(wetShader, snow, Time.time, PixLook.Legacy ? 0f : Preset.RainIntensity));
            Shader.SetGlobalVector(PixLook.PixWeather2, new Vector4(PixLook.Legacy || NoPuddles ? 0f : Preset.Puddles, Preset.RainIntensity > 0.05f ? 0.55f : 0.1f, Preset.FieldIlluminance / Mathf.PI, 0f));
            Shader.SetGlobalFloat(LegacyId, PixLook.Legacy ? 1f : 0f);
            var wear = FieldWear.Instance; if (wear != null) wear.Publish();
        }

        /// <summary>Debug dump (GFX-082): the exposure the volume stack really resolves, the sun, and every active volume with its priority / weight / exposure.</summary>
        public string DebugState()
        {
            var sb = new System.Text.StringBuilder();
            var e = VolumeManager.instance.stack != null ? VolumeManager.instance.stack.GetComponent<Exposure>() : null;
            sb.Append("stack exposure ").Append(e != null ? e.mode.value + " " + e.fixedExposure.value.ToString("0.00") + " comp " + e.compensation.value.ToString("0.00") + " active=" + e.active : "none");
            sb.Append(" | weather volume exposure ").Append(_exposure != null ? _exposure.fixedExposure.value.ToString("0.00") : "-");
            sb.Append(" | sun ").Append(RenderSettings.sun != null ? RenderSettings.sun.name + " " + RenderSettings.sun.intensity.ToString("0") + " " + RenderSettings.sun.gameObject.activeInHierarchy : "none");
            sb.Append(" | volumes:");
            foreach (var v in FindObjectsByType<Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!v.enabled || v.profile == null) continue;
                Exposure ex; bool has = v.profile.TryGet(out ex);
                sb.Append(" [").Append(v.name).Append(" p").Append(v.priority.ToString("0")).Append(" w").Append(v.weight.ToString("0.00")).Append(has && ex.active ? " EXP " + ex.mode.value + " " + ex.fixedExposure.value.ToString("0.0") + (ex.fixedExposure.overrideState ? "" : "(no override)") : "").Append("]");
            }
            return sb.ToString();
        }

        /// <summary>Instant state for tests, tools and the gallery (no particle warm-up): applies the preset and advances nothing.</summary>
        public void SetSnowCover(float v) { if (Climate != null) { Climate.Force(v, Climate.Wet); _lastSnow = v; PushGlobals(); } }

        /// <summary>The v0.5 look (before / after gallery): no weather volume, particles or surface response; the scene's own day / night volumes are all that is left.</summary>
        public void SetLegacy(bool on)
        {
            PixLook.Legacy = on;
            if (_volume != null) _volume.weight = on ? 0f : 1f;
            if (_drama != null) _drama.weight = on ? 0f : DramaWeight;
            if (Fx != null) Fx.SetVisible(!on);
            foreach (var av in FindObjectsByType<PlayerAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None)) av.ApplyLookMode();
            PushGlobals();
        }
    }
}
