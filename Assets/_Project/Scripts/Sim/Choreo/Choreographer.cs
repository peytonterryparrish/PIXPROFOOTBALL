using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.10.0 CHOREO (playtest notes 4-6): the sim-side sequence planner. The simulation decides WHAT happens (ratings, geometry, rules); the choreographer decides HOW it is shown so that the
    /// bodies and the authored takes agree:
    ///  1. Forecast: every tick it looks 0.5-1.2 s ahead for interactions (tackle, catch) from the positions, velocities and intents of the agents;
    ///  2. Steering: from the forecast to the contact it bends the pursuer's path (bounded velocity change, never a snap) so both arrive at the sync point with the take's layout;
    ///  3. Booking: when the interaction really happens it picks the take (or pair of takes) by approach angle, speed, height and side, places it on the field at the sync point, and from then on the
    ///     SIMULATED position and facing of the actors follow the take's root motion (warped in, momentum carried through, handed back at the take's exit moment), so nothing floats while a take plays;
    ///  4. The Unity layer starts the same takes from the booking and only draws.
    /// Deterministic: no random numbers, every choice is a pure function of the play state and a stable hash. Off unless <see cref="PlaySimOptions.ChoreoLibrary"/> is set.
    /// </summary>
    public sealed partial class Choreographer
    {
        /// <summary>Seconds ahead the forecast looks.</summary>
        public const float Horizon = 1.2f;
        /// <summary>Longest a take may own an actor (the last part of the long Draft Fever takes is idle).</summary>
        public const float MaxPlay = 6.5f;
        /// <summary>Time constant (s) of the momentum slid through a hit.</summary>
        public const float SlideTau = 0.22f;
        /// <summary>Largest velocity (yd/s) of the momentum slid through a hit: with the time constant it is at most about half a yard of extra travel.</summary>
        public const float SlideMaxVDefault = 2.0f;
        /// <summary>Largest slid momentum velocity (yd/s); QA: -pixSlideCap <v>.</summary>
        public static float SlideMaxV = ArgFloat("-pixSlideCap", SlideMaxVDefault);
        /// <summary>Scale of the pursuers' steering into a booked tackle (1 = full); QA: -pixPursuerScale.</summary>
        public static float PursuerSteerScale = ArgFloat("-pixPursuerScale", 1f);
        /// <summary>QA: bend the pass rushers toward the quarterback as well (the first-pass behaviour); -pixSteerSacks.</summary>
        public static bool SteerSacks = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixSteerSacks") >= 0;
        /// <summary>Scale of the catchers' lateral steering; QA: -pixCatcherScale.</summary>
        public static float CatcherSteerScale = ArgFloat("-pixCatcherScale", 1f);
        /// <summary>Bend the receivers' paths laterally toward the ball in the last 0.65 s; off by default (it moved the pass yards). -pixSteerCatchers.</summary>
        public static bool SteerCatchersOn = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixSteerCatchers") >= 0;
        static float ArgFloat(string name, float def) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) { float v; if (float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v; } return def; }

        readonly PlaySimulation S;
        readonly IChoreoLibrary _lib;
        readonly List<ChoreoBooking> _books = new List<ChoreoBooking>();
        readonly ChoreoBooking[] _byAgent = new ChoreoBooking[22];
        readonly List<string> _recent = new List<string>();
        float _t;
        int _nextId = 1;
        public readonly ChoreoStats Stats = new ChoreoStats();
        /// <summary>QA: calibration switches (the headless runs measure what each mechanism does to the football numbers).</summary>
        public static bool DebugNoSteer = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoChoreoSteer") >= 0, DebugNoReach, DebugNoFollow, DebugNoHand, DebugNoSteerTackle = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoChoreoSteerTackle") >= 0, DebugNoSteerCatch = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoChoreoSteerCatch") >= 0;

        public Choreographer(PlaySimulation sim, IChoreoLibrary lib) { S = sim; _lib = lib; }

        public IChoreoLibrary Library { get { return _lib; } }
        /// <summary>All bookings of this play (forecasts, playing and done).</summary>
        public IReadOnlyList<ChoreoBooking> Bookings { get { return _books; } }
        /// <summary>The booking that owns this agent's sim position right now (null when none).</summary>
        public ChoreoBooking OwnerOf(int agent) { return agent >= 0 && agent < 22 ? _byAgent[agent] : null; }
        /// <summary>The newest booking this agent takes part in (playing or finished), null when none.</summary>
        public ChoreoBooking LastBookingOf(int agent)
        {
            for (int i = _books.Count - 1; i >= 0; i--) if (_books[i].Involves(agent)) return _books[i];
            return null;
        }
        /// <summary>The choreographer's clock (seconds of simulated play, it keeps running after the whistle).</summary>
        public float Clock { get { return _t; } }

        // ===================================================================================================
        // Per tick: forecast and steer (after the AI has set every agent's intent, before the bodies step)
        // ===================================================================================================
        public void Steer()
        {
            if (S.Phase != PlayPhase.Live) return;
            ForecastTackles();
            ForecastCatches();      // (also steers the catchers)
            PlanTurns();        // the defensive backs' hip flips follow a planned rate profile
        }

        // ===================================================================================================
        // Per tick: the takes move their actors (after the bodies stepped and were separated)
        // ===================================================================================================
        public void Follow()
        {
            _t += PlaySimulation.Dt;
            if (DebugNoFollow) return;
            for (int bi = 0; bi < _books.Count; bi++)
            {
                var b = _books[bi];
                if (b.Phase != ChoreoPhase.Playing) continue;
                float ct = b.ClipTime(_t);
                bool anyOwned = false;
                if (b.OwnsA) { FollowActor(b, true, ct); anyOwned |= b.OwnsA; }
                if (b.OwnsB) { FollowActor(b, false, ct); anyOwned |= b.OwnsB; }
                b.FramesFollowed++;
                Stats.FollowTicks++;
                if (!anyOwned && b.Kind != ChoreoKind.Catch) b.Phase = ChoreoPhase.Done;
            }
        }

        void FollowActor(ChoreoBooking b, bool isA, float ct)
        {
            int id = isA ? b.A : b.B;
            if (id < 0) return;
            SimAgent ag = S.Agents[id];
            ChoreoClip clip = isA ? b.ClipA : b.ClipB;
            float exitT = isA ? b.ExitTA : b.ExitTB;
            if (_t < b.StartSim) return;                          // a take that starts later (a catch booked early) does not own him yet
            float u = _t - b.WarpStart;
            if (ct >= exitT || clip == null)
            {
                // handed back: the body leaves with the velocity the take had, the simulation drives him again
                Vec2 v = b.Align.RotateOnly(clip != null ? clip.RootVelocity(Math.Min(exitT, clip.Length)) : Vec2.Zero) * b.Rate;
                if (v.Length > ag.Body.VMax * 1.02f) v = v.Norm() * (ag.Body.VMax * 1.02f);
                ag.Body.Vel = v;
                if (isA) b.OwnsA = false; else b.OwnsB = false;
                if (_byAgent[id] == b) _byAgent[id] = null;
                return;
            }
            float tc = Math.Min(ct, clip.Length);
            Vec2 root = b.Align.Apply(clip.Root(tc));
            Vec2 slideV = isA ? b.SlideA : b.SlideB;
            Vec2 resid = isA ? b.ResidA : b.ResidB;
            Vec2 pos = root + slideV * (SlideTau * (1f - (float)Math.Exp(-u / SlideTau))) + resid * (1f - ChoreoMath.Smooth01(u / b.WarpOut));
            Vec2 old = ag.Body.Pos;
            Vec2 d;
            { Vec2 last = isA ? b.LastA : b.LastB; bool have = isA ? b.HaveA : b.HaveB;
              d = have ? pos - last : pos - (old - ag.Body.Vel * PlaySimulation.Dt);      // the visible step of this tick (the velocity the other systems see)
              if (have) { Vec2 rl = isA ? b.RootLastA : b.RootLastB; float excess = ((pos - last) - (root - rl)).Length * 0.9144f; int age = u < 0.3f ? 0 : (u < 0.6f ? 1 : 2); Stats.OwnedTicks++; Stats.OwnedByAge[age]++; if (excess > 0.05f) Stats.ExcessTicks[age]++; }
              if (isA) b.RootLastA = root; else b.RootLastB = root; if (have) { float vis = (pos - last).Length; if (vis > b.MaxStepErr) b.MaxStepErr = vis; } if (isA) { b.LastA = pos; b.HaveA = true; } else { b.LastB = pos; b.HaveB = true; } }
            ag.Body.Pos = pos;
            Vec2 vel = d * (1f / PlaySimulation.Dt);
            float cap = ag.Body.VMax * 1.3f + 1f;
            if (vel.Length > cap) vel = vel.Norm() * cap;
            ag.Body.Vel = vel;
            // the body faces where the take faces (the pelvis heading of the take, carried by the alignment)
            ag.Body.Facing = ChoreoMath.DirOfYaw(clip.YawAt(tc) + b.Align.Yaw);
            ag.PrevTickVel = vel;
        }

        // ===================================================================================================
        // helpers
        // ===================================================================================================
        static float RadiusM(SimAgent a) { return a.Body.Radius * 0.9144f * 0.85f; }

        internal ChoreoBooking Add(ChoreoBooking b)
        {
            b.Id = _nextId++;
            _books.Add(b);
            Stats.Bookings++;
            return b;
        }

        void Own(ChoreoBooking b)
        {
            if (b.OwnsA && b.A >= 0) _byAgent[b.A] = b;
            if (b.OwnsB && b.B >= 0) _byAgent[b.B] = b;
        }

        void Remember(string name) { _recent.Add(name); if (_recent.Count > 40) _recent.RemoveAt(0); }      // v0.13 (ANIMLIB-BULK): 40 bookings (was 6), see ScorePair

        static float Rating01(int r) { return SimMath.Rating01(r); }
    }
}
