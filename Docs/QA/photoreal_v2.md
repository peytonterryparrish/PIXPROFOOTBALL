# PHOTOREAL ROSTER v2 track log (PH2-###)

Owner: PHOTOREAL ROSTER v2 (v0.7 STAGING: shoulder pads, 15 body shapes, Biped clips). Nothing here is live until `PhotoRosterV2.Promote()` is called by the lead after v0.6.0 ships.
Staging: `Assets/_Licensed/PhotorealPlayer/Staging_v07/` (git-ignored like the rest of the licensed art). Work files: `D:\PIXFootball\PhotorealWork\v2\`. Renders: `D:\PIXFootball\Renders\roster_v2\`.

## 0. What was built (staging)

| Piece | Where | Notes |
|---|---|---|
| Pads | `Tools/Blender/phr2_pads.py`, `phr2_pad_textures.py` | 47.4 k-tri store pads (14 meshes, no logos) merged into ONE skinned part `Pads` with 3 material slots (Shell / Trim / Metal), LOD0 8.5 k, LOD1 4.2 k, LOD2 1.9 k, LOD3 0.75 k. Fitted in the R1 frame like every other part (reshaped with the body), vest taper, shortened epaulets. |
| Jersey over the pads | `phr2_pads.conform_to_pads / extend_sleeves / drape_jersey` | the jersey sleeve is stretched 5 cm along the upper arm, every jersey vertex is pushed along its normal (rays through all pad layers, dilate + blur so rivets / plate edges do not ripple the cloth) until it clears the pad shell by 7 mm; skin weights of the pushed region blend to the pad weights. LOD1..3 get the LOD0 displacement + weights by inverse distance (one padded silhouette for every LOD). The old phr_body shoulder "bump" is switched off for these builds. |
| 4 extra bodies | `Tools/Blender/phr2_warp.py` | see section 2 |
| Build | `Tools/Blender/phr2_build.py <Size> <Base>`, `phr2_build_all.sh` | = `phr_build.build` (untouched, still builds the live v0.6.0 bodies) + pads + base warp + jersey drape, 26 - 60 s per body, FBX `PHR_<Size>_<Base>.fbx` (20.6 MB) |
| Unity staging | `Scripts/Editor/Assets/PhotoRosterV2.cs` | `BuildStaging`, `Promote`, `BuildLive`, `RegisterArtLibrary`; prefabs `Staging_v07/Prefabs/PIX_PhotoPlayer_<Size>_<Base>.prefab` (same layout as `PhotoPlayerBuilder` + `Pads` part + `PhotoPadTint`) |
| Biped clips | `Scripts/Editor/Assets/PhotoRosterV2Clips.cs` | see section 3 |
| QA | `PhotoRosterV2Qa.cs` (renders + numeric pose check), `Tests/EditMode/Art/PhotoRosterV2Tests.cs`, `Tools/compile_check.ps1` (compiles Art / Editor / test files with Unity's own Roslyn in 10 s, no editor needed) |
| Runtime (additive only) | `PlayerAppearance.cs` (+`BodyBase`, `Base`, `ChooseBody`, `PrefabKey`, `FromSeed(..., BodyBase)`), `PhotoPadTint.cs` (new) | `PhotoPlayer` / `PhotoPlayerLibrary` / live prefabs untouched |

Body base names are neutral on purpose (nothing named after a person ships): **Std** (R1, the existing S / M / L), **Stocky** (S / LB), **Athletic** (CB / S), **Tall** (QB / TE), **Runner** (WR / RB).

## 1. Shoulder pads

Source `D:\PIXFootball\Source\PPF_NEW\shoulder_pads` (47,400 tris, 14 meshes, 4 materials, 4K maps, no logo or text anywhere in the maps: checked by eye on every map, `Renders/roster_v2/_work/pads_tex_sheet.png`).

* **Fit** (`phr2_pads.make_pads`): z +0.97 -> +1.05 (pads are modelled at the pelvis), uniform fit x 1.03 / y 0.86 / z 0.88, **vest taper** (0.78 x / 0.80 y at the lower ribs, 1.0 over the shoulders, a full-width pad gave a barrel chest: chest circumference +32 %, now +10 %), epaulets scaled to 45 % about their shoulder anchor (the store epaulets reach past the elbow) and 85 % in depth. The pads are fitted on the R1 frame and then reshaped like the jersey (`phr_body.reshape` with the pad's own skin weights), so S / M / L scale them with the torso and arms; the base warp moves them with the other parts.
* **Skinning** (`weight_pads`): chest / back plates on Spine2 (Spine1 below the ribs), clavicle weight on the outer shoulder, epaulets follow the upper arm up to 62 % at the tip, <= 4 influences, smooth gradients (no candy wrap). Verified in motion: `pads_motion.png` (catch high, throw, stiff arm, swim, engage, TD, tackle wrap, dive on S / M / L).
* **Jersey over the pads** (`extend_sleeves`, `conform_to_pads`, `drape_jersey`): the R1 jersey sleeve is only 6.5 cm long, so it is stretched 7 cm along the upper arm; every jersey vertex is pushed along its normal until it clears the pad envelope (plastic + metal + epaulet trim) by 7 mm (rays from 4 cm under the cloth through all pad layers), the push is dilated 2 rings and blurred 14 times so rivets / plate edges do not ripple the cloth, faded out towards the hem (pads end at the ribs) and for downward-facing cloth (armpit spikes), and the skin weights of the pushed region blend to the pad weights. The old phr_body shoulder "bump" is off in these builds. Jersey LOD1..3 receive the LOD0 displacement and weights by inverse distance (one padded silhouette for every LOD). `tuck_pads` finally moves every pad vertex that would still show (epaulet rims under the sleeve end, neck roll) 8 mm under the jersey surface.
* **Parts / LODs**: one skinned `Pads` part with 3 slots (Shell = PIX/Lit uniform mode, tinted by `PhotoPadTint` with the kit's secondary colour; Trim = strap + fabric atlas, neutral; Metal = rivets / buckles), tris 8,500 / 4,250 / 1,869 / 758 (collapse decimation with material borders protected, no UV-seam protection: the pads are hidden under the jersey, the neck roll is the only thing that can show). Textures 1K (shell gray + normal) and a 2048 x 1024 trim atlas (`Textures/Pads`). The pads add 8.5 k / 4.2 k / 1.9 k / 0.76 k triangles per player (LOD0 total about 69 k drawn).

## 2. Four extra bodies -> 15 body shapes

**Method (differs from the intake suggestion on purpose).** The intake proposed parameterising `phr_build` by a source path + mesh-name map. The purchased bodies turned out to share R1's base topology *and* UVs exactly (`phr2_cmp.py`: for arms, jersey, pants, socks, shorts, cleats, helmet, facemask the UV coordinate sets are identical, 4,416 of 4,417 facemask coordinates etc.), but not its vertex order (quads vs triangles, different loop splits) and the name sets differ per body. So instead of re-importing each body as a new source (new names, new UV checks, four re-authored texture sets, four chances to leak a mark), `phr2_warp.py` **measures a smooth displacement field R1 -> body** from the meshes both have (matched per vertex through the identical UV coordinates; 24 - 31 k samples per body, outliers > 2 cm from the neighbour median dropped: none were found) plus the new rest skeleton (52 `rig:*` bone heads / tails / rolls), caches it (`PhotorealWork/v2/field_<Base>.npz`) and applies it as the LAST step of the normal pipeline to every part, every LOD and the pads (head, hair shells, eyes, helmet parts move rigidly by the skinned bone deltas). Consequences: every texture / UV / weight / jersey layout / LOD rule / test of the live pipeline is reused unchanged (nothing is re-authored), the existing S / M / L reshape composes with the base (15 = 5 x 3), and **only vertex positions and bone positions of the purchased bodies are read**: no head, face, hair, beard, eye, texture, material, name or number of the four players or their teams is ever opened, so the likeness / branding strip of the intake section 1.3 is satisfied by construction (the banned-name test additionally scans every shipped file / node / material / mesh name).
* **Derwin's smeared arm map**: never used. Arms / skin textures are R1's own arm map re-tinted to 8 tones (`phr_textures2.py`) on R1's UV, which the new arms share, so the smear cannot appear (checked: all arm UV coordinates are identical, `phr2_warp` log "arms matched 12698 / 12698").
* Unmatched parts (cleats of two bodies, towel, wristband, pouch for some) follow the field of the nearest matched cloth (inverse distance, 10 neighbours); the head follows the Spine2 / Neck / Head bone deltas (so the Draft Fever head stays in the helmet for the 2 m tall body: Head bone +0.07 m).
* Neutral base names: `Std` (R1: the existing S / M / L), `Stocky` (safety / linebacker: shoulders +7 %, thigh +7 %, calf +9 %), `Athletic` (corner / safety), `Tall` (QB / TE, +7.1 cm, hips 1.131 m), `Runner` (WR / RB: wide hips, strong calves).
* **Honest note**: `Athletic` is almost R1 (all arm / jersey vertices within 5 mm, legs within 7 - 14 mm, hips 1.1 cm lower). It is a real extra base but visually 90 % `Std`; `Runner` differs from `Std` by less than 1.1 cm (cloth) with a wider hip / calf. `Stocky` and `Tall` are clearly different, the S / M / L reshape (+40 % thigh) remains the dominant variety.
* `PlayerAppearance` (additive): `BodyBase` enum, `Base` field (Std = 0, so old appearances / saves / `Key()` values are unchanged), `FromSeed(seed, body, role, base)` (the same seed gives the same person for every base), `ChooseBody(position, heightCm, weightKg, out size, out base)` (QB / TE >= 190 / 195 cm = Tall, WR / RB = Runner, CB = Athletic, LB and safeties >= 96 kg = Stocky, OL / DL = Large Std) and `PrefabKey(size, base)` (`photo_player_small` for Std, `photo_player_medium_tall` ...). `PhotoRosterV2Qa.PositionRoster` renders 24 positions through it (`roster_positions.png` + `.txt`).

## 3. Biped clips (`american-football-player-animated`)

Only the animation of the purchased files is used (the seller's body, the scanned head and its OUTSOURCE2 / O2U marks never reach the project: the FBX copies are imported into `Staging_v07/Biped`, the clips extracted, the copies DELETED again by `PhotoRosterV2Clips.ImportAndClean`).
* Unity imported all of them as **valid Humanoid** with the automap (76-bone Biped: pelvis / 3 spine bones / clavicles / 5-finger hands), **including MachoWalk.FBX that Blender 5.2 cannot import** (it only needed Unity's FBX importer). Clinch (two skeletons in one file, 152 bones) is imported twice: Bip001 by the automap, Bip002 by an explicit 52-bone map (`ApplyBipedMap`), giving clinch A and clinch B.
* Cleaning = the ANIMATION track's own `ClipCleaner` (muscle space: despike, Gaussian filter, human-limit clamp, loop seam closing, ground fix, facing to +Z), plans per clip in `PhotoRosterV2Clips.Specs`. Output `.anim` assets in `Staging_v07/Clips` (promoted to `PhotorealPlayer/Clips`), manifest `clips.json`.
* New ArtLibrary keys (**add only**: no existing key is touched, `PhotoRosterV2.RegisterArtLibrary` adds them with source `biped_v07`; verified that none of them exists in `ArtKeys` / `ArtLibraryReport`):

| key | file | length | loop | notes (measured on the Humanoid rig, 60 Hz sampling) |
|---|---|---|---|---|
| `kick_off_biped` (+ `_m` mirrored, for left-footed kickers, ANI-017) | Kick | 2.67 s | no | 2.5 m of hip travel (2 - 3 approach steps), the kicking foot peaks at 14.0 m/s at t = 1.27 s (= the foot contact), start / end pose 0.14 m apart |
| `qb_throw_biped` (+ `_m`) | Throw | 1.67 s | no | footwork + throw, 1.1 m hip travel |
| `sprint_biped` | sprint | 0.53 s | yes | in place, seamless (start-end pose distance 0.000 m), one gait cycle |
| `bc_sprint_biped` | sprintball | 0.53 s | yes | ball carried right arm, in place, seamless |
| `helmet_off` | Helmet_TakeOn | 3.90 s | no | in place (0.03 m), pre / post game cutaway |
| `clinch_a`, `clinch_b` | Clinch | 2.67 s each | no | paired takes in one shared frame (no trim, no re-base, no ground fix: `KeepRootFrame`), twin names `_a` / `_b` |
| `walk_macho` | MachoWalk | 1.33 s | yes | in place strut cycle, seamless; NOT a neutral walk (the library's `walk` stays the default) |

Foot contact: loops are grounded (lowest foot bone y = 0.06 - 0.12 m = ankle height of the Draft Fever Medium rig; the cleaner lowered kick 6 cm, sprint 3 - 4 cm); on the 15 photoreal bodies the feet read on the ground in all sheets (`biped_clips_{small,medium,large}.png`: 8 clips x 6 time steps each). Root motion stays in the clips (kick 2.5 m, throw 1.1 m) like the other cleaned takes (ANI-037: the simulation owns the position).

## 4. Issue log

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| PH2-001 | fixed | S1 | legal | 4 purchased bodies | heads / faces / hair / beards / eyes / glasses / visor decals, jersey + pants + socks + helmet + glove + cleat maps (embossed Nike / shield in the normals), names and numbers, team schemes (Chargers / Steelers / Packers / Vikings) are all in the source files | none of it is read: only vertex positions of cloth / skin / gear meshes and bone positions go through the field (`phr2_warp.py`); the Draft Fever heads and the PIX uniform maps of the live pipeline are used for every body; the biped FBX copies are deleted after the clips are extracted | `PhotoRosterV2Tests.NothingOfTheRealPlayersOrTeamsShipsInTheRosterV2Assets` (tokens james / ramsey / love / jefferson / derwin / jalen / jordan / justin / chargers / steelers / packers / vikings / lightning / bolt / nfl / nike / riddell / adidas ... in every file, node, material and mesh name), `roster15_faces.png` |
| PH2-002 | fixed | S2 | texture | Derwin arm map | the purchased arm texture has a UV smear | not used; R1's arm map + R1 UV (identical on the new arms) re-tinted | `phr2_warp` log: arms matched 12698 / 12698 through identical UV coordinates; `roster15_faces.png` arms in `roster15_sheet.png` |
| PH2-003 | fixed | S1 | pads | first pad drape | normal-based push of a 1.9 k-vertex jersey over a 47 k-tri pad rippled and spiked (armpit, flanks) | dilate + blur of the push, hem fade, downward-normal fade, envelope without the belt wings | `pads_before_after.png`, `fbx_*` Blender renders |
| PH2-004 | fixed | S1 | pads | first Unity render | the pads showed through the jersey at the epaulets (Blender renders had the pads hidden, so the first sheets looked clean) | epaulet trim in the envelope, shorter epaulets (45 %), longer sleeve (+7 cm), `tuck_pads`, poke-through counter `poke_report` | `pads_before_after.png`, `roster15_sheet.png`, `phr2_poke_test.py` |
| PH2-005 | fixed | S2 | LOD | jersey LOD1..3 | an independent conform of the decimated jersey changed the shoulder hull by up to 12 % | LOD0 displacement + weights transferred to the LODs (`drape_jersey`) | `body_measurements.md`, `pads_lod_silhouette.png` (whole-body front silhouette IoU vs LOD0: LOD1 0.993, LOD2 0.983, LOD3 0.973) |
| PH2-006 | fixed | S2 | proportions | first pad fit | a full-width pad made a barrel chest (+32 % circumference) | vest taper, smaller depth | chest circumference +10 % / +9 % / +11 % (S / M / L) in `body_measurements.md` |
| PH2-007 | fixed | S1 | tooling | `PhotoRosterV2Clips` | every cleaned clip was named after the FBX take ("Take 001"), so the eight clips overwrote each other | the raw clip is renamed to the key before `ClipCleaner` and the folder is emptied first | `biped_clips_report.txt`, `PhotoRosterV2Tests.BipedClips...` |
| PH2-008 | fixed | S3 | tooling | compile | `Object.GetInstanceID()` is an error in this Unity (obsolete as error) | `GetHashCode()` | `Tools/compile_check.ps1` |
| PH2-009 | open | S3 | bodies | `Athletic` base | almost identical to `Std` (<= 1.4 cm); a real but weak extra shape | pick a stronger source or push the field by 1.5x for this base | `body_measurements.md` |
| PH2-010 | open | S2 | integration | `PhotoPlayerBuilder.BuildAll` | after Promote the old builder would rebuild the live prefabs without the Pads part (all four pad LODs would stay active) | use `PhotoRosterV2.BuildLive()`; the lead should fold `Pads` + `PhotoPadTint` into `PhotoPlayerBuilder.PartKeys` when v0.7 starts | code |
| PH2-011 | open | S3 | pads | tint | only the shell is team-tinted (kit secondary), trim and metal are neutral; pads are hidden except for the collar | acceptable for the silhouette goal; `PhotoPlayer` could own the pad tint instead of `PhotoPadTint` | `pads_before_after.png` |
| PH2-012 | open | S3 | LOD | jersey LOD1..3 | hull circumference at shoulder height still differs by 3 - 8.5 % (silhouette overall 1 - 3 %): decimated cap corners | more triangles in the caps at LOD1 / 2 | `body_measurements.md` |
| PH2-013 | open | S2 | gameplay | Promote | `Promote()` was written but not run (the lead calls it after v0.6.0 ships); every step it contains (`BuildPrefab`, `ConfigureModels`, `RegisterArtLibrary`, folder moves) is the code that built the staging prefabs, except the moves | run once on a branch and check `PhotoPlayerTests` + `PhotoRosterV2Tests` | - |

## 5. Measurements (Blender, rest pose; `Tools/Blender/phr2_measure.py`, full table `D:\PIXFootball\Renders\roster_v2\body_measurements.md`)

Shoulder width = x extent of the jersey (the epaulets are the widest line); circumferences are convex-hull perimeters of the jersey section at the shoulder joint -0.02 / -0.17 / -0.36 m (they include the sleeves where the section crosses them).

| body | shoulder width m | depth m | shoulder circ | chest circ | waist circ | hips circ | thigh circ | height m | hip z | LOD1-3 worst circ error |
|---|---|---|---|---|---|---|---|---|---|---|
| BEFORE Small (live v0.6.0) | 0.579 | 0.305 | 1.35 | 1.05 | 0.95 | 1.03 | 0.65 | 1.892 | 1.081 | 1.9 % (Jersey LOD3 z1.33) |
| BEFORE Medium (live v0.6.0) | 0.592 | 0.343 | 1.41 | 1.20 | 1.02 | 1.11 | 0.74 | 1.892 | 1.081 | 1.7 % (Jersey LOD3 z1.33) |
| BEFORE Large (live v0.6.0) | 0.612 | 0.391 | 1.50 | 1.39 | 1.25 | 1.31 | 0.90 | 1.892 | 1.081 | 1.4 % (Jersey LOD3 z1.33) |
| AFTER Small Std | 0.741 | 0.346 | 1.76 | 1.33 | 0.95 | 1.03 | 0.65 | 1.892 | 1.081 | 2.7 % (Jersey LOD3 z1.21) |
| &nbsp;&nbsp;delta vs before | +0.163 (+28 %) | +0.040 | +0.41 | +0.28 | +0.01 | | | | | pad tris {0: 8500, 1: 4250, 2: 1867, 3: 754} |
| AFTER Medium Std | 0.780 | 0.383 | 1.82 | 1.57 | 1.03 | 1.11 | 0.74 | 1.892 | 1.081 | 3.0 % (Jersey LOD3 z1.53) |
| &nbsp;&nbsp;delta vs before | +0.188 (+32 %) | +0.040 | +0.41 | +0.36 | +0.01 | | | | | pad tris {0: 8500, 1: 4249, 2: 1868, 3: 754} |
| AFTER Large Std | 0.855 | 0.428 | 2.04 | 1.73 | 1.26 | 1.31 | 0.90 | 1.892 | 1.081 | 6.1 % (Jersey LOD1 z1.33) |
| &nbsp;&nbsp;delta vs before | +0.242 (+40 %) | +0.037 | +0.54 | +0.34 | +0.01 | | | | | pad tris {0: 8500, 1: 4250, 2: 1868, 3: 749} |
| AFTER Small Stocky | 0.795 | 0.356 | 1.91 | 1.38 | 0.99 | 1.08 | 0.68 | 1.913 | 1.062 | 4.4 % (Jersey LOD3 z1.54) |
| AFTER Medium Stocky | 0.822 | 0.394 | 2.02 | 1.63 | 1.08 | 1.16 | 0.77 | 1.913 | 1.062 | 5.9 % (Jersey LOD3 z1.54) |
| AFTER Large Stocky | 0.892 | 0.439 | 2.15 | 1.80 | 1.31 | 1.36 | 0.93 | 1.913 | 1.062 | 5.0 % (Jersey LOD2 z1.54) |
| AFTER Small Athletic | 0.764 | 0.346 | 1.84 | 1.31 | 0.95 | 1.03 | 0.64 | 1.883 | 1.069 | 3.7 % (Jersey LOD3 z1.52) |
| AFTER Medium Athletic | 0.794 | 0.383 | 1.86 | 1.53 | 1.03 | 1.11 | 0.73 | 1.883 | 1.069 | 3.1 % (Jersey LOD3 z1.52) |
| AFTER Large Athletic | 0.856 | 0.428 | 2.05 | 1.74 | 1.26 | 1.31 | 0.89 | 1.883 | 1.069 | 4.1 % (Jersey LOD2 z1.52) |
| AFTER Small Tall | 0.786 | 0.354 | 1.90 | 1.34 | 0.99 | 1.08 | 0.68 | 1.963 | 1.131 | 4.0 % (Jersey LOD3 z1.60) |
| AFTER Medium Tall | 0.813 | 0.391 | 1.93 | 1.57 | 1.08 | 1.15 | 0.78 | 1.963 | 1.131 | 3.0 % (Jersey LOD3 z1.60) |
| AFTER Large Tall | 0.869 | 0.437 | 2.10 | 1.82 | 1.31 | 1.36 | 0.95 | 1.963 | 1.131 | 6.5 % (Jersey LOD2 z1.40) |
| AFTER Small Runner | 0.763 | 0.349 | 1.85 | 1.38 | 0.97 | 1.08 | 0.68 | 1.895 | 1.075 | 3.9 % (Jersey LOD3 z1.53) |
| AFTER Medium Runner | 0.793 | 0.386 | 1.89 | 1.59 | 1.04 | 1.16 | 0.77 | 1.895 | 1.075 | 3.3 % (Jersey LOD3 z1.53) |
| AFTER Large Runner | 0.858 | 0.432 | 2.08 | 1.74 | 1.27 | 1.36 | 0.94 | 1.895 | 1.075 | 6.2 % (Jersey LOD1 z1.33) |

Pad fit, before -> after (Std base, same body size): **shoulder width 0.579 -> 0.741 m (+28 %) Small, 0.592 -> 0.780 m (+32 %) Medium, 0.612 -> 0.855 m (+40 %) Large**; chest (jersey) circumference +0.28 / +0.36 / +0.34 m; waist and hips unchanged (+0.01 m), body depth +4 cm; front silhouette area +6 %, side +3 - 4 % (`pads_silhouette_front.png`, `pads_silhouette_side.png`: red = before only, blue = after only, white = both). The "broad, squared shoulders" read is at the shoulder line only; the torso keeps its waist taper.
LOD silhouettes (Medium Std, whole body front view, `pads_lod_silhouette.png`): IoU vs LOD0 LOD1 0.992, LOD2 0.984, LOD3 0.974 (area +0.06 / +0.28 / +0.64 %). The target "within 2 %" holds for LOD1 and LOD2 as a whole-body silhouette; at shoulder height the jersey hull circumference of LOD1..3 differs by 3.0 - 6.5 % and the jersey bounds width by up to 5.3 % (LOD2 Large) (PH2-012) because decimation cuts the epaulet corners.
Complexity: Unity default-drawn triangles per player (LOD0 / 1 / 2 / 3): about 69 k / 32 k / 13.7 k / 5.9 k including pads (`PhotoRosterV2Tests.FifteenHumanoidBodiesWithPadsAndFourLodsEach` budget 90 k / 42 k / 19 k / 10 k).

## 6. Verification in Unity (`PhotoRosterV2Qa`, renders in `D:\PIXFootball\Renders\roster_v2\`)

* **Every body plays the clips**: 15 bodies x 24 representative clips (idle, 2-point / 3-point / DL / OL / shotgun stances, run, sprint, backpedal, hard cut, stiff arm, tackle wrap, being tackled, OL pass set / engage, DL swim, QB throw / drop, catches high / low / dive, TD celebration, fall, hurdle) sampled with `AnimationMode.SampleAnimationClip` at 45 % (NOT `PlayableGraph`, PHR-015): 360 poses, 0 failures (`pose_check.txt`): leg / arm stretch ratios agree with the Std body of the same size within 0.10, no folded leg, baked jersey / pads / pants / cleats / head / helmet bounds contain their bones. Visual: `clips_{std,stocky,athletic,tall,runner}.png` (12 clips x S / M / L each), `pads_motion.png` (shoulder stress poses).
* **Mesh follows the skeleton at all LODs**: the skinning of the pads and the re-weighted jersey is checked per prefab (`bones.Length == bindposes.Length`, >= 4 bones on the pads, `PhotoRosterV2Tests`); the LOD checks of `PhotoPlayerTests` (helmet shell / mask / chin strap under the head bone, every SkinnedMeshRenderer bound inside the rig) run on the live prefabs and, after Promote, on all 15.
* **Roster in PIX uniforms** (fictional kits, helmets on / off): `roster15_sheet.png` (rows Small / Medium / Large, columns Std / Stocky / Athletic / Tall / Runner), `roster15_faces.png` (helmet off: only the Draft Fever generic heads, no trace of the four players, no logos / marks; the collar is the jersey's own rib), `roster_positions.png` (24 positions through `ChooseBody`).
* **Biped clips**: `biped_clips_{small,medium,large}.png`, `biped_clips_report.txt`.

## 7. Open items / handover to the lead

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| PH2-014 | open | S2 | pads / jersey | sleeve opening | at close range the stretched jersey sleeve opening is a little jagged / torn where it wraps the epaulet (visible at 2 - 4 m in `roster15_sheet.png`, e.g. the shoulders of several cells); the sleeve stripes are stretched 1.9x along the arm in the outer 4 cm | smooth the sleeve-end ring after the drape (Laplacian on the open boundary) and add a loop of faces to the sleeve in the R1 jersey (changes UV / layout, needs `phr_layout.py`) | `roster15_sheet.png` |
| PH2-015 | open | S3 | integration | `Promote()` | `PhotoRosterV2.Promote()` is NOT called. It moves pad textures / materials, copies the 15 FBX over / next to the live ones, rebuilds the 15 prefabs at the live paths (Std keeps the live prefab GUIDs, so the three existing ArtLibrary keys keep working), moves the Biped clips to `PhotorealPlayer/Clips`, registers `photo_player_{size}_{base}` (12 new) and 8 + 2 clip keys, deletes the staging meshes / prefabs | run it once after v0.6.0 ships, then `PhotoPlayerTests` + `PhotoRosterV2Tests`; roll back by `git checkout`-ing nothing (all art is git-ignored): keep a copy of `Assets/_Licensed/PhotorealPlayer` first | - |
| PH2-016 | open | S2 | gameplay | spawning | nothing spawns the new bases yet (like PHR-022): `PlayerAppearance.ChooseBody(position, height, weight)` + `PrefabKey(size, base)` + `ArtLibrary.Load().Prefab(key)` is the hook; the LeagueDB would store `Base` with the seed; `PlayerAvatar.Build` must skip its material pass for these prefabs as for the Std ones | lead | - |
| PH2-017 | open | S3 | animation | new clips | the ANIMATION track has to wire `kick_off_biped(_m)`, `qb_throw_biped(_m)`, `sprint_biped`, `bc_sprint_biped`, `helmet_off`, `clinch_a/_b`, `walk_macho` into `AnimKit` / `ClipFacingTable` / `ClipTimingTable` (they are plain library keys now; kick contact measured at t = 1.27 s in `biped_clips_report.txt`) | ANI | `biped_clips_report.txt` |
| PH2-018 | open | S3 | tooling | Unity | other tracks' compile errors (e.g. `MatchHud.Glass.cs(306,140)` for a few minutes) made batch launches exit 1: the QA chain retries; `Tools/compile_check.ps1` checks my files without a Unity launch | - | `Logs/ph2_stage2.log` |

How to rebuild from scratch:
```
blender -b --python Tools/Blender/phr2_warp.py -- build all          # fields for the 4 bases (cached in PhotorealWork/v2)
blender -b --python Tools/Blender/phr2_pad_textures.py               # pad textures (Staging_v07/Textures/Pads)
Tools/Blender/phr2_build_all.sh                                      # 15 FBX in Staging_v07/Meshes (about 25 min)
Tools/unity_wait_run.ps1 -Method PixFootball.EditorTools.PhotoRosterV2.Stage1Batch -Log ph2_stage1        # prefabs + Biped clips
Tools/unity_wait_run.ps1 -Method PixFootball.EditorTools.PhotoRosterV2Qa.RunAll -Log ph2_qa              # renders + pose check (no -nographics)
blender -b --python Tools/Blender/phr2_measure.py -- D:/PIXFootball/Renders/roster_v2/body_measurements.md
blender -b --python Tools/Blender/phr2_silhouette.py
```

Full EditMode suite after this work (`Logs/ph2_tests.xml`): 810 tests, 810 passed, 0 failed (7 new: `PhotoRosterV2Tests`; the whole existing suite incl. `PhotoPlayerTests` and `PlayerAppearanceTests` stays green with the additive `PlayerAppearance` changes).
