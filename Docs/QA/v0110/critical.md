# CRITICAL track log (v0.11.0, owner notes 2 and 3)

Notes: "players floating, half bodies missing, the whole team disappearing on kickoff" and "keep working on the player models and their stances".
Everything below was measured in the BUILT player (broadcast camera, CPU vs CPU, `-pixAutotest -pixAutotestMatchOnly`, four 2-minute quarters at 2x) with the new probe `Gameplay/Player/BodyWatch.cs` (`-pixBodyWatch <dir>`), and in the editor with `Editor/Tools/CriticalStances.cs` (`-pixCriticalStances`). Raw output: `D:\PIXFootball\Renders\critical_v0110\` (`watch_base2` = v0.10.0 code + probe, `watch_crit1` = fixed, `watch_crit1_old` = the fixed build run with the old behaviour switched back on, `stances_*`, `strips`). Private copy: `D:\PIXFootball\Work\Critical\PIX` (= git HEAD + this track's files, via `sync2.sh` / `hooks.sh`). Nothing committed.

## 1. Root causes (with evidence)

| # | Cause | Evidence | Fix |
|---|---|---|---|
| 1 | **The broadcast lens never framed a kickoff.** Fixed 27 degree lens, only follows the ball along the field; a kickoff spreads the teams over 55-60 m. The receiving team (11 bodies) stood outside the picture for the whole set and the start of the kick. Unchanged since v0.9; the owner now watches CPU-vs-CPU on this camera. | `watch_base2`: kickoff PlayCall 79/80, PreSnap **107/107**, Live 273/1288 frames with the whole defense off screen. The 22-body table at the kickoff set (`watch_before/watch_events.txt`) shows vp.x -0.5..0.08 for the receivers. v0.10 screenshot `strips/kickoff_setup_BEFORE_v0.10.png`. | `MatchCameraRig.Fit.cs` + `MatchDirector.Frame.cs`: while a kickoff or punt is set up and in the air (until a returner holds the ball) the focus is the middle of the 22 bodies and the lens widens to hold them (`FovForSpan`). `-pixNoKickFit` = old look. |
| 2 | **`Animator.cullingMode = CullUpdateTransforms`** on the 22 match bodies (since v0.2). An off-screen body keeps a stale pose, but the LateUpdate layers (ground fit, action ground, stance IK, foot plant, limb IK, motion-matcher inertializer, BodySanity) keep reading and changing the stale bones. Since v0.10 (MOTION matcher, CHOREO, QB layers) they work for seconds on a frozen pose: hips **5 m below the turf** (kickoff coverage runners, `Sprint`), NaN bones, trunks twisted 90-160 degrees, bodies sunk 0.2-0.5 m, and the first frames after a body enters the picture show that pose. This is the "half bodies", "bugged out stances", "floating" of the notes. | `watch_base2` events: `bone rig:Hips at (-20.9, -5.1, -4.6)`, `sole=-4.4` on all 5 outer coverage men each kickoff. Editor A/B (`stances_noprep` vs `stances_noprep_fix`, same plays, camera at the broadcast spot, animators not forced on): 1034 vs 202 bad body-samples (twisted trunk 384 -> 0, off place 575 -> 1, NaN / bad scale 48 -> 0). | `PlayerLod.MatchCulling` = `AlwaysAnimate` (one-line hook in `SimAgentView.CreateForMatch`). `-pixCullUpdate` = old mode. No frame-time cost: Live frames avg 10.8 ms vs 10.9 ms with the old mode (same build). |
| 3 | **The ArtLibrary rebuild dropped the 12 roster prefabs** (`photo_player_{small,medium,large}_{athletic,runner,stocky,tall}`): `ArtLibraryBuilder` only registers the 3 base bodies, `PhotoRosterV2.RegisterArtLibrary` is a separate step. `PlayerAvatar` silently falls back to the 3 base bodies, so the 15-body roster and the BODIES v5 prefab work never reached the player. | `watch_before/watch_stanceclips.txt` (v0.10 build): photo prefabs `small,medium,large` only; `watch_crit1`: all 15. | `CriticalAudit.EnsureRoster()` (prefabs only, idempotent), called first thing in `BuildScript.SwapInSlimLibrary` (one line). |

Not the cause (checked, with numbers): GPU skinning / `meshDeformation 2`, PlayerLod level switching, padded skinned bounds, the BODIES v5 prefab rebuild and the hand-written `PixArmProportions.v5.txt` / patched `BodyHelperBones` GUID, the slim ArtLibrary clips.
* Static audit of all 15 prefabs (`CriticalAudit.Prefabs`, `prefabs_roster.txt`): human avatar, 23 parts, 52 bones, no null mesh / renderer / bone, bindpose counts match, bounds sane (only the eyes have a tiny localBounds, padded by `PhotoPlayer.Init`).
* Runtime, 13-14 k frames x 22 bodies per run: **0 frames with a missing part, 0 culled-while-on-screen, 0 T / bind pose** (arms out in a line or every muscle at zero).
* Slim library in the build: `watch_stanceclips.txt` resolves a clip for every stance kind x role (298 keys, 1622 clips, 0 "NO CLIP").
* Pre-snap stances in the editor (`stances_before`, 3806 body-samples over every formation, special, goal line, huddle, first 2.5 s after the snap): feet within +-3 cm of the turf, no T pose, no twist; the contact sheet (`stances_after1/stance_tiles.png`, one tile per stance kind and mirror) shows no stance that breaks the reference (tackles up with forearms on thighs, guards lower, centre over the ball, DL hand down / four-point, WR stagger, CB / LB / S two-point, QB under centre and shotgun).

## 2. Before / after (built player, same seed, same machine load)

| per run (13.7-14.5 k frames) | v0.10 code + probe (`watch_base2`) | fixed (`watch_crit1`) | fixed build, old behaviour (`crit1_old`) |
|---|---|---|---|
| Kickoff PreSnap: frames with the whole receiving team off screen | **107 / 107** | **0 / 131** | 128 / 128 |
| Kickoff PlayCall | 79 / 80 | 1 / 147 | 131 / 132 |
| Kickoff Live | 273 / 1288 | **10 / 1584** | 347 / 1649 |
| NaN / absurd bone, on-screen body-frames | 344 (6 bodies) | **0** | 472 |
| Body off its place (hips under the turf / far from the root), on screen | 1405 | **132** | 1474 |
| Sunk body (lowest sole more than 5 cm into the turf), on screen | 2971 | 1893 | 3579 |
| Twisted trunk (shoulders 62+ degrees off the hips), on screen | 305 | 365 | 317 |
| Pop on entry (bad pose in the first 5 frames in the picture) | 776 | 376 | 999 |
| Floating feet (lowest sole 4+ cm up while the simulation has him on the turf), on screen | 25760 (9.96 %) | 26576 (8.35 %) | 41829 (13.9 %) |
| Missing parts / culled while on screen / T pose | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| Live-play frame time (avg / p99) | 12.4 / 29.6 ms | 10.8 / 33.5 ms | 10.9 / 33.9 ms |

Editor stance audit, all formations and specials (`CriticalStances`): bad body-samples **195 (5.1 %)** at the start -> **97 (2.5 %)** after (and 1034 with the old culling mode, 202 with the new). In the `set` and `snap` samples only 5 remain: goal-line four-point ends 3 cm into the turf. The rest are huddle samples (the check assumes a stance; the body is walking).

## 3. Still wrong (said plainly)

1. **Floating while moving: 8 % of on-screen body-frames.** Run / Backpedal / Route / Shuffle / Sprint / Carry have the lowest sole 4+ cm up in 26-29 % of their frames (5-15 cm; flight phases plus a stride that does not land), Block 8 %, Engaged 11 %, Idle 1.4 %, Stance 0.16 %. Locomotion is MOTION's (matcher, `FootPlant`), pair takes are SIM-FLOW's (`ContactDirector`, `EngageStepper`). Final IK (`Grounder`) is the tool for it once MOTION documents it; this track did not use it (the pose layers it would sit beside are all mine, the stance grounding is already inside +-4 cm).
2. **Sunk 5-10 cm in blocks and hand fights** (Engaged 2.3 %, Block 0.9 % of their frames), the same owner.
3. **Twisted trunk 0.1 %**: `GetUp` 25 %, `Fallen` 6 %, `TackleWrapUp` 1.9 %, `Tackled` 1.3 % of those frames (ground states, partly legitimate), run / block under 0.2 %.
4. Players are small in the wide kickoff shot (all 22 fit, the lens is about 45-50 degrees instead of 27); an "end-zone" high angle would read better and is the WATCH track's camera work.
5. Between plays the live stage is hidden for replays (857 frames with all 22 views inactive in `watch_crit1`, no player affected); the probe counts those as "whole team off screen" in PostPlay.
6. `popOnEntry` still counts brief floating of runners that enter the picture mid-stride (the probe does not separate that from a stale pose).

## 4. Files

New: `Gameplay/Player/BodyWatch.cs` (match probe), `Gameplay/Player/BodyCheck.cs` (the per-body checks), `Gameplay/Cameras/MatchCameraRig.Fit.cs`, `Gameplay/Match/MatchDirector.Frame.cs`, `Editor/Tools/CriticalAudit.cs` (prefab audit, `EnsureRoster`), `Editor/Tools/CriticalStances.cs` (stance audit), `Tests/EditMode/Gameplay/CriticalTests.cs`.
Changed (owned): `Gameplay/Player/PlayerLod.cs` (`MatchCulling`).
One-line hooks in other tracks' files: `SimAgentView.cs` (`cullingMode = PlayerLod.MatchCulling`), `MatchCameraRig.cs` (class made `partial`, `FitFov(ref wantBcFov)`), `MatchDirector.cs` (`FitFraming(ref focus, ref fvel)` in `ApplyCameraTarget`), `BuildScript.cs` (`CriticalAudit.EnsureRoster()`).
New files need `.meta` (generated on first editor open).

## 5. How to run

* Match probe in a player: `PIXFootball.exe -pixAutotest -pixAutotestMatchOnly -pixAutotestNoV3 -pixAutotestSpeed 2 -pixBodyWatch <dir> [-pixBodyWatchShots N]`; outputs `watch_summary.txt`, `watch_frames.csv`, `watch_events.txt`, `watch_stanceclips.txt`, `shot_*.png`. A/B switches: `-pixCullUpdate`, `-pixNoKickFit`.
* Stance audit: `Unity -batchmode -projectPath <copy> -executeMethod PixFootball.EditorTools.CriticalStances.Run -pixCriticalStances -pixCriticalOut <dir>` (batch mode WITHOUT -nographics; `-pixCriticalNoPrep 1 -pixCriticalCam broadcast` keeps the match culling so off-screen bodies behave as in the game).
* Prefab audit: `-executeMethod PixFootball.EditorTools.CriticalAudit.Prefabs -nographics [-pixEnsureRoster]`.
* **Integration:** the roster fix needs `Assets/_Licensed/ArtLibrary.asset` and `Resources/ArtLibrary.asset` to carry the 12 roster keys: opening the shared project and running `CriticalAudit.EnsureRoster` (or any build, it is hooked) does it; the files are git-ignored.

## 6. Test run
See the end of this file.

One full EditMode run (`-testCategory "!Slow"`) on the private copy (git HEAD + this track's files and hooks, including the punt framing and `CriticalTests`): **1251 tests, 1251 passed, 0 failed** (`D:\PIXFootball\Work\Critical\editmode1.xml`). The punt framing was added after the last built-player run: it is covered by the same code path and the compile, not by a recorded punt (no punt occurred in the four-quarter sample).
BUGS.md had no CRITICAL rows when this log was written (the file did not exist yet).

## 7. BUGS.md rows (second pass, re-measured with the AUDIT tool `MatchWatchAudit`, 16 plays, seed 20261021, on this track's copy)
Outputs: `D:\PIXFootball\Renders\critical_v0110\mw_after` (cull + stance fixes), `mw_after2` (+ LOD dwell + goal-line stance), `stances_bodies` (all 15 bodies).

| Row | Result |
|---|---|
| B-001, B-002, B-003 | **Fixed** (`PlayerLod.MatchCulling` = AlwaysAnimate). The audit's CULL_OFFSCREEN_* and VIS_BURIED_* issue types no longer fire (0 hits; v0.10: 800+ and 36-49). |
| B-004 returner legs folded, B-013 folded legs on other bodies | **Fixed by the same change** (stale pose compounded by the leg layers on a culled body). Largest "feet up" reading in 16 plays: 0.47 m on a get-off frame, none at 1.4-1.8 m; FLOAT_RUN_AIRBORNE 1-4 hits of 0.12-0.24 m (was 1.4-1.8 m on every return). |
| B-005 centre handstand | Not reproducible: all 15 bodies x every position in 5 sets (`stances_bodies`, 11550 samples, 0 inverted legs, 31 marginal flags: 3-6 cm sole, a pulling guard 3 m from his root at 2.5 s, four goal-line four-point hands not quite down on the Tall bodies). Same cause as B-004 (a culled centre between plays). |
| B-014 OL hands up | **As specified** (BODIES v0.10 / owner note 9): tackles hips 0.86-0.90, lean 50-57; guards 0.80, 64-69; centre down; verified every scrimmage formation. The "alternation" is scrimmage (per-position stance) vs kicks (old low three-point). **New:** goal-line / short-yardage downs (formation "Goal Line", or ball at the 97+) now put tackles and guards in the true three-point (`StanceRules.ShortYardage`, hook in `PlayStage.Begin`): hips 0.73, lean 92, one hand down (was 0.86 / 50, no hand). |
| B-058 LOD2 all game | **Not changed** (S3): a body is 50-60 px at 1080p on this lens, LOD1 starts at 200 px; v0.9 A/B measured -30 % fps for LOD1 on everybody. Candidate: Lod1Px 200 -> 120 if the owner wants helmets sharper. |
| B-059 LOD flip-flop | **Partly:** 0.3 s dwell per body (`PlayerLod.LevelDwell`): flip-flops 108 -> 77, LOD changes 870 -> 641 in the same 16 plays. The rest do not come through `PlayerLod.Update`; not found. |
| B-060 replay records culled bodies | **Fixed at the source** (the recorded poses are now valid); the replay recorder itself is untouched. |

Audit note: it reports VIS_VIEW_INACTIVE / VIS_TEAM_MISSING in PostPlay after kickoffs, punts and some plays: the live stage is hidden while the replay actors play (857 frames in the probe run, none visible to the viewer); not a body defect.
Second EditMode run (private copy, all changes above): **1251 / 1251 passed** (`editmode2.xml`). New: `BodySelect.ForceBody` (QA), `StanceRules.ShortYardage`, `PlayerLod.LevelDwell`, harness `-pixCriticalBodies 1`.
