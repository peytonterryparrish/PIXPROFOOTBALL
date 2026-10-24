# v0.11.0 integration notes (collected as tracks finish)

## Final IK
- Installed by MOTION to `Assets/_Licensed/RootMotion` (git-ignored) behind the `PIX_FINALIK` define.
- **At integration, run `FinalIkInstaller.Run`.** The `PixIk` bridge (`Hand` / `Foot` / `Body`) is not in the shared tree yet.
- Grounder is off by default (`-pixGrounder`).

## AUDIO+CROWD (done; log `audio.md`)
- **Before building, run "PIX Football > Audio > Build Audio Bank"** (or its batch method).
- Known issues:
  - Cadence voice is missing; there is no source audio.
  - Replays are silent.
  - Crowd animation is hard to see on the broadcast camera (dark stands, edge-on).
  - The MoCap Online crowd pack is deferred to v0.12.
  - The owner should listen to `menu_calm`, `menu_house` and `halftime_loop` to confirm there are no vocals.

## SIM-FLOW (done; log `simflow.md`; 19 BUGS rows fixed)
- One line changed in WATCH's `BroadcastDirector.Shots.cs` (B-070).
- **S1 to fix at integration:** auto replays hold the field for 70–120 s in the capture runs. Cap a replay at about 8–12 s, with a skip.
- Failing tests to resolve:
  - RB's `YardsPerCarryStayRealistic`
  - a career-draft test
  - `TeamPenaltyYardsEqualThePlayerPenaltyYards`: a QuickSim penalty accounting gap, 13 vs 12.
  - The new `NaturalPaceGivesAShortQuarterGameTheSnapsOfARealOne` test has not been run in Unity yet.
- Knobs: `GameState.ClockScale` (CPU games), `TackleGapScale` 0.55, `PassBias` 0.06.
- Whistle-to-snap is now 16 s. The owner's decision is about 25 s by default; that setting lands in v0.12.
- Open:
  - Wrap-tackle slide 1.98 m/s.
  - Jump/GetUp ownership.
  - Lineman clip-change rate.
  - Players standing still after the whistle: 9.9 per play.
  - Too few out-of-bounds plays: passing is too middle-heavy.

## WATCH+HUD (done; EditMode 1292/1296)
- Review the one-line hooks in `MatchDirector.cs`, `MatchCameraRig.cs` and `MatchPresentation.cs`. CRITICAL also hooked `MatchCameraRig` and `MatchDirector`, so check both sets merge cleanly.
- `SimCalibrationTests.EveryRunConceptAverages...` hit its 180 s timeout. Re-run it on the integrated tree; raise the timeout if needed.
- Not done, for known issues:
  - User-mode panels: B-047, 048, 049, 092, 093.
  - Director shots: B-111, 112, 116, 117.
  - HUD overlaps at 1080p and 4:3 were not re-measured after the last fix.

## UI-WP1 (done; Presentation tests 166/166)
- Tile is the default theme (`UiThemeKind.Tile` = 2).
- Barlow Bold/Medium TTFs and the OFL licence are committed.
- `MenuHubTests.TickerJoins` now accepts the diamond separator.
- Shots: `D:\PIXFootball\Renders\ui16\`.

## UI-WP2 (done; front-end tests 34/34)
- College / Road to Heisman is removed from the menus. The "College rules" match option stays.
- Integration to-dos:
  - Confirm the default theme is Tile. WP2's copy still showed Glass.
  - Set the version string to v0.11.0.
  - Add a `PixFootball.Sim` reference to `PixFootball.UI.asmdef`, needed by WATCH's `MatchHud.Popups.cs`.
  - The ticker's NEWS block renders dark (WP1 polish).
- Its full run had 13 failures in other areas: player models, blocking, tackling, engine penalty yards. Re-check on the integrated tree.

## UI-WP4 (done; 29 franchise pages in Tile)
- It was built against WP1's API overlaid from `UiWp1\PIX`. WP1 has since landed in the shared tree; confirm it compiles together.
- `MenuHubTests.TickerJoins...` should pass with WP1's ticker update.
- `FranchiseShots` replaces `CareerUiShots` (stale tab indexes): remove or update the old one.
- Open: two pages need 4 presses; no hero art yet.

## MOTION (done; EditMode 1248/1248)
- Fixed: B-018, B-020; back-pedal posture.
- Open, for the known-issues list:
  - Jog and shuffle slide 0.17 / 0.21 m/s.
  - Slow-gait floating 17–34%.
  - B-016 flight ratio 0.25.
  - B-017 unchanged.
- 100STYLE is converted but unused (too slow).
- Verify no Final IK file is staged before committing.

## ANIMLIB (done, follow-up running: legacy trench retarget, pop clips)
- Finger curve-name fix: every clip since v0.8 had claw hands. 694 clips now carry real finger mocap.
- One-line hooks:
  - `SimAgentView.cs` (B-057 dwell)
  - `MatchOfficials` / `MatchDirector` (`KickGood` signal) ×4
- New files lack `.meta`, including `AnimKit.Extra3.cs`.
- Its run had the 2 `Lod_*` `GfxTests` failing. Check them on the integrated tree.
- Purchases waiting on the owner: Cascadeur, Tomasi ($15.50), Mobility Pro ($50).

## QB (done after 2 passes; stance compliance 96.8%; log `qb.md`)
- Its final run was 1314/1320. The 6 failures are outside QB and look like SIM-FLOW pace/flow changes: pulling linemen, draft, 2 league-engine tests, quick-sim plays per team, and match-flow snaps (174 per game). Check them on the integrated tree.
- Known issues for v0.12:
  - Kneel and spike unverified.
  - Play-action fake reach.
  - Stumble recovery.
  - Scissored feet on some under-center throws.
- Flag `-pixQbV10` brings back the v0.10 QB behaviour.
- One-line hooks: `PlayStage.Stance.cs`, `SimAgentView.cs`, `BallFollower.cs`.
- Watch these tests at integration:
  - `BlockingTests.PullingLinemenLeaveTheirPostsAndEngageDefenders` (SIM-FLOW)
  - `TackleTests.TacklingFromBehindIsHarderThanHeadOn` (SIM-FLOW)
  - `BodiesTests.ASpikedBall...`: asset assertion in a private copy.

## CRITICAL (done, log `critical.md`, its EditMode run 1251/1251)
- One-line hooks in other files:
  - `SimAgentView.cs`: Animator culling set to `AlwaysAnimate` in `CreateForMatch`.
  - `MatchCameraRig.cs`: made `partial`, plus `FitFov`.
  - `MatchDirector.cs`: `FitFraming` in `ApplyCameraTarget`.
  - `BuildScript.cs`: `CriticalAudit.EnsureRoster()` at the top of `SwapInSlimLibrary`.
- `PlayerLod.cs`: `MatchCulling` changed.
- New files need `.meta`:
  - `BodyWatch.cs`, `BodyCheck.cs`
  - `MatchCameraRig.Fit.cs`, `MatchDirector.Frame.cs`
  - `CriticalAudit.cs`, `CriticalStances.cs`
  - `CriticalTests.cs`
- The roster fix is in the git-ignored `ArtLibrary.asset`. A build runs `EnsureRoster`, which restores the 12 prefab keys.
  - Verify the built player has all 15 bodies.
- Flags: `-pixNoKickFit`, `-pixCullUpdate`, `-pixBodyWatch <dir>`.
- Second pass: B-001–005, B-013 and B-060 fixed; B-059 partly fixed.
  - New one-line hook in `PlayStage.Begin`: `StanceRules.ShortYardage`.
  - B-058 (players 50–60 px tall) is open. The candidate fix is `Lod1Px` 120, but it costs frames.
- Handed on:
  - Floating while moving (27% of moving frames) → MOTION.
  - Sunk bodies in blocks and hand-fights → SIM-FLOW.
  - Kickoff/punt wide shot → WATCH.
