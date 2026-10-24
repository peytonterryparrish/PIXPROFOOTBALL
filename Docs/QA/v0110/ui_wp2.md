# UI-WP2 Front end: Direction 16 "Tile Hub Modern" (v0.11.0)

Owner: UI-WP2. Spec: `D:\PIXFootball\Work\UI16\SPEC.md` section 4. Renders: `D:\PIXFootball\Renders\v0110_ui_wp2\` (all screens, Tile theme: 1920x1080, 3440x1440 in `..._main`; 2560x1440, 3840x2160, 1440x1080 for hub, team select, settings, career). Tool: `Editor/Tools/UiWp2Shots.cs` (`-pixShotTheme Tile -pixShotSizes ...`).

## What changed (WP2 files only, plus new files)
| Screen | Files | Result |
|---|---|---|
| Hub | `Glass/Hub/MainMenuHub.cs` (rewritten), `MenuHubPlan.cs` (new, pure page plans), `MenuTile.cs` (new, tile builder with crests, shade, badge, Frame on top in Tile), `MainMenuController.Glass.cs`, `HubNav.cs` (unchanged) | 3 pages HOME / PLAY / SETTINGS, mock positions (hero 780x470, 380x292, 480x290/162, 230x288, 240x238, 500x250/252; 1800x784 block, centred, FitScale), NEWS ticker via `GlassKit.Ticker`, player chip, section label on sub screens, Tile backdrop |
| Career start | `Franchise/CareerStartPanel.cs` | block 1800x784 centred, wider grid / table; no college entry (there never was one in this panel) |
| Team select | `TeamSelectPanel.Glass.cs` | HOME / AWAY team tiles with OVR / OFF / DEF stat panel (bars), 8x4 crest grid, MATCH OPTIONS tab, watch note; `OpenMatchup()` for QUICK REMATCH |
| Matchup | `Glass/Hub/MatchupCard.cs` | wide card, two stat panels, WATCH MODE chip instead of YOU |
| Settings | `SettingsPanel.Glass.cs`, `SettingsPanel.cs` | wide panel, option rows in two columns (the Gameplay page now has 10 rows and overflowed one column), BACK full width; WATCH+HUD rows (Controller input, Helper bar, Game speed) show on Gameplay |
| Dialog | `UiDialog.cs` | short questions get a 340 high box (rest is WP1's Modal) |
| Loading | `Glass/Hub/LoadingContent.cs` (new) | watch-mode tips (Start pauses, View camera, Y HUD, X helper bar, RB speed) written into `GlassLoading.Tips` (same length) at startup and before every scene load |
| Footers | `MainMenuController.cs/.Glass.cs` | legend per screen: hub (A, LB/RB tabs), team select (A, B, X swap, LB/RB), matchup, settings (LB/RB pages) |
| Tests | `Tests/EditMode/Presentation/UiWp2Tests.cs` | 11 new tests (plan fits / no overlap / reachable, no college or Heisman or real marks, only 4 locked tiles, loading tips, top teams, Tile contrast) |

## Decisions
- **College / Road to Heisman:** gone from the hub (the old locked "COLLEGE NETWORK" tile was the only trace) and the plan has a test that fails on the words "college", "heisman", "draft fever". Code is untouched. The match-option "Rules: Pro / College" row is a rule set of the exhibition (hash marks, clock, overtime), not the mode: kept.
- **Locked tiles:** only Online, Superstar, Trophy Room, Mini Games (the four the SPEC names, "COMING SOON" + lock). Hidden: My PIX, Weekly Challenge, Share, Scouting, the old Practice drills: they would look unfinished. QUICK REMATCH (opens the last setup's matchup card) locks until a game was set up; POWER RANKINGS is a non-selectable info panel (top 4 teams by OVR).
- **Tabs:** HOME, PLAY (mode list), SETTINGS (five page tiles + QUIT) instead of the mock's SHARE / CUSTOMIZE / STORE.
- **Watch mode:** Play Now / Rematch go team select, matchup, KICK OFF. `MatchDirector.ApplyWatchMode()` turns every game into CPU vs CPU on the broadcast camera, so no code was needed in the menu. In watch mode: no "You play as" row, no YOU chips, WATCH MODE chip, settings tile / hero show "Controller input: Off (watch)".
- **No menu player** in the themed menus (the tiles fill the screen); `StageInMenu` is true for Classic only.
- Art: crests over a team-colour wash and line icons; **no photo art** (the mock's photos are not in the game; existing `_shared/game` renders carry baked HUD). Gap: fresh HUD-free in-engine 3840 renders for the hero, Franchise and Free Roam tiles.

## Acceptance (SPEC section 4)
- Scaling: rendered at 1920x1080, 2560x1440, 3840x2160, 3440x1440, 1440x1080; block is centred, FitScale only shrinks, backdrop / ticker / footer full bleed (WP1); nothing clipped. No text below 20 units in my builders (grep clean; Lay helpers clamp).
- Controller: `navaudit.txt` of 42 screen renders in the last pass: every one "all reachable"; the quit dialog reports the two buttons unreachable from the hub tabs, which is the intended focus trap (A / B inside). LB / RB: hub tabs, settings pages, team select TEAMS / OPTIONS; B: team select, matchup (one level), settings, career start (existing handlers, unchanged); focus returns to the opener (existing `RememberOpener`). Not tested with a physical pad in this pass.
- Contrast: unit test on the Tile tokens (white on tile >= 7, muted on row >= 4.5, onGold on gold >= 7, sub on tile >= 4.5) passes.
- Licensing: no NFL / Madden / Draft Fever text in plan strings (test); no `_Licensed` reference added to a committed prefab (code builds everything).
- Tests: `UiWp2Tests`, `MenuHubTests`, `UiFlowUiTests` 34 / 34 green; full `!Slow` run: see the end of this file.

## Compared with the mock (once, hub 1920x1080)
Matches: tab bar with LB / RB caps and gold plate, tile sizes and positions, square 2 unit borders, gold focus ring on the hero, FEATURED / RESUME badges, locked tiles, ticker band, footer legend, Barlow, player chip (top right). Differs: crests instead of photos, tabs HOME / PLAY / SETTINGS, chip shows the last team instead of level and PX (no progression exists), footer shows key caps because the shots run with the keyboard device.

## Found in other tracks (not mine, not touched)
- WP1: the gold "NEWS" block of the ticker renders dark (also in WP1's own `ui16/tile_p0` shot). `GlassLoading` still titles a watch game "KICKOFF": use `LoadingContent.Subtitle(scene, WatchMode.Enabled)` and `LoadingContent.Pick(...)`. The footer right text shows `v0.10.0` (version bump is the integration's). `UiTheme.DefaultKind` is still Glass: the Tile switch is the integration step.
- WATCH+HUD: `MatchHud.Popups.cs` uses `PixFootball.Sim` but `PixFootball.UI.asmdef` has no reference (UI does not compile from the shared tree without it; my private copy patches it in `sync.sh`).
- Private copy only: `PixRuntimeAssets.asset` does not import there, so the shots tool injects the crest sprites itself.

## Private copy
`D:\PIXFootball\Work\UiWp2\PIX` (sync.sh = Scripts, Tests, UI, Shaders from the shared tree; run.ps1). Shots log `logs\shots11.log`, `shots12.log`.

## Full EditMode run (`-testCategory "!Slow"`, private copy, 1318 tests)
1293 passed, 13 failed, 12 skipped. All 13 failures are outside the front end (PhotoPlayerTests x5, BlockingTests, TackleTests, MatchFlowTests, QuickSimTests, LeagueEngineFixTests x2, CareerDraftTests, UiFlowStatsTests final-screen penalty yards = the same penalty-yard / engine data as LeagueEngineFixTests). `UiWp2Tests`, `MenuHubTests`, `UiFlowUiTests` are green.
