# v0.12.0 CONTACT track (`contact.md`)

Scope (TRACKS.md, CONTACT row): contact-foot anchors while engaged (OL/DL blocks incl. get-off, wrap tackles, engaged pairs), the pre-snap walk-in, the three view-speed guards in `MatchWatchAudit`.
Private copy `D:\PIXFootball\Work\Choreo\PIX`, synced from the shared tree (other tracks' in-flight work included). Nothing committed.
**Switch for the whole track: `-pixV011Contact`** (v0.11 stepper, caps, walk-in, no pad hands, no foot re-apply, no wrap steps). Every "before" below is the same build with it.

**Which motion database each number came from** (coordinator note: the shared `PixMotionDb.asset` holds MOBILITY's v12 content since 10:30 on 10-24):
* ChoreoAudit engaged / block / tackle numbers (section 1) and the first walk-in table: **old database** (09:45-09:52), except where marked "k_" (re-run 12:5x, new database; the engaged numbers did not change).
* MatchWatchAudit before/after pair (section 4) and the walk-in re-measure (section 2, second table): **new v12 database** (both runs after 10:30; `contact_before` 10:50-11:07, `contact` 13:30-14:00).
* The first "after" MatchWatchAudit (10:19-10:50) ran on the old database and was discarded (it was also taken before three later fixes).

## 1. Contact feet (ChoreoAudit, 40 real plays; `D:\PIXFootball\anim_choreo\v0120\choreo_audit_{g_before,g_after,g_nopad,k_before,k_after}.txt`)

New metric (v0.11 "contact min slide" is the slowest frame of a contact; engaged median read 0.00 m/s and hid the skating): **planted-interval drift** = for every interval in which a foot is on the turf (toe or ankle low, same hysteresis as the other audits, at least 3 frames) in an Engaged / Shed / Block / Tackle* / Tackled action: the largest horizontal distance of the toe (and separately the ankle) from where the interval started. Live = before the whistle.

| LIVE engaged / blocking / tackle | before (v0.11) | after | goal |
|---|---|---|---|
| planted-interval toe drift, median | 13.9 cm | **4.5-4.7 cm** | < 4 cm (**not met**, close) |
| planted-interval toe drift, p95 | 134 cm | **30-35 cm** | < 8 cm (**not met**) |
| planted-interval ankle drift (block), median / p95 | 10.3 / 136 cm | **0.1-0.9 / 24-27 cm** | |
| planted-foot slide per frame, all classes, mean / p95 | 1.90 / 5.46 m/s | **0.28-0.31 / 1.1 m/s** | |
| same, Block live, median / mean | 0.46 / 1.58 m/s | **0.00 / 0.26-0.28 m/s** | |
| same, tackle live (wrap, gang, carrier), median / mean | 2.49-2.64 / 4.07-4.13 m/s | **0.55-0.00 / 2.3-2.6 m/s** | (v0.11 log: wrap mean 1.6-2.0) |
| tackle toe drift live, median / p95 | 36-44 / 135-139 cm | **15 / 87-93 cm** | |
| intervals where the stepper had the legs the whole time: share / median / p95 | 72 % / 10.5 / 62 cm | **95 % / 4.3 / 24 cm** | |
| engaged hand to the opponent's chest bone, nearer hand, median | 0.68 m | **0.33 m** | |
| engaged frames with a hand within 0.40 m of that bone | 11.7 % | **79.3 %** | |
| pinned ankle in the final pose against the stepper target, p95, Final IK present, pad hands on / off | | 6.0-7.2 / 6.8-8.1 cm (0 after the pelvis fix: `h_after`) | Final IK does not write the legs |

**Goal 1 is partly met**: median 4.5 cm (goal 4), p95 30 cm (goal 8). What is left: about 5 % of the intervals are bodies the stepper does not take (above the 5.5 m/s cap, tackler run-ins), about 5 % are hand-fight clips whose own feet travel up to 1 m (the stepper follows them with a step), and wraps (tackle p95 87-93 cm).

### What was wrong (found by measuring, in this order)
1. **The stepper's swing counted as planted.** A stepping foot was lifted 6 cm over the animated ankle, the toe stayed under the 7 cm line for most of the swing: every step read as a 20-30 cm slide (stepper on: median 10.5 cm, p95 62 cm). The foot now goes up 12 cm first, travels in the middle half, comes down.
2. **A heel-up foot with the toe on the grass counted as "in the air"** (ankle above 22 cm): it was re-anchored to the animation every frame and the toe dragged with the clip. Biggest single item (median 6.4 -> 4.6 cm, p95 86 -> 53 cm). "In the air" is now the lowest sole point (ankle or toe against FootPlant's flat-foot rest heights) more than 10 cm over the turf.
3. **Wrong cap.** The stepper only had bodies pushed slower than 2.6 m/s; engaged bodies move at 2-6 m/s (bull rush, downfield blocks, wraps) and above the cap nothing planted the feet (median drift 40-100 cm). It now steps faster the faster the body moves (swing 0.20 -> 0.12 s, stride ahead grows with speed, hold 0.12 -> 0.03 s) up to 5.5 m/s, wraps included.
4. **Feet turned on their ankles** while the hand fight turned the body (toe 5-10 cm with the ankle pinned): a planted foot keeps the heading it was set down with.
5. **The leg could not reach the pinned foot** (reach error p95 8.5 cm): the pelvis is eased toward the pinned ankles by what the legs miss (<= 12 cm, 2 cm per frame); reach error after the re-apply 0 at p95.

### What changed
* `EngageStepper` (anchor/swing design kept): step trigger = pelvis ground projection further than 22 cm from the segment between the anchored feet (support area), leg stretched to 90 % of reach, or (safety) animated foot 34 cm off its anchor; one foot at a time; ramp-in 0.04 s. Planted feet are pressed onto the turf (ankle moved so the lowest sole point touches; the lower foot up to 35 cm, the other up to 12 cm; a foot the clip holds higher is the clip's step). The old behaviour stays for the get-off and stumble windows (they are not contacts) and under `-pixV011Contact`.
* `Reapply()` / `SimAgentView.EngageLegsRestore()` (one-line hook in `SimAgentView.LateUpdate` after `PixIk.Late`): after the hips leash and after Final IK the ankles go back on the stepper targets, feet in their world rotation. **One solver per chain**: Final IK reaches with the hands and moves spine/pelvis, our code owns the legs; the order is fixed by that call (EditMode test + audit line "pinned ankle, final pose vs target").
* Wrap tackles: tacklers (`TackleWrapUp`, `TackleGang`) and the carrier (`Tackled`) feet go through the same stepper.
* Hands (`PlayStage.Hands.cs`, new; one call from `ServiceChoreo`): while two men are engaged `PixIk.Hand` asks for the blocker's palms on the defender's numbers and the defender's hands on the blocker's shoulders (front of the other man's chest bone, 0.55 m reach gate, weight 0.85). `PixIk` stays the always-compiled door without Final IK types, so the public repo builds and plays without the asset (no `PIX_FINALIK` define was needed; `PixIk.Available` is false without it and `ServicePadHands` returns at once). 159k hand requests in 40 plays.
* `SimAgentView.ActionGround.cs`: optional per-frame limit on the ground-fit offset (`-pixSlew`); **off by default**: it cut OL hips jumps a little but the shipped runs did not need it (see 4).
* Tried and left off: giving the legs of a moving engaged body to the locomotion matcher (`ChoreoLocoLegs`, `-pixEngageLoco <m/s>`): with the upper-body mask at full rig weight the matcher legs do not reach the pose (drift 40 cm against 6 cm for the stepper).

## 2. Pre-snap walk-in (`PlayStage.Connect.cs`, `GlideStep`)

Probe in `ChoreoAudit` (`-pixChoreoAuditWalkIn n`, `ChoreoAudit.WalkIn.cs`): n plays whose bodies are carried through the real huddle / break / `GlideStep` flow; a body counts as walking in when the simulation has him in Stance / Idle / Block under 1.2 m/s and the view moves him at 0.35 m/s or more. 14 plays, 160-220k body-frames.

| walk-in | v0.11 pace, old DB | v0.12 pace, old DB (min pace 1.15) | v0.11 pace, **new DB** | v0.12 pace, **new DB** (min pace 3.0) |
|---|---|---|---|---|
| view speed median / p95 / max (m/s) | 3.04 / 5.40 / 5.40 | 2.40 / **3.80** / 3.80 | 3.04 / 5.40 / 5.40 | 3.00 / **3.80** / 3.80 |
| planted slide per contact (slowest frame), median / p95 (m/s) | 0.004 / 0.164 | **0.003** / 0.125 | 0.172 / 1.37 | 0.280 / 0.81 |
| planted-interval drift median / p95 (cm) | 7.5 / 27.6 | 3.4 / 22.8 | 5.9 / 40 | 5.7 / 44 |
| huddle break to last man on the line (median s) | 12.8 | 15.3 | | 15.2 |

Goal 2: glide speed p95 5.4 -> 3.8 m/s **met**. Planted slide <= 0.10 m/s median: **met on the old database (0.003), not met on the shipped v12 database (0.17 at the v0.11 pace, 0.28 at the new pace).** On the v12 database the slide by view speed (v0.11-pace run, per contact median, m/s): 0.35-1.2 -> 0.36, 1.2-2 -> 0.42, 2-3 -> 0.18, 3-4 -> 0.15, 4+ -> 0.12. The walk and slow jog takes slide; a fixed pace of 3.0-3.8 m/s still spends its start/stop in the slow band, so the median is worse than the v0.11 pace that crossed the slow band quickly and spent its time at 4-5 m/s. `PlayStage.WalkInMinPace` (3.0 now, with that comment) is the knob: when MOBILITY's walk takes plant, set it to about 1.4 and the pace follows the time left (distance over time, 1.4-3.8 m/s).

The v0.11 walk-in already planted its feet on the old database; what was wrong was the pace (a fixed 3.8-5.2 m/s jog from 6 m+, 7 m/s^2 starts) and a 0.55 m/s final crawl. Now `WalkInPace(d, left, decel)`: distance over the time left, clamped to [`WalkInMinPace`, 3.8 m/s], slowed with `sqrt(2 * decel * d)` so the body stops on its spot; starts at 3.2 m/s^2, stops at 3.75 m/s^2. The budget starts at the huddle break: `WalkInSeconds` 5.0 s (after the huddle break or a walk of more than 8 m), `WalkInShiftSeconds` 2.2 s (shift / audible / motion), `WalkInHurrySeconds` 2.5 s; the huddle gather jogs at most 3.8 m/s (was 5.4). `PlayStage.WalkInBudgetOverride` (seconds, 0 = defaults) is the hook for the play clock (Requests). `-pixOldWalkIn` (implied by `-pixV011Contact`) restores the old pace.

## 3. MatchWatchAudit guards (`MatchWatchAudit.Checks.cs`)

`SYNC_VIEW_FAR_FROM_SIM_1M`, `FACE_BODY_70DEG_OFF_SIM_FACING_STANDING_<group>`, `OVERLAP_BODIES_STANDING_OR_BLOCKING` now need the view still (`VT.ViewSpeed` < 0.35 m/s; for overlaps both bodies). The old rules remain as `..._V011_RULE_{STILL,MOVING}` (SYNC, OVERLAP) and `..._V011_RULE_<group>_{STILL,MOVING}` (FACE) for like-for-like counts.

## 4. MatchWatchAudit confirming run (40 plays, seed 110110, Buffalo v Miami, `...\match_watch_v0120\contact` against `...\contact_before` = `-pixV011Contact`, both on the v12 database)

| | before | after |
|---|---|---|
| hips jumps > 10 cm in one frame, OL / DL / TE / all bodies (frames) | 428 / 334 / 142 / 1,112 | 389 / 328 / 170 / 1,093 |
| both soles > 4 cm up, standing bodies, Block / Engaged (float_probe) | 4.5 % / 7.7 % | **7.5 % / 18.3 %** (worse) |
| FEET_BELOW_TURF_7CM (events, all groups) | 35 | **294** (worse; samples are Block / Engaged) |
| SUNK_HIPS_BELOW_0.35M | 55 | 29 |
| SYNC_VIEW_FAR_FROM_SIM_1M (view still) / v0.11 rule moving | 594 / 1,051 | 767 / 1,032 |
| FACE_BODY_70DEG_OFF_SIM_FACING_STANDING (view still, all groups) | 341 | 438 |
| OVERLAP_BODIES_STANDING_OR_BLOCKING (both still) / v0.11 rule moving | 6 / 474 | 10 / 473 |
| POP_HIPS_ROT_OVER_900DEGS (events) | 278 | 216 |

The guards do what they should (baseline: 4,700 / 4,837 / 2,068 over 184 plays, i.e. about 1,020 / 1,050 / 450 per 40 plays; the guarded OVERLAP count is 6-10, the guarded FACE count a third of the old rule; the guarded SYNC count is bodies that stand still 1 m or more from the sim, a real finding, not the walk-in). The OL/DL hips-jump goal ("clearly down") is **not met** (-9 % OL, -2 % DL). **Both-soles-up and FEET_BELOW_TURF in blocks are worse**: ablations in ChoreoAudit (mesh soles, still Block / Engaged frames, 40 plays) put it on the new stepper, not on the slew limit or the Reapply: stepper off 128 both-up / 1,429 under-turf; v0.11 295 / 710; new stepper without press 1,037 / 739; with press 670 / 966; with the pelvis fix 592 / 1,011. The press uses FootPlant's flat-foot rest heights, which do not match the drawn sole of a pitched foot (up to 10 cm under, 10-15 cm up in the `BlockHitRStale` / `BlockHitCStale` clips). This is the first thing to fix in v0.13 (see 7).

## 5. Tests
One EditMode run (`-testCategory "!Slow"`): **1373 of 1373 passed** (`D:/PIXFootball/Work/Choreo/logs/tests6.xml`, final code; an earlier run on intermediate code had 3 failures in other tracks' files: `GaitLeanTests`, `MotionMatchingTests.MobilitySpecsAreWellFormed`, a sim RNG test). New `ContactV012Tests` (5): ankle back on its anchor after the pelvis moved (Final IK order), no stretched legs, pad hands on the front of the pads, walk-in pace follows the time and never exceeds a jog, stops on its spot.

## 6. Files
New: `Gameplay/Match/PlayStage.Hands.cs`, `Editor/Tools/ChoreoAudit.WalkIn.cs`, `Tests/EditMode/Gameplay/ContactV012Tests.cs` (+ `.meta`).
Edited (owned): `Gameplay/Player/EngageStepper.cs`, `SimAgentView.Choreo.cs`, `SimAgentView.ActionGround.cs`, `Gameplay/Match/PlayStage.Connect.cs`, `PlayStage.Choreo.cs` (one line), `Editor/Tools/ChoreoAudit.cs`, `MatchWatchAudit.Checks.cs`.
One-line hooks elsewhere: `SimAgentView.cs` (`EngageLegsRestore()` after `PixIk.Late`), `SimAgentView.Loco.cs` (GAIT: `DriveMotion` `owned` uses `ChoreoLocoLegs`, default off). `PlayStage.Between.cs` (RULES) untouched.
Switches: `-pixV011Contact`, `-pixOldWalkIn`, `-pixNoPadHands`, `-pixNoPress`, `-pixNoPelvisDrop`, `-pixNoReapply`, `-pixSlew`, `-pixStepCap <m/s>`, `-pixStepCapWrap <m/s>`, `-pixEngageLoco <m/s>`; audit `-pixChoreoAuditWalkIn <n>`.

## 7. What still looks wrong / known issues for v0.13
* **Feet through the turf / floating in blocks (section 4)**: replace the rest-height press by the drawn sole (a baked-mesh sole probe on the first frames, or per-avatar sole offsets by foot pitch), or press only when the audit's sole proxy agrees; verify with `still Block/Engaged ... visible sole` in `ChoreoAudit` (3 minutes) before a MatchWatchAudit.
* p95 of the planted-interval drift is 30 cm, not 8 cm (bodies above the 5.5 m/s cap, tackler run-ins, hand-fight clips whose feet travel up to 1 m). Directions: a real stepping gait for engaged bodies from the matcher with its own lower-body layer (GAIT / MOBILITY), wrap pair-take root motion scaled to the sim path.
* The chase stepping at 3-5.5 m/s (downfield blocks, bull rush) is a cadence of quick 12 cm steps; it removes the skating but wants the owner's eye on a broadcast shot. If it looks like a treadmill: `-pixStepCap 3.6`.
* Walk-in slide on the v12 database (section 2) belongs to the walk / slow-jog takes (MOBILITY).
* `BodySanity` (thigh roll limit 80 degrees, execution order 100) runs after `SimAgentView.LateUpdate` and can move a pinned foot when the pelvis is turned far away (not measured separately).
* Pad hands use the other man's chest bone of the previous frame (3 cm at 2 m/s); no punch timing, no hand-fight combos.

## 8. Requests
* **RULES**: when the dead ball goes to ~25 s set `PlayStage.WalkInBudgetOverride` (seconds left before the snap minus the cadence) per play, or raise `PlayStage.WalkInSeconds` (5 s): the break-to-line walk costs +2.4 s at the default budget (median 12.8 -> 15.2 s begin-to-ready in the audit). `PlaceBetween` estimates the gather time with a 3.6 m/s jog (`dd / 3.6f + 0.4f`); the gather is now 2.4-3.8 m/s, late men hold the break up to 0.25 s: `/ 3.0f` there would line it up (one line in `PlayStage.Between.cs`, not done).
* **MOBILITY**: v12 walk / slow-jog takes slide 0.2-0.4 m/s per planted contact below 3 m/s (section 2); `WalkInMinPace` can drop to ~1.4 once they plant.
* **GAIT**: `HipsLeashLate` runs after the stepper and moves feet when it bites (> 0.30 m); `EngageLegsRestore` repairs it for engaged bodies only. A leash that keeps feet in world space (move the root, not the hips) would help everybody.

## 9. Follow-up (S1: floating / sinking feet in blocks) - drawn-sole press

Change (EngageStepper only): the flat-foot rest-height press is replaced by the **drawn sole**. Once per body the skinned mesh is baked (as `MatchWatchAudit.FloatProbe` does), the foot vertices (within 28 cm of the ankle, not above it) are thinned on a 3 cm grid in the foot-bone and toe-bone frames (about 100 points a foot, built at load, no stored table); the sole height is the lowest of those points in the world, so any foot pitch and any of the 10 avatars are exact. The ankle target is moved so that point is at `FootPlant.SoleGoal`; the lower foot is pressed up to 35 cm, and the support foot while the other swings. "In the air" and "lower foot" use the same sole. Switches: `-pixNoPress`, `-pixNoPelvisDrop`, `-pixPelMax`, `-pixPressUp`.

| | v0.11 | rest-height press | **drawn-sole press (shipped)** | goal |
|---|---|---|---|---|
| ChoreoAudit, still Block / Engaged, visible sole both > 4 cm up (frames) | 12 / 302 | 15 / 592 | **5 / 256** | <= 295 met |
| same, a sole > 4 cm under the turf | 6 / 717 | 27 / 1,011 | **21 / 346** | <= 710 met |
| MatchWatchAudit (40 plays, v12 DB), both soles > 4 cm up, Block / Engaged | 4.5 / 7.7 % | 7.5 / 18.3 % | **4.51 / 6.51 %** | <= 4.5 / 7.7 met |
| MatchWatchAudit FEET_BELOW_TURF_7CM (events) | 35 | 294 | **312** | < 60 **not met** |
| planted-interval drift, median / p95 | 13.7 / 135 cm | 4.8 / 34 cm | **4.7 / 37 cm** | <= 5 met |
| hips jumps >10 cm OL / DL (frames) | 428 / 334 | 389 / 328 | 386 / 324 | |

Sinking is NOT fixed. Ablations (ChoreoAudit, MatchWatchAudit's own detector = toe or ankle BONE more than 7 cm under, Block/Engaged/Tackle frames, 40 plays): v0.11 310; new stepper 2,490; without pelvis pull 1,157; without press and pelvis pull 1,206; step cap 2.6 m/s 1,804; cap 3.6 m/s 2,267; toe-bone sole points no change. So the bones go under the turf with the **base v2 stepping itself** (pinned feet in toe-off / pitched poses while the body moves; most of the frames are moving bodies, the standing-body sole audit is fine), and the pelvis pull doubles it. Not located within the budget; shipped setting = drawn-sole press + pelvis pull (best float number, slide gains kept; combined float + sink is lower than the rest-height press: still-body floating and sinking both inside the v0.11 numbers, bone-level sinking about 8x v0.11). Next step for v0.13: log per frame the foot pitch and the stepper state for the 1,200 bone-under frames of `u_nopress` (`choreo_audit_u_nopress.txt`), most likely the anchor y / pitch of a pinned moving foot; `-pixNoPelvisDrop` halves the count at a cost of ~60 % more still-body floating (410 vs 254).

Tests: targeted EditMode run (`ContactV012Tests`, `ContactTests`, `ChoreoViewTests`): 32/32 passed.
