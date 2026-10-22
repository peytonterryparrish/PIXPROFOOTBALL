using UnityEngine;

namespace PixFootball.Gameplay
{
    public enum CameraMode { Broadcast = 0, Behind = 1, FirstPerson = 2 }
    /// <summary>v0.6 user-play camera presets (used by <see cref="CameraMode.Behind"/>): Classic = All-Pro 2K8 framing, Madden = lower and closer behind the QB, Wide = high and far for the whole field.</summary>
    public enum CameraStyle { Classic = 0, Madden = 1, Wide = 2 }
    /// <summary>v0.6 scorebug layout: a full-width bar along the bottom of the screen (2K8 style, default) or the v0.5 block at the top left.</summary>
    public enum ScorebugStyle { Bottom = 0, Top = 1 }
    public enum LightingPreset { Night = 0, Day = 1 }

    /// <summary>
    /// Player settings persisted in PlayerPrefs. Every setter marks the preferences dirty and a small background flusher writes them to disk a moment later (and on quit / focus loss),
    /// so a crash or force-quit loses nothing and dragging a slider never writes to disk every frame.
    /// </summary>
    public static class PixSettings
    {
        const string K = "pix.";
        static bool _dirty;
        static float _dirtyAt;

        // ---- storage helpers: write the pref and schedule a flush
        static void SetI(string key, int v) { PlayerPrefs.SetInt(K + key, v); Dirty(); }
        static void SetF(string key, float v) { PlayerPrefs.SetFloat(K + key, v); Dirty(); }
        static int GetI(string key, int def) { return PlayerPrefs.GetInt(K + key, def); }
        static float GetF(string key, float def) { return PlayerPrefs.GetFloat(K + key, def); }

        /// <summary>Marks the preferences as changed; <see cref="PixSettingsFlusher"/> saves them about half a second later.</summary>
        public static void Dirty()
        {
            _dirty = true; _dirtyAt = Time.realtimeSinceStartup;
            PixSettingsFlusher.Ensure();
        }

        /// <summary>Writes pending changes to disk now.</summary>
        public static void Save() { _dirty = false; PlayerPrefs.Save(); }

        internal static void FlushIfDue(float delay)
        {
            if (_dirty && Time.realtimeSinceStartup - _dirtyAt >= delay) Save();
        }

        public static bool HasPending { get { return _dirty; } }

        // ---- video
        public static int ResolutionWidth { get { return GetI("resw", 0); } set { SetI("resw", value); } }
        public static int ResolutionHeight { get { return GetI("resh", 0); } set { SetI("resh", value); } }
        /// <summary>Display mode. With no stored choice it follows the real window state (a first launch in a window reads as Windowed).</summary>
        public static bool Fullscreen
        {
            get { return PlayerPrefs.HasKey(K + "full") ? PlayerPrefs.GetInt(K + "full", 1) == 1 : Screen.fullScreenMode != FullScreenMode.Windowed; }
            set { SetI("full", value ? 1 : 0); }
        }
        public static int Quality { get { return GetI("quality", -1); } set { SetI("quality", value); } }
        /// <summary>-1 = whatever the graphics tier says, 0 = vertical sync off, 1 = on.</summary>
        public static int VSync { get { return GetI("vsync", -1); } set { SetI("vsync", Mathf.Clamp(value, -1, 1)); } }
        /// <summary>Frame cap choices; 0 = uncapped.</summary>
        public static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 240 };
        public static readonly string[] FrameCapNames = { "Uncapped", "30", "60", "90", "120", "144", "240" };
        public static int FrameCapIndex { get { return Mathf.Clamp(GetI("fpscap", 0), 0, FrameCaps.Length - 1); } set { SetI("fpscap", Mathf.Clamp(value, 0, FrameCaps.Length - 1)); } }
        /// <summary>v0.4: HDRP dynamic resolution scale (1 = native).</summary>
        public static float RenderScale { get { return GetF("scale", 1f); } set { SetF("scale", Mathf.Clamp(value, 0.5f, 1f)); } }
        /// <summary>v0.5: 0 Off, 1 DLSS, 2 FSR.</summary>
        public static int Upscaler { get { return GetI("upscaler", 0); } set { SetI("upscaler", Mathf.Clamp(value, 0, 2)); } }
        public static readonly string[] UpscalerNames = { "Off", "DLSS", "FSR" };

        // ---- gameplay and camera
        public static bool InvertLookY { get { return GetI("invert", 0) == 1; } set { SetI("invert", value ? 1 : 0); } }
        /// <summary>The camera the player used last (it is remembered between games: the in-match camera cycle and the pause menu write it too).</summary>
        public static CameraMode DefaultCamera { get { return (CameraMode)GetI("cam2", (int)CameraMode.Behind); } set { SetI("cam2", (int)value); } }
        /// <summary>v0.6: preset of the behind-the-offense / behind-the-defense user camera.</summary>
        public static CameraStyle UserCameraStyle { get { return (CameraStyle)GetI("camstyle", (int)CameraStyle.Classic); } set { SetI("camstyle", Mathf.Clamp((int)value, 0, 2)); } }
        public static readonly string[] CameraStyleNames = { "Classic", "Madden", "Wide" };
        /// <summary>v0.6 user camera trims on top of the preset: height (m), distance (m), field of view (degrees), snap pull-back (0 none .. 1 full).</summary>
        public static float CameraHeightTrim { get { return GetF("camh", 0f); } set { SetF("camh", Mathf.Clamp(value, -3f, 6f)); } }
        public static float CameraDistanceTrim { get { return GetF("camd", 0f); } set { SetF("camd", Mathf.Clamp(value, -4f, 8f)); } }
        public static float CameraFovTrim { get { return GetF("camf", 0f); } set { SetF("camf", Mathf.Clamp(value, -10f, 15f)); } }
        public static float CameraZoomOut { get { return GetF("camz", 1f); } set { SetF("camz", Mathf.Clamp01(value)); } }
        /// <summary>v0.6: 0 bottom bar (default), 1 top block.</summary>
        public static ScorebugStyle Scorebug { get { return (ScorebugStyle)GetI("sbstyle", (int)ScorebugStyle.Bottom); } set { SetI("sbstyle", Mathf.Clamp((int)value, 0, 1)); } }
        public static readonly string[] ScorebugNames = { "Bottom bar", "Top left" };
        /// <summary>v0.6: gold stars under the eligible receivers and a ring under the user player before the snap.</summary>
        public static bool PreSnapStars { get { return GetI("stars", 1) == 1; } set { SetI("stars", value ? 1 : 0); } }
        /// <summary>Time of day for free roam and the pause-menu lighting switch. Matches write the time they resolved to <see cref="LastMatchLighting"/> instead.</summary>
        public static LightingPreset Lighting { get { return (LightingPreset)GetI("light", 0); } set { SetI("light", (int)value); } }
        /// <summary>The time of day the last match used (not a preference: Team Select owns the choice).</summary>
        public static LightingPreset LastMatchLighting { get { return (LightingPreset)GetI("lastmatchlight", 0); } set { SetI("lastmatchlight", (int)value); } }
        /// <summary>v0.4: controller rumble on hits, sacks and catches in traffic.</summary>
        public static bool Rumble { get { return GetI("rumble", 1) == 1; } set { SetI("rumble", value ? 1 : 0); } }
        /// <summary>v0.4: subtle camera shake on big hits.</summary>
        public static bool CameraShake { get { return GetI("shake", 1) == 1; } set { SetI("shake", value ? 1 : 0); } }
        /// <summary>Look sensitivity multiplier for free roam, the first-person camera and the replay free camera (mouse and right stick).</summary>
        public static float LookSensitivity { get { return GetF("look", 1f); } set { SetF("look", Mathf.Clamp(value, 0.2f, 3f)); } }
        /// <summary>v0.5: subtle helmet facemask overlay in the first-person camera.</summary>
        public static bool FirstPersonMask { get { return GetI("fpmask", 1) == 1; } set { SetI("fpmask", value ? 1 : 0); } }
        /// <summary>v0.6: photoreal players (PIX_PhotoPlayer_*) are the default; off falls back to the stylised Draft Fever bodies. Command line: -pixLegacyPlayers forces off, -pixPhotoPlayers forces on.</summary>
        public static bool PhotoPlayers
        {
            get
            {
                foreach (string a in System.Environment.GetCommandLineArgs()) { if (a == "-pixLegacyPlayers") return false; if (a == "-pixPhotoPlayers") return true; }
                return GetI("photoplayers", 1) == 1;
            }
            set { SetI("photoplayers", value ? 1 : 0); }
        }

        // ---- broadcast
        /// <summary>v0.5 broadcast cinematics: 0 Full (establishing shots, post-play cuts, auto replays, shows), 1 Minimal (big plays only), 2 Off (live camera only).</summary>
        public static int Cinematics { get { return GetI("cine", 0); } set { SetI("cine", Mathf.Clamp(value, 0, 2)); } }
        public static readonly string[] CinematicsNames = { "Full", "Minimal", "Off" };
        /// <summary>v0.5: automatic instant replays after big plays.</summary>
        public static bool AutoReplay { get { return GetI("autoreplay", 1) == 1; } set { SetI("autoreplay", value ? 1 : 0); } }
        /// <summary>v0.5: TV overlays (lower thirds, drive summaries, stat cards).</summary>
        public static bool TvOverlays { get { return GetI("tvov", 1) == 1; } set { SetI("tvov", value ? 1 : 0); } }

        // ---- audio
        public static float MasterVolume { get { return GetF("vol_master", 0.9f); } set { SetF("vol_master", Mathf.Clamp01(value)); } }
        public static float MusicVolume { get { return GetF("vol_music", 0.7f); } set { SetF("vol_music", Mathf.Clamp01(value)); } }
        public static float CrowdVolume { get { return GetF("vol_crowd", 0.8f); } set { SetF("vol_crowd", Mathf.Clamp01(value)); } }
        /// <summary>Hits, catches, kicks, whistles and menu sounds.</summary>
        public static float SfxVolume { get { return GetF("vol_sfx", 0.9f); } set { SetF("vol_sfx", Mathf.Clamp01(value)); } }

        // ---- match setup (Team Select): written when the game is started, not while the options are being browsed
        /// <summary>v0.6: weather and time of day choices of the team-select screen (PixFootball.Core.WeatherChoice / TimeChoice values).</summary>
        public static int WeatherSetting { get { return GetI("weather", 1); } set { SetI("weather", Mathf.Clamp(value, 0, 5)); } }
        public static int TimeSetting { get { return GetI("timeofday", 3); } set { SetI("timeofday", Mathf.Clamp(value, 0, 3)); } }

        /// <summary>The whole last Team Select configuration (teams, quarter length, rules, difficulty, side, weather, time) as JSON; empty if none was stored.</summary>
        public static string LastMatchConfigJson { get { return PlayerPrefs.GetString(K + "matchcfg", ""); } set { PlayerPrefs.SetString(K + "matchcfg", value ?? ""); Dirty(); } }

        /// <summary>Stores the match setup chosen at KICK OFF.</summary>
        public static void StoreMatchConfig(MatchConfig c) { if (c != null) LastMatchConfigJson = JsonUtility.ToJson(c); }

        /// <summary>The stored setup, or null when nothing (valid) was stored.</summary>
        public static MatchConfig LoadMatchConfig()
        {
            string j = LastMatchConfigJson;
            if (string.IsNullOrEmpty(j)) return null;
            try { return JsonUtility.FromJson<MatchConfig>(j); } catch { return null; }
        }

        /// <summary>Restores every preference to its default (video mode and match setup excluded) and re-applies.</summary>
        public static void ResetToDefaults()
        {
            foreach (string key in new[] { "invert", "cam2", "camstyle", "camh", "camd", "camf", "camz", "sbstyle", "stars", "light", "rumble", "shake", "look", "fpmask", "photoplayers", "cine", "autoreplay", "tvov",
                                           "vol_master", "vol_music", "vol_crowd", "vol_sfx", "vsync", "fpscap", "scale", "upscaler", "quality" })
                PlayerPrefs.DeleteKey(K + key);
            Dirty();
            Apply();
        }

        /// <summary>Applies display, quality, vsync / frame cap, background running and master volume.</summary>
        public static void Apply()
        {
            // the game keeps simulating and playing audio when the window loses focus (alt-tab, overlays, automation)
            Application.runInBackground = true;
            if (Quality >= 0 && Quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
            if (!Application.isEditor && !HasScreenArgs())
            {
                var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (ResolutionWidth > 0 && ResolutionHeight > 0) Screen.SetResolution(ResolutionWidth, ResolutionHeight, mode);
                else if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
            }
            ApplyFrameSettings();
            AudioListener.volume = MasterVolume;
        }

        /// <summary>Vertical sync and the frame cap (a graphics-tier switch resets vsync to the level's value, so QualityTiers calls this again after it).</summary>
        public static void ApplyFrameSettings()
        {
            if (VSync >= 0) QualitySettings.vSyncCount = VSync;
            int cap = FrameCaps[FrameCapIndex];
            Application.targetFrameRate = cap > 0 && QualitySettings.vSyncCount == 0 ? cap : -1;
        }

        static bool HasScreenArgs()
        {
            foreach (string a in System.Environment.GetCommandLineArgs()) if (a == "-screen-width" || a == "-screen-height") return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Apply();
            ConfigureAudio();
        }

        /// <summary>Enough voices for 22 players' hits, the crowd layers, whistles, UI and music (the project default of 32 let busy plays steal and mute effects), and a smaller mixer buffer so hit sounds land with the contact (QA-986).</summary>
        static void ConfigureAudio()
        {
            if (Application.isEditor) return;
            var ac = AudioSettings.GetConfiguration();
            bool change = false;
            if (ac.numRealVoices < 64) { ac.numRealVoices = 64; change = true; }
            if (ac.numVirtualVoices < 512) { ac.numVirtualVoices = 512; change = true; }
            if (ac.dspBufferSize > 512) { ac.dspBufferSize = 512; change = true; }
            if (change) AudioSettings.Reset(ac);
        }
    }

    /// <summary>Hidden runtime object that writes dirty preferences to disk shortly after the last change, on focus loss and on quit.</summary>
    public sealed class PixSettingsFlusher : MonoBehaviour
    {
        static PixSettingsFlusher _inst;

        public static void Ensure()
        {
            if (_inst != null || !Application.isPlaying) return;
            var go = new GameObject("PixSettingsFlusher") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _inst = go.AddComponent<PixSettingsFlusher>();
        }

        void Update() { PixSettings.FlushIfDue(0.5f); }
        void OnApplicationPause(bool paused) { if (paused && PixSettings.HasPending) PixSettings.Save(); }
        void OnApplicationFocus(bool focus) { if (!focus && PixSettings.HasPending) PixSettings.Save(); }
        void OnApplicationQuit() { if (PixSettings.HasPending) PixSettings.Save(); }
    }
}
