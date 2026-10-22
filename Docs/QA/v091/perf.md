# v0.9.1 "Frame Rate": where the v0.9.0 frame time went

Machine: RTX 5090 Laptop, D3D12, 2560 x 1440 windowed, tier High, night / clear (the autotest default match). Logs: `D:\PIXFootball\Logs\perf091_*`.

## Profile of the v0.9.0 player

- Live play (`-pixAutotestMeasure`, 80 s): 81.7 avg / 50.4 1 %-low, GPU 12.8 ms, main thread 5.3 ms, render thread 2.6 ms. GPU-bound; the "CPU 12.5 ms" in the v0.9.0 log was `cpuFrameTime`, which includes waiting for the GPU.
- Frozen pre-snap sweep (`-pixAutotestPerf`): GPU 19.0 ms. Biggest switches: all spot lights off -4.4 ms, all light shadows off -3.2 ms (spot shadows alone -1.2), players hidden -3.5 ms, volumetric fog -1.8 ms; SSAO / SSR / contact shadows / motion blur / SSGI / bloom / crowd within noise; DoF and cloud-layer switches read slower (thermal drift, about 1 ms run to run).
- Native (`-pixUpscaler 0`) was only 1 ms slower than "Auto": DLSS was not running.
- Editor GPU breakdown (new `-pixGpuBreakdown`, native 1440 p): ObjectsMotionVector 5.7 ms, shadow maps 2.8, GBuffer 2.7, depth prepass 2.6, forward transparent 1.8, deferred lighting 1.7, volumetric lighting 1.5, post 0.9. Census: 291 visible skinned renderers (0.9 M triangles) in the match; stadium / other movers cost nothing measurable ("others hidden" within noise).

## Causes and fixes

1. `ProjectSettings`: `gpuSkinning 0`, `meshDeformation 0` (CPU skinning since project setup). Switched to GPU (batched). Live GPU 12.8 -> 6.3 ms.
2. `QualityTiers.ApplyRenderScale` cleared `allowDynamicResolution` on every camera at 100 % render scale, after or before `UpscalerControl.Apply` set it depending on scene-load order; the camera reported `allowDynRes=False allowDLSS=True`. Now only `UpscalerControl.Apply` sets the flags; Auto renders 1792 x 1008 under DLSS Quality. On its own this saved only about 1.3 ms of GPU (the cost was geometry / skinning, not pixels).
3. `PlayerLod` per-frame array allocations removed.

## Result (v0.9.1 player, same flags)

| run | avg | 5 % low | 1 % low | GPU | main thread |
|---|---|---|---|---|---|
| v0.9.0 (16:20) | 81.7 | 56.7 | 50.4 | 12.8 ms | 5.3 ms |
| v0.9.1, idle machine (17:40) | 145.3 | 115.1 | 82.3 | 6.3 ms | 5.0 ms |
| v0.9.1 / v0.9.0 back to back while other sessions' Unity jobs ran (17:33) | 118.8 / 74.5 | 69.0 / 47.8 | 30.4 / 40.1 | 6.4 / 13.9 ms | 6.9 / 6.4 ms |
| v0.9.1 with 4 Unity batch jobs + Blender + dotnet test running (17:48) | 88.4 | 37.8 | 21.9 | 7.7 ms | 7.5 ms |

The spike log (`-pixAutotestMeasure`) shows the slow frames are main-thread bursts (20-70 ms) at the kickoff return and in the post-play right after a tackle, with no LOD switches and no garbage collection in those frames. On a quiet machine they stay under the 1 %; with the CPU shared they dominate the 1 %-low. Next lever if they show up in play: profile the post-play / pile-up path on the main thread (contact, help-up, officials walk-in) in a development build.

## Not changed (look kept)

Cascades, PCSS, floodlight shadows (2 on High), volumetric fog, DoF, SSR / SSAO, LOD edges 400 / 200 px: with the GPU at about 6.3 ms there is headroom, so the v0.9 look stays as shipped.
