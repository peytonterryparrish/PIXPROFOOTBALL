using System;

namespace PixFootball.Art
{
    // Engine-free appearance data for the photoreal player (PIX_PhotoPlayer_*). No UnityEngine types on purpose: the League can generate it, tests can
    // run it, the saved career can store it. PhotoPlayer (Unity side) applies it to a prefab instance.
    //
    // Hook for the lead (not wired into Gameplay yet): when the league generates a player, store `PlayerAppearance.FromSeed(player.Seed, body, role)`
    // (the seed makes it deterministic across sessions) and, when spawning, call `photoPlayer.ApplyAppearance(appearance)` after SetTeamColors.

    public enum PhotoBody : byte { Small = 0, Medium = 1, Large = 2 }
    /// <summary>Body base shape (second dimension next to PhotoBody's size, v0.7 roster): Std = the original S / M / L reshape of the first photoreal body, Stocky = broad
    /// shoulders / thick thighs (safety, linebacker), Athletic = lean (corner, safety), Tall = long legs and torso (QB, TE), Runner = wide hips / strong calves (WR, RB).
    /// 5 bases x 3 sizes = 15 body shapes. Std is value 0 so every older appearance / save keeps its meaning.</summary>
    public enum BodyBase : byte { Std = 0, Stocky = 1, Athletic = 2, Tall = 3, Runner = 4 }
    public enum FacialHair : byte { None = 0, Stubble = 1, Goatee = 2, FullBeard = 3, Moustache = 4 }
    public enum EyebrowStyle : byte { Thin = 0, Normal = 1, Thick = 2 }
    public enum ScalpStyle : byte { Shaved = 0, Buzz = 1, Short = 2 }
    public enum SleeveMode : byte { None = 0, Left = 1, Right = 2, Both = 3 }
    public enum VisorStyle : byte { None = 0, Clear = 1, Smoke = 2, Mirror = 3 }
    /// <summary>Accent colours for sleeves / gloves / wristbands: fixed neutrals or one of the three team colours.</summary>
    public enum AccentColor : byte { White = 0, Black = 1, Grey = 2, TeamPrimary = 3, TeamSecondary = 4, TeamTertiary = 5 }

    [Serializable]
    public struct PlayerAppearance : IEquatable<PlayerAppearance>
    {
        public const int HeadShapeCount = 12;     // S01..S12 blend shapes of the head meshes
        public const int SkinToneCount = 8;       // PHR_Head_T0..T7 / PHR_Arms_T0..T7, light to very dark
        public const int HairColorCount = 8;
        public const int EyeColorCount = 5;

        public PhotoBody Body;
        /// <summary>Base shape of the body (default Std = the pre-v0.7 behaviour). Use ChooseBody to derive size + base from position / height / weight.</summary>
        public BodyBase Base;
        /// <summary>Two head shapes (0..11) and the percentage of the second one (0..100): any pair mixes to a distinct face.</summary>
        public byte HeadA, HeadB, HeadBlend;
        /// <summary>Authored proportion tweaks, -100..100: jaw width, chin length, nose size, cheek fullness, brow ridge.</summary>
        public sbyte JawWide, ChinLong, NoseBig, CheekFull, BrowHeavy;
        public byte SkinTone;                    // 0 (lightest) .. 7 (darkest)
        public FacialHair FacialHair;
        public EyebrowStyle Brows;
        public ScalpStyle Scalp;
        public byte HairColor;                   // 0 black, 1 dark brown, 2 brown, 3 light brown, 4 blond, 5 ginger, 6 grey, 7 white
        public byte EyeColor;                    // 0 brown, 1 dark brown, 2 blue, 3 green, 4 hazel
        public VisorStyle Visor;
        public SleeveMode Sleeves;
        public AccentColor SleeveColor;
        public bool Gloves;
        public AccentColor GloveColor;
        public bool Wristbands;
        public AccentColor WristColor;
        public bool PlayCard, Towel, Pouch;      // wrist play card, towel on the belt, hand-warmer pouch (QBs)

        /// <summary>Position families used as hints (no dependency on the league's position enum): 0 skill / back / defender, 1 quarterback, 2 lineman, 3 receiver / DB.</summary>
        public const int RoleSkill = 0, RoleQuarterback = 1, RoleLine = 2, RoleReceiver = 3;

        public static readonly string[] HairColorNames = { "black", "dark brown", "brown", "light brown", "blond", "ginger", "grey", "white" };
        public static readonly string[] SkinToneNames = { "very light", "light", "light medium", "medium", "tan", "brown", "dark brown", "very dark" };

        // ------------------------------------------------------------------------------------------------ deterministic generation
        struct Rng
        {
            ulong _s;
            public Rng(ulong seed) { _s = seed; }
            public ulong Next()
            {
                // SplitMix64
                _s += 0x9E3779B97F4A7C15UL;
                ulong z = _s;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
            /// <summary>0 .. n-1</summary>
            public int Range(int n) { return (int)(Next() % (ulong)n); }
            public int Range(int lo, int hi) { return lo + Range(hi - lo + 1); }
            /// <summary>true with probability p percent</summary>
            public bool Chance(int percent) { return Range(100) < percent; }
            public int Pick(int[] weights)
            {
                int sum = 0; for (int i = 0; i < weights.Length; i++) sum += weights[i];
                int r = Range(sum);
                for (int i = 0; i < weights.Length; i++) { if (r < weights[i]) return i; r -= weights[i]; }
                return weights.Length - 1;
            }
        }

        // weights are shaped like the league's player pool, not like a census: all eight tones are present
        static readonly int[] SkinWeights = { 6, 9, 11, 13, 16, 17, 15, 13 };
        static readonly int[] HairWeightsLight = { 24, 22, 22, 14, 10, 4, 3, 1 };
        static readonly int[] HairWeightsDark = { 62, 28, 8, 1, 0, 0, 1, 0 };
        static readonly int[] BeardWeights = { 38, 24, 10, 20, 8 };           // none, stubble, goatee, full, moustache
        static readonly int[] EyeWeightsLight = { 34, 12, 30, 12, 12 };
        static readonly int[] EyeWeightsDark = { 62, 32, 0, 0, 6 };

        /// <summary>Same seed + body + role always give the same player (stable across sessions and machines: integer arithmetic only).</summary>
        public static PlayerAppearance FromSeed(int seed, PhotoBody body = PhotoBody.Medium, int role = RoleSkill)
        {
            var r = new Rng(unchecked((ulong)(uint)seed) * 0x2545F4914F6CDD1DUL + 0x1234567UL);
            for (int i = 0; i < 3; i++) r.Next();
            var a = new PlayerAppearance { Body = body };
            a.SkinTone = (byte)r.Pick(SkinWeights);
            a.HeadA = (byte)r.Range(HeadShapeCount);
            a.HeadB = (byte)((a.HeadA + 1 + r.Range(HeadShapeCount - 1)) % HeadShapeCount);
            a.HeadBlend = (byte)r.Range(0, 70);
            a.JawWide = (sbyte)r.Range(-60, 90); a.ChinLong = (sbyte)r.Range(-70, 80); a.NoseBig = (sbyte)r.Range(-60, 90);
            a.CheekFull = (sbyte)r.Range(-50, 90); a.BrowHeavy = (sbyte)r.Range(-60, 90);
            if (body == PhotoBody.Large) { a.JawWide = (sbyte)Math.Max((int)a.JawWide, 0); a.CheekFull = (sbyte)Math.Max((int)a.CheekFull, 0); }
            bool dark = a.SkinTone >= 5;
            a.HairColor = (byte)r.Pick(dark ? HairWeightsDark : HairWeightsLight);
            a.Scalp = (ScalpStyle)r.Pick(new[] { 30, 45, 25 });
            a.Brows = (EyebrowStyle)r.Pick(new[] { 25, 50, 25 });
            a.FacialHair = (FacialHair)r.Pick(BeardWeights);
            a.EyeColor = (byte)r.Pick(dark ? EyeWeightsDark : EyeWeightsLight);
            if (a.FacialHair == FacialHair.FullBeard && a.HairColor > 4 && r.Chance(60)) a.HairColor = 1;   // keep full beards dark more often
            a.Visor = r.Chance(role == RoleLine ? 4 : 22) ? (VisorStyle)r.Range(1, 3) : VisorStyle.None;
            a.Sleeves = (SleeveMode)r.Pick(role == RoleQuarterback ? new[] { 20, 10, 55, 15 } : new[] { 62, 8, 14, 16 });
            a.SleeveColor = (AccentColor)r.Pick(new[] { 50, 22, 8, 8, 8, 4 });
            a.Gloves = r.Chance(role == RoleLine ? 88 : role == RoleReceiver ? 82 : role == RoleQuarterback ? 20 : 45);
            a.GloveColor = (AccentColor)r.Pick(new[] { 30, 26, 4, 14, 20, 6 });
            a.Wristbands = r.Chance(role == RoleQuarterback ? 80 : 40);
            a.WristColor = (AccentColor)r.Pick(new[] { 55, 15, 4, 10, 12, 4 });
            a.PlayCard = role == RoleQuarterback && r.Chance(75);
            a.Towel = r.Chance(role == RoleQuarterback ? 35 : role == RoleLine ? 6 : 14);
            a.Pouch = role == RoleQuarterback && r.Chance(80);
            return a;
        }

        /// <summary>Same as FromSeed(seed, body, role) with an explicit body base (the random stream is the same, so the face / kit of a seed does not change with the base).</summary>
        public static PlayerAppearance FromSeed(int seed, PhotoBody body, int role, BodyBase baseShape)
        {
            var a = FromSeed(seed, body, role);
            a.Base = baseShape;
            return a;
        }

        /// <summary>Picks the body (size + base shape) of a player from position code, height (cm) and weight (kg). Position codes are the usual football abbreviations
        /// (QB RB FB WR TE OT OG C OL DE DT NT DL OLB ILB MLB LB CB FS SS S K P KR PR LS), unknown codes fall back to the weight. Deterministic, engine free.
        /// QB / TE of 190 cm and more get the Tall base, WR / RB the Runner base (lean), CB the Athletic base, LB and heavy safeties the Stocky base, OL / DL stay the Large Std body.</summary>
        public static void ChooseBody(string position, int heightCm, int weightKg, out PhotoBody size, out BodyBase baseShape)
        {
            string p = (position ?? "").Trim().ToUpperInvariant();
            size = PhotoBody.Medium; baseShape = BodyBase.Std;
            switch (p)
            {
                case "QB":
                    baseShape = heightCm >= 190 ? BodyBase.Tall : BodyBase.Std;
                    size = weightKg >= 108 ? PhotoBody.Medium : PhotoBody.Small; break;
                case "WR": case "KR": case "PR":
                    baseShape = heightCm >= 193 ? BodyBase.Tall : BodyBase.Runner;
                    size = weightKg >= 102 ? PhotoBody.Medium : PhotoBody.Small; break;
                case "RB": case "FB":
                    baseShape = BodyBase.Runner;
                    size = weightKg >= 104 || p == "FB" ? PhotoBody.Medium : PhotoBody.Small; break;
                case "TE":
                    baseShape = heightCm >= 195 ? BodyBase.Tall : BodyBase.Stocky;
                    size = weightKg >= 122 ? PhotoBody.Large : PhotoBody.Medium; break;
                case "OT": case "OG": case "C": case "OL": case "DE": case "DT": case "NT": case "DL":
                    baseShape = BodyBase.Std; size = PhotoBody.Large; break;
                case "OLB": case "ILB": case "MLB": case "LB":
                    baseShape = BodyBase.Stocky; size = weightKg >= 112 ? PhotoBody.Large : PhotoBody.Medium; break;
                case "CB": case "NB":
                    baseShape = BodyBase.Athletic; size = weightKg >= 96 ? PhotoBody.Medium : PhotoBody.Small; break;
                case "FS": case "SS": case "S": case "DB":
                    baseShape = weightKg >= 96 ? BodyBase.Stocky : BodyBase.Athletic; size = PhotoBody.Medium; break;
                case "K": case "P": case "LS":
                    baseShape = BodyBase.Std; size = p == "LS" ? PhotoBody.Medium : PhotoBody.Small; break;
                default:
                    baseShape = heightCm >= 192 ? BodyBase.Tall : BodyBase.Std;
                    size = weightKg >= 118 ? PhotoBody.Large : weightKg >= 96 ? PhotoBody.Medium : PhotoBody.Small; break;
            }
        }

        /// <summary>Art library key of the prefab: photo_player_{small|medium|large} for the Std base (the pre-v0.7 keys), photo_player_{size}_{base} for the others.</summary>
        public static string PrefabKey(PhotoBody size, BodyBase baseShape)
        {
            string s = "photo_player_" + (size == PhotoBody.Small ? "small" : size == PhotoBody.Large ? "large" : "medium");
            return baseShape == BodyBase.Std ? s : s + "_" + baseShape.ToString().ToLowerInvariant();
        }

        /// <summary>Stable 32-bit key of every field (two appearances are visually identical iff their keys are equal).</summary>
        public int Key()
        {
            unchecked
            {
                uint h = 2166136261u;
                h = Mix(h, (uint)Body); if (Base != BodyBase.Std) h = Mix(h, 100u + (uint)Base); h = Mix(h, HeadA); h = Mix(h, HeadB); h = Mix(h, HeadBlend);
                h = Mix(h, (uint)(JawWide + 128)); h = Mix(h, (uint)(ChinLong + 128)); h = Mix(h, (uint)(NoseBig + 128)); h = Mix(h, (uint)(CheekFull + 128)); h = Mix(h, (uint)(BrowHeavy + 128));
                h = Mix(h, SkinTone); h = Mix(h, (uint)FacialHair); h = Mix(h, (uint)Brows); h = Mix(h, (uint)Scalp); h = Mix(h, HairColor); h = Mix(h, EyeColor);
                h = Mix(h, (uint)Visor); h = Mix(h, (uint)Sleeves); h = Mix(h, (uint)SleeveColor); h = Mix(h, Gloves ? 1u : 0u); h = Mix(h, (uint)GloveColor);
                h = Mix(h, Wristbands ? 1u : 0u); h = Mix(h, (uint)WristColor); h = Mix(h, PlayCard ? 1u : 0u); h = Mix(h, Towel ? 1u : 0u); h = Mix(h, Pouch ? 1u : 0u);
                return (int)h;
            }
        }

        static uint Mix(uint h, uint v) { unchecked { return (h ^ v) * 16777619u; } }

        /// <summary>Face-only key: head shapes, tweaks, skin tone, hair and eyes (what makes a person recognisable, independent of the kit options).</summary>
        public int FaceKey()
        {
            unchecked
            {
                uint h = 2166136261u;
                h = Mix(h, HeadA); h = Mix(h, HeadB); h = Mix(h, HeadBlend);
                h = Mix(h, (uint)(JawWide + 128)); h = Mix(h, (uint)(ChinLong + 128)); h = Mix(h, (uint)(NoseBig + 128)); h = Mix(h, (uint)(CheekFull + 128)); h = Mix(h, (uint)(BrowHeavy + 128));
                h = Mix(h, SkinTone); h = Mix(h, (uint)FacialHair); h = Mix(h, (uint)Brows); h = Mix(h, (uint)Scalp); h = Mix(h, HairColor); h = Mix(h, EyeColor);
                return (int)h;
            }
        }

        public bool Equals(PlayerAppearance o) { return Key() == o.Key(); }
        public override bool Equals(object obj) { return obj is PlayerAppearance && Equals((PlayerAppearance)obj); }
        public override int GetHashCode() { return Key(); }

        public override string ToString()
        {
            return string.Format("{0} head {1}+{2}@{3}% skin {4} ({5}) {6} brows {7} scalp {8} hair {9} eyes {10}{11}{12}{13}",
                Body, HeadA, HeadB, HeadBlend, SkinTone, SkinToneNames[SkinTone % SkinToneCount], FacialHair, Brows, Scalp, HairColorNames[HairColor % HairColorCount], EyeColor,
                Visor != VisorStyle.None ? " visor" : "", Gloves ? " gloves" : "", Sleeves != SleeveMode.None ? " sleeves" : "");
        }

        /// <summary>Clamps every field into its valid range (for data loaded from older saves).</summary>
        public PlayerAppearance Sanitized()
        {
            var a = this;
            if (a.Base > BodyBase.Runner) a.Base = BodyBase.Std;
            a.HeadA = ClampB(a.HeadA, HeadShapeCount - 1); a.HeadB = ClampB(a.HeadB, HeadShapeCount - 1); a.HeadBlend = ClampB(a.HeadBlend, 100);
            a.JawWide = Clamp100(a.JawWide); a.ChinLong = Clamp100(a.ChinLong); a.NoseBig = Clamp100(a.NoseBig); a.CheekFull = Clamp100(a.CheekFull); a.BrowHeavy = Clamp100(a.BrowHeavy);
            a.SkinTone = ClampB(a.SkinTone, SkinToneCount - 1);
            a.HairColor = ClampB(a.HairColor, HairColorCount - 1);
            a.EyeColor = ClampB(a.EyeColor, EyeColorCount - 1);
            return a;
        }

        static byte ClampB(int v, int max) { return (byte)(v < 0 ? 0 : v > max ? max : v); }

        static sbyte Clamp100(sbyte v) { return (sbyte)Math.Max(-100, Math.Min(100, (int)v)); }
    }
}
