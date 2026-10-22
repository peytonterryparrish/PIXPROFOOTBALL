using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Registry of the licensed (git-ignored) Draft Fever art, looked up by semantic key so gameplay code never
    /// hard-references files under Assets/_Licensed. Populated by PIX Football/Assets/Build ArtLibrary.
    /// Load() returns null when the licensed folder is absent: callers must degrade gracefully.
    /// </summary>
    [CreateAssetMenu(menuName = "PIX Football/Art Library", fileName = "ArtLibrary")]
    public class ArtLibrary : ScriptableObject
    {
        public const string EditorAssetPath = "Assets/_Licensed/ArtLibrary.asset";
        public const string ResourcesName = "ArtLibrary";

        [Serializable] public class ClipEntry { public string key; public AnimationClip clip; public string source; public float length; public bool loop; }
        [Serializable] public class PrefabEntry { public string key; public GameObject prefab; }
        [Serializable] public class MaterialEntry { public string key; public Material material; }
        [Serializable] public class SpriteEntry { public string key; public Sprite sprite; }
        [Serializable] public class AudioEntry { public string key; public AudioClip clip; }
        [Serializable] public class TextureEntry { public string key; public Texture texture; }
        [Serializable] public class FontEntry { public string key; public Font font; }

        [Header("Shared humanoid avatar (player rig). All BoNES/Motus clips are Humanoid so any avatar plays them.")]
        public Avatar playerAvatar;
        public RuntimeAnimatorController playerController;

        public List<ClipEntry> clips = new List<ClipEntry>();
        public List<PrefabEntry> prefabs = new List<PrefabEntry>();
        public List<MaterialEntry> materials = new List<MaterialEntry>();
        public List<SpriteEntry> sprites = new List<SpriteEntry>();
        public List<AudioEntry> audio = new List<AudioEntry>();
        public List<TextureEntry> textures = new List<TextureEntry>();
        public List<FontEntry> fonts = new List<FontEntry>();

        [NonSerialized] Dictionary<string, List<AnimationClip>> _clipMap;
        [NonSerialized] Dictionary<string, AnimationClip> _nameMap;
        [NonSerialized] Dictionary<string, GameObject> _prefabMap;
        [NonSerialized] Dictionary<string, Material> _matMap;
        [NonSerialized] Dictionary<string, Sprite> _spriteMap;
        [NonSerialized] Dictionary<string, List<AudioClip>> _audioMap;
        [NonSerialized] Dictionary<string, Texture> _texMap;
        [NonSerialized] Dictionary<string, Font> _fontMap;

        static ArtLibrary _instance;
        static bool _loadAttempted;
        static bool _warnLogged;

        /// <summary>Returns the library, or null if the licensed art is not present.</summary>
        public static ArtLibrary Load()
        {
            if (_instance != null) return _instance;
            if (_loadAttempted && !Application.isEditor) return null;
            _loadAttempted = true;
#if UNITY_EDITOR
            _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtLibrary>(EditorAssetPath);
#endif
            if (_instance == null) _instance = Resources.Load<ArtLibrary>(ResourcesName);
            if (_instance == null && !_warnLogged) { _warnLogged = true; Debug.LogWarning("[PIX] ArtLibrary not found (Resources/" + ResourcesName + "): clips, sounds and prefabs that come from the licensed art will be missing."); }
            if (_instance != null) _instance.BuildMaps();
            return _instance;
        }

        /// <summary>Editor/test hook.</summary>
        public static void ResetCache() { _instance = null; _loadAttempted = false; }

        public void BuildMaps()
        {
            _nameMap = null;
            _clipMap = new Dictionary<string, List<AnimationClip>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in clips)
            {
                if (e == null || e.clip == null || string.IsNullOrEmpty(e.key)) continue;
                List<AnimationClip> l;
                if (!_clipMap.TryGetValue(e.key, out l)) _clipMap[e.key] = l = new List<AnimationClip>();
                l.Add(e.clip);
            }
            _prefabMap = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in prefabs) if (e != null && e.prefab != null && !string.IsNullOrEmpty(e.key)) _prefabMap[e.key] = e.prefab;
            _matMap = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in materials) if (e != null && e.material != null && !string.IsNullOrEmpty(e.key)) _matMap[e.key] = e.material;
            _spriteMap = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in sprites) if (e != null && e.sprite != null && !string.IsNullOrEmpty(e.key)) _spriteMap[e.key] = e.sprite;
            _audioMap = new Dictionary<string, List<AudioClip>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in audio)
            {
                if (e == null || e.clip == null || string.IsNullOrEmpty(e.key)) continue;
                List<AudioClip> l;
                if (!_audioMap.TryGetValue(e.key, out l)) _audioMap[e.key] = l = new List<AudioClip>();
                l.Add(e.clip);
            }
            _texMap = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in textures) if (e != null && e.texture != null && !string.IsNullOrEmpty(e.key)) _texMap[e.key] = e.texture;
            _fontMap = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in fonts) if (e != null && e.font != null && !string.IsNullOrEmpty(e.key)) _fontMap[e.key] = e.font;
        }

        void EnsureMaps() { if (_clipMap == null) BuildMaps(); }

        // ---- clips -----------------------------------------------------------------------------------------
        public bool HasClip(string key) { EnsureMaps(); return _clipMap.ContainsKey(key); }

        /// <summary>First clip registered for the key, or null.</summary>
        public AnimationClip Clip(string key)
        {
            EnsureMaps();
            List<AnimationClip> l;
            return _clipMap.TryGetValue(key, out l) && l.Count > 0 ? l[0] : null;
        }

        /// <summary>Deterministic variant pick: pass any non-negative int (e.g. rng.Range). Wraps around.</summary>
        public AnimationClip ClipVariant(string key, int index)
        {
            EnsureMaps();
            List<AnimationClip> l;
            if (!_clipMap.TryGetValue(key, out l) || l.Count == 0) return null;
            return l[((index % l.Count) + l.Count) % l.Count];
        }

        public int ClipVariantCount(string key)
        {
            EnsureMaps();
            List<AnimationClip> l;
            return _clipMap.TryGetValue(key, out l) ? l.Count : 0;
        }

        public IReadOnlyList<AnimationClip> ClipVariants(string key)
        {
            EnsureMaps();
            List<AnimationClip> l;
            return _clipMap.TryGetValue(key, out l) ? l : (IReadOnlyList<AnimationClip>)Array.Empty<AnimationClip>();
        }

        public IEnumerable<string> ClipKeys { get { EnsureMaps(); return _clipMap.Keys; } }

        /// <summary>Clip by exact asset name (any key); null when it is not registered.</summary>
        public AnimationClip ClipByName(string name)
        {
            EnsureMaps();
            if (_nameMap == null)
            {
                _nameMap = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
                foreach (var l in _clipMap.Values) foreach (var c in l) if (c != null && !_nameMap.ContainsKey(c.name)) _nameMap[c.name] = c;
            }
            AnimationClip r; return _nameMap.TryGetValue(name, out r) ? r : null;
        }

        // ---- everything else -------------------------------------------------------------------------------
        public GameObject Prefab(string key) { EnsureMaps(); GameObject g; return _prefabMap.TryGetValue(key, out g) ? g : null; }
        public Material Material(string key) { EnsureMaps(); Material m; return _matMap.TryGetValue(key, out m) ? m : null; }
        public Sprite Sprite(string key) { EnsureMaps(); Sprite s; return _spriteMap.TryGetValue(key, out s) ? s : null; }
        public Texture Texture(string key) { EnsureMaps(); Texture t; return _texMap.TryGetValue(key, out t) ? t : null; }
        public Font Font(string key) { EnsureMaps(); Font f; return _fontMap.TryGetValue(key, out f) ? f : null; }

        public AudioClip Audio(string key, int variant = 0)
        {
            EnsureMaps();
            List<AudioClip> l;
            if (!_audioMap.TryGetValue(key, out l) || l.Count == 0) return null;
            return l[((variant % l.Count) + l.Count) % l.Count];
        }

        public int AudioVariantCount(string key) { EnsureMaps(); List<AudioClip> l; return _audioMap.TryGetValue(key, out l) ? l.Count : 0; }

        public IEnumerable<string> PrefabKeys { get { EnsureMaps(); return _prefabMap.Keys; } }
        public IEnumerable<string> SpriteKeys { get { EnsureMaps(); return _spriteMap.Keys; } }
        public IEnumerable<string> AudioKeys { get { EnsureMaps(); return _audioMap.Keys; } }
        public IEnumerable<string> MaterialKeys { get { EnsureMaps(); return _matMap.Keys; } }
    }

    /// <summary>Semantic animation keys. The editor ArtLibraryBuilder maps imported clips onto these by name heuristics.</summary>
    public static class ArtKeys
    {
        // locomotion
        public const string Idle = "idle", IdleAlt = "idle_alt", Walk = "walk", Jog = "jog", Run = "run", Sprint = "sprint",
            Backpedal = "backpedal", StrafeLeft = "strafe_left", StrafeRight = "strafe_right", ShuffleLeft = "shuffle_left",
            ShuffleRight = "shuffle_right", ShuffleSlow = "shuffle_slow", TurnLeft = "turn_left", TurnRight = "turn_right",
            Slowdown = "slowdown", StanceToSprint = "stance_to_sprint", BackpedalToSprint = "backpedal_to_sprint",
            ShuffleToSprint = "shuffle_to_sprint", CutLeft = "cut_left", CutRight = "cut_right", CutHard = "cut_hard";
        // stances
        public const string Stance2pt = "stance_2pt", Stance3pt = "stance_3pt", Stance4pt = "stance_4pt",
            StanceHalfback = "stance_halfback", StanceSafety = "stance_safety", StanceDefLine = "stance_dline",
            StanceWideDefense = "stance_wide_defense";
        // QB
        public const string QbStanceUnder = "qb_stance_under", QbStanceShotgun = "qb_stance_shotgun", QbSnapUnder = "qb_snap_under",
            QbSnapShotgun = "qb_snap_shotgun", QbDrop3 = "qb_drop_3", QbDrop5 = "qb_drop_5", QbDrop7 = "qb_drop_7",
            QbThrow = "qb_throw", QbThrowBullet = "qb_throw_bullet", QbThrowLobLong = "qb_throw_lob_long",
            QbThrowLobMedium = "qb_throw_lob_medium", QbThrowLobShort = "qb_throw_lob_short", QbThrowTouch = "qb_throw_touch",
            QbThrowJump = "qb_throw_jump", QbThrowOffbalance = "qb_throw_offbalance", QbThrowSidearm = "qb_throw_sidearm",
            QbThrowShovel = "qb_throw_shovel", QbThrowAway = "qb_throw_away", QbPumpFake = "qb_pump_fake",
            QbScramble = "qb_scramble", QbRunLeft = "qb_run_left", QbRunRight = "qb_run_right", QbSidestep = "qb_sidestep",
            QbHesitation = "qb_hesitation", QbLookAround = "qb_look_around", QbSneak = "qb_sneak", QbKneel = "qb_kneel",
            QbSlide = "qb_slide", QbSpike = "qb_spike", QbToss = "qb_toss", QbFakeHandoff = "qb_fake_handoff",
            QbHailMary = "qb_hail_mary", QbSack = "qb_sack", QbFallingThrow = "qb_falling_throw", QbDive = "qb_dive",
            QbCadence = "qb_cadence", QbReadyPass = "qb_ready_pass";
        // OL / DL
        public const string OlPassSet = "ol_pass_set", OlRunBlock = "ol_run_block", OlStance = "ol_stance", OlBlockEngage = "ol_block_engage",
            OlBlockPush = "ol_block_push", OlBlockCut = "ol_block_cut", OlBlockFail = "ol_block_fail", OlPulling = "ol_pull",
            DlRushSwim = "dl_rush_swim", DlRushRip = "dl_rush_rip", DlRushSpin = "dl_rush_spin", DlBullRush = "dl_bull_rush",
            DlShed = "dl_shed", DlWrongArm = "dl_wrong_arm", DlStance = "dl_stance", DlMove = "dl_move";
        // routes / catches
        public const string CatchHigh = "catch_high", CatchLow = "catch_low", CatchSide = "catch_side", CatchRun = "catch_run",
            CatchContested = "catch_contested", CatchFail = "catch_fail", CatchOneHand = "catch_one_hand", FairCatch = "fair_catch",
            CatchDive = "catch_dive", CatchJump = "catch_jump", RouteCut = "route_cut", RouteBreak = "route_break",
            ManCoverage = "man_coverage", ManTurnRun = "man_turn_run", ZoneDrop = "zone_drop", Interception = "interception";
        // ball carrier
        public const string JukeLeft = "juke_left", JukeRight = "juke_right", Spin = "spin_move", StiffArm = "stiff_arm",
            StiffArmLeft = "stiff_arm_left", StiffArmRight = "stiff_arm_right", Hurdle = "hurdle", DiveForward = "dive_forward",
            Truck = "truck", JukeTempo = "juke_tempo", RockerStep = "rocker_step", JukeBox = "juke_box", Handoff = "handoff",
            Fumble = "fumble";
        // tackles / contact
        public const string TackleWrap = "tackle_wrap", TackleBigHit = "tackle_big_hit", TackleDive = "tackle_dive", TackleStrip = "tackle_strip",
            TackleSuccess = "tackle_success", TackleFail = "tackle_fail", TackleArm = "tackle_arm", BeTackled = "be_tackled",
            BeTruck = "be_trucked", Gang = "tackle_gang", Fall = "fall", GetUp = "get_up", Injury = "injury", InjuredIdle = "injury_idle";
        // v0.4 contact pairs: every authored two-actor take (A = first actor, B = second actor registered as <key>_b), no per-key cap
        public const string ContactTackle = "contact_tackle", ContactSack = "contact_sack", ContactBigHit = "contact_bighit", ContactCollision = "contact_collision";
        // celebrations / ambient
        public const string CelebrateTd = "celebrate_td", CelebrateTd2 = "celebrate_td_2", CelebrateTd3 = "celebrate_td_3",
            CelebrateQb = "celebrate_qb", Cheer = "cheer", Spike = "spike", Frustration = "frustration", IdleConversation = "idle_conversation",
            Dance = "dance", Salute = "salute";
        // refs
        public const string RefIdle = "ref_idle", RefSignalTd = "ref_signal_td", RefSignalFirstDown = "ref_signal_first_down",
            RefSignalPenalty = "ref_signal_penalty", RefSignalIncomplete = "ref_signal_incomplete", RefSignalTimeout = "ref_signal_timeout",
            RefWhistle = "ref_whistle", RefSpotBall = "ref_spot_ball", RefRun = "ref_run", RefBackpedal = "ref_backpedal";
        // extra referee clips (real BGS gestures / locomotion + keyframed ones)
        public const string RefSignalSafety = "ref_signal_safety", RefSignalDeadBall = "ref_signal_dead_ball", RefBallReady = "ref_ball_ready",
            RefWalk = "ref_walk", RefWalkBack = "ref_walk_back", RefShuffleLeft = "ref_shuffle_left", RefShuffleRight = "ref_shuffle_right";
        // sideline coaches
        public const string CoachClap = "coach_clap", CoachHeadsetIdle = "coach_headset_idle", CoachClipboardIdle = "coach_clipboard_idle";
        // kicking
        public const string KickOff = "kick_off", KickFieldGoal = "kick_field_goal", Punt = "punt", Snapper = "long_snap";
        // v0.8 animation pass (BGS / Yan mocap + derived + authored clips; see Docs/QA/animation.md)
        public const string KickHolder = "kick_holder", KickReturnCatch = "kick_return_catch", PuntFast = "punt_fast", KickOnside = "kick_onside", KickSquib = "kick_squib";
        // ball-carrier locomotion set (carrying the ball): loops in 8 directions, circle runs, 90/180 plant-and-cut, stops (left / right foot planted)
        public const string BcJog = "bc_jog", BcRun = "bc_run", BcSprint = "bc_sprint", BcCircle = "bc_circle", BcTurn90 = "bc_turn_90", BcTurn180 = "bc_turn_180", BcStop = "bc_stop", BcBackpedal = "bc_backpedal";
        // non-carrier sprint set
        public const string SprintAlt = "sprint_alt", SprintTurn = "sprint_turn", StopSprint = "stop_sprint", WalkStart = "walk_start", WalkStop = "walk_stop";
        // pre-snap
        public const string OlPocketShuffle = "ol_pocket_shuffle", OlPocketStep = "ol_pocket_step", OlPocketStand = "ol_pocket_stand", CbIdle = "cb_idle", CbShuffle = "cb_shuffle",
            CbBreak = "cb_break", CbSprint = "cb_sprint", CbRotate = "cb_rotate", IdleLively = "idle_lively", IdleLook = "idle_look", HuddleIdle = "huddle_idle", HuddleBreak = "huddle_break",
            QbCadenceStomp = "qb_cadence_stomp", QbCadenceClap = "qb_cadence_clap", QbCadenceRaise = "qb_cadence_raise", QbHandSignal = "qb_hand_signal";
        // falls / get-ups / specials
        public const string GetUpFaceUp = "getup_face_up", GetUpFaceDown = "getup_face_down", GetUpSide = "getup_side", FumbleDive = "fumble_dive", PressJam = "press_jam",
            CatchToeTap = "catch_toe_tap", PushOob = "push_oob", BenchSit = "bench_sit", WrRelease = "wr_release", LbReadStep = "lb_read_step", Backpedal45 = "backpedal_diag",
            Swat = "swat", CoachAmbient = "coach_ambient", HuddleLeader = "huddle_leader", IdleHelmet = "idle_helmet";
        // distinct per-position stance clips (v0.8, QA-899..901): every position gets its own pose instead of one shared DL / safety clip
        // v0.7.0 Track A: derived clean QB throws (cut from the 10 s BoNES studio takes around the detected release), pump fake, and derived stance holds
        public const string PixQbThrowSet = "pixqb_throw_set", PixQbThrowRun = "pixqb_throw_run", PixQbThrowOff = "pixqb_throw_off", PixQbThrowAway = "pixqb_throw_away", PixQbPump = "pixqb_pump",
            PixStanceWr = "pixstance_wr", PixStanceQbUnder = "pixstance_qb_under", PixStanceCbPress = "pixstance_cb_press", PixStanceCbOff = "pixstance_cb_off", PixStanceOl = "pixstance_ol", PixStanceLb = "pixstance_lb", PixStanceRb = "pixstance_rb";
        public const string StanceOl3ptL = "stance_ol_3pt_l", StanceOl3ptR = "stance_ol_3pt_r", StanceCenter = "stance_center", StanceTe = "stance_te", StanceDl3pt = "stance_dl_3pt",
            StanceDl4pt = "stance_dl_4pt", StanceWide9 = "stance_wide9", StanceLb = "stance_lb", StanceCbPress = "stance_cb_press", StanceCbOff = "stance_cb_off", StanceS = "stance_s",
            StanceWr = "stance_wr", StanceRb = "stance_rb", StanceKicker = "stance_kicker", StanceHolder = "stance_holder", StanceSnapper = "stance_snapper";
    }
}
