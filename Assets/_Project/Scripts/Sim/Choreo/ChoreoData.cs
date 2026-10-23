using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>What an authored two-actor take is about (the tackle kinds follow what the simulation decides at the contact).</summary>
    public enum PairKind
    {
        /// <summary>Standing wrap-up: the carrier goes down, the tackler on top / beside him.</summary>
        Wrap,
        /// <summary>Low tackle at the legs (dive tackle, shoestring that brings him down).</summary>
        Dive,
        /// <summary>The tackler is beaten: he whiffs / falls while the carrier stumbles on.</summary>
        Broken,
        /// <summary>The carrier runs through the defender.</summary>
        Truck,
        /// <summary>A big hit: both bodies take the collision.</summary>
        BigHit,
        /// <summary>A big hit the carrier shrugs off.</summary>
        BigHitShrug,
        /// <summary>Quarterback taken down by a rusher.</summary>
        Sack,
        /// <summary>Quarterback escapes the rusher.</summary>
        SackBroken,
        /// <summary>Plain shoulder to shoulder collision (jam, blocker / hit reaction).</summary>
        Collision,
        /// <summary>A catch in traffic: receiver and defender both go up for the ball.</summary>
        Contested
    }

    /// <summary>
    /// One authored take as the simulation sees it (engine-free): its name (the view plays the clip by that name), its length, the moment that has to land on the simulated event, the moment the body
    /// is upright and free again, and the root path the take wants (metres, in the take's own frame; for the clips of a pair, in the pair's shared authored frame).
    /// Path samples: <see cref="Stride"/> floats each = root x, root z, hips x, hips z, pelvis yaw (degrees), hips height.
    /// </summary>
    public sealed class ChoreoClip
    {
        public const int Stride = 6;
        public const float MetersToYards = 1f / 0.9144f;

        public string Name = "";
        public float Length, Step = 0.05f;
        /// <summary>Key moment (catch: the hands close, hand-off: the exchange, tackle: the closest approach of the hips).</summary>
        public float ContactT;
        /// <summary>The body is upright and moving on its own again from this time on (after a fall: the get-up is done).</summary>
        public float SettleT;
        /// <summary>Hand mid-point at <see cref="ContactT"/> relative to the hips in the pelvis frame: x to the right, y height above the ground, z forward (metres).</summary>
        public float HandX, HandY, HandZ;
        public float MinHips, SettleLead;
        public float[] Path = new float[0];

        public int Count { get { return Path.Length / Stride; } }

        float Lerp(int channel, float t)
        {
            int n = Count; if (n == 0) return 0f;
            float f = t <= 0f ? 0f : t / Step;
            int i = (int)f; if (i > n - 1) i = n - 1;
            int j = i + 1 < n ? i + 1 : n - 1;
            float u = f - i; if (u > 1f) u = 1f;
            return Path[i * Stride + channel] * (1f - u) + Path[j * Stride + channel] * u;
        }

        /// <summary>Root position at clip time t (metres).</summary>
        public Vec2 Root(float t) { return new Vec2(Lerp(0, t), Lerp(1, t)); }
        /// <summary>Hips position at clip time t (metres).</summary>
        public Vec2 Hips(float t) { return new Vec2(Lerp(2, t), Lerp(3, t)); }
        /// <summary>Pelvis heading at clip time t (degrees, 0 = +Y of the sim frame).</summary>
        public float YawAt(float t)
        {
            int n = Count; if (n == 0) return 0f;
            float f = t <= 0f ? 0f : t / Step;
            int i = (int)f; if (i > n - 1) i = n - 1;
            int j = i + 1 < n ? i + 1 : n - 1;
            float u = f - i; if (u > 1f) u = 1f;
            float a = Path[i * Stride + 4], b = Path[j * Stride + 4];
            return a + ChoreoMath.DeltaAngle(a, b) * u;
        }
        public float HipsY(float t) { return Lerp(5, t); }
        /// <summary>Ground speed of the root at clip time t (metres per second; central difference over 0.1 s).</summary>
        public float RootSpeed(float t)
        {
            Vec2 a = Root(Math.Max(0f, t - 0.05f)), b = Root(Math.Min(Length, t + 0.05f));
            float dt = Math.Min(Length, t + 0.05f) - Math.Max(0f, t - 0.05f);
            return dt > 1e-4f ? (b - a).Length / dt : 0f;
        }
        public Vec2 RootVelocity(float t)
        {
            float t0 = Math.Max(0f, t - 0.05f), t1 = Math.Min(Length, t + 0.05f);
            float dt = t1 - t0;
            return dt > 1e-4f ? (Root(t1) - Root(t0)) * (1f / dt) : Vec2.Zero;
        }
    }

    /// <summary>One authored two-actor take: actor A (the offense: carrier, quarterback, receiver, blocker) and actor B (the defender).</summary>
    public sealed class ChoreoPair
    {
        public string Key = "";
        public PairKind Kind;
        public ChoreoClip A, B;
        /// <summary>Clock of the pair: the closest approach of the two hips (seconds, same for both actors).</summary>
        public float ContactT;
        /// <summary>Metres between the hips at contact.</summary>
        public float Distance;
        /// <summary>Degrees: where B is seen from A at contact (0 = in front of A, +/-180 = behind him, + = to A's right).</summary>
        public float Bearing;
        /// <summary>Metres per second just before contact: B's closing speed on A and A's own speed.</summary>
        public float ClosingB, SpeedA;
        /// <summary>0..1: how low B's hips are at contact (a dive is near 1).</summary>
        public float LowB;
    }

    /// <summary>What the choreographer needs to know about the clips (implemented by the Unity layer from the measured tables; tests use <see cref="SyntheticChoreoLibrary"/>).</summary>
    public interface IChoreoLibrary
    {
        IReadOnlyList<ChoreoPair> Pairs { get; }
        /// <summary>Catching takes of a receiver (high, low, side, jump, dive, running and standing).</summary>
        IReadOnlyList<ChoreoClip> Catches { get; }
        /// <summary>Takes of a defender playing the ball (interception, swat).</summary>
        IReadOnlyList<ChoreoClip> Intercepts { get; }
    }

    /// <summary>A procedural library for tests and headless runs: straight-line takes with a fixed contact layout. Deterministic.</summary>
    public sealed class SyntheticChoreoLibrary : IChoreoLibrary
    {
        readonly List<ChoreoPair> _pairs = new List<ChoreoPair>();
        readonly List<ChoreoClip> _catches = new List<ChoreoClip>();
        public IReadOnlyList<ChoreoPair> Pairs { get { return _pairs; } }
        public IReadOnlyList<ChoreoClip> Catches { get { return _catches; } }
        readonly List<ChoreoClip> _intercepts = new List<ChoreoClip>();
        public IReadOnlyList<ChoreoClip> Intercepts { get { return _intercepts; } }
        public List<ChoreoClip> CatchesMut { get { return _catches; } }
        public List<ChoreoClip> InterceptsMut { get { return _intercepts; } }

        /// <summary>A straight take: the actor walks from (x0, z0) to (x1, z1) in length seconds facing the travel direction (yaw in degrees), hips 0.95 m high, lying (hips 0.25) from lieFrom on.</summary>
        public static ChoreoClip Straight(string name, float length, float x0, float z0, float x1, float z1, float yaw, float lieFrom = -1f, float contactT = 0f)
        {
            int n = (int)(length / 0.05f + 0.5f) + 1;      // (2 / 0.05 is 39.999996 in single precision on some runtimes: round, or the path is one sample short and the clip is stretched)
            var c = new ChoreoClip { Name = name, Length = length, Step = 0.05f, ContactT = contactT, Path = new float[n * ChoreoClip.Stride] };
            for (int i = 0; i < n; i++)
            {
                float u = n > 1 ? (float)i / (n - 1) : 0f;
                float x = x0 + (x1 - x0) * u, z = z0 + (z1 - z0) * u;
                int o = i * ChoreoClip.Stride;
                c.Path[o] = x; c.Path[o + 1] = z; c.Path[o + 2] = x; c.Path[o + 3] = z; c.Path[o + 4] = yaw;
                c.Path[o + 5] = lieFrom >= 0f && i * 0.05f >= lieFrom ? 0.25f : 0.95f;
            }
            c.MinHips = lieFrom >= 0f ? 0.25f : 0.95f;
            c.SettleT = length * 0.9f;
            return c;
        }

        /// <summary>One wrap tackle from the front (tackler B comes from 0 degrees, hips 0.55 m apart at contact t = 0.6 s) and one dive from the right.</summary>
        public static SyntheticChoreoLibrary Default()
        {
            var lib = new SyntheticChoreoLibrary();
            // A: carrier runs +Z 2.4 m in 0.6 s then falls 1.2 m more over 1.4 s; B: tackler runs at him from 2.0 m ahead.
            var a = Straight("synthA_wrap", 3.0f, 0f, 0f, 0f, 3.6f, 0f, 0.9f, 0.6f);
            var b = Straight("synthB_wrap", 3.0f, 0f, 3.2f, 0f, 4.2f, 180f, 0.9f, 0.6f);
            // make B arrive 0.55 m in front of A at the contact time
            for (int i = 0; i < b.Count; i++)
            {
                float t = i * 0.05f;
                float u = Math.Min(1f, t / 0.6f);
                // A's hips at 0.6 s are at z = 0.6 / 3.0 * 3.6 = 0.72; put B 0.55 m ahead: z = 1.27
                b.Path[i * ChoreoClip.Stride + 1] = 3.2f + (1.27f - 3.2f) * u + (t > 0.6f ? (t - 0.6f) * 0.3f : 0f);
                b.Path[i * ChoreoClip.Stride + 3] = b.Path[i * ChoreoClip.Stride + 1];
            }
            lib._pairs.Add(new ChoreoPair { Key = "synthetic_wrap", Kind = PairKind.Wrap, A = a, B = b, ContactT = 0.6f, Distance = 0.55f, Bearing = 0f, ClosingB = 3.3f, SpeedA = 1.2f, LowB = 0.1f });
            var a2 = Straight("synthA_dive", 3.0f, 0f, 0f, 0f, 3.0f, 0f, 1.0f, 0.5f);
            var b2 = Straight("synthB_dive", 3.0f, 2.2f, 1.0f, 0.4f, 1.0f, -90f, 0.6f, 0.5f);
            lib._pairs.Add(new ChoreoPair { Key = "synthetic_dive", Kind = PairKind.Dive, A = a2, B = b2, ContactT = 0.5f, Distance = 0.6f, Bearing = 90f, ClosingB = 3.0f, SpeedA = 1.0f, LowB = 0.9f });
            return lib;
        }
    }
}
