# usage: Tools/Tripo/submit.ps1 -N <id> [-Model v3.1-20260211] [-Quality detailed|standard] [-FaceLimit 3000]   (needs $env:TRIPO_API_KEY) - prompts come from playerlooks_prompts.json / era_prompts.json; output goes to _AssetSource/Tripo/PlayerLooks (git-ignored)
param([string]$N, [string]$Model="v3.1-20260211", [string]$Quality="detailed", [int]$FaceLimit=0)
$ErrorActionPreference="Stop"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path   # repo root, independent of where the repo is cloned
$items = @(); foreach($f in "playerlooks_prompts.json","era_prompts.json"){ (Get-Content "$root\Tools\Tripo\$f" -Raw | ConvertFrom-Json) | ForEach-Object { $items += $_ } }
$it = $items | Where-Object { $_.n -eq $N }
$dir = "$root\_AssetSource\Tripo\PlayerLooks\$($it.n)_$($it.slug)"
New-Item -ItemType Directory -Force $dir | Out-Null
$body = @{ type="text_to_model"; prompt=$it.prompt; negative_prompt="text, letters, real team logos, NFL logo, deformed hands, extra limbs, missing limbs, fused legs, floating parts, background, ground"; model_version=$Model; texture=$true; pbr=$true; texture_quality=$Quality; geometry_quality=$Quality }
if ($FaceLimit -gt 0) { $body.face_limit = $FaceLimit }
$json = $body | ConvertTo-Json -Depth 5
$h=@{Authorization="Bearer $env:TRIPO_API_KEY"}
$r = Invoke-RestMethod -Method Post -Uri "https://api.tripo3d.ai/v2/openapi/task" -Headers $h -ContentType "application/json" -Body $json
"$N -> $($r.data.task_id)"
$r.data.task_id | Set-Content "$dir\task_id.txt"
$json | Set-Content "$dir\request.json"
