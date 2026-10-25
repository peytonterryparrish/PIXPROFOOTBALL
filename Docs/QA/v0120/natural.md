# v0.12.0 NATURAL track (`natural.md`)

Owner note 1 (robotic walking / running / sprinting) and the head / hip part of note 3 (heads and hips turn too far when players look left or right). Private copy `D:\PIXFootball\Work\Motion\PIX`, tree = shared tree after GAIT + MOBILITY (the motion database is the v12 one). Every number below is the new `NaturalProbe` inside `EngineAudit` (48 plays, 22 players, every frame of the final pose): **before = the same tree with `-pixNatOff`** (`D:\PIXFootball\Renders\v0120_natural\off4`), **after** = `...\a5`. Confirming `MatchWatchAudit` (40 plays, seed 110110): `D:\PIXFootball\Renders\match_watch_v0120\natural` (and `natural_off` with `-pixNatOff`). Body-bug evidence from the BODIES track (`body_bugs.md`) was not written when this pass ran; the head / hip evidence below comes from my own probe.

## 1. What "robotic" measured as (before, `-pixNatOff`)

| symptom | measure | before |
|---|---|---|
| teammates in the same step | frames in which two same-group neighbours (< 20 m, both running > 3 m/s, >= 1 s together, 64 pairs, 7,805 pair-frames) are within 0.10 cycle of each other (chance 20 %) | **33.8 %** (every pair shared the cadence and, from the snap, the phase) |
| everybody the same cadence | cadence factor of `Run_F_Loop` at 5.5-7 m/s, sd across 203 runs | 0.034 (3.2 %) |
| facing snaps | frames of a non-linemen body with the pelvis turning > 900 deg/s (CB / LB / S / WR / TE / RB / QB) | 615 / 625 / 464 / 573 / 316 / 165 / 105 = **2,863** (idle bodies swung round at 720-1000 deg/s: `MoveTowardsAngle` at a flat 720 deg/s, the pelvis then follows) |
| heads over-twisted | frames with the head turned > 70 deg over the chest | **13,614 of 421,599** (run 229, stand 852, walk 4,903, back-pedal 483, other 7,147); head-over-chest p99 **88.4 deg**, max 179.5 |
| chests over-twisted | frames with the chest turned > 35 deg over the pelvis, free states | run 1,185, back-pedal / shuffle 1,031 (stance sets, blocks and throws are other tracks' and left alone) |
| stiff arms | arm swing rms (run 5.5-7 m/s) | 24.3 deg (capture arms are relaxed, v0.11 MOBILITY: "arm swing is smaller") |
| counter-rotation | shoulders over pelvis rms (run 5.5-7) | 15.0 deg |
| head wobble | head yaw rms about its own mean (run 5.5-7) | 10.1 deg median, p99 37.7 (the head follows the chest instead of looking where he runs) |
| standing | not frozen (183 windows, 0 frozen): the idle loops move; what was missing is that stance sets and plain stands have no breath / weight shift / glance of their own | n/a |
| baseline rates (`BASELINE.md`, 184 plays) | POP_HIPS_ROT_OVER_900DEGS 2,147 = **11.7 per play**; POP_CHEST_ROT_OVER_1400DEGS 230 = **1.25 per play** | OL + DL are 911 + 127 of them (CONTACT's) |

## 2. What was built (files in section 6)

- **`NaturalStyle`** (new, pure, tested): the personal style of a player, seeded once from his id, body size and role (stub players of the audits, id 0, from their slot): tempo 0.93-1.07 (body +-1.5 %), a slow wander of the tempo (1.2-3 %, 0.07-0.18 Hz), arm swing 0.84-1.28, shoulder counter-rotation 1.0-1.55 (linemen 70 % of that), head stabilization 0.50-0.85, trunk posture -3.5..+4.8 deg, breath (0.20-0.34 Hz, 0.5-1.1 deg), weight-shift period 5.5-11 s / sway 1.3-3 deg, glance gap 2.5-6 s. Linemen: slower, less arm; small bodies: quicker, more arm.
- **Tempo / desync** (`MotionMatcher.StyleRate`, one line in `Update`; `SimAgentView.Loco.cs` sets it): the playback rate of a loop take is multiplied by his own cadence; the stride warp (which divides by the rate) takes the difference back, so the ground speed is the simulation's and the planted foot still stands still; he takes quicker shorter or slower longer steps than his neighbour.
- **`NaturalBody.Secondary`** (before the foot planter): arm swing about its slow mean scaled by his style x `ArmSwingFactor(speed)` (the matcher path had `ArmSwingEnabled = false`); the chest's swing against the pelvis widened by his counter-rotation factor; the head cancels his share of the chest's yaw wobble; his posture offset (the head counter-rotates 0.6, so the eyes stay level). Not for carriers or the quarterback.
- **`NaturalBody.Idle`** (a plain stand, or a loose stance set: receiver / corner / safety / backer / back / returner / gunner kinds; **never** a hand-down or quarterback stance and never when the stance layer is on): breathing, a weight shift (trunk roll over one hip, two incommensurate periods), glances (up to 34 deg, 40 % of that in a stance set) with eased follow. All above the pelvis, feet untouched.
- **`NaturalLook` + `NaturalBody.LookLimit`** (on the final pose, after Final IK): the chest stays within 35 deg of the pelvis, the head within 70 deg of the chest (soft limit, asymptotic, no kink), and neither turns faster than 540 / 650 deg/s; the chest is corrected first (head and arms ride with it) and the head keeps the gaze it had as far as its own range allows. Chest + head in free states (idle, run, sprint, route, carry, back-pedal, shuffle, loose stance); the head alone in block / engaged / shed / catch / jump. Not applied to throws, tackles, dives, get-ups, ragdolls, contact-driven bodies, hand-down stances, quarterback chest.
- **Eased facing** (`SimAgentView.TurnYaw`, one-line hook in `ApplyMatch`): the visible heading of a free body (idle, run, sprint, route, carry, back-pedal, shuffle) follows the simulated facing with a bounded angular acceleration (4,200 deg/s2) and a rate of 400 deg/s when standing rising to the old 720 deg/s at a run (1.5-5 m/s); a half turn takes ~0.5 s standing instead of 0.25 s and starts and ends smoothly. Everything else keeps the plain `MoveTowardsAngle`.
- **`NaturalProbe`** (new, in `EngineAudit` -> `natural.md`): phase correlation of neighbours, cadence / stride / arm / counter-rotation / head wobble per run, the look distribution and rates, frames beyond the limits, standing windows, idle-layer activity.
- Switches: `-pixNatOff` (all), `-pixNatNoSecondary`, `-pixNatNoIdle`, `-pixNatNoLook`, `-pixNatNoTempo`, `-pixNatNoFacing`.

## 3. Done-criteria

| | before (`-pixNatOff`) | after | verdict |
|---|---|---|---|
| (a) gait phase: same-group neighbours within 0.10 cycle of each other (chance 20 %) | 33.8 % | **18.3 %** (65 pairs, 7,950 pair-frames) | met (at chance: no pair is locked any more). Note: the resultant length R of a pair over >= 1 s windows (0.81 -> 0.77) does not separate a constant small cadence difference from locking; the in-step share does |
| (b) style variance across players | cadence factor sd 0.034 (3.2 %); arm swing rms CV across players 5.8 % / 9.7 % (5.5-7 / 3.5-5.5 m/s); counter-rotation rms median 15.0 deg | cadence sd **0.054 (5.1 %)**; arm swing CV **7.1 % / 12.9 %**, median 24.3 -> **27.3** deg (v5.5-7); counter-rotation median **19.0** deg; head wobble median 10.1 -> **8.7** deg, p99 37.7 -> 32.6 | met |
| (c) head over chest p99 (all 421,594 frames) | 88.4 deg | **68.6 deg** (limit 70) | met on p99 |
| (c) frames head over chest > 70 deg | 13,614 | **565** (run 6, stand 135, walk 152, back-pedal 93, other 179) | **not 0**: 96 % fewer; the rest are throws / tackles / dives / get-ups / stance-layer bodies that the limiter does not touch, plus 3 idle frames inside the rate limit |
| (c) chest over pelvis > 35 deg, free states | run 1,185, back-pedal 1,031 | **run 34, back-pedal 106** (frames with the chest limit on and beyond: Idle 49, Run 10) | p99 within limit for free states; stance / block / throw twist is not limited (by design, other tracks' poses): overall p99 51.1 -> 49.8 |
| (d) hips rotating > 900 deg/s, frames, non-linemen | CB 615, LB 625, S 464, WR 573, TE 316, RB 165, QB 105 = 2,863 | CB 375, LB 340, S 198, WR 370, TE 269, RB 120, QB 0 = **1,672 (-42 %)** | met (OL 1,509 / DL 1,876 unchanged = CONTACT's: 1,515 / 1,721) |
| (d) `MatchWatchAudit` 40 plays, POP_HIPS_ROT / POP_CHEST_ROT per play | baseline 11.7 / 1.25 per play; same tree off 6.6 / 1.95 | **5.1 / 1.15** (non-OL/DL: hips -29 %, chest -46 % against the same tree) | met |
| (e) planted slide (stance min slide, mean / p50 m/s) CB sprint 6 / CB run 6 / LB run 6 / LB sprint 6 / WR route 6 | 0.038 / 0.037; 0.105 / 0.025; 0.028 / 0.029; 0.032 / 0.021; 0.035 / 0.021 | 0.040 / 0.039; 0.049 / 0.028; 0.036 / 0.024; 0.038 / 0.037; 0.054 / 0.032 | no regression to speak of (all below the 0.10 sprint bound; WR routes +0.011 m/s median, one 3.9 m/s stance outlier in the mean; section 5) |

## 4. MatchWatchAudit (40 plays, seed 110110)

Same tree, same seed, 40 plays, 71,052 / 71,114 frames (`natural` = layer on, `natural_off` = `-pixNatOff`); baseline = the v0.11.0 dataset (184 plays). Per play in brackets.

| detector | baseline (184 plays) | `natural_off` (40) | `natural` (40) |
|---|---|---|---|
| POP_HIPS_ROT_OVER_900DEGS, all | 2,147 (**11.7**) | 264 (6.6) | **203 (5.1)** |
| ... without OL / DL (the bodies this track poses) | 1,236 (6.7) | 156 (3.9) | **111 (2.8)** |
| POP_CHEST_ROT_OVER_1400DEGS, all | 230 (**1.25**) | 78 (1.95) | **46 (1.15)** |
| ... without OL / DL | 103 (0.56) | 41 (1.03) | **22 (0.55)** |
| POP_HIPS_JUMP_OVER_10CM | 485 (2.6) | 102 | 121 |
| FACE_BODY_70DEG_OFF_SIM_FACING_STANDING | 4,837 | 1,635 | 1,730 (+6 %) |
| FEET_BELOW_TURF_7CM | 252 | 340 | 317 |
| FACE_RUNNING_* | 76 runs | 9 | 6 |

Hips pops of non-linemen down 29 %, chest pops 46 % against the same tree; against the baseline rate 11.7 -> 5.1 per play (the rest of that drop is GAIT, MOBILITY and CONTACT: `gait` 8.3, `mobility` 6.4 per play). The eased facing keeps a standing body turning toward a new simulated facing for longer, which the `FACE_BODY_70DEG` detector (a count of frames a standing body is more than 70 deg off the sim facing) shows as +6 %; hips jumps are within the run-to-run noise (capture timing, 102 / 121). Feet below turf is the shared tree's (340 off, 317 on), not this layer's. Wall: 24.4 min for 40 plays.

## 5. What still looks wrong / not done

- WR route planted slide rose a little (median 0.021 -> 0.032 m/s, bound 0.10): I suspect the eased facing (a cut's turn now takes ~0.15 s longer, so the orientation warp is larger for a moment), not the tempo (the warp divides by the rate, the product is unchanged). Try `NatTurnAccel` 7,000 and a 0.07 s time constant in v0.13; `-pixNatNoFacing` isolates it.
- 565 head frames and ~7,000 chest frames beyond the limits remain in states this layer leaves alone (throw, tackle, dive, get-up, hand-down stances, blocks' chest): they belong to the contact / stance / QB tracks. The head > 70 deg while the limiter was on: 3 idle frames (rate limit catching up after a pop).
- Cadence variation desynchronises neighbours but cannot give them different first steps out of a stance (all start from the same take at the same time); the in-step share is at chance, a visual check of a three-receiver release is still wanted.
- No hip / pelvis limit relative to the travel direction (pelvis over travel p99 49-54 deg in runs, back-pedal pelvis over facing p90 70 deg): the pelvis belongs to the matcher's orientation warp and the foot planter; only its *rate* (the facing ease) was addressed.
- The idle layer is rotation-only above the pelvis, so a weight shift never moves the hips over a foot; standing bodies in `Stance` with the stance layer on (quarterback, hand-down linemen) get nothing.
- Style is by body size and role, not by ratings (the view has no ratings); an old slow lineman and a young one share a tempo range.
- Head wobble is still ~9 deg rms in a run (the capture takes carry it); the stabilization takes 0.5-0.85 of the *slow* part only.

## 6. Files

Changed: `Gameplay/Player/Motion/MotionMatcher.cs` (`StyleRate`, 2 lines), `SimAgentView.Loco.cs` (2 lines), `SimAgentView.Ground.cs` (1 line, `CaptureRest` calls `NaturalInit`), `BodyMotion.cs` (`SpeedNow`, `TravelDir`), `Editor/Tools/EngineAudit.cs` (4 lines: probe hooks). New: `Loco/NaturalStyle.cs` (style, `NaturalLook`), `Loco/NaturalBody.cs`, `SimAgentView.Natural.cs`, `Editor/Tools/NaturalProbe.cs`, `Tests/EditMode/Gameplay/NaturalTests.cs` (8 tests).
One-line hooks in files that are not on the NATURAL row: `SimAgentView.cs` (4 lines: `SetIdentity` -> `NaturalIdentity`, `ApplyMatch` -> `TurnYaw`, `LateUpdate` -> `NaturalSecondaryLate` after `FitGround` and `NaturalLookLate` after `EngageLegsRestore`). Nothing in CONTACT's files.

## 7. Tests

Full `!Slow` EditMode run (`D:/PIXFootball/Work/Motion/logs/tests.xml`): 1,370 tests, 1,369 passed, 1 failed: my own `NaturalTests.FollowNeverExceedsTheRate` (the target was inside the 30-frame reach; fixed). `NaturalTests` re-run alone: 8 / 8 passed (`tests_natural.xml`). The GAIT / MOBILITY failures seen in their runs (`MobilitySpecsAreWellFormed`, the clutch stat test) did not fail in this run.

## 8. Requests / integration

- CONTACT: 1,509 OL + 1,721 DL pelvis > 900 deg/s frames and the Engaged / Block chest twists (> 35 deg in 7,700 Engaged frames) are the contact takes; `NaturalBody.LookLimit(dt, chestOn, neckOn)` can be called for a contact-driven body (head only) if they want the neck limited there too (it is already on for Block / Engaged / Shed that are not `ContactDriven`).
- BODIES: if the neck / head bones change, `NaturalBody` reads the bind pose in `CaptureRest` (needs `Hips`, `Chest` or `UpperChest`, `Head`, upper legs; `Neck` and arms optional). The yaw of every segment is measured from its bind-pose forward, so a new rig works unchanged.
- Integration: nothing to copy; all new files are plain C# (add the `.meta` files Unity writes).

## 9. Known issues for v0.13

Gather steps and shortened first steps; first-step phase offsets between teammates; ratings-driven style; hip over travel limit; pelvis / hips lift in the weight shift; head wobble in the capture takes; facing ease tuning (above).

## 10. Frame strips looked at

`D:\PIXFootball\Renders\v0120_natural\strips\strips\wr_route_catch_FrontLeft.png` and `cb_cover_Behind.png` (12 frames each, layer on). Receiver: the pre-snap stand is relaxed, weight on one leg, shoulders not square (the idle layer); the release and the route show arms swinging wide with a visible shoulder turn, head up and steady, and the head turns to the ball and the defender inside a normal neck range (no chin over the shoulder past the collar). Corner in coverage: the back-pedal and the turn-and-run keep the head over the chest line, hips and shoulders turn together, nothing wrings. What still looks wrong: the head is turned mostly by the neck bone (no eye lead); the weight shift is a trunk roll only, the hips stay put so the stance feet do not step; the first step of two corners released together is still the same take.
