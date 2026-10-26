# BODIES-2 log (v0.13.0 wave 2): cloth-quality uniform, shoulders, shoulder skin

Owner (2026-10-25): "Bodies and jerseys still need new art (cloth folds, shoulder skin)"; bar = the supplied R1 body (body / rig / look only, never the face or likeness, no team marks).
Private copy `D:\PIXFootball\Work\Bodies\PIX`. Everything was built in the private stage `Assets/_BodiesStage/` (excluded from sync); **nothing under `Assets/_Licensed/PhotorealPlayer` was written**. Nothing committed.
Renders / numbers: `D:\PIXFootball\Renders\v0130_bodies\` (`before\`, `after3\` = final stage (after the review fix; `after2\` = first pass), `diag\` = experiments, contact sheets `R1_vs_field_*.png`, `before_vs_after_shoulder.png`, `shoulders_{before,after}.csv`). Work files: `D:\PIXFootball\Work\v013\bodies2\` (`table.md` = the table below).

## 1. Result against the done-criteria (15 bodies, LOD0, same studio / seeds / poses as v0.12: 8 clips x 3 times)

| criterion | before (live v0.12 bodies) | after (stage) | |
|---|---|---|---|
| Large arm skin newly outside the cloth near the shoulder, % of the zone, per pose (mean) | **9.3 %** (L 147-160 verts of ~1,600) | **1.3 %** (L 16-28 verts); S 4.7 % > 1.3 %, M 5.1 % > 0.9 % | **met** (< 2 %; worst single body 1.7 %) |
| pad vertices out of the jersey, new per pose (mean of 15) | 35.7 | 25.7 | better |
| shoulder width, jersey + pads at the shoulder-joint height, mean S / M / L (R1 = 0.572 m) | 0.684 / 0.709 / 0.790 m (+20 % / +24 % / +38 % over R1) | **0.639 / 0.672 / 0.723 m (+12 % / +17 % / +26 %)** | narrower by 6-8 %, still wider than R1 (see 4) |
| pad width (Pads part, bind pose), mean of 15 | 0.737 m | 0.673 m (-9 %); S 0.607-0.630, M 0.647-0.668, L 0.734-0.758 | |
| cloth: folds / wrinkles / seams / mesh / AO on jersey, pants, compression sleeve | soft noise + dot lattice, no seams | sculpted folds, seams, side mesh panels, baked AO / cavity (section 2) | **done**, not R1-level |
| arm-up stretched skin / sleeve edges (BodySkeletonCheck, mean of 15) | 554 | **618 (+12 %)** (18-edge spread gave 811, the 30-edge spread 618): cost of the under-cloth weights (section 3) | slightly worse, hidden skin under the cap |
| hip-flex stretched edges (seat) | 223 | 223 | **not fixed** (no work done on the hip weights) |
| trunk-fold stretched edges | 117 | 165 (the Blender rebuild with the narrower pads; Large 150 > 205-259) | worse |
| triangles LOD0 / LOD1 / LOD2 / LOD3 (Large Std) | 80.5 k / 38.6 / 16.8 / 7.6 | 80.5 k / 38.6 / 16.8 / 7.6 | unchanged (test budget 90 / 42 / 19 / 10 k) |
| textures | jersey / pants 2048 (Gray, N, Team, Regions) | the same files and sizes, new content in Gray + N (+ armsleeve Gray / N) | no new texture, no new sampler |
| FPS | v0.12: 144.6 avg / 110.7 1 %-low | **not measured**: triangle, texture and shader cost are unchanged (no shader edit; only `_NormalScale` 0.5 > 1.0 on the jersey and pants materials, a constant) | held by construction, not by a run |

Full 15-body table (BodyProbe: `BodyShots` metrics + `BodySkeletonCheck`), before > after:

| body | pad width m (before > after) | pad verts out of jersey / pose | arm skin newly outside the cloth / pose (zone pct) | arm-up stretched edges | hip-flex | trunk fold | shoulder width m (R1 0.572) |
|---|---|---|---|---|---|---|---|
| small | 0.667 > 0.607 | 22.6 > 28.9 | 59.0 > 15.5 (4.8 % > 1.3 %) | 433 > 624 | 188 > 188 | 99 > 133 | 0.666 > 0.629 |
| medium | 0.708 > 0.650 | 21.6 > 24.5 | 69.7 > 8.7 (5.3 % > 0.7 %) | 539 > 605 | 220 > 220 | 100 > 131 | 0.698 > 0.659 |
| large | 0.802 > 0.734 | 40.8 > 23.0 | 147.3 > 23.6 (9.2 % > 1.5 %) | 687 > 550 | 273 > 273 | 155 > 223 | 0.778 > 0.714 |
| small_stocky | 0.707 > 0.630 | 62.5 > 16.3 | 57.2 > 20.1 (4.8 % > 1.7 %) | 389 > 674 | 186 > 186 | 111 > 130 | 0.692 > 0.647 |
| medium_stocky | 0.763 > 0.668 | 41.5 > 25.7 | 69.5 > 15.5 (5.3 % > 1.2 %) | 518 > 631 | 211 > 211 | 100 > 133 | 0.736 > 0.684 |
| large_stocky | 0.846 > 0.757 | 46.5 > 22.4 | 146.8 > 17.5 (9.3 % > 1.1 %) | 669 > 613 | 277 > 277 | 151 > 205 | 0.810 > 0.729 |
| small_athletic | 0.675 > 0.615 | 26.5 > 30.5 | 65.8 > 14.2 (4.8 % > 1.0 %) | 476 > 676 | 152 > 152 | 78 > 114 | 0.689 > 0.642 |
| medium_athletic | 0.708 > 0.661 | 18.1 > 24.6 | 74.6 > 12.5 (5.0 % > 0.8 %) | 624 > 636 | 176 > 176 | 90 > 123 | 0.705 > 0.662 |
| large_athletic | 0.809 > 0.748 | 49.9 > 35.5 | 160.0 > 16.3 (8.9 % > 0.9 %) | 622 > 636 | 229 > 229 | 149 > 259 | 0.793 > 0.725 |
| small_tall | 0.690 > 0.617 | 44.1 > 37.1 | 55.2 > 14.5 (4.4 % > 1.2 %) | 430 > 659 | 198 > 198 | 111 > 139 | 0.701 > 0.646 |
| medium_tall | 0.713 > 0.667 | 19.5 > 20.0 | 68.2 > 12.0 (5.0 % > 0.9 %) | 584 > 658 | 224 > 224 | 97 > 137 | 0.710 > 0.685 |
| large_tall | 0.807 > 0.758 | 42.4 > 16.4 | 155.2 > 27.4 (9.5 % > 1.7 %) | 682 > 548 | 287 > 287 | 151 > 232 | 0.794 > 0.734 |
| small_runner | 0.665 > 0.605 | 30.4 > 30.3 | 56.4 > 18.4 (4.5 % > 1.5 %) | 399 > 607 | 200 > 200 | 105 > 133 | 0.670 > 0.630 |
| medium_runner | 0.704 > 0.647 | 24.7 > 25.7 | 64.8 > 14.1 (4.8 % > 1.0 %) | 574 > 595 | 226 > 226 | 106 > 148 | 0.697 > 0.668 |
| large_runner | 0.797 > 0.738 | 44.0 > 25.0 | 159.2 > 19.5 (9.8 % > 1.2 %) | 689 > 560 | 304 > 304 | 155 > 235 | 0.777 > 0.714 |
| **mean of 15** | 0.737 > 0.673 | 35.7 > 25.7 | 93.9 > 16.7 (6.35 % > 1.17 %) | 554 > 618 | 223 > 223 | 117 > 165 | 0.728 > 0.678 |

Metric definitions: `BodyShots.Metrics` (v0.12 log section 1): arm-skin vertices within 0.28 m of the shoulder joint that were > 6 mm under the jersey / pads at rest and are > 2 mm outside in the pose, nearest-vertex normals (not a watertight test; the armpit of a raised arm legitimately shows skin, so there is a floor). Stretch = triangle edges > 1.7x or < 0.4x their bind length in four extreme poses (`BodySkeletonCheck`, STANCE's probe, hooked for `-pixBodyProbeStage 1`). The "before" numbers were measured again in this copy on the live bodies (same code, same poses), not copied from older logs.

## 2. Cloth: what was built (nothing is copied from R1's own jersey maps)

Method: **sculpted height field, not a Blender cloth sim.** `Tools/Blender/phr3_cloth.py` (new) evaluates the folds as functions of the 3D rest position and the skeleton landmarks (armpit, shoulder joint and arm axis, waist, hip, knee, elbow, wrist) at every texel of the existing UV layouts (the UV is position based, so one map set lands on all 15 bodies). The height (metres) is turned into a tangent-space normal with the true metres-per-texel of each island (a 2 mm fold is 2 mm everywhere), plus a cavity / AO multiplier baked into the grey albedo. A real cloth sim of 15 bodies in the 3 h budget was not attempted; the folds are authored, they are not simulated, so they do not react to the pose.
* **Jersey** (`jersey_N`, `jersey_Gray`): armpit tension folds fanning over chest and back (2 sets), a short irregular gather only where the jersey tucks at the belt (review fix: the even horizontal bands over the stomach read as a quilted jacket: amplitude -70 %, spacing and direction varied by noise, broken into patches, faded out over the stomach, the belly hang folds removed, back hang folds halved), shoulder / pad-edge drape (kept soft after the first pass looked like ropes on the cap), trapezius drape from the collar, hem ripple and stitch row with dashes (waist and sleeve ends), flat-felled side seam, armscye seam, ventilated side mesh panels (bigger diamond mesh, a little darker), fine knit, AO in the armpit pocket, waist tuck, seams and fold valleys.
* **Pants** (`pants_N`, `pants_Gray`): creases above the kneecap and V creases behind the knee, groin fan, seat folds, thigh tension folds, waistband ribs + two stitch rows + gather under the band, hem stitch, inseam and the sewn edges of the side-stripe insert (same iso-line as the colour), twill, AO at groin / back of knee / hem.
* **Compression sleeve** (`armsleeve_N`, `armsleeve_Gray`): elbow crook folds, wrist rings, biceps tension folds, cavity AO, combined with the existing fabric normal.
* Team / Regions maps regenerate byte-identical (checked with `cmp`): the colour design and the dirt regions are untouched.
* Material numbers (`PhotoBodiesStage.ApplyMaterialNumbers`): jersey / pants `_NormalScale` 0.5 > 1.0 (`ClothNormalScale`), the folds are physical slopes. The shader (`PixLitHooks.hlsl`) was **not** edited: the baked maps already go through the jersey branch (`_NormalMap` + the `_PixFabric` micro weave). A pose-driven wrinkle layer in the shader was not done.

## 3. Shoulders and skin

* **Shoulders**: `phr2_pads.FIT` sx 0.93 > 0.80, caps 0.40 > 0.34, upper-chest taper 0.78 > 0.74, height 0.865 > 0.85. Jersey width at the shoulder joint -6 to -8 % (table), pad width -9 %, volume -12 % (7.1 > 6.2 L mean).
* **Skin under the cloth** (`Editor/Assets/PhotoUnderCloth.cs`, new, called last from `PhotoArmProportions.Apply`, a hidden-geometry import step): cause = the jersey cap / pads are weighted to the trunk (Spine2 0.64, clavicle 0.29, no arm weight) while the arm skin under them follows the arm (0.6-0.8), and the import relaxation smooths each mesh on its own. With the arm up the skin leaves the cloth (the v0.12 finding that thicker / thinner arms did not matter fits: it is the weights). Final rule: a skin vertex > 2 mm under the jersey or the pads (the layer that covers it deepest, within 0.36 m of a shoulder joint) takes the cloth weights in proportion to its depth (2 > 8 mm), pushed up to 12 mm further under, the share spread over 30 skin edges (about 30 cm) so the weights change gradually; pad vertices within 5 cm of the jersey follow the jersey. Applied to the arm skin and both compression sleeves, all 4 LODs. Switch `-pixNoUnderCloth`.
* **What was tried and dropped** (numbers in `diag\m_*_metrics.csv`, `sk_*.csv`): (a) copy the cloth weights onto the covered skin with a hard depth ramp: skin 0.8-2.0 % but the armpit skin stretched into spikes with the arm up (render `diag\s5_large_c6.png`); (b) the same with an 8-edge spread: 1.28 %, arm-up stretch 1,372; (c) an 18-edge spread: 1.20 %, 811; (c2) the final 30-edge spread: 1.17 %, **618**; (d) the opposite direction (the cloth takes the skin's weights): worse (skin 1.5-2.7 %, Large pad poke 135-200 per pose); (e) push 25 mm without weight changes: 6.9 %, i.e. the weights are the cause.
* **Not fixed / the cost, said plainly**: the arm-up stretched edge count rose 554 > 618 (+12 %): the skin under the cap now turns with the cloth while the skin below it turns with the arm, the change spread over a few cm. With the 18-edge spread the arms-up render showed a lumpy faceted skin shape at the Large body (`diag\sh18_large_c6.png`); the 30-edge spread is the final setting. Hip / seat skin (hip-flex edges 150-300) was not worked on: unchanged. Trunk-fold edges rose with the rebuilt pads / jersey (117 > 165).

## 4. Contact sheets and how close to R1 (looked at once, said plainly)

`R1_vs_field_{0_front,1_3q,3_shoulder,7_side}.png` (R1 first, then the 15 bodies, same framing; R1 from Blender EEVEE, the field bodies from HDRP in the studio, so the lighting differs), `before_vs_after_shoulder.png` (before / after pairs). Looked at `R1_vs_field_1_3q.png`, `after_small_runner_c3.png`, `after_large_stocky_c3.png`, `after_large_c6.png` (arms up).
* Better: the jersey now has believable cloth at shoulder / armpit / waist / seams and a clear side panel; shoulders are 6-8 % narrower and the pad cap no longer reads as a wing; the skin no longer comes through the cloth at the cap in the poses measured.
* **How close to R1: not close yet.** R1's jersey is one soft, quiet surface with a round natural shoulder; ours still has a stronger, more regular fabric ribbing (the stomach bands were removed in the review fix; a faint knit cross-hatch remains) and a bulkier shoulder cap (+12 / +17 / +26 % wider than R1 by body size, the Large being a bigger man on purpose). The sleeve end of the jersey is still a short hard-edged cuff over the pad, with a dark gap behind it (a v0.11 mesh property, unchanged). Folds are baked in the rest pose, they do not deepen or move with the pose. My estimate: about halfway from v0.12 to R1 on cloth, a third of the way on the shoulder shape.

## 5. Tests
One EditMode run on the private copy (`-testCategory "!Slow"`, `D:\PIXFootball\Work\Bodies\logs\tests.xml`): see section 8 (run before the review fix, which changed a texture and one import constant only). New `BodiesV013Tests` (3): the under-cloth share rule, the cloth maps' importer settings (normal map / sRGB), the stage import keeps the live importer settings. The v0.12 `BodiesV012Tests` pad limits (S 0.74 / M 0.80 / L 0.87 m) hold after the promotion (stage widths 0.607-0.758).

## 6. Files
Edited: `Tools/Blender/phr_textures.py` (cloth hook `PHR_CLOTH`, `armsleeve`), `Tools/Blender/phr2_pads.py` (FIT), `Assets/_Project/Scripts/Editor/Assets/PhotoArmProportions.cs` (`Version` 5 > 6, the private stage path also gets the import step, one call to `PhotoUnderCloth.Apply`), `Editor/Assets/PhotoBodiesStage.cs` (cloth map import + promotion copy list, `ApplyStageTextures`, `ClothNormalScale`, `RebuildStageBatch`), `Editor/Tools/BodyShots.cs` (live prefab by path when the ArtLibrary lacks the roster keys, stage cloth textures, `-pixBodyShotsDiag` exposure histogram).
Hook in another track's file: `Editor/Tools/BodyProbe.Skeleton.cs` (STANCE-HEAD): two lines, the prefab by path when the key is missing + `-pixBodyProbeStage 1` (the private stage prefabs).
New: `Tools/Blender/phr3_cloth.py`, `phr3_shoulders.py`, `phr3_render_r1.py`, `Editor/Assets/PhotoUnderCloth.cs` (+ `.meta`), `Tests/EditMode/Gameplay/BodiesV013Tests.cs` (+ `.meta`). **Meta rule (coordinator): the two new `.meta` files were written into the SHARED tree first (GUIDs `b4d7d2fd...` and `c2cf662e...`) and the copy uses the same ones; no new ScriptableObject / MonoBehaviour / shader / material was added.**
Nothing changed under `_Licensed` and `PixLitHooks.hlsl` is untouched.
Local note: the shared tree did not compile at 08:20 (`SimAgentView.QbMocap.cs` line 84, `_qfFit` undeclared, QB-2's in-flight file); my copy removed that one assignment by hand, nothing was changed in the shared file.

## 7. Promotion recipe (integration; backup already taken)
Backup of everything the promotion overwrites: `D:\PIXFootball\Work\v013\backup\bodies2\` (`Meshes\` 15 FBX, `Prefabs\` 15, `Textures\` the 7 cloth maps, `Materials\PHR_Jersey.mat` + `PHR_Pants.mat`). Built output, independent of any Library: `D:\PIXFootball\Work\v013\backup\bodies2\stage_out\{Meshes (15 FBX named PHR_<Size>_<Base>.fbx), Textures}`.
Copy list into the shared tree (`PhotoBodiesStage.Promote` does the copies itself from `Assets/_BodiesStage/`): 15 FBX `PHR_<Size>_<Base>.fbx` > `Assets/_Licensed/PhotorealPlayer/Meshes/PHR_<Size>[_<Base>].fbx` (Std has no suffix), and the maps `jersey_Gray, jersey_N, jersey_Team, pants_Gray, pants_N, armsleeve_Gray, armsleeve_N` > `.../Textures/Uniform/` (the live `.meta` importer settings stay). Code needed first (all in the shared tree): `PhotoArmProportions.cs` (Version 6), `PhotoUnderCloth.cs` (+ meta), `PhotoBodiesStage.cs`.
Commands (no other copy running; the junction means the other copies see the new art at once):
1. copy `stage_out\Meshes\*` and `stage_out\Textures\*` into `D:\PIXFootball\Work\Bodies\PIX\Assets\_BodiesStage\Meshes|Textures` (they are there already in the Bodies copy), `bash D:/PIXFootball/Work/v012/sync.sh Bodies`
2. `bash D:/PIXFootball/Work/v012/unity.sh Bodies stage 3600 -executeMethod PixFootball.EditorTools.PhotoBodiesStage.BuildStageBatch` (imports the stage maps and FBX, builds the 15 stage prefabs)
3. `bash D:/PIXFootball/Work/v012/unity.sh Bodies promote 3600 -executeMethod PixFootball.EditorTools.PhotoBodiesStage.PromoteBatch` (copies FBX + maps, reimports with the v6 import step, rebuilds the 15 live prefabs in place, applies the material numbers incl. `_NormalScale` 1.0 on `PHR_Jersey` / `PHR_Pants`, `CriticalAudit.EnsureRoster`)
4. check: `-executeMethod PixFootball.EditorTools.BodyShots.Run -pixBodyShotsOut <dir> -pixBodyShotsTag live -pixBodyShotsWhat metrics` (expect skin poke mean 16.7 per pose, 1.17 % of the zone, arm-up stretch 618) and `BodySkeletonCheck.Run`.
Regenerating the stage from scratch: `PHR_TEX_OUT=<stage>/Textures blender -b --python Tools/Blender/phr_textures.py -- jersey,pants,armsleeve` (about 1 min), `blender -b --python Tools/Blender/phr2_build.py -- <Small|Medium|Large> <Std|Stocky|Athletic|Tall|Runner> --out <stage>/Meshes` x 15 (26 s each; `D:\PIXFootball\Work\v013\bodies2\build15.sh <outdir>` runs them 3 at a time), then `PhotoBodiesStage.BuildStageBatch`. Rollback: copy the backup files over the live ones and re-import (the v5 import step is the `-pixNoUnderCloth` switch).
Also for integration: the shared `ArtLibrary.asset` currently lacks the 12 non-Std roster keys (`BodyShots` found only 3 of 15 through `lib.Prefab`): `CriticalAudit.EnsureRoster` (`-pixEnsureRoster`) puts them back; `PromoteBatch` calls it.

## 8. Test result, confirming run
One EditMode run (`-testCategory "!Slow"`, shared tree at 09:15 incl. the other tracks' in-flight work, my copy with the `_qfFit` hand patch): **1,409 tests, 1,407 passed, 1 failed, 1 skipped**. The failure is NATURAL-3's `Natural3Tests.TheCapTurnsTheChestTowardsItsTargetInSmallStepsAndNeverOvershoots` (in-flight file, not a body test). The 3 new `BodiesV013Tests` pass, `BodiesV012Tests` / `PhotoPlayerTests` / `PhotoRosterV2Tests` pass against the live (v0.12) bodies.
No confirming `MatchWatchAudit`: the stage is not promoted (the shared bodies were not touched), so a match run in this copy would show the old bodies; the confirming evidence is the probe tables (section 1) and the renders on the stage prefabs. After the promotion run the check in section 7 step 4.

## 9. Known issues for v0.14
* Cloth looks authored, not simulated; folds do not move with the pose. A shoulder / elbow wrinkle layer driven by the joint angle in the jersey branch of `PixLitHooks.hlsl` (two normal layers blended by bend) would be the next step.
* A faint knit cross-hatch remains; the jersey sleeve end is a hard cuff; the shoulder is still wider than R1.
* Arm-up stretched edges +12 %, hip-flex seat edges (150-300) untouched.
* FPS not measured in a built player.

## 10. Requests
* Integration: run the recipe in section 7 when no other copy is running; a build player check of FPS (same scene as v0.12: 144.6 avg / 110.7 1 %-low).
* QB-2: `SimAgentView.QbMocap.cs` line 84 referenced an undeclared `_qfFit` at 08:20.

## 11. Review fix (coordinator, same day)
Coordinator review of `before_vs_after_shoulder.png`: shoulder / armpit folds and the skin fix kept; the even horizontal bands over the stomach (quilted look) removed (section 2: waist tuck only, -70 %, irregular, faded out; belly hang folds gone); arm-up stretch reduced 811 > 618 by spreading the under-cloth weights over 30 edges instead of 18 (poke 1.20 > 1.17 %, mean 15 bodies). Only `jersey_N` / `jersey_Gray` and `PhotoUnderCloth.SpreadHops` changed: `stage_out\Textures` in the backup folder was refreshed, the stage FBX are unchanged (the weights are an import step, so the promotion re-imports them with SpreadHops 30). Re-rendered `before_vs_after_shoulder.png` and `R1_vs_field_0_front.png` from `after3\`; the other sheets (`1_3q`, `3_shoulder`, `7_side`) are from the first pass. The tests were not re-run (one EditMode run rule); the change touches no test.
