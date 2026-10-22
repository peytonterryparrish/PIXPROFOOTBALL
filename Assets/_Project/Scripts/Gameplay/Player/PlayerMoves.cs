namespace PixFootball.Gameplay
{
    public enum PlayerMove { None = 0, JukeLeft, JukeRight, Spin, StiffArmLeft, StiffArmRight, Truck, Hurdle, Dive, Celebrate }

    /// <summary>Animation and gameplay timing for each ball-carrier move. Shared by the animator builder (editor) and the controller (runtime).</summary>
    public struct MoveSpec
    {
        public PlayerMove Move;
        /// <summary>Candidate clip asset names (first found wins) and fallback ArtLibrary key.</summary>
        public string[] ClipNames;
        public string ClipKey;
        /// <summary>Normalised in/out points of the source clip (long raw takes are trimmed).</summary>
        public float Start01, End01;
        public bool Mirror;
        public float PlaySpeed;
        /// <summary>Seconds the move owns the player (cooldown before the next one).</summary>
        public float Lock;
        public string Trigger { get { return TriggerNames.Of(Move); } }
    }

    /// <summary>Cached "T_<move>" trigger names (QA-727: no string concatenation per call).</summary>
    public static class TriggerNames
    {
        static readonly string[] _n = BuildNames();
        static string[] BuildNames() { var v = (PlayerMove[])System.Enum.GetValues(typeof(PlayerMove)); var r = new string[v.Length + 1]; foreach (var m in v) r[(int)m] = "T_" + m; return r; }
        public static string Of(PlayerMove m) { int i = (int)m; return i >= 0 && i < _n.Length ? _n[i] : "T_" + m; }
    }

    public static class PlayerMoves
    {
        public static readonly MoveSpec[] All =
        {
            new MoveSpec { Move = PlayerMove.JukeRight, ClipNames = new[] { "BGS__BGS_Dodge_FrontR_Suc_BC", "Avoid45degR" }, ClipKey = "juke_right", Start01 = 0f, End01 = 0.8f, PlaySpeed = 1.25f, Lock = 0.6f },
            new MoveSpec { Move = PlayerMove.JukeLeft, ClipNames = new[] { "BGS__BGS_Dodge_FrontL_Suc_BC" }, ClipKey = "juke_left", Start01 = 0f, End01 = 0.8f, PlaySpeed = 1.25f, Lock = 0.6f },
            new MoveSpec { Move = PlayerMove.Spin, ClipNames = new string[0], ClipKey = null, Start01 = 0f, End01 = 1f, PlaySpeed = 1f, Lock = 0.8f /* procedural spin: no suitable ball-carrier clip in the source set */ },
            new MoveSpec { Move = PlayerMove.StiffArmRight, ClipNames = new[] { "BGS__BGS_Stiffarm_FrontR_Suc_BC", "BallcarrierStiffarmR" }, ClipKey = "stiff_arm_right", Start01 = 0f, End01 = 0.95f, PlaySpeed = 1f, Lock = 0.85f },
            new MoveSpec { Move = PlayerMove.StiffArmLeft, ClipNames = new[] { "BGS__BGS_Stiffarm_FrontL_Suc_BC", "BallcarrierStiffarmRSwitch" }, ClipKey = "stiff_arm_left", Start01 = 0f, End01 = 0.95f, PlaySpeed = 1f, Lock = 0.85f },
            new MoveSpec { Move = PlayerMove.Truck, ClipNames = new[] { "BGS__Truck_Front_Suc_BC", "TackleBrokenBackCHighOmo_A" }, ClipKey = "truck", Start01 = 0f, End01 = 0.62f, PlaySpeed = 1.15f, Lock = 0.9f },
            new MoveSpec { Move = PlayerMove.Hurdle, ClipNames = new[] { "Avoid0degHurdle" }, ClipKey = "hurdle", Start01 = 0.08f, End01 = 0.62f, PlaySpeed = 1.2f, Lock = 0.8f },
            new MoveSpec { Move = PlayerMove.Dive, ClipNames = new[] { "BGS__BGS_Dive_Ball_Center", "Avoid0degDive" }, ClipKey = "dive_forward", Start01 = 0f, End01 = 0.68f, PlaySpeed = 1f, Lock = 1.9f },
            new MoveSpec { Move = PlayerMove.Celebrate, ClipNames = new[] { "CelebrationTouchdownFreestyle" }, ClipKey = "celebrate_td_2", Start01 = 0f, End01 = 1f, PlaySpeed = 1f, Lock = 0f },
        };

        public static MoveSpec Get(PlayerMove m)
        {
            foreach (var s in All) if (s.Move == m) return s;
            return default(MoveSpec);
        }
    }
}
