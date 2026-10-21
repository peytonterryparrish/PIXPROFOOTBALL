using UnityEngine;

namespace PixFootball.Gameplay
{
    public enum CameraMode { Broadcast = 0, Behind = 1, FirstPerson = 2 }
    public enum LightingPreset { Night = 0, Day = 1 }

    /// <summary>Player settings persisted in PlayerPrefs.</summary>
    public static class PixSettings
    {
        const string K = "pix.";
        public static int ResolutionWidth { get { return PlayerPrefs.GetInt(K + "resw", 0); } set { PlayerPrefs.SetInt(K + "resw", value); } }
        public static int ResolutionHeight { get { return PlayerPrefs.GetInt(K + "resh", 0); } set { PlayerPrefs.SetInt(K + "resh", value); } }
        public static bool Fullscreen { get { return PlayerPrefs.GetInt(K + "full", 1) == 1; } set { PlayerPrefs.SetInt(K + "full", value ? 1 : 0); } }
        public static int Quality { get { return PlayerPrefs.GetInt(K + "quality", -1); } set { PlayerPrefs.SetInt(K + "quality", value); } }
        public static bool InvertLookY { get { return PlayerPrefs.GetInt(K + "invert", 0) == 1; } set { PlayerPrefs.SetInt(K + "invert", value ? 1 : 0); } }
        public static CameraMode DefaultCamera { get { return (CameraMode)PlayerPrefs.GetInt(K + "cam", 0); } set { PlayerPrefs.SetInt(K + "cam", (int)value); } }
        public static LightingPreset Lighting { get { return (LightingPreset)PlayerPrefs.GetInt(K + "light", 0); } set { PlayerPrefs.SetInt(K + "light", (int)value); } }
        public static float MasterVolume { get { return PlayerPrefs.GetFloat(K + "vol_master", 0.9f); } set { PlayerPrefs.SetFloat(K + "vol_master", Mathf.Clamp01(value)); } }
        public static float MusicVolume { get { return PlayerPrefs.GetFloat(K + "vol_music", 0.7f); } set { PlayerPrefs.SetFloat(K + "vol_music", Mathf.Clamp01(value)); } }
        public static float CrowdVolume { get { return PlayerPrefs.GetFloat(K + "vol_crowd", 0.8f); } set { PlayerPrefs.SetFloat(K + "vol_crowd", Mathf.Clamp01(value)); } }
        /// <summary>v0.4: controller rumble on hits, sacks and catches in traffic.</summary>
        /// <summary>v0.4: HDRP dynamic resolution scale (1 = native).</summary>
        public static float RenderScale { get { return PlayerPrefs.GetFloat(K + "scale", 1f); } set { PlayerPrefs.SetFloat(K + "scale", Mathf.Clamp(value, 0.5f, 1f)); } }
        public static bool Rumble { get { return PlayerPrefs.GetInt(K + "rumble", 1) == 1; } set { PlayerPrefs.SetInt(K + "rumble", value ? 1 : 0); } }
        /// <summary>v0.4: subtle camera shake on big hits.</summary>
        public static bool CameraShake { get { return PlayerPrefs.GetInt(K + "shake", 1) == 1; } set { PlayerPrefs.SetInt(K + "shake", value ? 1 : 0); } }
        public static float LookSensitivity { get { return PlayerPrefs.GetFloat(K + "look", 1f); } set { PlayerPrefs.SetFloat(K + "look", Mathf.Clamp(value, 0.2f, 3f)); } }
        /// <summary>v0.5 broadcast cinematics: 0 Full (establishing shots, post-play cuts, auto replays, shows), 1 Minimal (big plays only), 2 Off (live camera only).</summary>
        public static int Cinematics { get { return PlayerPrefs.GetInt(K + "cine", 0); } set { PlayerPrefs.SetInt(K + "cine", Mathf.Clamp(value, 0, 2)); } }
        public static readonly string[] CinematicsNames = { "Full", "Minimal", "Off" };
        /// <summary>v0.5: automatic instant replays after big plays.</summary>
        public static bool AutoReplay { get { return PlayerPrefs.GetInt(K + "autoreplay", 1) == 1; } set { PlayerPrefs.SetInt(K + "autoreplay", value ? 1 : 0); } }
        /// <summary>v0.5: TV overlays (lower thirds, drive summaries, stat cards).</summary>
        public static bool TvOverlays { get { return PlayerPrefs.GetInt(K + "tvov", 1) == 1; } set { PlayerPrefs.SetInt(K + "tvov", value ? 1 : 0); } }
        /// <summary>v0.5: subtle helmet facemask overlay in the first-person camera.</summary>
        public static bool FirstPersonMask { get { return PlayerPrefs.GetInt(K + "fpmask", 1) == 1; } set { PlayerPrefs.SetInt(K + "fpmask", value ? 1 : 0); } }
        /// <summary>v0.5: 0 Off, 1 DLSS, 2 FSR.</summary>
        public static int Upscaler { get { return PlayerPrefs.GetInt(K + "upscaler", 0); } set { PlayerPrefs.SetInt(K + "upscaler", Mathf.Clamp(value, 0, 2)); } }
        public static readonly string[] UpscalerNames = { "Off", "DLSS", "FSR" };

        public static void Save() { PlayerPrefs.Save(); }

        /// <summary>Applies display, quality and master volume.</summary>
        public static void Apply()
        {
            if (Quality >= 0 && Quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
            if (ResolutionWidth > 0 && ResolutionHeight > 0 && !Application.isEditor && !HasScreenArgs())
                Screen.SetResolution(ResolutionWidth, ResolutionHeight, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            AudioListener.volume = MasterVolume;
        }

        static bool HasScreenArgs()
        {
            foreach (string a in System.Environment.GetCommandLineArgs()) if (a == "-screen-width" || a == "-screen-height") return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { Apply(); }
    }
}
