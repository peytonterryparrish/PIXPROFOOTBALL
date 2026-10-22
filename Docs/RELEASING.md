# Releasing a version

## Decision needed before the first public release: licensed art (QA-1015)

`Assets/_Licensed/` and `_AssetSource/` hold art, mocap, music and fonts that are licensed to the project owner and are git-ignored on purpose. The build in step 5 **bakes that art into the Windows build**, and step 8 attaches the zip to a GitHub release on a **public** repository. Anyone can then download and extract textures, music WAVs, mocap and fonts that the licence may not allow to be redistributed.

Do not run step 8 until one of these is confirmed and written down here:

- [ ] the licence terms of each source pack (Draft Fever, BoNES / Motus mocap, music, fonts, Tripo / other generated models) allow redistribution inside a game build, or
- [ ] the release is published privately (`gh release create ... --draft`, or a private repository / private distribution link), or
- [ ] the shipped data is changed so it cannot be extracted (stripped / encrypted bundles), or the licensed parts are replaced.

Decision and date: **2026-10-20/21 — the project owner stated they own all Draft Fever Bowl assets (art, mocap provided for that project, music they composed, UI kit, logos) and that builds may ship them; the R1 photoreal body model may be used commercially without the original athlete's likeness.** Fonts in use (Saira Condensed, Barlow Semi Condensed, Material Symbols) are OFL / Apache. Tripo models are generated on the owner's account. Public pre-release builds on GitHub are approved by the owner (releases v0.1.0–v0.5.0). **2026-10-21 owner confirmation: "I own everything I gave you"** — covers every asset the owner supplied (Draft Fever archive incl. mocap, UI export, college/conference/bowl/pro logo packs, music, the R1 model and the posed reference models). Redistribution inside game builds is approved.

## Tools and paths

All scripts find the Unity editor through the `UNITY_PATH` environment variable and fall back to `C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe`; Blender through `BLENDER_PATH` with the fallback `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`. In the commands below `$Unity` means:

```powershell
$Unity = if ($env:UNITY_PATH) { $env:UNITY_PATH } else { "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" }
```

`Tools\unity.ps1 -Method <Class.Method> -Log <name>.log [-NoGraphics] [-NoQuit] [-Extra '<args>']` runs one batch method, waits for another editor on the project to close first, writes `Logs\<name>.log` and exits with Unity's exit code (use `-NoQuit` for `-runTests`). Run PowerShell scripts with `powershell -ExecutionPolicy Bypass -File Tools\unity.ps1 ...` where script execution is disabled. Only one Unity editor can have the project open: close the editor first.

## Preflight checklist (generated data the build depends on)

One-session shortcut (v0.6.0): `powershell -ExecutionPolicy Bypass -File ToolsNity.ps1 -Method PixFootball.EditorTools.ReleasePreflight.Run -Log preflight.log` runs steps 2 - 4 below (ArtLibrary and the tables derived from it, the controller, the clip facing table, player mesh LODs, v6 art, scenes `Build All`). `ReleasePreflight.DataOnly` skips the scenes, `ScenesOnly` only builds them. It does not clean clips (step 1).

The committed scenes and the game read generated, git-ignored assets. Rebuild whatever changed since the last release, in this order, then run the tests. Menu path in brackets.

1. [ ] **Clean clips** after any mocap / clip change: `PIX Football > Animation > Clean All Clips` (`CleanBatch`, writes `Animations/Clean`).
2. [ ] **ArtLibrary** (`PIX Football > Assets > Build ArtLibrary`): rewrites `Assets/_Licensed/ArtLibrary.asset`, copies it to `Assets/_Licensed/Resources/ArtLibrary.asset` and regenerates `Docs/ArtLibraryReport.md`. Check the report for new MISSING keys.
3. [ ] **Tables derived from the library**: `Build Clip Timing`, `Build Contact Table` (and `Build Move Takes` if the spin takes changed), `Build Clip Facing` (`ClipFacingTable`, needed so linemen and corners face the right way), `Build Player Mesh LODs` / `Import Fixed Body Meshes` (`PixMeshLods.asset`), all under `PIX Football > Assets`.
4. [ ] **V6 art** (`PIX Football > V6 > Build All V6 Art`): number / letter atlases, weather sprites, `PIX/Lit` materials. Then `PIX Football > Scenes > Build All` if the scenes or stadium materials changed (the committed scenes are generated; see `Docs/ARCHITECTURE.md`, golden rule 2).
5. [ ] `git status`: no unexpected generated or licensed files staged (`Docs/QA` renders are git-ignored; keep it that way).

## Release flow

1. Update `CHANGELOG.md` (Keep a Changelog: Added / Changed / Fixed / Removed / Known Issues; move `[Unreleased]` into the new version, set the date, update the compare links at the bottom).
2. Write player-facing notes in `PatchNotes/vX.Y.Z.md` (headline features, gameplay, graphics, fixes, known issues, what's next). `bundleVersion` in `ProjectSettings` must match (step 4 sets it).
3. Run the EditMode tests (must be green):
   `& $Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml -logFile Logs/editmode.log`
4. Build (sets `bundleVersion`):
   `& $Unity -batchmode -projectPath . -executeMethod PixFootball.EditorTools.BuildScript.BuildWindows -pixVersion X.Y.Z -quit -logFile Logs/build.log`
   While it runs, `BuildScript` swaps `Assets/_Licensed/Resources/ArtLibrary.asset` for a **slim** copy (only the whitelisted prefabs, clips and audio of the match) and restores the full library afterwards in a `finally` block (backup in `Library/PIXBackup`). If the editor is killed or crashes mid-build the slim library stays in `Assets/_Licensed/Resources` and the backup stays in `Library/PIXBackup`: the next `BuildWindows` run restores it, or copy `Library/PIXBackup/ArtLibrary.asset` (+ `.meta`) back by hand, or simply re-run `Build ArtLibrary`. Never commit or playtest in the editor with the slim library in place.
5. Zip the build folder. Builds go to `D:/PIXFootball/Builds/PIXFootball_vX.Y.Z_Windows` when a D: drive exists (override with `PIX_BUILD_DIR`), otherwise `Builds/`. Zip to `D:/PIXFootball/Builds/PIXFootball_vX.Y.Z_Windows.zip`.
6. Smoke-test the zip on the player: run `PIXFootball.exe -pixAutotest` (see `Logs/autotest`).
7. Commit, `git tag -a vX.Y.Z -m "vX.Y.Z"`, `git push && git push origin vX.Y.Z`. Tags `v0.1.0` .. `v0.5.0` exist on the remote; `0.6.0` is built and notes are written, tag it after review.
8. **Only after the licensing decision above is recorded:** `gh release create vX.Y.Z Builds/PIXFootball_vX.Y.Z_Windows.zip --title "PIX Football vX.Y.Z - <name>" --notes-file PatchNotes/vX.Y.Z.md --prerelease` (pre-release while < 1.0.0; add `--draft` to keep it private until the decision is made).

Builds larger than GitHub's 2 GB per-asset limit are split with `split -b 1900m` and attached as `.zip.001`, `.zip.002`, ...

## Build settings that matter

- Scripting backend: not set in `ProjectSettings` (Unity default, Mono, default stripping). IL2CPP and a stripping level have not been evaluated; record the decision here and in `Docs/ARCHITECTURE.md` ("Time steps and build settings") if it changes.
- The build ships `MainMenu`, `Stadium_Pro` (Free Roam sandbox) and `Match_Pro`; the two stadium scenes each carry a full copy of the stadium (backlog: share it through a prefab or drop Free Roam from release builds).
