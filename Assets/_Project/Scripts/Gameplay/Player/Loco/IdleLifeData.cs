using System;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 NATURAL-2: the micro-motion of real standing mocap, above the pelvis, as small per-frame rotation offsets: MoCap Online Mobility Pro "Stand_Relaxed_Idle", "Idle_v2" and the four "Fgt" fidget takes,
    /// sampled on the match body (<c>IdleLifeBuilder</c>) and stored as the turn of every bone against its own mean over the take, in degrees (a rotation vector in the bone's local frame). <see cref="IdleLife"/> plays them
    /// back on standing bodies, a different take, speed and amplitude for every player. Derived from licensed motion: lives in Resources under _Licensed (git-ignored); without it the procedural rhythms of IdleLife play.
    /// </summary>
    public sealed class IdleLifeData : ScriptableObject
    {
        public const string ResourcesName = "PixIdleLife";
        public const string EditorPath = "Assets/_Licensed/Resources/PixIdleLife.asset";
        /// <summary>Bones in the order of the per-frame triples: spine, chest, head, left / right shoulder, left / right upper arm, left / right forearm, left / right hand.</summary>
        public const int Bones = 11;

        [Serializable]
        public sealed class Take
        {
            public string name;
            public float fps = 30f;
            public int frames;
            /// <summary>frames x Bones x 3 (degrees).</summary>
            public float[] delta = new float[0];
            /// <summary>Largest turn (degrees) of any bone in the take: the amplitude the player's scale is applied to.</summary>
            public float peak;
            /// <summary>RMS turn (degrees) over all bones and frames: the playback scales every take to the same energy.</summary>
            public float rms;
        }

        public Take[] takes = new Take[0];

        static IdleLifeData _inst; static bool _tried;
        public static IdleLifeData Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<IdleLifeData>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<IdleLifeData>(ResourcesName);
            return _inst;
        }

        /// <summary>Ping-pong position of a take at <paramref name="t"/> seconds (forward, backward, forward ...: no seam): frame position in 0..frames-1.</summary>
        public static float PingPong(float t, float fps, int frames)
        {
            if (frames < 2) return 0f;
            float len = (frames - 1) / fps, u = Mathf.Repeat(t, 2f * len);
            if (u > len) u = 2f * len - u;
            return u * fps;
        }

        /// <summary>Offset (degrees, rotation vector) of bone <paramref name="bone"/> at frame position <paramref name="pos"/>, linear between the two frames.</summary>
        public Vector3 Sample(Take tk, float pos, int bone)
        {
            int f0 = Mathf.Clamp((int)pos, 0, tk.frames - 1), f1 = Mathf.Min(f0 + 1, tk.frames - 1); float u = pos - f0;
            int a = (f0 * Bones + bone) * 3, b = (f1 * Bones + bone) * 3;
            return new Vector3(Mathf.Lerp(tk.delta[a], tk.delta[b], u), Mathf.Lerp(tk.delta[a + 1], tk.delta[b + 1], u), Mathf.Lerp(tk.delta[a + 2], tk.delta[b + 2], u));
        }
    }
}
