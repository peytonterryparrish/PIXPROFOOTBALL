# Changelog

All notable changes to PIX Football. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions: [SemVer](https://semver.org/).

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

## [0.1.0] - 2026-10-21 â€” "First Steps"

### Added
- Unity 6 HDRP project (D3D12, linear color, 1440p default), Input System, Cinemachine 3.
- **Main menu** built from the PIX UI kit: PIX PRO FOOTBALL LEAGUE shield, Free Roam / Settings / Quit, Xbox glyph footer, full gamepad navigation, the composer's own soundtrack rotating in the background.
- **Settings**: resolution, display mode, quality, invert look, default camera, lighting, master/music/crowd volume (saved).
- **Stadium_Pro** scene: full pro stadium with regulation field (120 Ã— 53â…“ yd), NFL-height goal posts, pylons, yard markers, benches, hydration stands, chain crew.
- **Night game** (16 floodlights, volumetric fog) and **day game** lighting presets with broadcast-style color grading (ACES, bloom, SSAO, SSR, contact shadows, light motion blur, film grain).
- **25,817-fan crowd** (billboard fans baked from the crowd characters, 8 clothing palettes).
- **Free Roam**: one controllable player driven by the PIX momentum engine â€” ratings-based acceleration, speed-dependent turning, plant-and-cut speed loss, stamina-limited sprint.
- **Ball-carrier moves**: juke L/R, spin, stiff-arm L/R, truck, hurdle, dive, protect ball, celebrate â€” mocap animation layer over a locomotion blend tree.
- **Cameras**: broadcast sideline (2K-style), behind-the-player, and first-person helmet cam; cycle with View.
- **Uniforms** in all 32 team color sets; cycle teams from the pause menu.
- HUD: team crest, speed (mph), stamina bar, controls legend that switches between Xbox and keyboard glyphs.
- Pause menu: Resume, Camera, Lighting, Uniform, Quit to Menu.
- Football rules engine (pro + college rule sets, downs, clock, scoring, overtime, 16 penalties), playbook with ~195 offensive / ~70 defensive plays + special teams.
- League layer: official 32 pro teams (Liberty & Frontier Conferences), 138-school college world with 10 conferences and 41 bowls, regen player generator, scouting, draft, offseason, quick-sim box scores, seasons, awards.
- 22-player on-field simulation (blocking, routes, coverage, pursuit, catching, tackling) â€” wired into full games in v0.2.0.
- 357 automated EditMode tests; `-pixAutotest` smoke-test mode in the player build.

### Changed
- All Draft Fever branding replaced with PIX PRO FOOTBALL branding (wall pads, banners, video boards, end zones, jerseys, props).

### Known Issues
- Midfield league logo is oversized (spans 40 to 40); will be scaled to regulation size.
- Night lighting is a little dim compared with a real broadcast.
- Crowd is static (no animation or team-color sections yet); soft up close.
- No walk or spin mocap exists in the source set: walk is a slowed jog, spin is procedural.
- Ball is not yet carried in the player's hands; no goal-post/wall collision.
- Some head models and logo source files still carry real-person / real-school names internally; to be replaced before Steam.

