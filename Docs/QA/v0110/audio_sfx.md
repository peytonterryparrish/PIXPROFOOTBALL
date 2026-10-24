# v0.11.0 AUDIO+CROWD - on-field SFX library (sub-track log)

Output (git-ignored): `Assets/_Licensed/PIX/Audio/Sfx/<group>/<group>_NN.wav` + `Assets/_Licensed/PIX/Audio/Sfx/sfx_manifest.csv`.
272 clips in 38 groups, all 44.1 kHz / mono / 16-bit, sample peak -3.0 dBFS (verified +-0.01), no leading silence, tails faded (25-80 ms), DC removed, no clipped sources.
Nobody auditioned these: every choice is by name/folder + measured features. See Gaps for the weak groups.

## Rebuild
Tool verbs live in `Tools/AudioTool/Sfx.cs`: `sfxbuild Tools/AudioTool/SfxSpec.csv` renders everything + manifest;
`sfxverify` audits; `sfxdups` finds near-identical clips; `sfxprobe <file|dir>` and `sfxenv <file> [hopMs] [t0 t1]` inspect sources.
`Tools/AudioTool/SfxExtract.sh` extracts the source wavs from the Unity packages into `D:\PIXFootball\Work\Audio\sfx_tmp\<pack>\` (never the repo).
Spec row: `group,layers,maxdur,fadeout_ms,notes`; layer = `pack:path[#start-end][@offset][&extendTo][*gainDb][~k-th transient]`; layers joined by `|` are mixed.

## Groups (n, sources, confidence, suggested gain)
Gain = per-group mix trim in dB relative to the -3 dBFS-peak clips, for a broadcast mix with crowd beds near -26 LUFS (hits must read clearly above the bed; steps sit under it unless the camera is close).
df = Draft Fever, gm = Gamemaster Pro, ss = Soundstripe Sports, ad = AD Ball Sports.

| group | n | sources | conf | gain dB |
|---|---|---|---|---|
| hit_light | 7 | gm punch_blocked / soft_jab / body_impact | med | -7 |
| hit_medium | 8 | gm body_impact, grit_wet, kick_hard | med | -4 |
| hit_heavy | 9 | df Hard_hitting_tackles x5, df ClientAssets tackle impacts x2, 2 df+gm low-thud layers | high | -2 |
| hit_big | 7 | df ClientAssets body-impact / tackle / heavy-body, df Bodies_impact, 3 layered df tackle + gm low thump + sub slam | med-high | 0 |
| block_thud | 9 | gm punch_low_deep_impact x7, body_impact x2 | med | -4 |
| helmet_clack | 7 | gm wood_block_sticks_hit_clap x6, one bat-on-head impact | LOW | -6 |
| shed_slap | 6 | gm punch_slap_whack x4, ss Gloves_Parry_Punch_Hard, gm grab_throw | med | -8 |
| grunt_hit | 10 | gm Human Male A/B/C/D hurt_pain / grunt_pain | med | -7 |
| grunt_effort | 11 | gm Human Male A/B/C effort_grunt / quick_action / jump | med | -10 |
| pile_scrum | 4 | df multi-body tackle (1 take) + 3 built piles (df tackles + gm body hits + cloth + gear rattle) | med | -3 |
| ground_thump | 6 | gm Object Deep Thud, punch_low_deep + turf-land layers | med | -5 |
| snap_slap | 5 | df Ball hit, ss Medicine_Ball_Catch, df+gm glove layer | med | -5 |
| handoff_slap | 6 | df Ball hits (new version) x4, gm glove_catch x2 | med | -8 |
| throw_whoosh | 8 | df Client whooshes / hand-throw / swoosh, gm knife swing, ss Racket_Whoosh | med-low | -12 |
| ball_whirr | 2 | gm whoosh_weapon_spin, loop-extended to 1.8 s (optional) | LOW | -16 |
| catch_soft | 8 | df Cath ball, ss Single_Hand_Catch Soft/Gentle, gm glove_catch | high | -10 |
| catch_hard | 8 | df Cath ball / Client Catch_ball, ss Catch_Hard_Heavy / Single_Hand_Catch_Hard, gm glove_catch | high | -6 |
| catch_contested | 6 | df Catch_Tackle edit, 5 built (named catch + gm pad/body hit 20-40 ms later) | med | -3 |
| drop_thud | 5 | df Ball hit with bounces, df bounce + gm turf thud | med | -8 |
| bounce_dry / wet / snow | 6 / 6 / 6 | df Ball Bounce grass / wet_grass / snow_grass (folder-named) | high | -12 |
| fumble_pop | 6 | gm grab_throw, ss glove punch, 2 built slap + ball thump | med-low | -6 |
| kick_thump | 6 | df Kicks, df ClientAssets kick, ss Football_Kick | high | -1 |
| kick_punt | 4 | df ClientAssets kick (long), ss Football_Kick longer tails | med | -1 |
| kick_short | 6 | ad KickFootBall v1-v3, df Ball_kick_04 | med | -5 |
| step_run_dry / wet / snow | 13 / 10 / 10 | df Footsteps Run (grass+dirt, wet, snow) + gm footstep grass/mud/water/snow run | high | -20 |
| step_walk_dry / wet / snow | 10 / 9 / 10 | df Footsteps Walk + gm walk | high | -26 |
| plant_cut | 8 | gm footstep_dirt_land / grass_land | med | -16 |
| slide_turf | 4 | gm footstep_sand_slide (stand-in) | LOW | -16 |
| whistle_short | 5 | ad Whistle short/double, df Umpire whistle (cut) | high | -8 |
| whistle_long | 5 | ad Whistlev1Long (raw + sustain-extended), df Umpire_whistle_01 (raw + extended), ad Multiple | high (extended: med) | -9 |
| chain_clank | 9 | ss chain-fall windows x4, gm metal shake / light clank, key jingle + low tin | LOW-med | -12 |
| flag_toss | 7 | gm foley cloth fast movement, object_throw_move | low-med | -16 |

## How classification was decided
1. File/folder name is the primary key (Cath ball -> catches, Hard_hitting_tackles -> hit_heavy, Ball Bounce/<surface> -> bounce_*, Umpire/Whistle -> whistles, Footsteps/<surface>/<Run|Walk>). Catch groups contain ONLY named catch files; the df catch+tackle edit is used only in catch_contested (the old build's catch/tackle mix-up cannot recur).
2. Measured features ranked within a family: spectral centroid and 4 ms RMS decay separated slap (2.5-4.5 kHz centroid) from thud (130-220 Hz) and tiered hit weight (light = bright short block/jab; medium = body thud 0.3-0.6 kHz; heavy = DF tackles; big = long multi-body tackles plus sub layers).
3. Transient detection (`sfxprobe` onset list) located each event in multi-event sources (Soundstripe catches/kicks, chain falls); `~k` / `#a-b` cut one event per clip. Footstep clips are cut before a second footfall (onset >= 170 ms) so each holds one step.
4. Clipping detector (>= 4 consecutive samples at +-0.995) dropped one clip (gs Kick Ball_5). Stereo sources whose mono fold loses > 4 dB are warned (only df snow bounce 03/06, -4.6 dB; kept).
5. `sfxdups` (peak-aligned correlation > 0.9) removed one recording filed under two names: df Client_SFX_Catch_Tackle_01 = ClientAssets 232633539 take, Client_SFX_Tackle_04 = 314286435, ClientAssets 152684303 = ss Football_Kick_03, plus overlapping Ball_hit / Ball_kick variants. Only the intentional raw vs sustain-extended whistle pair remains.
6. Contact, ball and foot groups start with their first strong transient (within 20 dB of the peak) at t ~ 0 so gameplay can trigger on contact; exact peak time is in the manifest notes (`peak@s`). Whistles, grunts, whooshes, slides and piles start at the first audible sample.
7. Layered clips (`built:` in notes) are peak-normalised per layer, mixed at the stated dB offsets, then re-normalised.

## Gaps and honesty
- cadence ("hut/set/hike"): MISSING. The only speech in the sources is soldier radio/combat barks (intelligible, wrong), so no `cadence` group was made.
- helmet_clack: no true plastic-shell crack in any source; clacks are wood-block hits (dry, ~2 kHz centroid) plus one bat-on-head impact. Placeholder quality.
- slide_turf: no grass/turf skid exists; sand slides (soft, 0.5-1 kHz) stand in. ball_whirr: no spiral hiss exists; spinning-object whooshes loop-extended (splice is correlation-matched but unheard).
- chain_clank: closest are chain-fall / keys / metal shakes; centroid is bright (several kHz), so low-pass in the mix if it sounds tinkly.
- hit_big / pile_scrum: about half are built from layers; the df ClientAssets originals are the realistic core, the gm sub layers add weight unheard.
- Voices: grunt clips are labelled pain/effort/grunt from four male actors; screened by name only for non-verbal content.
- Sources reused across groups (different cut or layer): df Ball hit (snap, handoff, fumble, drop), gm glove_catch (snap, handoff, catch), ss Catch_Hard_Heavy (catch_hard, catch_contested), gm punch_slap/jab (shed, fumble, hit_light, contested), gm grab_throw (shed, fumble).
- Stereo to mono is a plain average; 48/96 kHz sources resampled with the tool's sinc resampler.

## Gain notes
Gains are mix trims, not normalisation: clips already peak at -3 dBFS. Whistles are tonal (RMS about -10 dBFS), so they need the -8/-9 trim to sit roughly 9 LU over the bed instead of louder than the hits. Hits at 0/-2/-4 land 20+ dB over a -26 LUFS bed on peaks; footsteps at -20/-26 only emerge at close 3D range, which is intended.
Verification: `sfxverify` 272 files, 0 problems; `sfxdups` 1 intentional pair.
