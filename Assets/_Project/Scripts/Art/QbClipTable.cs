using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>v0.13.0 QB-MOCAP: the families of the quarterback mocap catalog (<see cref="QbClipTable"/>). One family = one slot of the quarterback's sequence.</summary>
    public static class QbFam
    {
        /// <summary>Loop windows: the shotgun ready stance, the under-centre stance, the pocket (ball at the chest, scanning).</summary>
        public const string StanceSg = "stance_sg", StanceUc = "stance_uc", Pocket = "pocket";
        /// <summary>Whole-clip cadences (the count), shotgun and under centre.</summary>
        public const string CadenceSg = "cadence_sg", CadenceUc = "cadence_uc";
        /// <summary>The snap arriving in his hands (sync = the moment the hands close on the ball): shotgun, under centre (the drop clips of the under-centre takes are <see cref="DropUc"/>).</summary>
        public const string SnapSg = "snap_sg", SnapUc = "snap_uc";
        /// <summary>Drops: the under-centre catch-and-drop takes (distance-matched), the shotgun gather.</summary>
        public const string DropUc = "drop_uc", DropSg = "drop_sg";
        /// <summary>Throws with the feet set (sync = the release): bullet, touch, lob, sidearm, jump, off balance, shovel, hail mary, falling, on the run, pump fake.</summary>
        public const string ThrowSet = "throw_set", ThrowRun = "throw_run", ThrowOff = "throw_off", ThrowFall = "throw_fall", ThrowShovel = "throw_shovel", Pump = "pump";
        /// <summary>The rest: play fake, scramble start, roll / sidestep, slide, kneel, spike, sneak, celebration / reaction.</summary>
        public const string Fake = "fake", Scramble = "scramble", Sidestep = "sidestep", Slide = "slide", Kneel = "kneel", Spike = "spike", Sneak = "sneak", Celebrate = "celebrate", React = "react";
        /// <summary>v0.13.0 QB-2: the fitted drop takes (time-warped and distance-fitted to the simulated drop at run time): the backpedal at speed, the settle (the brake and the plant), the two in one (a drop into a stop); the hand-off ride, the roll-out / scramble start, the sneak push.</summary>
        public const string DropWalk = "drop_walk", DropSettle = "drop_settle", DropStop = "drop_stop", Handoff = "handoff_qb", Roll = "roll";
    }

    /// <summary>
    /// v0.13.0 QB-MOCAP: the catalog of the quarterback's mocap windows, measured offline by the editor tool <c>QbClipCatalog</c> and looked up by family at run time. A row is a WINDOW of a clip (the long BoNES takes are cut at
    /// 10 s pieces of a continuous recording: the quarterback walks, turns and throws inside one clip), with the key moment inside it (release, catch), the facing of the chest at its start and at the moment,
    /// the throwing hand, the travel and the foot slide of the window as the game shows it (root motion is discarded in the match). Lives in Resources next to PixClipTags (licensed art, git-ignored).
    /// </summary>
    public class QbClipTable : ScriptableObject
    {
        /// <summary>The most a window moves the displayed quarterback away from the simulated one (m), and the rate of its path samples (Hz).</summary>
        public const float PathCap = 0.40f, PathRate = 15f;
        public const string ResourcesName = "PixQbClips";
        public const string EditorPath = "Assets/_Licensed/Resources/PixQbClips.asset";

        [Serializable]
        public class Entry
        {
            public string clip;          // AnimationClip name (ArtLibrary.ClipByName)
            public string family;        // QbFam.*
            public string kind;          // free tag inside the family: bullet / touch / lob / sidearm / jump / off / shovel / hail / fall / away / pump / run ...
            public float a, b;           // the window (seconds into the clip)
            public float sync;           // key moment: release / catch / secure / exchange (clip time); 0 = none
            public char hand;            // the throwing hand R / L, '?' = none
            public float yaw0;           // chest yaw (deg, game frame) at the start of the window
            public float yawSync;        // chest yaw at the key moment
            public float handYaw;        // heading of the throwing hand at the key moment
            public float dist;           // hips travel over the window (m)
            public float travelDir;      // heading of that travel relative to the chest at the start (deg)
            public float slide;          // planted-foot slide of the window, body frame (m/s), mean
            public float hips, lean, handsY;   // pose at the start (hips height, trunk lean in degrees, hands height)
            public float score;          // QA score of the clip (0..100)
            public bool loop;            // the window closes on itself (loop it)
            public string mirror;        // clip name of the mirror image for a left-hander ("" = none)
            public float quality;        // 0..100: the catalog's own verdict of the window (foot slide, facing, hands, seam)
            public AnimationClip clipRef;   // the clip itself for a mirror-image row (the twin is not in the ArtLibrary); null = ArtLibrary.ClipByName(clip)
            public string mirrorOf;      // name of the source clip of a mirror-image row ("" = a source row)
            public float[] path;         // hips path of the window (x, z pairs every 1 / PathRate s from the start, relative to the hips there, authored frame): the match discards root motion, the view re-applies it (SimAgentView.QbMocap)
            public float side;           // v0.13.0 QB-2: lateral position of the hands at the key moment (m, + = the passer's right), for the fake hand-off
            public float mvA, mvB;       // v0.13.0 QB-2: the travel segment of a fitted drop (clip seconds): the first step after the catch to the plant

            /// <summary>Hips position (xz, authored frame, relative to the hips at the start of the window) at clip time <paramref name="t"/> (seconds into the clip); zero when the row has no path.</summary>
            public Vector2 PathAt(float t)
            {
                if (path == null || path.Length < 4) return Vector2.zero;
                int n = path.Length / 2; float u = Mathf.Clamp((t - a) * PathRate, 0f, n - 1); int i = Mathf.Min((int)u, n - 2); float f = u - i;
                return new Vector2(Mathf.Lerp(path[2 * i], path[2 * i + 2], f), Mathf.Lerp(path[2 * i + 1], path[2 * i + 3], f));
            }

            /// <summary>Straight-line distance (m) the hips cover between two clip times.</summary>
            public float PathLen(float t0, float t1) { return (PathAt(t1) - PathAt(t0)).magnitude; }

            /// <summary>The first clip time at or after <paramref name="t0"/> where the hips are <paramref name="dist"/> metres from where they were at t0 (the end of the window when it never gets there).</summary>
            public float TimeAtDist(float t0, float dist)
            {
                if (path == null || path.Length < 4) return b;
                Vector2 p0 = PathAt(t0); float prev = t0, dprev = 0f;
                for (float t = t0 + 1f / PathRate; t <= b + 1e-4f; t += 1f / PathRate)
                {
                    float d = (PathAt(t) - p0).magnitude;
                    if (d >= dist) return d > dprev + 1e-5f ? Mathf.Lerp(prev, t, (dist - dprev) / (d - dprev)) : t;
                    prev = t; dprev = d;
                }
                return b;
            }

            /// <summary>The number of drop steps named in the clip (3 / 5 / 7), 0 when the name has none.</summary>
            public int Steps { get { var m = System.Text.RegularExpressions.Regex.Match(clip, @"(?<![0-9])([1357])_?step", System.Text.RegularExpressions.RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value[0] - (char)48 : 0; } }
        }

        public List<Entry> entries = new List<Entry>();

        Dictionary<string, List<Entry>> _byFamily;
        static QbClipTable _inst; static bool _tried;

        public static QbClipTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<QbClipTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<QbClipTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        static readonly List<Entry> NoEntries = new List<Entry>();

        /// <summary>All rows of a family (never null).</summary>
        public List<Entry> Family(string family)
        {
            if (_byFamily == null)
            {
                _byFamily = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
                foreach (var e in entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.clip)) continue;
                    List<Entry> l; if (!_byFamily.TryGetValue(e.family, out l)) _byFamily[e.family] = l = new List<Entry>();
                    l.Add(e);
                }
            }
            List<Entry> r; return _byFamily.TryGetValue(family, out r) ? r : NoEntries;
        }

        public int Count(string family) { return Family(family).Count; }
    }
}
