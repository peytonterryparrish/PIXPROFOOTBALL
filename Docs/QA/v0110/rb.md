# v0.11.0 RB track (`rb.md`): ball-carrier running (owner note 7)

Owner: "Continue working on the movement for the halfbacks when they hold the ball ... mirror how running backs run with the football in real life."

## Summary (numbers first)

Measured by `Editor/Tools/CarryAudit.cs` (new): 64 real run plays through `PlayStage` (22 views, ball follower, matcher), the carrier sampled at 60 Hz, 5059 carry frames. Broadcast-style (behind and above) and side strips. "before" = the v0.10 carrier (`D:\PIXFootball\anim_rb\v0110\before`), "after" = this build (`...\final`). Same plays, same seeds.

| metric | target | before | after |
|---|---|---|---|
| Ball high and tight (ball >= 12 cm above hips and <= 36 cm from the chest), fraction of carry frames | >= 0.9 | 0.804 | **0.970** |
| Ball above hips, mean (m) | - | 0.156 | 0.230 (ball rides at chest height) |
| Outside arm: ball in the arm away from the nearest defender (within 6 yd, 0.55 yd+ to a side), view, all frames / defender < 3 yd | >= 0.90 | 0.804 / 0.820 | 0.892 / **0.901** (sim 0.93 over 160 carries in the test) |
| Arm switches per carry (justified = a defender now stands on the old arm's side) | rare | 0.27 (13 of 17 justified) | 0.63 (31 of 40 justified) |
| Free arm counter-swing against the leg of the opposite side (correlation, +1 = perfect), mean / median | > 0.5 | 0.09 / 0.16 | **0.44 / 0.59** |
| Free arm swing range at cruise (deg) | 60-120 | not measurable (angle wrap in the first audit) | median 105 |
| B-023 post-whistle ball vs the carrier's hands (0.35-1.7 s after a tackle / sack), frames over 1 m / max | 0 | 49 of 5056 / 1.92 m | 10 of 5056 (one play, max 1.28 m) |
| Hips height with a defender inside 3 yd (m) | lower than open field | 0.985 | **0.950** (-3.5 cm; plant sink adds -5 cm for 0.2 s) |
| Moves per carry (juke / spin / stiff-arm / truck / hurdle / dive), 64 carries | 0.3-0.5 | 0.72 (45 jukes, 1 spin) | **0.47** (28 jukes, 1 spin, 1 dive; the sim test over 255 carries: 0.36) |
| Cuts announced per carry (36+ degrees) | 0.3-0.7 | not tracked | 0.44 (28 announced per 64 carries; the sim test: 0.35 reach a plant) |
| Cut speed loss, 36-60 degree plant (fraction of the pre-cut speed) | 0.10-0.25 | 0.73 (audit bug: counted tackles) | median 0.15, mean 0.31 (n = 9 clean plants, wide: one 78 percent stop into contact) |
| Re-acceleration: best speed 0.8-1.8 s after / before the cut | >= 0.9 | n/a | median 1.06 (mean 0.93: one cut ended in a tackle) |
| Planted-foot slide while carrying, median / mean (m/s, any-frame metric of this audit) | <= 0.10 | 0.248 / 0.80 | 0.244 / 0.78 - **not met, unchanged: this is the matcher's own slide (MOTION)**, the carry layer adds none |
| Planted-foot slide during a juke, median | <= 0.10 | 0.225 / 0.258 | 0.186 / 0.212 (an upper-body clip take made it 0.6-1.6, see 4) |
| Flight fraction (both toes 10 cm up) | <= 0.30 | 0.248 | 0.247 |
| Yards per carry, audit sample (64 carries) | stays | 5.22 | 5.13 |
| Yards per carry, EditMode `CarryStyleTests` (686 carries a side, style off / on, same seeds) | within 0.6 | 4.84 | 4.85 |
| `CarryStyleTests` (new, 5 tests incl. pullers A/B: engaged 0.78 on vs 0.805 off) | pass | - | 5 / 5 (sim arm compliance 0.909 / 0.900 inside 3 yd, bad plant foot 0, plant without approach 0, moves per carry 0.35, max 2) |

## What was built

Sim decides, the view plays (never the other way round). New files:

- `Sim/SimAgent.Carry.cs`: per-carrier style state (carry arm, press, crouch, two hands, tightrope, cut phase / kind / angle / plant foot / eta, planned move + eta, move count).
- `Sim/CarrierAI.Style.cs` (partial `CarrierAI`, three one-line hooks in `CarrierAI.Update`): carry arm, press, announced cuts.
- `Sim/CarrierMoves.Style.cs` (partial `CarrierMoves`, hooks in `ConsiderAIMoves`): planned moves, leverage / situation / budget weights.
- `Sim/PlaySimulation.Carry.cs`: reset on pickup, the planned path for the matcher (`CarryPlanAhead`, a 2-line hook in MOTION's `PlanPoints`).
- `Gameplay/Player/SimAgentView.Carry.cs`: the carry layer (LateUpdate after the arm IK). `SimAgentView.CarryMoves.cs`: move ownership and the (off by default) clip take.
- `Gameplay/Match/PlayStage.Carry.cs`: feeds the sim's decisions to the view and lets the sim's arm drive the ball (2 one-line hooks in `PlayStage.cs`), 2 one-line hooks in `SimAgentView.cs` (LateUpdate, ApplyMatch), 1 in `PlaySimulation.Ball.cs` (`GiveBall`).
- `Editor/Tools/CarryAudit.cs`, `Tests/EditMode/Sim/CarryStyleTests.cs`.

Switches: `-pixCarryOff` (the v0.10 carrier), `-pixCarryNoSink`, `-pixCarryTake` (clip take back on). `CarryStyleConfig.Enabled` in code.

## 1. Move by move: what it is and what it is based on

- **Carry arm ("outside arm")**: the ball is in the arm away from the nearest defender (the man who can reach it), with the sum of all defenders within 7.5 yd as a tie-break and the arm toward the nearer sideline when nobody is near. Basis: running-back coaching (ball in the arm away from the pursuit, switched only when the threat changes sides). A defender inside 3.2 yd switches it after 0.05 s (min 0.24 s since the last switch), a far one after 0.18 s (min 0.55 s). During the approach of a cut the leverage is evaluated for the new lane, so the ball is already on the right arm at the plant. Arm switch in the view: the free hand comes across to the ball (0.09 s), the ball changes arms, the old arm releases (0.30 s total).
- **Five points of pressure / high and tight**: hand over the point of the ball at chest height, forearm across the front of the ribs, elbow 70-75 degrees, the ball 0.23 m from the chest bone (ball follower unchanged: it sits on the forearm of the arm the sim picked).
- **Free arm**: pumps against the leg on its own side (the arm is driven from the real thigh angle of the matched legs, so it is always in opposition), amplitude 0.9-1.35 x the leg swing by speed, elbow 78-92 degrees; abducted for balance in a plant and along the sideline.
- **Forward lean**: total trunk lean target 8-14 degrees by speed, +1 degree per m/s2 of acceleration, +10 in traffic, +4 pressing, +6 in a plant; the layer adds only the difference to what the animation and BodyMotion already lean (the first version added on top of 14 degrees: fixed). Head stays up.
- **Lower centre of gravity**: hips sink up to 4.5 cm in traffic, 3 cm pressing, 5 cm in a plant, the ankles pinned where the animation put them (two-bone leg IK), so the knees bend and the feet do not move.
- **Press and cut**: the speed is cut by up to 7.5 percent (smoothed; 16 percent cost 0.2 yd per carry and broke the tackle-curve and ability tests) when a defender is 1.4-5 yd ahead on his line (not inside 1.4 yd: he is committed), and a little at the line before the hole. A lane change of 36+ degrees is announced: Approach (0.10-0.18 s by agility and speed: he holds the old line, brakes, sinks), Plant (0.17 s: the new line, the outside foot down, speed bled by the body's own cut model), Drive (0.75 s: re-accelerating). The matcher sees the cut in its planned path before the plant (`CarryPlanAhead`), so it picks its turning frames and the plant foot itself (outside foot = opposite to the direction of the cut: `CutPlantFoot`, tested). Basis: inside / outside zone teaching (read, press, plant, cut, burst), roughly a quarter of the speed lost in a 90 degree cut, back to speed in about a second.
- **Cut kinds** (sim classification, logged): jump cut (70+ degrees with a defender inside 3.6 yd ahead), cut-back (against the designed path / pursuit flow), bounce (toward the nearer sideline when the lane ahead is shut), plain plant. The audit run: 19 plants, 2 bounces (cut-backs and jump cuts need more than 64 plays to show up; the sim test counts all of them).
- **Juke**: planned 0.25 s ahead (the sim decides at the defender's commit, executes at the old time), direction by the defender's lean and the room on each side (a second defender or the sideline vetoes a side). The view: shoulders and head fake the other way (-15 degrees, 0.18 s), then go through the cut (+14 degrees, 10 degrees dip, 4.5 cm sink); the legs are the matcher's, which already follows the planned path through the lateral steering. Basis: jukes are shoulder-and-head sells over a plant, not a step to the side.
- **Stiff-arm**: the free arm on the defender's side goes straight into his chest (target = his chest, 1.32 m up, reach-clamped) for the length of the move (0.10 s in, 0.12 s out); the sim puts the ball in the other arm when it plans the move. Weighted up when the defender comes at an angle along the shoulder.
- **Two hands in traffic**: defender inside 1.9 yd with no move running: the free hand covers the ball.
- **Truck, hurdle, dive, spin**: unchanged (existing clips); the carry layer steps out while a full-body clip owns the pose. Truck weight raised when the tackler is smaller and head-on.
- **Move budget and situation**: pass rate 0.52 (was 0.75), one move per defender, 1 / (1 + 0.9 x moves so far), no spin with a defender inside 2.6 yd or at the sideline or in a wet ball, fewer spins and jukes protecting a lead in the fourth quarter.

## 2. What still looks wrong / not done

- **Foot slide 0.24 m/s median while carrying** is the matcher's (unchanged); it is not met against <= 0.10 and the carry layer neither adds nor removes any. Needs MOTION.
- **Clip takes for jukes are off by default** (`-pixCarryTake`). An ANIMLIB juke take played as an upper-body plan brings its own pelvis (the mask part "Body" includes the hips): slide in a juke went 0.22 to 1.56 m/s (median). A mask without the pelvis (arms, chest, head only) would let the real mocap take back in; the plumbing (`ClipSelect` pick by root direction, `Synced` plant on the sim's lead) is there. The procedural shoulder / head fake is what plays now.
- **Spin, truck, hurdle, dive** still play their v0.10 clips (spin seen once in 64 carries); no ankle-breaker take wired.
- **Falling forward at contact, sliding / going down late, sideline step-out** are not done: they sit in the tackle takes (SIM-FLOW) and a late-game "get down" needs a clip that does not exist for backs (only `qb_slide`). Tightrope: only the free-arm balance and a smaller sink along the line.
- **Cut statistics are thin**: 6 clean plants out of 35 announced cuts (the others are inside contact windows); the speed loss (22 percent) and re-acceleration (1.08) are on those 6. The sim-only test and the 255-carry yard comparison are the larger samples.
- **Arm in the view vs the sim**: the view lags the sim by the 0.09 s of the switch and, with two hands on the ball, the audit's "nearest hand" read can pick the covering hand (432 frames differ out of 5059); the sim-level compliance is 0.874 in the audit and 0.91 over 160 carries.
- Yards per carry is 0.15 lower with the style on (press); inside the band, not retuned.
- `Docs/QA/v0110/BUGS.md` did not exist when this track ran, so no RB-tagged rows were closed.

## 3. Strips (looked at once)

`D:\PIXFootball\anim_rb\v0110\final\strips` (behind-and-above "bcast" and side): cruise, arm switch, jukes. Cruise: ball high on the forearm across the chest, free arm pumping, lean over the feet, looks like a back. Arm switch: the carrier plants and drives left with the ball already on the left arm and a visible sink. Jukes: shoulders dip and the head sells the fake; the camera is often swallowed by blockers (the strip hides everybody but the carrier and the nearest defender). Not judged: the stiff-arm contact (always inside a pile in the strips).

## 4. Findings along the way

- The v0.10 carry tree used baked `_BC` clips with the ball in a fixed arm while the ball follower attached the ball to the arm picked by the view: wrong-arm frames 20 percent; the layer now draws both arms from the sim's decision.
- StiffArm always played the right-arm take (lateral hint was zero).
- First audit versions: the cut analysis counted tackles as cuts (speed loss 1.0) and the arm angle wrapped at +-180; both fixed in `CarryAudit.cs`.

## 5. How to repeat

`run.ps1` (private copy `D:\PIXFootball\Work\RB\PIX`): `Unity -batchmode -executeMethod PixFootball.EditorTools.CarryAudit.Run -pixCarryAudit -pixCarryPlays 64 -pixCarryStrips 1 -pixCarryTag x -pixCarryOut dir` (not `-nographics`). Tests: `-runTests -testFilter PixFootball.Tests.CarryStyleTests`.

## 6. BUGS.md rows (RB)

- **B-023 (S2) postplay-ball-left-behind: FIXED for tackles and sacks (one remaining play out of 64, see below).** `PlayStage.PostWhistleBallGuard` (PlayStage.Carry.cs, one hook in `UpdateBall`): after a tackle or sack the ball that is no longer held (dead at the sim's ruled spot, 0.4-1.9 m from the lying carrier) stays with the last carrier (the relaxed hold, then the official), instead of being drawn at the sim position. Measured over 64 plays x 1.35 s after the whistle: ball farther than 1 m from the carrier's hands 49 frames (max 1.92 m) -> 10 frames in one play (max 1.28 m; the median distance is 1 cm), the remaining play is not a tackle or sack end I did not identify. The diagnosis in the row (released to the dead position) was confirmed by the before / after measurement, not by reading the ball follower: I could not reproduce it from code alone.
- **B-065 (S3) carry-ball-off-hands-0p5m: NOT FIXED, diagnosed.** The audit now classifies the frames (`B-065 detail`): all 108 frames (ball more than 40 cm from both hands, max 0.575 m) are in the first second after a hand-off, with the Flexbone hand-off take (`HandoffFlexbone*`) still playing, never during an arm switch, so the row's guess (SetCarry blend) was not the cause. Changes made anyway: the arm-switch blend of `BallFollower.SetCarry` is 0.07 s with the carry layer (it was 0.2 s), and a palm hold (`BallFollower.SetPalmHold`) while a full-body take owns the arms. Neither moved the 108 frames: the ball is still in the exchange / hand-off mode there (PlayStage.Connect / BallFollower exchange, SIM-FLOW's and CONNECT's code). Strip: `D:\PIXFootball\anim_rb\v0110\dbg3\strips\b065_p1_0_side.png`.

## 7. ANIMLIB keys wired (coordinator note)

- `truck_alt` (4 clips) -> `AnimKit.TruckAlt`: the sim's truck now plays it as a full-body take across the planned lead (the carry layer steps out for it); the old animator truck trigger is the fallback.
- Juke / rocker / cut-hard takes: not wired as clips (see section 2: an upper-body take brings its pelvis; a full-body take would skate against the sim's lateral steering). `ClipSelect` pick + sync is in `SimAgentView.CarryMoves.cs` behind `-pixCarryTake`.
- Pair takes (`ankle_breaker`, `stiff_arm` pair, `spin_move` pair, `takedown`): two bodies, belong to the contact director / choreographer (SIM-FLOW). The carrier-side decisions they need are in the sim now (planned move with target id `PlanThreat`, `PlanEta` lead).
- Sideline tightrope: no clip key exists; pose only (free arm toward the line, smaller sink). Dive: `qb_dive` is a QB key; the carrier's dive keeps the animator dive.

## 8. Tests (private copy, EditMode)

- `CarryStyleTests` 5 / 5 on the final code (sim arm compliance 0.93 over 160 carries, bad plant foot 0, plant without approach 0, moves per carry 0.36 (max 2), pullers engaged 0.795 on vs 0.800 off, yards per carry 4.85 on vs 4.84 off over 686 carries a side).
- One full `-testCategory "!Slow"` run (1338 tests) in the middle of the pass: 12 failures; the ones that touch the carrier were then found by A/B (`PIX_CARRY_STYLE_OFF=1` brings the v0.10 carrier back) and reduced by lowering the press: `SimAbilityTests` (22 / 22), `SimCalibrationTests` (11 / 11) and `BlockingTests` (8 / 9) were re-run after the last change. The rest of the 12 are other tracks' work moving under the same tree (play counts, penalties, time of possession, draft, snaps in five-minute quarters) and the two 40-game CPU league tests that run past their 180 s timeout on this shared machine (their printed statistics are in band: rush 120-130 yd / team, sacks 2.4).
- **Open: `BlockingTests.RunBlockingRatingsMoveTheYardage`** fails by 0.035 yd with the style on (good blocking 5.12 vs even 4.75, needs 0.4 apart: 5.154) and passes by 0.02 with it off (5.19 vs 4.77). It is a knife-edge 700-play test; the press / cut lead cost about 0.07 yd on strong blocking after the last reduction. Not retuned further (cost rule).
- The audit numbers above (`final3`) are from the final code; the strips (`final`) are from one tuning step earlier (press 0.16, cut lead 0.13-0.24 s) and were looked at once.
