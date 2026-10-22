# v0.7.0 Track B: engine pacing, play-end clarity, kicking (`B-###`)

Owner area: `Scripts/Sim` (plus the kick-meter / end-reason hooks in `Gameplay/Match`). Playtest notes 3, 8, 28 and the shared 7 / 25 / 30 (`PLAYTEST_v060.md`).
Method: the headless film room (`%TEMP%\pixfilm`: `gap`, `endaudit`, `endkinds`, `kicktempo`, `game`), then the Sim EditMode suite through `%TEMP%\pixsim\tests` (`dotnet test`, 276 tests green, 17 of them new in `PaceAndPlayEndTests`).

## What Track A and Track C need to read

| Need | API |
|---|---|
| One pace constant | `PixFootball.Sim.GamePace.Scale` (0.88), `GamePace.AnimationRate` (same value). Bodies are slowed **in world units**: `SimAgent.Vel` is already the real on-screen speed (yd/s), so locomotion keeps driving off velocity. Non-velocity clips (throw, tackle, catch, kick, celebrate) should play at `AnimationRate`. |
| Commit windows (sim seconds) | `GamePace.CatchSeconds` 0.40 (catch holds the `Catch` action; receiver capped at 78 % of top speed meanwhile: `SimAgent.CommitUntil / CommitSpeedFrac`), `GamePace.TackleWrapSeconds` 0.24 (first contact to whistle), `GamePace.MinEngageSeconds` 0.45 (a lock-up cannot be won or lost earlier), `GamePace.DiveCommitSeconds` 0.5. |
| Play end reason | `PlaySimulation.EndInfo` (`PlayEndReason`: `Kind`, `Text` "TACKLED BY #52", `Detail`, `Primary/Secondary` agent ids and jersey numbers, `Tackle`, `SpotY`, `HasVisibleCause`). Also `PlayOutcome.EndReason`, event `MatchDirector.PlayEndReasonShown`, and a minimal banner (see B-004). |
| Kick animation sync | `SimEventType.KickApproach` (Value = seconds until contact) is logged the tick the kicker's approach starts; `SimAgent.Action == Kick` from then on; `PlaySimulation.KickContactTime` = absolute contact time; the ball launches exactly then (`SimEventType.Kick`). `KickTiming.KickoffClipContact` = 1.27 (the `kick_off_biped` contact frame); the approach is `1.27 / AnimationRate` = 1.44 s so the clip, played at `AnimationRate`, lands its contact frame on the ball. |
| Sim queries for the HUD | `PlaySimulation.KickerSlot`, `KickStruck`, `AwaitingUserKick`, `EndPending` (the wrap window). |

## Before and after

| Metric (600 CPU games, 75 v 75, same seed) | v0.7 start | now | NFL |
|---|---|---|---|
| Points per team | 26.4 | 25.6 | 22-24 |
| Plays per team | 63.5 | 61.9 | 63 |
| Yards per carry | 4.66 | 4.41 | 4.3 |
| Completion % / yards per attempt | 63.2 / 7.12 | 64.6 / 7.17 | 64-65 / 7.0 |
| Sacks per team / interceptions | 2.48 / 0.89 | 2.00 / 0.78 | 2.4 / 0.8 |
| FG % | 86 | 86 | 84 |
| Seconds per play, run / pass (sim time) | 3.3 / 4.4 | 3.8 / 4.8 | |

| Body (92 rated, in pads, from rest) | v0.6 | now |
|---|---|---|
| WR top speed / 10 yd / 40 yd | 21.2 mph / 1.70 s / 4.68 s | 18.7 mph / 1.88 s / 5.27 s |
| RB | 21.9 mph / 4.52 s | 19.3 mph / 5.07 s |
| CB | 22.2 mph / 4.47 s | 20.0 mph / 4.92 s |
| QB (75) 40 yd | 4.95 s | 5.55 s |

## Rows

| ID | Status | Sev | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|
| B-001 | fixed | S1 | `AgentBody`, `GamePace` | Gameplay a touch too fast (note 8). | One `GamePace`: skill players x0.88 top speed and burst, slower cut / brake / turn (x0.88) and a 6 % longer wind-up; OL x0.93, LB x0.89, DB x0.90, DL x1.0. A uniform 0.88 broke the balance (28.9 points a team, 7.9 yd per attempt, 1.5 sacks) because the thrown ball did not slow, so DBs / LBs / DL give up less; re-fit with 600-game runs. Free roam and the body unit tests keep pace 1.0 (`new AgentBody(...)` default), so no test band moved. | `GamePaceSlowsBodiesByPositionAndStaysInTheTenToFifteenPercentBand`, `SimulatedPlayersUseTheGamePace`, `SimCalibrationTests` green |
| B-002 | fixed | S1 | `TackleResolver` | **Phantom dive tackles.** A dive tackle downed the runner the tick the lunge STARTED, up to 1.8 yd away (4000 plays: dive tackle median gap between bodies 1.20 yd, 66 % over 1 yd; shoestrings 37 % over 1 yd). | Dive is now a commit window: the outcome is decided at the lunge but applied only when the diver's hands reach the carrier (gap <= arm reach), whiff after 0.5 s. The lunge is aimed at the carrier (it went along the tackler's own heading), 4.4 yd/s over 0.46 s. After: every tackle type has max gap 0.59 yd. | `ATackleIsAlwaysAnActualContactNeverAPhantomAtDistance` (600 plays, widest gap 0.58) |
| B-003 | fixed | S1 | `TackleResolver.UpdateDrag` | **Tackle credited from 3-6 yd away.** The drag tackler's "hold on" intent was set after the body step, wiped at the start of the next tick, so the first tackler stood still while the carrier dragged away; the gang / wrap-up then ended the play with nobody touching him (0.8 % of wrap-ups, up to 6.6 yd). | `ApplyDragHold` runs before the bodies step; the credited tackler is the one still touching him (or the nearest defender within 0.7 yd), and a tackler with his arms around a carrier who outruns his legs is pulled along (up to 3.6 yd/s). After: every tackle type, wrap-ups included, has max gap 0.59 yd over 8000 plays (a shove out of bounds 0.95). | gap audit, same test |
| B-004 | fixed | S1 | `PlayEndReason`, `MatchDirector` | **No visible reason a play ended** (note 3): ordinary tackles, out of bounds, forward progress showed no text. | Every end publishes a `PlayEndReason` (Tackle / gang tackle / Sack / Safety / Out of bounds / Pushed out / Incomplete / Dropped / Broken up / Thrown away / Interception / Touchdown / Fumble recovered / Fumble turnover / Kneel / Spike / Touchback / Fair catch / FG good, no good / Kick blocked / Punt downed / Kick out of bounds / Flag / Forward progress). Unnumbered players are named by position. Hook: headline banners get it as their second line, headline-less plays get a small "TACKLED BY #52" banner with the run / pass line; `PlayOutcome.EndReason`, event `PlayEndReasonShown`. 6000 plays: 0 null, 0 empty text. Track C styles the banner. | `EveryPlayEndPublishesAReasonWithText`, `EveryKindOfFinishedKickPlayHasAReason` |
| B-005 | fixed | S2 | `PlaySimulation` | Tackle -> down was an instant flip, spot moved with the slide. | Wrap window: contact -> carrier Tackled/Down at once, whistle 0.24 s later, ball spotted where he was wrapped (`EndPlayAfter`, bounds / goal line no longer apply once wrapped). | `TheWhistleComesAfterTheWrapWindowAndTheBallIsSpottedAtContact` |
| B-006 | fixed | S2 | `PlaySimulation.CompleteCatch` | Catch -> sprint was an instant flip. | `Catch` action >= 0.40 s, receiver capped at 78 % of top speed while he secures it (generic `SimAgent.CommitUntil`). | `CatchHoldsTheCatchActionForItsCommitWindow` |
| B-007 | fixed | S2 | `BlockEngagement` | Blocks locked up and were won or lost in a few frames. | Lev clamped to +-0.92 until the engagement is 0.45 s old. | `ABlockEngagementLastsAtLeastTheMinimumBeforeItIsWonOrLost` |
| B-008 | fixed | S1 | `PlaySimulation.Tick` | **Button presses dropped.** A press on a frame shorter than one 1/60 s step (any display above 60 Hz: 58 % of frames at 144 Hz) was consumed by nothing and lost. | Pulses wait for the next fixed step. | `ButtonPulsesOnAFrameShorterThanOneStepAreNotDropped` |
| B-009 | fixed | S1 | `SpecialTeamsAI`, `KickTiming` | **Field goals too fast** (note 28): snap -> kick 0.70 s, the kicker shuffled 0.28 s at 2.2 yd/s from 2 yd away. | Snap -> kick 1.32 s (snap about 0.45, catch and place, kick); 2-3 step approach, 0.63 s from 3.2 yd to a plant spot beside the ball, peak 3.5 yd/s. Same timeline for extra points. | `FieldGoalOperationTakesAboutOnePointThreeSecondsWithAShortApproach` |
| B-010 | fixed | S1 | `SpecialTeamsAI` | **Kickoffs too fast and unconvincing:** whistle -> kick 1.18 s, kicker started 5 yd back with a 1.1 s shuffle, and the whole coverage line was already running from the whistle (about 6 yd past the ball before contact). Hang 3.55-4.55 s. | Whistle -> kick 2.0 s: 0.55 s on the mark, then a 1.45 s approach (5.3 yd, peak 4.7 yd/s) timed to the clip contact (1.27 / 0.88). The line waits 3.5 yd back and runs a constant-acceleration 1.3 s so it is level with the ball and at 4.9 yd/s at contact (never ahead of it: -0.17 yd). Hang 3.65-4.3 s (average 4.03). | `KickoffHasARealRunUpAndAFourSecondHang`, `KickoffCoverageLineRunsLevelWithTheBallAtContactNotAheadOfTheKicker` |
| B-011 | fixed | S1 | `MatchDirector.KickHud`, `SpecialTeamsAI.Meter` | **Kick meter was display-only.** | Meter drives the kick: the timing cursor ping-pongs (1.15 s), the left stick steers the aim, A stops it and starts the approach; the sim takes the reading at the press (power 1.0, `KickMeter` scales power and spread), kicks one approach later. Kickoff: one press (also the whistle). FG / PAT / punt: A snaps, A again kicks (2.6 s, then a default reading). A press only counts while the meter is live and only the first. | `UserKickPressStartsTheRunUpAndTheKickLandsOneApproachLater`, existing `EngineRealismTests` kick-meter tests |
| B-012 | fixed | S1 | `CarrierMoves`, `PlaySimulation.Ball` | **Goal-line dive:** the dive's lunge was braked away by the body in a few ticks (under a yard of travel) and the touchdown test used a ball reach of 0.8 yd while the ball is drawn 1.13 yd ahead of the carrier, so a QB sneak from the 1 scored 30 % before the pace change and 3 % with it (MatchFlow scripted-sequence test failed in Unity). | The dive holds its lunge speed for the whole move (carrier 4.6 yd/s, tackler 5.2); the ball breaks the plane at 0.95 yd (consistent with the drawn ball). Sneak from the 1: about 50 % (59 of 120). A defensive lineman's lunge is capped at 1.03 x his own top speed. | `GoalLineDiveCrossesThePlaneOftenEnoughAndNeverFromNothing`, `NoPlayerExceedsRealisticTopSpeed` |
| B-013 | open | S2 | `DefenseAlignment.AlignLine` | Lead request (DL techniques 0-9, `REFERENCE_Stances.md`): implemented (4-3 under: 1-tech strong nose, 3-tech weak tackle, 5-tech weak end, 7-tech strong end or 5 with no TE; 3-4: 0-tech nose, 4 / 5-tech ends; derived from the real OL and TE positions, with `DefenseAlignment.ShadeYards`) and tested, but **OFF by default** (`DefenseAlignment.UseTechniques`). ON, the pass protection collapses (8.7 sacks a game: `BlockerAI.AssignPassPro` slide protection sends the tackle to the guard's man and leaves the end unblocked); with a tackle-takes-the-end patch the front is realistic but too strong (4.0 yd a carry, 7.9 yd an attempt, 1.5 sacks). Needs a blocking-scheme and run-game recalibration. | Keep OFF for v0.7.0; tests run it ON via `WithTechniques`. | `DefensiveLineAlignsByTechniqueRelativeToTheActualOffensiveLine`, `FourThreeUnderPutsTheOneTechOnTheStrongSideAndTheThreeTechOnTheWeakSide`, `TechniqueAlignmentShipsOffAndTheBaseFrontsAreUnchanged` |
| B-014 | open | S3 | tuning | Sack rate 2.0 against the NFL 2.4 and completion 64.6 % (the DB / LB / DL pace split is the lever); a further knob is the quarterback's hold time. | | |

## Notes for the lead

- Calibration: every Sim EditMode band that encoded the old kickoff alignment (`KickoffTeamLinesUpAcrossTheFieldWithTheKickerBehind`) was re-fit to the new marks (coverage 3.5 yd back, kicker 4.5-8 yd back); no pace band changed.
- Not done: punts keep their old tempo (about 1.2 s snap -> kick; the user punt press now starts a 0.45 s step like the kicks); no kick clips were wired (Track A owns clips: start the clip on `KickApproach`).
- Stumble treated as down: not found in the sim (no code path puts a stumbling carrier in `Down`); the ends without a tackle were the two phantom bugs above (B-002 / B-003) plus fumble recoveries (now "FUMBLE - RECOVERED BY") and dives (now "RUNNER DOWN").
