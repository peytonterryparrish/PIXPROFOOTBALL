# v0.13.0 mocap coverage (step 0)

Measured 2026-10-24 on the v0.12.0 tree plus the new clip recorder. Full-game baseline: `D:\PIXFootball\Renders\match_watch_v0130\baseline\` (MatchWatchAudit, seed 110110, Buffalo v Miami, 3-minute quarters, no `-pixMwMaxPlays`, 168 plays, 84 min wall, `clips_played.csv`). Per-take inventory: `D:\PIXFootball\Work\v013\mocap_inventory.csv` (9,766 rows, one per unique take). Rebuild scripts: `D:\PIXFootball\Work\v013\inv\` (`inventory.pl <clips_played.csv>`).

## 1. Headline numbers

| measure | value |
|---|---:|
| Unique takes supplied, everything found (after dedupe) | **9,766** |
| of which football-relevant (without 5,152 weapon / soccer takes and 422 crowd takes) | **4,192** (4,524 clips once long takes are cut into `_sNN` segments) |
| football takes imported into the project (FBX under `_Licensed` and / or cleaned `.anim`) | 2,677 (64 %) |
| football takes reachable by the engine (ArtLibrary row, MotionDb, contact / choreo / move tables) | 1,248 (30 %) |
| football takes played in the baseline game | **343** (8.2 %) |
| **Distinct clips played in the baseline (the recorder's number)** | **364** (361 names; includes 35 mirrored `_M` twins, `_sNN` segments, referee / coach clips) |
| QB takes supplied / imported / reachable / played | **461** (724 clips) / 414 / 141 (251 clips) / **9** (10 clips) |
| Estimated NEW played clips available to the round | QB-MOCAP **~280-350**, ANIMLIB-BULK **~850-1,150 modelled, plan for ~400** (section 7) |

What "a clip" means from here on: one `AnimationClip` asset (a mirrored twin or a `_sNN` segment is its own clip). **A NEW clip = a clip name in a later run's `clips_played.csv` that is not in the baseline's.** Take counts (inventory) collapse mirrors and segments into the take.

The QB number is the owner's complaint in one line: 461 QB takes supplied, 9 played in 137 scrimmage plays. Of those 9 only six are QB-body clips (`BONES_QB_pass_quick_bullet_001_s01` 13 plays, `BONES_QB_shotgun_ready_to_catch_loop_004` as the shotgun stance, `BONES_QB_undercenter_to_catch_loop_001` once, `BONES_QB_kneel_001`, `BONES_QB_frustrated_001`, `BGS__QB_Pass_PumpFake`), three are running-back-side handoff takes (`HandoffFlexbone*_B`). The reason is structural: since v0.10 / v0.11 the QB's stance, cadence, snap, drops, pocket and throw run on the procedural POSE layer (`SimAgentView.QbSeq.cs`, `SimAgentView.QbThrow.cs`, `StancePoses.Qb*.cs`; the throw header says "no mocap throw take"), so almost no QB mocap is ever asked for. The 6 + 3 clips are the leftovers that still go through `ActionAnimator`.

## 2. Method and definitions

**Recorder (new, this step).** `Gameplay/Player/Motion/ClipUsage.cs`: static, one bool test per site while off, on with `-pixClipUsage` or inside the match-watch audit. `ClipUsage.Note(clip, system)` counts a start; `ClipUsage.Hold(clip, system, seconds)` adds screen time. Sites: `ActionAnimator.Play` (every `Rig.Play(...)` of `SimAgentView*`, `ContactDirector*`, `PlayStage*`, with the caller taken from the stack once per play: `Class.Method`), `ActionAnimator.Update` (seconds per slot, and the clips the animator controller's states show: `AnimatorState`), `MotionMatcher.Jump` (one count per selection, database clip) and `.Update` (seconds), `GaitPlayer` (slot first picked, seconds), `MatchOfficials.MiniAnim` (referee / coach clips). `MatchWatchAudit.Clips.cs` writes `clips_played.csv` (clip, asset path, system, plays, seconds, first play#) and a "distinct clips played" block in `summary.txt`. "plays" counts calls, not independent decisions: a looping stance that is re-issued counts again, `AnimatorState` (89,004) and `MatchOfficials` (16,261) are inflated in this baseline (the `AnimatorState` rule had no hysteresis; fixed afterwards, distinct clips are unaffected). Use `seconds` and the distinct count, not `plays`.

**Inventory.** One row per unique take over: `_AssetSource/DraftFever` (all FBX, take names read from the binary FBX `AnimStack` nodes; 4,240 FBX scanned, all mocap folders), `Assets/_Licensed/{DraftFever,Mixamo,MocapOnline,PIX,Style100}` (`.fbx.meta` clipAnimations, 2,791 `.anim`), `Desktop\Mixamo`, `Desktop\PPF NEW ASSETS` (+ `D:\PIXFootball\Source\PPF_NEW`), `D:\PIXFootball\Source` (100STYLE BVH + 502 pre-cut `.anim`), the 38 animation `.unitypackage` of `Desktop\Assets\Asset Store-5.x` (pathnames read from the packages), and the archive listings. Dedupe key = lower-case name without container prefix (`RAW__`, `YAN__`, `BGS__`, ...), `root|`, `_remap`, `_mcp`, mirror `_M`, segment `_sNN`, plus the `_1/_2/_3` cut-range suffix of the day-1 cut packs. Status per take: **played** (any of its clips is in the baseline's `clips_played.csv`), **reachable-not-played** (some clip is an ArtLibrary row, or in MotionDb / ContactTable / ChoreoTable / MoveTakes), **imported-unreachable** (FBX imported or `.anim` cleaned, no row anywhere), **not-imported**. The reason column says which rule applies (for unreachable takes it names the ArtLibraryBuilder rule that matches but is full, or says no rule matches).

**Not enumerable without Blender:** 27 `BoNES Retargets/*.rar` hold only `.blend` working files (about 14.6 GB; names carry the pack numbers 03-30 of the FBX exports that exist: AmbientInjuries_26, BigHits_24, ..., so most are the same takes), `DFBQuinoAnims.rar` 1.2 GB `.blend`, `Final All Sizes.7z` 197 MB, `STOREFRONT PHOTOSHOOT4.zip` 1.1 GB. No take count is claimed for them.

## 3. What was supplied

Raw files before dedupe, and what the dedupe found:

| source | raw FBX | notes |
|---|---:|---|
| Motus / Howards: `Motus_Football_08-12-2024__x/FBX`, `Howards animation Motus Football__x/Howards/FBX`, `_Licensed/DraftFever/Animations/Motus` | 713 + 715 + 710 | the same 710 takes three times (+ zip 376 MB, rar 95 MB) |
| BoNES day 1 (`root_motion_ret_cut_ranges_pack_1/2` 244, `root_motion_after_retarget_pack_1/2` 120) | 364 | cut ranges are `_1/_2/_3` copies of the retargeted takes; retargeted = `_clean_retarget/Studio_pack1/2` (121) = `_Licensed/BoNES_Studio` (121) |
| BoNES day 2 (`_A` 94, `_B` 94, `_AB` 96), day 3 (`_A` 75, `_B` 75, `_AB` 75, single actor ~24) | 535 | paired A / B actor files and the combined AB file; 343 unique takes not imported (tackles, catches, jukes, ambient bench / coach / injury, endzone saves, QB falling throws / sneaks / cadences) |
| BoNES QB: `QB new recordings` (`_mcp` raw) 85, `2026_04_22_Josh_clean_retarget` 86, `_clean_retarget/BoNES_QB` 86, `_Licensed/BoNES_QB` 86 | 343 | one 86-take set in four copies; the `_mcp` copies are the raw skeleton |
| `_clean_retarget` Yan 279, Yan2 128, Raw 17 / 18 / 37, Blend 123 | 602 | Yan + Yan2 overlap `_Licensed/BoNES_Yan` (459); Blend = DLine / OL move takes (BoNES_Extra) plus head blend-shape FBX |
| `Yan Files/FBXs backup` | 382 (1,077 take stacks) | test / alt versions (`_FrameDelay`, `_Choppy`, `_Faster`, RootLeft ...): 118 unique takes not imported |
| `BoNES Retargets/retarget fbx files__x` | 33 FBX = 330 takes | `_Licensed/BoNES_Retargets` + `BoNES_Extra` (26 + 65 FBX, 225 + 65 takes) |
| `DFBQuinoAnims`, `Dimitri Motion Test`, `Storefront Photoshoot`, `AnimationImport3` | 12 FBX (115 stacks) | test / scene files, 4 unique takes |
| MoCap Online MOBILITY Pro (Desktop pack, 2019 `Unity_Mobility_27B_Pro` copy, `_Licensed/MocapOnline`) | 528 | 524 unique takes, all imported; the other two copies are duplicates |
| Mixamo (`Desktop\Mixamo` 69, `_Licensed/Mixamo` 69, 38 cleaned `.anim`) | 69 | same 69 files twice |
| 100STYLE (CC BY 4.0, `D:\PIXFootball\Source\100STYLE`) | 96 BVH (12 of the 100 styles x 8 moves) + 502 pre-cut `.anim` slices | `_Licensed/Style100` is empty: never imported |
| `PPF_NEW/player_animated/anims`: Clinch, Helmet_TakeOn, Kick, MachoWalk, sprint, sprintball, Throw; crowd pack 6 cheers x 6 characters | 7 + 6 | not imported |
| Asset Store packs, 38 animation packs, 3.65 GB | 5,152 + 1,268 takes | weapons (Sword, Dagger, Spear, Axe, Rifle, Katana, Samurai, Gun ...) and soccer (Studio33 910, AA Soccer, KEYFRAME "Football Animation Pack" is a soccer pack) are non-football; football-usable generic human motion: MC Core Motion 581, MC Idles 240, Running Motions Pack 2 (52), Movement Animset Pro (8), MOBILITY Pivot / Strafe (names mostly equal to Mobility Pro); crowd: ANIMSET CROWD 193, Crowd Mocap 108, Motion Cast spectators 84 |
| `_Licensed` result | 1,749 FBX + 2,597 `.anim` | `DraftFever/Animations/Clean`: 2,569 `.anim` (335 mirrored), `Resources/MirroredClips` 81, Mobility / Mixamo cleaned |

## 4. Coverage per source family and per category

Unit = unique take (mirrors and segments collapsed); the clip columns count `AnimationClip` assets (segments, not mirrors). "reachable" includes "played". "imported" = FBX imported or cleaned `.anim` exists.

### 4a. By source family


| Source family (primary source of each unique take) | supplied takes | imported | reachable | played | clips (Clean/subclips) | clips reachable | clips played |
|---|---:|---:|---:|---:|---:|---:|---:|
| Asset Store packs (soccer / weapons: non-football) | 5152 | 0 | 0 | 0 | 5152 | 0 | 0 |
| BoNES (studio / QB / retargets / extra / Yan) | 1801 | 1265 | 756 | 126 | 2132 | 884 | 127 |
| Asset Store packs (football-relevant: generic human / crowd) | 1268 | 0 | 0 | 0 | 1268 | 0 | 0 |
| Howards / Motus | 719 | 716 | 420 | 154 | 720 | 421 | 154 |
| MoCap Online Mobility Pro | 540 | 540 | 31 | 31 | 540 | 15 | 31 |
| 100STYLE (CC BY) | 96 | 0 | 0 | 0 | 96 | 0 | 0 |
| Mixamo | 93 | 93 | 12 | 12 | 93 | 6 | 12 |
| Referee / coach / crowd rigs (DFB) | 71 | 70 | 28 | 20 | 71 | 28 | 20 |
| PPF_NEW (player_animated / referee / crowd) | 19 | 0 | 0 | 0 | 19 | 0 | 0 |
| DFB test / scene FBX | 4 | 0 | 0 | 0 | 4 | 0 | 0 |
| other: LIC:Resources/MirroredClips | 2 | 2 | 2 | 0 | 2 | 2 | 0 |
| other: LIC | 1 | 1 | 0 | 0 | 1 | 0 | 0 |
| **total** | 9766 | 2687 | 1249 | 343 | 10098 | 1356 | 344 |

### 4b. By category (QB first)

| Category | supplied takes | imported | reachable | played | clips (Clean/subclips) | clips reachable | clips played |
|---|---:|---:|---:|---:|---:|---:|---:|
| QB: cadence / snap / under centre | 42 | 34 | 28 | 1 | 45 | 31 | 1 |
| QB: gun (shotgun catch / stance) | 34 | 33 | 5 | 1 | 86 | 7 | 1 |
| QB: drops 1/3/5/7 | 23 | 19 | 19 | 0 | 23 | 19 | 0 |
| QB: throws | 150 | 140 | 34 | 1 | 259 | 84 | 1 |
| QB: play action / fakes | 9 | 4 | 3 | 1 | 15 | 6 | 1 |
| QB: handoffs | 126 | 126 | 14 | 3 | 135 | 18 | 3 |
| QB: scrambles / rolls / run | 25 | 23 | 11 | 0 | 65 | 31 | 0 |
| QB: slide | 1 | 1 | 1 | 0 | 4 | 4 | 0 |
| QB: kneel | 1 | 1 | 1 | 1 | 4 | 4 | 2 |
| QB: spike | 1 | 1 | 1 | 0 | 4 | 4 | 0 |
| QB: sneak | 25 | 8 | 8 | 0 | 25 | 8 | 0 |
| QB: pocket / ready / freestyle | 11 | 11 | 7 | 0 | 32 | 18 | 0 |
| QB: celebration / reaction | 6 | 6 | 5 | 1 | 20 | 13 | 1 |
| QB: other | 7 | 7 | 4 | 0 | 7 | 4 | 0 |
| RB / ball carrier | 343 | 203 | 95 | 6 | 347 | 98 | 6 |
| WR / receiver | 3 | 3 | 2 | 0 | 3 | 2 | 0 |
| OL / DL | 132 | 85 | 58 | 14 | 134 | 59 | 14 |
| LB / DB | 211 | 178 | 99 | 21 | 215 | 98 | 21 |
| tackles / blocks / contact | 631 | 526 | 335 | 127 | 655 | 337 | 127 |
| catches | 166 | 69 | 56 | 18 | 167 | 57 | 18 |
| kicks / punts / snaps | 53 | 44 | 42 | 19 | 53 | 42 | 19 |
| celebrations | 76 | 52 | 47 | 7 | 76 | 47 | 7 |
| idle / walk / sideline / ref | 1299 | 674 | 190 | 67 | 1326 | 194 | 67 |
| locomotion / transitions | 661 | 324 | 129 | 51 | 664 | 116 | 51 |
| other | 156 | 105 | 54 | 4 | 160 | 54 | 4 |
| crowd/spectators | 421 | 10 | 1 | 0 | 421 | 1 | 0 |
| non-football (soccer) | 966 | 0 | 0 | 0 | 966 | 0 | 0 |
| non-football (combat/weapons) | 4187 | 0 | 0 | 0 | 4187 | 0 | 0 |
| **total** | 9766 | 2687 | 1249 | 343 | 10098 | 1356 | 344 |

### 4c. By primary group (football groups)

| Primary group | supplied takes | imported | reachable | played | clips (Clean/subclips) | clips reachable | clips played |
|---|---:|---:|---:|---:|---:|---:|---:|
| LIC:Motus | 695 | 695 | 399 | 154 | 696 | 400 | 154 |
| PKG:MC Core Motion - Believable 3D Animations by MoCap Central | 581 | 0 | 0 | 0 | 581 | 0 | 0 |
| LIC:MocapOnline/Mobility_Pro | 524 | 524 | 15 | 15 | 524 | 15 | 15 |
| LIC:BoNES_Yan | 410 | 410 | 316 | 82 | 433 | 325 | 82 |
| LIC:Animations/Clean | 388 | 388 | 171 | 16 | 391 | 171 | 16 |
| PKG:MC Idles - Believable 3D Animations by MoCap Central | 240 | 0 | 0 | 0 | 240 | 0 | 0 |
| LIC:BoNES_Retargets | 220 | 220 | 132 | 6 | 241 | 140 | 6 |
| DFB:BoNES day2 (A/B/AB) | 203 | 0 | 0 | 0 | 203 | 0 | 0 |
| PKG:ANIMSET CROWD | 193 | 0 | 0 | 0 | 193 | 0 | 0 |
| DFB:BoNES day3 (A/B/AB) | 140 | 0 | 0 | 0 | 140 | 0 | 0 |
| DFB:Yan Files backup | 118 | 0 | 0 | 0 | 118 | 0 | 0 |
| PKG:Crowd - Mocap Animation Pack | 108 | 0 | 0 | 0 | 108 | 0 | 0 |
| CC:100STYLE bvh (12 of 100 styles) | 96 | 0 | 0 | 0 | 96 | 0 | 0 |
| LIC:BoNES_QB | 86 | 86 | 51 | 5 | 309 | 153 | 6 |
| PKG:Spectators animations Motion Cast02 Vol1 | 84 | 0 | 0 | 0 | 84 | 0 | 0 |
| LIC:Mixamo | 69 | 69 | 6 | 6 | 69 | 6 | 6 |
| LIC:BoNES_Extra | 65 | 65 | 37 | 8 | 88 | 40 | 8 |
| PKG:Running Motions Pack 2 | 52 | 0 | 0 | 0 | 52 | 0 | 0 |
| LIC:BoNES_Studio/pack2 | 51 | 51 | 21 | 0 | 86 | 26 | 0 |
| DFB:BoNES Retargets (multi-take) | 45 | 0 | 0 | 0 | 45 | 0 | 0 |
| LIC:BoNES_Studio/pack1 | 41 | 41 | 24 | 6 | 44 | 25 | 6 |
| LIC:Characters/Referee | 35 | 35 | 22 | 14 | 35 | 22 | 14 |
| LIC:Characters/Coaches | 26 | 26 | 6 | 6 | 26 | 6 | 6 |
| LIC:Legacy2024 | 21 | 21 | 21 | 0 | 21 | 21 | 0 |
| LIC:MocapOnline/Clean | 16 | 16 | 16 | 16 | 16 | 0 | 16 |
| LIC:Mixamo/Clean | 14 | 14 | 6 | 6 | 14 | 0 | 6 |
| DFB:_clean_retarget/Blend | 12 | 0 | 0 | 0 | 12 | 0 | 0 |
| PPF_NEW:crowd_pack | 12 | 0 | 0 | 0 | 12 | 0 | 0 |
| DFB:_clean_retarget/Yan2 | 11 | 0 | 0 | 0 | 11 | 0 | 0 |
| LIC:Mixamo/Clean11 | 10 | 10 | 0 | 0 | 10 | 0 | 0 |
| LIC:Characters/Crowd | 9 | 9 | 0 | 0 | 9 | 0 | 0 |
| PKG:Movement Animset Pro | 8 | 0 | 0 | 0 | 8 | 0 | 0 |
| DFB:BoNES day1 cut ranges | 7 | 0 | 0 | 0 | 7 | 0 | 0 |
| PPF_NEW:player_animated | 6 | 0 | 0 | 0 | 6 | 0 | 0 |
| LIC:PIX_Authored | 4 | 4 | 4 | 3 | 4 | 4 | 3 |
| DFB:Howards/Motus raw (client pack) | 3 | 0 | 0 | 0 | 3 | 0 | 0 |
| DFB:Storefront photoshoot scenes | 3 | 0 | 0 | 0 | 3 | 0 | 0 |
| LIC:Resources/MirroredClips | 2 | 2 | 2 | 0 | 2 | 2 | 0 |
| PKG:AAAudienceCheer | 1 | 0 | 0 | 0 | 1 | 0 | 0 |
| PKG:MOBILITY Walk Jog Run - Strafe Animations | 1 | 0 | 0 | 0 | 1 | 0 | 0 |
| DFB:Quino / import test FBX | 1 | 0 | 0 | 0 | 1 | 0 | 0 |
| PPF_NEW:referee | 1 | 0 | 0 | 0 | 1 | 0 | 0 |
| LIC | 1 | 1 | 0 | 0 | 1 | 0 | 0 |
| **total** | 9766 | 2687 | 1249 | 343 | 10098 | 1356 | 344 |

(Counts are of the baseline game: 168 plays = 137 scrimmage, 16 punts, 7 kickoffs, 4 field goals, 2 PATs, 4 penalty replays.)

## 5. Why the unused groups are unused, and what it takes

Reason classes over all football takes that were not played (takes / clips; "clips" for imported-unreachable = clips without a row):

| scope | status | reason | takes | clips |
|---|---|---|---:|---:|
| QB | imported-unreachable | imported, duplicate (_CO combined pair) | 60 | 60 |
| QB | imported-unreachable | imported, no rule | 37 | 49 |
| QB | imported-unreachable | imported, rule key full (cap) | 176 | 278 |
| QB | not-imported | not imported: duplicate | 2 | 2 |
| QB | not-imported | not imported: never imported | 45 | 45 |
| QB | reachable-not-played | reachable but tag reject | 4 | 13 |
| QB | reachable-not-played | reachable, but runtime never asks for the key | 31 | 75 |
| QB | reachable-not-played | reachable, selectable, not drawn in the game | 97 | 184 |
| other football | imported-unreachable | imported, duplicate (_CO combined pair) | 141 | 141 |
| other football | imported-unreachable | imported, no rule | 535 | 535 |
| other football | imported-unreachable | imported, rule key full (cap) | 480 | 518 |
| other football | not-imported | not imported: duplicate | 5 | 5 |
| other football | not-imported | not imported: never imported | 1463 | 1463 |
| other football | reachable-not-played | reachable but tag reject | 9 | 11 |
| other football | reachable-not-played | reachable, but runtime never asks for the key | 126 | 147 |
| other football | reachable-not-played | reachable, selectable, not drawn in the game | 638 | 644 |

Groups, with the reason and what it takes (football takes only; takes / clips; "clips" = AnimationClip assets):

| group | takes / clips | status | WHY | what it takes |
|---|---|---|---|---|
| **QB, imported, no row: rule key full** (`qb_snap_shotgun`, `qb_throw_*`, `qb_falling_throw`, `qb_scramble` ... capped at 6 per key) | 176 / 278 | imported-unreachable | no engine slot beyond the cap | raise `max` of the QB rules in `ArtLibraryBuilder.cs`, rebuild ArtLibrary + tags (ANIMLIB tools), then the QB sequence must ask for them |
| QB, imported, no rule matches (`pixqb_*` derived throws, `FB_Quarterback_Pass`, `QB_ready_to_lookaround_002`, `QB_pumpfake_pass_006` ...) | 37 / 49 | imported-unreachable | no rule | new rules (a name pattern each) |
| QB, `_CO` combined handoff files | 60 / 60 | imported-unreachable | duplicate: the builder skips `_co`; the `_A` / `_B` clips are the playable ones | nothing |
| **QB, row exists but the runtime never asks for the key** (`qb_scramble`, `qb_run_left/right`, `qb_throw_touch / sidearm / jump / offbalance / away / lob_medium`, `qb_snap_dropback / hands / drop_under`, `qb_drop_walk`) | 31 / 75 | reachable-not-played | **no engine slot**: the QB sequence is on the pose layer, so the key has no consumer | QB-MOCAP: mocap-driven sequence slots (below) |
| QB, row exists and is asked for, not drawn (`qb_throw_bullet`, `qb_pump_fake`, `qb_fake_handoff`, `qb_hail_mary`, `qb_falling_throw` ...) | 97 / 184 | reachable-not-played | `ClipSelect.Pick` returns one of the clips within 8 QA points of the best of the key and the seed repeats; only one draw per key per situation | widen the pool (score window), rotate through the pool (no repeat) |
| QB, tag reject | 4 / 13 | reachable | bad data (`BONES_QB_falling_throw_*_s03`: slide, waist / torso twist, yaw offset; score 46-47) | re-clean or leave out (`Docs/QA/v0100/animlib_rejects.txt` lists 13, tag table has 17 `reject=1`) |
| QB, never imported | 45 / 45 (+2 duplicate) | not-imported | never imported: day-2 / day-3 `QB_falling_throw_00N_AB` (10), `QB_Sneak_00N_AB` (7), `QB_cadence_00N_A/B/AB` (5), `_clean_retarget/QBSneak__IMPORT_QB_Sneak` (10), Yan-backup `QB_pumpfake_pass_005_*` variants (5), `QB_ready_to_sidestep_006_*` (2), `YAN__QB_undercener_catch_to_N_step_dropback` (4), `QB remap 004 shotgun`, `YAN__QB_UNDERCENTER_SNAP_00N` (3) | copy to a stage folder, humanoid import, `CleanBatch` / `ClipCleaner`, tags, rules |
| Non-QB, imported, rule key full | 480 / 518 | imported-unreachable | cap of 6 (2-4 for several) per key: ball-carrier jukes / spins (107 `RAW__Ball_Carrier_*`), stance-to-sprint / shuffle transitions (LB / DB 79, OL / DL 27), contact blocks, celebrations | raise `max`, or add a key per family |
| Non-QB, imported, no rule matches | 535 / 535 | imported-unreachable | no rule: BoNES ambient (coach, bench, injury, idle conversation, frustrations; 465 clips), Mobility Pro 509 locomotion takes (MotionDb holds 44 clips), Mixamo 63, Draft-night, IntNormal* jumps | ambient: stage scenes that ask for them (sideline / bench / injury), Mobility: `MotionDbBuilder` rebuild + `MobilityCurveStrip` |
| Non-QB, `_CO` combined pair files (tackle / block / handoff) | 141 / 141 | imported-unreachable | duplicate of the `_A` / `_B` clips | nothing |
| Non-QB, row exists, runtime never asks for the key (`be_tackled`, `be_trucked`, `tackle_fail`, `catch_fail`, `cb_shuffle`, `cb_sprint`, `cb_rotate`, `route_cut`, `route_break`, `stance_3pt`, `stance_dline`, `trench_atk / def`, `slowdown`, `sprint_alt`, `shuffle_*`, `turn_left / right`, `kickoff_*_fidget`, `walk_start / stop`, crowd sit / stand) | 126 / 147 | reachable-not-played | no engine slot | wire the key in AnimKit / ContactDirector (one line per key) |
| Non-QB, row exists and asked for, not drawn | 638 / 644 | reachable-not-played | pool narrowing (Pick window), few draws per game, deterministic seeds | rotate (no-repeat bag per key and per team), widen window |
| Non-QB tag reject | 9 / 11 | reachable | bad data | re-clean |
| Never imported: BoNES day 2 / day 3 (A / B / AB actor files) | 343 / 343 | not-imported | never imported (tackles, catches, juke finishes, ambient bench, endzone saves) | split A / B, retarget, clean, tag; FBX are 20-290 MB each |
| Never imported: MC Core Motion 581, MC Idles 215, Running Motions Pack 2 52, Movement Animset Pro 8 | 856 / 856 | not-imported | Asset Store generic human motion, no football slot | import as ambient / sideline / jog variants; low priority |
| Never imported: Yan Files backup 118, `_clean_retarget` Yan2 / Blend 23, BoNES Retargets multi-take FBX 45 | 186 / 186 | not-imported | test / alt versions (`_FrameDelay`, `_Choppy`, `_Faster`) and FBX never copied under `_Licensed` | import the non-duplicate ones |
| Never imported: 100STYLE | 96 BVH (+502 pre-cut `.anim`) | not-imported | CC BY locomotion styles, `Style100` folder empty | import the 502 slices for the matcher |
| Never imported: PPF_NEW `player_animated` (Clinch, Helmet_TakeOn, Kick, MachoWalk, sprint, sprintball, Throw), Storefront scenes, Quino | 12 | not-imported | test content | optional |
| Duplicate copies | Motus x3, QB x4, day-1 cut ranges 244 files, `_mcp` 85, Desktop Mixamo 69, Mobility Pro x3, Crowd x2 | not counted | duplicate | none |
| Non-football | weapons / soccer 5,152, crowd 422 | not-imported | non-football; crowd has no slot in the match (fans are a separate system) | none |
| Bad data known from earlier rounds | v0.12 notes: Mobility stop / turn takes rejected (feet slide 0.5-11 m/s), "Speed" curve on Mobility clips (strip by name) | | | `MobilityCurveStrip` after every MotionDb rebuild |

## 6. Engine facts that cap what is played (read from the code)

- **ArtLibrary** has 1,805 clip rows on 321 keys (1,534 distinct clips). `ArtLibraryBuilder` has 303 rules, default `max` 6 per key (some 2-4). A clip matching a full key is silently dropped: 656 football takes sit behind a full key, 572 more match no rule at all (counts of takes, `_CO` duplicates excluded).
- **63 keys / 346 rows are never asked for by runtime code** (list in `inv\unref_keys.txt`), among them 13 QB keys. They are "reachable" only on paper.
- **`ClipSelect.Pick`** (CHOREO / QB users) keeps only clips within 8 QA points of the best of the key: 944 of the 1,805 rows (QB keys: 147 of 246; `qb_throw`, `qb_dive`, `qb_run_left/right`, `qb_slide`, all the drops: pool of 1). Picks are seeded and the same seed repeats.
- **The quarterback is on the pose layer** (`SimAgentView.QbSeq.cs`, `.QbThrow.cs`, `StancePoses.Qb*.cs`): pre-snap walk / survey / crouch, drops, pocket, throw and follow-through are keyed poses, not clips. That is why 461 QB takes give 6 QB-body clips in a game.
- **MotionDb holds 44 clips** (22 takes + mirrors): the whole locomotion stream (44,028 selections in the baseline) comes from them; 509 Mobility Pro takes and the 96 / 502 100STYLE takes are outside.
- **Contact and choreography** (`ContactDirector`, `PlayStage`) use `ContactTable` (224 clip names) / `ChoreoTable` (847) through `ArtLibrary.ClipByName`: a clip not registered in ArtLibrary can not be booked.
- **Recorder blind spots:** only clips that start through `ActionAnimator`, `MotionMatcher`, `GaitPlayer`, `MiniAnim` or an animator-controller state are seen; the replay system re-plays transforms and plays no clips.

## 7. Plan: where >= 300 new played clips come from

Method (assumptions stated, so the next run can check them): per category the pool is reachable + imported-unreachable (without `_CO` duplicates) + not-imported (without duplicates); "decisions per game" is the number of independent clip choices in a game like the baseline (137 scrimmage plays: 57 runs, 44 completions + 28 incompletions + 1 INT = 73 throws, 6 sacks; 29 kicking plays); estimate with random pick = pool x (1 - e^(-decisions / pool)) minus what is played now; with a no-repeat bag per key = min(pool, decisions) minus what is played now; locomotion halved (the matcher chooses by fit). **These are model numbers, not measurements**; the confirming runs decide.


**Plan table (groups that can become new played clips):**

| track | group | pool (clips) | new played, model | what makes it happen |
|---|---|---:|---:|---|
| QB-MOCAP | stance + cadence + snap, under centre and gun (`qb_stance_*`, `qb_cadence_*`, `qb_snap_*`, `BONES_QB_shotgun_to_catch_*`, `undercenter_to_catch_*`) | 31 + 6 + 8 and 7 + 78 + 1 | 42-44 and 52-81 | mocap stance / cadence / snap clips as the pre-snap and snap segments (replace the pose-layer crouch); per-play random variant, no repeat; the 4 `_sNN` segments of each take are separate clips |
| QB-MOCAP | drops 1 / 3 / 5 / 7, pocket, ready, look-around | 19 + 4 and 18 + 14 | 22-23 and 29-32 | drop clips (`qb_drop_3/5/7`, `qb_snap_dropback`, `qb_drop_walk/settle`) played as the dropback with the sim's drop distance choosing 1 / 3 / 5 / 7; pocket variants while he waits |
| QB-MOCAP | throws by family (bullet, touch, lob short / medium / long, sidearm, shovel, jump, off-balance, on the run, away, hail mary, falling) | 84 + 165 + 10 | 63-72 | the throw family from the sim's throw (distance, lob, pressure, run) picks a family; clip with a sync moment (`ClipSelect.Synced`) so the ball leaves on the release tick; replaces `QbThrow11` pose |
| QB-MOCAP | play action, fake handoff, pump fake, handoffs (`HandoffFlexbone*_A/B`, `QB_FakeHandoff_to_Roll`) | 6 + 4 + 5 and 18 + 57 | 6-9 and 33-47 | play-action and handoff pairs on the run plays |
| QB-MOCAP | scrambles, rolls, sidesteps, slides, kneel, spike, sneak, celebrations / reactions | 31 + 32, 4+4+4, 8 + 17, 13 + 7 + 4 + 3 | 17-20, 4-7, 4, 10-12 | scramble keys wired to the QB sequence; the never-asked `qb_scramble`, `qb_run_left/right` get a consumer |
| **QB-MOCAP total** | | | **~282-351** | needs the sequence rewritten on clips (largest risk: facing, feet, ball sync on 461 QB takes) |
| ANIMLIB-BULK | tackles / blocks / contact (`_CO` excluded) | 337 + 72 + 105 | 151-273 | raise caps of `tackle_*` / `contact_*` / `ol_block_*` keys, import day-2 / day-3 `BC_StripTackle_*`, `LB_ContestedCatch*`, rotate through booked pairs |
| ANIMLIB-BULK | LB / DB (stances, shuffles, backpedals, zone, man coverage, transitions) and OL / DL (stances, pocket shuffles, pass sets) | 98 + 84 + 33 and 59 + 28 + 47 | 134-194 and 103-120 | raise caps, wire `cb_shuffle`, `cb_rotate`, `stance_dline`, `trench_*`, per-player variants instead of one pick per team |
| ANIMLIB-BULK | RB / ball carrier (juke, spin, stiff arm, truck, hurdle, cuts, turns) and catches | 98 + 109 + 140 and 57 + 13 + 97 | 81-94 and 42-57 | cap lift for the juke / spin / stiff-arm keys (107 `RAW__Ball_Carrier_*` waiting), import day-2 juke finishes and contested catches |
| ANIMLIB-BULK | idle / walk / sideline / huddle / bench / referee | 194 + 507 + 796 | 205-233 | BoNES ambient (bench, coach, injury, idle conversation: 465 clips) + MC Idles; stage them on the sideline and in the huddle (`PlayStage.ToSideline`, `SetHuddle` today use 21 + 18 clips) |
| ANIMLIB-BULK | locomotion in the matcher (Mobility Pro 509, 100STYLE 502 slices, Mixamo 63) | 116 + 211 + 337 | 95-124 (halved) | `MotionDbBuilder` rebuild + `MobilityCurveStrip`; the matcher picks by fit |
| ANIMLIB-BULK | kicks, celebrations, other | 42, 47 + 39, 54 + 55 + 53 | 3-10, 2-3, 39-46 | minor; wiring of the unasked keys |
| **ANIMLIB-BULK total** | | | **~855-1,158 modelled** | plan for ~400 (half of the lower figure): contact + defence + ball carrier + sideline groups above |

Full per-category model table (the numbers above are copied from it):

| category | reachable | imported, unreachable | not imported | played now | decisions per game (est.) | + new, pools opened, random pick | + new, pools opened, no-repeat pick | track |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| QB: cadence / snap / under centre | 31 | 6 | 8 | 1 | 137 | 42 | 44 | QB-MOCAP |
| QB: celebration / reaction | 13 | 7 | 0 | 1 | 8 | 6 | 7 | QB-MOCAP |
| QB: drops 1/3/5/7 | 19 | 0 | 4 | 0 | 80 | 22 | 23 | QB-MOCAP |
| QB: gun (shotgun catch / stance) | 7 | 78 | 1 | 1 | 82 | 52 | 81 | QB-MOCAP |
| QB: handoffs | 18 | 57 | 0 | 3 | 50 | 33 | 47 | QB-MOCAP |
| QB: kneel | 4 | 0 | 0 | 2 | 3 | 0 | 1 | QB-MOCAP |
| QB: other | 4 | 3 | 0 | 0 | 5 | 4 | 5 | QB-MOCAP |
| QB: play action / fakes | 6 | 4 | 5 | 1 | 10 | 6 | 9 | QB-MOCAP |
| QB: pocket / ready / freestyle | 18 | 14 | 0 | 0 | 80 | 29 | 32 | QB-MOCAP |
| QB: scrambles / rolls / run | 31 | 32 | 0 | 0 | 20 | 17 | 20 | QB-MOCAP |
| QB: slide | 4 | 0 | 0 | 0 | 3 | 2 | 3 | QB-MOCAP |
| QB: sneak | 8 | 0 | 17 | 0 | 4 | 4 | 4 | QB-MOCAP |
| QB: spike | 4 | 0 | 0 | 0 | 3 | 2 | 3 | QB-MOCAP |
| QB: throws | 84 | 165 | 10 | 1 | 73 | 63 | 72 | QB-MOCAP |
| LB / DB | 98 | 84 | 33 | 21 | 274 | 134 | 194 | ANIMLIB-BULK |
| OL / DL | 59 | 28 | 47 | 14 | 274 | 103 | 120 | ANIMLIB-BULK |
| RB / ball carrier | 98 | 109 | 140 | 6 | 100 | 81 | 94 | ANIMLIB-BULK |
| WR / receiver | 2 | 1 | 0 | 0 | 137 | 3 | 3 | ANIMLIB-BULK |
| catches | 57 | 13 | 97 | 18 | 75 | 42 | 57 | ANIMLIB-BULK |
| celebrations | 47 | 5 | 24 | 7 | 10 | 2 | 3 | ANIMLIB-BULK |
| idle / walk / sideline / ref | 194 | 507 | 620 | 67 | 300 | 201 | 233 | ANIMLIB-BULK |
| kicks / punts / snaps | 42 | 2 | 9 | 19 | 29 | 3 | 10 | ANIMLIB-BULK |
| locomotion / transitions | 116 | 211 | 337 | 51 | 300 | 95 | 124 | ANIMLIB-BULK |
| other | 54 | 55 | 51 | 4 | 50 | 39 | 46 | ANIMLIB-BULK |
| tackles / blocks / contact | 337 | 72 | 105 | 127 | 400 | 151 | 273 | ANIMLIB-BULK |
| **ANIMLIB-BULK total** | | | | 334 | | **855** | **1158** | |
| **QB-MOCAP total** | | | | 10 | | **282** | **351** | |

## 8. Baseline: what the 364 clips are

By system (distinct clips; `plays` is calls, see section 2): `SimAgentView.ApplyMatch` 159 (stances, blocks, catches, carry moves), `ContactDirector` booked tackles / sessions / piles / one-shots / get-ups / joiners 121, `MotionMatcher` 44 (all 44 of the database), `AnimatorState` 29, `ServiceEngageLoop` 28, `PlayStage.ToSideline` 21, `MatchOfficials` 20, `SetHuddle` 18, `PlayStage.ServiceScenes` 12, reactions / celebrations / choreo 10, QB `EnterPocket` 1 (the shotgun stance, 185 plays, 775 s) and `PocketLife` 1 (`BGS__QB_Pass_PumpFake`). By folder: `DraftFever/Animations/Clean` 295, `MocapOnline/Mobility_Pro/Clean` 32, referee 13, Mixamo 12, coaches 4, `Resources/MirroredClips` 4 (35 mirrored `_M` twins in total). Everything in the baseline set is reachable through ArtLibrary or MotionDb; nothing played is outside the inventory.

Not in this measure: cutscene / menu animators and replays (transform playback, no clips).

## 9. This step: files, test, known limits

Files changed (shared tree, uncommitted): NEW `Assets/_Project/Scripts/Gameplay/Player/Motion/ClipUsage.cs`, NEW `Assets/_Project/Scripts/Editor/Tools/MatchWatchAudit.Clips.cs`, NEW `Assets/_Project/Tests/EditMode/Gameplay/ClipUsageTests.cs` (+ their `.meta`); one-line hooks (marked `v0.13.0 COVERAGE`) in `ActionAnimator.cs` (Play, deferred Play, Update seconds, animator-state notes), `GaitPlayer.cs` (slot create x2, seconds), `MotionMatcher.cs` (`Jump`, `Update`), `MatchOfficials.cs` (`MiniAnim.Play`, `Update`), `MatchWatchAudit.cs` (`ClipsHook()` in `SetupHooks`, `ClipsReport()` in `WriteReports`). The existing SimAgentView / ContactDirector / PlayStage sites need no edit: they all start clips through `ActionAnimator.Play`, and the recorder reads the caller from the stack.

Test: `ClipUsageTests` 3 / 3 pass (EditMode, Audit copy; counts plays / seconds / first play#, nothing recorded while off, system = calling method). Compile clean. Baseline run: exit 0, 168 plays, final 3-17, no recorder errors in the log.

Known limits of the numbers: (1) name-based dedupe merges two different takes that happen to share a name across packs and misses renamed copies; (2) multi-take FBX inside Asset Store packages count as one take; (3) the 502 100STYLE `.anim` slices are counted as clips of the 96 BVH takes only in this text, not in the CSV; (4) the `.blend` working files are not enumerated; (5) the baseline is 168 plays, the v0.12 integration game had 189 (the v0.12.0 rules follow-up), so absolute counts of a later run on the same seed are comparable only run to run; (6) the `AnimatorState` / `MatchOfficials` play counts are inflated (calls); a later run uses the hysteresis version of `NoteStateClips`.

Round rule suggested by this data: count NEW clips as `clips_played.csv` names minus the baseline's 364, and always report separately the number of QB-body clips (baseline: 6).
