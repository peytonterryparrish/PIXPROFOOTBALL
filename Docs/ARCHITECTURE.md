# PIX Football â€” Architecture Contract

Unity 6000.6.6f1, HDRP 17.6, C#, Input System. Windows (Steam) target, Xbox controller first.
Unity path: `C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe`.

## Golden rules
1. **No placeholder art.** Visuals come from the user's Draft Fever assets (imported under
   `Assets/_Licensed/`), Blender-generated meshes, Tripo or PixelLab. Never ship capsules/greybox.
2. `Assets/_Licensed/` and `_AssetSource/` are **git-ignored** (licensed art stays local; repo is public).
   Code must never hard-reference `_Licensed` GUIDs from committed assets in a way that breaks
   compilation when the folder is missing â€” load through `ArtLibrary` (Resources-free registry
   ScriptableObject at `Assets/_Licensed/ArtLibrary.asset`, looked up by `ArtLibrary.Load()`).
3. Football logic is **pure C#** (no UnityEngine types except `Mathf`-free math in `PixFootball.Core`),
   deterministic with a seeded `Rng`. Unity-side code only adapts it.
4. Everything data-driven: ScriptableObjects wrap Core data for authoring; JSON for saves.
5. Every system gets EditMode tests in `Assets/_Project/Tests/EditMode`.
6. Only original names/branding in generated content.

## Assemblies (asmdef) and folders
| Assembly | Folder | Depends on | Contents |
|---|---|---|---|
| `PixFootball.Core` | `Assets/_Project/Scripts/Core` | â€” (noEngineReferences: true) | Rng, Ratings, Positions, RuleSet, GameState (downs, clock, score, possession), Penalties, Playbook model (Formation/Set/Play/Route/Assignment), PlayResult |
| `PixFootball.League` | `Assets/_Project/Scripts/League` | Core (noEngineReferences: true) | Team, School, Conference, Player (bio+ratings+contract), League, CollegeWorld, Generator (regen + names), QuickSim (box scores), Season/Schedule |
| `PixFootball.Sim` | `Assets/_Project/Scripts/Sim` | Core, League (noEngineReferences: true) | Engine-free, deterministic 60 Hz simulation of one live play (22 agents + ball, yards/seconds): `PlaySimulation` (Setup/Tick/RunToCompletion), `AgentBody` kinematics, `BallSim` + `BallPhysics`, blocking/tackling/catch resolvers, offense/defense/special-teams AI, `PlayEventLog`, `PlayResult` for `GameState.Apply`, `PhysicalGameSim` (headless CPU-vs-CPU games) |
| `PixFootball.Gameplay` | `Assets/_Project/Scripts/Gameplay` | Core, League, Unity.InputSystem, HDRP, Cinemachine | MatchDirector, PlayerAgent (momentum locomotion), BallController, Tackling, Blocking, AI (Offense/Defense/Coach), Cameras, Input |
| `PixFootball.Presentation` | `Assets/_Project/Scripts/Presentation` | Core, Gameplay | Scorebug/overlays, replay recorder, broadcast camera director |
| `PixFootball.UI` | `Assets/_Project/Scripts/UI` | Core, League, Gameplay | Menus, play-call screen |
| `PixFootball.Editor` | `Assets/_Project/Scripts/Editor` | all, editor-only | ProjectBootstrap, asset importers, scene builders, build script |
| `PixFootball.Tests.EditMode` | `Assets/_Project/Tests/EditMode` | Core, League, Sim | NUnit tests |

Namespaces mirror assembly names (`PixFootball.Core`, ...).

## Units & coordinates
- Field: X = sideline-to-sideline (width 53.33 yd), Z = goal-line axis. 1 Unity unit = 1 meter;
  `Yards.ToMeters = 0.9144`. Core works in **yards**; Gameplay converts.
- Core field position: `BallSpot` 0..100 yards from offense's own goal line.

## Key Core types (contract â€” keep these names)
- `Rng` (xorshift128+, seedable, `NextFloat()`, `Range(int,int)`, `Gaussian()`).
- `Position` enum: QB,RB,FB,WR,TE,LT,LG,C,RG,RT,LE,DT,RE,LOLB,MLB,ROLB,CB,FS,SS,K,P.
- `Ratings` struct: Speed, Acceleration, Agility, Strength, Awareness, Catching, Carrying,
  ThrowPower, ThrowAccuracy, Tackling, BlockShedding, RunBlocking, PassBlocking, ManCoverage,
  ZoneCoverage, Jumping, Stamina, Injury, KickPower, KickAccuracy, BreakTackle, Elusiveness (0â€“99) + `Overall(Position)`.
- `RuleSet` (data): `Pro` and `College` presets â€” feet in bounds, overtime format, clock-stops-on-first-down,
  hash width, PAT distance, two-point, quarter length, timeouts, challenges.
- `GameState`: quarter, clock seconds, down, distance, ball spot, possession, score, timeouts;
  `Apply(PlayResult)` advances it deterministically (first downs, TDs, safeties, turnovers, change of possession, end of quarter/half/OT).
- `Penalty` catalog + enforcement.
- `Playbook` â†’ `Formation` â†’ `PlaySet` â†’ `Play` (offense: per-slot `Assignment` = Route/Block/Run path; defense: Coverage/Rush/Zone).

## Gameplay feel targets (ESPN NFL 2K5)
- Locomotion is momentum based: velocity integrates toward desired velocity with accel from ratings,
  turn rate falls with speed and mass. No instant direction change.
- Root-motion mocap from Draft Fever (BoNES/Motus retargets) layered over physics-driven capsule.
- Outcomes come from simulated positions + ratings, not dice.

## Versions
See `CHANGELOG.md` and `PatchNotes/`. Release flow in `Docs/RELEASING.md`.

## v0.2 match architecture
```
MainMenu --Play Now--> TeamSelectPanel (UI) --GameSession.StartMatch(MatchConfig)--> Match_Pro scene
Match_Pro:  MatchDirector (Gameplay) --- IMatchPresentation ---> MatchHud (UI)
            |-- MatchFlow (engine-free: GameState + PhysicalGameSim coordinator + PlaySimulation per snap)
            |-- PlayStage  (22 SimAgentViews pooled by body size + BallFollower; PlayFrame mapping)
            |-- MatchCameraRig (Cinemachine: broadcast / behind / first person / debug close-up)
            |-- FieldMarkers (blue LOS, yellow first-down strip, ring under the user's player)
            `-- MatchAudio
```
- **MatchFlow** (`Gameplay/Match/MatchFlow.cs`) has no Unity types: `PrepareSnap` (burn the runoff, report period changes), `CpuCalls` / `DefenseAnswer`, `OffenseOptions` / `DefenseOptions` (the 4-6 plays on A/B/X/Y/LB/RB), `CreateSim`, `Finish` (apply to `GameState`, classify the outcome for banners / stats). The accelerated clock is `MatchConfig.RunoffScale` (1.0 = rule-book runoff, about 60-70 snaps for 5-minute quarters).
- **Coordinates** (`PlayFrame`): the simulation always has the offense driving toward +Y. `PlayFrame.For(possession, quarter)` rotates the frame 180 degrees about the vertical axis when the offense attacks the -Z end zone (X and Z both mirrored, so handedness is kept). Home attacks +Z in odd quarters, -Z in even ones; the broadcast camera moves to the side that makes the offense drive left to right.
- **Animation**: `SimAgentView.ApplyMatch` feeds locomotion (velocity / facing) to the controller inside a `PlayableGraph` (`ActionAnimator`: layer mixer, controller at the bottom, one clip on top with manual time, fade in / out, loop / hold). `AnimKit` maps each `AgentAction` to ArtLibrary clip keys; `ClipTimingTable` (built by `ClipTimingBuilder`) gives the throw-release and catch moments so the clip is trimmed to line up with the ball. Paired takes (`<key>` for the first actor, `<key>_b` for the second) are used for engaged blocks, handoffs, tackles and sacks, aligned by variant name. Carrier moves (juke, spin, stiff-arm, truck, hurdle, dive) reuse the v0.1 action layer.
- **Kicks / punts**: there is no kicking mocap in the source set. `SimAgentView.LateUpdate` drives the right hip and knee procedurally (back-swing, contact at 0.12 s, follow-through) over the running animation when the simulation reports `AgentAction.Kick`.
- **User control**: `MatchInput` polls the pad / keyboard directly (no input-action asset). The director picks the controlled agent (QB pre-snap; ball carrier; intended receiver while a pass is in the air; the defender nearest the ball) and only hands him to the simulation when the user is actually steering, so the AI keeps playing the rest of the time.
- **Autotest**: `MatchDirector.ForceAutopilot` / `Autopilot` makes the director play the user's side; `AutoTest` listens to `PlayStarted`, `PlayFinished`, `SimEvent` and `Milestone`, and `MatchCameraRig.CloseUp` takes close shots of tackles, catches, throws and kicks.

## v0.3 play calling, pre-snap and penalties
```
Core:      Playbook (Formation Family/SetName) - PlayEditing (clone, mirror, hot route, slide) - PlayDiagram (route/block/zone geometry)
           PlayCaller (CPU + Ask Coach) - AudibleBook / DefenseAdjust - PlayCallModel (pure navigation view-model) - PenaltyEnforcer.Describe
Sim:       PlaySimulation.PreSnap (ChangeOffensePlay, FlipOffense, SetHotRoute, SetSlideProtection, SetMotion, SetDefenseAdjust, CallDelayOfGame)
Gameplay:  MatchFlow(.PlayCall) builds the PlayCallModel, penalty choices and referee text; MatchDirector(.PreSnap) polls input and drives everything
           MatchOfficials (7-man crew + coaches) - FieldMarkers.SetHashMarks (college ticks)
UI:        PlayCallScreen + PlayDiagramGraphic, PreSnapPanel, PenaltyPanel, UiPrompts - all rendered from models the director hands over
```
- **Play-call screen**: `PlayCallModel` (Core, engine-free) holds tabs, stage (Formation -> Set -> Play), filters, paging (3 cards), flip, timeout request and expiry; `MatchDirector.DriveCallScreen` maps pad input onto it and `PlayCallScreen` renders it. Sets are the formation variants of a family (offense) or coverage groups (defense).
- **Pre-snap changes work on clones**: `PlayEditing` never mutates the playbook; `PlaySimulation.PreSnap` swaps `OffPlay` / `DefPlay`, rewrites every agent's `Assign`, re-lays out alignments (flip, shifts, press) and re-prepares the AI. Motion is a pre-snap walk (`PreSnapWant`).
- **Penalties**: `PenaltyEnforcer.Describe` clones the state to compute both options; `MatchFlow.Finish(sim, decision)` applies the choice; `MatchDirector.PenaltyFlow` shows the panel (user) or the CPU's expected-points choice.
- **College rules**: `PlayFrame.For(GameState)` keeps both overtime possessions at one end zone; hash X comes from `FieldGeometry.HashX(hash, rules)` in the sim; `FieldMarkers.SetHashMarks` paints the 40-foot hashes.
- **Playbook review**: `PIX Football > Tools > Render Playbook Atlas` (or `PlayCardPainter` in a console harness) renders every play to `Docs/Playbook/*.png`.
- **Autotest**: `MatchInput.ScriptMode` + `Enqueue` feed scripted frames through the real input path (`AutoTest.V3.cs`).

## v0.4 contact, ragdoll and performance
```
Art:       ContactTable (PixContact.asset)      measured root / hips / heading paths of every authored two-actor take + contact moment, approach geometry, get-up timing
           MoveTakeTable (PixMoveTakes.asset)   usable windows inside the long BoNES takes (the spin)
           PlayerMeshLodTable (PixMeshLods.asset) simplified torso / legs / arms / hands / shoes / head meshes
Editor:    ContactClipBuilder (Build Contact Table / Build Move Takes / contact sheets), PlayerMeshLodBuilder, HdrpTuning, PerfDiagnostics
Gameplay/Contact:
           ContactMath      Rigid2 alignment, bearing, warp / authority weights, array sampling (pure, tested)
           TackleVariants   TackleType + contact angle + speed -> best authored pair (pure, tested)
           ContactSession   one pair played against the simulation: align at contact, warp in, follow the animation root motion, hand back to the sim (pure)
           ContactDirector  sim events -> sessions, gang tacklers / drags (hold), contested catches, stiff-arm IK, truck / pancake knock-backs, get-up takes
           RagdollStateMachine (pure) + RagdollRig (bodies, joints, ground plane, blend)
Gameplay/Player:
           BodyMotion       spine lean / pitch, stance-foot lock, stiff-arm and protect-ball arm IK (LimbIK = analytic two-bone solver, pure)
           PlayerLod        distance LOD of helmet / mask / body meshes
Gameplay:  CollisionFeel (camera trauma shake + Xbox rumble), QualityTiers (Low / Medium / High / Ultra + render scale)
```
- **Authored pairs.** Every Draft Fever tackle / sack / big-hit / contested-catch take is two clips (`_A` = the offense: ball carrier, quarterback, receiver; `_B` = the defender). The default import re-bases each clip to its own start, which throws the authored layout away, so `ContactClipBuilder` imports the pair clips once with "Root Transform Position (XZ) Based Upon: Original", records where each actor starts in the shared mocap scene, reverts to Center of Mass (the clips still play in place at the view origin) and stores `root(t)` and `hips(t)` of both actors in that shared frame. v0.2 / v0.3 played the A clip on the tackler and the B clip on the carrier; v0.4 has the roles right.
- **Alignment.** At a tackle event `ContactDirector` picks the pair with `TackleVariants.Choose` (kind from the sim's `TackleType`, bearing of the tackler in the carrier's frame, carrier speed, closing speed), builds a `ContactSession` that puts the authored carrier hips on the simulated carrier (position exact, heading blended with the contact axis) and plays both clips from 0.18 s before the authored contact. The views warp from the simulation onto the animation root path over that lead-in (`ContactMath.WarpIn`), follow it exactly for 0.3 s after the hit and then blend back to the simulation (`ContactMath.Authority`) so the carrier ends where the sim says he is.
- **Gang tackles and drags.** A `Drag` event plays the pair up to 0.5 s after contact and freezes it (`ActionAnimator.SetFrozen`) while the sim carrier churns on; the next tackle event for that carrier releases it. Extra defenders play the B clip of another pair aligned to the carrier's pose.
- **Ragdoll.** `RagdollRig` builds Humanoid-bone rigid bodies, capsule / box colliders and character joints lazily from the avatar's bind pose; bodies sit on a layer that only collides with an invisible ground plane. Animation to ragdoll is a 0.12 s pose blend (the animated pose pulls the bodies), 0.4 to 1.2 s of simulation with capped velocities, then the settled pose is blended into a get-up take cut from the lying-and-rising part of the authored tackle takes, chosen by the face up / down test.
- **Performance.** The 80,000-vertex match player was the cost: the 26,000-vertex helmet and the 11,000 vertices of hands and shoes drew into every shadow cascade 22 times. `PlayerLod` swaps the gear to its authored LOD01 / LOD02 and the body to clustered meshes by distance; coaches and officials are not posed off screen and the coaches cast no shadows.
- **Autotest.** `-pixAutotestContactOnly [-pixAutotestContactCases wrap,bighit,...]` scripts one play per case with the `PlaySimOptions.ForceTackle` / `ForceTackleAfter` test hooks, `MatchDirector.ForcedCall` / `AutoMove` and takes a close-up screenshot sequence of each contact into `Logs/autotest`.
- **Sim changes** are engine-free and deterministic: the quarterback reads a hot-routed receiver from the snap and favours him (`OffenseAI.HotReadEdge` / `HotReadBonus`); the two test hooks default to off.

## v0.5 broadcast layer
```
Gameplay:      IBroadcast (BroadcastHooks.cs) - the only thing MatchDirector knows about the broadcast layer
               MatchCameraRig.SetOverride (Cinemachine brain off, live cameras keep tracking) + SetLookAssist / head bob (first person)
               PlayStage.WalkTo / PlayReaction (celebration choreography), MatchOfficials.CoachTransform / SetCrewVisible, UpscalerControl
Presentation:  BroadcastDirector (partial: core, Shots, Shows, Debug)  implements IBroadcast, added to the match scene (also by a sceneLoaded bootstrap)
               Replay/    ReplayData (QuatPack, ReplayClip, ReplayLibrary), ReplayRecorder, ReplayPlayer (actors), ReplayView (angles + free camera), ReplayController (auto / manual / reel), ReplayHud
               Director/  ShotRules (pure), ShotLibrary (camera moves), BroadcastCamera (override camera + HDRP depth of field), HighlightScorer (pure)
               Stats/     GameTracker (live box score, drives), OverlayText, Commentary (ICommentaryHook + CommentaryBus)
               Overlays/  TvOverlays (cards in four slots), TvUi (procedural parallelogram sprites)
               World/     CrowdReaction, FirstPersonFx
               BroadcastAutoTest (registered in AutoTest.ExtraParts)
UI:            MatchHud (pause: Instant Replay + Cinematics, SetReplayMode, SetHudMinimal), SettingsPanel (4 pages)
```
- **Wiring.** `MatchDirector.Start` finds the `IBroadcast` component (`Match` object) and calls `Attach`; every hook is a null-checked call, so the game plays exactly like v0.4 without it. The broadcast layer never owns game state: it reads the finished `PlayOutcome`, the simulation (`PlaySimulation.Log`, agents), the stage views and the game state.
- **Instant replay: record, not re-simulate.** The simulation is deterministic per play, but what the viewer sees is not a function of it: the contact director warps players onto authored tackle takes, the ragdoll is physics, clip variants and the carry arm depend on view state and input timing. `ReplayRecorder` therefore stores what was shown. Every 1/30 s (in `LateUpdate`, after animation, IK and the ball) it writes the root pose of the 22 views, the local rotation of every animated bone (smallest-three packing, 4 bytes; `BoneSet` = skinned bones plus their ancestors, same order for every body of a size), the hip / root-bone positions and the ball. About 6-10 KB per sample, 2-3 MB for a play, 0.2 ms per sample. Pre-snap samples older than 1.1 s are dropped at the snap. `ReplayLibrary` keeps the newest 8 plays plus the 12 most exciting (`HighlightScorer`) inside a 90 MB budget. Playback (`ReplayPlayer`) poses 22 plain avatars (no animation graph) from the interpolated samples: pure function of (clip, time), so scrubbing, slow motion and rewind are exact.
- **Camera hand-over.** `BroadcastCamera.Begin` turns the Cinemachine brain off and drives the main camera directly (position, rotation, field of view). Depth of field uses the HDRP physical camera: the camera becomes a 36 x 24 mm sensor with the focal length from the shot's field of view, `Camera.aperture` and `Camera.focusDistance` set the blur and a global volume with a `DepthOfField` override (physical camera mode) fades in. `End` is an instant cut back to the live cameras, which kept tracking underneath. TAA is swapped for FXAA for three frames after a cut.
- **Director rules** (`ShotRules`, tested): nothing with Cinematics Off; a pre-snap shot only when the play clock leaves at least 0.6 s of live view after it (max 2.6 s) and ends the instant the snap happens (`OnPreSnapFrame(snapping)`); establishing shots on the first play of the game (sky swoop), of a drive (sky / sideline QB / end zone, rotating) and, in Minimal, the second half; post-play: tight carrier shot, touchdown celebration plus crowd or coach, turnover coach reaction, an occasional crowd shot, always inside the 5.2 s budget. The automatic replay runs after the wind-down for plays scoring above the threshold (45 Full, 60 Minimal) and is skippable with A.
- **Stats.** `GameTracker` builds a real box score for the live game (player lines, team box scores, drives with plays / yards / time of possession / result, scoring events) from `PlayOutcome` + the players on the field; the lower-thirds, drive summaries, team comparison, halftime and postgame shows read from it (`OverlayText`).
- **Overlays** live in four screen slots (lower left, right side, lower right, centre) so they never overlap each other; the scorebug / banner / hint bar of the HUD sit outside those slots. Cards are procedural (parallelogram sprites drawn into textures), UI-kit colours, Saira Condensed / Barlow, crests from the kit.
- **Crowd.** The fan billboards have two poses in the atlas (seated, arms up) one atlas row apart; a reaction scrolls each chunk's atlas window by one row in a wave around the bowl (`CrowdReaction`), with additive camera-flash sprites at seat positions. The crowd meshes are built in 18 m chunks for this.
- **Autotest.** `-pixAutotest` runs the broadcast segment after the v0.4 segments (`AutoTest.ExtraParts`); `-pixAutotestBroadcastOnly` runs only it. It writes `bc_*.png` to the output directory and logs FPS for live play and for replays / cinematics. `-pixUpscaler 0|1|2` overrides the upscaler.
