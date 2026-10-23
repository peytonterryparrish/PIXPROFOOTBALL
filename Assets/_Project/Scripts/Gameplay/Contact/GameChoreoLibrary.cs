using System;
using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 CHOREO: the clip library of the simulation's sequence planner, built from the measured tables (<see cref="ContactTable"/> for the two-actor takes, <see cref="ChoreoTable"/> for the
    /// single-actor takes) and the ArtLibrary keys. Engine-free data for the sim; null when the tables are missing (the match then plays exactly as v0.9.0).
    /// </summary>
    public sealed class GameChoreoLibrary : IChoreoLibrary
    {
        readonly List<ChoreoPair> _pairs = new List<ChoreoPair>();
        readonly List<ChoreoClip> _catches = new List<ChoreoClip>(), _intercepts = new List<ChoreoClip>();
        public IReadOnlyList<ChoreoPair> Pairs { get { return _pairs; } }
        public IReadOnlyList<ChoreoClip> Catches { get { return _catches; } }
        public IReadOnlyList<ChoreoClip> Intercepts { get { return _intercepts; } }

        static GameChoreoLibrary _shared; static bool _tried;
        /// <summary>QA: switch the choreographer off (the audit's "before" state, <c>-pixNoChoreo</c>).</summary>
        public static bool Disabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoChoreo") >= 0;

        /// <summary>The shared library (built once), or null when the choreography is off or its tables are missing.</summary>
        public static GameChoreoLibrary Shared
        {
            get
            {
                if (Disabled) return null;
                if (_tried) return _shared;
                _tried = true;
                try { _shared = Build(); } catch (Exception e) { Debug.LogWarning("[Choreo] library not built: " + e.Message); _shared = null; }
                return _shared;
            }
        }

        public static void ResetCache() { _shared = null; _tried = false; }

        /// <summary>Audit switch: <c>-pixChoreoDefault</c> gives every PlaySimOptions the shared library (EngineAudit, AutoTest and the other tools build their own options).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BootDefault()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pixChoreoDefault") >= 0) PlaySimOptions.DefaultChoreoLibrary = Shared;
        }

        static readonly ContactKind[] Kinds =
        {
            ContactKind.Wrap, ContactKind.Dive, ContactKind.Broken, ContactKind.Truck, ContactKind.BigHit, ContactKind.BigHitShrug,
            ContactKind.Sack, ContactKind.SackBroken, ContactKind.Collision, ContactKind.Contested
        };

        static PairKind Map(ContactKind k)
        {
            switch (k)
            {
                case ContactKind.Wrap: return PairKind.Wrap;
                case ContactKind.Dive: return PairKind.Dive;
                case ContactKind.Broken: return PairKind.Broken;
                case ContactKind.Truck: return PairKind.Truck;
                case ContactKind.BigHit: return PairKind.BigHit;
                case ContactKind.BigHitShrug: return PairKind.BigHitShrug;
                case ContactKind.Sack: return PairKind.Sack;
                case ContactKind.SackBroken: return PairKind.SackBroken;
                case ContactKind.Collision: return PairKind.Collision;
                default: return PairKind.Contested;
            }
        }

        static GameChoreoLibrary Build()
        {
            var contacts = ContactTable.Load();
            var table = ChoreoTable.Load();
            var lib = ArtLibrary.Load();
            if (contacts == null || contacts.pairs.Count == 0) return null;
            if (Application.isEditor) Debug.Log("[Choreo] building the library: " + contacts.pairs.Count + " contact pairs, choreo table " + (table != null ? table.clips.Count + " clips" : "MISSING") + ", art library " + (lib != null ? "ok" : "MISSING"));
            var g = new GameChoreoLibrary();
            foreach (var p in contacts.pairs)
            {
                if (p == null || p.CountA < 2 || p.CountB < 2 || Rejected(p.clipA) || Rejected(p.clipB)) continue;
                ContactKind? found = null;
                foreach (var k in Kinds) if (TackleVariants.Matches(p, k)) { found = k; break; }
                if (found == null) continue;
                var cp = new ChoreoPair
                {
                    Key = p.key, Kind = Map(found.Value), ContactT = p.tContact, Distance = p.distance, Bearing = p.bearing,
                    ClosingB = p.closingB, SpeedA = p.speedA, LowB = p.lowB,
                    A = FromContact(p, true, table), B = FromContact(p, false, table)
                };
                g._pairs.Add(cp);
            }
            if (table != null && lib != null)
            {
                AddSolo(g._catches, lib, table, ArtKeys.CatchHigh, ArtKeys.CatchLow, ArtKeys.CatchSide, ArtKeys.CatchRun, ArtKeys.CatchJump);
                AddSolo(g._intercepts, lib, table, ArtKeys.Interception, ArtKeys.Swat);
            }
            return g._pairs.Count > 0 ? g : null;
        }

        /// <summary>The ANIMLIB QA table (when it exists) can reject a take; a rejected clip is never booked.</summary>
        static bool Rejected(string clip)
        {
            var tags = ClipTagTable.Load(); ClipTagTable.Entry e;
            return tags != null && tags.TryGet(clip, out e) && (e.reject || e.score < 50f);
        }

        /// <summary>QA: use the measured native travel of the pair takes (the v0.10.0 first-pass behaviour); default is the travel scaled to the planted feet. -pixNoRootScale.</summary>
        public static bool NoRootScale = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoRootScale") >= 0;

        /// <summary>
        /// The travel of a take scaled to what its planted feet allow (<see cref="ChoreoTable.Entry.k"/>): the path keeps its shape, each step is multiplied by the measured factor, and the path is held through the contact
        /// point (the pair's layout at the key moment is unchanged: both actors are scaled with the same timing and meet where they met). Hips follow the root by the same offset.
        /// </summary>
        static void ScaleTravel(ChoreoClip c, float[] k, int n, int ic)
        {
            int S = ChoreoClip.Stride; if (n < 3 || k == null || k.Length < n) return;
            var lx = new float[n]; var lz = new float[n]; lx[0] = c.Path[0]; lz[0] = c.Path[1];
            for (int i = 0; i < n - 1; i++) { float kk = 0.5f * (k[i] + k[i + 1]); lx[i + 1] = lx[i] + (c.Path[(i + 1) * S] - c.Path[i * S]) * kk; lz[i + 1] = lz[i] + (c.Path[(i + 1) * S + 1] - c.Path[i * S + 1]) * kk; }
            ic = Mathf.Clamp(ic, 0, n - 1);
            float sx = c.Path[ic * S] - lx[ic], sz = c.Path[ic * S + 1] - lz[ic];
            for (int i = 0; i < n; i++)
            {
                float nx = lx[i] + sx, nz = lz[i] + sz; float dx = nx - c.Path[i * S], dz = nz - c.Path[i * S + 1];
                c.Path[i * S] = nx; c.Path[i * S + 1] = nz; c.Path[i * S + 2] += dx; c.Path[i * S + 3] += dz;
            }
        }

        static ChoreoClip FromContact(ContactTable.Pair p, bool a, ChoreoTable table)
        {
            var src = a ? p.a : p.b;
            string name = a ? p.clipA : p.clipB;
            float len = a ? p.lengthA : p.lengthB;
            int n = src.Length / ContactTable.Stride;
            var c = new ChoreoClip { Name = name, Length = len, Step = p.step, ContactT = p.tContact, Path = new float[n * ChoreoClip.Stride] };
            ChoreoTable.Entry e = table != null ? table.Get(name) : null;
            float getUp = a ? p.getUpA : p.getUpB;
            for (int i = 0; i < n; i++)
            {
                int o = i * ChoreoClip.Stride, s = i * ContactTable.Stride;
                for (int k = 0; k < 5; k++) c.Path[o + k] = src[s + k];
                c.Path[o + 5] = e != null && i < e.Count ? e.s[i * ChoreoTable.Stride + 5] : 0.9f;
            }
            if (!NoRootScale && e != null && e.k != null && e.k.Length >= n) ScaleTravel(c, e.k, n, Mathf.RoundToInt(p.tContact / Mathf.Max(0.001f, p.step)));
            if (e != null) { c.SettleT = e.settleT; c.MinHips = e.minHips; c.SettleLead = e.settleLead; }
            else c.SettleT = getUp > 0f ? Mathf.Min(len, getUp + 1.1f) : len;
            return c;
        }

        static void AddSolo(List<ChoreoClip> into, ArtLibrary lib, ChoreoTable table, params string[] keys)
        {
            var seen = new HashSet<string>();
            int variants = 0, inTable = 0;
            foreach (string key in keys)
                foreach (var clip in lib.ClipVariants(key))
                {
                    variants++;
                    if (clip == null || !seen.Add(clip.name) || Rejected(clip.name)) continue;
                    var e = table.Get(clip.name);
                    if (e != null) inTable++;
                    if (e == null || e.Count < 2 || e.contactT <= 0.05f || e.hand.y <= 0.2f || e.length > 3.5f) continue;
                    var c = new ChoreoClip { Name = clip.name, Length = e.length, Step = e.step, ContactT = e.contactT, SettleT = e.settleT, MinHips = e.minHips, SettleLead = e.settleLead, HandX = e.hand.x, HandY = e.hand.y, HandZ = e.hand.z };
                    c.Path = (float[])e.s.Clone();
                    into.Add(c);
                }
            if (Application.isEditor) Debug.Log("[Choreo] solo keys " + string.Join(",", keys) + ": " + variants + " variants, " + inTable + " in the table, " + into.Count + " usable");
        }
    }
}
