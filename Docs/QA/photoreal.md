# PHOTOREAL PLAYER track log (PHR-###)

Owner: PHOTOREAL PLAYER (R1 body / rig / topology -> original photoreal PIX football player). Format: see `README.md`. Evidence: renders in `D:PIXFootballRendersphotoreal` (new large files; older ones remain in `Docs/QA/photoreal/`), Blender work in `D:PIXFootballPhotorealWork`.
User decisions (2026-10-21): "the Burrow model wins" / "make fictional characters with Joe Burrow's model" / "we can use the model, just not Joe Burrow's likeness".
Rule that drove every step: the BODY / RIG / TOPOLOGY / mesh structure of R1 (`_AssetSource/UserModels/a-pose-rigged-joe-burrow-cincinnati-bengals-nfl-3d-model`) is used; nothing that identifies the real
player or the team ships (head / face / hair, jersey / pants / sock / helmet / towel / pouch / cleat artwork, name, number, NFL / Nike / Wilson / Bengals marks).
Everything derived from R1 is isolated in `Assets/_Licensed/PhotorealPlayer/` and `_AssetSource/PhotorealPlayer/` (both git-ignored like the rest of the licensed art) so it can be swapped out if the store licence turns out to be editorial-only.

## What was built

| Piece | Where | Notes |
|---|---|---|
| Blender pipeline | `Tools/Blender/phr_*.py` | `phr_build.py <Small/Medium/Large>` (one FBX per body), `phr_textures.py` / `phr_textures2.py` (original uniform maps), `phr_hair.py` (beard / brow / scalp shells), `phr_layout.py` (jersey number placements), checks: `phr_verify.py`, `phr_lod_measure.py`, `phr_body_table.py`, `phr_pose_test.py`, `phr_face_variants.py`, `phr_tex_review.py`, `phr_render_r1.py` |
| Meshes (FBX, Humanoid) | `Assets/_Licensed/PhotorealPlayer/Meshes/PHR_{Small,Medium,Large}.fbx` | R1's 52-bone `rig:*` skeleton unchanged; Unity automap gives a valid Humanoid on all three |
| Textures | `Assets/_Licensed/PhotorealPlayer/Textures/{Uniform,Skin,Hair}` | gray + team mask + normal + dirt regions per part, 8 skin tones (head 2K + shared 4K normal, arms 2K), 9 hair cut-outs |
| Materials / library | `Assets/_Licensed/PhotorealPlayer/Materials`, `PhotoPlayerLibrary.asset` | PIX/Lit uniform mode (jersey, pants, socks, cleats, sleeves, gloves, wristband, pouch, towel), PIX/Lit helmet mode, Arm_Skin SSS skin x8, Eyes_Shader, alpha-clip hair |
| Prefabs | `Assets/_Licensed/PhotorealPlayer/Prefabs/PIX_PhotoPlayer_{Small,Medium,Large}.prefab` | Animator (Humanoid avatar of the FBX), `PhotoPlayer`, `TeamTint`, `Helmet/<gear>/<LOD>` layout that `PlayerLod` understands |
| ArtLibrary keys | `photo_player_small`, `photo_player_medium`, `photo_player_large` | additive only (`PhotoPlayerBuilder.AddArtLibraryKeys`, one line at the end of `ArtLibraryBuilder.Build`); `player_*` untouched, nothing is the default yet |
| Runtime code | `Scripts/Art/PlayerAppearance.cs` (engine free), `PhotoPlayer.cs`, `PhotoPlayerLibrary.cs` | appearance descriptor + applier, team colours, LOD, helmet frame |
| Editor code | `Scripts/Editor/Assets/PhotoPlayerBuilder.cs`, `PhotoPlayerQa.cs` | menu `PIX Football/Photoreal Player/*`, batch `PhotoPlayerBuilder.BuildAllBatch`, `PhotoPlayerQa.RunAll` |
| Tests | `Tests/EditMode/Art/PlayerAppearanceTests.cs`, `PhotoPlayerTests.cs` | descriptor determinism / variety, prefab structure, triangle budget, blend shapes, banned names |

Rebuild from scratch (nothing is hand edited):
```
blender -b --python Tools/Blender/phr_textures.py                    # jersey / pants / socks maps
blender -b --python Tools/Blender/phr_textures2.py                   # cleats, skin tones, sleeves, gloves, small parts, helmet
blender -b --python Tools/Blender/phr_hair_test.py -- x --textures   # hair shell textures (writes Textures/Hair)
blender -b --python Tools/Blender/phr_layout.py                      # jersey_layout.json
blender -b --python Tools/Blender/phr_build.py -- Small|Medium|Large # FBX per body (about 20 s each)
Tools/unity_wait_run.ps1 -Method PixFootball.EditorTools.PhotoPlayerBuilder.BuildAllBatch -Log photo_build
Tools/unity_wait_run.ps1 -Method PixFootball.EditorTools.PhotoPlayerQa.RunAll -Log photo_qa      # renders into D:/PIXFootball/Renders/photoreal (else Docs/QA/photoreal)
```
(`-Extra "-qaWhat portraits,compare,roster,faces,clips,lods,eyes,skin,perf"` selects parts.)

## 1. R1 inspected (what it is, what was kept, what was replaced)

`A-P.glb`: 115 k tris, 19 meshes, 18 materials, 2048 px maps, 52-bone Mixamo-style rig (`rig:Hips ... rig:RightToeBase`, 15 finger bones per hand), A-pose (arms 36 deg below horizontal), 1.885 m with helmet, faces -Y, glTF scale 0.001 on the armature, one stray `Icosphere` helper (dropped). Every texture was opened and looked at (`Docs/QA/photoreal/` only keeps the new ones).
Real-world content found and what happened to it:

| R1 part | Content that identifies the player / team / brands | Treatment |
|---|---|---|
| `head`, `eyes`, `eyelashes`, `face.jpg` | the real player's face, hair, skin | **deleted entirely**; replaced by Draft Fever generic heads (see 3) |
| `jersey` + `jersey.jpg` / `jersey_nrm.jpg` | tiger stripes, "BENGALS", "BURROW", 9, NFL shield, Nike swooshes embossed in the normal map | albedo, mask and normal **regenerated procedurally** (no pixel of the original is used), number / name / wordmark now drawn by the PIX/Lit shader |
| `pants` | NFL shield, Nike swoosh, tiger side stripes | regenerated procedurally (stripe design = ours) |
| `socks`, `shorts` | orange wrinkle sheet | regenerated (stripes by height); `shorts` kept as the under-sock layer on the sock material |
| `helmet` | tiger stripes, "BENGALS" text | mesh kept (rigid, bone-parented); paint is the PIX helmet shader (team colour + stripe + crest from the helmet frame), no R1 texture |
| `facemask`, `chinstrap`, `Padding` | plain geometry, flat black / white | kept, own materials |
| `cleats` + `Cleat.jpg` | three Nike swooshes, hexagon tag, swoosh on the sole | knit / lace / sole detail kept; **all five marks painted out** of albedo and normal (`phr_textures2.py` `CLEAT_MARKS`), colours remapped to a team mask (white upper = primary, orange accents = secondary, rubber = dark) |
| `handwarmer` (pouch) | tiger head, Nike | albedo regenerated (plain), normal kept (fabric folds / stitching only, checked) |
| `towel` | NFL / Wilson logo | albedo regenerated, normal kept (terry cloth) |
| `wristcoach`, `twistband` | none | albedo regenerated (plain), normal kept |
| `arms` + `arm.jpg` | skin (no tattoos found) | skin map kept as the pore / vein / knuckle detail and **re-tinted to 8 skin tones**; normal kept; hands split into their own material slot for gloves |
| `armsleeve`, `innershirt` | plain white sleeves | luminance of the wrinkles kept, colour = accent colour |

Machine check on top of the eyes: `PhotoPlayerTests.NothingNamedAfterTheRealPlayerOrTheTeamShips` scans every shipped file / node / material name for burrow, bengal, nfl, nike, wilson, cincinnati, tiger.

## 2. Body types (S / M / L) by reshaping the MESH, not the bones

`phr_body.py`: every vertex of the cloth, skin and accessory meshes is moved by `v' = sum_g W_g(v) * (C_g(v) + S_g(v) * (v - C_g(v)))` where g = torso, neck, each arm, hand, leg, foot; W_g are R1's own skin weights summed per group, C_g the closest point on the bone axis, S_g a height / limb-position dependent radial scale (torso: width x depth per height). Shoulder pads are a smooth outward + upward bump on jersey and sleeve around each shoulder joint. The skeleton is identical for the three bodies, so one set of animation retargets fits all, and thick limbs deform with the same weights as thin ones (the previous "thin waist / legs" came from scaling bones).
Measured on LOD0 in rest pose (`Docs/QA/photoreal/body_measurements.txt`; hull circumference in m, width / depth in m):

| | Small | Medium | Large | existing DF Large (`Docs/QA/models.md` measure) |
|---|---|---|---|---|
| shoulders (jersey, z 1.52) circ | 1.32 | 1.39 | 1.49 | 1.57 (jersey+arms) |
| chest (z 1.30) circ | 0.98 | 1.10 | 1.31 | 1.35 |
| waist (z 1.15) circ | 0.91 | 0.96 | 1.18 | 1.20 - 1.27 |
| hips (pants, z 1.02) circ | 1.02 | 1.09 | 1.30 | 1.20 |
| thigh (one leg, z 0.85) circ | 0.66 | 0.75 | 0.93 | 0.73 (z 0.80) |
| calf (one leg, z 0.30) circ | 0.29 | 0.31 | 0.34 | 0.29 - 0.41 |

Large is clearly wider than Medium than Small at waist, hips and thighs (+30 % / +40 % over Small), with no thin spots; renders: `blender_bodies_SML.png`, `unity_{small,medium,large}_portrait.png`.

## 3. Heads and the fictional-roster system

Source: the 28 sculpted head blend shapes of the Draft Fever body FBX (`Head_01..36_SubD01`, one shared topology + UV, generic ZBrush heads: no real-person names; hair-groom folders named after real people were NOT touched). Twelve visibly different faces were selected (`S01..S12`), a 4K pore / wrinkle normal map (Rodrigo head 12) and the base skin texture are shared. The neck is fitted into R1's helmet, jersey collar and chin strap (eye centre + scale 0.969), neck weights Head / Neck / Spine2, neck thickened per body type.
* LOD0 head 13 k tris (Catmull-Clark of the cage, decimated, blend shapes transferred by barycentric mapping), LOD1 = the 5.5 k cage with all shapes, LOD2 2.2 k, LOD3 0.7 k.
* **Face proportion tweaks** authored on the cage: `T_JawWide`, `T_ChinLong`, `T_NoseBig`, `T_CheekFull`, `T_BrowHeavy` (weights -100..100, `Docs/QA/photoreal/faces_tweaks.png`).
* **Skin**: 8 tones (very light ... very dark) by re-tinting the shared maps (`PHR_Head_T0..7`, `PHR_Arms_T0..7`), existing HDRP SSS (`SubSurface_Head` / `SubSurface_Arms` diffusion profiles, Arm_Skin graph), `_Roughness_Value` 0.1 so skin is matte, not oily.
* **Hair as shells**: eyebrows (3 styles), facial hair (stubble, goatee, full beard, moustache), scalp hair (shaved, buzz, short) are 0.8 - 1 mm alpha-clipped shells with the head's blend shapes, tinted by 8 hair colours (black ... white) at runtime; beards need no head texture variants.
* **Eyes**: Eyes_Shader hue / saturation presets (brown, dark brown, blue, green, hazel; sweep in `eye_hue_sweep.png`).
* **Kit options**: visor (clear / smoke / mirror), sleeves (none / left / right / both, 6 colours), gloves (colour back / grip palm / cuff), wristbands, play card, towel, QB pouch.

`PlayerAppearance` (`Scripts/Art/PlayerAppearance.cs`, engine free): body, two head shapes + blend %, five tweaks, skin tone, facial hair, brows, scalp, hair colour, eye colour, visor, sleeves, gloves, wristbands, play card, towel, pouch. `PlayerAppearance.FromSeed(seed, body, role)` is deterministic (SplitMix64, integer only; `PlayerAppearanceTests`). 12 head shapes x 66 mix steps x 8 tones x 5 beards x 3 brows x 3 scalps x 8 hair colours x 5 eye colours before tweaks: every seed is a different face (24 seeds -> 24 distinct `FaceKey`s in the unit test).
Roster evidence: `Docs/QA/photoreal/roster_sheet.png` (24 generated players in 6 fictional kits, helmets on) and `roster_faces.png` (helmets off).

## 4. Uniform system

PIX/Lit (the graphics track's HDRP shader) uniform mode: `albedo = gray x team colour` with `_PixColorMask` (G = secondary, B = tertiary), jersey numbers / player name / team wordmark placed by `_PixNumFront/Back/ShoulderL/R`, `_PixNamePlace`, `_PixWordPlace` (computed by `phr_layout.py` from the 3D surface, upright digits, isotropic UV), dirt by `_PixRegions`, weave `_PixMicroNormal`, wet / snow from the global weather. No shader was edited. R1's jersey UV was stretched 2.2x along v (digits would have been squashed), so the jersey got a new deterministic isotropic UV (planar front / back / flank / shoulder, cylindrical sleeves), re-used (position based, clamped) by the LODs.
Textures generated from geometry: stripes, collar rib and trim, sleeve bands, side stripes, sock bands, panel seams from the UV island borders, athletic mesh dimples, folds. Helmet: PIX/Lit helmet mode (shell colour, centre stripe, crest decal slot) driven by the helmet frame that `PhotoPlayer` computes exactly like `PlayerAvatar.SetupHelmetFrame` (holder-space scale fixed for the x100 bone scale of Blender FBX, see PHR-021).


## 5. Rig and animation (Humanoid retarget verified)

R1's 52-bone `rig:*` skeleton is unchanged; all three bodies share it (bodies are reshaped meshes, not bones). Unity auto-map gives a valid Humanoid avatar on all three FBXs (`PHR_{Small,Medium,Large}Avatar`). FBXs are exported with `FBX_SCALE_ALL` so the `Armature` node is scale 1 (an earlier export had scale 100 on the armature, PHR-021, which also hit the helmet frame).
Verification (`PhotoPlayerQa.ClipsAndLods`, evidence `D:\PIXFootball\Renders\photoreal\clips_{small,medium,large}.png` and `posed_lods_{small,medium,large}.png`):
- 24 clips x 3 bodies (idle, 2-point / 3-point / DL / OL / shotgun stances, run, sprint, backpedal, hard cut, stiff-arm, tackle wrap, being tackled, OL pass set / engage, DL swim, QB throw / drop, catches high / low / dive, TD celebration, fall, hurdle) from the ArtLibrary clip keys, sampled with `AnimationMode.SampleAnimationClip` at 45 % of the clip: knees bend the right way, feet stay on the ground, hands and fingers follow, shoulders and pads follow the arms, no candy-wrapper at hip / shoulder / elbow, helmet stays on the head.
- 3-point stance, sprint and tackle wrap at LOD0, LOD1, LOD2, LOD3 for every size: the skinned mesh follows the animated skeleton at every level. Numeric check in the same run: every active SkinnedMeshRenderer has `bones.Length == bindposes.Length`, all bones inside the rig; helmet shell, face mask and chinstrap are children of the head bone at all LODs (also asserted by `PhotoPlayerTests.EveryLodFollowsTheAnimatedSkeletonAndTheHelmetSitsOnTheHeadBone`).
- Muscle values of the clips at rest agree with the avatar (standing idle: upper leg front-back 0.5, lower leg stretch 0.95).
Note for the ANIMATION track: stances / run clips look right on this rig; I have no clip problems to report. A few cells in the sheets show a player on the ground (fall, be_tackled, catch_dive) which sit low in the frame by design.

## 6. LODs (silhouette preserved, measured)

Built by collapse decimation per part (UV-seam and material-border protection where the texture is UV dependent, jersey re-projected with its LOD0 packing, overlay shells inflated by 0.3 - 2.8 mm so they do not sink into the layer below). Triangle counts of the parts that are visible by default (renders in `D:PIXFootballRendersphotoreal`, `PhotoQa` log "drawn tris"):

| | LOD0 (hero) | LOD1 | LOD2 | LOD3 |
|---|---|---|---|---|
| Blender, all parts incl. optional hair / visor / accessories | 70.4 k | 33.0 k | 14.1 k | 6.3 k |
| Unity, parts drawn by default (Small / Medium / Large) | 60.1 / 60.9 / 61.6 k | 27.9 / 28.3 / 28.6 k | 11.8 / 11.8 / 12.2 k | 5.1 / 5.1 / 5.4 k |

Switch distances (same as `PlayerLod`): 11 m / 26 m / 60 m, `PhotoPlayer.Bias` scales them. Head LOD0/1 keep the blend shapes; eyes LOD0/1 only (hidden from LOD2), hair shells and visor LOD0/1, rigid helmet parts have 4 LODs.
Silhouette check (`phr_lod_measure.py`, width x / depth y / hull circumference of torso, legs, arms, head, cleats at 5 - 6 heights, rest pose, vs LOD0): LOD1 within 0.7 %, LOD2 within 2.0 %, LOD3 within 3.4 % in width and circumference (4.8 % in depth at the ankle = 5 mm). `Docs/QA/photoreal/lod_measure_{Small,Medium,Large}.txt`.

## 7. Integration hooks for the lead (nothing is wired into Gameplay yet)

1. Spawn: `ArtLibrary.Load().Prefab("photo_player_" + size)`. The prefab faces +Z like the existing bodies (check `PlayerAvatar.ModelYaw` convention before reusing the -90 yaw), root = Animator with the FBX's Humanoid avatar, `applyRootMotion=false`.
2. `PlayerAvatar.Build` must skip its name-prefix material pass for this prefab (it replaces every material of `Torso*` / `Legs*` renderers); the photo parts are called `Jersey`, `Pants`, `Socks`, `Undersock`, `Arms`, `Cleats`, `Head`, `Eyes`, `Head_Brows/Beard/Scalp`, `SleeveL/R`, `Wristband`, `PlayCard`, `Pouch`, `Towel`, `Helmet_Shell`, `FaceMask`, `Chinstrap`, `HelmetPad`, `Visor` (`<name>_LOD0..3`). `Head*` / `Eyes*` names keep working for `HideHead`.
3. Team colours (LINEAR, exactly `TeamColors`): `photo.SetTeamColors(c.Primary, c.Secondary, c.Tertiary)`; helmet only: `photo.SetHelmetColors(...)`.
4. Number / name / wordmark / dirt: `PixLook.ApplyJersey(photo.JerseyMaterial, look); photo.ApplyLayout(photo.JerseyMaterial);` (ApplyLayout writes the PHR jersey placements and the name / wordmark widths over the DF values), dirt on the pants: `PixLook.ApplyJersey(photo.PantsMaterial, lookWithoutNumber)`. Helmet crest: `PixLook.ApplyHelmet(photo.HelmetMaterial, look)` (or `photo.SetHelmetDecal(tex)`) and the scuff vector as `PlayerAvatar.SetJersey` does.
5. Level of detail: either let `PhotoPlayer.Update` do it (`PhotoPlayer.ViewCamera = PlayerLod.ViewCamera; PhotoPlayer.Bias = PlayerLod.Bias`) or call `photo.SetLevel(PlayerLod.LevelFor(distance))` from the existing tick (`autoLod=false`). `PlayerLod(headBone)` also works on this prefab (Helmet holder layout); then set `manageHelmetLod=false`.
6. Appearance: `photo.ApplyAppearance(PlayerAppearance.FromSeed(player.Seed, body, role))` right after the colours. Store the seed with the player (the descriptor is deterministic and tiny). Role hints: 0 skill, 1 QB, 2 line, 3 receiver. Body type = the prefab size (S: WR/CB/QB, M: RB/LB/TE/S, L: OL/DL).
7. `HideHead(bool)` for first person, `SetPartVisible(key, bool)` for any option.
8. Rebuild after any art change: `PIX Football/Photoreal Player/Build All` (also re-adds the ArtLibrary keys; `ArtLibraryBuilder.Build` now calls `PhotoPlayerBuilder.AddArtLibraryKeys`).

## 8. Issue log

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| PHR-001 | fixed | S1 | legal | R1 head / face / hair | The real player's face, hair and skin are in the model | head, eyes and eyelashes deleted before anything else is built; Draft Fever generic heads fitted instead | `phr_build.py` (`for n in ("head","eyes","eyelashes")`), `compare_R1_vs_PHR_Medium.png`, roster sheets |
| PHR-002 | fixed | S1 | legal | jersey / pants / socks / helmet textures | Bengals stripes, wordmark, "BURROW", 9, NFL shield, Nike swooshes (also embossed in the normal maps) | none of these textures is used: masks, albedo and normals regenerated from geometry (`phr_textures.py`) | `textures_review.png`, `PhotoPlayerTests.NothingNamed...` |
| PHR-003 | fixed | S1 | legal | cleat texture + normal | 3 swoosh logos, hexagon tag, sole swoosh | polygons painted out of albedo and normal with patches of clean knit (`CLEAT_MARKS`) | `textures_review.png` (cleats tile), `unity_*_portrait.png` |
| PHR-004 | fixed | S1 | legal | towel / pouch / wristband | NFL + Wilson logo, tiger head, Nike | albedo regenerated plain, normals reviewed (fabric only) | `textures_review.png` |
| PHR-005 | fixed | S2 | uniform | jersey UV | R1's jersey UV is ~2.2x stretched along v: shader digits would be squashed 2x | new isotropic UV (`unwrap_jersey`), placements computed from the 3D surface (`phr_layout.py`) | `unity_medium_portrait.png` (readable 28 / ALDER / ROVERS) |
| PHR-006 | fixed | S1 | model | head LOD0 | decimating the subdivided head looked corrupt: python-created shape keys start at weight 1.0 (all 12 + 5 shapes summed) | all shape keys set to 0 before export, LOD0 built from a plain mesh | `faces_sculpted.png`, `faces_tweaks.png` |
| PHR-007 | fixed | S2 | LOD | jersey LOD1-3 | collapse with 300+ protected seam vertices pushed the back out by 6 cm; coarse faces reached into neighbouring UV islands (white patches) | seam protection off for the jersey, UV re-projected with LOD0's layout, classes taken from the nearest LOD0 face, clamped to the island | `lod_measure_*.txt`, `blender_lods_Large.png` |
| PHR-008 | fixed | S2 | LOD | arms LOD2 | material-border protection made the elbow collapse (-10 % width) | protection only up to LOD1 | `lod_measure_*.txt` |
| PHR-009 | fixed | S2 | LOD | sleeves / socks / pants LOD2-3 | decimated shells sank into the layer below (skin through the sleeve) | per-part inflate 0.3 - 2.8 mm per LOD, floors for small parts | `blender_lods_Large.png` |
| PHR-010 | fixed | S1 | prefab | LODGroup | the FBX importer turns `<Name>_LOD0..3` into LODGroups that cull the LODs `PhotoPlayer` activates (helmet missing at LOD1+) | LODGroups destroyed in the prefab builder | `lods_*.png` |
| PHR-011 | fixed | S1 | helmet | PIX/Lit helmet mode | the whole crown was painted with the stripe colour: the helmet-space scale was clamped at 0.01 in holder space but the FBX bones carry a x100 unit scale (holder space is in cm) | no absolute clamp (`PhotoPlayer.SetupHelmetFrame`) | `roster_sheet.png` (thin centre stripe) |
| PHR-012 | fixed | S2 | uniform | fabric | first pass looked like wet leather (smoothness 0.4, normal 0.9, large bumps) | matte mask alpha 0.2, normal scale 0.45 - 0.5, weaker patterns | `unity_medium_portrait.png` |
| PHR-013 | fixed | S2 | skin | Arm_Skin graph | `_Roughness_Value` is glossiness: default 0.49 made skin oily | 0.1 | `skin_roughness_sweep.png` |
| PHR-014 | fixed | S2 | tooling | FBX export | python-added shape keys, bone scale | see PHR-006 / PHR-021 | |
| PHR-015 | fixed | S1 | tooling | PhotoPlayerQa clip poses | clip sheets showed legs folded to hip height / sunk players and the Humanoid retarget looked broken, on the Draft Fever prefab as well | not the avatar: evaluating a humanoid clip through `AnimationPlayableUtilities.PlayClip` + `PlayableGraph.Evaluate` in an edit-mode batch run gives wrong leg rotations for every avatar; `AnimationMode.SampleAnimationClip` gives the right pose (DebugHarness: foot y 0.117 for idle on both models) | `PoseClip` now uses AnimationMode; `clips_*.png`, `posed_lods_*.png` |
| PHR-016 | fixed | S2 | uniform | jersey / pants sheen | jersey and black pants still read as wet leather in the roster (mask alpha 0.2) | fabric mask smoothness alpha 0.1 | `unity_*_portrait.png`, `roster_sheet.png` |
| PHR-017 | verified | S1 | prefab | skeleton binding | lead heads-up: skinned mesh stays in rest / A-pose while the skeleton animates, helmets detach (control vs deform bones, LOD bindposes) | not present on PIX_PhotoPlayer_*: the mesh is skinned straight to the 52 Humanoid bones, all LODs share them, helmet is parented to the head bone; checked numerically and by posed renders at every LOD | `posed_lods_*.png`, `PhotoPlayerTests` |
| PHR-018 | open | S3 | head / hair | hair shells | scalp hair reads as a tight cap and beard coverage is speckled at close range (alpha-clip procedural strands, no cards / strands) | needs hand-authored hair cards or a hair shader for a true photoreal look | `roster_faces.png` |
| PHR-019 | open | S3 | head | Draft Fever heads | the 12 sculpted heads + 5 authored shapes are game-grade scans of a stylised look, not photographic skin: faces are distinct and not any real person, but not as photoreal as a hero scan | swap in better head scans later (the head part is isolated, blend-shape names fixed) | `roster_faces.png` |
| PHR-020 | open | S2 | perf | 22-player frame rate | not measured: editor `Camera.Render` is 3 - 4.5 fps for both photo and current, meaningless | triangle numbers only: 22 players at LOD1 photo 633 k vs current 740 k tris; measure in a player build on the target laptop | `Docs/QA/photoreal/perf.txt` |
| PHR-021 | fixed | S1 | tooling | FBX export scale | `FBX_SCALE_NONE` leaves `Armature` at localScale 100 (bones carry x100): helmet frame clamp wrong (stripe over the whole crown), humanoid chains scaled | export with `FBX_SCALE_ALL`; helmet-frame clamp 1e-6 | `DebugRoot` log (all chain scales 1.00) |
| PHR-022 | fixed | S2 | integration | PlayerAvatar / Gameplay | nothing spawned the photo prefabs yet; `PlayerAvatar.Build` would overwrite the materials by name prefix | v0.6.0: photoreal is the default body, `PlayerAvatar.BuildPhoto` skips the name-prefix pass; see PHR-024 | gallery 9,914 checks, `Logs/rel_tests2.xml`, `D:PIXFootballRendersv060_releaseutotest2match_*` |
| PHR-023 | open | S3 | legal | store licence | R1 body / rig / topology derives from a store model titled after a real player; if the licence is editorial-only everything under `Assets/_Licensed/PhotorealPlayer` and `_AssetSource/PhotorealPlayer` must be swapped (nothing outside references them except the 3 ArtLibrary keys) | isolated folders | |

## 9. v0.6.0 release integration rows (release integrator)

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| PHR-024 | fixed | S1 | integration | PlayerAvatar, PlayStage, SimAgentView, PlayerLod, ReplayPlayer, MainMenuController, PlayerController | The photoreal prefabs were not spawned by anything; the user wants very high quality graphics | `PixSettings.PhotoPlayers` (default on; `-pixLegacyPlayers` / Settings > Gameplay > Player models for the old bodies); `PlayerAvatar.BuildPhoto`, `PlayerLod.AttachPhoto` (4 levels), `SimAgentView.SetIdentity` / `PlayerAvatar.SetAppearance` (deterministic from the player id), `RoleUtil.SizeOf(pos, weight)`, replay stores the player id, slim build whitelist has `photo_player_*` | Formation Gallery 9,909 / 9,914 (99.9 %), `PhotoDefaultTests`, `D:\PIXFootball\Renders\v060_release\autotest2\` |
| PHR-025 | fixed | S2 | head / hair | PhotoPlayer.ApplyAppearance | Lead review: the beard / stubble shells read as speckle | `PhotoPlayer.ShowFacialHair = false` (clean-shaven until hair cards, PHR-018 stays open for the scalp) | `PhotoPlayerTests.ApplyingAnAppearanceSwitchesSkinHairAndLevels`, `look_*_3_player_portrait.png` |
| PHR-026 | fixed | S1 | uniform | PixLitHooks.hlsl (PixTextOutline), JerseyLook.For | Lead review: chest wordmarks and numbers nearly illegible on dark (navy / black) jerseys: lettering used the trim colour with no outline, number colours were picked by an absolute luminance difference | name and wordmark use the number fill colour with a trim-colour outline ring; fill and trim are picked by contrast ratio (>= 3.4 against the jersey, >= 2.0 between fill and trim) | `PhotoDefaultTests.NumbersAndLetteringKeepContrastOnAnyKit`, `match_tackle_close_1.png`, `match_block_close_1.png` |
| PHR-027 | fixed | S1 | shader | PixLitProperties.hlsl | the build reported 2 `PIX/Lit` variants over the D3D11 limit of 16 samplers (alpha test + emissive, transparent back face): those variants fail to compile in a player | six jersey / helmet textures share the inline `sampler_TrilinearClamp` | build log: 0 shader errors (was 2) |
| PHR-028 | fixed | S2 | perf | PhotoPlayer shadows, QualityTiers | live play 1 %-low 51.5 fps at 2560x1440 High (22 photoreal players x 14 parts x 4 shadow cascades, floodlight rim lamp shadows) | shadow casters limited to jersey, pants, socks, arms and helmet shell; pooled bodies keep their appearance (no re-dressing hitch); High tier no longer casts rim lamp shadows (Ultra does) | autotest2: avg 108.1, 1 %-low 60.8, min 46.0 (was 96.5 / 51.5 / 31.4) |
| PHR-029 | open | S3 | perf | live play | 1 %-low 60.8 fps is below the 70 fps target at 2560x1440 High | none yet: remaining cost is the v6 look (volumetric fog about 2 ms, shadows) and LOD0 players in close-ups | `autotest2/player.log` |
