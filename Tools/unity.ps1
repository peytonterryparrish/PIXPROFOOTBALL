# Runs the project's Unity editor in batch mode and waits for it to finish (waits up to 25 min for another editor on this project to close first).
# usage: Tools/unity.ps1 -Method PixFootball.EditorTools.V6ArtBuilder.BuildAll -Log v6_art.log [-NoGraphics] [-Extra '-pixFoo 1'] [-NoQuit] [-UnityPath <Unity.exe>]
#   -NoQuit    do not pass -quit (needed for -runTests, which exits on its own)
#   -Log       file name inside <repo>\Logs (the folder is created when missing)
#   Unity.exe  -UnityPath, else the UNITY_PATH environment variable, else C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe
#   exit code  Unity's own exit code (also printed as "exit N"); 99 = another editor kept the project open past the deadline
param([string]$Method = "", [string]$Log = "unity.log", [switch]$NoGraphics, [string]$Extra = "", [switch]$NoQuit, [string]$UnityPath = "")
if (-not $UnityPath) { $UnityPath = if ($env:UNITY_PATH) { $env:UNITY_PATH } else { "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" } }
$root = (Resolve-Path "$PSScriptRoot\..").Path
$projName = Split-Path $root -Leaf
New-Item -ItemType Directory -Force "$root\Logs" | Out-Null
$deadline = (Get-Date).AddMinutes(25)   # the project is shared by several agents: wait for the other editor to finish
do { $busy = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like "*$projName*" }; if ($busy) { Start-Sleep 10 } } while ($busy -and (Get-Date) -lt $deadline)
if ($busy) { Write-Error "another Unity editor has the project open"; exit 99 }
$unityArgs = @('-batchmode', '-projectPath', "`"$root`"", '-logFile', "`"$root\Logs\$Log`"")
if ($Method) { $unityArgs += @('-executeMethod', $Method) }
if (-not $NoQuit) { $unityArgs += '-quit' }
if ($NoGraphics) { $unityArgs += '-nographics' }
if ($Extra) { $unityArgs += $Extra.Split(' ') }
$p = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -PassThru -Wait
"exit " + $p.ExitCode
exit $p.ExitCode
