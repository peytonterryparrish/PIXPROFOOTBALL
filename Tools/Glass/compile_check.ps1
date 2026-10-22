# Fast compile check of project assemblies WITHOUT opening Unity (dotnet build of a scratch csproj derived from the Unity-generated one; sources globbed from the asmdef folder).
# Each assembly is built from the CURRENT sources and the next one references that fresh output (Library\ScriptAssemblies can be stale while other agents edit).
# usage: Tools/Glass/compile_check.ps1 -ProjectList Core,League,Art,Sim,Gameplay,UI,Presentation,Editor   (default: UI against the last Unity build of the others)
param([string]$ProjectList = "UI")
$ErrorActionPreference = "Continue"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
Set-Location $root
$Projects = $ProjectList.Split(",")
$dirs = @{
  Core = "Assets\_Project\Scripts\Core"; League = "Assets\_Project\Scripts\League"; Art = "Assets\_Project\Scripts\Art"; Sim = "Assets\_Project\Scripts\Sim";
  Gameplay = "Assets\_Project\Scripts\Gameplay"; UI = "Assets\_Project\Scripts\UI"; Presentation = "Assets\_Project\Scripts\Presentation"; Editor = "Assets\_Project\Scripts\Editor";
  TestsPresentation = "Assets\_Project\Tests\EditMode\Presentation"
}
$out = "$root\Temp\_check"; New-Item -ItemType Directory -Force $out | Out-Null
$rc = 0
foreach ($p in $Projects) {
  $src = "$root\PixFootball.$p.csproj"
  if ($p -eq "TestsPresentation") { $src = "$root\PixFootball.Tests.Presentation.csproj" }
  $x = Get-Content $src -Raw
  $x = [regex]::Replace($x, '<Compile Include="[^"]*" />\s*', '')
  $x = $x -replace '</Project>', ("  <ItemGroup><Compile Include=`"" + $dirs[$p] + "\**\*.cs`" /></ItemGroup>`r`n</Project>")
  $x = [regex]::Replace($x, '<ProjectReference Include="(PixFootball\.[A-Za-z.]+)\.csproj" />', {
      param($m) $n = $m.Groups[1].Value
      $dll = if (Test-Path "$out\$n.dll") { "Temp\_check\$n.dll" } else { "Library\ScriptAssemblies\$n.dll" }
      "<Reference Include=`"$n`"><HintPath>$dll</HintPath></Reference>" })
  $x = $x -replace '<OutputPath>[^<]*</OutputPath>', '<OutputPath>Temp\_check\</OutputPath>'
  $x = $x -replace '<BaseIntermediateOutputPath>[^<]*</BaseIntermediateOutputPath>', '<BaseIntermediateOutputPath>Temp\_check\obj\$(MSBuildProjectName)</BaseIntermediateOutputPath>'
  $tmpName = "_check_$p.csproj"
  Set-Content "$root\$tmpName" $x -Encoding utf8
  Write-Host "== compile $p"
  $o = dotnet build "$root\$tmpName" --nologo -v q -clp:ErrorsOnly 2>&1
  $o | Where-Object { $_ -match 'error' } | ForEach-Object { $_.ToString().Replace($root + '\', '') } | Sort-Object -Unique | Select-Object -First 60
  if ($LASTEXITCODE -ne 0) { $rc = 1; Write-Host "FAILED $p" } else { Write-Host "OK $p" }
  Remove-Item "$root\$tmpName" -ErrorAction SilentlyContinue
}
exit $rc
