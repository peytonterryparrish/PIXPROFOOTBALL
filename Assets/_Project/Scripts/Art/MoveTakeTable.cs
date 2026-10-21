using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Usable windows inside the long mocap takes (the BoNES takes run 15 to 40 seconds and repeat the move several times): for every window the clip, the trim range and the key
    /// moment (the spin's start and end) measured offline by ContactClipBuilder.BuildMoveTakes. The runtime plays only the window.
    /// </summary>
    public class MoveTakeTable : ScriptableObject
    {
        public const string ResourcesName = "PixMoveTakes";
        public const string EditorPath = "Assets/_Licensed/Resources/PixMoveTakes.asset";

        [Serializable]
        public class Take
        {
            public string key;         // semantic key (spin_move)
            public string clip;        // asset name
            public float start, end;   // trim window (seconds into the clip)
            public float t0, t1;       // the move itself (spin: first to last frame of the turn)
            public float turnDegrees;  // total pelvis rotation inside the move, signed (+ = clockwise seen from above)
        }

        public List<Take> takes = new List<Take>();
        static MoveTakeTable _inst; static bool _tried;

        public static MoveTakeTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<MoveTakeTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<MoveTakeTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        public void Of(string key, List<Take> into)
        {
            into.Clear();
            foreach (var t in takes) if (t != null && t.key == key) into.Add(t);
        }
    }
}
