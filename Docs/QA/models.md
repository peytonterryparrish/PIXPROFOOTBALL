# MODELS track log (MDL-###)

Owner: MODELS (meshes, skeletons / Humanoid avatars, prefabs, LODs). Format: see `README.md`. Evidence: `Docs/QA/models/before_*` = v0.5.0 renders and measurements (kept in the repo); `after_summary.md` and `after_measurements.csv` are written to the same folder; the "after" image sheets (`after_*.png` / `after_*.jpg`) of the final run are written to `D:\PIXFootball\Renders\models\` (large outputs live on D:; any `Docs/QA/models/after_*` image is from an earlier run and superseded). Tools: `ModelQaRenderer` (Unity editor, `-qaTag before|after`), `Tools/Blender/model_qa_*.py`, `pix_player_fix.py`.

## Root cause of user note 9 ("waists / legs super thin")

1. **LOD key collision (the main one).** `PlayerMeshLodTable` looked LOD meshes up by `meshName#vertexCount`. The three body FBXs share mesh names and vertex counts (`Legs_GEO#2369` in S, M and L; `Torso_GEO#3067` in M and L; `Shoes_GEO#4324` in all three) and the old `PlayerMeshLodBuilder` skipped every key it had already seen, so **the Medium torso and the Small legs were the LOD1 / LOD2 meshes of every body**. A Large lineman beyond 9 m (LOD1) wore a Medium torso and Small legs (waist 1.298 m -> 1.090 m, hip 1.203 m -> 0.957 m = 0.80x). The Madden-style camera sits 8-12 m behind the QB, so almost every player was drawn at LOD1 or LOD2.
2. **Vertex-clustering decimation** (grid average of positions, normals, UVs and weights) shrinks thin shells (jersey hem, pant legs), merges the two sides of a leg, tears UV islands (smeared pants texture, white patches where pants show through the jersey) and opens holes (black knee patches).
3. **LOD0 proportions**: pants / thigh pads and calves are thin next to the shoulder pads (Medium thigh 0.565 m at z 0.65 is thinner than Small 0.594 m), shoulders narrow (0.57 / 0.64 / 0.70 m), linemen not clearly wider than skill players: not the 2K8 padded silhouette the user wants.
4. Ruled out by measurement (see MDL rows "wontfix / checked"): 4-bone truncation (mean 0.7-1.8 % weight lost), Humanoid avatar mismatch (hip height 0.907 m on all bodies, feet on the ground within 2 cm on every clip of every body, leg stretch <= 5 %), blendshape weights, pelvis / thigh joint offsets, twist bones.

## Fix summary

* `Tools/Blender/pix_player_fix.py`: source FBXs untouched; bulky padded LOD0 (shoulder pads, thicker thighs / calves / waist, linemen widest), skin weights limited to 4 influences, LOD1..LOD3 by quadric decimation with UV-border protection (silhouette within +-1 % at LOD1 / LOD2), written to `Assets/_Licensed/PIX/Meshes/Bodies/<Body>.pixmesh`.
* `BodyMeshImporter` (editor): builds the meshes in the prefab renderer's space onto the original bones / bind poses (Humanoid avatars unchanged), unique per size (`<Part>_<Size>_LOD<n>`), rewrites the `PlayerMeshLodTable` (+ `lod3`) and the three player prefabs.
* `PlayerLod`: four levels (11 m / 26 m / 60 m), eyes hidden from LOD2.
* `ModelQaRenderer` + `BodyMeshTests` keep it that way.

## Issues

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| MDL-001 | fixed | S1 | LOD | `PlayerMeshLodTable` key `meshName#vertexCount`, old `PlayerMeshLodBuilder` (`seen.Add(k)`) | Key collisions between bodies: `Legs_GEO#2369` is the same key for S, M and L, `Torso_GEO#3067` for M and L, `Shoes_GEO#4324` for all three. The builder skipped every key it had already seen, so the Small legs / Small shoes / Medium torso were the LOD1 and LOD2 mesh of EVERY body (a Large lineman beyond 9 m wore a Medium torso + Small legs, skinned in his own bone space). | Meshes are named `<Part>_<Size>_LOD<n>`, table rebuilt from the Blender export, one entry per real mesh; `BodyMeshTests.EveryBodyPartHasFourDecreasingLods`. | `before_summary.md` Large LOD1 waist 0.840x vs `after_summary.md` 1.000x |
| MDL-002 | fixed | S1 | LOD | Large LOD1, waist plane | Waist hull circumference 1.298 m (LOD0) -> 1.090 m (LOD1): 0.840x, the lineman at 11-24 m has the waist of a Medium player (QA-940). | as MDL-001 + decimated LODs | after: 1.396 -> 1.396 m (1.000x), `after_summary.md` |
| MDL-003 | fixed | S1 | LOD | Large LOD1, hip plane | Hip circumference 1.203 -> 0.957 m (0.795x): narrower than the waist above it. | as MDL-001 | after 1.294 -> 1.294 m (1.000x) |
| MDL-004 | fixed | S1 | LOD | Large LOD1, mid-thigh / calf | Mid-thigh 0.686 -> 0.620 m (0.904x), calf 0.427 -> 0.374 m (0.874x); minimum thigh over the 32 clips 0.787x (catch_dive). | as MDL-001 | after thigh 0.993x, calf 0.997x |
| MDL-005 | fixed | S1 | LOD | Large LOD2 / LOD3 | waist 0.822x, hip 0.79x, thigh 0.878x, calf 0.802x of LOD0; min thigh 0.762x (catch_dive). The broadcast camera (44+ m) draws everybody at LOD2, so every lineman on TV was thin. | as MDL-001 | `after_summary.md` LOD2 / LOD3 rows: waist 0.999x / 0.998x |
| MDL-006 | fixed | S2 | LOD | Small + Medium LOD2 (clustered) | Even without the key collision clustering shrank the thin shells: calf 0.900x (S) / 0.903x (M), thigh 0.969x / 0.974x, waist 0.974x / 0.971x. | quadric decimation with UV protection instead of grid clustering | after LOD2 calf 1.001x (S) 0.997x (M) |
| MDL-007 | fixed | S1 | LOD | jersey hem, Large LOD1 / LOD2 | Torn jersey hem: white pants visible through ragged holes along the whole hem (clustering merged the two layers of the hem). | decimation keeps the hem border (UV-border protect) | `before_clips_Large_LOD1_Front.jpg` vs `after_clips_Large_LOD1_Front.jpg` |
| MDL-008 | fixed | S1 | LOD | UVs, Legs / Torso LOD1-3 | Clustering averaged the UVs of vertices of different islands: smeared pants texture, stripes tearing, white patches on the jersey (`before_posemat_Large_T.png`, LOD1-3 columns). | UV-border vertices are protected in the decimator; UV error measured in `pix_player_lods.py` (3-12 px p95 at LOD1) | `after_posemat_Large_T.png` |
| MDL-009 | fixed | S2 | LOD | knees, Large LOD1 | Black patches / holes at both knees (open triangles after clustering). | decimation keeps the surface closed | `before_clips_Large_LOD1_Front.jpg` knees vs after |
| MDL-010 | fixed | S2 | LOD | `PlayerLod` | Only three levels (LOD3 of the Blender export unused), `LevelFor` returned 0..2, thresholds 9 / 24 m put the Madden camera's whole field (8-12 m behind the QB) on LOD1 / LOD2 (QA-699, QA-700). | four levels 11 / 26 / 60 m, `lod3` in the table, silhouette identical across levels so the distance no longer changes the build | `ContactTests.PlayerLodLevelStepsDownWithDistanceAndFollowsTheBias` |
| MDL-011 | fixed | S3 | LOD | `PlayerLod.LevelFor` | Hard thresholds, no hysteresis: a player standing on 11 m / 26 m flips mesh + helmet LOD every 4th frame (QA-701, QA-950). | +-10 percent hysteresis relative to the level in use | `ContactTests.PlayerLodHasHysteresisAroundTheThresholds` |
| MDL-012 | fixed | S3 | LOD | `PlayerLod.Update` | With `ViewCamera == null` (free roam, before the director sets it) `_level` stays -1 and every helmet / mask LOD renderer is active at once (z-fighting shells, QA-702). | `Apply(0)` when there is no camera | code |
| MDL-013 | fixed | S2 | LOD | Head LOD1 / LOD2 (clustered) | Facial landmarks blur: 2783 verts clustered to about 1060 / 335 (eyes, nose, lips merge), faces look melted at 11-26 m. | decimated head: 1402 / 712 verts, borders protected | `after_posemat_*` face columns |
| MDL-014 | fixed | S3 | LOD | Shoes LOD2 / LOD3 | UV-border protection pinned 28 percent of the shoe vertices: LOD3 kept 1226 of 4324 verts (QA-951): 22 players x about 4.7k verts at broadcast distance. | shoes drop the border protection from LOD2 on: 1639 / 819 tris | `Logs/model_qa/fix_report.txt` Shoes row |
| MDL-015 | fixed | S3 | LOD | Hands LOD ratios | Hands (5.1k verts of micro triangles) were 38 / 12 percent clustered; they are tiny on screen. | Hands 30 / 12 / 5 percent (1584 / 649 / 620 verts) | fix_report.txt |
| MDL-016 | fixed | S3 | LOD | `PlayerMeshLodBuilder`, `PlayerMeshLodTable` | The clustering builder and a table without `lod3` stayed in the code base as a trap for the next rebuild. | builder is a one-line wrapper around `BodyMeshImporter.Run`; `Entry.lod3` | code |
| MDL-017 | fixed | S3 | LOD | tests | Nothing measured LOD silhouettes: bounds-only checks pass an 18 percent waist loss (QA-1053). | `BodyMeshTests`: four decreasing LODs per part, bounds 10 percent, area 15 percent, weights normalised, linemen wider, hull circumference at waist / hip / mid-thigh within 4 / 5 / 8 percent per LOD | `BodyMeshTests.*` |
| MDL-018 | fixed | S1 | Proportions | LOD0 Medium legs | Medium mid-thigh circumference (z 0.65 m) 0.565 m is THINNER than the Small body's 0.594 m although the Medium torso is 8 percent wider: QBs / RBs / LBs / TEs have stick legs under big pads (user note 9, QA-042, QA-073, QA-084). | thigh field in `pix_player_fix.py` (grows outward, tapered at knee and hip) | thigh 0.658 -> 0.780 m, `after_pose_Medium_*` |
| MDL-019 | fixed | S1 | Proportions | LOD0 Small legs | Small mid-thigh 0.652 m, calf 0.386 m: spindly receivers / corners. | thigh x1.13, calf x1.06 | 0.736 / 0.409 m |
| MDL-020 | fixed | S1 | Proportions | LOD0 Large legs | Large thigh 0.710 m is only 8 percent more than Medium although the torso is 18 percent wider (waist 1.298 vs 1.102): linemen on bird legs. | thigh x1.22 | 0.869 m, `after_posemat_Large_T.png` |
| MDL-021 | fixed | S2 | Proportions | calves, all bodies | Mid-calf 0.386 / 0.387 / 0.424 m (S / M / L): thin shins under the thigh pads. | calf field x1.06 / 1.09 / 1.11 | 0.409 / 0.421 / 0.471 m |
| MDL-022 | fixed | S2 | Proportions | shoulder pads | Jersey / pad width (Torso bounds) 0.557 / 0.639 / 0.704 m: no pad silhouette, sleeves end on the biceps (QA-036, QA-041, QA-230). | pad bulge about the arm axis + lateral widening | 0.587 / 0.684 / 0.769 m |
| MDL-023 | fixed | S2 | Proportions | OL / DL vs skill players | Large was only 1.26x the Small shoulder width: OL / DL read as WRs with a bigger helmet (QA-284, QA-288; 2K8 refs 2, 5, 9, 14, 16, 18). | Large pads x1.17, chest x1.09, waist x1.12: Large/Small shoulder 1.31x, waist 1.40 vs 1.10 m | `after_pose_*_T.png` |
| MDL-024 | fixed | S2 | Proportions | waist / belly | Waist 0.99 / 1.04 / 1.24 m with a narrow jersey-over-pants step; waist narrower than the hips. | waist x1.05 / 1.08 / 1.12, depth scaled less than width (no belly) | 1.035 / 1.098 / 1.396 m |
| MDL-025 | fixed | S2 | Proportions | jersey hem vs pants | After the first bulk pass (thigh x1.26) the pants top poked through the jersey hem and the thighs looked pear-shaped. | thigh field fades out above the hip joint (u 0.55-0.90); hem follows the hip profile | `Logs/model_qa/blender_preview_*_bulk_front.png` |
| MDL-026 | fixed | S2 | Proportions | inner thighs | Thicker thighs would intersect at the crotch. | growth shifted outward by 0.65 x the radius growth | preview front views |
| MDL-027 | open | S3 | Proportions | Large LOD0, back hem left | A small white rectangle at the left back hem (pants visible through an opening of the jersey hem mesh); present in the original FBX (`before_posemat_Large_T.png`). Geometry / UV, not LOD. | needs a hem patch in Blender or a shader fix; GFX owns the material | `after_posemat_Large_T.png` back, column 1 |
| MDL-028 | open | S3 | Proportions | per-player build | Every OL / DL uses Large, every LB / TE / RB / QB Medium: all linemen share one shape (QA-942). | a runtime width scale needs weight / rating per player in `SimAgentView` (STN / ENGINE data) | none |
| MDL-029 | fixed | S2 | Weights | Torso (S / M / L) | 27.5 / 29.8 / 25.8 percent of the vertices have more than 4 influences (max 8); Unity keeps 4: weight lost on those up to 9.6 / 9.2 / 15.1 percent (`blender_weights.txt`). | `limit_weights`: top 4 renormalised in Blender (what Bone4 would keep) | `Logs/model_qa/blender_weights.txt` |
| MDL-030 | fixed | S3 | Weights | Legs (S / M / L) | 9.5 / 5.8 / 5.7 percent >4 influences, up to 7; max weight lost 7.8 / 7.2 / 6.9 percent. | same | same |
| MDL-031 | fixed | S3 | Weights | Arms / Hands | Arms 5.2 / 1.8 / 6.1 percent >4 influences; Small hands 1.1 percent with up to 8, max lost 2.7 percent. | same | same |
| MDL-032 | fixed | S2 | Weights | twist bones | thigh_twist carries 17 percent, leg_twist 14 percent, forearm_twist 25 percent of the leg / arm weight, arm_twist about 10 percent. They are not Humanoid-mapped (QA-944) and cost one of the four influence slots. | twist weights folded into the stretch bone they are children of (identical motion in Unity; 0 remapped bones) | `body_mesh_import.txt` |
| MDL-033 | wontfix | S3 | Weights | candy-wrapper at knees / elbows | Suspected from the twist bones: they are children of the stretch bones and rotate 100 percent with them; measured min thigh / calf over 32 clips at LOD0 is 0.95-0.97x (qb_throw, catch_dive: real folding), no pinch. | none needed | `before_summary.md` LOD0 rows |
| MDL-034 | fixed | S3 | Weights | unweighted vertices | (checked) 0 unweighted vertices in Torso / Legs / Arms / Hands / Shoes / Head of all three FBXs; `limit_weights` also repairs any (nearest weighted neighbour). | check in the script | `blender_weights.txt` |
| MDL-035 | fixed | S3 | Weights | hidden hair meshes | The Medium prefab ships two inactive `Hair Mesh For Skin` skinned renderers (5733 / 5936 verts, 76 bones, DefaultHDMaterial) that nothing shows. | removed from the player prefabs by the importer | `prefab_dump.txt` |
| MDL-036 | wontfix | S3 | Skeleton | pelvis vs hip joints | `hip` / `root.x` is at z 0.907 on all bodies but the thigh joints are at 0.903 (S) / 0.952 (M) / 1.000 m (L): the Large pelvis bone sits 9.3 cm below the hip joints. Measured effect: none, feet stay on the ground within 2 cm in every grounded clip of every body (footMin -0.017 / -0.013 / -0.011 m in idle). | none (retarget works in muscle space) | `before_measurements.csv` idle rows |
| MDL-037 | wontfix | S3 | Skeleton | leg lengths | thigh 0.423 / 0.463 / 0.480 m and shin 0.391 / 0.393 / 0.428 m: the sizes differ in height (head top 1.77 / 1.86 / 1.94 m); Humanoid retargets proportions. | none | `blender_measure.txt` |
| MDL-038 | wontfix | S3 | Avatar | leg stretch | Humanoid default leg stretch 0.05: legs stretch up to 1.050x in catch_dive / qb_throw. Changing it on the body avatars alone would mis-scale the clip muscle space; the clips keep their own default ranges. | none; limit documented | `before_summary.md` "max leg stretch" |
| MDL-039 | wontfix | S3 | Avatar | muscle limits | Body avatars keep the default muscle limits (the ~1000 clip avatars have the same defaults). Coaches / referee elbow limits are widened to 150 degrees (RefCoachBuilder) because their keyframed clips were authored that way. | none | `AssetInventory.md` section 13 |
| MDL-040 | fixed | S3 | Avatar | S / M / L avatars | (checked) SmallPlayerAvatar, MediumPlayerAvatar, LargePlayerAvatar are valid Humanoid, hips 0.907 m on all three. | `ArtLibraryBuilder` logs an error if a body avatar is invalid / the Medium one is null (QA-952); each prefab animates with its own avatar, `lib.playerAvatar` is only a tool default | `prefab_dump.txt` |
| MDL-041 | fixed | S3 | Avatar | `HumanoidMapper` | No validation that the mapped bones form a hierarchy chain (QA-955): a wrong `root.x` would still give a valid avatar that drives the wrong bones. | chain check hips > spine > head, hips > leg chain, spine > arm chain; logs the chosen bones | `HumanoidMapper.Map` report |
| MDL-042 | fixed | S3 | Avatar | `PixImportPresets` | The `Model_PlayerBody` preset is captured from MediumPlayer.fbx and was applied to the first import of every body / LOD FBX, carrying Medium's human description (QA-954). | preset application clears human bones + skeleton and re-creates the avatar from the model | code |
| MDL-043 | fixed | S3 | Runtime | `PlayerAvatar` | The four per-avatar `new Material` instances (legacy / V6 uniform and helmet) were never destroyed (QA-703). | `OnDestroy` destroys them (DestroyImmediate outside play mode) | code |
| MDL-044 | fixed | S3 | Runtime | `PlayerAvatar.HideHead` | Leaving first person set `ShadowCastingMode.On` on every head renderer including Eyes / FM_ / visor that were Off (QA-704). | original shadow modes are remembered and restored | code |
| MDL-045 | wontfix | S2 | Prefabs | helmet / body material slots (QA-696, QA-697, QA-932) | The audit suspected `SetMats` repainting extra sub-meshes (rear plate, strap, socks) with the shell / uniform material. `prefab_dump.txt`: every body renderer and every helmet / mask / visor LOD renderer has exactly one sub-mesh and one material slot, so there is nothing to overwrite. | none | `Logs/model_qa/prefab_dump.txt` |
| MDL-046 | dup of GFX-001 | S1 | Helmet | helmet back "weird patch" (QA-037, QA-071, QA-082, QA-102, QA-123, QA-148, QA-252, QA-933) | The patch is the licensed helmet base map's baked logo plates + mask hole shown through the team colour, not a mesh gap: every helmet LOD is one sub-mesh with one slot (MDL-045). Repainted by the v0.6 helmet shader. | GFX-001 / GFX-002 / GFX-017 (V6 look); `PlayerLod` no longer leaves all helmet LODs active (MDL-012) | `graphics.md` GFX-001, `after_heads.png` |
| MDL-047 | fixed | S2 | Helmet | helmet fit S / M / L (QA-938) | Helmet placement uses magic offsets (Medium centred on its pivot, Small / Large in body space); the shoulder pads grew 7-9 percent, so fit was re-checked on all three heads. | verified, no change needed: the shell sits on the head, mask over the face, no collar or pad intersection | `after_heads.png`, `after_closeups_*.png` NECK column |
| MDL-048 | wontfix | S3 | Helmet | helmet sockets (QA-938, QA-939) | A Blender-authored socket per head would replace the offsets; helmet LOD renderers are `Basic_Helmet_01_LOD00/01/02` (alphabetical = LOD order, one mesh per shell, no helper renderers: `prefab_dump.txt`), so the "alphabetically first" rule is correct for these assets. | none | `prefab_dump.txt` |
| MDL-049 | fixed | S2 | Helmet | jersey collar vs helmet (QA-074) | Collar suspected to clip through the helmet opening. | checked with the wider shoulders: collar sits below the shell and jaw on all bodies | `after_closeups_*.png` NECK |
| MDL-050 | open | S2 | Helmet | face mask (QA-036, QA-074, QA-252) | `FM_01` reads as 2-3 thin bars at game distance (LOD00 3.3-4.3k verts of thin wire); the 2K8 masks are heavier. | needs a thicker mask mesh / the other FM_0x variants (FM_02..05 exist in the source) | `after_heads.png` |
| MDL-051 | open | S3 | Helmet | chin strap (QA-103) | The helmet FBX has no chin strap mesh. | new mesh needed | `after_closeups_*.png` NECK side |
| MDL-052 | open | S2 | Hands | bare hands, no gloves (QA-038, QA-073, QA-147, QA-151) | `Hands_GEO` is bare skin on all bodies; the glove accessories (`PIX_Acc_Gloves`, QB / lineman gloves) are static meshes without the rig and do not fit the animated fingers. | a glove material on `Hands_GEO` (GFX) is the cheap fix; fitting the accessory meshes is a Blender job | `after_closeups_*.png` HAND |
| MDL-053 | open | S3 | Hands | claw fingers in clips (QA-084, QA-147) | Fingers curl into claws in idle / stance clips: mocap finger muscles, not mesh. | ANI: finger pose clean-up | `before_clips_*_Front.jpg` idle cell |
| MDL-054 | open | S3 | Eyes | blank white eyes (QA-003) | At game distance the eyes behind the mask read as white discs; with the helmet hidden the iris is correct (green, all three bodies). | Eyes_Shader brightness / emissive is GFX | `after_closeups_*.png` EYES |
| MDL-055 | open | S3 | Heads | one head per size (QA-152) | Each body has one head sculpt and one skin tone (the 15 arm skin textures exist). | needs a head kit / variants, outside this pass | `after_heads.png` |
| MDL-056 | wontfix | S3 | Heads | head scale per body (QA-230) | The helmet is about 1.5 head heights by design; head widths 0.20 / 0.22 / 0.25 m against shoulders now 0.59 / 0.68 / 0.77 m (was 0.56 / 0.64 / 0.70) so the head reads smaller relative to the pads. | none | `after_heads.png` |
| MDL-057 | fixed | S3 | Blend shapes | Head / Eyes (QA-953) | The FBX head and eyes carry 28 blend shapes each; no code drives them (no `SetBlendShapeWeight` in the project). | the prefab meshes are the exported ones without blend shapes (less memory); the FBX import settings are left alone | `prefab_dump.txt` (bs0) |
| MDL-058 | wontfix | S3 | Meshes | tangents / vertex colours / uv2 (QA-947) | Imported meshes carry positions, normals, uv0 and weights; tangents are recalculated (Unity's MikkTSpace implementation, same basis as the baked normal maps), no material uses vertex colours or uv2. | none | visual check `after_posemat_*` |
| MDL-059 | fixed | S2 | Coach | coach SMR bounds | All 29 coach skinned renderers have a 0.01 m local bounds box at the root (`prefab_dump.txt`): with `updateWhenOffscreen` off the whole coach is culled as soon as that point leaves the screen (pops at the frame edge). | bone-driven bounds (`updateWhenOffscreen = true`) on the coach renderers (3 coaches, negligible) | `prefab_dump.txt` after `ModelFix.FixNpcBounds` |
| MDL-060 | open | S3 | Coach | Coach B jacket hem | Ragged / torn hem along the waist of the bomber jacket (holes at the front hem in idle, stance_2pt). | kit mesh issue, fix in `Tools/Blender/build_coach.py` (hem loop) | `before_extra_coach_b_clips.jpg` |
| MDL-061 | open | S2 | Coach | tablet / clipboard (QA-100) | The prop is parented to `hand_l`; in coach_clap / coach_clipboard_idle the crossed forearms push it through the chest and it floats in front of the belly. | ANI: arm fit of the keyframed coach clips (`ArmFit`) | `before_extra_coach_b_clips.jpg` |
| MDL-062 | fixed | S3 | Referee | referee skinning | (checked) 16 clips (idle, run, sprint, stance, catch, tackle, td signal, spot ball ...): no collapsed legs / arms, uniform follows; 95-bone deform rig. | none needed | `before_extra_referee_clips.jpg` |
| MDL-063 | wontfix | S3 | NPCs | T-pose prop sheet (QA-098) | Crowd / referee prefabs look T-posed only in the editor prop sheet; in the game every NPC gets its clip from its controller. | none | `Docs/Screens/extras_referee_crowd_props.png` |
| MDL-064 | open | S2 | Clips | clip facing conventions (for STN / ANI) | After Humanoid retarget the pelvis facing differs per clip: Motus idle / run / sprint / ol_stance -90 degrees, BoNES stance_2pt / stance_3pt / qb_stance_shotgun 0 degrees, ol_pass_set +93, backpedal -23, cut_hard -54, tackle_wrap +105, dl_stance +139 (same on S, M and L = clip property, not body). Explains part of "OL / DL / CB facing backwards". | per-clip yaw table in the view layer (`AnimKit` / `StanceRules`) | `before_measurements.csv` column yaw |
| MDL-065 | open | S2 | Clips | feet below the ground | dive_forward soles 8.3 / 7.4 / 7.6 cm below y = 0 (S / M / L), catch_high 4.2 / 3.6 / 3.7 cm, hurdle 3.6 / 3.3 / 3.0 cm, catch_low 3.4 / 3.0 / 2.8 cm (idle baseline -1.7 / -1.3 / -1.1 cm): the same on every body, so the clip, not the avatar. | ANI: foot contact clean-up | `before_measurements.csv` column footMinY |
| MDL-066 | open | S3 | Clips | feet floating | ol_run_block both soles 22-25 cm above the ground at the sampled frame, ol_block_engage 24-26 cm (player on his back), tackle_dive 9-11 cm. | ANI | `before_measurements.csv` |
| MDL-067 | fixed | S3 | Clips | body / clip consistency | Every clip plays on S, M and L with the same hip height (0.907 m), soles within +-2 cm of the same baseline and leg stretch <= 1.05x: no clip needs re-baking for a body. | none (documented) | `before_measurements.csv`, 3 x 32 clips |
| MDL-068 | fixed | S3 | Exporter | `.pixmesh` header (QA-949) | The format version was read and discarded: a format change produced garbage. | importer throws on a version mismatch | code |
| MDL-069 | fixed | S2 | Exporter | bone remap (QA-946) | A bone the renderer does not list fell back silently to an ancestor / bone 0. | counted + logged per mesh; error when no ancestor exists; import log prints the total (0) | `body_mesh_import.txt` |
| MDL-070 | fixed | S1 | Exporter | `BodyMeshImporter` (QA-948) | First version assigned `AssetDatabase.LoadAssetAtPath` results inside `StartAssetEditing`: they were null, `continue` skipped the part and the prefabs stayed on the old thin meshes with no error (the first "after" renders were identical to "before"). | the created mesh objects are used directly; a missing LOD1-3 of a body part is an error | `modelfix_all.log` vs `after_summary.md` |
| MDL-071 | fixed | S3 | Exporter | bone table (QA-945) | `b.use_deform or True` exported every control bone (hundreds on ARP rigs). | only bones that carry weights | `fix_report.txt` |
| MDL-072 | fixed | S2 | Exporter | landmarks (QA-943) | Landmarks from the left bones only, KeyError without a hint. | both sides read, symmetry checked (2 cm), missing bone named in the error | `pix_player_fix.py` |
| MDL-073 | fixed | S3 | Exporter | Blender API (QA-1029) | `corner_normals` needs Blender 4.1+; no version check; `load_png` docstring said it returned an array. | version assert, docstring fixed | code |
| MDL-074 | fixed | S3 | Pipeline | `build_v6_player_art.ps1` (QA-1028) | The documented one-command rebuild never ran the fix / measure scripts. | step 1b added (`pix_player_fix.py`, `model_qa_measure.py`, `model_qa_weights.py`) | script |
| MDL-075 | fixed | S1 | Exporter | exporter fidelity | Risk: a wrong axis / winding conversion would silently mirror or invert the body. | `BodyMeshImporter.Verify` rebuilds the unmodified export (`--no-bulk`) and compares with the original meshes: mean nearest-vertex distance 0.00 mm, winding agreement 1.000 on Torso / Legs / Arms (21 parts); `ModelFix.All` aborts otherwise | `Logs/model_qa/body_mesh_verify.txt` |
| MDL-076 | open | S3 | Pipeline | numerals boxes (QA-1043) | `V6ArtBuilder` paints out the baked "10" with hard-coded 4096-space boxes of the Medium sheet. | GFX: detect numerals per sheet | none |
| MDL-077 | fixed | S3 | Tools | `ModelQaRenderer` | There was no way to see or measure the bodies: now renders S / M / L x LOD0-3 x T / A / 32 clips x front / side / back (orthographic, labelled sheets), region close-ups, heads, referee / coaches, prefab dump, and writes the section circumferences to `<tag>_measurements.csv` / `<tag>_summary.md`. | `ModelQaRenderer.RunAll` (`-qaTag before|after`) | `Docs/QA/models/` |
| MDL-078 | fixed | S3 | Tools | measure scripts | Blender-side checks of the source bodies. | `model_qa_measure.py` (bones, section widths per height), `model_qa_weights.py` (influences, twist share, hierarchy) | `Logs/model_qa/blender_*.txt` |
| MDL-079 | fixed | S2 | Performance | LOD vertex budget | Decimated LODs keep more vertices than the clustered ones: body vertices per player (Unity counts, no eyes): LOD0 21.5k, LOD1 11.8k (clustered v0.5: about 8.6k), LOD2 6.9k (about 2.7k), LOD3 5.3k. 22 players at LOD2 = about 150k vertices, tiny next to the 26k-vertex helmet LOD00 that v0.4 already moved to LOD01 / LOD02. | measured in `fix_report.txt` / `body_mesh_import.txt`; live FPS in the report | report |
| MDL-080 | fixed | S3 | Prefabs | player prefabs | Prefab meshes of Torso / Legs / Arms / Hands / Shoes / Head / Eyes replaced by the unique per-size LOD0 meshes (original FBX + avatars untouched, the Medium hair renderers dropped); everything else (materials, helmet gear, Animator) unchanged. | `BodyMeshImporter.Run` | `prefab_dump.txt` |
| MDL-081 | fixed | S1 | Exporter | `pix_player_fix.py` bone table | Regression found by the after clip sheets: the bone-name table was computed before the LODs existed, so it was empty, every vertex lost its weights and was bound to bone 0 by the importer: the bodies stayed in the bind pose while the rigid helmets followed the animated head (floating helmets). A second leak: the decimator's helper group `_pix_seam` was exported as a "bone". The T- and A-pose renders cannot show this (bind pose / arms only). | table built after the LODs from real armature bones only; the script aborts when the export has no bones or an unweighted vertex; the importer logs an error for unweighted vertices; `BodyMeshTests.SkinWeightsAreNormalisedAndIndicesValid` requires >= 4 distinct bones per Torso / Legs / Arms mesh; `model_qa_pixmesh.py` reads a `.pixmesh` back and prints the bones that carry the weight | `after_clips_*.jpg` (posed bodies), `after_summary.md` |
| MDL-082 | fixed | S3 | Tools | `ModelQaRenderer` skinning | `SkinnedMeshRenderer.BakeMesh` in a batch-mode editor is not a reliable posing path (stale pose while the bones moved); tools that depend on it can show a bind-pose body. | the renderer skins on the CPU (linear blend skinning with bone matrix x bind pose), A-pose and T-pose verified against it | `after_pose_*_A.png` |
| MDL-084 | fixed | S3 | LOD | hands / shoes seam protection | After MDL-014 the Hands LOD3 still had more vertices than LOD2 (UV-border protection; test caught 1270 > 1262 Unity vertices). | hands drop the border protection from LOD2 on as well (LOD2 1223 / LOD3 508 tris); the ordering test compares triangles | `BodyMeshTests.EveryBodyPartHasFourDecreasingLods` |
| MDL-086 | fixed | S3 | Performance | live cost | Body triangles per player (no eyes): LOD0 38.5k (same as v0.5), LOD1 16.4k, LOD2 7.9k, LOD3 5.5k (v0.5 clustered LOD1 / LOD2: about 8.6k / 2.7k vertices); the 22-player cost at the broadcast LOD2 is about 170k triangles, small next to the shadow-cascade cost of the 26k-triangle helmets that v0.4 already LODs. | no live FPS run was possible while other tracks held the editor (see report); numbers from `body_mesh_import.txt` | `Logs/model_qa/body_mesh_import.txt` |
| MDL-083 | open | S1 | Clips | `Animations/Clean/*.anim` library clips (ANI) | The cleaned clips that the ArtLibrary keys now point at fold the legs of every player body when played on the Humanoid avatar (same `AnimationPlayableUtilities.PlayClip` + Evaluate that poses the original FBX takes correctly): hip-to-foot distance / leg length 0.08-0.40 (legs folded up to the chest, head height wrong, e.g. idle 0.08, stance_3pt head height 1.08 m vs 1.64 m, tackle_wrap 0.12) against 0.80-0.99 for the original take of the same name. 7 of the 32 test keys have no original FBX take under that name (`_s01` copies and BoNES retake names: ol_stance, stiff_arm, tackle_big_hit, ol_run_block, qb_throw, qb_throw_lob_long, qb_sack) and show the fold in the QA sheets even with `UseOriginalTakes`. Muscle-space clips do not depend on the body, so this affects all three bodies and the referee / coaches. | none in the body assets (the same prefabs pose correctly with the original takes); ANI: check how the Clean clips were baked (muscle curves / root) and re-run `ModelQaRenderer` before shipping them | `Docs/QA/models/clean_vs_original_clips_diag.txt`, `ani_cleanclips_on_player_Medium_LOD0_Side.jpg`, `D:\PIXFootball\Renders\models\after_clips_Medium_LOD0_Side.jpg` (original takes: sprint / 3-point stance / tackles correct) |
| MDL-085 | open | S3 | Tests | other tracks' EditMode tests | Full EditMode suite after this pass: 782 tests, 776 passed, 6 failed, none in the MODELS area (`BodyMeshTests` 4 / 4 green incl. the circumference test, `ContactTests` LOD tests green): `LookTests.TextAtlas_CleanUpperCasesTrimsAndCuts`, `OffseasonTests.StandaloneOffseasonWithoutACollegeWorld...`, `SimCalibrationTests` x2 (run concepts, pulling / misdirection), `AnimationTests.DespikeRemovesASingleFrameSpike...`, `MatchV030Tests.CollegeOvertimeAlternates...`. | GFX / ENGINE / ANI own them | `Logs/model_qa/editmode_results.xml` |
| MDL-087 | fixed | S2 | LOD | LOD vs LOD0 in the same pose | The T-pose table alone does not prove the LODs hold up in motion: for 34 poses (T, A, 32 clips) on each body the section at waist / hip / mid-thigh / mid-calf of LOD1 and LOD2 was compared with LOD0 in the same pose. Mean ratios 0.95-1.00 (worst single pose: Small LOD2 thigh 0.914 in qb_sack, Medium LOD2 hip 0.923 in ol_stance); v0.5: Large LOD1 0.84 / 0.90 / 0.87 already in the T-pose. | none needed | `Docs/QA/models/after_lod_vs_lod0_in_pose.txt` |

## Audit rows assigned to MDL (74)

Format as agreed in `README.md`: `QA-### -> status (MDL-###)`. "dup of GFX-###" = the defect is a texture / shader look issue that the GRAPHICS track already fixed in the v0.6 look (the audit captures were the v0.5 look); "open" = needs another track or new art.

| Audit row | Result |
|---|---|
| QA-002 | fixed (MDL-018, MDL-019, MDL-020, MDL-022, MDL-024); number contrast dup of GFX-009 |
| QA-003 | open (MDL-054) |
| QA-008 | wontfix here: team crest art (UI / GFX), no model asset involved |
| QA-036 | fixed for the pads (MDL-022); face mask open (MDL-050) |
| QA-037 | dup of GFX-001 (MDL-046) |
| QA-038 | dup of GFX-017 (stripe / crest); hands open (MDL-052) |
| QA-041 | fixed (MDL-022, MDL-023); dirt / AO dup of GFX-011 |
| QA-042 | fixed (MDL-018, MDL-019, MDL-020) |
| QA-071 | dup of GFX-001 (MDL-046) |
| QA-072 | dup of GFX-006 / GFX-007 (numbers, name bar) |
| QA-073 | fixed (MDL-018 .. MDL-024); hands open (MDL-052) |
| QA-074 | collar fixed (MDL-049); reflection dup of GFX-015; face mask open (MDL-050) |
| QA-075 | wontfix here: skin SSS tint / macro normal is material work (GFX) |
| QA-082 | dup of GFX-001 (MDL-046) |
| QA-084 | fixed (MDL-018 .. MDL-024); claw fingers open (MDL-053) |
| QA-097 | wontfix here: stadium prop texture branding (BrandingCleaner / GFX), not a player or skeleton asset |
| QA-098 | wontfix (MDL-063) |
| QA-100 | open (MDL-061) |
| QA-102 | dup of GFX-001; slot overwrite ruled out (MDL-045) |
| QA-103 | lighting dup of GFX-015; chin strap open (MDL-051) |
| QA-104 | dup of GFX-008 (wordmark replaced in the V6 uniform) |
| QA-108 | dup of GFX-007 / GFX-008 |
| QA-109 | fixed (MDL-022 .. MDL-025: pants volume, hem over the pants); helmet dup of GFX-001 |
| QA-123 | dup of GFX-001 (MDL-046) |
| QA-147 | dup of GFX-008 (branding text) |
| QA-148 | dup of GFX-001 (MDL-046) |
| QA-149 | dup of GFX-006 / GFX-009 |
| QA-150 | fixed (MDL-018 .. MDL-024) |
| QA-151 | open (MDL-052, MDL-053) |
| QA-152 | open (MDL-055) |
| QA-222 | fixed (MDL-018 .. MDL-024); number dup of GFX-006 |
| QA-230 | fixed (MDL-018, MDL-022); helmet size wontfix (MDL-056) |
| QA-238 | dup of GFX-001 / GFX-006 |
| QA-248 | fixed (MDL-018 .. MDL-025); number dup of GFX-006 |
| QA-252 | dup of GFX-001; face mask open (MDL-050) |
| QA-254 | dup of GFX-006 / GFX-007 |
| QA-271 | fixed for the pants volume (MDL-018 .. MDL-020); matte material dup of GFX-010 |
| QA-288 | fixed (MDL-023: Large 1.31x the Small shoulder width, waist 1.40 vs 1.10 m); per-player scale open (MDL-028) |
| QA-289 | dup of GFX-006 / GFX-009 / GFX-017 |
| QA-696 | wontfix (MDL-045: one slot per renderer) |
| QA-697 | wontfix (MDL-045) |
| QA-698 | fixed by the v0.6 helmet frame (`PlayerAvatar.SetupHelmetFrame` unions every helmet renderer) |
| QA-699 | fixed (MDL-010) |
| QA-700 | fixed (MDL-010) |
| QA-701 | fixed (MDL-011) |
| QA-702 | fixed (MDL-012) |
| QA-703 | fixed (MDL-043) |
| QA-704 | fixed (MDL-044) |
| QA-932 | wontfix (MDL-045) |
| QA-933 | dup of GFX-001 |
| QA-937 | fixed by the v0.6 helmet frame (as QA-698); per-size fit verified (MDL-047) |
| QA-938 | fixed / verified (MDL-047, MDL-048) |
| QA-939 | wontfix (MDL-048) |
| QA-940 | fixed (MDL-001 .. MDL-005, `after_summary.md` committed) |
| QA-941 | fixed (MDL-006: LOD1 / LOD2 thigh 0.98x, calf 1.00x) |
| QA-942 | open (MDL-028) |
| QA-943 | fixed (MDL-072) |
| QA-944 | fixed (MDL-032, MDL-033) |
| QA-945 | fixed (MDL-071) |
| QA-946 | fixed (MDL-069) |
| QA-947 | wontfix (MDL-058) |
| QA-948 | fixed (MDL-070) |
| QA-949 | fixed (MDL-068) |
| QA-950 | fixed (MDL-011; `ArtPipeline.md` section 5 now describes four levels 11 / 26 / 60 m) |
| QA-951 | fixed (MDL-014) |
| QA-952 | fixed (MDL-040) |
| QA-953 | fixed (MDL-057) |
| QA-954 | fixed (MDL-042) |
| QA-955 | fixed (MDL-041) |
| QA-956 | wontfix: the mapper lists the deform names (`root.x`, `spine_01.x`) before the control names (`c_*`), and the chain check (MDL-041) now reports a wrong pick |
| QA-1028 | fixed (MDL-074) |
| QA-1029 | fixed (MDL-073) |
| QA-1043 | open (MDL-076) |
| QA-1053 | fixed (MDL-017) |
