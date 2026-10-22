# v0.9.0 track QBSTANCE: stances, quarterback movement and throwing, kickers

Track owner: QBSTANCE (65 rows of `ENGINE_AUDIT_issues.csv`). Work was done in a private Unity copy (`D:\PIXFootball\Work\QB\PIX`, HEAD baseline + this track's files + scripted hooks into the shared files other tracks also edit), nothing committed or built.
All numbers below come from the audit tool (`EngineAudit`, `-pixEngineAudit`) run before and after; raw output in `D:\PIXFootball\Renders\qbstance\` (`before/`, `final/`, `final_strips/`, `strips*/`, `probe.txt` = mocap clip probe).

## What was built

| Piece | File(s) |
|---|---|
| Authored stance poses (data per stance kind, mirror, stance tells) | `Gameplay/Player/StancePose.cs` |
| Stance pose solver in LateUpdate: hips, spine, legs and arms by IK, **down hand planted on the turf** (the trunk deepens, then the hips sink until the shoulder reaches), heel raise, toe-out, head look, shoulder turn | `Gameplay/Player/StanceIK.cs` |
| Footfall planner (a foot only lifts while the other is planted; planted feet never move; steps follow the simulated velocity forward / backward / sideways; settles to the stance when he stops) | `Gameplay/Player/ProcGait.cs` |
| View integration, pose blending, keyed throwing / toss / kneel / spike poses | `SimAgentView.Stance.cs`, `SimAgentView.Qb.cs`, `StancePoses.Qb.cs` |
| Stance rules with the play context (pass/run set, press call, edge, goal line) and 5 new stance kinds | `StanceRules.cs`, `StanceRules.Qb.cs` |
| Stage inputs: centre's rear for the QB hands, snap reach, cadence, look targets, hints | `PlayStage.Stance.cs`, `AnimHints.Qb.cs` |
| Cadence window between snap request and snap | `MatchDirector.Cadence.cs` (+1 hook line in `MatchDirector.cs`) |
| Kick plans (right foot, onside, holder side, long snap) | `AnimKit.Qb.cs` |
| Sim: quarterback movement helpers (additive partial) | `Sim/OffenseAI.QbMotion.cs`, `Sim/PlaySimulation.QbHooks.cs` (+7 hook edits in `OffenseAI.Qb.cs`), kicker/punter alignment in `SpecialTeamsAI.cs`, `GamePace.cs`, `DefaultPlaybooks.Special.cs` |
| Mirrored clip twins (left-handed QB, right-footed kickers, holder side) | `Editor/Tools/QbMirrorBuilder.cs` -> `Assets/_Licensed/Resources/MirroredClips/*_M.anim`, `PixClipFacing.asset`, `PixClipTiming.asset` (licensed folder, already copied into the shared tree) |
| QA tooling | `Editor/Tools/QbStanceProbe.cs` (clip probe), `QbStanceTools.cs`, audit additions in `EngineAudit.cs` (QB drop metrics, side stance tiles, strip filter / hide-others, kneel / spike / toss strips) |
| Tests | `Tests/EditMode/Gameplay/QbStanceTests.cs`, `Tests/EditMode/Sim/QbStanceSimTests.cs`, updated `StanceCameraTests.cs` |

Honest design note: the mocap stance clips are the wrong shape (the audit measured linemen standing with hands on knees) and every kind is now **solved procedurally over the animation** (the clip underneath only supplies fingers and clavicles). That is the "IK / pose correction" route of the brief; no Blender-authored clips were needed. The mocap QB drop takes (flight phase 68-77 percent in the probe) are no longer used for the drop.

## Before / after (audit tool, 24 plays + formation sweep)

Stance joint angles (means; stagger is the absolute foot offset; "down" = share of players with a hand under 20 cm):

| Stance | hips m (before -> after) | head m | trunk lean deg | other |
|---|---|---|---|---|
| OL 3-point (LT/LG/RG/RT) | 0.74 -> 0.68 | 1.00 -> 0.76 | 22 -> 116 | lowest hand 0.54 -> 0.10 m, hands down 0 -> 100% |
| Centre (hand on the ball) | 0.76 -> 0.82 | 1.03 -> 1.03 | 21 -> 90 | hand on the ball (anchor) |
| DT/DE 3-point | 0.88 -> 0.72 | 0.94 -> 0.78 | 81 -> 124 | lowest hand 0.40 -> 0.15 m, down 0 -> 100% |
| DT/DE 4-point | 0.81 -> 0.72 | 1.15 -> 0.78 | 16 -> 126 | lowest hand 0.58 -> 0.20 m (one hand on the turf, 50% both) |
| QB under centre | 0.98 -> 0.81 | 1.46 -> 1.32 | 10 -> 34 | hands 0.97 -> 0.80 m, under the centre's hips |
| QB shotgun | 1.06 -> 0.92 | 1.59 -> 1.46 | 3 -> 16 | hands at the chest 1.08 m |
| WR split / slot | 0.97 -> 0.86 | 1.43 -> 1.41 | 10.8 -> 22 | stagger 0.14 / 0.12 -> 0.41 / 0.34 m (back heel up, outside foot back) |
| CB off | 1.02 -> 0.81 | 1.45 -> 1.34 | 38 -> 26 | stagger 0.05 -> 0.26 m (outside foot back) |
| CB press | 1.03 -> 0.87 | 1.44 -> 1.42 | 40 -> 14.5 | stagger 0.05 -> 0.04 (squared), hands up at 1.12 m |
| LB | 0.97 -> 0.82 | 1.42 -> 1.38 | 39 -> 30 | |
| Safety | 0.89 -> 0.88 | 1.38 -> 1.47 | 7.3 -> 18.5 | stagger 0.20 |
| Kickoff coverage (runner) | 0.89 -> 0.92 | | 7 -> 14 | staggered 0.32, own stance (receiving line: hands-up "KickoffReceive") |
| RB 2-point | 0.98 -> 0.83 | 1.43 -> 1.36 | 11 -> 30 | hands on thighs |

Quarterback flight phase (both toes more than 10 cm off the turf): the audit measured **51-63 percent** of the drop frames in the old back-pedal bins (and 68-77 percent in the mocap drop takes, `probe.txt`). With the pose layer and the footfall planner: **0 of 556 drop-back frames** (`QBDROP flight frames` counter, `final/motion_summary.txt`); the per-action gait bins fall from Backpedal 0.54 / 0.68 to 0.24 (the rest is plain locomotion in non-pose states: hand-offs, scrambles) and Carry 0.19-0.48 to 0.11-0.19.
QB body yaw vs simulated facing during a throw: p50 24 -> 18 deg, p95 86 -> 25 deg, p99 103 -> 25 deg; during idle / pocket: mean 11.9 -> 6.2 deg.

Kickers (kick report): kickoff kicker pre-snap lateral offset **+1.65 m -> -3.29 m** (left), at contact +0.49 -> -0.29 m; field goal +1.65 -> -1.65 m, contact +0.96 -> -0.67 m; punter 0.00 -> -0.41 m. Kicking foot: kickoff RIGHT (17.7 m/s) and field goal RIGHT (17.6) were right already; the punt was **LEFT (23.9 vs 8.5) -> RIGHT (20.4 vs 8.1)**.

Strips looked at once (final code): `final_strips/strips/qb_uc5_Side.png` (under centre: stance, snap received at the centre's rear, ball into the chest, reverse pivot, drop, cocked ball, throw), `qb_shotgun_Side.png` (snap caught at the chest, pocket, throw), `kick_fieldgoal_Behind.png`, `kick_kickoff_Behind.png`, `kick_punt_Side.png` (right-footed, left of the ball), `qb_kneel_Side.png`. Earlier iterations kept in `strips2..6/` and `after6/crop_*.png`.

Deviation, stated plainly: E-046 asks for an OL trunk lean of 55-75 degrees AND E-045 asks for a hand on the turf. On this rig the two cannot both hold: with the hand on the turf the shoulder joint has to be about 0.62-0.66 m high (arm length 0.6 m), so the chest bone ends up level with or below the hips (hips about 6-8 cm above the shoulders, trunk 108-126 degrees from vertical by the audit's definition). That is also what `REFERENCE_Stances.md` says ("hips level with or slightly above the shoulders"), so the hand-on-turf reading was chosen; the OL/DL hips are therefore 0.68-0.72 m and head 0.76-0.78 m, below the 0.80-0.90 / 0.85-0.95 targets of the CSV.

## Status per audit ID (65 rows)

Legend: FIXED = changed and measured / looked at; PARTIAL = part done, remainder stated; WONTFIX = reason given.

### Stances
| ID | Status | Evidence |
|---|---|---|
| E-045 | FIXED | Down-hand IK onto the turf (`StanceIK`, `HandMode.Ground`, deepening trunk then sinking hips); OL lowest hand 0.54 -> 0.10 m, DT 0.40 -> 0.15, hands-down 0 -> 100%; test `LineStancesPutAHandOnTheTurf...` |
| E-046 | PARTIAL | Hips-level-with-shoulders, flat back, weight on the balls of the feet and head up are in; the 55-75 degree trunk and the 0.80-0.90 hips conflict with the hand on the turf (see deviation above) |
| E-047 | FIXED | QB under centre trunk 34, hips 0.81, feet pigeon-toed (-7 deg toe-out), hands 0.80 m at the centre's hips (anchor from his hip bone), cadence hip drop |
| E-048 | FIXED | Hips above the shoulders (trunk 124 / 126), fingertips loaded: 3-point one hand, 4-point both hands |
| E-049 | FIXED | Shotgun hips 1.06 -> 0.92, trunk 3 -> 16, hands at the chest |
| E-050 | FIXED | Stagger 0.41 / 0.34 m (>= 0.30), back heel +7 cm, outside foot back per side (mirror), head turned inside; test `ReceiverStance...` |
| E-051 | FIXED | Press: square (stagger 0.04), hands up at 1.12 m; off: in the chair (hips 0.81 vs 0.87), outside foot back 0.26; test `PressAndOffCornersAreDifferentStances` |
| E-052 | FIXED | Kicker / punter / long snapper stances (`Kicker`, `Punter`, `LongSnapper`): relaxed upright, eyes on the ball via the head look; punter square with hands out |
| E-053 | FIXED | `OlTwoPoint` when the QB is in shotgun/pistol and the play is a pass (`StanceContext.PassPlay`); centre keeps his hand on the ball; test `OffensiveLineGoesToTwoPoint...` |
| E-054 | FIXED | Cadence: head swivel + bounce + hips drop 0.15 m on the go (`PlayStage.Stance.cs`), window in `MatchDirector.Cadence.cs` |
| E-055 | PARTIAL | Head look layer (stance head yaw toward the ball for receivers / corners, QB eyes on the read / downfield, kicker eyes on the ball); a catcher's eyes on the ball belong to the catch take (CONNECT) and are not done |
| E-056 | FIXED | Outside foot back for CB / S / LB / WR via the pose mirror (`StancePoses.For`); RBs stay square by spec |
| E-057 | FIXED | FB / goal-line back: real one-hand-down pose (`ThreePoint` with the RB role) |
| E-059 | FIXED | Stance poses are solved, not looped: no 20% slice or loop seam; breathing is a sine; remaining mocap clip is only fingers |
| E-060 | FIXED | Hand IK + stance tells (`Tell`: run = weight on the hand, pass = sits back, light hand) |
| E-062 | FIXED | Holder take mirrored so the placing hand is on the kicker's (left) side (`AnimKit.HolderClip`, mirror twin of `YAN__H_fieldgoal_00_remap`); probe showed the native take places with the right hand |
| E-063 | FIXED | `StanceKind.LongSnapper` (wide base, head down, hips high, hands on the ball) |
| E-064 | FIXED | `KickoffRunner` (staggered, toes) / `Returner` (hands out) poses, no longer the shared safety clip |
| E-065 | FIXED | Edge / contain / 7-9 technique tilts 14-22 degrees toward the quarterback (`StanceSpec.Tilt`), goal-line fronts all four-point; test `EdgeRusherTilts...` |
| E-066 | FIXED | Kicking line staggered, receiving line `KickoffReceive` (hands up), returner hands out |
| E-067 | FIXED | Corner press from `CoverageAssignment.Press` (`StanceContext`), `LinebackerBlitz` for edge backers within 2.5 yd; test `CornerPressFollowsTheCall...` |
| E-068 | FIXED | Same as E-066 (no blitz crouch for ten kickoff coverage men) |

### Quarterback movement
| ID | Status | Evidence |
|---|---|---|
| E-069 | FIXED | Drop on the pose layer + `ProcGait`: 0 of 556 drop frames with both toes off the turf (was 51-63%); tests `FootfallPlanner...` (every speed/direction) |
| E-070 | FIXED | `QbDropFacing`: reverse pivot (0.24 s) under centre, shotgun keeps facing the line; test `UnderCenterDropTurnsTheBody...` |
| E-071 | FIXED | Snap received: hands go to the centre's rear and follow the ball in flight (`StanceDyn.Anchor`), shotgun hands reach the incoming ball (`Reach`), then into the chest carriage; strips `qb_uc5`, `qb_shotgun` |
| E-073 | FIXED | No clip scrub: the pose layer follows the simulated position, plant is a pose state; the old 0.9 m threshold path is only a fallback |
| E-074 | FIXED | Pocket slide / step-up are stepped by the planner in the velocity direction (`ProcGait`), stationary only under 0.25 m/s |
| E-076 | FIXED | Shotgun drops: the view steps the retreat; the gather distance itself is the SIM track's E-087 (this copy has the HEAD sim) |
| E-077 | FIXED | Dedicated pocket pose (`QbPocket`: base wider than the shoulders, knee bend, ball high at the chest, light sway) replaces the locomotion idle |
| E-078 | FIXED | The sim turns the body (pivot) and the pose layer sets the hips yaw absolutely; QB body-yaw error p95 in a throw 86 -> 25 deg |
| E-079 | FIXED | No compression: the catch / turn part is the stance -> pivot blend (0.2 s), not 6% of the metres |
| E-080 | PARTIAL | A passer on the move (drop, set, scramble, wind-up) no longer enters the tucked-ball Carry tree (`h.Carrying=false`) so the chest / ear carriage owns the ball; the unused `qb_scramble` / `qb_run_*` studio takes have no timing data and were not wired |
| E-081 | PARTIAL | Pump plays in the pocket only (`PocketLife`) and the pose layer holds legs / trunk so the feet stay set; it is still seeded, not tied to a sim read event |
| E-082 | FIXED | `QbPhase.Fake` on the pose layer: the ball is carried out toward the runner and back (`ChestShift`), head on the runner; no solo hand-off take |
| E-084 | FIXED | The mocap kneel takes never leave standing (probe: hips 1.05 m throughout), so kneel and spike are keyed poses (`QbKneel`, `QbSpike`); strip `qb_kneel_Side.png` shows the kneel |
| E-085 | FIXED | `CadenceWindow` before `PrepareSnap`: 0.6-1.0 s of cadence for the user's snap and the CPU's |
| E-086 | FIXED | Brake to 0.8 yd/s over the last yard, set at < 0.30 yd (was 0.55), pose layer plants; combined with the SIM track's hitch |
| E-088 | FIXED | A rolling QB keeps going to about 10 yd laterally at 0.8 vmax (`QbSetMotion`); test `RollOutKeepsMoving...` |
| E-089 | FIXED | `Want = 0.9 * Vel` in the wind-up when rolling |
| E-090 | FIXED | Live feet: slow lateral slide in a clean pocket (period 1.6 s), step-up kept; test `CleanPocketHasLiveFeet` |
| E-091 | FIXED | Facing within 35 degrees of the current read (`QbSetFacing`), `QbLookTargetId` hook, head on the receiver |
| E-096 | FIXED | Carry-through after a hand-off / throw (0.9 s decay), step toward the runner on the fake |

### Throwing
| ID | Status | Evidence |
|---|---|---|
| E-098 | FIXED | Keyed throwing pose: front shoulder closed (+40 deg), stride planted toward the target, hips / stride before shoulders, shoulders square at release, finish across the body; arms cocked by the ear and high at release (`StancePoses.QbThrow`) |
| E-099 | FIXED | `QbWindupExtra` (time to turn) + `QbAligned` gate (release only within 20 degrees or after +0.2 s); test `ReleaseWaitsForTheBodyToFaceTheTarget` |
| E-105 | FIXED | Clip speed 0.9-1.45 instead of 0.8-1.8 (lead cut from the front) |
| E-106 | PARTIAL | Off-platform / hurried throws use the moving take plus the on-the-run pose; no timed off-balance / sidearm / jump takes exist (long untimed studio takes) |
| E-107 | FIXED | Left-handed QB: mirrored throw takes with timing, mirrored poses, ball in the left hand (`BallFollower` throw hand); the shared-tree `BallFollower` edit could not be exercised in the HEAD-based test copy; all kickers are right-footed by design |
| E-109 | FIXED | `SetQbHold` re-marks the attach blend when the throw / carriage state changes (the ball glides over 0.12 s instead of jumping 20-40 cm) |
| E-114 | FIXED | Throw-away starts 0.26 s before the release |
| E-115 | PARTIAL | Pocket carriage is now the pose layer (offsets scaled by the body size, follow the shoulders); palm orientation to the ball not done |
| E-116 | FIXED | `BallSpinHint`: bullet 3000, touch 2400, lob 1800 deg/s (one-line hook in the GFX `BallFollower.Gfx.cs`; kicks are the GFX track's) |
| E-143 | FIXED | Procedural underhand pitch pose (`QbToss`) instead of the overhand lob take |
| E-190 | FIXED | Press only within 1.2 yd (or by call) |
| E-254 | FIXED | `AnimHints.Snapper` + `AnimKit.LongSnapPlan` (`long_snap_00`, released at its moment) |

### Kicking
| ID | Status | Evidence |
|---|---|---|
| E-300 | FIXED | Kickoff -3.29 m pre-snap, -0.29 at contact; FG -1.65 / -0.67; strips `kick_*_Behind.png` |
| E-301 | FIXED | Punt kicks with the right foot (`YAN__P_punt_00_remap`, R 20.4 vs L 8.1 m/s); punter 0.41 m left of the ball line |
| E-302 | FIXED | `KickoffStartSide = -3.6`, `KickoffStartBack = 5.2` |
| E-303 | FIXED | Plant (-0.40, -0.70) |
| E-304 | FIXED | FG kicker (-1.8, -2.6) from the holder, formation slot (-1.5, -9.0) |
| E-305 | FIXED | FG plant (-0.35, -0.55); approach cap 0.78 vmax; contact lateral -0.67 m (was +0.96) |
| E-306 | FIXED | `AnimKit.KickPlan` swaps the two left-footed takes (`BGS__P_punt_*`, `onside_kick_00`) for right-footed ones (Yan punt / mirror twin); probe table in `probe.txt` shows the foot of every kick take; a unit test on the foot speed bone was not added (it needs the licensed clips; the audit kick report is the check) |
| E-309 | FIXED | Approach angle about 35 degrees, `shape = 1.6` |
| E-311 | FIXED | Onside uses the dedicated take mirrored to the right foot (`KickPlan`), squib uses the kickoff plan (its take is right-footed) |
| E-314 | FIXED | Punter: two steps on a slight angle toward the aim (2.0 then 3.6 yd/s) |
| E-331 | FIXED | Edge flag from `RushAssignment.Contain` / `BlitzAssignment.Edge`, goal-line unit -> all DL four-point |

Counts: 65 rows, 59 FIXED, 6 PARTIAL (E-046, E-055, E-080, E-081, E-106, E-115), 0 WONTFIX; E-046 is the one where the written target was not met and why is above.

## Open items

* The private copy is HEAD + this track; the shared tree additionally carries other tracks' work in progress (SIM already rewrote the drop timing / hitch; CONNECT is rewriting `BallFollower`). The hooks into `OffenseAI.Qb.cs`, `SimAgentView*.cs`, `PlayStage.cs`, `MatchDirector.cs`, `BallFollower*.cs` are one-liners that were re-applied by script on HEAD; the shared tree was re-checked for compile at the end (see the run log).
* Mirror twins and the updated facing / timing tables live in `Assets/_Licensed/Resources` (git-ignored); ship them with the licensed art.
* Stance numbers for OL / DL are deeper than the CSV's hip targets (physics of a short arm), see the deviation note.
* Not done: tuned left-hander carriage visuals (no left-handed QB in the audit sample), off-balance / jump throw takes, a visual gallery of every mirrored stance.

## Test runs

* One full EditMode run on the private copy (HEAD + this track, 1145 tests, 41 min): **1143 passed, 2 failed** (`SimCalibrationTests.FortyCpuGamesProduceNflLikeTeamStatistics` points per team 29.8 vs 17-29, `PaceAndPlayEndTests.ATackleIsAlwaysAnActualContactNeverAPhantomAtDistance` 0.93 vs 0.85 yd). Both pass on pure HEAD and both pass on the full shared tree (below): in the HEAD-only copy the under-centre quarterback, now facing his drop, runs it at full speed (the old back-pedal factor capped it) and the SIM track's time-parametrised drop planner, which the shared tree has, removes that effect.
* Shared tree (HEAD + all tracks' working changes, synced at the end): compiles; `QbStanceTests` (15), `QbStanceSimTests` (6), `StanceCameraTests`, the calibration test and the tackle-distance test pass. One failure in that subset is not from this track: `StanceCameraTests.LinemenLineUpTightAcrossTheNeutralZone` (a defensive formation now puts a DL 1.45 yd off the ball, test expects <= 1.1).
* Compile fixes after the lead's note: `SimAgentView.Gait` renamed `FootGait` (LOCO owns `Gait*`), `QbStanceProbe.cs` syntax error fixed; experiments stay in the private copy from now on.
