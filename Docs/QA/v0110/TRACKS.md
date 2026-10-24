# v0.11.0 work tracks (started 2026-10-23)

Goal: fix every note in `PLAYTEST_v0100.md`, then ship one build, v0.11.0. The owner will judge it by WATCHING CPU-vs-CPU matches on the broadcast camera, so that is the lens for everything.

## Shared rules (same as v0.10, see `../v0100/TRACKS.md`)
- **Shared tree:** `C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL`.
  - Edit only the files your track owns (table below). Keep the shared tree compiling.
  - New files are fine. Use `partial` classes, with at most one-line hooks in another track's file.
  - Do not commit; integration commits.
- **Unity:**
  - Never open the shared project in Unity. Use your private copy `D:\PIXFootball\Work\<Track>\PIX`, with junctions to `_Licensed`/`_AssetSource`; refresh it with `robocopy` from the shared tree.
  - Every `-batchmode` run needs `-logFile` and a timeout. `WaitForEndOfFrame` never fires in batchmode.
  - Don't kill `PIXFootball.exe`; the owner may be playing it. The owner's own `C:\Users\peyto\My project` editor is unrelated, so never touch it.
- **Verification that counts:**
  - Numbers from recorded plays/matches (`EngineAudit`, `LegProbe`, `ChoreoAudit`, `BodyProbe`, `QbChoreoAudit`, `CoverageAudit`).
  - Broadcast-camera frame strips; look once and say plainly what still looks wrong.
  - One full EditMode run at the end (`-testCategory "!Slow"`). No galleries; keep costs down.
- **Bug list:** `BUGS.md` in this folder is the shared ≥100-item list from the AUDIT track. Each track fixes the rows tagged with its name and marks them in its log.
- **Log:** write your work to `Docs/QA/v0110/<track>.md`, with before/after numbers per note and per bug ID. Final report: under 400 words, numbers first.
- **Style bar:**
  - All-Pro 2K8 / Madden, photoreal, "simulated, calculated, choreographed — not physics, not arcade".
  - Specs: `Docs/QA/REFERENCE_Stances.md`, `REFERENCE_AllPro2K8.md`, `Docs/QA/v0100/*.md`.
  - No real likeness and no NFL marks. Never use Draft Fever logos.

## Cost rule (owner, 2026-10-23, in effect now)
- Finish the pass you're on within the stated budget (about 2–3 hours of work) and don't add scope.
- No more follow-up passes this round, except for S1 bugs.
- One EditMode run at the end.
- Reports ≤150 words, numbers first; put the details in your log.
- Anything unfinished goes into v0.11.0's known issues and v0.12's queue.

## Final IK (owner bought it, 2026-10-23)
- MOTION installs it into `Assets/_Licensed/RootMotion/`. It is git-ignored: never commit Final IK source, since the Asset Store EULA forbids redistributing it.
- Code that uses it sits behind the `PIX_FINALIK` define, so the repo compiles without it.
- How to use it is in the "Final IK in PIX" section of `motion.md`:
  - Grounder/LegIK for feet.
  - FullBodyBipedIK/InteractionSystem for hand contacts on tackles, blocks and handoffs.
- Other tracks use it through MOTION's notes and don't import it again.

## Tracks and file ownership

| Track | Notes | Owns |
|---|---|---|
| AUDIT | 1 (+ feeds all) | `Editor/Tools/MatchWatchAudit*` (new), `Docs/QA/v0110/BUGS.md`. Reads everything, fixes nothing. |
| CRITICAL | 2, 3 | Disappearing/missing/half bodies, floating, stance bugs: `StancePose`, `StanceIK`, `StanceRules*` (not Qb), `SimAgentView.Stance.cs`, `Art/PhotoPlayer*`, `PlayerLod`, `BodySanity`, kickoff formation/spawn and visibility code (`PlayStage` kickoff setup via partials) |
| WATCH+HUD | 4, 12 | Watch mode (CPU vs CPU, broadcast camera, no input), `Presentation/*` broadcast camera, `UI` match HUD (pop-ups, yard-gain banners, helper/button bar toggle and minimize), input routing (`Gameplay/Input*`), receiver input lock (note 9: no user control of a receiver until he has the ball) |
| QB | 8, 10 (QB footwork), 9 (ball flight) | `SimAgentView.Qb*`, `StanceRules.Qb.cs`, `StancePoses.QbSeq.cs`, `ProcGait.cs`, `AnimKit.Qb.cs`, Sim `OffenseAI.QbMotion.cs`, `QbSeqTiming.cs`, `PlayStage.QbSeq.cs`, ball flight (`BallSim*`, `BallFollower.Gfx` flight only), receiver ball-tracking AI (Sim `OffenseAI` route → ball adjustment) |
| RB | 7 | New `SimAgentView.Carry*.cs` / `CarryStyle*` partials: ball-carrier locomotion style (carry arm, free arm, lean, cuts, jukes, spin, stiff-arm, cutbacks, contact balance) via MOTION's matcher API; Sim carrier decision tweaks in new `CarrierAI*` partials |
| MOTION | 11, 5 (locomotion) | `Loco/*`, `Motion/*`, `SimAgentView.Loco.cs`, Mixamo / locomotion mocap import and cleanup |
| ANIMLIB | 5 (actions) | `Editor/Tools/AnimLib/*`, `ClipCleaner`, `CleanBatch`, `ArtLibraryBuilder`, `AnimKit.Extra*.cs`, `ClipSelect.cs`, `ClipTagTable.cs`; DFB/new mocap import and cleanup |
| AUDIO+CROWD | owner request 2026-10-23: cinematic instrumental football music (Draft Fever OST) instead of the owner's songs; crowd noise + crowd animation; full on-field SFX; all audio rows in `BUGS.md` | `MatchAudio*`, `StadiumAudio*`, `UiAudio*`, `CrowdBuilder*`, `CrowdInfo*`, `CrowdReaction*`, music config, new `Audio/*` / `Crowd/*` |
| UI-WP1 Foundation | Owner 2026-10-23: menu direction #16 "Tile Hub Modern" ships in v0.11.0, with Road to Heisman / college mode left out | Per `D:\PIXFootball\Work\UI16\SPEC.md` §4 WP1: `Glass/**`, `Shaders/PixGlass`, `UI/Glass/**` prefabs, `UiTheme.cs`, `UiKit`, `UiControls`, `UiPrompts`, `GlassUiShots*` |
| UI-WP2 Front end | same | SPEC WP2: `MainMenuController*`, `Hub/MainMenuHub`, `HubNav`, `MatchupCard`, `MenuHubModel`, `TeamSelectPanel*`, `SettingsPanel*`, `CareerStartPanel`, `UiDialog` |
| UI-WP4 Franchise | same | SPEC WP4: `UI/Franchise/**` |
| (WP3 Match) | | Done by WATCH+HUD inside its HUD files, using WP1 tokens and components |
| SIM-FLOW | 6, 10, 13 | `Sim/Choreo/*`, `Contact/*`, `ActionAnimator.cs`, `SimAgentView.Choreo/ActionGround/Flow.cs`, `BodyMotion.cs`, `EngageStepper.cs`, match flow between plays (`MatchDirector*` except `Cadence`/`KickHud`, `PlayStage.Flow/Connect`), situational AI and scenes (big hits, gang tackles, sideline/huddle/officials flow) |
