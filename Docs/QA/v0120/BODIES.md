# BODIES track log (v0.12.0 wave 2, owner notes 2 and the helmet part of 3)

Owner notes: "a lot of bugs on the pads", "make the pads a little smaller", players should look like Madden 27 / real life (the Burrow model looked more lifelike than the field bodies), bigger builds for OL / DL, "things missing on the helmets". Body / rig / look only: no real likeness, no team or league marks (`PhotoPlayerTests.NothingNamedAfterTheRealPlayerOrTheTeamShips` stays green).
Private copy: `D:\PIXFootball\Work\SimFlow\PIX`. Renders: `D:\PIXFootball\Renders\v0120_bodies\` (`before\`, `after\`, `r1\`, the contact sheets `after_contact_c*.png` and `R1_vs_field_*.png`). Bug list with the status of every row: `D:\PIXFootball\Work\v012\body_bugs.md`. Nothing committed.

## 1. Numbers (15 roster bodies, LOD0, same studio, same seeds; live prefabs before vs after the promotion)

| Measure | before | after |
|---|---|---|
| Pad width (Pads part, bind pose), S / M / L | 0.774 / 0.841 / 0.893 m | **0.681 / 0.719 / 0.813 m (-12 % / -15 % / -9 %)** |
| Pad volume, S / M / L | 6.4 / 7.7 / 10.4 L | **5.6 / 6.7 / 9.1 L (-12 % / -13 % / -13 %)** |
| Pad depth / height | 0.32-0.39 x 0.39-0.41 m | 0.30-0.39 x 0.38-0.40 m (-4 % / -2 %: a padded silhouette, not a thin one) |
| Chest width incl. pads (BodyProportions), S / M / L | 0.453 / 0.515 / 0.586 m | 0.426 / 0.481 / 0.566 m (-6 % / -7 % / -3 %) |
| Waist width / thigh diameter, S, M | unchanged (0.36 / 0.22, 0.40 / 0.26) | unchanged: no thin waists or legs |
| Waist width / thigh diameter, Large (OL / DL) | 0.47 / 0.32 | **0.48 / 0.33-0.34 (+2 % / +5 %: bigger and heavier)** |
| Pad vertices that come out of the jersey in poses (24 poses: catch, throw, tackle wrap, block, swim, sprint, 3-point, celebrate), mean new per pose, S / M / L | 78 / 85 / 113 | **37 / 25 / 46 (-52 % / -70 % / -60 %)** (all 15: 92 -> 36) |
| Arm-skin vertices newly exposed near the shoulder, mean per pose, S / M / L | 66 / 97 / 128 (5.3 / 7.1 / 8.0 % of the zone) | 59 / 69 / 154 (4.7 / 5.1 / **9.3 %**) : -12 % / -29 % / **+21 %** |
| Helmet shell / face mask / chinstrap / helmet pad triangles at LOD0 | 8,999 / 6,500 / 2,499 / 1,600 | 11,499 / 9,499 / 2,799 / 1,900 (LOD2: 1,979 -> 2,528 shell, 1,430 -> 2,088 mask) |
| Bodies with a missing helmet part (shell, face mask, chinstrap, pad at all 4 LODs, renderer + mesh + triangles + near the head bone) | 0 / 15 | 0 / 15 |
| Triangles per body LOD0 / LOD1 / LOD2 / LOD3 (Large Std) | 74.4 k / 35.0 k / 15.0 k / 6.9 k | 80.5 k / 38.6 k / 16.8 k / 7.6 k (test budget 90 / 42 / 19 / 10 k) |

Probe: `Editor/Tools/BodyShots.cs` (new; `-pixBodyShotsWhat metrics,shots`, `-pixBodyShotsOnly <prefix>`, `-pixBodyShotsStage 1`); numbers in `before\beforeM_metrics.csv|txt`, `after\live_metrics.csv|txt`, `before\before_proportions.txt`, `after\after_proportions.txt`.
"New exposure" means a pad vertex that is under the jersey at rest (4 mm margin) and outside it in the pose, an arm-skin vertex within 0.28 m of the shoulder joint that was inside the jersey / pad at rest (6 mm) and is outside by 2 mm in the pose. Nearest-vertex normals, not a watertight test: good for before / after, not an absolute count of visible defects. The armpit side of a raised arm legitimately shows skin, so the skin number has a floor.
Note on the stage renders: measurements taken on the stage prefabs (before the promotion) read lower than the live ones (pad poke 15 vs 22-62, skin zone 1,589 vs 1,233 vertices) because the stage avatar poses differently; every number above is from the live prefabs after the promotion.

## 2. What was wrong and what changed

| Row (`body_bugs.md`) | Cause | Fix |
|---|---|---|
| P1 pads too big | `phr2_pads.FIT` (width scale 1.03, caps 0.45) | `sx` 1.03 -> 0.93, `sy` 0.86 -> 0.82, `sz` 0.88 -> 0.865, caps 0.45 -> 0.40: width -12 %, volume -13 % on the live meshes |
| P2 pad vertices through the jersey | pad skin weights (rigid-ish, 62 % arm) differ from the jersey's (blended only where the conform pushed) | `phr2_pads.pads_follow_jersey`: every pad vertex within 5 cm of the cloth takes the inverse-distance weights of its 4 nearest jersey vertices (2,114 / 2,426 vertices); the neck roll keeps its own |
| P3 skin through the cloth | arm skin follows the upper arm 100 %, the jersey dome only partly | not solved: S / M down 12 / 29 %, Large up 21 % (bigger sleeve cap relative to the arm after the pad shrink). Known issue |
| P4 / P5 boxy dome, torn trim | oversize pad envelope | smaller envelope (P1); the sleeve trim shards at LOD1 / LOD2 are L1 |
| H1 helmet detail halved against R1 | LOD0 budgets 9.0 k / 6.5 k vs R1 18.8 k / 14.4 k | `phr2_build.py`: 11.5 k / 9.5 k, LOD floors [3400, 1800, 800] / [3200, 1800, 800] so the face mask bars survive LOD2 |
| H2 "missing parts" | not found as a defect: the metric shows 0 / 15 bodies with a missing part; vents show the bare head, the crest only exists in the match (PixLook.ApplyHelmet) | listed (no inner liner built; see Known issues) |
| H3 helmet gloss | PIX/Lit hard-codes `lerp(smooth, 0.9, shellW)` at `PixLitHooks.hlsl:381` (graphics-owned): the material numbers have no effect | Request below |
| L1 jersey LOD trim shredding | 1,800 / 900 / 600 triangles for a 3,491 triangle jersey | LOD floors 2,300 / 1,300 / 800 (LOD1..3) |
| M1 fabric dot grid | `_PixFabric` tiling 70, strength 0.55 | 52 / 0.36 and smoothness remap max 0.46 on `PHR_Jersey` / `PHR_Pants` (`PhotoBodiesStage.ApplyMaterialNumbers`, applied to the shared assets at the promotion) |
| B1 / B2 bigger OL / DL | Large pads swallowed the silhouette | `phr2_build.BULK`: Large torso width x1.045, depth x1.04, legs x1.05 (arms left alone: x1.04 made the skin exposure worse). `ChooseBody` mapping unchanged (QB / WR / DB lean, RB / LB stocky, OL / DL Large; tests pin it) |

How it was built: `Tools/Blender/phr2_build.py <Size> <Base> --out Assets/_BodiesStage/Meshes` (26 s per body, 15 bodies), `PhotoBodiesStage.BuildStage` (stage prefabs through `PhotoRosterV2.BuildPrefab`), renders / probes with `-pixBodyShotsStage 1`, then `PhotoBodiesStage.PromoteBatch` copied the 15 stage FBX over the live `PHR_*.fbx` (same files: GUIDs and prefab references hold), rebuilt the 15 live prefabs in place, applied the material numbers and ran `CriticalAudit.EnsureRoster`. The stage folder was removed from the copy afterwards. Rollback copy of the old meshes / prefabs / materials: `D:\PIXFootball\Work\v012\bodies_backup\`.

## 3. Verification
* `CriticalAudit.Prefabs` on the promoted prefabs: 15 prefabs, human avatars, 23 parts, 0 null bones / bind pose mismatches (the 15 "problems" are the known tiny Eyes bounds, padded at runtime).
* EditMode: see section 6.
* Contact sheets: `D:\PIXFootball\Renders\v0120_bodies\R1_vs_field_*.png` (R1 first, then the 15 bodies; 3/4 view, shoulder close-up, helmet front, arms-up).

## 4. Still wrong (said plainly)
1. The pads are smaller, not R1-sized: a field body still has a clearly padded shoulder (width 0.68-0.81 m); R1's pad bump is about half of that. The owner asked for "a little smaller" and "never thin".
2. Arm skin still leaves the cloth near the shoulder in 5-9 % of the zone in poses (Large 9.3 %, worse than before).
3. Helmet gloss is a shader constant (0.9) and the crest decal only shows in the match; vents still show the bare head.
4. Jersey / head LOD artifacts (L2 head LOD1 cheek band) not touched.
5. Hip / head over-rotation (N1-N3 in `body_bugs.md`) is NATURAL's.

## 5. Requests
* GFX (PixLit): helmet smoothness is `lerp(smooth, 0.9, shellW)` at `PixLitHooks.hlsl:381`; a 0.65-0.7 ceiling reads as R1's satin shell.
* NATURAL: `SimAgentView.Natural.cs` lacked `using PixFootball.Sim;` when I synced (my copy was patched by hand; the shared file has it now).
* Integration: the shared `Assets/_Licensed/PhotorealPlayer/{Meshes,Prefabs,Materials}` content changed (15 FBX, 15 prefabs, `PHR_Jersey.mat`, `PHR_Pants.mat`); other tracks' open editors need a refresh, their private copies see it through the junction.

## 6. Files
New: `Editor/Tools/BodyShots.cs`, `Editor/Assets/PhotoBodiesStage.cs`. Changed (Blender, outside Assets): `Tools/Blender/phr2_pads.py` (FIT, `pads_follow_jersey`), `Tools/Blender/phr2_build.py` (budgets, bulk, call). Changed (git-ignored): 15 FBX, 15 prefabs, 2 materials under `Assets/_Licensed/PhotorealPlayer/`.

Test result: one EditMode run on the private copy (`-testCategory "!Slow"`, shared tree at 11:10 incl. the other tracks): **1370 tests, 1369 passed, 1 failed** (`NaturalTests.FollowNeverExceedsTheRate`, NATURAL's in-progress file; no body / art / roster / photoreal test fails, `NothingNamedAfterTheRealPlayerOrTheTeamShips` and the triangle budget tests pass). Confirming `MatchWatchAudit` (40 plays, seed 110110, `D:PIXFootballRendersmatch_watch_v0120odies`): completed, no exceptions; issue list shows only the known replay-hidden `VIS_*` rows, 0 missing parts / T poses. In the anomaly close-up (`anomalySTANCE_LINEMAN_TOO_UPRIGHT_DL_1_p32.png`, DL #95) the new Large body reads as a normally padded lineman (no wing shoulders, no torn trim). Perf: triangles +8 % at LOD0 (80.5 k), +12 % at LOD2; not re-measured in a built player (budget 90 / 42 / 19 / 10 k holds).

## 7. Follow-up (coordinator S1-visual round)
1. Black / white shards at the outer shoulder: not the pads (hidden `Pads` still showed them): the jersey Team mask had two sleeve-end bands (`phr_textures.py`, `band2` / `band3`, 1.4-4.8 cm from the shoulder joint) that land on the pad dome as wedge-shaped slabs. Bands switched off (`SLEEVE_BANDS=0`, env `PHR_TEX_OUT` for a private output folder), only `jersey_Team.png` changed (Gray / Normal / Regions byte-identical), promoted to the live texture. Result: no shards on any of the 15 bodies (`R1_vs_field_3_shoulder_v2.png`). Collar trim kept.
2. Chin cup: `phr2_build.shrink_chin_cup` (width x0.72, height x0.70, bottom kept) and a Chinstrap material of its own in `PhotoPlayer.Init` (matte neutral grey 0.30, smoothness 0.18; the cheek / forehead pads keep white). Cup front width 0.117 -> 0.085 m. `R1_vs_field_4_helmetfront_v2.png`.
3. Helmet gloss (cross-track edit): `Assets/_Project/Shaders/PixLit/PixLitHooks.hlsl`, helmet branch only (mode 3, lines 380-381): metallic 0.35 -> 0.12, smoothness 0.9 -> 0.66; `PHR_Helmet.mat` smoothness 0.9 -> 0.66, remap max 0.96 -> 0.72, metallic 0.2 -> 0.1.
4. Large arm-skin exposure: not fixed. Large arms x1.04 / x1.0 / x0.95 all gave 154 +- 5 vertices per pose (9.3 % of the zone): the cause is the jersey cap weights, not the arm thickness. Left at x0.95 (arms still x1.33 against Medium x1.16). Known issue for v0.13.
Correction to `body_bugs.md` L2: the "brown band across the cheek / eyes" is the smoke visor option of some seeds, not a head LOD defect.
Tests (only body / photo classes): `PhotoPlayerTests` + `PhotoRosterV2Tests` + new `BodiesV012Tests` (pad width limits S 0.74 / M 0.80 / L 0.87 m, chin cup width and own matte material, helmet parts with triangles at all LODs): 17 / 17 + 3 / 3 passed. Rollback: `D:\PIXFootball\Work\v012\bodies_backup\` (meshes, prefabs, `jersey_Team.png`, materials of v0.11). Renders: `D:\PIXFootball\Renders\v0120_bodies\R1_vs_field_{3_shoulder,4_helmetfront}_v2.png`. New live metrics: `live2\v2_metrics.csv` (pad width S/M/L 0.681 / 0.719 / 0.812 m, pad poke 37 / 25 / 45, skin poke 59 / 69 / 154).
