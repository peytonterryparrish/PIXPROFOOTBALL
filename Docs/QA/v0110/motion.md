# v0.11.0 MOTION track (`motion.md`)

Notes: 11 "go through a lot of motion capture cleanup ... look at the feet, the ankles, the legs, make sure they're running properly and running in a proper form", 5 (locomotion part) "continue adding more and more motion capture ... use plugins and products". Coordinator additions: Final IK install (section 8), CRITICAL's "feet 4+ cm up in 27 % of moving frames" (section 5), BUGS.md rows B-015 .. B-020 (section 7).
Everything below was measured in this track's private copy `D:\PIXFootball\Work\Motion\PIX` (= git HEAD + this track's files, `sync5.sh`; other tracks' uncommitted edits are NOT taken so their half-finished files cannot break it). Real plays = `EngineAudit`, 36 plays, every running player every frame (`D:\PIXFootball\Renders\v0110_motion\<tag>`); treadmill / positions = `LocoShots` (`D:\PIXFootball\anim_loco\v0110\<tag>`). Nothing committed. Staging databases `PixMotionDb_v11a..g.asset` (`-pixMmDb`, cleaned / mirrored clips in `Mixamo/Clean11`, `Mirrored11`) were used for every experiment so the shipped database was not touched (section 9).

## 1. Summary (numbers first)

Baseline = v0.10.0 final code and database (`base`, `fl0`, `fin0`); after = this track (`final`, `fin1`). Plays = 36 plays x 22 players, treadmill = one body per position (QB body shown, the other eight positions are within 1 % of it: motion does not depend on the body, only on its leg length).

| what | before | after |
|---|---|---|
| Back-pedal trunk lean (treadmill 3.0 m/s, forward +) | -0.2 deg (upright, pelvis 1.09 leg lengths) | **+8.4 deg**, knee flexion at foot strike 65 -> 78 deg, pelvis 1.05 leg lengths |
| Shuffle trunk lean | -2.8 deg | +3.8 deg (still reads as a side-step jog: needs a real take, section 6) |
| Body facing against travel for a coverage runner who faces away at 7.5 m/s (treadmill, B-018) | 180 deg for the whole run (a back-run take played at sprint speed) | **0 deg** (turn-and-run over 0.25 s) |
| Mid-contact frames with the lowest sole > 4 cm (treadmill, share of mid-stance frames) | jog 16 %, run 17 %, sprint 13 %, back-pedal 3 %, shuffle 4 %, start 9 %, stop 3 %, cut 5 % | jog 6 %, run 10 %, **sprint 3 %**, back-pedal 0 %, shuffle 0 %, start 5 %, stop 5 %, cut 0 % |
| ... same in the 36 plays (share of mid-contact frames, speed band) | >= 5.5 m/s: run 6 %, route 3 %, sprint 4 %; 3-5.5: 9-15 %; < 3: 17-33 % | unchanged (+-1 point), see section 5 |
| Planted-foot slide per stance (min over the contact, m/s, CB / LB / S mean, plays): sprint 6, run 4.5, run 6, jog 3.0, shuffle 3.0, back-pedal 3.0 | 0.027, 0.059, 0.060, 0.168, 0.208, 0.108 | 0.026, 0.058, 0.062, 0.178, 0.220, 0.131 (the back-pedal crouch costs 0.02; the jog / shuffle targets of <= 0.10 are **not met**) |
| Flight fraction (both toes > 10 cm), treadmill run 5.0 / sprint 7.5 | 0.27 / 0.26 | 0.27 / 0.26 (levers tried in section 5, none moves it) |
| Hips offset from the sim position, TE max (plays) | 0.59 m | **0.30 m** (leash 0.30 m, B-020) |
| Final IK | not available | installed for all tracks, `PixIk` door, hand reach exact to 0.0 cm, 0.055-0.09 ms per solve (section 8) |
| 100STYLE | - | licensed, downloaded, converted, cut, measured: **too slow to use** (max 3.4 m/s); pipeline kept (section 6) |
| Explicit start / stop / cut takes | not in the matcher | built (distance-based playback, planned-path gating) and measured **no better** than the loops alone: shipped off (section 4) |

## 2. Running form audit

Tools (new): `Editor/Tools/FormProbe.cs` (final pose, every running frame: knee drive, heel recovery, foot strike distance ahead of the pelvis along the travel direction, foot pitch and knee flexion at touch-down, toe-off, trunk lean split by acceleration, arm-leg phase, pelvis bob, floating split into flight / contact edge / mid-contact), `FormAuditHook` (the same inside `EngineAudit`: `form_quality.md` per gait band and per position, plus the new `hipsOff` and `soleLow` columns of `motion_frames.csv`), `LocoShots -pixLocoModes positions` (all nine positions with their own body size and role x jog / run / sprint / back-pedal / shuffle / start / stop / cut / coverage lane: form + leg tables, a close side strip and a telephoto "broadcast" strip from behind and above, one sheet per gait with the nine positions as rows), `-pixLocoModes ik,ground` (section 8). Strips looked at: `D:\PIXFootball\anim_loco\v0110\base\pos_*_{side,bcast}.png` (before) and `final\` (after). Position tables: `metrics_positions.md` (the "strike" columns of `base` come from the first version of the probe, which called touch-down by foot height; every later run uses the matcher's contact tag, so strike distances are comparable only among the later runs).

What the numbers say per gait (treadmill, QB body, after = `final`):

| gait | knee drive (peak thigh flexion, mean / p95) | heel recovery (peak knee flexion) | foot strike ahead of the pelvis (cm, mean / p95) | trunk lean (deg) | verdict |
|---|---|---|---|---|---|
| jog 3.2 | 46 / 47 | 97 / 100 | 27 / 32 | 6.3 | knee drive low for a jog; strike 27 cm is 0.31 leg lengths (literature 0.2-0.3 m): no overstride |
| run 5.0 | 74 / 85 | 138 / 145 | 35 / 41 | 14.2 | good; strike 35 cm is slightly long (the stride is stretched +10 % to the simulated speed) |
| sprint 7.5 | 76 / 81 | 135 / 140 | 40 / 46 | 14.1 | knee drive and heel recovery of a real sprinter (75-85 / 130-145 deg); strike 40 cm (touch-down distance of elite sprinters 30-40 cm); arm-leg phase 0.93; trunk lean flat: it does not grow with speed |
| back-pedal 3.0 | -16 (thigh behind the hip) | 101 | 28 / 34 | -0.2 -> **+8.4** | was upright on straight legs; now chest over bent knees. Still no hands-up arms |
| shuffle 3.0 | 24 / 41 | 80 / 91 | 28 / 51 | -2.8 -> +3.8 | upright, narrow base, arms dangling: a side-step jog (source limit) |
| start to 6.5 | 70 / 81 | 129 / 142 | 36 / 43 | 14.2 (accel 10.6) | lean grows with acceleration only 0.4 deg per m/s2; a real start leans 30-40 deg |
| stop from 6.0 | 68 / 71 | 130 / 134 | 34 / 38 | 16.5 (decel 7.5) | no lean back |
| cut 90 at 6.0 | 58 / 70 | 123 / 142 | 24 / 41 | 5.8 (decel 10.5) | the body turns early and the plant is soft (no plant-and-cut hip drop) |

Left-right and shape (plays, `leg_quality.md`, unchanged from v0.10): toe-out at contact 3.9-5.3 deg at speed (route / run / sprint >= 5.5 m/s) with a left-right difference of 0.3-1.7 deg, knee off the hip-ankle plane 1.2 cm, ankle roll 2.0 deg: the legs run square, no knock-knees, no bow legs. Not good: back-pedal 28 deg and shuffle 23-27 deg toe-out against the facing, carry < 3 m/s 25 deg (oblique takes and action-layer clips).

What was wrong and what was done, per gait:

1. **Back-pedal** (upright, legs nearly straight, no lean): runtime athletic posture in the matcher (`MotionMatcher.PostureW / PostureLean / PostureDropW`): trunk forward 8-11 deg about the pelvis lateral axis (read before the facing-convention turn and rotated with it: the first version leaned sideways), pelvis 3.5 cm lower for back-pedal takes (the planted feet stay, `FootPlant` bends the knees; the same drop on the shuffle made the side-stepping feet slide, 0.38 -> 0.58 m/s, so the shuffle gets the lean only). Cost: back-pedal slide per stance 0.067 -> 0.099 m/s (1.5 m/s bin), 0.108 -> 0.131 (3 m/s bin). Switch `-pixMmNoPosture`.
2. **Coverage men facing away at sprint speed** (B-018): `SimAgentView.LocoFacing` turns the body to its travel over 0.25 s when it travels over 4.5 m/s more than 100 deg off its facing (back to the simulated facing under 3.5 m/s or within 60 deg); `-pixNoTurnRun` for A/B.
3. **Sprint / run top speed** (+20-25 % over the fastest take): at 6-7.5 m/s the matcher plays warp 1.13 x rate 1.10 = x1.24, at 3-4.5 m/s x1.18 (the database has a hole between the 2.7 m/s jog takes and the 4.35 m/s run take). That is 1.73 m stride at 4.6 steps/s = 7.96 m/s: inside the human range (elite 1.8 m / 4.4 steps/s at 8 m/s), not visibly wrong. A faster source does not exist in the free packs (DraftFever `Sprint09` is 8 m/s but one foot never lands: flight 0.71, 1.9 steps/s). Wanted: section 6.
4. **Ankle / toe-out / roll**: the left-right bias of the Mixamo takes is already removed by the mirrored twins and the foot polish; a further source cleanup would not change the measured 3-5 deg toe-out and 2 deg roll, so `MotionClean` stays as it is (muscle re-zero, soft limit).
5. **Torso lean by acceleration** (0.4 deg per m/s2 in the start, 7.5 deg in the stop against 30-40 deg of a real start): open. The sim knows the acceleration; a lean proportional to it in `MotionMatcher.Late` (next to the posture) is the next step.
6. **Walking**: the contact tags of the library walk counted any foot within 7 cm of the floor as planted, so every step was held 3 frames after it lifted (6-9 cm up with weight 1). Tags are now speed dependent in the builder (walk: enter 2.4 cm, leave 3.8 cm, `MotionDbBuilder`; only in the staging database `v11f`, the shipped one keeps the old tags). On the treadmill the last two frames of each walking stance no longer float; in plays the slow bins did not change (section 5): not closed.

## 3. v0.10 open items

| item | v0.10 | now |
|---|---|---|
| jog 3.0 mean slide <= 0.10 | 0.205-0.403 | **0.168-0.178** (median 0.03; worst take `Jog_Running` 0.35 on its own, 0.14 mirrored; leaving it out did not help: 0.185) - **not met** |
| shuffle 3.0 mean slide <= 0.10 | 0.33-0.37 | **0.208-0.220** (median 0.15) - **not met**: the strafe takes travel 98 / 79 deg, so the foot sweep and the 90 deg path differ by 8-11 deg; the orientation warp turns the legs but about 0.4 m/s of lateral slide stays on the treadmill |
| sprinters played +20-25 % over the fastest take | build a faster cycle | not buildable from the free sources (2.3); documented, wanted in section 6 |
| starts / stops / cuts as explicit transitions in the matcher (with sim yaw agreement) | none | built, measured, shipped off (section 4) |
| walk quality | library walk | builder tags fixed (2.6, staging database only); the 100STYLE walk is 0.5-1.3 m/s, not used |

## 4. Explicit transitions (built, measured, off)

Matcher: one-shot takes of the kinds Start / Stop / Turn / Cut play by distance (time runs as fast as the body covers the ground the take covers: rate = sim speed / take ground speed in [0.6, 1.8], the warp absorbs the rest), they have no speed-band penalty, and they are candidates **only when the simulated plan says so** (`MotionMatcher.Search`): a stop when the planned path brakes (`|P3| < 0.55 v`), a start when the body stands and the path runs away, a cut / turn when the planned facing change exceeds 25 / 30 / 45 deg at +0.33 / +0.66 / +1.0 s (the sim yaw agreement). Database: `Run_Idle_To_Sprint`, `Stop_Run_To_Stopss`, `Stop_Standing_Torch_Run_Forward_Stops`, `Run_Sprint_Turn`, `Run_Running_Right_Turn`, `Turn_Goalkeeper_Sidestep(_2)`, `Jog_Turn_To_Runninggx`, `Jog_Running_Turn_180`, each with its mirror (`MotionDbBuilder -pixMmTrans 1`; `Run_Crouched_To_Sprinting` left out: its retarget stands 37 cm too low). The facing-change features (dims 18-20) keep their v0.10 scale (the turning takes would have tripled their spread and halved their weight for every other take).

Measured:
* First attempt (ungated): `Stop_Standing_Torch_Run_Forward_Stops` (a 2 m/s take with no speed penalty) was chosen for 19,000 of 75,000 matcher frames, i.e. for any slow gait, with planted slide above 0.8 m/s on 71-75 % of its frames. Gating fixed that.
* Gated, 36 plays (`c2` against `c0`): back-pedal and coverage bins better (CB back-pedal 1.5 m/s 0.106 -> 0.039, LB back-pedal 3 m/s 0.071 -> 0.042, CB run 6 m/s 0.116 -> 0.021), receivers, tight ends and backs worse (TE route 1.5 m/s 0.106 -> 0.408, TE route 3 m/s 0.31 -> 0.575, RB run 4.5 m/s 0.029 -> 0.122): net zero within the noise of 10-150 stances per bin.
* Treadmill (start 6.5, stop 6.0, cut 90 at 6.0, planted speed m/s): start 0.27 -> 0.30-0.32, stop 0.27 -> 0.28-0.30, cut 0.49 -> 0.55-0.58; the cut takes tag one foot for 15 of 18 frames (`Run_Sprint_Turn` L 3 / R 15): their contact tags need a hand clean.

**Decision: the loops stay the default and the shipped database has no transition rows.** The machinery is switched on by building with `-pixMmTrans 1` (that database is `PixMotionDb_v11a.asset`). They need better takes (section 6, item 3).

## 5. Floating (CRITICAL: "lowest sole 4+ cm up in 27 % of moving frames")

Not one phenomenon. Classified frame by frame (`FormProbe`; the matcher's contact tag decides; it runs 1.5 source frames ahead of the foot, so a tagged stance starts before the foot arrives), treadmill, QB body (the plays are in the same range, `fin1/form_quality.md`):

| gait | flight (no contact tag) | contact edges (first / last two tagged frames) | mid-contact | sole > 4 cm in mid-contact | in edges | in flight |
|---|---|---|---|---|---|---|
| jog 3.2 | 17 % | 17 % | 67 % | 6 % | 52 % | 100 % |
| run 5.0 | 22 % | 22 % | 56 % | 10 % | 38 % | 88 % |
| sprint 7.5 | 26 % | 29 % | 45 % | 3 % | 28 % | 92 % |
| back-pedal 3.0 | 15 % | 20 % | 66 % | 0 % | 0 % | 33 % |
| shuffle 3.0 | 23 % | 28 % | 48 % | 0 % | 11 % | 89 % |
| start 6.5 / stop 6.0 / cut 6.0 | 26 / 19 / 25 % | 27 / 22 / 30 % | 46 / 59 / 44 % | 5 / 5 / 0 % | 28 / 25 / 17 % | 88 / 100 / 95 % |

* **Flight** (both feet off the turf) is a stride: about a quarter of the frames at run and sprint speed, and every flight frame is 4+ cm up by definition. v0.9.0 had 0.58, v0.10 0.28, now 0.25-0.27. Real sprinters are airborne for a large part of every step (about 0.10 s of a 0.22 s step), so the 10 % of B-016 is not a physical target.
* **Contact edges** (foot arriving or leaving): the foot is 5-10 cm above the turf for the last two frames before it lands (50 ms lead) and the first two after the take lifts it. That is the foot moving, not floating.
* **Mid-contact** is what "a foot should be in contact" means: sprint 3 % (4 % in plays), run 5.0 10 % (6 % at >= 5.5 m/s in plays), jog 6 %. As a share of **all** moving frames (mid-contact share x floating share): jog 4 %, run 5.6 %, sprint 1.4 %, back-pedal and shuffle 0 %: the < 5 % target is met except the 5.0 run (5.6 %) and the slow plays bins below.
* **Not met**: plays under 3 m/s (walk, jog, acceleration steps): mid-contact floating 17-34 % (run 27 %, route 28 %, carry 34 %, back-pedal 20 %, shuffle 19 %). `Run_Jog_Forward` is the worst take (39 % of its tagged frames with the lowest sole > 4 cm), then the diagonal jogs (22-28 %) and the library walk (18 %). Causes found: heel-strike jog takes whose tag (4 cm enter / 7 cm leave) lasts 2-3 frames beyond the lift, the stride warp changing during a stance while accelerating, take switches in the slow band. Tighter tags for every take were tried (v11b: treadmill planted slide jog 0.12 -> 0.47, shuffle 0.38 -> 0.72: the foot is released while it is still on the turf and skids) and reverted.
* **Levers tried for the flight fraction (none moves it):** shave the pelvis peaks above its running average by 45-90 % (bob 0.107 -> 0.095, flight 0.27 / 0.26 unchanged, `-pixFpPeak`, left at 0.45); sink the pelvis 3.5 cm during an un-planted flight (bob 0.107 -> 0.128, flight 0.26 -> 0.23: more bounce, rejected); keep the contact until the foot is 10 cm up (`-pixMmTagExit 0.10`: flight 0.26 -> 0.25, slide unchanged); read the tag 2.5 / 3.5 frames ahead instead of 1.5 (`-pixMmLead`: slide 0.30 -> 0.45 at sprint, 0.27 -> 0.56 at run, flight 0.26 -> 0.25, rejected); release the ball of the foot at 5 cm (`-pixFpBallRel`: slide up, off); Final IK GrounderIK on top of FootPlant over all positions and gaits: identical to the digit (section 8).


## 6. More locomotion mocap: 100STYLE (CC BY 4.0, "The 100STYLE Dataset - Ian Mason")

**Source and license (checked before the download):** official site https://www.ianxmason.com/100style/ ("Creative Commons Attribution 4.0 International License", "for creative or commercial work please credit in an appropriate way", example credit "The 100STYLE Dataset - Ian Mason") and the Zenodo record 8127870 (CC BY 4.0, open access, 100STYLE.zip 1.5 GB, MD5 3cf627852fd8192024c04a8d0ef49583, verified). Motion data only: the retarget mesh of Holden's `100style-retarget` is non-commercial and was not downloaded. Downloaded from the Zenodo record into its own folder `D:\PIXFootball\Source\100STYLE` (zip, then only 12 styles extracted into `data\`, untrusted data: nothing was run from inside it, paths are arguments). The credit line goes into the in-game credits if any 100STYLE motion ships (nothing ships yet: see below).

**Pipeline built** (`Editor/Tools/Style100Import.cs`, no game types, runs in an empty project): BVH (60 fps, cm, Y up, +Z forward, right handed) parser; mirror in x to Unity; a Humanoid avatar built from the BVH T-pose (Chest -> Spine, Chest2 -> Chest, Chest3 -> UpperChest, Chest4 unmapped); `HumanPoseHandler` per frame -> muscles + body root; Humanoid muscle clips at 30 fps with the root normalised to the origin and heading +Z at the first frame, relaxed fingers; automatic cutting of the long takes (3,000-17,000 frames) into loops (steady speed / direction / closed seam, best seam per speed and angle bin), starts, stops and cuts from the root and foot trajectories; catalogue CSV with speed, travel angle, cadence, flight, seam. Self test (clip played on the avatar against the BVH forward kinematics): ankle 2.5-3.2 cm, wrist 0.6-2.0 cm. 500 pieces were produced from 12 styles x (FR, BR, SR, TR1) and 48 loops from (FW, BW, SW) of 4 styles (Neutral, Proud, Heavyset, Stiff); folders kept in `D:\PIXFootball\Source\100STYLE\clips_all` and `Work\Style100`.

**Why nothing of it is in the database (measured):** the capture volume is small. Ground speed of the whole takes (smoothed 0.25 s, m/s):

| take | median | p90 | max |
|---|---|---|---|
| Neutral FW (walk) / BW / SW | 0.74 / 0.60 / 0.44 | 0.96 / 0.79 / 0.88 | 1.23 / 1.27 / 1.26 |
| Neutral FR (run) / BR / SR | 1.29 / 0.90 / 1.02 | 1.63 / 1.17 / 1.45 | 2.00 / 1.44 / 1.86 |
| Rushed FR (the fastest style) / BR / SR | 1.68 / 1.22 / 1.33 | 2.68 / 1.87 / 2.03 | 3.41 / 2.28 / 2.50 |
| Rushed TR1 (transitions) | 1.30 | 2.24 | 3.16 |
| Proud, March, Heavyset, BentKnees, OnToesBentForward, Stiff, Strutting, Roadrunner, Rocket FR | 1.0-1.4 | 1.3-2.0 | 1.5-2.4 |

The "run" of the dataset is a brisk walk to a slow jog; the game's players back-pedal and shuffle at 2-4.5 m/s and sprint at 7-9, the matcher engages from 0.85 m/s. Nothing in 100STYLE reaches a football speed, and everything that reaches the walk band is a 0.5-1.3 m/s stroll (the library walk take is 1.7 m/s). The dataset would only have value for pre-snap shifts and huddle walking; the loops are cut and measured, none beats the library walk enough to pay for an inertialised jump, so **no 100STYLE take was added to the database**. The importer and the cut pieces stay (CC BY 4.0, free to use) for any later low-speed need (idle shifts, slow back-pedal for a QB after the hand-off).

**What I would want, and why (owner, nothing bought):**
1. A true football sprint capture, 8-9 m/s, flight <= 0.35, both feet landing: the matcher tops out at 5.5 m/s of planted-foot speed (Run_Fast_Run) and plays the fastest runners at x1.2 rate and x1.2 stride. Candidates: a sprint pack from Mocap Online / Rokoko / Actorcore (ask for the 100 m sprint set) or a Perception Neuron / Xsens capture of two players.
2. Back-pedal, defensive shuffle and slide (2-4.5 m/s) in a football stance (low hips, hands up): the Mixamo back-run takes stand upright (the trunk lean and knee bend added at run time are a patch). Mixamo "Defensive Shuffle", "Side Shuffle", "Quick Sidestep" (Mixamo sign-in needed), or a football defensive-back pack.
3. Start / stop / cut takes with a real plant (45-90 degree cut at 5-7 m/s, "Run To Stop", "Jog To Stop", "Walk To Stop", a relaxed standing "Idle" from Mixamo): the explicit-transition machinery is built and measured (section 4), the database needs better takes than the five Mixamo ones available to make it win.
4. Plugins: Final IK is in and used (section 8). Cascadeur (AI cleanup of single clips), Rokoko / Actorcore cleanup: not needed for what I could measure; a footskate cleaner for the DraftFever pair takes is ANIMLIB's.
## 7. BUGS.md rows owned by MOTION

| id | status |
|---|---|
| B-015 carry feet off the ground at contact | The picture (`FLOAT_STANDING_BOTH_FEET_UP_WR_2_p2.png`) is the RB hurdling a defender who lies under him (a legal `Hurdle`, RB track `CarryMoves`): both cleats up is the move. The carry gait itself: mid-contact floating 8 % at >= 5.5 m/s, 13 % at 3-5.5 m/s (`fin1`), the same code path as free running. No locomotion defect found; the probe should skip `Hurdle` / `Dive` (AUDIT). |
| B-016 flight phase ratio | Per position (36 plays, matcher-owned frames above 3 m/s, both toes > 10 cm): CB 16.6 %, FS 16.7 %, LB 15.8 %, TE 17.0 %, WR 17.7 %, RB 22.9 %, DT 22.5 %; the other numbers of BUGS (24-33 %) are the whole game including tackle takes. **Not reduced** (section 5: six levers, none moves it; a sprint has 0.10-0.12 s of flight in a 0.22 s step). |
| B-017 chest / hips rotation pops | 36 plays: chest > 1,400 deg/s CB 20, S 22, LB 18, WR 15, TE 15; hips > 900 deg/s CB 81, TE 84, WR 70, LB 50, S 30. 181 matcher-owned pops: only 22 % within 3 frames of a clip switch, 59 above 4.5 m/s, the rest the visible body chasing the simulated facing at 720 deg/s; the visible heading turn rate got a hook (`LocoTurnRate`, `-pixTurnRate`, default unchanged at 720) but a lower rate was not evaluated this round. Most of the BUGS counts are `Engaged` / `Block` clips (SIM-FLOW). |
| B-018 coverage men run backwards | **Fixed** (turn-and-run, `LocoFacing`): 180 deg off travel -> 0 deg (treadmill coverage lane, 7.5 m/s). The 36 scrimmage plays contain no kickoff coverage: their share of frames above 4.5 m/s more than 100 deg off travel is CB 2.7 %, TE 2.0 %. |
| B-019 planted-foot slide | The audit's number is the per-frame speed of any foot the solver flags planted. Mid-stance 0.37 m/s mean (9 % of frames above 0.8), first two frames 0.33, **last two frames 2.33 (67 % above 0.8): the toe-off, still flagged planted for two frames after the foot starts to leave**. Per-stance slide (min over the contact) is 0.03-0.06 at run / sprint speed, 0.17 jog (section 3). OL / DL / K / P (1.6-2.8 m/s) are `Engaged`, `Block` and kick-approach clips, not the matcher (SIM-FLOW / QB / ANIMLIB). Not changed: releasing the lock earlier made the physical skid worse (section 5). |
| B-020 hips detach from root | **Leash**: the skeleton is held within 0.30 m of the simulated position (`SimAgentView.HipsLeashLate`, whole body shifted, `-pixHipsLeash`). 36 plays: TE max 0.59 -> 0.30 m; OL / DL stay under 0.18 m in these plays (the 1.5 m of BUGS happens in block takes this sample did not hit); the leash caps them at 0.30 m. |

## 8. Final IK in PIX

Owner-licensed Final IK (RootMotion, Asset Store) is installed for every track; it is **never** part of the public repo (Asset Store EULA).

**Install (done once, shared):** `Assets/_Licensed/RootMotion/` (junction target, git-ignored by `/Assets/_Licensed/`; verified with `git check-ignore`). Runtime = FinalIK solvers, Grounder, InteractionSystem, Posers, Rotation Limits, Shared Scripts (172 scripts, 2.7 MB); demos, third-party integrations and the baker were left out. The two assembly definitions Final IK ships in its nested "Import Assembly Definitions" package are installed: runtime `RootMotion`, editor `RootMotionEditor`. It compiles clean in Unity 6000.6 (scratch project and the game project). Reproduce / reinstall: `Tools/FinalIk/install_finalik.sh "<Final IK.unitypackage>"` (a variant unpacker that keeps the `.cs` files and the GUIDs, refuses to run if `_Licensed` is not ignored).

**How the game talks to it (no compile define needed):** `Gameplay/Player/Motion/PixIk.cs` is always compiled and contains no Final IK types: `IPixIk` + the static door `PixIk` (`Hand`, `Foot`, `Body`, `Late`, `Release`, `Available`, `Off`). Without the asset `PixIk.Service` is null and every call is a no-op, so the public repo builds and plays as before. With the asset, the bridge assembly `PixFootball.FinalIkBridge` (source in the repo: `Editor/Tools/FinalIk/Bridge~/`, a tilde folder Unity ignores; installed next to the asset by `PixFootball.EditorTools.FinalIkInstaller.Run`, menu PIX / Install Final IK bridge, into `Assets/_Licensed/RootMotion/PixBridge/`) registers itself at startup (`[RuntimeInitializeOnLoadMethod]`). A `PIX_FINALIK` define would need a package version define, which an Asset Store folder cannot provide, and `defineConstraints` cannot be satisfied without editing ProjectSettings; the registration pattern needs neither, and `PixIk.Available` is the runtime flag. **The bridge is not installed in the shared `_Licensed` yet** (every track's project compiles whatever is in there and the bridge needs `PixIk` / `SimAgentView.PixIkState`, which only exist in trees that synced this track's files). Integration step after the merge: run `FinalIkInstaller.Run` once (or copy `Bridge~` to `Assets/_Licensed/RootMotion/PixBridge`). Until then the bridge lives in the MOTION private copy (`Assets/PixBridgePrivate`, `-pixIkBridgeDst`).

**What the bridge does:** one `FullBodyBipedIK` per body (references from the humanoid avatar), `enabled = false`, `fixTransforms = false`; `SimAgentView.LateUpdate` calls `PixIk.Late(this, dt)` after every procedural pass (matcher, FootPlant, stance IK, arm IK, carry, engage step, hips leash), which solves **only while an effector has weight**, only for bodies at LOD 0 / 1, with 3 iterations. Effectors ramp in over 0.10 s and out over 0.20 s.

**How to use it from CRITICAL / SIM-FLOW / RB / QB code (call every frame while the contact exists; stop calling and it fades by itself):**
```csharp
PixIk.Hand(view, left: false, worldPos, worldRot, weight: 1f, rotWeight: 0f);   // a hand on the ball, a shoulder, a facemask, a hand-off
PixIk.Foot(view, left, worldPos, worldRot, weight, rotWeight);                // a foot placed on a hurdled body, a plant on a line
PixIk.Body(view, worldOffset, weight);                                        // lean / drive the whole body toward a contact (tackle wrap, block punch)
```
The solver moves the shoulders, spine and pelvis to bring the hand there (`FullBodyBipedIK`); the hand lands within 0.0 cm of the target in every case measured. For several hand targets on one frame call `Hand` for each; for an attachment to a prop that moves, pass its current world pose every frame. `PixIk.Off` / `-pixIkOff` switches it off (A/B). The game's own `LimbIK` (PixFootball.Gameplay) is a different class from `RootMotion.FinalIK.LimbIK`: inside the bridge the Final IK one is aliased. `InteractionSystem` was not wired (it needs `InteractionObject` prefabs on every prop); the effector calls above cover hands on tackles, blocks and hand-offs without it.

**Measured** (`LocoShots -pixLocoModes ik`, treadmill body, `D:\PIXFootball\anim_loco\v0110\gr1\metrics_ground_ik.md`, strips `ik_pairs.png`: left tile without, right tile with IK):

| case | effector | distance to target without IK | with IK | solves | ms per solve |
|---|---|---|---|---|---|
| standing, right hand reach 55 cm | right hand | 82.2 cm | 0.0 cm | 270 | 0.087 |
| standing, both hands (ball catch) | right hand | 67.9 | 0.0 | 270 | 0.057 |
| run 5.0, both hands | right hand | 60.0 | 0.0 | 270 | 0.057 |
| run 5.0, right hand reach | right hand | 74.3 | 0.0 | 270 | 0.053 |
| standing, tackle wrap (both hands + body pull 18 cm) | right hand | 66.4 | 0.0 | 270 | 0.060 |
| back-pedal 3.0, both hands | right hand | 75.1 | 0.0 | 270 | 0.058 |

Cost: 0.055-0.09 ms per FBBIK solve, i.e. about 3-4x the 0.02 ms budget **per body that is in contact**, 0 for every other body (nothing is solved without an effector), so a handful of simultaneous contacts cost 0.3-0.5 ms per frame. Looked at once: with IK the chest turns into the reach, the shoulder follows, the pelvis shifts back a few centimetres; no fold-over of the spine at 55 cm, hands on the ball are exact.

**Grounder (GrounderIK + two Final IK LimbIK legs, driven by hand, flat collider on a private layer for the turf) as the fix for feet on the turf: measured, not used.** `-pixGrounder` (`PixIk.Grounder`) switches it on for A/B. Treadmill, same body, with FootPlant running underneath (`gr1`):

| gait | grounder | planted speed mean | toe slide | lowest sole mean (cm) | flight | ankle roll | ms per body-frame |
|---|---|---|---|---|---|---|---|
| idle | off / on | - | - | -0.4 / -1.0 | 0 / 0 | - | - / 0.025 |
| jog 3.2 | off / on | 0.12 / 0.12 | 0.01 / 0.01 | 2.4 / 2.4 | 0.15 / 0.15 | 1.8 / 1.9 | - / 0.023 |
| run 5.0 | off / on | 0.28 / 0.27 | 0.04 / 0.04 | 3.6 / 2.8 | 0.27 / 0.17 | 1.8 / 1.9 | - / 0.024 |
| sprint 7.5 | off / on | 0.30 / 0.31 | 0.06 / 0.06 | 3.4 / 3.7 | 0.25 / 0.26 | 1.9 / 1.9 | - / 0.024 |
| back-pedal 3.0 | off / on | 0.07 / 0.07 | 0.01 / 0.00 | 0.5 / 0.6 | 0.02 / 0.02 | 1.8 / 1.9 | - / 0.022 |
| shuffle 3.0 | off / on | 0.37 / 0.37 | 0.06 / 0.06 | 1.9 / 2.1 | 0.09 / 0.09 | 1.9 / 1.9 | - / 0.022 |

It fits the 0.02 ms budget (0.022-0.025 ms) but changes nothing measurable: FootPlant already pins and presses the planted foot (mid-stance soles are at -0.1 to +0.6 cm), Grounder has no notion of a stride lock, and standing it sinks the soles 0.6 cm. The "feet 4+ cm up in 27 % of moving frames" is not a missing ground contact: section 5 splits it into flight, contact edges and mid-contact.

## 9. Switches, files, tests, how to repeat

**Switches (all QA, defaults are the shipped behaviour):** `-pixMmNoPosture` (no back-pedal / shuffle athletic stance), `-pixNoTurnRun` (no turn-and-run), `-pixHipsLeash <m>` (0 = off, default 0.30), `-pixTurnRate <deg/s>` (visible body heading, default 720), `-pixMmNoTrans` (never search start / stop / cut takes), `-pixMmLead <frames>` (contact tag lead, 1.5), `-pixFpPeak <0..1>` (pelvis peak shave, 0.45), `-pixFpFlightDrop <m>` (shave cap, 0.04), `-pixFpBallRel <m>` (ball release height under hinted contact, off), `-pixIkOff`, `-pixGrounder`, `-pixMmDb <asset>` (load / write another motion database). Builder (editor): `-pixMmTrans 1` (include transition takes), `-pixMmTagExit <m>`, `-pixMmOldTags 1`, `-pixMmSkip a,b`, `-pixMmCalib 1`, `-pixMmDb <asset>`.

**New files:** `Gameplay/Player/Motion/PixIk.cs`; `Editor/Tools/FormProbe.cs`, `Style100Import.cs`, `FinalIk/FinalIkInstaller.cs`, `FinalIk/Bridge~/{FinalIkService.cs, PixFootball.FinalIkBridge.asmdef}`; `Tools/FinalIk/install_finalik.sh`; `Docs/QA/v0110/motion.md`. **Changed (owned):** `Motion/{MotionDb,MotionMatcher}.cs` (gated transition search, distance playback, posture, `-pixMmDb`, contact lead), `Loco/FootPlant.cs` (pelvis peak shave, ball release switch), `SimAgentView.Loco.cs` (posture inputs, `LocoFacing`, `HipsLeashLate`, switches, `PixIkState`), `Editor/Tools/{LocoShots (positions, ik, ground modes), MotionDbBuilder (speed-dependent tags, transitions opt-in, staging suffix, skip), MotionClean (staging folder), MixamoScan (FolderScan)}.cs`, `Tests/EditMode/Gameplay/MotionMatchingTests.cs` (+4 tests). **One-line hooks in shared files:** `SimAgentView.cs` (`LocoFacing` before the heading, `LocoTurnRate`, `HipsLeashLate`, `PixIk.Late`), `Editor/Tools/EngineAudit.cs` (`FormAuditHook`, `hipsOff`, `soleLow` columns).

**Database:** the shipped `PixMotionDb.asset` is **unchanged** (the only difference of the staging database `PixMotionDb_v11f.asset` is the walk contact tags, which did not move the plays; no rebuild means no new clip GUIDs while other tracks have the project open). The v0.10 database plays with the new matcher as is.

**Tests:** one EditMode run `-testCategory "!Slow"` in the private copy (git HEAD tests + this track's, Final IK bridge installed there): **1248 of 1248 passed** (`D:/PIXFootball/Work/Motion/tests4.xml`), including the four new tests in `MotionMatchingTests` (`TransitionTakesHaveNoSpeedPenalty`, `ForbiddenKindsAreNotSearched`, `StagingDatabasePathFollowsTheCommandLine`, `FinalIkDoorIsSafeWithoutABody`).

**How to repeat:** private copy `D:\PIXFootball\Work\Motion\PIX` (`bash sync5.sh`), `run.ps1 -Method ... -Log ...`. Database: `MotionDbBuilder.Build -pixMmCalib 1 [-pixMmDb Assets/_Licensed/Resources/PixMotionDb_x.asset]`. Positions + strips: `bash pos.sh <tag> "[-pixMmDb ...] [-pixLocoOnly sprint] [-pixLocoFilm 0]"`. Plays: `bash audit.sh <tag> "<flags>"`. Final IK: `LocoShots -pixLoco -pixLocoModes ik,ground`. 100STYLE: `Style100Import.Run -s100In a.bvh,b.bvh -s100Out Assets/X -s100Log cat.csv` in any project (`D:\PIXFootball\Work\Style100\run.sh`). Helpers: `Work\Motion\tools\{slide.sh, tcol.sh, posform.sh}`.
