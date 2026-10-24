# ANIMLIB track log (v0.11.0, playtest notes 5 and 11)

Owner note 5: more mocap and much more cleanup, use plugins / products / tools, say what is needed. Note 11: feet, ankles, legs. Everything below is measured with the v0.10 per-clip QA (`ClipQa`, same metric before and after) plus new leg metrics, on a private Unity copy (`D:\PIXFootball\Work\AnimLib\PIX`, which junctions `_Licensed`, so the cleaned clips, the rebuilt ArtLibrary, `PixClipTags.asset` and `PixClipFacing.asset` are already in the shared tree). The v0.10 clips are kept at `D:\PIXFootball\Work\AnimLib\v0110\clean_v0100` (2,545 `.anim`). "Live" = the 788 clips (167 library keys) that a game script or controller reaches; 763 of them exist as Clean clips and are the QA population below. "All" = every clip in `Animations/Clean` (2,545).

## 0. The finding that changes the hands (root cause, not polish)

`PoseSeq` read and wrote the 40 finger muscles under the HumanTrait names (`Left Index 1 Stretched`). The Animator only drives fingers from curves named `LeftHand.Index.1 Stretched` (the same canon `MuscleReport.Canon` already used). Consequences, all measured on the real player:

* Every cleaned clip since v0.8 had **no working finger curves**: the fingers stayed in the bind pose (index tip to hand 0.173-0.185 m whatever the clip said). The v0.10 hand authoring (relaxed / grip / wrap / open) and its two re-tunes never reached a bone; the "claw" in the v0.10 strips was the bind pose. `HandLab.Diag`: curl +0.4 vs -1.0 through the playable graph gave the same 0.173; through `HumanPoseHandler` 0.187 vs 0.101. After the fix the graph gives 0.187 vs 0.101.
* The raw takes DO carry finger mocap (`HandLab.FingerProbe`, one clip per FBX): BoNES QB 15 / 15 files with moving fingers, studio pack 1 + 2 20 / 20, retargets 3 / 4, yan 33 / 77, extras 1 / 11, Mixamo 11 / 12 (Motus has finger curves but static). All of it had been dropped by the same naming bug.
* Fix: `PoseSeq.CurveName` (read both names, write the Animator name; `Simplify` already keyed on the substrings). `FingerTransplant` copies the raw take's fingers into the cleaned window (raw take found by name minus `_M` / `_sNN`, window located by the body muscles, mean body difference under 0.08, mirrored takes mirrored). 694 clips now carry the real finger mocap, 1,851 the authored hand poses (now live), 108 had a finger-bearing raw take that did not match its window (kept authored).
* `Hands()` poses: open / flat spread cut from 0.22 to 0.08 (fingers were fanning). Close-up renders on the real HDRP player: `D:\PIXFootball\Renders\animlib_v0110\handlab.png` (curl +0.4 straight, 0 bind, -0.3, -0.6, -1.0 fist, and the old grip) and `handstrip.png` (8 situations, before | after: catch, tackle wrap, carry, hand on the turf, block hands, stiff arm, throw, celebration).

## 1. QA, before and after (same metric)

Live clips (763 found):

| | v0.10 | v0.11 |
|---|---|---|
| mean QA score | 81.8 | 87.0 |
| slide fault (QA rule, see below) | 70.0% | 54.4% |
| ground fault (penetrates / floats) | 10.9% | 4.1% |
| hand fault | 9.8% | 0.3% |
| skeleton fault (knee, elbow, 2+ pops) | 2.0% | 0.9% |
| knee hyperextension over 8 deg | 7 clips | 0 |
| wrist muscle beyond the avatar limit | 72 clips | 0 |
| clips with sole under the turf over 5% of frames | 82 | 30 |
| T-pose frames inside a take | 7 clips | 2 |
| mean sole-under-turf share | 4.0% | 1.15% |

All 2,545 clips: mean 80.5 -> 85.8, slide fault 70.3 -> 54.1%, ground 13.3 -> 5.8%, hand 9.8 -> 0.1%, skeleton 6.0 -> 5.2%, knee over 8 deg 17 -> 0, wrist 247 -> 1, penetrates 299 -> 105 clips, T-pose 15 -> 2, QA rejects 36 -> 33. Not fixed: 133 clips with 2+ bone pops (fast QB falls / dives, they are the take), 186 arms-out (paired takes kept in sync), 44 / 48 hips-to-shoulder / torso twists (tackles, falls), 60 dead-time (loops and the partner-moving halves of pairs).

Why the QA slide fault only fell 70 -> 54%: it flags any planted foot above 0.25 m/s. 226 of the live flagged clips are travelling takes (stops, cuts, idle-to-sprint: the planted foot slides at the take's native ground speed, 3-5 m/s for `Sprint_To_Stand`), 34 are treadmill loops and 154 paired takes. For those the ground speed is the take's design: the runtime `FootPlant` stride warp and the `gaitSpeed` tag match the player to it. The honest foot metric is therefore per plant (below).

### Foot metric per plant (the plants of the v0.10 clip, the same frames measured in the clip now; `AnimLibV11.PlantReport`)

| plants (live 3,514 / all 13,739) | ankle displacement while planted | ankle roll while flat | foot yaw drift |
|---|---|---|---|
| pinnable plants (old displacement within 12 cm), live 2,614 | 4.4 -> 2.2 cm | 12.7 -> 8.3 deg | 11.7 -> 10.4 deg |
| of those with 3 cm or more slide, live 1,315 | 6.8 -> 3.4 cm (665 halved or better) | 9.5 -> 3.1 deg | 16.1 -> 14.4 deg |
| all pinnable, all clips 10,865 | 4.6 -> 2.2 cm | 9.2 -> 5.4 deg | 11.2 -> 9.5 deg |
| of those with 3 cm or more slide, all clips 5,532 | 6.5 -> 3.1 cm (2,958 halved or better) | 7.6 -> 2.7 deg | 14.5 -> 12.3 deg |
| sweeping plants (foot moves more than 12 cm: treadmill, cut; levelled, not pinned), live 900 | 27.0 -> 26.9 cm (by design) | 8.3 -> 2.1 deg | 36.4 -> 34.7 deg |

Still wrong: the pinned plants keep about 2 cm (the IK error plus the simplification of the stored curve), 2,574 of the 5,532 slid plants are not halved (plants inside clips where a limit made the read-back miss, or where the take sweeps), the toe-out against the pelvis only moves 25.8 -> 25.3 deg on average (it is measured on every plant, lying and pivoting ones included; the 14 deg pull only applies to upright, non-pivoting plants), and 133 clips still have bone pops.

## 2. What the cleanup does now

* **Foot IK bake (`FootIk.cs`, new).** Per plant (sole within 4 cm, little vertical speed): the ankle is pinned to the world point where the foot landed by an analytic two-bone leg IK (the knee keeps its bend plane, reach capped at 99.2% so the leg never over-extends), the flat foot is laid level on the turf (roll out) with the toes held on the plant's heading, a foot turned more than 14 deg off the hips is brought back, the sole is pressed to the turf. The result is read back into the Humanoid leg muscles through `HumanPoseHandler` (the clip stays a plain muscle clip), then evaluated again: frames the muscles cannot reproduce (limits) get a smaller weight and the bake repeats (up to 4 rounds); a plant that comes out worse than it went in goes back to the take's own motion (per-plant cull); the clip is kept only when score, pops, foot pops, knee, ground and the per-plant numbers do not get worse. The root is not touched, so paired takes stay in sync. Pin releases smoothly over 5 frames, never grabs a foot more than 16 cm from its anchor.
* **Floor.** Body lowest point over the take (soles, hands, knees, elbows, hips, spine, head with limb radii): a take sunk more than 3.5 cm into the turf is lifted (pairs by the smaller of the two lifts, so they stay in sync): 289 clips lifted.
* **T-pose frames** inside a take bridged between the neighbouring good frames (14 clips); **wrist / forearm** hard limit (452 clips); **dead time** at the ends of single takes cut (29, pairs by the smaller cut).
* **Forced limits** (twist / bend / head limit, pop limiter, knee clamp) on the 196 clips still flagged: 114 changed.
* **Hands:** section 0.
* **Written clips** use monotone (Fritsch-Butland) tangents in `Simplify` (the Hermite curve between kept keys no longer overshoots; finger muscles saturate at +-1), leg muscles of IK clips at 0.0012 tolerance, and every written clip is re-measured from the stored file and rewritten near-lossless or rolled back when it scores worse (67 rewritten, 37 rolled back).
* Tools new this pass: `Editor/Tools/AnimLib/` `FootAxes` (foot axes from the bind pose, plant detection, leg metrics), `FootIk`, `ClipPolish2` (T-pose, wrist, floor, dead cut), `FingerTransplant`, `AnimLibV11` (QA of lists, `Pipeline`, `FootPass`, `ForceFix`, `PlantReport`, `ContactSheets`, `LyingReport`), `HandLab`, `FootShots`. New QA columns: `plantN, plantDisp, plantYaw, plantRoll, footPops, plantToe`. Blender 5.2 headless: used (its bundled Python) for the data work (inventories, usage ranking of the engine audit, plant statistics); the muscle-space cleanup is better done where the avatar's muscle limits and `HumanPoseHandler` are, so no clip went through Blender this pass.

## 3. Real usage: the 24 most-played clips (engine audit v0.10, 230,819 clip frames over the recorded plays)

`BlockHitCStale_A` 30,125 frames, `BlockHitRStale_A` 20,440, the OL `stance_to_Block_Run_Foot_Work_Left_001_s01` (+ `_M`) 20,429, `BlockHitCPass_A/B` and `BlockHitRPass_A/B` 30,000, `BONES_QB_shotgun_ready_to_catch_loop_004`, the OLM shuffle-stand loops, `BlockShove*`, `BlockDrive*`: the line's pair takes are most of what the game plays, which is why B-009 / B-012 matter. Contact sheets before | after (stick figures, 6 poses side and front, left v0.10, right now): `D:\PIXFootball\Renders\animlib_v0110\contact_sheet_top24_before_after_1.png` / `_2.png` (list `top24.txt`). Feet close-up strip on the real player (6 frames across the longest plant, red marker line where the foot landed, row 1 v0.10, row 2 now; 5 clips): `feet_strip.png`. Looked once: no broken knees, flipped feet or pops in any of them; the before / after difference at a 1.6 m close-up is a few centimetres (the stances and the OL footwork already looked planted), the proof is the per-plant numbers above, not the picture. The hand strip shows the visible change (stiff arm and throw hands open and straight instead of the bind claw).

## 4. More mocap

* **Inventory, again:** nothing new in the owner's folders beyond what v0.10 listed. `PPF NEW ASSETS\american-football-player-animated` (Kick, Throw, sprint, sprintball, Helmet_TakeOn, Clinch, MachoWalk) was already imported by the PHOTOREAL track (`PhotorealPlayer/Clips`, keys `kick_off_biped`, `qb_throw_biped`, `helmet_off`, `clinch_a/b`, `walk_macho`). `Desktop\Assets\offensive-lineman-setting-his-feet-in-protection.zip` and `quarterback-winding-up-for-a-deep-throw.zip` are posed static meshes (one pose, no animation). `archivedwl-738.zip` (250 GB) is the Draft Fever archive that `_AssetSource` was extracted from. What is left in `_AssetSource` is the ~100 hand-keyed Aug-2024 legacy-rig takes (trench holds, shoulder tackles; superseded by Motus), ~60 blend edit-history copies and the 14 AnimUprising walks (third-party, "DontUpload": not imported, not allowed). Say if you want the legacy trench holds retargeted; I judged them below the Motus pairs.
* **Ones with no caller (155 library keys, 700 clips by the strict count):** wired in my files as `AnimKit.Extra3.cs` helpers (every one returns an empty plan when the key / QA-passed clip is missing, `AnimKit.Mocap(key, role, seed, maxLen, eta)` is the generic one, pairs through `TryPair`). The callers belong to other tracks; suggested caller and situation:

| key (clips) | suggested caller / situation | owner |
|---|---|---|
| `qb_pump_fake` (6) | pump fake before the throw when the target is covered / pressure; release moment tagged: `AnimKit.QbPumpFake(seed, eta)` | QB |
| `qb_fake_handoff` (6) | play-action plays, exchange moment tagged: `QbFakeHandoff` | QB |
| `qb_hail_mary` (6) | last play of a half, throws over 45 yd: `QbHailMary` | QB |
| `qb_falling_throw` (6, +b 6) | release while being hit (pair with the pass rusher): `QbFallingThrow`; the pair through `TryPair("qb_falling_throw", ...)` | QB + SIM-FLOW |
| `qb_throw_shovel` (6), `qb_toss` (6) | shovel pass / pitch to the RB in option plays: `QbShovel`, `QbToss` | QB |
| `qb_throw_on_run` (1), `qb_throw_tackled` (1) | throw rolling out / as he is going down | QB |
| `qb_look_around` (6), `qb_hesitation` (6), `qb_sidestep` (6), `qb_ready_pass` (6) | pocket idle between the drop and the release, footwork around a rusher | QB |
| `qb_sneak` (4, +b 4) | 4th and 1, goal line (pair with the OL pile) | QB + SIM-FLOW |
| `qb_slide` (3), `qb_dive` (6) | end of a scramble, dive for the line to gain | QB |
| `qb_snap_under` (4), `qb_snap_shotgun` (6), `qb_snap_hands` (3), `qb_snap_dropback` (4), `qb_snap_drop_under` (4), `qb_drop_walk` (3), `qb_drop_settle` (3), `qb_cadence_under` (5), `qb_cadence_shotgun` (6) | the snap and dropback sequences (`QbSnapUnder / Shotgun`, `AnimKit.Cadence(seed, underCenter)`, `QbDropSettle`) | QB |
| `qb_hand_signal` (2), `huddle_break` (1), `huddle_leader` (1) | huddle break, pre-snap audible | SIM-FLOW |
| `stiff_arm` (6, +b 6; `_left_b` / `_right_b` 4), `juke_tempo` (6), `juke_box` (4), `rocker_step` (5), `spin_move_b` (5), `cut_hard` (4), `cut_left/right` (4), `truck_alt` (4), `truck_b` (8), `takedown` (1), `ankle_breaker` (1+1) | ball carrier moves with a defender (the A / B pairs through `TryPair`), via MOTION's matcher | RB |
| `route_break` (6), `route_cut` (6), `catch_fail` (6), `catch_fail_b` (5), `catch_contested_b` (6) | receiver breaks at the route stem; drop / contested incompletions | SIM-FLOW |
| `tackle_big_hit` (6, +b 6), `tackle_fail` (4, +b 4), `tackle_gang` (8, +b 8), `tackle_strip` (4, +b 4), `tackle_success_b` (3), `tackle_wrap_b`, `tackle_dive_b`, `tackle_arm_b`, `contact_*_b` (B sides, 107), `be_tackled` / `be_trucked` (+b), `fall_b` | contact selection (big hits, gang tackles, strips); B sides are the partner halves | SIM-FLOW |
| `dl_wrong_arm` (6, +b 6), `dl_rush_rip` (4), `dl_*_b`, `ol_*_b` (30), `push_oob` (2), `press_jam` (2), `man_coverage` (6, +b 6), `trench_atk` / `trench_def` (5) | pass rush / run block variety, press, man coverage pairs | SIM-FLOW |
| `injury` (6, +b 2), `injury_idle` (2), `getup_side` (8), `idle_conversation` (5, +b 5), `salute` (2), `celebrate_qb` (6), `dance` (1), `celebrate_td_*_b` (10), `frustration_b` (6), `react_frustration_b` (8) | injury stoppage scene, side get-up, sideline / celebration scenes (`InjuryPair`, `CelebrateQb`, `Dance`, `Salute`) | SIM-FLOW |
| `kick_squib` (1), `kickoff_att_fidget` (9), `kickoff_def_fidget` (6), `kickoff_def_run` (2), `kickoff_att_idle` (9), `kickoff_def_idle` (3), `dl_getoff_4pt` (2), `center_snap` (1) | squib kickoff; line-up fidgets (a looped fidget would tic: play once per 6-8 s) | SIM-FLOW |
| `ref_ball_ready` (1), `ref_shuffle_left/right` (2), `ref_walk_back` (1) | spotting the ball, officials moving | SIM-FLOW |
| `backpedal*` (20), `bc_*` loops (48), `cb_rotate` (6), `cb_shuffle` (12), `cb_sprint` (4), `shuffle_*` (22), `slowdown` (6), `sprint*` (28), `jog`, `walk*`, `turn_left/right` (10), `stance_3pt/4pt/dline/holder/wide_defense` (13), `strafe_*`, `zone_drop`, `lb_read_step` | reached by clip name through the motion matcher, not by key | MOTION |
| `crowd_*` (26), `draft_night` (48), `endzone_save` (21) | no scene in the game (baked crowd billboards, no draft stage) | none |

## 5. Bug rows (BUGS.md, tagged ANIMLIB)

| id | sev | result |
|---|---|---|
| B-057 clip_switch_flipflop | S3 | **Fixed** (one line, `SimAgentView.cs`): a rush-move change re-picks the pair clip at most once per move length (`_clock - _lastChangeAt >= 0.45`), so the 43 DL clip switches under 0.13 s per game cannot happen; real rush changes still apply after 0.45 s. Not measured in a match (no match was run). |
| B-056 ref-no-fg-pat-signal | S3 | **Fixed** (4 one-line hooks): `OfficialSignal.KickGood` (referee + field judge + back judge raise both arms with the existing `ref_signal_td` take), sent from `MatchDirector` for FieldGoal / ExtraPoint before the coach cheer. No new clip needed. |
| B-055 stale clip-facing table | S3 | **Fixed**: the library rebuild re-measured the facing table on the current clips (1,776 clips, 683 with a correction, 33 mirrored twins; `3pstance_aggressive_001_M` included), so the "table is stale" warning cannot occur for an ANIMLIB clip. Re-run `Build Clip Facing` after any re-cut. |
| B-012 block-feet-hover | S2 | **Fixed on the view side** (one line + `SimAgentView.AnimLibFix.cs`): `FitGround` now also runs for `Engaged` pair takes while the hips are above 0.8 m (a pancaked blocker is left alone), so the 9-21 cm hover of `BlockHitCPass_A/B` etc. is pressed to the turf. Clips: `BlockHitCPass_B` has both soles 4-28 cm up for 100 frames with the hips at 1.0 m (the whole body hovers); the clip-side floor pass only lifts takes that sink, so the hover is only fixed at runtime. Not play-tested. |
| B-011 engaged_body_vs_sim_facing | S2 | **Data checked, runtime remains.** The BlockHit / Drive / Shove pairs start with A and B 150-180 deg apart (they face each other), but which of the two faces +Z varies per take (`BlockHitCPass_A` 172 / `_B` -10, `BlockDriveRFail_A` 2 / `_B` 180); the orientation is chosen by the facing table (rebuilt, B-055) and `AnimKit.EngagedPair`. A wrong end facing comes from the choreography turn / mirrored pick (SIM-FLOW), not from a clip fault. |
| B-010 linemen_stance_entry_spin | S2 | **Data checked, runtime remains.** 227 of 245 stance clips start within 4 deg of +Z (the rest are the authored `pixstance_wr/rb/cb` at 5-15 deg and the CB rotate takes, which are rotations); floor offsets of stance clips are fixed (QB under-centre cadences were sunk 14 cm, the kickoff idles 8 cm, the 4-point stance 9 cm: lifted). The one-frame pop of the whole line at the first PreSnap frame is a missing blend from the previous pose in the stance start (`StanceClips` / `PlayStage.Stance`, CRITICAL / SIM-FLOW). |
| B-009 body_lying_while_engaged | S2 | **Handed over with data.** The pancake / fail takes put the blocker flat (hips 3-30 cm) while the sim still says Engaged: that is a sim state / choreography disagreement (`ChoreoDeferEngage`, `BlockEngagement`). `animlib_lying_clips.csv` lists the 272 clips that go down (clip, time the hips drop under 0.40 m and stay under 0.45 m for 0.5 s, lowest hips, length, hips at the end), so the state can flip to Down at that time. |

## 6. Files, data, test

* Clips: `Assets/_Licensed/DraftFever/Animations/Clean/*.anim` (2,545 rewritten), `Resources/PixClipTags.asset` (2,628 rows) and `PixClipFacing.asset` rebuilt, ArtLibrary rebuilt (1,491 registered clips, 167 live keys = 791 live clips; no key lost a clip: tag table non-rejected clips per key identical for all 314 keys).
* Docs regenerated: `Docs/QA/v0100/animlib_clip_tags.csv`, `animlib_rejects.txt` (61 rows: 33 QA rejects + 28 live-key drops; v0.10 had 46), `Docs/ArtLibraryReport.md`, `Docs/QA/v0110/animlib_lying_clips.csv`.
* Data in this folder: `animlib_qa_before.csv` / `animlib_qa_final.csv` (QA of all 2,545 clips, v0.10 clips vs now), `animlib_plants_before_after.csv` (every plant, before / after), `animlib_pipeline_notes.csv` (per clip notes of the first full pass), `animlib_top24_clips.txt`, `animlib_lying_clips.csv`. Working data, logs, the v0.10 clips: `D:\PIXFootball\Work\AnimLib\v0110`.
* New runtime code: `Gameplay/Player/AnimKit.Extra3.cs`, `SimAgentView.AnimLibFix.cs`; one-line hooks in `SimAgentView.cs`, `SimAgentView.Ground.cs`, `MatchOfficials.cs`, `MatchDirector.cs`. Editor: `Editor/Tools/AnimLib/*` (see section 2), `ClipCleaner.WriteSeq(seq, tol, legTol)` and `Simplify` (monotone tangents), `PoseSeq.CurveName`.
* **Test run** (one full EditMode `-testCategory "!Slow"` on the private copy = HEAD + ANIMLIB files + the four one-line hooks applied to the HEAD versions): 1,175 tests, 1,173 passed, 2 failed (`GfxTests.Lod_ClassicCameraShowsLod0AroundTheBall`, `Lod_NoTierBiasCrushesTheSilhouette`: LOD tiers of the perf session, not in this snapshot, same two as v0.10). 5 new `AnimLibTests` pass (finger curve names, wrist clamp, dead cut, plant detection, empty plan for a missing key). The shared tree with the other tracks' uncommitted work was not compiled by me: the new files (`AnimKit.Extra3.cs`, `SimAgentView.AnimLibFix.cs`, the `Editor/Tools/AnimLib` files) have no `.meta` yet, Unity makes them on first open.

## 7. What I need from the owner (nothing was bought, no account was created)

1. **Cascadeur** (free tier needs an account; Pro about 20 USD / month): physics-aware cleanup of the fast QB falls / dives / sacks (133 clips with bone pops that muscle-space limiting cannot fix) and foot-contact cleanup on the long studio takes. If you sign in I can export / import FBX for them.
2. **Mocap packs from the research** (`Docs/QA` football research): Tomasi football pack (15.50 USD): QB footwork and throwing variety, receiver catch / route breaks, so the QB and receiver tracks have clip material for real footwork; MoCap Online Mobility Pro (50 USD): clean locomotion / transitions (MOTION's `shuffle`, `backpedal`, stop clips that today slide 3-5 m/s). Fingers: these vendors' takes usually carry finger data now that the import no longer drops it, ask for the finger-bearing version.
3. **Final IK** (Asset Store, about 90 USD) or Unity's free **Animation Rigging** package: not needed for the baked foot lock; useful only if you want runtime hand IK on the ball / on the opponent's chest (block hands, tackle wraps), which the clips cannot give. The game already has runtime foot IK (`FootPlant`).
4. **Auto-Rig Pro** (about 40 USD, Blender): only for retargeting future packs onto the 95-bone skeleton with the leg / foot constraints that fix hover and slide at source. **Rokoko Studio** (free, account) and **Expy Kit** (free Blender add-on from GitHub, installable without an account, not installed: it targets the Mixamo-style rigs) are the free alternatives for the same job.
5. Say if you want the legacy trench holds / shoulder tackles from the 2024 Draft Fever rig retargeted (hand-keyed, judged weaker than Motus).

## 8. Follow-up (coordinator answers): bone-pop clips with our own tools, and the 2024 legacy takes

**Pop clips (133 with 2+ bone pops) fixed without Cascadeur: 117 of 133.**
* Cause found with `AnimLibV11.PopProbe`: the pops are exact 180 deg flips of the upper-leg bone with no muscle jump. In the BoNES QB takes the `Upper Leg Twist` / `Front-Back` muscles sit beyond the avatar limit (up to 301 of 301 frames at a value far over 1) and the Humanoid solve wraps, so the thigh turns half a circle and back. A second source is a single-frame 75 deg step of the root rotation (`CelebrationTouchdownFreestyleOne_12`).
* Fix (`AnimLibV11.PopFix`): upper-leg muscles hard-limited to +-1, single-frame root rotation steps bridged between the neighbours, then the per-muscle rate limiter; kept only when the pops drop and the score does not fall by more than 2 (or the pops are gone and the toe-out against the pelvis improves by 10 deg or more, score loss under 22). Pass 1 fixed 100, pass 2 (relaxed rule) 17.
* On the 100 of pass 1: pops 4.6 -> 0 per clip, mean score 64.6 -> 75.7, QA rejects 22 -> 1, toe-out against the pelvis 97 -> 48 deg (the feet were turned about 100 deg off the hips by the same out-of-range twist), slide share 53 -> 60% (more plants are now detected on legs that stand sensibly).
* All 2,545 clips now: pops over 2 133 -> 16 clips, skeleton fault 6.0 -> 0.6%, QA rejects 36 -> 10 (0.4%), mean score 86.2 (live was 87.0 before this step; the live set is unchanged apart from the QB clips).
* Left (16): `BGS__MOB1_Run_R_180_BC` (+`_M`, a fast turn), `BGS__BGS_Stiffarm_FrontL_CritSuc_BC` (+`_M`, 6 -> 4 pops, rate limit only), `CelebrationTouchdownFreestyleOne_12` (spin with a 75 deg root step the bridge did not catch), `TackleBrokenRightCHighOmo_2_B`, `getup_side_07`, `ref_spot_ball`, a few QB falls (`BONES_QB_falling_throw_forward_002_s01`). These are the ones Cascadeur would be used for; 16 clips do not justify the account by themselves.

**Legacy 2024 trench holds and shoulder tackles: 24 clips in.**
* `DraftFever_Player_M_Animations13.blend` (the most refined of the four legacy blends: 7, 9, 13, 28) turned out to carry an Auto-Rig-Pro rig (`mediumPlayer_rig`, 345 bones), so `Tools/Blender/retarget_actions.py --obj "^mediumPlayer_rig$"` re-baked 25 takes onto the clean skeleton (Blender 5.2 headless, 70 s), imported as Humanoid into `Animations/Legacy2024`, cleaned (`CleanBatch`, 28 clips with the mirrors) and run through the full v0.11 pipeline (T-pose, wrist, floor, foot IK, hands). `trench_hold_01_mirror`, `defstance_fall_*_M` and `run_stiffarm_00_M` are mirror clips.
* QA bar (score 70 or more, under 2 pops, not rejected) dropped 4: `tackle_hip_roll_right_00` (score 39, 6 pops), `get_tackled_left_side_hip_roll_00` (58.5, 5 pops), `run_dive_00` (2 pops), `trench_push_lose_00` (3 pops); they sit in `D:\PIXFootball\Work\AnimLib\v0110\legacy\dropped`. The 24 kept score 74-100 (mean 82), none rejected; QA per clip in `animlib_legacy_qa.csv`.
* Registered keys (tags for SIM-FLOW in `animlib_clip_tags.csv`, helpers in `AnimKit.Extra3.cs`): `trench_hold` 3 (looped holds, `TrenchHold`), `trench_win` 4 / `trench_lose` 3 / `trench_run` 2 (engaged lineman moves, `TrenchWin / TrenchLose / TrenchRun`), `tackle_shoulder` 2 (shoulder tackle and the jumping tackle, `ShoulderTackle`), `be_tackled_shoulder` 4 (front / back / left / right, the ball carrier going down: `BeTackledShoulder`), `defstance_fall` 4 (`DefStanceFall`), `stiff_arm_run` 2 (`StiffArmRun`). They are single-actor takes, not partner pairs. No caller yet: suggested SIM-FLOW use is the shoulder-tackle pair (`tackle_shoulder` + `be_tackled_shoulder` picked by direction) for low-speed wrap-ups, the trench takes for pass-block hand fights between the BlockHit pair takes.
* Not retargeted: the `ENGAGED MOVEMENT *` and `Trench HOLD WIN` takes (they live on separate armature objects, not the main rig), and the older copies in blends 7 and 9 (superseded by 13).
