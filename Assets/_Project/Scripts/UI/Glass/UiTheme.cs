using System;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.UI
{
    /// <summary>The two UI looks: the glass design system (default) and the Classic flat kit that shipped before it. Screens read <see cref="UiTheme.IsGlass"/> when they are built.</summary>
    public enum UiThemeKind { Glass = 0, Classic = 1 }

    /// <summary>
    /// The UI theme setting ("pix.uitheme", stored in PlayerPrefs through the same debounced flusher as every other option). Screens are built once, so a change applies to the next screen that
    /// is built (the Settings page asks for a restart of the menu to see it everywhere). Tests and tools can force a theme with <see cref="Force"/>.
    /// </summary>
    public static class UiTheme
    {
        const string Key = "pix.uitheme";
        /// <summary>The theme used when the player never chose one. Stays Classic until every screen is rebuilt and verified (Docs/QA/glass_ui.md says "ALL SCREENS COMPLETE" when it can flip to Glass).</summary>
        public const UiThemeKind DefaultKind = UiThemeKind.Classic;
        static int _forced = -1, _stored = -1; static bool _cliRead;

        public static readonly string[] Names = { "Glass", "Classic" };

        /// <summary>Raised after the theme changed.</summary>
        public static event Action Changed;

        public static UiThemeKind Current
        {
            get
            {
                if (_forced >= 0) return (UiThemeKind)_forced;
                if (!_cliRead) { _cliRead = true; int c = FromCommandLine(); if (c >= 0) _forced = c; }
                if (_forced >= 0) return (UiThemeKind)_forced;
                if (_stored < 0) { int v = PlayerPrefs.GetInt(Key, (int)DefaultKind); _stored = v == (int)UiThemeKind.Classic ? (int)UiThemeKind.Classic : (int)UiThemeKind.Glass; }   // cached: the palette properties read this every frame
                return (UiThemeKind)_stored;
            }
            set
            {
                PlayerPrefs.SetInt(Key, (int)value); _stored = (int)value;
                PixSettings.Dirty();
                if (Changed != null) Changed();
            }
        }

        public static bool IsGlass { get { return Current == UiThemeKind.Glass; } }

        /// <summary>Tests and QA tools: overrides the stored theme without writing it (pass null to go back to the stored value).</summary>
        public static void Force(UiThemeKind? kind)
        {
            _forced = kind.HasValue ? (int)kind.Value : -1;
            if (Changed != null) Changed();
        }

        /// <summary>`-pixUiTheme glass|classic` on the command line overrides the stored theme for that run (QA, autotests, player smoke tests); -1 when absent.</summary>
        static int FromCommandLine()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-pixUiTheme") return string.Equals(a[i + 1], "classic", StringComparison.OrdinalIgnoreCase) ? (int)UiThemeKind.Classic : string.Equals(a[i + 1], "glass", StringComparison.OrdinalIgnoreCase) ? (int)UiThemeKind.Glass : -1;
            return -1;
        }

        /// <summary>Pure parse used by the option row and tests: index of <see cref="Names"/> to a theme.</summary>
        public static UiThemeKind FromIndex(int i) { return i == 1 ? UiThemeKind.Classic : UiThemeKind.Glass; }
    }
}
