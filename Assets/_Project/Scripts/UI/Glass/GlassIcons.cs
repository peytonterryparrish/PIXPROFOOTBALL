using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Registry of the glass icon sprites rasterised from the user's art collection (Tools/Glass/rasterize_icons.ps1 -> Assets/_Licensed/UI/Glass/Icons): keys "fb_*" (football line icons, Pack 6),
    /// "ui_*" (interface primitives, Pack 1), "sym_*" (sports symbols, Pack 2), "sc_*" (scouting glyphs recoloured to silver, Pack 5). Lives in Assets/_Licensed/Resources like PixRuntimeAssets
    /// (git-ignored with the rest of the licensed art); <see cref="Sprite"/> returns null when it is missing and every caller copes (icons are optional decoration).
    /// </summary>
    public class GlassIcons : ScriptableObject
    {
        public const string ResourcesName = "PixGlassIcons";
        public const string EditorPath = "Assets/_Licensed/Resources/PixGlassIcons.asset";

        [Serializable] public class Entry { public string key; public Sprite sprite; }
        public List<Entry> icons = new List<Entry>();

        static GlassIcons _inst; static bool _tried;
        Dictionary<string, Sprite> _map;

        public static GlassIcons Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<GlassIcons>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<GlassIcons>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        public static Sprite Get(string key)
        {
            var l = Load();
            return l != null ? l.Sprite(key) : null;
        }

        public Sprite Sprite(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in icons) if (e != null && e.sprite != null && !string.IsNullOrEmpty(e.key)) _map[e.key] = e.sprite;
            }
            Sprite s; return _map.TryGetValue(key, out s) ? s : null;
        }
    }
}

namespace PixFootball.UI.Glass
{
}
