using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.Gameplay
{
    /// <summary>Switches between the night (floodlights) and day presets built by SceneBuilder. Each preset is a root object with its own Volume and lights.</summary>
    public class LightingController : MonoBehaviour
    {
        public GameObject NightRoot, DayRoot;
        public Light NightSun, DaySun;
        public LightingPreset Current { get; private set; } = LightingPreset.Night;
        /// <summary>Optional objects that only exist at night (stadium floodlight glow meshes etc.).</summary>
        public GameObject[] NightOnly;

        public void Apply(LightingPreset p)
        {
            Current = p;
            if (NightRoot != null) NightRoot.SetActive(p == LightingPreset.Night);
            if (DayRoot != null) DayRoot.SetActive(p == LightingPreset.Day);
            if (NightOnly != null) foreach (var g in NightOnly) if (g != null) g.SetActive(p == LightingPreset.Night);
            PixSettings.Lighting = p;
            RenderSettings.sun = p == LightingPreset.Night ? NightSun : DaySun;
        }

        public void Toggle() { Apply(Current == LightingPreset.Night ? LightingPreset.Day : LightingPreset.Night); }
    }
}
