# PIX Football art pipeline: Blender -> Unity (HDRP)

Practical guide for the player / gear art. Sections 1-4 and 6-8 were measured headless with Blender 5.2.2 on 2026-10-21. The Unity side described in sections 5 and 9 (`BodyMeshImporter`, `PixImportPresets`, `V6ArtBuilder`) is implemented in `Assets/_Project/Scripts/Editor`; it has been run in batch mode by the MODELS / GRAPHICS tracks (evidence in `Docs/QA/models.md` and `Docs/QA/graphics.md`); the import presets are only applied to new assets.

Tools live in `Tools/Blender/`, generated art in `Assets/_Licensed/PIX/` (git-ignored with the rest of `_Licensed`), logs and test output in `Logs/blender_v6/` and `Logs/model_qa/`. Blender is found at `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe` unless the `BLENDER_PATH` environment variable or a script's `-Blender` parameter says otherwise.

| Script | Purpose | Used by the game? |
|---|---|---|
| `pix_player_fix.py` | **The shipped body route.** Bulky padded LOD0 + LOD1..LOD3 (UV-border protected) for the Small / Medium / Large bodies, 4 influences per vertex, written as `Assets/_Licensed/PIX/Meshes/Bodies/<Body>.pixmesh` | yes (via `BodyMeshImporter`) |
| `pix_player_common.py` | shared numpy / PNG / UV-rasteriser / FBX helpers (import only) | helper |
| `pix_player_lods.py` | older FBX-per-size LOD export (`<Mesh>_LOD0..3`, Decimate collapse); still imported by `pix_player_fix.py` for its helpers and kept as a reference / measurement tool | no (output stays in `Logs/blender_v6`) |
| `bake_player_regions.py` | body-region mask `PIX_Uniform_Regions.png` + number-decal analysis | yes (dirt shader) |
| `pix_pack_pbr.py` | HDRP Lit mask-map packer + normal-map convention flip | tooling |
| `pix_bake_hi_to_lo.py` | generic hi -> low bake (normal, AO, curvature) | tooling |
| `pix_fabric_weave.py` | tileable fabric detail normal / AO textures (numpy only) | yes |
| `model_qa_measure.py`, `model_qa_weights.py` | body QA: cross-section widths per height, bone lengths; skin-weight statistics (influences, weight lost by 4-bone truncation) -> `Logs/model_qa/blender_*.txt` | QA |
| `render_lookdev.py` (+ `run_lookdev.sh`, `render_all_new.sh`) | consistent front / three-quarter / back look-dev renders of a GLB / FBX (used for the Tripo player-look study in `Docs/QA/tripo_looks`) | QA |
| `retarget_actions.py` | re-bakes every action of Auto-Rig-Pro FBX files onto the clean 95-bone deform skeleton, one FBX per action (the BoNES / Yan mocap route) | yes (animation import) |
| `referee_rebake.py`, `check_referee_baked.py`, `build_coach.py`, `coach_variants.py`, `make_coach_textures.py`, `build_anims.py`, `build_props.py`, `pix_props.py`, `pix_poses.py`, `paint_out_logos.py`, `scan_logos.py` | referee + sideline-coach rig, textures, keyframed clips and props (`build_all.ps1`, `Docs/AssetInventory.md` section 13) | yes |
| `phr_body.py`, `phr_common.py`, `phr_head_survey.py`, `phr_headfit_test.py`, `phr_body_test.py`, `phr_measure_r1.py`, `phr_render_r1.py` | PHR photoreal-player study: reshaping/fitting the user-supplied R1 clothing and skin meshes to the three body types and onto the Draft Fever head shapes (experimental, not part of the shipped route; other track) | no |
| `build_locomotion.py` | authored (keyframed) walk, stroll and huddle clips on the clean rig (`PIX_Authored_Anims.fbx`) | yes (animation) |
| `pix_common.py`, `pix_poses.py`, `inspect_scene.py`, `phr_inspect.py` | shared Blender helpers, pose authoring helpers, scene / GLB dump tools | helper |
| `build_v6_player_art.ps1` | runs the texture helpers in order (about 1 minute); the LOD export in step 1 is the older FBX route and optional (`-SkipLods`) | tooling |

```powershell
.\Tools\Blender\build_v6_player_art.ps1                 # texture helpers (+ the older FBX LOD export for Medium, Small, Large)
.\Tools\Blender\build_v6_player_art.ps1 -SkipLods       # skip step 1
```

End to end for the player bodies (what produced the v0.6 meshes):

1. `blender -b --python Tools/Blender/pix_player_fix.py -- --body Small,Medium,Large` writes the `.pixmesh` files (the source FBXs are never modified). `--no-bulk` writes the unmodified geometry, used to prove the exporter reproduces the original mesh.
2. Unity: `PIX Football > Assets > Import Fixed Body Meshes` (`BodyMeshImporter.Run`; `BodyMeshImporter.Verify` first compares the export with the current prefab meshes: winding and vertex distance) rebuilds the three player prefabs' skinned meshes and the `PlayerMeshLodTable` (`PixMeshLods.asset`). `ModelFix.All` runs verify + import + QA renders in one batch launch.
3. Unity: `PIX Football > V6 > Build All V6 Art` (`V6ArtBuilder`) regenerates the procedural v0.6 look art (number / letter atlases, turf blades, weather sprites, floodlight cookie, helmet scuffs) and the `PIX/Lit` materials in `Assets/_Licensed/PIX/V6`.
4. QA: `Tools/Blender/model_qa_*.py`, `ModelQaRenderer` (before / after sheets in `Docs/QA/models/`), EditMode `BodyMeshTests`.

## 1. Coordinates and scale

* Blender: metres, Z up, the character faces **-Y** (character's left = **+X**), feet on Z = 0, armature at the origin with identity scale / rotation. This matches `pix_common.py`.
* FBX export (all scripts): `axis_forward='-Z'`, `axis_up='Y'`, `global_scale=1.0`, `apply_unit_scale=True`, `apply_scale_options='FBX_SCALE_NONE'`, `add_leaf_bones=False`, `bake_anim=False`, `embed_textures=False`, `path_mode='STRIP'`. Unity then sees +Z forward, Y up, 1 unit = 1 m with an identity root node (the same settings the coach / referee pipeline uses).
* **Apply transforms before export.** The Draft Fever FBXs import with armature scale 0.01 and rotation 90 degrees X, meshes at -90 degrees X (data stored in cm). `pix_player_lods.py` bakes that into the vertex data and the bone data (`Mesh.transform(..., shape_keys=True)`, `Armature.data.transform`) so every object ends with identity transform. Re-import check of the exported LOD FBX: armature scale (1, 1, 1), Medium body spans Z 0.057 .. 1.855 (head top), i.e. 1 unit = 1 m.
* Why Y-up / -Z forward: it is the Blender FBX exporter's mapping that yields a Unity-correct orientation without a 90 degree root rotation and without scale factors in the importer. Do not rotate meshes inside Unity to compensate.
* Unity setting: `Model > Scale Factor 1`, `Convert Units` on (default), `Bake Axis Conversion` off.

## 2. What is in the Draft Fever player FBXs (inspected)

`Assets/_Licensed/DraftFever/Characters/Bodies/{Small,Medium,Large}Player.fbx`. Medium numbers (Small / Large differ only where noted).

| Object | Verts | Tris | UV layers | Vertex groups | Material | Notes |
|---|---:|---:|---|---:|---|---|
| `Torso_GEO` | 3067 | 6061 | `UVMap` | 17 | `Uniform_MAT` | jersey incl. short sleeves / shoulder caps; Small 3124 v, Large 3067 v |
| `Legs_GEO` | 2369 | 4584 | `UVMap`, `DiffuseUV`, `map1` (last two are empty, all zero) | 12 | `Legs_MAT` | pants + lower legs, one skin to the ankle |
| `Arms_GEO` | 2060 | 3928 | `UVMap`, `DiffuseUV` | 14 | `Arms_MAT` | bare arms (Small 1916 v, Large 1964 v) |
| `Hands_GEO` | 5124 | 10196 | `UVMap`, `DiffuseUV` | 36 | `Arms_MAT` | finger bones included (Small 5220, Large 5172) |
| `Shoes_GEO` | 4324 | 8198 | `UVMap` | 8 | `Shoes_MAT` | |
| `Head_BlendShape_<Size>_Player` | 2783 | 5522 | own UV | 3 | `Head_..._MAT` | 28 shape keys `Head_NN_SubD01` |
| `Eyes_BlendShape_<Size>_Player` | 616 | 1117 | own UV | 1 (`head`) | `Eyes_..._MAT` | 28 shape keys `Head_Eyes_NN` |
| `Hair Mesh For Skin - Non Physics` | 1468 | 2928 | `UVMap` | 12 | none | Medium only |
| `Hair Mesh For Skin - Physics Rig` | 1590 | 3168 | `UVMap` | 12 | none | Medium only, weights to `cc_Hair_*` bones |
| `root` (armature) | | | | | | 76 bones |

All meshes carry custom split normals and an Armature modifier (rest pose). Medium body height: feet -0.013 m, head top 1.855 m (hair 1.903 m). Torso Z 0.892 .. 1.621, legs Z 0.055 .. 1.077 (Small 1.552 / 1.031, Large 1.685 / 1.127 for torso top / legs top).

Armature `root` (Blender object name) with 76 bones, parent chain: `c_root` > `hip` > (`spine_01` > `spine_02` > `spine_03` > `neck` > `head`; `shoulder_l/r` > `arm_stretch_l/r` > `forearm_stretch_l/r` > `hand_l/r` > fingers; `thigh_stretch_l/r` > `leg_stretch_l/r` > `foot_l/r` > `toes_01_l/r`) plus helpers `cc_waist_back`, `cc_waist_front`, `arm_twist.l/r`, `forearm_twist_l/r`, `thigh_twist_l/r`, `leg_twist_l/r`, `cc_Hair_Back_*`, `cc_Hair_Front_*`, `cc_Bal_TGT_Hand.L/R` and a root-level `cc_Ball Bone`.

Torso vertex groups: `hip, spine_01..03, neck, shoulder_l/r, arm_stretch_l/r, arm_twist.l/r, forearm_stretch_r, forearm_twist_r, thigh_stretch_l/r, thigh_twist_l/r`. Legs: `hip, spine_01, thigh_stretch/thigh_twist l+r, leg_stretch/leg_twist l+r, foot l+r`.

### UV layout sharing between sizes

Torso and Legs share the single 4096 `Uniform` sheet (the Legs mesh is the top-left / top-right of the same sheet, so the legs material is the same texture set in practice). `bake_player_regions.py --compare` (rasterised UV coverage at 1024, IoU against Medium):

* Large: torso loops identical, max UV difference 0.03 px of 4096; coverage IoU 0.999.
* Small: torso has a different topology (3124 v, 12453 loops, one extra small collar island) but the same islands in the same places (main island rect identical within 1-2 px); coverage IoU 0.986.
* Legs (Small and Large vs Medium): same topology, max UV difference 15 px (0.0037 UV), a slight re-fit of the pants.
* So one region mask and one number-rect table serve all three sizes. The mask is baked from the Medium body; Small/Large wearers will see it shifted by less than the gradient width.

## 3. Uniform UV layout (4096 px sheet, origin top-left like an image viewer)

Source for the picture: `Mid_Size_Main_Player_Low_Texturing_Uniform_MAT_BaseColor.png` (grey, shows the islands and the baked-in "10" numerals).

| Island | Rect x0,y0 .. x1,y1 (px) | Mesh | Faces | What it is |
|---|---|---|---:|---|
| Torso main sheet | 38,2454 .. 3540,4070 | Torso | 1820 | the whole jersey body unrolled as a strip, see below |
| Hem strip | 3477,2416 .. 4041,4066 | Torso | 671 | curved band at the bottom of the jersey (Z 0.89 .. 1.05) |
| Left sleeve cap | 1135,1627 .. 1858,2492 | Torso | 219 | character LEFT (+X) shoulder cap / sleeve, Z 1.37 .. 1.58 |
| Right sleeve cap | 49,1697 .. 810,2516 | Torso | 215 | character RIGHT (-X) |
| Two small pieces | 1501,294 .. 1634,452 and 1295,279 .. 1424,442 | Torso | 120 / 75 | small waist-detail pieces at Z 0.91 .. 1.06 |
| Pants (thighs + hips + crotch) | 44,26 .. 3068,2012 | Legs | 1418 | both thighs down to Z 0.45, one connected island |
| Right lower leg | 3086,1234 .. 4070,2429 | Legs | 437 | character RIGHT (-X), Z 0.055 .. 0.51 |
| Left lower leg | 2945,26 .. 3984,1171 | Legs | 437 | character LEFT (+X) |

**Torso main sheet (verified by per-pixel position / normal rasterisation).** It is a strip, rotated 90 degrees:

* UV **horizontal axis = body height**: Z peaks at the collar line at x about 1850 (Z 1.60), falls toward the hem at both ends (x 38 -> Z 1.02, x 3540 -> Z 1.00).
* x 38 .. 1850: **BACK half** (normal +Y). x 1850 .. 3540: **FRONT half** (normal -Y). The neck hole (dark triangle) sits at the join, x about 1700 .. 2160, y about 3030 .. 3400.
* UV **vertical axis = body left/right**: top of the strip (y 2454) is character LEFT (+X, X about +0.17), bottom (y 4070) is character RIGHT (-X, X about -0.19).

### Number decals (the four baked "10"s)

Rects are measured on the image (threshold of the numeral fill, refined from your 2.12 x estimates) and checked on the mesh: for each rect the triangles under it were gathered, the UV Jacobian (dP/du, dP/dv) computed, and only outward-facing triangles used. Both Unity UV conventions are given: pixel rects have origin top-left, `rect_uv` is `umin, vmin, umax, vmax` with v up (Unity / Blender). Machine-readable copy: `Logs/blender_v6/uniform_layout.json` (`numerals`).

| Numeral | Rect px (x0,y0,x1,y1) | Size px | rect_uv (u0,v0,u1,v1) | Where on the body (mean pos, metres) | Digit "up" in UV | Reading direction in UV | Mirrored? |
|---|---|---|---|---|---|---|---|
| **Back number** | 663, 2916, 1264, 3624 | 601 x 708 | 0.1619, 0.1152, 0.3086, 0.2881 | (-0.01, 0.22, 1.31), faces +Y, up = +Z, reads toward -X (as seen from behind: left to right) | **+u** (image right) | **-v** (image down); "1" is at the top, "0" below | **no**, reads correctly from behind |
| **Front number** | 2307, 2962, 2786, 3526 | 479 x 564 | 0.5632, 0.1392, 0.6802, 0.2769 | (-0.01, -0.07, 1.41), faces -Y, up = +Z, reads toward +X (left to right seen from the front) | **-u** (image left) | **+v** (image up); "1" is at the bottom, "0" on top | **no**, reads correctly from the front |
| **Left shoulder / sleeve number** (+X) | 1248, 1878, 1498, 2091 | 250 x 213 | 0.3047, 0.4895, 0.3657, 0.5415 | (0.29, 0.06, 1.50): outer / front side of the left sleeve cap, digit up = (0.49, -0.03, 0.87) i.e. toward the shoulder | **+v** (image up) | **+u** (image right) | **no** |
| **Right shoulder / sleeve number** (-X) | 372, 1956, 622, 2169 | 250 x 213 | 0.0908, 0.4705, 0.1519, 0.5225 | (-0.28, 0.17, 1.47): underside / back of the right sleeve cap, digit up = (0.70, 0.37, -0.61) i.e. toward the cuff, so it hangs upside down in a standing pose | **+v** (image up) | **+u** (image right) | **no** (outer-shell triangles) |

* The big numerals each have the team name ("DALLAS COWBOYS") next to them on the "up" side: back name strip about x 1330 .. 1420, y 2910 .. 3626; front name strip about x 2185 .. 2255, y 2955 .. 3535.
* Shader mapping (digit-local `s` along the reading direction, `t` along digit-up, both 0..1, `rect_uv` = u0,v0,u1,v1):
  * back: `uv = (u0 + t*(u1-u0), v1 - s*(v1-v0))`
  * front: `uv = (u1 - t*(u1-u0), v0 + s*(v1-v0))`
  * sleeves: `uv = (u0 + s*(u1-u0), v0 + t*(v1-v0))`
* Visual confirmation: the BaseColor sheet was projected on the model (flat Workbench renders from front, back, both shoulders). The front and back "10" read correctly unmirrored, the left shoulder numeral reads upright and unmirrored on the front-outer sleeve, the right shoulder numeral sits low on the back / underside of the right sleeve cap and is only partly visible. If you want a symmetric pair of shoulder numbers you have to re-place the right one by hand: **the right-sleeve location is the one result here that deserves a look in the shader**. The sleeve caps also contain a second, inner lining layer overlapping the same UV area (inverted facing); the analysis ignores it.
* Digit width budget: a two-digit number fits the rects above; one digit should be centred in the same rect.

## 4. Body-region mask `PIX_Uniform_Regions.png`

`Assets/_Licensed/PIX/V6/Textures/PIX_Uniform_Regions.png`, RGBA 8-bit, 2048 x 2048, linear data (import with sRGB off), 12 px edge dilation (so mips do not bleed; Unity: keep mips on, "Alpha Is Transparency" **off**, because A carries data). Same UV layout as the 4096 uniform sheet (it is just half resolution).

| Channel | Region | Where (verified on the model, see `Logs/blender_v6/regions_debug.png`) |
|---|---|---|
| **R** | knees + shins | legs mesh, front and side facing, below mid-thigh (Z 0.72). Kneecap peak (1.0) at the knee joint (Z 0.49), about 0.45 -> 0.7 down the shin, about 0.25 at the ankle, 0 on the back of the leg |
| **G** | seat + thighs | legs mesh, back and side (front thigh only 0.22), ramps 0 -> 1 from mid-thigh (Z 0.72) to the hip (Z 0.91), full to the waist; the back of the jersey hem carries 0.6 fading out by Z 1.07 |
| **B** | chest / belly / front | torso mesh only, normal toward -Y, 0.85 at the hem, 1.0 from hip to lower chest (Z 1.28), fading to 0 at the shoulder line (Z 1.44); sleeves excluded |
| **A** | back + shoulders + sleeves | torso mesh: back-facing from hem to shoulders, plus the whole shoulder line above Z 1.41 (any facing) and the sleeve caps (|X| > 0.22, full at the ends) |

* Method: Python rasteriser in `bake_player_regions.py`. Each UV triangle is rasterised with barycentric interpolation of world position and smooth vertex normal (bind pose, Z-up, front = -Y), the channels are analytic smooth functions (cosine ramps) of height relative to rig landmarks (knee, mid-thigh, hip, spine, shoulder bones) and of the normal. Then a masked Gaussian blur (about 2 px) and dilation.
* The jersey does not have an elbow region: the short sleeves end near mid upper arm, so "elbows" are not on this sheet (they are on `Arms_GEO`, which uses a different UV sheet).
* Landmarks come from the Medium rig: hip Z 0.907, knee 0.492, ankle 0.102, shoulder joint 1.443, neck 1.569.

## 5. LODs (`pix_player_fix.py` -> `.pixmesh` -> `BodyMeshImporter`)

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python Tools\Blender\pix_player_fix.py -- --body Small,Medium,Large
#   --out Assets/_Licensed/PIX/Meshes/Bodies   --no-bulk (exporter self-check)   --preview Logs/model_qa   --report Logs/model_qa/fix_report.txt
```

* **Source and units.** `Assets/_Licensed/DraftFever/Characters/Bodies/<Body>Player.fbx` is read, never written. The import transforms (cm data, armature scale 0.01 / rotation 90) are baked to metres, Z-up, identity, as in section 1; hair meshes are dropped.
* **Bulk morph (LOD0).** The v0.5 bodies had thin thighs / calves and narrow shoulders. Smooth, vertex-group and height driven fields widen the shoulder pads, hips / waist / chest, thighs and calves per size (Large widest, linemen clearly wider than skill players; parameters in the `BULK` table of the script). The skeleton and bind pose do not change, so every Humanoid clip still fits.
* **Skin weights.** At most 4 influences per vertex (what Unity keeps with `SkinQuality.Bone4`), renormalised; unweighted vertices take the nearest weighted neighbour.
* **LOD1..LOD3.** Quadric (collapse) decimation per part with UV-border and open-boundary vertices protected (ratios `RATIOS` in the script: default 0.5 / 0.25 / 0.18, hands 0.30 / 0.12 / 0.05, shoes 0.40 / 0.20 / 0.10); eyes are not decimated.
* **Output.** `<Body>.pixmesh`, a binary data file in Unity's world bind-pose frame (`x_u = -x_b`, `y_u = z_b`, `z_u = -y_b`, winding flipped), one per size, with the bone-name table. It is not an FBX: Unity never imports it as a model.
* **`BodyMeshImporter`** (`PIX Football > Assets > Import Fixed Body Meshes`) builds the meshes in the prefab renderer's space onto the original bones and bind poses (bone indices re-mapped by name; bones a mesh weights but its renderer lacks are remapped to an ancestor and logged), so the Humanoid avatars are untouched and no "Copy From Other Avatar" import is needed. Meshes are unique per size (`<Part>_<Size>_LOD<n>`); the importer rewrites the `PlayerMeshLodTable` (including `lod3`) and the three player prefabs. It refuses to run when `Verify` finds a winding ratio below 0.98 or a mean vertex distance above 4 mm against the original.
* **Why not the FBX route / `LODGroup`.** The first attempt (`pix_player_lods.py`, one `<Mesh>_LOD0..3` FBX per size) was not the problem; the real defect was in the lookup: `PlayerMeshLodTable` used to find LOD meshes by `meshName#vertexCount`, and the three bodies share names and vertex counts, so one size's LOD replaced another's. Unique `<Part>_<Size>_LOD<n>` names fix that. The match players (22 skinned bodies sharing one rig) swap `SkinnedMeshRenderer.sharedMesh` themselves instead of using a `LODGroup`.
* **Runtime.** `PlayerLod` (`Assets/_Project/Scripts/Gameplay/Player/PlayerLod.cs`) picks the level by camera distance: level 0 below `Near` = 11 m, 1 below `Mid` = 26 m, 2 below `Far` = 60 m, 3 beyond; eyes are hidden from level 2; `QualityTiers` scales the distances with `PlayerLod.Bias`. Meshes come from the `PlayerMeshLodTable` (`PixMeshLods.asset`).
* Vertex counts per LOD and the measured before / after body widths are in `Docs/QA/models.md` and `Logs/model_qa/fix_report.txt`; the `pix_player_lods.py` figures of the first attempt (Medium 23401 -> 12253 -> 7011 -> 4749 vertices) are historical.

## 6. Humanoid rig expectations

Use the bone names of the Draft Fever rig as they are (the FBX carries no renames). Unity Humanoid auto-mapping works for the standard bones; set / verify the rest by hand once and keep the avatar.

| Unity Humanoid slot | Bone |
|---|---|
| Hips | `hip` (parent `c_root` stays outside the avatar) |
| Spine / Chest / Upper Chest | `spine_01` / `spine_02` / `spine_03` |
| Neck / Head | `neck` / `head` |
| Left / Right Shoulder | `shoulder_l` / `shoulder_r` |
| Left / Right Upper Arm | `arm_stretch_l` / `arm_stretch_r` |
| Left / Right Lower Arm | `forearm_stretch_l` / `forearm_stretch_r` |
| Left / Right Hand | `hand_l` / `hand_r` |
| Fingers (Proximal / Intermediate / Distal) | `c_thumb1..3`, `c_index1..3`, `c_middle1..3`, `c_ring1..3`, `c_pinky1..3` (`_l` / `_r`) |
| Left / Right Upper Leg | `thigh_stretch_l` / `thigh_stretch_r` |
| Left / Right Lower Leg | `leg_stretch_l` / `leg_stretch_r` |
| Left / Right Foot | `foot_l` / `foot_r` |
| Left / Right Toes | `toes_01_l` / `toes_01_r` |
| Not mapped (extra transforms) | `c_root`, `cc_waist_back`, `cc_waist_front`, `arm_twist.l/r`, `forearm_twist_l/r`, `thigh_twist_l/r`, `leg_twist_l/r`, `cc_Hair_*`, `cc_Bal_TGT_Hand.L/R`, `cc_Ball Bone`, eye / head blend shapes |

Rules for new rigged art: keep these names and the T-/A-pose rest pose, root at the origin facing -Y in Blender, no leaf bones (`add_leaf_bones=False`), at most 4 influences per vertex (the project uses `SkinQuality.Bone4`), twist bones may carry weights (they are extra transforms in Unity, the avatar ignores them).

## 7. PBR texture conventions for HDRP Lit

* **Base colour**: sRGB, with mips.
* **Everything else is linear** (sRGB off): mask map, detail map, region masks, normals, height.
* **Mask map (HDRP Lit)**: R = metallic, G = ambient occlusion, B = detail mask, A = smoothness (= 1 - roughness).
* **Normal maps: Unity wants OpenGL style (Y+, green up) in the `NormalMap` import type.** DirectX-style (Y-) sources (Unreal, Substance "DirectX", many marketplace sets) must have the green channel inverted. The Draft Fever maps named `..._Normal_OpenGL.png` are already correct. `pix_pack_pbr.py --normal-in directx` flips the green channel (and renormalises).

```powershell
# pack a folder (auto-detects base colour / metallic / roughness / ao / detail / normal by file name)
blender -b --python Tools\Blender\pix_pack_pbr.py -- --dir <folder> --name T_Uniform_Medium --out Logs\blender_v6
# explicit files, DirectX normal, 2K output, 16 bit
blender -b --python Tools\Blender\pix_pack_pbr.py -- --metallic m.png --roughness r.png --ao ao.png --normal n_dx.png --normal-in directx --size 2048 --bits 16 --name T_Gear
# options: --glossiness (input already smoothness), --default-roughness 0.5, --no-renormalize
```

Outputs `<name>_M.png` and `<name>_N.png`. Missing inputs default to metallic 0, AO 1, detail mask 1, smoothness 0.5. Inputs of different sizes are bilinearly resized to the largest (or `--size`). Tested on the Medium uniform maps (4096): `T_Uniform_Medium_M.png` (mean metallic 0.586, smoothness 0.444) and `T_Uniform_Medium_N.png` (OpenGL -> OpenGL, unchanged), plus a 1024 px / 16-bit DirectX -> OpenGL flip run.

## 8. Naming, folders, budgets

| Kind | Name | Example |
|---|---|---|
| Texture | `T_<Asset>_<BC\|N\|M\|E\|R>.png` (BC base colour, N normal, M mask map, E emissive, R region / RGB mask) | `T_Uniform_Medium_M.png` |
| Static mesh | `SM_<Asset>` | `SM_Cone_01` |
| Skeletal mesh | `SK_<Asset>` | `SK_Player_Medium` |
| Material / instance | `M_<Asset>` / `MI_<Asset>_<Variant>` | `MI_Uniform_Cowboys` |
| LODs | `<Mesh>_LOD0 .. _LOD3` | `Torso_GEO_LOD2` |

Existing generated files (`PIX_Uniform_Regions.png`, `PIX_FabricWeave_*`, `PIX_Knit_Normal.png`, the coach textures) keep the `PIX_` prefix; new assets should follow the table above.

Resolution budgets (max size on disk and at import):

| Asset | Budget |
|---|---|
| Hero player, helmet | 4096 (the delivered Draft Fever sets are 4096; 8192 only exists for one helmet, see section 10) |
| Uniform parts, gear (gloves, pads, shoes, sleeves) | 2048 |
| Props | 1024 - 2048 |
| Trim sheets, tileables (fabric, grass, turf detail) | 1024 - 2048 (fabric detail 512) |
| Region / RGB masks | half of the colour sheet (2048 for the 4096 uniform) |
| UI | separate pipeline (sprites, no mips) |

Where files live: `Assets/_Licensed/` = owner / licensed art (models, textures; **git-ignored**, rebuilt by the Tools scripts), `Assets/_Project/` = code, presets, editor tools, small project-owned data. Source (`.spp`, `.blend`, hi-poly) stays in `_AssetSource/` (git-ignored).

## 9. Unity import presets (`PixImportPresets`)

Implemented in `Assets/_Project/Scripts/Editor/Assets/PixImportPresets.cs`: `PIX Football > V6 > Create Import Presets` writes six `.preset` assets (normal map, HDRP mask map, base colour, fabric / tileable detail, player body model) to `Assets/_Project/Settings/ImportPresets` from freshly configured importers of the V6 art, and an `AssetPostprocessor` applies them **only to new assets** under `Assets/_Licensed/PIX` and `Assets/_Licensed/Art` (an asset that already has import settings is never touched, so hand-tuned textures and the Draft Fever rules in `LicensedImportRules` keep working). The `.preset` files do not exist until someone runs that menu item (the folder is absent in a fresh checkout). The texture-role and size-budget decisions are pure functions with EditMode tests.

| File name / path | Role and settings |
|---|---|
| `*_N*.png`, `*Normal*` | normal map, mips on, aniso 8, high-quality compression |
| `*_M*.png`, `*MaskMap*`, `*_R.png`, `*Regions*`, `*Detail*` | data texture: **sRGB off**, mips on, alpha is not transparency (A carries data) |
| `*_BC*.png`, `*BaseColor*`, `*Base_color*` | colour: **sRGB on**, mips on |
| Size budget by folder | Hero (player, helmet) 4096, Gear / Uniform parts 2048, Props 1024, tileable fabric / turf detail 1024 (fabric 512, wrap Repeat) |
| player bodies (models) | Humanoid, Read/Write off, Optimize Mesh on, blend shapes only for head / eyes files, tangents Mikktspace, 4-bone skin weights |

`V6ArtBuilder.Configure` sets the same texture options explicitly for the procedurally generated V6 textures, so they do not depend on the postprocessor. The earlier "intent only" table was reduced to what the code does; anything not listed is left to Unity's defaults.

## 10. Substance Painter and source resolutions

Export from Painter with the **Unity HDRP (Metallic Standard)** preset: it writes `<TextureSet>_BaseMap` (sRGB), `<TextureSet>_MaskMap` (R metallic, G AO, B detail mask, A smoothness), `<TextureSet>_Normal` (and optionally `_BentNormal`), which is exactly the layout of section 7; the Painter files already in the tree (`Maps\Unity\VintageHelmet_BaseMap / MaskMap / Normal / BentNormal / DetailMap / RGBMask`) come from that preset. Check the normal convention once per project (a bump lit from above must have a bright top edge with green up); in Painter the project / export setting decides between DirectX and OpenGL, and Unity needs OpenGL. Keep the texture set / material name equal to the Unity material name so file names map 1:1.

Where the sources are (`_AssetSource/DraftFever/3D/...`, all 1 - 5 GB, the resolution stored inside the project was not inspected, open in Painter to see it):

| Asset | .spp |
|---|---|
| Player uniform / skin / helmet (4K delivery) | `Characters\Players\Mid_Size_Player\Mid_Size_Main_Player\Source\Substance\Mid_Size_Player_Textures.spp` (1.2 GB) |
| Same, PC variant | `Characters\Players\Mid_Size_Player\PC_Mid_Size_Main_Player\Source\Substance\Mid_Szie_Main_Player_Textures.spp` (1.6 GB) |
| New jersey | `Characters\Players\Mid_Size_Player\PC_Mid_Size_Main_Player\Source\New_Jersey\Substance\New_Jersey_Textures.spp` (407 MB) |
| Legendary skins | `Characters\Players\LegendarySkins\{Futuristic,Vintage} Outfit\...` (0.9 - 1.7 GB) |
| Referee | `Characters\Referee\Substance\Referee_Textures.spp` |
| Helmets | `Props\New Helmets\Medium\Basic Helmet 1 / 2 / 3, Spartan Helmet\*.spp` (0.4 - 1.8 GB), `Props\Updated Helmet\source\helmetTextures.spp` (4.7 GB) |
| Gear (gloves, sleeves, tape, pads, mouth guards, shoes) | `Characters\Players\Accessories\...\*.spp` (36 - 430 MB each) |
| Heads | `Characters\Players\Heads\Victors Heads\LODS\...`, `3D\Leo's\*.spp` (about 3 GB each) |

### Higher-resolution texture sets that already exist

A scan of every image in `_AssetSource/DraftFever` (1517 files; PNG / PSD / TGA headers, JPG / TIF / BMP via System.Drawing) found:

| File | Resolution | Size |
|---|---|---|
| `3D\Props\New Helmets\Medium\Basic Helmet 1\textures2\helmet_low_helmet_MAT_BaseMap.png` | **8192 x 8192** | 24.7 MB |
| `...\textures2\helmet_low_helmet_MAT_MaskMap.png` | **8192 x 8192** | 27.5 MB |
| `...\textures2\helmet_low_helmet_MAT_Normal.png` | **8192 x 8192** | 26.0 MB |
| `3D\Unity_Packages\Cube_Map_Test.png` | 12288 x 2048 (cubemap strip, not a surface) | 15.7 MB |
| `UI\PSDs\rankbadges.psd` | 1536 x 4604 | 292 MB |
| `3D\DFB_logoIterations\...` (22 logo PNGs) | 4167 x 4167 | 0.1 - 0.3 MB |
| two Substance `.alg_meta` preview PNGs | header says 7.7 M x 6.5 M px (bogus metadata thumbnails, ignore) | |

**There is no 8K (or any > 4096) uniform, skin or player texture.** The 4K deliveries are in `3D\Characters\Players\Mid_Size_Player\Mid_Size_Main_Player\Delivery\Textures\{Uniform,Helmet,Skin}` (Uniform_Base_color, Metallic, Roughness, Normal_OpenGL, Color_Pattern_Masks/Uniform_User1..3, Dallas_Cowboys_Variant) and are the same as the 4096 maps already imported under `Assets/_Licensed/DraftFever/.../Mid_Size_Main_Player/Textures`. Other 4096 sets: Spartan outfit (`LegendarySkins\Spartan Outfit\Textures`), Vintage outfit (`...\SubstancePainter\Maps\{PBR,Unity}`), Vintage-kit jersey (`Accessories\Vintage Kit\Jersey\Texture`), helmets (`Props\Updated Helmet\delivery`, `Props\New Helmets\Medium\*\textures`). Anything beyond 4096 for players would need a fresh export from the `.spp` files above.

## 11. Workflows

### Player LODs
`pix_player_fix.py` -> `.pixmesh` -> `BodyMeshImporter` (section 5). The `.pixmesh` files go to `Assets/_Licensed/PIX/Meshes/Bodies/`; QA output and the report to `Logs/model_qa/`. The older `pix_player_lods.py` writes `Logs/blender_v6/<Body>Player_LODs.fbx` and never straight into Assets.

### Region mask and numbers
```powershell
blender -b --python Tools\Blender\bake_player_regions.py -- --body Medium --size 2048 --pad 12 --compare
#   writes Assets\_Licensed\PIX\V6\Textures\PIX_Uniform_Regions.png, Logs\blender_v6\regions_debug.png, uniform_layout.json
#   (--no-render skips the debug renders, --compare also checks the Small / Large UV layout)
```
`regions_debug.png`: top row = R, G, B, A over the dimmed sheet; middle row = RGB on the model (front, back, three-quarter, side); bottom row = A on the model.

### Fabric detail
```powershell
blender -b --python Tools\Blender\pix_fabric_weave.py -- --out Assets\_Licensed\PIX\V6\Textures --size 512 --seed 7 --strength 1.0
```
Writes `PIX_FabricWeave_Normal.png` (OpenGL normal, plain-weave yarns at 4 px + diamond mesh perforations at 16 px pitch), `PIX_FabricWeave_AO.png` (8-bit grey cavity) and `PIX_Knit_Normal.png` (stretch knit: 4 px horizontal ribs with zig-zag V stitches). Mean normal tilt 13 degrees (weave) and 16 degrees (knit); the wrap seam step equals an ordinary neighbour step (ratio 1.07 weave; the knit ribs are anisotropic so 1.27). Tileable because every period divides 512 and the noise is built in Fourier space. Debug: `Logs/blender_v6/fabric_debug.png` (3 x 3 tiled, lit preview + normals) and `fabric_debug_1to1.png`. Use as the HDRP detail map normal (tile roughly every 8 - 12 cm of uniform) or sample it in the PIX uniform shader; keep it at 512, import `Repeat`, linear, normal map type for the normals.

### Hi -> low bake
```powershell
blender -b --python Tools\Blender\pix_bake_hi_to_lo.py -- --hi hi.fbx --lo low.fbx --out Logs\blender_v6\bake --name Gloves --size 2048 --samples 32 --cage-extrusion 0.02 --max-ray 0.1
#   self test: -- --selftest --out Logs\blender_v6\bake_selftest
```
Cycles on the CPU, selected-to-active, tangent-space normal (OpenGL, Y+; `--normal-y directx` flips), ambient occlusion (`--ao-distance`), curvature from the hi mesh's Pointiness (0.5 flat, > 0.5 convex, < 0.5 cavity, `--curv-gain`). The low mesh needs UVs; units are metres. The self test (noise-displaced icosphere, 82k tris -> 320 tris) bakes all three in about 2 seconds at 1024 px.
