# Rasterizes SVG icon sets of the PIX glass UI collection to crisp PNGs at an exact pixel size (never upscales a PNG): headless Edge renders each SVG at the target size, a grid page is sliced with System.Drawing.
# usage: Tools/Glass/rasterize_icons.ps1 [-Size 192] [-OutRoot <Assets/_Licensed/UI/Glass/Icons>] [-SrcRoot D:\PIXFootball\Source]
param([int]$Size = 192, [string]$OutRoot = "", [string]$SrcRoot = "D:\PIXFootball\Source")
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Drawing
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
if (-not $OutRoot) { $OutRoot = "$root\Assets\_Licensed\UI\Glass\Icons" }
$edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "C:\Program Files\Microsoft\Edge\Application\msedge.exe" }
$sets = @(
  @{ Prefix = "fb";  Dir = "$SrcRoot\Glass6\assets\line_icons" },
  @{ Prefix = "ui";  Dir = "$SrcRoot\UI_Extracted\UIPack1\PIX_PRO_Clean_Modern_UI_300_PNG_SVG_PSD\PIX_PRO_Clean_Modern_UI_300\assets\07_icons" },
  @{ Prefix = "sym"; Dir = "$SrcRoot\UI_Extracted\UIPack2\PIX_PRO_Clean_Modern_UI_Pack2_300_PNG_SVG_PSD\PIX_PRO_Clean_UI_Pack2\assets\11_sports_symbols" },
  @{ Prefix = "sc"; Recolor = "#E1E2E4"; Dir = "$SrcRoot\UI_Extracted\UIPack5\PIX_PRO_Scouting_750_Part_03_of_03_Symbols_Frames_Controls\PIX_PRO_Scouting_UI_750\assets\31_scouting_symbols" }
)
$tmp = "$SrcRoot\_raster_tmp"; New-Item -ItemType Directory -Force $tmp | Out-Null
New-Item -ItemType Directory -Force $OutRoot | Out-Null
$manifest = @()
foreach ($set in $sets) {
  $files = Get-ChildItem $set.Dir -Filter *.svg | Sort-Object Name
  if ($set.Recolor) {   # multi-hue source art is restyled to the glass silver so it reads as one family
    $rc = "$tmp\recolor_$($set.Prefix)"; New-Item -ItemType Directory -Force $rc | Out-Null
    foreach ($f in $files) { (Get-Content $f.FullName -Raw) -replace '#[0-9A-Fa-f]{6}', $set.Recolor | Set-Content "$rc\$($f.Name)" -Encoding utf8 }
    $files = Get-ChildItem $rc -Filter *.svg | Sort-Object Name
  }
  for ($b = 0; $b -lt $files.Count; $b += 36) {
    $batch = $files[$b..([Math]::Min($b + 35, $files.Count - 1))]
    $cols = 6; $rows = [Math]::Ceiling($batch.Count / $cols)
    $html = "<!doctype html><html><body style='margin:0;background:transparent;width:$($cols*$Size)px'>"
    foreach ($f in $batch) { $html += "<img src='file:///$($f.FullName.Replace('\','/'))' width='$Size' height='$Size' style='display:block;float:left'>" }
    $html += "</body></html>"
    Set-Content "$tmp\grid.html" $html -Encoding utf8
    $png = "$tmp\grid.png"; if (Test-Path $png) { Remove-Item $png }
    & $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 --force-device-scale-factor=1 --window-size="$($cols*$Size),$($rows*$Size)" --screenshot="$png" "file:///$($tmp.Replace('\','/'))/grid.html" 2>&1 | Out-Null
    $bmp = [System.Drawing.Bitmap]::FromFile($png)
    for ($i = 0; $i -lt $batch.Count; $i++) {
      $rect = New-Object System.Drawing.Rectangle (($i % $cols) * $Size), ([Math]::Floor($i / $cols) * $Size), $Size, $Size
      $tile = $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
      $slug = ($batch[$i].BaseName -replace '^\d+_', '')
      $name = $set.Prefix + "_" + $slug
      $tile.Save("$OutRoot\$name.png", [System.Drawing.Imaging.ImageFormat]::Png); $tile.Dispose()
      $manifest += [pscustomobject]@{ key = $name; set = $set.Prefix; source = $batch[$i].FullName; px = $Size }
    }
    $bmp.Dispose()
  }
}
$manifest | ConvertTo-Json | Set-Content "$OutRoot\icons_manifest.json" -Encoding utf8
"rasterized $($manifest.Count) icons at ${Size}px to $OutRoot"



