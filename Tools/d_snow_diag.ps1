# Track D: GFX-082 diagnosis - runs the look gallery for day_snow with one effect disabled per launch (editor play mode, 2560x1440 Game view)
$root = (Resolve-Path "$PSScriptRoot\..").Path
$variants = ,@("fix1", "")
foreach ($v in $variants) {
  $extra = ("-pixAutotest -pixAutotestLookOnly -pixLookCase day_snow -pixTier 2 -pixAutotestOut D:\PIXFootball\Renders\v070_d\snow_" + $v[0] + " " + $v[1]).Trim()
  & "$root\Tools\d_play.ps1" -Log ("d_snow_" + $v[0] + ".log") -Extra $extra
}
