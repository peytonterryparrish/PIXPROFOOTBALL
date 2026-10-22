# Animation coverage matrix (v0.8)

Generated from `Docs/ArtLibraryReport.md` (variants per ArtLibrary key) and the requirement list of the animation brief. Source: **mocap** = existing / newly found motion capture (Motus, BoNES, BGS "Yan" files, BoNES day-2/3 raw takes), **authored** = keyframed in Blender on the clean skeleton (`Tools/Blender/build_locomotion.py`), **derived** = built from similar takes (trim / mirror / layer, `ClipCleaner`). Status is OK when every listed key has at least one clip, PARTIAL when some do, MISSING when none do.

| Category | Required animation | Keys (variants) | Source | Status |
|---|---|---|---|---|
| Locomotion | Idle / stand | idle (3), idle_alt (4), idle_lively (13), idle_look (2) | mocap (Motus + BGS idles) | OK |
| Locomotion | Walk (cycle) | walk (2) | authored (Blender build_locomotion.py) | OK |
| Locomotion | Jog / run / sprint cycles | jog (2), run (4), sprint (4), sprint_alt (24) | mocap (Motus JR / Leon + BGS Run07/08/09, Sprint*) | OK |
| Locomotion | Ball carrier run set (8 directions, tucked ball) | bc_jog (5), bc_run (10), bc_sprint (9), bc_circle (16), bc_backpedal (8) | mocap (BGS MOB1 + Sprint BC) | OK |
| Locomotion | Sprint start from stance / idle | stance_to_sprint (6), backpedal_to_sprint (6), shuffle_to_sprint (6), cb_break (10) | mocap (Motus, BoNES studio, BGS CB) | OK |
| Locomotion | Stops (jog / run / sprint, left or right foot planted) | bc_stop (40), stop_sprint (18), slowdown (6) | mocap (BGS To_Stand_Relaxed LU/RU) | OK |
| Locomotion | Plant-and-cut 45 / 90 / 135 / 180, both sides | cut_left (3), cut_right (1), cut_hard (4), bc_turn_90 (16), bc_turn_180 (16), sprint_turn (10), route_cut (6), route_break (6) | mocap + mirrored copies | OK |
| Locomotion | Backpedal / shuffle / crossover | backpedal (6), strafe_left (2), strafe_right (2), shuffle_left (6), shuffle_right (6), shuffle_slow (4), cb_shuffle (12), bc_circle (16) | mocap | OK |
| Locomotion | Turn in place | turn_left (5), turn_right (5) | mocap (Motus Stand_Idle turns) + mirrored | OK |
| Pre-snap | Huddle stand / listen | huddle_idle (2), huddle_leader (1) | authored (hands on knees, hands on hips) + mocap (BoNES Leader_idle) | OK |
| Pre-snap | Huddle break / clap | coach_clap (1), qb_cadence_clap (1) | derived (coach clap, QB clap cadence) | OK |
| Pre-snap | Jog to the line | jog (2), bc_jog (5) | mocap | OK |
| Pre-snap | Set into stance - centre / snapper | stance_center (2), stance_snapper (2) | mocap (3pstance aggressive) + mirrored | OK |
| Pre-snap | Set into stance - OL 3-point | stance_ol_3pt_r (4), stance_ol_3pt_l (2) | mocap + mirrored | OK |
| Pre-snap | Set into stance - DL 3-point / 4-point / wide-9 | stance_dl_3pt (2), stance_dl_4pt (3), stance_wide9 (2) | mocap + mirrored | OK |
| Pre-snap | Set into stance - TE / RB / WR / LB / CB / S | stance_te (4), stance_rb (4), stance_wr (4), stance_lb (3), stance_cb_press (2), stance_cb_off (4), stance_s (2) | mocap (+ mirrored) | OK |
| Pre-snap | Set into stance - QB under centre / shotgun | qb_stance_under (6), qb_stance_shotgun (3) | mocap (BoNES QB) | OK |
| Pre-snap | Set into stance - kicker / holder | stance_kicker (2), stance_holder (2) | derived (standing idle) | OK |
| Pre-snap | QB cadence / hand signals | qb_cadence (3), qb_cadence_stomp (2), qb_cadence_clap (1), qb_cadence_raise (1), idle_lively (13) | mocap (BGS cadence remake + BoNES cadences) | OK |
| Pre-snap | WR motion / release | wr_release (4), route_cut (6) | mocap (Motus ReceiverTransitionReadyIdle to Motion L/R) | OK |
| Pre-snap | OL / DL get-off from 3-point | ol_stance (6), dl_move (6), stance_to_sprint (6) | mocap (Motus Oline / Dline transitions) | OK |
| QB | Snap + drop 3 / 5 / 7 | qb_snap_under (6), qb_drop_3 (2), qb_drop_5 (3), qb_drop_7 (2) | mocap | OK |
| QB | Shotgun catch + set | qb_snap_shotgun (6), qb_ready_pass (6) | mocap | OK |
| QB | Play-action fake / handoff / pitch | qb_fake_handoff (6), handoff (6), qb_toss (6) | mocap | OK |
| QB | Throws short / deep / on the run / off-balance | qb_throw (6), qb_throw_bullet (6), qb_throw_lob_short (6), qb_throw_lob_medium (4), qb_throw_lob_long (6), qb_throw_touch (6), qb_throw_offbalance (6), qb_throw_sidearm (6), qb_throw_shovel (6), qb_falling_throw (6), qb_run_left (6), qb_run_right (6) | mocap | OK |
| QB | Pump fake / throw away / hail mary | qb_pump_fake (6), qb_throw_away (4), qb_hail_mary (6) | mocap | OK |
| QB | Slide / kneel / spike / sneak / scramble | qb_slide (4), qb_kneel (4), qb_spike (4), qb_sneak (4), qb_scramble (6) | mocap | OK |
| OL / DL | Pass set kick-slide (pocket footwork) | ol_pocket_shuffle (16), ol_pocket_step (16), ol_pocket_stand (8) | mocap (BGS Linemen Pocket Movement) | OK |
| OL / DL | Run block drive / pull / cut block / double team | ol_run_block (6), ol_block_push (4), ol_pull (4), ol_block_cut (6), ol_block_engage (2) | mocap (Motus Block* + BoNES OLine footwork) | OK |
| OL / DL | Pass rush - swim / spin / bull / rip / wrong-arm | dl_rush_swim (6), dl_rush_spin (1), dl_bull_rush (6), dl_rush_rip (4), dl_wrong_arm (6) | mocap + derived (rip = escape take) | OK |
| OL / DL | Shed / pancake fall | dl_shed (3), ol_block_fail (6) | mocap | OK |
| RB | Cutback / juke left and right | juke_left (10), juke_right (10), juke_tempo (6), juke_box (4), rocker_step (5) | mocap (BGS Dodge / Juke + BoNES) + mirrored | OK |
| RB | Spin / stiff-arm L+R / truck / hurdle / dive | spin_move (5), stiff_arm (6), stiff_arm_left (6), stiff_arm_right (6), truck (8), hurdle (2), dive_forward (6) | mocap | OK |
| RB | Fumble recovery dive | fumble_dive (1), fumble (1) | mocap (BGS) | OK |
| WR / TE | Release vs press | wr_release (4), press_jam (2) | mocap + derived | OK |
| WR / TE | Catches high / low / chest / over-shoulder / diving | catch_high (6), catch_low (6), catch_run (6), catch_side (6), catch_jump (6), catch_dive (4) | mocap | OK |
| WR / TE | One-hand / toe-tap catch | catch_one_hand (3), catch_toe_tap (3) | derived | OK |
| WR / TE | Contested catch / fair catch / return catch | catch_contested (6), catch_fail (6), fair_catch (4), kick_return_catch (2) | mocap | OK |
| DB / LB | Press jam / backpedal / turn and run | press_jam (2), backpedal (6), man_turn_run (6), man_coverage (6) | mocap | OK |
| DB / LB | Break on the ball / read step | cb_break (10), cb_rotate (6) | mocap (BGS CB) | OK |
| DB / LB | Zone drop / blitz | zone_drop (6), idle_alt (4) | mocap | OK |
| DB / LB | Swat / interception | swat (8), interception (6) | mocap (Motus Swat / Int) | OK |
| Tackles | Wrap / big hit / shoestring / dive | tackle_wrap (6), tackle_big_hit (6), tackle_dive (6), tackle_success (3), tackle_arm (6), contact_tackle (70) | mocap (paired) | OK |
| Tackles | Gang / strip / push out of bounds | tackle_gang (8), tackle_strip (4), push_oob (2) | mocap + derived | OK |
| Tackles | From behind / sack | contact_sack (31), qb_sack (6) | mocap (paired) | OK |
| Falls and get-ups | Falls, get-ups | fall (6), be_tackled (6), be_trucked (6), injury (6), injury_idle (2) | mocap (get-ups are played from the pair takes by ContactDirector) | OK |
| Kicking | Kickoff / onside | kick_off (1), kick_onside (1) | mocap (Yan rework) | OK |
| Kicking | Punt (normal / fast) | punt (3), punt_fast (1) | mocap (Yan rework) | OK |
| Kicking | Field goal / PAT kicker, holder, long snap | kick_field_goal (1), kick_holder (1), long_snap (1) | mocap (Yan rework, Howards hike) | OK |
| Celebrations | Touchdown / big play / frustration | celebrate_td (6), celebrate_td_2 (4), celebrate_td_3 (4), celebrate_qb (6), cheer (6), frustration (6), salute (2), dance (2) | mocap | OK |
| Referees | Signals / spotting / walking | ref_signal_td (1), ref_signal_first_down (1), ref_signal_penalty (6), ref_signal_incomplete (1), ref_signal_timeout (2), ref_signal_safety (1), ref_signal_dead_ball (1), ref_whistle (1), ref_spot_ball (1), ref_walk (1), ref_run (3), ref_idle (1) | mocap (BGS referee) + keyframed | OK |
| Sideline | Bench sit / coach idles | bench_sit (2), coach_ambient (6), coach_clap (1), coach_headset_idle (1), coach_clipboard_idle (1), idle_conversation (5), idle_helmet (4) | mocap (BoNES Ambient_bench / coach) + keyframed | OK |

Rows OK: 52, PARTIAL: 0, MISSING: 0
