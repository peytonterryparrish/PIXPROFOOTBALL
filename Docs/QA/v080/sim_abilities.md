# v0.8.0 Sim integration: abilities, morale, staff tendencies

Owner area: `Scripts/Sim` (plus the `CoachProfile` fields in `Scripts/Core/PlayCaller.cs`), tests in `Tests/EditMode/Sim`. Nothing in `Scripts/UI`, `Gameplay` or the Career data model was touched except one comment line in `Abilities.cs`.
Method: the headless harness (`%TEMP%\pixsim`: `tune game 600|6000`, the dotnet copy of the Sim EditMode suite) for every number below; the same tests ran in Unity at the end (see "Gates").

## What was wired

| Piece | Where | Notes |
|---|---|---|
| Ability lookup | `Sim/AbilityFx.cs` (new) | the player's `AbilityCatalog` effects flattened to (hook, context bit, value) when the agent is built; `null` for a player with no ability, so a league without abilities draws exactly the same random numbers as before (test `APlayerWithoutAbilitiesAndNeutralMoraleIsExactlyTheOldPlayer`, 30 seeds, event-log digests identical) |
| Rating-point hooks and morale | `Sim/PlaySimulation.Abilities.cs` (new), called from `PlaySimulation.MakeAgent` (`a.R = player.Ratings` is now followed by `BakeAbilities`) | contexts that are known at the snap are baked into `SimAgent.R`: third / fourth down, two-minute drill, red zone, short yardage, close game late (4th quarter or OT within 8 points), kick return, man coverage, blitz, pass play / run play |
| Dynamic contexts | same file `UpdateAbilityBodies` every tick, plus the use sites below | open field, deep route, under pressure, over the middle, line of scrimmage |
| Morale | `BakeAbilities` | `MoraleEngine.RatingModifier(player)` (-2..+2) added to every rating except Injury, rounded to whole points |
| Leadership | `LeadershipOf` | sum of the Locker Room Leader effects on the field (cap 4.5) x 0.34 rating points for the whole unit |
| Body hooks | `AgentBody` (`AbilityVMax`, `AbilityTau`, `CutBleed`, `DrainScale`) | top speed, acceleration, cut cost and tiring multipliers, set per tick, 1 for everybody without abilities |
| Staff play calling | `CoachProfile.BlitzBias / ManBias / DeepShotBias / NoHuddleBias` (Core), filled by `PhysicalGameSim.ProfileOf` from `StaffManager.Tendencies` | read by `PlayCaller.CallDefense` / `Weight`, and the tempo by `PhysicalGameSim.ApplyTempo` |
| Switches | `PlaySimOptions.PlayerModifiers` (default true), `PhysicalGameSim.PlayerModifiers`, `PhysicalGameSim.StaffTendencies`, `AbilityFx.Mute(hooks)` (tuning) | the live match (`MatchFlow`) builds `PlaySimOptions` without naming the flag, so abilities and morale apply there without any Gameplay change |

Use sites: `CarrierMoves.Contest` (juke / spin), `TackleResolver` (`Attempt`, `Resolve`, new `FumbleChance`), `CatchResolver` (`CatchBonus`, `BallHawk`, duel), `OffenseAI.Qb` (`ReleaseThrow` accuracy, `StyleSpeed` velocity), `OffenseAI.RunRoute` (break burst), `DefenseAI` (`BlitzUpdate`, `PressJam`, `ManUpdate`, `ZoneUpdate`), `BlockEngagement` (`BlockerScore`, `ApplyMove`), `FatigueTracker` (sees `DrainScale` through the body's energy).

New diagnostics: `PlaySimulation.LastThrowAimError / LastThrowDistance / LastThrowPressure / LastThrowSpeed`, `PlaySimulation.PlayContext`, `SimAgent.PlayerModPoints`.

## The calibration gain (read this before changing a number)

The catalogue sizes are what the player card shows. The Sim applies them times `AbilityFx.Scale(hook, context)` (and the speed hooks times `SpeedGain` 2 / `ReturnSpeedGain` 4 / `PullSpeedGain` 3 in `PlaySimulation.Abilities.cs`, because a tier-3 "+3 Speed" is only 1 percent of top speed):

| Hook | Gain | Why |
|---|---|---|
| TopSpeed, Acceleration | 0.4 (0.8 in the open field) | with the speed gain, a tier-3 Burner is +2 Speed on vertical routes |
| BreakTackle, JukeSuccess, SpinSuccess, CatchInTraffic, ContestedCatch, CutSpeedLoss, RouteSeparation | 0.5 | league-wide offense inflation, see below |
| YardsAfterCatch | 0.4 | same |
| RunBlock, PassBlock | 0.4 | 185 linemen carry them |
| BullRushResist | 0.8 | |
| PassRush, RunStop, TackleReliability, ManCoverage, ZoneRange | 1.5 | the defense side was the weak side of the budget |
| InterceptionCatch | 0.45 | interceptions were +8 percent |
| everything else | 1.0 | |

Why a gain at all: with the catalogue's distribution (`AssignInitial` on every roster player: 1376 abilities on 1696 players, 0.81 each) the full-size effects made a league score 28.6 points a team against 26.9 for the same league without them. Most of that was many small always-on offensive abilities (a 3000-game offense-only split: receiving +0.7, ball carriers +0.6, linemen +0.4, quarterbacks +0.1) and **the leadership rating bonus, which was giving +1 to a whole unit for every leader (about +0.4 points a team)**; leadership is now a third of a point per effect point.

## Measured effect of every ability (tier 3 unless stated)

Each row is a paired experiment on the same seeds and rosters, with and without the ability on one player (or on the whole defense where the effect is a rate per tackle or per pass, marked "all"). `n` and z are in the test output (`ABILITY ...` lines). Tests: `SimAbilityTests`, `SimAbilityReceivingTests`, `SimAbilityLineTests` (`Tests/EditMode/Sim/Ability*.cs`).

| Ability | Hook -> consumer | Measured effect |
|---|---|---|
| Rocket Arm | ThrowPower -> baked rating (+6) and `StyleSpeed` boost 0.12 yd/s per point | ball speed at release 21.05 -> 21.76 yd/s (+3.4 percent) |
| Cool Under Fire | ThrowAccuracy[UnderPressure] -> `ReleaseThrow` when pocket pressure >= 0.25 | aim error per yard under pressure 0.076 -> 0.066 (-12.7 percent); clean pocket unchanged |
| Money Down | ThrowAccuracy + Clutch [3rd/4th down] -> baked | aim error on 3rd down 0.049 -> 0.039 (-21 percent); first down unchanged |
| Two-Minute General | ThrowAccuracy[TwoMinute] -> baked | aim error in the last two minutes 0.049 -> 0.043 (-11.5 percent) |
| Escape Artist | PocketEscape -> `TackleResolver.Attempt` (that share of would-be sacks become broken tackles); ScrambleSpeed -> body | sack rate on long-developing dropbacks 32.0 -> 25.7 percent (-19.6 percent); scramble top speed 9.04 -> 9.20 yd/s |
| Deep Touch | DeepAccuracy[Deep] -> `ReleaseThrow` (air distance >= 18 yd) | aim error on 18+ yd throws 0.051 -> 0.042 (-17 percent); short throws unchanged |
| Wrecking Ball | BreakTackle -> baked (x0.5); FumbleResist -> `FumbleChance` | broken tackles per carry 7.4 -> 9.1 percent, yards per carry 3.09 -> 3.24 (+5 percent), fumble chance on a tackle x0.85 |
| Ankle Shaker | JukeSuccess -> `CarrierMoves.Contest` | juke success 44.6 -> 50.9 percent at tier 3 (t1 +1.7, t2 +2.9 points: scales with the tier) |
| Whirlwind | SpinSuccess -> `CarrierMoves.Contest` | spin success 29.9 -> 41.2 percent |
| Home Run Burst | TopSpeed + Acceleration[OpenField] -> `UpdateAbilityBodies` (carrier 4+ yd past the line, or any returner) | 22.9 -> 23.2 yd in 5.5 s with a clean lane (+1.2 percent) |
| Stop On A Dime | CutSpeedLoss -> `AgentBody.CutBleed` and lateral grip | 0.4 s after a hard cut: speed 3.70 -> 3.75 yd/s, along the new line 1.42 -> 1.48 |
| Workhorse | FatigueRate -> `AgentBody.DrainScale` (and so the `FatigueTracker`) | energy burned by a 6-second run -25 percent |
| Short-Yardage Bruiser | ShortYardage[ShortYardage] -> `Attempt` carrier skill (+0.8 per percent) | third-and-1 conversion 74.8 -> 79.6 percent; first-and-10 unchanged |
| Burner | TopSpeed[Deep] + RouteSeparation[Deep] -> `UpdateAbilityBodies` | depth at 2.6 s on a go route 14.93 -> 15.30 yd; his man trails by 1.12 -> 1.09 yd; short routes unchanged |
| Sticky Hands | CatchInTraffic -> `CatchResolver.CatchBonus` (defender within a yard, or the duel) | catch chance with a defender on him 68.2 -> 70.1 percent; unchanged with nobody near |
| Crisp Routes | RouteSeparation -> route speed + a burst after a hard break (`OffenseAI.RunRoute`, `BreakBurstUntil`) | nearest defender at 1.9 s on hitch / curl / out routes 2.970 -> 3.002 yd (the weakest row: the route runner's timing, not his top speed, sets most of the separation) |
| Sky Walker | ContestedCatch -> `CatchBonus` and the duel | contested catch chance 47.9 -> 50.6 percent; staged jump ball won 64.1 -> 65.5 percent |
| Yards Machine | YardsAfterCatch -> speed after the catch + carrier skill in the tackle contest | yards after the catch 3.64 -> 3.85 (+6 percent) |
| Fearless Middle | CatchInTraffic[OverMiddle] -> `CatchBonus` (between the numbers, past the line) | catch chance with a defender on him over the middle 68.2 -> 70.4 percent, near the sideline unchanged |
| Seal Specialist | RunBlock -> baked | his man gets off the block on 10.0 -> 9.3 percent of run plays (-6.8 percent) |
| Return Ace | ReturnSpeed + ReturnElusiveness[Return] -> body while he has the ball / baked | kickoff return 16.9 -> 18.2 yd (+7.1 percent), punt return 11.0 -> 12.0 yd (+8.7 percent) |
| Road Grader | RunBlock -> baked | his man gets off the block on 12.8 -> 11.3 percent of run plays (-12 percent) |
| Immovable Anchor | BullRushResist -> `BlockEngagement.ApplyMove` (x0.8); PassBlock -> baked | the rusher gets free on 71.6 -> 70.3 percent of dropbacks (-1.8 percent, z -3.4) |
| Lead Puller | PullSpeed -> body while pulling; RunBlock[OpenField] -> `BlockerScore` downfield | top speed of the puller x1.028; ground covered in 1.5 s +0.5 percent (accelerating from a stance, not top-speed limited) |
| Swim Master | PassRush -> baked BlockShedding on pass plays | his blocker is shed 0.576 -> 0.724 times per dropback (+26 percent); run plays unchanged |
| Bull Rusher | PassRush[UnderPressure] -> `ApplyMove` once pocket pressure >= 0.3 | sheds per dropback 25.6 -> 27.6 percent (+7.8 percent) |
| Gap Plugger | RunStop -> baked BlockShedding + Tackling on run plays | tackles by the DT per run play 0.021 -> 0.036 (+69 percent); with the whole front: yards per carry 3.50 -> 2.67 (-24 percent) |
| Strip Artist | ForcedFumble -> `TackleResolver.FumbleChance` | fumble chance on a tackle x1.28 (big hit 2.80 -> 3.58 percent, wrap-up 0.37 -> 0.48); all: fumbles per run play +25 percent |
| Bone Rattler | HitPower -> big-hit threshold (-0.2 margin per percent) and knock-back; ForcedFumble | all: share of tackles that are big hits 14.7 -> 23.4 percent (+59 percent) |
| Sniper Blitz | BlitzTiming[Blitz] -> `BlitzUpdate` (delay x(1 - 2 pct), speed); PassRush[Blitz] -> baked | all blitzers: seconds until one reaches the quarterback 2.261 -> 2.217 |
| Sideline To Sideline | ZoneRange -> baked ZoneCoverage, `ZoneUpdate` reaction window (+0.15 yd per point), duel (+0.5 per point) | time with a receiver in his zone 63.2 -> 66.6 percent; jump balls won by the receiver against him 74.2 -> 71.4 percent |
| Shutdown Shadow | ManCoverage[ManToMan] -> baked, `ManUpdate` hip-turn delay (-2.5 percent per point) and speed, duel (+0.5 per point) | jump balls won by the receiver against him 70.9 -> 67.1 percent (completion rate over whole plays is within noise: 56.6 -> 56.8) |
| Ball Hawk | InterceptionCatch -> `CatchResolver.BallHawk` (share of deflections) | all: interceptions per attempt 2.5 -> 4.7 percent |
| Range Rover | ZoneRange (deep zones) as Sideline To Sideline | all: completion rate against zone 55.7 -> 52.9 percent; safety's jump balls 64.1 -> 60.6 percent |
| Jam Master | PressCoverage[LineOfScrimmage] -> `PressJam` (+0.8 per point) | press jams won 48.2 -> 65.6 percent |
| Sure Tackler | TackleReliability -> `Attempt` (that share of misses become clean stops) | all: missed share of tackle attempts 8.2 -> 6.2 percent (-25 percent) |
| Ice In The Veins | KickAccuracyPressure[CloseGameLate] -> baked KickAccuracy | 51-yard field goal in a tied fourth quarter 71.3 -> 77.0 percent; first quarter unchanged (72.1) |
| Boomer | KickPower -> baked | gross punt 47.0 -> 48.3 yd (+1.26); 56-yard field goal 63.1 -> 64.2 percent |
| Iron Man | InjuryResist -> `PhysicalGameSim.InjuryScale` (x0.85 / 0.75 / 0.65), same scale as `Season.RollInjuries` | the match simulation has no in-play injury model; the scale is there for the one that will use it |
| Clutch Gene | Clutch[CloseGameLate] -> baked on every rating except Injury / Stamina | QB accuracy 66 -> 72 (and awareness +6) in a close fourth quarter; aim error -10.6 percent; nothing in a blowout |
| Locker Room Leader | Leadership -> `LeadershipOf` | tier 3: +1 on every rating of his unit; tier 1 alone: 0 (a morale story); two tier-3 leaders: +2 |

Tier scaling is tested on Ankle Shaker (`AbilityEffectsScaleWithTheTier`: +1.7 / +2.9 / +6.2 points at tier 1 / 2 / 3).

Morale: every rating moves by `round(RatingModifier)`; a happy team (morale 100) against an unhappy one (20) runs for 3.77 against 3.17 yards a carry (`MoraleMakesAMeasurableDifferenceOverAGame`). On the league's own morale spread (55-92, mean 72) it changes nothing league-wide (6000 games: 26.7 points a team against 26.9).

## Staff tendencies (`StaffManager.Tendencies` -> CPU coach)

References are the league-average staff (blitz 0.25, man 0.40, deep shots 0.148, no-huddle 0.21), so the league as a whole plays as before (test: mean bias across 32 teams within +-0.001).

| Rate | Applied as | Measured |
|---|---|---|
| Blitz | `BlitzBias = (rate - reference) x 0.5` added to the blitz chance in `CallDefense` (the coordinator's aggression is already in `Aggression`) | Aggressive-scheme staff blitzes 28.3 percent of calls, Zone staff 14.0 |
| Man coverage | `ManBias = (rate - 0.40) x 0.8` added to the man chance | 41.5 vs 16.3 percent |
| Deep shots | `DeepShotBias = (rate - 0.148) x 5`, weight of deep passes x (1 + bias) | deep passes are 38.7 percent of an Air Raid staff's passes (31.9 without the staff rate) against 19.7 for Power Run |
| No-huddle | `NoHuddleBias = rate - 0.21`, `PhysicalGameSim.ApplyTempo` takes `bias x 20` s off the clock runoff between snaps | a spread team pair runs 183.5 plays a game, a power-run pair 154.5 (171.5 with the staff rates off) |

The live match's CPU coach reads the same profile (`PhysicalGameSim.ProfileOf`, used by `MatchFlow`), so blitz / man / deep-shot calls follow the staff there. The tempo is applied by `ApplyTempo`, which the headless game calls after every play; `MatchFlow` keeps its own clock, so it still has to call `game.ApplyTempo(State.Possession)` after `State.Apply` to get the no-huddle effect (public, one line, not done: Gameplay is not mine).

## Calibration before / after

League = `LeagueFactory.CreatePro(7)`, random pairings, `PhysicalGameSim` games; "with abilities" = `AbilityCatalog.AssignInitial` on every roster player (0.81 per player), morale as generated (55-92), staff rates on.

| Per team | 600 games, pre-0.8.0 | 600 games, now | 6000 games, pre-0.8.0 | 6000 games, now | NFL |
|---|---|---|---|---|---|
| Points | 26.7 | 27.5 | 26.9 | 27.6 | 22-24 |
| Plays | 61.8 | 61.9 | 61.9 | 62.0 | 63 |
| Yards per carry | 4.50 | 4.60 | 4.43 | 4.50 | 4.3 |
| Completion % | 64.9 | 65.0 | 64.8 | 64.9 | 64-65 |
| Yards per attempt | 7.44 | 7.56 | 7.47 | 7.60 | 7.0 |
| Sacks | 2.01 | 2.04 | 2.00 | 2.00 | 2.4 |
| Interceptions | 0.79 | 0.76 | 0.77 | 0.81 | 0.8 |
| Fumbles lost | 0.43 | 0.43 | 0.44 | 0.46 | 0.5 |
| Accepted penalties | 5.29 | 5.22 | 5.31 | 5.26 | |
| FG % | 85 | 87 | 86 | 86 | 84 |

Breakdown of the 6000-game points: morale alone 26.7, staff rates alone 26.7, abilities + morale + staff 27.6, so the abilities add about 0.7 a team (+2.6 percent); nothing leaves its band. The existing 40-game band test (`FortyCpuGamesProduceNflLikeTeamStatistics`) now opts out of the career layer (`PlayerModifiers = false, StaffTendencies = false`): it is a fixed-seed calibration of the engine itself and moves by +-2 points under any change of the random stream (25.2 -> 28.4 with morale alone on its 40 seeds, against 26.7 vs 26.9 over 6000). The league with the career layer on has its own test, `CpuLeagueWithTheCatalogueAbilitiesStillProducesNflLikeTeamStatistics` (320 games twice on the same seeds, in parallel: 28.2 points with abilities against 27.4 without in the dotnet copy, 28.0 against 26.4 in Unity; the usual bands plus a control of +-2.2 points, +-20 passing yards, +-14 rushing yards, +-2.5 plays).

`SimTestKit` players now carry a neutral morale (70) so every other Sim test keeps measuring the engine.

## Gates

* dotnet copy of the Sim EditMode suite (`%TEMP%\pixsim\tests`): 331 / 331 green (276 existing, 55 new: 49 ability tests, 6 staff / league tests), about 2 minutes.
* Unity EditMode, whole project (`Logs/editmode-simtune.xml`, run before the last trimming of the two slow league tests): 1039 / 1039 green in 1121 s. After the trim, the four new fixtures alone (`Logs/editmode-simtune2.xml`): 55 / 55 green in 197 s (`SimAbilityTests` 21 s, `SimAbilityReceivingTests` 23 s, `SimAbilityLineTests` 38 s, `SimStaffTendencyTests` 114 s: two 320-game leagues and the tempo pairs, both marked Slow).

## Notes for the lead

* The player card now shows the in-match sizes: the gain table moved into `AbilityCatalog.Gain` and `AbilityFx.Scale` reads it (done in the release pass). `MatchFlow` calls `Cpu.ApplyTempo(State.Possession)` after `State.Apply` (done).
* `QuickSim` (the League's simulated games) does not read abilities: it is a stat model and not part of the Sim.
* Iron Man only matters where injuries are rolled (`Season.RollInjuries`); there is no in-match injury model.
* Possible follow-ups: a `NoHuddle` call in `MatchFlow`; Crisp Routes is the least visible ability (route timing, not speed, sets separation); Lead Puller moves the pull but the pull is accel-limited.
