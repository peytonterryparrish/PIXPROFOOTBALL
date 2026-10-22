# PIX Football QA — v0.5.0 playtest fix pass

> **Scope note (2026-10-21):** the "find ≥ 500 issues" goal below was a one-time request for v0.6.0. Later versions log only real issues they hit — no row quotas, no exhaustive galleries; verify proportionally (targeted tests while iterating, one full suite run and one autotest per release).

Goal set by the user after playtesting v0.5.0: find and fix **at least 500** bugs / issues / graphical problems / model problems / wrong stances, and deliver a checklist.

## User playtest notes (source of truth)
1. Heavy motion-capture cleanup needed.
2. Many animations missing — clean up and add them so everything flows.
3. User-play camera must be **Madden-style behind the QB** (zooms out a little after the snap, etc.). Broadcast camera only for simulated games.
4. ≥ 500 issues found, fixed and checklisted.
5. Stances are wrong; OL, DL and corners are often **facing backwards**.
6. Every position in the correct stance, facing the correct way, on all plays and circumstances.
7. Match engine must be more convincing and realistic.
8. Weird patch on the back of the helmets.
9. Models: waists / legs super thin — fix meshes, skeletons and every animation so bodies are consistent and correct.
10. More cinematic, dramatic atmosphere lighting; graphics a touch better.

## How issues are logged
Each work track keeps its own file in this folder (no two agents write the same file):

| Track | File | ID prefix | Owner area |
|---|---|---|---|
| Audit (finder) | `audit.md` | `QA-` | finds issues everywhere; assigns a track |
| Stances, facing, cameras | `stance_camera.md` | `STN-`, `CAM-` | `Scripts/Gameplay` view layer, cameras |
| Animation / mocap | `animation.md` | `ANI-` | clips, Blender retarget/cleanup, AnimKit clip choice |
| Models / skeletons | `models.md` | `MDL-` | meshes, avatars, prefabs, LODs |
| Match engine | `engine.md` | `SIM-` | `Scripts/Sim`, `Scripts/Core` AI/plays |
| Graphics / lighting | `graphics.md` | `GFX-` | materials, lighting, volumes, weather |
| Photoreal player | `photoreal.md` | `PHR-` | R1-based photoreal player (`PIX_PhotoPlayer_*`), heads / appearance, uniform maps, LODs |
| UI / flow / docs / tools | `ui_flow.md` (assembled from the helper part files `ui_flow_part_*.md`) | `UI-` | menus, HUD, settings, match flow, docs, `Tools/`, repo hygiene |
| Release checklist | `CHECKLIST.md` | - | produced by the lead at release by merging all track files; not written by tracks |

Row format (Markdown table): `| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |`
- Status: `open`, `fixed`, `wontfix` (with reason), `dup of <ID>`.
- Severity: `S1` broken / very visible, `S2` clearly wrong, `S3` polish.
- "Verified by": screenshot path, test name or autotest check that proves the fix.

Fix agents also work through `audit.md` rows assigned to their track and record the fix in their own file as `QA-123 → fixed (see ANI-045)`.
The lead merges everything into `CHECKLIST.md` at release.

Notes: a track file is created by its owner when the first row is written, so not every file in the table exists at every moment. `graphics_notes.md` is a scratch draft of the graphics track and `REFERENCE_AllPro2K8.md` is the visual reference. Evidence images (`stance_gallery/`, `tripo_looks/`, `models/`, `camera/`, any `*.png` / `*.jpg` under `Docs/QA`) are **git-ignored** (about 0.9 GB); only the Markdown logs are tracked, so cite the file name in "Verified by" and regenerate the images with the editor QA tools when needed.
