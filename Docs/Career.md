# Career Mode (v0.8.0 "Franchise") - design and UI API

Endless franchise mode on top of the fictional pro league. Everything here is **engine-free C#** in the `PixFootball.League` assembly, folder `Assets/_Project/Scripts/League/Career/` (namespace `PixFootball.League`). The UI layer only reads and calls it; the Unity match only plays the user's games and hands back a `GameResult`.

Contents: 1 Conventions - 2 The calendar - 3 Systems (roster, cap and contracts, free agency, trades, staff and morale, abilities, draft, history) - 4 Screen to API map - 5 Match integration - 6 Sim integration points - 7 Tuning knobs - 8 Tests and soak numbers - 9 Known limits

## 1. Conventions

* **One save object.** `CareerSave` (root, `[Serializable]`) wraps the existing `LeagueSave` (`League.Pro`, optional `League.College`) and adds the franchise state: user team, `CareerPhase`, settings, pick book, negotiations, free-agency offers, waivers, trade inbox, staff contracts, morale, draft state, transaction log, news, history (team seasons, trophies, All-Pro, accolades, Hall of Fame, owner goals).
* **Static services take the save.** `CareerFlow`, `RosterManager`, `DepthManager`, `CapManager`, `CapMath`, `Negotiator`, `FreeAgency`, `TradeEngine`, `StaffManager`, `MoraleEngine`, `AbilityCatalog`, `CareerDraft`, `HistoryEngine`, `OwnerEngine`, `NewsWriter`, `Ledger`, `CareerViews`, `CareerValidator`. No singletons, no globals: two careers can live in one process.
* **Management calls return `OpResult`** (`Ok`, `Message`): refused moves explain themselves, they never throw. Query calls return plain lists / view models.
* **Deterministic.** `CareerSave.NewRng()` hands out an independent forked stream and advances the persistent career generator (`RngS0/RngS1`, saved). Same seed + same user actions = same league, including after save/load.
* **JsonUtility-compatible.** Public fields, `List<T>`, ids and enums only; runtime caches are `[NonSerialized]`. `CareerSaveTests.SavedDataFollowsTheJsonUtilityRules` reflects over every `[Serializable]` type of the assembly and fails on anything JsonUtility cannot store. After `JsonUtility.FromJson<CareerSave>` call `save.Bind()`. A save is about 7 MB after three seasons of logs (news capped at 900 items, transactions at 4000).
* **Money** is `long` dollars. The cap is 255M in the first year and grows 5 percent a year (`CapMath.CapFor(save, year)`); market contracts, rookie scale and the minimum salary scale with it (`CapMath.Market/Rookie/MinSalary`).
* **Added to existing League types** (backward compatible, empty by default): `Team.PracticeSquad`, `Team.InjuredReserve` (both count on the cap), `Team.FindAnyPlayer`, `Player.Abilities`, `InjuryState.IrWeeks`, `GameResult.Injuries` / `InjuriesProvided`, `Season.ApplyExternalResult` / `SimOneGame` (new partial `SeasonExternal.cs`). `Season.RollInjuries` now scales the injury roll with the Iron Man ability. A few `Offseason` / `DraftEngine` / `DepthChartBuilder` helpers became `internal` so the career layer can reuse them. `Offseason.Run` (the standalone league) is unchanged.

## 2. The calendar

`CareerSave.Phase` is a `CareerPhase`. `CareerFlow.Advance(save)` performs exactly one step and returns a `StepResult` (`Ok`, `Blockers`, `Messages`, `Week` for a week step, `FreeAgencyDay` for a bidding day). If `Blockers(save)` is not empty nothing happens and `Ok` is false.

| Phase | Entered by | What `Advance` does from here | Blockers for the user |
|---|---|---|---|
| `Preseason` | new career, or the end of camp | CPU teams make final cuts (53 active, 16 practice squad, under the cap); waivers clear; week 1 opens | more than 53 active, practice squad over 16, over the cap, fewer than 45 active |
| `RegularSeason` | cuts done | one week: waivers, CPU roster upkeep (IR, replacements), CPU trades and offers to the user, **all games** (user's included if not already played), injuries, records, morale, college week | none |
| `Playoffs` | week 18 played | Wild Card, Divisional, Conference rounds (one per step) | none |
| `PixBowl` | conference finals played | the final; captures the PIX Bowl MVP and finalizes the season | none |
| `Awards` | season complete | retirements for everyone (age-based), Hall of Fame class | none |
| `Retirements` | | the **new league year**: contracts tick, dead cap rolls over, cap grows, staff ages/changes, morale resets | none |
| `ReSign` | | CPU teams decide on their expiring players (or tag one); the user's unsigned expiring players hit the market; everybody must be under the cap; the draft class is generated | over the cap |
| `Scouting` | | CPU-to-CPU trades, then the draft order is built from pick ownership | none |
| `Draft` | | CPU picks until the user is on the clock (or the draft ends, then CPU rookies sign, undrafted prospects become free agents, the college world turns over) | on the clock |
| `RookieSigning` | draft over | user rookies are signed (those that do not fit the cap lapse), free agency opens | none |
| `FreeAgency` | | one bidding day per step (14 days); after the last day the next step is camp | over the cap / over 90 after the last day |
| `TrainingCamp` | last day done | progression, aging and ability changes for everybody, healed players return from IR, cap check; the next step rolls the year and builds the new season | coaching vacancy, over the cap |

Helpers: `CareerFlow.NewCareer(CareerOptions)`, `Blockers(save)`, `AutoResolveUser(save)` (resolves blockers the way a CPU team would: cuts, re-signs, auto-pick, auto-bid, hires), `AdvanceUntil(save, phase)`, `SimToNextSeason(save)`, `SkipFreeAgency(save)`. `CareerText.PhaseLabel(save)` gives the header text, `CareerViews.NextStepLabel(save)` the label of the big continue button.

## 3. Systems

### 3.1 Roster management (`RosterManager`, `DepthManager`, `RosterRules`)

* Active roster 53 (offseason limit 90), practice squad 16, injured reserve up to 14. Practice squad deals are 400K scaled with the cap (below the minimum salary) and count on the cap; only players with up to 3 years of experience on small contracts qualify.
* `Release(save, team, id)`: dead money lands on the cap (half now, half next year when more than one year is left; `CapManager.DeadMoneyPreview` shows it first). In the regular season the player goes on **waivers**; elsewhere straight to the free agent pool.
* Waivers: `WaiverOrder` / `WaiverPriority` (worst record first), `ClaimWaiver(save, id)` for the user; CPU teams claim by need when they have a spot and cap room. A claim returns the dead money to the cutting team. Unclaimed players become free agents when the week advances.
* `ToPracticeSquad`, `Promote`, `PlaceOnIR` (injury of 4+ weeks), `ActivateFromIR` (healed and 4+ weeks on IR), `Validate(save, team, atSeasonStart)` returns `RosterIssue`s, `FinalCuts` / `FillOne` / `FillDeficits` / `FillPracticeSquad` are what the CPU uses and what an "auto manage" button can call.
* Injuries: `GameResult.Injuries` lists who got hurt, what and for how many weeks (`GameInjury`). The season simulation rolls them after each game; a played match can supply its own (`InjuriesProvided = true`). `WeekReport.Injuries` carries them as `InjuryEvent` (also in `CareerSave.InjuryLog`, and as news for the user's team and notable stars). IR players heal each week (`RosterManager.HealReserves`).
* Depth chart: `DepthManager.Entries/Candidates/Issues` for the screen; edits `SetOrder/Swap/Insert/Remove` validate **position eligibility** (`IsEligible`: own position and the neighbouring ones, e.g. guard at tackle, corner at safety) and mark the position as manual; `Refresh` (called after every roster move) rebuilds the automatic chart and keeps manual positions; `AutoSort/AutoSortAll` go back to automatic. `CareerSettings.AutoDepthChart` controls whether the user's chart is rebuilt at all.

### 3.2 Contracts and cap (`CapMath`, `CapManager`, `Negotiator`)

* `Contract` (existing): years, average annual value, prorated signing bonus, guarantees, per-year cap hits. Dead money = larger of the unamortised bonus and the remaining guarantee. A **trade** moves the contract without its signing bonus; the sender keeps the unamortised bonus as dead cap (`TradeEngine.ReceivedContract/TradeDead`).
* `CapManager.Report/LedgerRows` (cap used, dead money, committed next year, per-player rows for the cap screen), `Restructure` (half of the base becomes bonus), `AutoFix` (restructure, then release expensive backups), `NonCompliantTeams`.
* **Rookie scale** by overall pick (`CapMath.Rookie`, 4 years, 3 for undrafted), scaled with the cap; `CareerDraft.RookieDeal`.
* **Re-sign window**: `Negotiator.ExpiringPlayers`, `Ask(save, team, player)` returns a `ContractAsk` (aav, years, bonus share, `WillNegotiate`, interest, mood, notes). The ask comes from the cap-scaled market for overall (and ceiling for the young), age and position, then moves with morale (-10 percent at 100, +5 at 40), team success and role. `Offer(save, team, id, aav, years, bonusPct)` returns an `OfferResult`: `Accepted` (contract applied if the cap allows), `Counter` (within 12 percent of the ask), `Declined`, `Refuses` (unhappy player on a losing team wants to test the market) or `WalkedAway` after three rounds. A happy player gives up to a 5 percent hometown discount. In the final contract year the same call **extends** (`Apply` keeps this year's cap hit).
* **Franchise tag** (`CapManager.TagCost/FranchiseTag/TagAvailable`): one per team and offseason, average of the five biggest deals at the position bucket and at least 120 percent of his last salary, one year, fully guaranteed.
* **Cap compliance** is enforced at three points: the league year (the user is blocked in `ReSign`; CPU teams are auto-fixed when the window closes and before camp), the end of free agency / camp, and the regular season start (final cuts). Free agent bids and trades are cap-checked at the moment they resolve.

### 3.3 Free agency (`FreeAgency`)

* `FreeAgency.Pool(save, ...)` lists the market; `Ask(save, player, day)` is the market price (falls 1.2 percent a day to 80 percent; undrafted rookies ask the minimum).
* `PlaceOffer(save, id, aav, years, bonusPct)` / `WithdrawOffer`: standing bids, decided every time `Advance` runs a day. Each day the CPU teams bid on up to 3 players (6 on day 1) chosen by **positional need and best player available** within cap space (keeping minimum-salary room for the open roster spots). Each player picks the offer that scores best and signs if the score clears a threshold that drops from 0.55 to 0.30 over the period.
* The decision (`Evaluate` returns an `OfferEvaluation` with every component for the UI) mixes **money**, **contender** status (team rating + last record), **role** (would he start?) and **mood/loyalty** (old team, team morale). Each player has a stable main priority (`PriorityOf`: money, contender, role or loyalty) that weighs 45-60 percent.
* `SignNow` signs a free agent outside the bidding (preseason / in-season street signings, practice squad), refusing less than 95 percent of the ask.

### 3.4 Trades (`TradeEngine`)

* **Assets**: players on the active roster (not expiring, not unsigned rookies) and draft picks of the **current and next two drafts** (`CareerSave.Picks`, `PicksOf`, 7 rounds x 32 teams x 3 years; the book rolls with the year).
* **Value model** (one scale, the first overall pick = 3000): picks by a draft chart (`PickChartValue`, future years discounted 12 and 22 percent, expected slot from team strength before the order is final); players by overall (+ceiling for the young), position multiplier (QB 1.5 ... K 0.25), age curve (shifted by the position's decline age), contract surplus against the cap-scaled market, injuries (`PlayerValue`). A 75 is about a second-round pick, an 85 about 1750, a 90 about 2300.
* `Evaluate(save, proposal)` -> `TradeEvaluation` (legal?, problems, each side's received/given value, `CpuAccepts`, message); `Propose` executes when the CPU accepts (it wants a 5 percent edge, values its starters and needs, treats a franchise quarterback or a 90+ as untouchable); `AskWhatItTakes` builds a counter from the user's own assets; `Execute` is the raw mover (dead cap, jerseys, depth charts, news, transaction).
* Legality: ownership, roster limit (the user's), cap (both), no practice squad / IR / expiring / unsigned players. **Deadline**: `Window(save)` is `Open`, `ClosedDeadline` (after week 9, `CareerSettings.TradeDeadlineWeek`) or `ClosedPostseason`.
* **CPU-initiated**: `GenerateOffers` puts up to 3 offers in `CareerSave.Inbox` (a CPU team that is weak where the user is strong pays a bit over value); `PendingOffers`, `AcceptOffer`, `RejectOffer`. **CPU to CPU**: `SimulateCpuTrades` (about 1 a week before the deadline, a few in the offseason). **Draft day**: `CareerDraft.TradeUpOffers` when the user owns the pick on the clock.

### 3.5 Coaching staff and morale (`StaffManager`, `MoraleEngine`)

* Every team has a head coach, offensive and defensive coordinator (existing `Coach`: ratings, development, scouting, aggression, pass tendency, schemes) plus a `StaffContract` (salary, years) and a staff budget. `Fire` (role becomes vacant, the coach goes to the market), `Hire` (market candidates in `CareerSave.CoachPool`, budget checked), `HireBest`, `CandidateValue`. CPU teams age their staff, let contracts run out, fire head coaches after bad seasons and hire from the pool every new year; the user's staff is never changed automatically, a vacancy blocks camp.
* **Progression**: `DevelopmentRating(team, player)` (head coach blended with the position's coordinator, team prestige, **scheme fit** +/-4, mood) is what camp passes to `Progression.Advance`. `SchemeFit` is +1 for archetypes the scheme is built around, -0.5 for clashes; `TeamSchemeFit` is the 0-100 number for the staff screen.
* **Play calling data**: `StaffManager.Tendencies(team)` -> `PlaycallTendencies` (pass rate, 4th-down aggression, blitz rate, deep-shot rate, man-coverage rate, no-huddle rate, schemes). `QuickSim` and `PhysicalGameSim.ProfileOf` already read the coach's aggression, pass tendency, ratings and scheme; the extra rates are there for the live play caller.
* **Morale**: `MoraleEngine.Breakdown(save, team, player)` explains a player's mood (team success, starting role / wants more playing time, contract, head coach, scheme fit, injury, leaders on the team); every week each player moves a quarter of the way to his target, `TeamMorale` combines the starters and the results. Morale changes re-sign asks, the keep probability and free agent mood. `RatingModifier(player)` (-2..+2) is data for the Sim.

### 3.6 Abilities (`AbilityCatalog`, `PlayerAbility`)

* 41 abilities with our own names (Rocket Arm, Money Down, Two-Minute General, Escape Artist, Wrecking Ball, Ankle Shaker, Whirlwind, Stop On A Dime, Home Run Burst, Burner, Sticky Hands, Sky Walker, Fearless Middle, Return Ace, Road Grader, Immovable Anchor, Swim Master, Bone Rattler, Sniper Blitz, Shutdown Shadow, Ball Hawk, Ice In The Veins, Iron Man, Clutch Gene ...), each for position buckets, favoured archetypes, key ratings with thresholds for tier 1/2/3 (shown as one to three stars) and a list of **effects**: `SimHook` + `AbilityContext` + the size at each tier (rating points or percent).
* **Assignment**: `AssignInitial(player, rng)` for every regen (league creation, draft prospects, street free agents); the number of abilities follows overall (0 at 55, 1 at 68, 2 at 76, 3 at 84, 4 at 90, +1 for superstar development), weighted by tier and archetype. **Progression**: `Progress(player, rng, devBonus)` at camp upgrades a tier when the ratings support it, downgrades or removes one when they fall, and can unlock a new one. `ScoutedHints(player, pointsSpent)` limits what a scouting report shows.
* **Reading the effects**: `Modifier(player, SimHook, AbilityContext)`, `ModifierTable(player)` (flat list for caching at kickoff), `Describe(ability)` (text for the player card), `Stars(tier)`.

### 3.7 Scouting, draft and rookies (`CareerDraft`)

* `PrepareClass` (at the start of `Scouting`): the class comes from the college world (juniors and seniors) or is synthetic when there is none; the user's report starts blurry with his full scouting-point budget. `Board(save, sort)` lists `ProspectRow`s with the user's estimate and uncertainty; `Scout(save, prospectId, points)` sharpens it; `SetStarred/MoveOnBoard` keep the personal big board.
* `Build` makes the pick list from **pick ownership** (traded picks move with their owner), `Current/UserOnClock/UserPick/AutoPick/SimToUserPick/SimToEnd`; every pick gets a live **grade** against the public consensus board (`GradeOf`, `TeamGrade`). `TradeUpOffers` gives the draft-day offers.
* After the draft: `UnsignedRights`, `SignRookie/SignAllRookies` (rookie scale, cap checked), `FinishDraft` signs the CPU picks, sends undrafted prospects (grade 52+) to the pool at the minimum and turns the college world over.

### 3.8 History and legacy (`HistoryEngine`, `OwnerEngine`, `NewsWriter`)

* When the PIX Bowl ends: `TeamSeasonRecord` per team (record, seed, division title, playoff result), `TrophyEntry` (PIX Bowl with opponent, score and game MVP; conference and division titles; perfect seasons), accolades (MVP, offensive/defensive player of the year, rookie of the year, All-Pro first and second team, PIX Bowl MVP, a ring for every player who played for the champion), **All-Pro teams** (24 slots, ratings plus production plus team success, `GetAllPro`), team records, milestones (career yardage, touchdowns, sacks), owner goals.
* Record book: the existing player records (single game / season / career) plus `DetectBrokenRecords` (called every week; headlines for broken records from the second season on) and team records (`CareerSave.TeamRecords`).
* **Hall of Fame**: `RecordRetirement` at every retirement, `InductClass` each year for players retired at least `HofWaitYears` (2) that scored enough (`HofScore`: seasons, awards, All-Pro, rings, production; bar 42, at most 4 a year); `HofEntry` keeps the summary text.
* **Legacy**: `HistoryEngine.Legacy(save, teamId)` -> `LegacyBreakdown` (score, tier Rebuilding / Contender / Powerhouse / Dynasty / Legendary, itemised lines: titles 50, conference titles 15, division titles 6, playoff runs 8, wins above eight, playoff streaks, MVPs, Hall of Famers 15, records 3). `TrophyCase(save, teamId)` for the trophy room.
* **Owner**: `OwnerEngine.SetGoals` at the start of each season (win total, playoffs, division, playoff win or title by team strength), `Evaluate` at the end, `OwnerSatisfaction` 0-100, `JobSecurity` text.
* **News / ticker**: `NewsWriter.Ticker(save, n)` (newest, important and own-team items first), `Feed(save, max, onlyMine, kind)`. Kinds: signings, re-signings, trades, injuries, records, upsets, awards, retirements, draft picks, releases, coaches, milestones, Hall of Fame, titles, owner. `NewsItem.Priority` 1-5, `Mine`, team and player ids.

## 4. Screen to API map

| Screen / tab | Read | Act |
|---|---|---|
| **Franchise hub HOME** | `CareerViews.Home(save)` (phase label, record, next game with OVR/OFF/DEF tiles, division standings, ticker, owner goals, cap space, pending offers, injured starters, legacy, blockers, continue label) | `CareerFlow.Advance` |
| **Game day** | `CareerFlow.UserGame`, `GamesThisWeek`, `SetupUserGame` -> `GameSetup` | play: run the match, `SubmitUserGame(result)`, `Advance`; sim: just `Advance`, or `SimGame(save, gameId)` for one box score now |
| **NEWS** | `NewsWriter.Feed/Ticker` | - |
| **OWNER** | `CareerSave.Goals`, `OwnerSatisfaction`, `OwnerEngine.JobSecurity`, `HistoryEngine.Legacy` | - |
| **TEAM > Roster** | `CareerViews.RosterTable`, `Needs`, `InjuryReport`, `Card(save, playerId)` | `RosterManager.Release/ToPracticeSquad/Promote/PlaceOnIR/ActivateFromIR/ClaimWaiver` |
| **TEAM > Depth chart** | `DepthManager.Entries/Candidates/Issues` | `SetOrder/Swap/Insert/Remove/AutoSort` |
| **TEAM > Contracts and cap** | `CapManager.Report/LedgerRows`, `Negotiator.ExpiringPlayers/Ask` | `Negotiator.Offer`, `CapManager.FranchiseTag/Restructure/AutoFix` |
| **TEAM > Staff** | `StaffManager.Payroll/Budget/Vacancies`, `TeamSchemeFit`, `Tendencies`, `CareerSave.CoachPool` | `StaffManager.Fire/Hire` |
| **TEAM > Player card** | `CareerViews.Card` (ratings, abilities with stars and effect text, contract, dead money, morale factors, season lines, honors, trade value) | - |
| **Free agency** | `FreeAgency.Pool/Ask/Evaluate/OfferOf`, `CareerSave.FaLog` | `PlaceOffer/WithdrawOffer`, `Advance` (a day), `SkipFreeAgency`, `SignNow` |
| **Trades** | `TradeEngine.PicksOf/PickLabel/PlayerValue/Evaluate/PendingOffers/Window` | `Propose`, `AskWhatItTakes`, `AcceptOffer/RejectOffer` |
| **Scouting / Draft house** | `CareerDraft.Board/PointsRemaining/Current/UserOnClock`, `s.Draft.Slots` (grades), `TradeUpOffers` | `Scout`, `SetStarred`, `UserPick`, `AutoPick`, `SimToUserPick`, then `SignRookie/SignAllRookies` |
| **LEAGUE** | `Season.Standings/DivisionStandings/ConferenceStandings`, `CareerViews.Schedule/PowerRankings`, `LeagueLeaders.Top`, `Pro.Records`, `CareerSave.TeamRecords`, `GetAllPro`, `Trophies`, `HallOfFame`, `TrophyCase`, `Pro.History` | - |
| **Training camp** | `CareerSave.LastCamp` (`CampResult`: ovr before/after, ability changes) | - |
| **Settings** | `CareerSave.Settings` | auto depth chart, auto IR, deadline week, free agency days, injuries on/off |

## 5. Match integration

`GameSetup` (from `SetupGame` / `SetupUserGame`) carries the scheduled game, home and away `Team`s (their depth charts feed the match), the `RuleSet`, `SimOptions` (neutral site, forced winner in the playoffs) and a weather seed. The match must return a `GameResult` for the same two teams with `HomeScore/AwayScore`, `Overtime` and `PlayerLines` (same stat format as `QuickSim`, so stats, awards and records work). If the match simulates injuries, fill `GameResult.Injuries` and set `InjuriesProvided = true` (otherwise the season rolls them). `SubmitUserGame` posts stats, records and injuries; the rest of the week is simulated when the user advances, so a played game and a simmed game leave the same traces.

## 6. Sim integration points (not wired yet - the Sim was not touched)

| Data | Where the Sim should read it |
|---|---|
| Ability rating hooks (`ThrowPower`, `ThrowAccuracy`, `DeepAccuracy`, `BreakTackle`, `ManCoverage`, `ZoneRange`, `PressCoverage`, `RunBlock`, `PassBlock`, `PassRush`, `RunStop`, `KickPower`, `KickAccuracyPressure`, `ReturnSpeed`, `ReturnElusiveness`, `TopSpeed`, `Acceleration`, `ScrambleSpeed`, `PullSpeed`) | where the agent is built, `PlaySimulation.cs` (`a.R = player.Ratings`): add `AbilityCatalog.Modifier(player, hook, context)` to the matching rating for the play context (down and distance, time, pressure, coverage type) |
| `ThrowPower`, `ThrowAccuracy`, `DeepAccuracy`, `PocketEscape`, `Clutch` | `OffenseAI.Qb.cs` (throw speed from `ThrowPower`, accuracy cone from `ThrowAccuracy`, sack/escape logic), `BallSim.cs` |
| `BreakTackle`, `FumbleResist`, `ForcedFumble`, `HitPower`, `TackleReliability` | `TackleResolver.cs` (carrier skill term, fumble chance), `CarrierMoves.cs` |
| `JukeSuccess`, `SpinSuccess`, `CutSpeedLoss`, `ShortYardage`, `YardsAfterCatch` | `CarrierMoves.cs` (juke/spin win terms), `AgentBody.cs` (speed lost in a cut), `CarrierAI.cs` |
| `CatchInTraffic`, `ContestedCatch`, `RouteSeparation`, `InterceptionCatch` | `CatchResolver.cs`, `PlaySimulation.Catch.cs`, route running in `OffenseAI.cs` |
| `FatigueRate` | `FatigueTracker.cs` |
| `BlitzTiming`, `ZoneRange`, `ManCoverage` | `DefenseAI.cs`, `DefenseAlignment.cs` |
| `RunBlock`, `PassBlock`, `BullRushResist`, `PassRush` | `BlockEngagement.cs`, `BlockerAI.cs` |
| `KickPower`, `KickAccuracyPressure` | `SpecialTeamsAI.cs`, `SpecialTeamsAI.Meter.cs` |
| `InjuryResist` | already used by `Season.RollInjuries`; an in-match injury model should scale with it too |
| `MoraleEngine.RatingModifier` and `Leadership` | the same agent construction, +/-2 on every rating |
| `StaffManager.Tendencies` (blitz, deep shot, man coverage, no-huddle rates) | the live play caller / `PlayCallModel` and `DefenseAI` |

## 7. Tuning knobs

`CareerSettings` (auto depth chart, auto IR, trade deadline week 9, free agency days 14, Hall of Fame wait 2, injuries on/off, simulate the college world); constants in `RosterRules` (53 / 90 / 16 / IR rules), `CapMath` (practice squad base), `TradeEngine` (`CpuMargin` 1.05, offers limit 3, value formula), `FreeAgency.Threshold`, `Negotiator.MaxRounds`, `HistoryEngine.HofThreshold` (42) / `HofMaxPerClass` (4), `AbilityCatalog` thresholds.

## 8. Tests and soak numbers

Tests live in `Assets/_Project/Tests/EditMode/Career/` (99 tests in 13 files: flow, roster, contracts, free agency, trades, staff and morale, abilities, history, draft, save/load, views, fuzz and the soak). `CareerValidator.Check(save, strict)` is the consistency checker behind them (unique ids, valid contracts, nobody in two places, jerseys, pick book, and at season start 53 active / practice squad / cap / position minimums / starters filled).

Soak (6 seeds x 10 seasons with full offseasons, user on auto pilot): zero validator problems, every team has 53 active and a 16-man practice squad and is under the cap at the start of every season, no team over the cap in any preseason, no exceptions. League strength (average depth-chart starter overall) rises at most 1.3 points over ten years and never falls below the start (for example 75.3 -> 75.3..76.6, 76.2 -> 76.2..77.3), roster overall stays within about +/-1.3; average age settles near 28.0 (the standalone offseason settles near 27.7); 7-10 different champions in ten years; 0-7 Hall of Famers in ten years (no inductions before year 3 because of the two-year wait). A season simulates in about 1.5 s and an offseason in about 0.5 s on the dev machine. 40 extra seeds of random management actions (`CareerFuzzTests`, three seeds are in the repo) ran clean.

## 9. Known limits

* Compensatory draft picks are not modelled (224 picks, the existing standalone draft has 260); scouts are not individual people, scouting is a point budget.
* Pro Bowl, suspensions and the practice-squad poaching rule are not modelled.
* Ability effects are data only until the Sim integration above is done (the Iron Man injury effect is live).
* The college world's own coach and recruiting decisions are not part of Career (it simulates alongside and feeds the draft class).
