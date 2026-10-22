# Rebuilds the referee / sideline-coach art into Assets/_Licensed (git-ignored). Requires Blender 5.2 and the owner-supplied
# _AssetSource/DraftFever tree. Afterwards run in Unity (batch or menu):
#   PixFootball.EditorTools.RefCoachShots.FullBuildAndShoot
# = Humanoid import setup, materials, prefabs, arm-clip fitting (ArmFit), prop attachment, ArtLibrary rebuild and the verification renders
#   (Docs/Screens/ref_coach_*.png).
#
#   .\Tools\Blender\build_all.ps1 [-Blender <blender.exe>]     Blender: -Blender, else the BLENDER_PATH environment variable, else the default 5.2 install.
#   Stops at the first failing step (non-zero Blender exit code) and at a missing source file.
#
# Referee mesh: the owner's pre-rigged "BGS" referee (Yan Files/FBXs backup/Referee2.fbx) is copied unmodified; only its uniform texture is
# cleaned (paint_out_logos.py removes the four baked-in league badges, boxes found with scan_logos.py).
# NOTE: the four boxes below are pixel rectangles measured on T_Referee_Uniform_BC.png only; a different texture needs new boxes (scan_logos.py).
# Referee clips: Referee.fbx only converts ~6 of 35 takes to valid Humanoid motion, so the actions are re-baked from the full ARP rig in
# "Studio33 Referee GESTURES.blend" onto the clean 95-bone rig (referee_rebake.py).
param([string]$Blender = "")
$ErrorActionPreference = "Stop"
if (-not $Blender) { $Blender = if ($env:BLENDER_PATH) { $env:BLENDER_PATH } else { "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" } }
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$coach = "$root\Assets\_Licensed\DraftFever\Characters\Coaches\Rigged"
$ref = "$root\Assets\_Licensed\DraftFever\Characters\Referee\Rigged"
$yan = "$root\_AssetSource\DraftFever\3D\Characters\Players\Animations Files\Yan Files\FBXs backup"
$refSource = "$yan\Referee2.fbx"
$refTexture = "$root\Assets\_Licensed\DraftFever\Characters\Referee\Textures\Uniform\T_Referee_Uniform_BC.png"

function Require-File([string]$path, [string]$what) {
    if (-not (Test-Path -LiteralPath $path)) { throw "missing $what : $path" }
}
function Run-Blender([string]$script, [string[]]$scriptArgs) {
    Write-Host "== $script $($scriptArgs -join ' ')" -ForegroundColor Cyan
    & $Blender -b --python "$PSScriptRoot\$script" -- @scriptArgs
    if ($LASTEXITCODE -ne 0) { throw "$script failed with exit code $LASTEXITCODE" }
}

Require-File $Blender "Blender (set BLENDER_PATH or pass -Blender)"
Require-File $refSource "owner's rigged referee (Yan Files / FBXs backup)"
Require-File $refTexture "referee uniform texture"

New-Item -ItemType Directory -Force $coach, $ref, "$ref\Textures" | Out-Null
Copy-Item $refSource "$ref\Referee_Rigged.fbx" -Force
Run-Blender "referee_rebake.py" @("$ref\Referee_Gestures.fbx")
Run-Blender "paint_out_logos.py" @($refTexture, "$ref\Textures\PIX_Referee_Uniform_BC.png", "3520,3136,3712,3264", "2048,256,2176,384", "3264,3840,3456,3968", "320,3840,448,3904")

Run-Blender "make_coach_textures.py" @($root)
Run-Blender "build_coach.py" @($coach, "A,B,C")
Run-Blender "build_anims.py" @($coach)      # also writes Coach_ArmTargets.json for ArmFit
Run-Blender "build_props.py" @($coach)
Write-Host "Referee / coach art done. Next: Unity PixFootball.EditorTools.RefCoachShots.FullBuildAndShoot" -ForegroundColor Green
