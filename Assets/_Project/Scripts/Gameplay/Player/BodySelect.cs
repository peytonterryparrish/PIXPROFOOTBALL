using PixFootball.Art;
using PixFootball.Core;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.7 roster v2: picks one of the 15 photoreal body shapes (3 sizes x 5 bases) for a player from his position, height and weight
    /// (<see cref="PlayerAppearance.ChooseBody"/> does the rules, this maps the simulation's <see cref="Position"/> / league bio onto it).
    /// </summary>
    public static class BodySelect
    {
        /// <summary>Position code in the vocabulary of PlayerAppearance.ChooseBody.</summary>
        public static string Code(Position p)
        {
            switch (p)
            {
                case Position.LT: case Position.RT: return "OT";
                case Position.LG: case Position.RG: return "OG";
                case Position.LE: case Position.RE: return "DE";
                case Position.LOLB: case Position.ROLB: return "OLB";
                case Position.FS: case Position.SS: return "S";
                default: return p.ToString();   // QB RB FB WR TE C DT MLB CB K P
            }
        }

        /// <summary>Size + base of a player. A missing bio (height / weight 0) uses a typical build of the position (ChooseBody falls back on the weight).</summary>
        public static void For(Position pos, int heightIn, int weightLb, out PlayerSize size, out BodyBase baseShape)
        {
            int cm = heightIn > 55 ? (int)(heightIn * 2.54f + 0.5f) : 183;
            int kg = weightLb > 100 ? (int)(weightLb * 0.45359f + 0.5f) : TypicalKg(pos);
            PhotoBody s; BodyBase b;
            PlayerAppearance.ChooseBody(Code(pos), cm, kg, out s, out b);
            size = s == PhotoBody.Small ? PlayerSize.Small : s == PhotoBody.Large ? PlayerSize.Large : PlayerSize.Medium;
            baseShape = b;
        }

        static int TypicalKg(Position p)
        {
            switch (RoleUtil.Of(p))
            {
                case PlayerRole.OL: case PlayerRole.DL: return 138;
                case PlayerRole.LB: return 108;
                case PlayerRole.TE: return 114;
                case PlayerRole.QB: return 100;
                case PlayerRole.RB: return 98;
                case PlayerRole.DB: return 88;
                case PlayerRole.WR: return 90;
                default: return 88;
            }
        }
    }
}
