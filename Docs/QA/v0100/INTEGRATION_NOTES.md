# v0.10.0 integration notes (collected as tracks finish)

## Already on main
- Perf fixes: 81d001b, plus the note e2635a8 (`perf.md`). v0.9.1 shipped as an interim build; v0.10.0 builds on it.

## UI (done, no game code)
- Gallery: `D:\PIXFootball\Renders\ui_directions\index.html`. Notes in `ui.md`.
- The owner hasn't picked a direction yet. Implementing it comes after v0.10.0.

## MOTION (done, log `motion.md`)
- Motion matcher is on by default. A/B flags: `-pixLocoLegacy` (v0.9 GaitPlayer), `-pixLocoOff`.
- Its EditMode run: 1169/1169.
- One-line hooks in other files:
  - `ActionAnimator.cs`
  - `SimAgentView.cs`
  - `SimAgentView.ActionGround.cs`
  - `Match/PlayStage.cs`
  - `EngineAudit.cs`
- New files lack `.meta`: `Motion/*`, `PlaySimulation.MotionPlan.cs`, `PlayStage.Motion.cs`, editor tools, `MotionMatchingTests.cs`.
- Git-ignored assets the build needs:
  - `Assets/_Licensed/Mixamo/*`
  - `Assets/_Licensed/Resources/PixMotionDb.asset`
  - Check that the slim-ArtLibrary build swap keeps `PixMotionDb`.
- Open items:
  - Jog, shuffle and backpedal slide (backpedal handed to CHOREO).
  - Below 1.7 m/s the old tree walks.
  - Sprinters are played at +20–25% rate. A real sprint capture would fix it.
  - Chest pops: audit total rose from 306 to 419.

## QB (first pass done, follow-up running; log `qb.md`)
- One-line hooks in other tracks' files:
  - BODIES: `SimAgentView.Stance.cs`
  - CHOREO: `PlayStage.Connect.cs`, `PlayStage.cs`, `OffenseAI.Qb.cs`
  - Other: `SimAgentView.cs`, `MatchDirector.cs`
- Updated stale v0.9.0 assertions in `ConnectTests.cs` and `QbStanceSimTests.cs`.
- `SimAbilityLineTests.SniperBlitzArrivesSooner` reads −0.026 against a −0.03 bound, probably shifted by the 0.2 s secure. Loosen the bound or re-tune at integration.
- Its run had 4 failures belonging to other tracks: `ChoreoTests` ×2, `BodiesTests` ×1, and the SniperBlitz test above.

## BODIES (first pass done, follow-up running; log `bodies.md`)
- One-line hooks in other files:
  - `PlayStage.cs` (`BallFlowHeld`)
  - `MatchOfficials.cs` (made `partial`)
  - `LimbIK.cs` (hinge alignment)
- New files need `.meta`:
  - `BodySanity.cs`
  - `BallFollower.Release.cs`
  - `PlayStage.BallFlow.cs`
  - `MatchOfficials.Ball.cs`
  - `BodiesTests.cs`
  - `PhotoArmProportions.cs`
- `PhotoPlayerTests` pass in its run, so the DEFENSE failures were probably its copy missing `_Licensed/PhotorealPlayer`. Re-check at integration.
- Follow-ups running:
  - Rebuild the prefabs with proper arm proportions.
  - Seat and shoulder skin weights.
  - The official must never leave the ball in the carrier's hand.
  - MOTION: Mixamo forearm twist.
  - ANIMLIB: OL get-off clip with T-pose arms.

## QB (done, 3 passes)
- Open: scramble running slide is now MOTION's. Boot slide is 0.24 m/s.

## ANIMLIB (first pass done, follow-up running; log `animlib.md`)
- The shared `_Licensed` junction now holds the new FBX, the cleaned clips, the rebuilt ArtLibrary and `PixClipTags.asset`.
- Re-run `CycleSpeeds` and `ClipFacingAudit` after integration.
- Its run had 4 failures belonging to others:
  - PhotoPlayer / PhotoRosterV2 bind-pose counts, 52 vs 54. BODIES is rebuilding the prefabs; re-check.
  - 2 `GfxTests` LOD cases. Possibly a stale copy, since the perf commit updated them; re-check.
- 30 QB clips it used to have were rejected for pops. Check that the QB track's picks still resolve.

## CHOREO (first pass done, second pass running; log `choreo.md`)
- Flags:
  - `-pixNoChoreo`
  - `-pixNoChoreoView`
  - `-pixChoreoDefault`
  - `-pixNoTurn`
  - `-pixNoGetOffFit`
- `PixChoreo.asset`: script guid patched.
- Add the `ARCHITECTURE.md` paragraph from the end of `choreo.md`.
- The pass YPA band was loosened (7.61 → 6.96 in the 4000-play test). Judge it with all tracks merged.
- Its run showed `BodiesTests` ×2 and `SniperBlitz` failing. Re-check both.
- Second pass:
  - New `EngageStepper`.
  - `MeshMinGap` added to the QB track's `QbMeshPull` (one line).
  - `LoopRate` cap of 360°/s.
  - Test bands loosened: rush 0.40, pass 0.85.
  - Remaining failure: a `BodiesTests` helper-bones test (BODIES switched helper bones off in its last pass). Re-check it.
- Third pass done:
  - The rebuilt `Assets/_Licensed/Resources/PixChoreo.asset` is git-ignored and must be in the build.
  - New flags: `-pixNoRootScale`, `-pixNoDuel`, `-pixNoChoreoCelebrate`, `-pixSteerSacks`, `-pixSteerCatchers`.
  - Pass tilt fixed (rusher and catcher steering off). Rush is −0.25 yd/play, inside its band.
- Fourth (final) pass is running: lead-in time warp, GetUp/Jump/Stumble foot lock, INT/big-play celebrations.

## MOTION follow-up (done)
- New hooks: `ActionAnimator.LegsToLoco`, `BodyMotion.ForceLoco`.
- Its run: 1167/1169. The 2 failures are photoreal bind-pose tests; they should pass after BODIES' v5 prefab rebuild.
- Optional Mixamo clips are waiting on the owner's sign-in: Run/Jog/Walk To Stop, Idle, Side Shuffle, Defensive Shuffle.

## BODIES: required build step
- Before building, open the shared project once in the editor, or run `PhotoArmProportions.RunBatch` in batchmode without `-pixSkipArmReimport`.
- That rebuilds the 15 prefabs (arm proportions, skin relax) and writes `Library/PixArmProportions.v5.txt` (Version 5, final).
- Known issue for the patch notes: the seat (p99 1.6–1.7×) and sleeve (1.8–2.1×) still stretch slightly at extreme poses. Fixing that needs a Blender corrective shape later.
- Its tests: `BodiesTests` + `PhotoPlayerTests` 19/19.

## DEFENSE (done, log `defense.md`)
- Edited files owned by other tracks:
  - `OffenseAI.Qb.cs`: next to the QB track's `QbSeqWatch` hooks. Check the merge.
  - `TackleResolver.cs`: one line.
  - `SpecialTeamsAI.cs`: one line, the punt constant.
  - `PaceAndPlayEndTests.cs`: shove-out tolerance raised to 1.1 yd.
- New files that need `.meta`:
  - `DefaultPlaybooks.Concepts.cs`
  - `DefenseAI.Rules.cs`
  - `SimAgent.Defense.cs`
  - `SimTuning.cs`
  - `CoverageAudit.cs`
  - `CoverageRulesTests.cs`
  - `PlayDataV100Tests.cs`
- Re-run `SimCalibrationTests` after all tracks land. Every tuning knob is in `Sim/SimTuning.cs`.
- Two QB-stance tests failed headless. They're the QB track's: `FieldGoalKickerStartsLeftRearOfTheHolder`, `UnderCenterDropTurnsTheBody...`. Confirm they're fixed.
- One-line UI hook still to add: show `Play.Note` on the play-call card.
- Its own EditMode run was unfinished. Results: `D:\PIXFootball\Work\Defense\logs\editmode_results.xml`.
- Fix pass done. All five of its own failures now pass (`fix_results.xml`); see the end of `defense.md`.
  - Changes in the fix pass: the PlayCaller goal-line and sideline weights were raised, the seven-step sack bound raised from 16% to 18%, and the blocking test sample enlarged.
- The 5 `PhotoPlayerTests` failures are real: the avatar, LOD and blend-shape checks fail on the current photoreal prefabs. Handed to BODIES. A possible cause is the perf commit's GPU skinning change.
