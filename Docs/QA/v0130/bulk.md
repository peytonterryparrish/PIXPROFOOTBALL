# ANIMLIB-BULK track log (v0.13.0)

Owner (v0.12 playtest): "so much motion capture is missing". Non-QB share of the round: open the library, draw from the pools, wire the keys nobody asked for, import what was never imported, ambient on the sideline / in the huddle. Private copy `D:\PIXFootball\Work\AnimLib\PIX` (private `_Licensed` data). Baseline: `D:\PIXFootball\Renders\match_watch_v0130\baseline\` (364 distinct clips). Confirming run: `...\animlib\` (full game, seed 110110). 40-play probe: `...\animlib_q40\`.

## 1. Done-criteria (full game, seed 110110, vs baseline)

(Filled in by the coordinator: the track agent was stopped by a tooling outage while this game ran. Game: 179 plays, 8-33, 108.6 min wall. Comparison script `D:\PIXFootball\Work\v013\isscmp.sh`.)

| criterion | baseline | this run | met |
|---|---:|---:|---|
| distinct clips played | 364 (360 names) | **496** (491 names) | |
| NEW clips (names not in the baseline) | | **177** (4 of them QB: cheer, kneel; list `D:\PIXFootball\Work\v013\new_clips_bulk.txt`); 46 baseline clips no longer drawn | **no** (target 250) |
| new played clips that are `ClipQa` rejects | | 0 by construction (`Query` filters rejects, score >= 50); not re-checked against the game list | yes (by construction) |
| FACE_BODY_70DEG (standing) | 1,385 | 1,552 (+12 %) | **no** (> 10 %) |
| FACE_RUNNING_55DEG / BACKWARDS | 38 / 32 | 25 / 16 | yes |
| POP_HIPS_ROT / CHEST_ROT / HIPS_JUMP | 975 / 202 / 490 | 746 / 207 / 496 | yes |
| FLOAT_STANDING_BOTH_FEET_UP | 246 | 283 (+15 %) | **no** |
| FLOAT_GROUND_ACTION / RUN_AIRBORNE | 82 / 71 | 36 / 52 | yes |
| OVERLAP_IN_TACKLE_OR_PILE | 65 | 70 (+8 %) | yes |
| HAND_OR_HEAD_BELOW_TURF (not a gate) | 128 | 249 (+95 %) | watch: new catch / celebration / ambient takes |
| SUNK_HIPS_BELOW_0.35M (not a gate) | 178 | 232 (+30 %) | watch |

The copy also carried other tracks' shared-tree code from its syncs (e.g. FEET_BELOW_TURF 1,098 -> 27, STANCE_LINEMAN_NO_HAND 301 -> 1 are STANCE-HEAD's), so the gate deltas are not this track's alone; the integration game decides.

## 2. What was done (in the order of new played clips per hour)

1. **Library opened** (`ArtLibraryBuilder.cs`). New `Cap(rule)`: every non-QB key takes up to 4x its old cap (at least 24); QB keys keep theirs (QB-MOCAP owns them in `.Qb.cs`); the pre-snap stance keys (`stance_*`, `idle`, `idle_alt`, `dl_stance`, `ol_stance`, `cb_idle*`) keep the old caps after the 40-play probe showed standing FACE +35 % for WR / S / CB with wider stance pools. The hard-coded mirror caps (cut / juke 6, turn 8) are 24 / 16. ArtLibrary clip rows: 1,805 -> 2,981 (before the stance exemption). `-pixOldCaps` restores the old caps. New rules for no-rule takes: Motus `BlockEscape/Push/Shove ..._B` (dl_rush_rip_b, press_jam_b, push_oob_b), `CatchFairFrontCMid`, `Trans_Run_to_Ready_Idle`, `Runningback/LinebackerTransition*`; the 535 "no rule" takes of the coverage table are 384 Mobility Pro + 61 Mixamo (NATURAL-2's, no football slot) and ~90 others, so most of that bucket has no engine slot (see 5). `_CO` duplicates stay out (`n.EndsWith("_co")`).
2. **Engine draws from the pools.** New `Gameplay/Player/ClipBag.cs`: least-used-first draw with a seeded tie break, memoised per (pool, seed) so both actors of a pair and every re-issued plan get the same take, `key_b` shares the pool of `key` (A / B index stays aligned), seed 0 and `qb_*` keys keep the old pick, `-pixNoClipBag` switches it off. Used by `AnimKit.V / Pick / PickTokens / Catch / Tackle / TacklePair / TryPair / PairFiltered / Spin / SpinDir`, `ClipSelect.Pick` (QA window 8 -> 20 points, candidates still pass `Query`: not rejected, score >= 50), the carrier juke pool (one-line hook in `SimAgentView.CarryMoves.cs`). Booked tackles: the contact score counts every recent use of a take (0.7 per use, up to 3, window 40 bookings instead of a flat 0.9 for the last 6) in `TackleVariants.Score` and, as two one-line hooks, in `Sim/Choreo/Choreographer.Tackle.cs ScorePair` / `Choreographer.cs Remember` (not an ANIMLIB file).
3. **Never-asked keys wired** (one site each): `catch_fail` (a receiver who drops it plays a failed-catch take, 50 %), `cb_shuffle` (DB whose first move is a shuffle: the left take or its mirror), `cheer_mc` (cheer reactions), the juke / spin pools via the bag. NOT wired (no clean engine event or the file belongs to STANCE-HEAD): `kickoff_*_fidget` (needs a one-shot over the stance in `StanceClips` / `PlayStage.Stance`), `stance_dline`, `trench_atk / def` (single-actor DL / OL moves, no event), `route_cut / break`, `cb_rotate`, `turn_left / right` (matcher), `be_tackled / be_trucked / tackle_fail` (the same clips are in the contact keys and are drawn there).
4. **Imports** (all cleaned with the v0.8 cleaner, QA'd with `ClipQa`, tagged):
   - MoCap Central "MC Idles" (Asset Store pack on the desktop, never imported): 110 of 240 takes (stands with arms crossed / hands on hips / clasped, weight shifts and look-arounds, heavy breathing and hands-on-knees, body aches, fidgets, brush-offs, nods, 12 warm-up stretches, crowd cheers / claps / fist shakes). Left out: smoking, toilet, phone / music, signs, hot / cold, wall lean, start / stop transitions. 112 clips cleaned (one stretch cut into 3 segments), QA score 92-100, 0 rejects. New keys `idle_mc_stand / look / tired / fidget`, `warmup_stretch`, `cheer_mc` (rules in the new `ArtLibraryBuilder.Bulk.cs`, which claims before the QB rules: their unanchored `lookaround` pattern also takes every studio look-around, see Requests).
   - BoNES day 2 / day 3: 86 raw studio takes re-baked with Blender (`retarget_actions.py`, 13 s - 60 s each): 60 `_A` files (7 China-route juke finishes, 10 tempo-change finishes, rocker / box steps, 27 `LB_ContestedCatch*`, idle conversations, touchdown celebrations) + 26 `_B` partners of the contested catches. Cleaned: 218 clips (segments + mirrors), 31 rejected by QA (listed in `animlib_rejects.txt`, mostly `_s02` windows of the juke takes and the crit-fail catches), the rest registered (`juke_tempo` 28, `catch_contested` 23 / `_b` 22, `catch_fail` 14, `idle_conversation` 7). Not imported: the other ~250 raw takes (BigHit / StripTackle / DLINE / Man_Coverage / OT_vs_LB pairs, QB takes) for budget.
5. **Sideline / huddle ambient**: `SidelineKeys` / `HuddleKeys` pools in `AnimKit.Extra2.cs` (the MC stands, tired, look, fidget, stretch clips join the studio idles; only one of three huddle players keeps the authored stand, was two of three).

Tables rebuilt in the private copy: ArtLibrary (2,981 rows before the stance exemption), tags 2,994, timing, move takes, runtime assets, facing, cycle speeds; `ContactClipBuilder.Run` (113 pairs, no new ones: its pair files are the Motus / Retargets pair patterns, so the day-2 contested-catch pairs are NOT booked), `ChoreoClipBuilder.Run` (1,357 clips measured, was 847).

## 3. Integration recipe

**New source files under `_Licensed` (copy additively into the shared tree; Unity makes the `.meta`):**
- `Assets/_Licensed/DraftFever/Animations/MC_Idles/MC__*.fbx` (110 files, 185 MB). Source: `Desktop\Assets\Asset Store-5.x\Mocap Central\Animation\MC Idles - Believable 3D Animations by MoCap Central.unitypackage` (tar.gz; `asset` + `pathname` per GUID folder). Selection list: `D:\PIXFootball\Work\AnimLib\mc_idles_selected.txt`; copies renamed `MC__<name after MCU_am_>`, and `Stand_Idle_{Fidget,BrushOff,Greet,Tired}` -> `Stand_{...}` (no "idle" in the name: not looped by name).
- `Assets/_Licensed/DraftFever/Animations/BoNES_Day23/RAW__*.fbx` (86 files, 720 MB). Staged at `D:\PIXFootball\Work\AnimLib\stage_day23` (or regenerate: `blender -b --python Tools/Blender/retarget_actions.py -- <StiffArm_14.fbx> <out> RAW <src files>`; file lists `D:\PIXFootball\Work\AnimLib\day23_names.txt`; source files under `_AssetSource\...\studio BoNES\day {2,3}\_{A,B}`; **15 of them fail to open at their full path (path too long): copy them to a short folder first**).
- Backup of the private data before regeneration: `D:\PIXFootball\Work\v013\backup\animlib\`.

**Ordered regeneration commands** (each `bash D:/PIXFootball/Work/v012/unity.sh <copy> <log> <seconds> -executeMethod ...`, add `-quit` to the ones that do not exit by themselves):
1. `PixFootball.EditorTools.BulkV13.ImportFolder -pixFolder Assets/_Licensed/DraftFever/Animations/MC_Idles -pixList D:/PIXFootball/Work/AnimLib/mc_idles_selected.txt` (humanoid import, names from files, loop flags by name, cleaner per clip -> `Animations/Clean/MC__*.anim`; ~3 min)
2. `PixFootball.EditorTools.BulkV13.ImportFolder -pixFolder Assets/_Licensed/DraftFever/Animations/BoNES_Day23 -pixList D:/PIXFootball/Work/AnimLib/day23_names.txt -pixMode takes` (travelling takes: name-driven flags; ~4 min)
3. optional `PixFootball.EditorTools.BulkV13.QaNew -pixList <list> -pixOut Logs/animlib/x.csv` (ClipQa of the new clips)
4. `PixFootball.EditorTools.AnimLibBatch.Rebuild` (QA of all Clean clips + `Docs/QA/v0100/animlib_rejects.txt`, ArtLibrary, ClipTiming, MoveTakes, RuntimeAssets, ClipFacing + MirroredClips, CycleSpeeds, ClipTags; ~15 min)
5. `PixFootball.EditorTools.ContactClipBuilder.Run -quit` (PixContact) and `PixFootball.EditorTools.ChoreoClipBuilder.Run -quit` (PixChoreo)
6. (NATURAL-2 / QB) `MotionDbBuilder` + `MobilityCurveStrip` are not part of this track.
Do not run `CleanBatch.RunList` for new folders: it loads every FBX of the library (out of memory next to three other editors); `BulkV13.ImportFolder` cleans only the folder.

## 4. Test

One EditMode run (`-testCategory "!Slow"`, AnimLib copy): 1,392 tests, 1,389 passed, 2 failed, 1 skipped. Both failures are QB-MOCAP's in-progress tests (`QbMocapTests.EveryQuarterbackTakeNameHasAKeyInTheQbRules`: `BONES_QB_run_forward_pass_001_s03` has no QB rule; `QbSequenceTests.SecureStartsOnTheCrouchHandsAndEndsAtTheBelly` 0.92 vs < 0.88). New `AnimLibBulkTests` (5) pass: the bag walks the whole pool before a repeat, same (pool, seed) = same take, twins share the index, seed 0 / QB keys keep the old pick, every recent use costs contact score.

## 5. What still looks wrong / known issues

- 177 new clips, short of 250: the bag widens the pools, but most keys are drawn only a few times a game; the biggest unopened pools are the unbooked pair takes (contested catch, BigHit, StripTackle) and the never-imported day-2 / day-3 pairs.
- Standing FACE +12 % and FLOAT_STANDING +15 % with the wider pools; HAND_OR_HEAD_BELOW_TURF doubled (new catch / celebration / ambient takes reach lower). The frame strip was not reviewed by the track (stopped).
- The shared tree did not compile while I worked: STANCE-HEAD's new `Editor/Tools/BodyProbe.Skeleton.cs` (`HumanBodyBones.UpperArm / LowerArm / Hand / UpperLeg / LowerLeg / Foot / Toes / Shoulder` do not exist; they are `LeftUpperArm`...). I deleted it in my private copy only.
- The contested-catch pairs (`catch_contested` + `_b`, 22 pairs) and the other raw pairs are registered and QA'd but not in `PixContact` (the builder's file pattern), so no booked contested catch plays them; the A takes play only as the solo failed-catch (`catch_fail`) and the juke / finish takes through `PickJukeClip`.
- Not done for budget: the other ~250 never-imported day-2 / day-3 pair takes (BigHit, StripTackle, DLINE, Man_Coverage, OT_vs_LB), MC Core Motion (581), Yan-backup variants; `kickoff_*_fidget`, `stance_dline`, `trench_*`, `route_*`, `cb_rotate` wiring.
- The 100STYLE / Mobility / Mixamo locomotion is NATURAL-2's.

## 6. Requests

- **QB-MOCAP:** `new Rule(ArtKeys.QbLookAround, @"lookaround", ...)` is unanchored: it claims every clip with "lookaround" in the name, including the studio `BGS_FIX_MCU_AM_*LookAround` idles (key `idle_look` went from 2 clips to 0) and the MC look-arounds. Anchor it (`^(bones_)?qb_.*lookaround`). My `BulkRules` (claim first) protect the two non-QB families meanwhile.
- **STANCE-HEAD:** fix `BodyProbe.Skeleton.cs` (does not compile); `StanceClips` / `PlayStage.Stance` need a one-shot hook if the kickoff fidgets (`kickoff_att_fidget` 9, `kickoff_def_fidget` 6) should play (every 6-8 s, not looped).
- **Integration:** `Docs/QA/v0100/animlib_rejects.txt` is regenerated by the rebuild (the shared file has 14 rows; the private rebuild has ~50 incl. the new takes).

# Wave 2 (BULK-2, 2026-10-25)

Owner: "about 250 raw two-actor takes were not imported", "hands or heads touching the turf roughly doubled", "standing players facing the wrong way are up". Copy `AnimLib` (private `_Licensed`, refreshed from the merged tree). Full game: `D:\PIXFootball\Renders\match_watch_v0130\bulk2` (seed 110110, 177 scrimmage plays, 106 min). "Before" = baseline, wave 1 (`animlib`) and the integration game (`...\integration`, merged wave 1). This copy carries every track's wave-1 code and data, so the new-clip total contains the QB and matcher clips of the other tracks.

## 1. Done-criteria

| criterion | baseline | wave 1 (animlib) | integration | wave 2 (bulk2) | met |
|---|---:|---:|---:|---:|---|
| HAND_OR_HEAD_BELOW_TURF (<= 140) | 128 | 249 | 235 | **8** | yes |
| FLOAT_STANDING_BOTH_FEET_UP (<= 246) | 246 | 283 | 288 | **93** | yes |
| SUNK_HIPS_BELOW_0.35M (<= 178) | 178 | 232 | 233 | **18** | yes |
| FACE_BODY_70DEG standing (<= 1,385), all positions (non-QB in brackets) | 1,385 (1,284) | 1,552 (1,447) | 1,551 (1,446) | **1,655 (1,530)** | **no**; per play 8.2 -> 9.35 |
| new clips vs the step-0 baseline, whole game | | 177 | | **568** (QB 179 and matcher-only 132 come from the other tracks' data in this tree) | |
| new clips of this track (non-QB, not matcher-only) | | 173 | | **257** (+84 vs wave 1; 113 not in wave 1's list, 33 of wave 1's no longer drawn) | **no**: asked +100 |
| POP_CHEST_ROT / POP_HIPS_ROT / OVERLAP_IN_TACKLE (wave-1 gates) | 202 / 975 / 65 | 207 / 746 / 70 | | 43 / 773 / 67 | yes |
| FLOAT_GROUND_ACTION | 82 | 36 | 36 | 108 (strip pairs, fixed after the game, see 2.4) | probe only |
| OVERLAP_BODIES_STANDING_OR_BLOCKING | 16 | 18 | 17 | 49 | watch: pre-snap / post-play overlaps of 0.1-0.2 m (C / QB, LE / MLB); not looked into |

Tests: one EditMode run (`!Slow`, AnimLib copy): 1,414 tests, 1,413 passed, 1 failed (`Natural3Tests.TheCapTurnsTheChestTowardsItsTargetInSmallStepsAndNeverOvershoots`, NATURAL-3's in-flight test). After the last code edit (strip pairs, 2.4) `AnimLibBulkTests` alone: 7 / 7 (2 new tests this wave).

## 2. What was done

1. **Raw two-actor takes imported.** `Tools/Blender/retarget_actions.py --split`: a studio `<take>_AB.fbx` holds two armatures (`Krzysztof:` or `1:` prefix = actor A, no prefix or `2:` = actor B, checked against the separate `_A` / `_B` files of the same take); each is baked onto the clean skeleton and written as `<take>_A` / `<take>_B` with the same frame range. The 269 not-yet-imported files were copied to a short folder first (`D:\PIXFootball\Work\AnimLib\b2\src`, 15 GB; no long-path failures). 155 actor takes baked (`b2\out`). **Most of the "250 never imported" takes already exist, cut down, under other names** (`DLineMoves__`, `OLine01_016__`, `root|BigHit1_*`, `DLINE_Wrong_Arm_*`, Man_Coverage / OT_vs_LB / QB_falling_throw / QB_Sneak retargets): 133 of 223 actor takes were name duplicates of clips in Clean/ and were not imported again. Left out because the engine has no caller: `Man_Coverage` pairs, `HB_Stiff_Arm` B partners, `Ball_Carrier_Juke_SpinDodge` defenders, `DLINE_Wrong_Arm_009`, `Ambient_Injury`, `Draft_night`, `standing_idle_loop_2_*` (stance pool, FACE risk), `Player_turns`.
2. **Windowed pairs** (`BulkV13.ImportPairs`, new `Editor/Tools/AnimLib/BulkV13.Pairs.cs`). A raw pair is 15-60 s of walk-in, three repetitions and walk-away, so wave 1's whole-take pairs (15-35 s) could never be booked (`ContactClipBuilder` skips pairs over 8 s). Both actors are now imported with the authored layout (Root Transform Position XZ = Original), sampled together at 10 Hz and cut into the SAME windows around each interaction (contact families: contact -1.0 s .. +3.6 / 4.0 / 6.6 s by family; engagement families: first close approach -0.3 .. +2.8 s = 3.1 s, which `AnimKit.EngagedPair` plays). Written as `<take>_s01_A / _B ...`; the whole-take clips are deleted (and `ArtLibraryBuilder` drops any RAW whole-take pair that has `_s01` segments). 84 contested-catch windows (wave 1's 26 pairs), 36 DLINE, 12 strip-tackle pairs, others. Takes without a usable window are listed in `Logs/animlib/pairs_clean.csv` (`no window`: 3 Var2 catches before the 2.0 m catch threshold, BigHit CritSuccess_001, Helmet, Leader idle).
3. **Booked.** `ContactClipBuilder.PairKeys` + `ArtKeys.TackleStrip`; PixContact 113 -> **156 pairs**: `catch_contested` 8 -> 39 (31 new windows of the day-2 `LB_ContestedCatch*` pairs, booked through the `Choreographer` duels), `tackle_strip` 12 (new). The DLINE windows join `dl_bull_rush` / `dl_rush_swim` / `dl_shed` (EngagedPair). New BigHit pairs: **none booked**, all rejected by `ClipQa` (slide / waist-twist / torso-twist, listed in `animlib_rejects.txt`). Played in the game: 58 distinct `RAW__` clips (28 OLINE footwork, 11 strip, 13 DLINE ...), but **no RAW contested catch** (a duel win needs a nearly standing receiver; the 2 duels played Motus takes).
4. **Strip tackles.** First booked as wraps; two of them (plays 8 and 76) gave FLOAT_GROUND_ACTION 36 -> 108 (act Fallen at standing height): none of the 12 strip pairs takes anybody to the ground (get-up 0 for both actors). After the game they were moved to `ContactKind.Broken` (tackler rakes the ball, carrier runs on); 14-play probe (`bulk2_fix14`): FLOAT_GROUND_ACTION 2 frames in 1 play, no strip session in the first 14 plays. **The full game ran before this edit**, so its strip numbers are the wrap version.
5. **Regressions.** Every sample of HAND_OR_HEAD / SUNK_HIPS and most of FLOAT_STANDING was an `Engaged` OL / DL body, so `EngageProbe` (new, `Editor/Tools/AnimLib/EngageProbe.cs`) measures each engagement-pool take over the 3 s the engine plays (1.4 s for the run-block footwork): hips < 0.35 m for 0.17 s, a hand or the head < -8 cm, both toes > 9 cm for 0.5 s (no float check for the footwork) -> `Docs/QA/v0130/bulk2_engage_rejects.txt` (83 takes, twins together; read by `ArtLibraryBuilder.LoadRejects`). The bad takes: the DLineMoves__ rushes cut from the raw start (3 s of both toes up from frame 1) and several BlockHit / Cut / Drive stale-pass pairs. The DL pairs re-cut from the raw takes (item 2) replace them. HAND 235 -> 8, SUNK 233 -> 18, FLOAT 288 -> 93.
6. **Standing FACE not fixed.** The pre-snap stance pools (`stance_*`, `idle`, `dl_stance`, `ol_stance`, `cb_idle*`) are now exempt from the no-repeat bag and from the wider QA window (`ClipBag.IsStancePool`, `ClipSelect.Pick`), as they already were from the cap lift. FACE went up anyway (every position, QB included; per play 8.7 integration -> 9.35). So the cause is not the stance-pool draw: the samples are PreSnap `Stance` bodies 70-180 deg off the sim facing (RT, DL, WR, S, CB, the same agents every play), STANCE-HEAD's area (pose facing in `StanceClips`), not the library. Not met.
7. **Single-actor `_B` takes.** The builder registered the day-3 `OLINE_MAN_3_*_B` footwork (24 takes, single actor) under `ol_run_block_b`, which nothing asks for; `SingleActorB` keeps them in `ol_run_block` (cap 64): 28 distinct clips played, the biggest yield. The second actors of ambient pairs (`bench_sit_b`, `coach_ambient_b`, `idle_conversation_b`, `frustration_b`, `react_frustration_b`) join their pools through `AnimKit.PickEither` (9 more B takes imported). MC Core dances (5, `celebrate_td_mc` in `CelebrateKeys`): imported, none drawn (few touchdowns).
8. **Extras asked later (+60 from never-used sources): not reached.** `D:\PIXFootball\Work\v013\new_clips_bulk2_extra.txt`: **2** played (`YAN__kickoff_transition_00` + mirror). Findings: the Yan-backup takes (118) and the BoNES Retargets multi-take files are mostly duplicates of clips Clean/ holds under another prefix (`BGS_CB_*` 40 clips, `OLine017_027__*`, `DLineMoves__*`, `2ptstance_to_sprint_*`, `slowdown_stop_*`); the rest is locomotion (NATURAL-3) or QB (imported for QB-2: `QBSneak__IMPORT_QB_Sneak_*` 10, `YAN__QB_*` 7, `RAW__QB_Sneak_006`, `QB_cadence_001-003`, `QB_falling_throw_004/010`, `QB_ready_to_sidestep_006_10/11`; their names carry `qb_`). `PPF NEW ASSETS` player_animated is a 3ds Max Biped rig (Clinch, Helmet_TakeOn, Kick, Throw, sprint, MachoWalk): not retargeted (no key but QB throw / kick). The "offensive-lineman..." / "quarterback-winding-up..." zips on the desktop are static posed meshes. 4 `YAN__kickoff_*` imported (transition played), 2 `Player_interaction_002` pairs (no window: 150 s of idle talk).

## 3. New source files under `_Licensed` (additive; list `D:\PIXFootball\Work\v013\bulk2_new_licensed.txt`, 120 files, 1.1 GB)
- `Assets/_Licensed/DraftFever/Animations/BoNES_Day23B/*.fbx` (115): Blender outputs (`RAW__*`) + the clean-skeleton extras copied from `_AssetSource\DraftFever\_clean_retarget__x\{Blend,Yan2}` (`QBSneak__IMPORT_*`, `PlayerInteraction__IMPORT_*`, `YAN__kickoff_*`, `YAN__QB_*`). Names: `D:\PIXFootball\Work\AnimLib\b2\day23b_names.txt`.
- `Assets/_Licensed/DraftFever/Animations/MC_Core_Dance/MC__celebrate_*.fbx` (5), from `Desktop\Assets\Asset Store-5.x\Mocap Central\Animation\MC Core Motion ....unitypackage` (tar.gz, GUID folders in `b2\mcc`).
- In git: `Docs/QA/v0130/bulk2_engage_rejects.txt`.
- Backup: wave 1's `D:\PIXFootball\Work\v013\backup\animlib\` is the pre-wave-2 private state; the shared tree was not touched. Changed (not new): wave 1's `RAW__LB_ContestedCatch*` whole-take clips in Clean/ are replaced by their `_sNN` segments (importer of those 52 FBX: Original XZ).

## 4. Ordered regeneration commands (each `bash D:/PIXFootball/Work/v012/unity.sh <copy> <log> <seconds> -executeMethod ...`)
0. Blender (only to rebuild the FBX): `blender -b --python Tools/Blender/retarget_actions.py -- <StiffArm_14.fbx> <out> RAW --split <copies of the day 2 / 3 files in a short folder>` (`b2\jobs*.txt` are the lists).
1. `PixFootball.EditorTools.BulkV13.ImportPairs -pixFolder "Assets/_Licensed/DraftFever/Animations/BoNES_Day23;Assets/_Licensed/DraftFever/Animations/BoNES_Day23B;Assets/_Licensed/DraftFever/Animations/MC_Core_Dance" -pixList <all names>` (`b2\import_all.txt` + `b2\amb_names.txt` + `b2\dl_names.txt`, ~4 min per run)
2. `PixFootball.EditorTools.AnimLibBatch.Rebuild -pixWatchdog 150` (QA + `animlib_rejects.txt` + ArtLibrary + tables + tags; ~10 min)
3. `PixFootball.EditorTools.EngageProbe.Run` (measures, writes `bulk2_engage_rejects.txt`, rebuilds ArtLibrary + runtime assets; 3 min)
4. `PixFootball.EditorTools.ContactClipBuilder.Run -pixWatchdog 80` (156 pairs; the editor hangs on exit after the table is written: kill it) and `PixFootball.EditorTools.ChoreoClipBuilder.Run -quit`
5. QB-2 / NATURAL-3 steps (`QbClipCatalog.BuildBatch`, `MotionDbBuilder`, `MobilityCurveStrip`) after these.
Hooks in other tracks' files: none (ClipBag, ClipSelect, AnimKit.Extra2 / Flow, TackleVariants, ContactClipBuilder, ArtLibraryBuilder(.Bulk), Tools/Blender/retarget_actions.py are ANIMLIB files). New scripts are static classes, no `.meta` GUID dependency.

## 5. What still looks wrong / known issues
- Standing FACE 1,655 (> 1,385): not fixed, see 2.6. OVERLAP_STANDING 49 (was 17): not analysed.
- No contested catch of the 31 new windows was drawn in this game (duels are rare); a booked pair needs the sim to ask for it. New BigHit pairs: none usable (QA rejects).
- `Man_Coverage`, `HB_Stiff_Arm` and juke-defender pairs have no engine caller (`AnimKit.ManCoveragePair`, `StiffArmPair`, `SpinMovePair` are unused); left out of the import.
- The full game ran before the strip-tackle fix (2.4); the fix is checked by a 14-play probe and a unit test only. Frame strips not reviewed (budget).

# Wave 2b (BULK-2 follow-up: more clips from the owner's files, full game `bulk2b`)

Asked: +60 clips played from never-used football takes, wire the unasked keys, contested catches drawn, OVERLAP_STANDING 17 -> 49, one full game. Result: **not reached**. Full game `D:\PIXFootball\Renders\match_watch_v0130\bulk2b` (seed 110110, 181 plays, 116 min, code of 2b below).

| | wave 2 (`bulk2`) | 2b (`bulk2b`) |
|---|---:|---:|
| new clips vs baseline, whole game (QB + matcher data of other tracks included) | 568 | 574 |
| this track (non-QB, not matcher-only) | 257 | **262** (44 gained, 39 of wave 2's not drawn this time) |
| extras from never-used sources (`new_clips_bulk2_extra.txt`) | 2 | **4** (`YAN__kickoff_transition_00` + mirror, 2 `RAW__Touchdown_celebrations_001` segments); plus 2 booked contested-catch clips (`RAW__LB_ContestedCatchBack_CritSuccess_001_s03_A/B`) |
| HAND_OR_HEAD / SUNK / FLOAT_STANDING | 8 / 18 / 93 | 3 / 17 / 119 (targets 140 / 178 / 246) |
| FLOAT_GROUND_ACTION | 108 | **82** (strip pairs as broken tackles: back to the baseline's 82) |
| OVERLAP_BODIES_STANDING_OR_BLOCKING | 49 | 31 (integration 17) |
| standing FACE (now STANCE-HEAD's) | 1,655 | 1,487 |

## What was wired (all ANIMLIB code; the two Sim/Choreo lines are one-line hooks into CHOREO files)
- **Contested catches**: `Choreographer.Duel.cs` gate relaxed (receiver speed 2.0 -> 3.4 m/s, catch height 3.0 -> 3.4 m, defender gap 1.0 -> 1.5 yd; constants `DuelMaxSpeed`, `DuelMaxGapYd`). One duel drew a window in the whole game (2 clips): the gate is not the limit, the sim rarely has a receiver who wins a rolled duel with a defender on him. The 39 booked windows stay available.
- **Pair variety**: random weight of the take score 0.45 -> 0.9 in `Choreographer.ScorePair` and `TackleVariants.Score` (one-line hooks; recent-use penalty unchanged). Distinct pairs drawn rose a little (+4 strip clips and ~8 tackle pairs gained) but as many were not drawn this time: the draw is limited by fit (bearing, speed), not by the weight.
- **Failed catches**: `catch_fail` for 3 of 4 receiver drops (was 2 of 4), alternating A / B takes (`PickEither`); a defender who drops the interception plays `catch_fail_b` (18 takes, never asked before). Drops are rare: 13 failure-type clips played in the game.
- Strip tackles are broken tackles (wave 2 item 4): 4 more strip clips played, FLOAT_GROUND_ACTION back to 82.

## Group by group: why the rest is not used
- **Yan backup (118 takes)**: duplicates of clips Clean/ already holds under another prefix (`BGS_CB_*` 14 takes -> 40 clips, `OLINE_MAN_3 ...017-027` -> `OLine017_027__*`, `kickoff_catch_00_FIX`, `slowdown_stop_*`, `QB_pumpfake_pass_005_*` Faster / RootLeft_Faster); `FrameDelay` / `Choppy` / `Faster` / `ControllerTest` / `RunnSprint` / `4BasicAnimations` are test variants of the same takes; `2pstance_to_sprint_*` / `3pstance_to_sprint_*` exist as `2ptstance_to_sprint_*` and the `stance_to_sprint` pool is already 24 unplayed; `sprint_*`, `run_loop`, `Sprint004`, `Spiral_movements`, `BGS_158_Dribble_*` are locomotion (NATURAL-3's matcher); `JukesFRCritCascadeurTest3` is a Cascadeur test rig (346 bones, 97 frames); Referee / CoachSuit / Heroic pose / Player card: no action.
- **BoNES Retargets multi-take FBX**: every football take is already in Clean/ (`DLineMoves__DLINE_*`, `OLine01_016__*`, `OLine017_027__*`, `Paired__IMPORT_OT_vs_LB_001_AB`, `root|BigHit*`, ...); the `IMPORT_` copies are the long raw version of the same takes, used through the windowed `RAW__DLINE_*`. `QBSneak_28`, `QBPasses01_10`, `QBSnapActions_09`, `UndercenterSnap_08` are QB-2's.
- **`_clean_retarget`**: 23 imported (wave 2); 17 QB takes belong to QB-2; `YAN__kickoff_*` (4): one transition drawn, `kickoff_catch_idle` / `kickoff_jog` have no key the engine asks for; `Player_interaction_002` (150 s idle talk): no usable window.
- **PPF `player_animated`**: 3ds Max Biped rig (Clinch, Helmet_TakeOn, Kick, Throw, sprint, sprintball, MachoWalk): needs a Biped -> ARP retarget; the only keys they would feed are QB throw / kick (QB-2 / kicking), not mine. Not done.
- **MC Core Motion dances (5)**: imported (`celebrate_td_mc`), not drawn in this game either (about 5 touchdown celebrations per game, 5 pools).
- **`unref_keys.txt` keys**: `be_tackled` / `be_trucked` / `cb_shuffle` / `catch_fail` are asked (11/18, 6/13, 9/18 played); `tackle_fail` (+ `_b`) are the BigHit failure takes (booked as BigHitShrug); `cb_rotate`, `cb_sprint`, `route_cut`, `route_break`, `turn_left/right`, `shuffle_*`, `strafe_*`, `slowdown`, `walk_start/stop`, `sprint_alt`, `backpedal_to_sprint`, `bc_stop` (66), `bc_turn_*`, `bc_circle`, `stop_sprint`, `sprint_turn`: movement transitions chosen by the motion matcher (NATURAL-3); `AnimKit.Stop / Cut / BreakOnBall` have no caller and a second owner of stops and turns would fight the matcher's own. `stance_dline`, `stance_holder`, `stance_4pt`, `stance_wide_defense`, `kickoff_att/def_fidget`: stance poses (STANCE-HEAD; the fidgets need a one-shot over the looping stance clip in `StanceClips`). `trench_atk/def/win/lose/hold/run`: single-actor hand-fight outcomes; the sim has BlockWin / BlockLoss / Pancake events but the view feeds the engaged pairs, not these. `dl_rush_rip` (2): `RushMove.Rip` plays the `ol_block_fail` escape takes. `juke_tempo`, `juke_box`, `rocker_step`, `spin_move`, `stiff_arm`, `truck_alt` (A-only studio takes, 130 clips): the carry take layer is OFF by default (`DebugNoCarryTake`: its upper-body take dragged the planted feet, slide 0.2 -> 1.0-1.5 m/s) and their defenders (B) were never recorded as pairs. `injury`, `draft_night`, `endzone_save`, `man_coverage` (+ `_b`): the simulation has no injury, draft-night or end-zone-save event and no man-coverage tackle/jam pair slot.
- **OVERLAP_BODIES_STANDING_OR_BLOCKING** (17 -> 49 -> 31): the listed samples are pre-snap QB-vs-centre hips 0.1-0.2 m apart (the mocap under-centre stance, QB track) and post-play block / idle pairs; none is a clip of this track's pools (the engagement pools only got rarer after the probe). Not fixed; between 17 and 49 the run-to-run spread is as big as the change.

Tests: no new code needing tests; the compile check passed (`-quit`, no `error CS`). No new scripts (no `.meta` needed).
