# v0.13.0 STANCE-HEAD track (`stance.md`)

Owner (v0.12 playtest): "many players are not in the right stances", "heads look too turned", "many model, skeleton and mocap issues". Private copy `D:\PIXFootball\Work\Choreo\PIX` (`_Licensed` is the shared junction: nothing written under it). Nothing committed. Data: `D:\PIXFootball\Renders\match_watch_v0130\{baseline, stance_before, stance_after40, stance, stance_v012, skeleton}`.
`stance_before` = the v0.12 tree + this track's probes, 40 plays, seed 110110 (same plays as `stance_after40`); `stance` = the confirming FULL game with the final code (seed 110110, no play cap); `stance_v012` = the final binary with `-pixV012Stance` (restores everything this track changed) so the head numbers have a like-for-like "before" on the new measure.

## 0. Result against the done-criteria (full game, seed 110110)

Baseline = `baseline\` (v0.12 tree, 168 plays, 3-17). After = `stance\` (final code, 174 plays, 0-48, finished). Head "before" on the new measure = `stance_v012\` (same binary, `-pixV012Stance`, first 40 plays; the baseline run has no head probe).

| done-criterion | v0.12 baseline | v0.13 final | target | |
|---|---|---|---|---|
| stance compliance per position (3,102 / 3,124 scrimmage snapshots) | **OL 46.8 %** (every non-shotgun three-point failed: hand 300, lean 180), C 99.3, CB 99.0, others 100; all 90.2 % | **lowest CB 98.7 %**, OL 99.8, C 100, DL 99.8, LB 99.7, RB 99.3, QB / S / WR / TE / FB 100; all **99.7 %** (all play kinds 99.8 %) | >= 95 % each | **met** |
| head over chest, all free (non-exempt) frames, p95 / p99 / max | 42 / **60** / 179.8 deg on the new twist measure (`stance_v012`, 40 plays); 69 / 70 / 179.8 on the v0.12 yaw measure; frames over 60 deg 0.94 % (15,013 of 1.59 M) | **42 / 48 / 79.5** deg (6.74 M frames); frames over 60 deg 84 (0.001 %); contact-driven bodies max 66.6 | p95 <= 45, p99 <= 60 | **met** |
| head turn rate over chest, free frames, p95 / p99 | 120 / 260 deg/s (max 10,376: spikes at layer hand-overs) | 120 / **240** (max 4,525); limiter 380 deg/s | a rate limit | **met** (limiter), spikes remain |
| FEET_BELOW_TURF_7CM per game | **1,098** (OL 306, DL 297, WR 145, LB 107, CB 81, S 57, TE 48, FB 31, RB 26) | **49** (DL 19, LB 11, S 5, TE 5, CB 4, OL 2, RB 2, QB 1: the samples are ragdoll / fallen / get-up bodies) | <= 300 | **met** |
| floating counts, per game (per play) | BOTH_FEET_UP 246 (1.46), RUN_AIRBORNE 71 (0.42), GROUND_ACTION_STANDING_HEIGHT 82 (0.49) | 251 (1.44), 85 (0.49), 91 (0.52) | not up | **flat per play except RUN_AIRBORNE +14, all of it WR routes (34 -> 48; OL / DL 8 -> 6), not a contact body**; standing Block / Engaged frames with BOTH drawn soles > 4 cm up: 4.6 / 6.5 % -> 5.4 / 7.3 % (+0.8 pp), with a sole > 4 cm under the turf 7.7 / 8.4 % -> 1.6 / 3.7 % |
| skeleton check table | none | section 4 (15 bodies) | table in the log | **done** |
| stance snapshots set in time | n/a | 3 of 3,124 rows in the stance for < 0.7 s, 9 with the pose layer under 0.95 | | informational |

## 1. Stances: measured, root cause, fix

**Measure** (`Docs/QA/v0130/tools/stance_compliance.pl stance_snapshots.csv`): the snapshot is the last pre-snap sample of every body at the snap. A body COMPLIES when (1) his stance type is one of those allowed for his position (C: CenterSnap; OL: three-point / four-point / the shotgun two-point; DL: three / four-point / wide-9 two-point line; LB, CB, S, WR, TE, RB, FB, QB their own families), (2) the depth is in the band of the stance family (hand-down: trunk lean 55-110 deg, hips 0.55-0.95 m, head not under the hips; two-point: lean 5-42, hips 0.62-1.02, head 0.25 m over the hips; under-centre QB 15-55 / 0.5-0.95; press corner / shotgun QB 2-32 / 0.7-1.05), (3) facing within 20 deg of the simulated facing (28 for the tilted wide-9 edge), (4) hand down (lowest hand <= 0.18 m, four-point both <= 0.25), (5) lowest toe bone -0.06..+0.07 m of the turf. Run over `baseline\stance_snapshots.csv` (full game, 3,102 scrimmage snapshots):

| position | n | v0.12 baseline | failures |
|---|---|---|---|
| C | 141 | 99.3 % | hand 1 |
| OL (LT LG RG RT) | 564 | **46.8 %** | hand 300, lean 180 (every non-shotgun OL "three-point") |
| DL | 534 | 100 % | |
| LB / CB / S / WR / TE / RB / FB / QB | 343 / 392 / 282 / 390 / 151 / 138 / 26 / 141 | 99.0-100 % | CB: face 2, feet 3, lean 2 |

So the only systematic stance defect at the snap was the offensive line: 300 of 564 OL snapshots had the lowest hand at 0.66 m (hips 0.82, lean 57 deg) = a crouch with both forearms on the thighs, which no one reads as a three-point; the shotgun two-point OL (264) is the owner's own reference (images 97 / 101) and stays.

**Root cause.** The owner's v0.10 note 9 ("tackles more upright with an arm ON THE THIGH, guards lower, the centre down over the ball") was built as both forearms on the thighs and NO hand on the turf (`StancePoses.Raw`, `Ol` 1 / 2, `MaxLean` 62 / 80; the BodiesTests pinned it). A three-point has one hand down (reference image 98) and the free arm on the thigh. Short-yardage downs (`ShortYardage`) already used the real three-point; every other scrimmage down used the crouch.
**Fix** (`StancePose.cs`): tackles (`Ol` 1) hips 0.80 / lean 70 and guards (`Ol` 2) hips 0.76 / lean 80, both with ONE hand on the turf (`HandMode.Ground`) and the free forearm on the thigh (`HandMode.Thigh`), `MaxLean` 100 / 104 so the reach IK can deepen the trunk until the shoulder reaches the turf; the tackle stays the most upright of the line (hips and lean order tackle < guard < centre kept, test). Switch for the old poses: `-pixV012Stance`.
**Why the integration run had `STANCE_LINEMAN_TOO_UPRIGHT` 9 -> 47:** all 47 samples are from three plays (a field goal at the 85, `play#18 / #32 / #61`) in which the snap fired with the whole line still standing upright and 73-97 deg off the facing (lean 3-9 deg, hips 1.0, yawErr 73-97): the pre-snap "not set at the snap" bug that the RULES follow-up fixed (16 s floor, snap held until the stage has been set for 0.8 s). It is not a pose defect; the baseline (after that fix) has 0 of them, the audit rule is unchanged. New columns in `stance_snapshots.csv`: `poseW` (weight of the stance pose layer at the snap: 0.999 mean, no body below 0.95) and `secSet` (seconds in the stance before the snap).

**After** (full game, 3,124 scrimmage snapshots, compliance table in section 0): the OL three-point snapshots (1,015 of 1,259 OL / C rows; the other 244 are the shotgun two-point) read, per position, lean / hips / lowest hand: LT / RT 81 deg / 0.68 m / 0.05 m, LG / RG 85 / 0.70 / 0.05 (v0.12: LT 32 / 0.89 / 0.72, LG 57 / 0.82 / 0.66); DT / LE / RE unchanged at 85 / 0.67. Remaining failures of the whole game: 4 facing (CB 3, LB 1), 4 hips (CB 2, DL 1, RB 1), 3 CB lean, 2 hand (LT 1, DL 1): 13 of 3,124 snapshots. The tackle / guard difference is only 4 deg of lean after the reach IK (the shoulder has to reach the turf, so the authored 70 / 80 deepen to 81 / 85): the order tackle < guard < centre (86) holds, but the tackle no longer reads "more upright" than the guard by much (known issue).

## 2. Heads

**Measure.** New in `MatchWatchAudit.Stance.cs` (`head_yaw.txt` and the summary): head over chest for every drawn body and frame, with p50 / p95 / p99 / max and the head turn rate, split into the EXEMPT actions (Throw, Tackle*, Tackled, Dive, GetUp, Fallen, Slide, ragdolls) and FREE (everything else), by action, group, stance kind and contact-driven or not.
**Finding 1: the v0.12 measure was wrong for bent-over bodies.** `HeadOverChest` was the difference of the world yaws of the bind-forward axes of the chest and the head. For a three-point lineman, the centre, the long snapper and the holder those axes point at the turf and their yaw is noise: the v0.12 numbers read 70 degrees for 31.6 % of the three-point frames (CenterSnap 25 %, LongSnapper 78 %, Holder 95 %), over-all free p95 69 / p99 70, and worse, the limiter then "corrected" it by rotating the head about the WORLD vertical, i.e. it twisted a bent-over head sideways to the limit: the 70-degree plateau was partly made by the limiter. `NaturalBody.HeadTwist` is now the swing-twist of the head's rotation over the chest about the neck axis (chest to head in the chest frame): no singularity at any trunk pitch, and the limiter rotates about that axis. The old yaw measure is still printed (`v0.12 yaw measure, free`) and still selectable (`-pixV012Stance`).
**Finding 2: coverage and limits.** v0.12 limited head only for free bodies in Idle / Run / Sprint / Route / Carry / Backpedal / Shuffle / Stance / Block / Engaged / Shed / Catch / Jump: no limit on Handoff / Juke / Spin / StiffArm / Truck / Hurdle / Celebrate / Kick / Stumble and none on any contact-driven body (authored hand-fight and pair takes turn the head 80-130 deg over the chest: `free contact-driven` p95 97 / p99 132 in `stance_before`). The limit itself was 70 (soft knee 56) at 650 deg/s.
**Fix.** `NaturalBody.HeadMax` 48 (soft knee 38; v0.12 70) and `HeadRate` 380 deg/s (650), the limit applies to every action except the exempt ones (`SimAgentView.Natural.cs` `HeadLimited`), also to contact-driven bodies (head only: the take keeps its chest and arms). `NaturalLook` (NATURAL-2's file) is untouched: the constants live in `NaturalBody`. Exempt list check: stance kinds are NOT exempt (the exemption list is the five actions above; `free act Stance` is limited and measured: p95 38, p99 45 in the 40-play re-run); the only stance-adjacent exempt bodies are Throw (QB) and the ground.

(The action rows of the v0.13 column are from `stance_after40`, the same 40 plays as `stance_v012` and `stance_before`; the other rows from the full game.)

Head over chest, free frames (`head_yaw.txt`), same measure (twist about the neck axis), v0.12 behaviour (`stance_v012`, 40 plays) against the final code (`stance`, full game):

| class | v0.12: p95 / p99 / max | v0.13: p95 / p99 / max |
|---|---|---|
| all free frames | 42 / 60 / 179.8 | **42 / 48 / 79.5** |
| not contact-driven | 42 / 60 / 179.8 (0.94 % of frames over 60) | 42 / 48 / 79.5 (84 frames over 60, 0.001 %) |
| contact-driven (authored hand-fight / pair takes) | 61 / 69 / 78.2 (5.8 % over 60) | **47 / 48 / 66.6** (0.05 %) |
| act Block | 60 / 64 / 67 | 48 / 48 / 67 |
| act Engaged | 49 / 62 / 172.7 | 45 / 48 / 66.6 |
| act Handoff | 52 / 69 / 72 | 45 / 48 / 48 |
| act Catch | 58 / 74 / 78 | 48 / 74 / 78 (contact catches: not all limited, 437 frames) |
| act Idle | 32 / 51 / 179.8 | 32 / 48 / 69.6 |
| act Run | 33 / 63 / 169.1 | 32 / 48 / 70.9 |
| stance Holder (kneeling, eyes on the ball) | 67 / 67 / 68 (89 % over 60) | 48 / 48 / 48 (it looks at the ball at the limit) |
| stance LongSnapper / CenterSnap | 64 / 68 / 177 and 29 / 57 / 70 | 15 / 27 / 30 and 21 / 42 / 48 |
| stance ReceiverSplit / OlTwoPoint / QbShotgun | 46 / 50 / 140, 35 / 56 / 70, 27 / 62 / 64 | 44 / 46 / 48, 35 / 42 / 48, 26 / 48 / 48 |
| EXEMPT (throws, tackles, dives, get-ups, ground) | 51 / 71 / 80 | 51 / 74 / 81 (not limited by design) |

The v0.12 "yaw measure" read 69 / 70 for the same frames (`v0.12 yaw measure, free` in both summaries): the numbers the v0.12 NATURAL log reported (p99 68.6) were mostly the bent-over artefact. On the correct measure the v0.12 head was already inside p95 45; what the owner saw were the p99 tails: 15,013 frames over 60 deg (up to 179.8 in Idle / Run / Stance spikes, 64-67 in blocks, hand-offs 69, jukes, the holder, hand-fight takes). Those are gone (84 frames of 6.7 M).

## 3. FEET_BELOW_TURF_7CM: real sinking, not a metric artefact

**Root cause** (probe rows `tag below` in `float_probe.csv`, written at every sampled FEET_BELOW frame next to the DRAWN sole of the baked mesh): of 332 sampled sunk-foot frames in the 40-play `stance_before` run, **331 had the drawn sole 7-16 cm under the turf** (1 between 3 and 7 cm, 0 artefacts): the bone-based count measures what the viewer sees. 317 were the engage stepper at full weight, 255 had the ankle at exactly the flat 0.085 m `AnkleFloor`, 197 were Block, 110 Engaged, 15 contact-driven; the foot was pitched 45-84 degrees toes-down (flat is about 30). The clip's toe-off foot (ankle high, heel up) keeps its WORLD rotation through `LimbIK.Apply`, the stepper put the ankle on a pinned, lower point, and the ball of the foot went into the grass: toe bone -7..-10 cm, sole -11..-16 cm. v0.12's CONTACT track measured the standing-body sole (fine) and called the moving ones "not located". The skeleton check shows why a flat floor cannot be right: the ankle bone is 0.113-0.140 m above the sole by body, the toe bone 1.6-2.5 cm, 72-78 % along the sole.
**Fix** (`EngageStepper.FootGuard`, after the stepper and the pelvis correction, before the ankles are stored for `Reapply`): a foot never points further than 62 deg toes-down (rotated about its lateral axis, a pointe foot is not a stance), then the ankle is raised (<= 0.24 m, leg re-solved, foot keeps its rotation) until the toe BONE is 3 cm over the turf. Pure bone arithmetic on the solved pose, no mesh. Switch: `-pixNoPitchFloor` (and `-pixV012Stance`). The detector is NOT changed (the bone rule is right), so no `_V012_RULE` copy is needed.

| | v0.12 baseline (full game) | v0.13 (full game) |
|---|---|---|
| FEET_BELOW_TURF_7CM, all groups | 1,098 | **49** (the samples read in the issue list are ragdoll bodies: Fallen, get-up, tackled, a corner after a block; Block + Engaged: 10 of the 49 samples) |
| sampled sunk frames with the drawn sole > 7 cm under the turf (`float_probe.csv` tag below) | 331 of 332 (40 plays) | 48 of 49 (full game): the detector and the mesh agree before and after |
| standing Block / Engaged frames, a drawn sole > 4 cm under the turf (mesh) | 7.7 % / 8.4 % | **1.6 % / 3.7 %** |
| standing Block / Engaged frames, BOTH drawn soles > 4 cm up (floating) | 4.6 % / 6.5 % | 5.4 % / 7.3 % (+0.8 pp: the cost; FLOAT_STANDING_BOTH_FEET_UP 246 -> 251 over 168 -> 174 plays, OL 75 -> 73, DL 54 -> 57) |
| 40-play A/B on one binary, FEET_BELOW count (`stance_v012` with `-pixV012Stance` / `stance_before` / `stance_after40`) | 286 / 332 | 9 |

## 4. Skeleton: bone vs mesh over the 15 bodies

`BodyProbe.Skeleton.cs` (`BodySkeletonCheck`, edit mode, `-executeMethod PixFootball.EditorTools.BodySkeletonCheck.Run -pixBodyProbeOut <dir> -pixBodyProbeTag <tag>`, 2 minutes): per body, in the bind pose and four extreme poses, from the baked skinned mesh of the LOD0 parts. Output `skeleton2.txt / .csv / _poses.csv` in `...\skeleton\`.

| body | L/R length diff | ankle over sole | toe bone over sole | arm-up stretch edges | hip flex | elbow | trunk fold | worst region |
|---|---|---|---|---|---|---|---|---|
| photo_player_small | 1.1 % | 0.128 m | 0.016 m | 433 | 188 | 78 | 99 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_small_stocky | 0.7 % | 0.113 m | 0.019 m | 389 | 186 | 86 | 111 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_small_athletic | 0.8 % | 0.125 m | 0.017 m | 476 | 152 | 76 | 78 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_small_tall | 1.4 % | 0.134 m | 0.018 m | 430 | 198 | 76 | 111 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_small_runner | 1.1 % | 0.134 m | 0.018 m | 399 | 200 | 78 | 105 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_medium | 1.1 % | 0.130 m | 0.019 m | 539 | 220 | 68 | 100 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_medium_stocky | 0.7 % | 0.116 m | 0.021 m | 518 | 211 | 68 | 100 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_medium_athletic | 0.8 % | 0.127 m | 0.019 m | 624 | 176 | 78 | 90 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_medium_tall | 1.4 % | 0.136 m | 0.020 m | 584 | 224 | 88 | 97 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_medium_runner | 1.1 % | 0.136 m | 0.020 m | 574 | 226 | 82 | 106 | RightArm (sleeve + arm skin), Spine1 in the trunk fold |
| photo_player_large | 1.1 % | 0.134 m | 0.023 m | 687 | 273 | 74 | 155 | RightArm (sleeve + arm skin), Hips (seat) in the trunk fold |
| photo_player_large_stocky | 0.7 % | 0.120 m | 0.025 m | 669 | 277 | 74 | 151 | RightArm (sleeve + arm skin), Hips (seat) in the trunk fold |
| photo_player_large_athletic | 0.8 % | 0.131 m | 0.024 m | 622 | 229 | 76 | 149 | RightArm (sleeve + arm skin), Hips (seat) in the trunk fold |
| photo_player_large_tall | 1.4 % | 0.140 m | 0.024 m | 682 | 287 | 70 | 151 | RightArm (sleeve + arm skin), Hips (seat) in the trunk fold |
| photo_player_large_runner | 1.1 % | 0.140 m | 0.024 m | 689 | 304 | 66 | 155 | RightArm (sleeve + arm skin), Hips (seat) in the trunk fold |

(stretch = triangle edges of any part longer than 1.7x or shorter than 0.4x their bind length after the pose: arm overhead 150 deg, hip flexed 100 deg with the knee bent, elbow bent 120 deg, trunk folded 70 deg; asymmetry of the mirrored joint positions read 0.3-3.2 cm with the hips bone as the plane, which is the hips bone being off the mid-plane by up to 1.6 cm in the base bodies; the plane now goes through the middle of the hip joints, not re-run.)
Reading: rig symmetric (bone lengths within 1.4 %), no weight-sum errors, no body without humanoid mapping. Defects, in order:
1. **Ankle / toe bones against the sole (fixed in code):** the sole sits 11-14 cm below the ankle bone and the ball of the foot (toe bone) 1.6-2.5 cm over the sole; every constant in the code assumed a flat ankle at 8.5-9 cm (AnkleFloor 0.085, the audit's 0.09 proxy) and a toe bone at the sole. A toes-down foot therefore put the whole foot into the turf (section 3).
2. **Shoulder skin and sleeve under an overhead arm (art, requested):** with the arm raised 150 deg the right sleeve and arm skin keep 7,300 stretched edges across the 15 bodies (Large 620-690 a body, Medium 520-620, Small 390-480; region `RightArm` 1,800-2,100 edges, almost all in `SleeveR` and `Arms`), the Large body worst; that is the "Large arm skin at the shoulder" of v0.12. Tried in code: a scapulohumeral rhythm (the clavicle takes 15 / 25 / 35 deg of the 150): 25-60 % MORE stretched edges (534 / 586 / 688 against 433 on the small body), so it stays out. Needs the sleeve / pad cap weights re-done on the clavicle (new art, `Requests`).
3. Hip flexion 100 deg (three-point, sprint knee): Pants 2,200 edges (`LeftUpLeg` region, the seat), Pouch 850; centre of the clothed thigh 4-5.5 cm off the thigh bone axis, shin 3-6 cm, upper arm 6-11 cm (the pad dome above the arm axis); no code fix (weights).

## 5. Tests
One EditMode run on the final code (`-testCategory "!Slow"`, `D:/PIXFootball/Work/Choreo/logs/tests.xml`, shared tree incl. the other tracks' in-flight work at 00:00 on 10-25): **1,392 tests, 1,389 passed, 2 failed, 1 skipped.** The 2 failures are the QB-MOCAP track's in-flight work, neither touches this track's files: `QbMocapTests.EveryQuarterbackTakeNameHasAKeyInTheQbRules` (no QB rule claims `BONES_QB_run_forward_pass_001_s03`) and `QbSequenceTests.SecureStartsOnTheCrouchHandsAndEndsAtTheBelly` (`StancePoses.QbSeq.cs`, `QbSecure` hips). New `StanceHeadV013Tests` (4): every OL three-point (tackle, guard, short yardage) has one hand on the turf and the free arm on the thigh; tackle most upright, centre lowest; head limit <= 55 deg / 420 deg/s and the soft limit never passes it; the receiver stance look stays inside the limit. `BodiesTests` (OL stance, two assertions updated: tackle lean < 75, one hand down) 12 / 12. All pass.

## 6. Files
Edited (owned): `Gameplay/Player/StancePose.cs` (OL three-points), `StanceRules.cs` (`V012` switch), `EngageStepper.cs` (`FootGuard`), `Loco/NaturalBody.cs` (`HeadTwist`, `HeadMax` / `HeadRate`, limiter about the neck axis), `SimAgentView.Natural.cs` (limit coverage, `HeadLimited`, `-pixNatNoHeadAll`), `Editor/Tools/MatchWatchAudit.Checks.cs` (stance `poseW` / `secSet`, feet and head hooks), `MatchWatchAudit.FloatProbe.cs` (`extra` column), `MatchWatchAudit.cs` (one line: `StanceHeadReport`), `CriticalStances.cs` (OL hand-down expectation), `Tests/EditMode/Gameplay/BodiesTests.cs` (the two OL assertions: lean < 75, one hand down).
New: `Editor/Tools/MatchWatchAudit.Stance.cs`, `Editor/Tools/BodyProbe.Skeleton.cs`, `Tests/EditMode/Gameplay/StanceHeadV013Tests.cs` (4 tests) (+ `.meta`), `Docs/QA/v0130/tools/stance_compliance.pl`.
Hooks in other tracks' files: none (the OL two-point rule line in `StanceRules.Qb.cs` was left alone: the shotgun two-point OL is the owner's reference). Nothing written under `_Licensed`.
Switches: `-pixV012Stance` (OL poses, head limits / coverage / measure, flat ankle floor: the v0.12 behaviour on one binary), `-pixNoPitchFloor`, `-pixNatNoHeadAll`.

## 7. What still looks wrong / known issues for v0.14
Looked at: `stance\strips\snap_4_p4.png` (broadcast, 6 frames around a snap) and the side views `stance\sidestance\p2_snap_LT_a2.png`, `p4_snap_LE_a11.png`. Said plainly:
* At broadcast distance a body is about 30 px tall: the strip shows the line set, bodies in their stances and no head turned round at that scale, but it cannot show a three-point from a crouch. The side views show the OL and DL three-point correctly now (hand on the turf at the green line, back flat, head up, the other arm hanging by the side or resting forward: the free arm is not always ON the thigh, it follows the reach IK; not looked at in detail).
* The tackle is only 4 deg more upright than the guard (81 vs 85) once the reach IK has deepened the trunk to the turf; the owner's "tackles more upright" is now mostly the hips (0.68 vs 0.70), not the back angle. A more upright tackle needs the down hand placed under the shoulder with a longer arm reach (a pose / reach change, not tried).
* `FootGuard` caps a toe-off foot at 62 deg and lifts the ankle: block feet that the clips hold on the toes (60-84 deg) now stand on the ball of the foot with the heel higher than before; not looked at close up (broadcast distance cannot show it). The cost is +0.8 pp of standing Block / Engaged frames with both soles > 4 cm up.
* The head limit of 48 deg is a wall for the glances and the holder: 73 % of the holder frames sit AT the limit (he looks at the ball in the snapper's hands), 2 % of receiver / QB stance frames. A head at exactly the limit for seconds can look held; the idle glance amplitude (NATURAL-2) is not retuned.
* Head turn-rate spikes remain at layer hand-overs (free max 4,525 deg/s, QB 3,911, RB 3,071 in the 40-play run): single frames when the stance layer, the look limiter and a clip switch hand the head over; p99 is 240 deg/s. Contact-driven catches / jumps are only partly limited (`free act Catch` p99 74).
* 49 FEET_BELOW samples remain, all in ragdolls and the first frames of falls; ragdoll feet are not this track's layer.
* Skeleton: arm-overhead sleeve / skin stretch (390-690 edges a body, Large worst) and the hip-flexion seat (150-300) are weight problems of the 15 bodies and need new art; the clavicle rhythm tried in code made it worse (+23..+59 %).
* Not done: no `_V012_RULE` copy was needed because the detector was not changed; the compliance script is offline (perl on `stance_snapshots.csv`), not an audit line; the two-point OL is still `OlTwoPoint` in shotgun pass sets (owner reference images 97 / 101).
* The audit's `FLOAT_RUN_AIRBORNE_WR` rose 34 -> 48 in this run (WR routes, loco matcher, not a contact body): NATURAL-2 / ANIMLIB-BULK should look at it, it is not caused by this track's change (OL / DL fell 8 -> 6).

## 8. Requests
* ART (needs new content): sleeve / shoulder-pad cap and pants-seat skin weights for the 15 bodies (section 4, items 2-3); a clavicle weighting would let a scapulohumeral rhythm help instead of hurt.
* NATURAL-2: `NaturalLook.NeckMax / NeckRate` are now only the v0.12 constants; the live head limit is `NaturalBody.HeadMax / HeadRate` (48 / 380). If the idle layer's glances (up to 34 deg, 40 % in a stance set) are retuned the limit still holds.
* QB-MOCAP: two EditMode tests fail in the shared tree with their in-flight work (`QbMocapTests.EveryQuarterbackTakeNameHasAKeyInTheQbRules`: no QB rule claims `BONES_QB_run_forward_pass_001_s03`; `QbSequenceTests.SecureStartsOnTheCrouchHandsAndEndsAtTheBelly`, `StancePoses.QbSeq.cs`).
* ANIMLIB-BULK: the `BlockHit*Stale_A / _B`, `BlockHitC*`, `BlockDrive*`, `BGS_OLM_Shuffle_CStance_R_Loop` and `DLineMoves ReachBlockShedR` takes carry the 60-84 deg toes-down foot; a clip-level fix (a flatter toe-off) would remove the need for `FootGuard`'s 62-degree cap.

## 9. Integration recipe
New source files under `_Licensed`: none. Generated assets changed: none (no clip, table or database was touched; `-pixV012Stance` and the code are the whole change). Everything is C# plus `Docs/QA/v0130/tools/stance_compliance.pl`; add the `.meta` files Unity writes for `MatchWatchAudit.Stance.cs`, `BodyProbe.Skeleton.cs`, `StanceHeadV013Tests.cs` (generated here by hand).
Commands: confirming run `bash D:/PIXFootball/Work/v012/unity.sh Choreo mw_full 9000 -executeMethod PixFootball.EditorTools.MatchWatchAudit.Run -pixMatchWatch -pixMwOut D:/PIXFootball/Renders/match_watch_v0130/stance -pixMwWallMinutes 140` (118 min wall, 174 plays); like-for-like v0.12 head / stance counts: the same with `-pixMwMaxPlays 40 -pixV012Stance`; skeleton table: `-executeMethod PixFootball.EditorTools.BodySkeletonCheck.Run -pixBodyProbeOut <dir> -pixBodyProbeTag <tag>`; stance compliance: `perl Docs/QA/v0130/tools/stance_compliance.pl <run>/stance_snapshots.csv scrimmage`.

## 10. Follow-up: FACE_BODY_70DEG_OFF_SIM_FACING_STANDING (owner rule: every position faces the correct way)
Counts per game: baseline 1,385, integration 1,551, this track's full game 1,515 (1,562 in the coordinator's number), BULK-2 1,655. Probe: `face_probe.csv` (new, `MatchWatchAudit.Stance.cs` `FaceProbe`): one row at the start and one at the end of every counted run with the match / sim phase, action, stance, clip, view yaw, hips yaw, shoulder yaw and measured pose yaw against the simulated facing, view speed, huddle flag, turn rate. 40 plays (`stance_face0`), 429 runs:

| class | runs | share |
|---|---|---|
| **view in the huddle** (`SimAgentView.Huddle`, clips `PIX_Huddle_Hips / _Knees`, huddle idles; all PreSnap): WR 73, CB 68, DT 49, SS 42, FS 30, RE 29, QB 28, LE 26, TE 23, LOLB 20, LT 16, RT 12, ... | 413 | **96 %** |
| post-play Idle (PostPlay / Dead; view yaw 70-160 deg off the dead-ball sim facing, not turning) | 15 | 3.5 % |
| a clip with a baked pose yaw while the view yaw is right (an MLB in `TackleWrapFrontCHighDmo_B`; QB shotgun `BONES_QB_random_pocket_001_s04`: pose 114, view 0) | 2 | 0.5 % |

**Cause: the detector, counting a correct body.** In the huddle the simulation already has the agent in `Stance` at 0 m/s (the sim is at the formation) while the view keeps the body in the ring, standing still, facing the middle of the ring: 70-180 deg off the line he will face at the snap, for the whole huddle (mean run 541 frames = 9 s; one count per body per play). v0.12's dead-ball setting (25 s whistle to snap) and the longer huddle break made the huddle longer and the number rise with it; every position rises because every position huddles; it is not one library. `stance_snapshots` at the snap (3,124 bodies, 99.7 % comply, facing failures 4) show nobody faces the wrong way when it matters. The stale `ClipFacingTable` entry (`3pstance_aggressive_001_M`, length changed so its offset is 0) did not appear in any counted run: the stance pose layer turns the hips to the stance yaw whatever the clip's baked yaw (poseW 0.999 at the snap).
**Fix (detector only, no body changes):** the rule needs a body that is not in the huddle (`SimAgentView.Huddle`). The v0.12 rule stays as `FACE_BODY_70DEG_OFF_SIM_FACING_STANDING_V012_RULE_<group>_{HUDDLE,SET}` for like-for-like counts. The ring is checked on its own: `HUDDLE_FACING_OFF_RING_CENTRE_60DEG_<group>` (a huddled body whose measured heading is more than 60 deg off the direction to the middle of his team's ring for 20 frames; ring = mean of the bodies the view has in the huddle).
**Full game** (`stance_face`, seed 110110, 186 plays, 16-56, finished, 117 min): FACE standing **1,385 -> 40** (S 10, QB 9, LB 8, CB 3, DL 3, RB 3, WR 3, TE 1; target <= 700). The v0.12 rule on the same game: 1,523 (1,483 huddle, 40 set): 97 % huddle. Ring check: 12 runs of 20 frames over the whole game (TE 3, WR 3, DL 2, LB 2, OL 2): the huddle faces its middle. QB frames (QB-2's, untouched): 98 huddle, 9 in the set (the shotgun clip above, pose yaw 114 deg off with the view right, `STANCE_FACING_OFF_SIM_QB` 9).
**Other counts (not met, not this change):** the change is audit-only, but this full game ran on the wave-2 tree (BULK / QB / NATURAL work merged: 186 plays) and several counts are up more than 10 % against my previous full game (`stance`, 174 plays): FLOAT_STANDING_BOTH_FEET_UP 251 -> 368 (OL 73 -> 150, DL 57 -> 90), HAND_OR_HEAD_BELOW_TURF 103 -> 338 (OL 55 -> 182, DL 33 -> 116), SUNK_HIPS_BELOW_0.35M 156 -> 303 (OL 55 -> 117, DL 23 -> 79), POP_HIPS_JUMP 487 -> 574, OVERLAP 71 -> 101, STANCE_* 67 -> 101 (QB 16 -> 33), FEET_BELOW 49 -> 60, CAM_JUMP 401 -> 440; down: POP_CHEST_ROT 178 -> 37, POP_HIPS_ROT 722 -> 640, FLOAT_RUN_AIRBORNE 85 -> 70, FLOAT_GROUND 91 -> 72, FACE_RUNNING_BACK 28 -> 15. The OL / DL ones are Engaged bodies with the hips at 0.05-0.30 m and the hands at -0.3 m, in the new DLine takes (`DLineMoves__DLINE_SwimR_CritSucces_001_B`, `DLINE_BullRushC_Success_002_B`: hips 0.25-0.31, feet 0.13-0.16 m up) and `BlockHitCPass_A` (hips 0.10): `Engaged` standing frames with the hips under 0.45 m 155 -> 449 in `float_probe.csv`. For ANIMLIB-BULK-2 / CONTACT: not investigated here (the cause is in those takes or in how the engaged ground fit treats them); a hips floor for Engaged bodies is the first thing to check.
