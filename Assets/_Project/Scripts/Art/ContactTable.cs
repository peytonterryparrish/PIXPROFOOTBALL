using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Measured data of the authored two-actor clips (tackles, sacks, big hits, trucks, stiff-arms, spins, contested catches ...), built offline by ContactClipBuilder.
    /// Actor A is the first actor of the take (the ball carrier / quarterback / receiver: the offense) and actor B the second (the tackler / rusher / defender), exactly
    /// as the Draft Fever files name them. Both actors' samples are expressed in ONE shared authored frame (the mocap scene of the take, body facing +Z at yaw 0 for the view
    /// transform), so the pair meshes when both clips are placed with the same rigid transform. Per sample: object root (x, z), hips (x, z), pelvis heading (degrees).
    /// The root path is that of the Center-of-Mass import (the clip plays at the view origin); hips = root + the pose offset.
    /// </summary>
    public class ContactTable : ScriptableObject
    {
        public const string ResourcesName = "PixContact";
        public const string EditorPath = "Assets/_Licensed/Resources/PixContact.asset";
        public const int Stride = 5;   // per sample: root x, root z, hips x, hips z, pelvis yaw (degrees)

        [Serializable]
        public class Pair
        {
            public string key;            // ArtLibrary key of the A clip (the B clip is key + "_b")
            public string clipA, clipB;
            public float lengthA, lengthB;
            public float step;            // seconds between samples
            public float tContact;        // seconds: closest approach of the two hips (same clock for both actors)
            public float distance;        // metres between the hips at contact
            public float bearing;         // degrees: where B (the tackler) is seen from A (the carrier) at contact (0 = in front of A, +/-180 = behind him, + = to A's right)
            public float closingA;        // m/s: A's hips speed toward B just before contact (negative = running away)
            public float closingB;        // m/s: B's hips speed toward A just before contact
            public float speedA;          // m/s: A's hips speed just before contact (a running carrier is fast)
            public float lowB;            // 0..1: how low B's hips are at contact (a dive / shoestring is near 1)
            public float lowA;
            public float getUpA, getUpB;  // seconds into the take when the actor starts to rise from the ground (0 = never lies down in the take)
            public float faceUpA, faceUpB; // +1 lying face up .. -1 lying face down (0 = does not lie down)
            public float[] a, b;          // samples, Stride floats each (different lengths: B often keeps going longer)

            public int CountA { get { return a != null ? a.Length / Stride : 0; } }
            public int CountB { get { return b != null ? b.Length / Stride : 0; } }
            public float Length { get { return Mathf.Max(lengthA, lengthB); } }
        }

        public List<Pair> pairs = new List<Pair>();
        Dictionary<string, Pair> _byA;
        static ContactTable _inst; static bool _tried;

        public static ContactTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<ContactTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<ContactTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        /// <summary>The pair whose A clip has this asset name.</summary>
        public Pair ByClipA(string clipA)
        {
            if (_byA == null)
            {
                _byA = new Dictionary<string, Pair>(StringComparer.Ordinal);
                foreach (var p in pairs) if (p != null && !string.IsNullOrEmpty(p.clipA) && !_byA.ContainsKey(p.clipA)) _byA[p.clipA] = p;
            }
            Pair r; return _byA.TryGetValue(clipA, out r) ? r : null;
        }

        /// <summary>Pairs of one key.</summary>
        public void OfKey(string key, List<Pair> into)
        {
            into.Clear();
            foreach (var p in pairs) if (p != null && p.key == key) into.Add(p);
        }
    }
}
