# Changelog

All notable changes to PIX Football. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions: [SemVer](https://semver.org/).

## [Unreleased]

Nothing yet. Next: the graphics release (shoulder pads, 15 body shapes, glass UI, Draft House stage / studio / jumbotrons / tunnel, PIX Bowl trophy, stadium upgrade), then v0.7.0 "League".

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


[Unreleased]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/peytonterryparrish/PIXPROFOOTBALL/releases/tag/v0.1.0
