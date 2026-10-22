# usage: Tools/Tripo/fetch.ps1 -N <id e.g. 01 or E3a>   (needs $env:TRIPO_API_KEY) - polls the Tripo task saved by submit.ps1 and downloads the finished files next to task_id.txt
param([string]$N)
$ErrorActionPreference="Stop"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path   # repo root, independent of where the repo is cloned
$dir = (Get-ChildItem "$root\_AssetSource\Tripo\PlayerLooks" -Directory | Where-Object { $_.Name -like "$N`_*" }).FullName
$id = (Get-Content "$dir\task_id.txt").Trim()
$h=@{Authorization="Bearer $env:TRIPO_API_KEY"}
$r = Invoke-RestMethod -Uri "https://api.tripo3d.ai/v2/openapi/task/$id" -Headers $h
$st = $r.data.status
"$N status=$st progress=$($r.data.progress)"
if ($st -eq "success") {
  $r.data | ConvertTo-Json -Depth 8 | Set-Content "$dir\task.json"
  $out = $r.data.output
  foreach ($p in $out.PSObject.Properties) {
    $v = $p.Value
    if ($v -is [string] -and $v.StartsWith("http")) {
      $ext = "bin"
      if ($v -match "\.(glb|fbx|webp|png|jpg|jpeg|obj|stl|usdz)\?") { $ext = $Matches[1] }
      $f = "$dir\$($p.Name).$ext"
      Invoke-WebRequest -Uri $v -OutFile $f
      "saved $f ($((Get-Item $f).Length) bytes)"
    }
  }
}
elseif ($st -in @("failed","cancelled","banned","expired")) { $r.data | ConvertTo-Json -Depth 8 }
