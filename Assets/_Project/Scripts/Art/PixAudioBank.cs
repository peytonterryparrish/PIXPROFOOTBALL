using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD: every curated clip of the game (music, stingers, on-field SFX, crowd beds and reactions) in named groups, plus a per-group trim in dB.
    /// Lives at Assets/_Licensed/Resources/PixAudioBank.asset (git-ignored with the licensed audio, written by <c>AudioBankBuilder</c>), so a build carries exactly these clips.
    /// Group keys are the folder names (hit_heavy, bed_murmur_mid, ...); "music" and "stinger" hold the music cues by file name.
    /// </summary>
    public sealed class PixAudioBank : ScriptableObject
    {
        public const string ResourcesName = "PixAudioBank";
        public const string EditorPath = "Assets/_Licensed/Resources/PixAudioBank.asset";

        [Serializable] public sealed class Group { public string key; public float gainDb; public List<AudioClip> clips = new List<AudioClip>(); }
        public List<Group> groups = new List<Group>();

        Dictionary<string, Group> _map;
        Dictionary<string, AudioClip> _byName;
        readonly Dictionary<string, int> _last = new Dictionary<string, int>();

        static PixAudioBank _inst; static bool _tried;

        public static PixAudioBank Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<PixAudioBank>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<PixAudioBank>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        void Index()
        {
            if (_map != null) return;
            _map = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            _byName = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (g == null || string.IsNullOrEmpty(g.key)) continue;
                _map[g.key] = g;
                foreach (var c in g.clips) if (c != null) _byName[c.name] = c;
            }
        }

        public Group Get(string key) { Index(); Group g; return key != null && _map.TryGetValue(key, out g) ? g : null; }
        public bool Has(string key) { var g = Get(key); return g != null && g.clips.Count > 0; }
        public int Count(string key) { var g = Get(key); return g == null ? 0 : g.clips.Count; }
        public AudioClip Named(string clipName) { Index(); AudioClip c; return clipName != null && _byName.TryGetValue(clipName, out c) ? c : null; }

        /// <summary>Linear gain of the group trim (1 when the group is unknown).</summary>
        public float Gain(string key) { var g = Get(key); return g == null ? 1f : Mathf.Pow(10f, g.gainDb / 20f); }

        /// <summary>A random clip of the group that is not the one handed out last (when there are two or more).</summary>
        public AudioClip Pick(string key)
        {
            var g = Get(key);
            if (g == null || g.clips.Count == 0) return null;
            int n = g.clips.Count, i = n == 1 ? 0 : UnityEngine.Random.Range(0, n);
            int last; if (n > 1 && _last.TryGetValue(key, out last) && i == last) i = (i + 1 + UnityEngine.Random.Range(0, n - 1)) % n;
            _last[key] = i;
            return g.clips[i];
        }
    }
}
