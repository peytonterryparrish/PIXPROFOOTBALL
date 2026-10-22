# Rebuilds the V6 player art helpers (LODs, body-region mask, PBR packing test, hi->lo bake self test, fabric detail textures).
# Requires Blender 5.2 and the owner-supplied Assets/_Licensed/DraftFever tree (git-ignored). No Unity needed.
#   .\Tools\Blender\build_v6_player_art.ps1 [-Bodies Medium,Small,Large] [-SkipLods] [-Blender <blender.exe>]   (default: BLENDER_PATH env var, else the Blender 5.2 install)
#   Note: the shipped player-body LODs come from pix_player_fix.py (.pixmesh), see Docs/ArtPipeline.md; step 1 below is the older FBX route.
# Outputs:
#   Logs/blender_v6/<Body>Player_LODs.fbx            1. LOD0..LOD3 per body mesh (NOT under Assets: import deliberately)
#   Assets/_Licensed/PIX/V6/Textures/PIX_Uniform_Regions.png + Logs/blender_v6/regions_debug.png + uniform_layout.json   2.
#   Logs/blender_v6/T_Uniform_Medium_{M,N}.png       3. HDRP mask map + normal pack test on the Medium uniform maps
#   Logs/blender_v6/bake_selftest/*.png              4. hi->lo bake self test (synthetic icosphere)
#   Assets/_Licensed/PIX/V6/Textures/PIX_FabricWeave_{Normal,AO}.png, PIX_Knit_Normal.png + Logs/blender_v6/fabric_debug*.png   5.
param(
    [string[]]$Bodies = @("Medium", "Small", "Large"),
    [switch]$SkipLods,
    [string]$Blender = $(if ($env:BLENDER_PATH) { $env:BLENDER_PATH } else { "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" })
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$tools = "$PSScriptRoot"
$logs = "$root\Logs\blender_v6"
$tex = "$root\Assets\_Licensed\PIX\V6\Textures"
$uniform = "$root\Assets\_Licensed\DraftFever\Packages\MainPlayer\Mid_Size_Main_Player\Textures\Uniform"
New-Item -ItemType Directory -Force $logs, $tex | Out-Null

function Run-Blender([string]$script, [string[]]$scriptArgs) {
    Write-Host "== $script $($scriptArgs -join ' ')" -ForegroundColor Cyan
    & $Blender -b --python "$tools\$script" -- @scriptArgs
    if ($LASTEXITCODE -ne 0) { throw "$script failed with exit code $LASTEXITCODE" }
}

# 1. LODs (Decimate collapse 0.5 / 0.25 / 0.1, UV-border protected, armature shared, FBX per size)
if (-not $SkipLods) { Run-Blender "pix_player_lods.py" @("--body", ($Bodies -join ","), "--out", $logs) }

# 1b. v0.5.x MODELS: bulky padded LOD0 + Blender-decimated LOD1..3 (.pixmesh for BodyMeshImporter, Unity menu: PIX Football/Assets/Import Fixed Body Meshes) and the measurement scripts (QA-1028)
if (-not $SkipLods) {
    Run-Blender "pix_player_fix.py" @("--body", ($Bodies -join ","), "--out", "$root\Assets\_Licensed\PIX\Meshes\Bodies", "--preview", $logs, "--report", "$logs\fix_report.txt")
    Run-Blender "model_qa_measure.py" @("--out", "$logs\blender_measure.txt")
    Run-Blender "model_qa_weights.py" @("--out", "$logs\blender_weights.txt")
}

# 2. Body-region mask in the uniform UV layout (+ number-decal analysis, + Small/Large UV comparison)
Run-Blender "bake_player_regions.py" @("--body", "Medium", "--size", "2048", "--pad", "12", "--compare",
    "--out", "$tex\PIX_Uniform_Regions.png", "--debug", "$logs\regions_debug.png", "--layout", "$logs\uniform_layout.json")

# 3. PBR pack: HDRP Lit mask map (R metal, G AO, B detail mask, A smoothness) + normal (OpenGL)
Run-Blender "pix_pack_pbr.py" @("--dir", $uniform, "--name", "T_Uniform_Medium", "--out", $logs)

# 4. Generic hi->lo bake, proven on a synthetic scene
Run-Blender "pix_bake_hi_to_lo.py" @("--selftest", "--out", "$logs\bake_selftest")

# 5. Tileable fabric micro detail
Run-Blender "pix_fabric_weave.py" @("--out", $tex, "--debug", "$logs\fabric_debug.png")

Write-Host "V6 player art helpers done." -ForegroundColor Green
