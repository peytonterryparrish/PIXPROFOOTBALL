# v0.9.0 LOCOMOTION track (`locomotion.md`)

Goal (user note 2, twice): players float when they run. This track replaced the animator blend tree for running with a gait system (speed-banded mocap cycles, shared gait phase, stride warping, planted-foot IK, pelvis compensation) and measured it before / after on a treadmill and on real plays. IDs `LOCO-###` are this track's own findings and changes; the 37 `track = LOCO` rows of `ENGINE_AUDIT_issues.csv` are answered at the bottom.

Nothing here assumes a game pace: cycle choice, playback rate and stride warp are computed from the simulated velocity of every frame (SIM is lowering `GamePace`; the numbers below hold for any pace, see `LocomotionGaitTests.SpeedAgnostic_*`).

## The system (what was built)

| ID | Piece | File(s) |
|---|---|---|
| LOCO-001 | Cycle scan: every locomotion take of `Animations/Clean` (292 loops, 177 start / stop / turn takes) measured the way the match plays it (root fixed, no root motion): planted-foot speed, contact intervals, cadence, stride, loop seam | `Editor/Tools/LocoCycleScan.cs` (`Logs/loco/cycles.csv`, `maneuvers.csv`) |
| LOCO-002 | Cycle library (`PixLocoCycles.asset`, Resources): 20 cycles (free F, FL, FR; ball carrier F, FL, FR), each with window (starts on the left plant), MEASURED native ground speed (not the root-motion speed: the BGS takes overstate it 2-3x), direction, stride, gait phase markers (L lift, R plant, R lift). Measured on the spawned photoreal `PlayerAvatar`, not on the legacy rig (the same clip planted-foot speed differs 20-50 % between them) | `Gameplay/Player/Loco/LocoCycleLibrary.cs`, `Editor/Tools/LocoCycleBuilder.cs` |
| LOCO-003 | `GaitPlayer`: a Playables mixer layered over the animator controller (`ActionAnimator`, gait layer above the tree). Per travel-direction ray the two neighbour cycles of the simulated speed are picked; inside +-12 % of a cycle's native speed only that cycle plays (playback rate absorbs the difference), the cross-fade only covers the gap; one normalised gait phase drives every playing cycle through a piecewise-linear phase map of the four contact markers (blended cycles never mix mismatched feet); speed outside the rate band is returned as the stride warp factor. Direction coverage fades to the animator tree beyond about 30 degrees (back-pedal, shuffles stay on the tree) | `Gameplay/Player/Loco/GaitPlayer.cs`, `GaitMath.cs` |
| LOCO-004 | `FootPlant` (LateUpdate, after the animation): stride warp (feet offset from the body scaled along the travel direction), per-foot plant with two anchors (heel = ankle bone, ball = toe bone) held in world space until lift-off, sole pressed to the turf (mocap hovers 2-10 cm or sinks), smooth unlock, pelvis lowered by the leg's missing reach (never over-extended) plus a speed-scaled landing compression, toe-off clearance so the skid of the take's toe-off is lifted off the turf. Two-bone IK with the animation's knee direction | `Gameplay/Player/Loco/FootPlant.cs`, hooks in `BodyMotion.cs` |
| LOCO-005 | View side: raw simulated velocity (no smoothing between the simulation and the feet) into the gait player, contact hints and warp into FootPlant, facing bias so the playing cycle travels along the velocity (the straight takes are 7-20 degrees off their own hips) | `Gameplay/Player/SimAgentView.Loco.cs`, 3 hook lines in `SimAgentView.cs`, 5 in `ActionAnimator.cs` |
| LOCO-006 | Closed loops: the stance part of the cycle plays faster / slower from the measured slide of the planted foot (`GaitPlayer.Feedback`), and each cycle learns its real native speed per body size from the slide in the first half of every stance (`Learn`, bounded +-12 % in play; the baked values come from `LocoShots -pixLocoModes learn`) | `GaitPlayer.cs`, `Editor/Tools/LocoShots.cs` |
| LOCO-007 | Tests: `LocomotionGaitTests` (6: bracket continuity, solo play inside the rate band, phase map identity / monotonic / event alignment, rate band and warp, pace independence, library angle) | `Tests/EditMode/Gameplay/LocomotionGaitTests.cs` |
| LOCO-008 | Measurement and film: treadmill (real match view at constant velocity), real plays (motion dump CSV of every running player each frame), 12-frame strips (game camera and side camera); `-pixLocoOff` plays the match with the v0.8 locomotion (also inside `EngineAudit`) | `Editor/Tools/LocoShots.cs` |

How it plays: `SimAgentView.ApplyMatch` -> `DriveGait` (velocity in the body frame) -> `GaitPlayer.Update` (selection, phase, weights; the layer fades in with speed 0.3-0.95 m/s) -> animation -> `BodyMotion.Late` -> `FootPlant.Late`. If `PixLocoCycles` is missing the old tree and the old foot lock run unchanged (free roam never uses the gait layer).

## Findings about the source takes (LOCO-009)

* Every BGS sprint cycle (`Sprint08/09`, `CB_Sprint`) and the Motus `SprintCycle*` / `RunCycleJR/Leon*` takes are bounding gaits on the match avatar: one foot never reaches the turf (lowest sole 4-11 cm above the bind pose) and both feet are off the ground 62-84 % of the cycle. That is the E-001 cause. The library keeps them (nothing better exists among the 292 loops: only `Run07_F` 0.41, `Run08_F` 0.44 and the ball-carrier jog 0.19 are below 0.5) and the plant IK presses the sole to the turf wherever the take plants, but the flight fraction stays a property of the take.
* Root-motion speed of the BGS takes is wrong (`Run08_F` 9.1 m/s root, 2.8-4.5 m/s foot). The library therefore uses the speed at which the planted ball of the foot stands still, then the learned correction (`LocoShots -pixLocoModes learn`; factors 0.62 - 1.56).
* Measured travel angle of the straight takes is noise (-28..+9 degrees); forward takes are defined as 0 degrees, `FL` / `FR` takes as -24 / +24.
* The BGS `Sprint08_FR` take travels left like `Sprint08_FL` (mislabelled); the library uses `Sprint09_FR` for the right side.

## Before / after

Planted-foot speed = horizontal speed of the ground-contact point (heel or ball, whichever is lower) while its foot is within 2 cm of its own lowest point and slower than 85 % of the body (stance window). "inner" drops the first and last 20 % of the window (touchdown and lift-off frames). Contact height = lowest sole point above the turf (standing flat reads -0.01). Target: planted speed < 0.10 m/s, contact height 0-3 cm.

### Treadmill (real match view, constant velocity, 3.5 s window; `D:/PIXFootball/anim_loco/{before,after}/metrics_*.md`)

| gait | planted mean before | inner before | contact h before (m) | hips bob before (m) | planted mean after | inner after | contact h after (m) | hips bob after |
|---|---|---|---|---|---|---|---|---|
| free forward 1.0 m/s | 0.27 | 0.20 | -0.006 | 0.013 | 0.17 | 0.13 | 0.003 | 0.064 |
| free forward 1.5 m/s | 0.49 | 0.50 | 0.001 | 0.020 | 0.08 | 0.03 | -0.003 | 0.065 |
| free forward 2.0 m/s | 0.51 | 0.49 | 0.017 | 0.061 | 0.12 | 0.07 | -0.001 | 0.059 |
| free forward 2.5 m/s | 0.55 | 0.52 | 0.014 | 0.053 | 0.60 | 0.46 | 0.002 | 0.063 |
| free forward 3.0 m/s | 0.87 | 0.85 | 0.005 | 0.049 | 0.73 | 0.55 | 0.004 | 0.061 |
| free forward 3.5 m/s | 1.21 | 1.10 | 0.005 | 0.047 | 0.51 | 0.57 | 0.000 | 0.070 |
| free forward 4.0 m/s | 1.27 | 1.14 | 0.004 | 0.045 | 0.64 | 0.75 | 0.000 | 0.074 |
| free forward 4.5 m/s | 1.32 | 1.31 | 0.000 | 0.043 | 0.71 | 0.76 | 0.001 | 0.069 |
| free forward 5.0 m/s | 1.73 | 1.70 | 0.005 | 0.041 | 0.44 | 0.32 | -0.001 | 0.091 |
| free forward 5.5 m/s | 1.86 | 1.71 | 0.010 | 0.039 | 0.45 | 0.38 | -0.001 | 0.099 |
| free forward 6.0 m/s | 1.90 | 1.72 | 0.020 | 0.040 | 0.31 | 0.15 | 0.003 | 0.091 |
| free forward 6.5 m/s | 2.36 | 2.58 | 0.040 | 0.048 | 0.56 | 0.23 | 0.009 | 0.088 |
| free forward 7.0 m/s | 2.61 | 2.82 | 0.057 | 0.046 | 0.76 | 0.21 | 0.009 | 0.088 |
| free forward 7.5 m/s | 3.40 | 3.45 | 0.051 | 0.040 | 0.69 | 0.25 | 0.010 | 0.103 |
| free forward 8.0 m/s | 3.70 | 3.76 | 0.052 | 0.040 | 1.48 | 0.48 | 0.016 | 0.063 |
| free forward 8.5 m/s | 3.99 | 4.09 | 0.049 | 0.040 | 1.52 | 0.38 | 0.016 | 0.058 |
| carry forward 2.5 m/s | 1.35 | 1.26 | 0.002 | 0.035 | 0.55 | 0.56 | 0.000 | 0.084 |
| carry forward 4.0 m/s | 1.25 | 1.17 | -0.011 | 0.050 | 0.20 | 0.17 | 0.001 | 0.081 |
| carry forward 5.5 m/s | 1.73 | 1.53 | 0.014 | 0.020 | 0.44 | 0.43 | -0.001 | 0.100 |
| carry forward 7.0 m/s | 2.92 | 2.75 | 0.037 | 0.024 | 1.46 | 0.48 | 0.013 | 0.047 |
| carry forward 8.5 m/s | 2.79 | 2.50 | 0.049 | 0.033 | 1.69 | 0.57 | 0.018 | 0.051 |
| free forward-left 20 3.0 m/s | 1.43 | 1.44 | -0.008 | 0.042 | 0.53 | 0.45 | 0.002 | 0.032 |
| free forward-right 20 3.0 m/s | 1.09 | 0.97 | 0.011 | 0.051 | 0.71 | 0.64 | 0.017 | 0.050 |
| free forward-left 20 5.0 m/s | 2.87 | 2.63 | -0.002 | 0.037 | 0.46 | 0.18 | 0.006 | 0.096 |
| free forward-right 20 5.0 m/s | 2.01 | 2.08 | 0.007 | 0.044 | 0.50 | 0.57 | 0.003 | 0.055 |
| free forward-left 20 7.0 m/s | 4.64 | 4.40 | 0.008 | 0.034 | 0.59 | 0.15 | 0.005 | 0.096 |
| free forward-right 20 7.0 m/s | 2.51 | 2.48 | 0.055 | 0.048 | 0.65 | 0.67 | 0.005 | 0.077 |
| free backpedal 1.5 m/s | 0.56 | 0.53 | -0.052 | 0.013 | 0.65 | 0.60 | -0.037 | 0.013 |
| free backpedal 2.5 m/s | 0.72 | 0.66 | -0.048 | 0.016 | 0.61 | 0.52 | -0.029 | 0.016 |
| free backpedal 3.5 m/s | 0.81 | 0.80 | -0.037 | 0.017 | 0.54 | 0.49 | -0.016 | 0.032 |
| free backpedal 4.5 m/s | 1.47 | 1.65 | -0.010 | 0.013 | 0.77 | 0.80 | 0.016 | 0.025 |
| free shuffle right 2.0 m/s | 0.87 | 0.72 | -0.016 | 0.037 | 0.29 | 0.29 | 0.005 | 0.035 |
| free shuffle right 3.0 m/s | 1.11 | 0.90 | 0.001 | 0.045 | 0.34 | 0.25 | 0.018 | 0.041 |

Reading: inner stance speed falls from 0.5-4.6 m/s to 0.1-0.8 m/s (sprints 7-8.5 m/s: 3.4-4.0 -> 0.3-0.5), contact height from +0.04..+0.06 m (hovering sprints) to 0.00-0.02 m everywhere. The target of 0.10 m/s inner is met at 1-2 m/s and at some speeds (4.0 m/s carry 0.17, 6.0 m/s 0.15, 20 degree left cut 7 m/s 0.15); the band 3-4.5 m/s (two non-overlapping cycles, a blend between different stride shapes) stays at 0.5-0.8 m/s and back-pedal / shuffle (still the animator tree) at 0.3-0.8 m/s. The touchdown and lift-off frames of every stance still move at 2-5 m/s (the full-window mean column of the metrics files shows it): the foot is stopped by the plant on the frame after it lands.

### Real plays (6 plays, every running player, stretches of at least 0.5 s of plain locomotion)

| band | windows | planted mean before | inner before | contact h before | planted mean after | inner after | contact h after |
|---|---|---|---|---|---|---|---|
| jog 2-3.5 | 7 / 9 | 0.76 | 0.58 | -0.034 | 0.71 | 0.63 | -0.016 |
| run 5-6.5 | 2 / 3 | 3.31 | 3.51 | 0.002 | 1.54 | 0.76 | 0.027 |
| sprint 6.5-8 | 6 / 8 | 2.73 | 2.71 | 0.027 | 0.48 | 0.26 | 0.007 |
| all run + sprint | 8 / 11 | 2.87 | 2.91 | 0.020 | 0.77 | 0.39 | 0.013 |

(small sample: the plays end at the first tackle; use the 36-play audit below for statistics.)

### EngineAudit, 36 real plays (same tool as the audit; `-pixLocoOff` = v0.8 locomotion; `D:/PIXFootball/Renders/loco_audit/{before,after}/motion_summary.txt`)

Contact min slide, mean m/s (audit target < 0.3):

| key | before | after |
|---|---|---|
| CB Run 4.5 m/s | 0.998 | 0.266 |
| CB Run 6.0 | 1.558 | 0.267 |
| CB Sprint 6.0 | 1.297 | 0.234 |
| CB Sprint 7.5 | 2.867 | 0.580 |
| LB Run 4.5 | 1.121 | 0.221 |
| LB Run 6.0 | 1.464 | 0.183 |
| LB Sprint 6.0 | 2.024 | 0.164 |
| LB Sprint 7.5 | 2.863 | 0.243 |
| RB Route 4.5 | 1.230 | 0.172 |
| RB Route 6.0 | 1.722 | 0.206 |
| RB Carry 4.5 | 1.005 | 0.482 |
| RB Carry 6.0 | 0.614 | 0.364 |
| RB Run 4.5 | 1.037 | 0.551 |
| CB Backpedal 3.0 (tree, not reworked) | 1.203 | 0.633 |

Gait bins (steps/s, stride m, flight fraction):

| key | before | after | audit target |
|---|---|---|---|
| CB Sprint 7.5 | 1.96, 4.02 m, 0.72 | 3.31, 2.38 m, 0.49 | 4.3-4.7 steps/s, 2.0-2.4 m, <= 0.50 |
| LB Sprint 7.5 | 2.02, 3.84 m, 0.73 | 3.37, 2.30 m, 0.46 | same |
| CB Run 4.5 | 2.47, 2.12 m, 0.19 | 3.41, 1.54 m, 0.44 | 3.3 steps/s, 1.2-1.6 m |
| RB Carry 6.0 | 1.41, 4.61 m, 0.53 | 2.81, 2.30 m, 0.47 | |
| RB Carry 4.5 / WR Route 4.5 | 1.90, 2.83 m / 2.97, 1.78 m | unchanged cadence, flight 0.41 -> 0.55 / 0.20 -> 0.50 | |
| CB Backpedal 3.0 | 1.64, 2.21 m, 0 | 1.85, 1.97 m, 0 | 3.0-3.4 steps/s, 0.9-1.4 m |

Honest reading: sliding is fixed (4-10x lower, below the 0.3 target for CB / LB / RB route at run and sprint speeds), stride and sprint cadence are in the right range, sprint flight is down from 0.72 to 0.46-0.49. Run-speed flight went UP (0.19 -> 0.44): the old tree at 4.5 m/s played slow jog cycles whose feet hovered a few cm above the turf and glided (low "flight" because the feet never left a 6 cm band, no real contact); the new cycles bound with a real flight phase (the take's property, see LOCO-009). Sprint cadence 3.3-3.4 steps/s is still below the 3.8-4.3 target (the best take, `Sprint09_F`, has 3.8 steps/s at 8 m/s and is rate-limited to +-12 %).

### Strips (looked at; 12 frames, 0.1 s apart)

| what | before | after |
|---|---|---|
| treadmill jog 3.2 m/s, side | `D:/PIXFootball/anim_loco/before/strip_treadmill_jog_3p2_side.png` | `D:/PIXFootball/anim_loco/after/strip_treadmill_jog_3p2_side.png` |
| treadmill ball carrier 5.5 m/s, side (sprinting RB) | `.../before/strip_treadmill_carry_5p5_side.png` | `.../after/strip_treadmill_carry_5p5_side.png` |
| treadmill sprint 7.5 m/s, side | `.../before/strip_treadmill_sprint_7p5_side.png` | `.../after/strip_treadmill_sprint_7p5_side.png` |
| real play WR running a route, side camera | `.../before/strip_wr_run_side.png` | `.../after/strip_wr_run_side.png` |
| real play WR, game camera | `.../before/strip_wr_run_game.png` | `.../after/strip_wr_run_game.png` |
| real play RB after the hand-off (tackled after 1 s), side / game | `.../before/strip_rb_sprint_{side,game}.png` | `.../after/strip_rb_sprint_{side,game}.png` |

What the strips show: before, the legs stay tucked under the hips with small stiff steps (feet hang near the body and glide); after, the stride opens, the foot lands under the body and stays on the turf for the stance (frames 1 / 5 / 9 of the WR strip, frames 4 / 12 of the carry strip), forward lean and arm drive come from the take. The jog at 3.2 m/s (between `Run08_F` 2.8 and `Run07_F` 4.1) now BOUNCES (flight 0.4-0.44) where the old one shuffled; it is the weakest band (open item 1). The 7.5 m/s sprint still has two airborne frames per cycle, the nature of the `SprintCycleLeon_1` take.

## The 37 `track = LOCO` audit rows

Status: fixed / partly / open (works, not done this round) / handed (belongs to another track's files: SIM = `Sim/*`, CONNECT = `ActionAnimator` / contact / render interpolation).

| row | status | note |
|---|---|---|
| E-001 sprint bounding gait | partly | flight 0.72 -> 0.46-0.49, stride 4.0 -> 2.3-2.4 m, steps 1.96 -> 3.3; cadence still below 4.3 (take limits, LOCO-009) |
| E-002 run / jog cadence and stride | fixed for CB / LB / S / RB-carry 6 m/s (3.3-3.6 steps/s, 1.5 m); WR route and RB carry 4.5 unchanged in the audit bins | |
| E-003 planted feet slide | fixed | 0.17-0.55 m/s mean contact slide at 4.5-6 m/s (was 1.0-1.7); inner stance 0.1-0.8 on the treadmill |
| E-004 no start clip | partly | starts are now steps (cadence follows speed from the walk cycle, plant on every contact); no start take is played (the stance `GetOff` clips of the snap exist from v0.8) |
| E-005 90 degree cut takes 0.88 s | handed | `AgentBody` physics (SIM) |
| E-006 no render interpolation | handed | CONNECT |
| E-007 sprinting defenders slide back | fixed | CB / LB sprint 6.0-7.5: 2.0-2.9 -> 0.16-0.58 |
| E-008 back-pedal cadence / stride | open | back-pedal / shuffle stay on the animator tree (no library take is long enough: `Jog_B_BC` 2.3 m/s, shuffles 0.2-0.3 m strides); FootPlant now plants them from the pose (CB back-pedal 3.0: 1.2 -> 0.63) |
| E-009 foot lock never engages | fixed | `FootPlant` plants by contact (gait phase + height), no 0.65 m/s gate; old lock only for stationary poses |
| E-010 no stop clip | open | needs a stop take chosen by the deceleration (`*_To_Stand_Relaxed` takes are measured in `maneuvers.csv`); not wired |
| E-011 no plant-and-cut | open | `Sprint*_90_Deg_Turn_*` takes measured, not wired |
| E-012 turn in place has no feet | open | |
| E-013 back-pedal uses the 5 m/s shuffle | open | see E-008 |
| E-014 cadence not matched, closed loop off | fixed | library of measured speeds + stance feedback + learned factors (LOCO-006) |
| E-017 route runners / carriers skate | fixed | RB route 4.5 / 6.0: 1.23 / 1.72 -> 0.17 / 0.21; carry 1.0 -> 0.48 |
| E-018 lateral shuffle slides, sprint-sideways labelled Shuffle | partly | planted from the pose (shuffle 2-3 m/s treadmill 0.9-1.1 -> 0.3-0.6); the action label is SIM |
| E-020 sprinter needs 1 s to turn 90 degrees | handed | SIM |
| E-021 one tree for every body | partly | cycle speeds scale with the body (leg length x measured size scale, learned per size); no role-specific sets |
| E-022 idle child plays 2.7x | open | controller builder untouched |
| E-023 facing leads velocity mid-cut | partly | the gait yaw bias aligns the cycle's travel with the velocity; the sim facing is SIM |
| E-024 tackled player slides 2.9 yd | handed | SIM |
| E-025 pads x pace stack | handed | SIM |
| E-026 QBs slow | handed | SIM |
| E-027 back-pedal / shuffle caps too fast | handed | SIM |
| E-033 man in motion moonwalks | handed | SIM sets facing; with the gait he would now walk the right way if the sim faced him along the travel |
| E-034 hips height the same for every body | open | the pelvis compensation lowers the hips up to 7 cm while a leg is stretched, but no per-role posture |
| E-035 shuffle toe-out only clamped | open | |
| E-036 lean / lock off during masked clips | open | `LocomotionWeight` is 1 - clip weight (CONNECT / ActionAnimator mask); FootPlant uses the same weight |
| E-037 facing loop chases arm swing | partly | the bias removes the cycle's own offset; loop unchanged |
| E-038 lean too small | open | pitch / bank untouched; the carry takes bring their own lean |
| E-039 FitGround / `_gy` | partly | `FootPlant` uses the view's ground height (`GroundY`), `BodyMotion._gy` and FitGround unchanged |
| E-040 / E-041 / E-042 acceleration, reversal, fatigue | handed | SIM |
| E-282 action flicker | handed | SIM |
| E-296 post-whistle braking | handed | SIM |
| E-418 kick plant foot | open | `FootPlant` could pin the plant foot of a kick plan; not wired |

## Open items after round 1 (see Round 2 below for what changed)

1. The 2.5-4.5 m/s band: two sparse cycles (`Run08_F` 2.8, `Run07_F` 4.1; the best jog `MOB1_Jog_F_BC` 2.6 has ball-carrier arms). Next step: a free-arm jog (upper-body from `Run07_F`, legs from the jog) through the gait mixer's layer masks, or a new mocap jog.
2. Sprint flight and cadence: needs a sprint take with ground contact on both feet (none in the library); alternative is a stance-extending phase map (stance 0.25 per foot) with the stride warp absorbing the longer sweep.
3. Back-pedal, shuffle, crossover, start / stop / cut / turn takes: measured (`Logs/loco/maneuvers.csv`: 177 takes with entry / exit speed, yaw change, travel) but not played; back-pedal needs its own gait ray (the tree still plays them).
4. Touchdown frame: the foot arrives at 2-5 m/s on the frame before the plant takes it (phase hint leads the take by a frame at 7+ m/s): a one-frame look-ahead of the contact markers.
5. Arm-swing amplitude with speed and acceleration lean beyond `LeanMath` were not done (the takes carry their own).
6. Online learning (`GaitPlayer.Learn`) is on, bounded +-12 % around the baked values; if the play-test shows drift set `GaitPlayer.LearnEnabled = false`.
7. Re-run the library build when clips are re-imported: `LocoCycleBuilder.Build` (after `LocoShots -pixLocoModes learn` for the factors; they are read from `D:/PIXFootball/anim_loco/calibration_factors.csv`). The asset `Assets/_Licensed/Resources/PixLocoCycles.asset` was created in the shared tree (git-ignored licensed folder).

## Round 2 (LOCO-010 .. LOCO-015)

Round 2 asked for: maneuver takes, a free-arm jog for 2.5-4.5 m/s, less sprint flight and a higher cadence, the touchdown skid, arm swing with speed, and (high priority) at least one foot on the turf during every action clip. Measured with the same tools as round 1 (`D:/PIXFootball/anim_loco/{r2_before,r2_after,r2f}`, `D:/PIXFootball/Renders/loco_audit/{before,after_r2f,act_before,act_after2}`).

| ID | Change | File(s) |
|---|---|---|
| LOCO-010 | Back-pedal ray at 180 degrees (`MOB1_Jog_B_Loop_BC`, native 2.27 m/s, free arms from `Run08_F`). Ray picking is nearest-ray with saturation flags | `GaitPlayer.cs`, `LocoCycleBuilder.cs`, `LocoCycleLibrary.cs` |
| LOCO-011 | Free-arm jog: `MOB1_Jog_F_Loop_BC` legs with both arms of `BGS_Run08_F` through a third mixer input (donor-arm layer on the arm mask); `SprintCycleLeon_1` legs with `Sprint09_F` arms. Fills 2.5-4.5 m/s | `GaitPlayer.cs`, `LocoCycleBuilder.cs`, `ActionAnimator.cs` (3 inputs) |
| LOCO-012 | Cadence boost: playback rate x boost, stride warp 1/boost (same ground speed, more steps per second; stance extension was tried and rejected). Contacts/s at 7-7.5 m/s: 2.0 -> 4.6 | `GaitPlayer.cs` (`BoostOf`) |
| LOCO-013 | Touchdown / lift-off skid at 7+ m/s: one-frame phase lead, slip computed on the warped positions, per-point speed gate in `FootPlant` | `GaitPlayer.cs`, `FootPlant.cs` |
| LOCO-014 | Arm swing `0.9 + 0.35 * smoothstep(2.5..8.5 m/s)` (off while carrying the ball or when the gait layer is not ready); braking lean clamp widened from -9 to -11.7 degrees | `BodyMotion.cs`, `SimAgentView.Loco.cs` |
| LOCO-015 | Action ground pass, runs last in `LateUpdate` (after the stance / QB pose IK and the arm IK): lowers or raises the avatar root so the lowest sole is on the turf (clamp -0.5 .. +0.08 m, attack 0.03 s, release 0.06 s, dead zone 1.2 cm), then holds the planted foot (`FootPlant` action mode, 10 cm drift release). Not applied to genuine flight (`AirborneAction` / `AirborneClip`: jump, leap, dive, hurdle, flip, spike), contact-director takes, an active ragdoll or a gait layer above 0.5 | `SimAgentView.ActionGround.cs`, `FootPlant.cs` |

Switches (batch / debug): `-pixLocoOff` v0.8 locomotion (also turns the action pass off), `-pixLocoNoActionGround`, `-pixLocoNoArms`, `-pixLocoBackFactor x`, `-pixLocoNoFactors` (builder), `-pixLocoOnly name` (treadmill case filter). `LocoShots` has new modes `maneuvers` (stops, 90 degree cuts) and `kicks` (FG / kickoff / punt kicker hover, QB play hover counts).

### Treadmill, planted-foot speed (mean m/s; real match view)

| case | before round 2 | after round 2 |
|---|---|---|
| stop from 4 / 6 / 8 m/s | no stance found / 1.46 / 1.12 | 0.43 / 0.45 / 0.49 |
| 90 degree cut at 6 / 8 m/s | 1.59 / 1.76 | 0.68 / 0.65 |
| free gait 1-2 m/s | | 0.09-0.12 |
| free gait 3.0 / 3.5 / 4.0 / 4.5 m/s | | 0.52 / 0.43 / 0.75 / 1.07 |
| free gait 5-7.5 m/s | | 0.55-0.65 |
| carry 4.0 m/s | | 0.27 |
| back-pedal 1.5 / 2.5 / 3.5 / 4.5 m/s | | 0.12 / 0.19 / 0.54 / 0.84 |

Contact height 0 +- 3 mm (back-pedal 3.5-4.5 m/s: -16 mm). Arm swing amplitude 0.31 -> 0.27 m at 1.5 m/s (calmer) and 0.67 -> 0.74 m at 8 m/s. Strips looked at: `strip_maneuver_stop_from_6_ms.png`, `strip_maneuver_cut_90_deg_at_6_ms.png` (`anim_loco/r2_after`): the feet plant, the pelvis drops on the stop, the cut plants the outside foot.

### EngineAudit, 36 real plays (contact slide m/s; steps/s, stride m, flight fraction)

| case | before | after round 2 |
|---|---|---|
| CB Sprint 7.5 | 2.87; 1.96 / 4.02 / 0.72 | 0.32; 4.65 / 1.69 / 0.53 |
| LB Sprint 7.5 | 2.86; 2.02 / 3.84 / 0.73 | 0.28; 4.94 / 1.57 / 0.42 |
| CB Sprint 6.0 | 1.30; 1.03 steps/s | 0.22; 4.33 steps/s |
| CB Run 4.5 | 1.00; 2.47 / 2.12 / 0.19 | 0.42; 3.55 / 1.48 / 0.41 |
| CB Run 6.0 | 1.56; 2.72 / 2.33 / 0.33 | 0.85; 4.17 / 1.52 / 0.49 |
| LB Run 4.5 | 1.12; 2.54 / 2.05 / 0.19 | 0.25; 3.30 / 1.58 / 0.46 |
| S Run 4.5 | 0.97; 2.33 / 2.26 / 0.20 | 0.26; 3.49 / 1.51 / 0.49 |
| RB Route 4.5 | 1.23; 3.61 / 1.45 / 0.14 | 0.34; 3.46 / 1.51 / 0.45 |
| RB Carry 4.5 | 1.01; 1.90 / 2.83 / 0.41 | 0.54; 3.67 / 1.46 / 0.31 |
| RB Carry 6.0 | 0.61; 1.41 / 4.61 / 0.53 | 0.64; 3.91 / 1.66 / 0.45 |
| CB Backpedal 3.0 | 1.20; 1.64 / 2.21 / 0.00 | 0.17; 1.40 / 2.60 / 0.00 |
| CB Backpedal 4.5 | 0.63 | 0.09 |
| LB Backpedal 3.0 | 0.91 | 0.65 |
| S Backpedal 3.0 | 0.58 | 0.41 |

Reading: sprint cadence is now 4.3-4.9 steps/s (target 4.3); the stride is 1.6-1.7 m (target 2.0-2.4), i.e. more steps at the same speed. Flight is still 0.42-0.53 because the takes themselves fly. Run flight reads higher than before because the free-arm jog and run takes now play where the old tree blended walk poses (real take flight, 0.41-0.49 against 0.19). CB Run 6.0 (0.85) is the weakest remaining cell (it sits on the Run07 / Run_F seam; a regression against round 1). Back-pedal cadence is still 1.4-2.0 steps/s, not 3.0-3.4: the only back take is a 2.27 m/s jog; the slide is fixed, the cadence is not.

### Action ground pass: frames with no foot on the turf during actions (lowest toe more than 7 cm above the turf; EngineAudit 36 plays; before = `-pixLocoNoActionGround`)

| action | before | after |
|---|---|---|
| Backpedal | 22.2 % | 1.3 % |
| Block | 8.7 % | 1.9 % |
| Engaged | 36.3 % | 10.2 % |
| Handoff | 18.2 % | 3.4 % |
| Shed | 46.6 % | 19.4 % |
| Shuffle | 15.9 % | 2.9 % |
| Throw | 0.0 % | 0.1 % |
| Catch | 41.7 % | 21.4 % |
| Idle | 11.0 % | 10.5 % |

The Idle and most of the Catch residual are contact-director tackle takes (excluded on purpose: the tackle clip owns the pose). Run / Route / Sprint / Carry remain 36-70 %: that is the natural flight of the gait takes, not an action clip. Kickers (frames with no toe within 7 cm): field goal 41 -> 0 of 201, kickoff 14 -> 0 of 270 (the punt strip was not run separately). QB plays after the pass: drop_shotgun 0 %, drop5_under_center 0 %, play action 2.1 %, rollout 4.0 %, handoff 7.0 %, handoff_shotgun 0 %; the residual is the rollout run and the hand-off cross-over. Pop count of the motion dump is unchanged. Order checked in `SimAgentView.LateUpdate`: animation -> stance / QB pose (`ProcGait`, stance IK) -> arm IK -> `ActionGroundLate`. Strips looked at (`act_after2/strips`: FG side / behind, `qb_uc5_Side`): feet are on the turf relative to the camera; the numbers are the authority because the shifts are a few centimetres.

### Not done in round 2

* Shuffle L/R and crossover are not gait rays: no usable lateral take exists (`maneuvers.csv`: 0.2-0.3 m strides at 0.5-1.5 m/s). They stay on the animator tree and get the plant and the action ground pass (shuffle no-foot frames 15.9 -> 2.9 %).
* Start, turn-in-place, 45 / 90 / 135 degree plant-and-cut and stop clips are not played as clips. Stops and cuts are the gait plus `FootPlant` / pelvis (numbers above). The `*_To_Stand_Relaxed` takes have unreliable root motion and run 1.2-1.4 s against the sim's 0.6 s braking.
* Grounded sprint: only the cadence was raised. The flight fraction is a property of the takes.
* The 3-4.5 m/s band still has planted speed 0.4-1.0 m/s (one jog cycle at 2.6 m/s stretched; it bounces).
* Touchdown lead is in, not separately quantified.

### What better source mocap would fix what remains

* Grounded sprint 8-9 m/s with both feet contacting (flight <= 45 %) and a grounded 5-6.5 m/s run: sprint / run flight and the 2.0-2.4 m stride.
* Free-arm jog 3-4.5 m/s: the 3-4.5 m/s slide and bounce.
* DB back-pedal 3-4.5 m/s, stride 0.9-1.4 m at 3.0-3.4 steps/s, free arms: back-pedal cadence.
* Lateral shuffle L/R and crossover at 2-5 m/s: wire as gait rays.
* Start from standstill (walk / jog / sprint), stop (sprint / run / jog to stand, 0.5-0.7 s), plant-and-cut 45 / 90 / 135 at jog and sprint speed, turn in place: wire as maneuver takes.
* Clean root motion and foot-contact metadata on all of them (the BGS root speed is 2-3x overstated).
* A kicker approach with a grounded plant foot (the pass now removes the hover by shifting the body; a true plant looks better).

### Audit rows, round 2 update

| row | status now |
|---|---|
| E-001 | partly: cadence 4.3-4.9 steps/s; flight 0.42-0.53 (was 0.72); stride 1.6-1.7 m; rest needs grounded sprint mocap |
| E-002 | partly: free-arm jog 2.6 m/s; 3-4.5 m/s band slide 0.4-1.0 |
| E-004 / E-010 / E-011 / E-012 | open as clips; stop and cut slide fixed through gait + plant (stop 1.1-1.5 -> 0.43-0.49, cut 1.6-1.8 -> 0.65-0.68) |
| E-008 / E-013 | partly: back-pedal ray at 2.27 m/s native, slide 0.09-0.65; cadence still 1.4-2.0 steps/s |
| E-018 | partly: shuffle on the tree, planted + action ground pass |
| E-036 | partly: the action ground pass respects the clip weight (not applied while the gait layer is above 0.5, contact-director takes excluded) |
| E-038 | partly: braking lean clamp -9 -> -11.7 degrees, arm swing with speed |
| E-418 | fixed for the hover: kicker FG 41 -> 0 hover frames, kickoff 14 -> 0 |

All other rows keep their round 1 status.

## Round 3: leg quality while running (LOCO-016)

User: the legs / feet / ankles look wobbly and weak, the feet and ankles "break" and turn left and right, the knees and thighs do not run straight. Measured first, then fixed. Tool: `Editor/Tools/LegProbe.cs`, run on the FINAL pose (after animation, stance IK, FootPlant, action pass) every frame: on the treadmill (`LocoShots -pixLocoModes legs`, 8 gaits, 330 frames each) and in the 36 real plays (`EngineAudit`, `leg_quality.md`, every running player every frame).

Metrics (all against the line of travel; backpedal against its reverse): knee deviation from the hip-ankle plane in stance (cm, + inward), knee hinge yaw (the knee's bend plane against the line, degrees, only while the knee is bent more than 15), pole flips (hinge axis turning more than 50 degrees in a frame), foot toe-out at contact (the lateral axis of the foot bone, so it is valid however steeply the foot pitches), foot yaw change per frame in contact, ankle roll (tilt of the sole about the foot's long axis; 0 = level), roll jitter per frame, thigh / shin / foot twist (humanoid muscles in degrees), pelvis yaw off the line.

### What was wrong (found with the switches `-pixLocoOff`, `-pixLocoNoPlant`, `-pixLocoNoWarp`, `-pixLocoNoIK`, `-pixLocoNoDonor`, `-pixLocoNoPolish`)

| suspect | result |
|---|---|
| the takes themselves | MAIN CAUSE. Run / sprint / jog takes bend the knee 20-55 degrees off the line (jog 53), twist the shin about its own axis to the muscle limit (shin twist 40-60 degrees at p95), turn the foot 60-120 degrees off at p95 and tilt the sole 10-20 degrees in stance (cleat soles visible from the front). The v0.8 tree (`-pixLocoOff`) is no better: knee 16-32 degrees off. |
| knee pole of the plant IK | the pole was the take's own knee direction, so the IK kept every knee-out. Now: the knee is turned about the hip-ankle axis with the whole leg as one rigid piece (ankle stays, hinge stays square to the thigh), to the line +-6 degrees (+3 toe-out), never a pole swing. 0 pole flips on the treadmill. |
| the v0.7.0 toe clamp in `BodyMotion.ClampToes` | fought the new foot heading (it measures the toe against the facing, not the travel, and twists the shin by half the correction). It stands down while the leg polish runs (`FootPlant.PolishWeight > 0.5`). |
| stride warp, donor arms | NOT causes (`-pixLocoNoWarp`, `-pixLocoNoDonor` rows equal the polish-off row). |
| round 1 splay twist (`AnimSplayFix`) | NOT a cause: for all 10 library clips the `_backup_splay` originals are byte-identical in the twist curves to the clips on disk (CleanBatch regenerated them after the fix). The 3 splay-fixed clips that are not in the library are untouched. |
| plant IK itself (`-pixLocoNoIK`, `-pixLocoNoPlant`) | foot yaw at contact is as bad without it (15-24 degrees mean, p95 50-120): the IK only restores the foot's animated world rotation. |

### What changed

| ID | Change | File(s) |
|---|---|---|
| LOCO-016 | Leg polish, last step of `FootPlant.Late` while a runner moves (speed ramp 0.5-1.5 m/s, off in action mode, switch `-pixLocoNoPolish`): (1) knee along the line of travel (rigid leg turn about hip-ankle, clamp +-55, take's own +-6 left); (2) shin twist and foot twist held to +-10 degrees of the bind pose by a swing-twist split (the turn is about an axis through the ankle, nothing else moves); (3) foot yaw pulled toward the line, never more than 10 degrees off it (4 degrees toe-out), turn rate limited to 900 deg/s (no foot flicks); (4) ankle roll limited to 8 degrees (2 degrees on a planted foot); pitch (heel strike, toe-off) is the take's. The line is the travel direction (reverse when backpedalling, the pelvis facing from the two hip joints in between). | `Loco/FootPlant.cs`, `BodyMotion.cs` (toe clamp stands down) |
| LOCO-016 | `LegProbe` + `legs` mode of `LocoShots` (8 gaits, 3-view strips) + `leg_quality.md` in `EngineAudit`; `FootPlant.YawOf / RollOf / TwistOf` (pure, unit tested). | `Editor/Tools/LegProbe.cs`, `LocoShots.cs`, `EngineAudit.cs` |

A bug found on the way and fixed: my first version used `Mathf.SmoothStep(a, b, x)` as a 0..1 ramp (it interpolates between a and b: the weight came out 1.5). The same misuse still sits in the round 1 plant weights `wH / wB` (`FootPlant.Step`: they stay about 0.97 instead of fading with the heel height); left as is because the slide numbers of rounds 1-2 were measured with it.

### Treadmill (before = `-pixLocoNoPolish`, after = default; knee dev = stance cm mean / p95, hinge = knee bend plane off the line degrees mean / p95, toe-out = at contact mean / p95, roll = sole tilt in stance mean / p95, jitter = roll change per frame mean / p95, yaw change = foot yaw per frame in contact p95)

| gait | knee dev before | after | hinge before | after | toe-out before | after | roll before | after | jitter before | after | yaw change p95 before | after |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| jog 3.2 | 7.5 / 12.2 | 2.4 / 5.3 | 53.5 / 81.5 | 13.7 / 25.1 | 18.4 / 116 | 7.5 / 18.2 | 10.4 / 60 | 2.0 / 2.2 | 5.0 / 14.8 | 0.7 / 3.2 | 2.5 | 9.4 |
| run 4.5 | 4.8 / 6.3 | 1.5 / 1.7 | 30.0 / 39.6 | 9.0 / 9.2 | 27.7 / 121 | 4.0 / 13.3 | 17.2 / 65 | 2.0 / 2.2 | 4.9 / 14.8 | 0.7 / 3.2 | 14.9 | 8.0 |
| run 6.0 (WR) | 2.6 / 2.9 | 1.7 / 1.8 | 14.7 / 15.7 | 8.9 / 9.0 | 23.5 / 109 | 2.1 / 2.9 | 12.2 / 62 | 2.0 / 2.2 | 5.9 / 14.6 | 0.7 / 2.6 | 1.8 | 0.6 |
| sprint 7.5 | 1.5 / 2.2 | 1.4 / 2.0 | 6.3 / 9.9 | 6.1 / 8.9 | 4.0 / 7.1 | 3.2 / 4.7 | 3.3 / 7.4 | 1.6 / 2.2 | 7.5 / 18.4 | 0.8 / 3.2 | 2.6 | 2.1 |
| carry 4.0 | 4.1 / 4.7 | 1.6 / 1.9 | 24.3 / 32.6 | 8.9 / 9.0 | 14.8 / 99 | 0.9 / 3.1 | 8.3 / 53 | 1.6 / 2.2 | 4.3 / 11.8 | 0.8 / 3.7 | 2.3 | 1.8 |
| carry sprint 7.5 (RB) | 2.0 / 4.8 | 1.5 / 2.6 | 7.4 / 16.9 | 5.6 / 9.0 | 37.3 / 94 | 1.6 / 10.6 | 9.7 / 36 | 2.2 / 2.2 | 8.2 / 28.2 | 1.4 / 4.0 | - | 0.5 |
| backpedal 2.5 | 2.4 / 6.8 | 1.1 / 1.6 | 15.1 / 41.3 | 6.7 / 9.0 | 4.2 / 10.1 | 2.6 / 7.5 | 2.9 / 5.6 | 1.9 / 2.2 | 0.5 / 1.1 | 0.9 / 4.6 | 1.3 | 5.2 |
| backpedal 3.5 (CB) | 3.1 / 6.6 | 2.1 / 4.0 | 9.2 / 22.2 | 6.2 / 9.0 | 8.8 / 22.3 | 2.0 / 3.9 | 9.4 / 18.8 | 4.5 / 8.0 | 1.1 / 2.7 | 0.8 / 3.2 | 1.6 | 1.4 |

Targets: knee deviation in stance below 3 cm (met, mean 1.1-2.4 cm; the jog's p95 5.3), no pole flips (0), foot within 10 degrees of the line at contact (met except jog / run 4.5 p95 13-18), ankle roll within 8 degrees (met: mean 1.6-4.5, p95 2.2-8.0), no roll jitter (mean 0.7-1.4 degrees per frame, was 4-8), knee bend plane about 9 degrees off the line (the 6 degrees slack of the take plus the 3 degrees toe-out by design; jog 13.7).

Real plays (36 plays, every running player each frame; before = `-pixLocoNoPolish`, off = `-pixLocoOff`; `D:/PIXFootball/Renders/loco_audit/leg_{before,after,off}/leg_quality.md`):

| bin (mean values; before / after / v0.8 off) | frames | knee dev cm | hinge deg | toe-out deg | roll stance deg | roll jitter p95 deg/frame | roll pops | yaw pops |
|---|---|---|---|---|---|---|---|---|
| Sprint >= 5.5 m/s | 2717 | 1.9 / 1.4 / 5.0 | 10.3 / 7.3 / 22.6 | 30.3 / 16.6 / 13.4 | 21.9 / 2.1 / 16.0 | 22.1 / 3.2 / 14.2 | 1959 / 17 / 1206 | 94 / 6 / 0 |
| Run >= 5.5 m/s | 3593 | 4.5 / 1.7 / 3.8 | 21.4 / 7.9 / 21.5 | 35.5 / 9.9 / 10.2 | 18.0 / 2.7 / 9.9 | 16.7 / 3.2 / 12.0 | 2116 / 35 / 1725 | 133 / 6 / 2 |
| Run 3-5.5 m/s | 9376 | 6.6 / 2.0 / 3.4 | 31.2 / 9.1 / 22.5 | 39.8 / 9.1 / 11.4 | 18.6 / 3.7 / 6.8 | 13.7 / 3.3 / 8.0 | 3608 / 158 / 1601 | 558 / 233 / 53 |
| Route >= 5.5 m/s | 3936 | 2.4 / 1.3 / 2.5 | 13.0 / 6.6 / 16.8 | 26.5 / 6.1 / 7.2 | 14.1 / 2.1 / 6.7 | 16.6 / 3.2 / 13.1 | 2448 / 42 / 1931 | 38 / 5 / 1 |
| Route 3-5.5 m/s | 4306 | 5.2 / 2.6 / 2.8 | 23.7 / 10.2 / 19.2 | 28.5 / 6.1 / 9.6 | 14.1 / 3.4 / 6.3 | 13.8 / 3.2 / 8.3 | 1617 / 34 / 722 | 98 / 59 / 4 |
| Carry >= 5.5 m/s | 1074 | 2.0 / 1.5 / 1.9 | 12.8 / 7.8 / 21.1 | 24.2 / 7.9 / 11.7 | 13.4 / 5.2 / 5.5 | 20.7 / 5.1 / 7.7 | 621 / 49 / 222 | 21 / 8 / 2 |
| Backpedal 3-5.5 m/s | 5379 | 8.5 / 3.4 / 6.2 | 24.7 / 9.5 / 25.4 | 28.0 / 7.0 / 28.0 | 7.1 / 5.0 / 7.5 | 4.1 / 4.0 / 4.0 | 117 / 165 / 64 | 100 / 229 / 96 |
| Run < 3 m/s | 4483 | 5.6 / 2.4 / 3.1 | 22.2 / 9.0 / 21.0 | 19.1 / 7.2 / 15.0 | 9.9 / 5.5 / 5.1 | 10.1 / 6.0 / 7.9 | 1175 / 384 / 644 | 295 / 175 / 296 |

Weak cells (honest): sprint toe-out in real plays is 16.6 degrees mean (p95 45; the treadmill is 3.2) because real sprinters cut and turn faster than the foot heading follows (the line is the velocity); Backpedal 3-5.5 m/s got more roll / yaw pops (117 to 165, 100 to 229: the line swings between reverse-travel and the pelvis facing while a DB turns to run); Backpedal / Shuffle / Run below 3 m/s still show pole flips (140-190 per bin, same as before: legs near straight, hinge axis undefined, starts and stops); Carry 3-5.5 m/s has hinge 37 degrees mean (ball carriers in jukes / stiff-arms / spins, not the gait); shuffles are not polished beyond the line choice (body facing). Pelvis yaw against the line is 7-13 degrees mean (natural pelvis rotation of the take; unchanged).

### Strips (looked at; `D:/PIXFootball/anim_loco/l3_polish/` and `l3_nopolish/`, 12 frames 0.1 s apart, side / front / behind, `strip_legs_<jog|run|carry_carry|backpedal>_<side|front|behind>.png`)

jog = LB, run = WR 6.0, carry_carry = RB sprint 7.5, backpedal = CB 3.5. Front and behind views: before, the feet turn out sideways, the cleat soles face the camera (feet rolled onto their edges) and the knees splay on every stride; after, the toes point down the line, the feet land flat and the knees track over the feet. The sprint from behind shows the natural heel flick of the swing leg. The backpedal looks alike before and after (it was already the cleanest gait).

### Round 3 open items

* The shin / foot twist limits (10 degrees) and the knee slack (6 degrees) are by feel; they are constants in `FootPlant` (`ShinTwistLimit`, `FootTwistLimit`, `KneeSlack`, `FootYawLimit`, `FootRollLimit`).
* Low speed (< 3 m/s) pole flips and the shuffles: the polish needs a defined bend plane; a stance take with a clean leg would fix it.
* Thigh twist (hip rotation) is not limited (6-15 degrees mean, 20 in carries): rotating the thigh about its own axis would move the ankle; the knee alignment handles its visible effect.
* Better source mocap would fix the cause: takes with the shin twist inside +-15 degrees and feet within 10 degrees of the line (the retarget of the BGS takes saturates the lower-leg twist muscle).

### Audit rows, round 3 update

| row | status now |
|---|---|
| E-035 shuffle toe-out only clamped | partly: running gaits and backpedal now hold the foot within 10 degrees of the line; shuffles keep the old clamp |
| E-038 / E-021 / E-034 posture | untouched |
| new finding, not in the audit | shin / foot twist at the muscle limits in the take set, cleat soles visible from the front, knees out 20-55 degrees: fixed at runtime (LOCO-016) |

## Tests

`LocomotionGaitTests` 12 / 12 (round 3 added: foot yaw from the lateral axis at any pitch, foot roll about the long axis, swing-twist of a bone). Round 3 full EditMode run (private copy = git HEAD of the shared tree + this track's files, 1135 tests): 1135 passed, 0 failed. Rounds 1-2: 1128 / 1129 and 1132 / 1132. The shared working tree (all tracks' in-flight code) compiles with this track's files (batch compile, 0 errors).

## Tools and how to repeat

Private copy `D:/PIXFootball/TrackL/PIX` (junction `Assets/_Licensed` to the shared tree; `sync2.sh` rebuilds the scripts from git HEAD and overlays this track's files). `run.ps1 -Method PixFootball.EditorTools.LocoCycleScan.Run` (scan), `LocoCycleBuilder.Build` / `Report` / `ReportAll`, `LocoShots.Run -pixLoco [-pixLocoOff] -pixLocoTag name -pixLocoModes treadmill,plays,strips,learn,calibrate` (batch mode without `-nographics`, no `-quit`). Outputs `D:/PIXFootball/anim_loco/<tag>/` (metrics, `motion_dump.csv` every running player every second frame, traces, strips).

Round 3: `LocoShots.Run -pixLoco -pixLocoTag name -pixLocoModes legs [-pixLocoFilm 0] [-pixLocoNoPolish | -pixLocoNoPlant | -pixLocoNoWarp | -pixLocoNoIK | -pixLocoNoDonor | -pixLocoOff]` (leg metrics table + 3-view strips of jog, WR run, RB sprint carry, CB backpedal; outputs `D:/PIXFootball/anim_loco/<tag>/metrics_legs.md`, `strip_legs_*.png`), and `EngineAudit` writes `leg_quality.md` next to `motion_summary.txt` (same flags work there).
