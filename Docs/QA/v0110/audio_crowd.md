# v0.11.0 crowd audio (AUDIO+CROWD, crowd sub-track)

Stadium crowd set for the broadcast-style sim: 8 seamless ambient beds (28 loops) and 11 reaction groups (31 one-shots), 59 files, all 44.1 kHz stereo 16-bit, in `Assets/_Licensed/PIX/Audio/Crowd/<group>/<group>_NN.wav` (git-ignored) with `crowd_manifest.csv` (group, file, loop, dur_s, lufs, peak_db, centroid_hz, syllabic, source_pack, source_file, source_range_s, derived, notes). I could not listen; every choice is by name, duration and measured features. Confidence figures below are about that, not about ears.

## Groups
| Group | n | LUFS | Loop s | Packs |
|---|---|---|---|---|
| bed_murmur_low | 4 | -30 | 24-37 | Inside the Match (mono, widened) |
| bed_murmur_mid | 4 | -26 | 20-49 | Inside the Match x2, Stadium Ambience, Draft Fever |
| bed_murmur_high | 4 | -24 | 12-30 | Stadium Ambience x2, Draft Fever x2 (+ SA21 layer) |
| bed_tension | 4 | -28 | 32-39 | Inside the Match x3, Stadium Ambience (hp 80 Hz, lp 0.8-1.1 kHz, levelled) |
| bed_clap | 2 | -26 | 6, 10 | AD Ball Sports (CheerClapLoop 01, 03) |
| bed_cheer | 4 | -24 | 7-31 | Stadium Ambience x2, Draft Fever long cheer, AD CheerLoop_01 |
| bed_boo | 2 | -26 | 6, 18 | AD Ball Sports (BooLoop 01, 02) |
| bed_noise | 4 | -24 | 25-41 | Stadium Ambience x4 (one with AD clap loop layered) |
| cheer_s / cheer_m / cheer_l | 3 each | -22 / -20 / -18 | 2.6-2.8 / 5.0-5.3 / 9.4-9.8 | Draft Fever cheers, AD CheerOneShot |
| roar_td | 3 | -16 | 11.5-15 | DF long cheer / cheers + AD OneShot layered, ramped build (derived) |
| groan, ooh_hit | 3 each | -24, -22 | 2.0-3.1 | all derived (see below) |
| boo_short | 3 | -22 | 3.0-3.5 | AD BooLoop slices |
| gasp | 3 | -24 | 1.1-2.0 | DF Crowd_noises_gasps_01 (1 plain, 2 derived) |
| clap_burst | 3 | -24 | 3.0 | AD CheerClapLoop slices |
| sigh_relief, whoop_short | 2 each | -24, -22 | 2.6-2.7, 1.0-1.1 | sigh derived; whoop = plain excerpts |

All files hit target LUFS within 0.05 dB; every peak is <= -2.0 dBFS (limit -1). Loops shorter than the 20 s brief because the source is short: clap 6/10, boo 6/18, cheer 7/15, high_03/04 18/12 (flagged in the manifest notes; the game just loops them more often).

## Tool (`Tools/AudioTool/Crowd.cs`, spec `CrowdSpec.txt`)
`cscan dir|file --out f.csv` (whole-file features + chant screens), `cwin files --win 12 --hop 6 --out f.csv` (same per window), `cenv file hop [a b]` (RMS timeline), `cprobe file a b`, `crender CrowdSpec.txt [--only group]` (renders everything, writes the manifest), `ccheck dir --spec CrowdSpec.txt` (format, LUFS, peak, loop seams), `cselftest` (positive control). Render options: `lp hp gain pitch flat snap wide ramp rampt add=key:a:b:dB:offset derived=1`. Loop builder searches start/end within +-1.5 s (`snap`) for the best seam (50 ms edge level, +-0.5 s wrap level, step ratio), filters and the slow leveller run before the wrap (a filter after it leaves a start-up step), mono sources get the right channel from the same take 7.3 s later (decorrelated, nothing synthesised).

## Chant / words screening (relative screen, medium-low confidence)
Measures: syllabic (3-7 Hz envelope modulation 250-3400 Hz; weak on music, only used relatively), envper (autocorrelation of the speech-band envelope at 0.25-2 s lags: rhythm, chant, claps), vfrac (whitened pitch-clarity: share of 93 ms frames with a clear 80-500 Hz harmonic peak). Positive control (`cselftest`, synthetic gated harmonic tone in noise): syllabic 0.27 -> 0.8 and envper 0.07 -> 0.83 when the tone is present; vfrac only rises (0.04 -> 0.7) for a clean tone, so it catches loud unison singing but not words buried in babble. Baselines: DF/AD clean loops vfrac 0.03-0.06; Stadium and Match ambience 0.10-0.18 (low-frequency rumble raises it); Fan Zone 0.22-0.41. Windows were accepted only with vfrac <= 0.22, envper <= 0.35, syllabic <= 0.42 and 250 ms level spread <= 4.5 dB, then chosen by level and centroid. Selected beds measure syllabic 0.18-0.35, envper 0.06-0.31, vfrac 0.04-0.18.

Excluded:
| Source | Why |
|---|---|
| Inside the Fan Zone, all 17 | vfrac median 0.30 (0.22-0.41 per window), highest syllabic (median 0.33), 0.8-1.6 kHz centroid: conversational / sung pub crowd. Not used. |
| AD CrowdCheerLoop_02 | vfrac 0.78 (windows 0.87-0.96), envper 0.39 (windows 0.44-0.70): voiced and rhythmic, probable chant / yelled phrase. |
| AD CrowdCheerClapLoop_02 | vfrac 0.55 (windows 0.5-0.8) vs 0.04 for siblings 01/03: voiced element on the claps. |
| AD Victory 1-5, Defeat 1-5 | jingle / stinger material (harm 0.14-0.44, vfrac up to 0.9), not crowd. |
| Stadium Ambience 2, 5, 10, 20, 39 (whole-file envper 0.29-0.57; SA2 0.57 is 7.7 s) and every window with envper > 0.35 (e.g. SA34 108-130) | rhythmic envelope, possible chanting; not used. Highest-envper windows that did ship: bed_murmur_high_02 (SA11, 0.32), bed_noise_04 (clap layer, 0.31), bed_tension_03 (0.29). |
| Soundstripe pack | no crowd names in the index (sports SFX only). |
Remaining doubt: Match / Stadium ambiences are far-field soccer recordings at 200-900 Hz centroid; any speech there is unintelligible-level murmur, but a distant chant fragment could survive, and the AD boo loops are a pitched vowel ('oo'), not words. Please give bed_murmur_*, bed_cheer_01/02, bed_noise and bed_boo one listen.

## Loop seams (`ccheck`, 28 loops, all PASS)
Step ratio (wrap jump / typical sample change) max 2.70 (median ~1.0; pass <= 6); level change last 50 ms -> first 50 ms max 1.7 dB (pass <= 3); wrap-region level vs whole loop max 1.6 dB (pass <= 2). Crossfades 1.0-2.5 s, equal power against the audio before the start. Full table: `D:\PIXFootball\Work\Audio\crowd_work\check_final.csv`.

## Derived clips (manifest derived=1; real crowd material, pitched with the resampler / lowpassed / ramped, no synthesis)
groan_01 (DF gasp -3 st, lp 1.8k), groan_02 (AD boo head -2 st, lp 1.4k), groan_03 (DF cheer decay -4 st); ooh_hit_01 (DF gasp lp 2.2k), ooh_hit_02 (DF cheer swell -2 st lp 1.1k), ooh_hit_03 (AD boo head +3 st); gasp_02 (+2 st), gasp_03 (-1.5 st lp); sigh_relief_01/02 (gasp tail, cheer decay); roar_td_01-03 (two real cheers layered, -12..-14 dB to 0 dB ramp over 1.6-2 s for the build). Confidence: gasp_01, cheers, boos, clap bursts (real excerpts) medium-high; groan, ooh_hit, sigh (low; no real example in the sources, the DF "gasps" file is a single swell with a slow decay, not separate gasps); roar_td medium. clap_burst is cheer + claps (AD "CheerClap"), not clean applause.

## Gaps
No clean applause, no real "ooh" or groan, no stomping, no "defense" style noise. bed_clap and bed_boo have only 2 variants each and are short. bed_noise is a loud low-centroid (~210-350 Hz) stadium roar; the rhythmic stomp feel only exists in bed_noise_04 (AD claps layered). Beds are soccer stadiums, no football-specific PA / horn / band (intentionally none).

## Suggested layering (levels as shipped, add offsets in dB)
| State | Beds (crossfade 1.5-3 s) | Reactions |
|---|---|---|
| Pregame / empty | murmur_low 0 dB | |
| Normal play | murmur_mid 0 dB; rotate variants every 1-2 loops | |
| Big moment / full house / red zone | murmur_high 0 dB, murmur_mid -6 dB | |
| 3rd/4th down, offence needs it | tension 0 dB replaces murmur (fade 1 s) | |
| Opponent offence 3rd down (home D) | bed_noise -2 dB + murmur_high -6 dB; clap bed -6 dB on pulse | |
| Pass complete / first down | | cheer_s/m, murmur ducked -3 dB for the clip |
| Big play / sack | | ooh_hit or cheer_m, then cheer_l |
| Touchdown | bed_cheer 0 dB for ~8 s under roar_td, then back to high | roar_td +0 dB, duck beds -6 dB for 2 s |
| Turnover / miss | tension out, murmur -4 dB | groan or boo_short (+ bed_boo -4 dB under it) |
| Near miss / incomplete | | gasp (then sigh_relief if saved) |
| Rally, timeout, halftime | bed_clap -3 dB (phase to nothing, loops are 6-10 s) | clap_burst, whoop_short |
Keep all reactions within 1 dB of manifest loudness before applying the ducks; rotate variants 01-04 to avoid repetition.
