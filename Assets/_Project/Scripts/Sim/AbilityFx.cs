using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.8.0 ability lookup of one on-field player: the player's <see cref="AbilityCatalog"/> effects flattened to (hook, context, value) at kickoff
    /// so the hot paths of the simulation ask a tiny array instead of walking the catalogue. Null for the (large) majority of players who have no
    /// ability, so a simulation without abilities behaves exactly as before and draws exactly the same random numbers.
    /// Contexts are bit flags (<see cref="Bit"/>): an effect with context Always counts for every query, a contextual effect only when the query
    /// carries its bit. The play-level contexts (third down, two-minute drill, red zone, short yardage, close game late, kick return, man coverage,
    /// blitz) are fixed per play and per agent (<see cref="SimAgent.CtxMask"/>); the rest (under pressure, over the middle, open field, deep,
    /// line of scrimmage) are decided by the code that asks.
    /// </summary>
    internal sealed class AbilityFx
    {
        private struct Entry { public SimHook Hook; public AbilityContext Ctx; public int Bit; public float V; }

        private readonly Entry[] _e;

        /// <summary>Tuning / test switch: hooks listed here are ignored by every query (a bit per <see cref="SimHook"/>). Set before the simulation runs; never changed during a play.</summary>
        internal static ulong MutedHooks;
        internal static void Mute(params SimHook[] hooks) { MutedHooks = 0; for (int i = 0; i < hooks.Length; i++) MutedHooks |= 1UL << (int)hooks[i]; }

        private AbilityFx(Entry[] e) { _e = e; }

        public static int Bit(AbilityContext c) { return 1 << (int)c; }

        /// <summary>The context bits that are decided once per play / per agent (the rest are dynamic).</summary>
        public const int StaticContexts = (1 << (int)AbilityContext.ThirdFourthDown) | (1 << (int)AbilityContext.TwoMinute) | (1 << (int)AbilityContext.RedZone)
            | (1 << (int)AbilityContext.ShortYardage) | (1 << (int)AbilityContext.CloseGameLate) | (1 << (int)AbilityContext.Return)
            | (1 << (int)AbilityContext.ManToMan) | (1 << (int)AbilityContext.Blitz);

        /// <summary>
        /// Calibration gain per hook: the catalogue sizes are what the player card shows; the simulation applies them times this factor so that a league full of
        /// abilities scores as the unmodified league does (600-game and 6000-game runs, Docs/QA/v080/sim_abilities.md). Offensive hooks are the strongest in a
        /// league-wide count (a dozen different abilities all help the offense a little), so they carry a gain below 1.
        /// </summary>
        public static float Scale(SimHook hook, AbilityContext ctx = AbilityContext.Always) { return AbilityCatalog.Gain(hook, ctx); }   // one table, shared with the player card

        /// <summary>Flattens the player's abilities; null when he has none (or none that does anything).</summary>
        public static AbilityFx For(Player p)
        {
            if (p == null || p.Abilities == null || p.Abilities.Count == 0) return null;
            List<Entry> l = null;
            for (int i = 0; i < p.Abilities.Count; i++)
            {
                AbilityDef d = AbilityCatalog.Get(p.Abilities[i].Id);
                if (d == null) continue;
                for (int k = 0; k < d.Effects.Length; k++)
                {
                    AbilityEffect fx = d.Effects[k];
                    float v = fx.At(p.Abilities[i].Tier) * Scale(fx.Hook, fx.Context);
                    if (v == 0f) continue;
                    if (l == null) l = new List<Entry>(4);
                    l.Add(new Entry { Hook = fx.Hook, Ctx = fx.Context, Bit = Bit(fx.Context), V = v });
                }
            }
            return l == null ? null : new AbilityFx(l.ToArray());
        }

        /// <summary>Sum of the effects for a hook: every Always effect plus the contextual ones whose bit is in <paramref name="mask"/>.</summary>
        public float Sum(SimHook hook, int mask)
        {
            float s = 0f;
            for (int i = 0; i < _e.Length; i++)
            {
                if (_e[i].Hook != hook || (MutedHooks & (1UL << (int)hook)) != 0) continue;
                if (_e[i].Ctx == AbilityContext.Always || (_e[i].Bit & mask) != 0) s += _e[i].V;
            }
            return s;
        }

        /// <summary>Sum of only the contextual effects whose bit is in <paramref name="mask"/> (Always effects excluded): the part a dynamic context adds on top of what was already baked into the ratings.</summary>
        public float SumOnly(SimHook hook, int mask)
        {
            float s = 0f;
            for (int i = 0; i < _e.Length; i++)
                if (_e[i].Hook == hook && (MutedHooks & (1UL << (int)hook)) == 0 && _e[i].Ctx != AbilityContext.Always && (_e[i].Bit & mask) != 0) s += _e[i].V;
            return s;
        }

        public bool Has(SimHook hook)
        {
            for (int i = 0; i < _e.Length; i++) if (_e[i].Hook == hook) return true;
            return false;
        }

        public int Count { get { return _e.Length; } }
    }
}
