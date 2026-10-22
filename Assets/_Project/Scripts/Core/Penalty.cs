using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    public enum PenaltyType
    {
        FalseStart, Offside, Encroachment, DelayOfGame,
        OffensiveHolding, DefensiveHolding,
        OffensivePassInterference, DefensivePassInterference,
        IllegalBlockInBack, FaceMask, RoughingThePasser, UnnecessaryRoughness,
        IllegalFormation, IntentionalGrounding, IllegalContact, TooManyMen
    }

    public enum PenaltySide { Offense, Defense, Either }

    public enum PenaltyDecision { Auto, Accept, Decline }

    /// <summary>Catalog entry describing a penalty type. Immutable; look up with <see cref="Get"/>.</summary>
    public sealed class Penalty
    {
        public readonly PenaltyType Type;
        public readonly string Name;
        public readonly PenaltySide Side;
        public readonly int ProYards;
        public readonly int CollegeYards;
        /// <summary>Automatic first down when committed by the defense.</summary>
        public readonly bool AutomaticFirstDown;
        public readonly bool LossOfDown;
        /// <summary>Enforced from the spot of the foul rather than the previous spot.</summary>
        public readonly bool SpotFoul;
        public readonly bool PreSnap;
        public readonly bool DeadBall;

        private Penalty(PenaltyType type, string name, PenaltySide side, int pro, int college,
            bool autoFirst, bool lossOfDown, bool spotFoul, bool preSnap)
        {
            Type = type; Name = name; Side = side; ProYards = pro; CollegeYards = college;
            AutomaticFirstDown = autoFirst; LossOfDown = lossOfDown; SpotFoul = spotFoul; PreSnap = preSnap;
            DeadBall = preSnap;
        }

        private static readonly Dictionary<PenaltyType, Penalty> Catalog = new Dictionary<PenaltyType, Penalty>();
        private static readonly List<Penalty> AllList = new List<Penalty>();

        static Penalty()
        {
            Add(PenaltyType.FalseStart, "False Start", PenaltySide.Offense, 5, 5, false, false, false, true);
            Add(PenaltyType.Offside, "Offside", PenaltySide.Defense, 5, 5, false, false, false, true);
            Add(PenaltyType.Encroachment, "Encroachment", PenaltySide.Defense, 5, 5, false, false, false, true);
            Add(PenaltyType.DelayOfGame, "Delay of Game", PenaltySide.Offense, 5, 5, false, false, false, true);
            Add(PenaltyType.OffensiveHolding, "Offensive Holding", PenaltySide.Offense, 10, 10, false, false, false, false);
            Add(PenaltyType.DefensiveHolding, "Defensive Holding", PenaltySide.Defense, 5, 10, true, false, false, false);
            Add(PenaltyType.OffensivePassInterference, "Offensive Pass Interference", PenaltySide.Offense, 10, 15, false, false, false, false);
            Add(PenaltyType.DefensivePassInterference, "Defensive Pass Interference", PenaltySide.Defense, 0, 15, true, false, true, false);
            Add(PenaltyType.IllegalBlockInBack, "Illegal Block in the Back", PenaltySide.Offense, 10, 10, false, false, true, false);
            Add(PenaltyType.FaceMask, "Face Mask", PenaltySide.Either, 15, 15, true, false, false, false);
            Add(PenaltyType.RoughingThePasser, "Roughing the Passer", PenaltySide.Defense, 15, 15, true, false, false, false);
            Add(PenaltyType.UnnecessaryRoughness, "Unnecessary Roughness", PenaltySide.Either, 15, 15, true, false, false, false);
            Add(PenaltyType.IllegalFormation, "Illegal Formation", PenaltySide.Offense, 5, 5, false, false, false, true);
            Add(PenaltyType.IntentionalGrounding, "Intentional Grounding", PenaltySide.Offense, 10, 10, false, true, true, false);
            Add(PenaltyType.IllegalContact, "Illegal Contact", PenaltySide.Defense, 5, 5, true, false, false, false);
            Add(PenaltyType.TooManyMen, "Too Many Men on the Field", PenaltySide.Either, 5, 5, false, false, false, true);
        }

        private static void Add(PenaltyType t, string name, PenaltySide side, int pro, int col,
            bool auto, bool lod, bool spot, bool pre)
        {
            var p = new Penalty(t, name, side, pro, col, auto, lod, spot, pre);
            Catalog[t] = p;
            AllList.Add(p);
        }

        public static Penalty Get(PenaltyType type) { return Catalog[type]; }
        public static IReadOnlyList<Penalty> All { get { return AllList; } }

        /// <summary>
        /// Base yardage for this foul under the given rules. <paramref name="foulSpot"/> is the yards past
        /// the line of scrimmage where the foul occurred (negative = behind it); used by spot fouls.
        /// </summary>
        public int YardsFor(RuleSet rules, float foulSpot)
        {
            switch (Type)
            {
                case PenaltyType.DefensivePassInterference:
                {
                    int y = Math.Max(1, (int)Math.Round(foulSpot, MidpointRounding.AwayFromZero));
                    if (!rules.DpiSpotFoul && rules.DpiMaxYards > 0) return rules.DpiMaxYards;   // college: a flat 15 yards (not a spot foul)
                    if (rules.DpiMaxYards > 0) y = Math.Min(y, rules.DpiMaxYards);
                    return y;
                }
                case PenaltyType.OffensivePassInterference: return rules.OffensivePassInterferenceYards;
                case PenaltyType.DefensiveHolding: return rules.DefensiveHoldingYards;
                case PenaltyType.IntentionalGrounding:
                    return foulSpot < -10f ? (int)Math.Round(-foulSpot, MidpointRounding.AwayFromZero) : 10;
                default:
                    return rules.CollegeStyle ? CollegeYards : ProYards;
            }
        }

        public override string ToString() { return Name; }
    }

    /// <summary>A specific flag thrown in a game.</summary>
    public sealed class PenaltyCall
    {
        public readonly PenaltyType Type;
        public readonly Team CommittedBy;
        /// <summary>Yards past the line of scrimmage where the foul occurred (negative = behind).</summary>
        public readonly float FoulSpotYards;
        public readonly int PlayerId;

        public PenaltyCall(PenaltyType type, Team committedBy, float foulSpotYards = 0f, int playerId = 0)
        {
            Type = type; CommittedBy = committedBy; FoulSpotYards = foulSpotYards; PlayerId = playerId;
        }

        public Penalty Def { get { return Penalty.Get(Type); } }

        /// <summary>Builds a call, deriving the offending team from the penalty's side and the current possession.</summary>
        public static PenaltyCall For(PenaltyType type, GameState state, float foulSpotYards = 0f, int playerId = 0)
        {
            Penalty def = Penalty.Get(type);
            if (def.Side == PenaltySide.Either)
                throw new ArgumentException(type + " can be committed by either team; use the constructor.");
            Team by = def.Side == PenaltySide.Offense ? state.Possession : state.Possession.Opponent();
            // on a kickoff the team in possession is the KICKING team, and it is the kicking team that is offside or encroaches
            if (state.Phase == GamePhase.Kickoff && (type == PenaltyType.Offside || type == PenaltyType.Encroachment)) by = state.Possession;
            return new PenaltyCall(type, by, foulSpotYards, playerId);
        }

        public override string ToString() { return Def.Name + " on " + CommittedBy; }
    }
}
