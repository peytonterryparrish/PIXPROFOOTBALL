# Submits remaining era models as concurrency slots free up, and downloads finished ones.
# usage: Tools/Tripo/queue.ps1   (needs $env:TRIPO_API_KEY; loops until every listed model is downloaded or failed)
$root = (Resolve-Path "$PSScriptRoot\..\..").Path   # repo root, independent of where the repo is cloned
$pending = @("E1b","E2a","E2b","E3a","E3b","E4a","E4b","E5a","E5b","E6")
$all = @("01","02","03","04","05","06","07","08","09","10","E1a") + $pending
$h=@{Authorization="Bearer $env:TRIPO_API_KEY"}
$done=@{}
while ($true) {
  # fetch finished
  foreach ($n in $all) {
    if ($done[$n]) { continue }
    $d = Get-ChildItem "$root\_AssetSource\Tripo\PlayerLooks" -Directory | Where-Object { $_.Name -like "$n`_*" }
    if (-not $d -or -not (Test-Path "$($d.FullName)\task_id.txt")) { continue }
    if (Test-Path "$($d.FullName)\pbr_model.glb") { $done[$n]=$true; continue }
    $o = & "$root\Tools\Tripo\fetch.ps1" -N $n
    "$(Get-Date -f HH:mm:ss) $o"
    if ($o -match "saved") { $done[$n]=$true }
    if ($o -match "status=(failed|cancelled|banned|expired)") { $done[$n]=$true; "FAILED $n" }
  }
  # submit pending
  $left=@()
  foreach ($n in $pending) {
    $d = Get-ChildItem "$root\_AssetSource\Tripo\PlayerLooks" -Directory | Where-Object { $_.Name -like "$n`_*" }
    if ($d -and (Test-Path "$($d.FullName)\task_id.txt")) { continue }
    try {
      if ($n -eq "E6") { & "$root\Tools\Tripo\submit.ps1" -N $n -Quality standard -FaceLimit 3000 } else { & "$root\Tools\Tripo\submit.ps1" -N $n }
    } catch { $left += $n; break }
  }
  $unfinished = $all | Where-Object { -not $done[$_] }
  if ($unfinished.Count -eq 0) { "ALL DONE"; break }
  Start-Sleep 20
}
