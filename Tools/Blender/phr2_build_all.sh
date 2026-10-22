#!/bin/bash
# builds the 15 body FBX (3 sizes x 5 bases) into Assets/_Licensed/PhotorealPlayer/Staging_v07/Meshes
cd "$(dirname "$0")/../.."
BL="${BLENDER:-C:/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
for base in Std Stocky Athletic Tall Runner; do
  for size in Small Medium Large; do
    "$BL" -b --python Tools/Blender/phr2_build.py -- $size $base 2>&1 | grep -E "^PHR (TOTAL|exported|base)|Traceback|Error:" 
  done
done
