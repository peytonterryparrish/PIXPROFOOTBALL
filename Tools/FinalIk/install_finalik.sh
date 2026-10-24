#!/bin/bash
# v0.11.0 MOTION: installs the owner-licensed Final IK (RootMotion) into Assets/_Licensed/RootMotion (git-ignored: the Asset Store EULA forbids redistributing its source, it must never reach the public repo).
# Usage: Tools/FinalIk/install_finalik.sh "<path to Final IK.unitypackage>"
# Unity's own importer would do the same, but this keeps GUIDs, skips the demos / third-party integrations / baker, and works without opening the (shared) project.
set -e
PKG="$1"; [ -f "$PKG" ] || { echo "usage: $0 <Final IK.unitypackage>"; exit 1; }
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; DEST="$ROOT/Assets/_Licensed/RootMotion"
git -C "$ROOT" check-ignore -q Assets/_Licensed/RootMotion/x || { echo "Assets/_Licensed is not git-ignored: refusing to install"; exit 2; }
TMP="$(mktemp -d)"; tar -xzf "$PKG" -C "$TMP"
mkdir -p "$DEST"
for d in "$TMP"/*/; do
  p=$(head -1 "$d/pathname" | tr -d '\r')
  case "$p" in *_DEMOS*|*"Shared Demo"*|*_Integration*|*/Baker/*|*/Baker|*.unitypackage*) continue;; esac
  rel=${p#Assets/Plugins/RootMotion/}; [ "$rel" = "$p" ] && continue
  dst="$DEST/$rel"
  if [ -f "$d/asset" ]; then mkdir -p "$(dirname "$dst")"; cp "$d/asset" "$dst"; [ -f "$d/asset.meta" ] && cp "$d/asset.meta" "$dst.meta"; else mkdir -p "$dst"; [ -f "$d/asset.meta" ] && cp "$d/asset.meta" "$dst.meta"; fi
done
# the assembly definitions Final IK ships in "Import Assembly Definitions.unitypackage" (runtime: RootMotion, editor: RootMotionEditor)
for d in "$TMP"/*/; do
  p=$(head -1 "$d/pathname" | tr -d '\r'); [ "$p" = "Assets/Plugins/RootMotion/Import Assembly Definitions.unitypackage" ] || continue
  A="$(mktemp -d)"; tar -xzf "$d/asset" -C "$A"
  for e in "$A"/*/; do q=$(head -1 "$e/pathname" | tr -d '\r'); r=${q#Assets/Plugins/RootMotion/}; mkdir -p "$(dirname "$DEST/$r")"; cp "$e/asset" "$DEST/$r"; cp "$e/asset.meta" "$DEST/$r.meta"; done
done
rm -rf "$TMP"
echo "Final IK installed in $DEST (git-ignored). Open the project once; then run PixFootball.EditorTools.FinalIkInstaller.Run to install the bridge."
