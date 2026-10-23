# v0.10.0 track QB: the quarterback sequence (playtest note 4) and the kicks (note 7)

Work was done in a private Unity copy (`D:\PIXFootball\Work\QB\PIX`, synced from the shared tree), nothing committed. Every number below comes from `Editor/Tools/QbChoreoAudit.cs` (flag `-pixQbChoreo`): it plays real plays through `PlayStage` at 60 Hz with the huddle break on, logs the quarterback / kicker frame by frame, and writes frame strips.
Raw output: `D:\PIXFootball\anim_qb\v0100\before\` (the untouched v0.9.0 code: 4 plays + 3 kicks), `...\after\` (this build: 10 plays + 3 kicks; `qb_summary.txt`, `kick_report.txt`, `qb_frames_*.csv`, `kick_frames_*.csv`, `strips\*.png`). The runs in `r1`..`r7`, `final*`, `k_noik*` are the iterations in between.

## How it works (decided by the simulation before it happens, not physics)

The quarterback is a sequence of explicit segments. The simulation fixes the duration of each one in a shared table (`Sim/QbSeqTiming.cs`), the body does not travel while a segment that has no travel is on, and the presentation plays the segment pose for exactly that window. The next segment starts only when the current one is over.

| Segment | Who decides | What you see |
|---|---|---|
| Break the huddle, **walk** to a survey spot 2.7 yd behind the centre (shotgun: to his spot) | stage (`PlayStage.QbSeq.cs`) | the real locomotion walk (no pose layer, no stance hold), at 2.5 m/s with a constant-deceleration profile |
| **Survey / point** (1.9 s) | stage | upright, head sweeps the defence left then right, the right arm rises and points at the middle of it, head holds on the point |
| **Step in** behind the centre | stage | a slow walk (1.5 m/s), squared to the line |
| **Crouch** (0.9 s, shotgun 0.55 s) | stage | hips sink, trunk folds to the under-centre stance, feet turn in, hands rise and reach the centre's rear (Reach layer, hands land on the anchor with no pop) |
| Cadence, **snap** | `MatchDirector.Cadence.cs` (unchanged: head swivel, bounce, hips drop on "go") | ball is drawn from the centre's hands into the quarterback's hands so it is in them on the frame the simulation completes the catch |
| **Secure** (0.20 s under centre, 0.10 s shotgun) | `QbSeqTiming`, sim holds the body (`CommitSpeedFrac = 0`), drop clock starts after it | the ball is pulled to the belly, nothing else moves |
| **Drop** (3 / 5 / 7 step, shotgun gather) | sim drop planner + new facing profile | reverse pivot opens the hips 72 degrees (never the back to the field), holds it through the first half, squares back to the line over the second half; feet by the planner: long strides, a high foot arc, the feet never cross, the planted foot turns on the ball of the foot, leg-reach re-step |
| **Set / pocket** | sim faces him north (`QbSetFacing`) | chest faces north, ball high at the chest in both hands, base just wider than the shoulders, light alternating heel bounce, head follows the read in progression order at a limited turn rate (520 deg/s) |
| **Pocket to scramble** | `QbSeqWatch`: 0.34 s transition (speed held to 30 then 62 percent) | ball up to the ear, trunk into the run, planner drive step, then the locomotion takes over |
| Pocket to throw (set feet) | existing keyed throw pose, now on the footfall planner | the front foot's stride is a planned step to the target (it used to snap to the pose) |
| Kicks | see below | |

Not done as clips: the quarterback is built on the procedural pose layer (v0.9.0 QBSTANCE) because the Mixamo QB takes (`FB_Quarterback_Pass`, `FB_Football_Hike*`) are one-off long takes with no foot-phase tags for the pocket; the segments above are key poses with solved feet, which is what lets the simulation plan around them exactly. `ClipSelect` (ANIMLIB) and the Mixamo import (MOTION) were not needed and nothing was imported twice.

## Playtest note 4: QB sequence (before -> after, from the audit)

| Measure | Before (v0.9.0) | After | Target |
|---|---|---|---|
| Snap to hands, under centre: first frame the ball is within 3.5 cm of its hold, vs the simulated catch frame | +6 frames late, ball 12.8 cm from the hand at the catch | **-2 frames** (ball in the hands 33 ms early), 8.7 cm at the catch | 0 +-1 |
| Snap to hands, shotgun | +8 frames late, **101 cm** from the hand at the catch | **+1 frame**, 26 cm (hands extended; the ball rides in them while they bring it in) | 0 +-1 |
| Chest yaw vs north, **pocket** (Set), under centre | mean **100** deg (median 131: his back to the field) | mean **3.2** deg (max 4.6) | <= 15 |
| Chest yaw vs north, pocket, shotgun | mean 13.8, p95 43 | mean **3.0**, p95 3.7 | <= 15 |
| Chest yaw vs north, **drop**, under centre (5-step) | mean 100, median 131, hips 168 deg | mean **18.7**, median **9.7**, p95 40.7, hips yaw peak 72 | (pocket target) |
| Head vs the read he is on (pocket) | mean 37 deg, 65 deg on play action | **2.2 deg** (shotgun 2.6) | follows the progression |
| Planted-foot contacts of 6+ frames during drop/pocket (core frames, toe bone), mean slide | (v0.9.0 planner, same metric measured mid-way: 1.2 to 1.5 m/s) | 5-step **0.053**, 3-step **0.049**, 7-step **0.044**, shotgun 0.064 m/s | <= 0.10 |
| Old frame-based planted-foot slide (includes swing edges), 5-step drop, mean / p95 | 0.764 / 5.67 m/s | 0.261 / 1.65 | |
| Drop footfalls, 5-step drop (planner landings) | 11 | 8 (3-step 5, 7-step 11) | about N |
| Frames with the sim moving > 0.8 m/s while a survey / crouch / secure segment is on | n/a | **0** in all 10 plays | 0 |
| Pre-snap: huddle break to ready | 5.75 s | 7.6 s (shotgun 5.75 s) | |

Other things measured: ball to hand through the drop 9.4 cm mean (max 11.8), through the pocket 10.4 cm (the hold distance). The snap in the shotgun lands the ball in the extended hands, so the nearest hand is 26 cm away on the catch frame for one frame and 11 cm six frames later.

## Playtest note 7: kickoffs (and punts, field goals) out of sync with the ball

The kick is one event: the sim fixes the contact time, the take is planned to land its foot contact there, the stage tells the kicker's view where the ball will be, and a short IK pulse bends the foot's path through the ball. Measured on the right foot's instep (ankle to toe, 55 percent along), swept between frames, minus the ball radius plus the foot (0.145 m); contact = best frame pair; launch = the first frame the displayed ball moves faster than 12 m/s.

| Kick | Launch minus contact (frames) before -> after | Foot to ball gap before -> after | Target |
|---|---|---|---|
| Kickoff | 0 -> **+1** | **8.9 cm -> 0.6 cm** | 0 +-1 frame, <= 5 cm |
| Punt (drop to foot contact) | 2 -> **+1** | 11.4 cm -> **-3.5 cm** | same |
| Field goal | 1 -> **+1** | 55 cm -> **17 cm** (not met) | same |

What changed: kickoff plant 0.24 yd further left and a lower tee (0.30 -> 0.24 yd); punt: contact point 0.30 yd to the right of the body line and the take planned 0.042 s later (the table's contact moment was 3 frames late); field goal: the holder's spot was never used (the code tested the wrong slot, so the ball stayed in his hands at 0.5 m), now the holder id is exposed and the ball sits on the turf at the spot, the kicker starts closer, plants nearer the ball and arrives at the plant spot on time; all three: the kick IK (`SimAgentView.Kick.cs`) corrects the miss ACROSS the swing only (the along-swing timing stays the clip's), smoothed to 4 m/s extra, only while the swing is fast, so the leg is never dragged.

## What still looks wrong (looked at the strips once)

* Field goal: the swing still passes about 17 cm from the ball (the take's leg is nearly straight at contact, so the IK cannot lower the foot enough; the kicker also stands close to the holder). Fixing it needs a take that kicks a ball at ground height.
* Under-centre drop: 8 landings for a 5-step (the sim's drop is 7 yd in 1.75 s; a 5-landing drop would need strides the legs cannot reach at that hip height). The strides are long, the feet no longer cross or scuff, but a coach would count more steps than the playcall says.
* Roll-out and boot (`Rolling`) are still the locomotion tree with the ball at the ear (contact slide 1.6 m/s: the locomotion track's); the scramble start is a transition for pocket scrambles only.
* Hand-off and play-action fake keep the mocap pair take / the chest carry (secure and the hold work, the pivot toward the runner is not choreographed).
* The pocket "feet bounce" is subtle (alternating 3 cm heel raise at 1.9 Hz); the walk to the survey spot is the locomotion tree and inherits MOTION's foot work.
* The shotgun snap: the hands are extended at the catch frame (26 cm from the ball for one frame).
* The audit's "user presses snap" is the cadence window plus the stage's `BreakInProgress`; a press during the walk now hurries the rest (`QbSeqHurry`), it does not skip it.

## Files touched

New: `Gameplay/Match/PlayStage.QbSeq.cs`, `PlayStage.Kick.cs`; `Gameplay/Player/SimAgentView.QbSeq.cs`, `SimAgentView.Kick.cs`, `StancePoses.QbSeq.cs`; `Sim/QbSeqTiming.cs`; `Editor/Tools/QbChoreoAudit.cs`; `Tests/EditMode/Gameplay/QbSequenceTests.cs` (+ .meta files).
Owned files edited: `SimAgentView.Qb.cs` (walk branch, secure, scramble start, tracking), `ProcGait.cs` (seed, step scale, reach re-step, pivot on the ball of the foot, shuffle, no crossing, high arc, lift before travel), `AnimKit.Qb.cs` (kick eta bias), `Sim/OffenseAI.QbMotion.cs` (drop facing, set facing north, secure / scramble watch, rolling facing), `Sim/KickGeometry.cs`, `Sim/SpecialTeamsAI.cs` (plants, start, contact point), `Sim/PlaySimulation.QbHooks.cs` (holder id), `PlayStage.Stance.cs` (kick target hook, shotgun catch reach).
One-line hooks in other owners' files: `SimAgentView.Stance.cs` (`QbSeqPose`, `QbSeqDyn`, `gait = ... || QbThrowGait`), `SimAgentView.cs` (`KickIkLate`), `PlayStage.Connect.cs` (`QbSeqBegin`, `QbSeqStep`, punt contact aside, FG holder spot), `PlayStage.cs` (`QbSnapBall`), `OffenseAI.Qb.cs` (`QbSeqWatch` x2), `MatchDirector.cs` (`QbSeqHurry`).

## Tests

One full EditMode run (`-testCategory "!Slow"`, private copy synced from the shared tree, 1243 tests): 1236 passed, 7 failed. Three of the seven were this track's old assertions that encode the v0.9.0 behaviour and were updated (`ConnectTests.KickGeometryIsConsistent`: tee 0.24 yd; `QbStanceSimTests.FieldGoalKickerStartsLeftRearOfTheHolder`: the kicker now starts 0.9 yd left / 1.45 yd back; `QbStanceSimTests.UnderCenterDropTurns...`: he opens 60+ degrees, never past his side); the QB-related classes (`QbStanceTests`, `QbStanceSimTests`, `ConnectTests`, `StanceCameraTests`, the new `QbSequenceTests`) were re-run after the edit: 54 of 54 pass.
The other four are not this track's: `ChoreoTests` (2, CHOREO, in flux), `BodiesTests.ArmIk...` (BODIES, in flux), and `SimAbilityLineTests.SniperBlitzArrivesSooner` (a 600-play statistic, delta -0.026 s against a -0.03 s threshold, z -1.8): the 0.2 s secure and the new drop facing move every blitzer-reaches-the-quarterback time a little, so this borderline statistic crossed its line; the DEFENSE owner may want to loosen it to -0.02 or re-baseline it.

## Second pass (coordinator follow-up: close the remaining gaps)

Audit: `D:\PIXFootball\anim_qb\v0100\final6\` (60 Hz, real PlayStage; `qb_summary.txt`, `kick_report.txt`, frame CSVs). Before = first-pass numbers above.

| Item | Before | After | Status |
|---|---|---|---|
| 1. FG instep-to-ball swept gap | 17 cm | **-5.1 cm** (ball inside the foot path, within 5 cm), launch - contact +1 frame | met |
| 1b. Kickoff / punt gap | 2.3 cm / -3.5 cm | 2.2 cm / -4.8 cm, +1 frame both | met |
| 2. Under-centre snap to hands | -2 frames | **-1 frame** (ball 2.8 cm from the hand at the catch); shotgun -1 (7 on the hand-off scenario's early-hands frame) | met (UC) |
| 3. Roll-out contact slide | 1.6 m/s | **0.16 m/s** mean (p50 0), boot 0.25, ball high, shoulders turned | roll-out met; boot 0.25 |
| 3b. Scramble running slide | 1.6 m/s | 1.1 m/s mean, p50 0.29 (outliers on starts and cuts) | not met (matcher / FootPlant, MOTION's) |
| 4. Shotgun hand-off hands to runner's belly | not choreographed | **0.22 m** at the mesh frame (closest 0.18); eyes to the field after | met |
| 4b. Under-centre hand-off hands to belly | not choreographed | 0.47 m (open step, mesh pull, carry-out and look-off work; the reach is limited by the 125 degree facing clamp / arm reach) | partial |
| 4c. Play-action fake drop slide | n/a | 0.36 m/s mean (p50 0.20) | not met |
| 5. Pocket bounce | 3 cm alternating heel raise, subtle | 5.9-10 cm peak heel lift per foot, planted-foot slide 0.00-0.08 m/s | visible, no slide (one foot in uc5) |
| 6. Footfalls vs planned drop | 8 landings for a 5-step | 3-step: 3, 5-step: 4, 7-step: 6 (within 1) | met |
| Drop contact slide | - | 0.03-0.08 m/s mean | met |

What changed: ProcGait gained a reach-only mode (stride limited to what the legs reach at the dropping hip height, swing clamp 1.12 x reach, first step forced), so the planned step count matches landings; FG holder spot, kicker start and plant moved and the kick IK corrects the miss over a longer window; mesh/carry-out/fake branches in `QbSeqDyn` (reach ramps from the sim's `QbMeshEta`, shoulders squared to the runner, look to the field after); `QbViewAction` stops the mocap hand-off pair playing over a boot or drop after a fake; roll-out and boot keep the QB in the QbPoseFlow branch with the ball high.

Still wrong: under-centre hand-off reach (0.47 m), play-action drop slide, scramble-running slide (matcher's). Stale test assertions (punt tee and per-kind eta bias) updated to the current geometry.

Tests: QbStanceTests, QbStanceSimTests, ConnectTests, StanceCameraTests, QbSequenceTests: 54/54 pass. `SimAbilityLineTests.SniperBlitzArrivesSooner` left to integration.

## Third pass (the two QB-owned items left)

Audit: `D:\PIXFootball\anim_qb\v0100\final7\` (`qb_summary.txt`, `kick_report.txt`).

| Item | Before | After | Status |
|---|---|---|---|
| Under-centre hand-off, hands to runner's belly at the exchange frame | 0.47 m | **0.24 m** (closest 0.16 m) | met (<= 0.25) |
| Shotgun hand-off (same code path) | 0.22 m | 0.15 m | better |
| Play-action fake drop, planted-foot contact slide (mean) | 0.36 m/s | **0.062 m/s** (p50 0.009; 1 of 5 contacts above 0.10) | met (<= 0.10) |
| Play-action landings for a planned 5-step | 6 | 5 | met |
| Regression: 3-step / 5-step / 7-step drop slide (mean) | 0.008 / 0.076 / 0.029 | 0.001 / 0.066 / 0.062 | no regression |
| Regression: drop landings 3 / 5 / 7 | 3 / 4 / 6 | 3 / 4 / 6 | unchanged |
| Kickoff / FG / punt gap | 2.2 / -5.1 / -4.8 cm | same | unchanged |

Cause of the hand-off miss: it was not the facing clamp (raising it from 125 to 172 degrees left the hands at 0.52 m). The back's pocket marker led him by 0.12 s of his velocity (about 0.5 m ahead of his belly at a 5 m/s closing speed) and the mesh pull moved the quarterback only 0.55 m, so the hands arrived at a point the runner had not reached yet. Fix: pocket lead 0.12 s -> 0.02 s, mesh pull up to 0.80 m (minimum body separation 0.42 m), mesh facing clamp 125 -> 150 degrees (the runner is directly behind an under-centre quarterback: a reverse pivot).

Cause of the play-action slide: `ProcGait` sent the right foot again every time both feet were over reach (the 9.0 cap tied, the right foot won) while the left stayed 2 m behind; the swing's landing spot also fell behind the body when it accelerated, so the clamp dragged the descending foot. Fix (all in `ProcGait`, reach-only drops): the foot that trails farther steps first; the landing spot rides with the predicted body for the first 35 percent of the stride and is then fixed; a foot in the air turns with the body (lands square); a planted foot pivots only past a 25 degree slack.

Still open (not QB-owned): scramble running slide 1.1-1.5 m/s (MOTION), boot 0.24 m/s. Tests: QbStanceTests, QbStanceSimTests, ConnectTests, StanceCameraTests, QbSequenceTests: 54/54 pass.
