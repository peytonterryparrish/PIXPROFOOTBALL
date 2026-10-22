# Track D: runs the autotest in the editor (GUI, not batch) at a fixed Game view resolution. usage: d_play.ps1 -Log name.log -Extra '-pixAutotest ... -pixAutotestOut D:\dir' [-W 2560 -H 1440] [-MaxMin 45]
param([string]$Log, [string]$Extra, [int]$W = 2560, [int]$H = 1440, [int]$MaxMin = 45)
$root = (Resolve-Path "$PSScriptRoot\..").Path
$u = "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe"
& "$PSScriptRoot\unity_retry.ps1" -Method PixFootball.EditorTools.GameViewResolution.Nop -Log "d_precheck.log" -Tries 30 | Out-Null     # waits for the other editor and for a compiling project
$projName = Split-Path $root -Leaf
while (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like "*$projName*" -and $_.CommandLine -notlike "*AssetImportWorker*" }) { Start-Sleep 5 }
Remove-Item (Join-Path $root "Temp\__Backupscenes") -Recurse -Force -ErrorAction SilentlyContinue   # a killed play-mode editor leaves a scene backup that opens a modal dialog on the next launch
$a = @("-projectPath", "`"$root`"", "-logFile", "`"$root\Logs\$Log`"", "-executeMethod", "PixFootball.EditorTools.GameViewResolution.RunAutoTest", "-pixGvW", "$W", "-pixGvH", "$H") + $Extra.Split(" ")
$p = Start-Process -FilePath $u -ArgumentList $a -PassThru
if (-not $p.WaitForExit($MaxMin * 60000)) { Stop-Process -Id $p.Id -Force; "TIMEOUT killed"; exit 3 }
"exit " + $p.ExitCode
