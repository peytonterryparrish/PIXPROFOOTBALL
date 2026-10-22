#!/bin/bash
# usage: render_all_new.sh
# renders every Tripo PlayerLooks model (_AssetSource/Tripo/PlayerLooks/*/pbr_model.glb) that has no back render yet
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
R="$(cd "$D/../.." && pwd)"
for d in "$R"/_AssetSource/Tripo/PlayerLooks/*/; do
  b=$(basename "$d"); [ -f "$d/pbr_model.glb" ] || continue
  case "$b" in
    E*a_*) name="${b%%a_*}_skill";; E*b_*) name="${b%%b_*}_lineman";; E6_*) name="E6_lowpoly";; *) name="$b";;
  esac
  [ -f "$R/Docs/QA/tripo_looks/${name}_back.png" ] && continue
  rot=-90; [ "$name" = "05_gritty_gameworn" ] && rot=-130; echo "== $name"; "$R/Tools/Blender/run_lookdev.sh" "$R/_AssetSource/Tripo/PlayerLooks/$b/pbr_model.glb" "$name" $rot | cut -c1-400
done
