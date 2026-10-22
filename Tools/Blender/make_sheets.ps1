# usage: Tools/Blender/make_sheets.ps1 [-Which all]   builds contact sheets (*_sheet.png, all_10.png, all_13.png) from the look-dev renders in Docs/QA/tripo_looks
param([string]$Which="all")
Add-Type -AssemblyName System.Drawing
$d = Join-Path (Resolve-Path "$PSScriptRoot\..\..").Path "Docs\QA\tripo_looks"   # contact sheets of the Tripo player-look renders (images are git-ignored)
$entries = [ordered]@{
 "01_faithful_2k8"="1 Faithful 2K8"; "02_photoreal_broadcast"="2 Photoreal broadcast"; "03_bulky_lineman"="3 Bulky lineman"; "04_lean_receiver"="4 Lean receiver";
 "05_gritty_gameworn"="5 Gritty game-worn"; "06_retro_throwback"="6 Retro throwback"; "07_hero_cover_athlete"="7 Hero cover athlete"; "08_clean_readable_gameplay"="8 Clean/readable";
 "09_running_back_ball_tucked"="9 RB ball tucked"; "10_quarterback_ball_in_hand"="10 QB ball in hand";
 "E1_skill"="E1 Era 2004-arcade WR"; "E1_lineman"="E1 Era 2004-arcade OL"; "E2_skill"="E2 Era 2004-sim RB"; "E2_lineman"="E2 Era 2004-sim DL";
 "E3_skill"="E3 Era 2007-HD WR"; "E3_lineman"="E3 Era 2007-HD OG"; "E4_skill"="E4 Era 2011 QB"; "E4_lineman"="E4 Era 2011 OT";
 "E5_skill"="E5 Era 2019 photoreal RB"; "E5_lineman"="E5 Era 2019 photoreal OL"; "E6_lowpoly"="E6 Era 1997 low-poly";
 "R1_rigged_apose_qb"="R1 Rigged A-pose QB (ref only)"; "R2_qb_deep_throw"="R2 QB deep throw"; "R3_lineman_protection"="R3 OL protection"
}
$font = New-Object System.Drawing.Font("Segoe UI", 22, [System.Drawing.FontStyle]::Bold)
$fontS = New-Object System.Drawing.Font("Segoe UI", 34, [System.Drawing.FontStyle]::Bold)
$bgc = [System.Drawing.Color]::FromArgb(24,26,30)
function Crop($path) {
  $img = [System.Drawing.Image]::FromFile($path)
  $bmp = New-Object System.Drawing.Bitmap $img
  $img.Dispose(); return $bmp
}
# crop region of the 1024x1536 render, aspect 0.72
$cx=122; $cy=240; $cw=780; $ch=1080
function Draw-Cell($g, $key, $x, $y, $cellW, $cellH, $label) {
  $half = [int]($cellW/2)
  $h = $cellH - 44
  foreach ($i in 0,1) {
    $v = @("front","threequarter")[$i]
    $p = "$d\$($key)_$v.png"
    if (Test-Path $p) {
      $b = Crop $p
      $dst = New-Object System.Drawing.Rectangle(($x + $i*$half), $y, $half, $h)
      $g.DrawImage($b, $dst, $cx, $cy, $cw, $ch, [System.Drawing.GraphicsUnit]::Pixel)
      $b.Dispose()
    }
  }
  $g.DrawString($label, $font, [System.Drawing.Brushes]::White, ($x+10), ($y+$h+4))
}
function Make-Sheet($out, $groups, $cols) {
  # $groups: array of @{title=..; keys=@(...)}
  $cellW=560; $cellH=424; $titleH=70
  $rows = 0; foreach ($gg in $groups) { $rows += [math]::Ceiling($gg.keys.Count / $cols) }
  $H = $rows*$cellH + $groups.Count*$titleH + 10
  $W = $cols*$cellW
  $bmp = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear($bgc); $g.InterpolationMode='HighQualityBicubic'; $g.SmoothingMode='AntiAlias'; $g.TextRenderingHint='ClearTypeGridFit'
  $y = 0
  foreach ($grp in $groups) {
    $g.DrawString($grp.title, $fontS, [System.Drawing.Brushes]::Gold, 12, ($y+10)); $y += $titleH
    $i=0
    foreach ($k in $grp.keys) {
      $col = $i % $cols; $row = [math]::Floor($i / $cols)
      Draw-Cell $g $k ($col*$cellW) ($y + $row*$cellH) $cellW $cellH $entries[$k]
      $i++
    }
    $y += [math]::Ceiling($grp.keys.Count / $cols) * $cellH
  }
  $g.Dispose(); $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); "wrote $out"
}
$t10 = @($entries.Keys | Where-Object { $_ -match "^\d\d_" })
$era = @($entries.Keys | Where-Object { $_ -match "^E\d" })
$ref = @($entries.Keys | Where-Object { $_ -match "^R\d" })
# per-model sheets
foreach ($k in $entries.Keys) {
  $f = "$d\$($k)_front.png"; $t="$d\$($k)_threequarter.png"; $b="$d\$($k)_back.png"
  if ((Test-Path $f) -and (Test-Path $t) -and (Test-Path $b)) {
    $out = "$d\$($k)_sheet.png"
    if ((Test-Path $out) -and ((Get-Item $out).LastWriteTime -gt (Get-Item $b).LastWriteTime)) { continue }
    $bm = New-Object System.Drawing.Bitmap 1920, 1020
    $g = [System.Drawing.Graphics]::FromImage($bm); $g.Clear($bgc); $g.InterpolationMode='HighQualityBicubic'
    $i=0
    foreach ($p in $f,$t,$b) { $im=Crop $p; $g.DrawImage($im, (New-Object System.Drawing.Rectangle(($i*640),0,640,960)), 0,0,1024,1536,[System.Drawing.GraphicsUnit]::Pixel); $im.Dispose(); $i++ }
    $g.DrawString($entries[$k], $font, [System.Drawing.Brushes]::White, 10, 965)
    $g.Dispose(); $bm.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bm.Dispose()
  }
}
Make-Sheet "$d\all_10.png" @(@{title="Tripo looks 1-10"; keys=$t10}) 5
Make-Sheet "$d\all_13.png" @(@{title="Tripo looks 1-10"; keys=$t10}, @{title="User reference models R1-R3"; keys=$ref}) 5
Make-Sheet "$d\all_looks.png" @(@{title="Tripo looks 1-10"; keys=$t10}, @{title="Era looks E1-E6 (skill + lineman per era)"; keys=$era}, @{title="User reference models R1-R3"; keys=$ref}) 5
