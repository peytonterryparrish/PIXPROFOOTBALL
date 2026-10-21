namespace PixFootball.Core
{
    public enum Position
    {
        QB, RB, FB, WR, TE, LT, LG, C, RG, RT,
        LE, DT, RE, LOLB, MLB, ROLB, CB, FS, SS,
        K, P
    }

    public enum PositionUnit { Offense, Defense, Special }

    public static class PositionGroup
    {
        public static readonly Position[] OffensiveLine = { Position.LT, Position.LG, Position.C, Position.RG, Position.RT };
        public static readonly Position[] DefensiveLine = { Position.LE, Position.DT, Position.RE };
        public static readonly Position[] Linebackers = { Position.LOLB, Position.MLB, Position.ROLB };
        public static readonly Position[] DefensiveBacks = { Position.CB, Position.FS, Position.SS };

        public static PositionUnit UnitOf(Position p)
        {
            if (p == Position.K || p == Position.P) return PositionUnit.Special;
            return IsOffense(p) ? PositionUnit.Offense : PositionUnit.Defense;
        }

        public static bool IsOffense(Position p)
        {
            switch (p)
            {
                case Position.QB: case Position.RB: case Position.FB: case Position.WR: case Position.TE:
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT:
                    return true;
                default: return false;
            }
        }

        public static bool IsDefense(Position p) { return !IsOffense(p) && !IsSpecial(p); }
        public static bool IsSpecial(Position p) { return p == Position.K || p == Position.P; }

        public static bool IsOffensiveLine(Position p)
        {
            return p == Position.LT || p == Position.LG || p == Position.C || p == Position.RG || p == Position.RT;
        }

        public static bool IsDefensiveLine(Position p) { return p == Position.LE || p == Position.DT || p == Position.RE; }
        public static bool IsLine(Position p) { return IsOffensiveLine(p) || IsDefensiveLine(p); }
        public static bool IsLinebacker(Position p) { return p == Position.LOLB || p == Position.MLB || p == Position.ROLB; }
        public static bool IsDefensiveBack(Position p) { return p == Position.CB || p == Position.FS || p == Position.SS; }
        public static bool IsSafety(Position p) { return p == Position.FS || p == Position.SS; }

        /// <summary>Skill positions: ball carriers and pass catchers (QB included).</summary>
        public static bool IsSkill(Position p)
        {
            return p == Position.QB || p == Position.RB || p == Position.FB || p == Position.WR || p == Position.TE;
        }

        /// <summary>Offensive players eligible to catch a forward pass.</summary>
        public static bool IsEligibleReceiver(Position p)
        {
            return p == Position.RB || p == Position.FB || p == Position.WR || p == Position.TE;
        }

        public static bool IsKicker(Position p) { return p == Position.K || p == Position.P; }
    }
}
