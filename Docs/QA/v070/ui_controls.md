# v0.7.0 Track C: controls, HUD, call sheet, menus, replay (`C-###`)

Owner area: `Scripts/UI/**` (except the menu hub, see `ui_menus_part.md`), `Scripts/Presentation/**` (replay, probes), `Gameplay/Match/{MatchInput,FieldMarkers,RouteOverlay,MatchDirector.Routes,MatchDirector.PreSnap}.cs`, `PixSettings` display fit. Playtest notes 1, 2, 10, 11, 16, 20, 21, 23, 27, 31 (`PLAYTEST_v060.md`) plus the references in `Docs/QA/REFERENCE_MaddenUI.md` (layout and flow only).

Verification: edit-mode renders (`GlassUiShots.V7All`, output `D:\PIXFootball\Renders\v070_c\ui\`), a real scripted match (`-pixAutotest -pixAutotestBroadcastOnly -pixControlsProbe 1`, output `...\v070_c\probe*\`), EditMode tests `V070ControlsTests` (new), `UiFlowPlayCallUiTests`, `GlassUiTests`.

## Controls table (for the v0.7.0 patch notes)

| Situation | Button (keyboard) | Action |
|---|---|---|
| Offense, at the line | hold LT (V) | route view: every receiver's route drawn on the turf in the team colour with arrowheads, read numbers 1-5 at the route ends, the primary read thick and white-hot, hot routes orange, the ball carrier's run path, and the pass buttons (X Y B A RB / LB) over the receivers. Follows audibles, flips, hot routes and motion |
| Offense, quarterback in the play | hold LT (V) | the same route view while the quarterback has the ball (no slower tuck any more) |
| Offense, at the line | D-pad down (T) | **timeout** (moved from LT) |
| Defense, kicking and return teams, at the line | LT (V) | timeout (unchanged, there is no route view on defense) |
| Quarterback | tap a receiver button | **lob / touch**: a soft touch pass to a close receiver (under 19 yards), a high lob to a deep one |
| Quarterback | hold a receiver button (about 0.17 s) | **bullet**; it leaves the moment the hold passes the threshold (this swaps the old mapping: tap was bullet, hold was lob) |
| Play call sheet | D-pad left / right | move along the play cards |
| Play call sheet | D-pad up / down | change group (formation, concept, play type, personnel package) |
| Play call sheet | A | call the highlighted play |
| Play call sheet | B | clear a search, then back to SUGGESTED |
| Play call sheet | LB / RB | page the cards (3 per page) |
| Play call sheet | RT / LT (Shift / V) | next / previous tab |
| Play call sheet | X | cycle the type filter |
| Play call sheet | Y | flip the play (offense) |
| Play call sheet | RS left / right (J / L) | quick find by initial letter A-Z |
| Play call sheet | `/` on the keyboard | type to search by play name (Enter / Esc leave) |
| Play call sheet | R3 (Backspace) | call a timeout |
| Everywhere in play | View / Menu | camera / pause (unchanged) |

## Call sheet design (note 11 / 20 / 23, references 48 / 52 / 53 / 51)

- Glass overlay strip along the bottom of the screen over the live, blurred field (not a full-screen page). `PlayCallStrip` renders the sheet mode of `PlayCallModel` (`sheet: true`); the Classic theme keeps the old three-stage page (`PlayCallScreen`).
- Tabs: SUGGESTED, FORMATION, CONCEPT, PLAY TYPE, PERSONNEL, RECENT, FAVORITES, SPECIAL TEAMS (when the situation has kicks). Defense uses the same tabs (formation = defensive front, concept = coverage name, personnel = unit).
- Left column: the group list (5 to 8 rows, scrolls). Centre: three play cards, the focused one is wider and has the mini-field diagram enlarged. Right column: the coach's three picks stay visible on every tab (on SUGGESTED it shows the situation).
- Presses to snap-ready: the sheet opens on SUGGESTED with the first pick focused, so a suggested play is **1 press (A)**, the second or third pick 2 or 3; from another tab B returns to SUGGESTED. Test: `AnySuggestionIsCalledInAtMostThreePresses`.
- FAVORITES lists the plays called twice or more (PlayerPrefs counts `pix.fav.*`), RECENT the last 12 calls.
- Audible list (pre-snap X) is the compact side list: play art, personnel, concept, PASS / RUN tag per row.

## Display and resolution (note 2)

- `CanvasScaler` is now `Expand` (reference 1920 x 1080 is always fully on screen; the canvas grows on the other axis), so nothing laid out for 1080 high can be cut off at 21:9, 16:10, 4:3 or in a window.
- `PixSettings.ApplyDisplayMode`: first launch is borderless full screen at the monitor's native size; a stored size larger than the monitor is scaled down keeping its aspect; a window leaves 110 px for title bar + taskbar. Pure maths `PixSettings.FitSize` is unit tested.
- Resolution shots of the call sheet and the HUD widgets: 1366x768, 1440x1080 (4:3), 1920x1080, 1920x1200, 2560x1440, 3440x1440, 3840x2160 in `v070_c\ui\`.

## Issues

Row format as in `README.md`: `| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |`

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| C-001 | fixed | S1 | markers | `FieldMarkers`, `MatchHud.Field` | The user's ring was a thin teal band on green turf and hid behind bodies from a low defense camera: nothing told the user who they controlled on defense | ring 2.5 m with a dark outline on both edges, plus a HUD marker (pill with number and position, bobbing) over the controlled player that no body can hide, offense and defense, follows B switches at once | `field_widgets_*`, `c_defense_*` |
| C-002 | fixed | S1 | display | `UiKit.CreateCanvas` | Match-width-or-height scaling at 0.5 cut layouts built for 1080 high off the bottom on wide screens | `Expand` scaler | `V070ControlsTests.EveryAspectKeepsTheWholeReferenceRectangleOnScreen`, resolution shots |
| C-003 | fixed | S1 | display | `PixSettings` | A first launch (or a stored larger size) opened a 2560x1440 window on a smaller monitor | native-size first launch, fit to monitor, windows leave room for the taskbar | `DisplayFitNeverExceedsTheMonitorAndKeepsTheAspect` |
| C-004 | fixed | S2 | controls | `MatchDirector`, `RouteOverlay` | No way to see the routes | hold LT route view (see table) | `RouteViewDrawsEveryReceiverRouteWithReadNumbers`, `RouteViewFollowsFlipsAndHotRoutes`, `c_routes_*` |
| C-005 | fixed | S2 | controls | `MatchDirector.BuildThrow` | Tap was a bullet, hold a lob | hold = bullet at 0.17 s, tap = touch / lob | `HoldIsABulletAndTapIsALobOrTouch` |
| C-006 | fixed | S2 | call sheet | `PlayCallModel`, `PlayCallStrip` | Full-screen three-stage call page, many presses | the sheet (see above) | `V070ControlsTests`, `sheet_*`, `c_sheet_*` |
| C-007 | fixed | S2 | field | `FieldLogo` | Two logos at midfield: the weather clones (`WX_*`) and the original stadium material of the markings use the texture that still has the painted shield | every renderer using a `*FieldMarkings*` material is moved onto the cleaned markings material when the home crest is drawn | `match_playcall`-style midfield check |
| C-008 | fixed | S1 | replay | `ReplayPlayer.Pose` | The replay showed only the ball: the recorded line-stance setback was written to the actor's local position, but a replay actor is the avatar object itself (the live view has a view root above it), so all 22 actors were moved to the origin | the setback is applied along the facing to the world position | `-pixReplayProbe` dump (positions matched the recording after the fix), `bc_manual_*` |
| C-009 | fixed | S3 | HUD | `MatchHud.Field` | No reason for the end of a play | whistle banner "TACKLED BY #52" etc. from `PlaySimulation.EndInfo` | `field_widgets_*`, `c_whistle_banner` |
| C-010 | fixed | S3 | glass | `UiTheme` | Glass was opt-in | `DefaultKind = Glass` (test `ThemeDefaultsToGlass`) | `GlassUiTests` |
| C-011 | fixed | S2 | HUD | `MatchHud` pause | In a real match the pre-snap panel, the user marker and the route badges stayed up under the glass pause menu (found by the real-match probe, GUI-043) | all hidden while paused and in replays | `c_pause_in_match` (before), probe re-run |
| C-012 | fixed | S3 | glass | `PreSnapPanel` audible list | Wide keyboard caps ran into the play art of the audible rows | the art column starts after the measured key / glyph | `c_audible_list` |
| C-013 | fixed | S3 | replay | `ReplayHud` | The play title ("INTERCEPTION") sat on the wind compass (GUI-025) | tag and slow-motion tag moved left of the compass | `bc_manual_*` |
| C-014 | fixed | S3 | call sheet | `PlayCallStrip` header and cards | First render: coach text overlapped the clock, the timeout label overlapped "PLAY CLOCK", RUN / PASS chips overlapped long titles, defense zone bubbles spilled out of the diagram | header re-laid out, title width excludes the chip, the diagram well masks its content | `sheet_*` |
| C-015 | open | S3 | call sheet | `PlayCallStrip` | At 4:3 and 21:9 the strip keeps its 1824 x 440 design size (it does not grow with the canvas); side cards shorten long names ("PA B...") because of the chip | accepted; revisit with the next playbook pass | `sheet_off_suggested_1440x1080`, `_3440x1440` |
| C-016 | fixed | S2 | verification | real match, defense | The defense marker and the call sheet on defense were only verified in the harness | scripted real-match probe (`-pixControlsProbe 2`, the user defers so the defense plays first): marker over #59 MLB at the line and #47 FS mid play, sheet tabs on defense, whistle logic | `v070_cprobe7defense_*`, `c_sheet_def_*` |
| C-017 | fixed | S2 | verification | GUI-043 / GUI-044 | Call sheet, audible list and pause were not captured in a real match; the blur cost was measured only at 977x430 | real-match probe captures (call sheet, route view, audible list, pause, defense); `GlassProbe` at 2560x1440 on an RTX 5090 laptop GPU: blur on 6.44 ms vs off 6.34 ms (about 0.1 ms) | `v070_cprobe4`, `v070_cglassprobeglassprobe.txt` |

## Verification log

- EditMode: `V070ControlsTests` (call sheet, routes, throw style, display fit, canvas maths), updated `UiFlowPlayCallUiTests`, `GlassUiTests` (default Glass). Full suite result: see the line at the end of this file.
- Real match probe (`-pixControlsProbe 1`, `v070_c\probe4`): call sheet opens on the suggestions with 8 tabs, RT / LT tab switching, search, B behaviour, R3 timeout, one A press calls the first suggestion; hold LT draws the routes (run path before the snap, receiver routes in the team colour with read numbers after a pass audible), follows audible, flip and hot route, works as the quarterback during the play; a held receiver button throws the pass; the audible list shows play art.
- Replay probe (`v070_c\replay2`): all 22 players replay at their recorded spots, one crest at midfield.
- Edit-mode renders: `v070_c\ui\` (call sheet on every tab, both sides, 7 resolutions; field widgets at 6 sizes), menus in `v070_c\menus\` (see `ui_menus_part.md`).
- Full EditMode suite (D:/PIXFootball/Logs/c_tests_full.xml): 869 tests, 865 passed, 4 failed. Not Track C: `PhotoRosterV2Tests.FifteenHumanoidBodiesWithPadsAndFourLodsEach` and `PhotoRosterV2Tests.PaddedJerseyAndPadSilhouettesAgreeAcrossLods` (Track D roster work in progress), `EngineRealismTests.NoPlayerExceedsRealisticTopSpeed` (Track B pacing). Track C: `MatchV030Tests.CallModelHasEveryScreenForOffenseDefenseAndKicks` expected the old Playbook tab (the default call model is now the sheet): test updated, `MatchV030Tests` rerun 18 of 18 pass. All 19 `V070ControlsTests` and the 7 `MenuHubTests` pass.
