using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Shared helpers for the simulation tests: synthetic rosters, standard plays, game states and small physics "labs".</summary>
    internal static class SimTestKit
    {
        private sealed class Ids : IPlayerIdSource
        {
            private int _n = 5000;
            public int NextPlayerId() { return _n++; }
        }

        private static readonly Playbook Book = DefaultPlaybooks.Create();
        public static Playbook Playbook { get { return Book; } }

        public static Player MakePlayer(Rng rng, Position pos, int ovr)
        {
            var used = new HashSet<string>();
            Player p = PlayerFactory.Create(rng, new Ids(), used, pos, ovr, 25, Archetype.None, DevTrait.Normal, true);
            p.Morale = 70;   // v0.8.0: the factory rolls a mood (55-90) and the simulation turns it into rating points; the kit players are neutral so the engine bands stay about the engine
            return p;
        }

        /// <summary>One player per formation slot, all around <paramref name="ovr"/> overall.</summary>
        public static Player[] Roster(Formation f, int ovr, Rng rng)
        {
            var ids = new Ids();
            var used = new HashSet<string>();
            var r = new Player[f.Slots.Length];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = PlayerFactory.Create(rng, ids, used, f.Slots[i].Position, ovr, 25, Archetype.None, DevTrait.Normal, true);
                r[i].Morale = 70;   // v0.8.0 (see MakePlayer)
            }
            return r;
        }

        public static Play Offense(string name, string formation = null)
        {
            foreach (Play p in Book.AllPlays)
                if (p.Side == PlaySide.Offense && p.Name == name && (formation == null || p.FormationName == formation)) return p;
            throw new ArgumentException("No offensive play " + name + " in " + formation);
        }

        public static Play Defense(string name, string formation = null)
        {
            foreach (Play p in Book.AllPlays)
                if (p.Side == PlaySide.Defense && p.Name == name && (formation == null || p.FormationName == formation)) return p;
            throw new ArgumentException("No defensive play " + name + " in " + formation);
        }

        public static Play Special(string name)
        {
            foreach (Play p in Book.AllPlays)
                if (p.Side == PlaySide.Special && p.Name == name) return p;
            throw new ArgumentException("No special teams play " + name);
        }

        /// <summary>A scrimmage game state (home has the ball).</summary>
        public static GameState Scrimmage(RuleSet rules, int spot = 35, int down = 1, int distance = 10)
        {
            var gs = new GameState(rules);
            gs.Phase = GamePhase.Scrimmage;
            gs.BallSpot = spot;
            gs.Down = down;
            gs.Distance = distance;
            gs.Possession = Core.Team.Home;
            return gs;
        }

        public static GameState PointAfter(RuleSet rules)
        {
            var gs = new GameState(rules);
            gs.Phase = GamePhase.PointAfter;
            gs.Possession = Core.Team.Home;
            gs.BallSpot = 98;
            return gs;
        }

        public static PlaySimulation Run(Play off, Play def, int offOvr, int defOvr, int seed, RuleSet rules = null, GameState gs = null, PlaySimOptions opts = null)
        {
            rules = rules ?? RuleSet.Pro();
            gs = gs ?? Scrimmage(rules);
            var rng = new Rng(seed);
            var sim = new PlaySimulation();
            Formation of = Book.FindFormation(off.FormationName);
            Formation df = Book.FindFormation(def.FormationName);
            Player[] ro = Roster(of, offOvr, rng);
            Player[] rd = Roster(df, defOvr, rng);
            sim.Setup(gs, off, def, ro, rd, rules, rng.Fork(), opts);
            return sim;
        }

        public static PlaySimulation RunNamed(string offName, string offForm, string defName, string defForm, int offOvr, int defOvr, int seed,
            RuleSet rules = null, GameState gs = null, PlaySimOptions opts = null)
        {
            return Run(Offense(offName, offForm), Defense(defName, defForm), offOvr, defOvr, seed, rules, gs, opts);
        }

        public static PlaySimulation Kick(string offName, string defName, int seed, GameState gs, int offOvr = 75, int defOvr = 75, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.Pro();
            return Run(Special(offName), Special(defName), offOvr, defOvr, seed, rules, gs);
        }

        /// <summary>Steps the simulation (no input) until the snap has happened.</summary>
        public static void StepToLive(PlaySimulation sim)
        {
            var none = new ControlInput();
            int guard = 0;
            while (sim.Phase == PlayPhase.PreSnap && guard++ < 600) sim.Step(none);
        }

        public static void Steps(PlaySimulation sim, int n, ControlInput input = default(ControlInput))
        {
            for (int i = 0; i < n && sim.Phase != PlayPhase.Dead; i++) sim.Step(input);
        }

        public static float Dist(Vec2 a, Vec2 b) { return (a - b).Length; }

        /// <summary>Moves an agent to a field position at rest.</summary>
        public static void Place(SimAgent a, float x, float y, float vx = 0f, float vy = 0f)
        {
            a.Body.Pos = new Vec2(x, y);
            a.Body.Vel = new Vec2(vx, vy);
            a.Body.FallenTime = 0f;
            a.Body.StumbleTime = 0f;
            a.State = AgentState.Normal;
            a.ClearHistory(a.Body.Pos);
        }

        /// <summary>Parks every agent far from the action so a single interaction can be staged.</summary>
        public static void ParkAll(PlaySimulation sim, float farY = 6f)
        {
            for (int i = 0; i < 22; i++) Place(sim.Agents[i], 3f + i * 2f, farY);
        }

        public static string Describe(PlaySimulation sim) { return sim.Log.ToText(); }
    }
}
