# v0.6.0 graphics track: CHANGELOG / patch-notes drafts (for the lead to merge)

## CHANGELOG draft ([0.6.0] "Graphics pass 1", graphics track)

### Added
- **PIX/Lit shader** (`Assets/_Project/Shaders/PixLit`): a copy of HDRP/Lit with one hook (`PixLitHooks.hlsl`) for five material modes: stadium surfaces (wetness, puddles, rain ripples, snow cover), turf, uniform, helmet shell, field markings. HDRP lighting, shadows, SSS, decals, SSR and ray tracing paths are untouched.
- **Uniforms**: team-colour masks on the licensed grey sheet, mesh-weave micro normal + view-dependent fuzz sheen, wet-cloth response, **jersey numbers** (collegiate block numerals from an atlas, back / chest / both sleeve caps, per-player from the roster), **player name** on the back and **team wordmark** on the chest (letter atlas), number colours chosen for contrast against the jersey. No render textures and no decals per player: all in the shader.
- **Helmets**: glossy clear-coat shell in the team colour, centre stripe, team crest decal on both sides, scuffs and scratches that accumulate with line contact and hits.
- **Dynamic dirt**: `DirtModel` (Core, pure) soils knees / seat / front / back by event (tackled, dove, fell, ragdoll, sacked, pancaked, line contact, piles), kept per player for the whole game; heavier and browner in rain, lighter on artificial turf. Replays show the same stains.
- **Field wear**: one 2048 x 4096 wear texture written by a compute shader (divots at tackles and piles, footstep wear, mud in the wet, dark footprints in snow that fill back in).
- **Turf**: procedural 5-yard mowing stripes, macro variation, blade micro detail, natural grass or artificial turf (a third of the league plays on turf), wetness / puddles / snow response; painted lines chip and wear.
- **Weather and time of day**: Day / Dusk / Night x Clear / Overcast / Rain / Snow / Fog (15 presets, pure data in `WeatherPresets`), picked on the team-select screen (Random weights toward fair weather). Rain streaks + splashes + wet shine + ripples; falling snow that accumulates on the field and stands during the game; breath vapour in the cold; volumetric fog per preset; a ball-slickness factor (`PlaySimOptions.BallSlickness`, 0 = unchanged simulation) lowers catch rates a little in the wet.
- **Lighting**: floodlight rig on its own root (on at dusk / overcast / rain / night) with a soft-edged beam cookie, volumetric beams, rim lamps with shadows by graphics tier, per-preset sun / sky / exposure, a real-time reflection probe over the pitch, optional SSGI on Ultra.
- **Colour grade**: 2010s broadcast look (warm highlights, cool shadows, rich greens, crisp contrast) per preset; a second "drama" volume blends in for replays, cutaways and shows (and a touch at night); live play stays clean and bright.
- **Home crest at midfield** (`FieldLogo`), the painted league shield is cleared from the field texture.
- Graphics tiers scale rain / snow amounts, splashes, breath, flood shadows, SSGI.
- Tools: `Tools/Blender` player LOD / region bake / PBR pack / hi-lo bake / fabric weave scripts, `Docs/ArtPipeline.md`, import presets + `AssetPostprocessor`, `Tools/unity.ps1`, edit-mode look renders (`V6Shots`), `-pixAutotestLookOnly` before / after gallery.

### Fixed
- The "weird patch" on the back of helmets: the v6 shell no longer draws the licensed helmet base-map logo plates; verified in back / side / top close-ups (Logs/v6shots/helmet_*_o_*). A stale subsurface material flag that rendered tertiary colours magenta was also found and fixed.
- Same placeholder number ("10") on every jersey; baked numerals painted out of the colour, normal and roughness maps.

### Known issues (draft)
- The right sleeve cap numeral may read upside down in some poses; the helmet crest is placed by eye and can sit partly behind the facemask from the front.
- Ray-traced shadows / reflections ("Ultra RT") are not enabled (SSGI only).
- Grass blade cards near the camera are not implemented (the micro normal + albedo variation carry the close-up).

## Patch notes draft (player-facing)
Your game now looks like a TV broadcast under real conditions. Pick the **weather** (clear, overcast, rain, snow, fog) and **time of day** (day, dusk, night) on the team-select screen. Rain soaks the field and the uniforms, snow piles up through the game and shows every footprint, floodlights cut through the haze. Players get dirty: grass stains and mud build up on the knees, chest and back as they dive, get tackled and pile up, and helmets pick up scuffs. Every player now wears his own number (front, back, sleeves), his name across the back and the team name on the chest; helmets have a team stripe and crest. The home team's crest is painted at midfield. The turf has mowing stripes, wear marks where the action was, and natural grass or artificial turf depending on the stadium.

What's next: v0.7.0 "League" (regen player generator, the 32-team league and 138-school college world in-game, quick sim with box scores).

## Update after the QA pass
- Night is now ~2,000 lux on the pitch (floodlights at 20 percent of the saved candela, exposure 6.4, indirect 1.1): pools of light, deeper shadows; live play keeps the clean grade and a second volume adds drama only in replays, cutaways and shows (and a little at night).
- Jersey name bar (back) and team wordmark (chest) from a letter atlas; team crest on both sides of the helmet plus a centre stripe; home crest painted at midfield (turns to face the playing camera).
- Free roam now runs the same night rig. Weather particles follow the camera's ground target and grow with its height.
- Graphics tier changes restore features (done in QualityTiers by another track; this track's tier features: rain / snow amount, splashes, breath, flood shadows, SSGI).
- Not done: single-dispatch wear compute, crowd team colours, end-zone paint, user-marker ring decal, settings UI (other tracks); see the open list in graphics.md.
- Tests: LookTests (dirt model, number atlas, text atlas, weather presets) and ImportRuleTests are in; the full suite could not be run while other tracks' test files did not compile (see the report).
