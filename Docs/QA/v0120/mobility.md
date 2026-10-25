# v0.12.0 MOBILITY track (`mobility.md`)

Candidate 3: MoCap Online "Mobility 27B Pro" (the owner's, licensed, never committed) into the motion matcher. Private copy `D:\PIXFootball\Work\AnimLib\PIX` (= git HEAD + this track's files, because other tracks' half-done edits did not compile together: `headfix.sh` / `pushmine.sh` in `D:\PIXFootball\Work\AnimLib`). Before = the v0.11.0 database and code (`D:\PIXFootball\Renders\v0110_motion\fin1`, the same 36-play `EngineAudit`); after = the new database `PixMotionDb_v12.asset`. Plays: `EngineAudit` 36 plays (`D:\PIXFootball\Renders\v0120_mobility\<tag>`, tag `i` = the shipped database), treadmill: `LocoShots -pixLocoModes positions|mmtake` (`D:\PIXFootball\anim_loco\v0120\...`).

## 1. Summary (numbers first)

| Done criterion | before (v0.11.0) | after | verdict |
|---|---|---|---|
| Sprint stride warp x playback rate (plays, matcher frames, sim speed 6-7 / 7-8 m/s) | warp 1.116 x rate 1.075 / warp 1.170 x rate 1.144 (warp over 1.10 on 67 % / 98 % of the frames) | **warp 0.999 x rate 1.093 / warp 0.987 x rate 1.209** (warp within 0.95-1.05 on 80 % / 96 %; 4.3 steps/s at 7.5 m/s) | **met** |
| Sprint (>= 6 m/s) planted-foot slide per stance, matcher on (mean / median, m/s) | 0.037 / 0.018 | 0.051 / 0.035 (CB / LB / S bins 0.037-0.044 at 6-7.5, 7.5+: 0.040) | stays inside 0.03-0.06, **median doubled** |
| Run 4.5-6 m/s slide | 0.077 / 0.017 | 0.087 / 0.024 (typical stance < 1 m/s: 0.047 -> 0.047) | no change |
| Jog 3-4.5 m/s slide | mean 0.179, median 0.036 (typical 0.115) | mean 0.180, median 0.054 (typical 0.110) | **not met** (<= 0.10) |
| Shuffle 3-4.5 m/s slide | mean 0.206, **median 0.143** (typical 0.166) | mean 0.195, **median 0.054** (typical 0.106) | median met, mean **not met** |
| Back-pedal 3-4.5 m/s slide | 0.131 / 0.029 (typical 0.085) | 0.079 / 0.015 (typical 0.051) | better |
| Both-feet-air at sprint (both toes > 10 cm; treadmill 7.5 m/s; plays: share of floating frames that are flight) | 0.26 (plays 0.25) | 0.24 (plays 0.23) | **not met** (<= 0.20) |
| Walk / start takes chosen by the matcher (36 plays) | the library walk only (no selection log in v0.11; no start, stop or turn takes in the default database) | 806 entries of the pack's walks (`Walk_F/L/R/B`, 344 s of 1,597 s matcher time), 193 entries of its starts from standing (79 s) | **met** |
| Stop / turn takes chosen by the matcher | none | **not shipped**: each measured worse than the loops (section 5) | **not met** |

"Typical" = stances below 1 m/s; the mean is carried by 1-3 % of stances (3 frames flagged planted at 3-11 m/s: swing feet that skim the turf, take switches, `(tree)` frames where the matcher is parked). Median and typical are what the eye sees; the mean is what the audit reports.

Tests: EditMode `!Slow`: 1,327 run, 1,326 passed; the one failure was my own new test with a wrong count (fixed, class re-run 12 / 12 passed). Files changed: 7 (+ 1 new tool, 1 new test class addition), licensed content moved into `Assets/_Licensed/MocapOnline` (git-ignored).

## 2. What the package contains (inventory)

`Unity_Mobility_27B_Pro.unitypackage`, 538 MB, 552 entries, staged under `Assets/_MocapOnlineStage/Mobility_Pro` (never in the shared `_Licensed`).
- **Rig: Humanoid** (all 223 Root_Motion takes), every take copies the avatar of `MotusMan_v55` (shipped in the pack, own skeleton with `Root`, `Hips`, spine/arm/leg roll leaf bones, fingers). **30 fps** (`takeFps 30`, `clipFps 30`), loops 17-36 frames.
- Folders: `Animation/Root_Motion` 223 takes (the source used), `Animation/IPC` 223 in-place copies with `Speed` / `Rot(yaw)` custom curves (cm/s, degrees) and 63 split jumps, `Aim_Offsets` 18, `MotusMan` (mesh, not used), two PDFs (animation list, Unity notes), demo scene / controller.
- **Root motion:** both: the default Hips root and a true **reference `Root`** bone that stays under the body and always faces forward. Importer setting `motionNodeName = Root` (set by `MobilityImport`): a strafe keeps facing forward and travels at +-90 deg, the root path is smooth, the clip loops at a constant velocity.
- Takes (root speed from the sampled clips on our body; the planted foot says 5 to 12 percent less on the runs): walk loops (F, B, L, R, FL, FR, BL, BR, circles, back-pedal strafes) 1.07-1.40 m/s; jog loops 2.43-3.61 (F, B, L, R, 45 deg, 135 deg back-pedals, circles); **run loops 5.9-6.6 m/s (F 6.57 root / 5.76 planted-foot, L, R, FL, FR)**; crouch walks; starts from standing into walk / jog / run, straight and turning 45/90/135/180 (36 takes, 1.2-1.7 s); stops from walk / jog / run / strafes / back (34 takes: long, left-foot-up, right-foot-up); jog and run turns 90 / 180 on the move (8); in-place turns 45-180 (12); idles, jumps, hops, deaths, conversations (86).
- **There is no sprint.** The fastest take is `Run_F_Loop`: 17 frames = 0.57 s per two steps (3.5 steps/s), 1.63 m per step, 5.76 m/s planted foot. A 7.5 m/s sprint needs the cadence of 4.3 steps/s, i.e. a playback rate of 1.25 with the stride untouched: that is what the database now does (section 3). The full per-take table: `D:\PIXFootball\Work\AnimLib\PIX\Logs\motion\mobility_inventory.csv` / `.md`, `mobility_import.csv` (rig, fps, root node).

## 3. What changed

- **Import and conversion (`MobilityImport`, new):** importer settings (Humanoid, root node `Root`, no compression, single clip named after the file) and an inventory (`MixamoScan.Analyze` on every take on the match body). Retarget = Unity Humanoid MotusMan -> match body, exactly the Mixamo path; `MotionClean` (muscle re-zero per source group: the offsets of the MotusMan export differ from Mixamo's, soft limit) writes the cleaned / mirrored copies inside the package folder. The sampled floor of the pack is 0.00 m (Mixamo needed 0.11-0.15 m of lowering), so the retarget needs no floor fix.
- **Database (`MotionDbBuilder`):** `-pixMmSrc mix|mixamo|mobility` (default `mix`), pack groups `-pixMmMobSet walk,jog,run,strafe,back,trans` (default) plus opt-in `diag,bkpd,turn180,stops,cuts`, `-pixMmMixKeep` for the Mixamo takes that stay (the 2.4 and 2.7 m/s jogs, the 4.4 m/s run, the three back-runs: the pack has nothing there). Final database: **44 clips, 1,314 rows** (v0.11: 62 clips): `Walk_F`, `Jog_F`, `Run_F` loops; `Walk/Jog/Run L/R` strafes; `Walk_B`, `Jog_B`; starts `Stand_Relaxed_To_Walk_F / Jog_F / Run_F / Jog_L45 / Jog_R45`; each with its mirror. Contact tags are derived by the existing builder from the sole heights (enter 4 cm, leave 7 cm for fast takes); no hand tags were needed (sprint stances read 0.035 m/s).
- **Speed calibration:** the builder's planted-foot speed of the run was 11 percent too high (6.40 against 5.76 m/s). New `LocoShots -pixLocoModes mmtake` (every loop alone at 1.0 x and above, learning off, `-pixLocoCalibOut 1` writes `trim = believed / shown` speed) and `-pixMmCalibFile` (private calibration table; the shared `mm_calibration.csv` is untouched). `Run_F` trim 1.09 (the plays' own closed loop settled at 0.98 of the first estimate 1.114).
- **Cadence split (`MotionMatcher.RateFor`, `MotionDb.Clip.cadenceK`):** v0.11 shared a speed difference equally between playback rate and stride warp (`rate = sqrt(r)`). A long-stride take (1.6 m at 3.5 steps/s) now takes it as cadence (`k` 0.85 run, 0.6 jog, rate up to 1.36 = 4.8 steps/s) so its feet are not stretched. `cadenceK` = 0.5 for every older database, so v0.11 databases play unchanged.
- **QA tools:** `-pixMmSelLog file.csv` (how often every take was entered and how long it played, any run), `positions` table gets the matcher's stride warp / rate and the takes in use, `mmtake` table, `stance_clip.awk` / `trim.awk` / `lay.awk` (per-stance min slide by take, by speed band, matcher on or off) in `D:\PIXFootball\Work\AnimLib`.
- `MotionMatcher.SettleStops` (`-pixMmNoSettle`): a stop take ends when the body is already at rest. Only matters for the opt-in stops.

## 4. Per-take numbers that chose the set (plays, min slide per stance, m/s)

| take | before / after | decision |
|---|---|---|
| `Jog_F_Loop` (pack) | Mixamo jogs 0.13-0.23 | **0.085** (n 143) in; |
| `Run_F_Loop` | `Run_Fast_Run` 0.037 | 0.023-0.052 (4.5-6), 0.040 (6+) in; flight 0.21-0.25 against 0.26-0.33 |
| `Jog_L / Jog_R` strafes | Mixamo strafes 0.17-0.29 | 0.032-0.076 (treadmill drift +0.00..+0.02) in |
| `Jog_B_Loop`, back-runs | 0.058-0.125 | planted 0.04 on the treadmill (0.07) in |
| `Stand_Relaxed_To_Jog_F` / `To_Run_F` / `To_Walk_F` / `To_Jog_R45` | - | 0.125 / 0.098 / 0.085 / 0.024 in |
| `Walk_F_Loop` | library walk 0.20 | 0.18-0.28 (walking speeds, n 703): kept, slower than the library walk by 0.08 |
| Walk stop `Walk_F_To_Stand_Relaxed` | - | **0.52** (n 499, entered 559 times as the generic braking take): out |
| Jog / run stops | - | 0.65-1.97 out; 90 deg cuts 0.30-1.72 out; jog 180 0.46-0.82 out; **run 180 turns 0.14 to 11** (the take's root yaw fights the simulated one) out |
| `Jog_FL/FR`, `Run_FL/FR` 45 deg, `Jog_BR/BL_BkPd`, `Jog_L_BkPd` | - | 0.12-1.09, swing feet flagged planted at 7-11 m/s: out (opt-in `diag`, `bkpd`) |

## 5. What still looks wrong / not met

1. **Both-feet-air at sprint 0.24 (plays 0.23), not <= 0.20.** The pack's run is airborne 0.21-0.25 of the time on this measure; nothing in it is lower, and a real 8 m/s capture is airborne longer (v0.11: six levers tried, none moves it). The criterion cannot be met from the data without a faked pelvis; left at the honest value.
2. **Jog mean slide 0.18 (not <= 0.10).** The jog take itself is 0.085; the band is mixed with `Run_Medium_Run` (0.18, the only 4-4.5 m/s take), the library tree (0.5) and 1-3 % outlier stances. Typical stance 0.110 (0.115 before), median 0.054.
3. **Stop and turn takes are not shipped.** Every stop / cut / 180 of the pack slides (section 4): the simulation brakes in about 0.5 s while the takes are 1.7-3.2 s long and carry root motion, so the feet slide at the take's residual root speed once the body rests; the settle (`SettleStops`) helps the stop only a little (treadmill planted 0.46 -> 0.35). They need a plan-driven entry (brake distance and time, not nearest row) and a yaw-matched playback: v0.13.
4. **Treadmill planted-speed mean at run / sprint rose** (sprint 7.5: 0.30 -> 0.70, inner 60 percent 0.11 -> 0.24; jog 3.2: 0.13 -> 0.32, run 5.0: 0.27 -> 0.51) while the plays' per-stance minimum stayed 0.035-0.05. The window includes the skid frames of touch-down and lift-off and the low-clearance swing foot of the pack's takes; a window-based audit reads the pack worse than the stance-based one. Look at the strips (`D:\PIXFootball\anim_loco\v0120\final\pos_*_side.png`): sprint form is a real sprint (knee drive, forward lean, arms), feet land flat, no visible sliding at 1x.
5. **Arm swing is smaller** (sprint 0.67 -> 0.48-0.57 m, jog 0.45 -> 0.28-0.30): relaxed capture arms, not a ball-carrier's pumping arms.
6. **Freak planted flags** (3 frames at 3-11 m/s, 1-3 % of stances, in the shuffle and jog bands) come from `FootPlant`'s geometric planted rule on a swing foot that skims the turf (GAIT's file, `Loco/FootPlant.cs`): request below.

## 6. Requests / integration

- **Integration (database):** `Assets/_Licensed/Resources/PixMotionDb_v12.asset` is new, `PixMotionDb.asset` was not touched (other tracks run on the old one). To switch: copy `PixMotionDb_v12.asset` over the content of `PixMotionDb.asset` (keep its `.meta`), or run with `-pixMmDb Assets/_Licensed/Resources/PixMotionDb_v12.asset`. Its clips (cleaned pack takes, cleaned Mixamo takes) live in `Assets/_Licensed/MocapOnline/` (GUIDs kept; git-ignored, never commit). Build it again with `MotionDbBuilder.Build -pixMmCalib 1 -pixMmCalibFile <calib_v12.csv>` (table: `D:\PIXFootball\Work\AnimLib\calib_v12.csv`).
- **GAIT (`FootPlant`):** a swing foot flagged planted at 7-11 m/s on shuffle / strafe takes (section 5.6): ask the matcher's contact tag (`LeftContact` / `RightContact`) to veto the geometric rule when the tag says the foot is up. No hook added.
- **CONTACT / audit owners:** `MatchWatchAudit` can read the selections with `-pixMmSelLog <csv>` (static, no hook needed).
- Credit lines: none needed (commercial licence of the owner's purchase).

## 7. Files

Changed: `Gameplay/Player/Motion/MotionDb.cs`, `Motion/MotionMatcher.cs`, `Editor/Tools/MotionDbBuilder.cs`, `MotionClean.cs`, `LocoShots.cs`, `Tests/EditMode/Gameplay/MotionMatchingTests.cs` (+3 tests). New: `Editor/Tools/AnimLib/MobilityImport.cs` (+ `.meta`). Licensed (git-ignored): `Assets/_Licensed/MocapOnline/` (package + `Mobility_Pro/Clean`, `Mixamo/Clean`), `Assets/_Licensed/Resources/PixMotionDb_v12.asset`. One-line hooks in other tracks' files: none.

Repeat: `bash D:/PIXFootball/Work/AnimLib/pushmine.sh`; `unity.sh AnimLib <log> <s> -executeMethod PixFootball.EditorTools.MobilityImport.Run` (inventory); `MotionDbBuilder.Build -pixMmDb <asset> -pixMmCalib 1 -pixMmCalibFile ...`; `LocoShots.Run -pixLoco -pixLocoModes mmtake|positions -pixMmDb <asset>`; `EngineAudit.Run -pixEngineAudit -pixMmDb <asset> -pixMmSelLog <csv> ... -pixEngineAuditPlays 36`.

## 8. Hand-over check (end of pass)

Stage `Assets/_MocapOnlineStage/**` (with `.meta`, 528 FBX) copied to the shared `Assets/_Licensed/MocapOnline/` (folder meta kept), database to `Assets/_Licensed/Resources/PixMotionDb_v12.asset` (+ meta); the stage was deleted in the AnimLib copy (no duplicate GUIDs) and the copy re-opened through the `_Licensed` junction: compiles, `MotionBench` loads 1,314 rows / 44 clips, the sprint 7.5 treadmill case reproduces the numbers above (warp 0.96, rate 1.23, flight 0.24). Confirming `MatchWatchAudit` (40 plays, seed 110110, 51,013 frames, 17.5 min wall, no exceptions): `D:\PIXFootball\Renders\match_watch_v0120\mobility` (`mm_selections.csv` there). `EngineAudit` runs of every candidate database: `D:\PIXFootball\Renders\v0120_mobility\{b,c,e,f,g,h,i}`.
