# v0.10.0 work tracks (started 2026-10-22)

Goal: fix every note in `PLAYTEST_v090.md`, then ship one build, v0.10.0. The owner will not move on (no College Network, nothing else) until these are fixed.

## Shared rules (every track)
- **Shared tree:** `C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL`.
  - Edit ONLY the files your track owns (table below). Keep the shared tree compiling at all times.
  - New files are fine. Use `partial` classes to add code to a file another track owns, with at most one-line hooks in their file.
  - Do not commit. The integration step commits.
- **Unity:** only one editor per project. Never open the shared project in Unity.
  - Use your private copy `D:\PIXFootball\Work\<Track>\PIX`, with junctions to `_Licensed`/`_AssetSource`. Reuse it if it exists; `robocopy` `Assets\_Project`, `Packages` and `ProjectSettings` from the shared tree to refresh it.
  - Every `-batchmode` run needs `-logFile` and a timeout/watchdog. `WaitForEndOfFrame` never fires in batchmode.
  - For clip renders use `AnimPlay.PlayClip` (Foot IK off) or `AnimationMode.SampleAnimationClip`.
- **Concurrent perf work:** a separate session is committing frame-rate fixes to main (hot paths in `Loco/*`, `PlayerLod`, `FootShadow`, `PhotoPlayer`, `Look/*`, `BallFollower.Gfx`, `CollisionFeel`, `FieldWear`, `QualityTiers`). Before you change any of those files, run `git log -3` and `git diff <file>` and build on top of its version. Never revert its edits.
- **Verification that counts:** judge motion with numbers from recorded plays (`Editor/Tools/EngineAudit.cs`, `LegProbe.cs`, `LocoShots.cs`) and frame strips (side, front, behind). Single screenshots don't count.
  - Look at your strips once and say plainly what still looks wrong.
  - One full EditMode run at the end (`-testCategory "!Slow"`). No galleries, no repeated suites. The owner wants costs kept down.
- **Style bar:**
  - All-Pro Football 2K8 / Madden, photoreal, "simulated, calculated, choreographed — not physics, not arcade".
  - Specs: `Docs/QA/REFERENCE_Stances.md`, `REFERENCE_AllPro2K8.md`, `REFERENCE_MaddenUI.md`, `Docs/QA/v090/*.md`.
  - No real-person likeness and no NFL/team/brand marks. Never use the Draft Fever / Fever Bowl logos.
- **Log:** write your work to `Docs/QA/v0100/<track>.md`. For each playtest note you own, give what changed, before/after numbers, what still looks wrong, and the files touched.
- **Final report:** under 500 words, in plain words for the owner, numbers first.

## Tracks and file ownership

| Track | Playtest notes | Owns |
|---|---|---|
| MOTION | 1, 4 (no gliding in locomotion) | `Scripts/Gameplay/Player/Loco/*`, `SimAgentView.Loco.cs`, new `Motion/` (motion matching), Mixamo import under `Assets/_Licensed/Mixamo/` (git-ignored) |
| QB | 4 (QB sequence), 7 | `SimAgentView.Qb.cs`, `StanceRules.Qb.cs`, `ProcGait.cs`, `AnimKit.Qb.cs`, `QbMirrorBuilder.cs`, Sim `OffenseAI.QbMotion.cs`, `MatchDirector.Cadence.cs`, kickoff/kick view and sim code (`MatchDirector.KickHud.cs`, kick sequencing in `PlayStage*`) |
| CHOREO | 4 (play-out, no floating during actions), 5, 6 | new Sim `Choreo/` (sequence planner), `Contact/*`, `ActionAnimator.cs`, `SimAgentView.ActionGround.cs`, `SimAgentView.Flow.cs`, `BodyMotion.cs` |
| DEFENSE | 2, 3 | Sim `DefenseAI*`, `OffenseAI*` (except `QbMotion`), `Core/Playbook.cs`, `DefaultPlaybooks*`, play-art/playbook data, `SimCalibrationTests` |
| BODIES | 9 | `StancePose.cs`, `StanceIK.cs`, `StanceRules*` (except `Qb`), `SimAgentView.Stance.cs`, `Art/PhotoPlayer*` skinning/proportions (perf session: build on its version), ball attach/carry code (`BallFollower.cs` hold sockets) |
| ANIMLIB | 8 | `Editor/` clip import/cleanup tools (`ClipCleaner`, `CleanBatch`, `ArtLibraryBuilder` rules), `AnimKit.Extra.cs`, new clip-selection tables; the DFB animation sources in `_AssetSource/DraftFever`, `Assets/_Licensed/DraftFever/Animations` |
| UI | 10 | Nothing in the game. Output goes to `D:\PIXFootball\Renders\ui_directions\` plus `Docs/QA/v0100/ui.md`. |
