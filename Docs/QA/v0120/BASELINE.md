# v0.12 baseline (step 0): MatchWatchAudit on the v0.11.0 code (commit 319841f)

Shared dataset for every v0.12 track: `D:\PIXFootball\Renders\match_watch_v0110\baseline\`
(`issues.txt`, `summary.txt`, `plays.txt`, `events.log`, `stance_snapshots.csv`, `float_probe.csv`, `sidestance\`, `anomaly\`, `strips\`, `hud\`).

Run: private copy `D:\PIXFootball\Work\Audit\PIX`, `-pixMatchWatch`, Buffalo v Miami, 3-minute quarters, clear weather, night, seed 110110, cinematics as shipped (`-pixMwCine` default 0 = full), log `D:\PIXFootball\Work\Audit\logs\baseline.log`.
Result: 184 plays, 10-38 final (same game as the v0.11 integration run `g1`), 251,371 frames, 63.4 min wall, finished. The run is deterministic: most counts reproduce `g1` to within a few units.

## 1. Issue counts by detector (baseline vs v0.11 integration, `STATUS.md`)

| Detector | v0.11 integration | baseline | Note |
|---|---|---|---|
| CULL_OFFSCREEN_* / VIS_BURIED_* / BALL_BELOW_TURF / SYNC_HIPS_FAR_FROM_ROOT | 0 | 0 | stay fixed |
| FLOAT_STANDING_BOTH_FEET_UP, **v0.11 rule** | 30,918 | 30,883 | reproduced: 30,687 are bodies moving on the stage |
| FLOAT_STANDING_BOTH_FEET_UP, **fixed rule** (body still on the stage) | n/a | **190** | see section 2 |
| SYNC_VIEW_FAR_FROM_SIM_1M | 4,702 | 4,700 | suspected the same pre-snap glide artefact, not re-scored |
| FACE_BODY_70DEG_OFF_SIM_FACING_STANDING | 4,829 | 4,837 | same suspicion; at the snap 0 of 4,048 stance snapshots are more than 40 deg off |
| OVERLAP_BODIES_STANDING_OR_BLOCKING | 2,066 | 2,068 | same suspicion (first frames of a pre-snap, hips 0.00-0.01 m apart) |
| POP_HIPS_ROT_OVER_900DEGS | 2,041 | 2,147 | OL 504, DL 407, CB 294, WR 279, S 212, LB 188; max 9,498 deg/s |
| POP_HIPS_JUMP_OVER_10CM | 469 | 485 | |
| POP_CHEST_ROT_OVER_1400DEGS | 297 | 230 | |
| CAM_JUMP_OVER_6M_IN_ONE_FRAME | 398 | 398 | includes replay and show cuts; max 129.5 m |
| STANCE_LINEMAN_NO_HAND_ON_TURF | 323 | 323 | OL 312, TE 7, DL 4 |
| FEET_BELOW_TURF_7CM | 236 | 252 | OL 100, TE 31, RB 30, DL 25 |
| HAND_OR_HEAD_BELOW_TURF | 164 | 155 | |
| SUNK_HIPS_BELOW_0.35M | 174 | 174 | OL 61, DL 27, LB 23, WR 20 |
| FLOAT_RUN_AIRBORNE_OVER_0.23S | 50 | 51 | |
| FLOAT_GROUND_ACTION_STANDING_HEIGHT | 117 | 117 | downed players at standing hip height (S 68) |
| BALL_OFF_BOTH_HANDS_40CM_WHILE_HELD | 43 | 43 | 40 plays |
| CAM_BALL_OFFSCREEN_OVER_0.5S | 30 | 30 | every kick and some long passes |
| PERF_HITCH_OVER_120MS | 84 | 118 | wall time of an editor run with the new probe on, not a game number |
| VIS_VIEW_INACTIVE / VIS_TEAM_MISSING (body-frames) | 415,998 / 37,818 | 419,452 / 38,132 | automatic replay hides the live stage (known), not a body defect |
| Log | | 175 errors | `CinemachineBrain.ManualUpdate was called but CinemachineBrain is not in ManualUpdate mode`, from `MatchCameraRig.SetOverride` via `BroadcastCamera.End` |

Flow: PreSnap median 11.8 s (p90 14.1, max 26.4), Live median 4.2 s, PostPlay median 3.7 s, play finished to next play started median 15.6 s (p90 26.3). Plays: 142 scrimmage, 17 punts, 10 kickoffs, 8 field goals, 2 spikes.
Fixed seed: no run-to-run noise worth chasing below about 5 %, except POP_*, which varies with capture timing (2,041 / 2,147).

## 2. FLOAT_STANDING_BOTH_FEET_UP: verdict

**Metric artefact (classification), not a floating stance.** Not the toe bone, not a sole offset on the v5 prefabs, not a reference-height change.

How the number got to 30,918: the detector calls a body "standing" from the simulation (`SimAgent.Action` Stance / Idle / Block and `Speed` < 1.2 m/s). Since v0.11 SIM-FLOW the view walks to its place by itself: `PlayStage.Connect.cs` `GlideStep` moves the body at 0.55 to 5.2 m/s (huddle break 3.0 to 5.4 m/s) while the simulated agent already stands in formation at 0 m/s; `ConnectAgent` then gives only the view `Run` / `Idle`. The audit sees "Stance, 0 m/s, both toes up 10 cm" on a jogging body. In v0.10 the bodies were teleported, hence 140.

Evidence (`float_probe.csv`, 365,836 rows: per body bone heights next to the lowest vertex of the baked skinned mesh within 28 cm of each ankle, plus the view speed):
- **Where the hits are:** v0.11 rule 30,883 = 30,687 on bodies moving on the stage (view speed at least 0.35 m/s) + 196 on bodies standing still. New rule (needs view speed under 0.35 m/s): **190**, all in `Engaged` / `Block` frames of live plays (blocks hover: section 3, item 5).
- **Both soles more than 4 cm up, by view speed** (visible sole, not toe): under 0.1 m/s 0.4 %, 0.35 to 1 m/s 13 %, 1 to 2 m/s 21 %, 2 to 3.5 m/s 38 %, 3.5 to 4.5 m/s 51 %, over 4.5 m/s 66 % (more than 8 cm: 24 % and 35 %). That is stride flight of a jog at 4 to 5 m/s.
- **Bodies that stand still:** 117,955 rows with act `Stance`: both soles more than 4 cm up in 0.1 %, toe rule in 0.0 %; `Idle` 15,827 rows 0.1 % / 0.2 %. Lowest visible sole per stance kind: median -0.5 to -1.8 cm (about 1 cm of cleat in the turf), p95 -0.3 to +0.4 cm (ThreePoint, CornerOff, SafetyDeep, OlTwoPoint, ReceiverSlot, QbShotgun, KickoffReceive and the others, `summary.txt` `probe stand:` lines). At the snap, all 4,048 snapshots read lowest toe 0.00 to 0.03 m (`stance_snapshots.csv`).
- **The toe bone is a good proxy:** toe bone minus lowest sole median 0.019 to 0.030 m on all 10 body avatars (p5 0.017, p95 0.023 to 0.039); `FootPlant` rest toe is 0.024. A flat foot reads ankle 0.124 / toe 0.013 / sole -0.005 with the ankle-to-toe pitch at -37 deg. On still rows the toe rule and the sole rule agree on 246 rows, differ on 1,066 of 153,514.
- **Pictures with a ruler** (orthographic side view, the other 21 bodies clipped away, pole: green 0, yellow 5 cm, red 10 cm, white every 10 cm): `sidestance\p<N>_snap_{LT,LE,QB,WR}_*.png` feet on the green line at the snap; `p<N>_early_*.png` 3 s into the pre-snap, feet mid-stride 8 to 15 cm up, all moving; `p<N>_float_*.png` the shots of the remaining hits.

Fix in `MatchWatchAudit` (audit code only; `Checks.cs`, `.cs`, new `FloatProbe.cs`): FLOAT_STANDING now needs `VT.ViewSpeed` (root displacement, smoothed over about 4 frames) under 0.35 m/s. The old rule stays as `FLOAT_STANDING_V011_RULE_<group>_{STILL,MOVING}` for like-for-like counts. New recordings: `float_probe.csv` (tags `stand`, `glide`, `loco`, `dense`), `sidestance\`, summary lines `probe ...`.
Not re-scored, same sim-state classification, add the same guard first thing in v0.12: SYNC_VIEW_FAR_FROM_SIM_1M, FACE_BODY_70DEG_OFF_SIM_FACING_STANDING, OVERLAP_BODIES_STANDING_OR_BLOCKING.

What stays real: bodies jog to the line at up to 5.2 m/s with a 4 to 5 m/s stride (flight ratio, known), and feet hover in blocks (below).

## 3. Top 10 visible issues for the v0.12 tracks

| # | Issue | Evidence | Track |
|---|---|---|---|
| 1 | **OL "three-point" is a hands-on-thigh crouch.** 74 % of OL / ThreePoint snapshots (312 of 424, 78 plays) have the lowest hand at 0.66 m (hips 0.82, lean 59 deg). DL 1 %, centre 0 %, so the DL and snapper are right. | `anomaly\STANCE_LINEMAN_NO_HAND_ON_TURF_OL_1_p2.png`, `stance_snapshots.csv` | CRITICAL / stances |
| 2 | **Pre-snap is long and the walk-in is a jog from far away.** PreSnap median 11.8 s, p90 14.1, max 26.4; whistle to next play 15.6 s median; the view lags the sim up to 86.7 m (median 0.7 m, p95 15 m over all frames); glide speed 2.7 m/s median, 5.2 p95 (6-play probe run). | `summary.txt` phase durations, `view-vs-sim`, `probe glide` | SIM-FLOW |
| 3 | **Camera:** broadcast lens FOV 22 deg (p50), height 17 m (p50, p95 26): kickoff and wide shots show bodies of about 10 px; 398 one-frame jumps over 6 m (CoinToss 83 m, PlayCall 73 m, PreSnap 105 m, max 129.5 m); the ball leaves the top of the frame on 30 kicks / long passes (viewport y 0.97 to 1.02). | `strips\kickoff_setup_2_p20.png`, `hud\playcall_1_1920x1080.png`, `issues.txt` | WATCH+HUD |
| 4 | **Pops:** hips rotation over 900 deg/s 2,147 times (max 9,498, OL 504, DL 407), hips jump 485, chest 230; 272 clip switches under 0.13 s on DL. Clips: `OLine01_016__OLINE_MAN_3_stance_to_Block_Run_Foot_Work_Left_001` (C in PostPlay), `BGS_FIX_MCU_am_Ready_Idle_03_LookAround` (LT, PreSnap, 951 deg/s), `Armature|PIX_Huddle_Knees`. | `issues.txt`, `summary.txt` | MOTION / ANIMLIB |
| 5 | **Blocks hover and sink:** on bodies that stand still, both soles more than 4 cm up in 4.5 % of `Block` and 7.1 % of `Engaged` frames (14,842 / 4,890 rows); hips below 0.35 m on 174 frames (OL 61); feet below the turf 252 (OL up to -0.26 m, mostly `Block` in PostPlay with a ragdoll rig present; the one picture shows feet on the turf, so check the ankle-offset proxy before fixing); hands or head below the turf 155. | `FLOAT_STANDING_V011_RULE_*_STILL`, `anomaly\FEET_BELOW_TURF_7CM_OL_1_p4.png`, `SUNK_HIPS_BELOW_0.35M_OL_*` | MOTION / SIM-FLOW |
| 6 | **Post-play pop-up stack:** "FIRST DOWN / 1ST & 10", "TACKLED BY #41", the lower third "RICE RUNS FOR 11 YARDS", and the scorebug at once; post-play HUD covers a median 36 % of the centre area (p95 70.5 %); an element rect off screen 66 times (`Mask`, playcall: x 0.46..118.8, y 0.58..200). | `hud\post_FirstDown_1_1920x1080.png`, `hud\playcall_1_*.txt` | WATCH+HUD |
| 7 | **Referee and bodies in the shot:** the referee stands between the lens and the tackle in the post-play close-up; 62 tackle / pile overlaps (46 plays, 0.26 m apart at worst). | `hud\post_FirstDown_1_1920x1080.png`, `OVERLAP_BODIES_IN_TACKLE_OR_PILE` | WATCH+HUD / SIM-FLOW |
| 8 | **Ball off the hands while held:** 43 times in 40 plays, 0.5 to 1.64 m (RB `Carry` 0.59 m in play 5, P `Kick` 0.67 m in `Drop` mode). | `anomaly\BALL_OFF_BOTH_HANDS_40CM_WHILE_HELD_1_p5.png` | RB / ANIMLIB |
| 9 | **Downed players at standing height:** 117 frames in about 14 plays with act `Fallen` / `Tackled` and hips 0.9 to 1.17 m (S 68, DL 23, CB 15). | `FLOAT_GROUND_ACTION_STANDING_HEIGHT_*` | ANIMLIB / SIM-FLOW |
| 10 | **Running facing the wrong way:** 76 frames-runs where a runner's body is 55 deg or more off his travel (28 of them backwards, up to 179 deg: WR 32, TE 20, CB 8). | `FACE_RUNNING_*` in `issues.txt` | MOTION |

Smaller: the 175 Cinemachine `ManualUpdate` errors per game (`MatchCameraRig.SetOverride`); `BlockWin` 7 against `BlockLoss` 350 events (check what the two count); pre-snap `CenterSnap` and `OlTwoPoint` show hands at 0.15 and 0.91 m (the two-point OL stance is, by design, upright).
Not defects of the game (audit): VIS_VIEW_INACTIVE / VIS_TEAM_MISSING (replay), PERF_HITCH (editor), FLOAT_STANDING on moving bodies.

## Coordinator notes for v0.12 (2026-10-24)
- **The pre-snap walk-in moves bodies at 0.55–5.2 m/s through `GlideStep` (`PlayStage.Connect.cs`) while the sim still reports Stance at 0 m/s.** Treat this as a likely visible glide, which the owner explicitly hates. Check foot slide during the walk-in. Drive the walk-in through the motion matcher, or make the sim report a matching speed. Wave-1 track (a) or (b) owns it.
- **"74 % of OL ThreePoint snaps show hands at 0.66 m"** is mostly the owner's own v0.10 per-position spec, not a bug:
  - Tackles sit more upright with a hand on the thigh.
  - Guards sit lower with a hand on the thigh.
  - The centre is down over the ball.
  - A true 3-point is used on goal-line and short-yardage downs.
  - Fix the stance label/metric instead. Only touch the pose if the formation calls for a 3-point.
