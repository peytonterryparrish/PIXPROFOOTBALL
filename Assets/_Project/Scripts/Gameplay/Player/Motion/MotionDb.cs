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
        /// <summary>QA / staging: -pixMmDb <asset path> loads (and the builder writes) another database next to the shipped one, so a private experiment never changes what the other tracks play.</summary>
        public static string AssetPath { get { var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, "-pixMmDb"); return i >= 0 && i + 1 < a.Length ? a[i + 1] : EditorPath; } }
        public const int Dim = 21;
        public const float Fps = 30f;
        public const int TransitionKinds = (int)Kind.Start | (int)Kind.Stop | (int)Kind.Turn | (int)Kind.Cut;
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
            /// <summary>v0.12.0: how a speed difference is shared between playback rate (cadence) and stride warp: rate = ratio^k, warp = the rest. 0.5 = both equally (the v0.11 split); a take with a long stride and a slow cadence (the MoCap Online runs: 1.6 m at 3.5 steps/s) takes the difference as cadence, up to the 4.5 steps/s of a sprinter, so its feet are not stretched.</summary>
            public float cadenceK = 0.5f;
            /// <summary>v0.13.0 NATURAL-2: quality prior of the take (cost added whenever it is a candidate; 0 = no penalty). Set by the builder from the per-take QA (-pixMmPriorFile): a take that is allowed but not preferred.</summary>
            public float prior;
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
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<MotionDb>(AssetPath);
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

        /// <summary>v0.13.0 NATURAL-3: the last <see cref="TailRows"/> rows of a take that does not close (a 100STYLE slice, a one-shot) are not candidates: a take that ends is searched again, and the best row of the same take was often one of its last three (the path features clamp at the end), so the take replayed its last three frames every 50 ms (stutter at 20 Hz; 467 jumps in 2,200 frames of one slice). Starts / stops / turns end where they end. QA: -pixMmNoTailBan.</summary>
        public static int TailRows = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixMmNoTailBan") >= 0 ? 0 : 8;
        [NonSerialized] bool[] _tail;
        bool[] Tail()
        {
            if (_tail != null && _tail.Length == Rows) return _tail;
            _tail = new bool[Rows];
            if (TailRows > 0)
                foreach (var c in clips)
                    if (!c.loop && (c.kind & TransitionKinds) == 0 && c.count > TailRows + 6)
                        for (int r = c.first + c.count - TailRows; r < c.first + c.count; r++) if (r >= 0 && r < _tail.Length) _tail[r] = true;
            return _tail;
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
        public float SpeedPenalty(int row, float wantSpeed, float limit, float k, float wantMax = 0f)
        {
            if (wantSpeed < 0.5f) return 0f;
            if ((Kinds()[row] & TransitionKinds) != 0) return 0f;          // a start / stop / cut changes its speed through the clip: the root velocity and path features decide, the rate follows the distance
            float g = clips[clipOf[row]].speed;      // v0.13.0 NATURAL-2: the speed of the TAKE, not of the row: rows of a slice that does not close dip and spike (a back-run slice at 1.2 m/s had rows that passed for 3-5 m/s and played a 5 m/s route in the air, 83 % flight); loops are constant, so nothing changes for them
            if (g < 0.9f) return 0f;                       // a start / stop / turn row: its speed is not a gait speed
            float r = wantSpeed / g;
            if (r > 1f && wantMax > wantSpeed) r = wantMax / g;      // v0.13.0 NATURAL-2: a take that is too slow is judged against the speed the planned path reaches (a body that accelerates leaves a slow take within a stride)
            if (r <= 1f + limit && r * (1f + limit) >= 1f) return 0f;      // inside the band: no logarithm
            float e = Mathf.Abs(Mathf.Log(r)) - Mathf.Log(1f + limit);
            float pen = e > 0f ? k * e * e : 0f;
            if (r > 1.6f) pen += 40f * (r - 1.6f) * (r - 1.6f);      // v0.13.0 NATURAL-2: a take that is more than 1.6 times too slow is out whatever the direction features say (at a hard cut the body faces away from its travel: a back-run slice at 1 m/s fit the direction of a 5 m/s route and played it in the air)
            if (r > 2.2f) pen += 3000f;      // and out for good beyond 2.2 times (a slice that walks cannot play a sprint)
            return pen;
        }

        /// <summary>Best row for a query (normalised, weighted features). <paramref name="skipFrom"/> &gt; 0 ignores the dimensions from 0 to skipFrom - 1 (a first search without pose information).</summary>
        public int Search(float[] q, float wantSpeed, float limit, float k, int skipFrom, int forbidKinds, out float best, int onlyClip = -1, float turnPenalty = 0f, float[] bias = null, int jitterSeed = 0, float wantMax = 0f, int wantContact = -1, float contactPen = 0f, int stayClip = -1)
        {
            best = float.MaxValue; int bi = -1;
            int n = Rows; var f = feat; var kinds = Kinds(); var co = clipOf; var tail = Tail();
            for (int r = 0; r < n; r++)
            {
                if ((kinds[r] & forbidKinds) != 0 || (onlyClip >= 0 && co[r] != onlyClip) || (tail[r] && onlyClip < 0)) continue;
                int o = r * Dim;
                float d = SpeedPenalty(r, wantSpeed, limit, k, wantMax);
                if (bias != null) d += bias[co[r]];      // v0.13.0 NATURAL-2: personal choice + recent use + quality prior per take (MotionStyle)
                if (jitterSeed != 0) d += RowJitter * RowJit(r, jitterSeed);      // v0.13.0 NATURAL-2: a first search without pose information (masked) ties between the rows of a loop: each player enters the cycle at a phase of his own
                if (turnPenalty > 0f && (kinds[r] & (int)Kind.Turn) != 0) d += turnPenalty;      // a turning take is only for a turning path
                if (wantContact >= 0 && contactPen > 0f && co[r] != stayClip) { int x = (contact[r] & 3) ^ wantContact; if (x != 0) d += contactPen * ((x & 1) + (x >> 1));      /* v0.13.0 NATURAL-3: contact-aware switching: a foot that is planted now stays planted in the new take, a foot that is in the air does not land at the switch (a landing foot while the inertializer still moves the pose is a planted foot that slides) */ }
                if (d >= best) continue;
                int i = skipFrom;
                for (; i < Dim; i++) { float x = q[i] - f[o + i]; d += x * x; if (d >= best) break; }
                if (i < Dim) continue;
                best = d; bi = r;
            }
            return bi;
        }

        /// <summary>Largest cost a row gets from the personal phase jitter of a masked first search (ties between the rows of one loop, a tenth of the cost of a 0.1 m/s velocity error).</summary>
        public const float RowJitter = 0.04f;
        static float RowJit(int r, int seed) { unchecked { uint h = (uint)r * 2654435761u ^ (uint)seed * 2246822519u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16; return (h & 0xFFFF) / 65535f; } }

        public float Distance(float[] q, int row)
        {
            float d = 0f; int o = row * Dim;
            for (int i = 0; i < Dim; i++) { float x = q[i] - feat[o + i]; d += x * x; }
            return d;
        }
    }
}
