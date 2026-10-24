# v0.11.0 AUDIO + CROWD track log

Owner request: cinematic instrumental football music from the Draft Fever OST instead of the owner's sung songs (menus and matches), crowd noise and crowd animation, and a full on-field sound set. Priority order set by the coordinator: music, on-field SFX for every event, crowd sound reactions, crowd animation (simple is fine), one EditMode run at the end.

Nothing was listened to (no audio device in the tooling): every selection is by file name, measured features and the numbers below. Please give the music loops, the crowd beds and the hit sounds one listen before shipping.

## What changed
| Area | Before (v0.10) | Now |
|---|---|---|
| Menu music | 5 of the owner's sung songs (`music_pp_*`) from `PixRuntimeAssets.menuMusic` | `MusicDirector` playlist: `menu_calm`, `menu_drive`, `menu_house`, equal-power crossfade 3.5 s, random start. The sung songs are still imported but nothing plays them (`RuntimeAssetsBuilder` no longer lists them; slim ArtLibrary already excluded music). |
| Match music | none | pre-game: `pregame_intro` then `pregame_bed`; `match_under_low` between plays (pushed down in live play); `match_under_tense` + `st_twominute` in the two-minute drill; `st_halftime` then `halftime_loop`; stingers: touchdown, field goal, turnover, final win / loss. Ducking: UI click (-22 %), stingers (-50 %), big hits (-35 %). |
| On-field SFX | whistle, tackle, catch, kick, 1 block sound: 8 event types | 32 sim event types + every choreographed contact + every planted foot, 3D spatialised, 38 SFX groups (272 clips) |
| Crowd sound | one loop + 5 cheers, 1 table, no groans/boos clips | 10 looping layers (3 murmur levels, tension, "noise", claps, cheer, boo, rain, wind) + 11 reaction groups, home / away bias |
| Crowd animation | baked billboards, 2 poses swapped per 18 m chunk | GPU vertex rewrite per fan (compute shader), idle sway for everyone, home / visitor sections cheer, stand, hop, slump differently |

## Files
* New code: `Gameplay/Audio/{AudioHub,MasterLimiter,MusicDirector,CrowdMix,AudioProbe}.cs`, `Art/PixAudioBank.cs`, `Editor/Audio/AudioBankBuilder.cs`, `Presentation/Crowd/CrowdAnimator.cs`, `Resources/PixCrowdSway.compute`, `Tests/EditMode/Gameplay/AudioTests.cs`.
* Rewritten: `Gameplay/Match/MatchAudio.cs` (+ `MatchAudio.Events.cs`), `Presentation/World/CrowdReaction.cs` (additions).
* One-line hooks in other files: `MatchDirector.cs` (`Audio.OnContact(c)` in `OnContactImpact`, `Audio.OnOutcome(o)` in `React`), `LookDirector.cs` (`MatchAudio.FootPlant` where a foot plants), `MainMenuController.cs` (`StartMusic` -> `MusicDirector.PlayMenu`, fade out on load), `UiAudio.cs` (music duck), `RuntimeAssetsBuilder.cs` (no sung songs in `menuMusic`).
* Assets (git-ignored with the rest of `_Licensed`): `Assets/_Licensed/PIX/Audio/{Music,Stingers,Sfx,Crowd}`, `Assets/_Licensed/Resources/PixAudioBank.asset` (built by `PIX Football > Audio > Build Audio Bank`, batch `PixFootball.EditorTools.AudioBankBuilder.Build`). **Run the bank build before a release build** (the clips ship through that asset in Resources).
* Offline tool (committed): `Tools/AudioTool` (WAV IO, BS.1770 LUFS, spectral / vocal-ness proxies, loop maker; verbs `Music*.cs`, `Sfx.cs`, `Crowd.cs`, specs `SfxSpec.csv`, `CrowdSpec.txt`). Sub-logs: `audio_music.md`, `audio_sfx.md`, `audio_crowd.md`.
* QA flags: `-pixAudioProbe` (CPU-vs-CPU game + report, run with `WatchRun.Run`), `-pixAudioSolo music|crowd|sfx`, `-pixAudioLog file`, `-pixNoCrowdAnim`.

## Music cue map
See `audio_music.md` for the exact source ranges and the loop-seam numbers. Summary of use:

| Cue | Where it plays |
|---|---|
| menu_calm / menu_drive / menu_house | main menu playlist |
| pregame_intro (31 s) then pregame_bed | match start, coin toss and line-ups (bed replaced by the underscore at the first play call) |
| match_under_low | between plays, under the crowd; ducked to 45 % while a play is live |
| match_under_tense + st_twominute | last two minutes of Q2 / Q4 with a one-score game |
| st_touchdown | touchdown event (either team, 2 dB lower for a visitor score) |
| st_fieldgoal | field goal good |
| st_turnover | interception / fumble lost / turnover on downs |
| st_halftime + halftime_loop | halftime |
| st_final_win / st_final_loss | final whistle (home result) |
| st_sack_hit, st_firstdown | produced but not wired (too frequent, the SFX and crowd already mark them) |

Vocal check (details in `audio_music.md`): only the named instrument stems of the Draft Fever Bowl and Draft House OSTs were used (brass, strings, woodwind, PCH, percussion, guitar, synth, fx), never the full mixes. The stem sums explain only 36-51 % of the mixes, so "no vocal in the mixes" is NOT proven; the cues are about 90-95 % likely clean. **Residual risk: a vocal sitting inside an unlabelled stem (PCH, fx, synth). Owner: please listen once to menu_calm, menu_house, halftime_loop.**

## Measurements (CPU-vs-CPU watch game through `GameSession.StartMatch`, 2-minute quarters, 2x speed, 1920x1080 editor; probe `-pixAudioProbe`, raw reports `D:\PIXFootball\Work\Audio\out\`)
**SFX events per play against the target list** (final run, 22 plays, 234 sim events): every sim event type that is meant to be heard started a sound within 1 s, 100 % each (BlockLoss 47/47, Shed 47/47, Tackle 18/18, Catch 8/8, Throw 6/6, Snap 22/22, Kick 7/7, PileOn 10/10, Pancake 3/3, Touchdown 2/2 ...). v0.10 (B-051 / B-102): 8 event types made sound, 33 plays gave whistle 41, tackle 59, catch 19, ball hit 19, kick 7, cheer 5. Now 34.8 sounds per play on average (min 6, max 143), of which about 60 % are foot plants; 771 effects started, 684 more skipped by the distance / spacing limits (steps mostly), at most 13 of 28 voices busy. 22 choreographed contacts played their pad crack by weight (light / medium / heavy / big) + helmet clack + grunt + ground thump. Untested by the probe (no such event in the seed): safety, fair catch, interception, blocked kick.

**Loudness of the final mix** (K-weighted, measured on the master after the limiter; make-up gain +6 dB before the limiter):
| | Before make-up (v0.11 first build) | Final |
|---|---|---|
| Whole mix, integrated | -25.6 LUFS | **-19.7 LUFS** (momentary p10 / p50 / p90 / max -22.9 / -21.0 / -16.9 / -11.3) |
| Sample peak | -7.4 dBFS | +1.3 dBFS before the limiter, **-1.0 dBFS after** (limiter touched 73 samples in 4 minutes, so no clipping) |
| Music alone (`-pixAudioSolo music`, menu fade-out + match) | -29.8 LUFS | about -23.8 LUFS (menu loops are mastered -22, match underscore -30, live play pushes it 7 dB down) |
| Crowd alone | -28.9 LUFS | about -22.9 LUFS |
| SFX alone (sparse, gated) | -26.5 LUFS, peaks -12 dBFS | about -20.5 LUFS, hits peak near the ceiling about 9 LU above the crowd bed |
Targets used: music under crowd under hits; whole mix near -20 LUFS integrated with -1 dBFS ceiling (typical console football is -18 to -22).

**Cost:** voice pool 28 + 10 crowd layers x 2 + 6 crowd shots + 3 music = at most about 40 real voices of the 64 configured; `AudioHub` / `CrowdMix` / `MusicDirector` Update work is a few dictionary look-ups per frame. Frame time while pre-snap + live in the editor, alternating runs (other editors were running on the machine, so run-to-run noise is about 1 ms): GPU crowd animation on median 7.98 / 7.80 ms, animation off (old per-chunk pose swap) 8.69 / 8.34 ms, no regression. A first version (every sector at 60 Hz) looked 0.5-1 ms slower and was throttled: each sector is rewritten about 10 Hz when the crowd is quiet and 30 Hz while a section reacts; 24 sector meshes, 25,817 fans, 8 compute dispatches at most per frame, vertex stride 32 B.

## Crowd animation: what it does and what it does not
* Each fan is rewritten on the GPU (`Resources/PixCrowdSway.compute`): idle sway and breathing with a per-fan phase for everyone; per section (home = most of the bowl, visitors = the +z end zone and its corners): arms-up pose swap (atlas row below), hopping, standing up (quad grows 16 %), slumping, with a wave round the bowl. Driven by `CrowdMix.Reacted` (touchdown, turnover, field goal, sack, big gain, first down, big hit ...) with the home bias: a home touchdown raises home fans and slumps visitors, a visitor touchdown the other way.
* Needs baked fan data (the chunk meshes are static-batched at runtime and cannot be read back): `PIX Football > Assets > Bake Crowd Fan Data` (`CrowdBuilder.Fans.cs`; `AudioBankBuilder.Build` runs it); writes `Assets/_Licensed/Resources/PixCrowdFans*.bytes` (git-ignored, 1.6 / 2.0 MB). Without it, or without compute shaders, the old per-chunk pose swap runs.
* **Looked at once on the broadcast camera** (`crowd_idle.png`, `crowd_after_home_score.png`): the sideline stands are dark and the fans are seen edge-on from the side as thin dark slivers (the billboards face the field), so the sway and the arms-up swap are NOT visible on this camera; only the pose changes of the end-zone sections could read. That is the same picture as v0.10 (not a regression), and is a lighting / viewing-angle issue of the billboards for GFX, not of the animation. I could not verify the cheer wave by eye.
* MoCap Online "Crowd Stadium 01" (coordinator note): NOT used, over this track's budget. v0.12 item: bake its sit / stand / cheer cycles into the atlas as extra poses (the compute shader already takes the pose row from a per-fan state) or an instanced 3D crowd near the broadcast camera, where the billboards are seen edge-on.

## Audio rows of BUGS.md
| ID | Result |
|---|---|
| B-051 trench and misc silent | fixed: block win / loss, shed, broken tackle, QB hit / hurry, handoff, pitch, throw, deflection, drop, incomplete, snap, pile-on, jam, fumble, recovery, spike, kneel, penalty flag, carrier cuts all play (table above) |
| B-102 single layer | fixed: 10 crowd layers + 11 reaction groups, 61 bank groups, 355 clips |
| B-103 block sound on tackle clips | fixed: groups are separate (hit_* / block_thud / catch_* / bounce_* ...), catches only draw from catch groups |
| B-104 no footsteps | fixed: `MatchAudio.FootPlant` from `LookDirector.FootSteps` (real foot plants), run / walk x dry / wet / snow, only near the camera |
| B-105 snap and cadence silent | snap fixed (`snap_slap` + crowd hush); the QB cadence VOICE is not done: no generic "hut / set" voice exists in any owner pack (only soldier barks); known issue |
| B-106 roar never faded | fixed: reactions play their own clip to its end on a 6-voice pool (no in-place restart), beds duck under a reaction and recover over about 1.2 s, roar_td is 11-15 s with its tail |
| B-107 crowd reaction gaps | fixed: sack, incomplete, penalty, punt, kickoff, near miss, big hit, first down, field goal good / missed, turnover, hush and boo all have cues from sim events and the play outcome |
| B-108 hit pool mixes families | fixed (see B-103) |
| B-109 replays silent | NOT fixed: replay playback (WATCH) still has no hit / catch / kick sound; known issue, the events are available through `MatchAudio.OnEvent` |

## Known issues / gaps
* Nothing was listened to; the owner should check: menu_calm, menu_house, halftime_loop (vocal residual risk, see above), the crowd beds (soccer-stadium recordings, no chants found by the screens but unproven), `helmet_clack` (wood-block placeholders), `slide_turf` (sand), `chain_clank`.
* Not wired: `slide_turf`, `chain_clank` (first-down measure), `ball_whirr`, `st_sack_hit`, `st_firstdown`, `bed_noise` rhythm (no stomp source), crowd boos from a visitor-sideline view.
* Replays and the free-roam stadium (`StadiumAudio`) keep their old sound.
* Crowd sound sections are by event only; attendance is a seeded 82-96 % (no per-team stadium data in the pro league).
* A release build needs `PIX Football > Audio > Build Audio Bank` run once beforehand (it also bakes the crowd fan data); the build then carries 355 clips through `Resources/PixAudioBank.asset`. The owner's sung songs stay imported but are not referenced by anything in a build.
* `Tools/AudioTool` is in the repo; the audio sources it reads are not (git-ignored `_Licensed` and `_AssetSource`).

## EditMode
Full run in the private copy (`-testCategory "!Slow"`): 1323 tests, 1314 passed, 9 failed. Eight failures are not audio (sim / carry / league: BlockingTests.PullingLinemen..., CareerDraftTests, CarryStyleTests.YardsPerCarry..., LeagueEngineFixTests x2, QuickSimTests.PlaysPerTeam, SimAbilityTests.WreckingBall..., MatchFlowTests.FiveMinuteQuarters...). The ninth was my own `AudioTests.EverySimEventThatShouldBeHeardStartsASound` (edit-mode `Awake` not run); fixed (`[ExecuteAlways]` on the hub / music director, limits reset between events), `AudioTests` 3 / 3 pass in a filtered re-run.
