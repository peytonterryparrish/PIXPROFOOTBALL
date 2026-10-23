using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// v0.10.0 CHOREO: measured root motion and key moments of every action clip of the ArtLibrary, built offline by <c>ChoreoClipBuilder</c>.
    /// The clips play in place on the match avatar (Center-of-Mass import, root motion off); the root path stored here is the travel the take WANTS (the Center-of-Mass path of the
    /// authored take, expressed in the clip's own frame: starts at the origin, the body faces +Z at yaw 0), so the simulation can move the agent along it and nothing floats.
    /// Per sample (Stride floats): root x, root z, hips x, hips z, pelvis yaw (degrees), hips height (m). Lookup by clip asset name.
    /// </summary>
    public class ChoreoTable : ScriptableObject
    {
        public const string ResourcesName = "PixChoreo";
        public const string EditorPath = "Assets/_Licensed/Resources/PixChoreo.asset";
        public const int Stride = 6;

        [Serializable]
        public class Entry
        {
            public string clip;
            public float length;
            public float step;
            /// <summary>Key moment of the take (catch: the hands close; hand-off: the exchange; block: the hands meet), 0 when unknown.</summary>
            public float contactT;
            /// <summary>Seconds into the take when the body is upright and moving on its own again (after a fall: the get-up is done; a catch: the hands are secure). The take can hand back to locomotion from here.</summary>
            public float settleT;
            /// <summary>Hand mid-point at <see cref="contactT"/> relative to the hips, in the hips frame: x = to the right, y = up, z = forward (metres). Zero when not measured.</summary>
            public Vector3 hand;
            /// <summary>Lowest hips height of the take (a fall / dive is low).</summary>
            public float minHips;
            /// <summary>+1: the left foot is the forward / planted one at the settle moment, -1 the right one, 0 unknown (gait hand-back).</summary>
            public float settleLead;
            public float[] s;
            /// <summary>Per sample: the factor that scales the take's travel so the body moves at the speed its planted feet allow (the root of mocap takes overstates the planted foot by 10 to 50 percent); 1 = leave. Empty when not measured.</summary>
            public float[] k;

            public int Count { get { return s != null ? s.Length / Stride : 0; } }
        }

        public List<Entry> clips = new List<Entry>();
        Dictionary<string, Entry> _map;
        static ChoreoTable _inst; static bool _tried;

        public static ChoreoTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<ChoreoTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<ChoreoTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        public Entry Get(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (var e in clips) if (e != null && !string.IsNullOrEmpty(e.clip) && !_map.ContainsKey(e.clip)) _map[e.clip] = e;
            }
            Entry r; return _map.TryGetValue(clipName, out r) ? r : null;
        }

        /// <summary>Root displacement of a take between two clip times in the clip frame (metres, x to the right, z forward). Zero when the clip is not in the table.</summary>
        public Vector2 RootDelta(string clipName, float t0, float t1)
        {
            var e = Get(clipName);
            if (e == null || e.Count < 2) return Vector2.zero;
            return RootAt(e, t1) - RootAt(e, t0);
        }

        /// <summary>Root displacement between two clip times with the travel scaled to the planted feet (see <see cref="Entry.k"/>); equals <see cref="RootDelta"/> when no factors were measured.</summary>
        public Vector2 RootDeltaScaled(string clipName, float t0, float t1)
        {
            var e = Get(clipName);
            if (e == null || e.Count < 2) return Vector2.zero;
            if (e.k == null || e.k.Length < e.Count) return RootAt(e, t1) - RootAt(e, t0);
            return ScaledAt(e, t1) - ScaledAt(e, t0);
        }

        static Vector2 ScaledAt(Entry e, float t)
        {
            int n = e.Count; float f = Mathf.Clamp(t / Mathf.Max(0.001f, e.step), 0f, n - 1); int i = Mathf.Min((int)f, n - 1);
            Vector2 acc = Vector2.zero;
            for (int j = 0; j < i; j++) acc += (new Vector2(e.s[(j + 1) * Stride], e.s[(j + 1) * Stride + 1]) - new Vector2(e.s[j * Stride], e.s[j * Stride + 1])) * (0.5f * (e.k[j] + e.k[j + 1]));
            if (i < n - 1) { float u = f - i; acc += (new Vector2(e.s[(i + 1) * Stride], e.s[(i + 1) * Stride + 1]) - new Vector2(e.s[i * Stride], e.s[i * Stride + 1])) * (0.5f * (e.k[i] + e.k[i + 1])) * u; }
            return acc;
        }

        /// <summary>Pelvis heading of the take at clip time t (degrees, measured in the same frame as the root path).</summary>
        public static float YawAt(Entry e, float t)
        {
            int n = e.Count; if (n == 0) return 0f;
            float f = Mathf.Max(0f, t / Mathf.Max(0.001f, e.step));
            int i = Mathf.Min((int)f, n - 1), j = Mathf.Min(i + 1, n - 1);
            float u = f - i;
            float a = e.s[i * Stride + 4], b = e.s[j * Stride + 4];
            return a + Mathf.DeltaAngle(a, b) * u;
        }

        public static Vector2 RootAt(Entry e, float t)
        {
            int n = e.Count; if (n == 0) return Vector2.zero;
            float f = Mathf.Max(0f, t / Mathf.Max(0.001f, e.step));
            int i = Mathf.Min((int)f, n - 1), j = Mathf.Min(i + 1, n - 1);
            float u = f - i;
            return new Vector2(Mathf.Lerp(e.s[i * Stride], e.s[j * Stride], u), Mathf.Lerp(e.s[i * Stride + 1], e.s[j * Stride + 1], u));
        }
    }
}
