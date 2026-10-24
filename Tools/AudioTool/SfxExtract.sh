#!/bin/bash
# Extracts the source wavs used by SfxSpec.csv from the Unity packages into D:/PIXFootball/Work/Audio/sfx_tmp/<pack>/<dir>/<file>.wav (never into the repo).
# Needs the guid->path indexes in D:/PIXFootball/Work/Audio/pk/*.idx. Usage: bash SfxExtract.sh   (gm = Gamemaster, takes a couple of minutes)
root=/d/PIXFootball/Work/Audio; A="/c/Users/peyto/Desktop/Assets"
ex() { # pack package idx regex
  pack="$1"; pkg="$2"; idx="$3"; rx="$4"; tmp="$root/sfx_tmp"; work="$tmp/_x_$pack"; rm -rf "$work"; mkdir -p "$work"
  grep -iP "\t.*\.wav\s*$" "$idx" | tr -d '\r' | awk -F'\t' '{print $1"\t"$2}' | grep -iP "$rx" > "$work/sel.tsv"
  awk -F'\t' '{print "*"$1"/asset"}' "$work/sel.tsv" > "$work/pat.txt"
  ( cd "$work" && tar -xzf "$pkg" --wildcards -T pat.txt )
  mkdir -p "$tmp/$pack"
  while IFS=$'\t' read -r g p; do rel=$(echo "${p#Assets/}" | awk -F/ '{print $(NF-1)"/"$NF}')
    [ -f "$work/$g/asset" ] && { mkdir -p "$tmp/$pack/$(dirname "$rel")"; mv "$work/$g/asset" "$tmp/$pack/$rel"; } || echo "MISSING $g $p"; done < "$work/sel.tsv"
  rm -rf "$work"; echo "$pack: $(find "$tmp/$pack" -name '*.wav' | wc -l) wavs"
}
ex ad  "$A/Asset Store-5.x/AD Sounds/AudioSound FX/Ball Sports - Sound Effects.unitypackage" $root/pk/adball.idx '.'
ex gs  "$A/Asset Store-5.x/Gravity Sound/AudioSound FX/Soccer Sound Effects.unitypackage" $root/pk/gravsoccer.idx '(Kick Ball|Pass Ball|Run_|Post Hit|Goalie Save|Goal Mesh)'
ex ss  "$A/Asset Store-5.x/Soundstripe/AudioSound FX/Sports SFXs.unitypackage" $root/pk/soundstripe.idx '(Catch|Football|Gloves|Medicine|Shoe_Fric|Small_Run|Punching_Bag|Racket|Hits_Asph|Ball_Pass|Handling|Dribble|Jump|Running|Indoors|Outdoor_Two|Street_Two|Turn_Swipe|Standing_Bag|Roll_And|Bat_Hit|Slide|Weight|Barbell|Pin_Hit|Path_Dirt|Rope_Throw|Cable|Pullup|Maneuver_Ramp)'
ex wow "$A/Asset Store-5.x/WOW Sound/AudioSound FX/Anime Epic Combat Sound Effect Pack AAA.unitypackage" $root/pk/wow.idx '(Punch_Impact|Body_Falls|BoneBreak|SinglePunch|Final_Punch|P3_Punch_)'
ex gm  "$A/Gamemaster Audio/AudioSound FX/Pro Sound Collection.unitypackage" $root/pk/gamemaster.idx 'Gamemaster Audio - Pro Sound Collection/(Punches/|Foley/(foley_cloth_sports_glove|foley_combat|foley_object_throw|foley_jump|foley_cloth_light|foley_object_grab|foley_keys|foley_soldier_gear_equipment_(metal|rattle)|bone_break)|Whooshes/|Impacts_Smashable/rock_(impact|smashable_hit)|Miscellaneous/(Object Deep|Gravel|Wood/wood_block)|Footsteps/footstep_(grass|dirt|mud|water|snow|sand|gravel_slide|concrete_slide|ice_crunchy_slide)|Metal/(metal_hit|metal_impact|metal_med|metal_small|metal_tin|metal_sheet|metal_drum)|Snow_Ice/snowball|Voice/Human Male (A|B|C|D)/voice_male_[a-d]?_?(effort|grunt|groan|hurt|attack|growl|breath_(gasp|fast)))'
# then:  dotnet build -c Release -o <bin>   and   AudioTool sfxbuild Tools/AudioTool/SfxSpec.csv   (writes Assets/_Licensed/PIX/Audio/Sfx/*)
