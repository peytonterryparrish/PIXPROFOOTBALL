# Master Prompt gap analysis for v0.12 (2026-10-24)

Source: `Docs/Reference/MasterPrompt/Unity_American_Football_Master_Prompt.txt` (25 modules). Evidence: `Docs/QA/v0100/STATUS.md`, `Docs/QA/v0110/{motion,qb,rb,simflow,critical,animlib,audio}.md`, `v0110/BUGS.md`, `v0110/INTEGRATION_NOTES.md`, and a code grep of `Assets/_Project/Scripts`. Read-only: nothing was run for this file, so every number is quoted from those logs. Tools: `EngineAudit`, `MatchWatchAudit`, `LegProbe`, `ChoreoAudit`, `BodyProbe`, `QbChoreoAudit`, `CoverageAudit`, `CarryAudit`, `SimFlowAudit`, `FormProbe`.

## 1. Module by module

**M1 Contract and first audit.** Audit before changing, writer-ownership map, baseline scene with 4 cameras, facts vs hypotheses. **PARTIAL.** 120-row `BUGS.md`, `MatchWatchAudit`, per-track audits, fixed seeds. Missing: a bone/root writer map (the `SimAgentView.LateUpdate` pass order is prose in `motion.md` section 8 and the track file-ownership table) and a four-camera baseline protocol.

**M2 Field coordinates and rule profiles.** One conversion API in meters, hashes 18.5 ft / 40 ft, `CompetitionRules` keyed league/season/version with formation, motion, eligibility and catch rules. **PARTIAL.** `FieldGeometry` + `Hash` + `RuleSet.Pro()/College()` (hash width, clock, overtime, kickoff spots, penalty yards). Sim is in yards, play-local X right / Y downfield. No season/version key; formation, motion and catch rules are not in the profile; eligibility is the role flag `IsReceiverEligible`.

**M3 Mocap inventory and calibration.** Clip manifest (provenance, rate, root, contacts, ball events, mirror policy), per-rig calibration (leg/arm lengths, sole frames, grip sockets), coverage report. **PARTIAL.** `PixClipTags` (2,628 clips), `ClipTimingTable`, `ClipFacingTable`, `ContactTable` (paired takes), `Docs/AnimationCoverage.md`, mirrored twins, `ClipQa`. Missing: provenance, release/handoff markers as data, a mirror-safe flag, any `RigCalibration` beyond 3 leg-length sizes (`LocoCycleLibrary`).

**M4 Architecture and frame ownership.** Intent / motor / animation / pose-correction split, one authority per transform, explicit ownership modes, no solver competing with Final IK. **PARTIAL.** Sim-authoritative motor and a view that plays (`PlaySimulation` 60 Hz, `MotionMatcher`, choreographer booking, 0.30 m hips leash). Missing: ownership-mode enum, duplicate-writer validator, a test that Final IK (hands only; Grounder tested and left off) does not fight `FootPlant`/`StanceIK`/`LimbIK`.

**M5 Foot contacts and stable skeleton.** Swing/Acquire/Planted/ToeOff/Release with hysteresis, anchor in the surface frame, reach limits, pelvis correction, drift 2-4 cm, cuts need braking and a plant. **PARTIAL.** `FootPlant` (anchor, stride warp, pelvis drop, two-bone IK), `MotionDb` contact tags, `LegProbe`/`FormProbe`/`EngineAudit`. Run/sprint planted slide 0.03-0.06 m/s, both-feet-air 0.12 run / 0.28 sprint. Open: jog/shuffle slide 0.17-0.22 (target 0.10), engaged OL/DL and wrap-tackle slide (~1.6 m/s mean), hips jumps over 10 cm in the 36-play audit (DL 149 / OL 125), no plant on start/stop/cut (`motion.md` 2, 4).

**M6 Stances and movement vocabulary.** Stance families with seeded variation and exits; transitions between accel, stop, pivot, landing, stumble, recovery. **PARTIAL.** Stances are DONE: `StanceKind` (3/4-point, 2-point line, press/off CB, deep S, LB, returner, kicker), `REFERENCE_Stances.md`, QB compliance 96.8 %, stance snapshots in `MatchWatchAudit`. Vocabulary is PARTIAL: explicit start/stop/cut takes built, measured no better, shipped off; trunk lean grows 0.4 deg per m/s2 (real starts 30-40 deg); `StumbleTime` is only a speed cut.

**M7 Formation and pre-snap.** Authoring tool with computed legality, 7-on-line counting, eligibility from position, shifts need a 1 s set, motion as a path, hash variants. **PARTIAL.** 22 offensive formations / 619 plays, `Playbook` validator (7 on the line), `Hash`, one man in motion (`CanMotion`/`SetMotion`), huddle and cadence. Penalties are dice (`TryPreSnapPenalty`), not movement events; no shift/reset rule; no editor view of line count or covered receivers.

**M8 Center and OL.** Center snap with grip and between-legs delivery, exchange vs gun snap, varied snap accuracy, pass protection as a group, block types, emergent pad level. **PARTIAL.** Center on the ball (hips 0.63 m), snap-to-hands -2 frames, `BallState.Snap`, rare bad snap by awareness, `BlockKind` Pull/Chip/PassPro, combos with a climbing man (`BlockEngagement`). Open: engaged slide, sunk bodies in blocks (BUGS, SIM-FLOW), no hand-fight IK, no snap-height-driven QB recovery.

**M9 RB / FB / TE.** Track-specific first steps, no omniscient lane choice, ball-side security, cuts with inertia, pass protection, FB lead, TE chip. **PARTIAL (RB done).** `CarryAudit`: ball high-and-tight 0.969, outside arm 0.88, free-arm swing correlation 0.44, announced cuts, 0.45 moves per carry; carry planted slide unchanged 0.24 m/s (matcher). FB lead and TE chip exist as assignments (`BlockKind.Chip`), not audited individually.

**M10 Wide receivers.** Release techniques vs press, routes as intent and landmarks, break deceleration, head tracking, catch contexts, receiver blocking. **PARTIAL.** `RouteKind` set, `Jam` events, receiver and covering DB turn to the ball 0.10 s after release (`SimAgentView.QbTrack`), ball to hand 0.06 m at catch. No named release moves or press hand fight; breaks inherit the soft cut.

**M11 DL and LB.** Technique landmarks, gap/stunt/contain, rush moves, LB keys and read steps, pursuit with uncertainty. **PARTIAL.** `DefenseAlignment`, `DefenseAI.Rules` (zone/man/spy/blitz gaps), `CoverageAudit` 0.094 %, 101 defensive plays. Linebackers respond to true state with rating-based delays; no key-based false steps; no named rush moves.

**M12 DBs.** Press, backpedal, shuffle, hip transitions as a quality gate, man/zone/match, ball play only after a cue. **PARTIAL.** Hip-flip planner (CHOREO), quarters pattern-match, back-pedal lean +8.4 deg, turn-and-run for backward runners. Shuffle is a "side-step jog" (source limit); a turn costs speed but not vision; no look-for-ball cue model.

**M13 QB stance, snap, exchange.** Interruptible graph, under-center pocket, gun stance, cadence events, ball custody, grip frame. **DONE except defense-cadence interaction.** `QbSeq` walk/survey/crouch/cadence/snap/0.2 s secure; stance compliance 13.7 -> 76.9 % (later 96.8 %); hips pops 20 -> 0. No silent count; kneel/spike unverified.

**M14 QB drops and pocket.** 1/3/5/7-step families, ready platform, hitch, climb/slide, rollout, sack/brace. **PARTIAL (mostly done).** Drops land 3/4/6 steps, chest north 3 deg, pocket evasion, `PocketPressure`. Open: scissored feet on some under-center throws, stumble recovery, play-action fake reach, boot slide 0.24 m/s, no brace/tuck family.

**M15 QB vision and reads.** Concept reads, pre-snap hypothesis, confidence, look-off, decision clock, debug trace. **PARTIAL.** `ReadProgression`/`ReadIdx`, hold time by drop and awareness, delayed pressure sense, `DefenderRead` by awareness, head on read 2.2 deg. No shell hypothesis or memory, no look-off, no decision trace.

**M16 QB throw and release.** Whole-body throw families, authoritative release, ballistic solve, spiral, swept collision. **PARTIAL (mostly done).** `QbThrow` hips-then-shoulders, ball to hand 3.5 cm at release, front-foot yaw error median 3.0 deg, launch curve fitted to NFL flight times, wobble by pressure. Open: scissored feet on 60-100 deg targets, high follow-through hand, no sidearm/falling/jump families, no tunneling proof at max speed.

**M17 Handoff / PA / RPO / option.** Handoff corridor with single owner, PA shares early cues, RPO deadline, read-option key defender, pitch as free ball. **PARTIAL.** `QbAction.Handoff/Toss/ReadOption/PlayActionFake`, `OptionReadGive` (0.58 s ride), `BallState.Pitch`. Hands to belly 0.24 m under center (0.15 gun); PA fake reach open. **RPO MISSING** (no declared conflict defender, no ineligible-downfield rule).

**M18 Special teams.** Own formations, long snap, hold, kick sequence, punter drop, returner decisions, gunner/jammer. **PARTIAL.** `SpecialTeamsAI` (fair catch, muff, onside), `KickGeometry`, kick sync (foot to ball 0.6 cm). No long-snap/holder clips (grep), no gunner/jammer release mechanics, no 2026 kickoff setup.

**M19 Blocking, tackling, catching, contact.** Interaction contracts, phases, tackle by approach, partial ragdoll, recovery, ball state machine. **PARTIAL.** Choreographer bookings, `ContactTable`, tackle variants, blow-ups 4.2/game, assisted tackles 35 %, ragdoll, `BallState` Held/Snap/Pass/Pitch/Kick/Loose/Dead. Jump/GetUp/Stumble not sim-owned; no unique event ids; no HandoffTransfer state.

**M20 Team intelligence and match flow.** Perceived vs true state, reaction distributions, communication, causal penalties, fatigue, realism config, animation LOD. **PARTIAL.** `SimFlowAudit` (0 teleports per play, huddle, 857 substitutions/game, timeouts), `FatigueTracker`, abilities, culling fixed (`AlwaysAnimate`). No perception layer (`DefenseAI` reads true positions), dice penalties, no realism-config panel.

**M21 Data contracts.** `RigCalibration`, `StanceProfile`, `MotionClipMetadata`, `PlayerIntent`, `BallEvent` with ids, idempotent release. **PARTIAL.** Equivalents: `StanceSpec`, `PixClipTags`/`MotionDb`, `SimEvent` log, `Assignment`. No ids/ticks on ball events, no `PlayerIntent` record.

**M22 Debug and acceptance.** Diagnostic scene with overlays, validators, zero-duplicate-event checks, 30/60/120 fps, position/QB/specialist suites. **PARTIAL.** Strong offline audits, ~1,300 EditMode tests, fixed-seed 60 Hz captures. No runtime overlay (no gizmos), no 30/120 fps run, missing suite cases: center vs shaded nose, guard pull, sack before release, RPO, specialist suite.

**M23 Play timelines.** Success, recoverable disruption, failure per example. **PARTIAL.** Sim produces disruptions (sack, pressure, bad snap) but no replayable timeline scripts exposing observations.

**M24 Phased delivery.** Gated phases, per-phase measured deliverables. **PARTIAL.** Tracks follow it in spirit (numbers in each log), not gated.

**M25 Sources.** Reference only; N/A.

## 2. v0.12 candidates (ranked by what the broadcast camera shows)

1. **Contact-foot anchors in engagements** (`EngageStepper` + `FootPlant`). Blocks, wrap tackles, DL/OL get-off still slide 0.7-1.6 m/s. Track CHOREO/SIM-FLOW + MOTION. M. Done = planted-interval drift median <4 cm, p95 <8 cm in `ChoreoAudit`/`EngineAudit`; hips jumps over 10 cm = 0 (M5/M22).
2. **Start / stop / cut mechanics**: lean from acceleration (30-40 deg start), lean back on stops, plant-and-cut hip drop, gather steps for tight turns. MOTION. M. Done = trunk-lean vs acceleration in `FormProbe`; cut audit shows a plant before the direction change.
3. **Sprint and jog source gap** (sprint warped +24 %, jog/shuffle slide 0.17-0.22, run flight 0.25-0.28). ANIMLIB/MOTION. L (needs the pending Mobility Pro / Tomasi / Cascadeur purchases). Done = both-feet-air <=0.20 at sprint, 3 m/s slide <=0.10.
4. **Trench hands and leverage** via `PixIk.Hand`: punch on pads, combination hand-off between blockers, pull-and-gather. BODIES + CHOREO. L. Done = M22 position cases (guard pull, tackle vertical set, combo vs scraping LB) with no sunk hips and no hand through pads in `MatchWatchAudit`.
5. **Pre-snap legality from alignment**: computed eligibility, 7-on-line, 1 s set before a shift, one motioner, reset; penalties from events not dice. SIM-FLOW. M. Done = M22 rule tests (covered eligible, simultaneous shifts, motion toward the line) as EditMode tests.
6. **Handoff, mesh and play-action integrity**: hands to belly <0.10 m, single owner at transfer, PA fake reach, read-option key defender. QB + RB. M. Done = `QbChoreoAudit` ball-to-hand <5 cm at transfer, zero duplicate possession events, PA reacquire before throw.
7. **Defender perception and false steps**: observation delay, play-action bite by cue, LB keys, scrape recovery. DEFENSE. M. Done = M22 "LB play-action misread", "safety rotation", "edge contain vs boot" scenarios in `CoverageAudit`, with lost defenders still <=0.1 per 100.
8. **WR press release and break**: named releases vs jam duel with hands, break deceleration, stem lean. CHOREO + MOTION. L. Done = M22 "WR press release with disrupted timing"; break decel visible in cut strips.
9. **QB gaze and progression visible**: head follows `ReadIdx`, look-off, anticipation throw, decision trace. QB. S-M. Done = gaze-to-read-target angle p90 <15 deg in `QbChoreoAudit`; one trace line per decision.
10. **Throw families and footwork**: fix scissored feet on 60-100 deg targets, hitch/reset, sidearm and off-platform families, brace/tuck. QB. M. Done = scissored events 0, front-foot yaw error p90 <10 deg, M22 QB cases (cross-field reset, hitch and throw, sack before release).
11. **Special-teams operations**: long snap, holder placement, punt drop, gunner/jammer releases, 2026 kickoff. SIM-FLOW + ANIMLIB. M-L. Done = M22 specialist suite (bad snap, fair catch, muff, double jammer).
12. **Tackle finish and recovery sim-owned**: GetUp, Jump, stumble by momentum direction. CHOREO. M. Done = `ChoreoAudit` slide/float for GetUp and Jump at 0; stumble variants by direction.
13. **Ball custody event ids**: `BallEvent` ids, HandoffTransfer state, idempotent release. SIM-FLOW/QB. S. Done = zero duplicate possession/release events over 1,000 recorded plays and the 4,000-play calibration.
14. **Diagnostic overlay and writer validator**: runtime anchors/ownership/perception overlay, duplicate-root-writer check, 30/60/120 fps run. WATCH+HUD + MOTION. M. Done = M22 zero hidden simultaneous writers; slide metrics within 10 % across frame rates.
15. **Versioned `CompetitionRules` and clip metadata** (formation, motion, catch, kickoff; release markers, mirror-safe flag). SIM-FLOW + ANIMLIB. M. Done = both profiles pass the same rule tests; `ClipQa` reports a mirror policy for every live clip.

## 3. Conflicts with decisions already made (owner to decide)

1. **Outcome-first, booked contacts** (v0.10 note 6; the choreographer books tackles and catches 0.5-1.2 s ahead) vs M19 ("first contact not a guaranteed scripted outcome", interruptible pairs) and M16 (no steering after release; `-pixSteerCatchers` exists, off). Keep booking and add interruption rules, or loosen?
2. **Dice pre-snap penalties** vs M7/M13/M20 causal movement events. Keep dice for frequency, or require causes?
3. **Sim-authoritative locomotion with matcher and stride warp** vs M5 ("do not stretch the leg", shorten the step) and M3 (define MM feature vector, horizon and cost first). Sprint plays at warp 1.13 x rate 1.10.
4. **Own `FootPlant`/`StanceIK`/`LimbIK` plus the Final IK bridge** vs M4 (one solver per chain, ordering tested). Who owns legs, who owns hands?
5. **Watch mode, CPU vs CPU, no controller** vs the brief's responsiveness and user-intent items (M1, M13-16). User control stays behind `WatchMode.UserControl`; input-latency items deferred.
6. **Compressed dead ball and short-quarter clock** (`ClockScale`; 16 s between plays vs NFL 35-40 s) vs M20 realistic flow. Owner has also said the game is "a touch too fast".
7. **Yards in the sim, meters in the brief** (M2). Convert at the view only, or migrate?
8. **Rule profiles**: the brief wants verified NFL 2026 and NCAA; the game uses "Pro"/"College", fictional leagues, no NFL marks, and college mode is hidden in v0.11 (the "College rules" option stays). The 2026 kickoff cannot be verified from here.
9. **Process**: the brief gates phases on resolved rig/ownership; the owner cost rule (2-3 h passes, no follow-ups except S1) favors independent slices (candidates 1, 2, 5, 13).
10. **Pending purchases** (Cascadeur, Tomasi $15.50, Mobility Pro $50) gate candidates 3, 8, 11; the brief assumes existing mocap only.

## 4. Owner decisions (2026-10-23: "go with your recommendation")

1. **Contacts:** keep outcome-first booking, and add interruption rules (e.g. a second defender arrives, or the carrier breaks the angle).
2. **Penalties:** pre-snap and in-play penalties come from causal events (alignment, movement, contact), tuned to realistic frequencies. No dice.
3. **Stride warp:** keep it for now. Replace it with real sprint and jog source mocap when the packs are bought (Mobility Pro, Tomasi).
4. **IK ownership:** our FootPlant/StanceIK owns the legs; Final IK (`PixIk.Hand`) owns the hands. One solver per chain, with the order tested.
5. **Dead-ball time:** make it a setting, default about 25 s between snaps (it was 16 s). This also answers "game a touch too fast".
6. **Rules:** keep the "Pro" and "College" rule sets modelled on NFL 2026 and NCAA. No real league names or marks.
7. **Watch mode:** the controller-responsiveness items are deferred until controller input returns.
8. **v0.12 order:** start with candidates 1, 2 and 5 (no purchases needed), at most 4 tracks at a time under the cost workflow. Candidates 3, 8 and 11 wait for the mocap packs.
9. **Purchases arrived (2026-10-23):** MoCap Online Mobility 27B Pro and Crowd Stadium 01 are at `D:\PIXFootball\Source\MocapOnline\`; import them under `Assets/_Licensed/MocapOnline`, which is git-ignored. Candidate 3 (sprint/jog source, stops, turns) moves into the first v0.12 wave, alongside 1, 2 and 5.
