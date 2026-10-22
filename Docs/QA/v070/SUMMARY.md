# v0.7.0 "Motion": per-note status (release integration)

Scripted summary, no re-verification beyond the release gates. Evidence roots: REL = `D:\PIXFootball\Renders\v070_release\autotest\` (shipped player, `-pixAutotest`, 2560x1440), C = `D:\PIXFootball\Renders\v070_c\`, A = `D:\PIXFootball\anim_qb\shots\`, D = `D:\PIXFootball\Renders\v070_d\`.

Gates: full EditMode suite 869 / 869 green (`D:\PIXFootball\Logs\v070_full_final.xml`), build `D:\PIXFootball\Builds\PIXFootball_v0.7.0_Windows` (1,983 MB, 0 errors) plus zip, autotest 0 errors / exceptions. Built player FPS at 2560x1440 High: live play 107.2 avg / 70.3 1%-low / 37.1 min; broadcast layer on 99.7 / 72.1 / 59.0; replays and shows 107.8 / 72.0 / 44.2.

| # | Status | Evidence |
|---|---|---|
| 1 defense ring / marker | done | REL match_carry_1.png (marker over the controlled WR), C probe7*\c_defense_* |
| 2 resolution fit | done (maths + shots at 7 sizes; the player ran at 2560x1440) | C ui\*, V070ControlsTests.EveryAspectKeepsTheWholeReferenceRectangleOnScreen |
| 3 play ended with no reason | done | REL match_tackle_1.png ("TACKLED BY #59"), PaceAndPlayEndTests, gap audit in engine_pace.md |
| 4 QB stances | done | A qb_drop5_undercenter_side.png, REL match_presnap_1.png (QB under center) |
| 5 janky movement | partly (planted-foot stride, grounded stances; back-pedal slides remain) | anim_qb.md metrics, REL match_carry_1.png |
| 6 mocap clean-up | partly (derived / re-twisted clips; ongoing) | anim_qb.md |
| 7 simulated and calculated | done (commit windows) | AnimFlowTests, PaceAndPlayEndTests |
| 8 too fast | done | engine_pace.md table (600-game runs) |
| 9 let animations play out | done | anim_qb.md A-006 / A-009, REL contact_wrap_*.png |
| 10 hold LT routes | done | C probe4\c_routes_presnap.png, c_routes_live_qb.png (real-match probe; not a step of the shipped-player autotest) |
| 11 / 20 / 23 call sheet, navigation | done in the strip; no shipped-player shot of the strip in the standard autotest | C probe4\c_sheet_*, C ui\sheet_*, V070ControlsTests |
| 12 WR stance | done | A stances_side.png, REL match_presnap_1.png |
| 13 right-foot splay | done (data + clamp) | anim_qb.md A-001 / A-013, REL match_carry_1.png |
| 14 QB scramble | done | A qb_rollout_side.png |
| 15 QB operates like a QB | done (right-handed only) | A qb_* film strips |
| 16 hold = bullet, tap = lob | done | V070ControlsTests.HoldIsABulletAndTapIsALobOrTouch |
| 17 OL stance | done | REL match_presnap_1.png, A stances_side.png |
| 18 MLB / CB stance | done | A stances_side.png, REL match_presnap_1.png |
| 19 graphics | partly (15 bodies, pads, turf, dirt, snow; day-game ambience, skin shading open) | D final_b\match_*.png, REL match_throw_close_1.png |
| 21 two logos | done: one logo at midfield from the default camera | REL match_presnap_1.png, match_playcall.png |
| 22 tackle bugs | done | REL contact_*.png, B-002 / B-003 |
| 24 / 25 blending, simulated flow | done (commit windows, blended ball) | anim_qb.md |
| 26 floating | partly (back-pedal below 3 m/s) | anim_qb.md |
| 27 replay shows no players | done (22 actors, full body via size + 3 * base) | REL bc_replay_a1.png, C replay2\* |
| 28 FG / kickoff too fast | done | REL match_special_punt.png (kick prompt), match_kick_close_1.png, engine_pace.md B-009 / B-010; kick meter not captured mid-run by the autotest |
| 29 ball in hand, TD celebration | done | REL bc_shot_celebration.png, match_td_close.png |
| 30 out-of-sync actions | done | A catch_quick_side.png, kick_kickoff_side.png, REL match_throw_close_1.png |
| 31 AAA menus | done | REL menu.png (hub), C menus\matchup_2560x1440.png (matchup card) |

Hand-off fixes made at integration: replay actors carry the full body (`ReplayClip.BodyKinds` = 15); `BuildScript` whitelists `photo_player_*` (the 12 new body prefabs were missing from the slim player library); the DL lunge cap is 1.0x top speed (`NoPlayerExceedsRealisticTopSpeed`).
