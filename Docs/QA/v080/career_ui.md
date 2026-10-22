# v0.8 Franchise: career UI (track "career UI")

Code: `Assets/_Project/Scripts/UI/Franchise/` (namespace `PixFootball.UI.Franchise`), the match bridge `Gameplay/Core/CareerSession.cs`, hooks in `MatchDirector.Career.cs`, `BroadcastDirector.Result.cs`, `MatchData.cs`, `MainMenuHub.cs`, `MainMenuController.Career.cs`.
Renders (2560x1440, edit mode, tier 3): `D:\PIXFootball\Renders\v080_career\` (`Tools/unity.ps1 -Method PixFootball.EditorTools.CareerUiShots.Render -Log career_shots.log`, optional `-Extra "-pixShots home,roster"`). Gamepad audit: `navaudit.txt` there (every screen: all selectables reachable).
Tests: `Tests/EditMode/Presentation/FranchiseUiTests.cs` (16 tests).

## Flow

Main menu hub CAREER tile (enabled; subtitle = the newest save) -> start panel (CONTINUE / NEW CAREER / LOAD CAREER) -> franchise shell. Tabs HOME / TEAM / LEAGUE / OFFICE (LB RB), sub tabs (LT RT), B backs out (HOME: leave dialog). PLAY launches the Unity match with the career league, the result comes back through `CareerSession.CompleteMatch` -> `CareerFlow.SubmitGame`, the menu reopens the hub (`ReturnToFranchise`) and shows the box score of the game. SIM uses `CareerFlow.SimGame` (box score first, then the week finishes). Sim options: this week, to week N, to the playoffs, to the offseason, the whole season (auto pilot). Autosave to the slot file after every `Advance`, every sim and the draft picks.

| Screen | Where | Notes |
|---|---|---|
| New career | start panel | 8x4 crest grid, difficulty, quarter length, league (standard = exact ratings on the card, fresh = random seed), save slot (5 slots, overwrite asks), rules fixed to Pro |
| Continue / Load | start panel | slot table, detail card, X deletes (asks) |
| HOME | hub | matchup card (crests, records, OVR OFF DEF, kickoff, venue, conditions) with PLAY GAME / SIM GAME / SIM OPTIONS, or the offseason step (step n of 8, blockers, "resolve it for me"), status tiles (team, cap and roster, inbox), division standings, owner goals, ticker |
| ROSTER | TEAM | sortable list (Y or header click), position and show filters, photoreal portrait card (live RenderTexture of the player's appearance in team colours), ratings, abilities with stars, contract, morale; A: extend, depth, trade, practice squad, IR, release with dead-money preview |
| DEPTH CHART | TEAM | position list + order list, A picks up and up/down places, X options (starter, add eligible player, remove, auto sort), auto sort position / all, issues line |
| INJURIES & SQUAD | TEAM | injury report, practice squad, injured reserve, waiver wire with claim |
| SCHEDULE, STANDINGS, LEADERS, AWARDS, RECORDS, TROPHIES & HOF (incl. legacy), NEWS | LEAGUE | schedule with week results and box scores; standings by division, conference, league, power rankings, playoff picture with the cut line; leaders by category with the player card; awards and All-Pro by season; record book and team records; trophy case, Hall of Fame, legacy lines; news feed with filters |
| CAP & CONTRACTS | OFFICE | cap tiles, ledger sortable, restructure, release, franchise tag, auto-fix |
| RE-SIGN | OFFICE | what he wants, interest, mood, years / money / bonus editor, accept / counter (put into the editor) / decline / walks away, franchise tag |
| FREE AGENCY | OFFICE | pool with filters, standing offers, offer window with the four decision scores and the live verdict, advance day with a results modal, skip to the end, street signing outside the bidding |
| TRADES | OFFICE | any partner team, both sides' players and picks, value meter, legality / CPU message, ask what it takes, propose; inbox with accept / reject; deadline text |
| STAFF, OWNER | OFFICE | coaches with ratings, schemes and contracts, fire / hire from the market, payroll, scheme fit; owner trust, goals, past seasons, legacy |
| DRAFT | overlay from the hero | scouting (points, star, sort), board with the user's estimates, prospect card, live pick list with grades, pick clock (3:00, auto pick at zero), trade-up offers, auto pick, sim to my pick / to the end, rookie signing with rookie scale |
| Training camp results | HOME (after the step) | risers, fallers, ability changes of the user's team |

## Issues log

| ID | Status | Sev | Where | Issue | Fix / note |
|---|---|---|---|---|---|
| CU-001 | fixed | S2 | tables | Rows over the bright stadium were unreadable | `GlassTable` draws its own glass backing; text pills (`Lay.Under`) behind loose captions |
| CU-002 | fixed | S3 | tables | Right aligned number columns touched the next left aligned column ("#NAME", "88 +/- 9Round 1") | left aligned index columns, padded text columns |
| CU-003 | fixed | S3 | home hero | Stat tiles and conditions overlapped the buttons; "WEEK 5 OF 18 - WEEK 5"; division name "LIB LIB North" | layout moved up, label and name helpers |
| CU-004 | fixed | S3 | player card | Bottom lines clipped, ability effect repeated the name | compact mode below 640 units, lines only when they fit, name stripped |
| CU-005 | fixed | S3 | action sheet / box score / FA offer | label under the hint, last stat row under the button, verdict over the stepper | left aligned labels, taller box, moved text |
| CU-006 | fixed | S2 | franchise shell | `FranchiseUi.OnEnable` ran before the controller was assigned, so pages never refreshed after an action | subscribe in `Build`, unsubscribe in `OnDestroy` |
| CU-007 | fixed | S3 | live match result | Tracker lines carried the player's own team id, which can be stale after a trade | the roster decides the line's team in `BroadcastDirector.FillResult` |
| CU-008 | fixed | S3 | box scores | The engine keeps final scores only; box scores live for the session (the controller remembers them) | the user's own games are stored in the save (`CareerSave.UserBoxes`, newest 40); other teams' box scores are still session only |
| CU-009 | open | S3 | college world | The runtime assets have no `schools.csv`, so a new career has no college world (synthetic draft class, no college network) | add the school files to `PixRuntimeAssets` in the release build |
| CU-010 | open | S3 | match | The played match does not report injuries (`InjuriesProvided` stays false, the season rolls them); a playoff game that ends level is posted with the home team one point up | wire the match injury model later; sudden death overtime makes ties rare |
| CU-011 | open | S3 | career settings | Auto depth chart, auto IR, deadline week, free agency days, injuries on/off have no screen (defaults only) | small settings page in a later pass |
| CU-012 | open | S3 | portraits | Edit-mode renders show the T pose (no animator); the live player card renders every frame while the card is on screen (one 392x504 RT) | check cost in a played session |
| CU-013 | fixed | S3 | verification | The full play-through (menu -> match scene -> result -> hub) was reviewed in code and unit tested (`APlayedGameResultIsPostedToTheCareer`) but not played in a built player in this pass | `-pixAutotestCareer` plays it in the built player (menu, match scene, result, hub, save, load, draft), 0 failed checks, see `UI/Franchise/CareerAutoTest.cs` |
