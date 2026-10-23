# Changelog

All notable changes to PIX Football. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions: [SemVer](https://semver.org/).

## [Unreleased]

Next: **College Network** returns (paused in 0.9.0): a college season you can follow and watch, scouting, the big board and real draft classes from the 138 schools. Its logic is in main (`League/Career/College*`); its screens live on the local branch `wip/v0.9-college-ui`.

## [0.10.0] - 2026-10-23 - "Choreography"

Answers the v0.9.0 playtest notes (`Docs/QA/v0100/PLAYTEST_v090.md`) with seven tracks (MOTION, QB, CHOREO, DEFENSE, BODIES, ANIMLIB, UI); status of every note in `Docs/QA/v0100/STATUS.md`, track logs next to it. Built player, 80 s live play, 2560 x 1440 High: 148.5 fps average, 114.5 1 %-low (v0.9.1: 145 / 82). 1,248 EditMode tests in the `!Slow` run, all green after two re-baselines.

### Added
- **Motion matching** (`Gameplay/Player/Motion/*`, `PlaySimulation.MotionPlan.cs`, `PlayStage.Motion.cs`, `PixMotionDb`): locomotion picks the pose from clean Mixamo running, jogging, back-pedal and shuffle takes (skeleton-only exports re-based from their first-frame rest pose), with inertialization, per-take floor height and planted-foot hold. Flags `-pixLocoLegacy` (v0.9.0 gait player), `-pixLocoOff`.
- **Choreographer** (`Sim/Choreo/*`, `ContactDirector.Choreo`, `SimAgentView.Choreo`, `GameChoreoLibrary`, `PixChoreo`): forecasts tackles and catches 0.5-1.2 s ahead, bends paths with a bounded rate, books the paired take by bearing / speed / height / side, and makes the sim body follow the take's root path; lead-in time warp, contested-catch pre-roll, `EngageStepper` foot reach and plant, DB hip-flip planner, sack / interception / big-play celebrations. Flags `-pixNoChoreo`, `-pixNoChoreoView`, `-pixNoTurn`, `-pixNoGetOffFit`, `-pixNoRootScale`, `-pixNoDuel`, `-pixNoChoreoCelebrate`, `-pixSteerSacks`, `-pixSteerCatchers`.
- **Quarterback sequence** (`PlayStage.QbSeq`, `SimAgentView.QbSeq`, `StancePoses.QbSeq`, `Sim/QbSeqTiming`): walk, survey / point, step in, crouch, cadence, snap, 0.2 s secure, 3 / 5 / 7 step drop, set with the chest north, scramble transition. The sim fixes each segment's length.
- **Kick sync** (`PlayStage.Kick`, `SimAgentView.Kick`): contact time from the sim, foot-path IK pulse, holder spot used.
- **Defense rules** (`DefenseAI.Rules`, `SimAgent.Defense`, `SimTuning`): zone landmarks, man targets with swap rules, spy, blitz gaps. `CoverageAudit` probe and `CoverageRulesTests`.
- **Plays** (`DefaultPlaybooks.Concepts`, `PlayDataV100Tests`): 22 offensive formations / 619 plays / 49 concepts (was 12 / 287 / 46), 8 defensive formations / 101 plays (was 5 / 66); `Play.Note` coaching line shown on the play-call card.
- **Bodies** (`BodySanity`, `PhotoArmProportions` v5, `BallFollower.Release`, `MatchOfficials.Ball`, `PlayStage.BallFlow`): new OL / DL stances, human arm proportions on all 15 photoreal prefabs, seat / shoulder skin relaxation, joint-limit layer, ball carried in the hand and dropped after a celebration.
- **Animation library** (`ClipSelect`, `ClipTagTable` / `PixClipTags`, `AnimKit.Extra2`, `Editor/Tools/AnimLib/*`): per-clip QA, 383 more cleaned clips, tag table of 2,628 clips, kickoff idles / runs, QB cadences and snaps, center snap, 4-point get-off, celebrations and reactions, 71 bad takes rejected.
- **Tools:** `ChoreoAudit`, `QbChoreoAudit`, `BodyProbe`, `MotionBench`, `MixamoScan` / `MixamoImport`, `MotionDbBuilder`, `ChoreoClipBuilder`, `SkinFixCheck`.
- 25 menu design directions (3 screens each) as mockups outside the repo (`D:\PIXFootball\Renders\ui_directions`, notes in `Docs/QA/v0100/ui.md`); the in-game menus are unchanged.

### Changed
- Running: both feet in the air 12 % at a run and 28 % at a sprint (was 44 % / 58 %), toe-out at contact 3-5 degrees (was 14), planted-foot slide at run / sprint 0.07-0.10 m/s (was 0.24-0.30).
- QB: chest 3 degrees off north in the pocket (was 100), head on the read 2 degrees (was 37), ball in the hands on the snap frame, drop slide 0.04-0.06 m/s.
- Choreography: planted-foot slide in action takes 1.16 -> 0.20 m/s, floating 20 % -> 5 % of action frames, catch ball-to-hand gap 0.57 -> 0.10 m, chest snaps 3,724 -> 704 per 60 plays.
- Kicks: kickoff foot-to-ball gap 8.9 -> 0.6 cm.
- Defense: lost defenders 14.7 -> 0.1 per 100 pass plays, sacks per team per game 1.3 -> 2.5, Read Option 2.97 -> 4.61 yd, Outside Zone 3.38 -> 5.08, punts 49.2 -> 45.9 yd.
- 4,000-play check (choreography on): rush 3.88 yd, pass 7.41 yd, completion 52.8 %, 213 sacks, 47 interceptions, 36 fumbles. Pass was 7.61 with choreography off; the test bands are rush 0.40 / pass 0.85 yd.
- `SimAbilityLineTests.SniperBlitzArrivesSooner` bound -0.03 -> -0.02 s (the 0.2 s QB secure and new drop facing shift every blitzer-arrival time; measured -0.026); `BodiesTests` helper-bone test requires the driver only while `BodyHelperBones.Enabled`.
- `BuildScript` slim ArtLibrary whitelist now keeps the v0.10.0 string-keyed takes (kickoff, QB cadence, center snap, 4-point get-off, celebrations, crowd, draft night).

### Fixed
- Players leaving zones or coverage; QB with his back to the field in the drop; kickoff / punt ball sync; ball glued to the forearm in celebrations; OL / DL stances; twisting waists and forearms; arm proportions; pass-yard tilt from catcher / rusher steering (off by default).

### Known Issues
- Wrap-tackle planted-foot slide about 1.6 m/s mean (0.7 median); Jump and GetUp contacts are played by the view, not the sim, and still slide.
- Jog and shuffle slide means 0.2-0.4 m/s (medians about 0.1); the old tree walks below 1.7 m/s.
- Sprinters are played 20-25 % over the fastest Mixamo take.
- Seat (p99 1.6-1.7x) and sleeve (1.8-2.1x) skin stretch at extreme poses; grip and wrap hands slightly clawed.
- Clip-change rate above target for linemen (about 1.0 per second against 0.5).
- Field goal swing passes about 17 cm from the ball; 5-step drops land 8 steps; hand-off and play-action pivots not choreographed; interception and big-play celebrations not yet seen in a strip.
- UI: 25 directions delivered as mockups, owner to pick; in-game menus unchanged.
- `CollegeAssets.asset` has no script in main (College screens live on `wip/v0.9-college-ui`); harmless warning at build.

## [0.9.1] - 2026-10-22 - "Frame Rate"

Performance fix for the v0.9.0 frame-rate regression, same look. Built player, 80 s of live play (`-pixAutotestMatchOnly -pixAutotestNoV3 -pixAutotestMeasure`), night, 2560 x 1440, High, Auto upscaler, RTX 5090 Laptop: v0.9.0 81.7 fps average / 50.4 1 %-low (GPU 12.8 ms, main thread 5.3 ms) -> v0.9.1 145.3 / 82.3 (GPU 6.3 ms, main thread 5.0 ms) on an otherwise idle machine. Runs taken while other Unity / Blender jobs ran on the same laptop gave 88-119 average and 22-30 1 %-low (CPU contention). Profiling notes: `Docs/QA/v091/perf.md`.

### Fixed
- **GPU skinning was off** (`ProjectSettings` `gpuSkinning 0`, `meshDeformation 0` since project setup): about 300 skinned player / official renderers were skinned on the CPU and uploaded every frame; the editor GPU profile showed 5.7 ms in the object motion-vector pass alone. Now GPU (batched) skinning: GPU time in live play 12.8 -> 6.3 ms.
- **DLSS / FSR 2 never ran** (`QualityTiers.ApplyRenderScale` set `allowDynamicResolution = false` at 100 % render scale after `UpscalerControl.Apply` had set it, the scene-load order decided who won; the status line still said "DLSS active"). The camera flags are now set in one place (`UpscalerControl.Apply`, which honours the render-scale option); Auto now really renders at 1792 x 1008 under DLSS Quality at 1440 p. In the editor the upscaler flags stay off unless `-pixUpscaler` is given.
- `PlayerLod.LevelFor` / `LevelForPixels` allocated an array per player per frame.
- `GfxTests` LOD cases updated to the 400 / 200 px edges v0.9.0 shipped with (2 tests were stale).

### Added
- Autotest perf diagnostics: the render size actually used (live measure and `-pixAutotestPerf`), `-pixGpuBreakdown` (GPU time per HDRP pass and a motion-vector census; editor / development players), a spike log of slow live frames under `-pixAutotestMeasure`, sweep trial "others hidden" (skinned / moving renderers outside the players).

### Known Issues
- Short main-thread hitches remain around kickoffs and post-play (pile-ups, close-ups); they show up as a lower 1 %-low when the CPU is shared with other heavy programs.

## [0.9.0] - 2026-10-22 - "Match Engine"

The match-engine release, answering the v0.8.0 playtest notes (`Docs/QA/v090/PLAYTEST_v080.md`). A 448-row audit (`Docs/QA/v090/ENGINE_AUDIT.md`, `ENGINE_AUDIT_issues.csv`, status of every row in `ENGINE_AUDIT_STATUS.md`) was worked by five tracks: LOCO (locomotion), QBSTANCE (stances, quarterback, kickers), CONNECT (animation connections), SIM (engine realism and pace), GFX (graphics in play). Track logs: `locomotion.md`, `qbstance.md`, `connect.md`, `sim.md`, `gfx.md`. Final audit status: 361 fixed, 54 partial, 14 handed over, 14 wontfix, 5 open. College Network UI is paused (its logic commit stays in main).

### Added
- **Gait system** (`Gameplay/Player/Loco/*`, `SimAgentView.Loco.cs`): `PixLocoCycles` library of 20 measured speed-banded mocap cycles, `GaitPlayer` playables mixer on one shared gait phase, `FootPlant` (stride warp, per-foot world-space plant, sole pressed to the turf, pelvis compensation, two-bone IK, knee / foot / ankle polish: leg twist limits, toe-out, roll), learned per-cycle speed factors, action ground pass so kicks and dives keep a foot on the turf. `-pixLocoOff` plays the v0.8 locomotion.
- **Stance solver** (`StancePose`, `StanceIK`, `StanceRules.Qb`, `PlayStage.Stance`): authored pose per stance kind solved by IK over the clip (down hand planted on the turf, heel raise, toe-out, head look, shoulder turn), stance tells, 5 new kinds (kicker, punter, long snapper, kickoff runner, returner), press / off / edge / goal-line variants from the play context, cadence window (`MatchDirector.Cadence`).
- **Quarterback** (`SimAgentView.Qb`, `AnimKit.Qb`, `ProcGait`, `OffenseAI.QbMotion`): footfall planner (a foot lifts only while the other is planted), under-center snap received at the centre's rear, 3 / 5 / 7 step drops with plant, shotgun gather, pocket, throws per style, pump fake, throw-away, scramble / roll-out / kneel / spike / toss poses; mirrored clip twins for left-handed passers, right-footed kickers and the holder side (`QbMirrorBuilder`).
- **Connection layer** (`AnimKit.Connect`, `SimAgentView.Connect`, `PlayStage.Connect`, `PlaySimulation.Connect`): render interpolation between 60 Hz sim steps, pelvis / chest pop filter, ActionAnimator mask and fade rules, walked shifts / flips / huddle break, snap gating, matched block pairs and run-block footwork, momentum-aware contact takes, gang tackles, ragdoll hand-over, reach IK catches, hand-off exchange, post-whistle help-up and pile-on, QB-hit reaction.
- **Sim realism** (`Sim/*`): time-parametrised QB drop and release floors per drop type, fitted NFL ball-flight curve with drag, throw styles that trade speed for arc, loft over underneath defenders, per-position reaction time, position-specific pace (`GamePace`), body acceleration / cutting / braking limits, get-up after a fall (`AgentAction.GetUp`), fatigue on acceleration, special-teams penalties (roughing / running into the kicker, kick-catch interference), onside kick rate 3 % -> 11 %, kick meter period 1.45 s, `PlaySimOptions.Cadence`, `PlayEvent.Vel`.
- **Graphics** (`FootShadow`, `BallFollower.Gfx`, `BallSpinHint`, `PlayerLod`, `QualityTiers`, `UpscalerControl`): size-based player LOD with a shadow proxy, PCSS 4-cascade shadows, foot blobs, DLSS Quality / FSR Auto, anisotropic 16x, turf blade detail, cloud layer, grade pass, ball spin per play kind, officials staying with the spot, larger numerals.
- **Tools:** `EngineAudit` (real plays through PlayStage, bone-level metrics and strips), `ConnectAudit`, `LocoCycleBuilder` / `LocoCycleScan` / `LocoShots`, `LegProbe`, `QbStanceProbe` / `QbStanceTools`; autotest flags `-pixAutotestMeasure` (80 s of live play for frame rates), `-pixAutotestQuick`, `-pixAblate`, `-pixLodPx`.
- 1,164 EditMode tests in the `!Slow` release run, all green (new: `LocomotionGaitTests`, `QbStanceTests`, `QbStanceSimTests`, `ConnectTests`, `GfxTests`).

### Changed
- **Game pace:** run plays last 4.2 s on average (was 3.3), passes 4.8 s (was 4.0); `GamePace` skill 0.85, TE / QB 0.89, DB 0.87, LB 0.86, edge 0.91, DT 0.835, OL 0.805 (linemen were not slowed at all before).
- Quarterback release 1.9 s (shotgun 3-step) to 3.7 s (7-step); throw speed capped at 57 mph; pass flight 20-50 yd no longer 20-30 % too fast.
- Kicking: every kicker right-footed and aligned left of the ball (kickoff 3.3 m, field goal 1.65 m), the punt uses the right foot (it was left).
- Player LOD edges: LOD0 from 400 px, LOD1 from 200 px of on-screen height (release fallback after the player measurement, see Performance).
- Calibration bands retuned to the new engine (sacks per team 0.9-3.8, pass flight times, throw range, shuffle speed, `Penalty.All` 19).

### Fixed
- Floating sprints (feet in the air 72 % of a sprint cycle -> 42-53 %), sliding planted feet (contact slide 1.0-2.9 m/s -> 0.17-0.55), cadence about 2 steps / s at a sprint -> 4.3-4.9, feet turned out 25-40 degrees and knees splaying, cleats rolled onto their edges, ankle jitter.
- Linemen stood with their hands on their knees; QB hovered during the drop (51-63 % flight frames -> 0); the quarterback sat stuck behind his own back and inflated under-centre sacks to 13 %.
- Pre-snap flip / shift teleports (32 m -> 0.09 m largest frame), the carrier stopping dead at the tackle, the ball popping from hand to hand on a hand-off (1.06 m -> 0.27 m), contested catch restarting the catch animation, broken tackles that never put the carrier down.
- A defender running into the kicker or a receiver colliding into a catch were not penalised.
- All the other rows in `Docs/QA/v090/ENGINE_AUDIT_STATUS.md`.

### Performance
Built player, 2560x1440 High (windowed, RTX 5090 laptop, 80 s of live play in an exhibition match, `-pixAutotest -pixAutotestMeasure`): 75.3 FPS average, 52.4 1 %-low, 34.8 minimum (CPU 12.5 ms, GPU 12.5 ms per frame); free roam 113 average. The first player measurement with the GFX edges (LOD0 from 300 px, LOD1 from 170 px) gave 73.3 / 51.5; the release fallback (LOD0 from 400 px, LOD1 from 200 px) gave 75.3 / 52.4. This is below v0.8.0 on the same machine (109 average, 69 1 %-low, 2364x1330) and **below the 60 FPS 1 %-low target**; the extra cost is the look pass (shadows, LOD0 near the camera, SSAO / contact shadows, depth of field) plus the connection layer. Levers left: depth of field off, lower shadow cascades, TAA quality medium (see `Docs/QA/v090/gfx.md`).

### Known Issues
- Sprint cycles still have both feet off the ground 42-53 % of the cycle (mocap property); hard cuts at a sprint toe out by 17 degrees on average (p95 45).
- Back-pedal cadence 1.4-2.0 steps / s, back-pedal pops when a defender turns, shuffles and crossovers on the old tree, no start / stop / cut clips, pole flips of the knee below 3 m/s.
- Catch frame: ball still 0.58 m from the hand at the simulated catch frame (pulled in over 0.2 s); clip changes per second went up for OL / DL / WR / CB; residual hip snaps at the start of get-off clips.
- OL / DL hips 0.68-0.72 m with a hand on the turf (CSV target 0.80-0.90); left-handed carriage not tuned; no off-balance / jump throws.
- Crowd not animated, LOD not cross-faded, clouds do not scroll, numeral outline not thicker, lit field markers cast no shadows.
- Sim: sack rate 3.3 % of dropbacks (NFL about 6.5 %), Read Option 2.4-3.2 yd per carry, zero-blitz breakaways about 1 in 5 beaten runs, punt average 49.1, pass snap-to-dead about 5.8 s with the director tail.
- Huddle break, call-screen tick and post-whistle walk not tested with a controller; the legacy "V3" part of `-pixAutotest` still looks for the pre-0.7 call screen and reports FAIL lines (pre-existing, the match, broadcast and career autotests are clean).
- 14 audit rows handed over and 5 left open (see `ENGINE_AUDIT_STATUS.md`).

## [0.8.0] - 2026-10-22 - "Franchise"

Career Mode: an endless franchise on the 32-team pro league, played game by game in the real match scene, with the match now reading player abilities, morale and the coaches' tendencies. Three tracks: the franchise logic (`League/Career`, `Docs/Career.md`), the simulation integration (`Docs/QA/v080/sim_abilities.md`) and the franchise UI (`Docs/QA/v080/career_ui.md`).

### Added
- **Career Mode** (CAREER tile of the menu hub): new career (32 crests, difficulty, quarter length, standard or random league, five save slots), continue / load / delete. The franchise shell has HOME / TEAM / LEAGUE / OFFICE tabs with sub tabs:
  - HOME: matchup card, PLAY GAME / SIM GAME / SIM OPTIONS (this week, to week N, to the playoffs, to the offseason, a whole season), division standings, owner goals, cap and roster status, league wire ticker, and the offseason steps with blockers and "resolve it for me".
  - TEAM: roster (sortable, filters, photoreal player card with ratings, abilities, contract, morale), depth chart editor with eligibility rules and auto sort, injuries, practice squad, injured reserve and waivers.
  - LEAGUE: schedule with box scores, standings (division, conference, league, power rankings, playoff picture), leaders, awards and All-Pro, record book, trophy case, Hall of Fame, news feed.
  - OFFICE: cap and contracts (restructure, release, franchise tag, auto-fix), re-sign negotiation, free agency (14 bidding days, the four decision scores of each player), trades (value meter, CPU counters, inbox, deadline), coaching staff, owner trust and legacy.
  - The draft (scouting points, big board, live pick list with grades, pick clock, trade-up offers, rookie scale contracts) and training camp results.
- **Played games count:** PLAY launches the match scene with the career league (rosters, depth charts, abilities); the result, the box score and the player lines post to the franchise (`CareerSession`, `MatchDirector.Career`, `BroadcastDirector.Result`), the menu reopens the hub and shows the box score. Autosave after every week, phase and draft pick.
- **Franchise engine** (`League/Career`, engine-free, 99 tests): season calendar with 12 phases, roster 53 / practice squad 16 / IR rules, cap, dead money and contracts, free agency, trades and picks, staff and morale, 41 abilities (assignment, progression, scouting hints), draft and rookies, history, Hall of Fame, owner goals, news. Ten-season soaks across six seeds with zero validator problems.
- **Abilities in the match** (`AbilityFx`, `PlaySimulation.Abilities`): every ability hook is read by the simulation (throw accuracy and power, juke / spin / break tackle, catching in traffic, route separation, blocking, pass rush, run stop, coverage, tackling, fumbles, kicking, return speed, fatigue, leadership). Measured effects per ability in `Docs/QA/v080/sim_abilities.md`.
- **Morale and staff tendencies in the match:** every rating moves by the player's morale modifier (-2..+2); the coordinators' blitz, man coverage, deep shot and no-huddle rates shape the CPU play calling, and a no-huddle offense burns less clock between snaps (`MatchFlow` now calls `ApplyTempo`).
- Player cards show the in-match effect sizes (`AbilityCatalog.Gain` is the one calibration table shared by the card and the simulation).
- **Box scores of the user's games are saved** with the career (`CareerSave.UserBoxes`, newest 40).
- `-pixAutotestCareer` autotest of the built player (`UI/Franchise/CareerAutoTest.cs`): new career, the real match scene, the result and box score in the hub, save and load, sim to the draft and the auto draft, screenshots `career_*.png`. Saves go to a private folder under the output directory.
- 1,041 EditMode tests, all green (career, ability, calibration and franchise UI tests added).

### Changed
- `PlaySimOptions.PlayerModifiers` / `PhysicalGameSim.StaffTendencies` switches (default on); the 40-game engine calibration test opts out of the career layer.
- `SimTestKit` players carry neutral morale so every other Sim test measures the engine.

### Fixed
- A strip-sack was credited to nobody in the box score (the sacker was lost when the ball came loose); the sack now belongs to the sacker (`PlaySimulation.Result`).
- Franchise screens fixes found while building (CU-001..CU-007): unreadable tables over the bright stadium, overlapping columns and tiles, clipped card lines, stale pages after an action, wrong team on tracker lines after a trade.
- A playoff game that ends level is posted with the home team one point ahead (sudden death makes ties rare).

### Known Issues
- Draft classes are synthetic: the college world is not part of the career yet (v0.9.0 "College Network").
- No in-match injuries: injuries are rolled by the season after the game (Iron Man scales that roll).
- The career settings (auto depth chart, auto IR, trade deadline week, free agency days, injuries on / off) have defaults and no screen yet.
- A regular-season game that ends level is a tie (record 0-0-1); short autotest quarters often end 0-0.
- Ability descriptions are cut off at the card width on long texts.
- Player portraits render live in the card (one 392 x 504 render texture); edit-mode renders show the T pose.
- The play-call strip is not shown when the autopilot plays (the career autotest shows the live game instead).
- QA: the multi-season soaks and fuzz tests are marked `[Category("Slow")]` (career and v0.9 college tests) so the release run can use `-testCategory "!Slow"`; the 0.8.0 run in this release executed the career soaks too (all 1,041 green).

## [0.7.0] - 2026-10-22 - "Motion"

The playtest-fix release for the 31 notes of the v0.6.0 playtest (`Docs/QA/v070/PLAYTEST_v060.md`, per-note status in `Docs/QA/v070/SUMMARY.md`). Four parallel tracks: animation and the quarterback (A), engine pace and kicks (B), controls, call sheet, menus and replay (C), graphics (D). Track logs: `anim_qb.md`, `engine_pace.md`, `ui_controls.md`, `ui_menus_part.md`, `graphics_v070.md` in `Docs/QA/v070/`.

### Added
- **Real quarterback** (`SimAgentView.Flow`, `AnimQbBuilder`): under-center stance and snap received into the belly, 3 / 5 / 7 step drops scrubbed to the simulated drop point, shotgun ready loop, pocket stance with the ball at the chest, seeded pump fake, wind-up and throw clips cut around the detected release frame (`pixqb_throw_set / _run / _away`; the ball leaves the hand on the frame the simulation launches it), scramble and roll-out carry with the ball by the ear, hand-off and play-action takes timed to the simulated exchange.
- **Hold LT: route view** (`RouteOverlay`): every receiver's route drawn on the turf with arrowheads, read numbers 1-5, the primary read hot, hot routes orange, the run path, and the pass button over each receiver. Follows audibles, flips, hot routes and motion; works as the quarterback during the play.
- **Throw style swap:** hold a receiver button = bullet pass, tap = touch / lob (it was the other way round). **Timeout** on offense moved from LT to D-pad down.
- **Play-call strip** (`PlayCallStrip`, `PlayCallModel` sheet mode): glass overlay along the bottom of the live field with eight tabs (Suggested, Formation, Concept, Play Type, Personnel, Recent, Favorites, Special Teams), a group list, three play cards with an enlarged mini-field diagram, and the coach's three picks always visible; a suggested play is one press of A. The same sheet on defense. Search with `/`, quick find by letter on the right stick, favourites and recent calls.
- **Menu hub** (`MenuHubModel`, `HubNav`, `GlassTicker`): tile hub with HOME / PLAY / PRACTICE / OPTIONS tabs, a hero PLAY NOW tile, a league news ticker built from the league data, full d-pad navigation (locked tiles skipped), staggered reveal. **Matchup card** before kickoff (crests, records, OVR / OFF / DEF, venue, conditions). The menu player wears the home kit and the face of the starting quarterback.
- **Why the play ended:** a banner for every whistle ("TACKLED BY #52", sack, out of bounds, incomplete, broken up, forward progress ...) from `PlayEndReason`; 6,000 plays checked, no empty reason.
- **Controlled-player marker on defense:** a 2.5 m ring with a dark outline plus a HUD pill (number and position) over the player you control, visible through bodies, follows B switches.
- **Roster v2:** 15 photoreal bodies (five body bases times three sizes: Std, Stocky, Athletic, Tall, Runner) chosen by position, height and weight (`BodySelect`); replays use the same bodies.
- **Presentation sets (prefabs):** three jumbotrons (one hangs over midfield in the stadium scenes), tunnel and news-studio prefabs, stadium kit modules (`PresentationSetBuilder`, `StadiumKitInstaller`); not yet wired into the shows.
- **Kick meter drives the kick:** the timing cursor, the aim and your press of A decide power and accuracy of your field goals, extra points, punts and kickoffs (`KickMeter`, `SpecialTeamsAI.Meter`).
- `GamePace` (one pace constant, 0.88), `PlayEndReason`, commit windows, `AnimKit.V7`, `AnimFeel`, `AnimSplayFix`, `GameViewResolution`, `ControlsProbe` and the other tooling listed in the track logs.

### Changed
- **Pace (note 8):** skill players about 12 % slower (WR top speed 21.2 -> 18.7 mph, 40 yd 4.68 -> 5.27 s), OL x0.93, LB x0.89, DB x0.90, DL unchanged, with slower cuts, turns and a 6 % longer wind-up; the sim was re-fitted with 600-game runs (25.6 points a team, 61.9 plays, 4.41 yd per carry, 7.17 yd per attempt, 2.0 sacks).
- **Field goals and kickoffs (note 28):** snap to kick 0.70 s -> 1.32 s with a 2-3 step approach; whistle to kickoff 1.18 s -> 2.0 s with a real run-up timed to the kick clip, coverage line level with the ball at contact, hang 3.65-4.3 s.
- **Locomotion weight (notes 5, 26):** the planted foot stands still (stride cadence trims, a ground fit of all stances so linemen no longer hover and receivers no longer sink, an idle that no longer drags the run cycles).
- **Commit windows (notes 7, 9, 24, 25):** an action plays its clip out (catch >= 0.4 s, wrap 0.24 s, blocks >= 0.45 s) and the simulation waits for it; no instant state flips.
- **Menus and display:** the glass UI is the default theme; the canvas scales with `Expand`, so nothing laid out for 1080 high is cut off at any aspect ratio; the first launch is borderless full screen at the monitor's native size and stored sizes are fitted to the monitor.
- Turf blade-level detail, dirt stains confined to knees / seat / chest instead of broad blotches, shoulder pads rebuilt (no pad shadows, 4 k triangles), player LOD distances 9 / 22 / 55 m.
- Snow day exposure +1.2 -> +0.3 EV (see Fixed, weather).

### Fixed
- **Note 1 (defense ring), note 2 (resolution):** see Added and Changed. **Note 21 (two logos):** the weather clones and the stadium markings material both carried the painted shield; every renderer using a markings material now moves to the cleaned material.
- **Note 3 (play ended for no reason):** phantom dive tackles (the tackle was applied when the lunge started, up to 1.8 yd away; now when the hands reach the carrier) and tackles credited from 3-6 yd away (the drag tackler's hold was wiped every tick); every tackle type now has a maximum gap of 0.59 yd over 8,000 plays.
- **Note 13 (right foot splays):** 24 locomotion cycles re-twisted (`AnimSplayFix`, originals in `Animations/Clean/_backup_splay`) plus a runtime toe clamp: the carry run right foot 46 -> 20 deg out, the plain run -18..-26 -> within -7..+12 deg.
- **Notes 12, 17, 18 (stances):** derived WR stance (staggered, knees bent), grounded OL / DL / TE / QB / holder stances, press and off corner stances, linebacker blitz idle, kneeling field-goal holder.
- **Note 22 (tackle bugs):** a dragged carrier stayed frozen in the wrap pose after the whistle; carriers and tacklers now play on (fall, lie, get up).
- **Note 27 (replay shows no players):** the recorded line-stance setback was written to the actor's local position, so all 22 actors sat at the origin; it is applied in world space now.
- **Notes 29, 30 (ball in hand, desync):** the ball blends between hand and simulation over 0.12 s at the release and at the catch; in a touchdown celebration it stays in one hand; the throw clip's release frame matches the simulated release; the catch starts 0.34 s before the ball arrives.
- Button presses lost on displays faster than 60 Hz (a press on a frame shorter than one 1/60 s step was dropped).
- Goal-line dive: a quarterback sneak from the 1 scored 3 % after the pace change, about 50 % now.
- Weather: a runtime preset switch never changed exposure, sky or fog (the volume used a stale profile copy); the pause-menu day / night toggle was affected too.
- Jersey sleeve openings, armpits and shoulder seams tore open on the padded bodies (welded vertex groups fix the cloth drape).
- Replay actors use the full body of the live player (`size + 3 * base`).
- A defensive lineman's lunge could take him past his own top speed.

### Removed
- Nothing user-facing. The old three-stage full-screen call page stays in the Classic theme only.

### Known Issues
- Back-pedal below 3 m/s and above 4 m/s still slides 1-4 m/s; the shuffle cornerbacks' right foot is 35-41 deg out (`BGS_CB_Shuffle*`).
- No start / stop / plant-and-cut transition clips, no left-handed quarterback, no receiver head turn to the ball, no OL stance tells.
- Defensive line technique alignment (0-9 techniques) is implemented but off (`DefenseAlignment.UseTechniques`): it needs a blocking-scheme recalibration.
- The jumbotron hangs over midfield, but the tunnel, news studio, roof, light towers and ribbon boards are not placed or wired into the shows yet.
- Day games under the roof read dark in the stands; skin / eye shading and player contact shadows are unchanged.
- The sleeve stripe artwork reads as a "wing" on some kits and the shoulder numeral is cut by the cap rim.
- At 4:3 and 21:9 the call strip keeps its design size; the menu ticker is clipped hard at its right end.
- Performance (built player, 2560x1440 High, RTX 5090 laptop, `-pixAutotest`): live play 107 FPS average, 70.3 1%-low (target 70, met by a hair), 37 minimum; broadcast layer on 100 / 72 / 59; replays and shows 108 / 72 / 44. Heavy frames are still close-ups of LOD0 photoreal players under night / weather lighting.
- The autotest's old V3 call-screen checks (three-stage Playbook flow) report FAIL because the default call sheet is now the strip; the new flow is covered by `V070ControlsTests` and the real-match controls probe (`-pixControlsProbe`).

## [0.6.0] - 2026-10-21 - "Photoreal"

The playtest-fix release. The ten notes from the v0.5.0 playtest (`Docs/QA/README.md`) were turned into a QA pass with five parallel tracks (stances and cameras, animation, models, engine, graphics) plus UI / flow and the photoreal player; every row is logged and cross-referenced in `Docs/QA/CHECKLIST.md` (3,540 distinct issues logged, 2,908 fixed). Style target: All-Pro Football 2K8 (`Docs/QA/REFERENCE_AllPro2K8.md`). Verified in the shipped Windows build: screenshots in `D:\PIXFootball\Renders\v060_release\`, per-note evidence in the checklist.

### Added
- **Photoreal players are the default body** (`PIX_PhotoPlayer_Small / Medium / Large`, `PhotoPlayer`, `PlayerAppearance`): a hero-quality body with 4 levels of detail, per-player face / skin tone / brows / gloves / sleeves / wristbands / visor chosen deterministically from the player id (the same man always looks the same, also in replays), body size by position and weight (a 255 lb tight end uses the large body, a 215 lb receiver the medium one). The stylised Draft Fever bodies stay as a fallback (Settings > Gameplay > Player models, or `-pixLegacyPlayers`).
- **Stances by rule, not by clip pool** (`StanceRules`, `StanceClips`): the pre-snap body position of a player is chosen from his position, alignment and the formation (3-point / 4-point / 2-point, under center, shotgun, press and off corners, kicker, holder, snapper, returners, gunners); mirrored stance clips for left / right hands.
- **Clip facing table** (`ClipFacingTable`, `ClipFacingAudit`, `BodyHeading`): the measured direction of every mocap clip plus a closed hips-heading loop, so linemen and corners no longer face backwards. Formation Gallery (`StanceGallery`): 9,914 numeric checks over every formation in both directions, 99.9 % pass with the photoreal bodies.
- **2K8-style user camera** (`UserCameraView`, `UserCameraMath`, `MatchCameraRig`): behind the quarterback (Classic / Madden / Wide presets), pulls back and up at the snap, follows the carrier and the ball, defense and kick framing, both directions. The broadcast camera is for simulated games and an option.
- **Bottom-bar scorebug** (`MatchHud.BottomBar`) with a slim player stat table, gold receiver stars and the user ring before the snap.
- **Coin toss on the field** (`MatchDirector.Toss`, `MatchHud.Toss`): referee at midfield, two captains per team, close camera, caption strip.
- **Kick meter and wind** (`KickHud`, `KickMeter`, `MatchConditions`): timing and aim gauge, wind compass with mph, wind per quarter shared by the simulation and the HUD.
- **PIX/Lit shader and the v6 look**: team-colour uniforms with numbers (front, back, sleeve caps), name on the back and team wordmark on the chest drawn from atlases, per-player dirt (`DirtModel`), clear-coat helmets with stripe and crest, procedural turf with mowing stripes and wear (`FieldWear`), 15 weather and time-of-day presets (`WeatherPresets`, `WeatherSystem`), floodlight rig, per-preset colour grade and a drama grade for replays and shows, home crest at midfield, wet ball.
- **Engine realism**: game-long fatigue (`FatigueTracker`), coverage-specific pre-snap alignment (`DefenseAlignment`), speed in pads, turn rate, pocket (kick-slide), routes, throw speed, kickoff wall, onside kicks, punt returns, penalties (16 fouls), wind on kicks, college overtime.
- **Mocap cleanup and audit tooling** (editor): `ClipCleaner` / `CleanBatch` (cleaned clips in `Animations/Clean`), `AnimAudit`, `AnimRig`, `AnimTools`, `PoseSeq`, `StanceGallery`, `StickSheet`, `BatchWatchdog`, `ReleasePreflight` (one-session preflight: ArtLibrary, tables, controller, clip facing, mesh LODs, v6 art, scenes), `Tools/MakeChecklist.ps1`.

### Changed
- Default match camera is the Classic 2K8 camera behind the quarterback; the scorebug is the bottom bar. The pause menu and Settings have the camera and scorebug options.
- Graphics tiers scale the new features (rain and snow amount, splashes, breath, floodlight shadows, SSGI on Ultra). The High tier no longer casts shadows from the floodlight rim lamps (about 3 ms at night); Ultra keeps them.
- Shadow casters on a photoreal player are limited to jersey, pants, socks, arms, and the helmet shell.
- Lettering on jerseys (name and wordmark) uses the number colour with an outline ring in the trim colour, and the number / lettering colours are chosen by contrast ratio, so navy and black kits are legible.
- Replays dress their actors with the same numbers, dirt, kit, wordmark, crest and (photoreal) face as the live view.
- Pooled player views prefer the body that last wore the same player (no re-dressing between plays).
- The autotest's penalty tour no longer waits for a decision on dead-ball flags (nothing to decide).

### Fixed
- **Note 1 / 2 (mocap, missing animations):** heavy clip cleanup (despike, foot lock, root and facing re-basing), missing keys added (walk, kick, field goal, gang tackle, celebrations, mirrored stances); every cleaned clip faces +Z.
- **Note 3 (camera):** see Added.
- **Notes 5 / 6 (stances, facing):** root cause was one fixed model yaw for clips authored facing other ways (only 60 of 1,232 pre-snap bodies faced the right way); fixed by the facing table and the heading loop (100 % in the Formation Gallery); distinct stances per position.
- **Note 7 (engine):** top speed 28.7 mph to 23.6 mph (hard cap), 40-yard dash 4.30 s to about 4.50 s in pads, pass speed 57 / 62 mph to 39 / 46 mph, 180-degree turn 0.25-0.50 s to 0.33-0.63 s, kick-slide pocket (line depth +0.26 yd to -1.23 yd), off-man corners no longer charge the line, onside recoveries 84 % to 34 %, mean sack time about 2 s. Run game recalibrated (every run concept averages 3.0 yd or more, overall 3.9 yd per carry).
- **Note 8 (patch on the helmet back):** the white logo plates and the mask hole of the licensed helmet map are no longer drawn; clean shell, stripe and crest.
- **Note 9 (thin waists / legs):** LOD meshes were looked up by `meshName#vertexCount`, which collided across the three body sizes (Large LOD1-3 had a waist 18 % thinner than LOD0); rewritten `PlayerMeshLodTable`; the photoreal bodies are built with padded proportions at every level of detail.
- **Note 10 (atmosphere):** weather, floodlights, colour grade and drama grade (see Added).
- Offseason: a standalone offseason could leave rosters at 48-52 players after a cap-forced release; the last roster fill is never followed by a cut now.
- Calibration: `TextAtlas.Clean` turned digits into word gaps; run blocking in the leverage contest was too weak after the realism pass (concept averages 2.6-3.1 yd), so Jet Sweep and Read Option were below the football band.
- PHR-022: `PlayerAvatar` no longer overwrites the photoreal materials by name prefix.
- Shader: the `PIX/Lit` alpha-tested + emissive variants exceeded the 16-sampler limit (two variants failed to compile in the build); the jersey / helmet textures share one inline sampler.
- Jersey number placeholder ("10" on every shirt), helmet crest plates, magenta tertiary colours (stale subsurface flag), black frame with weather normals (NaN through bloom), undefined `smoothstep` arguments, plus the rest of the `Docs/QA/CHECKLIST.md`.

### Removed
- `Assets/Resources/BillingMode.json`: a leftover `{"androidStore":"GooglePlay"}` file from the removed purchasing package.
- Beard and stubble shells of the photoreal heads are off (they read as speckle at close range, PHR-018): clean-shaven until proper hair cards exist.

### Known Issues
- The kick meter and the wind are display-only for the user's kicks: the simulation reads them (`KickMeter`), the live match does not feed the user's button timing into it yet; the CPU kicks for the user.
- Live play at 2560x1440 High on an RTX 5090 laptop: 108 FPS average, 61 FPS 1%-low (target 70), 45 FPS minimum; broadcast camera and cinematics 100 average, 74 1%-low; replays and shows 120 average. The 1%-low target is missed: heavy frames are close-ups of LOD0 photoreal players under the night / weather lighting. Settings > Gameplay > Player models (Classic) and the graphics tier help on weaker machines.
- The photoreal heads are game-grade sculpts, not photographic scans (PHR-019); scalp hair is a shell without hair cards (PHR-018).
- The home crest at midfield is a low-resolution sprite and looks blocky up close; dirt on white away jerseys can look like camouflage late in a game.
- Distinct OL / DL stance takes (3-point vs 4-point, wide-9, press corner) are partly reused clips (open ANI rows); the formation gallery reports 5 of 9,914 checks failing (two 3-point head heights a few centimetres over the limit, three body-versus-root angles during a block and a handoff take).
- 374 audit and track rows are still open (`Docs/QA/CHECKLIST.md`), mostly polish and the long tail of data / UI items.
- The glass UI redesign is in the tree but off: the default theme is Classic.
- Ray-traced shadows / reflections are not enabled; grass blade cards near the camera are not implemented.
- The release flow still bakes the git-ignored licensed art into the build zip (see `Docs/RELEASING.md`, owner confirmed redistribution on 2026-10-21).
- The build still ships two large scenes (`Stadium_Pro`, `Match_Pro`) that each carry a full copy of the stadium.

## [0.5.0] - 2026-10-21 - "Broadcast"

The 2K5-style presentation pass: a TV broadcast around the game. Every play is recorded for instant replay (free camera, slow motion, depth of field), a camera director cuts like a TV truck, TV overlays and statistics appear when they matter, halftime and postgame shows run the highlights, the crowd and the sidelines react, and the first-person camera got a helmet, head bob and a look-to-ball assist.

### Fixed
- **"Packages with Errors" dialog when opening the project in the Unity editor.** `Packages/manifest.json` carried five packages from the editor's default template that the game never uses (`com.unity.analytics`, `com.unity.purchasing`, `com.unity.collab-proxy`, `com.unity.multiplayer.center`, `com.unity.xr.legacyinputhelpers`, with the Unity Services packages they pull in). The Package Manager listed the project as having packages with errors and showed a modal dialog that blocked play mode (batchmode never showed it). They are removed; the project opens cleanly (verified by launching the editor GUI on the project and listing its windows: before the change a "Packages with Errors" window was open, after it only the HDRP wizard and the editor window).

### Added
- **`PixFootball.Presentation` assembly** (`Scripts/Presentation`): the broadcast layer, driven through `IBroadcast` hooks in `MatchDirector` (the game plays like v0.4 without it).
- **Instant replay.** Every play is recorded at 30 Hz (root pose of the 22 players, every animated bone packed to 4 bytes, the ball): re-simulating from the seed would not reproduce what was watched (contact warps, ragdolls, view state), so the recorder stores what was shown (about 2-3 MB a play, 0.2 ms a sample). Replay UI in the UI-kit style: play / pause, scrub, rewind and fast forward on the D-pad, 1x / 0.5x / 0.25x slow motion, cinematic letterbox, a transport bar with the key moment marked, **free camera** (orbit, pan, zoom, ground level, switch the target player), preset angles (sideline, end zone, high, behind the QB, follow the ball) and **depth of field** focused on the target (HDRP physical camera). Pause menu: **Instant Replay**. Browse the last plays with View.
- **Automatic replays** after big plays (touchdown, turnover, sack, long gain of 20+ yards, big hit): two broadcast angles with slow motion through the key moment and the key player in focus, skippable with A. Setting: Automatic replays.
- **Broadcast camera director**: sky-cam swoop for the game's first play, establishing shots for drives (sky cam, low sideline angle on the quarterback, end-zone view), tight shots on the carrier and the pile after the whistle, touchdown celebration close-ups with teammates running in, the coach after a turnover, the crowd. Cuts never hide the start of a play (they end when the snap happens and need at least 0.6 s of live view). Setting: **Cinematics: Full / Minimal / Off**.
- **TV overlays** (animated in and out, four screen slots so cards never overlap): drive summary (plays, yards, time of possession, result), player stat lower-third (`#12 J. DOE - 18/24, 231 YDS, 2 TD`), play-by-play ticker, team stat comparison, score update card, two-minute warning, "next play" down-and-distance card, penalty lower-third, stadium and kickoff-temperature card, and the **game intro** (sky camera over the stadium, matchup card with both crests, records and key players). Setting: TV overlays.
- **Halftime show and postgame show**: first-half team comparison, top performers (passing, rushing, receiving, defense), a highlights reel of the best plays by excitement score with slow motion and depth of field; postgame adds the final score card, player of the game, box score and returns to the menu. Skippable (B), A advances.
- **Live box score** (`GameTracker`): player lines, team box scores, drives with time of possession, scoring events - engine-free and tested.
- **Crowd and sideline reactions**: on big home plays the crowd jumps up in a wave around the bowl and camera flashes pop in the stands; coaches clap for touchdowns, takeaways and big sacks; after a touchdown teammates run to the scorer and cheer while the beaten defenders hang their heads.
- **First-person polish**: optional helmet facemask overlay, head bob tied to the stride, look-to-ball assist (the view leans toward the ball in the air and the carrier, on offense and defense), a small scorebug and a quiet hint bar.
- **Play-by-play commentary hook** (`ICommentaryHook`, `CommentaryBus`, `CommentaryWriter`): play events become text lines for the ticker; a voice layer can subscribe later (no commentary audio in v0.5).
- **Upscaler setting** (Settings > Video): Off / DLSS / FSR (HDRP 17.6 advanced upscalers; the NVIDIA and AMD modules are in the manifest; falls back to the engine upscaler where unsupported).
- Settings now has four pages (Video, Gameplay, Broadcast, Audio).
- Autotest: `-pixAutotest` now also runs the broadcast segment (`-pixAutotestBroadcastOnly` runs only it): game intro, establishing shots, a staged touchdown with its automatic replay at both angles, a manual replay with the free camera at every preset angle and ground level, every overlay, first person on offense and defense, halftime and postgame shows; `bc_*.png` screenshots plus FPS for live play and for replays. `-pixUpscaler 0|1|2`.

### Changed
- Crowd meshes are built in 18 m chunks (228 draw chunks, was 160) so the crowd can react in a wave.
- **Performance** (autotest broadcast game, 2560x1440, High, RTX 5090 laptop, Upscaler Off): live play 101 FPS average, 87 1%-low, 70 minimum with the whole broadcast layer on; replays, cinematics and shows 119 average, 71 1%-low. The recorder costs 0.24 ms per sample (30 Hz). With DLSS (Balanced) live play was about 16 percent faster.
- `ArtLibrary` slim build whitelist adds `celebrate_td`, `celebrate_qb`, `cheer`, `frustration`, `salute`, `dance`, `idle_conversation`, `fumble`.
- `MatchInputFrame` has `DPadLeftHeld` / `DPadRightHeld` (replay scrubbing).

### Known Issues
- Replays show players the live camera was not rendering (off screen, `CullUpdateTransforms`) in the last pose they had; the broadcast camera frames all 22 so this rarely shows.
- The crowd wave swaps whole 18 m chunks between the seated and the arms-up pose, so a very close crowd shot shows blocks of fans moving together; fans cannot bob individually without a custom crowd shader.
- DLSS needs an NVIDIA RTX GPU and FSR 2 a D3D12 GPU; the option falls back silently on other hardware.
- Referees are hidden during replays of older plays (they would stand where the current play put them).
- No commentary audio.

## [0.4.0] - 2026-10-21 - "Contact"

The ESPN NFL 2K5 feel pass: tackles that look and feel physical, ball carriers who lean and plant, a quarterback who reads your hot route, hits you can feel in the camera and the controller, and a frame rate 75 percent higher.

### Added
- **Physics-blended tackles** (`ContactDirector`, `ContactSession`, `ContactMath`, `TackleVariants`). Every authored two-actor take in the mocap set (112 pairs: standing wraps, low wraps, tackle-broken takes, sacks, big hits, contested catches) is aligned to the simulated bodies by its authored root offsets: the carrier's hips land on the simulated carrier, the tackler comes in from where he really came from, the views warp from the simulation onto the animation root motion over 0.18 s, follow it through the hit and hand back to the simulation, which still owns where the carrier ends and the forward progress. The take is chosen from the sim's tackle type (wrap-up, big hit, dive, shoestring, broken tackle, drag, gang, push out of bounds, sack), the contact angle (front / side / behind), the carrier's speed and the closing speed.
- **Drags and gang tackles**: a drag plays the wrap up to the grab and holds it while the carrier churns on with the defender attached; the tackle that finally brings him down releases the same take, and extra defenders join with the tackler half of another take. Broken tackles show the carrier stumbling on while the defender whiffs and falls; sacks use the paired sack takes.
- **Active ragdoll** (`RagdollRig`, `RagdollStateMachine`): Humanoid-bone bodies, colliders and character joints built from the bind pose on demand; a pose blend from the animation (0.12 s), 0.4 to 1.2 s of simulation with capped velocities on a ground collider, then the settled pose is blended into a get-up take cut from the authored tackles (chosen by face up / face down). Used for trucked defenders, pancaked linemen and won stiff-arms.
- **Carrier polish** (`BodyMotion`, `LimbIK`): the spine banks into turns and pitches into accelerations, the stance foot is locked to the ground with two-bone IK while it is planted (no sliding), the stiff arm reaches for the defender's chest, protect-ball (LT) folds the ball high and tight, and the spin is now a real mocap spin cut from the BoNES spin / dodge takes. Trucks knock the defender back through the ragdoll.
- **Catching in traffic**: a catch with a defender on the receiver plays the authored contested-catch pair (both players go up for the ball).
- **Quarterback reads the hot route**: a receiver you hot-route is read from the snap and favoured when he is open (`OffenseAI.HotReadEdge` / `HotReadBonus`).
- **Collision feel** (`CollisionFeel`): trauma-based camera shake on hits (subtle, only big hits are felt), Xbox controller rumble on your own hits, sacks, trucks and catches, and hit sounds layered by impact energy (pad crack, body thud, second thud and a lower pitch on the big ones). Settings: Controller rumble, Camera shake on hits.
- **Graphics tiers and render scale**: Low / Medium / High / Ultra (`QualityTiers`: player LOD bias, volumetric fog, ambient occlusion, reflections, contact shadows, motion blur, stadium spot-light shadows, shadow distance) and a Render scale option (100 / 90 / 80 / 67 percent) on HDRP dynamic resolution.
- **Contact gallery autotest**: `-pixAutotestContactOnly [-pixAutotestContactCases wrap,bighit,...]` plays a scripted play for every tackle type, each carrier move, a contested catch, a sack and a big-hit ragdoll and saves a close-up screenshot sequence of each to `Logs/autotest`. New test hooks: `PlaySimOptions.ForceTackle` / `ForceTackleAfter`, `MatchDirector.ForcedCall` / `AutoMove`, `-pixTier`, `-pixRenderScale`.
- Editor tools: `Build Contact Table`, `Build Move Takes`, `Render Contact Sheet`, `Build Player Mesh LODs`, `Enable HDRP Dynamic Resolution`; `ArtLibrary` registers every authored pair (`contact_tackle`, `contact_sack`, `contact_bighit`, `contact_collision`) without the six-variant cap and can look a clip up by name.

### Changed
- **Performance**: the match player was an 80,000-vertex model (26,000-vertex helmet, 11,000 vertices of hands and shoes) drawn 22 times into every shadow cascade. The gear now uses its authored LODs and the body meshes simplified LODs by camera distance (`PlayerLod`, `PlayerMeshLodTable`), skinning is capped at four bones, coaches and officials are not posed off screen and coaches cast no shadows. Live play at 2560x1440 on the RTX 5090 laptop: 61 avg / 42 min / 50 1%-low FPS (v0.3) -> 107 avg / 79 min / 90 1%-low (Ultra, a full autotest game; GPU 15.8 -> 9.4 ms). Static 22-player formation at 2560x1440: Low 124, Medium 106, High 102, Ultra 89 FPS; Ultra at 67 percent render scale 110 FPS.
- Tackles no longer play the carrier's clip on the tackler and vice versa: in the authored two-actor pairs (`contact_*` keys, `ContactTable`) `_A` is the offense and `_B` the defender (v0.2 and v0.3 had them swapped). The older single-actor keys (`tackle_wrap`, `be_tackled`, `be_trucked`) are picked by the `Dmo` / `Omo` (defender / offense motion) part of the clip name instead.
- Hit sounds scale with impact energy; the settings screen has a graphics tier selector instead of the six Unity quality levels and rows are tighter to fit the new options.

### Fixed
- Players popping to a new pose when a tackle clip started: the take now starts from the simulation pose and warps.
- Tacklers and carriers interpenetrating after the hit: the pair keeps its authored layout and ends where the simulation says the carrier is.

### Known Issues
- Gang tackles with three or more defenders use the tackler half of a different take for the extras, so the wrap on the pile is approximate.
- The ragdoll is an animation-blended active ragdoll (pose pull-in, limits, capped velocities), not a fully motor-driven one; big-hit carriers use the authored pair rather than the ragdoll.
- Foot lock and lean are tuned for running and cuts; they fade out while an authored take, ragdoll or one-shot clip owns the body.
- Sideline toe-tap catches use the side-catch clips; there is no dedicated toe-tap take in the mocap set.
- DLSS / FSR are not wired: the Render scale option uses HDRP dynamic resolution with the asset's CAS upscaler.

## [0.3.0] - 2026-10-21 - "Playbook"

### Added
- **Full play-call screen** (2K5 / NCAA style, gamepad first, built from the UI kit): Formation -> Set -> Play cards, three cards per page with the play art drawn straight from the play's assignments (routes with arrows, the primary read in gold, blocks as T's, run paths, QB drop / hand-off, defensive zone bubbles, blitz arrows, man lines, spy). Tabs for Playbook, Ask Coach (coach suggestions from down, distance, field zone, score and clock, with the reason), Recent and Special Teams; type filters (Run / Pass / Play Action / Screen, Zone / Man / Blitz); LB / RB turn the page, Y flips (mirrors) the play, LT calls a timeout, RT changes tabs. The play clock keeps running while you choose. Works with D-pad + A / B.
- **Alignment sets**: new offensive sets (Shotgun Doubles / Bunch, Singleback Ace (12), I-Form Slot (20), Pistol Trips) alongside the base formations (now 371 plays); defense chooses Formation -> coverage group (Zone / Man / Blitz / Prevent / Goal Line) -> play.
- **Special-teams screens**: pick the kickoff (deep L / M / R, squib, onside), punt, field goal, extra point - and the kick return, punt return or kick block when the CPU kicks.
- **At the line (offense)**: five audibles per formation on A / B / Y / LB / RB (`AudibleBook`, editable), hot routes (receiver button, then a stick direction: streak, fade, slant, out, in, curl, block), flip the play, motion (RB to pick the man, LS to send him), slide protection (D-pad left / right), change the play, timeout. Every change rewrites the simulation's real assignments and alignments before the snap (`PlaySimulation.PreSnap`).
- **At the line (defense)**: coverage audibles (Cover 2 / 3 / 1 / 0 / 4), press / off, line pinch / spread, linebacker shifts in / out, show blitz / back off, and B cycles the defender you control.
- **Penalty flow**: the flag panel shows the foul, team, player number, yards and the referee's announcement with ACCEPT / DECLINE and the resulting down, distance and spot of each; the CPU decides by expected points. Pre-snap fouls (false start, offside, encroachment) and delay of game (the clock runs out) blow the play dead at once.
- **Officials and coaches**: a seven-man crew follows the play with locomotion and signals (touchdown, first down, incomplete, penalty flag, timeout, safety); two sideline coaches per team in team colours.
- **CPU play calling v1** (`PlayCaller`): situational calls from the whole playbook (down, distance, field zone, score, clock), run / pass tendency and scheme from the coach, anti-repetition, two-minute hurry-up with sideline routes, spikes and kneels, defensive personnel that matches the offense (nickel vs 11, base vs two backs, dime vs empty, goal-line), coverage by situation and blitz rate by aggression.
- **College rules end to end**: two feet in bounds, clock stops on first downs, hash marks 40 feet apart (ball spotted on them, college ticks painted), PAT from the 3, alternating overtime from the 25 at one end zone (scorebug shows OT, no clock), two-point tries from the third overtime.
- Playbook atlas tool (`PIX Football > Tools > Render Playbook Atlas`): every play's diagram to PNG pages in `Docs/Playbook`.
- `-pixAutotest` drives the new screens with scripted controller input (formation / set / play, filters, flip, audible, hot route, motion, slide, coverage audibles, accepting and declining a flag, delay of game) plus a college-rules game into overtime.

### Changed
- Pre-snap controls: Y is now "hot route" (was "change play"); D-pad up changes the play.
- Contact: paired tackle and block clips pull the two players to the clip's authored distance over 0.15 s and the tackler / blockers face their partner.
- The CPU's defense answers the offense's formation instead of a random call; punts and field goals are decided by `PlayCaller.FourthDown`.

### Fixed
- The scorebug, banner and hint bar are hidden under the final screen, coin toss, play-call screen and pause menu.
- Event banners are fully opaque and always readable.
- Receiver button icons never overlap (they spread out, stay inside the screen and keep a leader line to their player).

### Known Issues
- The stadium is painted with pro hash marks; in college games the college ticks are drawn on top, so both sets are visible.
- Audibles cannot be edited in a menu yet (the data supports it).
- A hot route moves the quarterback's first read but the quarterback still throws to whoever is open first.

## [0.2.0] - 2026-10-21 - "Kickoff"

### Added
- **Play Now / Team Select**: 32 pro teams with crest, colours and roster overall, quarter length (2/3/5/8/15 min), pro or college rules, difficulty (CPU ratings -8 / 0 / +6), user side.
- **MatchDirector** (Gameplay) over an engine-free **MatchFlow**: coin toss, kickoff, drives, scoring, PAT / two-point choice, quarters and halftime with ends swapping every quarter, overtime, final score and box score. Accelerated game clock (configurable runoff scale), play clock, user timeouts, CPU timeouts, two-minute warning, kneel and spike.
- **22 live players** (`PlayStage`, `SimAgentView`): S / M / L bodies by position, home / away kits with helmet in team colour, per-player `PlayableGraph` (`ActionAnimator`) layering mocap clips over the locomotion blend tree, `AnimKit` mapping of every `AgentAction` (stances, blocks, engaged / shed pairs, catches by direction and height, throws trimmed to the release moment, handoffs, paired tackles, sacks, falls, celebrations) and a procedural kick / punt leg swing.
- **Ball** (`BallFollower`): spiral, tumble, bounce, tee, tucked carry on the arm away from the nearest defender, quarterback hands.
- **Play calling**: 4-6 plays per situation on A/B/X/Y/LB/RB; CPU calls reuse the `PhysicalGameSim` coordinator (`ChoosePlays`, `DefenseResponseTo`).
- **2K-style control**: quarterback pre-snap with receiver button icons (tap bullet, hold touch / lob, right stick lead, throw away), auto-switch to the ball carrier or target receiver, defender selection with B, assist (the AI keeps playing your player until you steer), ring marker under the user.
- **Presentation**: scorebug, event banners, blue line of scrimmage and yellow first-down line on the grass, hint bar, pause menu, whistle / hits / catches / kicks / crowd cheers and boos, three match cameras (sideline broadcast that keeps the offense moving left to right, behind player, first person).
- `PlayFrame`: one mapping from the simulation frame to the stadium (mirrors both axes when the offense attacks the other end zone).
- `-pixAutotest` plays a full CPU game (2-minute quarters), screenshots the key moments to `Logs/autotest/`, logs FPS and writes `boxscore.txt`.
- `ClipTimingBuilder` (editor): measures throw release and catch moments in the mocap clips.
- New scene `Match_Pro`.

### Changed
- Night lighting is brighter (exposure and floodlights raised).
- Build whitelist includes the match animation clips, the ball and SFX.

### Fixed
- Midfield league logo is regulation size (about 11 yards across).

### Known Issues
- No kick mocap; procedural leg swing is a stand-in.
- Paired tackles and blocks play in place, so contact timing is approximate.
- Jersey numbers are not on the uniform shader.
- Crowd is static.

## [0.1.0] - 2026-10-20 - "First Steps"

### Added
- Unity 6 HDRP project (D3D12, linear color, 1440p default), Input System, Cinemachine 3.
- **Main menu** built from the PIX UI kit: PIX PRO FOOTBALL LEAGUE shield, Free Roam / Settings / Quit, Xbox glyph footer, full gamepad navigation, the composer's own soundtrack rotating in the background.
- **Settings**: resolution, display mode, quality, invert look, default camera, lighting, master/music/crowd volume (saved).
- **Stadium_Pro** scene: full pro stadium with regulation field (120 x 53 1/3 yd), NFL-height goal posts, pylons, yard markers, benches, hydration stands, chain crew.
- **Night game** (16 floodlights, volumetric fog) and **day game** lighting presets with broadcast-style color grading (ACES, bloom, SSAO, SSR, contact shadows, light motion blur, film grain).
- **25,817-fan crowd** (billboard fans baked from the crowd characters, 8 clothing palettes).
- **Free Roam**: one controllable player driven by the PIX momentum engine - ratings-based acceleration, speed-dependent turning, plant-and-cut speed loss, stamina-limited sprint.
- **Ball-carrier moves**: juke L/R, spin, stiff-arm L/R, truck, hurdle, dive, protect ball, celebrate - mocap animation layer over a locomotion blend tree.
- **Cameras**: broadcast sideline (2K-style), behind-the-player, and first-person helmet cam; cycle with View.
- **Uniforms** in all 32 team color sets; cycle teams from the pause menu.
- HUD: team crest, speed (mph), stamina bar, controls legend that switches between Xbox and keyboard glyphs.
- Pause menu: Resume, Camera, Lighting, Uniform, Quit to Menu.
- Football rules engine (pro + college rule sets, downs, clock, scoring, overtime, 16 penalties), playbook with ~195 offensive / ~70 defensive plays + special teams.
- League layer: official 32 pro teams (Liberty & Frontier Conferences), 138-school college world with 10 conferences and 41 bowls, regen player generator, scouting, draft, offseason, quick-sim box scores, seasons, awards.
- 22-player on-field simulation (blocking, routes, coverage, pursuit, catching, tackling) - wired into full games in v0.2.0.
- 357 automated EditMode tests; `-pixAutotest` smoke-test mode in the player build.

### Changed
- All Draft Fever branding replaced with PIX PRO FOOTBALL branding (wall pads, banners, video boards, end zones, jerseys, props).

### Known Issues
- Midfield league logo is oversized (spans 40 to 40); will be scaled to regulation size.
- Night lighting is a little dim compared with a real broadcast.
- Crowd is static (no animation or team-color sections yet); soft up close.
- No walk or spin mocap exists in the source set: walk is a slowed jog, spin is procedural.
- Ball is not yet carried in the player's hands; no goal-post/wall collision.
- Some head models and logo source files still carry real-person / real-school names internally; to be replaced before any public release.


[Unreleased]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.10.0...HEAD
[0.10.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.9.1...v0.10.0
[0.9.1]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/releases/tag/v0.1.0
