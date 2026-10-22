# Track D final measurements: the broadcast match autotest (22 players live play, 2560x1440 Game view, tier High) with the new and the v0.6 player LOD distances
$root = (Resolve-Path "$PSScriptRoot\..").Path
$base = "-pixAutotest -pixAutotestMatchOnly -pixAutotestNoV3 -pixTier 2"
& "$root\Tools\d_play.ps1" -Log "d_final_b.log" -Extra ($base + " -pixAutotestOut D:\PIXFootball\Renders\v070_d\final_b") -MaxMin 40
& "$root\Tools\d_play.ps1" -Log "d_final_a.log" -Extra ($base + " -pixLodNear 11 -pixLodMid 26 -pixLodFar 60 -pixAutotestOut D:\PIXFootball\Renders\v070_d\final_a") -MaxMin 40
