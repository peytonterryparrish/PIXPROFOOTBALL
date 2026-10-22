using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>Gameplay events that dirty a player's kit. The Gameplay layer maps simulation events onto these.</summary>
    public enum DirtEvent
    {
        None,
        /// <summary>Ball carrier brought down by a normal wrap-up.</summary>
        TackledWrap,
        /// <summary>Carrier driven back / hit hard (big hit, sack victim, pancake).</summary>
        TackledHard,
        /// <summary>Carrier tripped at the feet (shoestring) or dove for extra yards.</summary>
        TackledLow,
        /// <summary>Tackler dove at the carrier.</summary>
        DiveTackle,
        /// <summary>The player dove (carrier dive, diving catch).</summary>
        Dove,
        /// <summary>Fell down (stumble, broken tackle whiff).</summary>
        Fell,
        /// <summary>Went to the ground in an active ragdoll (trucked, knocked over).</summary>
        Ragdoll,
        /// <summary>Quarterback sacked.</summary>
        Sacked,
        /// <summary>One play of line contact (blocking engagement).</summary>
        LineContact,
        /// <summary>Pile at the end of a play (gang tackle, bodies on the ground).</summary>
        Pile,
        /// <summary>Beaten and pancaked by a blocker.</summary>
        Pancaked,
    }

    /// <summary>Per player kit dirt: how soiled each body region of the uniform is (0 clean .. 1 caked), plus the kind of dirt.</summary>
    public struct DirtState
    {
        public float Knees, Seat, Front, Back;
        /// <summary>General grime that spreads over everything a little (0..1).</summary>
        public float Grime;
        /// <summary>0 = brown soil, 1 = green grass stains (the colour of the dirt).</summary>
        public float GrassFraction;
        /// <summary>0 dry dirt .. 1 mud (darker, glossier).</summary>
        public float Mud;
        /// <summary>Helmet scuffs and scratches 0..1.</summary>
        public float HelmetScuff;
        /// <summary>Number of events applied (for statistics and tests).</summary>
        public int Events;

        public float Total { get { return Knees + Seat + Front + Back; } }
        public float Max { get { return Math.Max(Math.Max(Knees, Seat), Math.Max(Front, Back)); } }
        public bool IsClean { get { return Total < 0.001f && Grime < 0.001f && HelmetScuff < 0.001f; } }
    }

    /// <summary>The playing surface and weather a dirt event happens in.</summary>
    public struct DirtContext
    {
        /// <summary>Wet surfaces 0..1 (rain): dirt turns to mud and builds faster.</summary>
        public float Wetness;
        /// <summary>Snow cover 0..1: snow sticks and melts into slush; stains stay light.</summary>
        public float Snow;
        /// <summary>Artificial turf stains much less than natural grass and never produces mud.</summary>
        public bool ArtificialTurf;

        public static DirtContext Dry { get { return new DirtContext(); } }
    }

    /// <summary>
    /// The dirt accumulation model: events add to body regions with a soft saturation (the cleaner the region, the faster it soils), scaled by the weather and surface.
    /// Pure and deterministic; the per-player state lives for the whole game (<see cref="DirtBook"/>).
    /// </summary>
    public static class DirtModel
    {
        public const float SaturationSlope = 0.55f;

        /// <summary>Base increments (knees, seat, front, back, helmet scuff) of an event at full intensity on dry natural grass.</summary>
        public static void Increments(DirtEvent e, out float knees, out float seat, out float front, out float back, out float scuff)
        {
            knees = seat = front = back = scuff = 0f;
            switch (e)
            {
                case DirtEvent.TackledWrap: knees = 0.06f; seat = 0.09f; front = 0.07f; back = 0.12f; scuff = 0.02f; break;
                case DirtEvent.TackledHard: knees = 0.05f; seat = 0.14f; front = 0.06f; back = 0.20f; scuff = 0.06f; break;
                case DirtEvent.TackledLow: knees = 0.16f; seat = 0.04f; front = 0.18f; back = 0.02f; scuff = 0.03f; break;
                case DirtEvent.DiveTackle: knees = 0.14f; seat = 0.02f; front = 0.20f; back = 0.02f; scuff = 0.03f; break;
                case DirtEvent.Dove: knees = 0.14f; seat = 0.02f; front = 0.22f; back = 0.02f; scuff = 0.02f; break;
                case DirtEvent.Fell: knees = 0.11f; seat = 0.05f; front = 0.09f; back = 0.03f; scuff = 0.01f; break;
                case DirtEvent.Ragdoll: knees = 0.05f; seat = 0.12f; front = 0.07f; back = 0.18f; scuff = 0.05f; break;
                case DirtEvent.Sacked: knees = 0.04f; seat = 0.15f; front = 0.04f; back = 0.20f; scuff = 0.05f; break;
                case DirtEvent.LineContact: knees = 0.01f; seat = 0f; front = 0.02f; back = 0.015f; scuff = 0.012f; break;
                case DirtEvent.Pile: knees = 0.06f; seat = 0.05f; front = 0.05f; back = 0.05f; scuff = 0.03f; break;
                case DirtEvent.Pancaked: knees = 0.03f; seat = 0.13f; front = 0.06f; back = 0.18f; scuff = 0.04f; break;
            }
        }

        /// <summary>Multiplier on the dirt gained for the weather and surface.</summary>
        public static float WeatherFactor(DirtContext c)
        {
            float f = 1f + 0.9f * Clamp01(c.Wetness) + 0.25f * Clamp01(c.Snow);
            if (c.ArtificialTurf) f *= 0.55f;
            return f;
        }

        /// <summary>Fraction of the dirt that is green grass stain: natural grass on dry ground stains green; wet ground and artificial turf give brown / black.</summary>
        public static float GrassFractionFor(DirtContext c)
        {
            if (c.ArtificialTurf) return 0.05f;
            float g = 0.82f - 0.5f * Clamp01(c.Wetness) - 0.2f * Clamp01(c.Snow);
            return Clamp01(g);
        }

        public static float MudFor(DirtContext c)
        {
            if (c.ArtificialTurf) return 0f;
            return Clamp01(c.Wetness * 0.85f + c.Snow * 0.2f);
        }

        static float Soak(float current, float increment)
        {
            // saturating accumulation: dirt on dirt shows less
            float v = current + increment * (1f - SaturationSlope * current);
            return v > 1f ? 1f : (v < 0f ? 0f : v);
        }

        /// <summary>Applies one event. <paramref name="intensity"/> 0..1 scales it (impact energy, speed, number of defenders).</summary>
        public static void Apply(ref DirtState s, DirtEvent e, float intensity, DirtContext context)
        {
            if (e == DirtEvent.None) return;
            float k, st, f, b, sc;
            Increments(e, out k, out st, out f, out b, out sc);
            if (s.Events == 0) { s.GrassFraction = GrassFractionFor(context); s.Mud = MudFor(context); }   // first stains show the right colour at once (QA-551)
            float scale = Clamp01(intensity) * WeatherFactor(context);
            s.Knees = Soak(s.Knees, k * scale);
            s.Seat = Soak(s.Seat, st * scale);
            s.Front = Soak(s.Front, f * scale);
            s.Back = Soak(s.Back, b * scale);
            s.HelmetScuff = Soak(s.HelmetScuff, sc * Clamp01(intensity));
            float gain = (k + st + f + b) * scale;
            s.Grime = Soak(s.Grime, gain * 0.1f);
            // the kind of dirt follows the conditions of the last events (blended, so a wet half after a dry half turns muddy)
            float blend = Clamp01(0.25f + gain * 2f);
            s.GrassFraction += (GrassFractionFor(context) - s.GrassFraction) * blend;
            s.Mud += (MudFor(context) - s.Mud) * blend;
            s.Events++;
        }

        /// <summary>Weights handed to the shader: (knees, seat, front, back) after a gentle contrast curve so a little dirt is visible and a lot is not just black.</summary>
        public static void ShaderRegions(DirtState s, out float knees, out float seat, out float front, out float back)
        {
            knees = Curve(s.Knees); seat = Curve(s.Seat); front = Curve(s.Front); back = Curve(s.Back);
        }

        static float Curve(float v) { v = Clamp01(v); return (float)Math.Sqrt(v) * 0.9f + 0.1f * v; }
        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
    }

    /// <summary>Dirt state of every player of the game, keyed by player id (the whole game, both teams).</summary>
    public sealed class DirtBook
    {
        readonly Dictionary<int, DirtState> _states = new Dictionary<int, DirtState>();

        public int Count { get { return _states.Count; } }

        public DirtState Get(int playerId)
        {
            DirtState s; return _states.TryGetValue(playerId, out s) ? s : default(DirtState);
        }

        public DirtState Add(int playerId, DirtEvent e, float intensity, DirtContext context)
        {
            DirtState s; _states.TryGetValue(playerId, out s);
            DirtModel.Apply(ref s, e, intensity, context);
            _states[playerId] = s;
            return s;
        }

        public void Set(int playerId, DirtState s) { _states[playerId] = s; }
        public void Clear() { _states.Clear(); }

        /// <summary>True when the player has any recorded dirt state.</summary>
        public bool Has(int playerId) { return _states.ContainsKey(playerId); }
    }
}
