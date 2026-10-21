# PIX Football — Architecture Contract

Unity 6000.6.6f1, HDRP 17.6, C#, Input System. Windows (Steam) target, Xbox controller first.
Unity path: `C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe`.

## Golden rules
1. **No placeholder art.** Visuals come from the user's Draft Fever assets (imported under
   `Assets/_Licensed/`), Blender-generated meshes, Tripo or PixelLab. Never ship capsules/greybox.
2. `Assets/_Licensed/` and `_AssetSource/` are **git-ignored** (licensed art stays local; repo is public).
   Code must never hard-reference `_Licensed` GUIDs from committed assets in a way that breaks
   compilation when the folder is missing — load through `ArtLibrary` (Resources-free registry
   ScriptableObject at `Assets/_Licensed/ArtLibrary.asset`, looked up by `ArtLibrary.Load()`).
3. Football logic is **pure C#** (no UnityEngine types except `Mathf`-free math in `PixFootball.Core`),
   deterministic with a seeded `Rng`. Unity-side code only adapts it.
4. Everything data-driven: ScriptableObjects wrap Core data for authoring; JSON for saves.
5. Every system gets EditMode tests in `Assets/_Project/Tests/EditMode`.
6. Only original names/branding in generated content.

## Assemblies (asmdef) and folders
| Assembly | Folder | Depends on | Contents |
|---|---|---|---|
| `PixFootball.Core` | `Assets/_Project/Scripts/Core` | — (noEngineReferences: true) | Rng, Ratings, Positions, RuleSet, GameState (downs, clock, score, possession), Penalties, Playbook model (Formation/Set/Play/Route/Assignment), PlayResult |
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

## Key Core types (contract — keep these names)
- `Rng` (xorshift128+, seedable, `NextFloat()`, `Range(int,int)`, `Gaussian()`).
- `Position` enum: QB,RB,FB,WR,TE,LT,LG,C,RG,RT,LE,DT,RE,LOLB,MLB,ROLB,CB,FS,SS,K,P.
- `Ratings` struct: Speed, Acceleration, Agility, Strength, Awareness, Catching, Carrying,
  ThrowPower, ThrowAccuracy, Tackling, BlockShedding, RunBlocking, PassBlocking, ManCoverage,
  ZoneCoverage, Jumping, Stamina, Injury, KickPower, KickAccuracy, BreakTackle, Elusiveness (0–99) + `Overall(Position)`.
- `RuleSet` (data): `Pro` and `College` presets — feet in bounds, overtime format, clock-stops-on-first-down,
  hash width, PAT distance, two-point, quarter length, timeouts, challenges.
- `GameState`: quarter, clock seconds, down, distance, ball spot, possession, score, timeouts;
  `Apply(PlayResult)` advances it deterministically (first downs, TDs, safeties, turnovers, change of possession, end of quarter/half/OT).
- `Penalty` catalog + enforcement.
- `Playbook` → `Formation` → `PlaySet` → `Play` (offense: per-slot `Assignment` = Route/Block/Run path; defense: Coverage/Rush/Zone).

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
