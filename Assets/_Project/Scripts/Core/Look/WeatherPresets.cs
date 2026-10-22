using System;

namespace PixFootball.Core
{
    public enum WeatherKind { Clear = 0, Overcast = 1, Rain = 2, Snow = 3, Fog = 4 }
    public enum TimeOfDay { Day = 0, Dusk = 1, Night = 2 }

    /// <summary>What the team-select screen stores: a fixed weather / time of day or "random" (resolved with the game seed).</summary>
    public enum WeatherChoice { Random = 0, Clear = 1, Overcast = 2, Rain = 3, Snow = 4, Fog = 5 }
    public enum TimeChoice { Random = 0, Day = 1, Dusk = 2, Night = 3 }

    /// <summary>Plain RGB triple so the preset data stays engine free.</summary>
    public struct Rgb
    {
        public float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        public static Rgb Lerp(Rgb a, Rgb b, float t) { return new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t); }
    }

    /// <summary>
    /// Everything the look of one game depends on: sun, sky, floodlights, fog, precipitation, wet surfaces, snow accumulation, the colour grade
    /// (a 2010s college broadcast look: slightly warm, saturated greens, crisp contrast) and the light gameplay effect of the weather.
    /// Pure data (engine free); <c>PixFootball.Gameplay.WeatherSystem</c> turns it into lights, volumes, particles and shader globals.
    /// </summary>
    public struct WeatherPreset
    {
        public WeatherKind Kind;
        public TimeOfDay Time;

        // ---- sun / sky ----
        /// <summary>Elevation of the sun (day) or moon (night) above the horizon, degrees.</summary>
        public float SunElevation;
        public float SunAzimuth;
        public float SunLux;
        public Rgb SunColor;
        /// <summary>Fixed exposure value for the volume (HDRP fixed exposure, EV100).</summary>
        public float ExposureEV;
        /// <summary>0 = clear sky, 1 = fully overcast (sky multiplier and sun shadow strength follow it).</summary>
        public float CloudCover;
        public bool PhysicallyBasedSky;
        /// <summary>Gradient sky colours (night / overcast): zenith, horizon, ground.</summary>
        public Rgb SkyTop, SkyMiddle, SkyBottom;
        public float SkyMultiplier;
        public float AmbientMultiplier;

        // ---- floodlights ----
        public bool FloodlightsOn;
        /// <summary>Multiplier on the rig's intensity (dusk uses less, rain / fog more to punch through the haze).</summary>
        public float FloodlightScale;

        // ---- fog ----
        public float FogMeanFreePath;
        public Rgb FogAlbedo;
        public float FogAnisotropy;
        public bool VolumetricFog;
        public float FogMaxHeight;

        // ---- surfaces / precipitation ----
        /// <summary>Wet surfaces 0..1 (darker, glossier, ripples). Rain starts the game already wet.</summary>
        public float Wetness;
        public float Puddles;
        public float RainIntensity;
        public float SnowfallIntensity;
        /// <summary>Snow cover the ground starts the game with, and the cover it keeps building toward.</summary>
        public float SnowStart, SnowTarget;
        /// <summary>Cover gained per 60 seconds of game time while it snows.</summary>
        public float SnowPerMinute;
        public float WindStrength;
        public bool BreathVapor;

        // ---- colour grade ----
        public float Temperature;      // white balance, -100..100
        public float Tint;
        public float PostExposure;     // EV
        public float Contrast;         // -100..100
        public float Saturation;       // -100..100
        public float GreenBoost;       // hue vs saturation: how much the greens are pushed (0..1)
        public Rgb Lift, Gamma, Gain;  // offsets around neutral (0)
        /// <summary>Split-toning grade (multipliers around 1): cool shadows, warm highlights give the cinematic broadcast look.</summary>
        public Rgb ShadowTint, MidtoneTint, HighlightTint;
        public float Chromatic;        // chromatic aberration 0..1 (lens)
        /// <summary>Volumetric dimmer of the floodlights: how visible their beams are in the haze (light shafts).</summary>
        public float FloodVolumetric;
        /// <summary>Balance of the floodlight rig: above 0 favours the far (rim) lamps, below 0 the near (key) lamps.</summary>
        public float FloodRimBias;
        public float Bloom;
        public float Vignette;
        public float FilmGrain;
        public Rgb ColorFilter;

        // ---- gameplay (light, via PlaySimOptions.BallSlickness) ----
        /// <summary>0 = dry ball, 1 = soaked: catching gets harder and fumbles a bit more likely.</summary>
        public float BallSlickness;

        // ---- derived ----
        /// <summary>Rough illuminance on the field (lux) used to scale view dependent fabric sheen so it looks right in every preset.</summary>
        public float FieldIlluminance
        {
            get
            {
                float sun = SunLux * (float)Math.Max(0.0, Math.Sin(SunElevation * Math.PI / 180.0)) * (1f - 0.75f * CloudCover);
                float flood = FloodlightsOn ? 1800f * FloodlightScale : 0f;
                return sun + flood + 300f * AmbientMultiplier;
            }
        }

        public bool IsWet { get { return Wetness > 0.05f; } }
        public bool Precipitating { get { return RainIntensity > 0.01f || SnowfallIntensity > 0.01f; } }
        public string Label { get { return TimeName(Time) + " - " + KindName(Kind); } }

        public static string KindName(WeatherKind k)
        {
            switch (k) { case WeatherKind.Clear: return "Clear"; case WeatherKind.Overcast: return "Overcast"; case WeatherKind.Rain: return "Rain"; case WeatherKind.Snow: return "Snow"; default: return "Fog"; }
        }
        public static string TimeName(TimeOfDay t) { return t == TimeOfDay.Day ? "Day" : (t == TimeOfDay.Dusk ? "Dusk" : "Night"); }
    }

    /// <summary>The preset table: Day / Dusk / Night x Clear / Overcast / Rain / Snow / Fog (15 combinations) built from a time-of-day base and a weather modifier.</summary>
    public static class WeatherPresets
    {
        public static readonly WeatherKind[] Kinds = { WeatherKind.Clear, WeatherKind.Overcast, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Fog };
        public static readonly TimeOfDay[] Times = { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night };
        public static readonly string[] WeatherChoiceNames = { "Random", "Clear", "Overcast", "Rain", "Snow", "Fog" };
        public static readonly string[] TimeChoiceNames = { "Random", "Day", "Dusk", "Night" };

        public static WeatherKind Resolve(WeatherChoice c, Rng rng)
        {
            if (c == WeatherChoice.Random)
            {
                // weighted: mostly fair weather, with the occasional rain, snow or fog game
                float r = rng.NextFloat();
                if (r < 0.50f) return WeatherKind.Clear;
                if (r < 0.70f) return WeatherKind.Overcast;
                if (r < 0.84f) return WeatherKind.Rain;
                if (r < 0.94f) return WeatherKind.Snow;
                return WeatherKind.Fog;
            }
            return (WeatherKind)((int)c - 1);
        }

        public static TimeOfDay Resolve(TimeChoice c, Rng rng)
        {
            if (c == TimeChoice.Random)
            {
                float r = rng.NextFloat();
                return r < 0.40f ? TimeOfDay.Day : (r < 0.62f ? TimeOfDay.Dusk : TimeOfDay.Night);
            }
            return (TimeOfDay)((int)c - 1);
        }

        public static WeatherPreset Get(WeatherKind kind, TimeOfDay time)
        {
            var p = TimeBase(time);
            p.Kind = kind; p.Time = time;
            ApplyWeather(ref p, kind, time);
            return p;
        }

        // ------------------------------------------------------------------------------------------------ time of day
        static WeatherPreset TimeBase(TimeOfDay t)
        {
            var p = new WeatherPreset();
            // shared grade: the broadcast look - warm highlights, crisp contrast, rich greens
            p.Temperature = 8f; p.Tint = 0f; p.PostExposure = 0f; p.Contrast = 14f; p.Saturation = 10f; p.GreenBoost = 0.3f;
            p.ShadowTint = new Rgb(0.94f, 0.97f, 1.06f); p.MidtoneTint = new Rgb(1f, 1f, 1f); p.HighlightTint = new Rgb(1.02f, 1.0f, 0.96f); p.Chromatic = 0.0f; p.FloodVolumetric = 2.5f; p.FloodRimBias = 0.25f;
            p.Lift = new Rgb(0f, 0f, 0.01f); p.Gamma = new Rgb(0f, 0f, 0f); p.Gain = new Rgb(0.02f, 0.01f, 0f);
            p.Bloom = 0.14f; p.Vignette = 0.2f; p.FilmGrain = 0.08f; p.ColorFilter = new Rgb(1f, 0.97f, 0.93f);
            p.SunColor = new Rgb(1f, 0.96f, 0.9f); p.FloodlightScale = 1f; p.AmbientMultiplier = 1f; p.SkyMultiplier = 1f; p.SunAzimuth = 38f;
            p.FogMeanFreePath = 4000f; p.FogAlbedo = new Rgb(0.75f, 0.8f, 0.9f); p.FogAnisotropy = 0.4f; p.FogMaxHeight = 140f;
            switch (t)
            {
                case TimeOfDay.Day:
                    p.SunElevation = 58f; p.SunLux = 105000f; p.ExposureEV = 13.7f; p.PhysicallyBasedSky = true; p.FloodlightsOn = false; p.AmbientMultiplier = 1.5f;
                    p.VolumetricFog = false; p.FogMeanFreePath = 6000f;
                    break;
                case TimeOfDay.Dusk:
                    p.SunElevation = 5.5f; p.SunLux = 52000f; p.SunColor = new Rgb(1f, 0.62f, 0.34f); p.ExposureEV = 12.5f; p.PhysicallyBasedSky = true; p.FloodlightsOn = true; p.FloodlightScale = 0.85f;
                    p.Temperature = 16f; p.Saturation = 18f; p.Contrast = 19f; p.Bloom = 0.18f; p.Vignette = 0.24f; p.ShadowTint = new Rgb(0.93f, 0.94f, 1.08f); p.HighlightTint = new Rgb(1.07f, 1.0f, 0.91f); p.FogMeanFreePath = 900f; p.VolumetricFog = true; p.FogAlbedo = new Rgb(0.8f, 0.66f, 0.6f); p.FogAnisotropy = 0.6f;
                    break;
                default:
                    p.SunElevation = 52f; p.SunLux = 90f; p.SunColor = new Rgb(0.55f, 0.66f, 1f); p.ExposureEV = 6.4f; p.PhysicallyBasedSky = false; p.FloodlightsOn = true; p.AmbientMultiplier = 1.1f; p.Contrast = 19f; p.ShadowTint = new Rgb(0.94f, 0.97f, 1.05f); p.HighlightTint = new Rgb(1.02f, 1.0f, 0.96f); p.Vignette = 0.22f; p.FloodRimBias = 0.3f; p.Chromatic = 0.0f; p.PostExposure = -0.25f;
                    p.SkyTop = new Rgb(0.10f, 0.14f, 0.30f); p.SkyMiddle = new Rgb(0.08f, 0.10f, 0.20f); p.SkyBottom = new Rgb(0.04f, 0.045f, 0.07f); p.SkyMultiplier = 120f; p.SunAzimuth = -35f;
                    p.VolumetricFog = true; p.FogMeanFreePath = 340f; p.FogAlbedo = new Rgb(0.62f, 0.7f, 0.85f); p.FogAnisotropy = 0.68f; p.Bloom = 0.2f;
                    break;
            }
            return p;
        }

        // ------------------------------------------------------------------------------------------------ weather modifiers
        static void ApplyWeather(ref WeatherPreset p, WeatherKind kind, TimeOfDay t)
        {
            bool night = t == TimeOfDay.Night, dusk = t == TimeOfDay.Dusk;
            switch (kind)
            {
                case WeatherKind.Clear:
                    break;
                case WeatherKind.Overcast:
                    p.CloudCover = 0.85f; p.SunLux *= 0.32f; p.ExposureEV -= night ? 0f : 0.9f; p.Saturation -= 8f; p.Temperature -= 5f; p.Contrast += 3f;
                    p.AmbientMultiplier *= 1.25f; p.SunColor = Rgb.Lerp(p.SunColor, new Rgb(0.85f, 0.9f, 1f), 0.5f); p.FogMeanFreePath = Math.Min(p.FogMeanFreePath, 2400f);
                    p.FloodlightScale *= night ? 1f : 1.2f; if (!night && !dusk) p.FloodlightsOn = true;   // flat light: the lights come on at midday too
                    p.FloodlightScale = p.FloodlightsOn && !night && !dusk ? 0.55f : p.FloodlightScale;
                    p.PhysicallyBasedSky = !night; p.SkyMultiplier = night ? p.SkyMultiplier * 0.7f : 0.6f;
                    p.BallSlickness = 0f;
                    break;
                case WeatherKind.Rain:
                    p.CloudCover = 1f; p.SunLux *= 0.2f; p.ExposureEV -= night ? 0f : 1.2f; p.Saturation -= 12f; p.Temperature -= 9f; p.Contrast += 6f; p.GreenBoost += 0.1f;
                    p.AmbientMultiplier *= 1.15f; p.SunColor = Rgb.Lerp(p.SunColor, new Rgb(0.75f, 0.85f, 1f), 0.6f);
                    p.Wetness = 1f; p.Puddles = 0.85f; p.RainIntensity = 1f; p.WindStrength = 0.4f; p.BallSlickness = 0.6f;
                    p.VolumetricFog = true; p.FogMeanFreePath = night ? 230f : 650f; p.FogAnisotropy = 0.6f; p.FogAlbedo = new Rgb(0.6f, 0.66f, 0.74f);
                    p.FloodlightsOn = true; p.FloodlightScale = night ? 1.15f : (dusk ? 0.95f : 0.6f);
                    p.PhysicallyBasedSky = !night; p.SkyMultiplier = night ? p.SkyMultiplier * 0.55f : 0.5f; p.Bloom += 0.05f; p.FilmGrain += 0.02f;
                    break;
                case WeatherKind.Snow:
                    p.CloudCover = 0.95f; p.SunLux *= 0.3f; p.ExposureEV -= night ? 0f : 0.7f; p.Saturation -= 10f; p.Temperature -= 14f; p.Contrast += 2f; p.Tint = -4f; p.GreenBoost = 0.1f;
                    p.AmbientMultiplier *= 1.3f; p.SunColor = Rgb.Lerp(p.SunColor, new Rgb(0.8f, 0.88f, 1f), 0.6f); p.ExposureEV += night ? 0f : 0.3f;   // v0.7: was +1.2 (tuned while the runtime switch kept the previous preset exposure, GFX-082): with the volume fix the day snow game read as dusk
                    p.SnowfallIntensity = 1f; p.SnowStart = 0.35f; p.SnowTarget = 1f; p.SnowPerMinute = 0.07f; p.WindStrength = 0.25f; p.BreathVapor = true; p.BallSlickness = 0.3f; p.Wetness = 0.15f;
                    p.VolumetricFog = true; p.FogMeanFreePath = night ? 300f : 520f; p.FogAnisotropy = 0.55f; p.FogAlbedo = new Rgb(0.78f, 0.82f, 0.9f);
                    p.FloodlightsOn = true; p.FloodlightScale = night ? 1.1f : (dusk ? 0.95f : 0.6f);
                    p.PhysicallyBasedSky = !night; p.SkyMultiplier = night ? p.SkyMultiplier * 0.75f : 0.55f; p.Bloom += 0.06f;
                    break;
                case WeatherKind.Fog:
                    p.CloudCover = 0.7f; p.SunLux *= 0.45f; p.ExposureEV -= night ? 0f : 0.5f; p.Saturation -= 10f; p.Temperature -= 4f; p.Contrast -= 2f; p.GreenBoost *= 0.7f;
                    p.AmbientMultiplier *= 1.2f; p.VolumetricFog = true; p.FogMeanFreePath = night ? 110f : (dusk ? 150f : 190f); p.FogAnisotropy = 0.5f; p.FogAlbedo = new Rgb(0.74f, 0.78f, 0.84f);
                    p.FogMaxHeight = 60f; p.Wetness = 0.25f; p.BreathVapor = night || dusk;
                    p.FloodlightsOn = true; p.FloodlightScale = night ? 1.0f : (dusk ? 0.9f : 0.55f); p.PhysicallyBasedSky = !night; p.SkyMultiplier = night ? p.SkyMultiplier * 0.8f : 0.6f; p.Bloom += 0.08f;
                    break;
            }
        }

        /// <summary>Preset index 0..14 in (kind, time) order, used for the gallery and tests.</summary>
        public static int Index(WeatherKind k, TimeOfDay t) { return (int)k * 3 + (int)t; }
    }

    /// <summary>Slowly changing state of one game's weather: snow builds up on the ground while it snows, wet surfaces dry out when it stops.</summary>
    public sealed class WeatherClimate
    {
        public WeatherPreset Preset { get; private set; }
        public float Snow { get; private set; }
        public float Wet { get; private set; }

        public WeatherClimate(WeatherPreset preset) { Set(preset); }

        public void Set(WeatherPreset preset)
        {
            Preset = preset;
            Snow = preset.SnowStart;
            Wet = preset.Wetness;
        }

        /// <summary>Sets the surface state directly (tools, tests, the gallery).</summary>
        public void Force(float snow, float wet) { Snow = snow < 0f ? 0f : (snow > 1f ? 1f : snow); Wet = wet < 0f ? 0f : (wet > 1f ? 1f : wet); }

        /// <summary>Advances by <paramref name="gameSeconds"/> of game clock (not real time).</summary>
        public void Advance(float gameSeconds)
        {
            if (gameSeconds <= 0f) return;
            if (Preset.SnowfallIntensity > 0.01f && Snow < Preset.SnowTarget)
                Snow = Math.Min(Preset.SnowTarget, Snow + Preset.SnowPerMinute * Preset.SnowfallIntensity * gameSeconds / 60f);
            float targetWet = Preset.Wetness;
            // surfaces dry slowly when nothing is falling, and wet quickly when it rains
            Wet += (targetWet - Wet) * Math.Min(1f, gameSeconds / (targetWet > Wet ? 20f : 600f));
        }
    }
}
