using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 MOTION: the locomotion motion database. Every source frame (30 fps) of every selected clip is one row of normalised, weighted features; the matcher searches the rows for the one whose
    /// feet, foot velocities, root velocity and FUTURE TRAJECTORY (planned path at +0.33 / +0.66 / +1.0 s) are closest to what the simulation asks for. Lives in Resources (licensed art, git-ignored);
    /// without it the v0.9.0 gait synthesizer plays (<see cref="MotionMatcher.Wanted"/>).
    /// Feature layout (all in the clip's root frame at that frame: x right, z forward, metres of the reference body, seconds):
    ///   0..5   left / right ankle x, z and height above the clip's floor
    ///   6..9   left / right ankle velocity relative to the root (x, z)
    ///   10..11 root velocity (x, z)
    ///   12..17 root position at +0.33 / +0.66 / +1.0 s (x, z)
    ///   18..20 root yaw change at +0.33 / +0.66 / +1.0 s (degrees)
    /// </summary>
    public sealed class MotionDb : ScriptableObject
    {
        public const string ResourcesName = "PixMotionDb";
        public const string EditorPath = "Assets/_Licensed/Resources/PixMotionDb.asset";
        public const int Dim = 21;
        public const float Fps = 30f;
        public static readonly float[] Horizon = { 0.33f, 0.66f, 1.0f };

        [Flags] public enum Kind { Loop = 1, Start = 2, Stop = 4, Turn = 8, Strafe = 16, Back = 32, Idle = 64, Cut = 128 }

        [Serializable]
        public sealed class Clip
        {
            public string name;
            public AnimationClip clip;
            public int first, count;                // rows [first, first + count)
            public bool loop;
            public int kind;                        // Kind flags
            /// <summary>Mean ground speed of the clip in the reference body (m/s, the planted foot stands still at this speed) and the factor that was applied to its root motion.</summary>
            public float speed, speedScale = 1f;
            /// <summary>Travel direction in the root frame, degrees (+ = right), and root yaw rate (deg/s).</summary>
            public float angle, turnRate;
            public float stepsPerSec;
            public int mirrorOf = -1;
            /// <summary>Height of the lowest sole above the ground the retargeted take stands on (m, reference body): the whole pose is lowered by it so a planted foot is on the turf.</summary>
            public float floorY;
            /// <summary>The take is authored in the older convention (faces the way the match body faces with its model turned by ModelYaw): its pose is not turned back.</summary>
            public bool legacyConv;
        }

        public List<Clip> clips = new List<Clip>();
        /// <summary>Weighted, normalised features, Dim floats per row.</summary>
        public float[] feat = new float[0];
        /// <summary>Per row: ground velocity (x, z) in the root frame, root yaw rate (deg/s), planted-foot bits (1 = left, 2 = right), clip index.</summary>
        public float[] vel = new float[0];
        public float[] yawRate = new float[0];
        public byte[] contact = new byte[0];
        public int[] clipOf = new int[0];
        /// <summary>Normalisation: raw feature = feat / scale + mean (scale = weight / std).</summary>
        public float[] mean = new float[Dim], scale = new float[Dim];
        public float refLeg = 0.8535f;
        public int Rows { get { return clipOf.Length; } }

        static MotionDb _inst; static bool _tried;
        public static MotionDb Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<MotionDb>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<MotionDb>(ResourcesName);
            return _inst;
        }
        public static void ResetCache() { _inst = null; _tried = false; }

        /// <summary>Weighted, normalised copy of a raw feature vector.</summary>
        public void Normalise(float[] raw, float[] dst)
        {
            for (int i = 0; i < Dim; i++) dst[i] = (raw[i] - mean[i]) * scale[i];
        }

        /// <summary>Row after <paramref name="row"/> in the same clip (a looping clip wraps; the last row of a one-shot clip has none: -1).</summary>
        public int Next(int row)
        {
            var c = clips[clipOf[row]];
            if (row + 1 < c.first + c.count) return row + 1;
            return c.loop ? c.first : -1;
        }

        [NonSerialized] int[] _kinds;
        int[] Kinds()
        {
            if (_kinds != null && _kinds.Length == Rows) return _kinds;
            _kinds = new int[Rows]; for (int r = 0; r < _kinds.Length; r++) _kinds[r] = clips[clipOf[r]].kind;
            return _kinds;
        }

        [NonSerialized] float[] _gv;
        float[] Gv()
        {
            if (_gv != null && _gv.Length == Rows) return _gv;
            _gv = new float[Rows];
            for (int r = 0; r < _gv.Length; r++) { float gx = vel[r * 2], gz = vel[r * 2 + 1]; _gv[r] = Mathf.Sqrt(gx * gx + gz * gz); }
            return _gv;
        }

        /// <summary>Extra cost of a row whose ground speed (reference body, m/s) is further from the wanted speed than the stride warp may stretch it (more than +-limit).</summary>
        public float SpeedPenalty(int row, float wantSpeed, float limit, float k)
        {
            if (wantSpeed < 0.5f) return 0f;
            float g = Gv()[row];
            if (g < 0.9f) return 0f;                       // a start / stop / turn row: its speed is not a gait speed
            float r = wantSpeed / g;
            if (r <= 1f + limit && r * (1f + limit) >= 1f) return 0f;      // inside the band: no logarithm
            float e = Mathf.Abs(Mathf.Log(r)) - Mathf.Log(1f + limit);
            return e > 0f ? k * e * e : 0f;
        }

        /// <summary>Best row for a query (normalised, weighted features). <paramref name="skipFrom"/> &gt; 0 ignores the dimensions from 0 to skipFrom - 1 (a first search without pose information).</summary>
        public int Search(float[] q, float wantSpeed, float limit, float k, int skipFrom, int forbidKinds, out float best, int onlyClip = -1, float turnPenalty = 0f)
        {
            best = float.MaxValue; int bi = -1;
            int n = Rows; var f = feat; var kinds = Kinds(); var co = clipOf;
            for (int r = 0; r < n; r++)
            {
                if ((kinds[r] & forbidKinds) != 0 || (onlyClip >= 0 && co[r] != onlyClip)) continue;
                int o = r * Dim;
                float d = SpeedPenalty(r, wantSpeed, limit, k);
                if (turnPenalty > 0f && (kinds[r] & (int)Kind.Turn) != 0) d += turnPenalty;      // a turning take is only for a turning path
                if (d >= best) continue;
                int i = skipFrom;
                for (; i < Dim; i++) { float x = q[i] - f[o + i]; d += x * x; if (d >= best) break; }
                if (i < Dim) continue;
                best = d; bi = r;
            }
            return bi;
        }

        public float Distance(float[] q, int row)
        {
            float d = 0f; int o = row * Dim;
            for (int i = 0; i < Dim; i++) { float x = q[i] - feat[o + i]; d += x * x; }
            return d;
        }
    }
}
