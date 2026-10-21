using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Presentation
{
    /// <summary>
    /// Smallest-three quaternion packing into 32 bits (2 bits for the largest component's index, 10 bits for each of the other three). About 0.16 degrees of error: a bone costs 4 bytes per sample.
    /// </summary>
    public static class QuatPack
    {
        const float Range = 0.70710678f;

        public static uint Pack(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float n = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (n < 1e-6f) return Pack(0, 0f, 0f, 0f, 0f);
            x /= n; y /= n; z /= n; w /= n;
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y), az = Mathf.Abs(z), aw = Mathf.Abs(w);
            int idx = 0; float best = ax;
            if (ay > best) { best = ay; idx = 1; }
            if (az > best) { best = az; idx = 2; }
            if (aw > best) { best = aw; idx = 3; }
            float sign = (idx == 0 ? x : idx == 1 ? y : idx == 2 ? z : w) < 0f ? -1f : 1f;
            x *= sign; y *= sign; z *= sign; w *= sign;
            float a, b, c;
            switch (idx)
            {
                case 0: a = y; b = z; c = w; break;
                case 1: a = x; b = z; c = w; break;
                case 2: a = x; b = y; c = w; break;
                default: a = x; b = y; c = z; break;
            }
            return Pack(idx, a, b, c, 0f);
        }

        static uint Pack(int idx, float a, float b, float c, float unused)
        {
            return ((uint)idx << 30) | (Q(a) << 20) | (Q(b) << 10) | Q(c);
        }

        static uint Q(float v)
        {
            float t = Mathf.Clamp01(v / Range * 0.5f + 0.5f);
            return (uint)Mathf.RoundToInt(t * 1023f);
        }

        static float D(uint bits) { return ((bits & 1023u) / 1023f - 0.5f) * 2f * Range; }

        public static Quaternion Unpack(uint v)
        {
            int idx = (int)(v >> 30);
            float a = D(v >> 20), b = D(v >> 10), c = D(v);
            float m = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));
            switch (idx)
            {
                case 0: return new Quaternion(m, a, b, c);
                case 1: return new Quaternion(a, m, b, c);
                case 2: return new Quaternion(a, b, m, c);
                default: return new Quaternion(a, b, c, m);
            }
        }
    }

    /// <summary>Per body size: how many animated transforms the recorder stores and which of them also store a local position (the hips and the bones directly under the model root).</summary>
    public sealed class ReplayLayout
    {
        public int BoneCount;
        public int[] PosBones = new int[0];
    }

    public struct ReplayAgent
    {
        /// <summary>0 small, 1 medium, 2 large (matches <c>PlayerSize</c>).</summary>
        public byte Size;
        /// <summary>0 offense, 1 defense.</summary>
        public byte Side;
        public byte Slot;
        public string Position;
        public string Name;
        public int Number;
    }

    /// <summary>One recorded sample (30 Hz) of the 22 players and the ball.</summary>
    public sealed class ReplayFrame
    {
        public float T;
        public Vector3[] Pos = new Vector3[22];
        public float[] Yaw = new float[22];
        public uint[] Rot;
        public Vector3[] BonePos;
        public Vector3 BallPos;
        public uint BallRot;
        public bool BallVisible;

        public ReplayFrame(int rotCount, int bonePosCount) { Rot = new uint[rotCount]; BonePos = new Vector3[bonePosCount]; }
    }

    /// <summary>
    /// One recorded play: the 22 players' view transforms (root pose plus every animated bone) and the ball at 30 Hz, from about a second before the snap until the play was over,
    /// with the facts the replay needs (kits, who is who, the moment of interest, the situation text). Pure data: playback sampling lives here, applying it to bodies in <see cref="ReplayPlayer"/>.
    /// </summary>
    public sealed class ReplayClip
    {
        public int Id;
        public string Title = "";
        public string Situation = "";
        public int Quarter;
        public string Clock = "";
        public float Excitement;
        public bool TowardsPlusZ = true;
        public bool OffenseIsHome;
        public int Possession;   // 0 home, 1 away
        public float SnapTime;     // frame time of the snap (the clip starts before it)
        public float KeyTime;
        public int KeyAgent;
        public int SecondAgent = -1;
        public float EndTime;
        public ReplayAgent[] Agents = new ReplayAgent[22];
        public Color[] OffenseKit = new Color[6];   // uniform primary / secondary / tertiary, helmet primary / secondary / tertiary
        public Color[] DefenseKit = new Color[6];
        public ReplayLayout[] Layouts = new ReplayLayout[3];
        public int[] RotOffset = new int[23];
        public int[] PosOffset = new int[23];
        public readonly List<ReplayFrame> Frames = new List<ReplayFrame>();
        public readonly List<string> Tags = new List<string>();
        public Vector3 LosPos;

        public int FrameCount { get { return Frames.Count; } }
        public float StartTime { get { return Frames.Count > 0 ? Frames[0].T : 0f; } }
        public float Duration { get { return Frames.Count > 1 ? Frames[Frames.Count - 1].T - Frames[0].T : 0f; } }

        /// <summary>Approximate size of the recorded samples in bytes.</summary>
        public long Bytes
        {
            get
            {
                long n = 0;
                foreach (var f in Frames) n += 4 + 22 * 16 + f.Rot.Length * 4 + f.BonePos.Length * 12 + 32;
                return n;
            }
        }

        public void BuildOffsets()
        {
            int r = 0, p = 0;
            for (int i = 0; i < 22; i++)
            {
                RotOffset[i] = r; PosOffset[i] = p;
                var l = Layouts[Mathf.Clamp(Agents[i].Size, 0, 2)];
                if (l != null) { r += l.BoneCount; p += l.PosBones.Length; }
            }
            RotOffset[22] = r; PosOffset[22] = p;
        }

        public int RotCount { get { return RotOffset[22]; } }
        public int PosCount { get { return PosOffset[22]; } }

        /// <summary>Index of the frame at or before <paramref name="t"/> and the blend toward the next one (binary search on the frame times).</summary>
        public void Locate(float t, out int i, out float a)
        {
            int n = Frames.Count;
            if (n == 0) { i = 0; a = 0f; return; }
            if (t <= Frames[0].T) { i = 0; a = 0f; return; }
            if (t >= Frames[n - 1].T) { i = n - 1; a = 0f; return; }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (Frames[mid].T <= t) lo = mid; else hi = mid; }
            i = lo;
            float span = Frames[lo + 1].T - Frames[lo].T;
            a = span > 1e-5f ? Mathf.Clamp01((t - Frames[lo].T) / span) : 0f;
        }

        public Vector3 AgentPos(int agent, float t)
        {
            int i; float a; Locate(t, out i, out a);
            if (Frames.Count == 0) return Vector3.zero;
            var f0 = Frames[i]; var f1 = Frames[Mathf.Min(i + 1, Frames.Count - 1)];
            return Vector3.Lerp(f0.Pos[agent], f1.Pos[agent], a);
        }

        public Vector3 BallPosAt(float t)
        {
            int i; float a; Locate(t, out i, out a);
            if (Frames.Count == 0) return Vector3.zero;
            var f0 = Frames[i]; var f1 = Frames[Mathf.Min(i + 1, Frames.Count - 1)];
            return Vector3.Lerp(f0.BallPos, f1.BallPos, a);
        }

        public float AgentYaw(int agent, float t)
        {
            int i; float a; Locate(t, out i, out a);
            if (Frames.Count == 0) return 0f;
            var f0 = Frames[i]; var f1 = Frames[Mathf.Min(i + 1, Frames.Count - 1)];
            return Mathf.LerpAngle(f0.Yaw[agent], f1.Yaw[agent], a);
        }

        /// <summary>The agent whose recorded position is nearest the ball at time t (used to follow the action when no key player is known).</summary>
        public int NearestToBall(float t, int side = -1)
        {
            Vector3 b = BallPosAt(t); int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < 22; i++)
            {
                if (side >= 0 && Agents[i].Side != side) continue;
                float d = (AgentPos(i, t) - b).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
    }

    /// <summary>Ordered store of recorded plays: the last few plays are always kept (instant replay) plus the most exciting ones of the game (the highlight reels), within a memory budget.</summary>
    public sealed class ReplayLibrary
    {
        public int KeepRecent = 8, KeepHighlights = 12;
        public long MaxBytes = 90L * 1024 * 1024;
        readonly List<ReplayClip> _all = new List<ReplayClip>();
        int _nextId = 1;

        public IReadOnlyList<ReplayClip> Clips { get { return _all; } }
        public int Count { get { return _all.Count; } }
        public ReplayClip Latest { get { return _all.Count > 0 ? _all[_all.Count - 1] : null; } }

        public ReplayClip Add(ReplayClip c)
        {
            c.Id = _nextId++;
            _all.Add(c);
            Trim();
            return c;
        }

        public long TotalBytes { get { long n = 0; foreach (var c in _all) n += c.Bytes; return n; } }

        void Trim()
        {
            // keep the newest KeepRecent plays and the KeepHighlights most exciting of the rest
            while (_all.Count > KeepRecent + KeepHighlights || (TotalBytes > MaxBytes && _all.Count > KeepRecent))
            {
                int worst = -1; float ws = float.MaxValue;
                for (int i = 0; i < _all.Count - KeepRecent; i++) if (_all[i].Excitement < ws) { ws = _all[i].Excitement; worst = i; }
                if (worst < 0) break;
                _all.RemoveAt(worst);
            }
        }

        /// <summary>The most exciting plays (best first) within an optional id / quarter window; ties keep the later play first.</summary>
        public List<ReplayClip> Top(int count, int firstQuarter = 1, int lastQuarter = 99, float minScore = 0f)
        {
            var l = new List<ReplayClip>();
            foreach (var c in _all) if (c.Quarter >= firstQuarter && c.Quarter <= lastQuarter && c.Excitement >= minScore) l.Add(c);
            l.Sort((a, b) => { int s = b.Excitement.CompareTo(a.Excitement); return s != 0 ? s : b.Id.CompareTo(a.Id); });
            if (l.Count > count) l.RemoveRange(count, l.Count - count);
            // the reel plays in game order
            l.Sort((a, b) => a.Id.CompareTo(b.Id));
            return l;
        }

        public void Clear() { _all.Clear(); }
    }
}
