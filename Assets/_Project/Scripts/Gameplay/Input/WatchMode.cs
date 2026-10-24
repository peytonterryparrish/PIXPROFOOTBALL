using System;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>The helper bar (the controls line at the bottom of the match screen): Auto shows it for a few seconds when something new can be pressed, On keeps a small one up, Off never draws it.</summary>
    public enum HelperBarMode { Auto = 0, On = 1, Off = 2 }

    /// <summary>
    /// v0.11.0 watch mode. Until controller input comes back, every game (Play Now, Franchise, Practice) starts as CPU versus CPU on the broadcast camera: the CPU calls both sides, the user only
    /// pauses, cycles the camera, hides the HUD and changes the game speed. The user-control code is untouched behind <see cref="UserControl"/> (Settings: "Controller input", or the
    /// <c>-pixUserControl</c> command line flag), so it comes back by flipping one setting.
    /// </summary>
    public static class WatchMode
    {
        const string KeyUserControl = "pix.userctl", KeyHelper = "pix.helperbar";

        static bool Arg(string name) { foreach (string a in Environment.GetCommandLineArgs()) if (a == name) return true; return false; }

        /// <summary>True when the user's controller drives a team (the pre-v0.11 game). False (the default) = watch mode.</summary>
        public static bool UserControl
        {
            get
            {
                if (Arg("-pixUserControl")) return true;
                if (Arg("-pixWatch")) return false;
                return PlayerPrefs.GetInt(KeyUserControl, 0) == 1;
            }
            set { PlayerPrefs.SetInt(KeyUserControl, value ? 1 : 0); PixSettings.Dirty(); }
        }

        public static bool Enabled { get { return !UserControl; } }

        /// <summary>The helper bar preference (persisted). Auto by default.</summary>
        public static HelperBarMode Helper
        {
            get { return (HelperBarMode)Mathf.Clamp(PlayerPrefs.GetInt(KeyHelper, 0), 0, 2); }
            set { PlayerPrefs.SetInt(KeyHelper, (int)value); PixSettings.Dirty(); }
        }
        public static readonly string[] HelperNames = { "Auto", "On", "Off" };

        /// <summary>Raised when the helper preference changes (the HUD redraws).</summary>
        public static event Action HelperChanged;
        public static void SetHelper(HelperBarMode m) { if (Helper == m) return; Helper = m; if (HelperChanged != null) HelperChanged(); }

        /// <summary>The next helper mode for the toggle button: Auto and On go to Off, Off goes back to Auto.</summary>
        public static HelperBarMode ToggleHelper(HelperBarMode m) { return m == HelperBarMode.Off ? HelperBarMode.Auto : HelperBarMode.Off; }

        // ---- game speed (not persisted: every game starts at 1x)
        public static readonly float[] Speeds = { 1f, 2f };
        public static int SpeedIndex;
        public static float Speed { get { return Speeds[Mathf.Clamp(SpeedIndex, 0, Speeds.Length - 1)]; } }
        public static float NextSpeed() { SpeedIndex = (SpeedIndex + 1) % Speeds.Length; return Speed; }
        public static string SpeedLabel { get { return Speed.ToString("0") + "X"; } }
    }

    /// <summary>
    /// The receiver input lock (owner note 9): the user must not steer a receiver while the pass is in the air; control only goes to a receiver once the ball is secured in his hands. The pure rule
    /// lives here (no engine types) so it is unit-tested; <see cref="MatchDirector"/> asks it every frame before it hands a stick to a player.
    /// </summary>
    public static class ReceiverInputLock
    {
        /// <summary>
        /// True when the user's input must not reach the agent he is "on".
        /// An offensive player other than the quarterback (slot 0) on a scrimmage play who does not have the ball in his hands is locked: that covers the whole flight of a pass
        /// (the intended target is only selected for the camera and the marker) and the catch itself, until <paramref name="hasBall"/> turns true.
        /// </summary>
        public static bool IsLocked(bool offense, bool scrimmage, int slot, bool hasBall)
        {
            if (!offense || !scrimmage) return false;
            if (slot == 0) return false;     // the quarterback (throwing / scrambling) keeps his own controls
            return !hasBall;
        }

        /// <summary>The input the sim is allowed to see: the stick and the buttons are dropped for a locked agent.</summary>
        public static bool Allow(bool offense, bool scrimmage, int slot, bool hasBall) { return !IsLocked(offense, scrimmage, slot, hasBall); }
    }
}
