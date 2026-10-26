using System;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 NATURAL-2: every player chooses his own takes among the matcher's database. The search used to take the single nearest row for everybody, so 22 men at the same speed played the same loop
    /// (that is what the owner calls robots). Three small, bounded additions to the matcher's cost (all in the units of its squared normalised distance, so a take that fits clearly worse never wins):
    ///  * a PERSONAL spread: a fixed 0..<see cref="Spread"/> per take drawn from the player's id (a take and its mirror twin share it, so left and right stay balanced);
    ///  * a PERSONAL style: each player likes two of the 100STYLE families (role-weighted: linemen march and stiff-walk, receivers and backs run on their toes) and gets <see cref="FamilyBonus"/> off their takes;
    ///  * a RECENT-USE penalty shared by the whole stadium: a take that was entered a moment ago by anybody costs a little more, so near-equal takes are used in turn instead of one winning forever.
    /// A per-take quality prior (<c>MotionDb.Clip.prior</c>, set by the builder from the per-take QA) is added on top. Pure data and math (<c>MotionStyleTests</c>); switches <c>-pixStyleOff</c>, <c>-pixStyleSpread</c>, <c>-pixStyleFamily</c>, <c>-pixStyleRecent</c>.
    /// </summary>
    public static class MotionStyle
    {
        public static bool Enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixStyleOff") < 0;
        /// <summary>Largest personal cost per take, family bonus, recent-use cost of one fresh entry (cap <see cref="RecentCap"/> entries) and its decay time (s).</summary>
        public static float Spread = ArgF("-pixStyleSpread", 1.20f), FamilyBonus = ArgF("-pixStyleFamily", 0.60f), RecentK = ArgF("-pixStyleRecent", 0.30f), RecentTau = 25f, RecentCap = 4f;
        /// <summary>QA: added to every player's seed (a second run with another draw of the personal choices).</summary>
        public static int SeedAdd = (int)ArgF("-pixStyleSeedAdd", 0f);
        /// <summary>QA: -pixStyleBan a,b adds a large cost to the named takes (and their mirrors), to test a database without them before it is rebuilt.</summary>
        public static readonly string[] Ban = BanArg();
        static string[] BanArg() { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-pixStyleBan"); return i >= 0 && i + 1 < a.Length ? a[i + 1].Split(',') : new string[0]; }

        static float ArgF(string name, float def)
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); float v;
            return i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def;
        }

        /// <summary>The families of the database: 0 library / Mixamo, 1 Mobility Pro, 2.. the twelve 100STYLE styles (Ian Mason, CC BY 4.0).</summary>
        public static readonly string[] Families = { "Mixamo", "Mobility", "Neutral", "Heavyset", "Stiff", "March", "Strutting", "Proud", "Roadrunner", "Rocket", "OnToesBentForward", "BentKnees", "Rushed", "StartStop" };

        /// <summary>Family of a database take by name: <c>S100_&lt;Style&gt;_...</c> is a 100STYLE take, <c>MOB1_</c> a Mobility Pro take, everything else the older Mixamo / library takes.</summary>
        public static int FamilyOf(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return 0;
            if (clipName.StartsWith("MOB1_")) return 1;
            if (!clipName.StartsWith("S100_")) return 0;
            for (int i = 2; i < Families.Length; i++) if (clipName.Length > 5 + Families[i].Length && string.CompareOrdinal(clipName, 5, Families[i], 0, Families[i].Length) == 0 && clipName[5 + Families[i].Length] == '_') return i;
            return 0;
        }

        /// <summary>How well a 100STYLE style suits a position group (0..1): the weight of the style in the draw of the player's two favourites.</summary>
        public static float Affinity(int family, PlayerRole role)
        {
            switch (family)
            {
                case 2: return 0.8f;                                                                        // Neutral: everybody
                case 3: return role == PlayerRole.OL || role == PlayerRole.DL ? 1.6f : role == PlayerRole.TE ? 0.7f : 0.1f;      // Heavyset
                case 4: return role == PlayerRole.OL || role == PlayerRole.DL || role == PlayerRole.TE ? 1.2f : 0.4f;           // Stiff
                case 5: return role == PlayerRole.OL || role == PlayerRole.DL || role == PlayerRole.LB ? 1.0f : 0.5f;           // March
                case 6: return role == PlayerRole.WR || role == PlayerRole.DB || role == PlayerRole.RB ? 1.0f : 0.3f;           // Strutting
                case 7: return role == PlayerRole.QB || role == PlayerRole.WR || role == PlayerRole.K ? 1.4f : 0.4f;            // Proud
                case 8: return role == PlayerRole.WR || role == PlayerRole.DB || role == PlayerRole.RB ? 1.3f : 0.3f;           // Roadrunner
                case 9: return role == PlayerRole.WR || role == PlayerRole.RB || role == PlayerRole.DB ? 1.3f : 0.3f;           // Rocket
                case 10: return role == PlayerRole.DB || role == PlayerRole.LB || role == PlayerRole.WR ? 1.2f : 0.4f;          // OnToesBentForward
                case 11: return role == PlayerRole.DL || role == PlayerRole.LB || role == PlayerRole.DB ? 1.1f : 0.4f;          // BentKnees
                case 12: return role == PlayerRole.RB || role == PlayerRole.LB || role == PlayerRole.DB ? 1.0f : 0.4f;          // Rushed
                case 13: return 0.5f;                                                                       // StartStop
                default: return 0f;
            }
        }

        /// <summary>The two favourite families (100STYLE, 2..13) of a player: a weighted draw from the player's seed, the second distinct from the first.</summary>
        public static void Favourites(int seed, PlayerRole role, out int a, out int b)
        {
            a = Draw(seed, 101, role, -1); b = Draw(seed, 102, role, a);
        }

        static int Draw(int seed, int salt, PlayerRole role, int not)
        {
            float tot = 0f; for (int f = 2; f < Families.Length; f++) if (f != not) tot += Affinity(f, role);
            float u = NaturalStyle.U(seed, salt) * tot, acc = 0f;
            for (int f = 2; f < Families.Length; f++) { if (f == not) continue; acc += Affinity(f, role); if (u <= acc) return f; }
            return Families.Length - 1;
        }

        /// <summary>Personal part of the cost per database clip (constant for the player): spread + family bonus + the clip's own prior. A take and its mirror share the spread.</summary>
        public static void Personal(MotionDb db, int seed, PlayerRole role, float[] dst)
        {
            int n = db.clips.Count; int fa, fb; Favourites(seed + SeedAdd, role, out fa, out fb);
            for (int c = 0; c < n; c++)
            {
                var cl = db.clips[c];
                float v = cl.prior;
                if (Enabled)
                {
                    int key = cl.mirrorOf >= 0 && cl.mirrorOf < c ? cl.mirrorOf : c;      // the twin shares the take's draw
                    v += Spread * NaturalStyle.U(seed + SeedAdd, 1000 + key);
                    int fam = FamilyOf(cl.name);
                    if (fam == fa || fam == fb) v -= FamilyBonus;
                }
                if (Ban.Length > 0) { string nm = cl.name.EndsWith("_M") ? cl.name.Substring(0, cl.name.Length - 2) : cl.name; if (Array.IndexOf(Ban, nm) >= 0) v += 100f; }
                dst[c] = v;
            }
            // costs are relative: nothing below zero (the continuation of the playing take is compared with the same numbers)
            float mn = float.MaxValue; for (int c = 0; c < n; c++) if (dst[c] < mn) mn = dst[c];
            if (mn < 0f) for (int c = 0; c < n; c++) dst[c] -= mn;
        }

        // ------------------------------------------------------------------------------------------ recent use (shared by every matcher of the stadium)
        static float[] _recent; static float _stamp; static MotionDb _recentDb;

        public static void NoteUse(MotionDb db, int clip)
        {
            if (!Enabled || RecentK <= 0f) return;
            Decay(db);
            if (clip >= 0 && clip < _recent.Length) _recent[clip] = Mathf.Min(RecentCap, _recent[clip] + 1f);
        }

        static void Decay(MotionDb db)
        {
            if (_recentDb != db || _recent == null || _recent.Length != db.clips.Count) { _recentDb = db; _recent = new float[db.clips.Count]; _stamp = Time.time; return; }
            float dt = Time.time - _stamp;
            if (dt < 0f) { Array.Clear(_recent, 0, _recent.Length); _stamp = Time.time; return; }
            if (dt < 0.25f) return;
            float f = Mathf.Exp(-dt / RecentTau); for (int i = 0; i < _recent.Length; i++) _recent[i] *= f;
            _stamp = Time.time;
        }

        /// <summary>Total extra cost per clip for one search: the player's personal part plus the recent-use cost.</summary>
        public static void Total(MotionDb db, float[] personal, float[] dst)
        {
            if (!Enabled || RecentK <= 0f) { Array.Copy(personal, dst, personal.Length); return; }
            Decay(db);
            for (int c = 0; c < dst.Length; c++) dst[c] = personal[c] + RecentK * _recent[c];
        }

        public static void ResetRecent() { _recent = null; _recentDb = null; }
    }
}
