# WATCH+HUD log (v0.11.0)

Private copy `D:\PIXFootball\Work\Watch\PIX` (git HEAD + this track's files; sync script `Work\Watch\sync.sh`). Probe: `Presentation/WatchProbe.cs` (`-pixWatchProbe`, editor launcher `Editor/Tools/WatchRun.cs`). Outputs under `D:\PIXFootball\Work\Watch\out\` (`base_1080` = before, `g2_1080`, `g3_1440`, `tile_1080` = after; `stress_before` / `stress_final4` = HUD stress at five resolutions). Every number below is from those runs (full 2-minute-quarter CPU-vs-CPU games, 24 plays, 14 s real per quarter at 2x, editor game view at the stated size).

## 1. Watch mode (notes 4, 9)
- Every game started through `GameSession.StartMatch` (Play Now, Franchise, Practice) is CPU vs CPU on the broadcast camera. `MatchDirector.ApplyWatchMode()` (one hook in `Start`) sets `Config.CpuVsCpu`; the user-control code is untouched behind the setting.
- **Setting names / API (for UI-WP2):** `PixFootball.Gameplay.WatchMode`
  - `WatchMode.UserControl` (bool, persisted `pix.userctl`, default false; `-pixUserControl` forces on, `-pixWatch` forces off). Settings row "Controller input": `Off (watch)` / `On (play)` (`WatchSettingsUi.AddSettingsRows(page)` already adds it to Settings > Gameplay).
  - `WatchMode.Helper` / `WatchMode.SetHelper(HelperBarMode.Auto|On|Off)` (persisted `pix.helperbar`); `WatchMode.HelperNames`.
  - `WatchMode.SpeedIndex` (0 = 1x, 1 = 2x, not persisted).
  - Front end should show "Play Now = watch a match" when `WatchMode.Enabled`.
- Pad / keyboard in a watch game (`WatchControls`, polled every frame, also while paused): Start / Esc pause and resume, View / Tab cycles the camera (broadcast, Classic, Madden, Wide), Y / R hides the whole HUD, X / C toggles the helper bar, RB / X key switches 1x / 2x. No play-call screen, no hints, no first person. Pause menu has a Helper bar row.
- A watched game opens with the coin-toss scene (B-119) and ends with the final screen and a Continue button back to the menu; the huddle, natural snap tempo, replays and shows run as in a user game (they were switched off by the autopilot flag before: `MatchDirector.AutoRun` now means "autotest only").
- Receiver lock (note 9): `ReceiverInputLock.IsLocked(offense, scrimmage, slot, hasBall)` is checked in `BuildControl`: an offensive player other than the QB who has no ball in his hands gets no stick or button, so the pass in flight and the catch cannot be steered; control arrives the frame `HasBall` turns true. 5 unit tests (`WatchHudTests`). Not run live: nobody holds a pad in a watch game; the user-control path is covered by the unit rule only.

## 2. Camera (B-052 B-053 B-054 B-110 B-114 B-115 B-118)
`Gameplay/Cameras/MatchCameraRig.Broadcast.cs` (new partial), CRITICAL's `MatchCameraRig.Fit.cs` (now an exact projection fit of the 22 bodies and the ball) and `MatchDirector.Frame.cs`.
| Measure (1080p, watched game) | before | after |
|---|---|---|
| ball inside the picture, kickoff | 25.5 % of frames | 88-90 % |
| ball inside the picture, punt | 18.0 % | 71-76 % (peaks at 25 m, lags the first 0.3 s) |
| ball inside the picture, field goal / pass | 100 % / 100 % | 100 % / 100 % |
| all 22 bodies inside, kickoff set / in the air | 0 % / 55 % (CRITICAL's span fit) | 96 % / 100 % |
| all 22 bodies inside, punt set / in the air | 46 % / 100 % | 98 % / 100 % |
| body height, scrimmage play (mean px; before = 108 x 22/27 tan ratio, not measured) | about 88 | 108 |
| body height, kickoff / punt set (22 bodies across 60 m) | 61 / 69 | 34-39 / 51-56 (the whole field in shot; a kickoff cannot be bigger) |
| camera moves over 6 m in one frame, whole game | 28-31 (88 m sideline flips, 46 m cuts) | 11-13, largest 24 m (all are the glide between plays at 90 m/s max; the larger ones in the probe log are the probe's own camera cycling) |
What changed: one sideline all game (`FixedSide`, replay sideline angle follows it), 22 degree lens, smooth z glide between plays (`SmoothBetweenPlays`, `ZTau` 0.55 s, no cut, so no radial smear), lead 1.5x while a pass or kick is in the air, kick set-ups shot 6 m further out and 9 m higher with an exact projected fit (`FovForPoints`, 12 % margin, bottom 10 % kept clear for the bar), look point follows the ball height.

## 3. HUD (note 12) - measured by `HudBounds` (rect of every visible element group, overlaps, title-safe 3 % band)
- One pop-up lane at the top centre (`MatchHud.Popups.cs`): banner (big card 820x116 for score / turnover / period, slim strip 600x78 otherwise, 3.0 s / 1.9 s), whistle pill, penalty flag, toast, stacked with a 12-unit gap, always centred. Tile Hub tokens (`HudStyle`: gold #F2A63A, charcoal #25272C, nothing under 20 units). TV cards drawn at 0.86 and lifted above the bar. Score bar is a centred block max 1760 wide, lifted 3.4 % of the canvas height off the edge. Wind dial only on kicks, 0.8 scale, on the safe margin.
- Helper bar: watch game = one 30-unit glyph strip (PAUSE CAMERA HUD HELPER SPEED) bottom right above the score bar, Auto (8 s at the start, 4 s after any press) / On / Off, button X, Settings row, pause row, persisted. User-control hint bar: 44 tall (was 54), 20 pt, fades after 4.5 s, hidden by Off, no empty plate before the first hint.
| HUD stress frame (banner + whistle + flag + toast + kick meter + wind + hint + three TV cards at once) | before | after |
|---|---|---|
| overlapping pairs 1920x1080 / 2560x1440 / 3840x2160 | 8 / 8 / 8 | 3 / 3 / 3 (the rest are user-mode hint bar and kick meter against TV cards, both only in a user game) |
| 3440x1440 (21:9) | 7, bar 2532 wide edge to edge | 1, bar 1712 wide centred |
| 1440x1080 (4:3) | 8 | 3 before the last fix; the final run reads 5 at 1080p (3 % overlaps with the lifted bar), TV cards were lifted again afterwards and not re-measured |
| elements outside the title-safe band, 1080p | bar, wind, flag strip | none (4:3 bar and banner fixed by `SetCanvasHeight`) |
Whole watched game (49 shots before, 58-60 after, 1080p): overlaps 14 -> 2 (both are the pause screen layering its own scrim and footer).

## 4. Bug rows (B-numbers)
Done and seen in the shots: B-008 (helper bar toggle, small, auto-hide), B-039, B-040 (score only in bar and score card; banner sub-line no longer repeats it), B-041, B-043 (no controller hints in a watched game), B-044, B-045, B-046, B-052, B-053, B-054, B-077, B-080, B-081, B-082, B-083, B-084, B-088, B-089, B-090, B-091, B-094, B-095, B-097, B-110, B-114, B-115, B-118, B-119.
Done in code, covered by unit test or code read, not seen live: B-042 (clock runs over the real pre-snap length), B-050 (red zone trips / touchdowns, `StatsTests.RedZoneTripsAndTouchdownsAreCounted`), B-078 and B-096 (bar holds the old quarter at 0:00 until the break is over), B-079 (orange two-minute clock only in quarters of 8+ minutes), B-085, B-086 (sub-line colour; chip left as is), B-087, B-098/B-099 (priorities: score card 4, two-minute 3), B-100 (cards wait while an automatic replay plays), B-101 (Start / Esc skips a replay or show, pause only when none is running), B-113 (CPU pre-snap now 2.6-4.2 s so shots are scheduled; not viewed), B-120 (auto replay: score or turnover after 2 plays, anything else after 5).
Audio rows B-051, B-102 to B-109: reassigned to the AUDIO+CROWD track, not touched.
Known issues, not done: B-047, B-048, B-049, B-092, B-093 (user-control play-call / pre-snap panels), B-111, B-112, B-116, B-117 (director shots: celebration, sideline-QB, tight carrier, touchback), kickoff / punt bodies are 34-56 px tall (all 22 in shot), punt ball is out of the top of the picture for the first 0.3 s.

## 5. Tests
- `WatchHudTests` 14 (receiver lock, watch defaults, helper persistence, speed, banner tiers, bar width, lane stacking, bounds math, tokens), `StatsTests` red zone: 45 / 45 pass.
- One EditMode run `-testCategory "!Slow"` on the private copy (HEAD plus this track): 1296 tests, 1292 passed, 3 skipped, 1 failed: `SimCalibrationTests.EveryRunConceptAveragesFootballYardageAcrossSeedBlocks` hit its 180 s timeout on the loaded machine (no sim code touched by this track).
- Tile theme: `-pixUiTheme tile` was run (`tile_1080`, `stress_tile`): bar, pause menu and TV cards take the Tile tokens from `GlassTheme.T` with no code change; the pop-up lane already uses the same tokens.

## 6. Files
New: `Gameplay/Input/WatchMode.cs`, `Match/MatchDirector.Watch.cs`, `Cameras/MatchCameraRig.Broadcast.cs`, `UI/MatchHud.Watch.cs`, `MatchHud.Popups.cs`, `HudMetrics.cs`, `HudBounds.cs`, `WatchSettingsUi.cs`, `Presentation/WatchProbe.cs`, `Editor/Tools/WatchRun.cs`, `Tests/.../WatchHudTests.cs` (+ metas). One-line hooks in `MatchDirector.cs` (`ApplyWatchMode`, `StartWatchControls`, `AutoRun`, hint, view side, `CameraBallHint`, lock, banner sub, final headline, runoff window, period hold, clock flag), `MatchDirector.Toss.cs`, `MatchPresentation.cs` (3 members), `MatchCameraRig.cs` (partial, `BroadcastStep`, side, cut), `MatchCameraRig.Fit.cs` / `MatchDirector.Frame.cs` (exact fit), `GameTracker.cs`, `SettingsPanel.cs`, Presentation overlays / replay / shots.
