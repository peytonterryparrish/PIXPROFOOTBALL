# v0.10 perf (frame-rate fix, commit 81d001b)

Built player, 80 s of live play (`-pixAutotestMatchOnly -pixAutotestNoV3 -pixAutotestMeasure`), night, 2560 x 1440, High, Auto upscaler, RTX 5090 Laptop.

| build | avg fps | 1 %-low | GPU | main thread |
|---|---|---|---|---|
| v0.9.0 | 81.7 | 50.4 | 12.8 ms | 5.3 ms |
| perf fix, idle machine | 145.3 | 82.3 | 6.3 ms | 5.0 ms |
| perf fix, other Unity / Blender jobs running | 88-119 | 22-30 | 6.4-7.7 ms | 6.9-7.5 ms |

Fixes: GPU (batched) skinning on (`ProjectSettings` `meshDeformation` was 0 = CPU skinning), DLSS / FSR 2 actually enabled (`QualityTiers.ApplyRenderScale` no longer clears the camera's dynamic resolution; `UpscalerControl.Apply` owns the flags), no per-frame arrays in `PlayerLod`. Look unchanged. Full profile: `Docs/QA/v091/perf.md`.

For v0.10 tracks: measure only on an idle machine (concurrent Unity batch jobs halve the 1 %-low). Remaining: main-thread bursts of 20-70 ms after kickoffs and tackles (post-play), visible in the `-pixAutotestMeasure` spike log.
