# Changelog

All notable changes to PIX Football. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions: [SemVer](https://semver.org/).

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
- Tackles no longer play the carrier's clip on the tackler and vice versa: in the Draft Fever pairs `_A` is the offense and `_B` the defender (v0.2 and v0.3 had them swapped).
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

