# v0.11.0 music production (AUDIO+CROWD, music sub-track)

Instrumental American-football menu / presentation music built **only from the owner's Draft Fever OST stems** (Bowl, 108 BPM; House, 90 BPM). The owner's sung songs were not used (only as a vocal reference for the checks). No logos, no Epikton, nothing else. The full OST mixes were deliberately **not** used (see vocal evidence). Output (git-ignored): `Assets/_Licensed/PIX/Audio/Music/` (7 loops + `music_manifest.csv`) and `.../Stingers/` (10 one-shots). All 44.1 kHz stereo 16-bit.

## In-game mapping
| File | Use |
|---|---|
| menu_calm / menu_drive / menu_house | Main menu A / B / C: alternate, 2-4 s equal-power crossfade (no beat sync, BPM 108/108/90). menu_drive is 2 dB hotter than A/C by design. |
| pregame_intro (31 s) then pregame_bed | Pregame cue: build, big hit at 24.0 s, ring-out to silence. Start pregame_bed (loop) at ~26 s under the coin toss / line-ups, crossfade 2 s with the intro tail. |
| st_halftime then halftime_loop | Halftime fanfare, then relaxed loop under the halftime show / stats. |
| match_under_low | Between plays, under crowd noise (-30 LUFS, 3.6% energy above 600 Hz). |
| match_under_tense | Two-minute drill / 4th-down tension (ostinato perc + strings); fire st_twominute on the warning. |
| st_touchdown / st_fieldgoal / st_turnover / st_sack_hit / st_firstdown | By event. |
| st_final_win / st_final_loss | Final whistle (resolved endings). |

## Source map (exact; source time = mix timeline; Bowl stems carry 1.4815 s of extra lead-in, trimmed in the tool)
Bowl = `Draft Fever Bowl OST\Stems\{brass,PCH,Perc,strings,WW}_03.wav`; House = `Draft House OST\Stems\{PCH,Perc,WW,brass,strings,fx.cm_01,guitar.cm_02,synth.cm_01}_04.wav`. Balance (dB) Bowl: strings 0, WW +4, PCH +7, brass +1.5, Perc +2.5 (from the LS fit). Loops cut A..B with a bar-grid/chroma/level search (`mloop`), then A/B shifted by whole beats to minimise the seam step (`mseam`); the last X s are crossfaded (equal power) with the audio before A.
| Cue | Source, stems, range (s) | Loop X |
|---|---|---|
| menu_calm | Bowl strings+WW+PCH 64.73-180.29 (no brass/perc) | 3.0 |
| menu_drive | Bowl all 5 stems 15.85-80.29 (groove, 4-bar perc/strings break, return) | 2.5 |
| menu_house | House synth,fx,guitar,strings(+5),WW(+6),brass(+3),Perc(+3) 12.65-129.99 (44 bars; the arrangement repeats at +44 bars) | 3.0 |
| pregame_bed | Bowl Perc(+6),strings,PCH,WW,brass(-2) 23.62-81.40 | 2.5 |
| halftime_loop | House synth,guitar(+3),strings(+6),WW(+6),brass(+3), no perc 65.98-116.65 | 3.0 |
| match_under_low | House synth(lp 800 Hz),strings(lp 1.5 kHz),Perc(lp 300 Hz, -2) 71.32-159.32 | 3.0 |
| match_under_tense | Bowl Perc(+4),strings(+3),PCH,brass(-2) 117.51-148.62 (steady ostinato) | 2.0 |
| pregame_intro | Bowl 5 stems 49.18-78.0 with gain automation (-22 dB crescendo through the groove and the bar29-32 break) onto the bar-33 return hit (src 73.28, out 24.0 s) + extra perc/brass hit, reverb, 3.2 s fade | - |
| st_touchdown | Bowl 98.5-105.0 (bar-44 fill to bar-45 theme), brass/perc favoured, reverb | - |
| st_fieldgoal | Bowl 109.18-111.9 | - |
| st_turnover | Bowl Perc 100.7-102.0 @0.6x (lp 500) + brass @0.5x + strings @0.5x, reverb | - |
| st_sack_hit | Bowl Perc 73.24-74.1 + octave-down thump (lp 220) + brass | - |
| st_firstdown | Bowl strings+WW 84.73- swept 0.9x-1.35x over 1.5 s + brass/Perc accent 109.35-110.1 at 1.4 s | - |
| st_halftime | Bowl 5 stems 9.18-15.4 (groove entrance), 1.8 s fade | - |
| st_final_win | Bowl 5 stems 215.35-226.0 (the piece's own ending, cut at 222.2 s + natural tail) | - |
| st_final_loss | House synth,strings,WW,brass,guitar 229.98-239.4 @0.92x, lp 2 kHz, no perc (the piece's own ending) | - |
| st_twominute | Bowl strings+WW swept 0.75x-1.6x over 3.6 s + Perc 118.07-122.5 crescendo, brass/Perc hit at 3.6 s | - |

## Final measurements (re-read from the written 16-bit files; `mcheck`)
| File | dur s | LUFS | ST-max | peak dB | seam: lvl 50 ms dB / jump:step / flux pct |
|---|---|---|---|---|---|
| menu_calm | 115.6 | -22.0 | -17.2 | -6.5 | 0.30 / 1.59 / 73 |
| menu_drive | 64.4 | -20.0 | -18.0 | -6.1 | -0.45 / 0.96 / 45 |
| menu_house | 117.3 | -22.0 | -18.3 | -5.3 | -1.75 / 1.10 / 73 |
| pregame_bed | 57.8 | -22.0 | -20.0 | -6.4 | -0.48 / 0.35 / 34 |
| halftime_loop | 50.7 | -24.0 | -22.2 | -10.2 | 0.10 / 0.58 / 33 |
| match_under_low | 88.0 | -30.0 | -27.3 | -11.9 | -1.12 / 1.83 / 45 |
| match_under_tense | 31.1 | -26.0 | -24.4 | -11.3 | -0.60 / 0.28 / 12 |
| pregame_intro | 31.0 | -20.1 | -12.9 | -1.3 | one-shot |
| st_touchdown | 7.7 | -16.8 | -16.4 | -1.5 | |
| st_fieldgoal / turnover / sack_hit | 3.4 / 3.6 / 1.8 | -18.0 / -18.0 / -18.1 | -17.3 / -17.8 / -18.1 | -4.4 / -1.8 / -1.3 | |
| st_firstdown / halftime | 2.7 / 7.2 | -18.0 / -18.0 | -18.0 / -17.4 | -1.3 / -4.3 | |
| st_final_win / final_loss | 10.7 / 11.2 | -18.0 / -18.0 | -16.6 / -16.5 | -6.1 / -5.0 | |
| st_twominute | 5.6 | -18.1 | -15.6 | -1.3 | |
Seam: "lvl" = RMS first 50 ms vs last 50 ms of the file, "jump:step" = |x[0]-x[N-1]| over the RMS sample step (about 1 = no click), "flux pct" = spectral-flux of the 7 frames around the wrap as a percentile of all such windows when the loop is played twice back-to-back (<90 = no more abrupt than ordinary events). All seven pass; no clipped samples anywhere; one-shots start and end at |x|<=0.00003. A look-ahead limiter (-1.3 dBFS) acts only on the stingers peaking at -1.3 to -1.8. TD short-term max -16.4 LUFS.

## No-vocal evidence (honest)
**Verdict: no sung or spoken vocals in the 17 files, high confidence (about 90-95%) but not proof.** The assurance rests mostly on the construction (instrument-named stems only, never the mixes), and the analysis below is supporting evidence. Nobody has listened to the result.
1. **Stem reconstruction does not close, so the full mixes are not certified and were not used.** Bowl stems carry 1.4815 s of extra lead-in vs the mix (offset stable +-1 sample over the track); after alignment: LS-fit stems explain 36.4% of mix energy (residual -2.0 dB); with a free bus gain every 100 ms -2.1 dB; with per-band complex gains (EQ/phase) 47.1% (-2.8 dB, 250-3400 Hz 41%). House (lag 0): 40.2% (-2.2 dB), bus -2.7 dB, complex 50.6% (-3.1 dB, vocal band 44.7%). Controls: the same fits with stems shifted by 41 s explain 3.7% / 4.7%, so the relation is real but partial (mastering chain, bus reverb, stereo processing, unexported tracks all fit this). Phase-agnostic third-octave NNLS: 75.4% explained for both (shifted control 40.9% / 36.8%). Sensitivity check: adding a whole sung song to the mix at -6 dB only moves Bowl 75.4% to 69.5% (0 dB: 58.9%), so these fits cannot rule out a quiet vocal; do not read them as proof.
2. **Sections where one stem plays:** House 1-11 s (only synth), House 121-128 s (only synth), Bowl 86-96 s (strings/WW/PCH): mix minus stem is a smooth EQ curve (+6..+16 dB tilt, RMS deviation 0.63 / 0.76 / 0.87 dB, max 1.7 dB) with no formant-like humps; a voice on top would leave humps at 300-3000 Hz. (Bowl 67-72 s, perc+strings, deviates 1.8 dB RMS.)
3. **Stem names/spectra** (`mvocal`): owner vocal refs have syl 0.29-0.37 and 20-26% energy above 1 kHz. Used stems: syl 0.09-0.19 (Bowl brass .11, PCH .14, Perc .14, WW .17, strings .19; House PCH .09, Perc .12, brass .14, strings .14, WW .16, guitar .17). Not discriminative on its own: Bowl brass/WW have 36%/44% above 1 kHz like instruments do. House synth.cm_01 and fx.cm_01 score syl 0.50/0.51 (which is why the House mix reads 0.45) but have 0.9% / 1.7% energy above 1 kHz and r85 301 / 258 Hz, so they cannot carry words; their modulation peaks sit at exactly 4.00x and 8.00x (synth) and 1.5x/2x/4x (fx) the beat rate = tempo-locked 16th-note gating, not speech. Bowl PCH pulses at exactly 1x/2x beat (plucked/pulsed chords; "PCH" is not documented).
4. **Cue features** (`mvocal`, cues): syl 0.01-0.32 (Bowl-based), 0.31-0.46 (House-based, from the synth gating above); energy above 1 kHz 0.2-28.7%; 3.4-8 kHz share 0-1.8% vs 3.4-6.7% in the vocal refs. Everything in the DF set is low-passed at about 3.4 kHz.
Residual risk: a vocal bounced inside an unlabeled stem (PCH, fx, synth). Have the owner listen once to menu_calm, menu_house and halftime_loop.

## Still missing for a full cinematic football set (ask the owner)
- No marching-band / drumline / snare-march material, no stadium organ or brass-band charge, no school-fight-song style tune.
- No choir hits or pads (avoided on purpose) and no real timpani rolls, braams, risers, cymbal crashes or whooshes: "hits" are the short Perc-stem strikes (about 0.2 s decay) plus reverb; risers are varispeed sweeps of strings/WW.
- All Draft Fever audio is low-passed (almost nothing above 3.4 kHz), so every cue is dark; no sparkle/air. An HF exciter in-engine or Epikton layers would help.
- Only two source pieces: the 3 menu tracks share motifs/instruments by family; no lighter "hype" track and no distinct per-quarter / per-stadium music.
- Win/loss differ mostly by source piece and pitch; no true minor/major resolution authored. Turnover/sack are generated from percussion, not designed sound-effects.
- Epikton Cinematic Action Bundle was **not** used (DF filled every role); candidates if wanted: Stomp And Claps, Classical Action Drums, Countdown Trailer.

## Tools / repro
Verbs in `Tools/AudioTool/Music*.cs` (copied from `D:\PIXFootball\Work\Audio\music_tool`; build in a copy, e.g. `dotnet build -c Release -o D:\PIXFootball\Work\Audio\music_bin`): `mrender spec.txt [--only a,b]` (renders every cue + manifest; spec at `D:\PIXFootball\Work\Audio\music_tmp\music_spec.txt`), `mcheck dir`, `mseam spec cue [--range n]`, `mloop set "stem:gain,..." min max [--from --to --step]`, `mgrid`, `mbars`, `mhits`, `menv`, `msim`, `mrange set t0 t1 [--ref stem]`, `mrecon set [--lo --hi --from --to --nolag]`, `mlag`, `mlagw`, `mfit`, `mspec`, `mvocal`, `mmod`, `mfeat`.
