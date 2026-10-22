# Like unity.ps1 but retries when the project does not compile because another agent is mid-edit (shared project): up to -Tries attempts, 90 s apart.
param([string]$Method = "", [string]$Log = "unity.log", [string]$Extra = "", [int]$Tries = 12, [switch]$NoGraphics)
$root = (Resolve-Path "$PSScriptRoot\..").Path
for ($i = 1; $i -le $Tries; $i++) {
  $a = @{ Method = $Method; Log = $Log; Extra = $Extra }
  if ($NoGraphics) { $a.NoGraphics = $true }
  & "$PSScriptRoot\unity.ps1" @a | Out-Null
  $txt = Get-Content "$root\Logs\$Log" -ErrorAction SilentlyContinue
  if (-not ($txt | Select-String -Pattern "error CS" -Quiet)) { "ok after $i attempt(s)"; exit 0 }
  Start-Sleep 90
}
"gave up: compile errors persisted"; exit 1
