using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Populates Assets/_Licensed/ArtLibrary.asset (+ a copy in Assets/_Licensed/Resources) from the imported Draft Fever assets
    /// using name heuristics, then prints which semantic keys were found / missing (also written to Docs/ArtLibraryReport.md).
    /// </summary>
    public static class ArtLibraryBuilder
    {
        const string Root = "Assets/_Licensed/DraftFever";
        const string LibPath = ArtLibrary.EditorAssetPath;
        const string ResPath = "Assets/_Licensed/Resources/ArtLibrary.asset";

        class Rule
        {
            public string key; public Regex inc; public Regex exc; public int max;
            public Rule(string k, string inc, string exc = null, int max = 6)
            { key = k; this.inc = new Regex(inc, RegexOptions.IgnoreCase); this.exc = exc == null ? null : new Regex(exc, RegexOptions.IgnoreCase); this.max = max; }
        }

        // Matched against the normalised clip name (lowercase, "root|" and "<pack>__" prefixes stripped, "import_" stripped).
        // Order inside the table matters: the first rule that claims a clip wins for single-claim rules; paired (_a/_b) clips register
        // the _A actor under <key> and the _B actor under <key>_b.
        static readonly Rule[] ClipRules =
        {
            // ---- locomotion (Motus cycles / BoNES studio)
            new Rule(ArtKeys.Idle, @"^(player_)?standing_idle"),
            new Rule(ArtKeys.IdleAlt, @"^(receivertransitionreadyidle|linebackerblitzidle|2pstance_halfback)"),
            new Rule(ArtKeys.Walk, @"(^|_)walk(cycle)?(_|$)", @"transition|collision|idle"),
            new Rule(ArtKeys.Jog, @"^jogcycle"),
            new Rule(ArtKeys.Run, @"^runcycle"),
            new Rule(ArtKeys.Sprint, @"^sprintcycle"),
            new Rule(ArtKeys.Backpedal, @"^quarterbackshufflebkwd|^bones_backpedal_to_shuffle"),
            new Rule(ArtKeys.StrafeLeft, @"^quarterbackshuffleleft|^slow_shuffle_loop_270"),
            new Rule(ArtKeys.StrafeRight, @"^quarterbackshuffleright|^slow_shuffle_loop_090"),
            new Rule(ArtKeys.ShuffleLeft, @"^(2point|3point|4point)_to_shuffle_270|^slow_shuffle_loop_315"),
            new Rule(ArtKeys.ShuffleRight, @"^(2point|3point|4point)_to_shuffle_090|^slow_shuffle_loop_045"),
            new Rule(ArtKeys.ShuffleSlow, @"^slow_shuffle_loop"),
            new Rule(ArtKeys.TurnRight, @"^trans(ition)?_stand_idle_to_stand_idle_\d+_deg_right|^transition_walk_to_walk|^transitionwalkidle"),
            new Rule(ArtKeys.TurnLeft, @"^trans_jog_to_jog_180|^jog_to_jog_135"),
            new Rule(ArtKeys.Slowdown, @"slowdown"),
            new Rule(ArtKeys.StanceToSprint, @"stance_to_sprint|^trans(ition)?_stand_idle_to_run"),
            new Rule(ArtKeys.BackpedalToSprint, @"stance_to_backpedal"),
            new Rule(ArtKeys.ShuffleToSprint, @"^shuffle_to_sprint"),
            new Rule(ArtKeys.CutLeft, @"^avoid45degr"),
            new Rule(ArtKeys.CutRight, @"^avoid90degr"),
            new Rule(ArtKeys.CutHard, @"^avoid(135|180)degr"),
            // ---- stances
            new Rule(ArtKeys.Stance2pt, @"^2pstance_"),
            new Rule(ArtKeys.Stance3pt, @"^3pstance_aggressive|^3pstance_halfback"),
            new Rule(ArtKeys.Stance4pt, @"^dlineidle3ptrv"),
            new Rule(ArtKeys.StanceHalfback, @"^(2|3)pstance_halfback"),
            new Rule(ArtKeys.StanceSafety, @"^2pstance_safety"),
            new Rule(ArtKeys.StanceDefLine, @"^dlineidle3ptr$|^dlinecreep"),
            new Rule(ArtKeys.StanceWideDefense, @"^3pstance_wide_defence"),
            // ---- QB
            new Rule(ArtKeys.QbStanceUnder, @"undercenter_to_catch_loop|^qb_stance_transition"),
            new Rule(ArtKeys.QbStanceShotgun, @"shotgun_ready_to_catch_loop|^qb_shotgun_to_ready|ready_to_pass_loop"),
            new Rule(ArtKeys.QbSnapUnder, @"undercenter_snap|undercenter_to_catch_bad"),
            new Rule(ArtKeys.QbSnapShotgun, @"shotgun_to_catch_(center|high|low|side)|^quarterback_shotgun|^qb_shotgun_00[4-6]|shotgun_snap"),
            new Rule(ArtKeys.QbDrop3, @"3_step_dropback"),
            new Rule(ArtKeys.QbDrop5, @"5_step_dropback"),
            new Rule(ArtKeys.QbDrop7, @"7_step_dropback"),
            new Rule(ArtKeys.QbThrowBullet, @"pass_(quick_|strong_)?bullet|^quarterbackpass(long|short)bullet"),
            new Rule(ArtKeys.QbThrowLobLong, @"pass_long_lob|^quarterbackpasslonglob"),
            new Rule(ArtKeys.QbThrowLobMedium, @"pass_medium_lob"),
            new Rule(ArtKeys.QbThrowLobShort, @"pass_short_lob|^quarterbackpassshortlob"),
            new Rule(ArtKeys.QbThrowTouch, @"touch_pass"),
            new Rule(ArtKeys.QbThrowJump, @"jump_pass"),
            new Rule(ArtKeys.QbThrowOffbalance, @"offbalance_pass"),
            new Rule(ArtKeys.QbThrowSidearm, @"sidearm_pass|siderman_pass"),
            new Rule(ArtKeys.QbThrowShovel, @"shovel_pass"),
            new Rule(ArtKeys.QbThrowAway, @"throwaway"),
            new Rule(ArtKeys.QbPumpFake, @"pumpfake"),
            new Rule(ArtKeys.QbFakeHandoff, @"fakehandoff"),
            new Rule(ArtKeys.QbHailMary, @"hail_mary"),
            new Rule(ArtKeys.QbSack, @"falling_throw.*sack|^sack(wrap|broken)"),
            new Rule(ArtKeys.QbFallingThrow, @"falling_throw"),
            new Rule(ArtKeys.QbScramble, @"scramble|ready_to_run_forward|run_forward_pass|ready_to_run_00[2-5]"),
            new Rule(ArtKeys.QbRunLeft, @"run_left_pass|ready_to_run_left"),
            new Rule(ArtKeys.QbRunRight, @"run_right_pass|ready_to_run_right"),
            new Rule(ArtKeys.QbSidestep, @"sidestep"),
            new Rule(ArtKeys.QbHesitation, @"hesitation"),
            new Rule(ArtKeys.QbLookAround, @"lookaround"),
            new Rule(ArtKeys.QbSneak, @"qb_sneak"),
            new Rule(ArtKeys.QbKneel, @"qb_kneel"),
            new Rule(ArtKeys.QbSlide, @"qb_slide"),
            new Rule(ArtKeys.QbSpike, @"qb_spike"),
            new Rule(ArtKeys.QbToss, @"qb_toss"),
            new Rule(ArtKeys.QbDive, @"qb_dive"),
            new Rule(ArtKeys.QbCadence, @"qb_cadence"),
            new Rule(ArtKeys.QbReadyPass, @"ready_pass_to_pass|ready_to_confident"),
            new Rule(ArtKeys.QbThrow, @"^(bones_)?qb_shotgun_pass|^qb_ready_pass"),
            // ---- OL / DL
            new Rule(ArtKeys.OlRunBlock, @"oline_man_3_stance_to_block_run_foot_work"),
            new Rule(ArtKeys.OlPassSet, @"^block(hit|drive)(c|r)stale|^ot_vs_lb"),
            new Rule(ArtKeys.OlStance, @"^3pstance_aggressive|^olinecenterpoints|^olinetransition3ptr"),
            new Rule(ArtKeys.OlBlockEngage, @"^blockhit(c|r)pass"),
            new Rule(ArtKeys.OlBlockPush, @"^block(push|drive|shove)(c|r)pass"),
            new Rule(ArtKeys.OlBlockCut, @"^blockcut"),
            new Rule(ArtKeys.OlBlockFail, @"^block.*fail"),
            new Rule(ArtKeys.OlPulling, @"^blocksiderun"),
            new Rule(ArtKeys.DlRushSwim, @"dline_swim|^blockswim"),
            new Rule(ArtKeys.DlRushSpin, @"^blockspin"),
            new Rule(ArtKeys.DlBullRush, @"dline_bullrush|^blockbull"),
            new Rule(ArtKeys.DlShed, @"reachblockshed"),
            new Rule(ArtKeys.DlWrongArm, @"wrong_arm"),
            new Rule(ArtKeys.DlStance, @"^dlineidle3pt"),
            new Rule(ArtKeys.DlMove, @"^dline(shift|transition)"),
            // ---- catches / coverage
            new Rule(ArtKeys.CatchContested, @"contestedcatch.*success"),
            new Rule(ArtKeys.CatchFail, @"contestedcatch.*fail|^fail_catch"),
            new Rule(ArtKeys.CatchJump, @"^catchnormal.*jump"),
            new Rule(ArtKeys.CatchHigh, @"^catchnormal.*high"),
            new Rule(ArtKeys.CatchLow, @"^catchnormal.*low"),
            new Rule(ArtKeys.CatchSide, @"^catchnormalright"),
            new Rule(ArtKeys.CatchRun, @"^catch_run|^catchsnap|^catchnormal.*mid"),
            new Rule(ArtKeys.FairCatch, @"fair_?catch"),
            new Rule(ArtKeys.Interception, @"^intnormal"),
            new Rule(ArtKeys.ManCoverage, @"man_coverage|^cornerbackshuffle"),
            new Rule(ArtKeys.ManTurnRun, @"jog_to_jog"),
            new Rule(ArtKeys.ZoneDrop, @"^linebackertransition|^linebackerpumpcrowd"),
            new Rule(ArtKeys.RouteCut, @"^receivertransition"),
            // ---- ball carrier
            new Rule(ArtKeys.JukeBox, @"boxstep"),
            new Rule(ArtKeys.RockerStep, @"rockerstep"),
            new Rule(ArtKeys.JukeTempo, @"juke_tempo"),
            new Rule(ArtKeys.Spin, @"spindodge|^avoid(135|180)"),
            new Rule(ArtKeys.JukeLeft, @"juke.*left"),
            new Rule(ArtKeys.JukeRight, @"^avoid(45|90)degr|juke.*right"),
            new Rule(ArtKeys.StiffArmRight, @"^ballcarrierstiffarmr$"),
            new Rule(ArtKeys.StiffArmLeft, @"^ballcarrierstiffarmrswitch"),
            new Rule(ArtKeys.StiffArm, @"stiff_?arm"),
            new Rule(ArtKeys.Hurdle, @"hurdle"),
            new Rule(ArtKeys.DiveForward, @"^dive(short|stretch)withball|avoid0degdive"),
            new Rule(ArtKeys.Truck, @"tacklebroken.*omo"),
            new Rule(ArtKeys.Handoff, @"^handoff"),
            // ---- tackles
            new Rule(ArtKeys.TackleBigHit, @"bighit"),
            new Rule(ArtKeys.TackleDive, @"^tacklewrap.*low.*dmo"),
            new Rule(ArtKeys.TackleWrap, @"^tacklewrap.*dmo"),
            new Rule(ArtKeys.BeTackled, @"^tacklewrap.*omo"),
            new Rule(ArtKeys.TackleArm, @"^tacklebroken.*dmo"),
            new Rule(ArtKeys.TackleStrip, @"striptackle"),
            new Rule(ArtKeys.Gang, @"^sackwrap"),
            new Rule(ArtKeys.Fall, @"^collision"),
            new Rule(ArtKeys.Injury, @"ambient_injury"),
            // ---- celebrations
            new Rule(ArtKeys.CelebrateTd, @"celebrationtouchdown|touchdown_celebrations"),
            new Rule(ArtKeys.CelebrateQb, @"qb_celebration"),
            new Rule(ArtKeys.Cheer, @"cheer|^celebrationbig"),
            new Rule(ArtKeys.Spike, @"spike"),
            new Rule(ArtKeys.Frustration, @"frustrat"),
            new Rule(ArtKeys.IdleConversation, @"idles_conversations"),
            new Rule(ArtKeys.Dance, @"breakdance|freestyle"),
            new Rule(ArtKeys.Salute, @"soldierboy"),
            // ---- kicking
            new Rule(ArtKeys.Punt, @"^punt|downpunt"),
            new Rule(ArtKeys.KickOff, @"^kickoff"),
            new Rule(ArtKeys.KickFieldGoal, @"^fieldgoal|^kick_fg"),
            // ---- refs (no referee mocap exists in the source set; kept so the report shows them as missing)
            new Rule(ArtKeys.RefSignalTd, @"^ref_.*touchdown"),
            new Rule(ArtKeys.RefSignalFirstDown, @"^ref_.*first"),
            new Rule(ArtKeys.RefSignalPenalty, @"^ref_.*penalty"),
            new Rule(ArtKeys.RefSignalIncomplete, @"^ref_.*incomplete"),
            new Rule(ArtKeys.RefSignalTimeout, @"^ref_.*timeout"),
            new Rule(ArtKeys.RefWhistle, @"^ref_.*whistle"),
            new Rule(ArtKeys.RefSpotBall, @"^ref_.*spot"),
        };

        // Secondary keys: a clip that already won a primary rule can also be offered under these (gameplay-facing synonyms).
        static readonly Rule[] AliasRules =
        {
            new Rule(ArtKeys.JukeRight, @"^avoid(45|90)degr|boxstepright|rockerstepright", null, 6),
            new Rule(ArtKeys.CatchSide, @"^catchnormalright", null, 6),
            new Rule(ArtKeys.TackleSuccess, @"bighit1_(critical)?success", null, 6),
            new Rule(ArtKeys.TackleFail, @"bighit1_(critical)?failure", null, 6),
            new Rule(ArtKeys.BeTruck, @"^tacklebroken.*dmo", null, 6),
            new Rule(ArtKeys.CelebrateTd2, @"touchdown_celebrations_02|celebrationtouchdownfreestyle", null, 4),
            new Rule(ArtKeys.CelebrateTd3, @"touchdown_celebrations_03|celebrationtouchdownheismanpose|celebrationtouchdownhushcrowd", null, 4),
            new Rule(ArtKeys.Spike, @"spike", null, 3),
            new Rule(ArtKeys.Salute, @"soldierboy|kneelreverently", null, 3),
            new Rule(ArtKeys.StanceHalfback, @"^(2|3)pstance_halfback", null, 3),
            new Rule(ArtKeys.StanceSafety, @"^2pstance_safety", null, 3),
            new Rule(ArtKeys.DlStance, @"^dlineidle", null, 6),
            new Rule(ArtKeys.RouteCut, @"^receivertransition|^avoid(45|90)degr", null, 6),
            new Rule(ArtKeys.RouteBreak, @"^avoid(135|180)degr|^trans_jog_to_jog", null, 6),
            new Rule(ArtKeys.ManTurnRun, @"jog_to_jog|^transition_stand_idle_to_run", null, 6),
            new Rule(ArtKeys.Fumble, @"fumblerecovery|^frustration.*(drop|fumble)", null, 3),
            new Rule(ArtKeys.ShuffleSlow, @"^slow_shuffle_loop", null, 4),
            new Rule(ArtKeys.Snapper, @"shotgun_to_catch_center|^bones_qb_shotgun_snap", null, 4),
            new Rule(ArtKeys.CatchDive, @"^catchnormal.*jump|^dive(short|stretch)withball", null, 4),
            new Rule(ArtKeys.InjuredIdle, @"ambient_injury_00[12]_a", null, 2),
            new Rule(ArtKeys.GetUp, @"^slowdown_stop_002|ambient_injury_001_b", null, 2),
        };

        static readonly string[] AllClipKeys = typeof(ArtKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToArray();

        static string Snake(string s)
        {
            s = Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            return s;
        }

        static string NormalizeClip(string name)
        {
            string n = name;
            int bar = n.LastIndexOf('|'); if (bar >= 0) n = n.Substring(bar + 1);
            int dd = n.IndexOf("__"); if (dd >= 0) n = n.Substring(dd + 2);
            n = n.ToLowerInvariant().Replace("import_", "").Trim();
            return n;
        }

        static int SourcePriority(string path)
        {
            // Motus first for cycles, BoNES clean next
            if (path.Contains("/Animations/Motus/")) return 0;
            if (path.Contains("/Animations/BoNES_QB")) return 1;
            if (path.Contains("/Animations/BoNES_Studio")) return 2;
            if (path.Contains("/Animations/BoNES_Retargets")) return 3;
            return 4;
        }

        [MenuItem("PIX Football/Assets/Build ArtLibrary")]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/_Licensed/Resources");
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<ArtLibrary>(); AssetDatabase.CreateAsset(lib, LibPath); }
            lib.clips.Clear(); lib.prefabs.Clear(); lib.materials.Clear(); lib.sprites.Clear(); lib.audio.Clear(); lib.textures.Clear(); lib.fonts.Clear();

            BuildClips(lib);
            BuildPrefabs(lib);
            BuildMaterials(lib);
            BuildSprites(lib);
            BuildAudio(lib);
            BuildFonts(lib);

            // shared avatar + controller
            var body = AssetDatabase.LoadAllAssetsAtPath(CharacterSetup.BodyPaths[1]).OfType<Avatar>().FirstOrDefault();
            lib.playerAvatar = body;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            // copy into Resources for the runtime fallback
            if (AssetDatabase.LoadAssetAtPath<ArtLibrary>(ResPath) != null) AssetDatabase.DeleteAsset(ResPath);
            AssetDatabase.CopyAsset(LibPath, ResPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Report(lib);
        }

        static void BuildClips(ArtLibrary lib)
        {
            var candidates = new List<KeyValuePair<string, AnimationClip>>();
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Animations" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.EndsWith("Motus_Football_Player_A_01.fbx")) continue;
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (c.name.StartsWith("__preview__")) continue;
                    candidates.Add(new KeyValuePair<string, AnimationClip>(path, c));
                }
            }
            // order: source priority, then name
            candidates = candidates.OrderBy(k => SourcePriority(k.Key)).ThenBy(k => NormalizeClip(k.Value.name), StringComparer.Ordinal).ToList();
            var count = new Dictionary<string, int>();
            var seenNames = new HashSet<string>();
            foreach (var kv in candidates)
            {
                string n = NormalizeClip(kv.Value.name);
                string pathLow = kv.Key;
                // skip duplicate normalised names coming from several packs, and namespaced paired "_co" combined clips
                string dedupe = n + "|" + (pathLow.Contains("/Motus/") ? "m" : "b");
                if (!seenNames.Add(dedupe)) continue;
                if (n.EndsWith("_co")) continue;
                foreach (var r in ClipRules)
                {
                    if (!r.inc.IsMatch(n)) continue;
                    if (r.exc != null && r.exc.IsMatch(n)) continue;
                    string key = r.key;
                    if (Regex.IsMatch(n, @"_b$") && !n.EndsWith("_ab")) key += "_b";
                    int c; count.TryGetValue(key, out c);
                    if (c >= r.max) { break; }
                    count[key] = c + 1;
                    lib.clips.Add(new ArtLibrary.ClipEntry { key = key, clip = kv.Value, source = pathLow.Substring(Root.Length + 1) + "#" + kv.Value.name, length = kv.Value.length, loop = kv.Value.isLooping });
                    break;
                }
                foreach (var ar in AliasRules)
                {
                    if (!ar.inc.IsMatch(n)) continue;
                    string akey = ar.key;
                    if (Regex.IsMatch(n, @"_b$") && !n.EndsWith("_ab")) akey += "_b";
                    int ac; count.TryGetValue(akey, out ac);
                    if (ac >= ar.max) continue;
                    count[akey] = ac + 1;
                    lib.clips.Add(new ArtLibrary.ClipEntry { key = akey, clip = kv.Value, source = pathLow.Substring(Root.Length + 1) + "#" + kv.Value.name, length = kv.Value.length, loop = kv.Value.isLooping });
                }
            }
        }

        static void BuildPrefabs(ArtLibrary lib)
        {
            string dir = Root + "/Prefabs";
            if (!AssetDatabase.IsValidFolder(dir)) return;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) continue;
                string k = Snake(go.name);
                if (k.StartsWith("pix_")) k = k.Substring(4);
                lib.prefabs.Add(new ArtLibrary.PrefabEntry { key = k, prefab = go });
            }
        }

        static void BuildMaterials(ArtLibrary lib)
        {
            string dir = Root + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) return;
            foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                string k = Snake(m.name); if (k.StartsWith("pix_")) k = k.Substring(4);
                lib.materials.Add(new ArtLibrary.MaterialEntry { key = k, material = m });
            }
        }

        static void BuildSprites(ArtLibrary lib)
        {
            AddSprites(lib, Root + "/UI/InGame", p => "ui_" + Snake(Path.GetFileNameWithoutExtension(p) + "_" + Path.GetFileName(Path.GetDirectoryName(p))));
            AddSprites(lib, Root + "/UI/RankBadges", p => "badge_" + Snake(Path.GetFileNameWithoutExtension(p)));
            AddSprites(lib, Root + "/UI/Logos/Pro", p => "logo_pro_" + Snake(Path.GetFileNameWithoutExtension(p)));
            AddSprites(lib, Root + "/UI/Logos/College", p => "logo_college_" + Snake(Path.GetFileNameWithoutExtension(p)));
            // atlases stay textures (single-sprite import); expose them as textures + sprites
            foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/UI/Atlases" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) lib.textures.Add(new ArtLibrary.TextureEntry { key = "atlas_" + Snake(Path.GetFileNameWithoutExtension(p)), texture = t });
            }
        }

        static void AddSprites(ArtLibrary lib, string folder, Func<string, string> keyOf)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            var used = new HashSet<string>();
            foreach (string g in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var s = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().FirstOrDefault();
                if (s == null) continue;
                string k = keyOf(p);
                if (!used.Add(k)) continue;
                lib.sprites.Add(new ArtLibrary.SpriteEntry { key = k, sprite = s });
            }
        }

        static readonly KeyValuePair<string, string>[] AudioMap =
        {
            new KeyValuePair<string, string>("/SFX/Footsteps/Grass", "footstep_grass"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Wet", "footstep_wet"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Dirt", "footstep_dirt"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Snow", "footstep_snow"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Concrete", "footstep_concrete"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Wood", "footstep_wood"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Carpet", "footstep_carpet"),
            new KeyValuePair<string, string>("/SFX/Ball Bounce/Ball_Bounce_wet_grass", "ball_bounce_wet"),
            new KeyValuePair<string, string>("/SFX/Ball Bounce/Ball_Bounce_snow_grass", "ball_bounce_snow"),
            new KeyValuePair<string, string>("/SFX/Ball Bounce", "ball_bounce"),
            new KeyValuePair<string, string>("/SFX/Ball hit", "ball_hit"),
            new KeyValuePair<string, string>("/SFX/Cath ball", "ball_catch"),
            new KeyValuePair<string, string>("Client_SFX_Catch_ball", "ball_catch"),
            new KeyValuePair<string, string>("/SFX/Hits/Hard_hitting_tackles", "tackle_hit_hard"),
            new KeyValuePair<string, string>("/SFX/Hits", "tackle_hit"),
            new KeyValuePair<string, string>("Client_SFX_Tackle_single", "tackle_hit"),
            new KeyValuePair<string, string>("Client_SFX_Tackle", "tackle_hit_hard"),
            new KeyValuePair<string, string>("Client_Tackle_Grunt", "tackle_grunt"),
            new KeyValuePair<string, string>("Client_SFX_Whoosh", "whoosh"),
            new KeyValuePair<string, string>("/SFX/Kicks", "kick"),
            new KeyValuePair<string, string>("/SFX/Umpire whistle", "whistle"),
            new KeyValuePair<string, string>("/SFX/Scout_booster", "ui_booster"),
            new KeyValuePair<string, string>("/SFX/Team screen", "ui_card_flip"),
            new KeyValuePair<string, string>("/SFX/UI/Notifications", "ui_notification"),
            new KeyValuePair<string, string>("/SFX/UI/Play clock", "ui_play_clock"),
            new KeyValuePair<string, string>("/SFX/UI/Popups sfx", "ui_popup"),
            new KeyValuePair<string, string>("/SFX/UI/Purchase", "ui_purchase"),
            new KeyValuePair<string, string>("/SFX/UI/Zoom", "ui_zoom"),
            new KeyValuePair<string, string>("/SFX/UI", "ui_click"),
            new KeyValuePair<string, string>("/SFX/ClientAssets", "sfx_client_misc"),
            new KeyValuePair<string, string>("/Ambience/Crowds/Crowd Cheer", "crowd_cheer"),
            new KeyValuePair<string, string>("/Ambience/Crowds", "crowd_ambience"),
            new KeyValuePair<string, string>("Ambience_crowd_loading", "crowd_loading"),
            new KeyValuePair<string, string>("/Ambience/Rain", "ambience_rain"),
            new KeyValuePair<string, string>("/Ambience/Winds", "ambience_wind"),
            new KeyValuePair<string, string>("/Ambience/Locker Room", "ambience_locker_room"),
            new KeyValuePair<string, string>("/Music/DraftFeverBowlOST", "music_stadium_anthem"),
            new KeyValuePair<string, string>("/Music/DraftHouseOST", "music_menu"),
            new KeyValuePair<string, string>("/Music/PeytonParrish", "music_pp"),
        };

        static void BuildAudio(ArtLibrary lib)
        {
            foreach (string g in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Audio" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                if (clip == null) continue;
                string key = null;
                foreach (var m in AudioMap) if (p.Contains(m.Key)) { key = m.Value; break; }
                if (key == null) key = "sfx_" + Snake(Path.GetFileName(Path.GetDirectoryName(p)));
                if (key == "music_pp") key = "music_pp_" + Snake(Path.GetFileNameWithoutExtension(p));
                lib.audio.Add(new ArtLibrary.AudioEntry { key = key, clip = clip });
            }
        }

        static void BuildFonts(ArtLibrary lib)
        {
            foreach (string g in AssetDatabase.FindAssets("t:Font", new[] { Root + "/Fonts" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var f = AssetDatabase.LoadAssetAtPath<Font>(p);
                if (f != null) lib.fonts.Add(new ArtLibrary.FontEntry { key = "font_" + Snake(Path.GetFileNameWithoutExtension(p)), font = f });
            }
        }

        static void Report(ArtLibrary lib)
        {
            lib.BuildMaps();
            var sb = new StringBuilder();
            sb.AppendLine("# ArtLibrary key report");
            sb.AppendLine();
            sb.AppendLine("Generated by `PIX Football/Assets/Build ArtLibrary`. Counts are variants per key. Paired (two-actor) clips register the second actor as `<key>_b`.");
            sb.AppendLine();
            sb.AppendLine("clips=" + lib.clips.Count + " prefabs=" + lib.prefabs.Count + " materials=" + lib.materials.Count + " sprites=" + lib.sprites.Count + " audioClips=" + lib.audio.Count + " textures=" + lib.textures.Count + " fonts=" + lib.fonts.Count);
            sb.AppendLine();
            sb.AppendLine("| animation key | variants | example clip |");
            sb.AppendLine("|---|---|---|");
            var missing = new List<string>();
            int found = 0;
            foreach (string k in AllClipKeys)
            {
                int n = lib.ClipVariantCount(k);
                if (n == 0) { missing.Add(k); continue; }
                found++;
                var ex = lib.clips.First(c => c.key == k);
                sb.AppendLine("| " + k + " | " + n + " | " + ex.clip.name + " (" + ex.source.Split('#')[0] + ") |");
            }
            var extra = lib.clips.Select(c => c.key).Where(k => !AllClipKeys.Contains(k)).Distinct().OrderBy(k => k).ToList();
            sb.AppendLine();
            sb.AppendLine("**Animation keys found: " + found + " / " + AllClipKeys.Length + "**");
            sb.AppendLine();
            sb.AppendLine("Second-actor keys (`_b`): " + string.Join(", ", extra.ToArray()));
            sb.AppendLine();
            sb.AppendLine("**MISSING animation keys (" + missing.Count + "):** " + string.Join(", ", missing.ToArray()));
            sb.AppendLine();
            sb.AppendLine("Prefab keys: " + string.Join(", ", lib.PrefabKeys.OrderBy(k => k).ToArray()));
            sb.AppendLine();
            var ak = lib.AudioKeys.OrderBy(k => k).Select(k => k + "(" + lib.AudioVariantCount(k) + ")");
            sb.AppendLine("Audio keys: " + string.Join(", ", ak.ToArray()));
            sb.AppendLine();
            sb.AppendLine("Material keys: " + string.Join(", ", lib.MaterialKeys.OrderBy(k => k).ToArray()));
            sb.AppendLine();
            sb.AppendLine("Sprite keys: " + lib.sprites.Count + " (ui_*, badge_*, logo_pro_*, logo_college_*)");
            File.WriteAllText("Docs/ArtLibraryReport.md", sb.ToString());
            Debug.Log("[ArtLibrary] animation keys found " + found + "/" + AllClipKeys.Length + ", missing: " + string.Join(", ", missing.ToArray()));
        }
    }
}
