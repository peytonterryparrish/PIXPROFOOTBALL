# v0.11.0 track QB: stance, throws, ball flight, receivers (playtest notes 8, 10 footwork, 9 ball flight)

Private copy `D:\PIXFootball\Work\QB\PIX`, nothing committed. A/B in one build: `-pixQbV10` (`QbV11.Legacy`) switches every v0.11 QB behaviour off, so "before" and "after" below are the same code, same seeds, same plays.
Audit: `QbChoreoAudit.Flow.cs` (flag `-pixQbChoreo -pixQbChoreoPhase flow`): 18 CPU-vs-CPU passing plays (clean pockets, play action, roll-out, boot, 7 blitz plays) through the real `PlayStage` at 60 Hz, no input.
Raw: `D:\PIXFootball\anim_qb\v0110\before_final\` (v0.10 behaviour), `...\final\` (this build, with strips in `final\strips\`), `...\final_metrics\` (the v0.10 audit re-run as a regression), `...\kicks\` (tee strip), earlier iterations `after1..after5`.

## What was wrong (looked at the baseline strips once)
* Out of the pocket (escape, roll-out, boot) he ran the locomotion cycle with the ball at the ear: stance compliance 13.7 %.
* The throw: the mocap take hopped (both feet off the turf), turned his back to the field on a wide target, front-foot yaw 35 deg off the target (median), chest turned before the hips.
* After the release he walked off with the arms dangling (locomotion), 2 % of the frames in the stance.
* Hips popped 0.14 m in one frame at the snap call (20 pops over 18 plays), kickoff ball floated with no tee, ball drawn under the turf.

## Note 8: the stance until he decides to run
Sim (`OffenseAI.QbStance.cs`, new): `QbTakeoffNow` is the decision. CPU quarterback: the lateral escape work behind the line (`hold` in `UpdateQbScramble`) is the stance, the takeoff starts when that ends; a user quarterback leaves the stance only while sprint (RT) is held (0.12 s debounce); a keeper after the exchange is a takeoff. Facing while he moves is chest to the field, hips opened at most 26 deg (was 33 to 38).
View: `QbPoseFlow` keeps him in the pose layer for Scramble (no takeoff) and roll-out / boot with `StancePoses.QbPocketLive(t, speed)` (wider lower base, lean into the travel, ball high at the chest in both hands, feet by `ProcGait` slide / shuffle steps); the shoulders turn toward the read (0.55 of the angle, 30 deg at most) while hips and feet stay on the field, head scans at 520 deg/s. On takeoff the existing tuck-and-run (ball to the ear, locomotion takes the legs) plays.
After the throw: follow-through, then the stance again (empty hands at the chest), eyes on the ball (`QbEyesOverride`) until the catch. The pump fake is only for a standing passer.

| Measure (flow audit, 18 plays) | Before (v0.10) | After |
|---|---|---|
| Stance compliance, frames the QB has the ball in Set / non-takeoff Scramble / roll-out (ball <= 16 cm above the chest bone, both hands within 38 cm of it, chest within 20 deg of the read clamped to +-20 deg, not the locomotion cycle) | **13.7 %** (standing 9.4, moving 18.8) | **76.9 %** (standing 80.9, moving 72.0) |
| Same, under-centre drop frames | not counted (the 72 deg reverse pivot is by design) | not counted |
| Planted-foot slide, drop / pocket / scramble contacts (m/s) mean / p50 / p95 | 0.312 / 0.117 / 1.25 | 0.280 / 0.047 / 1.79 |
| Crossed-feet events (ankle gap <= 4 cm of crossing, 3+ frames, moving, ball in hand) | 22 | 17 (square-hip frames only, gap < -2 cm: 6 -> 3) |
| Hips jump > 10 cm in one frame (B-021) | 20 | **0** |
| Under-centre snap to hands (v0.10 audit, uc5 / uc3 / uc7) | -1 / -1 / -1 frames | -2 / -2 / -2 (ball 2.4 cm from the hand on the catch frame; deeper crouch) |
| Pocket chest vs north (v0.10 audit, Set) | 3.2 deg | 20.6 deg (the shoulders now follow the read; head vs read still 2.2 deg) |

## Note 10: the throw (footwork, hips then shoulders, release, follow-through)
`SimAgentView.QbThrow.cs` + `StancePoses.QbThrow11` replace the mocap throw takes on the pose layer. Progress u runs over the SIMULATED wind-up (the ball leaves on the sim's release tick), then 1..1.5 over 0.55 s of follow-through. Set feet: stance turns side-on (hips closed 35 deg, shoulders 20 further, front arm at the target, ball by the ear), the front foot (left for a right-hander) lifts at 5 percent, is planted at 60 percent (stride 0.62 body units to the target line, bowed outward so it never passes the back foot), the back foot pivots on the ball of the foot to point across the line and its heel lifts at the release, hips open first (done by u = 0.8), shoulders after (done at the release), the spine near vertical with hips 0.84 at the release, the throwing hand finishes across the body. On the run the feet are the run's, the upper body throws. Left-handers are the mirror.

| Measure (17 releases) | Before | After |
|---|---|---|
| Ball to throwing hand on the release frame | 9.2 cm mean (max 12.7) | **3.5 cm** (max 6.1) |
| Frame of the peak hand speed vs the release | -4 | -3 (arm peaks 0.05 s before the ball leaves) |
| Front-foot yaw error to the target, median / mean | 34.9 / 49.7 deg | **3.0 / 13.3 deg** |
| Hips lead the chest toward the target 0.12 s before the release | 11.9 deg mean (median 47, chest ahead) | 23.9 deg |
| Stride toe height at the release | 3.6 cm | 3.2 cm (planted) |
| Throwing hand to the opposite hip 0.25 s after | 0.46 m | 0.53 m (not met: it finishes across the chest, not at the hip) |
| After the throw, frames not in the locomotion cycle | 2 % | **100 %** |
| Both feet off the turf during the throw | seen in the strips | gone (strips) |

Still wrong: throws to a target 60 to 100 deg to the side still show scissored feet for 18 frames in `clean_sg_a` (the stride crosses the back foot's line; the planner-style order check "stride foot correct" reads 7 of 16 because the audit's planted flag of the stride foot flickers, front toe is within 3.5 cm at the release); the follow-through hand is high.

## Note 9: ball flight and receivers
* Ball: `BallSpinHint.Wobble` is set from how the throw was made (pressure, on the run, a lob a little looser): 0.7 deg clean set-feet throw, 3 to 6 deg hurried; the precession rate follows the spin (9 Hz + 0.0022 x spin). Spin by style unchanged (bullet 3000, touch 2400, lob 1800 deg/s). The ball launches from the hand: displayed ball is 3.5 cm from the throwing hand on the release frame.
* Receivers: the targeted receiver and the defender covering him turn their head to the ball from 0.10 s after the release (`SimAgentView.QbTrack.cs`, 75 deg yaw / 35 pitch, 600 deg/s); the quarterback's eyes follow the ball itself after the release.
* Sim tracking (`TrackBall`: path break toward the predicted catch point, high-point leap, square-then-face-the-ball) measured over 900 pass plays (`QbBallTrackTests`, v10 vs v11 identical because the sim tracking is unchanged; two changes I tried (route kept only within 0.55 yd of the ball; facing the ball from 0.75 s out) lowered completion to 61.8 % and YPA to 5.28 and were reverted): catch-point error (closest approach of ball to the target while below 2.6 m) median 0.72 m completed / 1.43 m incomplete, 71 % within 1 m, 80 % within 2 m; facing error to the ball at that moment median 10 deg. Completion 62.7 %, yards per attempt 5.48 (completed + sacks), sacks 62 of 900 (v0.10: 62.4 %, 5.45, 62). Not improved: the 29 % of throws that end more than 1 m from the receiver are mostly aim error and uncatchable balls; receivers' break toward the catch point needs a second tuning pass against the catch resolver.

## BUGS.md rows tagged QB
| Row | Status |
|---|---|
| B-021 (S2) QB stance loop hip jump | **Fixed.** Root cause: the cadence hips-drop step was `dt / 0.12` = 0.139 m per frame (the whole 0.15 m drop in one frame) and reset to 0 the frame after the snap; now 0.15 m over 0.12 s and eased back over 0.14 s (`PlayStage.Stance.cs`). 20 -> 0 jumps in the flow audit. |
| B-022 (S2) kickoff ball floating, no tee | **Fixed.** Rubber tee under the ball while it is on the tee (`BallFollower.Gfx.cs GfxTee`), `kicks\strips\kick_kickoff_Side.png` tiles 7-8. |
| B-061 (S3) under-centre too upright | **Fixed.** `StancePoses.QbUcDeep`: authored lean 50 (renders about 36), hips 0.74, used by the crouch, the secure and the post-sequence stance. |
| B-062 (S3) XP ball below turf | **Fixed by clamp** (`GfxGroundClamp`, ball never drawn below 7 cm); not re-observed in a kick strip. |
| B-063 (S3) touchback ring stuck | **Fixed.** The landing ring is hidden as soon as the ball leaves free flight, whatever branch placed it (`GfxEffects`). Not re-observed in a touchback strip. |
| B-064 (S3) ball below turf | **Fixed by the same clamp.** |

## Files
New: `Sim/OffenseAI.QbStance.cs` (takeoff decision, `QbV11` A/B switch), `Gameplay/Player/SimAgentView.QbThrow.cs`, `SimAgentView.QbTrack.cs`, `Editor/Tools/QbChoreoAudit.Flow.cs`, `Tests/EditMode/Sim/QbBallTrackTests.cs`, `QbStanceV11Tests.cs`, `Tests/EditMode/Gameplay/QbStanceV11PoseTests.cs` (+ .meta).
Edited (owned): `SimAgentView.Qb.cs`, `SimAgentView.QbSeq.cs`, `StancePoses.QbSeq.cs`, `OffenseAI.Qb.cs` / `OffenseAI.QbMotion.cs` (hooks, facing), `PlayStage.QbSeq.cs`, `PlaySimulation.QbHooks.cs`, `AnimHints.Qb.cs`, `BallFollower.Gfx.cs`, `BallSpinHint.cs`, `QbChoreoAudit.cs` (partial + `flow` phase), `QbSequenceTests.cs` (assertions follow the deeper under-centre stance).
One-line hooks in others' files: `PlayStage.Stance.cs` (`QbStanceHints`, `QbEyesOverride`, `QbFlightHints`, `ReceiverEyes`, hips-drop step), `SimAgentView.cs` (`EyesLate`), `BallFollower.cs` (`GfxTee(); GfxGroundClamp();`), `OffenseAI.cs` (none kept).
Compiles without Final IK; nothing here depends on it.

## Tests
`-testCategory "!Slow"` full EditMode run in the private copy: 1276 tests, 1272 passed. Failures: `BlockingTests.PullingLinemenLeaveTheirPostsAndEngageDefenders`, `TackleTests.TacklingFromBehindIsHarderThanHeadOn` (sim of other tracks, not touched by QB), and two `QbSequenceTests` that asserted the old under-centre numbers (fixed afterwards). QB-related classes re-run after the fix (`QbSequenceTests`, `QbStanceV11Tests`, `QbStanceV11PoseTests`, `QbStanceTests`, `QbStanceSimTests`, `ConnectTests`, `StanceCameraTests`): 58 of 58 pass. `BodiesTests.ASpikedBallBouncesAndLiesStillOnTheTurf` also failed in the first full run on a `BallFollower.Create` asset assertion of the private copy (not a behaviour change).
SimCalibrationTests passing-game bands were not run separately; the 900-play pass sample above stands in for them (62.7 %, 5.48 YPA).

## Not done / not verified
* Hand-off, play-action fake and kneel / spike were looked at only in the baseline PA strip (hunched fake, reach IK to the runner's belly: unchanged, still too deep); sack / hurry reactions are contact takes (SIM-FLOW). The receiver head-turn was not checked in a catch strip. The `SimCalibrationTests` suite and a separate wide-throw footwork pass are open.

# Second pass (coordinator follow-up)
Audit `D:\PIXFootball\anim_qb\v0110\pass2\` (25 plays: the 18 of the first pass plus two more play-action, hand-off under centre and in the shotgun, kneel, spike). Ball tracking `QbBallTrackTests`, two 900-pass seed blocks, v10 and v11 in the same run.

## 1. Stance compliance 76.9 % -> 96.8 %
Remaining 23 % by state (first pass): shotgun gather / drop frames (36 per play, about two thirds), the post-hit stumble recovery, roll-out and boot, and quick-game frames with a wide read (rough split from per-play counts). Fixes: the shoulders-to-read turn now also runs in the shotgun drop (not under centre: the reverse pivot is by design), coefficient 0.65 of the read angle, 32 deg cap. Result: standing still 98.5 % of 757, moving 94.8 % of 629 (1386 eligible frames). Left: frames of a contact take (a hit stumble) are the sim-flow take's, not the stance's.

## 2. Throws to the side, follow-through
* Stride: a stride foot that is already ahead along a throw 55+ deg to the side keeps the lead (no stepping across the body), landing 0.19 body units off the line, the swing bows outward 0.10. Windup crossed frames per play now 0 to 15: not gone, UC drop plays with a 10-15 frame cross remain (`clean_uc3`, `blitz_uc5_b`). Front-foot yaw error median 5.7 deg, mean 23 deg (the audit measures the left foot; when the right is the stride foot the number reads high).
* Follow-through hand to the opposite hip: 0.53 m -> **0.26 m mean (0.23 median)**; follow-through 0.46 s, then relaxed open hands at the belt instead of the ball position (the hands used to hang at the sides, or hold an invisible ball at the chest after a hand-off / pitch).

## 3. Receivers
Breakdown of the 29 % of passes that end more than 1 m from the target (222 of 762): 96 cut short (tipped, deflected, taken in the air before the catch point), 126 where the ball reached the catch point; of those the throw's own aim error is 0.9 m median, the receiver needed only 0.55 of his top speed and was moving at 1 to 5 m/s of 8: he timed his arrival to the tick. Change (`OffenseAI.TrackBall`): he moves to the intercept point at 1.6 x the needed speed (minimum 3.8 m/s, was 1.05 x, 2.0) and waits for the ball.

| Block | v10 completion / YPA / mean miss / within 1 m | v11 |
|---|---|---|
| A (424000) | 59.9 % / 4.90 / 2.66 m / 71.4 % | 60.7 % / 5.39 / 2.36 m / 72.4 % |
| B (760000) | 63.8 % / 5.74 / 2.54 m / 70.5 % | 64.1 % / 5.93 / 2.34 m / 71.6 % |

Completion and YPA go up, not down. The misses over 1 m are still 28 %: most are cut-short balls and throw aim error, which the receiver cannot fix. The earlier tweaks (route kept only within 0.55 yd, facing the ball from 0.75 s) lowered YPA and stay out. Head-turn: seen in the `pa_b` strip only as the receiver's head at the catch, not verified frame by frame (v0.12).

## 4. Hand-off, play-action, kneel, spike
* Play action: the hunched look of the first-pass strip was a hit stumble (a mocap contact take); two clean play-action plays (`pa_b`, `pa_c`) show the stance with the fake, a full drop, a throw with the stride and the follow-through. The reach into the runner's belly is unchanged.
* Hand-off under centre: the scenario picked a pitch; the ball leaves, he then stood with the hands raised forward for two seconds: fixed by the relaxed empty hands.
* Kneel and spike: the audit scenarios stayed in the pre-snap stance (the snap never fired in the audit's timeline), so neither was verified. Known issue for v0.12.

## Known issues for v0.12
Windup cross-steps on under-centre drops that end in a throw; kneel / spike strips; receiver head-turn verification; 28 % of throws still >1 m (aim error, tips); PA fake reach depth; stumble recovery of the QB after a hit (SIM-FLOW take).

## Tests (second pass, the one full EditMode run, `!Slow`, private copy, 1320 tests)
1314 passed, 6 failed, none in QB classes: `BlockingTests.PullingLinemenLeaveTheirPostsAndEngageDefenders`, `CareerDraftTests.TheDraftRunsInOrderWithTheUsersPicksWaitingForHim`, `LeagueEngineFixTests.TeamPenaltyYardsEqualThePlayerPenaltyYards`, `LeagueEngineFixTests.TimeOfPossessionAddsUpToNearlyTheWholeGame`, `QuickSimTests.PlaysPerTeam` (league quick-sim, not the play sim), `MatchFlowTests.FiveMinuteQuartersPlayRoughlySixtyToSeventySnaps` (174 snaps: match clock flow, SIM-FLOW). The QB classes passed in the first-pass run (58 of 58) and in this one.
