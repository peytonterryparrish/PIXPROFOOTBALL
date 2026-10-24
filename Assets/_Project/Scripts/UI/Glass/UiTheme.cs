using System;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.UI
{
    /// <summary>
    /// The three UI looks: Glass (v0.7 frosted design system), Classic (the flat kit that shipped before it) and Tile (v0.11 Direction 16 "Tile Hub Modern": flat charcoal tiles, gold focus, Barlow).
    /// The numeric values are stored in PlayerPrefs, so Glass = 0 and Classic = 1 never change. Tile belongs to the glass family: <see cref="UiTheme.IsGlass"/> is true for it and the glass kit draws the tile look.
    /// </summary>
    public enum UiThemeKind { Glass = 0, Classic = 1, Tile = 2 }

    /// <summary>
    /// The UI theme setting ("pix.uitheme", stored in PlayerPrefs through the same debounced flusher as every other option). Screens are built once, so a change applies to the next screen that
    /// is built (the Settings page asks for a restart of the menu to see it everywhere). Tests and tools can force a theme with <see cref="Force"/>.
    /// </summary>
    public static class UiTheme
    {
        const string Key = "pix.uitheme";
        /// <summary>The theme used when the player never chose one. v0.11: Tile (Direction 16). Glass and Classic stay available in Settings and with -pixUiTheme glass|classic.</summary>
        public const UiThemeKind DefaultKind = UiThemeKind.Tile;
        static int _forced = -1, _stored = -1; static bool _cliRead;

        /// <summary>Option row labels; the index IS the <see cref="UiThemeKind"/> value (SettingsPanel passes (int)Current and FromIndex(i)).</summary>
        public static readonly string[] Names = { "Glass", "Classic", "Tile" };

        /// <summary>Raised after the theme changed.</summary>
        public static event Action Changed;

        public static UiThemeKind Current
        {
            get
            {
                if (_forced >= 0) return (UiThemeKind)_forced;
                if (!_cliRead) { _cliRead = true; int c = FromCommandLine(); if (c >= 0) _forced = c; }
                if (_forced >= 0) return (UiThemeKind)_forced;
                if (_stored < 0) { int v = PlayerPrefs.GetInt(Key, (int)DefaultKind); _stored = Sanitize(v); }   // cached: the palette properties read this every frame
                return (UiThemeKind)_stored;
            }
            set
            {
                PlayerPrefs.SetInt(Key, (int)value); _stored = (int)value;
                PixSettings.Dirty();
                if (Changed != null) Changed();
            }
        }

        /// <summary>True for the glass family: Glass and Tile (both are drawn by the glass kit, so every "is the glass kit active" check keeps working in Tile).</summary>
        public static bool IsGlass { get { var c = Current; return c == UiThemeKind.Glass || c == UiThemeKind.Tile; } }
        /// <summary>True only for the Tile (Direction 16) look: flat surfaces, gold focus, Barlow, no blur.</summary>
        public static bool IsTile { get { return Current == UiThemeKind.Tile; } }

        /// <summary>Tests and QA tools: overrides the stored theme without writing it (pass null to go back to the stored value).</summary>
        public static void Force(UiThemeKind? kind)
        {
            _forced = kind.HasValue ? (int)kind.Value : -1;
            if (Changed != null) Changed();
        }

        /// <summary>`-pixUiTheme tile|glass|classic` on the command line overrides the stored theme for that run (QA, autotests, player smoke tests); -1 when absent.</summary>
        static int FromCommandLine()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-pixUiTheme") return ParseName(a[i + 1]);
            return -1;
        }

        /// <summary>Pure parse of a theme name ("tile", "glass", "classic", any case) to a <see cref="UiThemeKind"/> value, -1 when unknown.</summary>
        public static int ParseName(string s)
        {
            if (string.Equals(s, "classic", StringComparison.OrdinalIgnoreCase)) return (int)UiThemeKind.Classic;
            if (string.Equals(s, "glass", StringComparison.OrdinalIgnoreCase)) return (int)UiThemeKind.Glass;
            if (string.Equals(s, "tile", StringComparison.OrdinalIgnoreCase)) return (int)UiThemeKind.Tile;
            return -1;
        }

        static int Sanitize(int v) { return v == (int)UiThemeKind.Classic ? (int)UiThemeKind.Classic : v == (int)UiThemeKind.Tile ? (int)UiThemeKind.Tile : (int)UiThemeKind.Glass; }

        /// <summary>Pure parse used by the option row and tests: index of <see cref="Names"/> to a theme (unknown indexes give Glass).</summary>
        public static UiThemeKind FromIndex(int i) { return (UiThemeKind)Sanitize(i); }
    }
}
