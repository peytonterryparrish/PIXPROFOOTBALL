# Compile-checks the Art assembly sources and the given Editor .cs files with Unity's bundled Roslyn (no Unity launch needed, ~10 s).
# usage: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/compile_check.ps1 -EditorFilesCsv Assets/.../Foo.cs,Bar.cs
param([string]$EditorFilesCsv = "", [string]$TestFilesCsv = "")
$EditorFiles = if ($EditorFilesCsv) { $EditorFilesCsv.Split(",") } else { @() }
$u = "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor"
$proj = (Resolve-Path "$PSScriptRoot\..").Path
$csc = "$u\Data\DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll"
$dotnet = "$u\Data\NetCoreRuntime\dotnet.exe"
$out = Join-Path $env:TEMP "pix_cc"; New-Item -ItemType Directory -Force $out | Out-Null
function Refs($files) { $files | ForEach-Object { '-r:"' + $_.FullName + '"' } }
$ue = Refs (Get-ChildItem "$u\Data\Managed\UnityEngine" -Filter *.dll)
$ued = Refs (Get-ChildItem "$u\Data\Managed" -Filter "UnityEditor*.dll" | Where-Object { $_.Name -ne "UnityEditor.dll" })
$ns = Refs (Get-ChildItem "$u\Data\NetStandard\ref\2.1.0" -Filter *.dll)
$mono = "$u\Data\MonoBleedingEdge\lib\mono\4.7.1-api"
$monoRefs = (Refs (Get-ChildItem $mono -Filter *.dll)) + (Refs (Get-ChildItem "$mono\Facades" -Filter *.dll))
$sa = Refs (Get-ChildItem "$proj\Library\ScriptAssemblies" -Filter *.dll | Where-Object { $_.Name -notin @("PixFootball.Art.dll", "PixFootball.Editor.dll") })
$artSrc = Get-ChildItem "$proj\Assets\_Project\Scripts\Art" -Filter *.cs | ForEach-Object { '"' + $_.FullName + '"' }
$nw = "-nowarn:CS0618,CS0219,CS0169,CS0414,CS0649,CS0168,CS1701,CS1702"
$rsp1 = Join-Path $out "art.rsp"
(@("-nologo", $nw, "-target:library", ('-out:"' + $out + '\PixFootball.Art.dll"'), "-langversion:9", "-unsafe") + $ue + $ns + $sa + $artSrc) | Set-Content $rsp1
& $dotnet $csc "@$rsp1"
"ART exit $LASTEXITCODE"
if ($EditorFiles) {
    $ed = $EditorFiles | ForEach-Object { '"' + (Resolve-Path $_).Path + '"' }
    $rsp2 = Join-Path $out "ed.rsp"
    (@("-nologo", $nw, "-nostdlib+", "-target:library", ('-out:"' + $out + '\EdTest.dll"'), "-langversion:9") + $ue + $ued + $monoRefs + $sa + @(('-r:"' + $out + '\PixFootball.Art.dll"'), ('-r:"' + $proj + '\Library\ScriptAssemblies\PixFootball.Editor.dll"')) + $ed) | Set-Content $rsp2
    & $dotnet $csc "@$rsp2"
    "EDITOR exit $LASTEXITCODE"
}
if ($TestFilesCsv) {
    $tf = $TestFilesCsv.Split(",") | ForEach-Object { '"' + (Resolve-Path $_).Path + '"' }
    $nunit = (Get-ChildItem "$proj\Library\PackageCache" -Recurse -Filter "nunit.framework.dll" | Select-Object -First 1).FullName
    $rsp3 = Join-Path $out "tests.rsp"
    (@("-nologo", $nw, "-nostdlib+", "-target:library", ('-out:"' + $out + '\TestsCheck.dll"'), "-langversion:9") + $ue + $ued + $monoRefs + $sa + @(('-r:"' + $out + '\PixFootball.Art.dll"'), ('-r:"' + $nunit + '"')) + $tf) | Set-Content $rsp3
    & $dotnet $csc "@$rsp3"
    "TESTS exit $LASTEXITCODE"
}
