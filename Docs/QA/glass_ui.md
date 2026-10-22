# Glass UI QA log (track GUI)

Owner area: the Glass UI theme (`Scripts/UI/Glass/`, `Shaders/PixGlass`, the `*.Glass.cs` screen builders and the theme hooks in the existing UI files), its style guide (`Docs/UIStyleGuide_Glass.md`) and the editor render tools (`Scripts/Editor/Tools/GlassUiShots*.cs`).

## Status

**ALL SCREENS COMPLETE: NOT YET (checked 2026-10-21).** Every screen is built in Glass and rendered, and the real game path (overlay canvases over the live blur, match HUD, coin toss, final, halftime, intro and replay shows, menus, team select) is verified in play mode. Two items remain before the marker flips: GUI-043 (play call, penalty panel and pre-snap audible menu are verified only through the edit-mode harness, not yet captured in a real match) and GUI-044 (blur cost measured only at a 977x430 editor viewport; a 1440p player run is still needed). `UiTheme.DefaultKind` stays **Classic**. When both rows are closed this line becomes "ALL SCREENS COMPLETE (date)" and the integrator can flip the default. Until then Glass is opt-in: Settings > Game > UI theme, or `-pixUiTheme glass` on the command line for one run.

## How to look at it

- Edit-mode renders (no player, no `-nographics`): `Tools/unity.ps1 -Method PixFootball.EditorTools.GlassUiShots.All` (gallery + menus), `.Hud` (match HUD, play call, penalty, final, pause, toss), `.Extras` (TV cards, replay, free roam, kick meter, pre-snap, loading), `.Resolutions` and `.HudResolutions` (3840x2160, 1920x1080, 3440x1440 21:9, 1440x1080 4:3). Output: `D:\PIXFootball\Renders\glass_ui\` (outside the repo; images are not tracked).
- Gamepad reachability audit: every shot appends a line to `D:\PIXFootball\Renders\glass_ui\navaudit.txt` (every interactable selectable must be reachable from the first by up / down / left / right, the graph the EventSystem walks with the d-pad and stick).
- Real game path (play mode, a window, no `-batchmode`; `-pixUiTheme glass` forces the theme for the run):
  - menus, blur, frame cost: `Unity.exe -projectPath ... -executeMethod PixFootball.EditorTools.PlayModeRunner.RunAutoTest -pixGlassProbe -pixUiTheme glass -pixTier 3 -pixGlassProbeOut <dir>` (`GlassProbe`, writes `glassprobe.txt` and PNGs).
  - broadcast shows, replay, overlays: `... -pixAutotest -pixAutotestBroadcastOnly -pixUiTheme glass -pixTier 3 -pixAutotestOut <dir>`.
  - a played match (HUD, banners, coin toss, final): `... -pixAutotest -pixAutotestMatchOnly -pixAutotestNoV3 -pixUiTheme glass -pixAutotestSpeed 4 -pixAutotestOut <dir>`.
  - the same flags work on a built player (`PIXFootball.exe -pixAutotest -pixUiTheme glass`).
- Fast compile check without Unity: `Tools/Glass/compile_check.ps1 -ProjectList UI,Presentation,Editor`.

## Screens

| Screen | Glass builder | Edit-mode render (2560x1440 unless noted) | Real game path |
|---|---|---|---|
| Component gallery (QA only) | `GlassGallery` | `gallery_p0/p1` | n/a |
| Main menu | `MainMenuController.Glass.cs` | `menu`, + 1920x1080, 3840x2160, 3440x1440, 1440x1080 | yes: `real/real_menu.png` (blur on), `real_menu_fallback.png` (static frost) |
| Team select + match options | `TeamSelectPanel.Glass.cs` | `teamselect`, all four extra sizes | yes: `real/real_teamselect.png` |
| Settings, 5 pages | `SettingsPanel.Glass.cs` | `settings_page0..4`, all four extra sizes | via the same panel code as the menu, not separately captured |
| Confirm dialog | `UiDialog` + `GlassWidgets.BuildDialog` | `dialog` | not captured |
| Pause menu (match) and sound page | `MatchHud.Glass.cs` | `pause`, `pause_sound`, 3440x1440, 1440x1080 | not captured in a real match |
| Coin toss | `MatchHud.Glass.cs` | `toss` | yes: `real_match/match_cointoss.png` |
| Play call (formation, set, play) | `PlayCallScreen`, `PlayDiagramGraphic` | `playcall0..2`, 3440x1440, 1440x1080 | GUI-043 |
| Pre-snap panel, audible list | `PreSnapPanel` | `presnap` | GUI-043 (the pre-snap HUD itself: `real_match/match_presnap_1.png`) |
| Penalty panel | `PenaltyPanel` | `penalty` | GUI-043 |
| In-match HUD: bottom bar (2K8 layout), top-left scorebug, banner, toast, hint, flag strip, wind compass, kick meter | `MatchHud.Glass.cs`, `KickHud` | `hud_bottom`, `hud_top`, `hud_banner`, `hud_flag`, `hud_topleft_kick` | yes: `real_match/match_*.png` (banner, presnap, tackle, throw, kick) |
| TV cards (stats, score update, stadium, matchup, lower third, next play, ticker) | `TvOverlays`, `TvUi` | `overlays_cards`, `overlays_score_stadium`, `overlays_matchup` | yes: `real_bc/bc_overlay_*`, `bc_gallery_*` |
| Replay HUD | `ReplayHud` | `replay_transport` | yes: `real_bc/bc_manual_*` |
| Final screen and final-score show | `MatchHud.Glass.cs`, `BroadcastDirector.Shows.cs` | `final` | yes: `real_match/match_final.png` |
| Halftime, game intro show pages and stat tables | `BroadcastDirector.Shows.cs` (page skin) | n/a (coroutine pages) | yes: `real_bc/bc_halftime_stats.png`, `bc_halftime_performers.png`, `bc_intro_*` |
| Free-roam HUD | `FreeRoamHud` | `freeroam` | not captured |
| Loading screen | `GlassLoading` | `loading` (+ 3440x1440, 1440x1080) | not captured (needs a scene change) |

Gamepad: `navaudit.txt` lists every shot with its selectable count; all selectable screens report "all reachable" (menu 4, team select 38, settings 9 to 14 per page, pause 7 and 6, toss 2, final 1, dialog 6). Play call, penalty and the pre-snap menus are driven by their pad models (`PlayCallModel` etc.), not by Selectables, exactly as in Classic.

## Issues

Row format as in `README.md`: `| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |`

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| GUI-001 | fixed | S1 | render tool | `GlassUiShots.Bind` | First render showed no UI: the UI camera culled layer 5 but the canvases were on layer 0 | every canvas child is put on layer 5 for the shot | `gallery_p0` |
| GUI-002 | fixed | S1 | blur | `GlassBackdropDriver` | Blur pass did not run in edit mode and the editor resolved quality tier Low | `[ExecuteAlways]` on the driver, tier override in the tools | `gallery_p0`; in play mode `glassprobe.txt` shows the pass ran on 329 of 330 frames |
| GUI-003 | fixed | S2 | stage | `GlassStage` | Player stood on the wrong side of the camera target | `CamPosition (0.2,1.30,4.7)`, `CamTarget (1.25,1.05,0)` | `menu` |
| GUI-004 | fixed | S2 | footer | `PromptFooter` | Hint bar empty in edit-mode shots (OnEnable does not run) | `ForceRebuild()` in the shot settle step | `hud_bottom` |
| GUI-005 | fixed | S1 | crest | `GlassImageSkin` | Crest sprites became white boxes or invisible: the skin was created before the sprite was assigned, and the Image alpha used the previous role | a sprite assigned later is tracked (`_lastSprite`), the role flips to Skip and the Image alpha is set after the role is resolved | `teamselect`, `pause` (state card crests), `hud_banner` (bottom bar crests) |
| GUI-006 | fixed | S2 | play call | `PlayDiagramGraphic` | Black diagram background | removed the RectMask2D exclusion | `playcall2` |
| GUI-007 | fixed | S2 | play call | tag chips | White blank chips: the kit sprites have baked text | in Glass the chips are dark glass with category-coloured live text | `playcall1` |
| GUI-008 | fixed | S2 | shader | `PixGlassUI.shader` | Glow clipped at the quad edge and looked chunky | `glowScale` token and a larger quad pad | `menu`, `teamselect` |
| GUI-009 | fixed | S2 | prefabs | `Scripts/UI/Glass` | Several MonoBehaviours shared a file, which breaks prefab script references | one class per file | prefab build log: 25 prefabs saved |
| GUI-010 | fixed | S1 | licence | prefabs | Licensed sprites must not be referenced by committed prefabs | `GlassIconKey` stores the key and re-resolves from the `GlassIcons` registry; non-asset and `_Licensed` sprites cleared at prefab build | test `GlassPrefabsExistAndNeverReferenceLicensedArt` |
| GUI-011 | fixed | S3 | layout | gallery, hero, final, banner rail, pause card | Overlaps and wrapped hero text in first renders | layout constants adjusted | `gallery_p0`, `menu`, `final`, `hud_banner`, `pause` |
| GUI-012 | fixed | S2 | loading | `GlassLoading` | Team nicknames ran into the tip card | matchup block moved up, status label aligned to the bar | `loading_2560x1440.png` |
| GUI-013 | fixed | S2 | TV cards | `TvUi.Text` | DejaVu is wider than the Classic face: names, temperature and replay tag overflowed their card boxes | in Glass every `TvUi.Text` auto-shrinks (min half size) and is ellipsised; temperature tile text recentred; replay tag text gets padding | `overlays_score_stadium`, `replay_transport` |
| GUI-014 | fixed | S2 | replay | `ReplayHud` | Second keycap row overlapped ("REWIND/FORWARD" cut) | smaller label size and spacing in Glass | `replay_transport` |
| GUI-015 | fixed | S3 | settings | `GlassKit.Stepper` | Long labels ("CAMERA SHAKE ON HITS") touched the left chevron | label inset widened | `pause_sound` |
| GUI-016 | fixed | S3 | theme | `UiTheme` | No way to force the theme for an autotest or smoke run | `-pixUiTheme glass` or `classic` on the command line | the three play-mode runs above |
| GUI-017 | fixed | S1 | 21:9, 4:3 | main menu, settings card, pause card | Content laid out for a 1080 high canvas ran off the bottom and into the footer on 21:9 (canvas about 930 high) | each is a block inside a `FitScale` host that shrinks it about the top-left corner on shorter canvases | `menu_3440x1440`, `settings_page2_3440x1440`, `pause_3440x1440`, `settings_page2_1440x1080` |
| GUI-018 | fixed | S2 | pause | `MatchHud.Glass.cs` | The hint pill and the bottom scorebug sat under the pause footer and the scrim, overlapping its prompts | both step aside while paused (the state card shows the score) | `pause` |
| GUI-019 | fixed | S3 | pause | sound page | The pause card kept its full height on the short sound page | the card is 610 high on that page | `pause_sound` |
| GUI-020 | open | S3 | team select | 21:9 and 4:3 | The photoreal player is partly behind the crest grid at 4:3 and touches the last column at 21:9 | needs a per-aspect `GlassStage` framing shift; acceptable because the grid is glass over a blurred player | `teamselect_1440x1080`, `teamselect_3440x1440` |
| GUI-022 | open | S3 | free roam | `FreeRoamHud` | Camera plate shows as an empty glass bar in the edit-mode shot (its text is filled by Update at runtime) | none expected; confirm in a real free-roam run | `freeroam` |
| GUI-023 | open | S3 | play call | 4:3 | The three play cards keep a tall empty diagram area at 4:3 (the diagram scales by width) | cap the diagram height | `playcall2_1440x1080` |
| GUI-024 | open | S3 | final | 4:3 | In the harness the CONTINUE button sits on top of the hint pill and bottom bar (the harness leaves them on; confirm the real final hides them) | hide the hint pill and bottom bar while the final shows, as the pause now does | `final_1440x1080` |
| GUI-025 | open | S3 | replay | 21:9 and 16:9 | The play title ("TOUCHDOWN") sits against the wind compass at the top right | shift the replay tag left of the compass | `replay_transport`, `real_bc/bc_manual_paused_slow_ui.png` |
| GUI-030 | fixed | S1 | verification | real overlay path | The blur on Screen Space Overlay canvases (global texture orientation and validity after post-process) was only verified through the harness, which draws the canvases with a camera | `GlassProbe` in play mode: blur visible behind every glass surface, pass ran every frame, static frost fallback looks right | `real/real_menu.png` vs `real/real_menu_fallback.png`, `real/real_teamselect.png` |
| GUI-031 | fixed | S2 | show pages | halftime, intro, final-score show | Page builders run inside broadcast coroutines and cannot be rendered by the harness | captured through the existing broadcast autotest with `-pixUiTheme glass` | `real_bc/bc_halftime_stats.png`, `bc_intro_matchup.png`, `real_match/match_final.png` |
| GUI-043 | open | S2 | verification | play call, penalty panel, pre-snap audible menu | Not yet captured in a real match (the autotest never opens them) | run a played match with scripted pad input through the glass play call and a flag | pending |
| GUI-044 | open | S2 | performance | blur pass | Cost measured only in an editor game view of 977x430 | player build at 1440p and 4K with `GlassProbe` (`-pixGlassProbe`), on a mid-range GPU as well | pending |
| GUI-045 | wontfix | S3 | perf tool | `GlassUiShots.Perf` | The edit-mode timing tool (camera rendered in a loop with a 1 pixel readback) gives meaningless numbers: toggling the pass or its pass count changes frame time by 10x to 100x, and one run stalled the editor | superseded by `GlassProbe` in play mode; the tool is kept but its numbers must not be quoted | `Logs/glass_perf*.log` |

## Performance

Measured by `GlassProbe` in the real path (editor play mode, tier 3, HDRP, Direct3D12, RTX 5090 Laptop GPU, game view 977x430, main menu with the live stadium behind it; three interleaved rounds of 240 frames each):

| Setting | Frame | GPU (FrameTimingManager) | CPU |
|---|---|---|---|
| Blur pass on (329 of 330 frames ran the pass) | 3.18 to 3.65 ms (274 to 315 fps) | 2.35 to 2.45 ms | 3.17 to 3.66 ms |
| Blur pass off (static frosted fallback) | 3.16 to 3.52 ms (284 to 316 fps) | 2.40 to 2.41 ms | 3.17 to 3.52 ms |

Within noise at this size: the pass is a half-resolution copy plus two separable 9-tap gaussians (four compute dispatches) on two 8-bit targets, so its cost scales with the viewport (a quarter of the pixels, per frame, only while a glass surface is on screen). It is not measured at 1440p or 4K (GUI-044); the Low quality tier skips it entirely and the `blurPasses` and `blurRadius` tokens trim it further. CPU cost of the glass UI itself is the vertex build of the surfaces (rebuilt only when a surface changes) plus one extra draw per glass material batch.

## Tests

`Assets/_Project/Tests/EditMode/Presentation/GlassUiTests.cs` (14 tests): token defaults, JSON round trip, three-stop surface gradient, text contrast, theme always resolves without the asset, theme default is Classic until the marker above flips, forced theme override, blur tier rule, blur extent, flat-panel classification, navigation step, 32-team grid movement, prefab library never references licensed art, loading titles and tips.

Full EditMode suite after the last change (2026-10-21): 812 tests, 812 passed, 0 failed (`Logs/glass_tests2.xml`).
