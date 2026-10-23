# ANIMLIB track log (v0.10.0, playtest note 8)

Owner note 8: add more of the Draft Fever Bowl animations and keep cleaning them (hands, feet, skeletons) until everything looks right. Everything below was measured with the new per-clip QA (`ClipQa`), same metric before and after, on a private Unity copy (`D:\PIXFootball\Work\AnimLib\PIX`, HEAD + ANIMLIB files only). The shared-tree `_Licensed` (junctioned) now holds the new takes, the cleaned clips, the rebuilt ArtLibrary and `PixClipTags.asset`.

## 1. Inventory (what the owner gave vs what the game plays)

| | before | after |
|---|---|---|
| Distinct takes found in the owner's sources (by name; Blender scan of 103 `.blend` = 2,723 actions / 1,103 names, 73 loose FBX, 535 raw studio files = ~178 takes, Motus, BoNES packs) | ~2,100 | ~2,100 |
| Imported as FBX clips (`Animations/*`, Humanoid) | 1,503 clips / 1,306 FBX | 1,666 / 1,469 (+163 re-baked takes) |
| Cleaned `.anim` clips (`Animations/Clean`, includes mirrors + segments) | 2,179 | 2,562 (+383) |
| Registered in ArtLibrary (unique clips / entries) | 1,338 / 1,550 | 1,486 / ~1,740 (final, section 9) |
| Reachable from code (keys referenced by game scripts + controller builders) | 716 | 731 through live hooks (section 9; 811 counting helpers without a caller) |
| Rejected by QA / dropped from live keys (not registered; a key never loses its last variant) | - | 71 (list: `animlib_rejects.txt`) |

Unmatched takes before: ~386 (name match). What they are: ~100 raw studio takes that are the un-numbered duplicates of the retarget packs (BigHit / ContestedCatch / Man_Coverage variants, OL footwork), ~100 Aug-2024 legacy-rig takes (`DraftFever_Player_M_Animations*`: trench holds, shoulder tackles; hand-keyed on the old Draft Fever rig, superseded by Motus, NOT imported), ~60 edit-history `REMAP / COMBINE` copies inside blends, 14 `MotionMatchingDemo` walks (AnimUprising third-party, "DontUpload": NOT imported), the rest real new content, which is now in: kickoff line idles (33), QB under-centre / shotgun cadences (10), QB snaps + dropbacks (22), centre + long-snapper mocap (4), 4-point DL idle + get-off (2), crowd stand / sit happy / sad (17), draft night (12 files, 6 pairs), end-zone save (6, 3 pairs), ankle breaker pair, trench / truck / takedown / cut (13), ball-carrier juke finishing (wave 2, 37 raw takes: China route, spin dodge, tempo change, HB stiff arm, QB falling throws, injuries, man coverage).
Archives: the only `.7z` is `Final All Sizes.7z` (rig pack, no animation). The `.rar/.zip` packs were extracted earlier (tar.exe handles them). `retarget_actions.py` now also takes `.blend` sources (see section 7).

Per category (not rejected / registered / in play before / in play after; the full per-clip table with positions, foot phase, sync time is `animlib_clip_tags.csv`):

| group | clean | registered | before | after | biggest unused keys (clips) |
|---|---|---|---|---|---|
| QB | 677 | 384 | 170 | 183 | qb_pump_fake 6, qb_fake_handoff 6, qb_falling_throw 6, qb_hail_mary, qb_sneak |
| RB | 325 | 216 | 108 | 114 | stiff_arm 6, juke_tempo 6, catch_fail 6 |
| WR | 204 | 204 | 96 | 102 | stiff_arm 6, juke_tempo 6 |
| TE | 368 | 311 | 171 | 177 | pair B-sides |
| OL | 170 | 113 | 80 | 81 | trench_atk 3, ol_*_b |
| DL | 207 | 135 | 61 | 64 | stance_dline 6, dl_wrong_arm 6 |
| LB | 134 | 124 | 73 | 73 | cb_shuffle 12, cb_rotate 6, man_coverage 6 |
| DB | 139 | 129 | 78 | 78 | cb_shuffle 12, cb_rotate 6, man_coverage 6 |
| K/P | 9 | 9 | 8 | 8 | kick_squib |
| LS | 7 | 4 | 1 | 4 | - |
| ref | 23 | 23 | 21 | 19 | ref_ball_ready, ref_shuffle_l/r, ref_walk_back |
| celebrations | 98 | 98 | 14 | 59 | celebrate_qb 6, dance, draft_night (no caller) |
| reactions | 57 | 57 | 5 | 31 | injury 6, endzone_save (no caller) |
| tackles | 289 | 223 | 114 | 116 | contact_tackle_b / contact_sack_b (partner sides) |
| blocks | 163 | 106 | 44 | 44 | dl_wrong_arm 6, ol_*_b |
| catches | 70 | 70 | 53 | 53 | catch_fail 6, catch_contested_b |
| transitions | 240 | 186 | 144 | 140 | backpedal_to_sprint 6, slowdown 6, shuffle_to_sprint 6, turn_right 5, getup_side 8 |
| sideline / huddle / walk | 108 | 108 | 26 | 63 | crowd_* 24, coach_ambient 6, idle_conversation 4 |

"After" counts keys referenced from code, including `AnimKit.Extra2` helpers that no other track calls yet (kickoff idles, cadences, centre snap, 4-point get-off, crowd, draft night). Hooks live today: touchdown celebrations (`AnimKit.Celebrate`), sideline reactions (`AnimKit.Reaction`: cheer, frustration, celebrate_td_2/3). Net live gain is small (716 -> 724) because 30 previously used clips were rejected; the other 87 are waiting for callers.

## 2. QA, per clip (`Editor/Tools/AnimLib/ClipQa.cs`)

Measured on the Medium body, sampled at 30 Hz: foot contact (sole within 4 cm of the turf for 2+ frames, planted-foot speed = slide; treadmill cycles are not scored as slide), ground penetration / float, hips-to-shoulder twist (upright frames), torso twist / bend / head turn in joint degrees (muscle limits), knee / elbow hyperextension (muscle above straight), bone pops (>50 deg per frame on 11 body bones), jitter, wrist and forearm limits and flips, finger data and curl, T-pose frames, arms-out (T look) lead / tail / run, dead time, loop seam, plus the three import faults MOTION reported (heading, floor level, rest-pose foot asymmetry). Score 0-100; reject below 50, or 8+ pops, or an all-T-pose take. `CleanBatch` writes before/after score per clip.

Results, 2,200 clips cleaned in v0.8 (before = untouched Clean copy, after = now). New 450 clips are in the last row.

| | before | after |
|---|---|---|
| mean QA score | 69.7 | 80.2 (new clips 81.2) |
| clips with any fault | 100% | 77.5% |
| foot slide fault (>25% of planted frames over 0.25 m/s, or mean >0.28 m/s) | 78.0% | 72.2% |
| mean planted-foot speed | 0.78 m/s | 0.60 m/s |
| ground fault (penetrate >5% of frames or float >4 cm) | 25.5% | 14.3% |
| hand fault (no finger data, wrist / forearm over limit) | 100% | 11.3% |
| twist fault (waist > 85, torso > 80, bend, head) | 11.6% | 2.6% |
| skeleton fault (knee over-straight > 8 deg, elbow, 2+ pops) | 13.8% | 5.2% |
| rejected | 12.0% | 1.4% (30 of 2,178; 46 of 2,628 including 16 of the new; plus 22 more dropped from live keys, section 9) |
| improved / worse / same score | | 2,063 / 99 / 15 (the "worse" ones took the new arms-out penalty) |

What the cleaning does now (`ClipPolish.cs`, run inside `ClipCleaner.Clean` and as `AnimLibBatch.PolishAll`; each pass is kept only if the score does not drop):
* Hands: no clip in the library had any finger data (every finger muscle was 0 = straight board hands). Hands are now authored per situation, with measured curl directions (muscle -1 = curled, probe in `AnimLibProbe`): relaxed, running loose fist, ball grip (carry arm by chest distance, QB throw hand until the release then open, both hands on the ball in drops), open reach then grip at the catch moment, wrap at tackle contact, flat spread hand on the turf or held up. Wrist limit clamp, forearm twist flip smoothing.
* Skeleton: torso twist / bend / head turn summed limits (72 / 95 / 70 / 95 deg, excess compressed), knee and elbow over-straight clamp at 1.04.
* Feet: for travelling clips the planted foot's world velocity is removed from the root (3 passes, bounded to 35% change of the travel, kept only when slide drops), the body is lifted where a sole goes under the turf.
* Floor level and heading (MOTION's three faults, below), arms-out lead-in / tail trim (below).
Rejected clips cannot be fixed by these passes (pops from hard cuts, 44 of them are BoNES QB falls / sacks / celebrations); they stay in the files and out of the library.

Still wrong: **foot slide is still flagged on 72% of the clips.** The root-lock helps travelling clips but in-place transitions (CB idle-to-sprint, stance rotates), paired takes (root frame is shared and untouchable) and jukes / cuts keep sliding by measurement; fixing those needs foot IK at runtime, not clip edits. Hands are numbers only: the stick sheets have no fingers and I did not render the HDRP player close up. 100 of the old clips still have 2+ pops (fast dives and spins), 50 a hips-to-shoulder twist over 85 degrees (tackles, falls); library-wide 142 and 66 (section 9 for what that means for live keys). `cycle_speeds` SKATE flags: 20 before, 20 after (no gain).

## 3. The three import faults MOTION reported (rest pose from the first frame, yaw offset, floor offset)

Our takes are muscle-space Humanoid clips on one avatar family: every new FBX is a Blender re-bake onto the clean 95-bone skeleton of `StiffArm_14.fbx`, whose rest pose is the armature rest pose, not a take's first frame (Motus and the BoNES retargets are vendor Humanoid FBX with valid automap). Measured on the clips:

| fault | metric | before (2,200) | after (2,628) |
|---|---|---|---|
| heading offset | start heading more than 15 deg from +Z, non-paired, not a turn / cut | 180 | 29 (all 29 are QB / misc takes the pass could not rotate without lowering the score) |
| floor offset | planted-foot ground level more than 3 cm off the turf (upright clips) | 164 | 24 |
| rest pose from the first frame (one foot higher than the other) | left / right 10th-percentile sole height differs by more than 3 cm | 42, none over 5 cm | 55, none over 5 cm |

So there is no systematic rest-pose fault in the DFB set (MOTION saw 5-9 cm on every Mixamo run); the 3-5 cm asymmetries are real one-legged poses. Heading and floor are fixed by the polish pass (`yaw:` / `floor:` in `polish_log.csv`); a take is rotated about the vertical, or shifted in height, with a guard that never pushes a hand-down stance into the turf.

## 4. OL / DL stance-to-block (BODIES report: "hands drop to the turf, then both arms out to shoulder height ~0.3 s after the snap")

Cause found: every `OLine01_016 / OLine017_027 ... stance_to_Block_Run_Foot_Work_Left` segment started with the arms spread out (hands 10 cm below shoulder height, 1.4 m apart: the leftover calibration / T-pose lead-in), closing over the first 0.5 s. The stance faded into it, so the arms went out about 0.3 s after the snap. Measured with `ArmCheck` (hands within 30 cm of shoulder height, 85 cm apart, each 36 cm to its side).
* Fix: `ClipPolish` cuts the arms-out lead-in (and tail) of every non-paired take, and `AnimLibBatch.PairLeadIn` cuts it from both actors of paired takes by the same amount (24 pairs, `animlib_pair_leadin.txt`, e.g. DLINE_Wrong_Arm had a 4.7 s T-pose lead-in, BC juke pairs 2-3 s).
* Result: OL stance-to-block takes (45 clips): arms-out lead-ins 23 -> 0, arms-out clips 34 -> 0, mean QA 83.4 -> 89.6. Arms-out clips in the library 370 -> 237; the rest is 217 paired tackle / block takes (kept in sync, not trimmed), fair catch, and a few shuffles that really are arms-out. Two all-T-pose takes (`BONES_QB_ready_to_pass_loop_001`, `BONES_backpedal_to_shuffle_315_002_s03` + mirror) are now rejected (QB track: the ready loop was a `qb_ready_pass` variant).
* Good OL / DL stance, pass-set, run-block, get-off takes (score >= 75, no arms-out) for CHOREO / BODIES: `animlib_ol_dl_good.csv` (106 rows; key, positions, entry / exit foot, sync, speeds, score). `ol_run_block` registers only 6 variants (library cap); all 45 are in the tag table, ask `ClipTagTable` by name or raise the cap in `ArtLibraryBuilder` when CHOREO wants them.
* DL stance-to-block / bull / swim / shed pairs were checked with the same test (clips with `dline`, `swim`, `bull`, `wrong_arm`, `3point`, `4point` in the name): the paired ones keep their authored frame; only the Wrong_Arm T-pose lead-ins were cut.

## 5. The tag table for CHOREO and QB

`Assets/_Licensed/Resources/PixClipTags.asset` (runtime class `Art/ClipTagTable.cs`), CSV twin `animlib_clip_tags.csv` (2,628 rows). Per clip: semantic key + all keys, category, positions, duration, loop, entry / exit foot (L / R / B both / A air) and gait phase (0 = left strike), sync time + kind (release 96, catch 79, contact 410, exchange 130, plant 125, strike 41, snap 24, kick 9; 914 clips have one), root speed, gait speed (planted foot speed: the ground speed the steps were made for), root direction, turn, QA score, reject flag, twin clip.
Query API `Gameplay/Player/ClipSelect.cs`:
* `ClipSelect.Query(key, role, entryFoot, maxLen, minScore)`, `Pick(key, role, seed, entryFoot, maxLen)`, `PickMoving(key, role, dirDeg, speed, seed)`
* `ClipSelect.SyncTime(clip)` and `ClipSelect.Synced(clip, eta, lead, tail)`: a `ClipPlan` whose sync moment lands `eta` seconds from now (starts mid-clip, or slows down when there is no lead-in).
Rejected clips are never returned. Locomotion loops are tagged but belong to MOTION.

## 6. Wiring (AnimKit.Extra2.cs, one-line hooks in AnimKit.cs / AnimKit.Extra.cs)

New ArtLibrary keys (ArtLibraryBuilder rules, string keys): kickoff_att_idle 18, kickoff_def_idle 9, kickoff_def_run 3, qb_cadence_under 5, qb_cadence_shotgun 6, center_snap 1, long_snap (+ real `LS_snap_00`), stance_center / stance_snapper / stance_dl_4pt (+ real `C_idle_00`, `LS_idle_00`, `Dline4pt_idle_00`), dl_getoff_4pt 2, qb_drop_walk 3, qb_drop_settle 3, qb_snap_dropback 4, qb_snap_hands 3, qb_snap_drop_under 4, qb_throw_on_run 1, qb_throw_tackled 1, trench_atk / trench_def 3+3, takedown 1, truck_alt 4, ankle_breaker 1(+b), crowd_{stand,sit}_{happy,sad} 24, crowd_posture 2, draft_night 24(+b), endzone_save 9(+b), celebrate_big 17, celebrate_td_all 17, react_frustration 16.
Live: `AnimKit.Celebrate` rotates over celebrate_td / td_2 / td_all / td_3 (8 -> ~30 clips); `AnimKit.Reaction` widens "cheer" (+ big-play celebrations), "frustration" (+ Motus frustrations), "celebrate_td_2/3". The real centre / long-snapper / 4-point stance clips are now variants of the existing stance keys, so StanceClips picks them (BODIES: check them, they are mocap, see `sheet_animlib_cmp_0.png`).
Helpers waiting for a caller (all return an empty plan if the key is missing): `KickoffLineIdle(kickingTeam, seed)`, `KickoffRunUp`, `Cadence(seed, underCenter)` (QB), `CenterSnap`, `FourPointGetOff`, `QbDropSettle(steps, seed)` (QB), `SidelineIdle(seed, seated)`, `CrowdIdle`, `DraftNightPair`, `EndzoneSavePair`, `IdleVariantWide` (used by `IdleVariant`, which has no caller yet). Huddle, walk-back, ref and get-up pools were not widened: no new takes for them, the existing pools are already used.

## 7. Tools and files

* `Tools/Blender/retarget_actions.py` (`.blend` sources, `--obj`, `--fps`), `blend_export_actions.py` (blend action -> FBX, needed because baking from a library-loaded armature collapsed the pose), `anim_inventory.py`, `fbx_inventory.py`, `blend_rigs.py`.
* `Editor/Tools/AnimLib/`: `ClipQa`, `ClipPolish`, `AnimLibBatch` (Baseline, PolishAll, Rebuild, PairLeadIn, Sheets), `AnimLibTags`, `AnimLibProbe`, `ArmCheck`; `ClipCleaner` (polish hook, windows), `CleanBatch` (paired raw takes cut with shared windows, QA columns), `StickSheet.RenderGrid` and `path:` clip lookup, `ArtLibraryBuilder` (new rules, reject list).
* Runtime: `Art/ClipTagTable.cs`, `Gameplay/Player/ClipSelect.cs`, `AnimKit.Extra2.cs`; tests `Tests/EditMode/Gameplay/AnimLibTests.cs`.
* Data: `animlib_clip_tags.csv`, `animlib_qa_before.csv`, `animlib_qa_final.csv`, `animlib_rejects.txt`, `animlib_ol_dl_good.csv`, `animlib_pair_leadin.txt`, `Docs/ArtLibraryReport.md` (regenerated).
* Contact sheet of 24 new clips (side + front, 4 poses each): `D:\PIXFootball\Renders\animlib_v0100\contact_sheet_24_new_clips.png`; stance comparison and before / after strips `sheet_animlib_cmp_*.png` in the same folder.

Looked at the contact sheet once: the centre, long snapper and 4-point stances read as real stances; kickoff idles, cadences and crowd clips are in-place and clean. `QB_UNDERCENTER_SNAP_004` is a long lean-over take; `RAW__Draft_night_003_s01_A` still starts with the arms out (paired take, not trimmed to keep the actors in sync). Segments of the draft-night / end-zone pair takes with a score under 50 are rejected.

## 8. Test run

One full EditMode run (`-testCategory "!Slow"`) on the private copy (HEAD + ANIMLIB files): 1,170 tests, 1,166 passed, 4 failed; the 7 new `AnimLibTests` pass. The 4 failures are not animation: `PhotoPlayerTests.EveryLodFollowsTheAnimatedSkeleton...` and `PhotoRosterV2Tests.FifteenHumanoidBodies...` (mesh bind-pose counts 52 vs 54, body asset state, BODIES / PHOTOREAL), `GfxTests.Lod_ClassicCameraShowsLod0AroundTheBall` and `Lod_NoTierBiasCrushesTheSilhouette` (LOD tiers, the perf session's files are not in this snapshot).

## 9. Follow-up pass (coordinator: close note 8)

**Callers hooked (one-liners, shared tree):** `StanceRules.ClipKeys` (KickoffRunner -> `kickoff_att_idle`, KickoffReceive -> `kickoff_def_idle`: 9 + 3 loop-closed line-up stands; the 15 fidget one-shots `kickoff_*_fidget` have no caller, a looped helmet fix every 1.3 s would look like a tic); `SimAgentView.Connect.GetOffByFirst` (DL in a 4-point stance gets the `dl_getoff_4pt` take, 2 clips); `AnimKit.Connect.HuddlePlan` (two of three huddle players keep the authored stand, the rest get lively / look-around idles); `MatchOfficials.Spawn` (the third coach of each team plays one of the 6 sideline ambient takes, loop-closed); `AnimKit.Celebrate` / `AnimKit.Reaction` (before). The real centre / long-snapper / 4-point stance clips are live through the stance keys.
**Not hooked, on purpose:** QB cadence / drop-settle / centre snap. The QB pre-snap, cadence, snap and pocket are the pose layer's (procedural, with measured timing and hands under the centre); a mocap cadence has its own crouch height and would fight it, and the centre's snap is anchored to the simulated ball. They stay as `AnimKit.Cadence(seed, underCenter)`, `QbDropSettle`, `CenterSnap` plus `ClipSelect` for a future pass. Draft night, end-zone save, crowd and bench have no scene in the game (the crowd is baked billboards, no bench players, no draft stage); trench / truck / takedown / ankle breaker are CHOREO's contact selection. Their clips are tagged and registered.
**Clips live in play (keys the game reaches through a live hook, on the rebuilt library): 716 before -> 731 after.** The gain looks small because 43 previously live clips left the library (67 rejected by QA, 22 dropped from live keys, below), and another ~80 are registered, tagged and have helpers but no caller.

**Hands re-checked with a close-up render** (`D:\PIXFootball\Rendersnimlib_v0100\hands_closeup_v2.png`: catch reach, catch grip, carry grip, tackle reach, tackle wrap, hand on the turf, celebration, throw hand; real HDRP player). Looked once: the fingers read as hands (open reach with extended fingers, curled ball grip, flat hand on the turf, wrap), no board hands, no backwards fingers or broken wrists. Found and fixed: the "open" and "flat" poses were built around muscle 0, but the straight finger is about +0.4, so they read as claws; they now sit at +0.2..+0.38 and every clip's hands were rewritten (`HandsRetune`). Still wrong: the closed grip / wrap poses look a little clawed (finger spread too wide), the camera framing of the strip is crude.

**Multi-pop and over-twist clips on live keys:** of the 794 registered clips on live keys, 72 clip-key pairs were flagged (2+ pops, hips-to-shoulder twist over 85, torso twist over 80). Fixed at clean time: torso twist limit 72 -> 50 and head turn 95 -> 80 (BodySanity allows 46 for a throw), a rate limiter for single-frame muscle jumps (`ClipPolish.Pops`), knee / elbow clamp. Result over all clips: hips-to-shoulder twist over 85: 130 -> 50 (old clips), library-wide 66; pops: 144 -> 100 old clips, 142 library-wide; twist faults 6.8% -> 2.6%. 22 flagged clips on live keys that had a clean sibling variant were dropped from the library (`live-key` rows in `animlib_rejects.txt`); 22 clips on live keys are still flagged because their key has no clean variant (qb_scramble 6, qb_run_left 6, one-variant keys, referee takes that the cleaner never touches).

**Last two items.** (1) Grip and wrap hands: the finger spread of the closed poses is now 0 (fingers together; verified in the clip data: all four finger spreads 0.00 on the catch, tackle and carry takes), relaxed spread cut to 0.04, every clip's hands rewritten. Re-rendered strip: `hands_closeup_v3.png` (same 8 cells). Looked once: reach, flat-on-turf and throw hands read well; the grip and wrap still read slightly clawed at the strip's camera angle because the curl of the finger joints (not the spread) dominates; the lens framing is crude. (2) Still-flagged clips on live keys (15 on the first count, 12 of 788 on the final library): `ClipPolish.Force` (twist 36 / bend 80 / head 70, pop limiter 0.16, knee / elbow clamp) was run on all of them, the referee gestures and `ref_spot_ball` / `ref_penalty_flag` included (copied into `Clean/` under their own names, which replaces the originals in the library; pops 17 -> 0, 10 -> 2, 2 -> 0, QA 54-71 -> 74-94). Kept when the score would fall by more than 3: 9 QB takes whose twist is the throw / scramble itself. Left flagged on live keys, 12 clips: qb_frustration 2, qb_throw_offbalance 2, qb_run_left 2, one each of qb_sack, qb_throw_lob_medium, qb_scramble, qb_throw_bullet, ref_whistle, ref_spot_ball (2 pops each). Log `animlib_livefix_log.txt`.
