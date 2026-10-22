using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Key moments inside mocap clips, measured offline by the editor tool (ClipTimingBuilder): the throw release (peak hand speed),
    /// the catch (hands closest together) and similar. Looked up by clip name. Lives in Resources next to PixRuntimeAssets.
    /// </summary>
    public class ClipTimingTable : ScriptableObject
    {
        public const string ResourcesName = "PixClipTiming";
        public const string EditorPath = "Assets/_Licensed/Resources/PixClipTiming.asset";

        [Serializable] public class Entry { public string clip; public float t; public float t2; public float d; }
        public List<Entry> entries = new List<Entry>();

        Dictionary<string, Entry> _map;
        static ClipTimingTable _inst; static bool _tried;

        public static ClipTimingTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<ClipTimingTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<ClipTimingTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        public bool TryGet(string clipName, out float t, out float t2)
        {
            if (_map == null)
            {
                _map = new Dictionary<string, Entry>();
                foreach (var e in entries) if (e != null && !string.IsNullOrEmpty(e.clip)) _map[e.clip] = e;
            }
            Entry en; t = 0f; t2 = 0f;
            if (_map.TryGetValue(clipName, out en)) { t = en.t; t2 = en.t2; return true; }
            return false;
        }

        /// <summary>The full entry of a clip (v0.7.0: t = key moment, t2 = second moment, d = travel distance in metres of the window t..t2), or false when it was not measured.</summary>
        public static bool TryEntry(AnimationClip clip, out Entry e)
        {
            e = null;
            var tb = Load();
            if (tb == null || clip == null) return false;
            float a, b; if (!tb.TryGet(clip.name, out a, out b)) return false;
            e = tb._map[clip.name];
            return true;
        }

        /// <summary>Key moment of a clip, or fraction * length when it was not measured.</summary>
        public static float Moment(AnimationClip clip, float fallbackFraction)
        {
            var tb = Load();
            float t, t2;
            if (tb != null && tb.TryGet(clip.name, out t, out t2) && t > 0f) return t;
            return clip.length * fallbackFraction;
        }
    }
}
