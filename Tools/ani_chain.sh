#!/bin/bash
# animation track: rebuild -> sequence check -> stance gallery (each step waits for / retries around the other agents' editor sessions)
cd "$(dirname "$0")/.."
Tools/unity_retry.sh PixFootball.EditorTools.AnimRebuildAll.Run ani_all5 40 > Logs/ani_all5.retry.txt 2>&1
rm -f Docs/QA/anim_sequence.md
for i in 1 2 3 4 5 6 7 8; do
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Method PixFootball.EditorTools.AnimSequenceCheck.Run -Log ani_seq3.log -NoQuit -Extra "-pixAnimSeq" > Logs/ani_seq3.runner.txt 2>&1
  if [ -f Docs/QA/anim_sequence.md ]; then break; fi
  sleep 90
done
rm -rf Docs/QA/stance_gallery/anim_v08
for i in 1 2 3 4 5 6; do
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Method PixFootball.EditorTools.StanceGallery.Run -Log ani_gallery1.log -NoQuit -Extra "-pixGallery -pixGalleryMode formations -pixGalleryOut Docs/QA/stance_gallery/anim_v08 -pixGalleryShots 1 -pixGalleryOnly 4_3_Base_vs_I_Form_plusZ" > Logs/ani_gallery1.runner.txt 2>&1
  if ls Docs/QA/stance_gallery/anim_v08/*.png > /dev/null 2>&1; then break; fi
  sleep 90
done
echo CHAIN-DONE > Logs/ani_chain.done
