#!/bin/bash
# usage: run_lookdev.sh <model> <name> [rot] [height]
# Renders front / three-quarter / back look-dev images of a GLB/FBX into Docs/QA/tripo_looks/<name>_*.png.
# Blender: $BLENDER_PATH (a bash-style or Windows path), else the default Blender 5.2 install.
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
R="$(cd "$D/../.." && pwd)"
B="${BLENDER_PATH:-/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
[ -f "$B" ] || { echo "Blender not found: $B (set BLENDER_PATH)" >&2; exit 1; }
"$B" -b --python "$R/Tools/Blender/render_lookdev.py" -- "$1" "$R/Docs/QA/tripo_looks/$2" "${4:-1.88}" EEVEE "${3:--90}" 2>&1 | grep -E "STATS|Error|Traceback"
