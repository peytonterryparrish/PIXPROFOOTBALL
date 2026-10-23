# v0.10.0 MOTION track (`motion.md`)

Playtest notes 1 and 4 (locomotion part): "the running looks like galloping, the right foot, the ankle and the whole skeleton look wrong", "players gliding and floating on the field".
Job: replace the gait source, make locomotion grounded, square and slide-free, with the simulation deciding position and velocity and the view following.

## Summary (numbers first; real plays = 36 plays of `EngineAudit`, every running player every frame; "before" = the v0.9.0 gait player (`-pixLocoLegacy`) on the same code, "after" = motion matching)

| metric | target | before | after |
|---|---|---|---|
| Sprint flight fraction (both toes > 10 cm up), CB / LB / S, 6-7.5 m/s | <= 0.40 | 0.58-0.41 / 0.56 / 0.54-0.38 | **0.28-0.29 / 0.26 / 0.27-0.31** |
| Run flight, CB / LB / S, 4.5-6 m/s | <= 0.20 | 0.44 / 0.43 / 0.42 | **0.12 / 0.11 / 0.12** |
| Run flight, 6-7.5 m/s | <= 0.20 | 0.51 / 0.53 / 0.53 | 0.23 / 0.20 / 0.20 (just over) |
| Jog flight, 3-4.5 m/s | <= 0.20 | 0.16 / 0.14 / 0.18 | 0.07 / 0.08 / 0.07 |
| Mean lowest toe above the turf, CB run / sprint (cm) | - | 6.5 / 11.4 (median 5.6 / 11.6) | **4.4 / 5.9 (median 2.5 / 2.6)** |
| Planted-foot slide, contact-minimum per stance (the v0.9.0 audit metric), CB run 4.5 / 6.0, sprint 6.0 / 7.5 (m/s) | <= 0.10 | 0.30 / 0.27, 0.24 / 0.28 | **0.10 / 0.09, 0.07 / 0.07** (LB sprint 0.20 -> 0.09, S 0.20 -> 0.08) |
| same, jog 3.0 (CB / LB / S) | <= 0.10 | 0.62 / 0.23 / 0.28 | 0.25 / 0.26 / 0.23 (median 0.10) - **not met** |
| same, back-pedal 3.0 (CB / LB / S) | <= 0.10 | 3.7 / 2.3 / 3.3 | 1.1 / 1.0 / 1.7 (median 0.23 / 0.10 / 0.10) - **not met, tail** |
| same, shuffle 3.0 (CB / LB / S) | <= 0.10 | 1.1 / 1.6 / 1.7 | 0.36 / 0.38 / 0.39 (median 0.09-0.13) - **not met** |
| Treadmill, planted-point speed mean, 3-8.5 m/s (m/s; inner stance 0.2-0.5) | <= 0.10 | 0.8-3.0 | 0.2-0.5 - **not met, 2-10x better** |
| Treadmill contact height (sole above turf) | 0-3 cm | -2.4..+2.1 cm (hover and sink) | -0.3..+0.1 cm everywhere |
| Steps per second, CB sprint / run 4.5 / back-pedal 3.0 / shuffle 3.0 | 4.3 / 3.3 / 3.0 / - | 4.6 / 3.1 / 0.8 / 1.8 | 4.1 / 3.4 / 3.0 / 2.7 |
| Foot toe-out at contact against the line, sprint / run / route / carry >= 5.5 m/s (deg mean) | <= 10 | 14.4 / 14.6 / 8.5 / 8.7 | **3.2 / 3.7 / 3.5 / 5.0** |
| Knee off the hip-ankle plane in stance, sprint / run >= 5.5 m/s (cm) | <= 3 | 1.6 / 2.1 | **1.2 / 1.2** |
| Left / right toe-out difference (deg), sprint / run / route / carry >= 5.5 | <= 3 | 0.8 / 0.1 / 0.7 / 1.2 | **0.5 / 0.2 / 0.1 / 0.6** (back-pedal 3.8, shuffle 1.6) |
| Matcher CPU, search per player (editor Mono, 608 rows x 21 dims) | <= 0.03 ms / frame | - | **0.057 ms per query every 0.1 s = 0.0095 ms per 60 Hz frame** (0.022 before the row cache) |

Follow-up round (muscles, walk, slide, QB scramble, chest pops): section 9, which supersedes the slide, walk and pop rows above.

In one sentence: the gait source is clean, both feet reach the turf and stay on it, sprint and run no longer gallop (flight 0.12-0.30 instead of 0.4-0.6), the knees and feet run square (toe-out 3-5 degrees, knee 1 cm), left and right match within 1 degree at speed, and the planted foot is nearly still at run and sprint speed. Not met: slide at jog, back-pedal and shuffle speeds, and the run flight above 6 m/s is 0.20-0.23.

Strips (12 frames, 0.1 s apart; looked at once, see section 5 for what still looks wrong): before `D:\PIXFootball\anim_loco\v0100\legacy\`, after `D:\PIXFootball\anim_loco\v0100\mm_final\` (`strip_legs_{jog,run,sprint,backpedal,shuffle,carry_carry}_{side,front,behind}.png`, `strip_maneuver_{stop_from_6_ms,cut_90_deg_at_6_ms}.png`), metrics `metrics_treadmill_legs_maneuvers.md` in both, source scan `mixamo_scan.md / .csv`, calibration `mm_calibration.csv`. Audit outputs: `D:\PIXFootball\Renders\v0100_motion\{legacy,mm_final}\` (`motion_summary.txt`, `leg_quality.md`, `motion_frames.csv` with the matcher's layer / take / row per frame).

## 1. What was wrong (root causes, measured)

| # | finding | how it was found | fix |
|---|---|---|---|
| M-001 | The v0.9.0 sprint / run takes are bounding (galloping) mocap: both feet in the air 45-60 percent of the cycle, one foot never reaches the turf, root motion speed overstated 2-3x. Round 3 only polished knees and feet in code. | v0.9.0 audit numbers (CB sprint flight 0.57, run 0.45) and `locomotion.md` LOCO-009 | new clean source (Mixamo), per-take selection by measured flight fraction |
| M-002 | **The skeleton-only Mixamo exports carry the FIRST FRAME of the take as the rest pose of their avatar, not a T-pose.** Imported as delivered, every file is retargeted from a different and wrong zero pose: in every run one foot (the left) stayed 5-9 cm above the other and never reached the turf (contact fraction left 0.00, right 0.25), and toe-out / ankle roll differ left against right by 10-40 degrees. This is the mechanism behind "the right foot / ankle / skeleton is wrong" for any source whose avatar is built from a first-frame pose; it was measured on the new source before it could reach the game. | `MixamoScan` on the 69 clips: contact of the left foot 0.00 in almost all runs; `MixamoAvatarDump`: the rest-pose bone rotations differ in every file (LeftUpLeg 351/4/174 against 330/354/172 ...) | `MixamoTPose`: one T-pose for the X Bot skeleton (every limb aimed along the world axes by the shortest rotation, twist kept), written into one reference avatar, the other 68 files copy that avatar. After it left / right contact fractions are equal (0.26 / 0.26 in `Run_Standard_Run`) |
| M-003 | The older takes (DraftFever / BGS) are authored 90 degrees off the avatar's own forward; the whole codebase compensates (`PlayerAvatar.ModelYaw = -90`, `ClipFacingTable`). A properly authored take plays facing sideways in the match. | strips of the first matcher run: runner facing the side camera, pelvis 90-110 degrees off the line of travel | the matcher turns the pose back by `-ModelYaw` about the avatar root before anything else touches it (`MotionMatcher.Late`), scaled by the share of the pose that comes from the matcher; inertialization then sees both conventions as one |
| M-004 | The retargeted Mixamo takes stand 5-12 cm too high (lowest sole 0.02-0.17 m above the turf at contact). The planted-foot solver can lower the pelvis by 7 cm at most. | `floor` column of `Logs/motion/db_report.txt`; treadmill contact height +0.04-0.07 m | each take stores its floor; the pose is lowered by it (`MotionDb.Clip.floorY`) |
| M-005 | The Mixamo root motion overstates the ground speed of the feet by 20-50 percent (sprint: root 7.6 m/s, planted feet 5.1 m/s). | planted-foot velocity of every take, sampled at 120 Hz over the middle of each contact (`MotionDbBuilder.NativeVelocity`) | every take is stored with the speed its feet say; closed loop on the planted foot (`MotionMatcher.Learn`) and an offline learn pass on the treadmill (`LocoShots mmlearn`) |
| M-006 | Contact tags from foot height alone count a foot that skims the turf while it swings forward as planted (positive slip in every stance). | slip of the learn pass saturated at +40 percent for most takes | contact needs the foot to move BACKWARD relative to the body (28 percent of the ground speed), middle of the stance only |
| M-007 | A planted foot let go the moment the animated foot rose 3.5 cm or ran 5 cm ahead of its anchor (planted for 1-2 frames out of 6). | per-frame trace of the treadmill at 7 m/s (`trace_7.0.csv`) | `FootPlant.HintAuthority`: with frame-accurate hints a planted point holds until its hint ends (only the heel still lets go when it lifts) |

## 2. The new source: 68 Mixamo clips, measured

Imported git-ignored under `Assets/_Licensed/Mixamo/` (Humanoid, one shared avatar after M-002, root motion kept, clip named after the file). Every clip measured on the match body (`MixamoScan`, 30 fps, root motion applied, `Logs/motion/mixamo_scan.csv`; the table is copied to `D:\PIXFootball\anim_loco\v0100\mixamo_scan.md`).
Columns: root speed (m/s, path length), cadence (steps/s), flight (both toe bones more than 10 cm above the clip's floor, the audit's definition), contact fraction left / right, toe-out left / right (degrees, + = out), ankle roll left / right, asymmetry (largest left-right difference of toe-out or roll).

| take | root m/s | flight | contact L / R | toe-out L / R | ankle roll L / R | asym |
|---|---|---|---|---|---|---|
| **Run_Fast_Run** (kept: sprint) | 6.97 | 0.18 | 0.35 / 0.29 | - / -8.6 | - / -0.2 | - |
| **Run_Medium_Run** (kept: run) | 5.76 | 0.22 | 0.22 / 0.44 | - / 6.2 | - / 1.7 | - |
| **Jog_Running** (kept: run / jog) | 4.64 | 0.00 | 0.36 / 0.55 | 1.8 / -6.1 | -7.4 / -0.8 | 7.9 |
| **Run_Jog_Forward** (kept: jog) | 3.19 | 0.15 | 0.46 / 0.27 | 17.8 / -12.8 | -19.5 / 0.4 | 30.6 |
| **Jog_Runningk** (kept: slow jog) | 3.17 | 0.27 | 0.23 / 0.19 | 1.5 / - | -11.3 / - | - |
| Run_Two_Cycle_Sprint (out: 0.44 in play) | 7.83 | 0.39 | 0.24 / 0.27 | 25.4 / 9.5 | -3.6 / -0.7 | 15.9 |
| Sprint_Standard (out) | 7.60 | 0.59 | 0.18 / 0.18 | 18.9 / -4.2 | -3.9 / 5.5 | 23.0 |
| Run_Standard_Run (out) | 5.44 | 0.30 | 0.26 / 0.26 | 11.9 / -5.0 | -9.9 / 7.6 | 17.5 |
| Run_Slow_Run (out) | 3.86 | 0.57 | 0.22 / 0.22 | 9.1 / - | -16.2 / - | - |
| Jog_Runningg (out) | 5.39 | 0.55 | 0.18 / 0.27 | 1.1 / -5.2 | -5.7 / 5.9 | 11.6 |
| Jog_Runningf (out) | 5.85 | 0.41 | 0.36 / 0.14 | 2.7 / - | -6.2 / - | - |
| Jog_Jogging (out) | 2.44 | 0.37 | 0.31 / 0.28 | -2.4 / 1.0 | -7.8 / -4.1 | 3.7 |
| **Run_Running_Backward** (kept) | 3.91 | 0.00 | 0.55 / 0.50 | 5.3 / -16.5 | -5.1 / 1.4 | 21.9 |
| **Run_Jog_Backward** (kept) | 2.93 | 0.32 | 0.36 / 0.24 | 10.7 / 3.9 | -18.3 / -10.9 | 7.3 |
| **Run_Jog_Backward_Diagonal** (kept) | 3.15 | 0.19 | 0.19 / 0.54 | -9.1 / -9.6 | -12.5 / 2.9 | 15.4 |
| **Jog_Jog_Strafe_Left, Jog_Jog_Strafe_Right** (kept) | 3.71, 2.96 | 0.05, 0.00 | 0.23 / 0.36, 0.48 / 0.65 | 25.2 / -, 72.8 / -25.4 | -11.9 / -, -3.5 / 6.9 | -, 98 |
| **Jog_Left_Strafe_2, Jog_Right_Strafe** (kept) | 4.46, 4.46 | 0.00, 0.00 | 0.43 / 0.38, 0.43 / 0.48 | -2.3 / -, 10.2 / -13.4 | -7.5 / -, -4.1 / 3.7 | -, 23.6 |
| **Run_Jog_Forward_Diagonal, Jog_Jog_Forward_Diagonal_2** (kept) | 3.56, 3.36 | 0.20, 0.23 | 0.28 / 0.44, 0.38 / 0.27 | -5.2 / -38.1, 7.0 / -19.6 | -15.2 / 5.9, -14.0 / -14.8 | 33, 27 |

Cadence is not in the table: the takes are one cycle of 16-25 frames with at most two contact edges per foot, so the scan's step count is noise; the cadence of each take in play is in the audit. "Root m/s" is the path length of the root motion; the speed the planted feet say is 20-30 percent lower (M-005). The other 47 files (goalkeeper, soccer, football action takes, standing turns, starts, stops, turns) are in `mixamo_scan.md`.

Choice (by band, objective): forward gaits with flight <= 0.20 (jog / run) or <= 0.40 (sprint) only: `Jog_Running` 0.00, `Run_Jog_Forward` 0.15, `Jog_Runningk` 0.27 (jog), `Run_Medium_Run` 0.22, `Run_Fast_Run` 0.18 (sprint). Rejected as bounding: `Run_Two_Cycle_Sprint` 0.39 in the scan and 0.44 in play, `Sprint_Standard` 0.59, `Run_Slow_Run` 0.57, `Jog_Runningg` 0.55, `Jog_Runningf` 0.41, `Jog_Jogging` 0.37, `Run_Standard_Run` 0.30. Back-pedal: `Run_Running_Backward`, `Run_Jog_Backward`, `Run_Jog_Backward_Diagonal`. Strafes: `Jog_Jog_Strafe_Left/Right`, `Jog_Left_Strafe_2`, `Jog_Right_Strafe`, `Run_Jog_Forward_Diagonal`, `Jog_Jog_Forward_Diagonal_2`. One-shot starts, stops and turns were tried and dropped (their root turning fights the simulated yaw: planted slide 2-3.4 m/s in play against 0.5-1.3 for the loops; the first database had 21 takes, the final one 14 loops x 2 with their mirrors). Everything else of the 68 (goalkeeper, soccer, football actions: hike, stance, catch, pass, defender, tackle ...) belongs to the action systems and is not in the locomotion database; standing turns and the idle takes are not used (the animator tree owns standing).
Existing library takes were NOT mixed in: the library has no take that measures better (its best, `BGS Run08/Run07`, fly 0.41-0.44), and the one take worth having below a jog (`PIX_Walk_Cycle`) is authored with a 90 degree facing offset of its own; below 1.7 m/s the animator tree still walks (see "what still looks wrong").

## 3. The matcher (what was built)

Files: `Gameplay/Player/Motion/MotionDb.cs`, `MotionMatcher.cs`, `Inertializer.cs`; view side `SimAgentView.Loco.cs`; sim look-ahead `Sim/PlaySimulation.MotionPlan.cs`; stage hook `Match/PlayStage.Motion.cs`; editor `MixamoImport.cs`, `MixamoScan.cs`, `MotionDbBuilder.cs`, `MotionBench.cs`, `LocoShots.cs` (new modes).

* **Database** (`PixMotionDb.asset`, `Assets/_Licensed/Resources`, git-ignored): every 30 fps source frame of the selected takes is one row of 21 normalised, weighted features in the root frame of that frame: left / right ankle x, z and height above the take's floor; ankle velocity relative to the root; root velocity; root position at +0.33 / +0.66 / +1.0 s; facing change at the same times. Per row: ground velocity, planted-foot tag (left / right), take index. 14 takes and their 14 left-right mirrors = 28 clips, 608 rows.
* **Sim authoritative**: the sim hands in its velocity and its PLANNED path: route stems and breaks for receivers (the same speed schedule `RunRoute` uses), the desired velocity through the body's response (time constant, acceleration cap) for everybody else (`PlaySimulation.PlanPoints`, `PlayStage.MotionPlanHook`). The view only follows; position and yaw are never touched by the matcher.
* **Search** every 0.10 s (and when a one-shot take ends): squared distance over the 21 features with early-out, +0.78 stay factor for the continuation of the running take, a minimum dwell of 0.16 s, an extra cost for rows whose ground speed is further than +-15 percent from the wanted speed. The pose part of the query is the running frame's own features (continuity); after an action it is the live pose (`LiveFeet`), and without any pose a masked search on the trajectory alone.
* **Stride**: playback rate = sqrt(speed ratio) in [0.9, 1.22] (cadence and stride share the difference), the rest is the stride warp of `FootPlant` (+-15 percent by construction for the matched take; the top sprint needs up to +25 percent, see "what still looks wrong"). Closed loop: the slide of the planted foot over the middle of its stance trims the warp of that take (`MotionMatcher.Learn`, bounded 0.7-1.4, shared per body size).
* **Orientation warp**: the legs turn (pelvis yaw, counter-turned spine) toward the simulated travel direction, up to 35 degrees, so a strafe or back-pedal take serves the angles between the takes.
* **Foot locking**: the take's contact tags (one frame ahead) are the hints of `FootPlant`; with `HintAuthority` a planted point holds until its hint ends; stride warp, plant, pelvis compensation, knee / foot polish are the v0.9.0 solver.
* **Inertialization** (`Inertializer`): a jump to another take / frame never cross-fades two gaits; the difference between the pose shown last frame and the new pose becomes a per-bone rotation-vector offset (hips: position too) that dies out as a critically damped spring (half-life 0.09 s).
* **Ball carriers**: the matcher runs under a lower-body mask (root, body, legs): the arms are the animator tree's ball-carrier arms.
* **Legacy**: `-pixLocoLegacy` plays the v0.9.0 `GaitPlayer`; `-pixLocoOff` the v0.8 tree. QA switches: `-pixMmNoInertia`, `-pixMmNoOrientWarp`, `-pixMmNoPlan`, `-pixMmLog`.

### API for the CHOREO track (hand control to an action and take it back)

```
SimAgentView.LocoRelease()  -> MotionMatcher.FootPhase   // an action takes the legs; idempotent
SimAgentView.LocoTakeBack()                              // the action is over: the matcher searches from the live pose and the planned path
FootPhase { Valid, LeftPlanted, RightPlanted, Phase (0..1 through the stride, left plant = 0; -1 = not in a cycle), Speed, Angle, Clip, ClipTime }
```
`LocoRelease` parks the matcher at once (inertialization hides the change of pose source) and returns the tagged foot phase the locomotion left: which foot is planted, where in the stride, how fast. An action that wants a matching entry (a throw on the run, a plant-and-throw, a tackle approach) picks its start frame from it. `LocoTakeBack` clears the hold; the first search after it uses `LiveFeet` (ankle positions, heights, velocities of the real pose), so the matcher resumes from wherever the action left the feet. While held the view also treats the player as not owned by the locomotion (`GaitAction` stays the gate otherwise: `Rig.Weight >= 0.9` or a non-locomotion action suspends it by itself).

## 4. Left against right ("the right foot, the ankle, the skeleton")

What was measured, in this order:

1. **Source takes.** v0.9.0 library takes: shin / foot twist at the muscle limits, feet 60-120 degrees off at p95, soles tilted 10-20 degrees (round 3). New source as delivered: the left foot never within 4 cm of the floor in any run take, 5-9 cm higher than the right (M-002, a pose-reference problem of the skeleton-only export, not of the mocap).
2. **Real plays, per side** (`leg_quality.md`, columns "L / R toe-out signed", "L / R roll", "L / R knee inward"). v0.9.0 at speed: toe-out left / right -1.7 / -1.6 (run), -1.8 / -1.1 (sprint), -1.7 / -0.9 (route). The signed left-right difference of the plays is small; the foot is bad on both sides (14 degrees of toe-out against the line). What the owner sees as one-sided is a stance foot that hovers while the pelvis does not drop on that side, a property of the takes. After: left-right difference 0.2 / 0.5 / 0.1 degrees (run / sprint / route), knee 0.1-0.6 cm; back-pedal 3.8, shuffle 1.6.
3. **Per take.** Every Mixamo take has its own side bias (toe-out L / R: `Run_Standard_Run` +11.9 / -5.0, `Run_Two_Cycle_Sprint` +25 / +9.5; contact fraction `Jog_Running` 0.36 / 0.55). Fix: every loop is in the database twice, as the take and as its left-right mirror (the humanoid mirror flag of a twin clip, `Assets/_Licensed/Mixamo/Mirrored/*_M.anim`), so the matcher has both feet of every take; the contact tags use the floor of each foot separately; the knee / foot polish (knee along the line, foot within 10 degrees, roll <= 8 degrees) is symmetric by construction.
4. **Retarget.** The T-pose (M-002) and the facing convention (M-003) are the two retarget causes found; both are fixed at the source, not at run time. The DraftFever FBXs are Generic rigs (the humanoid avatar dump of 60 files returned no skeleton), so the first-frame-pose check cannot be done on them here; if the older takes were retargeted from a first-frame pose the same way, that would explain one-sided feet of the BGS takes (open item for ANIMLIB).

## 5. What still looks wrong (strips looked at once; the walk, jog / shuffle slide and chest-pop items below are superseded by section 9)

* **Run / sprint / jog side views**: the gallop is gone, the stride is long, knee lift and arm drive follow the speed, the foot lands under the body. At 7.5 m/s two airborne frames per cycle remain (flight 0.28-0.31, a real sprinter's range). The trunk leans further forward than the old takes (Mixamo posture).
* **Top speed**: the Mixamo takes top out at 5.7 m/s of planted-foot speed (`Run_Fast_Run`); the sim sprints 7-9 m/s, so the fastest runners are played at +20-25 percent rate and stride (cadence 4.1-4.7 steps/s, stride 1.6-1.9 m). It reads as a long fast stride; a true sprint capture (8-9 m/s, flight <= 0.35, both feet contacting) would be better.
* **Back-pedal, shuffle**: straight back and straight sideways are clean (toe-out 1-8 degrees on the treadmill). Oblique travel (60 / 125 degrees from the facing) is served by the nearest take plus the orientation warp (limit 35 degrees), and Mixamo has no true DB back-pedal or lateral shuffle. In plays the back-pedal and shuffle bins show toe-out 27-34 degrees against the facing and slide tails (mean 1.0-1.7 m/s at 3 m/s, median 0.1-0.23): hip flips and the turn-and-run take (CHOREO) are in the same bin. Shuffle at 60 degrees: feet 39 degrees off the facing.
* **Cuts and stops**: no start / stop / turn takes are used (their root turning fought the simulated yaw: slide 2-3.4 m/s against 0.5-1.3 for the loops); a cut is a loop plus the planned path, the orientation warp and the foot lock (treadmill cut 90 degrees at 6 / 8 m/s: planted 0.42 / 0.39 against 1.15 / 1.64 before; in the strip the body turns early, the foot plants, the cut is a little soft). Stops from 4-8 m/s: planted 0.28-0.34 (before 0.25-0.56); the strip shows a natural lean back into the tree's stand.
* **Below 1.7 m/s the animator tree still walks** (pre-snap shifts, walking after the whistle): treadmill planted 0.09-0.22 at 1.0-1.5 m/s, but that is the v0.8 tree, not the matcher.
* **Chest / head rotation pops**: 103 of 75,774 matcher-owned frames (0.14 percent) have a chest rotation over 1400 deg/s, mostly at the back-pedal entry; the audit total went from 306 to 419 (CB 23 -> 53, LB 20 -> 68, S 11 -> 32) because the matcher plays on more bodies; hips pops are equal (711).
* **Ball carriers** run on the matcher's legs under a lower-body mask with the arms of the animator tree's carry layer; that arm swing does not follow the matched stride.
* The matched pose is the Mixamo facing convention turned back by `-ModelYaw` at the hips; masked upper-body actions on the run (a throw while running) blend hips from two conventions at partial weight (`1 - Rig.Weight`): approximate, CHOREO should look at it when it wires the hand-off.

## 6. Cost, tests, switches

* CPU (editor, Mono, no Burst; `MotionBench`): search 0.130 ms per query over 608 rows x 21 dims, one query per 0.1 s = 0.022 ms per player per 60 Hz frame, worst case (no early out) 0.23 ms; inertializer 2.3 us per frame. The per-row speed and take-kind lookups were then cached (`MotionDb.Gv / Kinds`, no logarithm inside the +-15 percent band): search 0.057 ms per query = 0.0095 ms per player per 60 Hz frame, worst case 0.10 ms, inertializer 1.2 us. One playable per used take per player (lazy, zero weight when not playing).
* Tests: `MotionMatchingTests` (database search, masked search, speed band, loop wrap, normalisation). Full EditMode run `-testCategory "!Slow"`: 1169 passed, 0 failed (23 min; 5 of them are `MotionMatchingTests`, re-run alone after the cache change: 5 / 5).
* Switches: `-pixLocoLegacy` (v0.9.0 gait player), `-pixLocoOff` (v0.8 tree), `-pixMmNoInertia`, `-pixMmNoOrientWarp`, `-pixMmNoPlan`, `-pixMmLog`, `-pixMmNoMirror` (builder).

## 7. Files touched

New: `Gameplay/Player/Motion/{MotionDb,MotionMatcher,Inertializer}.cs`, `Sim/PlaySimulation.MotionPlan.cs`, `Gameplay/Match/PlayStage.Motion.cs`, `Editor/Tools/{MixamoImport,MixamoScan,MotionDbBuilder,MotionBench}.cs`, `Tests/EditMode/Gameplay/MotionMatchingTests.cs`; assets (git-ignored): `Assets/_Licensed/Mixamo/*.fbx` (+ `Mirrored/*_M.anim`), `Assets/_Licensed/Resources/PixMotionDb.asset`.
Owned and changed: `Gameplay/Player/SimAgentView.Loco.cs`, `Gameplay/Player/Loco/FootPlant.cs` (`HintAuthority`, `BodyLine`), `Editor/Tools/{LegProbe,LocoShots}.cs` (per-side metrics, `mmlearn`, planned path in the manoeuvres, angle cases, oblique travel measured against the facing).
One-line hooks in other tracks' files: `ActionAnimator.cs` (creates / attaches / detaches the matcher, `Mm`), `SimAgentView.cs` (`MotionLate` first in `LateUpdate`; the facing loop stands down while the matcher owns the pose), `SimAgentView.ActionGround.cs` (`LocoOwns`), `Match/PlayStage.cs` (`MotionPlanHook`), `Editor/Tools/EngineAudit.cs` (matcher columns in `motion_frames.csv`). The new files have no `.meta` yet (the first editor open creates them). The importer settings are written by an editor method and NOT by an `AssetPostprocessor`: a postprocessor with `OnPreprocessModel` re-imports every model of the project.

## 8. How to repeat

Private copy `D:\PIXFootball\Work\Motion\PIX` (`sync2.sh` = git HEAD scripts + this track's files + `patch.sh` hooks; `run.ps1 -Method ... -Log ... [-NoQuit]`). After a clip change: `MixamoImport.Run`, `MixamoTPose.Run`, `MixamoImport.RunKeepAvatar`, `MotionDbBuilder.Build`, `LocoShots.Run -pixLoco -pixLocoModes mmlearn` (writes `D:\PIXFootball\anim_loco\mm_calibration.csv`), `MotionDbBuilder.Build -pixMmCalib 1`. Measure: `MixamoScan.Run`, `LocoShots.Run -pixLoco -pixLocoTag v0100/x -pixLocoModes treadmill,legs,maneuvers [-pixLocoLegacy]`, `EngineAudit.Run -pixEngineAudit [-pixLocoLegacy] -pixEngineAuditPhase metrics -pixEngineAuditPlays 36`.

## 9. Follow-up round (owner: every note fully fixed before the build)

Brief: BODIES measured forearm twist beyond +-90 degrees and about ten humanoid muscles over their limits on the matcher; close the walk below 1.7 m/s, the jog and shuffle slide and the chest-pop increase; added later by the coordinator: QB scramble running slide 1.1 m/s (starts and cuts). Plays below = `EngineAudit`, 36 plays, same code base as section 2 (before this round = `mm_final3`, now = `mm_final6` in `D:\PIXFootball\Renders\v0100_motion\`); treadmill = `D:\PIXFootball\anim_loco\v0100\mm_final5`.

### 9.1 Forearm twist and muscle limits (fixed at the source)

* **Cause.** The skeleton-only Mixamo exports carry the first frame as rest pose (M-002). Re-posing them onto the T-pose avatar fixed the skeleton but kept the twist of the file, so every muscle curve has an offset: left shoulder Front-Back -1.86, left forearm twist -0.79 (beyond +-90 degrees in 17 percent of the samples), both shoulders Down-Up +0.51 / +0.53, and every finger curled past its limit in every frame (the relaxed hand of the export). 527 take samples beyond +-1.
* **Fix.** `Editor/Tools/MotionClean.cs` (new): each muscle curve is re-zeroed by the offset of the rest pose, the fingers get a canonical relaxed hand, and whatever still lies beyond 0.9 of the limit is soft-limited (tanh) so no muscle reaches +-1. The database is built from the cleaned copies (`Mixamo/Clean/*_C.anim`, originals untouched, `-pixMmNoClean` for A/B). `Editor/Tools/MuscleReport.cs` (new) writes the take-level table (`Logs/motion/muscles.txt`) and the default limits (`muscle_limits.txt`). Take level after cleaning: 0 samples beyond +-1.
* **Final pose in the match view** (`LocoShots` legs mode, 330 frames per case, 14 cases, `mm_final5/metrics_*.md`): forearm twist, hand and finger muscles stay within 1.04 in every case. The old gait player had left shoulder Front-Back over its limit in 176 frames, right shoulder 145, chest twist 118 frames (jog, run 5.0, back-pedal). What remains of the shoulders is 1.01-1.04 for 17-88 frames (hairline).
* **Still over, and not from the takes:** at run and sprint the feet: Foot Twist In-Out max 5.0-5.4, Foot Up-Down max 2.3-3.1, thigh Front-Back max 1.5 (hip flexion at the sprint knee drive). With the foot polish of `FootPlant` off (`-pixLocoNoPolish`) the foot muscles vanish (thigh 1.45-1.5 and lower leg 1.00 remain), so they come from the v0.9.0 foot yaw / roll polish (a yaw about the world vertical on a toe-down foot) and are the same with the old gait player (legacy sprint: foot twist max 4.5-4.9, foot up-down 2.3). Not changed: the polish is what holds the toe-out at 3-5 degrees. Open: BODIES widens the foot limits of the player avatar, or the polish works about the shin axis.

### 9.2 Walking below 1.7 m/s is in the matcher

The library walk (`PIX_Walk_Cycle`, legacy convention, 1.7 m/s) is a database take; the matcher engages from 0.85 m/s (0.6 m/s with a fast planned path) and hands back to the tree at standstill. Treadmill 1.0 / 1.5 / 2.0 / 2.5 m/s: planted speed 0.07 / 0.06 / 0.06 / 0.06 (tree: 0.09-0.22), toe slide (QB metric) 0.01-0.02 m/s. No extra clip is required. If the owner wants Mixamo walks, the takes are: `Walking`, `Walking Backwards`, `Left Strafe Walking`, `Right Strafe Walking`, `Start Walking`.

### 9.3 Planted-foot slide: four causes found and fixed

1. **The lock never reached full weight.** `Mathf.SmoothStep(0.03, 0.075, h)` in `FootPlant` is an interpolation between 0.03 and 0.075, not a smoothstep: the lock weight was at most 0.97 on the turf and 0.92 above it, so 3 percent of a foot sweeping at 13 m/s (0.4 m/s) leaked through in every stance. With frame-accurate hints the weight is now 1 while the point is within 3 cm of the height it touched down at (`Ramp01`); the old gait player keeps its curve.
2. **The polish moved the ball of the foot.** Yaw, roll and twist limits turn the foot about the ankle, the toe lies 17 cm from it: the planted ball slid 0.2-0.3 m/s with the lock at full strength. `KeepToe`: after the polish the ankle follows by the displacement of the ball (weighted by the ball's share of the lock); `YawHold`: a planted foot keeps the world heading it touched down with. Toe slide in stance (the QB audit's metric: toe < 3 cm, ankle < 14.5 cm, 6+ frames, core frames), treadmill: 5-8.5 m/s 0.17-0.27 -> **0.02-0.06**, 3-4.5 m/s 0.09-0.13 -> 0.01-0.04, back-pedal 0.05-0.10 -> 0.00, shuffle 0.16 -> 0.00-0.06. (One 3.0 m/s jog stance reads 0.44: a one-frame 3.6 cm step at the loop seam of `Run_Jog_Forward_M`.)
3. **Calibration noise.** The treadmill calibration of the stride (`mmlearn`) is now the average of three runs (`-pixMmCalib 1`), residuals 0.99-1.01 (back-pedal takes 0.82-0.92 of nominal).
4. **Hand-back from a clip.** While a clip fades out (0.3 s) the legs were a blend of the clip and the matcher, which drags the feet. `ActionAnimator.LegsToLoco()` gives the legs of an outgoing clip to the matcher at once (the inertializer takes the jump), `BodyMotion.ForceLoco` keeps the foot planter running through the fade, and a swing foot that the blended pelvis would sink into the turf is lifted clear (hinted points only). Switches: `-pixFpNoFadeRel`, `-pixFpNoPivot`, `-pixFpNoYawHold`, `-pixMmNoLegsBack`, `-pixFpRise` (pelvis rise: tried, worse, off).

Treadmill, planted-point speed (heel or ball, the lower; m/s), before this round -> now: sprint / run 5-8.5 m/s 0.40-0.47 -> **0.27-0.30**; back-pedal 1.5-4.5 m/s 0.20-0.28 -> **0.06-0.16**; 1-2.5 m/s 0.07-0.10 -> 0.06-0.07; 3 / 3.5 m/s 0.30 / 0.35 -> 0.10 / 0.17. Not met: 4.0 / 4.5 m/s (0.52 / 0.57; the take chosen there lands the left foot twice per cycle, the toe slide is 0.03-0.04, the heel rolls) and shuffle 3.0 (0.37, toe 0.06).
Manoeuvres, planted / toe slide: stop from 4 / 6 / 8 m/s 0.40 / 0.33 / 0.27 (before 0.40 / 0.37 / 0.37) with 0.03-0.05; cut 90 degrees at 6 / 8 m/s 0.42 / 0.35 (before 0.56 / 0.46) with 0.11 / 0.05. The v0.9.0 gait player: stops 0.18-0.68, cuts 1.97 / 1.79.

Real plays, contact-minimum slide per stance (m/s, mean; median in brackets), CB / LB / S:

| bin | v0.9.0 gait player | first matcher | now | target |
|---|---|---|---|---|
| sprint 6.0 | 0.24 / 0.20 / 0.20 | 0.095 / 0.075 / 0.091 | **0.033 / 0.024 / 0.029** (0.02) | <= 0.10: met |
| run 6.0 | 0.27 / 0.28 / 0.23 | 0.098 / 0.087 / 0.091 | **0.036 / 0.019 / 0.021** | met |
| run 4.5 | 0.30 / 0.29 / 0.22 | 0.161 / 0.121 / 0.145 | **0.073 / 0.061 / 0.106** (0.013-0.017) | met (S 0.106) |
| jog 3.0 | 0.62 / 0.23 / 0.28 | 0.377 / 0.227 / 0.333 | 0.403 / 0.301 / 0.205 (0.093 / 0.026 / 0.028) | median met, **mean not met** |
| shuffle 3.0 | 1.09 / 1.63 / 1.70 | 0.377 / 0.331 / 0.336 | 0.361 / 0.327 / 0.374 (0.154 / 0.159 / 0.170) | **not met** |
| back-pedal 3.0 | 3.7 / 2.3 / 3.3 | 1.06 / 1.01 / 1.88 | group mean 0.99 / 1.04 / 1.61 (median 0.07 / 0.02 / 0.05); matcher-owned stances only: 0.116 | matcher met; see below |

The back-pedal group mean is 30 stances of the action-layer clip `BGS_CB_IdleToSprint_BackRight` (and `_M`; 650 frames, slide 4.6 m/s, tree-owned): not the matcher; for CHOREO / ANIMLIB (a foot lock for that start clip, or the matcher under it).
Why jog and shuffle are not at 0.10 in plays: the median is (jog 0.03-0.09), the mean is a tail of about a quarter of the stances (minimum above 0.3 m/s), spread over the jog takes at the speed changes of the plays (starts, stops, the walk-to-jog hand-off); for the shuffle the strafe takes: the toe is pinned (0.06 on the treadmill) but the heel rolls. Next step: stop takes / a settle step in `FootPlant` (9.6), so the hand-off to the tree at standstill (the foot slides 0.3-0.5 m to the idle pose in 0.3 s, also with the old gait player) disappears.

### 9.4 QB scramble running (`D:\PIXFootball\anim_qb\v0100\final6`, rerun with this code in `D:\PIXFootball\Renders\v0100_motion\qb_h`)

Contact slide, scramble / roll, 6+ frame plants, core frames: `uc5_scramble` mean 1.15 / median 0.30 -> **0.49 / 0.09**; `sg_scramble` 1.08 / 0.29 -> 1.30 / **0.08**. The means are carried by contacts that are not locomotion: the `Tackled` action (frames 826+ / 710+: 3.8, 5.3, 4.8, 2.7 m/s, contact clips of CHOREO). Running contacts only (before the tackle): uc5 n=8 mean **0.077** (0.006-0.206), sg n=8 mean **0.19** (0.004-0.098, plus one start contact at 1.2 m/s over 2 core frames = 2 cm). The start: the QB leaves the pocket clip over a 0.3 s fade, and the legs of that fade were a blend of the pocket pose and the first matched stride (9.3 item 4 took 4.85 / 1.77 m/s down to 1.2 m/s for two frames). The end: the stop to idle (frames 808-828) slides the left foot 6.9 cm to the idle pose (0.21 m/s over 20 frames), the tree hand-off at standstill: not fixed (9.6).

### 9.5 Chest-rotation pops

Over 1400 degrees per second, 36 plays: v0.9.0 gait player 306, first matcher 428 (CB 53, LB 68, S 32), **now 310** (CB 25, LB 20, S 9; DL 121, OL 89 are unchanged by the matcher). I did not find one cause of the earlier increase (100 of 103 matcher pops had no clip switch: sim yaw swings and the facing loop); it went away with this round's changes (the lock and the leg hand-back no longer drag the pelvis against the facing). The diagnosis (`-pixMmLog`, `[MMCHEST]`) is kept.

### 9.6 Still open, and clips that would help

* The stop to idle slides the planted foot 0.3-0.5 m to the idle pose in 0.3 s (tree hand-off; the old gait player does the same). A step into the idle stance in `FootPlant` (settle mode) or stop takes would fix it. Mixamo takes for that: `Run To Stop`, `Jog To Stop`, `Walk To Stop`, one relaxed standing `Idle`.
* Shuffle: true lateral shuffle takes (`Side Shuffle`, `Defensive Shuffle`, `Quick Sidestep`) instead of strafe jogs.
* A true sprint capture for 8-9 m/s (`Run_Fast_Run` tops out at 5.7 m/s planted).
* Foot twist / up-down muscles at run and sprint come from the foot polish (9.1).
* Tests: full EditMode run `!Slow`: 1167 of 1169 passed; the two failures are `PhotoPlayerTests.EveryLodFollowsTheAnimatedSkeletonAndTheHelmetSitsOnTheHeadBone` and `PhotoRosterV2Tests.FifteenHumanoidBodiesWithPadsAndFourLodsEach` (mesh bones against bind poses of the photoreal body prefabs in the shared `_Licensed` folder, BODIES' assets; nothing in this track touches them).

### 9.7 Files of the follow-up

New: `Editor/Tools/MotionClean.cs`, `Editor/Tools/MuscleReport.cs`. Changed (owned): `Gameplay/Player/Loco/FootPlant.cs` (lock weight, `KeepToe`, `YawHold`, swing-foot floor, residual accessors), `Motion/{MotionMatcher,Inertializer,MotionDb}.cs`, `SimAgentView.Loco.cs` (hand-back hook, QA log `-pixMmLogQb`), `Editor/Tools/{LocoShots,LegProbe,MotionDbBuilder,MixamoImport,MixamoScan}.cs` (QB-style toe slide column, manoeuvre trace `-pixLocoManTrace`, muscle table with maxima). Two one-line hooks in shared files: `ActionAnimator.LegsToLoco()` and `BodyMotion.ForceLoco` (used in `Plant.Late(dt, w > 0.5f || ForceLoco)`).
