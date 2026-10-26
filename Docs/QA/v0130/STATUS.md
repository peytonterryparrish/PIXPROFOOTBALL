# v0.13.0 "Mocap" status (integration, 2026-10-25)

Scope: the owner's v0.12 playtest (`PLAYTEST_v0120.md`): "players still move like robots", "so much motion capture is missing" (use every supplied file, cleaned and working), bodies / stances / skeletons / heads, the QB clips barely used, and **at least 300 new mocap clips in the next build**. During the round the owner added: "i want all of this fixed or added in this build" (every wave-1 shortfall) and "another 100 more mocap pieces" from the files. Step 0 measured coverage (`MOCAP_COVERAGE.md`); wave 1 ran QB-MOCAP, ANIMLIB-BULK, NATURAL-2, STANCE-HEAD; wave 2 ran QB-2, BULK-2, NATURAL-3, BODIES-2 (+ one targeted follow-up each for QB-2, BULK-2, STANCE-HEAD, BODIES-2). Logs next to this file; running notes in `INTEGRATION_NOTES.md`.

"New clip" = a clip name in a game's `clips_played.csv` (the new clip recorder, `ClipUsage`) that is not in the step-0 baseline game (v0.12.0 tree, seed 110110, 364 distinct clips). Numbers are from each track's full game (seed 110110) unless marked "final".

## Final integrated tree (this commit)
| Check | Result |
|---|---|
| EditMode `!Slow`, merged tree | **1,413 / 1,414** first run; the failure was a v0.7 floor in `PhotoRosterV2Tests` (padded jersey > 0.68 m) that BODIES-2's narrower shoulders broke on purpose (Small 0.638 m): floor moved to 0.62 m, class re-run 7 / 7 -> **1,414 / 1,414** |
| Build | `PIXFootball_v0.13.0_Windows`, 3,067 MB, 0 errors; zip 1.25 GB (`tar`, zip64: `resources.assets.resS` is 2.15 GB, too big for `Compress-Archive`); autotest logs moved to `D:\PIXFootball\Work\v013\autotest_v0130` |
| Autotest of the build (`-pixAutotest -pixAutotestMeasure -pixAutotestMatchOnly -pixAutotestNoV3`, 2560 x 1440, High, DLSS auto, RTX 5090 laptop, nothing else on the GPU) | **145.5 FPS average, 113.0 1 %-low** (5 % low 125.9, min 30.5); v0.12.0 144.6 / 110.7; 0 errors / exceptions. This run was fullscreen (v0.12's was windowed). Screenshots checked once (hand-off close-up, pre-snap): bodies, facing and the new uniforms read right; the hand-off shows the QB with the ball high as the back passes, and a grey side panel on the pants. |
| MatchWatchAudit, one full game (seed 110110, Buffalo v Miami, 178 plays, 6-42; `D:\PIXFootball\Renders\match_watch_v0130\final`) vs the step-0 baseline (v0.12.0 tree, 168 plays) | **945 distinct clips played (364), 649 new, 209 of them QB clips** (by system: QB sequence 207 + 11 mesh, matcher 189, ApplyMatch 128, engage 58, huddle 45, sideline 32, booked contact 20). HAND_OR_HEAD_BELOW_TURF 128 -> **2**; FEET_BELOW_TURF 1,098 -> **48**; SUNK_HIPS 178 -> **22**; FLOAT_GROUND_ACTION 82 -> **9**; FLOAT_STANDING_BOTH_FEET_UP 246 -> **125**; POP_CHEST_ROT 202 -> **43**; POP_HIPS_ROT 975 -> 918; standing FACE 1,385 -> **55** (huddle bodies no longer counted; old rule kept); STANCE_LINEMAN_NO_HAND 301 -> **4**; FACE_RUNNING 38 / 32 -> 34 / 25. **Worse:** POP_HIPS_JUMP 490 -> 580 (+18 %), OVERLAP_STANDING_OR_BLOCKING 16 -> 35, CAM_JUMP 351 -> 397, BALL_OFF_HANDS 42 -> 48, STANCE_UPRIGHT_TOO_BENT 24 -> 35. |

## The owner's notes
| Note | Result | Status |
|---|---|---|
| >= 300 new mocap clips, cleaned and tested | final game: **649 new clips played** (945 distinct, baseline 364); every new clip passed `ClipQa` / the engage QA or the matcher's per-take QA before it was registered | **Done** |
| Go through EVERY supplied mocap file | `MOCAP_COVERAGE.md`: 9,766 unique takes found (4,192 football; 5,152 weapon / soccer and 422 crowd takes are non-football). Imported this round: 110 MC Idles, 86 + 115 BoNES day-2 / day-3 raw takes (Blender split / retarget), 15 QB day-2 / day-3 FBX, 5 MC Core celebrations, 63 100STYLE slices, more Mobility Pro / Mixamo takes in the matcher. Per-group reasons for what is still unused: `bulk.md` "Wave 2b", `qb.md`, `natural2.md`. | **Partly**: every group was inventoried; the rest is duplicates, bad data or has no engine slot (listed) |
| QB: many QB clips, only a couple used | 10 QB clips played at baseline -> **198 QB clips (193 new)** in QB-2's game. On mocap: pre-snap stance, cadence and snap (under centre and gun, a different take each play), pocket / ready / reset, throws by family with the ball released on the clip's release frame (17 / 17 and 5 / 5 at 0 frames), play fakes, hand-off (QB side), roll-out, spike. QB stance compliance 65 -> 91.3 %. | **Partly**: drops, scrambles, kneel, slide, sneak stay on poses (below) |
| Players move like robots | Matcher database 44 -> **248 clips** (Mobility Pro, 100STYLE, Mixamo; 192 new selected in a game); per-player take preference; jog planted slide 0.245 -> **0.094 m/s**; chest-twist pops per play 2.19 -> **0.22** (chest rate cap); still standing bodies with no pose change over 2 s -92 % (Mobility idle layer); neighbours in step 27 -> 20-22 % (chance 20) | **Done** (neighbours at the chance level, within run-to-run noise) |
| Stances | OL compliance 46.8 -> **99.8 %** (the three-point stance had no hand down), overall 99.7 % | **Done** (QB 91.3 %, holders on field goals fail) |
| Heads too turned | head over chest in free frames p95 / p99 42 / 48 deg (limit 48 deg at 380 deg/s, contact takes covered; v0.12 p99 60 on the same measure) | **Done** |
| Bodies / skeleton | feet sinking below the turf 1,098 -> 18-49 per game (toe-down feet, `EngageStepper.FootGuard`); arm skin outside the cloth 9.3 -> 1.2 %; jersey width at the shoulder vs R1 +20 / +24 / +38 % -> +12 / +17 / +26 %; sculpted folds, seams, mesh panels and knit on jersey, pants and sleeve | **Partly**: not yet R1-level (cloth is static, shoulders still wider than R1) |
| Another ~100 clips from the files | NATURAL-3: 66 / 66 played (Mobility stops / starts, 100STYLE); BULK-2: 4 + 2 contested-catch clips | **Partly** (72): the Yan backup / Retargets takes are mostly copies of imported takes; the Asset Store movement packs were not imported |

## Wave 1 (`qb.md`, `bulk.md`, `natural2.md`, `stance.md`)
| Track | Result |
|---|---|
| QB-MOCAP | 182 new QB clips; stance 97.4 % scrimmage; release 17 / 17 at 0 frames; drops / PA / hand-offs / scrambles stayed on poses |
| ANIMLIB-BULK | 177 new clips; caps lifted (rows 1,805 -> ~3,000), no-repeat bag per key, `catch_fail`, `cb_shuffle`, MC Idles, BoNES day-2 / 3 (86 takes); the agent was stopped by a tooling outage during its game (results filled from the game) |
| NATURAL-2 | DB 44 -> 182 clips, 132 new; idle micro-motion; jog slide and chest pops not met |
| STANCE-HEAD | stance 99.7 %, heads p95 / p99 42 / 48, FEET_BELOW_TURF 1,098 -> 49; skeleton table |
| Integration 1 (merged wave 1) | 623 distinct clips, 312 new; found S1: `QbClipTable.cs` had no `.meta` in the shared tree, so the QB catalog asset had a missing script in other copies and the QB fell back to poses (3 of 182 QB clips played) -> metas for all 22 new scripts written |

## Wave 2 (owner: "i want all of this fixed or added in this build")
| Track | Result | Status |
|---|---|---|
| QB-2 | fakes, hand-off (QB side), roll-out, spike on mocap; fitted mocap drops slid 0.40 m/s (gate 0.15) -> switched off by default (`-pixQbDropMocap`), pose drops; stance 91.3 % (target 95); standing QB FACE 109 (101); QB hip-jump issue lines 18 (wave 1: 7) | **Partly** |
| BULK-2 | 115 more raw day-2 / 3 takes split and retargeted; PixContact 113 -> 159 pairs (contested catch, strip); HAND_OR_HEAD_BELOW_TURF 235 -> 3-8, FLOAT_STANDING 288 -> 93-119, SUNK_HIPS 233 -> 17-18 (all under baseline); own new clips 262 | **Mostly** (+100 own clips not met; the new BigHit pairs are QA rejects) |
| NATURAL-3 | jog slide 0.094, chest pops 0.22 per play, DB 248, 66 extras | **Done** |
| BODIES-2 | skin / shoulder fixes, cloth detail, ribbing reviewed and removed | **Partly** (R1 not reached) |
| STANCE-HEAD follow-up | standing FACE was the detector (96 % huddle bodies facing the huddle centre): 1,385 -> 40; huddle-ring check added | **Done** |

## Known issues (v0.14)
- QB drops (all depths), scrambles / roll-out starts by trigger, the pocket side-step, kneel, slide and sneak are still keyed poses: the drop takes slide when fitted to the sim's drop (needs a foot lock on the fitted take); the supplied kneel / slide / sneak takes are unusable (the actor never kneels: hips >= 0.82 m; slide >= 0.69 m; sneak static).
- QB stance compliance 91.3 % (65 % at the start, 97.4 % after wave 1); QB hip-jump issue lines 18 a game.
- The hand-off take starts about 0.3 s before the exchange; the pose layer's open step shows before it.
- Every `ol_block_engage` / `ol_block_cut` take was rejected by the engage QA (sunk hips / toes up): blocks fall back to pass-set / push / fail pairs.
- The new BigHit pairs are QA rejects; contested catches are booked but rare (1 pair drawn in a game).
- Bodies: cloth is sculpted (static, does not move with the pose), shoulders still wider than R1, arm-up stretch 618 edges (v0.12 554), hip-flex seat stretch untouched.
- 90 / 180 turn and cut takes dropped from the matcher (they fly); neighbours in step 20-22 %.
- Not imported: MC Core Motion (581), Running Motions Pack 2 (52), Movement Animset Pro (8) Asset Store packs; ~250 day-2 / 3 pair takes that failed QA or duplicate earlier takes.
- `ClipFacing` stale entries are rebuilt by the library rebuild; the library rebuild drops the 12 roster prefabs (`CriticalAudit.EnsureRoster` after every rebuild; the build does it too).
- Final game: hip jumps over 10 cm +18 % (490 -> 580), standing / blocking overlaps 16 -> 35 (QB-vs-centre pre-snap samples), camera jumps 351 -> 397, ball off both hands 42 -> 48.
- The pants' side mesh panel reads as a flat grey patch in close-ups.
