# Merges the QA track logs in Docs/QA into Docs/QA/CHECKLIST.md (release checklist).
# usage: powershell -ExecutionPolicy Bypass -File Tools/MakeChecklist.ps1 [-Version 0.6.0] [-Tail <path to a markdown file appended as the last section>]
param([string]$Version = "0.6.0", [string]$Tail = "")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$qaDir = Join-Path $root "Docs\QA"
$enc = New-Object System.Text.UTF8Encoding($false)

function Split-Row([string]$line) {
    $t = $line.Trim()
    if ($t.StartsWith("|")) { $t = $t.Substring(1) }
    if ($t.EndsWith("|")) { $t = $t.Substring(0, $t.Length - 1) }
    $t = $t.Replace('\|', [string][char]1)
    $cells = $t.Split('|') | ForEach-Object { $_.Replace([string][char]1, '|').Trim() }
    return ,@($cells)
}
function Norm-Status([string]$s) {
    $l = $s.ToLowerInvariant()
    $best = ""; $bp = 9999
    foreach ($k in @("fixed", "wontfix", "open", "dup", "verified")) { $p = $l.IndexOf($k); if ($p -ge 0 -and $p -lt $bp) { $bp = $p; $best = $k } }
    if ($best -eq "verified") { $best = "fixed" }
    if ($best -eq "") { $best = "open" }
    return $best
}
function Clip([string]$s, [int]$n) {
    $s = ($s -replace '\s+', ' ').Replace('|', '/').Trim()
    if ($s.Length -gt $n) { return $s.Substring(0, $n - 1).TrimEnd() + [string][char]0x2026 }
    return $s
}
$groupOf = @{ STN = "Stances, facing, formations"; CAM = "Cameras"; ANI = "Animation and mocap"; MDL = "Models, skeletons, helmets"; PHR = "Photoreal player"; SIM = "Match engine, AI, rules, stats"; GFX = "Graphics and lighting"; UI = "UI, flow, settings, data, docs, tools" }
$groupOrder = @("STN", "CAM", "ANI", "MDL", "PHR", "GFX", "SIM", "UI")

# ------------------------------------------------------------------------------------------------ track rows
$track = New-Object System.Collections.Generic.List[object]
$files = @("stance_camera", "animation", "models", "engine", "graphics", "ui_flow", "photoreal")
foreach ($f in $files) {
    $path = Join-Path $qaDir "$f.md"
    if (-not (Test-Path $path)) { continue }
    $inCross = $false
    foreach ($line in [System.IO.File]::ReadAllLines($path, $enc)) {
        if ($line -match '^## ') { $inCross = ($line -match 'rudit (crosswalk|rows|index)' -or $line -match 'Audit (crosswalk|rows|index)') }
        if ($inCross) { continue }
        if ($line -notmatch '^\|\s*([A-Z]{2,3})-(\d{3,4})\s*\|') { continue }
        $c = Split-Row $line
        if ($c.Count -lt 8) { continue }
        $n = $c.Count
        $issue = ($c[5..($n - 3)] -join " | ")
        $track.Add([pscustomobject]@{ Id = $c[0]; Prefix = $Matches[1]; Num = [int]$Matches[2]; Status = (Norm-Status $c[1]); Sev = $c[2]; Cat = $c[3]; Where = $c[4]; Issue = $issue; Fix = $c[$n - 2]; Ver = $c[$n - 1]; File = $f; Qa = @() })
    }
}
$trackById = @{}
foreach ($r in $track) { if (-not $trackById.ContainsKey($r.Id)) { $trackById[$r.Id] = $r } }

# ------------------------------------------------------------------------------------------------ audit rows
$audit = [ordered]@{}
foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $qaDir "audit.md"), $enc)) {
    if ($line -notmatch '^\|\s*(QA-\d+)\s*\|') { continue }
    $c = Split-Row $line
    if ($c.Count -lt 9) { continue }
    $n = $c.Count
    $audit[$c[0]] = [pscustomobject]@{ Id = $c[0]; Sev = $c[2]; Track = $c[3]; Cat = $c[4]; Where = $c[5]; Issue = ($c[6..($n - 3)] -join " | "); AuditStatus = $c[1]; Status = "open"; Rows = (New-Object System.Collections.Generic.List[string]); Note = "" }
}

# ------------------------------------------------------------------------------------------------ crosswalks
$rank = @{ fixed = 4; wontfix = 3; dup = 2; open = 1 }
function Set-Qa([string]$qa, [string]$status, [string]$text) {
    if (-not $audit.Contains($qa)) { return }
    $a = $audit[$qa]
    $st = Norm-Status $status
    if ($rank[$st] -gt $rank[$a.Status]) { $a.Status = $st }
    foreach ($m in [regex]::Matches($text, '\b(?:STN|CAM|ANI|MDL|SIM|GFX|UI|PHR)-\d{3,4}\b')) { if (-not $a.Rows.Contains($m.Value)) { $a.Rows.Add($m.Value) } }
    if ($a.Note -eq "" -and $text.Length -gt 0) { $a.Note = $text }
}
foreach ($f in $files) {
    $path = Join-Path $qaDir "$f.md"
    if (-not (Test-Path $path)) { continue }
    $in = $false; $openList = $false
    foreach ($line in [System.IO.File]::ReadAllLines($path, $enc)) {
        if ($line -match '^## ') { $in = ($line -match 'Audit (crosswalk|rows|index)'); $openList = $false; continue }
        if (-not $in) { continue }
        if ($line -match '^Open \(') { $openList = $true; continue }
        if ($openList -and $line -notmatch '^\|') { foreach ($m in [regex]::Matches($line, 'QA-\d+')) { Set-Qa $m.Value "open" "" }; continue }
        if ($line -match '^\s*(?:[-*]|\|)?\s*(QA-\d+)\b') {
            $qa = $Matches[1]
            $rest = $line.Substring($line.IndexOf($qa) + $qa.Length)
            # ranges such as "QA-101 .. QA-110" are not used by the logs; only the first QA id of a line is the subject
            Set-Qa $qa $rest $rest
        }
    }
}
# fix rows that list "closes QA-nnn" / "QA-nnn" in their own text (UI track): attach the id to the audit row when the audit row has no crosswalk entry yet
foreach ($r in $track) {
    foreach ($m in [regex]::Matches(($r.Issue + " " + $r.Fix), 'QA-\d+')) {
        $qa = $m.Value
        if ($audit.Contains($qa)) {
            $a = $audit[$qa]
            if (-not $a.Rows.Contains($r.Id)) { $a.Rows.Add($r.Id) }
            if ($a.Rows.Count -eq 1 -and $a.Status -eq "open" -and $a.Note -eq "" -and $r.Status -eq "fixed" -and $r.Issue -match ('closes[^.]*' + $qa)) { $a.Status = "fixed" }
        }
    }
}
$unmapped = @($audit.Values | Where-Object { $_.Note -eq "" -and $_.Rows.Count -eq 0 })
foreach ($a in $audit.Values) { foreach ($rid in $a.Rows) { if ($trackById.ContainsKey($rid)) { $trackById[$rid].Qa += $a.Id } } }

# ------------------------------------------------------------------------------------------------ counts
function Count-By($rows, $pred) { return @($rows | Where-Object $pred).Count }
$sb = New-Object System.Text.StringBuilder
function W([string]$s = "") { [void]$sb.AppendLine($s) }

$galleryRows = @($track | Where-Object { $_.Prefix -eq "STN" -and $_.Num -ge 1000 })
$mainTrack = @($track | Where-Object { -not ($_.Prefix -eq "STN" -and $_.Num -ge 1000) })
$referenced = New-Object System.Collections.Generic.HashSet[string]
foreach ($a in $audit.Values) { foreach ($rid in $a.Rows) { [void]$referenced.Add($rid) } }
$independent = @($track | Where-Object { -not $referenced.Contains($_.Id) })
$qaFixed = Count-By $audit.Values { $_.Status -eq "fixed" -or $_.Status -eq "dup" }
$qaWont = Count-By $audit.Values { $_.Status -eq "wontfix" }
$qaOpen = Count-By $audit.Values { $_.Status -eq "open" }
$indFixed = Count-By $independent { $_.Status -eq "fixed" }
$indWont = Count-By $independent { $_.Status -eq "wontfix" }
$indOpen = Count-By $independent { $_.Status -eq "open" -or $_.Status -eq "dup" }
$totalFound = $audit.Count + $independent.Count
$totalFixed = $qaFixed + $indFixed

W "# PIX Football v$Version - release checklist"
W ""
W "Generated by ``Tools/MakeChecklist.ps1`` from the track logs in ``Docs/QA`` (audit, stance_camera, animation, models, engine, graphics, ui_flow, photoreal). Source of truth for rows is the track file; this file only merges and cross-references them. Evidence images are git-ignored, regenerate them with the editor QA tools."
W ""
W "## Totals (honest count)"
W ""
W "The user asked for at least 500 issues found, fixed and checklisted."
W ""
W "| Measure | Count |"
W "|---|---|"
W "| Audit rows logged against the v0.5.0 build (QA-001 .. QA-$($audit.Count)) | $($audit.Count) |"
W "| Track rows that are not an answer to an audit row (found while fixing, Formation Gallery failures, tooling) | $($independent.Count) |"
W "| **Distinct issues logged** | **$totalFound** |"
W "| Fixed (or duplicate of a fixed row) | **$totalFixed** |"
W "| Wontfix (with reason in the track log) | $($qaWont + $indWont) |"
W "| Open (carried to v0.7.0 and later) | $($qaOpen + $indOpen) |"
W ""
W "Of the audit rows: $qaFixed fixed or duplicate, $qaWont wontfix, $qaOpen open, $($unmapped.Count) with no crosswalk entry (counted as open). $($galleryRows.Count) of the track-only rows are Formation Gallery failures (STN-1001 ..): one row per position x formation x direction x check in the baseline run, caused by a handful of root causes (STN-801 .. STN-804), so they inflate the count; without them the distinct total is $($totalFound - $galleryRows.Count) and fixed $($totalFixed - (Count-By $galleryRows { $_.Status -eq 'fixed' }))."
W ""

# summary per track
W "## Summary by track"
W ""
W "Rows owned by each track file (audit rows are counted once, in the crosswalk table below)."
W ""
W "| Track | Rows | Fixed | Wontfix | Open | S1 found / fixed | S2 found / fixed | S3 found / fixed |"
W "|---|---|---|---|---|---|---|---|"
foreach ($p in $groupOrder) {
    $rows = @($track | Where-Object { $_.Prefix -eq $p })
    if ($rows.Count -eq 0) { continue }
    $cells = @()
    foreach ($s in @("S1", "S2", "S3")) { $cells += ("{0} / {1}" -f (Count-By $rows { $_.Sev -eq $s }), (Count-By $rows { $_.Sev -eq $s -and $_.Status -eq "fixed" })) }
    W ("| {0} ({1}) | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f $p, $groupOf[$p], $rows.Count, (Count-By $rows { $_.Status -eq "fixed" }), (Count-By $rows { $_.Status -eq "wontfix" }), (Count-By $rows { $_.Status -eq "open" -or $_.Status -eq "dup" }), $cells[0], $cells[1], $cells[2])
}
W ""
W "Audit rows by their assigned audit track:"
W ""
W "| Audit track | Rows | Fixed / dup | Wontfix | Open | S1 | S2 | S3 |"
W "|---|---|---|---|---|---|---|---|"
foreach ($t in @("STN", "CAM", "ANI", "MDL", "GFX", "SIM", "UI")) {
    $rows = @($audit.Values | Where-Object { $_.Track -eq $t })
    if ($rows.Count -eq 0) { continue }
    W ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f $t, $rows.Count, (Count-By $rows { $_.Status -eq "fixed" -or $_.Status -eq "dup" }), (Count-By $rows { $_.Status -eq "wontfix" }), (Count-By $rows { $_.Status -eq "open" }), (Count-By $rows { $_.Sev -eq "S1" }), (Count-By $rows { $_.Sev -eq "S2" }), (Count-By $rows { $_.Sev -eq "S3" }))
}
W ""

# master list
W "## Master list by category"
W ""
W "One row per distinct issue. Track rows carry the audit rows they answer (QA-###); audit rows no track row answers are listed under their audit track with the id QA-###. The Formation Gallery failures (STN-1001 ..) are aggregated by check. Text is clipped; the full text is in the track file named in the id."
W ""
$sevOrder = @{ S1 = 1; S2 = 2; S3 = 3 }
foreach ($p in $groupOrder) {
    $rows = @($mainTrack | Where-Object { $_.Prefix -eq $p })
    $qaOnly = @($audit.Values | Where-Object { $_.Rows.Count -eq 0 -and $groupOf.ContainsKey($_.Track) -and ($_.Track -eq $p -or ($p -eq "UI" -and $_.Track -notin $groupOrder)) })
    if ($p -eq "STN") { $qaOnly += @($audit.Values | Where-Object { $_.Rows.Count -eq 0 -and $_.Track -eq "CAM" }) }
    if ($p -eq "CAM") { $qaOnly = @($qaOnly | Where-Object { $_.Track -ne "CAM" }) }
    if ($p -eq "STN") { $rows = @($track | Where-Object { $_.Prefix -eq "STN" -and $_.Num -lt 1000 }) }
    $title = $groupOf[$p]
    W "### $title ($($rows.Count + $qaOnly.Count) rows$(if ($p -eq 'STN') { ', plus ' + $galleryRows.Count + ' gallery failures aggregated below' }))"
    W ""
    W "| ID(s) | Sev | Issue | Status | Fix | Verified by |"
    W "|---|---|---|---|---|---|"
    $all = @()
    foreach ($r in $rows) {
        $ids = $r.Id; if ($r.Qa.Count -gt 0) { $ids += " (" + ((@($r.Qa | Select-Object -First 4)) -join ", ") + $(if ($r.Qa.Count -gt 4) { ", +" + ($r.Qa.Count - 4) } else { "" }) + ")" }
        $all += [pscustomobject]@{ S = $sevOrder[$r.Sev]; K = $r.Id; Line = ("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $ids, $r.Sev, (Clip $r.Issue 150), $r.Status, (Clip $r.Fix 110), (Clip $r.Ver 60)) }
    }
    foreach ($a in $qaOnly) {
        $all += [pscustomobject]@{ S = $sevOrder[$a.Sev]; K = $a.Id; Line = ("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $a.Id, $a.Sev, (Clip $a.Issue 150), $a.Status, (Clip $a.Note 110), "") }
    }
    foreach ($x in ($all | Sort-Object S, K)) { W $x.Line }
    if ($p -eq "STN" -and $galleryRows.Count -gt 0) {
        W ""
        W "**Formation Gallery failures (STN-1001 .. STN-$(($galleryRows | Measure-Object Num -Maximum).Maximum))**, aggregated by check and fix:"
        W ""
        W "| Check | Sev | Rows | ID range | Status | Fix |"
        W "|---|---|---|---|---|---|"
        $groups = $galleryRows | Group-Object { ($_.Issue -split ':')[0] + "|" + $_.Sev + "|" + $_.Status + "|" + $_.Fix }
        foreach ($g in ($groups | Sort-Object { $_.Group[0].Sev }, Name)) {
            $first = $g.Group[0]; $nums = @($g.Group | ForEach-Object { $_.Num } | Sort-Object)
            W ("| {0} | {1} | {2} | STN-{3} .. STN-{4} | {5} | {6} |" -f (($first.Issue -split ':')[0]), $first.Sev, $g.Count, $nums[0], $nums[-1], $first.Status, (Clip $first.Fix 90))
        }
    }
    W ""
}

# crosswalk
W "## Crosswalk: every audit row (QA-###) to its final status"
W ""
W "| QA | Sev | Audit track | Final | Answered by | Note |"
W "|---|---|---|---|---|---|"
foreach ($a in $audit.Values) {
    $by = (@($a.Rows | Select-Object -First 5)) -join ", "
    $st = $a.Status; if ($a.Note -eq "" -and $a.Rows.Count -eq 0) { $st = "open (not triaged)" }
    W ("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $a.Id, $a.Sev, $a.Track, $st, $by, (Clip $a.Note 90))
}
W ""
if ($Tail -ne "" -and (Test-Path $Tail)) { W ([System.IO.File]::ReadAllText($Tail, $enc)) }
[System.IO.File]::WriteAllText((Join-Path $qaDir "CHECKLIST.md"), $sb.ToString(), $enc)
"checklist written: found=$totalFound fixed=$totalFixed audit=$($audit.Count) independent=$($independent.Count) gallery=$($galleryRows.Count) unmapped=$($unmapped.Count) qaFixed=$qaFixed qaWont=$qaWont qaOpen=$qaOpen indFixed=$indFixed"
