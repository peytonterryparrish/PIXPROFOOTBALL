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
| `PixFootball.Gameplay` | `Assets/_Project/Scripts/Gameplay` | Core, League, Unity.InputSystem, HDRP, Cinemachine | MatchDirector, PlayerAgent (momentum locomotion), BallController, Tackling, Blocking, AI (Offense/Defense/Coach), Cameras, Input |
| `PixFootball.Presentation` | `Assets/_Project/Scripts/Presentation` | Core, Gameplay | Scorebug/overlays, replay recorder, broadcast camera director |
| `PixFootball.UI` | `Assets/_Project/Scripts/UI` | Core, League, Gameplay | Menus, play-call screen |
| `PixFootball.Editor` | `Assets/_Project/Scripts/Editor` | all, editor-only | ProjectBootstrap, asset importers, scene builders, build script |
| `PixFootball.Tests.EditMode` | `Assets/_Project/Tests/EditMode` | Core, League | NUnit tests |

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
