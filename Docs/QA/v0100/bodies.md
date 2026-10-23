# BODIES track log (v0.10.0, playtest note 9)

Owner note 9: the OL stances are wrong (tackles more upright with an arm on the thigh, guards lower, the centre down over the ball); the DL bodies look misshapen (waist, head "in the sky"); every body should have human proportions with no twisting waists, backs, hands or arms; the ball must not be stuck to the forearm when celebrating or walking.

Everything below was measured on real plays through `PlayStage` in a private Unity copy (`D:\PIXFootball\Work\Bodies\PIX`), with the new probe `Editor/Tools/BodyProbe.cs` (see "How to run"). Renders and raw output: `D:\PIXFootball\Renders\bodies_v0100\` (`before\`, `final\`, `iterations\`). Nothing is committed.

Two baselines are used, because other tracks changed the tree while I worked (MOTION's Mixamo locomotion arrived in the middle):
* **BEFORE** = the first run, taken with the tree as it was at the start (24 scripted plays, every body every frame).
* **NO LIMITS** = the same final code with my limit layer and my IK change switched off (`-pixBodySanity 0 -pixOldIk 1`) on the current tree. This isolates what the new layer does from what other tracks' animation changes did.

## 1. Numbers first

### Stances before the snap (run play, pre-snap, muscle space and bone heights in metres)

| Position | | hips | shoulders | head | lowest hand | trunk from vertical | forearm twist | muscles beyond joint limit |
|---|---|---|---|---|---|---|---|---|
| LT / RT | before | 0.66 | 0.67 | 0.75 | 0.06-0.09 (on turf) | 82 deg | 103-113 deg | 15 |
| LT / RT | **after** | **0.90** | **1.24** | **1.35** | **0.75 (forearm on thigh)** | **35 deg** | 55 deg | 2-4 |
| LG / RG | before | 0.66 | 0.67 | 0.75 | 0.06-0.11 | 82 deg | 103-108 deg | 16-22 |
| LG / RG | **after** | **0.82** | **1.01** | **1.10** | **0.66 (forearm on thigh)** | **62 deg** | 40 deg | 3-10 |
| C | before | 0.80 | 0.92 | 1.00 | 0.33 | 69 deg | 92 deg | 6 |
| C | **after** | **0.63** | **0.62** | **0.66** | **0.18 (both hands on the ball)** | **89 deg (flat back)** | 49 deg | 7 |
| DL (LE DT DT RE) | before | 0.71 | 0.70 | 0.77 | 0.13-0.15 | 85 deg (upper back bent up 50 deg) | 30-99 deg | 15-20 |
| DL | **after** | **0.63** | **0.66** | **0.67 (head above the shoulders)** | **0.07-0.10 (hand planted)** | **87 deg** | 21-45 deg | 7-12 |

Neck + head nod in the DL stance: 70 deg before (a hunched upper back, helmet looking up), 79 deg after with a straight back and a level helmet (human limit about 80). Feet: the lowest ankle is 0.12-0.15 m above the turf in every stance, with the limit layer on or off (it does not lift a planted foot).
Tackles sit up, guards sit lower, the centre is down over the ball. `Docs/QA/REFERENCE_Stances.md` and images 97/98/101 (OL) were the target; the tells (run = weight forward, pass = sit back) are kept to +/-4 degrees of lean.

### Pose sanity over 24 recorded plays (every body, every frame, 60 Hz)

Spine+chest twist, muscle space (pelvis-to-chest yaw; the limit is 32 degrees, 46 in a throw), p95 / p99 / max:

| State | BEFORE | NO LIMITS (current tree) | AFTER |
|---|---|---|---|
| Stance | 38.8 / 39.6 / 39.8 | 39.4 / 39.7 / 42.3 | **32 / 32 / 32** |
| Run | 25.3 / 39.3 / 66.6 | 36.7 / 69.1 / 199 | 32 / 32 / 32 |
| Block | 18.5 / 34.4 / 186.7 | 21.9 / 56.6 / 188.7 | 22.4 / 32 / 32 |
| Engaged | 32.1 / 51.4 / 193.6 | 38.6 / 59.2 / 186.9 | 32 / 32 / 32 |
| Carry | **59.8 / 60.2 / 60.9** | 29.7 / 52.3 / 79.0 | 29.7 / 32 / 32 |
| Backpedal | 12.1 / 37.9 / 71.2 | **83 / 145 / 206** | **32 / 32 / 32** |
| Throw | 154 / 177 / 190 | 93 / 97 / 97 | **46 / 46 / 46** |

Forearm roll about its own axis, geometric against the bind pose (the candy-wrapper driver), p95 / p99 / max, NO LIMITS -> AFTER:
Stance 114 / 118 / 131 -> **27 / 31 / 63**; Throw 80 / 82 / 82 -> **38 / 40 / 40**; Idle 66 / 112 / 134 -> 88 / 88 / 88 (at the cap); Run 58 / 63 / 112 -> 68 / 75 / 88.
Hand roll on the forearm: Stance 88 / 109 / 164 -> **34 / 35 / 35**; Run 53 / 55 / 155 -> 35 / 35 / 36 (the excess moves into the forearm bone, the hand keeps its orientation in the world).
Muscle-space forearm twist in the stances (p95), BEFORE -> AFTER: OT 118 -> 56, OG 115 -> 43, DL 101 -> 57, C 98 -> 83. Muscles beyond the default joint limit in the OL/DL stances: 15-25 -> 2-12.
Thigh roll max 118 -> 80, upper-arm roll max 179 -> 100 (a pop). A real backwards elbow turned up in 0.15 percent of frames (up to 24 degrees) and is straightened; knees are measured but not corrected (swinging the shin lifts a planted foot, 12 cm for 16 degrees).
Skinned-mesh volume over the same plays (mean distance of a ring of vertices to the bone axis, against the same body's median; 1.00 = no change), NO LIMITS -> AFTER:

| Ring | min | p01 |
|---|---|---|
| wrist radius over forearm-middle radius (candy wrapper) | 0.71 -> **0.91** | 0.90 -> **0.95** |
| waist radius (lower spine) | 0.58 -> **0.90** | 0.90 -> 0.93 |
| jersey waist, worst edge stretch | 6.6x -> 3.9x | p99 2.58 -> 2.02 |

### Ball

| | BEFORE | AFTER |
|---|---|---|
| Celebration: ball to the carrying hand | 0.07 m for the whole celebration, glued (542 frames) | rests on the open palm for about 2 s, then it is **thrown down** when the hand swings down (or at 2.4 s at the latest): falls, bounces twice, lies on its side |
| After the whistle (tackle): ball to the forearm axis | 0.04 m (inside the forearm) in the tucked carry for 600 frames | tucked while he is down (1.6 s), then **hangs loose in the hand** (0.12 m from the forearm), then **the nearest official walks up and takes it** (in his hand at 2.75 s after the whistle, inside the 2.9 s dead-ball window) |
| Live tucked carry: ball to the forearm axis, p50 | 0.04-0.05 m (the ball sits inside the forearm) | **0.09 m**, 3 cm higher ("high and tight") |

### Proportions of the 15 photoreal bodies (bind pose, `proportions_proportions.txt`)

Heights 1.86-1.95 m. Fine: thigh 0.447-0.473 and shin 0.441-0.468 (ratio 1.01-1.03, human 1.0), torso 0.52-0.55, hip joints 0.19-0.21, head with helmet 0.31-0.32, padded chest 0.45-0.61 wide, waist 0.35-0.48, seat 0.32-0.44 (bulky, never thin).
**Wrong (round 1): the upper arm.** 0.237-0.264 m against a forearm of 0.247-0.271 m (ratio 0.91-1.05, a human's is 1.25: an upper arm of 0.31-0.35 m). Shoulder to wrist was 0.50 m instead of about 0.60. This is why a lineman could not put a hand on the turf without folding down (the DL shoulders ended at 0.66 m), and why mocap hands landed 10 cm short.
**Fixed (round 2, `final\rig3_proportions.txt`, measured on the 15 live prefabs after the rebuild):** upper arm 0.309-0.331 m, forearm 0.247-0.271 m, **ratio 1.25 on all 15**, shoulder to wrist about 0.59 m; every other number above unchanged. DL stance (run play, pre-snap, `final\h3_lines_lines.csv` against `g1_lines_lines.csv`): hips 0.627 -> 0.683 m, shoulders 0.658 -> 0.714 m, head 0.674 -> 0.730 m, hand on the turf at 0.056 m (was 0.070). Centre unchanged (hips 0.632, shoulders 0.62: the bent-over centre stance is limited by the stance spec, not by the arms; hand on the ball 0.154 m, was 0.175).

## 2. What was wrong (causes) and what changed

**DL "waist looks wrong, head in the sky" (`StanceIK.Lay`).** The stance's neck angle (50 degrees from vertical) was applied to the chest to upper-chest segment, not to the neck. With the trunk laid down at 110-125 degrees (the lean search overshot to its maximum for every lineman), the upper back bent up at 50 degrees: a hunchback with the shoulders thrown up, the neck folded back to 70-76 degrees of nod and the helmet looking at the sky. The pelvis was also tipped the whole way over the thighs (the hip joint folded about 100 degrees), which makes the seat of the pants balloon. Fixed: the thoracic spine stays on the trunk line (plus an 18 degree arch), only the neck rises from it (`NeckExtMax` 50), a two-thirds pelvis tilt / one-third lumbar split, the lean is found by bisection between the authored value and a lower cap (DL 104, was 124), the sink of the hips is capped (0.10 m with a hand on the ball), the face pitch is level or slightly down (-6 to -8, was +12 to +14).

**Twisting arms, wrists, waists (`LimbIK`, `BodySanity`).** Two causes: (a) `LimbIK.Apply` swung the bones by the shortest rotation and kept the hand's world orientation, so every time an arm moved far from its animation the forearm picked up a quarter turn of roll (106 degrees of forearm twist in the OL stance); now the upper arm is rolled about its own axis so the elbow hinge lies across the bending plane (`LimbIK.RegisterHinge`, arms only; legs untouched), and a hand follows the forearm for 60 percent of its roll (`HandKeepWorld` 0.4); (b) nothing limited a pose, so mocap pops and procedural layers could twist the chest 180 degrees off the hips or throw the head over a shoulder. `BodySanity` (new, per body, order 100) measures the final pose against per-joint limits (spine twist 32/46, head yaw 78, extension 105, flexion 62, forearm roll 88, hand roll 35, upper-arm roll 100, thigh roll 80, elbow and knee hyperextension 4) and eases any excess back; the torso part runs before the arm IK so hands that reach for a ball follow the corrected chest.

**OL stances (`StancePoses`, `StanceRules`).** `StanceSpec.Ol` (1 tackle, 2 guard, 3 centre) is set by `ForBasic` for scrimmage downs; poses: tackle hips 0.89 lean 46 with both forearms on the thighs, guard hips 0.82 lean 66 with the forearms on the thighs, centre hips 0.74 lean 98 with both hands on the ball (the Anchor hand now takes part in the reach solve). Kicks and the TE keep the old low three-point.

**Ball (`BallFollower.Release`, `PlayStage.BallFlow`, `MatchOfficials.Ball`).** New ball modes: Relaxed (loose in one hand, gripped from above), a palm-up raised hold, Spike (gravity, two bounces, tumble, lies on its side at rest height 0.089 m), Official (leaves the carrier's hand for the official's right hand). The post-whistle logic is `PlayStage.BallFlowHeld` (hooked into `UpdateBall` with one line): the carrier lets the tuck go after 0.55 s (1.6 s if he is lying down; "down" is read from the hips bone, the sim keeps the Tackled action long after the get-up), the nearest official sets off at 0.6 s and stops an arm's length from him, the ball goes to the official's hand when he is within 1.5 m and the carrier is on his feet; a scorer holds the ball on the palm and spikes it when the hand swings down (> 1.5 m/s after 0.9 s, at the latest 2.4 s). The live tuck sits 10 cm from the forearm axis (was 6.5) and 3 cm higher.

## 3. What still looks wrong (looked at once, final strips)

1. **The OL get-off clip.** At the snap the guard's clip (`OLine01_016__OLINE_MAN_3_stance_to_Block_Run_Foot_Work_Left_001`) starts from a hand-on-turf stance: both wrists drop from 0.7 m to 0.44 m, then both arms are thrown out to the sides at shoulder height (wrist 1.3 m, arms 75 degrees from the body) at +0.3 s, a T-pose look for about 10 frames, before the pass-set / block poses. This is clip choice (ANIMLIB / CHOREO), not the stance layer (`final\g1_lines_snapseq_LG.png` and `.csv`). A punch get-off that starts upright would flow from the new tackle / guard stances; hand-off.
2. **DL height: fixed (round 2)** (shoulders 0.66 -> 0.71 m with the longer arms). The centre stays low (shoulders 0.62 m) by design of the centre stance.
3. **Seat of the pants** still reads as a dark bulge behind the jersey on the Large bodies at deep hip flexion: reduced, not gone (see section 4: seat p95 edge stretch 1.93 -> 1.84 and the collapse 0.70 -> 0.76 at 100 degrees, but p99 2.33 -> 2.12; the rest is the 100 degree fold of a two-bone blend and needs a corrective shape or repainted weights in Blender).
4. **Shoulders: fixed to about 1.0-1.3x** (round 2): arms overhead 150 degrees, jersey edge stretch p99 2.58 -> 1.34, pads p99 1.79 -> 1.05, pads p95 1.15 -> 1.00; the armpit side of the jersey now shrinks more (p01 0.65 -> 0.46, a fold, not a stretch). The upper arm's own skin (Arms mesh) is still the worst (p95 2.4 -> 1.9, p99 3.7 -> 3.7): that is the bare-arm sleeve cut and needs weights in Blender.
5. **Run / sprint / route / backpedal on the current tree** have forearm twist of 109-116 degrees at p95-p99 and about 10 muscles beyond the default humanoid limits at p50 (BEFORE: 0). The NO LIMITS run shows the same numbers: they come from the Mixamo locomotion clips that arrived while I worked, not from this track. The limit layer keeps their geometry inside bounds (spine twist and head yaw above), but the clips should be retargeted with the avatar's real limits (MOTION).
6. **Celebration:** the spike is timed to the hand moving down, not to a frame of the celebrate clip (the clip has no tagged release moment).
7. ~~The official needs about 2.75 s to arrive...~~ **Fixed (round 2):** `PlayStage.BallFlowBusy` (Dead phase, ball held by a non-celebrating carrier, an official on the field, under 7 s) is part of `PostPlayBusy`, so the post-play window waits for the hand-off, and the hand-off is forced after 5.2 s whatever the carrier is doing. Ball scene (`final\h3_ball_*`): tackle 438 samples in BALLFLOW state 3 (official has it), none left in the carrier's hand; TD 1 ends `Official`, TD 2 ends `Spike` (ball 1.7 m from the hand).

## 4. Proportions and skin (round 2: on)

`Editor/Assets/PhotoArmProportions.cs` is **on** (`Enabled = true`, `Version 3`). As an AssetPostprocessor it takes each `PHR_*.fbx` from the source and (1) lengthens the upper arm to 1.25 x the forearm and carries forearm, hand and every vertex that follows an arm bone with it; (2) relaxes the pelvis/thigh and chest/arm weight blends (`SmoothTransition`: 12 rounds, radius 0.30 m at the hips and 0.22 m at the shoulders, the share of the driven bone is averaged with the welded neighbours, the two legs never mix); (3) adds four helper bones (`rig:LeftUpLegHelper`, `RightUpLegHelper`, `LeftArmHelper`, `RightArmHelper`, at the joint of the bone they follow) and moves the transition vertices partly onto them (peak weight 0.4 hips, 0.5 shoulders). Around it: `SyncPrefabs` makes the 15 flat prefabs take bone positions, bone list and the `BodyHelperBones` driver from the re-imported model, `FixAvatars` writes the new positions into each avatar's human description (Unity's Animator writes the avatar's stored skeleton back into the bones on bind, which had shrunk the arm again at run time: measured 0.24 before, 0.33 m after), and a one-time marker (`Library/PixArmProportions.v3.txt`) does all three when the project is next opened. `BodyHelperBones` (order 110) turns each helper by half of the driven bone's rotation: four quaternion products per body per frame (about 0.003 ms), skinning stays on the GPU. Three new tests: every body has a human upper arm, every bone sits where its mesh's bind pose says (no shearing), every body carries the four helpers and their driver.

Measured before / after on the source FBX against the processed one in the same poses (`SkinFixCheck`, `final\skin_helper_before_after.txt`; Large_Stocky | Medium_Runner; pelvis pitched 100 degrees over vertical legs, left arm 150 degrees overhead, edge length against the bind pose):

| metric | none | helpers only (0.5) | **shipped (hip 0.4, arm 0.5, smooth; measured at 10 rounds, shipped 12)** |
|---|---|---|---|
| seat p95 stretch, 100 deg | 1.93 / 1.77 | 2.06 / 1.92 | 1.84 / 1.77 |
| seat p99 stretch, 100 deg | 2.33 / 2.05 | 2.59 / 2.54 | 2.12 / 2.03 |
| seat collapse p01, 100 deg | 0.70 / 0.68 | 0.78 / 0.80 | 0.76 / 0.78 |
| jersey shoulder p99 stretch | 2.59 / 2.54 | 2.39 / 2.52 | 1.34 / 1.42 |
| pads shoulder p99 stretch | 1.79 / 1.57 | 1.30 / 1.23 | 1.05 / 1.05 |

(Helpers alone made the seat worse; the weight relaxation is what helps, the helpers add the collapse relief.) The close-up (`final\h3_lines_run_pre_dlclose.png` against `g1_...`) shows a smooth shoulder pad and sleeve and a smaller but still visible seat. The seat is the one item not closed.

## 5. Files

New: `Gameplay/Player/BodySanity.cs`, `Gameplay/Match/BallFollower.Release.cs`, `Gameplay/Match/PlayStage.BallFlow.cs`, `Gameplay/Match/MatchOfficials.Ball.cs`, `Editor/Tools/BodyProbe.cs`, `Editor/Tools/BodyProportions.cs`, `Editor/Assets/PhotoArmProportions.cs`, `Tests/EditMode/Gameplay/BodiesTests.cs`.
Changed (owned): `StancePose.cs` (OL tackle / guard / centre, DL and four-point shapes), `StanceIK.cs` (Lay, bisection, hand reach to the ball, sink cap, QA readouts), `StanceRules.cs` (`StanceSpec.Ol`), `SimAgentView.Stance.cs` (spec cache, `Sanity` creation and the torso call), `BallFollower.cs` (modes, tuck offset).
Hooks in files other tracks edit (one line each): `PlayStage.cs` (`if (BallFlowHeld(h, v)) return;` in `UpdateBall`), `MatchOfficials.cs` (class made `partial`, `_taker` target override in `Tick`, `_taker = null` in `BeginPlay`), `LimbIK.cs` (hinge alignment; the signatures are unchanged, the Solve is untouched).

Round 2 added: `Gameplay/Player/BodyHelperBones.cs`, `Editor/Tools/SkinFixCheck.cs`; `PhotoArmProportions.cs` extended (arms, `SmoothTransition`, helpers, `SyncPrefabs`, `FixAvatars`), `PlayStage.BallFlow.cs` (`BallFlowBusy`, forced hand-off at 5.2 s), one line in `PlayStage.Connect.cs` (`PostPlayBusy`). Hand-offs: the OL get-off clip (arms to a T-pose after the snap) goes to ANIMLIB, the Mixamo forearm twist (item 5) to MOTION.

Tests (`BodiesTests`, now 12; first count was 9): OL slots and their stances, tell size, neck and head of the bent-over stances, the roll math, the limits, the arm-IK hinge, the spike (never through the turf, comes to rest), the official hand-off. Full EditMode run (`-testCategory "!Slow"`): see the end of this file.

## 6. How to run

`Unity -batchmode -projectPath <copy> -executeMethod PixFootball.EditorTools.BodyProbe.Run -pixBodyProbe -pixBodyProbePhase lines|plays|ball|all -pixBodyProbePlays 24 -pixBodyProbeTag name -pixBodyProbeOut dir [-pixBodySanity 0] [-pixOldIk 1]` (batch mode WITHOUT `-nographics`). Outputs `<tag>_summary.txt` (percentiles per metric and state), `_lines.csv`, `_snapseq_LG.csv`, line / profile / close-up sheets, celebration, walk-back and carry sheets. `-executeMethod PixFootball.EditorTools.BodyProportions.Run` measures the 15 prefabs without play mode. The Humanoid muscle readout uses Unity's default limits (neck, head +/-40, forearm twist +/-90); the geometric "GEOM" rows are against the bind pose.

## 7. Test run

One full EditMode run on the private copy (`-testCategory "!Slow"`, tree as of 2026-10-22 19:30, including the other tracks' work in progress): **1245 tests, 1242 passed, 3 failed**, none in this track's files: `ChoreoTests.ClipSamplingInterpolatesRootAndYaw`, `ChoreoTests.TheContactFrameHasTheTakesLayout` (CHOREO, work in progress) and `SimAbilityLineTests.SniperBlitzArrivesSooner` (-0.0264 against < -0.03, a sim calibration test). `BodiesTests` 9 of 9.
Round 2 re-run (private copy, after the rebuild of the 15 prefabs): `BodiesTests` + `PhotoPlayerTests` **19 of 19 passed**.
The five `PhotoPlayerTests` that failed in the DEFENSE copy (`ApplyingAnAppearanceSwitchesSkinHairAndLevels`, `EveryLodFollowsTheAnimatedSkeletonAndTheHelmetSitsOnTheHeadBone`, `EveryPartHasItsLodsAndTheTriangleBudgetHolds`, `HeadsCarryTheSculptedAndAuthoredBlendShapes`, `PrefabsAreHumanoidWithAnimatorAndPhotoPlayer`) **all pass here** on the same `_Licensed` prefabs, with HEAD's `PhotoPlayer`/`PlayerLod`; this track did not touch the prefabs, the meshes or `PhotoPlayer.cs`. The DEFENSE failure message is a bare "Small" / NullReference on the prefab lookup, which points at a fresh project copy whose Library had not imported `Assets/_Licensed/PhotorealPlayer` (reimport the folder), not at the code.

## 8. Round 3: seat and sleeve (weight relaxation only, helpers retired)

Done in the import step (`PhotoArmProportions`, `Version 4`), no Blender needed: `SmoothTransition` now runs 40 rounds over a 0.45 m (hips) / 0.33 m (shoulders) radius, and the chest/arm blend includes Spine1. The helper bones are no longer weighted (peak 0; `BodyHelperBones.Enabled = false`): measured against smoothing alone they made the seat worse and the sleeve worse (visible-edge p99 2.30 vs 2.05 unprocessed), so they cost nothing now and the bones stay in the rig.

Source FBX against processed, same poses (`final\skin_helper_before_after.txt`, `skin_worst_edges.txt`), Large_Stocky | Medium_Runner:

| edge stretch | before | after | target 1.5 |
|---|---|---|---|
| seat p95, 100 deg flexion | 1.93 / 1.77 | 1.60 / 1.47 | met on Medium, 0.1 over on Large |
| seat p99, 100 deg | 2.33 / 2.05 | 1.98 / 1.74 | **not met** |
| sleeve (Arms) p99, arm 150 deg overhead | 3.71 / 3.35 | 2.30 / 2.09 | **not met** |
| sleeve, edges >= 2 cm | 2.05 / 2.07 | 1.64 / 1.59 | **not met** |
| jersey p99 | 2.59 / 2.54 | 1.05 / 1.09 | met |
| pads p99 | 1.79 / 1.57 | 1.00 / 1.00 | met |

The tail that remains is a handful of 2-3 cm edges in the gluteal cleft (left and right thigh meet under the pelvis, which cannot be relaxed without mixing the legs) and in the armpit of the bare-arm mesh, both under the jersey; the worst ones are listed in `skin_worst_edges.txt`. Closer to 1.5 needs a corrective shape or repainted weights in Blender. Close-up `final\h4_lines_run_pre_dlclose.png`: the seat reads as one rounded mass instead of a ballooned bag, still large on the Large bodies. BodiesTests + PhotoPlayerTests: 19 of 19.

**Integration, before the build:** `Assets/_Licensed` is gitignored, so the rebuilt prefabs are not in the repo. Open the shared project once in the editor (or run `-executeMethod PixFootball.EditorTools.PhotoArmProportions.RunBatch` batch, no `-pixSkipArmReimport`): `CheckOnce` sees that `Library/PixArmProportions.v4.txt` does not exist, re-imports the 15 `PHR_*.fbx`, runs `FixAvatars` and `SyncPrefabs`, then writes that marker. Stale markers `v1`-`v3` in `Library` are ignored. A CI/build machine with a fresh `Library` does the same on first import, as the postprocessor works from the source FBX; the prefab rewrite needs the editor to have run `CheckOnce` or `RunBatch` once before the build is made.

## 9. Round 4: one more pass on the cleft and the armpit (stopped, target not reached)

Done in the import step instead of Blender (the same weight edit, no extra tool in the build chain): `SmoothGroup` relaxes the whole split between a bone group over welded neighbours, 30 rounds: hips + both thighs inside a 0.34 m, +-0.12 m band around the cleft (all meshes), and arm + shoulder + Spine1 + Spine2 on the bare-arm mesh within 0.33 m of the joint. `Version 5` (marker `Library/PixArmProportions.v5.txt`, same procedure as section 8).

p99 edge stretch, source -> after this pass (Large_Stocky | Medium_Runner): seat at 100 degrees 2.33 -> 1.73 | 2.05 -> 1.64 (p95 1.50 | 1.42); sleeve with the arm overhead 3.71 -> 2.06 | 3.35 -> 1.81, edges >= 2 cm 2.05 -> 1.63 | 2.07 -> 1.45. Jersey 1.05, pads 1.00. Seat and sleeve tails stay above 1.5 on Large; as agreed no further iteration. What would still close it is a corrective shape or repainted weights authored in Blender. BodiesTests + PhotoPlayerTests 19 of 19. Close-up `final\h5_lines_run_pre_dlclose.png`: unchanged to the eye from section 8.
