# usage: unity_wait_run.ps1 -Method Class.Method -Log name [-MaxWaitMin 90] [-Extra '-arg value']
# Waits until no other Unity.exe has this project open, then runs a batch method (shared-project etiquette for parallel agents).
# Writes Logs\<name>.log (the folder is created when missing). Unity.exe: the UNITY_PATH environment variable, else the default 6000.6.6f1 install.
# Exit code: Unity's exit code, or 2 when the wait for the other editor timed out.
param([string]$Method, [string]$Log, [int]$MaxWaitMin = 90, [string]$Extra = "")
$root = (Resolve-Path "$PSScriptRoot\..").Path
$projName = Split-Path $root -Leaf
$deadline = (Get-Date).AddMinutes($MaxWaitMin)
function Busy { Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like "*$projName*" -and $_.CommandLine -notlike "*AssetImportWorker*" } }
while (Busy) { if ((Get-Date) -gt $deadline) { Write-Output "TIMEOUT waiting for Unity"; exit 2 }; Start-Sleep -Seconds 4 }
Start-Sleep -Seconds 2
if (Busy) { while (Busy) { Start-Sleep -Seconds 4 } }
$u = if ($env:UNITY_PATH) { $env:UNITY_PATH } else { "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" }
New-Item -ItemType Directory -Force "$root\Logs" | Out-Null
$args2 = @("-batchmode", "-projectPath", "`"$root`"", "-executeMethod", $Method, "-quit", "-logFile", "`"$root\Logs\$Log.log`"")
if ($Extra) { $args2 += $Extra.Split(" ") }
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $u -ArgumentList $args2 -PassThru -Wait
Write-Output ("UNITY exit " + $p.ExitCode + " in " + [int]$sw.Elapsed.TotalSeconds + "s")
exit $p.ExitCode
