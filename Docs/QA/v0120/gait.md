# v0.12.0 GAIT track (`gait.md`)

Candidate 2: start / stop / cut mechanics of free locomotion. Private copy `D:\PIXFootball\Work\Motion\PIX`. Every number below is `EngineAudit` (36 plays, 22 players, every frame), same tree for before and after (`-pixGaitOff` = v0.11 behaviour): before `D:\PIXFootball\Renders\v0120_gait\off4`, after `...\final2`. Confirming `MatchWatchAudit` (40 plays, seed 110110): `D:\PIXFootball\Renders\match_watch_v0120\gait`. Treadmill strips: `D:\PIXFootball\anim_loco\v0120\gait\pos_*_side.png` (before: `...\v0110\final`).

## Done-criteria

| | before | after | verdict |
|---|---|---|---|
| (a) median trunk lean at accel >= 4 m/s2 (free runners, 12,680 frames) | 10.3 deg | **28.5 deg** (p25 20.1, p75 34.8) | met (25-40) |
| (a) median trunk lean at decel <= -4 m/s2 (3,276 frames) | +8.1 deg (leaning forward) | **-10.6 deg** (p25 -13.7, p75 -3.4) | met (leans back) |
| (a) pelvis height / leg at accel >= 4 / decel <= -4 | 1.14 / 1.11 | 1.11 / 1.06 | sinks |
| (b) cuts > 45 deg, legs owned by the matcher (244): planted foot (FootPlant flag held, sole < 2.5 cm, drift < 4 cm) | 76 % at 6+ frames, 83 % at 4+ frames | 74 % at 6+ frames, **84 % at 4+ frames** | 6+ frames NOT met, 4+ frames met but not improved by this track |
| (b) same, straight running at speed (control, same windows) | 83 % (4+: 85 %) | 83 % (4+: 85 %) | the ceiling of the measure |
| (b) cuts, all 416 incl. blocks / engaged / catches | 46 % | 44 % | not meaningful: 172 of 416 are `Block` / `Engaged` (CONTACT's) |
| (b) hip drop into the cut (median, mean +-0.1 s vs 0.7-0.3 s before), matcher cuts | -0.3 cm | **+2.3 cm** (deepest sample 6.0 cm) | measured, modest |
| (c) planted slide, CB sprint 6 m/s / CB run 6 / LB run 6 / LB sprint 6 (mean m/s) | 0.023 / 0.025 / 0.016 / 0.013 | 0.024 / 0.023 / 0.016 / 0.020 | no regression |

Honest reading of (b): the plant rate of a cut (84 % at 4+ frames) already equals that of straight running (85 %) before this track; the 6+-frame strict rate is 74 % against 83 % straight. What I could not move: 41 of 244 cuts (17 %) have no held foot lower than 2.5 cm for 6 frames in +-0.15 s (flight or a hovering planted foot: shuffle takes show a flagged foot 3-6 cm up), and 23 have a held foot that drifts 4-10 cm (heel / ball anchors disagree while the body turns 20 deg during one stance). Both live in the matcher's contact tags and the FootPlant anchor pair.

Other lean numbers: ball carriers `Carry` accel >= 4: 1.2 -> 1.9 deg, decel: 21.6 -> -3.2 deg (n 205 / 34; these frames are mostly hand-off / action frames of RB's layer; RB style owns them, hook below). QB (not touched): 21.7 deg.

## What was built

- `Loco/GaitLean.cs` (new, pure, tested): from the SIMULATED velocity (smoothed longitudinal acceleration over 0.08 s, heading rate, alignment of the velocity with the body forward) it asks for the total trunk lean `AccelLean(a) = min(40, 4 + 1.1 atan(a/g))` (4 m/s2 -> 28 deg, 6 -> 39) and `BrakeLean(a) = max(-14, 4 - 0.55 atan(|a|/g))` (4 m/s2 -> -8 deg), gated by speed (>0.25 m/s) and alignment (a back-pedal / shuffle gets none), plus a hip sink (stop 5.5 cm, start 2.5 cm, cut 7 cm at severity 1; cut severity = heading rate 50-200 deg/s at speed over 1.8 m/s).
- `BodyMotion.ApplyGaitLean`: runs after the old bank / pitch. It MEASURES the trunk lean (pelvis to neck, the FormProbe definition) of whatever pose the matcher produced, low-passes it (0.3 s) and adds only the missing difference to spine + chest (50/50, one correction pass on the measured lean), head counter-rotated 0.6: it works on top of any clip. Rate: 0.05 s up, 0.13 s down (the lean follows the acceleration: straightens as the acceleration dies over 3-5 steps). The sink goes to `FootPlant.ExtraDrop` (feet stay planted, legs bend).
- `SimAgentView.Loco.cs`: `Motion.GaitScale = owned && Role != QB` (QB carriage and square stance untouched); switches `-pixGaitOff`, `-pixGaitNoSink`, `-pixGaitHold`, `-pixGaitHoldMax`, `-pixFpHeelBias`.
- `FootPlant`: `ExtraDrop`; QA / experiments left OFF: `CutHold` (hold a foot whose contact tag ends while the other is not down: no gain, 75 %, more drift), `HeelBias` (lean the correction on the ball of the foot: no gain on the final tree, 76 vs 77 %, more sprint slide).
- `EngineAudit` / `FormProbe` / new `CutProbe`: `lean_accel.md` (lean against acceleration, per bin and per group), `cut_audit.md` / `cut_events.csv` / `cut_trace_failed.txt` (direction changes of the sim path > 45 deg within 0.6 s, planted foot + drift, outside foot, hip drop, straight-running control, by position / angle / action / owner of the legs).
- Not done: shorter first steps out of a stance, gather steps on tight turns, outside-foot choice (needs the matcher's stride phase; MOBILITY's database / matcher files). Lean into the turn (bank, <= 17 deg) was already in `BodyMotion` since v0.4.

## Files changed (9, plus 1 test file)
`Loco/GaitLean.cs` (new), `Loco/FootPlant.cs`, `BodyMotion.cs`, `SimAgentView.Loco.cs`, `Editor/Tools/{FormProbe,EngineAudit}.cs`, `Editor/Tools/CutProbe.cs` (new), `Tests/EditMode/Gameplay/GaitLeanTests.cs` (new, 10 tests). One-line hook in another track's file: `SimAgentView.Carry.cs` line 144 (`leanWant` merged with `Motion.GaitWant(...)`, floor -4 -> -14) so the carrier style and this layer do not fight over the lean.

## Tests
Full `!Slow` EditMode run: 1,362 tests, 1,359 passed, 3 failed: (1) my `StopLeansBackAndBackpedalDoesNot` (back-pedal slowing below 1 m/s regained the alignment weight: fixed, `GaitLeanTests` 10 / 10 after), (2) `SimAbilityReceivingTests.ClutchGeneRaisesEveryRatingInACloseFourthQuarter` (statistical sim test, not this track: 0.0496 against a 0.0488 limit), (3) `MotionMatchingTests.MobilitySpecsAreWellFormed` (MOBILITY, pack count 20 not > 40, in progress). Confirming MatchWatchAudit finished 40 plays; pops / floats in `issues.txt` are the CONTACT / MOBILITY areas, no new detector family from this layer (PERF_HITCH and POP_* are editor-run and capture-timing noise).

## Looked at
Treadmill side strip `pos_start_to_6.5_side.png`, nine positions: v0.11 stands up and strides; now every body drives out with the chest 30 deg over the front foot and straightens through the third step. What still looks wrong: the head counter-rotation (0.6) leaves the chin tucked on the largest bodies (try 0.8); the hips do not move forward over the lean (the legs trail less than a real first step); no shorter first step.

## Requests
- MOBILITY: contact tags of shuffle / backpedal takes at 1-3 m/s (a flagged planted foot floats 3-6 cm up in ~15 % of cuts) and the stance length at 3-5 m/s (stances under 6 frames count as no plant); the lean layer will work on the new takes unchanged.
- RB: `Motion.Gait.WantTotal` / `GaitWant` is available if the carry style wants the same acceleration lean law.
- CONTACT: 172 of the 416 audited direction changes happen in `Block` / `Engaged` (drift median 21 cm there).

## Known issues for v0.13
Plant at 6+ frames 74 % (matcher cuts); outside foot is planted only in 51 % of cuts (the stride phase is not chosen); gather / shortened steps; first-step length out of a stance; head counter-rotation; carriers' lean owned by RB at stops (-3 deg median).
