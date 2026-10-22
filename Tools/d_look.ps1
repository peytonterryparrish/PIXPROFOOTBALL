# Track D: look gallery (day clear / night clear / day snow) at 2560x1440 in the editor
$root = (Resolve-Path "$PSScriptRoot\..").Path
& "$root\Tools\d_play.ps1" -Log "d_look.log" -Extra "-pixAutotest -pixAutotestLookOnly -pixLookCase day_clear,night_clear,day_snow -pixTier 2 -pixAutotestOut D:\PIXFootball\Renders\v070_d\look" -MaxMin 40
