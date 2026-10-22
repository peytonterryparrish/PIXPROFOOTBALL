# INTAKE: "PPF NEW ASSETS" (user-purchased CGTrader set, ~40 GB)

Date 2026-10-21. Owner: ASSET INTAKE. Nothing here was imported into the Unity project (another agent is using Unity, a release merge is pending). Everything is staged on D: and was inspected / rendered in Blender 5.2 headless.
User owns all items (all listings checked = Royalty Free). Real-person / real-team / real-brand rule is unchanged from R1 (`photoreal.md`): body / rig / topology only, never faces, likeness, NFL / team / Nike / Wilson / adidas / Reebok marks.

* Staging root: `D:\PIXFootball\Source\PPF_NEW\<item>\` (13 GB on D:, nothing extra on C:, the user's folder was not touched or deleted).
* Renders: `D:\PIXFootball\Renders\intake\<item>.png`, **contact sheet `D:\PIXFootball\Renders\intake\all_new_assets.png`**, plus `player_animated_clips.png` (Kick / sprint / throw sampled on the rig), `trophy_sheets\every_trophy_200plus_sheet_1..4.png` (all 207 trophies with names), `_stadium\stadium_{aerial,top,field_level,low_front}.png`, `R1_burrow_reference.png` (R1 rendered with the same camera, for comparison).
* Scratch / work files: `D:\PIXFootball\Renders\intake\_work\` (per-view renders, measurement json, texture montages).

## 0. Summary table

| Item | Staged at (D:\PIXFootball\Source\PPF_NEW\) | Format kept | Size staged | Verdict |
|---|---|---|---|---|
| 4 A-pose players (Derwin James, Jalen Ramsey, Jordan Love, Justin Jefferson) | `players\<name>\A-P.FBX` + `textures\textures\` | FBX + textures.zip content (GLB/GLTF/OBJ/Blend/C4D/Max skipped) | 211 MB | **Good.** Same 52-bone `rig:*` skeleton and same base topology as R1. Usable as 4 extra body bases (small builds differences, 1 tall QB). Everything on them except bone/mesh structure must be stripped. |
| American football player animated | `player_animated\{anims,mesh_tex}` | 7 animation FBX + 7 `.bip` (3ds Max only) + OBJ mesh + 8K textures; PSD / Marmoset / `.max` skipped | 411 MB | **Best raw body quality** (62 k tris, 8K), 76-bone Biped, clean mocap-like clips. Head is a real-person scan, jersey / helmet have the seller's "OUTSOURCE2.US / O2U" marks, tattoos. Great as a source of clips + a hero-body reference. |
| Helmet | `helmet\` | FBX base + 4K PNG textures (13 other formats skipped) | 36 MB | **Good.** Generic modern shell, plain green, 105 k tris (40 parts) -> needs LODs. |
| Shoulder pads | `shoulder_pads\` | FBX + 4K PNG | 398 MB (PNG) | **Very good** detail, 47 k tris, static (unskinned). Highest-value gear upgrade for the "bulky padded" silhouette. |
| Football uniform | `uniform\` | High / Low-poly FBX + Textures | 598 MB | **Poor value.** Garment pieces on an invisible mannequin, no rig, textures not linked, "low poly" = 935 k tris, "high" = 12.5 M tris. Skip except as a reference. |
| Cleats | `cleats\` | FBX (texture + materials variants) | 1.2 MB | **Low.** 5.7 k tris each, faceted, soccer-boot look with 2 white stripes (adidas-like). R1's cleats are far better. Skip. |
| Referee uniform | `referee\` | FBX + 4 x 4K TGA | 193 MB | **Usable with work.** Nice striped kit, 12.8 k tris, garment only (no body, no rig, 1 material). Needs a body under it + skinning, and the shoe logo painted out. |
| Realistic male head | `male_head\{unpackaged unitypackage, unpacked\}` | unitypackage (kept) + the FBX/textures unpacked from it | 113 MB | **Medium.** Clean generic bald head + neck/shoulder bust, 63 k tris, 4 expression blendshapes + 505-bone Rigify face rig, 2K textures, URP Lit materials. |
| Stadium | `stadium.unitypackage` (3.4 GB copy), `stadium_pkg\` (unpacked tar, 2250 GUID folders), `stadium_index.tsv` (GUID -> path) | unitypackage (+ the single 2.9 GB FBX inside it as backup); 3dsmax / Blender / C4D / alembic / GLB / GLTF / OBJ / Textures zips NOT extracted | 6.8 GB | **Spectacular geometry, not usable raw.** 63 M unique / 83 M instanced polys, Built-in Standard materials, soccer-layout oval bowl with roof. Needs heavy re-authoring (see 6). |
| Jumbotron / scoreboard pack | `jumbotron\` | FBX + Vray PNG textures (UE pack, Max/ma/stl/obj skipped) | 173 MB | **Good.** 3 designs, 11.5 k tris total, UDIM texture sets (no UDIM in Unity: re-pack). |
| Retractable stadium tunnel | `tunnel\` | OBJ + c4d + tex (animation is in the .c4d only) | 33 MB | **Medium.** Static extended tunnel (487 k tris, 424 parts), logos baked: adidas + "Sheffield United FC" PSD. |
| Draft House stage | `draft_house_stage\stages.fbx` | FBX (no textures, flat materials) | 15 MB | **Good.** Clean modern arch stage + truss + podium + 2 lounge sofas (133 k tris each). |
| Sports news studio | `news_studio\Sport News v1.fbx` | FBX (textures not in FBX; the .blend / .max have them, not extracted) | 3.7 MB | **Good.** 113 k tris, curved set, 17 lamps, glass desk, video wall. |
| Cheerleader | `cheerleader\` | **3ds Max only** + PNG textures + seller QA renders | 69 MB | **BLOCKED: no FBX/OBJ in the purchase.** Cannot be opened without 3ds Max. |
| Cheering crowd pack | `crowd_pack\` | FBX static + ANIM FBX + .blend + 6 atlas PNG | 30 MB | **Low fit.** Stylized faceless low-poly (1 k tris), 6 sit/stand cheer clips. Not photoreal. |
| Football trophy | `trophy_football\` | cgtrader FBX + FBX/TEXTURES rar contents | 91 MB | **Good, generic**, ready for a "PIX Bowl" trophy (remove the lightning bolt). |
| Every Trophy (200+) | `trophy_every\EveryTrophy.fbx` (copy of the user's FBX) + `trophy_packs\*.zip` (13 sub-pack zips, unextracted, dups of the above; `_x\` has USA + Football Awards FBX) | FBX | 4.1 GB | **NOT usable as-is.** All 207 are SOCCER trophies, replicas of real competitions. |

## 1. Four A-pose player bodies (3dddpassion, same author as R1)

### 1.1 Rig: matches R1 exactly
All four have the **same 52-bone Mixamo-style skeleton as R1** (`rig:Hips ... rig:RightToeBase`, 15 finger bones per hand, `rig:` prefix, same names, same hierarchy, armature scale 0.001). Bone name lists were diffed against R1: identical. Rest-pose proportions differ slightly per body (hip height 1.06 / 1.07 / 1.13 / 1.08 m vs R1 1.08; shoulder width 0.43-0.46 m), so Humanoid retargeting (auto-map on all four, as for R1) is expected to work and the existing 24 retargeted clips apply. Not verified in Unity (Unity not touched).
Topology: arms 23,528 tris, jersey 3,491, pants 2,053, shorts 890, socks 748, helmet 18,778, facemask 14,390 are **tri-identical to R1** on the new bodies, i.e. the same base mesh reshaped (+ different head / extra parts). So `phr_build.py` / `phr_body.py` can take these as alternative sources if it is parameterised by source path and a mesh-name map (names differ: Derwin `armsleeveL/R`, `bag`, `eyeL/R`; Jalen `innershỉt`, `glass`, `eyes`; Jordan `beard`, `wristcoach`; Justin `armL/armR`, `armband`, `Visor`, `padding`, `beard`).
Geometry notes: FBX armature scale 0.001 and `rig:` bones carry over; extra heads: Jalen / Jordan / Justin heads are 24.8 k tris (higher-res), Derwin's head is 6 k. Justin's cleats are 31 k tris (heavy), Jordan's chinstrap 27.8 k. Derwin's `arm.jpg` skin map has a visibly broken UV smear (torn texture on the back/arms) - do not reuse it (the PHR pipeline already re-tints R1's own arm map anyway).

### 1.2 Bodies (rest pose, jersey / pants hull circumference in m at R1's height fractions; `measure()` from `phr_common`)

| | Height (with helmet) | Hip bone z | Shoulders | Chest | Waist | Hips | Thigh | Calf | Read as |
|---|---|---|---|---|---|---|---|---|---|
| R1 (Burrow, existing base) | 1.885 | 1.08 | 1.316 | 0.952 | 0.904 | 1.018 | 0.651 | 0.294 | QB, lean |
| Derwin James | 1.911 | 1.06 | **1.408** | 0.988 | 0.949 | 1.023 | **0.697** | **0.319** | **S / LB**: broad shoulders, thick thighs and calves (+7 %) |
| Jalen Ramsey | 1.888 | 1.07 | 1.331 | 0.951 | 0.907 | 1.004 | 0.647 | 0.289 | **CB**: ~ identical to R1, lean |
| Jordan Love | **1.975** | **1.13** | 1.371 | 0.981 | 0.930 | **1.061** | 0.685 | 0.291 | **tall QB / TE**: +9 cm, long legs, narrow calves |
| Justin Jefferson | 1.898 | 1.08 | 1.359 | 0.959 | 0.923 | 1.046 | 0.684 | 0.316 | **WR / RB**: wide hips, strong calves |

Honest read: the differences are small (<= 8 %, +9 cm tall for Love). They give five slightly different athletic body bases (R1 + 4), not the S / M / L spread (+40 % thigh) that `phr_body.py` produces. They are best used as: (a) additional base meshes feeding `phr_body` (each reshaped S / M / L, so roster body shapes multiply from 3 to 15 without new bone sets), (b) the tall QB / TE (Love) and the stocky safety / LB (Derwin) as ready-made proportions. They do NOT replace the L (OL / DL) body.

### 1.3 Likeness / branding that must be removed (everything below is real-person or licensed; the same rule as R1)

| Player | Remove / regenerate |
|---|---|
| All four | `head` + `face`/`head.jpg` (real face scans, 2K), `eyes`, `eyelashes`, `hair` + `hair/hair_tr` (Derwin, Ramsey braids / twists), `beard`+`beard_tr` (Love, Jefferson), facial hair in the face maps; jersey + pants + socks + shorts + sleeve + glove + towel/pouch albedo and normal maps (embossed Nike / NFL shield in the normals, like R1); helmet + facemask paint; cleat albedo and normal; NFL shield at collar; names and numbers on jersey (JAMES JR. 3, RAMSEY 5, LOVE 10, JEFFERSON 18) |
| Derwin James (Chargers) | blue / gold LAC scheme, lightning-bolt logo on helmet, pouch and cleat tongue, "C" captain patch, gold Chargers facemask colour, Nike |
| Jalen Ramsey (Steelers) | black / gold, Steelers hypocycloid logos on helmet + jersey patch, gold gloves, Nike on pants / cleats |
| Jordan Love (Packers) | green / gold, "G" helmet logo, Nike, yellow Nike cleats, `wristcoach` (kept as geometry, repaint) |
| Justin Jefferson (Vikings) | purple / white, horn logo, the purple "JJ / 18" cleats print + lightning pattern, 'googly' helmet decals, visor tint, Nike |
| Arms | No tattoos found on the four `arm.jpg` maps (checked by eye). |
Reusable as-is after repaint: all geometry except head / hair / eyes (arms, hands, gloves, sleeve, jersey, pants, socks, cleats, helmet, facemask, chinstrap, padding, pouch).

### 1.4 Required work
1. `phr_build.py` source parameter + mesh-name map (0.5-1 day); run `phr_textures*` on the new maps; 2-3 hours per body to verify weights / UV (same jersey UV remap as R1 since the jersey UV is R1's stretched layout, verify).
2. Heads: use the existing Draft Fever head set (as for R1); do NOT use these heads.
3. Add to `PhotoPlayerTests` banned-name scan: james, ramsey, love, jefferson, chargers, steelers, packers, vikings, lightning bolt etc.
4. LODs: same as R1 (61 k LOD0 budget per player holds, Justin's cleats must be decimated to <= 13 k).

## 2. American football player animated (generic photoreal, Biped)
Files: `anims\{Kick,Throw,sprint,sprintball,MachoWalk,Helmet_TakeOn,Clinch}.FBX` (each = mesh + rig + 1 take), `anims\*.bip` (Biped motion files, 3ds Max only), `mesh_tex\` (`fotbalista.obj` 5 MB, 8K body color `col_telo.jpg` + normal 62 MB + gloss + spec, 8K head set, 2K helmet set, hair cards `.dds/.tif`).
Skipped: the 1.4 GB `fotbalista_mapy_max.rar` (PSD layers, 1.8 GB unpacked), `Body_color-PSD.rar` (1 GB), `helma_psdcka.rar`, Marmoset scene. (Not extracted, still inside the user's zip.)

* Mesh: 61 k tris total (body `telo` 14.6 k, helmet 20 k + 2.2 k, head 5.4 k + 6.2 k + beard cards, eyes 2 k + glass 1.9 k, shoulder pads `omotavky` 1.6 k). 8 materials, textures 8192 (body, head) / 2048 (helmet). Height 1.94 m. Strong muscular detail, dirt / grass stains painted in.
* Rig: **3ds Max Biped, 76 bones** (`Bip001 Pelvis / Spine / Spine1 / Spine2 / Neck / Head`, `L/R Clavicle / UpperArm / Forearm / Hand`, 5 fingers x 3 + Nub, `L/R Thigh / Calf / Foot / Toe0`, plus `jaw`, 6 eye bones, `Helmet_bone001`, `ForeTwist`, `Footsteps` dummy). This is the standard layout Unity's Humanoid auto-map handles (as for any Biped): spine = Spine..Spine2 (3 -> spine / chest / upperchest), clavicle = shoulder, fingers 5 x 3. Expect a valid Humanoid. Extra bones (jaw / eyes / helmet) stay as non-humanoid transforms. Not yet verified in Unity.
* Head: texture `hlava_col2.jpg` is a real scan (source path in the FBX: `Hlavy\Opravy2\2847_Harris_Elias_05` = a store head-scan of a person), eyes / hair share the same set. **Treat as real-person likeness: do not use the head.**
* Branding / identity on the body: "OUTSOURCE2.US" and "O2U" text baked in jersey, pants, helmet, gloves (seller's studio branding), number 2, burgundy / gold scheme that looks like a real team (no official mark, but replace), tattoos on the right forearm / torso (skull + tribal) painted in the body map. All in `col_telo.jpg` + `helma_col.jpg` -> must be repainted; the PSD layers (not extracted) would make this easy: **extract `Body_color-PSD.rar` only when starting that work.**
* Hairs: `hair cards` use `.dds/.tif` alpha maps (1024).

### 2.1 Animations (sampled on the rig; strips in `player_animated_clips.png`)

| Clip | File | Length | Rate | Root motion (pelvis) | Notes |
|---|---|---|---|---|---|
| **Kick** | Kick.FBX | **2.67 s** (80 f, 30 fps) | 30 | 2.7 m forward (approach steps), ball bone `Ball_bone` animated | Full place-kick / kickoff: 2-3 approach steps, plant, swing, follow-through. Clean, no visible foot slide in samples. Ball + tee (`Cylinder001`) separate objects. |
| **Walk** | MachoWalk.FBX | **1.33 s** loop | 60 | not measured | **Blender 5.2 cannot import this file** (`KeyError: Bip001` in the FBX importer, a mesh bound to a missing bone). 76 limb nodes + 88 clusters per `parse_fbx`. Unity's FBX importer is usually tolerant, but this was NOT verified. Re-export from the `.bip` (Machowalk.bip) in 3ds Max, or import with the mesh removed. This is a "macho" strut, not a neutral walk. |
| Sprint | sprint.FBX / Sprint.bip | 0.53 s loop (32 f at 60 fps) | 60 | **in place** (0.0 m) | Single full gait cycle, pelvis stays in place: ready for locomotion blend tree speed-scaling. |
| Sprint with ball | sprintball.FBX | 0.63 s loop | 60 | in place | Ball carried in right arm. |
| Throw | Throw.FBX | 1.67 s (50 f) | 30 | 1.2 m forward | One QB throw, ball bone parented to `Bip001`. |
| Helmet take-on | Helmet_TakeOn.FBX | 3.9 s | 30 | in place | Two-handed helmet removal: good for the pre/post-game and celebration cutaways. |
| Clinch | Clinch.FBX (+ clinchleft/right .bip) | 2.67 s | 60 | 2 skeletons (`Bip001`, `Bip002`), 152 bones | Two-player grapple / tackle clinch (player vs player), 120 k tris with both meshes. The most valuable one for contact takes, but it needs two retargeted bodies. |
Notes: FBX time mode differs per file (30 vs 60 fps); Blender shows frames at the scene fps (30) so lengths above come from the FBX AnimationStack. Clips ship with the full mesh (3-7 MB each); for Unity export animation-only FBX with the same skeleton (Blender, `bake_anim`, mesh unchecked) or just point Unity's Humanoid avatar of `Throw.FBX` at the others.
Project already has kick / punt / walk / throw mocap (`Docs/AnimationCoverage.md`), so these are **alternates** (second kicker style, right-leg only: mirrors needed, see ANI-017), plus helmet take-on and clinch which are not covered.

## 3. Gear
### Helmet (`helmet\`)
* 40 meshes, 104,698 tris, 7 materials (Dome, Pads, Metal / Plastic caps, facemask, chinstrap), **4096 maps** (diffuse, bump, glossiness, reflectivity). Dimensions ~ 27 x 38 x 28.5 cm (units are cm). Plain green dome with a tiny certification sticker at the back; no logos, no brand text visible. Facemask is a modern 13.4 k tris cage; pads fully modelled inside.
* Use: replacement / variant helmet with better inner-pad detail; needs LOD0 ~ 12 k, LOD1 5 k, LOD2 2 k (cage as alpha / thin tris); the existing helmet pipeline (`PIX/Lit helmet mode`, crest decal, frame math) can drive it. Remove the sticker label.
* Risks: shape is clearly Riddell-SpeedFlex-like (real product design) but no marks.

### Shoulder pads (`shoulder_pads\`)
* 14 meshes, 47,400 tris, 4 materials (red plastic, grey strap, carbon fabric, chrome), 4K maps. Unskinned, A-pose width 0.96 m x 0.46 m tall x 0.37 m deep, sits at z 0.23-0.68 (origin at pelvis, not centred on the shoulders).
* Use: the R1 pipeline fakes pads by bumping the jersey mesh; real pads (shell + epaulets + chest plate + back plates) would give the big-padded 2K8 silhouette (`REFERENCE_AllPro2K8.md`: "Bulky padded silhouettes"). Needs: scale to each body (S/M/L), re-skin to Spine2 / Shoulder / UpperArm bones (rigid parts bone-parented, strap + fabric weighted), jersey pushed outward under them, decimate to ~ 10 k LOD0 / 4 k LOD1 / 1.5 k LOD2 (bake normal from the 47 k), recolour via the team mask (red = secondary).
* Risk: none seen (no logos). Original is a sports-store-style pad (Riddell-like).

### Uniform (`uniform\`)
* High-poly FBX = **12.5 M tris**, Low-poly FBX = **935 k tris** (inner shirt 142 k, socks 116 k, mask 96 k, pants 24 k, gloves 22 k, shoes 22 k, shirt 9 k). The ZTL (ZBrush tool, 188 MB) was not extracted. Pieces sit on an invisible figure in an A-pose, no skeleton, no UV-linked textures in the FBX (materials are flat `wire_*` colours; the 30 texture files in `Textures\` have to be assigned by hand). Orange / black "20" jersey, no brand text. Also contains a "Floor" cyclorama plane.
* Verdict: **not worth integrating**: nothing on it beats R1's jersey / pants (3.5 k tris with a clean UV). Possible use only as a **sculpt reference** for fabric folds or to bake a tileable normal / wrinkle map from `Shirt_normal.png`, `Pants_normal.png`.

### Cleats (`cleats\`)
5.7 k tris each (2,848 polys), 4K PNG, faceted low-poly look, 2 white stripes (adidas-like) -> would need a repaint. R1's cleats (13 k, painted-out marks) are better. **Skip.**

### Referee uniform (`referee\`)
* 1 mesh `referee_body`, 12,773 tris, 1.58 m tall garment (shirt + white knickers + striped socks + black shoes, belt), 3 x 4K maps (`Referee_body.tga` albedo, `_norm`, `_refl`, `_spec`, all 48 MB TGA = convert to PNG/BC7). No head / hands / rig. Looks right for an NFL-style official (the 7-stripe shirt, white cap not included). A Reebok-style logo is on the shoe in the texture; a small logo box top right of the texture; the cap and a number box on the chest are plain.
* Use: replace/upgrade the existing referee (Draft Fever referee has its own baked mesh, `Tools/Blender/check_referee_baked.py`). Needs body (use a Medium R1-derived body + Draft Fever head), skin transfer, paint out the shoe logo, convert to the PIX/Lit uniform mask. ~ 1-1.5 days.

## 4. Realistic male head (`male_head\unpacked\`)
* Unitypackage -> `Meshes\...fbx` (27 MB), prefab, 12 textures 2048 (base color, normal OpenGL, roughness, AO, scattering map, 6 eye colours), **URP Lit** materials (guid `933532a4...` = Universal/Lit), Animator controller.
* Mesh 63 k tris head + 7.8 k mouth + brows 2.3 k + lashes 1 k + 2 x 512-tri eyes, bald, with neck / upper chest.
* Facial: **only 4 blendshapes** (Smile, Sad, Angry, Worried) + a 505-bone Rigify face rig; actions Blink, Look*, Speak, Turn*, Test. This is far fewer than Draft Fever heads (28 sculpted blendshapes, 4K normal). Textures are 2K (vs 4K shared normal in PHR).
* Generic, no obvious real-person likeness (a base-mesh style average head) -> usable as extra face variety for celebration / cut-scenes / Draft House presenter close-ups. Needs HDRP material conversion (use the Arm_Skin SSS graph / `SubSurface_Head`), a 4K normal bake, bind to the body neck. **Medium value**: the project's face system is already better; use it for a "hero reaction" head only (~ 1 day).

## 5. Gear / set / scene items

### Jumbotron / scoreboard pack
FBX: 3 designs (`Jumbotron_scoreboard_squared` 3.2 k tris, `_hexagon` 1.6 k, `_double` 1.1 k), 11.5 k tris total, units off (bbox 44 m wide when imported as m). Textures come as **UDIM tiles** (`.1001..1008`, BaseColor 10-14 MB each + Normal, Roughness, Metallic, Height, Emissive for the hexagon) from an Unreal pack. Unity has no UDIM -> merge tiles to 1-2 atlases (<= 2K each) or split the mesh by material. Screens are plain panels: use for the real-time video-wall (render texture) at the scoreboard. Good fit for the stadium upgrade / Draft Fever Bowl replacement (the project's own banner shader).

### Retractable stadium tunnel
`RETRACTABLE_TUNNEL.obj` 424 meshes, 487 k tris, Y-up, centimetres (14 m long, 5.5 m wide, 6.75 m high, extended state; the retract animation is only in `.c4d`, not usable in Unity). Materials all `default`, `tex\` has `Adidas_Logo copy.jpg`, `Sheffield_United_FC.psd` and a "Warehouse-with-lights" HDR-ish jpg: **both brands must go** (the `LOGOS` / `CANVAS` meshes are the carriers). Needs: scale 0.01, split into the ~ 20 telescope hoops + inflatable skin, add own team-colour canvas + PIX logo, decimate (3 k tris per hoop is overkill at 40 m viewing distance). Medium value for the player-entrance cutscene.

### Draft House conference stage
`stages.fbx` 12 meshes, 592 k tris; **132,828 tris each for the 2 sofas** (decimate to ~ 15 k), truss 14 k, arched backdrop, podium, 2 mics, stairs; 10 flat materials (Aluminum, Backdrop Display, Fabric, Front Desk, Main Stage Color, Stage Base...), no textures. Cream / white stylised set. Perfect as the "Draft House" presentation set; add the PIX Football backdrop display (render texture) and light it via area lights. ~ 1 day.

### Sports news studio
`Sport News v1.fbx` 37 meshes, 113 k tris (17 lamps x 5.4 k = 91 k, rest ~ 22 k) + `SM_Box` (a 16 x 20 x 5.6 m dummy room box that hides the set; delete it) + curved video wall, glass desk with `M_News` screen, stage; 56 materials, **18 texture references broken** (FBX has no textures; the `.blend` and `.max` in the zip carry them but were not extracted - extract `Sport News v1.blend` (42 MB) when needed). Unreal naming (`SM_*`, `M_*`). Fit: halftime / post-game / draft desk. ~ 1-1.5 days (rebuild materials in HDRP, bake lamps to 1 k tris, add video-wall RT).

### Cheerleader (rigged)
`Teenage_Black_Girl_Cheerleader_Rigged_*` zips contain **only 3ds Max files** (`max_corona/mentalray/scanline/vray`), 16 PBR maps (V-Ray Diffuse / Normal / Glossiness / Displace / Opacity / Scatter, 8 MB-14 MB PNGs, probably 4K) and seller QA renders. **There is no FBX / OBJ in the purchase** so it cannot be opened in Blender / Unity as-is. Action: either open it once in 3ds Max (export FBX) or ask CGTrader / the seller for the FBX (the listing normally includes it). Seller's renders (`cheerleader.png`) show a high-quality, stylised-realistic girl with pom-poms (white / purple outfit) - suits sideline atmosphere. Rig type unknown.

### Cheering crowd pack
6 characters (3 male, 3 female) x static FBX + ANIM FBX + glTF + OBJ + `.blend`; 1,030-1,318 tris each, 8000 x 1000 colour-swatch atlas per person, Rigify-like 58-bone `Metarig`, 6 actions of 99 frames (Sit Cheer Clap, Sit Cheer Clap Slow, Sit Cheer Pump, Stand Cheer Clap slow, Stand Cheer High Clap, Stand Cheer Pump). Faceless low-poly "Studio Ochi" style - **does not match the photoreal target**. The existing Draft Fever crowd (billboards with 2 poses + sit/stand anims, `CrowdReaction`) is the better base. Could provide only distant stylised filler or GPU-instance clip references. **Low priority.**

## 6. Stadium (OlegWER "Modular American Football Stadium Exterior Interior Props")
Unpacked the 3.3 GB unitypackage into `stadium_pkg\` (GUID folders) and indexed it in `stadium_index.tsv` (GUID, original path, size). Contents:

| Kind | Count | Notes |
|---|---|---|
| FBX | 1 (2.9 GB, `Assets/OlegWER/AmericanFootballStadium/FBX/...` guid `b634ed16...`, FBX 7400) | 1,436 mesh nodes (`SM_asset_NNN`) + 87 nulls (`FootballStadium` root, `Corona_Camera*`/`Corona_Sun*` leftovers from Corona Renderer) = 611 unique geometries, **57.2 M vertices, 63 M unique polys, 83 M polys with instances**. |
| Prefabs | 1,436 | one per mesh (`SM_asset_N` + child `Model` rotated -90 deg X + MeshFilter / MeshRenderer). No script, no collider. |
| Materials | 375 `.mat` | 372 Built-in **Standard** (shader fileID 46), 3 Standard fileID 210 (legacy / transparent); 340 opaque, 34 transparent Mode 3 / 1 Mode 2; only 90 use a normal map, **0 use a metallic / smoothness map** (the packed `MetallicRoughness` and `ClearCoat*` PNGs are NOT wired), 165 have a base color, the other 210 are flat `_Color`. |
| Textures | 430 PNG, 518 MB | 427 are **1024 x 1024**, `T_3_2_200000_Tex.png` 8192 (the pitch grass), `T_xmppmax..` 4096 (concrete), `poster.png` 8192 x 1226 (banner). Unreal-style names (`Mi_00098_SM_asset_1205_Normal.png`, `WorldGridMaterial_*`) = exported through Unreal. |
| Scene | `SampleScene.unity` (23 KB) | sun + 8 prefab instances only. |
| Pipeline | **Built-in Standard** | project is HDRP 17.6: all 375 materials need HDRP/Lit (or PIX/Lit) conversion, metallic-roughness unpacked (G = roughness, B = metallic; invert for HDRP smoothness), tangent-space normals checked. Standard transparent -> HDRP Lit transparent, cutout for fences. No shaders shipped. |

Layout (rendered from the FBX, `_stadium\*.png`): modern oval soccer-style bowl, 246 x 172 x 63 m bounding box (x -130..116, y -87..85, z -20..42 m), steel / membrane roof ring (`M_roof`, 9.2 k polys, z 19-42 m), two tiers with modelled individual seats, VIP / media boxes (680 k-poly modules), tunnel / entrances, facade, ribbon-board banner at the east end (`Mi_00074` = poster). **The pitch is a 12-poly plane (`SM_asset_438`, 122 x 61 m, z 2.1 m) with the 8K grass texture**; an NFL field (109.7 x 48.8 m with end zones + ~ 3 m margin) fits, but the first seat rows are only ~ 5 m off the touchlines (soccer geometry), so the project's usual sideline space (benches, camera platforms, 2K8-style framing) needs the seating pulled back ~ 8-10 m or the bowl scaled in Y.
Weight: the 40-odd seat meshes are **0.3 - 2.46 M polys each, ~ 50 M polys in total**, most of it `Mi_00088/00104/00105/00106` (seat plastic) i.e. every seat is real geometry. This is a render-farm stadium (Corona): not real-time. FBX unity meta has `indexFormat: 0`, `isReadable: 0`, compression off.

Brand / third-party content found in textures (looked at all 145 base color maps, contact sheet in `_work\stad_tex_sheet.png`): ~ 14 poster / banner images (AI-generated "American Football Championship" art showing players in navy / blue star-helmet uniforms that imitate the Dallas Cowboys, red-and-gold Chiefs-style uniforms with arrowhead helmets, Eagles / Patriots style marks, an actual **NFL shield and Patriots / Chiefs logos on `poster.png`**), national flags (USA, Spain, Germany, South Korea, Canada, UK), EXIT signs, a basketball parquet floor tile, and a football field texture with yard numbers. **All posters + the "GRAND FINAL" ribbon-board artwork must be replaced with PIX banners**; flags and exit signs are fine. Atlas textures for people (crowd color blocks) in the 1K tiles are faceless swatches (no brand).

Conversion / integration verdict: use as a **kit-of-parts source for a re-authored stadium**, not as an import:
1. Do NOT drag the 2.9 GB FBX into Unity: the importer would need > 40 GB RAM and hours, and 63 M polys cannot render. Import from the unpacked package selectively (the unitypackage on D: can be filtered with the existing `UnityPackageUnpacker`, which writes to `Assets/_Licensed/...` without the old scripts).
2. In Blender: keep **roof, facade, concourse, tunnels, ribbon boards, light towers, press / VIP boxes, signage frames** (everything < 50 k polys: about 400 of the 611 geometries), and **rebuild the seating bowl**: replace seat meshes with one low-poly seat row module (~ 400-800 tris, instanced) + a baked normal-mapped seat strip, or keep a 2-level LOD (LOD0 near, billboard crowd as the project already does). Pull the stands back 8-10 m or build NFL sideline extension.
3. Re-bake a 2K atlas set per area (the 1K per-prop textures x 430 are too many materials: merge 375 materials to ~ 40), HDRP-convert, add lightmaps / light probes.
4. Effort: 4-6 days for a playable "Pro stadium 2" with LODs and colliders; 8-10 days for the high-quality version with interior (concourse, tunnel).
Alternative (cheaper, less risk): keep the existing Draft Fever bowl and **cherry-pick** the roof structure, light towers, ribbon boards, tunnel mouth and media / VIP box meshes from here (~ 1.5-2 days).

## 7. Trophies
### `american-football-trophy` (generic; usable)
Single mesh 19,865 tris (10 k polys), 0.22 x 0.19 x 0.40 m, 3 x 4K PBR maps (BaseColor, Metallic, Roughness, Normal, AO, Height in the TEXTURES rar), silver-pewter helmet + football + star medallion on a hexagonal base. **The helmet carries a lightning-bolt decal** (resembles the LA Chargers logo): repaint / remove it before shipping. Otherwise generic. Use as PIX Bowl / conference title trophy (scale up for a hero 1:1 model, add engraved plaque; re-colour with gold, silver, bronze variants via the metal maps) and as MVP award base.

### `Every Trophy (200+)` and `Seperate Packs` (13 category zips)
207 objects, 23 M tris, 609 materials, 93 images (embedded names, 2K - 4K); the 13 pack zips (English, European, French, German, International, Italian, Other Continent, Other Leagues, Other Trophies, Saudi, Spanish, USA, Football Awards (41 award trophies, 6.1 M tris, 1 GB FBX), all contain the SAME models as the big file; USA Football = 7 MLS trophies). **All soccer; each is a replica of a named competition trophy** (see sheets `trophy_sheets\every_trophy_200plus_sheet_*.png` with names): FIFA World Cup family, UEFA Champions / Europa / Euro, Premier League / FA Cup, Bundesliga (Meisterschale), Copa Libertadores / America, Ballon d'Or, Golden Boot, Puskas, MLS Supporters' Shield, etc. Several carry real crests, text and logos in the textures (UEFA, FIFA "Best", Premier League POTS, CONMEBOL, KNVB).
**Not usable as-is** (replicas of copyrighted / registered 3D trademark designs). Policy: use none unchanged.
Candidates that are generic enough to be reshaped into PIX awards after changing all text / crest / proportions (still derived from soccer trophies; treat as sculpt starting points only, and replace the geometry details):
* Classic two-handled loving cups on a plinth with no crest on the model: `Catalan Cup`, `Belgian Super Cup`, `Carolina Challenge Cup`, `Regional Championship`, `Segunda Divison`, `Women's U21s PGA Cup`, `Primera Divison`, `Uruguay Primera Division`, `Hungary Football League`, `Ibero American Cup`, `Scottish Cup`, `Irish Cup` -> "Golden Helm"-type conference cup bases.
* Tall modern spires / sculptural columns (no ball): `Audi Cup`, `Danish Cup`, `Campeonate Paulista`, `Super Coppa Italiana`, `Swedish Cup`, `Coppa Delle Alpi`, `Pyrenees Cup`, `UEFA Euro U19`-style twists -> MVP / player-of-the-week awards.
* Footballs-on-pedestal items have a soccer ball: remove the ball and replace with a football (`Golden Boy`, `Fifa women's world cup`, `Thai League 1`).
The cleanest route for PIX awards: **build originals** (modelling / Tripo) and use these only as scale / material reference (gold, marble, wood bases, the 2K metal + marble + wood PBR sets in `Textures.zip` / `textures_v02.zip` are generic and free to use: `Gold_*`, `brass_*`, `clean chrome`, `marble10`, `Wood*` - note: `textures_v02.zip` also has an 8K HDR sky, a 16K football-lines texture, the AI poster art and national flags; the posters are NFL-lookalikes, ignore them).

## 8. Recommended integration ORDER (with effort estimates)

| # | Step | Why first | Effort |
|---|---|---|---|
| 1 | **Shoulder pads -> player gear** (scale to S/M/L, skin, LODs, team-mask materials) | Biggest silhouette win ("bulky padded 2K8" target) for the lowest risk and cost; a direct visual upgrade on all 22 players | 2-3 days |
| 2 | **4 new bodies -> `phr_build` sources** (mesh-name map, 4 x `phr_body` S/M/L, new weights check) | Roster diversity: 15 body shapes instead of 3; reuses the existing 52-bone rig, 24 clips, LOD / test suite | 2-3 days |
| 3 | **Biped clips (Kick, Clinch, Helmet take-on, sprint cycles) -> Humanoid in Unity** | Extra animation quality: second kick style, tackle clinch, pre / post-game cutaway; the avatar of `Throw.FBX` retargets onto all PHR bodies. Needs the MachoWalk fix (3ds Max export) | 1-1.5 days |
| 4 | **Presentation sets**: Draft House stage (1 d) + news studio (1.5 d) + jumbotron (1 d) + tunnel (1 d) | Self-contained, no gameplay coupling, high perceived polish; do after step 3 so the release merge is calm | 4.5 days total |
| 5 | **Hero trophy + awards**: American football trophy (0.5 d, remove bolt) + 3-4 original generic cups | Needed for PIX Bowl cutscene / trophy case; generic assets only | 1.5 days |
| 6 | **Helmet variant** (new 105 k-tri helmet -> LODs) and **referee uniform** (body + skin) | Detail upgrades, lower payoff | 1 d + 1.5 d |
| 7 | **Stadium kit-bash**: roof / light towers / ribbon boards / media boxes into the existing bowl (cherry-pick) | Real upgrade without the 63 M poly cost | 2 days (cherry-pick) |
| 8 | **Full new stadium (rebuilt bowl)** | Biggest effort, biggest risk; start only after 1-7 are shipped | 8-10 days |
| 9 | Cheerleader (once FBX obtained), male head (hero reactions), crowd pack (distant filler) | Optional | 1-2 d each |
Skip: uniform set, cleats, Every-Trophy replicas.

## 9. Graphics plan to reach very high quality (photoreal, All-Pro 2K8 style readability)

### 9.1 Roster: photoreal bodies + gear
* **Bodies**: 5 source bases (R1, Derwin, Ramsey, Love, Jefferson) x S/M/L reshape (`phr_body`) = up to 15 body shapes (tall QB/TE from Love, stocky S/LB from Derwin, lean CB/WR from Ramsey/Jefferson, bulk OL/DL from the L reshape). Heads from the Draft Fever head set + blend tweaks (never the real faces), 8 skin tones, hair shells.
* **Gear**: new shoulder pads (silhouette), existing helmet (variant: new helmet with modelled inside), jersey / pants from the R1 build with the PIX/Lit uniform mask, new referee kit on the Medium body.
* **Animation**: 24 retargeted clips on one Humanoid avatar for all 15 bodies + Biped extras.
* **Textures**: 2K per part by default, 4K only for hero LOD0 face / helmet; texture streaming on; BC7 + BC5 normals; no 8K in game (only as bake source).

### 9.2 Environment
* New stadium kit-bash (roof / ribbon boards / media boxes / tower lights) on the existing bowl, then the full rebuilt bowl if time allows. Seats instanced; crowd = existing Draft Fever crowd billboards + near-field low-poly fans in the first rows; jumbotron with live render-texture; tunnel entrance for walk-out cinematics.
* **Presentation sets** (Draft House stage, halftime/postgame studio) as separate additive scenes loaded only on the menu / break: they never share frame budget with gameplay.

### 9.3 Performance budgets (RTX 5090 Laptop, 24 GB VRAM, 1440p, HDRP deferred, DLSS/FSR "Quality" upscaling from ~ 1080p internal; target 90-120 FPS in play, >= 60 FPS floor in replays / cinematics)

| Budget (gameplay, per frame) | Target | Notes |
|---|---|---|
| Frame time | <= 9-10 ms at 100+ FPS (GPU), CPU main <= 5 ms | 11 ms = 90 FPS hard limit |
| Players on field (22 + 6 officials) | **<= 0.7 M tris** visible, avg ~ 20-25 k/player | LOD switch 11 / 26 / 60 m (61 k / 28 k / 12 k / 5 k). A user camera with 6 players within 11 m = ~ 0.36 M + 16 x 15 k ~ 0.6 M. |
| Players shadows | <= 0.5 M extra (shadow LODs 1 step coarser) | 3-4 cascades 2048, distance-limited |
| Stadium (visible after culling) | **<= 3-4 M tris, <= 1.5 k draw calls** | roof + facade + bowl in LOD / chunks, seats instanced, occlusion + GPU-resident drawer, no mesh > 100 k polys on screen at once |
| Crowd | <= 60-80 k fans: billboards / impostors beyond 25 m, <= 2 k fans of 400-1,000 tris near | existing `CrowdReaction` atlas + GPU instancing |
| Props / sideline | <= 0.3 M tris | benches, cameras, trucks, props (existing PIX_Prop_*) |
| Total scene | **<= 5-6 M tris, <= 3-4 k draw calls (SRP batcher), <= 300 materials** | |
| Textures / VRAM | <= 10-12 GB textures resident, streaming on; stadium atlases 2K, players 2K/4K hero | leave headroom for DLSS / RT |
| Lighting | one directional sun (shadow 4 cascades) + ~ 8 area / spot "tower lights" for night games with shadows only on 2; baked probes; no RT in gameplay (optional SSGI at "Quality"); volumetric fog low; contact shadows on players | RT / SSGI only in cinematics and replays |
| Post | TAA / DLSS, motion blur low, bloom, tonemap ACES, filmic grain off in play | |
| Presentation scenes | up to 2 M tris, RT GI / reflections allowed | no gameplay running underneath |
Verify with the existing autotest FPS log (`-pixAutotest`) after each step; budgets must hold at the stadium centre camera and the worst case (play with 10 players in a pile at LOD0).

### 9.4 Risks to track
1. **Likeness**: real faces + team marks remain in the source files; keep sources out of Git (they live in `D:` and the `Assets/_Licensed/` git-ignored folders). Add the new names to the banned-name test.
2. **Store licence**: R1's body is used already; the four extras are from the same seller and the same assumption (isolated in `_Licensed` so they can be dropped).
3. **Stadium**: Built-in Standard, soccer layout, 63 M polys: do not import wholesale; and 14 NFL-lookalike posters + 1 NFL-shield banner must be swapped.
4. **Cheerleader**: no usable format in the purchase.
5. **MachoWalk.FBX** fails in Blender 5.2's importer; Unity not tested.
6. **Trophy pack**: all replicas of real soccer trophies; do not ship.
7. **Uniform set**: 12.5 M tris, unusable as a game asset.
8. Missing: the news-studio / jumbotron texture sets are in other zip members (not extracted); the Draft House stage is untextured.

## 10. What was extracted / skipped (all on D:, source untouched)
Extracted: 4 player FBX + textures (zip inside zip), player-animated animations FBX + `.bip` + OBJ + textures, helmet FBX + textures, shoulder pads FBX + textures, uniform FBX + textures, cleats FBX, referee FBX + TGA, male head unitypackage (+ unpacked), stadium unitypackage (+ unpacked tar, GUID index), jumbotron FBX + Vray textures, tunnel OBJ + c4d + tex, stage FBX, news studio FBX, cheerleader textures + `.max` + QA renders, crowd pack, football trophy FBX + textures, Every Trophy FBX (copy), Seperate Packs (outer zip only: the 13 inner zips stay zipped; USA + Football Awards FBX unpacked to `trophy_packs\_x\`).
Not extracted (duplicates / unusable): all other formats (3dsmax, Blender, C4D, alembic, GLB, GLTF, OBJ for the stadium and its Textures zips; Max / Maya / C4D / Blender / LWO of gear), `.ZTL`, PSD rars (1.4 GB + 1 GB + 0.3 GB), Marmoset scene, UE `JumbotronPack.zip`, Max_textures, the 3dsmax rars of the cheerleader, `textures_v02.zip` / `Textures.zip` of the trophies (generic metal / marble / wood maps inside; extract on demand).
Tool notes: `tar.exe` (Windows bsdtar) handles zip + unitypackage; it cannot extract *solid* RAR archives (the referee `3ds` / `c4d` / `obj` rars failed, `mb` and `scanline_max` rars were non-solid and gave the TGA textures). No 7-Zip / WinRAR on the machine; Python is not installed (Blender's Python used instead).
