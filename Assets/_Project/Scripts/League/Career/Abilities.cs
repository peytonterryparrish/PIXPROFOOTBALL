using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>
    /// What an ability changes in the match simulation. Since v0.8.0 the Sim reads every hook (PlaySimulation.Abilities.cs, AbilityFx.cs; table in Docs/QA/v080/sim_abilities.md):
    /// each hook names one number in the Sim that an ability nudges, see <see cref="AbilityCatalog.Modifier"/>.
    /// </summary>
    public enum SimHook
    {
        // passing
        ThrowPower,             // effective ThrowPower rating (points)
        ThrowAccuracy,          // effective ThrowAccuracy rating (points) in the effect's context
        DeepAccuracy,           // accuracy bonus on deep throws
        PocketEscape,           // percent less likely to be sacked / more likely to break the rush
        ScrambleSpeed,          // QB run speed (points of Speed)
        // ball carrying
        BreakTackle,            // effective BreakTackle rating (points)
        JukeSuccess,            // percent added to a juke / ankle-breaker success roll
        SpinSuccess,            // percent added to a spin move success roll
        TopSpeed,               // points of Speed in the open field
        Acceleration,           // points of Acceleration
        CutSpeedLoss,           // percent change of the speed lost in a cut (negative = keeps more speed)
        FatigueRate,            // percent change of fatigue build-up (negative = tires slower)
        ShortYardage,           // percent added to the success of 1-2 yard runs
        FumbleResist,           // percent change of fumble chance (negative = safer)
        // receiving
        CatchInTraffic,         // catch rating points when a defender is within a yard
        ContestedCatch,         // jump-ball / contested catch rating points
        RouteSeparation,        // yards of separation at the top of the route (percent scale)
        YardsAfterCatch,        // percent more yards after the catch
        ReturnSpeed,            // points of Speed on kick / punt returns
        ReturnElusiveness,      // points of Elusiveness on returns
        // blocking
        RunBlock,               // effective RunBlocking points
        PassBlock,              // effective PassBlocking points
        PullSpeed,              // points of Speed while pulling
        BullRushResist,         // percent less effective power rushes against this blocker
        // defense
        PassRush,               // effective rush-move success (points of BlockShedding)
        RunStop,                // effective run defence (points of Tackling / BlockShedding)
        ForcedFumble,           // percent added to the strip / forced fumble chance on a tackle
        HitPower,               // percent added to the hit-stick knock-back
        BlitzTiming,            // percent faster blitz arrival
        ManCoverage,            // effective ManCoverage points
        ZoneRange,              // effective ZoneCoverage points / reaction radius
        PressCoverage,          // effective ManCoverage points at the line of scrimmage
        InterceptionCatch,      // percent of deflections turned into interceptions
        TackleReliability,      // percent fewer missed tackles
        // kicking
        KickPower,              // KickPower points
        KickAccuracyPressure,   // KickAccuracy points with the game on the line
        // universal
        InjuryResist,           // percent change of the injury roll (negative = fewer injuries)
        Clutch,                 // rating points on every action in the fourth quarter of a close game
        Leadership              // team morale points per week
    }

    public enum AbilityContext
    {
        Always, ThirdFourthDown, TwoMinute, RedZone, UnderPressure, OverMiddle, OpenField, ShortYardage, Deep, CloseGameLate, Blitz, LineOfScrimmage, Return, ManToMan
    }

    [Serializable]
    public class PlayerAbility
    {
        public string Id = "";
        /// <summary>1 to 3 stars.</summary>
        public int Tier = 1;
    }

    public sealed class AbilityEffect
    {
        public SimHook Hook;
        public AbilityContext Context;
        /// <summary>The effect size at tier 1, 2 and 3 (rating points or percent, see the hook).</summary>
        public float[] PerTier;
        public AbilityEffect(SimHook hook, AbilityContext ctx, float t1, float t2, float t3)
        {
            Hook = hook; Context = ctx; PerTier = new[] { t1, t2, t3 };
        }
        public float At(int tier) { return tier <= 0 ? 0f : PerTier[Math.Min(3, tier) - 1]; }
    }

    public sealed class AbilityDef
    {
        public string Id, Name, Description;
        public PositionBucket[] Buckets;
        public Archetype[] Archetypes;
        public RatingKind[] KeyRatings;
        /// <summary>Average of the key ratings needed for tier 1, 2 and 3.</summary>
        public int[] Thresholds;
        public AbilityEffect[] Effects;
        /// <summary>Tier 3 needs at least this overall.</summary>
        public int MinOverallTier3 = 84;

        public bool AppliesTo(Position p)
        {
            PositionBucket b = PositionBuckets.Of(p);
            for (int i = 0; i < Buckets.Length; i++) if (Buckets[i] == b) return true;
            return false;
        }

        public bool Favours(Archetype a)
        {
            for (int i = 0; i < Archetypes.Length; i++) if (Archetypes[i] == a) return true;
            return false;
        }

        /// <summary>Highest tier (0-3) the player's ratings support.</summary>
        public int TierFor(Player p)
        {
            float sum = 0f;
            for (int i = 0; i < KeyRatings.Length; i++) sum += p.Ratings.Get(KeyRatings[i]);
            float avg = sum / KeyRatings.Length;
            int tier = 0;
            for (int i = 0; i < Thresholds.Length; i++) if (avg >= Thresholds[i]) tier = i + 1;
            if (tier >= 3 && p.Overall < MinOverallTier3) tier = 2;
            if (tier >= 2 && p.Overall < 68) tier = 1;
            return tier;
        }
    }

    /// <summary>
    /// The ability (trait) catalogue, 2K-style but with our own names: special abilities with three star tiers that change how a player
    /// behaves on the field. Regens get abilities from their archetype and ratings, and unlock or lose tiers as their ratings move.
    /// </summary>
    public static class AbilityCatalog
    {
        private static readonly List<AbilityDef> _all = Build();
        private static readonly Dictionary<string, AbilityDef> _byId = Index(_all);

        public static IReadOnlyList<AbilityDef> All { get { return _all; } }
        public static int Count { get { return _all.Count; } }

        public static AbilityDef Get(string id)
        {
            AbilityDef d;
            return id != null && _byId.TryGetValue(id, out d) ? d : null;
        }

        private static Dictionary<string, AbilityDef> Index(List<AbilityDef> l)
        {
            var d = new Dictionary<string, AbilityDef>();
            for (int i = 0; i < l.Count; i++) d[l[i].Id] = l[i];
            return d;
        }

        public static string Stars(int tier) { return tier <= 0 ? "" : (tier == 1 ? "*" : (tier == 2 ? "**" : "***")); }

        // ---------------------------------------------------------------- the data
        private static PositionBucket[] B(params PositionBucket[] b) { return b; }
        private static Archetype[] A(params Archetype[] a) { return a; }
        private static RatingKind[] K(params RatingKind[] k) { return k; }
        private static AbilityEffect E(SimHook h, AbilityContext c, float a, float b, float d) { return new AbilityEffect(h, c, a, b, d); }

        private static AbilityDef D(string id, string name, string desc, PositionBucket[] buckets, Archetype[] fav, RatingKind[] keys, int t1, int t2, int t3, params AbilityEffect[] fx)
        {
            return new AbilityDef { Id = id, Name = name, Description = desc, Buckets = buckets, Archetypes = fav, KeyRatings = keys, Thresholds = new[] { t1, t2, t3 }, Effects = fx };
        }

        private static List<AbilityDef> Build()
        {
            var l = new List<AbilityDef>();
            const AbilityContext Al = AbilityContext.Always;
            PositionBucket QB = PositionBucket.QB, RB = PositionBucket.RB, WR = PositionBucket.WR, TE = PositionBucket.TE, OL = PositionBucket.OL,
                DL = PositionBucket.DL, LB = PositionBucket.LB, CB = PositionBucket.CB, S = PositionBucket.S, bK = PositionBucket.K, bP = PositionBucket.P, FB = PositionBucket.FB;

            // ---- quarterbacks
            l.Add(D("rocket_arm", "Rocket Arm", "Throws the ball harder than anyone: more velocity on every throw.", B(QB), A(Archetype.QbGunslinger), K(RatingKind.ThrowPower), 82, 89, 94,
                E(SimHook.ThrowPower, Al, 2, 4, 6)));
            l.Add(D("cool_under_fire", "Cool Under Fire", "Keeps his accuracy when the pocket collapses.", B(QB), A(Archetype.QbPocket), K(RatingKind.Awareness, RatingKind.ThrowAccuracy), 78, 85, 91,
                E(SimHook.ThrowAccuracy, AbilityContext.UnderPressure, 3, 5, 8)));
            l.Add(D("money_down", "Money Down", "Raises his game on third and fourth down.", B(QB), A(Archetype.QbPocket, Archetype.QbGunslinger), K(RatingKind.Awareness, RatingKind.ThrowAccuracy), 76, 84, 90,
                E(SimHook.ThrowAccuracy, AbilityContext.ThirdFourthDown, 3, 5, 7), E(SimHook.Clutch, AbilityContext.ThirdFourthDown, 1, 2, 3)));
            l.Add(D("two_minute_general", "Two-Minute General", "Deadly with the clock running down at the end of a half.", B(QB), A(Archetype.QbPocket), K(RatingKind.Awareness, RatingKind.ThrowAccuracy), 77, 85, 91,
                E(SimHook.ThrowAccuracy, AbilityContext.TwoMinute, 2, 4, 6)));
            l.Add(D("escape_artist", "Escape Artist", "Slips out of sacks and scrambles for yards.", B(QB), A(Archetype.QbScrambler), K(RatingKind.Speed, RatingKind.Agility, RatingKind.Elusiveness), 74, 82, 89,
                E(SimHook.PocketEscape, Al, 8, 14, 20), E(SimHook.ScrambleSpeed, Al, 1, 2, 3)));
            l.Add(D("deep_touch", "Deep Touch", "Drops long passes right into the receiver's hands.", B(QB), A(Archetype.QbGunslinger), K(RatingKind.ThrowAccuracy, RatingKind.ThrowPower), 78, 85, 91,
                E(SimHook.DeepAccuracy, AbilityContext.Deep, 3, 6, 9)));

            // ---- running backs / fullbacks
            l.Add(D("wrecking_ball", "Wrecking Ball", "Runs through arm tackles like they are not there.", B(RB, FB), A(Archetype.RbPower), K(RatingKind.BreakTackle, RatingKind.Strength), 78, 85, 91,
                E(SimHook.BreakTackle, Al, 4, 7, 10), E(SimHook.FumbleResist, Al, -5, -10, -15)));
            l.Add(D("ankle_shaker", "Ankle Shaker", "Jukes defenders out of their shoes.", B(RB, WR, TE), A(Archetype.RbSpeed, Archetype.RbAllPurpose, Archetype.WrSlot), K(RatingKind.Elusiveness, RatingKind.Agility), 80, 87, 93,
                E(SimHook.JukeSuccess, Al, 6, 11, 16)));
            l.Add(D("whirlwind", "Whirlwind", "A spin move that leaves tacklers grabbing air.", B(RB, WR), A(Archetype.RbSpeed, Archetype.RbAllPurpose), K(RatingKind.Elusiveness, RatingKind.Agility), 79, 86, 92,
                E(SimHook.SpinSuccess, Al, 8, 14, 20)));
            l.Add(D("home_run_burst", "Home Run Burst", "Gets to top speed in a blink once he is in the open field.", B(RB, WR, CB), A(Archetype.RbSpeed, Archetype.WrDeepThreat), K(RatingKind.Speed, RatingKind.Acceleration), 86, 91, 95,
                E(SimHook.TopSpeed, AbilityContext.OpenField, 1, 2, 3), E(SimHook.Acceleration, AbilityContext.OpenField, 2, 3, 4)));
            l.Add(D("stop_on_a_dime", "Stop On A Dime", "Plants and cuts without losing speed.", B(RB, WR, CB, S), A(Archetype.RbSpeed, Archetype.WrSlot, Archetype.CbManLockdown), K(RatingKind.Agility, RatingKind.Acceleration), 82, 88, 93,
                E(SimHook.CutSpeedLoss, Al, -12, -20, -30)));
            l.Add(D("workhorse", "Workhorse", "Carries the ball all day without wearing down.", B(RB, FB), A(Archetype.RbPower, Archetype.RbAllPurpose), K(RatingKind.Stamina, RatingKind.Strength), 76, 83, 89,
                E(SimHook.FatigueRate, Al, -10, -18, -25)));
            l.Add(D("short_yardage_bruiser", "Short-Yardage Bruiser", "Moves the pile when a yard is all that is needed.", B(RB, FB, QB), A(Archetype.RbPower, Archetype.FbBlocker), K(RatingKind.Strength, RatingKind.Carrying), 78, 85, 91,
                E(SimHook.ShortYardage, AbilityContext.ShortYardage, 6, 10, 15)));

            // ---- receivers / tight ends
            l.Add(D("burner", "Burner", "Runs by corners on go routes and deep posts.", B(WR), A(Archetype.WrDeepThreat), K(RatingKind.Speed, RatingKind.Acceleration), 87, 91, 95,
                E(SimHook.TopSpeed, AbilityContext.Deep, 1, 2, 3), E(SimHook.RouteSeparation, AbilityContext.Deep, 4, 8, 12)));
            l.Add(D("sticky_hands", "Sticky Hands", "Hangs on to the ball even with a defender draped on him.", B(WR, TE, RB), A(Archetype.WrPossession, Archetype.TeReceiver), K(RatingKind.Catching, RatingKind.Awareness), 80, 87, 92,
                E(SimHook.CatchInTraffic, Al, 4, 7, 11)));
            l.Add(D("crisp_routes", "Crisp Routes", "Sharp breaks create separation on every route.", B(WR, TE), A(Archetype.WrSlot, Archetype.WrPossession), K(RatingKind.Agility, RatingKind.Awareness), 80, 86, 92,
                E(SimHook.RouteSeparation, Al, 3, 6, 9)));
            l.Add(D("sky_walker", "Sky Walker", "Wins the jump ball over defenders.", B(WR, TE), A(Archetype.WrDeepThreat, Archetype.TeReceiver), K(RatingKind.Jumping, RatingKind.Catching), 80, 87, 92,
                E(SimHook.ContestedCatch, Al, 4, 8, 12)));
            l.Add(D("yards_machine", "Yards Machine", "Turns a short catch into a long gain.", B(WR, TE, RB), A(Archetype.WrSlot, Archetype.RbAllPurpose), K(RatingKind.Elusiveness, RatingKind.BreakTackle, RatingKind.Agility), 76, 83, 89,
                E(SimHook.YardsAfterCatch, Al, 5, 9, 14)));
            l.Add(D("fearless_middle", "Fearless Middle", "Does not flinch going over the middle.", B(WR, TE), A(Archetype.WrPossession, Archetype.TeReceiver), K(RatingKind.Catching, RatingKind.Strength), 74, 81, 88,
                E(SimHook.CatchInTraffic, AbilityContext.OverMiddle, 5, 9, 13)));
            l.Add(D("seal_specialist", "Seal Specialist", "Seals the edge on run plays.", B(TE, FB), A(Archetype.TeBlocker, Archetype.FbBlocker), K(RatingKind.RunBlocking, RatingKind.Strength), 78, 85, 91,
                E(SimHook.RunBlock, Al, 3, 5, 8)));
            l.Add(D("return_ace", "Return Ace", "A weapon on kick and punt returns.", B(WR, RB, CB), A(Archetype.WrSlot, Archetype.RbSpeed), K(RatingKind.Speed, RatingKind.Elusiveness, RatingKind.Agility), 80, 86, 92,
                E(SimHook.ReturnSpeed, AbilityContext.Return, 1, 2, 3), E(SimHook.ReturnElusiveness, AbilityContext.Return, 4, 7, 10)));

            // ---- offensive line
            l.Add(D("road_grader", "Road Grader", "Pushes defenders off the ball in the run game.", B(OL), A(Archetype.OlRunMauler), K(RatingKind.RunBlocking, RatingKind.Strength), 80, 87, 93,
                E(SimHook.RunBlock, Al, 3, 6, 9)));
            l.Add(D("immovable_anchor", "Immovable Anchor", "Bull rushes go nowhere against him.", B(OL), A(Archetype.OlPassProtector, Archetype.OlBalanced), K(RatingKind.PassBlocking, RatingKind.Strength), 80, 87, 93,
                E(SimHook.BullRushResist, Al, 6, 10, 15), E(SimHook.PassBlock, Al, 1, 3, 5)));
            l.Add(D("lead_puller", "Lead Puller", "Pulls around the end and clears the lane.", B(OL), A(Archetype.OlRunMauler), K(RatingKind.RunBlocking, RatingKind.Agility, RatingKind.Speed), 70, 77, 84,
                E(SimHook.PullSpeed, Al, 1, 2, 3), E(SimHook.RunBlock, AbilityContext.OpenField, 2, 4, 6)));

            // ---- defensive line / linebackers
            l.Add(D("swim_master", "Swim Master", "A pass rusher with a dangerous swim and spin.", B(DL, LB), A(Archetype.DlPassRusher, Archetype.LbBlitzer), K(RatingKind.BlockShedding, RatingKind.Agility, RatingKind.Acceleration), 78, 85, 91,
                E(SimHook.PassRush, Al, 3, 6, 9)));
            l.Add(D("bull_rusher", "Bull Rusher", "Drives tackles straight back into the quarterback.", B(DL), A(Archetype.DlPassRusher, Archetype.DlBalanced), K(RatingKind.Strength, RatingKind.BlockShedding), 82, 88, 93,
                E(SimHook.PassRush, AbilityContext.UnderPressure, 3, 5, 8)));
            l.Add(D("gap_plugger", "Gap Plugger", "Closes running lanes and shrugs off blockers.", B(DL, LB), A(Archetype.DlRunStuffer, Archetype.LbRunStopper), K(RatingKind.Strength, RatingKind.Tackling), 80, 86, 92,
                E(SimHook.RunStop, Al, 4, 7, 11)));
            l.Add(D("strip_artist", "Strip Artist", "Knocks the ball loose on tackles.", B(DL, LB, S, CB), A(Archetype.DlPassRusher, Archetype.LbBlitzer), K(RatingKind.Tackling, RatingKind.BlockShedding), 74, 81, 88,
                E(SimHook.ForcedFumble, Al, 10, 18, 28)));
            l.Add(D("bone_rattler", "Bone Rattler", "Hits so hard ball carriers remember it.", B(LB, S), A(Archetype.LbRunStopper, Archetype.SBox), K(RatingKind.Tackling, RatingKind.Strength), 80, 86, 92,
                E(SimHook.HitPower, Al, 10, 18, 26), E(SimHook.ForcedFumble, Al, 5, 9, 14)));
            l.Add(D("sniper_blitz", "Sniper Blitz", "Times the snap and arrives untouched.", B(LB, S, CB), A(Archetype.LbBlitzer, Archetype.SBox), K(RatingKind.Speed, RatingKind.Acceleration, RatingKind.Awareness), 78, 85, 91,
                E(SimHook.BlitzTiming, AbilityContext.Blitz, 6, 10, 15), E(SimHook.PassRush, AbilityContext.Blitz, 2, 4, 6)));
            l.Add(D("sideline_to_sideline", "Sideline To Sideline", "Covers the whole field in coverage and pursuit.", B(LB), A(Archetype.LbCoverage), K(RatingKind.Speed, RatingKind.ZoneCoverage, RatingKind.Tackling), 78, 84, 90,
                E(SimHook.ZoneRange, Al, 3, 5, 8)));

            // ---- defensive backs
            l.Add(D("shutdown_shadow", "Shutdown Shadow", "Follows the best receiver everywhere and wins the matchup.", B(CB), A(Archetype.CbManLockdown), K(RatingKind.ManCoverage, RatingKind.Speed), 82, 88, 93,
                E(SimHook.ManCoverage, AbilityContext.ManToMan, 3, 6, 9)));
            l.Add(D("ball_hawk", "Ball Hawk", "Turns deflections into interceptions.", B(CB, S, LB), A(Archetype.CbZone, Archetype.SCenterfield), K(RatingKind.Awareness, RatingKind.Catching, RatingKind.ZoneCoverage), 76, 83, 89,
                E(SimHook.InterceptionCatch, Al, 10, 18, 28)));
            l.Add(D("range_rover", "Range Rover", "Reads the quarterback and covers deep zones from hash to hash.", B(S, CB), A(Archetype.SCenterfield, Archetype.CbZone), K(RatingKind.Speed, RatingKind.ZoneCoverage, RatingKind.Awareness), 80, 86, 92,
                E(SimHook.ZoneRange, Al, 3, 6, 9)));
            l.Add(D("jam_master", "Jam Master", "Shuts down the release at the line of scrimmage.", B(CB, S), A(Archetype.CbManLockdown, Archetype.CbSlot), K(RatingKind.Strength, RatingKind.ManCoverage), 76, 83, 89,
                E(SimHook.PressCoverage, AbilityContext.LineOfScrimmage, 3, 6, 9)));
            l.Add(D("sure_tackler", "Sure Tackler", "Almost never misses a tackle.", B(LB, S, CB, DL), A(Archetype.SBox, Archetype.LbRunStopper), K(RatingKind.Tackling, RatingKind.Awareness), 80, 86, 92,
                E(SimHook.TackleReliability, Al, 8, 14, 20)));

            // ---- kicking
            l.Add(D("ice_in_veins", "Ice In The Veins", "Never rattled: kicks true with the game on the line.", B(bK, bP), A(Archetype.KPlacekicker, Archetype.PPunter), K(RatingKind.KickAccuracy, RatingKind.Awareness), 78, 85, 91,
                E(SimHook.KickAccuracyPressure, AbilityContext.CloseGameLate, 4, 7, 10)));
            l.Add(D("boomer", "Boomer", "A leg that reaches from midfield.", B(bK, bP), A(Archetype.KPlacekicker, Archetype.PPunter), K(RatingKind.KickPower), 84, 90, 95,
                E(SimHook.KickPower, Al, 2, 4, 6)));

            // ---- everybody
            l.Add(D("iron_man", "Iron Man", "Shakes off knocks: far less likely to get hurt.", B(QB, RB, FB, WR, TE, OL, DL, LB, CB, S), A(), K(RatingKind.Injury, RatingKind.Stamina), 82, 88, 93,
                E(SimHook.InjuryResist, Al, -15, -25, -35)));
            l.Add(D("clutch_gene", "Clutch Gene", "Plays his best when the game is tight in the fourth quarter.", B(QB, RB, WR, TE, CB, S, LB, DL), A(), K(RatingKind.Awareness), 80, 86, 92,
                E(SimHook.Clutch, AbilityContext.CloseGameLate, 2, 4, 6)));
            l.Add(D("locker_room_leader", "Locker Room Leader", "The team plays with more confidence when he is around.", B(QB, RB, WR, TE, OL, DL, LB, CB, S), A(), K(RatingKind.Awareness), 82, 88, 93,
                E(SimHook.Leadership, Al, 1, 2, 3)));
            return l;
        }

        // ---------------------------------------------------------------- queries
        public static List<AbilityDef> AvailableFor(Position pos)
        {
            var l = new List<AbilityDef>();
            for (int i = 0; i < _all.Count; i++) if (_all[i].AppliesTo(pos)) l.Add(_all[i]);
            return l;
        }

        public static PlayerAbility Find(Player p, string id)
        {
            for (int i = 0; i < p.Abilities.Count; i++) if (p.Abilities[i].Id == id) return p.Abilities[i];
            return null;
        }

        public static int TierOf(Player p, string id) { PlayerAbility a = Find(p, id); return a != null ? a.Tier : 0; }
        public static bool Has(Player p, string id) { return Find(p, id) != null; }

        /// <summary>Sum of the player's ability effects for a hook in a context (Always effects always count; contextual ones only for the matching context).</summary>
        public static float Modifier(Player p, SimHook hook, AbilityContext context = AbilityContext.Always)
        {
            float sum = 0f;
            for (int i = 0; i < p.Abilities.Count; i++)
            {
                AbilityDef d = Get(p.Abilities[i].Id);
                if (d == null) continue;
                for (int e = 0; e < d.Effects.Length; e++)
                {
                    AbilityEffect fx = d.Effects[e];
                    if (fx.Hook != hook) continue;
                    if (fx.Context != AbilityContext.Always && fx.Context != context) continue;
                    sum += fx.At(p.Abilities[i].Tier);
                }
            }
            return sum;
        }

        /// <summary>Flat (hook, context, value) list for caching in the Sim agent at kickoff.</summary>
        public static List<KeyValuePair<string, float>> ModifierTable(Player p)
        {
            var l = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < p.Abilities.Count; i++)
            {
                AbilityDef d = Get(p.Abilities[i].Id);
                if (d == null) continue;
                for (int e = 0; e < d.Effects.Length; e++)
                    l.Add(new KeyValuePair<string, float>(d.Effects[e].Hook + "/" + d.Effects[e].Context, d.Effects[e].At(p.Abilities[i].Tier)));
            }
            return l;
        }

        /// <summary>
        /// Calibration gain per hook: the size the match actually applies is the catalogue size times this factor (so a league full of abilities scores like the plain league,
        /// see Docs/QA/v080/sim_abilities.md). The Sim (<c>AbilityFx.Scale</c>) and the player card (<see cref="Describe"/>) both read it, so the card tells the truth.
        /// </summary>
        public static float Gain(SimHook hook, AbilityContext ctx = AbilityContext.Always)
        {
            switch (hook)
            {
                case SimHook.TopSpeed: case SimHook.Acceleration: return ctx == AbilityContext.OpenField ? 0.8f : 0.4f;
                case SimHook.YardsAfterCatch: return 0.4f;
                case SimHook.BreakTackle: case SimHook.JukeSuccess: case SimHook.SpinSuccess:
                case SimHook.CatchInTraffic: case SimHook.ContestedCatch:
                case SimHook.CutSpeedLoss: case SimHook.RouteSeparation: return 0.5f;
                case SimHook.RunBlock: case SimHook.PassBlock: return 0.4f;
                case SimHook.BullRushResist: return 0.8f;
                case SimHook.PassRush: case SimHook.RunStop: case SimHook.TackleReliability: case SimHook.ManCoverage: case SimHook.ZoneRange: return 1.5f;
                case SimHook.InterceptionCatch: return 0.45f;
                default: return 1f;
            }
        }

        /// <summary>"Rocket Arm ** - Throws the ball harder (+4 ThrowPower)" for player cards. The numbers are the in-match sizes (catalogue size times <see cref="Gain"/>).</summary>
        public static string Describe(PlayerAbility a)
        {
            AbilityDef d = Get(a.Id);
            if (d == null) return a.Id;
            var sb = new StringBuilder();
            sb.Append(d.Name).Append(' ').Append(Stars(a.Tier)).Append(" - ").Append(d.Description);
            for (int e = 0; e < d.Effects.Length; e++)
            {
                AbilityEffect fx = d.Effects[e];
                float v = fx.At(a.Tier) * Gain(fx.Hook, fx.Context);
                sb.Append(e == 0 ? " (" : ", ").Append(v >= 0 ? "+" : "").Append(v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(fx.Hook);
                if (fx.Context != AbilityContext.Always) sb.Append(" [").Append(fx.Context).Append(']');
            }
            if (d.Effects.Length > 0) sb.Append(')');
            return sb.ToString();
        }

        // ---------------------------------------------------------------- assignment
        /// <summary>How many abilities a player of this overall can hold.</summary>
        public static int SlotsFor(int overall, DevTrait dev)
        {
            int slots = overall >= 90 ? 4 : (overall >= 84 ? 3 : (overall >= 76 ? 2 : (overall >= 68 ? 1 : 0)));
            if (dev == DevTrait.Superstar && slots < 4 && overall >= 72) slots++;
            return slots;
        }

        /// <summary>Gives a new regen his starting abilities from archetype and ratings (deterministic for a given rng state).</summary>
        public static void AssignInitial(Player p, Rng rng)
        {
            p.Abilities.Clear();
            int ovr = p.Overall;
            int slots = SlotsFor(ovr, p.Dev);
            if (slots == 0 && ovr >= 62 && rng.Chance(0.3f)) slots = 1;   // role players sometimes have one trick
            if (slots > 0 && rng.Chance(0.3f)) slots--;
            if (slots <= 0) return;
            var cands = new List<KeyValuePair<AbilityDef, float>>();
            for (int i = 0; i < _all.Count; i++)
            {
                AbilityDef d = _all[i];
                if (!d.AppliesTo(p.Position)) continue;
                int tier = d.TierFor(p);
                if (tier <= 0) continue;
                float w = tier * tier * (d.Favours(p.Archetype) ? 2.5f : 1f);
                if (d.Archetypes.Length == 0) w *= 0.6f;
                cands.Add(new KeyValuePair<AbilityDef, float>(d, w));
            }
            for (int n = 0; n < slots && cands.Count > 0; n++)
            {
                float total = 0f;
                for (int i = 0; i < cands.Count; i++) total += cands[i].Value;
                float r = rng.NextFloat() * total;
                int pick = cands.Count - 1;
                for (int i = 0; i < cands.Count; i++) { r -= cands[i].Value; if (r <= 0f) { pick = i; break; } }
                AbilityDef chosen = cands[pick].Key;
                cands.RemoveAt(pick);
                int tier = chosen.TierFor(p);
                // a freshly generated player usually holds the top tier he qualifies for minus a step
                if (tier > 1 && rng.Chance(0.35f)) tier--;
                p.Abilities.Add(new PlayerAbility { Id = chosen.Id, Tier = tier });
            }
        }

        /// <summary>
        /// Offseason progression of abilities after the ratings moved: held abilities lose or gain a tier with the ratings, and a player can
        /// unlock a new one. Returns a short description of what changed ("" when nothing did).
        /// </summary>
        public static string Progress(Player p, Rng rng, int devBonus = 0)
        {
            var sb = new StringBuilder();
            int ovr = p.Overall;
            for (int i = p.Abilities.Count - 1; i >= 0; i--)
            {
                PlayerAbility a = p.Abilities[i];
                AbilityDef d = Get(a.Id);
                if (d == null || !d.AppliesTo(p.Position)) { p.Abilities.RemoveAt(i); continue; }
                int max = d.TierFor(p);
                if (max <= 0)
                {
                    if (rng.Chance(0.7f)) { sb.Append("lost ").Append(d.Name).Append("; "); p.Abilities.RemoveAt(i); }
                }
                else if (a.Tier > max)
                {
                    if (rng.Chance(0.8f)) { a.Tier--; sb.Append(d.Name).Append(" down to ").Append(Stars(a.Tier)).Append("; "); }
                }
                else if (a.Tier < max && rng.Chance(0.28f + devBonus * 0.01f))
                {
                    a.Tier++; sb.Append(d.Name).Append(" up to ").Append(Stars(a.Tier)).Append("; ");
                }
            }
            int slots = SlotsFor(ovr, p.Dev);
            if (p.Abilities.Count < slots && rng.Chance(0.3f + (p.Dev == DevTrait.Star ? 0.1f : 0f) + (p.Dev == DevTrait.Superstar ? 0.2f : 0f) + devBonus * 0.01f))
            {
                var cands = new List<AbilityDef>();
                for (int i = 0; i < _all.Count; i++)
                {
                    AbilityDef d = _all[i];
                    if (!d.AppliesTo(p.Position) || Has(p, d.Id) || d.TierFor(p) <= 0) continue;
                    cands.Add(d);
                }
                if (cands.Count > 0)
                {
                    AbilityDef pick = cands[rng.Range(0, cands.Count)];
                    for (int i = 0; i < cands.Count; i++) if (cands[i].Favours(p.Archetype) && rng.Chance(0.5f)) { pick = cands[i]; break; }
                    p.Abilities.Add(new PlayerAbility { Id = pick.Id, Tier = 1 });
                    sb.Append("unlocked ").Append(pick.Name).Append("; ");
                }
            }
            return sb.ToString().TrimEnd(' ', ';');
        }

        /// <summary>Abilities a scouting report can show for a prospect: only after enough scouting points were spent on him.</summary>
        public static List<PlayerAbility> ScoutedHints(Player prospect, int pointsSpent)
        {
            var l = new List<PlayerAbility>();
            if (pointsSpent < 20) return l;
            for (int i = 0; i < prospect.Abilities.Count; i++)
            {
                PlayerAbility a = prospect.Abilities[i];
                if (pointsSpent >= 60 || a.Tier == 1 || pointsSpent >= 40 && a.Tier == 2) l.Add(new PlayerAbility { Id = a.Id, Tier = pointsSpent >= 60 ? a.Tier : 1 });
            }
            return l;
        }
    }
}
