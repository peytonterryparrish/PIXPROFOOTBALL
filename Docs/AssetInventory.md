# PIX Football - Draft Fever asset inventory

Owner-supplied art from "Draft Fever Bowl" (user owns it). Source: `_AssetSource/DraftFever/` (189 GB, git-ignored).
Imported set: `Assets/_Licensed/DraftFever/` (git-ignored; ~18 GB in the project, ~3.7 GB of it animation FBX).
Everything is accessed from code through `PixFootball.Art.ArtLibrary` (see `Docs/ArtLibraryReport.md` for keys found / missing).
Re-create the import with `PIX Football/Assets/...` (`DraftFeverImporter`, stage 1 = extraction/copy, stage 2 = `PostImport`).

Legend - HDRP-ready: **yes** = renders correctly in HDRP 17.6 as imported / after our material pass; **partial** = needs a package we do not have; **n/a** = not a render asset.

## 1. Import method notes

| Topic | What was done |
|---|---|
| Unity packages | Not imported with `AssetDatabase.ImportPackage`. `UnityPackageUnpacker` (pure System.IO) unpacks the `.unitypackage` tar.gz, keeps GUID + `.meta`, **skips every `.cs/.asmdef/.dll`** (old Draft Fever code would collide with ours), de-duplicates by GUID across packages and writes to `Assets/_Licensed/DraftFever/Packages/<Name>/...`. No missing-script prefabs result because the unpacker also drops `.unity` scenes; the stadium swap prefabs only held old scripts (none kept). |
| `.rar/.zip` | Extracted with `C:\Windows\System32\tar.exe` into sibling folders `<archive>__x/` (see section 11 for the list and skipped archives). |
| Raw FBX/PNG/WAV/TTF | Copied with robocopy (excludes `.spp .blend .psd .zpr .ztl .mb .ma .als .aep .mp4 .abc .sbs/.sbsar` and archives). Textures stay next to their meshes. |
| Texture import | `LicensedImportRules`: sprites for `UI/`, normals as NormalMap, mask/metal/rough/AO linear, 4096 max for hero (bodies, heads, helmets, field, stadium packages, atlases) and 2048 otherwise; package textures only size-capped, their own `.meta` types are kept. |
| Audio import | SFX: DecompressOnLoad + ADPCM. Ambience: CompressedInMemory Vorbis q0.5. Music: Streaming Vorbis q0.6. |
| Animation import | `AnimationImportSettings` + `HumanoidMapper` (section 5). |
| Blender conversions | The BoNES retarget `.blend` sources and the referee `.blend` were converted to FBX with Blender 5.2 (scripts kept in the session scratchpad; outputs in `_AssetSource/DraftFever/_clean_retarget__x/`). |

## 2. Player bodies, heads, hair, helmets, gear

| Asset | Path (under `Assets/_Licensed/DraftFever/`) | Format | Size | HDRP-ready | Notes |
|---|---|---|---|---|---|
| Player body S / M / L (2025 versions) | `Characters/Bodies/{Small,Medium,Large}Player.fbx` | FBX skinned, ARP rig, 85-87 transforms | 3.5-4 MB each | yes (after our materials) | Humanoid avatars `SmallPlayerAvatar/MediumPlayerAvatar/LargePlayerAvatar` (valid). Meshes: Torso, Legs, Arms, Hands, Shoes, Head_BlendShape_*, Eyes_BlendShape_*. Hip height 0.907 m on all three. The Medium rig uses underscore names (`hip`, `shoulder_l`), Small/Large use `root.x`, `shoulder.l`. Older 2024 zips (`Assets/Characters/Players/*.zip`, ~30-50 MB each) and `All sizes/Old Files` skipped: superseded. |
| HDRP main player setup (materials + shader graphs) | `Packages/MainPlayer/` | package | 391 MB pkg | yes | Shader graphs `Mid_Size_Player_Uniform_HDRP` (3-colour mask jersey: `_Primary_Color`, `_Secondary_Color`, `_Terciary_Color`, `_Color_Mask`, `_Dirt_Mask`), `Arm_Skin`, `Eyes_Shader`, SSS diffusion profiles `SubSurface_Head/Arms.asset`. |
| Skin / jersey material packages | `Packages/PlayerMaterials/` | package | 270 + 315 MB pkgs | yes | `Skin_Shader`, `Uniform_Shader`, head textures + sweat masks. |
| Skin textures | `Characters/SkinTextures/T_Arms_Skin_01..15.png` | PNG 4K | 117 MB | yes | 15 skin tones for arms. |
| Main player delivery | `Characters/MainPlayerDelivery/` | FBX/OBJ/PNG | 124 MB | yes | blend-shape head `Head_Blend_Shapes_SubD03_Updated.fbx`, eyes, jersey normal/colour mask. |
| Heads | `Characters/Heads/` (`Head001/002_MESH.obj`, `Rodrigo/Head_11..13_SubD01/03.obj` + 50 MB tif maps, `Victor_LODS/MaleCrowd_001_LOD.fbx`) and `Characters/FacialRig/*player_facialrig.fbx` | OBJ/FBX | 333 + 308 MB | yes | Facial rig FBX per size has the blend-shape face. |
| Hair (hair cards, newest system 30-06-25) | `Packages/HairSystem/` | package (1.5 GB pkg; kept 755 MB) | | yes (HDRP/Hair mats) | **Strand hair `.asset/.abc` were dropped**: they are Unity Hair package assets (not installed; binary 2022.3 serialisation). Cards (`*.fbx` + `HDRP/Hair` mats + textures) kept: Backdread, CurlyMidHair, Curly002, Dread_Wicks, SkinnyDread, Jason_StraightHair, StraightHair_Varg, clumpydread_chadjones. Older hair packages (`Archive/*` 4 files, 12-06-25/16-06-25 identical size) skipped; `Hair_Package_Updated`, `HairAssets.unitypackage` (1.9 GB, named real-person grooms) skipped. |
| Helmets (newest "New Helmets") | `Helmets/New/{Small,Medium,Large}/` + `Visors/`, `Face Masks/` (FM_01..05, visor_01..03) | FBX LOD0/1/2 + PNG 4K | 618 MB (incl. `Updated/`) | yes with `New_Helmet_MAT` / `Helmet_Shader` | Medium helmets are centred on their own pivot (head centre); Small/Large are authored in body space. `Helmets/Updated/helmet_LODs.fbx` + `Helmet_Shader.shadergraph` (older pattern WIP in `patternsWIP/`). Helmet shader package: `Packages/HelmetShader/`. Old `Props/Leonardo's Helmet`, `Leo's/Helmet` zips (2.4 GB, 2024) skipped. |
| Gloves, sleeves, tape, pads, braces, socks, wristbands, mouthguards | `Characters/Customization/` (57 FBX, left/`_R` pairs) and `Characters/Accessories/` (44 FBX, 8k normals capped to 2048) | FBX + PNG | 1.5 GB + 5.0 GB | yes | `PIX_Acc_*` prefabs get HDRP/Lit mats from the folder textures. Shoes: `Accessories/Shoes/Shoes_2/Export/Shoes_1_low_LOD0.fbx` (textures extracted from `Maps.rar`). 628 MB `2_Variation_high.fbx` glove deleted (unusable). QB/linemen/receiver glove variants incl. `QbGloves.zip`, `Sleeves.zip` extracted (`__x`). |
| Vintage kit | `Characters/Accessories/Vintage Kit/` | FBX/PNG | | yes | retro jersey + gear. |
| Legendary skins | not imported: `Futuristic Outfit` pkg (90 MB), Spartan, Vintage (Maya) | | | | skipped (cosmetic extras, out of v0.1-0.7 scope). |
| Coaches | `Characters/Coaches/Rigged/PIX_Coach_{A,B,C}.fbx` (+ `Accessories/` watches, chains, earrings) | FBX skinned, Humanoid | 3 x ~3 MB | yes | **Assembled and rigged** from the modular coach kit (`Coach_Final.blend`) by `Tools/Blender/build_coach.py`: A = polo + cap + headset + clipboard, B = bomber jacket + visor + headset + goatee + tablet, C = puffer + cap + headset + beard + glasses. Skinned to the clean player deform skeleton (weights transferred from the player body, rigid head gear). The kit ships **no clothing/head textures** (the .blend points at the vendor machine), so tops/pants/shoes are flat HDRP/Lit colours (top + cap tintable through `PixFootball.Art.TeamTint`), heads use the player head atlas (+ a lightened copy), arms the `T_Arms_Skin_*` set, hair the `T_HairCards` atlas. Prefabs `PIX_Coach_A/B/C`. |
| Referee | `Characters/Referee/Rigged/Referee_Rigged.fbx` (+ `Referee_Gestures.fbx`) and the old static `Referee_LOD0.fbx` | FBX skinned, Humanoid | 12 + 20 MB | yes | The owner already had a **rigged referee** in `_AssetSource/.../Yan Files/FBXs backup` (`Referee2.fbx`: skinned ARP deform rig, 95 bones; `Referee.fbx`: the same rig with 14 gesture + 21 locomotion BGS clips). Used as-is (Humanoid, Hips explicitly mapped to `root.x`), instead of re-skinning the static mesh. Uniform texture cleaned of four baked-in league badges (`PIX_Referee_Uniform_BC.png`). Prefab `PIX_Referee_Rigged` = ArtLibrary key `referee`; the static T-pose prefab is now `referee_static`. |
| Crowd | `Characters/Crowd/` (Male1, Male2 Large, Male3, Girl, Girl3/4, Kid1/2 FBX + colour-mask atlases + RGB patterns + `crowd.shadergraph`) | FBX + PNG 4K | 312 MB | partial | Original `crowd.shadergraph` fails to compile (missing `GpuEcsAnimator.cginc` + sub graph GUID, 8 magenta materials). `ExtraPrefabBuilder.BuildCrowd` bakes team-recoloured albedo PNGs (`PIX_*_Baked.png`) and builds `PIX_Crowd_*` prefabs with HDRP/Lit. |

## 3. Stadiums, field, props

| Asset | Path | Format | Size | HDRP-ready | Notes |
|---|---|---|---|---|---|
| Pro stadium bowl | `Packages/Stadium_Regular/_Project/3D/Stadium_Regular/Meshes/Regular.fbx` (+ materials, `_ThirdParty/.../RainWetness` shader, crowd sit/stand anims, interior-mapping + banner shader graphs) | FBX 16 nodes | 547 MB pkg | yes | 281 x 96 x 268 m, centred on the pitch, authored with the pitch along **X**. |
| College stadium bowl | `Packages/Stadium_College/.../Meshes/Stadium_College.fbx` | FBX 20 nodes | 588 MB pkg | yes | 363 x 96 x 347 m (offset -17 m on Z in authoring space). Shares textures/materials with the regular package (GUID de-dup). |
| Modular stadium kit | `Packages/StadiumKit/` (`DraftFeverAssetImplementation.unitypackage`: MP_stands/corner/border/bigScreen modules, animated banners, decals, bench/pylon/flight-case/hydration prefabs) | FBX/prefab | 261 MB pkg | yes | Superset of `stadiumSwapPackage` (187 MB) and `stadiumSwapPackage_screenshot` (196 MB): those two skipped. |
| Field | `Props/Field/footballField_GEO.fbx` + `grassMarkings_NEW.png` (4096 transparent markings), `Lucca Grass Texture/` | FBX plane 137.75 x 76.75 m | 564 MB source | yes | Mesh is already 1:1 metres (120 yd play + 15 yd margins); marking texture spans the full plane (4096 px = 150 yd). `grassVariationTexture_WIP`, `WeatherAffectedTextures.spp` skipped. |
| Seat position meshes | `Props/SeatPositions/{Regular,College}/01..06.fbx` | FBX (3-9k nodes each) | ~100 MB | n/a | Seat anchor transforms for crowd spawning. |
| Stadium props | `Props/StadiumProps/` -> `PIX_Prop_*.prefab` (bench, bottleCarrier, kickingNet, downMarker, duffelBag, electricBox, endZonePylon, entranceProps, equipmentRack, fieldTarp, filmingTower, flightCases 1-3, floodlights 1-3, footballBall, goalPost, heater, hydrationStation 1-2, medicalTent, mistingFan, playClock, poleCamera, replayMonitor, satellite, sidelineCamera, stadiumDisplay, staircase, tabletStation 1-2, trashcan, yardMarkers 10/20/30/40/50/G) | FBX + 2k PNG | 1.4 GB (props+field) | yes | Base/mask/normal maps (HDRP mask-map packed). `.spp/.blend/.sbs` sources (8 GB) skipped. |
| Not imported | `Stadium_FeverBowl` (106 MB, branded fictional stadium), `draftVenue_WIP`, `Draft Night Scene`, `cardPack`, `USA map`, `stadium_WIP`, `Stadium_References`, `Locker Room` | | | | out of scope for the football pitch (draft-night/menu content). |

## 4. UI, fonts, logos

| Asset | Path | Count | Notes |
|---|---|---|---|
| In-game UI (scorebug, scoring labels, player markers, highlights, QB highlight) | `UI/InGame/` | 52 PNG -> Sprites (`ui_<name>_<folder>`) | from `UI - NEW/IN-GAME`; contains the **DFB LOGO**. |
| Atlases | `UI/Atlases/` (accomplishment badges, college logos 1-3, divisions, elo ranks, flag animation, in-game elements, mvp badges, player rings/glow, scoreboard) | 16 PNG up to 4k | atlas sprite sheets are single textures (`atlas_*` keys), not sliced. `cardatlas.psd` skipped. |
| Rank badges | `UI/RankBadges/` | | Sprites `badge_*`. |
| Team logos | `UI/Logos/Pro` 23 PNG, `UI/Logos/College` 143 PNG | Sprites `logo_pro_*`, `logo_college_*` | Parody names (see section 9). |
| Fonts | `Fonts/` | 26 TTF (Barlow Semi Condensed, Saira Condensed, cosmetics: Alfa Slab One, Bebas Neue, Oswald, Rubik, Ubuntu, ...) + Material Symbols Sharp | Google-fonts (OFL/Apache); `CollegeRegular.ttf` licence unknown. |
| Not imported | `UI/MotionGraphics` (renders, AE files), `UI/PSDs` (1.1 GB), `Pixel Art`, `Assets/Storefront Photoshoot` | | videos/PSD/AE out of scope. |

## 5. Animations

Rig facts: every BoNES / Motus file is Humanoid. Two skeleton families:

* **Motus** (`Hips / Spine..Spine4 / LeftLegUpper...`, 710 single-clip FBX, 60 fps, root motion on `GlobalSRT`) - Unity automap valid.
* **BoNES retargets** (Auto-Rig-Pro deform skeleton `root > c_traj > root.x > spine_01.x ...`, same as the player body `Small/LargePlayer`) - 26 multi-take FBX straight from the user's retarget export, valid automap.
* **BoNES studio / QB new recordings / blend packs** were exported by the vendor with the *full* 480-bone ARP control rig (deform bones flat under `c_traj`): Unity automap fails (`Required human bone 'Head' not found`) and an explicit mapping cannot work (no parent chain). All 207 files + 130 actions from `.blend` packs were **re-baked in Blender onto the clean deform skeleton** (copy loc/rot world-space constraints, character scaled from 1.10 m to 0.907 m hip height, 60 fps, take name = file name) and then import as valid humanoids.

| Pack | Path | Files | Clips | Notes |
|---|---|---|---|---|
| Motus Football (08-12-2024, Howards) | `Animations/Motus/` | 710 | 710 | blocks (Bull/Cut/Drive/Escape/Hit/Push/Shove/Siderun/Spin/Swim x Pass/Fail/Stale, `_A/_B/_CO` paired), catches (normal/fair/snap x front/back/right x high/mid/low/jump), interceptions, swats, celebrations (touchdown/big/fumble/block-kick), frustrations, handoffs (45 formations), tackles (Wrap/Broken x Dmo/Omo, 8 directions), sacks, collisions, avoids (juke/hurdle/dive), stiff arms, QB pass/shuffle/sidestep/scramble, OL/DL/LB/RB/CB/WR stance transitions, idle/jog/run/sprint cycles (JR + Leon), idle-to-run/walk turns. `Football_Player_A_01.fbx` bind-pose model skipped. Duplicate `Howards animation Motus Football.rar` (same 710 FBX) not imported (+ `HowardsOther` 2 fbx). |
| BoNES retargets (user clean export) | `Animations/BoNES_Retargets/` | 26 | ~330 | `_A/_B` paired actors: BigHits, Trucks, StiffArm, Juke (Box/Rocker/TempoChange/SpinDodge), ContestedCatches, DlineWrongArm, OT_vs_LB, ManCoverage, QB cadences, falling throws, shotgun passes, shovel/toss/touch, FairCatch+QB stance, Slowdowns, AmbientInjuries, Frustrations, IdleConversation (2 actors), TD celebrations. |
| BoNES QB (2026-04-22 clean/name-fix) | `Animations/BoNES_QB/` | 86 | 86 | QB drops/throws/scrambles/sacks/celebrations/hail-mary/dive/kneel/slide/spike/toss/shotgun catches. Re-baked. |
| BoNES studio day 1 (root motion after retarget) | `Animations/BoNES_Studio/pack1`,`pack2` | 43 + 78 | 121 | 2/3/4-point stance->sprint/shuffle/backpedal (all angles), shuffle->sprint, slow shuffle loops, standing idle loop, catch-run, fair catch, QB under-centre/shotgun/pass variety. Re-baked. `root_motion_ret_cut_ranges_*` (cut duplicates, 2 GB) and days 2/3 (A/AB/B paired raw takes, 23 GB, e.g. `BC_StripTackle`) skipped. |
| BoNES blend packs (OLine, DLine, QB sneak, player interaction, paired OT vs LB) | `Animations/BoNES_Extra/` | ~65 | ~65 | Exported from `.blend` (OLine01-027 run-block footwork, D-Line bull rush/swim/reach-shed A+B, QB sneak, 2-man interaction 2.4 min takes). QB packs 09/10/08 duplicated studio pack2 and were dropped. |
| Not imported | `studio BoNES.rar` raw/`QB new recordings` (superseded by 2026-04-22 pack), `DFBQuinoAnims*`/`QuinosAnimations.fbx` (2024-25 old), `OLD BACKUP/*.zip`, `Yan Files` (7z + blends), `Remap Test*.blend` (tests), `Remap Test Edits 29_09/01_10` (tests) | | | |

Importer settings (all FBX): Humanoid + avatar from the file, cameras/lights/blend shapes/materials off, keyframe reduction. Per clip (second pass): **lock root rotation (bake into pose)**, **lock root height Y (bake into pose)**, root XZ kept as root motion except in-place names (idle, stance, cadence, neutral, lookaround, cheer, celebration, freestyle), original orientation/Y kept, loopTime + loopPose for names containing idle/walk/jog/run/sprint/backpedal/strafe/shuffle/loop/cycle/stance/cadence. See `ArtLibraryReport.md` for the semantic keys.

## 6. Audio

| Type | Path | Count | Notes |
|---|---|---|---|
| SFX | `Audio/SFX/` Footsteps (carpet/concrete/dirt/grass/snow/wet/wood, 82), Ball Bounce (grass/snow/wet, 18), Ball hit (10), Catch (5+), Tackles/Hits (45), Kicks (5), Whoosh, Grunts, Whistle (2), UI (32), Team screen, Scout booster, client assets (20) | 244 WAV total | ADPCM, decompress on load. `Cliente SFX edited/Client SFX Project` (Ableton `.als/.asd`) skipped. |
| Ambience | `Audio/Ambience/` crowd loops (11), loading loop, rain (4), winds (3), locker room (2) | 21 | Vorbis in memory. |
| Music | `Audio/Music/DraftFeverBowlOST`, `DraftHouseOST` (1 mix each, stems skipped 800 MB), `PeytonParrish/` (17 tracks, the user's own releases, 920 MB) | 19 | Streaming Vorbis. |

## 7. Other

* `Pixel Art/`, motion-graphics renders, PSDs, Photoshoot scenes, Blender/Maya/ZBrush/Substance sources: not imported.
* Facial rig animation test videos: skipped.

## 8. Transform corrections (needed by gameplay / stadium)

| Item | Correction |
|---|---|
| Field `footballField_GEO.fbx` | authored long axis = X. Prefabs rotate the `Pitch_Rotated` container **+90 deg about Y** so the long axis is Z. Mesh already in metres, no scale. Goal lines Z = +/-45.72, end lines Z = +/-54.864, sidelines X = +/-24.384. Markings overlay raised 1.2 cm (transparent material). |
| Stadium bowls | same +90 deg Y container, no scale, no offset (bowl is centred on the pitch). |
| Goal posts | `PIX_Prop_goalPost` placed on the end lines (Z = +/-54.864 m), 180 deg yaw on +Z end. Post height 12.75 m (NFL 10 ft crossbar + uprights ~ 9-10 m: oversized, scale 0.75 if desired). |
| Pylons, yard markers | 8 pylons at goal line + end line corners; sideline markers every 10 yd 1.2 m outside the sideline. |
| Players | meshes already in metres, Y up, Z forward, feet on Y=0; hips 0.907 m. |
| Animations | Motus authored in cm (importer scale 0.01 applied), BoNES clean files in metres. Root motion: see section 5. |
| Referee | exported at global scale 0.01 (source authored in cm). |

## 9. Rename before Steam (real people, real schools, old branding)

**Real people**

* Hair groom folders (not imported but present in source `Heads/Victors Heads/Grooms`): AntonioBrown, BrettKeisel, ClayMatthews (+ `ClayMatthews_HAIRCARD`), DarrelTaylor, EzekielElliott, Harden, IslamMakachev (`IslamMakachev_HAIRCARD`, buzzcut), JoeNamath (`JoeNamath_HAIRCARD`), MauricioRuffy, MylesGarrett, OdellBeckhamJr, PatrickMaholmes, Sheamus, Viking; dreads: ChadJones, DeAndre, Lynch, Travis.
* Imported hair-card names: `Packages/HairSystem/.../001.HairCards/ClayMatthews`, `Jason_StraightHair` (Momoa_LOD0_pOSED.fbx), `StraightHair_Varg`, `clumpydread_chadjones`.
* Animator/test files named after people: `Dimitri Motion Test`, `DFBQuinoAnims`, `QuinosAnimations`.

**Real schools / teams (logo file names use real place + school names; art is parody but the names must change)**

Pro logos: Alabama, Clemson, Notre Dame Leprechauns, Michigan Badgers, Dallas Red Odins (+ "Dallas Cowboys" is printed on the jersey texture, see below) ...
College logos (143) - names that are real institutions or direct parodies: Akron, Appalachian, Arizona (x2), Arkansas State, Auburn Wildcats, Boise State, Bowling Green, Buffalo, Central Florida, Clemson, Colorado State, Connecticut, East Carolina, Eastern Michigan, Florida State, Fresno State, Georgia State, Illinois, Iowa State, Jacksonville State, Kansas State, Kennesaw, Kent, Louisiana State, Louisville, Michigan State, Mississippi State, Navy, Nebraska, North Texas, Northern Illinois, Northwestern, Notre Dame Leprechauns, Ohio State Peanuts, Oregon State, Penn State Mount Nittany Cats, Sam Houston, San Jose State, South Alabama, Southern Mississippi, Syracuse, Temple, Texas State, Texas Tech Looters, Toledo, Tulane, Tulsa, Utah State, Vanderbilt, Virginia Tech, Wake Forest, Washington State, Western Kentucky/Michigan.
Use generated original names in `PIX.League` content; keep these PNGs only as internal placeholders until the user replaces them.

**Old branding baked into textures**

* Jersey texture (`Packages/MainPlayer/.../Uniform/*`, `Characters/Bodies/mediumPlayer.fbm/T_Uniform_BC.png`): "DALLAS COWBOYS" / "DRAFT FEVER" lettering, a fake player name "MOORE 47".
* Stadium banners: "TIGERS", "bank VIII", "BOSSAN", "BAYTRONIC", "DRAFT LOGO" (`Banner01_Text_DraftLogo.png`, `Banner01_Text_ALLIN/GO`), `Endzone_Tigers*.png`, `UI/InGame/DFB LOGO.png`.
* Folder / file names: `Stadium_FeverBowl`, `DFB_logoIterations`, `Draft Fever Bowl OST`, `Draft House OST`.

## 10. Known issues / not done

* No walk cycle, no kicking/punting/long-snap mocap, no referee signals, no left-juke (mirror at runtime) in the source set.
* Strand-hair and the original crowd shader graph need packages/files that were not shipped.
* HDRP skin needs the SSS diffusion profiles registered in HDRP Global Settings (done by `PlayerPrefabBuilder.RegisterDiffusionProfiles`; the Volume diffusion list is also set in the screenshot studio).
* Jersey colour variation uses the shader-graph material properties; per-team runtime materials must clone `Mid_Size_Player_Uniform_MAT`.

## 11. Archives: extracted vs skipped

Extracted (`<archive>__x/`): `retarget fbx files.rar` (26 FBX), `2026_04_22_Josh_clean_retarget_name_fix.zip` (86 FBX), `Motus_Football_08-12-2024.zip` (710), `Howards animation Motus Football.rar` (dup, only `HowardsOther`), `studio BoNES.rar` (1003 entries, retarget packs used), BoNES blend rars (OLine01-016, OLine017-027, QBSnapActions, UndercenterSnap, QBPasses01, PairedAnimation, Player_interaction_29, QBSneak_28, D-LineMoves_30 and two Remap tests), `Maps.rar` (shoes textures), `QbGloves.zip`, `Sleeves.zip`.
Skipped: `OLD BACKUP/*.zip` (older mocap exports), `Yan Files/*.7z`, `Hair2.0001.ma.zip`, Clay/Islam/Joe Namath hair-card zips (real names, ZBrush/Maya), `Leo heads` and `Helmet-2024*` zips (6 GB, 2024), older hair packages, `Scifi_Suit_Package`, `Head_Test`, `DraftVenueSceneBackup`, `holograms`, `UI_Curtain_Dissolve_Effect` (UI shader test, not needed), `Storefront Photoshoot` rars/zips, `tom_williams_files.rar`, BoNES `.blend` rars already covered by the clean FBX set (AmbientInjury, BigHits, ContestedCatches, D-line wrong arm, FairCatch, Juke*, ManCoverage, OT_vs_LB, QB cadences/shotgun, Stiff arms, Remap Test Edits 03/04).

## 12. Final numbers

* 1009 animation FBX = 1208 Humanoid clips (all avatars valid); 698 clips are registered in `ArtLibrary` under 131 of 149 semantic keys (`Docs/ArtLibraryReport.md`).
* ArtLibrary: 138 prefabs, 67 materials, 234 sprites, 246 audio clips, 16 atlas textures, 26 fonts. Asset: `Assets/_Licensed/ArtLibrary.asset` (+ copy in `Assets/_Licensed/Resources/`).
* Many BoNES clips are long raw mocap takes (e.g. `BONES_QB_touch_pass_001` 54 s, shotgun snap 70 s, studio idle loop 217 MB): they contain several repetitions/lead-in. Cut sub-ranges in the gameplay animator (clip start/end) rather than playing whole clips.
* Screens: `Docs/Screens/*.png`.
