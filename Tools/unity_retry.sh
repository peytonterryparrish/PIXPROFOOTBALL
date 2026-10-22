#!/bin/bash
# usage: unity_retry.sh Class.Method logname [maxTries]  - runs the batch method, retries while somebody else's script error blocks compilation
cd "$(dirname "$0")/.."
M="$1"; L="$2"; N="${3:-30}"
for i in $(seq 1 $N); do
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity_wait_run.ps1 -Method "$M" -Log "$L" > "Logs/$L.runner.txt" 2>&1
  if grep -q "UNITY exit 0" "Logs/$L.runner.txt"; then echo "OK try $i"; exit 0; fi
  if grep -q "error CS" "Logs/$L.log" 2>/dev/null; then echo "compile error (try $i): $(grep 'error CS' Logs/$L.log | sort -u | head -2)"; sleep 90; else echo "failed without compile error"; exit 1; fi
done
exit 2
