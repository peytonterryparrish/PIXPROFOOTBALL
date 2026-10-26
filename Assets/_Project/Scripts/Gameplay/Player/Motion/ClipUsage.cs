using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 COVERAGE: which animation clips the running game really plays (the owner counts clips used in a recorded game, not clips imported). Static and free while disabled (one bool test per site);
    /// switched on by <c>-pixClipUsage</c> or by the match-watch audit, which writes <c>clips_played.csv</c> from <see cref="Rows"/>.
    /// A "play" is one start of a clip (ActionAnimator slot, motion-matcher selection, officials clip, a gait cycle slot getting picked, an animator-state clip crossing 25 percent weight); seconds are
    /// the weighted time the clip was on screen. The system column names the calling class and method, taken from the call stack once per play (never per frame).
    /// </summary>
    public static class ClipUsage
    {
        public sealed class Entry
        {
            public AnimationClip Clip; public string System; public int Plays, FirstPlay; public float Seconds;
        }

        /// <summary>True while clips are being recorded.</summary>
        public static bool Enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixClipUsage") >= 0;
        /// <summary>The play number the audit is in (stamped into <see cref="Entry.FirstPlay"/> when a clip is recorded for the first time).</summary>
        public static int Marker;

        static readonly Dictionary<(AnimationClip, string), Entry> _rows = new Dictionary<(AnimationClip, string), Entry>();
        static readonly HashSet<AnimationClip> _distinct = new HashSet<AnimationClip>();

        public static IEnumerable<Entry> Rows { get { return _rows.Values; } }
        /// <summary>Number of different clips recorded (a clip played by two systems counts once).</summary>
        public static int DistinctClips { get { return _distinct.Count; } }

        public static void Reset() { _rows.Clear(); _distinct.Clear(); Marker = 0; }

        static Entry Get(AnimationClip clip, string system)
        {
            var key = (clip, system);
            Entry e;
            if (!_rows.TryGetValue(key, out e)) { _rows[key] = e = new Entry { Clip = clip, System = system, FirstPlay = Marker }; _distinct.Add(clip); }
            return e;
        }

        /// <summary>One start of <paramref name="clip"/> by <paramref name="system"/> (null = the calling class and method).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Note(AnimationClip clip, string system = null, float seconds = 0f)
        {
            if (!Enabled || clip == null) return;
            var e = Get(clip, system ?? Caller());
            e.Plays++; e.Seconds += seconds;
        }

        /// <summary>Time on screen of a clip that is already playing (per frame; does not count a play).</summary>
        public static void Hold(AnimationClip clip, string system, float seconds)
        {
            if (!Enabled || clip == null || seconds <= 0f) return;
            Get(clip, system).Seconds += seconds;
        }

        /// <summary>"Class.Method" of the first caller outside ActionAnimator / ClipUsage; lambdas and iterators are named after the method that holds them.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Caller()
        {
            try
            {
                var st = new StackTrace(2, false);
                for (int i = 0; i < st.FrameCount && i < 8; i++)
                {
                    var m = st.GetFrame(i).GetMethod();
                    var t = m != null ? m.DeclaringType : null;
                    if (t == null) continue;
                    while (t.DeclaringType != null && t.Name.IndexOf('<') >= 0) t = t.DeclaringType;
                    if (t == typeof(ClipUsage) || t == typeof(ActionAnimator)) continue;
                    string n = m.Name;
                    if (n.Length > 2 && n[0] == '<') { int e = n.IndexOf('>'); if (e > 1) n = n.Substring(1, e - 1); }
                    return t.Name + "." + n;
                }
            }
            catch (Exception) { }
            return "unknown";
        }
    }
}
