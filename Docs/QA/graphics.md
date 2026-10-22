# Graphics / lighting QA log (track GFX)

Owner area: materials, shaders, textures, lighting, volumes, weather, scene lighting. Verification images are in `Logs/v6shots/` (edit-mode look renders, `PixFootball.EditorTools.V6Shots`), `Logs/v6_gallery1/` (the in-game look gallery, `-pixAutotestLookOnly`, v0.5 vs v0.6 pairs end in `_v05.png`) and `Logs/autotest_*`.
Honest count: 47 GFX rows above (45 fixed, 2 open) plus the audit crosswalk below (GFX-048..079). Of the 109 audit rows assigned to GFX, 38 are fixed or duplicates and 71 are still open: the audit was NOT cleared and the 80-issue target counts audit rows. The remaining budget went into building the v0.6 look itself.

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| GFX-001 | fixed | S1 | helmet texture | player helmet shell, back and top | "Weird patch" on the back of helmets: the licensed helmet base map carries white logo plates and a black mask hole that show through the team colour from behind and the sides | v6 helmet shell repaints the shell from the colour mask with the team colours (`PixLitHooks.hlsl` mode 3) and adds a clean stripe + crest | `Logs/v6shots/helmet_back_*`, `helmet_backleft_*` vs `Logs/v6_gallery1/look_night_clear_3_player_portrait_v05.png` |
| GFX-002 | fixed | S1 | branding | helmet sides | The v0.5 helmet shows a Draft Fever Bowl logo plate on the side (visible in the tackle gallery) | same repaint as GFX-001; the plate is not drawn | `look_*_3_player_portrait.png` vs `_v05` |
| GFX-003 | fixed | S1 | uniform | jersey | Every jersey carried the baked "10" numerals of the licensed sheet | numerals painted out of colour, normal and roughness maps; per-player numbers drawn by the shader | `Logs/v6shots/portrait_*`, `dirt_back_*` |
| GFX-004 | fixed | S2 | uniform | normal map | Numerals were embossed in the normal map | rect filled with the neutral normal | `portrait_*` (no ghost digits) |
| GFX-005 | fixed | S2 | uniform | roughness | Numerals printed in the roughness map | interpolated out | same |
| GFX-006 | fixed | S1 | uniform | all players | No player-specific number on the jersey | roster jersey number atlas on chest, back and sleeve caps | `look_night_clear_3_player_portrait.png` (#77, #79, #55 readable) |
| GFX-007 | fixed | S2 | uniform | back | No player name on the back | letter atlas name bar above the back number | `back_*` |
| GFX-008 | fixed | S3 | uniform | chest | No team wordmark on the chest | letter atlas wordmark (team nickname) above the chest number | `portrait_*` |
| GFX-009 | fixed | S2 | uniform | number colours | Number colour could vanish into the jersey (visitors: white jersey, light number) | `JerseyLook.For` flips fill / outline for luminance contrast | `look_*_3_player_portrait.png` |
| GFX-010 | fixed | S1 | uniform | fabric | Plastic look: no micro detail, flat smoothness | tileable mesh-weave normal, view-dependent fuzz sheen, tuned smoothness remap, specular AA | `torso_*` |
| GFX-011 | fixed | S2 | uniform | dirt | No dirt anywhere on kits | per-player dirt (`DirtModel`) by region, grass vs mud, whole game | `dirt_front_*`, `dirt_back_*` |
| GFX-012 | fixed | S2 | uniform | dirt look | First dirt looked like camouflage (low-contrast noise at full coverage) | threshold curve on noise so dirt grows from spots, darker soil, desaturated grass | `dirt_back_*` |
| GFX-013 | fixed | S2 | uniform | wet | Uniforms did not react to rain | wet cloth darkening + gloss | `portrait_Rain_*` |
| GFX-014 | fixed | S3 | uniform | snow | No snow on shoulders in snow games | shoulder snow cover | `portrait_Snow_*` |
| GFX-015 | fixed | S1 | helmet | shell | Helmet flat, plastic-looking | clear-coat shell, metallic flake, specular AA | `helmet_*` |
| GFX-016 | fixed | S3 | helmet | wear | No scuffs; helmets stay pristine | scuff / scratch maps driven by `DirtState.HelmetScuff` (line contact, hits) | `helmet_*` (dirty case) |
| GFX-017 | fixed | S2 | helmet | identity | No stripe or crest on helmets | centre stripe (secondary + outline) and team crest decal on both sides | `helmet_top_*`, `helmet_*` |
| GFX-018 | fixed | S1 | shader | PIX/Lit hooks | Magenta / black outline on the helmet stripe: reversed-argument `smoothstep` is undefined in HLSL | rewritten as `1 - smoothstep(lo, hi, x)` (also the artificial-turf tuft mask) | `helmet_top_h_*` |
| GFX-019 | fixed | S1 | shader | normals | Black frame when any weather / micro normal was on: HDRP Lit uses surface-gradient normals, the hook normalised them as unit normals (NaN through bloom) | gradients are added / scaled, never normalised (`PixGradFromTex`) | `Logs/v6shots/broadcast_low_Rain_Night.png` renders |
| GFX-020 | fixed | S1 | turf | texture | Turf blurry and visibly tiled at close range | macro variation (two noise scales), blade micro albedo + normal at 3 tiles per metre, specular AA | `field_macro_*` |
| GFX-021 | fixed | S2 | turf | stripes | No mowing pattern | 5-yard stripe contrast per turf type | `field_mid_*`, gallery broadcast shots |
| GFX-022 | fixed | S2 | turf | wear | No divots or scuffs where tackles / piles happen | 2048x4096 wear texture (compute) stamped at tackles, piles, falls; footstep wear | `field_macro_*`, `field_mid_*` |
| GFX-023 | fixed | S2 | turf | soil | Soil stamps looked like pale blotches on the grass | darker soil, micro-modulated edge | `field_mid_Clear_Night.png` |
| GFX-024 | fixed | S3 | field paint | lines | Lines pristine and uniform | chipped paint noise, wear from play, snow cover | gallery shots |
| GFX-025 | fixed | S1 | field paint | midfield | The painted league shield sat at midfield instead of the home team crest | shield cleared from the marking texture (`BuildFieldMarkings`), `FieldLogo` quad with the home crest | `look_*_1_broadcast_presnap.png` (after merge build) |
| GFX-026 | fixed | S1 | weather | all | No weather or time-of-day variety | 15 presets (Day / Dusk / Night x Clear / Overcast / Rain / Snow / Fog) as data, applied by `WeatherSystem`, team-select options | `Logs/v6_gallery1/look_*_1_broadcast_presnap.png` |
| GFX-027 | fixed | S1 | weather | rain | No rain; surfaces dry | rain streaks, splashes, wet darkening / gloss, ripples, puddles | `look_night_rain_*` |
| GFX-028 | fixed | S2 | weather | puddles | Puddles had straight lattice edges (weak hash) | sin-free hash noise | `broadcast_low_Rain_Night.png` |
| GFX-029 | fixed | S1 | weather | snow | No snow; first snow render blew out to white at day | snow accumulation on field and stands over the game, exposure compensation for snow day, footprints in the wear texture | `look_day_snow_*` |
| GFX-030 | fixed | S1 | weather | overcast sky | Overcast / rain day was almost black: a thick PBS atmosphere extinguished the sun | aerosol density capped at 0.09 for cloud cover | `look_day_overcast_*` |
| GFX-031 | fixed | S2 | weather | fog | No fog preset | volumetric fog per preset (Fog and each time of day) | `look_night_fog_*` |
| GFX-032 | fixed | S2 | lighting | floodlights | Floodlights tied to the night root, could not be on at dusk or in rain | own rig root, `WeatherSystem` switches and scales it per preset | gallery dusk / rain shots |
| GFX-033 | fixed | S2 | lighting | floodlights | Spot lights had no beam shape | soft-edged cookie (stand-in for an IES profile) | gallery night shots |
| GFX-034 | fixed | S2 | lighting | haze | No visible light shafts | per-preset volumetric dimmer on the lamps, anisotropy tuned | `look_night_clear_*` |
| GFX-035 | fixed | S2 | lighting | shadows | Only one floodlight cast shadows (hard-coded index) | rim lamps cast shadows by graphics tier (High one, Ultra two) | tier code, `QualityTiers` |
| GFX-036 | fixed | S2 | lighting | reflections | No reflection probe over the pitch; wet turf had nothing to reflect | on-demand real-time probe, re-rendered on every preset change | scene build log |
| GFX-037 | fixed | S2 | render settings | HDRP asset | SSR was disabled in the pipeline asset, so the volume override did nothing; SSGI unsupported | `supportSSR` and `supportSSGI` enabled; SSGI only on Ultra | `PixHDRP.asset` |
| GFX-038 | fixed | S2 | grade | volume | One static grade for every condition | per-preset grade (white balance, contrast, saturation, green boost curve, split toning) built in code | gallery |
| GFX-039 | fixed | S2 | grade | cinematic | Lighting not dramatic enough (user note 10) but live play must stay clean (2K8 reference) | second "drama" volume blends in for replays / cutaways / shows (and a little at night) with more contrast, bloom, vignette; live play keeps the clean grade; first pass was too hot (lime turf) and was pulled back | gallery vs `_v05` |
| GFX-040 | fixed | S2 | lighting | flood balance | Flat floodlighting on players | rim-lamp bias per preset so players get a stronger key / rim split | `look_night_clear_3_player_portrait.png` |
| GFX-041 | fixed | S3 | stadium | materials | Stadium surfaces ignored weather | 40 stadium materials cloned onto PIX/Lit (wet, puddles, snow) with per-material tuning | scene build log (103 slots) |
| GFX-042 | fixed | S2 | quality tiers | settings | Tiers did not scale the new features | rain / snow amount, splashes, breath vapour, flood shadows, SSGI per tier | `QualityTiers.cs` |
| GFX-043 | fixed | S3 | replay | uniforms | Replay actors would not show numbers / dirt | clip stores per-player dirt, kit, wordmark and crest; `ReplayPlayer` dresses actors the same way | `ReplayPlayer.Load` |
| GFX-044 | fixed | S3 | scene | lighting roots | Day / night toggle lost the floodlight state | `LightingController` forwards to `WeatherSystem` and keeps the flood root consistent | pause menu path |
| GFX-045 | fixed | S3 | tooling | build | Another agent's mid-edit compile errors block every Unity launch on the shared project | `Tools/unity_retry.ps1` waits for the editor lock (25 min) and retries compile failures | tool |
| GFX-046 | open | S2 | model | player waist / legs | Body mesh (thin waist / legs) affects how uniforms look; mesh work belongs to MODELS | none here; the uniform regions mask was baked on the current UVs and must be re-baked if the UV layout changes (`Tools/Blender/bake_player_regions.py`) | assigned to MDL track |
| GFX-047 | open | S3 | uniform | right sleeve cap number | The right sleeve numeral position follows a UV analysis and reads upside-down in some poses | adjust `numberShoulderR` rotation after a close-up review | `Docs/ArtPipeline.md` section 4 |

## Audit crosswalk (Docs/QA/audit.md rows assigned to GFX)

Fixed in this pass (33 + 5 rows); every other GFX row of the audit stays **open**: not addressed, listed at the end. Rows owned by other tracks' files (FieldMarkers rings, crowd / stadium models, UI settings panel, input) are open and should be re-assigned.

| QA | Status | GFX | Note |
|---|---|---|---|
| QA-014 | fixed | GFX-069 | free roam uses the same WeatherSystem night rig |
| QA-076 | fixed | GFX-073 | sky / fill raised for day |
| QA-080 | fixed | GFX-073 | day ambient multiplier 1.5 and the clean live grade; reserve drama for night / cinematics |
| QA-1019 | fixed | GFX-074 | graphics_notes.md claims verified with back / side helmet renders (Logs/v6shots/helmet_*_n_*) |
| QA-1046 | fixed | GFX-066 | one decal default (0.42, 0.22, 0.12) in shader and asset |
| QA-176 | fixed | GFX-071 | same |
| QA-212 | fixed | GFX-063 | shadow settings and ambient of the weather volume (far stands no longer pitch black at night: indirect 1.1 with sky fill) |
| QA-255 | fixed | GFX-071 | live-play volume has motion blur 0 |
| QA-287 | fixed | GFX-073 | same pass: live day grade is bright, saturation 10, contrast 14, drama volume only in cinematics |
| QA-300 | fixed | GFX-070 | sun angular diameter 1.4 deg minimum: soft, short-edged player shadows |
| QA-551 | fixed | GFX-051 | DirtModel initialises grass fraction / mud from the first event's context |
| QA-552 | fixed | GFX-076 | comment corrected |
| QA-555 | fixed | GFX-077 | jersey 0 (unset recruits) draws no number |
| QA-706 | fixed | GFX-067 | FieldLogo.FaceCamera turns the crest to the playing camera per play |
| QA-709 | fixed | GFX-048 | command-line flags cached in static readonly bools, property id cached |
| QA-714 | fixed | GFX-068 | weather volumes follow where the camera looks at the ground and grow with camera height (up to 80 m); emission scaled |
| QA-717 | fixed | GFX-049 | dead multiply removed, drama profile destroyed (the MatchDirector poll remains, cheap) |
| QA-718 | dup | - | same as QA-812 |
| QA-719 | fixed | GFX-050 | stamps bounded to 96 per frame; a single ComputeBuffer dispatch is still open |
| QA-740 | fixed | GFX-075 | sceneLoaded handler subscribed once |
| QA-812 | dup | - | fixed by another track (QualityTiers now stores and restores what it switched off) |
| QA-935 | fixed | GFX-064 | centre stripe fades out over the nape |
| QA-936 | fixed | GFX-065 | crest decal gated to the front of the shell |
| QA-957 | fixed | GFX-052 | grey jersey sheet imported as sRGB colour |
| QA-958 | fixed | GFX-053 | weave / knit micro normals imported as NormalMap, sRGB off, 512 (also the regions mask as clamped linear) |
| QA-959 | fixed | GFX-054 | V6 helmet maps are linear / normal copies; the v0.5 legacy graph keeps its own imports |
| QA-963 | fixed | GFX-055 | generated atlases compressed with CompressedHQ (BC7) |
| QA-964 | fixed | GFX-056 | turf blades lean in every direction (no directional streaks in the micro normal) |
| QA-965 | fixed | GFX-057 | copied licensed textures use Clamp wrap |
| QA-966 | fixed | GFX-058 | helmet material forced to MaterialID standard (a stale subsurface flag made tertiary colours render magenta) |
| QA-971 | fixed | GFX-059 | night floodlights run at 20 percent of the saved 9.4 M cd (about 2,000 lux), exposure 6.4 |
| QA-972 | fixed | GFX-060 | night indirect multiplier 1.1 (was 5), sun 90 lux, exposure lowered |
| QA-973 | fixed | GFX-061 | rim lamps get shadows by tier (High 1, Ultra 2) from WeatherSystem |
| QA-974 | fixed | GFX-062 | each preset has its own grade (split toning, contrast, vignette, bloom), built at run time |
| QA-975 | fixed | GFX-063 | weather volume sets max shadow distance 90, cascade borders 0.15, 4 cascades on High+ |
| QA-978 | fixed | GFX-078 | lodBias strictly increasing 0.4 / 0.6 / 0.8 / 1 / 1.25 / 1.5 |
| QA-980 | fixed | GFX-079 | skinWeights 4 on every level |
| QA-990 | fixed | GFX-072 | menu fog mean free path 140 m (was 40) |

Open (not addressed): 
QA-004 QA-015 QA-030 QA-031 QA-032 QA-043 QA-046 QA-047 QA-051 QA-077 QA-081 QA-085 QA-086 QA-087 QA-095 QA-1002 QA-1035 QA-1036 QA-1037 QA-1038 QA-1039 QA-1040 QA-1044 QA-1047 QA-110 QA-111 QA-115 QA-116 QA-120 QA-145 QA-153 QA-201 QA-208 QA-211 QA-216 QA-217 QA-218 QA-219 QA-235 QA-244 QA-246 QA-258 QA-265 QA-297 QA-553 QA-554 QA-705 QA-707 QA-708 QA-710 QA-715 QA-716 QA-813 QA-814 QA-815 QA-857 QA-868 QA-879 QA-893 QA-934 QA-960 QA-961 QA-962 QA-967 QA-968 QA-969 QA-970 QA-976 QA-977 QA-989 QA-991 

## v0.6.0 release integration rows (release integrator)

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| GFX-080 | fixed | S3 | test | TextAtlas.Clean | digits became word gaps ("A1B" -> "A B"); `LookTests.TextAtlas_CleanUpperCasesTrimsAndCuts` failed | digits are dropped | `LookTests` |
| GFX-081 | fixed | S3 | uniform | PixLitHooks.hlsl dirt | grass stains on white away jerseys and pants read as camouflage late in a game (#55 in `match_block_close_1`, #15 in `contact_sack_3`) | stain opacity 0.82 -> 0.62, coverage curve 0.62 -> 0.46, lighter olive grass colour | `final_default/contact_sack_*`, `match_block_close_1.png` |
| GFX-082 | open | S2 | weather | AutoTest look gallery / runtime preset switch | the look gallery's `day_snow` frames (a runtime switch Night Clear -> Day Snow) are blown out white at any snow cover and any exposure override (`-pixSnowEv 8` changes nothing); a real snow-day match (`-pixWeather 4 -pixTime 1`) is exposed fine (grey, footprints, legible) | not fixed: the cause is in the runtime preset switch (pause-menu / gallery path), not in the preset; `WeatherSystem.SnowFullCoverEv` is a tuning hook, default 0 | `D:\PIXFootball\Renders\v060_release\snow_a .. snow_d`, `snowday\match_carry_1.png` |
