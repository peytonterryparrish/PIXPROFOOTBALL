using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// v0.10.0 ANIMLIB: one tag row per library clip, measured offline by the editor tool AnimLibTags and looked up by clip name: the semantic key and category, the positions that
    /// use it, which foot is planted when it starts and ends (and the gait phase), the sync moment (release, catch, contact, kick, snap ...), duration, root speed and direction
    /// and the QA score. The CHOREO and QB tracks query it through <c>PixFootball.Gameplay.ClipSelect</c>. Lives in Resources next to PixClipTiming.
    /// </summary>
    public class ClipTagTable : ScriptableObject
    {
        public const string ResourcesName = "PixClipTags";
        public const string EditorPath = "Assets/_Licensed/Resources/PixClipTags.asset";

        [Serializable]
        public class Entry
        {
            public string clip;
            public string key;           // primary semantic key (ArtLibrary)
            public string keys;          // all keys, '+' separated
            public string cat;           // QB RB WR TE OL DL LB DB KP LS REF celebration reaction tackle block catch transition sideline huddle walk locomotion stance ...
            public string roles;         // positions that use the clip: QB,RB,WR,TE,OL,DL,LB,DB,K,P,LS,REF,ALL (comma separated)
            public float len;            // seconds
            public bool loop;
            public char entryFoot, exitFoot;   // L / R planted foot, B both, A airborne
            public float entryPhase, exitPhase; // gait phase 0..1 (0 = left foot strike), -1 = not a stepping clip
            public float syncT;          // seconds into the clip of the moment the action is about (0 = none)
            public string syncKind;      // release / catch / contact / kick / snap / strike / plant / exchange / none
            public float rootSpeed;      // m/s, mean horizontal speed of the hips
            public float gaitSpeed;      // m/s, speed of the planted feet relative to the body: the ground speed the steps are made for (use it to size the playback rate of an in-place or short-travel clip)
            public float rootDir;        // degrees from the facing at the start (+ = to the right), valid when travel > 0.3 m
            public float travel;         // metres, hips net travel
            public float turn;           // degrees the body heading changes over the clip
            public float score;          // QA score 0..100
            public bool reject;
            public string twin;          // paired take (the other actor), or empty
        }

        public List<Entry> entries = new List<Entry>();

        Dictionary<string, Entry> _byClip;
        Dictionary<string, List<Entry>> _byKey;
        static ClipTagTable _inst; static bool _tried;

        public static ClipTagTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<ClipTagTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<ClipTagTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        void Index()
        {
            if (_byClip != null) return;
            _byClip = new Dictionary<string, Entry>(StringComparer.Ordinal);
            _byKey = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.clip)) continue;
                _byClip[e.clip] = e;
                if (string.IsNullOrEmpty(e.keys)) continue;
                foreach (string k in e.keys.Split('+'))
                {
                    List<Entry> l; if (!_byKey.TryGetValue(k, out l)) _byKey[k] = l = new List<Entry>();
                    l.Add(e);
                }
            }
        }

        public bool TryGet(string clipName, out Entry e) { Index(); return _byClip.TryGetValue(clipName, out e); }

        /// <summary>All rows registered under a key (any of the clip's keys).</summary>
        public IReadOnlyList<Entry> OfKey(string key)
        {
            Index(); List<Entry> l; return _byKey.TryGetValue(key, out l) ? (IReadOnlyList<Entry>)l : Array.Empty<Entry>();
        }
    }
}
