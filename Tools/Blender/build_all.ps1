# Rebuilds the referee / sideline-coach art into Assets/_Licensed (git-ignored). Requires Blender 5.2 and the owner-supplied
# _AssetSource/DraftFever tree. Afterwards run in Unity (batch or menu):
#   PixFootball.EditorTools.RefCoachShots.FullBuildAndShoot
# = Humanoid import setup, materials, prefabs, arm-clip fitting (ArmFit), prop attachment, ArtLibrary rebuild and the verification renders
#   (Docs/Screens/ref_coach_*.png).
#
# Referee mesh: the owner's pre-rigged "BGS" referee (Yan Files/FBXs backup/Referee2.fbx) is copied unmodified; only its uniform texture is
# cleaned (paint_out_logos.py removes the four baked-in league badges, boxes found with scan_logos.py).
# Referee clips: Referee.fbx only converts ~6 of 35 takes to valid Humanoid motion, so the actions are re-baked from the full ARP rig in
# "Studio33 Referee GESTURES.blend" onto the clean 95-bone rig (referee_rebake.py).
$root = Resolve-Path "$PSScriptRoot\..\.."
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$coach = "$root\Assets\_Licensed\DraftFever\Characters\Coaches\Rigged"
$ref = "$root\Assets\_Licensed\DraftFever\Characters\Referee\Rigged"
$yan = "$root\_AssetSource\DraftFever\3D\Characters\Players\Animations Files\Yan Files\FBXs backup"

New-Item -ItemType Directory -Force $coach, $ref, "$ref\Textures" | Out-Null
Copy-Item "$yan\Referee2.fbx" "$ref\Referee_Rigged.fbx" -Force
& $blender -b --python "$PSScriptRoot\referee_rebake.py" -- "$ref\Referee_Gestures.fbx"
& $blender -b --python "$PSScriptRoot\paint_out_logos.py" -- "$root\Assets\_Licensed\DraftFever\Characters\Referee\Textures\Uniform\T_Referee_Uniform_BC.png" "$ref\Textures\PIX_Referee_Uniform_BC.png" 3520,3136,3712,3264 2048,256,2176,384 3264,3840,3456,3968 320,3840,448,3904

& $blender -b --python "$PSScriptRoot\make_coach_textures.py" -- "$root"
& $blender -b --python "$PSScriptRoot\build_coach.py" -- $coach "A,B,C"
& $blender -b --python "$PSScriptRoot\build_anims.py" -- $coach      # also writes Coach_ArmTargets.json for ArmFit
& $blender -b --python "$PSScriptRoot\build_props.py" -- $coach
