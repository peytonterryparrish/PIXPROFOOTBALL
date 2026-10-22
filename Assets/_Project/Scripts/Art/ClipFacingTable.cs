using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// The direction every mocap clip faces when it is played on a match avatar, measured offline (ClipFacingAudit): the Motus locomotion cycles face +Z once the body carries the model yaw, but the Motus
    /// line stances face ~130 degrees away, the linebacker idles ~160, the BoNES studio / quarterback takes -90 and the Motus block takes 180. Without a correction those players "face backwards".
    /// <see cref="Offset"/> is the yaw (degrees) the body must be turned by while the clip plays so it faces the way the simulation says. Lives in Resources (licensed art, git-ignored).
    /// Rebuild it (PIX Football/Assets/Build Clip Facing) whenever clips are re-imported or retargeted: a stale table double-corrects.
    /// </summary>
    public class ClipFacingTable : ScriptableObject
    {
        public const string ResourcesName = "PixClipFacing";
        public const string EditorPath = "Assets/_Licensed/Resources/PixClipFacing.asset";
        /// <summary>Headings smaller than this are measurement noise (arm swing) and are not corrected.</summary>
        public const float MinCorrection = 10f;
        /// <summary>A take whose heading wanders more than this over its length (spins, turns) has no single facing and is left alone.</summary>
        public const float MaxSpread = 70f;

        [Serializable] public class Entry { public string clip; public float yaw; public float spread; public int handDown; public int footBack; public float upright; public float length; /** <summary>The mirror-image copy of this clip (left hand down instead of right ...), built by the audit for the stance clips.</summary> */ public AnimationClip mirror; }
        public List<Entry> entries = new List<Entry>();

        Dictionary<string, Entry> _map;
        static ClipFacingTable _inst; static bool _tried;

        public static ClipFacingTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<ClipFacingTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<ClipFacingTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        /// <summary>The mirrored copy of a clip (a stance with the other hand down / the other foot back), or null.</summary>
        public static AnimationClip MirrorOf(AnimationClip clip)
        {
            if (clip == null) return null;
            var t = Load(); Entry e;
            return t != null && t.TryGet(clip.name, out e) ? e.mirror : null;
        }

        public bool TryGet(string clipName, out Entry e)
        {
            if (_map == null)
            {
                _map = new Dictionary<string, Entry>();
                foreach (var en in entries) if (en != null && !string.IsNullOrEmpty(en.clip)) _map[en.clip] = en;
            }
            return _map.TryGetValue(clipName, out e);
        }

        static bool _warnedMissing, _warnedStale;

        /// <summary>Degrees to turn the body (added to the avatar yaw) while <paramref name="clip"/> plays; 0 when the clip already faces forward or has no stable facing.</summary>
        public static float Offset(AnimationClip clip)
        {
            if (clip == null) return 0f;
            var t = Load(); Entry e;
            if (t == null)
            {
                if (!_warnedMissing) { _warnedMissing = true; Debug.LogWarning("[ClipFacing] PixClipFacing table missing: mocap clips keep their authored facing (run PIX Football > Assets > Build Clip Facing). The runtime hips-heading loop still turns the players."); }
                return 0f;
            }
            if (!t.TryGet(clip.name, out e)) return 0f;
            // a clip that was re-imported / retargeted since the table was built (different length) is not trusted
            if (e.length > 0f && Mathf.Abs(e.length - clip.length) > 0.02f)
            {
                if (!_warnedStale) { _warnedStale = true; Debug.LogWarning("[ClipFacing] table is stale for '" + clip.name + "' (length changed): rebuild it with PIX Football > Assets > Build Clip Facing."); }
                return 0f;
            }
            return OffsetOf(e.yaw, e.spread);
        }

        public static float OffsetOf(float measuredYaw, float spread)
        {
            if (spread > MaxSpread || Mathf.Abs(Mathf.DeltaAngle(0f, measuredYaw)) < MinCorrection) return 0f;
            return -Mathf.DeltaAngle(0f, measuredYaw);
        }

        /// <summary>Circular mean of three angles in degrees.</summary>
        public static float MeanAngle(float a, float b, float c)
        {
            float x = Mathf.Cos(a * Mathf.Deg2Rad) + Mathf.Cos(b * Mathf.Deg2Rad) + Mathf.Cos(c * Mathf.Deg2Rad);
            float y = Mathf.Sin(a * Mathf.Deg2Rad) + Mathf.Sin(b * Mathf.Deg2Rad) + Mathf.Sin(c * Mathf.Deg2Rad);
            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }
    }
}
