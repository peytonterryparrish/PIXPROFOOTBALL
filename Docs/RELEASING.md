# Releasing a version

1. Update `CHANGELOG.md` (Keep a Changelog: Added / Changed / Fixed / Removed / Known Issues).
2. Write player-facing notes in `PatchNotes/vX.Y.Z.md` (headline features, gameplay, graphics, fixes, known issues, what's next).
3. Run the EditMode tests (must be green):
   `Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml`
4. Build (sets `bundleVersion`):
   `Unity.exe -batchmode -projectPath . -executeMethod PixFootball.EditorTools.BuildScript.BuildWindows -pixVersion X.Y.Z -quit -logFile Logs/build.log`
5. Zip `Builds/PIXFootball_vX.Y.Z_Windows/` → `Builds/PIXFootball_vX.Y.Z_Windows.zip`.
6. Commit, `git tag -a vX.Y.Z -m "vX.Y.Z"`, `git push && git push origin vX.Y.Z`.
7. `gh release create vX.Y.Z Builds/PIXFootball_vX.Y.Z_Windows.zip --title "PIX Football vX.Y.Z – <name>" --notes-file PatchNotes/vX.Y.Z.md --prerelease` (pre-release while < 1.0.0).

Licensed art (`Assets/_Licensed`, `_AssetSource`) is local-only and never committed; it is baked into the build.
Builds larger than GitHub's 2 GB per-asset limit are split with `split -b 1900m` and attached as `.zip.001`, `.zip.002`, ...
