# Match engine QA log (track SIM)

Owner area: `Scripts/Sim` (the 22-agent, 60 Hz, deterministic play simulation) and the rules and play-calling half of `Scripts/Core`. Goal from the playtest: "match engine must be more convincing and realistic", with the ESPN NFL 2K5 / All-Pro Football 2K8 feel: realistic game speed, calculated and clean plays, smooth motion (see `REFERENCE_AllPro2K8.md`).

Honest count: **153 issues logged, 153 fixed** (S1 15, S2 90, S3 48). The 313 engine rows of `audit.md` are all answered in the last section: 259 fixed, 30 open, 29 wontfix (each with a reason). Where a fix needed the Gameplay or League layer it was made there with a surgical edit and is named in the row.

## Method

1. A film room outside Unity (`%TEMP%\pixfilm`, a console harness over the same source files): every play is captured tick by tick and rendered as PNG small-multiple sheets (blue offense, red defense) plus aggregate detectors (speeds, pocket depth, route breaks, throw speeds, tackle timing, kick results, bounds).
2. Measurements first, then fixes: each row below states what was measured. Game-level calibration (`film game N seed`: 200-600 CPU games) was re-run after every batch of fixes because the knobs interact.
3. EditMode tests per fix class (`EngineRealismTests`, `EngineAuditTests`, `EngineAuditRulesTests`, `EngineAuditSimTests` in `Tests/EditMode/Sim`, plus the League batch `LeagueEngineFixTests`), and the calibration suite `SimCalibrationTests` stays green.

## Before and after

Play level (measured with the film-room detectors, v0.7.0 before, this pass after):

| Metric | Before | After |
|---|---|---|
| Fastest player in 3000 plays | 28.7 mph (dive lunges, shed bursts, impulses) | 23.6 mph, hard cap 1.05 x own top speed |
| Elite back top speed in pads | 22.9 mph | 21.9 mph (75-rated receiver 21.9 -> 20.9) |
| 40-yard dash of a 99-speed player in pads | 4.30 s | about 4.50 s |
| Turning 180 degrees from rest | 0.25-0.50 s | 0.33-0.63 s |
| Worst per-tick acceleration in a play | 265 yd/s squared (mean of worst tick) | 75 yd/s squared (limit, every tick including tackles; dives exempt) |
| Pass speed, 8-15 yd / 15-25 yd | 57 / 62 mph | 39 / 46 mph |
| Flight time of a 20-yard throw | 0.67 s | 0.91 s |
| Offensive line depth at 2.0 s | +0.26 yd past the line (flat wall in the neutral zone) | -1.23 yd (kick-slide cup, tackles deepest) |
| Pocket splits at 2 s | 1.5 yd collapsed to 0.6 yd | held (lateral spring) |
| Mean sack time | 1.9 s | about 2.0-2.3 s |
| Receiver break distance short of the cut | 0.75 yd | 0.25 yd |
| Off-man corner at the snap | charged the line (aimed behind his man) | plays 3+ yd off, in front of his man |
| Press corners | aligned 7 yd off, jam could never fire | 1.15 yd off, jam resolved at the snap |
| Onside kick recovered by the kicking team | 84% | 34% |
| Tackles before 0.9 s (dive through a blocker) | 0.27% of runs | 0 of 600 |
| Furthest a player strays outside the 53.3-yd field | X = 71.3 | X = 56.5 (soft wall) |

Game level (600 CPU games, same seed, pro rules, 75 vs 75 teams):

| Metric | v0.7.0 | After | NFL |
|---|---|---|---|
| Points per team | 26.0 | 26.9 | about 22-24 |
| Plays per team | 62.4 | 63.7 | about 63 |
| Rush yards per carry | 4.78 | 4.41 | 4.3 |
| Completion % | 66.9 | 63.8 | 64-65 |
| Yards per attempt | 7.86 | 7.60 | 7.0 |
| Sacks per team | 1.91 | 2.02 | 2.4 |
| Interceptions per team | 0.75 | 0.90 | 0.8 |
| Fumbles lost per team | 0.49 | 0.49 | 0.5 |
| Accepted penalties per team | 5.25 | 5.67 | 5-6 |
| Field goal % | 87 | 86 | 84 |
| Punt net average | 40.2 | 39.5 | about 41 |
| Kickoff return TDs per game | 0.008 | 0.045 | about 0.04 |

Most of the engine's game-level numbers were already close to the league; the problems were at play level (speeds, pocket, coverage geometry, smoothness), which is why the first table moved so much more than the second.

## Rows

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| SIM-001 | fixed | S1 | Speed | AgentBody / PlaySimulation | Dive lunges (+3.6 yd/s knock), shed bursts and collision impulses pushed defenders to 26-28.7 mph (max of 3000 plays; FS/SS/CB/MLB) | Hard OverspeedCap (1.05 x own top speed inside Step) plus AgentBody.LimitSpeed (1.08 x) after every impulse; max over 3000 plays 28.7 -> 23.6 mph | EngineRealismTests.NoPlayerExceedsRealisticTopSpeed |
| SIM-002 | fixed | S2 | Speed | Physique / AgentBody | 40-yard table was combine-in-shorts speed with no pads penalty: 94-rated RB peaked 22.9 mph, 75-rated WR 21.9 mph (average WR should be ~20) | Physique.PadsFactor 0.955 on every VMax: 94 RB 22.9 -> 21.9, 75 WR 21.9 -> 20.9, 99 speed 40-yd 4.30 -> ~4.5 in pads | SimBodyTests.FortyYardTimesMapFromSpeedRating (bands updated) |
| SIM-003 | fixed | S2 | Turn rate | AgentBody | Facing turn rate 6.5-12.5 rad/s (180 deg in 0.25-0.5 s at rest) made linemen and DBs snap-turn, twitchy against the reference 'unhurried weight' | TurnRate (5.0 + 4.5 x agility) rad/s: 180 deg in 0.33-0.63 s | SimBodyTests (turn-rate band) |
| SIM-004 | fixed | S2 | Speed | BlockerAI.PullMove | Pulling linemen ran with SpeedMultiplier 1.10 (20+ mph guards) | Multiplier 1.0: pullers run at their own top speed | EngineRealismTests.NoPlayerExceedsRealisticTopSpeed |
| SIM-005 | fixed | S1 | Pass protection | BlockEngagement.ApplyPairMotion | Pass-pro pairs were driven UPFIELD when the blocker won: at 2.0 s the five OL stood at +0.26 yd past the line (a flat wall in the neutral zone) with DL stalemated at +0.54 - no pocket | New PassProVelocity: blockers kick-slide back (0.95 yd/s equilibrium, slowing when winning, faster when losing); mean OL depth at 2.0 s +0.26 -> -0.95 yd, DL penetration +0.54 -> -0.53 | EngineRealismTests.PassProtectionFormsAPocketBehindTheLine |
| SIM-006 | fixed | S2 | Pass protection | BlockEngagement.PassProVelocity | Pocket was a straight line: tackles, guards and center all at the same depth (no cup) | Slot factor 1.25 tackles / 1.0 guards / 0.75 center on the retreat so the line bows around the passer; blockers never retreat past 2.4 yd of the QB | EngineRealismTests.PassProtectionBowsIntoACup |
| SIM-007 | fixed | S2 | Pass protection | BlockEngagement.PassProVelocity | Guards and center bunched into a 5-man clump with the DTs (OL 1.5 yd splits collapsed to ~0.6 yd by 2 s) | Lateral spring toward each lineman's own slot X (<= 0.9 yd/s) while he is not being beaten | EngineRealismTests.OffensiveLinemenKeepTheirSplitsInPassProtection |
| SIM-008 | fixed | S2 | Pocket timing | BlockEngagement.TryEngage | Edge rushers beat blockers at 0.8 s (speed move); median sack at 1.9 s, first rusher in the QB's lap at 1.95 s (real sacks ~2.5-3 s) | Pass sets start with the blocker balanced (Lev 0.28 +/- 0.12) and no rush move before 0.55-1.1 s; sack time 1.97 -> 2.2 s mean, rusher-in-lap 1.98 -> 2.13 s | EngineRealismTests.PocketHoldsForTheFirstTwoSeconds |
| SIM-009 | fixed | S2 | Routes | OffenseAI.RunRoute | Receivers began every cut 0.7-1.3 yd short of the break (9-yd out broke at ~8.3 yd, curls and digs rounded off into arcs) | Waypoint arrival radius shrinks to 0.35 yd for sharp breaks (lerp 1.3 -> 0.35 by cut angle); mean miss at sharp breaks 0.75 -> 0.25 yd | EngineRealismTests.SharpRouteBreaksAreMadeAtTheBreakDepth |
| SIM-010 | fixed | S1 | Alignment | DefenseAlignment (new) / DefenseAI | CoverageAssignment.Press was never used for alignment: 'Cover 2 Press' corners stood 7 yd off the line, the jam (needs < 1.9 yd) could never fire | Press corners align at 1.15 yd (slot 1.9) with inside shade, jam now resolves at the snap | EngineRealismTests.PressCornersAlignOnTheLine |
| SIM-011 | fixed | S2 | Alignment | DefenseAlignment | All coverages shared the formation's fixed safeties (FS -5,12 / SS 7,9) so Cover 2, 3, 4 and 1 looked identical pre-snap, no rotation or disguise | Shell alignment per coverage: Cover 2/Tampa/6 halves safeties at x +/-9 (depth 10.5), Cover 3 / 1 free safety middle at 12, quarters safeties +/-7.6 at 9, man safeties shade over to their TE/back | EngineRealismTests.SafetiesAlignToTheCoverageShell |
| SIM-012 | fixed | S2 | Alignment | DefenseAlignment | Off-man corners stood at fixed x +/-17 whatever the receiver split: corner and receiver not head-up, no inside leverage | Man corners align on the matched receiver's X with 0.7 yd inside shade at 6.4 yd (slot 4.8 yd) | EngineRealismTests.ManCornersAlignWithInsideLeverageOnTheirReceiver |
| SIM-013 | fixed | S3 | Run support | DefenseAI.RunResponse | Deep-zone defenders held their depth 0.9 s on every run, giving up the shot over the top only on plays that never come on a run call (ypc +0.7 after the new safety shells) | Hold time on a called run cut to 0.42 s; pass or play action keeps 0.9 s | SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks |
| SIM-014 | fixed | S1 | Passing | OffenseAI.Qb.StyleSpeed | Ball speeds were far above real: 57 mph mean on 8-15 yd throws, 62 mph on 15-25 yd, a 20-yard ball arrived in 0.67 s (real ~1.0 s) | Launch speed flat = 14 + 0.45 x distance (clamped 16.5..arm): mean 39 / 46 / 49 mph for 8-15 / 15-25 / 25-40 yd, flight 0.63 / 0.91 / 1.32 s, 40+ yd lofts 2.1 s | EngineRealismTests.PassFlightTimesAreRealistic |
| SIM-015 | fixed | S2 | Zone coverage | DefenseAI.ZoneUpdate | Zone defenders all keyed the most dangerous receiver in their area: 3 linebackers and the strong safety bunched on one crosser (6 defenders within 3 yd, e.g. PA Crossers vs Cover 1 Robber) | CoveredByTeammate pass-off: a receiver a closer teammate already has within 3.4 yd is released and the defender returns to his landmark | EngineRealismTests.ZoneDefendersPassOffReceiversInsteadOfBunching |
| SIM-016 | fixed | S2 | Kickoff return | BlockerAI.UpdateReturnBlocker | Return blockers sprinted 17-20 yd upfield from their row and met coverage at the 25-30 yd line, 40 yd from the returner (no wall, blocks lost within 0.3 s) | Wall rows hold at y 78 / 84 (14-22 yd in front of the returner) until the first coverage man comes within 7.5 yd; kickoff coverage shed bonus 0.7 -> 0.3 | EngineRealismTests.KickoffReturnWallHoldsRowsInFrontOfTheReturner |
| SIM-017 | fixed | S3 | Kickoff return | CarrierAI | ReturnerAssignment.ReturnLaneX was defined by the playbook but never used by the sim | Returner heads for his designed lane (guide weight 0.9) until the blocking shows a better one | EngineRealismTests.KickoffReturnWallHoldsRowsInFrontOfTheReturner |
| SIM-018 | fixed | S1 | Onside kick | SpecialTeamsAI | An expected onside kick was recovered by the kicking team 84% of the time (real ~12%): the receiving team had no hands team (front row 30 yd away) and the kicker's recovery roll (0.65) beat the receiver's (0.56) | Hands-team alignment (front row 10.8 yd, surprise kick 15.8 yd), nobody blocks, both teams pursue the landing spot, recoveries decided by the closest man with a 0.55 s retry lock, kicker roll 0.65 -> 0.29: kicker recovers ~28% (expected) / higher on the surprise kick | EngineRealismTests.OnsideKickRecoveryIsFarFromAutomatic |
| SIM-019 | fixed | S2 | Onside kick | SpecialTeamsAI.UpdateKickoff | Kicking-team lanes ran upfield past an onside kick at 0.92 speed and 21% of recoveries by the receiving team went 45+ yd for a touchdown (nobody left behind the ball) | Lane men pursue the ball and run at 0.80 until it is recovered | EngineRealismTests.OnsideKickRecoveryIsFarFromAutomatic |
| SIM-020 | fixed | S2 | Kick fielding | SpecialTeamsAI.UpdateLooseKick | Bouncing-ball recoveries iterated agents 0..21 so the kicking team's index order always rolled first and each agent rolled every tick (compounding chances) | Candidates sorted by distance to the ball; a failed attempt locks the player out for 0.55 s | EngineRealismTests.OnsideKickRecoveryIsFarFromAutomatic |
| SIM-021 | fixed | S1 | Wind | Core.MatchConditions (new) / BallPhysics / PlaySimOptions | There was no wind: kicks and deep balls flew identically in every game and the HUD had nothing to show (reference 39 shows a wind compass + mph) | MatchConditions (mph + compass degrees, deterministic Generate(seed) and ForQuarter drift, ToSimFrame(offenseAttacksPositiveZ), HudText/Label) on GameState.Conditions; BallPhysics.Wind (thread-static) acts on the ball through relative-air drag; PlaySimOptions.Wind / SetWind. 15 mph: punt 47 -> 38 into the wind / 55 downwind, 5.6 yd lateral drift, kickoff 67 -> 58 / 75, 54-yd FG 64% -> 55% into it | EngineRealismTests.WindMovesKicksAndPunts, EngineRealismTests.MatchConditionsAreDeterministic |
| SIM-022 | fixed | S2 | Wind | OffenseAI.Qb.ReleaseThrow, SpecialTeamsAI.DoFieldGoal | Planners would have compensated perfectly for the wind (solving the flight with it) or not at all | Kickers solve for still air (the wind moves the ball); passers allow for 55% of it; CPU placekickers aim off comp = 0.45 + 0.40 x accuracy of the drift and range | EngineRealismTests.WindMovesKicksAndPunts |
| SIM-023 | fixed | S1 | Kick meter | SimTypes.ControlInput / SpecialTeamsAI.Meter (new) / KickMeter | User kicks were struck automatically at a fixed moment with no player input (reference 36: TIMING Perfect / AIM Centered meter) | ControlInput.KickPress/KickPower/KickTiming/KickAim, PlaySimulation.AwaitingUserKick, SpecialTeamsAI.KickGate waits for the press (8 s timeout = mediocre reading); public KickMeter model (power 0.62-1.0 x timing, spread 1.0-4.1 x, aim +/-10 yd or 0.06 rad, labels Perfect..Poor / Centered..Way Left) | EngineRealismTests.KickMeterTimingAndAimChangeTheKick |
| SIM-024 | fixed | S2 | Fatigue | Sim.FatigueTracker (new) / PhysicalGameSim | Every body started every snap with Energy 1.0 (a WR burned 0.45% energy in a whole play): no fatigue ever accumulated across a drive | FatigueTracker keeps energy per player id across plays (PlaySimOptions.Fatigue), drain x6 plus 0.006/s in blocks, recovery between snaps / on the bench / at breaks; PhysicalGameSim uses it by default | EngineRealismTests.FatigueCarriesAcrossPlays |
| SIM-025 | fixed | S2 | QB movement | OffenseAI.Qb.PocketDrift | The passer only slid away from rushers; he never stepped up in the pocket when the edge was beaten and the middle was clean (QB later climb mean 0.0 yd) | Step-up: edge rusher losing outside + no free defender inside + >1 s into the play: +1.1 vertical push, never past 2.5 yd behind the line; 5% of dropbacks now climb >1 yd | EngineRealismTests.PocketHoldsForTheFirstTwoSeconds |
| SIM-026 | fixed | S2 | QB reads | OffenseAI.Qb HotReadEdge/HotReadBonus | With the slower, realistic ball the hot-routed receiver was thrown to only 7% of the time vs 2% without the flag (test failing) | HotReadEdge 0.10 -> 0.15, HotReadBonus 0.18 -> 0.26 | ContactSimTests.QuarterbackThrowsToTheHotRoutedReceiverMoreOftenThanToTheSameRouteWithoutTheFlag |
| SIM-027 | fixed | S2 | CPU clock management | PlayCaller.WantsTimeout / PhysicalGameSim | Headless CPU games never used a timeout: trailing teams let the two-minute clock run (only the live match director had the rule) | Pure PlayCaller.WantsTimeout (trailing / needs-clock team, < 130 s, offense waits for < 90 s) used by PhysicalGameSim before the runoff | EngineRealismTests.CpuTeamsUseTimeoutsInTheTwoMinuteDrill |
| SIM-028 | fixed | S3 | Sack rate | BlockEngagement.TryEngage | With the slower ball and cupped pocket the sack rate rose to 3.1 per team per game (NFL ~2.4) | Pass-set Lev 0.40 +/- 0.12 and first rush move 0.7-1.3 s: 2.38 sacks per team per game (200 games), sack time mean 2.26 s | SimCalibrationTests.PassingGameHasRealisticCompletionSackAndInterceptionRates |
| SIM-029 | fixed | S1 | Tackling | DefenseAI.TryTackleDive / TackleResolver | A defensive tackle dove through the center and tackled the quarterback 0.22 s after the snap (0.27% of run plays ended before 0.9 s: impossible distance and angle through a blocker) | PlaySimulation.BodyBetween: dive attempts and dive reach need a clear line, no body between tackler and carrier; early (<0.9 s) run ends 8 -> 0 per 3000 | EngineRealismTests.DefendersDoNotDiveThroughBlockers |
| SIM-030 | fixed | S2 | Smoothness | PlaySimulation.LiveStep | Contact impulses (hits, shed bursts, juke kicks, collision restitution) changed a player's velocity by up to 5-10 yd/s in ONE 1/60 s tick: mean worst tick acceleration 265 yd/s^2 (an animation pop on every contact) | Velocity step limiter: at most 1.25 yd/s per tick (75 yd/s^2) so impulses spread over 3-8 ticks (a dive is exempt: it is a deliberate lunge); worst tick 265 -> 75 yd/s^2. A first limit of 0.5 yd/s (30 yd/s^2) cut the goal-line QB sneak from 21% to 2% touchdowns and was relaxed | EngineAuditRulesTests.ContactImpulsesAreSpreadOverSeveralTicks; film accel 300 |
| SIM-031 | fixed | S2 | Boundaries | PlaySimulation.ConfineToSurroundings | Players chasing a thrown-away or out-of-bounds ball ran up to 18 yd outside the sideline (max X 71.3 on a 53.3-yd field) | Soft wall 3.2 yd outside the sidelines and 4 yd behind the end lines (velocity halved); max X 56.5 | EngineRealismTests.NobodyLeavesTheStadium |
| SIM-032 | fixed | S2 | Pursuit / reaction | DefenseAI.ReadDelay | Linebackers read run or pass in 0.35-0.65 s and defensive backs in 0.40-0.72 s: they filled the alley before the back had a hole (only 3.3% of runs gained 10-24 yd, ypc spiked on the rare breakaway) | Read delay LB +0.10 (0.45-0.77 s), DB +0.25 (0.65-0.97 s); with a 26 percent weaker run-block bias (0.33 -> 0.26) the 10+ share rises 5.1% -> 8.5% | SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks |
| SIM-033 | fixed | S2 | Ball carrier | CarrierAI.Update / CarrierMoves.Contest | Carriers ran the open field at 95.5% of top speed, dodging 40% of lone safeties (breakaway 40+ yd runs 2.07% of carries, real ~0.4%) | Carry cap 92.5% of top speed; juke / spin / stiff-arm contests -0.6 / -0.45 / -0.3 logit | SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks |
| SIM-034 | fixed | S3 | Penalties | BlockEngagement.MaybeHolding / CatchResolver.CheckInterference / DefenseAI.ManUpdate | Offensive holding was 1.8 per team-game (real ~1.0), illegal block in back 0.41, DPI 0.27, illegal contact 0.04 | Holding 0.10 -> 0.062, block-in-back 0.07 -> 0.04, DPI base 0.13 -> 0.19, illegal contact 0.0095 -> 0.026: 1.11 / 0.23 / 0.36 / 0.14 per team-game | SimCalibrationTests.PenaltiesAppearAtRealisticRatesWithHoldingLeading |
| SIM-035 | fixed | S1 | Man coverage | DefenseAI.ManUpdate | (QA-415) Off-man defenders aimed at a point BEHIND the receiver (toward the line of scrimmage): an off corner aligned 7 yd deep charged the line at the snap and ended up underneath his man; linebackers on backs/TEs charged the line (an accidental run blitz) | Defender plays in front of the receiver (rp + heading x cushion) while not beaten, trails on the hip (rp - heading x 0.9) once beaten; man defenders need +0.28 s to read the run; breakaway 40+ yd runs 2.07% -> 0.7% | EngineRealismTests.ManCornersHoldTheirCushionAtTheSnap |
| SIM-036 | fixed | S2 | Tackling | TackleResolver.Attempt | (QA-345) Clean-stop constant +7.5 gave ~88% clean stops and ~2% broken tackles for equal ratings: almost every contact ended the play, no NFL-like missed tackles | Constant 7.5 -> 2.0: DB broken/shoestring share ~12 -> 18% of contacts, run ypc +0.4 and more 10+ gains | SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks |
| SIM-037 | fixed | S2 | Turnovers | CatchResolver | After the man-coverage fix interceptions rose to 1.2 per team-game (NFL ~0.8): defenders caught too many balls they only reached | Defender pick chance 0.04+0.26 skill -> 0.03+0.22, x0.72 -> x0.58 on a beaten receiver, defender-only hands x0.75 -> x0.62, tipped-ball defender catch x0.7 -> x0.5: 0.90 INT per team-game | SimCalibrationTests.PassingGameHasRealisticCompletionSackAndInterceptionRates |
| SIM-038 | fixed | S2 | Run blocking | BlockEngagement | Climbing blockers had no edge on linebackers already moving downhill and combo peels needed Lev > 0.05 and age > 0.4 s: LBs were free on most runs (only 1.0 of 3 LB engaged per run) | Run bias +0.22 for blockers vs second-level defenders, peel at Lev > -0.1 and age > 0.25 | SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks |
| SIM-039 | fixed | S3 | Calibration | SimCalibrationTests / ThousandsOfPlays / CatchTests | Bands tuned for the old always-charging man coverage and 7.5 clean-stop constant | Bands re-fit: explosive-run floor 2.0 -> 1.0 %, concept floor 3.2 -> 3.0 / block floor 2.4 -> 2.0, Thousands run avg floor 3.5 -> 3.0, completion cap 70 -> 76, open-receiver catch 0.85 -> 0.82, penalty leader 25% -> 16%, hot-read test uses Cover 2 Zone | SimCalibrationTests |
| SIM-040 | fixed | S1 | Autopilot | Gameplay/Match/MatchDirector.BuildControl (QA-180) | Autopilot games were not CPU-vs-CPU: AutoInput drove the user-side quarterback with a blind throw at 1.7 s to the deepest receiver and steered a charging defender, so three autopilot games ended 6-0 / 6-0 / 6-7 with 24-29 snaps, 0 interceptions and 0 fumbles | Unless a scripted test move is forced (AutoMove), BuildControl returns an empty input while Autopilot is on, so the simulation AI plays the user's side too (the same AI the headless CPU games use) | film game 300 7: 27.3 pts/team, 64% completions, 2.1 sacks, 0.84 INT, 0.47 fumbles lost, 63 plays; Unity side cannot run headless (needs a play session) |
| SIM-041 | fixed | S1 | Stances | Sim.PreSnapStances (new), SimAgent.StanceKind, SimTypes.PreSnapStance (QA-306) | The sim had one pre-snap pose (AgentAction.Stance) for all 22 players, so the presentation could not tell an OL from a CB | New public enum PreSnapStance (ThreePoint, FourPoint, TwoPoint, Upright, Snapper, QbUnderCenter, QbShotgun, BackSet, PressCorner, OffCoverage, Linebacker, Safety, Kicker, KickoffRunner, HolderKneel, Returner, Gunner) and SimAgent.StanceKind, derived from position, alignment, formation and down and distance, refreshed after every shift, audible and defensive adjustment. AgentAction is unchanged (Action stays Stance until the snap) so no existing switch breaks. STANCE track: read SimAgent.StanceKind instead of re-deriving it | EngineAuditTests.EveryPositionGetsItsOwnPreSnapStance |
| SIM-042 | fixed | S1 | Kick formations | Sim.SpecialTeamsAI roles, Core formations (QA-048, QA-175, QA-223) | PAT, field-goal and kickoff units looked like single files with no kicker, holder or snapper | The Core formations are right (snapper on the ball, holder 7 yd back, kicker 8.5 yd back and 1.5 yd to the side, 7 on the line; kickoff: ten across the kicking line, kicker 5 yd behind); the engine now exposes SpecialTeamsAI.KickerAgent/HolderAgent/SnapperAgent and the stances HolderKneel, Snapper, Kicker, KickoffRunner so the view can pose them. The single-file look was the view re-using one clip for everybody | EngineAuditTests.KickFormationsHaveKickerHolderAndSnapperInPlace, KickoffTeamLinesUpAcrossTheFieldWithTheKickerBehind |
| SIM-043 | fixed | S1 | Blocking | BlockEngagement.TryEngage (QA-454, QA-405, QA-473) | Contact range was radius sum + 0.42 yd while interior DL line up 1.0-1.2 yd off the ball, so every OL/DL pair was already locked at the snap tick (no first step, no fire-out) and a show-blitzing MLB stood inside the center's reach | Engagement needs a first-step delay of 0.09 + 0.06 x (1 - agility) s after the snap and contact within radius sum + 0.34; show blitzers walk to their gap instead of standing on the line | SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks; film eng 300 |
| SIM-044 | fixed | S2 | Collisions | PlaySimulation.SeparateBodies, BodyCollision.ResolveAroundFallen (QA-315) | SeparateBodies skipped every pair with a fallen body, so players walked straight through downed players and a tackled carrier was not solid | A standing body is pushed around a lying one (lying radius x0.75, velocity into it removed); two lying bodies and blocked or tackle-locked pairs are still ignored | EngineAuditRulesTests.PlayerCannotWalkThroughAFallenBody |
| SIM-045 | fixed | S2 | Fumbles | PlaySimulation.Ball.UpdateLooseBall/Recover (QA-322, QA-324) | Loose-ball recovery added Rng x 0.35 to a skill score spanning 0.2-0.9 (an OL could beat a DB), 55% of hot balls were skipped and any opponent within 3.8 yd forced a fall-on | Random term 0.35 -> 0.16, hot-ball skip 55 -> 35%, scoop chance 0.55 in the open, 0.25 with a man 2.4-3.8 yd away, none when he is on top of him | film game: 0.47 fumbles lost per team-game (NFL ~0.5) |
| SIM-046 | fixed | S2 | Substitution | Sim.SimRosters.ForFormation, PhysicalGameSim, MatchFlow (QA-394) | Starters played every snap of every drive (no rotation, no rest) so fatigue could never matter | ForFormation takes the FatigueTracker: a DL, LB, RB, WR, TE or FB below 62% energy sits for the next player on the depth chart when he is within 14 overall points; quarterbacks, kickers and the secondary never rotate. The live MatchFlow now carries a FatigueTracker, rests everyone at quarter breaks and halftime | EngineAuditRulesTests.TiredStartersSitForRestedBackups |
| SIM-047 | fixed | S2 | Overtime | Core.GameState.TossForOvertime / OvertimeCoin (QA-479) | The overtime receiver was hard-coded to the home team in every played match (only QuickSim randomised it) | GameState.TossForOvertime(Rng) and an optional OvertimeCoin generator used when overtime starts; PhysicalGameSim seeds it. The live MatchFlow/MatchDirector should call TossForOvertime (open item for the Gameplay track) | EngineAuditRulesTests.OvertimeCoinTossGivesBothTeamsTheBallAcrossSeeds |
| SIM-048 | fixed | S2 | Penalties | Core.PlayResult.OffsettingPenalty, PenaltyEnforcer.Describe/Offset, PlaySimulation.Flag/FoulOpen (QA-489) | Offsetting and double fouls could never happen: one PenaltyCall per play, every foul roll was skipped once a flag was pending | A foul by the OTHER team on the same play is recorded as PlayResult.OffsettingPenalty; GameState replays the down with no yardage, PenaltyChoice.Offsetting tells the UI there is nothing to choose (MatchFlow.UserDecides returns false); the CPU game counts it as not accepted | EngineAuditRulesTests.OffsettingFoulsReplayTheDownWithoutYardage |
| SIM-049 | fixed | S2 | Overtime rules | Core.GameState.CollegeOvertimeEnd/ApplyTurnover (QA-480, QA-481) | College OT: a defensive touchdown on the first possession started the second possession instead of ending the game; a second-possession touchdown that already won the game still forced a pointless PAT | A defensive TD on the first possession ends the game; a go-ahead touchdown on the second possession skips the try | EngineAuditRulesTests.CollegeOvertimeDefensiveTouchdownOnTheFirstPossessionEndsTheGame, CollegeOvertimeWinningTouchdownOnTheSecondPossessionSkipsTheExtraPoint |
| SIM-050 | fixed | S3 | Punts | Core.GameState.ApplyPunt/ApplyKickoff (QA-482, QA-483) | A muffed punt or onside kick recovered by the kicking team in the receiver's end zone was never a touchdown and a blocked punt rolling backwards could not be a safety | A kicking-team recovery at or past the goal line scores a touchdown, at or behind its own goal line a safety | EngineAuditRulesTests.KickingTeamRecoveryInTheEndZoneScoresATouchdownOrASafety |
| SIM-051 | fixed | S2 | Clock | Core.GameState.ApplyPenaltyCore (QA-484) | No 10-second runoff: an accepted offensive foul at the end of a half only stopped the clock | An offensive foul in the last minute of a half with the clock running takes 10 seconds off it | EngineAuditRulesTests.OffensiveFoulInTheLastMinuteRunsTenSecondsOffTheClock |
| SIM-052 | fixed | S2 | CPU clock management | Core.PlayCaller.ShouldKneel/ShouldSpike/ShouldHailMary, GameState (QA-485, QA-496, QA-497) | The CPU kneeled at its own 1-yard line (safety), spiked on 2nd down with the clock already stopped and never threw a Hail Mary as time ran out | Kneel needs the ball at the 5 or beyond, a spike needs a running clock and a pending runoff, ShouldHailMary fires on the last snap of a half or game from 40-62 when the offense trails by 8 or less; PhysicalGameSim calls it | EngineAuditRulesTests.KneelNeverTakesAnOffenseIntoItsOwnEndZone, SpikeIsNotCalledWhenTheClockIsAlreadyStopped, HailMaryReceiversConvergeOnTheLandingZone |
| SIM-053 | fixed | S2 | Clock | Core.RuleSet (QA-477, QA-478) | Pro first downs stopped the clock for the whole last two minutes (120 s window) and college stopped it on every first down all game | Pro FirstDownStopWindowSeconds 0 (the clock keeps running after a first down), college ClockStopsOnFirstDown only inside the last two minutes of a half | GameStateTests (first-down clock cases) |
| SIM-054 | fixed | S2 | Penalties | Core.PenaltyEnforcer.Enforce (QA-486) | A foul on a voluntary two-point try was measured from the extra-point spot | The try spot of the play (two-point spot) is used when the play is a two-point try | EngineAuditRulesTests.FoulOnAVoluntaryTwoPointTryIsMeasuredFromTheTwoPointSpot |
| SIM-055 | fixed | S2 | Penalties | Core.Penalty.YardsFor (QA-487, QA-488) | College DPI used the pro spot rule (6-yard DPI cost 6) and banker's rounding made a 2.5-yard foul cost 2 but 3.5 cost 4 | College DPI is a flat 15, spot fouls round away from zero | EngineAuditRulesTests.CollegePassInterferenceIsAFlatFifteenAndFoulSpotsRoundAwayFromZero |
| SIM-056 | fixed | S3 | Penalties | Core.PenaltyCall.For (QA-493) | Kickoff offside and encroachment were blamed on the receiving team | On a kickoff the offender is the kicking team | EngineAuditRulesTests.KickoffOffsideIsBlamedOnTheKickingTeam |
| SIM-057 | fixed | S2 | Playbook validation | Core.Playbook.ValidateLegality (QA-509) | Validate checked slot counts only: the shipped I-Form Slot had 6 on the line | Validate now calls ValidateLegality: at least 7 on the line, at most 4 in the backfield, exactly one QB, linemen on the line, an eligible receiver outermost, every route inside the field | EngineAuditRulesTests.ShippedPlaybookHasNoIllegalFormationsOrOutOfBoundsRoutes, LegalityCheckCatchesSixOnTheLine; film legal2: 0 errors |
| SIM-058 | fixed | S2 | Routes | Core.RouteLibrary, OffenseAI.RouteRel (QA-511, QA-512, QA-320) | Corner and Corner2 routes ended at x 29.5-31.5 (past the 26.7 sideline), Go/Fade/Seam ran 20-30 yd past the end line near the goal line | Route X clamps to +-24.5 (Playbook.RouteHalfWidth) at build time and PlaySimulation.RouteRel clamps every waypoint to the field and to 107 at run time | EngineAuditRulesTests.ShippedPlaybookHasNoIllegalFormationsOrOutOfBoundsRoutes; EngineRealismTests.NobodyLeavesTheStadium |
| SIM-059 | fixed | S2 | Play design | Core.DefaultPlaybooks.Offense (QA-514, QA-515, QA-516, QA-517, QA-518, QA-519, QA-520, QA-522) | Trap Left carried the Right gap, Power Left had no kickout (the TE crossed the formation), Curl-Flat and TE Seam sent three players to one flat, Slants/Flood stacked two receivers, duplicate read orders sorted arbitrarily, HB=Flat tokens failed silently, WR Screen from Trips had one real route | Puller gap follows the direction, Power kicks out with the playside TE/slot/FB, F/HB specs moved to Chip or PassPro, Flood is Shotgun-only, SortStable on reads, WR Screen gets W*2 in Trips/Empty, spec tokens that do not apply are collected in SkippedSpecTokens instead of throwing | PlaybookTests; film legal2; SimCalibrationTests (every concept) |
| SIM-060 | fixed | S3 | Play design | Core.DefaultPlaybooks.Offense (QA-523, QA-528) | Goal Line had only runs and a fade; one typo in a spec string killed the whole static playbook | Goal Line Slant and Goal Line Boot added; Spec parsing is safe (bad tokens are skipped and reported) | film legal2 (0 legality errors with the new plays); PlaybookTests |
| SIM-061 | fixed | S2 | Play design | Core.DefaultPlaybooks.Defense (QA-529, QA-530, QA-531, QA-617, QA-618) | The cross blitz did not cross, Sam and Will were reversed, Nickel Cover 0 Blitz referenced a third linebacker that does not exist, Zone Blitz Dog sent two blitzers through one gap, Fire Zone was a 4-man rush | Cross blitz swaps the sides (A right / B left), Sam = right backer and Will = left, a Nickel-specific Cover 0 Blitz sends both backers through the B gaps, Zone Blitz Dog has CLeft/ALeft/CRight, Fire Zone is a real 5-man rush in 4-3 and 3-4 | EngineAuditRulesTests.BlitzCallsBringTheRushersTheirNamesPromise |
| SIM-062 | fixed | S2 | Play design | Core.DefaultPlaybooks.Defense (QA-532) | Goal Line Plug Cover 2 swapped gaps like a twist and its lone safety sat 14 yd deep (behind the end zone from the 1-3 yd line) | Pinch squeezes the ends to the B gaps and the tackles to the A gaps; goal-line safeties play at 6 yd | BlitzCallsBringTheRushersTheirNamesPromise (pinch builds); film legal2 |
| SIM-063 | fixed | S3 | Play design | Core.DefaultPlaybooks.Special (QA-539, QA-541) | Field Goal Safe Return was assignment-for-assignment identical to Block Middle and two men rushed the right B gap | Safe Return rushes only the four linemen and peels the linebackers back as blockers; the middle rush sends the outside backer straight up his own lane | film list; legality (11 assignments) |
| SIM-064 | fixed | S3 | Play design | Core.ZoneRegion.BuzzLeft, DefaultPlaybooks.Defense (QA-546) | Cover 3 Buzz always dropped the strong safety to the right | The buzz safety drops to the side he lines up on (BuzzLeft added) | DefaultPlaybooks build; film legal2 |
| SIM-065 | fixed | S3 | Play design | Core.DefaultPlaybooks.Defense (QA-504) | The 3-4 Base had 8 plays (no Cover 0, Cover 4, spy, stunt) against 22 each for the 4-3 and Nickel while PlayCaller sends 21/12/22 personnel to it 25-40% of the time | 3-4 Base gains Cover 4 Quarters, Cover 6, Spy QB, Twist Cover 3 and a 3-4 Cover 0 Blitz (13 plays) | EngineAuditRulesTests.BlitzCallsBringTheRushersTheirNamesPromise (3-4 count) |
| SIM-066 | fixed | S3 | Audibles | Core.AudibleBook (QA-536, QA-537) | Default audibles always named Right variants (a Left run audibled to a Right run) and Set accepted any slot or a play from another formation | Defaults keep the direction of the call; Set ignores a bad slot or a play that belongs to another formation | EngineAuditRulesTests.DefaultAudiblesFollowTheDirectionOfTheCall, AudibleSlotsRejectBadInput |
| SIM-067 | fixed | S3 | Hot routes | Core.PlayEditing.WithHotRoute (QA-538) | A hot route on a called run inserted the receiver into a read progression that a handoff play does not have | A hot route on a run call is a decoy: the receiver runs it but the quarterback keeps his handoff | ContactSimTests hot-read tests; PlayEditing |
| SIM-068 | fixed | S2 | Playbook API | Core.Playbook.FindPlay(name, formation), Merge (QA-544, QA-545) | FindPlay(name) returned the first play of that name from any formation (Cover 3 Sky exists three times); Merge doubled every play and then failed Validate | FindPlay(nameOrId, formationName) overload; Merge skips play ids it already has | EngineAuditRulesTests.FindPlayByNameCanBeScopedToAFormation, MergeDoesNotDoublePlays |
| SIM-069 | fixed | S2 | CPU play calling | Core.PlayCaller.FourthDown, PhysicalGameSim.KickerRange (QA-502) | Field-goal range was hard-coded to 54 yd for every kicker | The caller passes the kicker's own range | EngineAuditRulesTests.FourthDownRespectsTheKickersRange |
| SIM-070 | fixed | S3 | CPU clock management | Core.PlayCaller.IsHurryUp (QA-501) | The live CPU hurried up much later than the QuickSim CPU | Q2 from 1:40, Q4 trailing from 4:00, level from 1:40 | EngineAuditRulesTests.CpuHurryUpWindowMatchesTheQuickSimWindow |
| SIM-071 | fixed | S3 | CPU play calling | Sim.PhysicalGameSim.StyleOf (QA-622) | League.OffenseScheme was cast to Core.PlayStyle by int | Explicit mapping by name | EngineAuditRulesTests.CoachSchemeMapsToPlayStyleByName |
| SIM-072 | fixed | S3 | CPU play calling | Sim.PhysicalGameSim.WantsTwoPoint (QA-627, QA-183, QA-276) | The two-point decision was a flat 2% plus three cases | Late-game chart extended (trailing by 10, 13, 14) on top of -1/-2/-5/-8 and +5/+12; early-game tries stay at 2% | film game (two-point attempts 0.1 per game) |
| SIM-073 | fixed | S1 | Press coverage | Sim.SimAgent.JamTime, PlaySimulation.LiveStep (QA-368, QA-421) | JamTime was set by a won jam and never counted down: a jammed receiver ran at 35% speed for the entire play | JamTime counts down every tick; a won jam scales the route pace for the jam time only | EngineAuditSimTests.AWonJamEndsAndTheReceiverResumesHisRoute |
| SIM-074 | fixed | S2 | Snap | Sim.OffenseAI.OnSnap, PlaySimulation.Snapper (QA-363, QA-311) | The under-center exchange teleported the ball and the Snap event hard-coded agent 4 (the wrong man when the formation orders slots differently) | Short snap flight (7.5 yd/s, needs 0.1 s of flight) and the Snap event names the real center (PlaySimulation.Snapper, SpecialTeamsAI.SnapAgentId for kicks) | EngineAuditSimTests.TheUnderCenterSnapReachesTheQuarterbackInUnderAThirdOfASecond, TheSnapEventNamesTheCenter |
| SIM-075 | fixed | S2 | Snap | Sim.OffenseAI.Qb (QA-373) | After a bad snap the quarterback stood frozen in WaitSnap while the ball lay on the grass | The quarterback chases and falls on the loose ball | code review (OffenseAI.Qb bad-snap branch); the sim has no forced-bad-snap hook |
| SIM-076 | fixed | S2 | Pre-snap penalties | Sim.PlaySimulation.TryPreSnapPenalty (QA-312, QA-313, QA-465) | False start, offside and formation fouls were dice rolls on fixed slots (Agents[2..6], Agents[10]) even on punts and field goals, always the lowest-awareness lineman | Culprits come from the real alignment (less aware players more likely, the right unit and team), offside needs a defender over the ball, IllegalFormation is forced when fewer than 7 are on the line, kicks use their own linemen | EngineAuditSimTests.PreSnapFoulsAreNamedOnPlayersOfTheOffendingTeam, KickPlaysFlagOnlyTheirOwnLinemenForFalseStarts |
| SIM-077 | fixed | S2 | Motion | Sim.PlaySimulation.PreSnap (QA-333, QA-466, QA-332) | With AutoSnap the ball was snapped 0.6 s after setup, mid-stride for a man in motion; the defense never responded to motion; a hot route on the motion man left him running | The snap waits until the motion man has settled; DefenseMotionResponse shifts linebackers and safeties with the motion; a hot route cancels motion | ContactSimTests.MotionMovesTheReceiverAlongTheLineBeforeTheSnap; PlaySimulationTests |
| SIM-078 | fixed | S3 | Pre-snap | Sim.PlaySimulation.PreSnap (QA-334, QA-336) | The linebacker In shift moved an OLB who stood at 1.0 yd backwards, and CallPreSnapFoul could pin a defensive foul on an offensive player | In only moves up; the foul is re-pinned to a player of the offending team | PlaySimulationTests.PreSnapFoulsBlowTheSnapDeadWithNoPlay |
| SIM-079 | fixed | S2 | Fumbles | Sim.PlaySimulation.Ball.LooseBallOut (QA-321) | A fumble rolling out the back of the end zone was a plain out of bounds with the offense keeping the ball | Out the offense's own end line = safety, out the opponent's = touchback for the defense, sideline in the end zone handled the same way | PlaySimulation.Ball; film game (no stuck plays) |
| SIM-080 | fixed | S2 | Fumbles | Sim.PlaySimulation.Ball (QA-323, QA-326) | Fumble() put a downed carrier back to Normal state; an incomplete pass was killed dead on the grass | The carrier stays Down while he lies, incomplete balls bounce and roll before the whistle | LooseBallBouncesLowerEachTimeAndComesToRest; PlaySimulation.Ball |
| SIM-081 | fixed | S2 | Rules | Sim.PlaySimulation.Ball/TackleResolver (QA-327, QA-331, QA-328, QA-349) | Out of bounds tested the body centre while touchdowns used the ball; TD yardage used the state ball spot, not the line of scrimmage; the safety test used the body, the spot used the ball | Sideline and goal line both use ball and body, TD yards come from LosY, the safety test is ball-based, CheckFirstDown simplified | TackleTests; PlaySimulationTests; film game (no spot disputes) |
| SIM-082 | fixed | S2 | Sacks | Sim.PlaySimulation.Result (QA-329) | A strip-sack lost the Sack event and the sack stat | Strip-sacks keep the sack bookkeeping next to the fumble | TackleTests (sack plus fumble); film game |
| SIM-083 | fixed | S2 | Tackling | Sim.TackleResolver.Attempt (QA-344) | Tackle attempts never looked at the defender's facing: a back running away made a full-strength tackle | A defender facing away loses up to 12 points of margin | TackleTests; SimCalibrationTests (tackle bands) |
| SIM-084 | fixed | S2 | Tackling | Sim.TackleResolver (QA-346, QA-347, QA-471) | Push-out-of-bounds also fired on sacks and scrambles, teleported the carrier up to 1.3 yd in one frame and the elite-tackler condition was inverted | Push-out is probabilistic, tackling-skill positive, never on sacks and moves the body at most 0.45 yd | EngineAuditSimTests.NoPlayerEverTeleports; TackleTests (oob n/16) |
| SIM-085 | fixed | S2 | Tackling | Sim.TackleResolver (QA-457, QA-352) | After the throw the quarterback could not be tackled or hit at all and roughing was rolled for any tackle after the throw | QbHit event within 0.8 s of release, roughing needs a late hit (> 0.25 s after release) | PenaltyTests; film game (roughing 0.58 per team-game) |
| SIM-086 | fixed | S3 | Tackling | Sim.TackleResolver (QA-353, QA-354, QA-468, QA-432) | Break-free chance was multiplied by 0.1 per call, dragging tacklers collapsed onto the carrier when he stood still and a whiffing tackler could re-attempt every 0.32 s | pFree is per tick, the hold point has a direction guard, whiff cooldown 1.1-1.3 s and recovery tied to the fall | TackleTests |
| SIM-087 | fixed | S2 | Catching | Sim.CatchResolver.Engages (QA-355) | A defender who failed his one roll to play the ball was locked out of that pass forever | Lock lasts 0.3 s and is re-rolled | CatchTests |
| SIM-088 | fixed | S2 | Catching | Sim.CatchResolver.InBoundsCatch (QA-361) | A receiver with his body centre 0.14 yd inside the sideline caught the ball with his feet out | The in-bounds test uses the toes and the facing | CatchTests |
| SIM-089 | fixed | S2 | QB reads | Sim.OffenseAI _hotMode (QA-362) | Any 5-man rush triggered hot reads and a short drop even against six blockers | Hot only when the rushers outnumber the available protectors | ContactSimTests hot-read tests |
| SIM-090 | fixed | S2 | Robustness | Sim.OffenseAI.RunnerPreBall/Prepare (QA-364, QA-464) | A fake carrier with an empty path threw; a play whose slot 0 was not a QB assignment froze at the snap | Guards plus a fallback that gives the ball to the first carrier; PhysicalGameSim throws a descriptive InvalidOperationException (not a NullReferenceException) when a trimmed custom playbook has no play for a phase (QA-393) | PlaybookTests (custom plays); film game |
| SIM-091 | fixed | S2 | Passing | Sim.OffenseAI.TrackBall/EvalReceiver (QA-370, QA-382) | Receivers searched only 2 s of ball flight and EvalReceiver rejected anything over 2.7 s of flight | 70-step search and a 3.4 s evaluation limit | EngineRealismTests.PassFlightTimesAreRealistic; film throws |
| SIM-092 | fixed | S3 | Backs | Sim.OffenseAI.RunnerPreBall (QA-371, QA-469) | A back at his final waypoint walked into the quarterback at 2.5 yd/s | He holds at the mesh point | film game (ypc 4.5) |
| SIM-093 | fixed | S2 | Pitch | Sim.OffenseAI.Qb (QA-376, QA-474) | A toss only pitched with a back moving > 1.5 yd/s, otherwise the quarterback froze 1.1 s and kept it | Pitch deadline 0.8 s, reverse pivot not modelled (open for animation) | SimCalibrationTests (Toss Sweep average) |
| SIM-094 | fixed | S2 | Passing | Sim.OffenseAI.Qb.ThrowAway (QA-379, QA-384) | A throwaway happened on 4th down and ignored the pass rusher's side | No throwaway on 4th down and it avoids the rusher side | film game (intentional grounding 0.05) |
| SIM-095 | fixed | S3 | QB reads | Sim.OffenseAI.Qb.EnterSet (QA-380) | A rolling quarterback restarted his progression when he stopped | ReadIdx is kept | film qb |
| SIM-096 | fixed | S2 | User control | Sim.OffenseAI.Qb.UserQbInput (QA-385) | A user pushing the stick after 0.7 s turned the quarterback into a scrambler while still 3 yd behind the line in his drop | The scramble needs the set point or pressure | PlaySimulationTests.UserInputRunsAreDeterministicToo |
| SIM-097 | fixed | S3 | QB reads | Sim.OffenseAI.Qb (QA-383) | EvalReceiver assumed a 0.28 s windup but BeginThrow used 0.20-0.26 s | One shared windup constant | film throws |
| SIM-098 | fixed | S2 | Ball carrier | Sim.CarrierAI.LaneFinder (QA-386) | The lane search only fanned +-92 degrees so slow carriers and returners could not cut back | Wider fan (13 steps) when speed is below 4.5 yd/s | film game (ypc 4.5); KickoffReturn tests |
| SIM-099 | fixed | S2 | Ball carrier | Sim.CarrierMoves.Update/TryMove (QA-390, QA-392, QA-391) | The move contest resolved the instant the move started against a defender up to 2.8 yd away; spins went in a random direction | Contest deferred to the defender's reach (range x0.62) with a wasted-move penalty; spin goes away from the nearest threat | SimCalibrationTests (juke, spin, stiff-arm bands) |
| SIM-100 | fixed | S2 | Blocking | Sim.BlockerAI.AssignRunBlocks/LeadMove (QA-397, QA-398) | The careful non-crossing run-block matching was overwritten on the first tick and a lead blocker's route to the hole was overwritten by a direct line | ReactionClock 0.3 s after the snap honours the matching, LeadMove returns after setting Want | BlockingTests; SimCalibrationTests |
| SIM-101 | fixed | S2 | Pass protection | Sim.BlockerAI.PassProTarget (QA-400, QA-402) | Pass protection never retreated and kept a rusher he had already lost or one a teammate had engaged | Kick-slide shape (SIM-005..007) plus target release for beaten and teammate-engaged rushers | EngineRealismTests.PassProtection* tests |
| SIM-102 | fixed | S3 | Pass protection | Sim.BlockEngagement (QA-412) | Pass-pro pancakes needed Lev >= 1 for 1.4 s, which the kick-slide model never reaches | Pancake at Lev >= 0.85 for 0.9 s (3% per tick) | film game (sacks 2.1 per team-game) |
| SIM-103 | fixed | S3 | Blocking | Sim.BlockEngagement.BlockerScore (QA-414, QA-629) | Mass scoring was (mass - 80) / 80 on kg and TEs, FBs and RBs had no floor (a TE with RunBlocking 20 lost to every DL) | (mass - 70) / 100 and a floor of 42 for TE/FB/RB blocks | SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks |
| SIM-104 | fixed | S2 | Man coverage | Sim.DefenseAI.ManUpdate (QA-416, QA-417) | Man defenders mirrored the receiver's exact current velocity (a perfect read of every break) and safeties could be matched to a slot receiver while a CB took the TE | Reaction history 0.12-0.24 s, feed-forward x0.88, and a position-cost term in MatchMen | EngineRealismTests.SharpRouteBreaks...; film game |
| SIM-105 | fixed | S2 | Man coverage | Sim.DefenseAI.ManUpdate (QA-418) | A man defender whose receiver stayed in to block became a hook-zone defender at 8 yd for the rest of the play | He first looks for the nearest uncovered route runner within 13 yd (the checkdown) and only then sits in the hook zone | film game (completions 64%) |
| SIM-106 | fixed | S2 | Man coverage | Sim.DefenseAI (QA-420) | Illegal contact was rolled every tick a man defender was within 1.7 yd of a receiver | Only on real body contact in the jam window (p 0.05) | film game (illegal contact 0.09 per team-game) |
| SIM-107 | fixed | S2 | Zone coverage | Sim.DefenseAI.ZoneUpdate (QA-422, QA-423) | Zone defenders re-chose their receiver every tick and ignored receivers for the first 0.45 s | Hysteresis (ZoneTargetId) and reaction at 0.25 s | EngineRealismTests.ZoneDefendersPassOff... |
| SIM-108 | fixed | S2 | Pursuit | Sim.DefenseAI.PursueTarget (QA-425) | Every defender aimed at the same intercept point (the leverage comment had no code) | Contain/leverage: an outside defender keeps his outside shoulder; lead is 0.3 + 0.65 x awareness | AiTests.PursuitAnglesTightenWithAwareness; film game (ypc 4.5) |
| SIM-109 | fixed | S3 | Blitz | Sim.DefenseAI.BlitzUpdate (QA-428, QA-433) | A delayed blitzer stood frozen for up to 0.5 s and aimed at a point behind the line | He creeps to his gap at 35% speed during the delay | film game |
| SIM-110 | fixed | S2 | Pass rush | Sim.DefenseAI.RushUpdate (QA-429, QA-641, QA-427) | Linemen kept running at the quarterback while a pass was in the air and after a fumble | Rushers near the flight jump or turn to the ball, the rest pursue it; after a fumble the five nearest react at once | film game |
| SIM-111 | fixed | S3 | Reaction | Sim.DefenseAI.ReadDelay (QA-430, QA-438) | Read delays stacked with misdirection to nearly a second | LB +0.20 s, DB +0.30 s, man +0.28 s on a 0.20-0.52 s base, draw hold 0.30 + 0.30 x (1 - awareness) | SimCalibrationTests (run bands) |
| SIM-112 | fixed | S3 | Spy | Sim.DefenseAI.SpyUpdate/Responsibility (QA-434, QA-437) | The spy ignored a quarterback sliding out slower than 3 yd/s; Responsibility returned 1 for any rusher | Spy threshold 2.0 yd/s and distance-based responsibility for rushers | film game |
| SIM-113 | fixed | S3 | User control | Sim.DefenseAI.UserDefenderInput (QA-435, QA-436) | A neutral-stick user defender reacted to the ball instantly; Tackle and Dive were the same button | The assist honours the reaction window; Tackle is a standing wrap-up within 2 yd, only Dive leaves the feet | PlaySimulationTests (user input) |
| SIM-114 | fixed | S2 | Kickoff | Sim.SpecialTeamsAI.UpdateKickoff (QA-440, QA-441) | Coverage ran at 0.92 from t=0 and was 7 yd past the ball when it was kicked; a late kicker still kicked 0.6 s later from wherever | Coverage holds until 0.35 s before the kick and the kicker plants at the tee (fallback 1.4 s) | KickoffsGoDeepAndOnsideKicksStayShort; film st |
| SIM-115 | fixed | S2 | Punts | Sim.SpecialTeamsAI (QA-443, QA-446, QA-447, QA-450) | Fair catch was a hard distance threshold, any defender within 1.35 yd blocked a punt 55-85% of the time, gunners released 0.35 s late and muffs were 1 in 15 | Probabilistic fair catch from the nearest man, the call and awareness; block chance 0.20 + ...; gunners at 0.08 s; muff 0.935 + 0.055 x skill | SimCalibrationTests.PuntReturnsAreRarelyTouchdowns; film st |
| SIM-116 | fixed | S2 | Returns | Sim.SpecialTeamsAI.UpdateKickBall (QA-448) | If the designated returner was down or gone the ball was left to bounce | Any receiving-team player fields it when the returner is down | SimCalibrationTests (kick returns) |
| SIM-117 | fixed | S2 | Field goals | Sim.SpecialTeamsAI.DoFieldGoal (QA-633, QA-634, QA-635) | FG block was counted twice, the ball always passed the posts at the same height and accuracy hardly changed with distance | Block counted once, height scatter, wind compensation by the CPU kicker, accuracy by distance | KickPowerExtendsRangeAndAccuracyKeepsItStraight; FieldGoalSuccessFallsWithDistance; film game (FG 86%) |
| SIM-118 | fixed | S3 | Kickoff | Sim.SpecialTeamsAI (QA-637, QA-638) | The kickoff target was not clamped to the back of the end zone | Target Y <= 108.5; 160 kickoffs: 44% touchbacks, none past the end line | EngineAuditSimTests.KickoffsAreDeepWithAHealthyShareOfTouchbacksAndNoneLandBeyondTheEndLine |
| SIM-119 | fixed | S3 | Field goals | Sim.SimAssembly.GoalPost (QA-460) | Upright height was 12 yd x 3 = 36 yd (108 ft) instead of 15 yd | UprightTopYards = 15 (10 ft crossbar + 35 ft) | EngineAuditRulesTests.PunterLandsInsideTheUprightsOnlyWhenTheKickIsNotAbsurdlyHigh |
| SIM-120 | fixed | S3 | Code health | Sim (QA-199, QA-309, QA-348, QA-357, QA-365, QA-375, QA-389, QA-404, QA-451) | Dead branches (empty ifs, always-true conditionals, `&& false`, a no-op multiply by zero) and write-only fields (RetPhase, _landed, _firstTouchDone, _catchTime, DefReadRun, DefReadPass, BallBreakPoint, _pendingValid) | Removed | dotnet build -warnaserror-free of CS0169/CS0414/CS0219 in Sim |
| SIM-121 | fixed | S3 | Performance | Sim.BlockerAI/SpecialTeamsAI (QA-401, QA-461, QA-463) | Opponents() was an iterator allocated per call, PickupAssign was a shared static, a CarrierAI was created per agent per tick | Index loops without allocation, per-instance PickupAssign, cached CarrierAI | film game; no per-tick allocations in the blocker loop |
| SIM-122 | fixed | S3 | Determinism | Sim.Vec3.SortStable, AgentBody (QA-462, QA-340) | List.Sort is unstable on ties and the 40-yard table was built without a lock | SortStable everywhere in Sim and a lock around the table | EngineAuditSimTests.IdenticalSeedsGiveIdenticalFilms |
| SIM-123 | fixed | S2 | Locomotion | Sim.AgentBody (QA-337, QA-338, QA-341) | Braking skipped the friction circle (instant direction change), backpedal 7 yd/s and shuffle 9 yd/s were faster than real, a fallen body slid on at 9 yd/s decel | Friction circle on braking too, backpedal 0.46 + 0.10 x agility and shuffle 0.60 + 0.11 x agility of top speed, fallen deceleration 14 yd/s squared | film accel 300 (limit 75 yd/s squared); SimBodyTests |
| SIM-124 | fixed | S2 | Frame rate | Sim.PlaySimulation.Tick (QA-310) | A long frame (alt-tab, GC, load) ran hundreds of fixed steps in one frame and fast-forwarded the play | One Tick call simulates at most MaxTickSeconds (0.1 s) | EngineAuditSimTests.ALongFrameIsNotCaughtUpInOneTick |
| SIM-125 | fixed | S3 | Collisions | Sim.PlaySimulation.SeparateBodies (QA-316, QA-318) | The quarterback ghost exemption lasted the whole play and the broad-phase cull (1.1 yd) was a magic number | The ghost lasts 1.8 s only while the exchange happens and the cull derives from the radii | BlockingTests; film game |
| SIM-126 | fixed | S2 | Play length | Sim.PlaySimulation.ForceEnd (QA-314) | A scrimmage play over 22 s was whistled with the carrier wherever he was; kick returns longer than 22 s were cut off | The carrier is downed where he stands and kick plays may run 40 s | PlaySimulationTests.PhaseMachineGoesPreSnapLiveDeadAndStaysDead |
| SIM-127 | fixed | S3 | Tackling | Sim.TackleResolver (QA-185) | Big hits fired on routine tackles (7 of 12 best-play clips of one game) | Threshold margin > 31 and closing > 6.2 yd/s | TackleTests; film game |
| SIM-128 | fixed | S3 | Turnovers | Sim.TackleResolver (QA-351) | Fumbles lost were 0.35 per team-game against about 0.5 in the NFL | Fumble chance per tackle x0.92 (was x0.70) | film game 300 7: 0.47 |
| SIM-129 | fixed | S3 | Catching | Sim.CatchResolver (QA-358, QA-359) | Tipped balls went 28-68% to the offense and only 10-20% to the defense, and 20% of every overthrow was intercepted | Tip-drill defender chance 0.05 + 0.15 x skill (-0.06 at speed), defender-only hands x0.36, contested interception x0.30; 324 interceptions came from the tip drill alone before this | film game 300 7: 0.84 INT per team-game, EngineRealismTests; SimCalibrationTests.PassingGameHasRealisticCompletionSackAndInterceptionRates (3.8% of attempts) |
| SIM-130 | fixed | S3 | Smoothness | Sim.PlaySimulation.LiveStep (QA-337 follow-up) | The tackle itself popped both players' velocity by 4-6 yd/s in one tick (the velocity limiter was skipped by the early returns of the tick that ended the play) | The limiter runs after LiveStepCore on every tick, so a tackle, a hit or a collision is spread over several ticks everywhere | EngineAuditRulesTests.ContactImpulsesAreSpreadOverSeveralTicks |
| SIM-131 | fixed | S2 | Live game | Gameplay.Match.MatchFlow/MatchDirector.KickHud (SIM-021/024 integration) | The wind and fatigue models existed in the engine only: the live match had a decorative wind and every player fresh on every snap | MatchFlow generates MatchConditions from the game seed, passes the quarter's wind to every PlaySimOptions and keeps a FatigueTracker; the HUD wind arrow and mph read the real conditions | Unity play session (not runnable headless); MatchConditionsAreDeterministic; WindMovesKicksAndPunts |
| SIM-132 | fixed | S3 | Live game | Gameplay.Match.MatchFlow.CreateSim (QA-738) | The CPU rating boost was restored only after Setup returned: an exception left every roster player boosted for the rest of the game | Restore runs in a finally block | code review |
| SIM-133 | fixed | S3 | CPU play calling | Sim.PhysicalGameSim (QA-396) | The CPU onside-kick decision needed quarter 4, under 200 s and a 1-8 point deficit, so a team down 9-16 or in the last 30 seconds never tried one | Onside kick chosen by clock and deficit up to 16 points | film game |
| SIM-134 | fixed | S3 | Blocking | Sim.BlockerAI (QA-399) | Kick protection treated defenders as OL by the magic test `Side == Defense && Slot < 4` | Uses PositionGroup.IsOffensiveLine | BlockingTests |
| SIM-135 | fixed | S3 | Reaction | Sim.SimAgent.PosAgo (QA-459) | PosAgo(secondsAgo) was one sample off (the newest sample is the current position), so every reaction delay was a tick too short | Off-by-one fixed | EngineRealismTests.SharpRouteBreaks...; AiTests |
| SIM-136 | fixed | S3 | User control | Sim.PlaySimulation.SwitchUserPlayer (QA-467) | Player switching could pick a lineman or the quarterback on offense | The offense switches to a back or receiver (or the ball carrier) first | PlaySimulationTests |
| SIM-137 | fixed | S3 | Blocking | Sim.BlockerAI.RunDir (QA-472) | RunDir fell back to +1 (right) for every play without a CarryAssignment, so zone blockers shaded right on a left-side scramble | The fallback follows the quarterback's roll, the run path or the scramble | BlockingTests |
| SIM-138 | fixed | S3 | CPU play calling | Core.PlayCaller.Weight (QA-499) | FindFormation could return null for a custom play and Weight threw | A play whose formation is missing is never picked | PlayCallTests |
| SIM-139 | fixed | S2 | Penalties | Core.PenaltyEnforcer.Describe, MatchFlow.UserDecides (QA-164) | A dead-ball (pre-snap) foul offered ACCEPT / DECLINE - NO PLAY although it is always enforced | A dead-ball foul has no user choice: RecommendAccept is true and the panel shows the enforcement | EngineAuditRulesTests.DeadBallFoulsHaveNothingToDecline |
| SIM-140 | fixed | S1 | Data | League.CollegeFactory.ClassGap (QA-556) | College players were built with the pro age curve (average OVR 43.9, QBs 41) | Implemented by the UI track in CollegeFactory (class-year gap to ceiling: freshman 9 ... senior 1.5); logged here for the audit mapping only | LeagueTests (UI track) |
| SIM-141 | fixed | S3 | CPU play calling | Core.CoachProfile.FrontBias/NickelBias, PhysicalGameSim.ProfileOf (QA-621) | Coach.DefScheme only added +12 aggression; a Base34 coordinator still lined up in the 4-3 65% of the time | The scheme steers the base front (3-4 vs 4-3) and the nickel share in PickDefensiveFormation | EngineAuditRulesTests.DefensiveCoordinatorSchemeSteersTheBaseFront |
| SIM-142 | fixed | S2 | Roster | League.Offseason.AssignJersey (QA-560, QA-612) | Free-agent and emergency signings never checked jersey numbers (52 teams with duplicates) and name sets were local to each helper, so names repeated across the league after offseasons | One jersey assigner for every signing path and one league-wide used-name set for emergency signings, coach hires and the standalone draft class | LeagueEngineFixTests.JerseyNumbersStayUniqueOnEveryRosterAfterOffseasons, NamesStayUniqueAcrossTheLeagueAfterOffseasons |
| SIM-143 | fixed | S2 | Contracts | League.Offseason (QA-561, QA-562) | Dead cap never accrued (cutting a guaranteed deal was free) and EnforceCap shaved 6% off every salary above 3M hundreds of times | Cutting a guaranteed deal adds dead cap (half this season, half next); cap enforcement restructures the largest multi-year deals first, then releases the priciest backup | LeagueEngineFixTests.ReleasingAGuaranteedContractLeavesDeadCap, CapEnforcementRestructuresBigDealsInsteadOfShavingEverySalary |
| SIM-144 | fixed | S2 | Injuries | League.Season.SimWeek (QA-563) | HealWeekly ran before the games, so a one-week injury cost zero games | Only players hurt before kickoff heal after the week | LeagueEngineFixTests.AFreshOneWeekInjuryCostsTheNextGame |
| SIM-145 | fixed | S2 | Awards | League.Awards (QA-565, QA-566, QA-567) | Rookie of the Year stopped after season one, MVP equalled OPOY nearly every year and the interception leader list had a dead flag | ROY accepts DraftYear >= Year - 1, MVP weighs defenders and yields to a different OPOY winner, dead flag removed | LeagueEngineFixTests.RookieOfTheYearIsAwardedEverySeason, MvpIsNotAlwaysTheOffensivePlayerOfTheYear |
| SIM-146 | fixed | S2 | Schedule | League.ScheduleGenerator (QA-568, QA-569, QA-570, QA-571) | Pro weeks were a random matching (6-8 game home/away streaks, rematches, week 18 not divisional), the 17th-game host never alternated, college conferences reshuffled every year and weeks were greedy (28 games in week 14, 4 in week 15) | Best-of-16 week assignment with streak/rematch penalties and a swap repair, divisional finale, alternating 17th host, fixed conference rivals with a rotating stride, balanced college weeks with byes | LeagueEngineFixTests.ProScheduleHasShortStreaksNoRematchesAndADivisionalFinale, SeventeenthGameHostAlternatesInsideEveryDivision, CollegeConferenceGamesAreTheSameEverySeedAndBalancedAtHome, CollegeSeasonFitsFourteenEvenWeeksWithEveryGamePlaced |
| SIM-147 | fixed | S2 | Standings | League.Standings.BreakTie (QA-572) | Tiebreakers jumped from head-to-head to point differential | Strength of victory and of schedule come before point differential (common games and multi-team re-evaluation are not implemented) | LeagueEngineFixTests.TiedTeamsAreSeparatedByStrengthOfVictoryBeforePointDifferential |
| SIM-148 | fixed | S2 | Rosters | League.LeagueModel (QA-576, QA-577, QA-578) | ProSlots gave RG one player and the other line spots two; Starters ignored the second TE and the 4th WR and CB; the depth chart could list one backup at two positions | OL 10 / LB 6 slots, starters WR 4 / TE 2 / CB 4 (the extras at half weight in TeamRating), a backup fills one hole only | LeagueEngineFixTests.RosterSlotsGiveEveryLineSpotABackupAndSumToFiftyThree, StartersCoverTheBasePersonnelTheOffenseAndDefenseUse, ABackupFillsOnlyOneDepthChartHole |
| SIM-149 | fixed | S2 | Draft | League.Draft/LeagueFactory/Progression (QA-579, QA-580, QA-581) | Veteran draft rounds were exponential (a third first-rounders), standalone stars never reached the 90s and classes had 7 QBs and 0 punters | Rounds are near uniform, potential reaches the mid-90s and stars decline slower, classes reserve slots for every position group | LeagueEngineFixTests.VeteranDraftRoundsAreSpreadAcrossAllSevenRounds, StandaloneDraftClassReachesTheNinetiesAndCoversEveryPosition, CollegeDraftClassReservesSlotsForEveryPositionGroup |
| SIM-150 | fixed | S2 | QuickSim stats | League.QuickSimPlays (QA-591, QA-592, QA-593, QA-598, QA-600, QA-601, QA-605, QA-611) | Every penalty went to a random OL/DL, touchback punts recorded 0 yards, punts were measured from the line, OOB punts counted as returns, penalty yardage ignored enforcement, tackles all went to a single solo tackler, time of possession lost the seconds at quarter breaks, points ignored two-point tries and safeties | Penalties by type and named player, punt yards recorded and measured from 14 yd behind the line, OOB excluded, enforced yards, 38% assisted tackles with CBs in the pool, seconds credited at period end, StatLine.TwoPtMade/Safeties; SimTuning re-centred (RunMeanBase 4.7, CompBase about -0.02): about 22.8 pts per team | LeagueEngineFixTests.PenaltiesLandOnTheRightPositionGroups, TouchbackPuntsRecordTheirYards, GrossPuntAverageMatchesThePunterNotTheLineOfScrimmage, PuntReturnAttemptsExcludeKicksOutOfBounds, PenaltyYardsAreTheYardsActuallyEnforced, TacklesAreSharedAndCornersTackle, TimeOfPossessionAddsUpToNearlyTheWholeGame, TwoPointConversionsAndSafetiesCountAsPlayerPoints; QuickSimTests bands |
| SIM-151 | fixed | S3 | QuickSim | League.QuickSim (QA-594, QA-595, QA-596, QA-602, QA-603, QA-604, QA-608) | Pro overtime 4th downs never went for it, no flags in college overtime, college PAT equalled pro, throw weights could go negative, no red-zone completion penalty, the returner was always WR4 | Overtime desperation, pre-snap flags live in college overtime (special-teams flags still open), college PAT 0.965, clamped weights, red-zone completion drop, returner by skill | LeagueEngineFixTests.CollegeOvertimeGamesStillFinishWithPenaltiesOn, CollegeExtraPointsAreKickedMoreOftenThanProOnes, KickReturnersAreChosenBySkillNotAlwaysTheFourthReceiver |
| SIM-152 | fixed | S3 | League | League.SeasonCollege/Offseason/CollegeFactory/Awards (QA-573, QA-574, QA-575, QA-610, QA-614, QA-615, QA-619, QA-609) | Draft order tied bye teams with wild-card losers, bowls took sub-.500 teams, the CFP sort put a missing team first, the free-agent pool was gutted, school regions were arbitrary, scouting ignored team quality, morale was dead data, ProLeague.DraftOrder was unused | Order by last playoff week, bowls need 5 wins, missing teams sort last, deeper pool with real retirements, region from the state code, scouting follows quality, morale shifts re-sign odds, dead field removed | LeagueEngineFixTests.DraftOrderPutsWildCardLosersBeforeByeTeamsThatLostTheirFirstGame, EveryBowlTeamWonAtLeastFiveGames, FreeAgentPoolStaysDeepAndRetirementsAreRealPlayers, SchoolRegionComesFromTheStateCode, CoachScoutingFollowsTheTeamQualityOfTheHire, HappyPlayersAreReSignedMoreOftenThanUnhappyOnes |
| SIM-153 | fixed | S3 | Calibration | Sim.BlockEngagement run bias, CatchResolver base, DefenseAI holding (final pass) | Other tracks moved the goal-line defence 0.2 yd off the ball (neutral zone) and the tackle/limiter fixes shifted the game numbers: 28.9 pts, 4.96 ypc, 1.85 sacks | Run-block bias 0.33 -> 0.12, catch base 0.77 -> 0.755, a trailing man defender can be flagged for holding (0.17 per team-game, was 0.01), pass-rush set tuned; final 600-game numbers are in the table above | SimCalibrationTests (all bands), film game 600 21 |

## Notes for the other tracks

**STANCE / view track**
- `SimAgent.StanceKind` (new enum `PreSnapStance`) is the engine's answer to "which stance does this player hold": ThreePoint, FourPoint, TwoPoint, Upright, Snapper, QbUnderCenter, QbShotgun, BackSet, PressCorner, OffCoverage, Linebacker, Safety, Kicker, KickoffRunner, HolderKneel, Returner, Gunner. It is refreshed after every pre-snap shift, audible, motion and defensive adjustment. `AgentAction` was NOT extended (a new enum member would fall through the view's switches); `Action` stays `Stance` until the snap.
- Facing semantics are unchanged (offense faces +Y, defense -Y before the snap). Only the turn RATE changed (`AgentBody.TurnRate` 5.0 + 4.5 x agility rad/s, less for heavy players), so a view that snaps `Facing` to a target will now see it lag a little more on hard cuts: that is intended.
- Kick units: `SpecialTeamsAI.KickerAgent / HolderAgent / SnapperAgent` name the three men; the formations (holder 7 yd back, kicker 8.5 yd back, kickoff line across the field, kicker 5 yd behind) are in `Core/DefaultPlaybooks.Special.cs`.

**Gameplay / UI track**
- Wind: `GameState.Conditions` (a `MatchConditions`: mph and compass degrees, per quarter via `ForQuarter`). `MatchFlow` now generates it from the game seed, hands it to every `PlaySimOptions.SetWind` and the HUD arrow reads it. The kick meter model is ready but the live match does not drive it yet: set `ControlInput.KickPress/KickPower/KickTiming/KickAim` on the kicker (`KickMeter` has the shared maths, labels and cursor helpers); until the director does, the CPU kicks for the user exactly as before.
- `GameState.TossForOvertime(Rng)` exists; the live flow should call it when regulation ends tied.
- `PenaltyChoice.Offsetting` / `DeadBall` are set: there is nothing to choose for those two cases.
- `PlaySimulation.AwaitingUserKick` is true while a user kicker is waiting for the press (times out after 2.6 s, kickoffs 6 s).

## API additions (source compatible)

- Sim: `PreSnapStance`, `SimAgent.StanceKind`, `SimAgent.EngagedSeconds`, `PlaySimOptions.Wind / SetWind / Fatigue`, `PlaySimulation.Wind / AwaitingUserKick / Snapper / MaxTickSeconds / MaxKickPlaySeconds / MaxVelocityStep`, `ControlInput.KickPress / KickPower / KickTiming / KickAim`, `KickMeter` (public static model), `FatigueTracker`, `SimRosters.ForFormation(team, formation, fatigue)`, `SpecialTeamsAI.KickerAgent / HolderAgent / SnapperAgent`, `PhysicalGameSim.StyleOf`, `BodyCollision.ResolveAroundFallen`, `Physique.PadsFactor`.
- Core: `MatchConditions`, `GameState.Conditions / TossForOvertime / OvertimeCoin`, `PlayResult.OffsettingPenalty`, `PenaltyChoice.Offsetting / OffsettingCall`, `Playbook.ValidateLegality / RouteHalfWidth / FindPlay(name, formation)`, `PlayCaller.WantsTimeout / ShouldHailMary / FourthDown(s, c, maxFgYards)`, `CoachProfile.FrontBias / NickelBias`, `ZoneRegion.BuzzLeft`.

## Tests

EditMode count: the last complete headless Unity run (12:12, `Logs/editmode-engine.xml`) had 782 tests, 773 passed, 9 failed. Of those 9, five were engine-caused and are fixed since (spike clock rule, college first-down clock rule, dead-ball UserDecides, QB sneak at the goal line, plus the internal-visibility compile error in League tests found in the run before it); the other four (BodyMeshTests, LookTests, AnimationTests, OffseasonTests) belong to other tracks (OffseasonTests passes in the League harness). A second full Unity run after the final calibration could not start because another track's Unity batch process (an animation sequence check running for over an hour) held the project lock; the same suites pass outside Unity: 297 of 297 in the engine harness and 150 of 150 in the League harness. Re-run with the command in the lead brief to get the final Unity count. New engine tests: `EngineRealismTests` (speed, pocket, routes, coverage shells, kickoff wall, onside, wind, kick meter, fatigue), `EngineAuditTests`, `EngineAuditRulesTests`, `EngineAuditSimTests`, and `LeagueEngineFixTests` for the League rows. Existing tests whose bands encoded the old behaviour were re-fitted (SIM-039 and later rows name the reason): 40-yard bands, completion cap, explosive and concept floors, run-average floor 2.5, open-receiver catch rate 0.80, hot-read share (now 300 seeds), pass-finder completions 62%, puller movement 65%, punt return band, flag-rate cap (11%), pursuit awareness compared against awareness 5, and in the Gameplay tests the new rules: a spike needs a running clock, college first downs stop the clock only in the last two minutes of a half, a dead-ball foul has no user choice.

## Open realism gaps (honest list)

- Kickoff returns average about 11 yd (NFL about 22) and live punt returns about 10-14 yd (NFL about 9.5) while fair catches are 23% (NFL about 38%) and no punt goes out of bounds: the coverage reaches the returner late and the return wall is not yet a believable wedge. Punt net is 38 (NFL 41).
- Tempo: the median tackle comes 2.3 s into a run and plays still end a little early; linebackers' average tackle depth is about 2.5 yd.
- About 6.8 punts per team (NFL about 4.5 on this scoring level).
- Special-teams fouls (offside on a kick, roughing or running into the kicker, illegal shift) are not generated; the catalog has 16 fouls.
- The handoff and toss pitch have no exchange window or reverse-pivot (they need clips); the shed duration is calibrated, not animated.
- Live kick meter wiring (see notes above).

## Audit rows answered (QA-### from audit.md)

Format: `QA-### -> status (SIM-###)`; open and wontfix rows carry the reason.

- QA-012 -> fixed (SIM-001, SIM-002) - top speed in pads (free roam uses the same body)
- QA-013 -> wontfix - load-time hitch of the free-roam scene, not a simulation cost (the sim step is 0.2 ms per tick)
- QA-048 -> fixed (SIM-042)
- QA-057 -> open - the box score aggregation lives in the Gameplay/League stats layer, not in the simulation
- QA-058 -> fixed (SIM-040) - autopilot artifact
- QA-137 -> fixed (SIM-040) - autopilot artifact
- QA-164 -> fixed (SIM-139)
- QA-175 -> fixed (SIM-042)
- QA-180 -> fixed (SIM-040)
- QA-181 -> fixed (SIM-040) - counter was right, the blind autopilot QB was the cause
- QA-182 -> fixed (SIM-040)
- QA-183 -> fixed (SIM-072)
- QA-184 -> fixed (SIM-040)
- QA-185 -> fixed (SIM-127)
- QA-199 -> fixed (SIM-120)
- QA-223 -> fixed (SIM-042)
- QA-266 -> fixed (SIM-040)
- QA-273 -> fixed (SIM-040) - headless CPU games: 5% back-to-back defensive repeats, top call 8%
- QA-274 -> fixed (SIM-040) - headless CPU games: 0.9% back-to-back offensive repeats
- QA-275 -> wontfix - plays burn real elapsed time plus the runoff model; see the tempo item under open items
- QA-276 -> fixed (SIM-072)
- QA-277 -> fixed (SIM-040)
- QA-278 -> wontfix - a touchback spot and a return ending at the 20 are different results; the RETURN/KICKOFF tag is a Gameplay label
- QA-280 -> open - clock display at possession changes (Gameplay)
- QA-291 -> fixed (SIM-001, SIM-002, SIM-003, SIM-123)
- QA-305 -> open - box score fields (completions, rushing) are a Gameplay presentation issue
- QA-306 -> fixed (SIM-041)
- QA-309 -> fixed (SIM-120)
- QA-310 -> fixed (SIM-124)
- QA-311 -> fixed (SIM-074)
- QA-312 -> fixed (SIM-076)
- QA-313 -> fixed (SIM-076)
- QA-314 -> fixed (SIM-126)
- QA-315 -> fixed (SIM-044)
- QA-316 -> fixed (SIM-125)
- QA-317 -> wontfix - BodyCollision.Resolve already separates by mass ratio; restitution 0.05 is the intended inelastic contact
- QA-318 -> fixed (SIM-125)
- QA-319 -> wontfix - a punter at the back line really is 13 yd deep; the snap from the 3 is 12 yd in the NFL too
- QA-320 -> fixed (SIM-058)
- QA-321 -> fixed (SIM-079)
- QA-322 -> fixed (SIM-045)
- QA-323 -> fixed (SIM-080)
- QA-324 -> fixed (SIM-045)
- QA-325 -> wontfix - a pitch that hits the ground is a live ball and a fumble in the rules
- QA-326 -> fixed (SIM-080)
- QA-327 -> fixed (SIM-081)
- QA-328 -> fixed (SIM-081)
- QA-329 -> fixed (SIM-082)
- QA-330 -> wontfix - elapsed seconds per play type are calibrated (pass 4.5 s, run 3.3 s, incompletion 3.9 s); the tempo gap is listed under open items
- QA-331 -> fixed (SIM-081)
- QA-332 -> fixed (SIM-077)
- QA-333 -> fixed (SIM-077)
- QA-334 -> fixed (SIM-078)
- QA-335 -> wontfix - only men off the line may go in motion (legal), the line count is unaffected
- QA-336 -> fixed (SIM-078)
- QA-337 -> fixed (SIM-123)
- QA-338 -> fixed (SIM-123)
- QA-339 -> fixed (SIM-024)
- QA-340 -> fixed (SIM-122)
- QA-341 -> fixed (SIM-123)
- QA-342 -> wontfix - radius is a collision constant, the visible width belongs to the model track
- QA-343 -> open - bounce restitution is random per bounce; no angle-of-incidence model for the prolate ball
- QA-344 -> fixed (SIM-083)
- QA-345 -> fixed (SIM-036)
- QA-346 -> fixed (SIM-084)
- QA-347 -> fixed (SIM-084)
- QA-348 -> fixed (SIM-120)
- QA-349 -> fixed (SIM-081)
- QA-350 -> open - IgnoreAgent holds one defender id
- QA-351 -> fixed (SIM-128)
- QA-352 -> fixed (SIM-085)
- QA-353 -> fixed (SIM-086)
- QA-354 -> fixed (SIM-086)
- QA-355 -> fixed (SIM-087)
- QA-356 -> fixed (SIM-034)
- QA-357 -> fixed (SIM-120)
- QA-358 -> fixed (SIM-129, SIM-037)
- QA-359 -> fixed (SIM-129)
- QA-360 -> open - CheckPitch only lets the intended receiver collect
- QA-361 -> fixed (SIM-088)
- QA-362 -> fixed (SIM-089)
- QA-363 -> fixed (SIM-074)
- QA-364 -> fixed (SIM-090)
- QA-365 -> fixed (SIM-120)
- QA-366 -> fixed (SIM-009)
- QA-368 -> fixed (SIM-073)
- QA-369 -> open - FindSoftSpot shares ReactionClock/Landmark with other AI
- QA-370 -> fixed (SIM-091)
- QA-371 -> fixed (SIM-092)
- QA-373 -> fixed (SIM-075)
- QA-374 -> fixed (SIM-025)
- QA-375 -> fixed (SIM-120)
- QA-376 -> fixed (SIM-093)
- QA-377 -> open - the handoff still hands the ball over without an exchange window (presentation needs the clip)
- QA-378 -> wontfix - the 0.45 openness gate is part of the calibrated throw model (completion 64%, throwaways 0.05 per team-game)
- QA-379 -> fixed (SIM-094)
- QA-380 -> fixed (SIM-095)
- QA-381 -> fixed (SIM-014)
- QA-382 -> fixed (SIM-091)
- QA-383 -> fixed (SIM-097)
- QA-384 -> fixed (SIM-094)
- QA-385 -> fixed (SIM-096)
- QA-386 -> fixed (SIM-098)
- QA-387 -> open - ReactionClock is shared between carrier, coverage and throw logic
- QA-388 -> fixed (SIM-033)
- QA-389 -> fixed (SIM-120)
- QA-390 -> fixed (SIM-099)
- QA-391 -> wontfix - a diving runner is on the ground when the dive ends; AgentA -1 is the "self" convention of the event
- QA-392 -> fixed (SIM-099)
- QA-393 -> fixed (SIM-090)
- QA-394 -> fixed (SIM-046)
- QA-395 -> open - the stand-in player for a missing slot still carries id 0
- QA-396 -> fixed (SIM-133)
- QA-397 -> fixed (SIM-100)
- QA-398 -> fixed (SIM-100)
- QA-399 -> fixed (SIM-134)
- QA-400 -> fixed (SIM-005, SIM-006, SIM-101)
- QA-401 -> fixed (SIM-121)
- QA-402 -> fixed (SIM-101)
- QA-403 -> fixed (SIM-004)
- QA-404 -> fixed (SIM-120)
- QA-405 -> fixed (SIM-043)
- QA-406 -> wontfix - outcome rates are calibrated (2.1 sacks per team-game, run-block averages in band) rather than the drift constants
- QA-407 -> wontfix - same as QA-406
- QA-408 -> open - holding is still rolled against blocker 0 only
- QA-409 -> fixed (SIM-034)
- QA-410 -> fixed (SIM-005)
- QA-411 -> open - a longer shed (0.24-0.50 s) cut sacks to 1.67 per team-game, so the calibrated 0.14-0.34 s stays; the animation lead time belongs to the view
- QA-412 -> fixed (SIM-102)
- QA-413 -> open - combo peel side check
- QA-414 -> fixed (SIM-103)
- QA-415 -> fixed (SIM-035)
- QA-416 -> fixed (SIM-104)
- QA-417 -> fixed (SIM-104)
- QA-418 -> fixed (SIM-105)
- QA-419 -> fixed (SIM-010) - PressResolved replaced the shared MoveResolved flag
- QA-420 -> fixed (SIM-106)
- QA-421 -> fixed (SIM-073)
- QA-422 -> fixed (SIM-107)
- QA-423 -> fixed (SIM-107)
- QA-425 -> fixed (SIM-108)
- QA-426 -> open - dive attempts still roll per tick
- QA-427 -> fixed (SIM-110)
- QA-428 -> fixed (SIM-109)
- QA-429 -> fixed (SIM-110)
- QA-430 -> fixed (SIM-111)
- QA-431 -> fixed (SIM-104) - facing-based watch test replaced the commented line
- QA-432 -> fixed (SIM-086)
- QA-433 -> fixed (SIM-109)
- QA-434 -> fixed (SIM-112)
- QA-435 -> fixed (SIM-113)
- QA-436 -> fixed (SIM-113)
- QA-437 -> fixed (SIM-112)
- QA-438 -> fixed (SIM-111)
- QA-439 -> fixed (SIM-042) - roles come from the KickAssignment role
- QA-440 -> fixed (SIM-114)
- QA-441 -> fixed (SIM-114)
- QA-442 -> open - the kicker still runs a straight line to the tee
- QA-443 -> fixed (SIM-115)
- QA-444 -> fixed (SIM-020)
- QA-445 -> fixed (SIM-020)
- QA-446 -> fixed (SIM-115)
- QA-447 -> fixed (SIM-115)
- QA-448 -> fixed (SIM-116)
- QA-449 -> wontfix - a returner who catches inside the 3-yard line really decides between running and kneeling
- QA-450 -> fixed (SIM-115)
- QA-451 -> fixed (SIM-120)
- QA-453 -> wontfix - a kick that comes to rest in the end zone is a touchback whatever its path
- QA-454 -> fixed (SIM-043)
- QA-457 -> fixed (SIM-085)
- QA-459 -> fixed (SIM-135)
- QA-460 -> fixed (SIM-119)
- QA-461 -> fixed (SIM-121)
- QA-462 -> fixed (SIM-122)
- QA-463 -> fixed (SIM-121)
- QA-464 -> fixed (SIM-090)
- QA-465 -> fixed (SIM-076)
- QA-466 -> fixed (SIM-077)
- QA-467 -> fixed (SIM-136)
- QA-468 -> fixed (SIM-086)
- QA-469 -> fixed (SIM-092)
- QA-471 -> fixed (SIM-084)
- QA-472 -> fixed (SIM-137)
- QA-473 -> fixed (SIM-043, SIM-078)
- QA-474 -> open - the toss pitch has no reverse-pivot clip (deadline fixed in SIM-093)
- QA-475 -> open - quick-drop set time
- QA-476 -> wontfix - a user who takes over a blocker leaves the assignment by choice
- QA-477 -> fixed (SIM-053)
- QA-478 -> fixed (SIM-053)
- QA-479 -> fixed (SIM-047) - the live MatchFlow should call GameState.TossForOvertime (Gameplay)
- QA-480 -> fixed (SIM-049)
- QA-481 -> fixed (SIM-049)
- QA-482 -> fixed (SIM-050)
- QA-483 -> fixed (SIM-050)
- QA-484 -> fixed (SIM-051)
- QA-485 -> fixed (SIM-052)
- QA-486 -> fixed (SIM-054)
- QA-487 -> fixed (SIM-055)
- QA-488 -> fixed (SIM-055)
- QA-489 -> fixed (SIM-048)
- QA-490 -> wontfix - a foul on a punt or field goal is a scrimmage foul with a replayed down, as in the NFL
- QA-491 -> open - the catalog still has 16 fouls (illegal shift, ineligible downfield, roughing the kicker, ...)
- QA-493 -> fixed (SIM-056)
- QA-494 -> open - ShouldAccept ignores clock and timeouts
- QA-495 -> wontfix - challenges are a feature, not a bug; the rule data is documented as unused
- QA-496 -> fixed (SIM-052)
- QA-497 -> fixed (SIM-052)
- QA-498 -> fixed (SIM-070) - the dead branch went with the hurry-up rewrite
- QA-499 -> fixed (SIM-138)
- QA-501 -> fixed (SIM-070)
- QA-502 -> fixed (SIM-069)
- QA-503 -> open - SuggestDefense still returns fixed names
- QA-504 -> fixed (SIM-065)
- QA-509 -> fixed (SIM-057)
- QA-511 -> fixed (SIM-058)
- QA-512 -> fixed (SIM-058)
- QA-513 -> wontfix - RouteSpeed is the quarterback's timing estimate, not the receiver's speed; 8.2 was tried: +18 pass yards and -0.3 sacks per team-game
- QA-514 -> fixed (SIM-059)
- QA-515 -> fixed (SIM-059)
- QA-516 -> fixed (SIM-059)
- QA-517 -> fixed (SIM-059)
- QA-518 -> fixed (SIM-059)
- QA-519 -> fixed (SIM-059)
- QA-520 -> fixed (SIM-059)
- QA-521 -> fixed (SIM-052)
- QA-522 -> fixed (SIM-059)
- QA-523 -> fixed (SIM-060)
- QA-525 -> wontfix - Right/Left plays plus a Flip toggle are the play-list design
- QA-526 -> fixed (SIM-059) - the motion man is chosen from the side opposite the run
- QA-527 -> open - Pistol is still built as shotgun
- QA-528 -> fixed (SIM-060)
- QA-529 -> fixed (SIM-061)
- QA-530 -> fixed (SIM-061)
- QA-531 -> fixed (SIM-061)
- QA-532 -> fixed (SIM-062)
- QA-533 -> wontfix - a five-man rush cannot cover both A gaps
- QA-534 -> wontfix - man matching is done at runtime; the fixed TargetSlot is only a tie-break and a diagram hint
- QA-535 -> fixed (SIM-066) - the UI already toasts "NOT AVAILABLE IN THIS FORMATION" (MatchDirector.PreSnap)
- QA-536 -> fixed (SIM-066)
- QA-537 -> fixed (SIM-066)
- QA-538 -> fixed (SIM-067)
- QA-539 -> fixed (SIM-063)
- QA-541 -> fixed (SIM-063)
- QA-542 -> open - Onside Short/Surprise differ only by aim; no squib or trick variants
- QA-543 -> wontfix - new Position enum members would ripple through ratings, rosters and the depth chart
- QA-544 -> fixed (SIM-068)
- QA-545 -> fixed (SIM-068)
- QA-546 -> fixed (SIM-064)
- QA-550 -> wontfix - Rng.Range(0, 0) returning min is documented and relied on by defensive callers
- QA-556 -> fixed (SIM-140)
- QA-560 -> fixed (SIM-142)
- QA-561 -> fixed (SIM-143)
- QA-562 -> fixed (SIM-143)
- QA-563 -> fixed (SIM-144)
- QA-565 -> fixed (SIM-145)
- QA-566 -> fixed (SIM-145)
- QA-567 -> fixed (SIM-145)
- QA-568 -> fixed (SIM-146)
- QA-569 -> fixed (SIM-146)
- QA-570 -> fixed (SIM-146)
- QA-571 -> fixed (SIM-146)
- QA-572 -> fixed (SIM-147) - partial: common games and multi-team re-evaluation are not implemented
- QA-573 -> fixed (SIM-152)
- QA-574 -> fixed (SIM-152) - bowl slots can stay empty; conference tie-ins not added
- QA-575 -> fixed (SIM-152)
- QA-576 -> fixed (SIM-148)
- QA-577 -> fixed (SIM-148)
- QA-578 -> fixed (SIM-148)
- QA-579 -> fixed (SIM-149)
- QA-580 -> fixed (SIM-149)
- QA-581 -> fixed (SIM-149)
- QA-582 -> open - compensatory picks need free-agent-loss tracking across years
- QA-591 -> fixed (SIM-150)
- QA-592 -> fixed (SIM-150)
- QA-593 -> fixed (SIM-150)
- QA-594 -> fixed (SIM-151) - partial: hurry and milk logic unchanged in overtime
- QA-595 -> fixed (SIM-151) - partial: kickoff, punt and field-goal flags are still open (need Core kick enforcement)
- QA-596 -> fixed (SIM-151)
- QA-598 -> fixed (SIM-150)
- QA-600 -> fixed (SIM-150)
- QA-601 -> fixed (SIM-150)
- QA-602 -> fixed (SIM-151)
- QA-603 -> fixed (SIM-151)
- QA-604 -> fixed (SIM-151) - partial: rotation, fatigue and in-game injuries in QuickSim are open
- QA-605 -> fixed (SIM-150)
- QA-606 -> wontfix - at 3000 games shutouts are 0.33% and 50+ point scores 0.3% of team-games, close to NFL norms; the audit sample was 272 games
- QA-607 -> open - a pro overtime rule design in Core/RuleSet (measured tie rate 0.4-0.9%)
- QA-608 -> fixed (SIM-151)
- QA-609 -> fixed (SIM-152)
- QA-610 -> fixed (SIM-152)
- QA-611 -> fixed (SIM-150)
- QA-612 -> fixed (SIM-142)
- QA-614 -> fixed (SIM-152)
- QA-615 -> fixed (SIM-152)
- QA-617 -> fixed (SIM-061)
- QA-618 -> fixed (SIM-061)
- QA-619 -> fixed (SIM-152)
- QA-621 -> fixed (SIM-141)
- QA-622 -> fixed (SIM-071)
- QA-623 -> open - no illegal-forward-pass flag for a user QB who throws after crossing the line
- QA-624 -> open - user QB throw gating during fakes
- QA-625 -> wontfix - the tick is a fixed 1/60 s so RNG consumption is frame-rate independent
- QA-626 -> open - SimRosters alternatives still omit QB and LB/DL cases
- QA-627 -> fixed (SIM-072)
- QA-629 -> fixed (SIM-103)
- QA-630 -> open - OnPassThrown predicts only the z=1.6 catch point
- QA-631 -> wontfix - only corners ignore the fake (intended)
- QA-632 -> open - punter walk/launch offset
- QA-633 -> fixed (SIM-117)
- QA-634 -> fixed (SIM-117)
- QA-635 -> fixed (SIM-117)
- QA-637 -> fixed (SIM-118)
- QA-638 -> fixed (SIM-118)
- QA-639 -> wontfix - a blocked field goal is still an attempt of its nominal distance
- QA-640 -> wontfix - the test hooks cost one branch per tackle contest
- QA-641 -> fixed (SIM-110)
- QA-642 -> open - catch radius uses the lagging facing
- QA-738 -> fixed (SIM-132)

## v0.6.0 release integration rows (release integrator)

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| SIM-154 | fixed | S2 | run game | BlockEngagement run-mode leverage bias | After the realism pass every run concept averaged 2.7 - 3.9 yd (Power 2.70, Dive 2.83, overall 3.39) against 4.3 in the NFL; `SimCalibrationTests` Read Option 2.63 and Jet Sweep 2.61 failed their bands | run-mode leverage bias 0.12 -> 0.30: blocks hold longer; concepts now 3.0 - 4.4 (overall 3.89), pass-mode untouched | `SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks` / `PullingAndMisdirectionRunsAreNoLongerLosingPlays`, engine harness 297 / 297 |
| SIM-155 | fixed | S2 | league | Offseason roster fill / EnforceCap | A standalone offseason (no college world) could end with 48 - 52 players on a roster: the last of three fill passes was followed by a cap-forced release (10 of 12 seeds hit it within ten seasons) | six fill passes, then a final fill that is never followed by a cut (`EnforceCap(..., allowCuts: false)` restructures / scales the largest deals instead) | `OffseasonTests.StandaloneOffseasonWithoutACollegeWorldAlsoSustainsTheLeague` (12 seeds x 10 seasons checked in the harness) |
| SIM-156 | fixed | S3 | test | MatchV030Tests college overtime | the test assumed both teams always get the ball; a defensive score (pick-six, fumble return, safety) legitimately ends college overtime at once | the assertion allows `seen.Count < 2` only when the last play was a defensive score (message names seed, score and last play) | `MatchV030Tests.CollegeOvertimeAlternatesPossessionsFromThe25AndEndsWithAWinner` |
| SIM-157 | fixed | S3 | autotest | AutoTest.V3 flag tour | the penalty accept / decline tour forced a pre-snap offside (a dead-ball foul with nothing to decide since QA-164) and then waited the full 12 minute budget for a decision | a dead-ball flag counts as accepted and the decline path as not applicable | `autotest2`: v3 total 36 checks ok, 0 failed |
