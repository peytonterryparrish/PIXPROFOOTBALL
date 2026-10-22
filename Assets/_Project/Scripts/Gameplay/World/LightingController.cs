using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Switches between the night (moon) and day (sun) presets built by SceneBuilder. Each preset is a root object with its own Volume and light; the floodlight rig is its
    /// own root (v0.6) so dusk, overcast and rain games can have the lights on under the sun. When a <see cref="WeatherSystem"/> exists it owns the details (intensities,
    /// sky, fog, grade) and this class only forwards the day / night request.
    /// </summary>
    public class LightingController : MonoBehaviour
    {
        public GameObject NightRoot, DayRoot, FloodRoot;
        public Light NightSun, DaySun;
        public LightingPreset Current { get; private set; } = LightingPreset.Night;
        /// <summary>Optional objects that only exist at night (stadium floodlight glow meshes etc.).</summary>
        public GameObject[] NightOnly;

        /// <param name="remember">store the choice as the free-roam default (the in-match toggle does not: the match owns its own time of day)</param>
        public void Apply(LightingPreset p, bool remember = true)
        {
            Current = p;
            if (NightRoot != null) NightRoot.SetActive(p == LightingPreset.Night);
            if (DayRoot != null) DayRoot.SetActive(p == LightingPreset.Day);
            if (FloodRoot != null && WeatherSystem.Instance == null) FloodRoot.SetActive(p == LightingPreset.Night);
            if (NightOnly != null) foreach (var g in NightOnly) if (g != null) g.SetActive(p == LightingPreset.Night);
            if (remember) PixSettings.Lighting = p;
            RenderSettings.sun = p == LightingPreset.Night ? NightSun : DaySun;
            if (WeatherSystem.Instance != null) WeatherSystem.Instance.OnLightingPreset(p);
        }

        /// <summary>The weather system switched the roots itself: record the preset without touching anything.</summary>
        public void NoteTime(LightingPreset p)
        {
            Current = p; PixSettings.LastMatchLighting = p;
            if (NightOnly != null) foreach (var g in NightOnly) if (g != null) g.SetActive(p == LightingPreset.Night);
        }

        public void Toggle(bool remember = true) { Apply(Current == LightingPreset.Night ? LightingPreset.Day : LightingPreset.Night, remember); }
    }
}
