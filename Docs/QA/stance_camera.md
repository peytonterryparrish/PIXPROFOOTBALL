# Stances, facing and cameras (track STN / CAM) - v0.6

Owner: STANCE & CAMERA track. User notes 3, 5, 6 (plus the All-Pro 2K8 direction: Classic camera, bottom scorebug, markers, coin toss, kick meter, wind, stat strip).
Row format: `| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |` (see `README.md`).

## Root cause of "OL, DL and corners face backwards" (notes 5 and 6)
The match avatar carries ONE fixed yaw (`PlayerAvatar.ModelYaw` = -90). That is right for the Motus locomotion cycles only. Measuring every clip of the ArtLibrary on a match avatar (`PIX Football > Assets > Build Clip Facing`, table in `Docs/QA/stance_gallery/clip_facing.csv`) shows the packs are authored facing other ways:

| clips | heading on the avatar | who it affected |
|---|---|---|
| Motus locomotion (idle, jog, run, sprint) | 0 | correct |
| Motus `DlineIdle3ptR` / `V2` (the only OL, C, TE and DL stance) | +133 | every lineman faced backwards / over his shoulder |
| Motus `LinebackerBlitzIdleOne/Two` (the LB stance) | -153 / -162 | linebackers faced backwards |
| BoNES studio `2pstance_safety` / `halfback`, `3pstance_*`, quarterback loops | -88 .. -92 | CB, safeties, WR, RB, QB faced sideways |
| Motus block takes (`BlockHit*Stale_A`, `BlockDrive*Stale_A`) | 36 .. 180 | engaged blockers faced away from their man |
| Quarterback shuffle takes used for back-pedal / strafe in the locomotion tree | -63 / -131 / -135 | defenders back-pedalling / shuffling faced sideways or backwards |

Nothing corrected it. The first gallery (before any fix, bodies measured from the hips and shoulders) had **60 of 1,232 pre-snap bodies facing the right way (4.9 %)**; the earlier autotests only took screenshots and never compared the pose with the simulation facing, which is why nobody saw it. The simulation facing itself was right in 100 % of the cases (offense +Y, defense -Y, returners at the kicker).

Fix, two layers: (1) `ClipFacingTable` offset per clip (feed forward, measured offline, also handles Humanoid mirror twins of the stance clips for left / right handedness); (2) a closed loop in `SimAgentView.LateUpdate` that measures the hips heading every frame and turns the body onto the simulation heading for plain locomotion / stance / block actions. Details in `Docs/ARCHITECTURE.md` (v0.6 stances, facing and the user camera). The import-side alternative (bake the facing into the clips, QA-898 / QA-1031) stays with the ANIMATION track: the loop makes either way safe, and a re-import only needs `Build Clip Facing` to be re-run.

## Formation Gallery (`D:\PIXFootball\Renders\stance_camera\formations\`)
`PixFootball.EditorTools.StanceGallery.Run` (`-pixGallery -pixGalleryMode formations|camera|hud|all`, works with `-batchmode` and without `-nographics`) plays the real `PlayStage` on the stadium scene: 28 scenarios (12 offensive formations, 5 defensive fronts, kickoff, squib / kick return left, onside, punt, directional punt, field goal x2, PAT, two-point try, kneel, spike) x both attack directions (+Z / -Z), plus a celebration run. Per player it checks numerically: simulation facing, body facing (hips + shoulders from the bones), stance clip, stance height, foot side, T-pose, live body-vs-root consistency at snap + 0.6 s and + 1.5 s, and celebration facing; and renders `*_presnap_top.png` (arrows: green = simulation facing, cyan = body facing, red = body facing out of tolerance, labels = position + stance), `_qb.png` (the Classic user camera), `_side.png`, `_line.png` (low line close-up) and `_snap06_top.png`. `results.csv` has every check, `summary.txt` the pass rate, `baseline/` the same run with the fix switched off (`-pixNoClipFacing`).

## Gallery pass rate (every check of every player in every scenario)

### baseline
| check | pass | total | rate |
|---|---|---|---|
| SIM_FACING | 1232 | 1232 | 100.0% |
| VISUAL_FACING | 60 | 1232 | 4.9% |
| NOT_TPOSE | 2455 | 2455 | 100.0% |
| STANCE_CLIP | 1232 | 1232 | 100.0% |
| STANCE_LOW | 983 | 1026 | 95.8% |
| FOOT_SIDE | 71 | 128 | 55.5% |
| LIVE_BODY_VS_ROOT | 688 | 1223 | 56.3% |
| LIVE_ROOT_VS_SIM | 1082 | 1082 | 100.0% |
| **all** | 7803 | 9610 | 81.2% |

### fixed
| check | pass | total | rate |
|---|---|---|---|
| SIM_FACING | 1232 | 1232 | 100.0% |
| VISUAL_FACING | 1232 | 1232 | 100.0% |
| NOT_TPOSE | 2598 | 2598 | 100.0% |
| STANCE_CLIP | 1232 | 1232 | 100.0% |
| STANCE_LOW | 1024 | 1026 | 99.8% |
| FOOT_SIDE | 128 | 128 | 100.0% |
| LIVE_BODY_VS_ROOT | 1349 | 1352 | 99.8% |
| LIVE_ROOT_VS_SIM | 1077 | 1077 | 100.0% |
| CELEBRATION_FACING | 14 | 14 | 100.0% |
| **all** | 9886 | 9891 | 99.9% |

## User camera (note 3) - what it does
`CameraMode.Behind` is the user-play camera; `UserCameraMath` (pure, EditMode tested) turns what the simulation says (`UserCameraView.Build`) into a pose, `MatchCameraRig` smooths it (along the view direction the camera lags a little, sideways it tracks tighter) and drives the Cinemachine camera.

| preset | height | behind the line | pitch | lens (vertical) | notes |
|---|---|---|---|---|---|
| Classic (default, All-Pro 2K8) | 9.0 m | 17 m (about 12 m behind a shotgun QB) | ~26 deg | 46 | whole formation and 15-20 yd of field |
| Madden | 5.4 m | 10.5 m | ~13 deg | 48 | over the quarterback's shoulders |
| Wide | 14.5 m | 21 m | ~26 deg | 42 | whole width, the secondary |

- **Offense:** behind the offense looking downfield, centred on the ball. At the snap it pulls back 3.2 m, up 1.8 m and widens 3 degrees over 0.9 s ("Zoom out at the snap" 0..1 in Settings) to show the pocket and the routes; after a handoff / catch it follows the ball carrier from behind (10.5 m back, 6.4 m up, 0.5 s lead of his velocity); the ball in the air is followed with a capped lead.
- **Defense:** behind the defense looking at the offense (10 % deeper / 8 % higher than the offense framing), the stick basis follows the view; it follows the ball / carrier from the defense side.
- **Kicks:** kicking team behind the kicker looking downfield; return team behind the returner looking at the kicker; the ball in flight is followed.
- **Direction changes** (possession, quarter): the view direction is the attack direction of the user's side in the `PlayFrame`; both end zones are mirror symmetric (tested).
- **Broadcast camera:** CPU-vs-CPU games and an option (Settings > Camera, pause menu, View button last in the cycle). With a human playing the broadcast director makes no cut before the snap after the intro and keeps post-whistle cuts under 3 s.
- **Settings > Camera:** Camera (Classic / Madden / Wide / Broadcast / First person), height, distance, zoom-out at the snap, field of view, first-person facemask, pre-snap stars. Scorebug layout (bottom bar default / top block) is on the Gameplay page and in the pause menu.

Renders (`D:\PIXFootball\Renders\stance_camera\camera\`, produced by `-pixGalleryMode camera` with the real `MatchCameraRig`): `pass_offense`, `run_offense`, `pass_defense`, `run_defense`, `kickoff_kicking`, `kickoff_returning`, `punt_kicking`, `fieldgoal` x both directions (`plusZ` / `minusZ`) at `t_pre` (pre-snap), `t_0.5`, `t_1.5`, `t_2.6` (+ `t_3.6`) seconds after the snap and `after_catch`. HUD pictures (`D:\PIXFootball\Renders\stance_camera\hud\`): `bottom_bar`, `top_block`, `kick_meter_banner`, `coin_toss`, with the teal user ring, the gold receiver stars and the wind compass.

## STN rows: root causes, logic, formations, markers, toss (STN-801 ..)

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| STN-801 | fixed | S1 | root cause | SimAgentView / ClipFacingTable | The match avatar has ONE fixed yaw (`PlayerAvatar.ModelYaw` -90) that is right for the Motus locomotion cycles only; every other pack is authored facing another way on that rig: Motus line stance +133, linebacker blitz idles -153..-170, BoNES studio and QB takes -90, Motus block takes 180. OL / DL / LB / CB / safeties / WR / QB / RB therefore faced backwards or sideways (gallery baseline: 60 of 1232 pre-snap bodies faced the right way, 4.9 %). | `ClipFacingAudit` measures the heading of all 947 clips (`D:\PIXFootball\Renders\stance_camera\formations\clip_facing.csv`) into `ClipFacingTable` (offset per clip, feed forward) and `SimAgentView` turns the body by it while the clip's weight is up (smoothed, no pop). | gallery VISUAL_FACING 60/1232 -> 1232/1232 (`D:\PIXFootball\Renders\stance_camera\formations\baseline/summary.txt` vs `summary.txt`) |
| STN-802 | fixed | S1 | root cause | SimAgentView.UpdateFacingLoop | Locomotion blend trees mix clips with different headings (back-pedal -63, strafes -131 / -135 from the quarterback shuffle takes, shuffles -87..-92): defenders back-pedalling or shuffling faced sideways / backwards, blocking takes (BlockHit / BlockDrive A actors) 36-180 degrees off. A table keyed by clip name cannot see blends. | Closed-loop correction: every LateUpdate the hips heading of the posed body is measured and the body turned onto the simulation heading for plain locomotion / stance / block actions (throws, spins, tackles, falls are left alone, 0.09 s time constant, 8 degree dead zone). Also makes a stale / missing table harmless. | gallery LIVE_BODY_VS_ROOT 688/1223 -> 1204/1221 |
| STN-803 | fixed | S1 | stance logic | StanceRules / StanceClips / PlayStage | One pre-snap stance for all 22 players (no per-position stance, no left / right handedness, special teams all in locomotion idle). | `StanceRules.For` derives the stance from position + alignment + formation (C snap stance, OL / TE 3-point, goal-line interior 4-point, QB under center vs shotgun, WR split vs slot, LB 2-point, CB press vs off by depth, safeties deep, gunners, returners, kickers); `StanceClips` picks the take (preferred takes by name, audited middle of the long loops) and the mirror-image twin by side of the ball; refreshed on audible / flip / shift. | EditMode StanceCameraTests, gallery STANCE_CLIP 1232/1232 |
| STN-804 | fixed | S2 | stance side | ClipFacingAudit mirror twins | Left and right tackles / ends / receivers used the same right-handed stance clip (outside foot back wrong on one side, down hand not mirrored across the ball). | The audit builds Humanoid-mirror twins of the stance clips (`Assets/_Licensed/Resources/MirroredClips`, 16 clips) and `StanceClips` picks the twin whose hand / foot matches the side of the ball (inside hand down for OL / DL, outside foot back for receivers). | gallery FOOT_SIDE 71/128 -> 116/128 (rest = idle weight shifts inside the long take) |
| STN-805 | fixed | S1 | formation | DefaultPlaybooks (Core), SimAgentView setback | OL / DL looked stacked and overlapping (OL 1.5 yd splits = 2.5 ft gaps, DL 1.2 yd off the ball, lineman bodies lean 0.5 m past their centre so helmets cross the ball). | Splits tightened to 1.25 yd (OL, TE, interior DL, ends) and DL to 1.0 yd off the ball in all scrimmage formations; linemen in a 3-point stance are pulled back 0.36 m from their simulated centre so the helmets meet across a ball-wide neutral zone. | gallery presnap_line.png, EditMode LinemenLineUpTightAcrossTheNeutralZone |
| STN-806 | fixed | S1 | formation | DefaultPlaybooks.Offense | I-Form Slot (20 personnel) had only 6 men on the line: an illegal formation (the sim flags Illegal Formation on every snap). | Z moved onto the line (7 on the line). | code |
| STN-807 | fixed | S3 | formation | DefaultPlaybooks.Special | Field-goal kicker at the same depth as the holder. | Kicker 1.5 yd behind and 1.5 yd off the holder. | gallery ST Field Goal |
| STN-808 | fixed | S2 | sim facing | PlaySimulation pre-snap | The man in motion turned 90 degrees to the sideline (facing followed the lateral motion). | Motion man steps with an explicit facing (0,1): he shuffles across the formation square to the line. | code |
| STN-809 | fixed | S2 | sim facing | OffenseAI.Qb QbFake | A fake with no runner to sell to made the QB face his own end zone for 0.3-0.45 s. | Fallback facing is downfield. | code |
| STN-810 | fixed | S3 | frame | PlayFrame.DirToUnity | Degenerate-direction fallback returned +Z for a -Z attacking frame. | Returns the frame's own downfield direction. | EditMode PlayFrameMapsFacing... |
| STN-811 | fixed | S3 | stance refresh | PlayStage.Present | Stance computed once at Begin; flips, audibles, motion and defensive shifts rewrite positions afterwards so handedness / press vs off stayed stale. | Stance recomputed every pre-snap frame from the current alignment; a changed stance replays its clip. | code |
| STN-812 | fixed | S2 | stance clips | StanceClips | Long loops windowed with a hard-coded 2.8 s trim that did not match the 21-39 s takes (T-pose / wander at the ends). | Loop the 40-60 % middle of long takes (the part the audit measured), stagger inside it. | gallery |
| STN-813 | fixed | S3 | facing | SimAgentView | Clip facing offset switched instantly on a new plan (whole body popped during the cross-fade). | Offset eased at 900 deg/s and scaled by the clip weight. | code |
| STN-814 | fixed | S3 | facing | PlayStage.PlayReaction | Cheer / frustration reactions and get-ups played with no facing correction. | Reactions note their clip's offset (`SimAgentView.NoteClip`); contact takes keep their own alignment. | gallery CELEBRATION_FACING |
| STN-815 | fixed | S2 | tooling | ClipFacingTable | Missing table silently returned 0; a stale table (re-imported clips) double-corrected. | One-time warning when missing; entries store the clip length and a changed length is ignored with a warning; the closed loop absorbs the rest. Rebuild with PIX Football > Assets > Build Clip Facing after every Build ArtLibrary. | EditMode, code |
| STN-816 | open | S2 | animation (ANI) | ArtLibrary keys | OL / C / TE / DL share the Motus DlineIdle3ptR family, WR / CB / S / returners share one BoNES 2-point clip (`2pstance_safety_001`), RB the halfback take; there are no distinct press / off corner, centre-with-hand-on-ball, 4-point, wide-9 takes (ANI v0.8 added per-role keys to `StanceRules.ClipKeys`; the gallery re-run after their clips land is the check). | Stance selection by role is in place; ANI supplies the clips. | - |
| STN-817 | open | S2 | animation (ANI) | holder / kicker / snapper / punter | No kneeling holder, kicking-stance kicker or long-snapper take: they stand in the locomotion idle (ANI added StanceKicker / Holder / Snapper keys). | `StanceKind.Kicker` / `Upright` play the new keys when present. | - |
| STN-818 | open | S2 | sim (SIM) | OffenseAI / DefenseAI | Route runners, zone defenders and pre-engagement blockers get no explicit facing (they face their travel direction): a hitch / curl turns his back to the QB, deep safeties do not turn to the receiver (QA-367, QA-424, QA-628). | Not changed (route / coverage logic belongs to the engine track). | - |
| STN-819 | open | S3 | animation (ANI) | contact | Double teams / combo blocks only pull and turn the primary pair (QA-679); fall poses / superman dive (QA-088, QA-203). | - | - |
| STN-820 | fixed | S2 | formation | PlaySimulation.ClampInField | Wide receivers lined up on the sideline stripe when the ball was on a hash and the split was wide. | Pre-snap placement clamps 2 yd inside the boundary. | code |
| STN-821 | fixed | S2 | markers | MatchDirector.PresentFrame | The first-down / scrimmage lines were redrawn from the NEW game state with the OLD frame after the whistle (wrong yard line, QA-675). | Lines are left alone during the post-play wind-down. | code |
| STN-822 | fixed | S3 | officials | MatchOfficials.BeginPlay | Crew sprinted 39 m across the field at 6 m/s after a frame flip. | Crew re-spots instantly when the offense changes direction. | code |
| STN-823 | fixed | S2 | markers | FieldMarkers | User marker was a violet spinning ring. | Bright teal ring (procedural texture, thick band + inner line); gold five-point stars under every eligible receiver before the snap (Settings > Camera > Pre-snap stars). | `D:\PIXFootball\Renders\stance_camera\hud\` |
| STN-824 | fixed | S2 | coin toss | MatchDirector.Toss / PlayStage.BeginToss / MatchOfficials.PlaceForToss | The coin toss was a full-screen black modal. | On-field toss: referee at midfield with two captains per team, close camera, caption strip ("Rollers choose tails." style) and the call / receive buttons in the lower third (`SetTossBackdrop`, `SetTossCaption`). | `D:\PIXFootball\Renders\stance_camera\hud\coin_toss.png` |
| STN-825 | wontfix | S3 | formation | DefaultPlaybooks | Pistol Trips repeats the base Shotgun trips-right look; DL / OL placement uses magic unit strings in StanceRules (QA-524, QA-654). | Data change belongs to the engine track and would shift every play's calibration; guarded by tests. | - |
| STN-826 | open | S2 | flow | MatchDirector | Possible empty kickoff formation after halftime (QA-262): not reproducible in the gallery (formations build for every kickoff scenario). | - | - |

## STN rows: Formation Gallery, one row per position x formation x direction x check that failed in the baseline run (STN-1001 ..)

Status = fixed when the same check passes in the final run (results.csv next to the renders), open otherwise. Fix: STN-801 clip facing, STN-802 closed-loop heading, STN-803 stances, STN-804 mirrored stances.

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| STN-1001 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1002 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 93deg | STN-802 | gallery pass |
| STN-1003 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1004 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1005 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1006 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -44, shoulders -48, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1007 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 50deg | STN-802 | gallery pass |
| STN-1008 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1009 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1010 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / LG LG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1011 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -39 (hips -40, shoulders -38, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1012 | fixed | S2 | stance pose | DEF 3-4 Base vs Goal Line / -Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.35m | STN-803 | gallery pass |
| STN-1013 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -47, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1014 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 104deg | STN-802 | gallery pass |
| STN-1015 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -84 (hips -89, shoulders -80, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1016 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 77 (hips 88, shoulders 65, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1017 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 102 (hips 92, shoulders 112, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1018 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1019 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1020 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -47, shoulders -52, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1021 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 54 (hips 55, shoulders 54, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1022 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / RT RT / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1023 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1024 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 88deg | STN-802 | gallery pass |
| STN-1025 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1026 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / TE Y2,Y (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1027 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / TE Y,Y2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1028 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / -Z / WR Z / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1029 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / -Z / WR Z / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1030 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 128, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1031 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 91deg | STN-802 | gallery pass |
| STN-1032 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1033 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -51 (hips -48, shoulders -54, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1034 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1035 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1036 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 51deg | STN-802 | gallery pass |
| STN-1037 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 131, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1038 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -46, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1039 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / LG LG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1040 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1041 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 95 (hips 91, shoulders 98, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1042 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 135, shoulders 128, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1043 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 96deg | STN-802 | gallery pass |
| STN-1044 | fixed | S2 | stance pose | DEF 3-4 Base vs Goal Line / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.83m | STN-803 | gallery pass |
| STN-1045 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 10 (hips 4, shoulders 16, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1046 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -106 (hips -93, shoulders -119, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1047 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -84, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1048 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -52 (hips -52, shoulders -51, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1049 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1050 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1051 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 23 (hips 25, shoulders 21, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1052 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1053 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 92deg | STN-802 | gallery pass |
| STN-1054 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 111 (hips 94, shoulders 129, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1055 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / TE Y / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 104deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1056 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / TE Y,Y2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 137, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1057 | fixed | S2 | facing (live) | DEF 3-4 Base vs Goal Line / +Z / WR Z (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1058 | fixed | S1 | facing | DEF 3-4 Base vs Goal Line / +Z / WR Z / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1059 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1060 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1061 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1062 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / -Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1063 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1064 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1065 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1066 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1067 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1068 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -156 (hips -154, shoulders -157, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1069 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 82 (hips 88, shoulders 75, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1070 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 95 (hips 91, shoulders 99, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1071 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1072 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -40 (hips -41, shoulders -40, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1073 | fixed | S2 | stance pose | DEF 4-3 Base vs I-Form / -Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.15m | STN-803 | gallery pass |
| STN-1074 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -39 (hips -41, shoulders -38, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1075 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1076 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1077 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -50, shoulders -50, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1078 | fixed | S2 | stance side | DEF 4-3 Base vs I-Form / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.33m | STN-804 | gallery pass |
| STN-1079 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / -Z / WR X,Z (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1080 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / -Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 116 (hips 95, shoulders 136, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1081 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / +Z / C C / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 171deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1082 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 133, shoulders 126, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1083 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / +Z / CB  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 117deg clip DLineMoves__DLINE_ReachBlockShedR_Failure_001_A | STN-802 | gallery pass |
| STN-1084 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 111 (hips 94, shoulders 128, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1085 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -39 (hips -40, shoulders -38, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1086 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / +Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1087 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1088 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 131, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1089 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -48, shoulders -46, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1090 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1091 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 25 (hips 26, shoulders 23, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1092 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1093 | fixed | S2 | stance pose | DEF 4-3 Base vs I-Form / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.19m | STN-803 | gallery pass |
| STN-1094 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -57 (hips -84, shoulders -30, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1095 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -87 (hips -89, shoulders -85, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1096 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -48 (hips -47, shoulders -49, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1097 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 130, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1098 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / +Z / ROLB  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 57deg | STN-802 | gallery pass |
| STN-1099 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 94 (hips 91, shoulders 98, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1100 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / +Z / RT RT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1101 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 132, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1102 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 112 (hips 94, shoulders 129, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1103 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / +Z / TE Y / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1104 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1105 | fixed | S2 | facing (live) | DEF 4-3 Base vs I-Form / +Z / WR X,Z (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1106 | fixed | S1 | facing | DEF 4-3 Base vs I-Form / +Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -81 (hips -88, shoulders -74, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1107 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1108 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / CB  (x4) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1109 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1110 | fixed | S2 | facing (live) | DEF Dime vs I-Form Slot / -Z / FB F (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1111 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -43, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1112 | fixed | S2 | facing (live) | DEF Dime vs I-Form Slot / -Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 48deg | STN-802 | gallery pass |
| STN-1113 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1114 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 133, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1115 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -49 (hips -48, shoulders -50, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1116 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -50, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1117 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -124 (hips -126, shoulders -122, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1118 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 141 (hips 100, shoulders -177, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1119 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 89 (hips 90, shoulders 89, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1120 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1121 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -48, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1122 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1123 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1124 | fixed | S2 | stance side | DEF Dime vs I-Form Slot / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1125 | fixed | S2 | facing (live) | DEF Dime vs I-Form Slot / -Z / WR H,X,Z (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1126 | fixed | S1 | facing | DEF Dime vs I-Form Slot / -Z / WR H,X,Z (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 136, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1127 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 137, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1128 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / CB  (x4) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 116 (hips 95, shoulders 136, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1129 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -46, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1130 | fixed | S2 | facing (live) | DEF Dime vs I-Form Slot / +Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 145deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1131 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1132 | fixed | S2 | facing (live) | DEF Dime vs I-Form Slot / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 48deg | STN-802 | gallery pass |
| STN-1133 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1134 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -46, shoulders -45, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1135 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1136 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1137 | fixed | S2 | stance pose | DEF Dime vs I-Form Slot / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.82m | STN-803 | gallery pass |
| STN-1138 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -100 (hips -92, shoulders -108, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1139 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -91 (hips -90, shoulders -93, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1140 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -82, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1141 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -44, shoulders -42, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1142 | fixed | S2 | facing (live) | DEF Dime vs I-Form Slot / +Z / RG RG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 173deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1143 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1144 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1145 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 112 (hips 94, shoulders 129, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1146 | fixed | S2 | stance side | DEF Dime vs I-Form Slot / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1147 | fixed | S2 | facing (live) | DEF Dime vs I-Form Slot / +Z / WR H,X,Z (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1148 | fixed | S1 | facing | DEF Dime vs I-Form Slot / +Z / WR H,X,Z (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -72 (hips -86, shoulders -58, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1149 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / -Z / C C / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1150 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -47, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1151 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1152 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1153 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / -Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1154 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -39 (hips -40, shoulders -38, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1155 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -72 (hips -86, shoulders -58, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1156 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 135, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1157 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / -Z / LG LG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 75deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1158 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1159 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / -Z / LOLB  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 37deg | STN-802 | gallery pass |
| STN-1160 | fixed | S2 | stance pose | DEF Goal Line Defense vs I-Form / -Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.62m | STN-803 | gallery pass |
| STN-1161 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -47, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1162 | fixed | S2 | stance pose | DEF Goal Line Defense vs I-Form / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.82m | STN-803 | gallery pass |
| STN-1163 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -80, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1164 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 129 (hips 97, shoulders 162, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1165 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 91 (hips 90, shoulders 92, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1166 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1167 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -43, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1168 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / -Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 86deg | STN-802 | gallery pass |
| STN-1169 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 93 (hips 94, shoulders 92, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1170 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -47, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1171 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1172 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / -Z / WR X,Z (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1173 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / -Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 96 (hips 91, shoulders 101, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1174 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1175 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 136, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1176 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -44 (hips -45, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1177 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / +Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1178 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1179 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 90 (hips 90, shoulders 89, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1180 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -47, shoulders -46, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1181 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1182 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 96 (hips 91, shoulders 102, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1183 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 140 (hips 139, shoulders 140, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1184 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 11 (hips 8, shoulders 15, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1185 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -31 (hips -79, shoulders 17, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1186 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -82, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1187 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -40 (hips -41, shoulders -39, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1188 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 130, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1189 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / +Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 93deg | STN-802 | gallery pass |
| STN-1190 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 26 (hips 28, shoulders 24, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1191 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 136, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1192 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 129, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1193 | fixed | S2 | stance side | DEF Goal Line Defense vs I-Form / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1194 | fixed | S2 | facing (live) | DEF Goal Line Defense vs I-Form / +Z / WR X,Z (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1195 | fixed | S1 | facing | DEF Goal Line Defense vs I-Form / +Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1196 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1197 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / CB  (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1198 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1199 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -77 (hips -87, shoulders -67, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1200 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 138, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1201 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -48, shoulders -55, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1202 | fixed | S2 | facing (live) | DEF Nickel vs Shotgun Doubles / -Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 85deg | STN-802 | gallery pass |
| STN-1203 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -157 (hips -155, shoulders -158, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1204 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1205 | fixed | S2 | stance pose | DEF Nickel vs Shotgun Doubles / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.85m | STN-803 | gallery pass |
| STN-1206 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -59 (hips -58, shoulders -61, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1207 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 102 (hips 92, shoulders 113, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1208 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 131, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1209 | fixed | S2 | facing (live) | DEF Nickel vs Shotgun Doubles / -Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1210 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -46, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1211 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -43, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1212 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1213 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -50, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1214 | fixed | S2 | stance side | DEF Nickel vs Shotgun Doubles / -Z / WR H / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1215 | fixed | S2 | facing (live) | DEF Nickel vs Shotgun Doubles / -Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1216 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 90 (hips 90, shoulders 89, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1217 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1218 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / CB  (x3) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1219 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -48 (hips -48, shoulders -49, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1220 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1221 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -51 (hips -51, shoulders -50, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1222 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1223 | fixed | S2 | facing (live) | DEF Nickel vs Shotgun Doubles / +Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 84deg | STN-802 | gallery pass |
| STN-1224 | fixed | S2 | stance pose | DEF Nickel vs Shotgun Doubles / +Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.04m | STN-803 | gallery pass |
| STN-1225 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1226 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 94 (hips 91, shoulders 97, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1227 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -40 (hips -82, shoulders 3, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1228 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -82, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1229 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -48 (hips -49, shoulders -48, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1230 | fixed | S2 | facing (live) | DEF Nickel vs Shotgun Doubles / +Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1231 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1232 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1233 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1234 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1235 | fixed | S2 | stance side | DEF Nickel vs Shotgun Doubles / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1236 | fixed | S2 | facing (live) | DEF Nickel vs Shotgun Doubles / +Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1237 | fixed | S1 | facing | DEF Nickel vs Shotgun Doubles / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1238 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -51, shoulders -50, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1239 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1240 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1241 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -75 (hips -87, shoulders -64, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1242 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 140 (hips 139, shoulders 140, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1243 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -47, shoulders -55, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1244 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / -Z / LOLB  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 104deg | STN-802 | gallery pass |
| STN-1245 | fixed | S2 | stance pose | OFF Empty vs 4-3 Base / -Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.80m | STN-803 | gallery pass |
| STN-1246 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -159 (hips -158, shoulders -160, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1247 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -47, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1248 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 130, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1249 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1250 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -156 (hips -154, shoulders -157, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1251 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / -Z / RT RT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1252 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1253 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / -Z / SS  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Run) -> 42deg | STN-802 | gallery pass |
| STN-1254 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1255 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -49, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1256 | fixed | S2 | stance side | OFF Empty vs 4-3 Base / -Z / WR W / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1257 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / -Z / WR X,Z,H,W (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1258 | fixed | S1 | facing | OFF Empty vs 4-3 Base / -Z / WR W,X,Z,H (x4) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 94, shoulders 132, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1259 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / +Z / C C / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1260 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1261 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1262 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -49 (hips -48, shoulders -50, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1263 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 87 (hips 90, shoulders 85, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1264 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -50 (hips -50, shoulders -49, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1265 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1266 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / +Z / LOLB  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Run) -> 61deg | STN-802 | gallery pass |
| STN-1267 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -148 (hips -149, shoulders -147, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1268 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 128, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1269 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 26 (hips 28, shoulders 24, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1270 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -38 (hips -82, shoulders 7, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1271 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -48, shoulders -46, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1272 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1273 | fixed | S2 | stance pose | OFF Empty vs 4-3 Base / +Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.25m | STN-803 | gallery pass |
| STN-1274 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1275 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1276 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / +Z / TE Y / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1277 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1278 | fixed | S2 | stance side | OFF Empty vs 4-3 Base / +Z / WR W,X (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.42m | STN-804 | gallery pass |
| STN-1279 | fixed | S2 | facing (live) | OFF Empty vs 4-3 Base / +Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1280 | fixed | S1 | facing | OFF Empty vs 4-3 Base / +Z / WR W,X,Z,H (x4) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -86, shoulders -51, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1281 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1282 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1283 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1284 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / DT  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1285 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1286 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / FB F (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1287 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -45, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1288 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1289 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / LE  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 168deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1290 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1291 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / LG LG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1292 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -39 (hips -40, shoulders -38, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1293 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -151 (hips -149, shoulders -154, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1294 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / LT LT (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1295 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -47, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1296 | fixed | S2 | stance pose | OFF Goal Line vs Goal Line Defense / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.52m | STN-803 | gallery pass |
| STN-1297 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -87 (hips -89, shoulders -84, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1298 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 89 (hips 90, shoulders 87, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1299 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 102 (hips 92, shoulders 113, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1300 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / RE  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 172deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1301 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1302 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / RG RG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1303 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -48, shoulders -54, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1304 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -158 (hips -156, shoulders -160, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1305 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / RT RT (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1306 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1307 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / TE Y,Y2 (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1308 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / TE Y,Y2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -43, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1309 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / -Z / WR Z (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1310 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / -Z / WR Z / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 87 (hips 90, shoulders 85, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1311 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1312 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 132, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1313 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -52 (hips -48, shoulders -55, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1314 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / +Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1315 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1316 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / +Z / FS  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 47deg | STN-802 | gallery pass |
| STN-1317 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 112 (hips 94, shoulders 130, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1318 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -46, shoulders -45, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1319 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / +Z / LG LG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 128deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1320 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1321 | fixed | S2 | stance pose | OFF Goal Line vs Goal Line Defense / +Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.43m | STN-803 | gallery pass |
| STN-1322 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 131 (hips 125, shoulders 138, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1323 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 136, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1324 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 95 (hips 91, shoulders 100, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1325 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -107 (hips -93, shoulders -120, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1326 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1327 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -46, shoulders -47, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1328 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1329 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / +Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 83deg | STN-802 | gallery pass |
| STN-1330 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -148 (hips -145, shoulders -151, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1331 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / +Z / RT RT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1332 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1333 | open | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / +Z / TE Y2 (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 135deg clip BlockDriveCStale_1_A | STN-802 | still: 51deg clip BGS__BGS_OLM_ShuffleStand_LStance_Loop |
| STN-1334 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / TE Y,Y2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1335 | fixed | S2 | facing (live) | OFF Goal Line vs Goal Line Defense / +Z / WR Z / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1336 | fixed | S1 | facing | OFF Goal Line vs Goal Line Defense / +Z / WR Z / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1337 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1338 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1339 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1340 | fixed | S2 | facing (live) | OFF I-Form Slot vs 4-3 Base / -Z / FB F (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1341 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -43, shoulders -40, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1342 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1343 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 139 (hips 138, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1344 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -43, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1345 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -133 (hips -134, shoulders -132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1346 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -51, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1347 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 89 (hips 90, shoulders 88, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1348 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 90 (hips 90, shoulders 89, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1349 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1350 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -53, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1351 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -156 (hips -154, shoulders -158, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1352 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1353 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -49, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1354 | fixed | S2 | stance side | OFF I-Form Slot vs 4-3 Base / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1355 | fixed | S2 | facing (live) | OFF I-Form Slot vs 4-3 Base / -Z / WR H,X,Z (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1356 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / -Z / WR H,X,Z (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 112 (hips 94, shoulders 129, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1357 | fixed | S2 | facing (live) | OFF I-Form Slot vs 4-3 Base / +Z / C C / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1358 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1359 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1360 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -52 (hips -52, shoulders -52, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1361 | fixed | S2 | facing (live) | OFF I-Form Slot vs 4-3 Base / +Z / FB F (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1362 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1363 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 103 (hips 93, shoulders 113, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1364 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -44 (hips -45, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1365 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1366 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -107 (hips -108, shoulders -106, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1367 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1368 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 27 (hips 29, shoulders 25, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1369 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -94 (hips -90, shoulders -98, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1370 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -82, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1371 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -44, shoulders -41, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1372 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1373 | fixed | S2 | stance pose | OFF I-Form Slot vs 4-3 Base / +Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.35m | STN-803 | gallery pass |
| STN-1374 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 141 (hips 140, shoulders 142, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1375 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 136, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1376 | fixed | S2 | stance side | OFF I-Form Slot vs 4-3 Base / +Z / WR H,X (x2) / presnap | FOOT_SIDE: outside foot back (right) -> left-right foot offset -0.25m | STN-804 | gallery pass |
| STN-1377 | fixed | S2 | facing (live) | OFF I-Form Slot vs 4-3 Base / +Z / WR H,X,Z (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1378 | fixed | S1 | facing | OFF I-Form Slot vs 4-3 Base / +Z / WR H,X,Z (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -88 (hips -89, shoulders -87, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1379 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1380 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 92deg | STN-802 | gallery pass |
| STN-1381 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -81 (hips -88, shoulders -74, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1382 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1383 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / FB F (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1384 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -44, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1385 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 49deg | STN-802 | gallery pass |
| STN-1386 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1387 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1388 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / LG LG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1389 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -43, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1390 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -154 (hips -151, shoulders -156, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1391 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1392 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 94deg | STN-802 | gallery pass |
| STN-1393 | fixed | S2 | stance pose | OFF I-Form vs 3-4 Base / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.30m | STN-803 | gallery pass |
| STN-1394 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1395 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 89 (hips 90, shoulders 87, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1396 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 88 (hips 90, shoulders 87, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1397 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 133, shoulders 126, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1398 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / RG RG / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1399 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -52, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1400 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -141 (hips -139, shoulders -142, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1401 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -48, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1402 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 82deg | STN-802 | gallery pass |
| STN-1403 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1404 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1405 | fixed | S2 | stance side | OFF I-Form vs 3-4 Base / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1406 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / -Z / WR X,Z (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1407 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / -Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 95, shoulders 133, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1408 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1409 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 95deg | STN-802 | gallery pass |
| STN-1410 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1411 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -51 (hips -51, shoulders -51, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1412 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / FB F / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1413 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / FB F / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1414 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 49deg | STN-802 | gallery pass |
| STN-1415 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 108 (hips 94, shoulders 122, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1416 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -44 (hips -44, shoulders -43, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1417 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1418 | fixed | S2 | stance pose | OFF I-Form vs 3-4 Base / +Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.62m | STN-803 | gallery pass |
| STN-1419 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1420 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 94deg | STN-802 | gallery pass |
| STN-1421 | fixed | S2 | stance pose | OFF I-Form vs 3-4 Base / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.82m | STN-803 | gallery pass |
| STN-1422 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 95 (hips 91, shoulders 100, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1423 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -94 (hips -90, shoulders -97, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1424 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -80, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1425 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -46, shoulders -45, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1426 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 116deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1427 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1428 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -86 (hips -85, shoulders -86, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1429 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 141 (hips 140, shoulders 142, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1430 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 91deg | STN-802 | gallery pass |
| STN-1431 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1432 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 131, shoulders 124, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1433 | fixed | S2 | stance side | OFF I-Form vs 3-4 Base / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1434 | fixed | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / WR X,Z (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1435 | fixed | S1 | facing | OFF I-Form vs 3-4 Base / +Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1436 | fixed | S1 | facing | OFF Kneel / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1437 | fixed | S1 | facing | OFF Kneel / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1438 | fixed | S1 | facing | OFF Kneel / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1439 | fixed | S1 | facing | OFF Kneel / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1440 | fixed | S1 | facing | OFF Kneel / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 130, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1441 | fixed | S1 | facing | OFF Kneel / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1442 | fixed | S2 | stance pose | OFF Kneel / -Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.98m | STN-803 | gallery pass |
| STN-1443 | fixed | S1 | facing | OFF Kneel / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -56 (hips -60, shoulders -52, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1444 | fixed | S1 | facing | OFF Kneel / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -47, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1445 | fixed | S1 | facing | OFF Kneel / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -82, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1446 | fixed | S1 | facing | OFF Kneel / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 76 (hips 87, shoulders 65, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1447 | fixed | S1 | facing | OFF Kneel / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 101 (hips 92, shoulders 110, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1448 | fixed | S1 | facing | OFF Kneel / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1449 | fixed | S1 | facing | OFF Kneel / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -40 (hips -41, shoulders -39, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1450 | fixed | S2 | stance pose | OFF Kneel / -Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.81m | STN-803 | gallery pass |
| STN-1451 | fixed | S1 | facing | OFF Kneel / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -169 (hips -166, shoulders -173, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1452 | fixed | S1 | facing | OFF Kneel / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1453 | fixed | S1 | facing | OFF Kneel / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1454 | fixed | S1 | facing | OFF Kneel / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -53 (hips -49, shoulders -57, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1455 | fixed | S2 | stance side | OFF Kneel / -Z / WR X,H (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1456 | fixed | S1 | facing | OFF Kneel / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 95, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1457 | fixed | S1 | facing | OFF Kneel / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 137, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1458 | fixed | S1 | facing | OFF Kneel / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1459 | fixed | S1 | facing | OFF Kneel / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -41 (hips -42, shoulders -41, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1460 | fixed | S1 | facing | OFF Kneel / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1461 | fixed | S1 | facing | OFF Kneel / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -46, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1462 | fixed | S1 | facing | OFF Kneel / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1463 | fixed | S1 | facing | OFF Kneel / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 95 (hips 91, shoulders 98, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1464 | fixed | S1 | facing | OFF Kneel / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 133, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1465 | fixed | S1 | facing | OFF Kneel / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 47 (hips 46, shoulders 48, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1466 | fixed | S1 | facing | OFF Kneel / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -93 (hips -91, shoulders -95, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1467 | fixed | S1 | facing | OFF Kneel / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -81, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1468 | fixed | S1 | facing | OFF Kneel / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -51 (hips -47, shoulders -55, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1469 | fixed | S1 | facing | OFF Kneel / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1470 | fixed | S1 | facing | OFF Kneel / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1471 | fixed | S1 | facing | OFF Kneel / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1472 | fixed | S1 | facing | OFF Kneel / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1473 | fixed | S2 | stance side | OFF Kneel / +Z / WR X,H (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1474 | fixed | S1 | facing | OFF Kneel / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -49, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1475 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1476 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / -Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 88deg | STN-802 | gallery pass |
| STN-1477 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1478 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 131, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1479 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / -Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 48deg | STN-802 | gallery pass |
| STN-1480 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1481 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 134, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1482 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / -Z / LG LG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1483 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -51, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1484 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -151 (hips -149, shoulders -153, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1485 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -43, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1486 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / -Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 99deg | STN-802 | gallery pass |
| STN-1487 | fixed | S2 | stance pose | OFF Pistol Trips vs 3-4 Base / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.43m | STN-803 | gallery pass |
| STN-1488 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -80, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1489 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 117 (hips 94, shoulders 140, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1490 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 86 (hips 90, shoulders 83, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1491 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 131, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1492 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / -Z / RG RG / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 175deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1493 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1494 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -143 (hips -137, shoulders -148, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1495 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1496 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / -Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 82deg | STN-802 | gallery pass |
| STN-1497 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -88 (hips -89, shoulders -87, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1498 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -43, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1499 | fixed | S2 | stance side | OFF Pistol Trips vs 3-4 Base / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1500 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / -Z / WR X,Z,H (x5) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1501 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 111 (hips 94, shoulders 129, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1502 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 140 (hips 139, shoulders 141, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1503 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / +Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 90deg | STN-802 | gallery pass |
| STN-1504 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1505 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -44, shoulders -42, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1506 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 48deg | STN-802 | gallery pass |
| STN-1507 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1508 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -46, shoulders -45, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1509 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / +Z / LG LG (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1510 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1511 | fixed | S2 | stance pose | OFF Pistol Trips vs 3-4 Base / +Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.98m | STN-803 | gallery pass |
| STN-1512 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 124 (hips 120, shoulders 128, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1513 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1514 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / +Z / MLB  (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 106deg | STN-802 | gallery pass |
| STN-1515 | fixed | S2 | stance pose | OFF Pistol Trips vs 3-4 Base / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.81m | STN-803 | gallery pass |
| STN-1516 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 94 (hips 91, shoulders 98, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1517 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -64 (hips -86, shoulders -42, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1518 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1519 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -48 (hips -48, shoulders -47, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1520 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / +Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1521 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1522 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -125 (hips -118, shoulders -131, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1523 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 132, shoulders 125, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1524 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / +Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 90deg | STN-802 | gallery pass |
| STN-1525 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 132, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1526 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1527 | fixed | S2 | stance side | OFF Pistol Trips vs 3-4 Base / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1528 | fixed | S2 | facing (live) | OFF Pistol Trips vs 3-4 Base / +Z / WR X,Z,H (x5) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1529 | fixed | S1 | facing | OFF Pistol Trips vs 3-4 Base / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1530 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -48, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1531 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / CB  (x4) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1532 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1533 | fixed | S2 | facing (live) | OFF Pistol vs Dime / -Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 48deg | STN-802 | gallery pass |
| STN-1534 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1535 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1536 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -43, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1537 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1538 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -81, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1539 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 126 (hips 95, shoulders 157, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1540 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 97 (hips 91, shoulders 103, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1541 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1542 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -39 (hips -40, shoulders -38, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1543 | fixed | S2 | facing (live) | OFF Pistol vs Dime / -Z / RT RT / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1544 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1545 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1546 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1547 | fixed | S2 | stance side | OFF Pistol vs Dime / -Z / WR X,H (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1548 | fixed | S2 | facing (live) | OFF Pistol vs Dime / -Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1549 | fixed | S1 | facing | OFF Pistol vs Dime / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1550 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 134, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1551 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / CB  (x4) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1552 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -39 (hips -40, shoulders -38, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1553 | fixed | S2 | facing (live) | OFF Pistol vs Dime / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 48deg | STN-802 | gallery pass |
| STN-1554 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1555 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -47, shoulders -45, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1556 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1557 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1558 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 31 (hips 37, shoulders 25, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1559 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -87 (hips -89, shoulders -84, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1560 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -50 (hips -47, shoulders -52, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1561 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 131, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1562 | fixed | S2 | facing (live) | OFF Pistol vs Dime / +Z / RT RT / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1563 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 131, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1564 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 116 (hips 95, shoulders 138, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1565 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1566 | fixed | S2 | stance side | OFF Pistol vs Dime / +Z / WR X,Z,H (x3) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1567 | fixed | S2 | facing (live) | OFF Pistol vs Dime / +Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1568 | fixed | S1 | facing | OFF Pistol vs Dime / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1569 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1570 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / CB  (x4) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1571 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1572 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / -Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 41deg | STN-802 | gallery pass |
| STN-1573 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -86, shoulders -50, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1574 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1575 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -43, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1576 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1577 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1578 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 118 (hips 94, shoulders 142, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1579 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 101 (hips 92, shoulders 110, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1580 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1581 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / -Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1582 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1583 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -47, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1584 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -69 (hips -86, shoulders -52, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1585 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / -Z / TE Y / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1586 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 102 (hips 92, shoulders 112, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1587 | fixed | S2 | stance side | OFF Shotgun Bunch vs Dime / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1588 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / -Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1589 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 136, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1590 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / +Z / C C / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 176deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1591 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 135, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1592 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / CB  (x4) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1593 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -44 (hips -44, shoulders -43, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1594 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 45deg | STN-802 | gallery pass |
| STN-1595 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1596 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -44 (hips -45, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1597 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1598 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 132, shoulders 126, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1599 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 22 (hips 24, shoulders 20, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1600 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -45 (hips -84, shoulders -6, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1601 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -83 (hips -89, shoulders -77, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1602 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -49 (hips -46, shoulders -53, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1603 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1604 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / +Z / RT RT / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 170deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1605 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1606 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1607 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / +Z / TE Y / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1608 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -93 (hips -90, shoulders -95, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1609 | fixed | S2 | stance side | OFF Shotgun Bunch vs Dime / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1610 | fixed | S2 | facing (live) | OFF Shotgun Bunch vs Dime / +Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1611 | fixed | S1 | facing | OFF Shotgun Bunch vs Dime / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1612 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -47, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1613 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / CB  (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -86, shoulders -51, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1614 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1615 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1616 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 134, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1617 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -44, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1618 | fixed | S2 | facing (live) | OFF Shotgun Doubles vs Nickel / -Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 83deg | STN-802 | gallery pass |
| STN-1619 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 34 (hips 37, shoulders 30, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1620 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -47, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1621 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -155 (hips -153, shoulders -156, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1622 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 96 (hips 91, shoulders 101, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1623 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1624 | fixed | S2 | facing (live) | OFF Shotgun Doubles vs Nickel / -Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 171deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1625 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1626 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1627 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1628 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -40 (hips -41, shoulders -39, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1629 | fixed | S2 | stance side | OFF Shotgun Doubles vs Nickel / -Z / WR X,Z,H (x3) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.39m | STN-804 | gallery pass |
| STN-1630 | fixed | S2 | facing (live) | OFF Shotgun Doubles vs Nickel / -Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1631 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 95, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1632 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 133, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1633 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / CB  (x3) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1634 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -45, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1635 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1636 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -48, shoulders -46, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1637 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1638 | fixed | S2 | facing (live) | OFF Shotgun Doubles vs Nickel / +Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 86deg | STN-802 | gallery pass |
| STN-1639 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 23 (hips 25, shoulders 21, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1640 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 141 (hips 140, shoulders 142, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1641 | fixed | S2 | stance pose | OFF Shotgun Doubles vs Nickel / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.09m | STN-803 | gallery pass |
| STN-1642 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -55 (hips -85, shoulders -25, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1643 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -82, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1644 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -41 (hips -42, shoulders -41, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1645 | fixed | S2 | facing (live) | OFF Shotgun Doubles vs Nickel / +Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 172deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1646 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1647 | fixed | S2 | facing (live) | OFF Shotgun Doubles vs Nickel / +Z / RT RT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 174deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1648 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 137, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1649 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 94 (hips 91, shoulders 97, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1650 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1651 | fixed | S2 | stance side | OFF Shotgun Doubles vs Nickel / +Z / WR X,H (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.37m | STN-804 | gallery pass |
| STN-1652 | fixed | S2 | facing (live) | OFF Shotgun Doubles vs Nickel / +Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1653 | fixed | S1 | facing | OFF Shotgun Doubles vs Nickel / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -69 (hips -86, shoulders -52, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1654 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1655 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / CB  (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1656 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1657 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1658 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 135, shoulders 128, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1659 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -50, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1660 | fixed | S2 | facing (live) | OFF Shotgun vs Nickel / -Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 86deg | STN-802 | gallery pass |
| STN-1661 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 71 (hips 81, shoulders 61, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1662 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -43, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1663 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -158 (hips -156, shoulders -160, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1664 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 137 (hips 97, shoulders 176, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1665 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 91 (hips 91, shoulders 91, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1666 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1667 | fixed | S2 | facing (live) | OFF Shotgun vs Nickel / -Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 171deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1668 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1669 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1670 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -84 (hips -89, shoulders -79, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1671 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -41 (hips -41, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1672 | fixed | S2 | stance side | OFF Shotgun vs Nickel / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.36m | STN-804 | gallery pass |
| STN-1673 | fixed | S2 | facing (live) | OFF Shotgun vs Nickel / -Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1674 | fixed | S1 | facing | OFF Shotgun vs Nickel / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 112 (hips 94, shoulders 130, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1675 | fixed | S2 | facing (live) | OFF Shotgun vs Nickel / +Z / C C / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 161deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1676 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 139 (hips 138, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1677 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / CB  (x3) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1678 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -44, shoulders -42, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1679 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 95, shoulders 131, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1680 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -46, shoulders -45, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1681 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1682 | fixed | S2 | facing (live) | OFF Shotgun vs Nickel / +Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 84deg | STN-802 | gallery pass |
| STN-1683 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 26 (hips 27, shoulders 24, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1684 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1685 | fixed | S2 | stance pose | OFF Shotgun vs Nickel / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.11m | STN-803 | gallery pass |
| STN-1686 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -71 (hips -87, shoulders -54, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1687 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1688 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -40 (hips -41, shoulders -40, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1689 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1690 | fixed | S2 | facing (live) | OFF Shotgun vs Nickel / +Z / RT RT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1691 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 133, shoulders 125, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1692 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1693 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1694 | fixed | S2 | stance side | OFF Shotgun vs Nickel / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1695 | fixed | S2 | facing (live) | OFF Shotgun vs Nickel / +Z / WR X,Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1696 | fixed | S1 | facing | OFF Shotgun vs Nickel / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1697 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -49 (hips -49, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1698 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1699 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1700 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1701 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1702 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1703 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -158 (hips -156, shoulders -161, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1704 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / -Z / LT LT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1705 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1706 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / -Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 35deg | STN-802 | gallery pass |
| STN-1707 | fixed | S2 | stance pose | OFF Singleback Ace vs Goal Line Defense / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.85m | STN-803 | gallery pass |
| STN-1708 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -34 (hips -34, shoulders -34, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1709 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 86 (hips 89, shoulders 83, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1710 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 102 (hips 92, shoulders 112, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1711 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1712 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / -Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1713 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -45, shoulders -52, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1714 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / -Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 84deg | STN-802 | gallery pass |
| STN-1715 | fixed | S2 | stance pose | OFF Singleback Ace vs Goal Line Defense / -Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.83m | STN-803 | gallery pass |
| STN-1716 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -170 (hips -176, shoulders -164, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1717 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / -Z / RT RT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1718 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -47, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1719 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / TE Y,Y2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -51, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1720 | fixed | S2 | stance side | OFF Singleback Ace vs Goal Line Defense / -Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1721 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / -Z / WR X,Z (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1722 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / -Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 116 (hips 95, shoulders 136, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1723 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 131, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1724 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1725 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -44, shoulders -50, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1726 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1727 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / +Z / LE  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 45deg | STN-802 | gallery pass |
| STN-1728 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -50 (hips -50, shoulders -50, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1729 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1730 | fixed | S2 | stance pose | OFF Singleback Ace vs Goal Line Defense / +Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.08m | STN-803 | gallery pass |
| STN-1731 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1732 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / +Z / MLB  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 111deg | STN-802 | gallery pass |
| STN-1733 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 94 (hips 91, shoulders 97, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1734 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -96 (hips -91, shoulders -101, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1735 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -82, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1736 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -52 (hips -52, shoulders -53, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1737 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1738 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / +Z / ROLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 84deg | STN-802 | gallery pass |
| STN-1739 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1740 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / +Z / TE Y2 / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 158deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1741 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / TE Y,Y2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1742 | fixed | S2 | stance side | OFF Singleback Ace vs Goal Line Defense / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.34m | STN-804 | gallery pass |
| STN-1743 | fixed | S2 | facing (live) | OFF Singleback Ace vs Goal Line Defense / +Z / WR X,Z (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1744 | fixed | S1 | facing | OFF Singleback Ace vs Goal Line Defense / +Z / WR X,Z (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1745 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -50, shoulders -50, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1746 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1747 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1748 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -90 (hips -90, shoulders -91, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1749 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1750 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1751 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -169 (hips -172, shoulders -165, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1752 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1753 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 84 (hips 89, shoulders 78, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1754 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 102 (hips 92, shoulders 112, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1755 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1756 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / -Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1757 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -44, shoulders -50, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1758 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -154 (hips -152, shoulders -156, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1759 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1760 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1761 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / -Z / TE Y / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1762 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -51, shoulders -53, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1763 | fixed | S2 | stance side | OFF Singleback vs 4-3 Base / -Z / WR X,H (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1764 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / -Z / WR X,Z,H (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1765 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1766 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1767 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1768 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / +Z / DT  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Run) -> 65deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1769 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -43, shoulders -48, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1770 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 132, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1771 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -51 (hips -51, shoulders -51, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1772 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / +Z / LG LG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 99deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1773 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1774 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -151 (hips -147, shoulders -156, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1775 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 131, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1776 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 23 (hips 25, shoulders 22, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1777 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -89 (hips -90, shoulders -89, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1778 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -81, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1779 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -52 (hips -52, shoulders -52, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1780 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / +Z / RG RG / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1781 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1782 | fixed | S2 | stance pose | OFF Singleback vs 4-3 Base / +Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.14m | STN-803 | gallery pass |
| STN-1783 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / +Z / RT RT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1784 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1785 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / +Z / SS  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Run) -> 37deg | STN-802 | gallery pass |
| STN-1786 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 136, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1787 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1788 | fixed | S2 | stance side | OFF Singleback vs 4-3 Base / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1789 | fixed | S2 | facing (live) | OFF Singleback vs 4-3 Base / +Z / WR X,Z,H (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1790 | fixed | S1 | facing | OFF Singleback vs 4-3 Base / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1791 | fixed | S2 | facing (live) | OFF Spike / -Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1792 | fixed | S1 | facing | OFF Spike / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -47, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1793 | fixed | S2 | facing (live) | OFF Spike / -Z / CB  (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 65deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1794 | fixed | S1 | facing | OFF Spike / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -86, shoulders -50, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1795 | fixed | S1 | facing | OFF Spike / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1796 | fixed | S2 | facing (live) | OFF Spike / -Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 65deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1797 | fixed | S1 | facing | OFF Spike / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1798 | fixed | S1 | facing | OFF Spike / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 137, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1799 | fixed | S1 | facing | OFF Spike / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1800 | fixed | S2 | facing (live) | OFF Spike / -Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 80deg clip ReceiverTransitionReadyIdleRToMotionR | STN-802 | gallery pass |
| STN-1801 | fixed | S1 | facing | OFF Spike / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1802 | fixed | S2 | facing (live) | OFF Spike / -Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 151deg clip LinebackerBlitzIdleOne | STN-802 | gallery pass |
| STN-1803 | fixed | S1 | facing | OFF Spike / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -155 (hips -154, shoulders -157, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1804 | fixed | S1 | facing | OFF Spike / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 142 (hips 97, shoulders -174, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1805 | fixed | S2 | facing (live) | OFF Spike / -Z / RB HB (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1806 | fixed | S1 | facing | OFF Spike / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 103 (hips 92, shoulders 114, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1807 | fixed | S1 | facing | OFF Spike / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1808 | fixed | S1 | facing | OFF Spike / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -47, shoulders -54, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1809 | fixed | S2 | stance pose | OFF Spike / -Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.19m | STN-803 | gallery pass |
| STN-1810 | fixed | S1 | facing | OFF Spike / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -50, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1811 | fixed | S2 | facing (live) | OFF Spike / -Z / SS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 66deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1812 | fixed | S1 | facing | OFF Spike / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -49, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1813 | fixed | S1 | facing | OFF Spike / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -49, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1814 | fixed | S2 | stance side | OFF Spike / -Z / WR X,H (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1815 | fixed | S2 | facing (live) | OFF Spike / -Z / WR X,Z,H (x5) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1816 | fixed | S1 | facing | OFF Spike / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 95, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1817 | fixed | S2 | facing (live) | OFF Spike / +Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1818 | fixed | S1 | facing | OFF Spike / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1819 | fixed | S2 | facing (live) | OFF Spike / +Z / CB  (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 66deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1820 | fixed | S1 | facing | OFF Spike / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1821 | fixed | S1 | facing | OFF Spike / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -49 (hips -46, shoulders -53, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1822 | fixed | S2 | facing (live) | OFF Spike / +Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 66deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1823 | fixed | S1 | facing | OFF Spike / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 116 (hips 95, shoulders 136, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1824 | fixed | S1 | facing | OFF Spike / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -48 (hips -48, shoulders -47, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1825 | fixed | S1 | facing | OFF Spike / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1826 | fixed | S2 | facing (live) | OFF Spike / +Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 150deg clip LinebackerBlitzIdleOne | STN-802 | gallery pass |
| STN-1827 | fixed | S1 | facing | OFF Spike / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 29 (hips 31, shoulders 27, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1828 | fixed | S1 | facing | OFF Spike / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 135, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1829 | fixed | S2 | facing (live) | OFF Spike / +Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 46deg clip ReceiverTransitionReadyIdleRToMotionL | STN-802 | gallery pass |
| STN-1830 | fixed | S2 | stance pose | OFF Spike / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.43m | STN-803 | gallery pass |
| STN-1831 | fixed | S1 | facing | OFF Spike / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -77 (hips -88, shoulders -66, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1832 | fixed | S2 | facing (live) | OFF Spike / +Z / RB HB (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1833 | fixed | S1 | facing | OFF Spike / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1834 | fixed | S1 | facing | OFF Spike / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -50 (hips -49, shoulders -51, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1835 | fixed | S1 | facing | OFF Spike / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1836 | fixed | S2 | facing (live) | OFF Spike / +Z / ROLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 85deg clip 2pstance_halfback_001 | STN-802 | gallery pass |
| STN-1837 | fixed | S1 | facing | OFF Spike / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 95 (hips 91, shoulders 100, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1838 | fixed | S2 | facing (live) | OFF Spike / +Z / RT RT (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1839 | fixed | S1 | facing | OFF Spike / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1840 | fixed | S2 | facing (live) | OFF Spike / +Z / SS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 64deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1841 | fixed | S1 | facing | OFF Spike / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 132, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1842 | fixed | S1 | facing | OFF Spike / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1843 | fixed | S2 | stance side | OFF Spike / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1844 | fixed | S2 | facing (live) | OFF Spike / +Z / WR X,Z,H (x6) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1845 | fixed | S1 | facing | OFF Spike / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -49, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1846 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1847 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 79deg | STN-802 | gallery pass |
| STN-1848 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -64 (hips -85, shoulders -42, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1849 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1850 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 41deg | STN-802 | gallery pass |
| STN-1851 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1852 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 133, shoulders 128, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1853 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -49, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1854 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -153 (hips -151, shoulders -155, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1855 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / LT LT / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1856 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -49, shoulders -48, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1857 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 120deg | STN-802 | gallery pass |
| STN-1858 | fixed | S2 | stance pose | OFF Trips vs 3-4 Base / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.29m | STN-803 | gallery pass |
| STN-1859 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -84 (hips -89, shoulders -79, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1860 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 111 (hips 93, shoulders 129, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1861 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / RB HB / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-1862 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 92 (hips 90, shoulders 93, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1863 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / RE  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 50deg | STN-802 | gallery pass |
| STN-1864 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 137, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1865 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / RG RG / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 117deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1866 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1867 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -154 (hips -148, shoulders -160, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1868 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1869 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 109deg | STN-802 | gallery pass |
| STN-1870 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1871 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -43, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1872 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / -Z / WR Z,H (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1873 | fixed | S1 | facing | OFF Trips vs 3-4 Base / -Z / WR Z,H,W (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1874 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 138, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1875 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / CB  (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 79deg | STN-802 | gallery pass |
| STN-1876 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 96 (hips 91, shoulders 101, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1877 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -44, shoulders -42, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1878 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 42deg | STN-802 | gallery pass |
| STN-1879 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1880 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -47, shoulders -46, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1881 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1882 | fixed | S2 | stance pose | OFF Trips vs 3-4 Base / +Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.78m | STN-803 | gallery pass |
| STN-1883 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 126 (hips 128, shoulders 124, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1884 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / LT LT (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1885 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1886 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 111deg | STN-802 | gallery pass |
| STN-1887 | fixed | S2 | stance pose | OFF Trips vs 3-4 Base / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.92m | STN-803 | gallery pass |
| STN-1888 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 95 (hips 91, shoulders 99, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1889 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -44 (hips -84, shoulders -3, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1890 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / RB HB (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1891 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -87 (hips -89, shoulders -84, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1892 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -47, shoulders -46, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1893 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1894 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -93 (hips -88, shoulders -98, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1895 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / RT RT (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-1896 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1897 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / SS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 118deg | STN-802 | gallery pass |
| STN-1898 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1899 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 131, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1900 | fixed | S2 | facing (live) | OFF Trips vs 3-4 Base / +Z / WR Z,H (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1901 | fixed | S1 | facing | OFF Trips vs 3-4 Base / +Z / WR Z,H,W (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1902 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / -Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 135deg clip DlineIdle3ptR | STN-802 | gallery pass |
| STN-1903 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -51, shoulders -50, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1904 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -75 (hips -87, shoulders -64, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1905 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 130, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1906 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / -Z / FS PR (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 68deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1907 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / FS PR / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1908 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / -Z / K K / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 50deg | STN-802 | gallery pass |
| STN-1909 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1910 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1911 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -156 (hips -154, shoulders -157, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1912 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -49, shoulders -48, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1913 | fixed | S2 | stance pose | ST Extra Point vs Block Middle / -Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.08m | STN-803 | gallery pass |
| STN-1914 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1915 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / -Z / RG  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1916 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1917 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1918 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1919 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1920 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -43, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1921 | fixed | S2 | stance side | ST Extra Point vs Block Middle / -Z / WR  / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-1922 | fixed | S1 | facing | ST Extra Point vs Block Middle / -Z / WR  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 94, shoulders 131, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1923 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / +Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 133deg clip DlineIdle3ptRV2 | STN-802 | gallery pass |
| STN-1924 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1925 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 87 (hips 90, shoulders 85, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1926 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -48, shoulders -45, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1927 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / +Z / FS PR (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 66deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1928 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / FS PR / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 132, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1929 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / +Z / K K / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 50deg | STN-802 | gallery pass |
| STN-1930 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -45, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1931 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1932 | fixed | S2 | stance pose | ST Extra Point vs Block Middle / +Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.25m | STN-803 | gallery pass |
| STN-1933 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / +Z / LT  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1934 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1935 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 94 (hips 91, shoulders 97, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1936 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -42 (hips -43, shoulders -41, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1937 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1938 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 55 (hips 52, shoulders 57, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1939 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1940 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 112 (hips 94, shoulders 130, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1941 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1942 | fixed | S2 | stance side | ST Extra Point vs Block Middle / +Z / WR  / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1943 | fixed | S2 | facing (live) | ST Extra Point vs Block Middle / +Z / WR  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 99deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-1944 | fixed | S1 | facing | ST Extra Point vs Block Middle / +Z / WR  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1945 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / -Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 134deg clip DlineIdle3ptR | STN-802 | gallery pass |
| STN-1946 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1947 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1948 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1949 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / -Z / FS PR (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 64deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1950 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / FS PR / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -77 (hips -87, shoulders -67, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1951 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / -Z / K K / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 50deg | STN-802 | gallery pass |
| STN-1952 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 137, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1953 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -48, shoulders -54, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1954 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -87 (hips -89, shoulders -84, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1955 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -45, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1956 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -158 (hips -156, shoulders -160, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1957 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1958 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1959 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 83 (hips 89, shoulders 76, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1960 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -44, shoulders -43, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1961 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1962 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -51, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1963 | fixed | S2 | stance side | ST Field Goal vs Block Edge / -Z / WR  / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-1964 | fixed | S1 | facing | ST Field Goal vs Block Edge / -Z / WR  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1965 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / +Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 135deg clip DlineIdle3ptRV2 | STN-802 | gallery pass |
| STN-1966 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 128, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1967 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 112 (hips 94, shoulders 129, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1968 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -47 (hips -46, shoulders -47, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1969 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / +Z / FS PR (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 64deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1970 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / FS PR / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1971 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / +Z / K K / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 50deg | STN-802 | gallery pass |
| STN-1972 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -52 (hips -52, shoulders -51, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1973 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1974 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 3 (hips 5, shoulders 1, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1975 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1976 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -146 (hips -143, shoulders -150, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1977 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -50 (hips -50, shoulders -49, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1978 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1979 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 25 (hips 27, shoulders 24, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1980 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / +Z / RT  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-1981 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1982 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 132, root 180) | STN-801 + STN-802 | gallery pass |
| STN-1983 | fixed | S2 | facing (live) | ST Field Goal vs Block Edge / +Z / TE  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 165deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-1984 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1985 | fixed | S2 | stance side | ST Field Goal vs Block Edge / +Z / WR  (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.39m | STN-804 | gallery pass |
| STN-1986 | fixed | S1 | facing | ST Field Goal vs Block Edge / +Z / WR  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1987 | fixed | S2 | facing (live) | ST Field Goal vs Block Middle / -Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 133deg clip DlineIdle3ptR | STN-802 | gallery pass |
| STN-1988 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -48, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1989 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1990 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 132, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1991 | fixed | S2 | facing (live) | ST Field Goal vs Block Middle / -Z / FS PR (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 66deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-1992 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / FS PR / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1993 | fixed | S2 | facing (live) | ST Field Goal vs Block Middle / -Z / K K / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 50deg | STN-802 | gallery pass |
| STN-1994 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 136, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1995 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -39 (hips -40, shoulders -38, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1996 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -47, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-1997 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -157 (hips -155, shoulders -159, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1998 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-1999 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2000 | fixed | S2 | stance pose | ST Field Goal vs Block Middle / -Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.09m | STN-803 | gallery pass |
| STN-2001 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -51 (hips -51, shoulders -51, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2002 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2003 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -44, shoulders -50, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2004 | fixed | S2 | stance side | ST Field Goal vs Block Middle / -Z / WR  / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.37m | STN-804 | gallery pass |
| STN-2005 | fixed | S1 | facing | ST Field Goal vs Block Middle / -Z / WR  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 111 (hips 94, shoulders 128, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2006 | fixed | S2 | facing (live) | ST Field Goal vs Block Middle / +Z / C C (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 131deg clip DlineIdle3ptRV2 | STN-802 | gallery pass |
| STN-2007 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 134, shoulders 127, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2008 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2009 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -50 (hips -47, shoulders -52, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2010 | fixed | S2 | facing (live) | ST Field Goal vs Block Middle / +Z / FS PR (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Stance) -> 65deg clip 2pstance_safety_001 | STN-802 | gallery pass |
| STN-2011 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / FS PR / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2012 | fixed | S2 | facing (live) | ST Field Goal vs Block Middle / +Z / K K / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 51deg | STN-802 | gallery pass |
| STN-2013 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -45, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2014 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 131, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2015 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 27 (hips 29, shoulders 25, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2016 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2017 | fixed | S2 | stance pose | ST Field Goal vs Block Middle / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.29m | STN-803 | gallery pass |
| STN-2018 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -45 (hips -43, shoulders -47, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2019 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 131, shoulders 129, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2020 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 96 (hips 91, shoulders 101, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2021 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2022 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2023 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 134, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2024 | fixed | S2 | stance side | ST Field Goal vs Block Middle / +Z / WR  / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-2025 | fixed | S1 | facing | ST Field Goal vs Block Middle / +Z / WR  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2026 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / CB  (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2027 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / CB L0,L9 (x4) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 94, shoulders 131, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2028 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / FB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2029 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / FB L5 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 93, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2030 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2031 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / FS L8 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 95, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2032 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2033 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / LOLB L2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 93, shoulders 133, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2034 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2035 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / MLB L4 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 112 (hips 93, shoulders 131, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2036 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / RB R1 / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 40deg | STN-802 | gallery pass |
| STN-2037 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / RB L7,R1 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 94, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2038 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2039 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / ROLB L6 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 102 (hips 92, shoulders 113, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2040 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / SS L1 / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 112 (hips 94, shoulders 129, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2041 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / -Z / TE  (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2042 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / TE L3 (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 93, shoulders 133, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2043 | fixed | S1 | facing | ST Kickoff Deep Middle / -Z / WR R2 / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -86 (hips -89, shoulders -83, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2044 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / CB  (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2045 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / CB L0,L9 (x4) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -86, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2046 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / FB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2047 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / FB L5 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -87, shoulders -49, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2048 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2049 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / FS L8 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -64 (hips -85, shoulders -42, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2050 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2051 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / LOLB L2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -87, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2052 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2053 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / MLB L4 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -69 (hips -87, shoulders -52, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2054 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / RB R1 / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Backpedal) -> 46deg | STN-802 | gallery pass |
| STN-2055 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / RB L7,R1 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -87, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2056 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / ROLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2057 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / ROLB L6 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -87, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2058 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / SS L1 / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -88 (hips -89, shoulders -87, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2059 | fixed | S2 | facing (live) | ST Kickoff Deep Middle / +Z / TE  (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2060 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / TE L3 (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -87, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2061 | fixed | S1 | facing | ST Kickoff Deep Middle / +Z / WR R2 / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2062 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / -Z / CB  (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2063 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / CB L0,L9 (x4) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 94, shoulders 131, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2064 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / -Z / FB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2065 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / FB L5 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 93, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2066 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / -Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2067 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / FS L8 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2068 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / -Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2069 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / LOLB L2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 93, shoulders 134, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2070 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / -Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2071 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / MLB L4 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 107 (hips 92, shoulders 122, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2072 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / RB L7,R1 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 112 (hips 93, shoulders 131, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2073 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / -Z / ROLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2074 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / ROLB L6 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 94, shoulders 133, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2075 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / SS L1 / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 111 (hips 94, shoulders 129, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2076 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / -Z / TE  (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2077 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / TE L3 (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 112 (hips 93, shoulders 131, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2078 | fixed | S1 | facing | ST Kickoff Squib / Return Left / -Z / WR R2 / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2079 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / +Z / CB  (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2080 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / CB L0,L9 (x4) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2081 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / +Z / FB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2082 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / FB L5 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -87, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2083 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / +Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2084 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / FS L8 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -86, shoulders -50, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2085 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / +Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2086 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / LOLB L2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -87, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2087 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / +Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2088 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / MLB L4 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -90 (hips -90, shoulders -91, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2089 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / RB L7,R1 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -70 (hips -87, shoulders -52, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2090 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / +Z / ROLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2091 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / ROLB L6 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -68 (hips -87, shoulders -49, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2092 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / SS L1 / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2093 | fixed | S2 | facing (live) | ST Kickoff Squib / Return Left / +Z / TE  (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2094 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / TE L3 (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -87, shoulders -48, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2095 | fixed | S1 | facing | ST Kickoff Squib / Return Left / +Z / WR R2 / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2096 | fixed | S2 | facing (live) | ST Onside Kick Short / -Z / CB  (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2097 | fixed | S1 | facing | ST Onside Kick Short / -Z / CB L0,L9 (x4) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2098 | fixed | S2 | facing (live) | ST Onside Kick Short / -Z / FB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2099 | fixed | S1 | facing | ST Onside Kick Short / -Z / FB L5 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 93, shoulders 133, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2100 | fixed | S2 | facing (live) | ST Onside Kick Short / -Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2101 | fixed | S1 | facing | ST Onside Kick Short / -Z / FS L8 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2102 | fixed | S2 | facing (live) | ST Onside Kick Short / -Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2103 | fixed | S1 | facing | ST Onside Kick Short / -Z / LOLB L2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 93, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2104 | fixed | S2 | facing (live) | ST Onside Kick Short / -Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2105 | fixed | S1 | facing | ST Onside Kick Short / -Z / MLB L4 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 111 (hips 93, shoulders 130, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2106 | fixed | S1 | facing | ST Onside Kick Short / -Z / RB L7,R1 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 104 (hips 92, shoulders 115, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2107 | fixed | S2 | facing (live) | ST Onside Kick Short / -Z / ROLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2108 | fixed | S1 | facing | ST Onside Kick Short / -Z / ROLB L6 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 93, shoulders 133, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2109 | fixed | S1 | facing | ST Onside Kick Short / -Z / SS L1 / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 115 (hips 95, shoulders 135, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2110 | fixed | S2 | facing (live) | ST Onside Kick Short / -Z / TE  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2111 | fixed | S1 | facing | ST Onside Kick Short / -Z / TE L3 (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 112 (hips 93, shoulders 131, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2112 | fixed | S1 | facing | ST Onside Kick Short / -Z / WR R2 / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2113 | fixed | S2 | facing (live) | ST Onside Kick Short / +Z / CB  (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2114 | fixed | S1 | facing | ST Onside Kick Short / +Z / CB L0,L9 (x4) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2115 | fixed | S2 | facing (live) | ST Onside Kick Short / +Z / FB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2116 | fixed | S1 | facing | ST Onside Kick Short / +Z / FB L5 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -87, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2117 | fixed | S2 | facing (live) | ST Onside Kick Short / +Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2118 | fixed | S1 | facing | ST Onside Kick Short / +Z / FS L8 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2119 | fixed | S2 | facing (live) | ST Onside Kick Short / +Z / LOLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2120 | fixed | S1 | facing | ST Onside Kick Short / +Z / LOLB L2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -81 (hips -89, shoulders -74, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2121 | fixed | S2 | facing (live) | ST Onside Kick Short / +Z / MLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2122 | fixed | S1 | facing | ST Onside Kick Short / +Z / MLB L4 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -87, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2123 | fixed | S1 | facing | ST Onside Kick Short / +Z / RB L7,R1 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -92 (hips -90, shoulders -95, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2124 | fixed | S2 | facing (live) | ST Onside Kick Short / +Z / ROLB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2125 | fixed | S1 | facing | ST Onside Kick Short / +Z / ROLB L6 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -87, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2126 | fixed | S1 | facing | ST Onside Kick Short / +Z / SS L1 / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2127 | fixed | S2 | facing (live) | ST Onside Kick Short / +Z / TE  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2128 | fixed | S1 | facing | ST Onside Kick Short / +Z / TE L3 (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -67 (hips -87, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2129 | fixed | S1 | facing | ST Onside Kick Short / +Z / WR R2 / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 111 (hips 94, shoulders 128, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2130 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -50 (hips -50, shoulders -49, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2131 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / CB PR (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2132 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / CB PR (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2133 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 138, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2134 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / FB PP (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2135 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / FB PP / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 95 (hips 91, shoulders 99, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2136 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2137 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2138 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2139 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2140 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2141 | fixed | S2 | stance pose | ST Punt Directional Left vs Return Safe / -Z / LOLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.78m | STN-803 | gallery pass |
| STN-2142 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -54 (hips -52, shoulders -56, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2143 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / LT  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2144 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2145 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2146 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -81, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2147 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2148 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / RG  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2149 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2150 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2151 | fixed | S2 | stance pose | ST Punt Directional Left vs Return Safe / -Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.92m | STN-803 | gallery pass |
| STN-2152 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -165 (hips -167, shoulders -163, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2153 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / RT  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2154 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -42 (hips -42, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2155 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / -Z / SS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2156 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2157 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2158 | fixed | S2 | stance side | ST Punt Directional Left vs Return Safe / -Z / WR G1 / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-2159 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / -Z / WR G1,G2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 94, shoulders 132, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2160 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2161 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / CB PR (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2162 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / CB PR (x3) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 94, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2163 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -44 (hips -45, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2164 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / FB PP / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2165 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / FB PP / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -79 (hips -88, shoulders -71, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2166 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / FS  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2167 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 135, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2168 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -40 (hips -41, shoulders -40, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2169 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 139 (hips 138, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2170 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2171 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 94 (hips 91, shoulders 97, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2172 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 133, shoulders 125, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2173 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2174 | fixed | S2 | stance pose | ST Punt Directional Left vs Return Safe / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 1.80m | STN-803 | gallery pass |
| STN-2175 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 21 (hips 22, shoulders 20, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2176 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / RE  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 153deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2177 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -44 (hips -44, shoulders -44, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2178 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / RG  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2179 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2180 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2181 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / RT  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2182 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 129 (hips 129, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2183 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / SS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2184 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 105 (hips 93, shoulders 116, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2185 | fixed | S2 | facing (live) | ST Punt Directional Left vs Return Safe / +Z / TE  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 69deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2186 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2187 | fixed | S2 | stance side | ST Punt Directional Left vs Return Safe / +Z / WR G1,G2 (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-2188 | fixed | S1 | facing | ST Punt Directional Left vs Return Safe / +Z / WR G1,G2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2189 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -43, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2190 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / CB PR (x3) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2191 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / CB PR (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -77 (hips -87, shoulders -67, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2192 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 132, shoulders 130, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2193 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / FB PP / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2194 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / FB PP / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 94 (hips 91, shoulders 98, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2195 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 177deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2196 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -44, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2197 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 130 (hips 130, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2198 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -44 (hips -45, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2199 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 178deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2200 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -141 (hips -139, shoulders -142, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2201 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -48 (hips -48, shoulders -48, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2202 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2203 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 32 (hips 31, shoulders 33, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2204 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2205 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / RG  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 169deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2206 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -52 (hips -52, shoulders -52, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2207 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2208 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -154 (hips -152, shoulders -156, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2209 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / RT  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2210 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -41, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2211 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / -Z / SS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2212 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -93 (hips -90, shoulders -95, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2213 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -47 (hips -48, shoulders -47, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2214 | fixed | S2 | stance side | ST Punt Standard vs Return Set / -Z / WR G1 / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.42m | STN-804 | gallery pass |
| STN-2215 | fixed | S1 | facing | ST Punt Standard vs Return Set / -Z / WR G1,G2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 114 (hips 95, shoulders 133, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2216 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 134, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2217 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / CB  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2218 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / CB PR (x3) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 87 (hips 90, shoulders 85, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2219 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / DT  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -48 (hips -48, shoulders -47, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2220 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / FB PP (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2221 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / FB PP / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -83 (hips -89, shoulders -77, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2222 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / FS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2223 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 115 (hips 95, shoulders 134, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2224 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -44, shoulders -42, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2225 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / LG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 137 (hips 136, shoulders 137, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2226 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / LOLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 160deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2227 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -87 (hips -86, shoulders -88, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2228 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / LT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 141 (hips 140, shoulders 142, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2229 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / MLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2230 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / MLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 29 (hips 31, shoulders 26, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2231 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -43, shoulders -43, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2232 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / RG  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2233 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / RG  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 134 (hips 133, shoulders 135, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2234 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 167deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2235 | fixed | S2 | stance pose | ST Punt Standard vs Return Set / +Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.52m | STN-803 | gallery pass |
| STN-2236 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / RT  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 159deg clip BlockHitCStale_A | STN-802 | gallery pass |
| STN-2237 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / RT  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 134, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2238 | fixed | S2 | facing (live) | ST Punt Standard vs Return Set / +Z / SS  (x2) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 179deg clip BlockDriveCStale_1_A | STN-802 | gallery pass |
| STN-2239 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / SS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 113 (hips 94, shoulders 132, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2240 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / TE  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 128 (hips 131, shoulders 124, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2241 | fixed | S2 | stance side | ST Punt Standard vs Return Set / +Z / WR G1 / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-2242 | fixed | S1 | facing | ST Punt Standard vs Return Set / +Z / WR G1,G2 (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -65 (hips -85, shoulders -45, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2243 | fixed | S2 | facing (live) | ST Two-point try (Shotgun vs Goal Line) / -Z / C C / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2244 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / C C / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -46, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2245 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -86, shoulders -47, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2246 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2247 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -77 (hips -87, shoulders -67, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2248 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 135 (hips 137, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2249 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -45 (hips -46, shoulders -44, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2250 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -154 (hips -148, shoulders -160, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2251 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -46, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2252 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 73 (hips 72, shoulders 74, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2253 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 91 (hips 90, shoulders 93, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2254 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 132, shoulders 132, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2255 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -43 (hips -44, shoulders -42, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2256 | fixed | S2 | facing (live) | ST Two-point try (Shotgun vs Goal Line) / -Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 90deg | STN-802 | gallery pass |
| STN-2257 | fixed | S2 | stance pose | ST Two-point try (Shotgun vs Goal Line) / -Z / ROLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.35m | STN-803 | gallery pass |
| STN-2258 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -46 (hips -47, shoulders -45, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2259 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw -39 (hips -39, shoulders -38, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2260 | fixed | S2 | stance side | ST Two-point try (Shotgun vs Goal Line) / -Z / WR X,Z (x2) / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.40m | STN-804 | gallery pass |
| STN-2261 | fixed | S2 | facing (live) | ST Two-point try (Shotgun vs Goal Line) / -Z / WR X,Z,H (x4) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 166deg clip BlockDriveRStale_A | STN-802 | gallery pass |
| STN-2262 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / -Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw -180 -> body yaw 113 (hips 94, shoulders 132, root -180) | STN-801 + STN-802 | gallery pass |
| STN-2263 | fixed | S2 | facing (live) | ST Two-point try (Shotgun vs Goal Line) / +Z / C C / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 180deg clip BlockDriveCStale_A | STN-802 | gallery pass |
| STN-2264 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / C C / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2265 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / CB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 114 (hips 95, shoulders 133, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2266 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / DT  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -43 (hips -44, shoulders -43, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2267 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / FS  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 87 (hips 90, shoulders 85, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2268 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / LE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -46 (hips -47, shoulders -46, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2269 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / LG LG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 136 (hips 135, shoulders 136, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2270 | fixed | S2 | facing (live) | ST Two-point try (Shotgun vs Goal Line) / +Z / LOLB  / snap+1.5s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Run) -> 39deg | STN-802 | gallery pass |
| STN-2271 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / LOLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -93 (hips -88, shoulders -98, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2272 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / LT LT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 138 (hips 138, shoulders 139, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2273 | fixed | S2 | stance pose | ST Two-point try (Shotgun vs Goal Line) / +Z / MLB  / presnap | STANCE_LOW: head <= 1.8m (2-point crouch) -> 2.85m | STN-803 | gallery pass |
| STN-2274 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / MLB  (x2) / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 22 (hips 24, shoulders 19, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2275 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / QB QB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -87, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2276 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / RB HB / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -85 (hips -89, shoulders -81, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2277 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / RE  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw -39 (hips -40, shoulders -38, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2278 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / RG RG / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 131 (hips 131, shoulders 131, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2279 | fixed | S2 | facing (live) | ST Two-point try (Shotgun vs Goal Line) / +Z / ROLB  / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Shuffle) -> 93deg | STN-802 | gallery pass |
| STN-2280 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / ROLB  / presnap | VISUAL_FACING: <=25deg of yaw 180 -> body yaw 95 (hips 91, shoulders 98, root 180) | STN-801 + STN-802 | gallery pass |
| STN-2281 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / RT RT / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 132 (hips 135, shoulders 128, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2282 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / TE Y / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw 133 (hips 133, shoulders 133, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2283 | fixed | S2 | stance side | ST Two-point try (Shotgun vs Goal Line) / +Z / WR X / presnap | FOOT_SIDE: outside foot back (left) -> left-right foot offset 0.41m | STN-804 | gallery pass |
| STN-2284 | fixed | S2 | facing (live) | ST Two-point try (Shotgun vs Goal Line) / +Z / WR X,Z,H (x5) / snap+0.6s | LIVE_BODY_VS_ROOT: <=35deg between body and root (Block) -> 54deg clip BlockHitRStale_A | STN-802 | gallery pass |
| STN-2285 | fixed | S1 | facing | ST Two-point try (Shotgun vs Goal Line) / +Z / WR X,Z,H (x3) / presnap | VISUAL_FACING: <=25deg of yaw 0 -> body yaw -66 (hips -85, shoulders -46, root 0) | STN-801 + STN-802 | gallery pass |
| STN-2286 | open | S2 | facing (live) | DEF Dime vs I-Form Slot / +Z / QB / snap+1.5s | LIVE_BODY_VS_ROOT: expected <=35deg between body and root (Shuffle), now 75deg clip HandoffFlexboneFbTwogap_A | - | stance_gallery/DEF_Dime_vs_I_Form_Slot_plusZ_presnap_top.png |
| STN-2287 | open | S2 | facing (live) | OFF I-Form vs 3-4 Base / +Z / RT / snap+0.6s | LIVE_BODY_VS_ROOT: expected <=35deg between body and root (Block), now 102deg clip BGS__BGS_OLM_ShuffleStand_RStance_Loop | - | stance_gallery/OFF_I_Form_vs_3_4_Base_plusZ_presnap_top.png |
| STN-2288 | open | S2 | stance pose | OFF Kneel / +Z / RG / presnap | STANCE_LOW: expected head <= 1.45m (3-point), now 1.53m | - | stance_gallery/OFF_Kneel_plusZ_presnap_top.png |
| STN-2289 | open | S2 | stance pose | OFF Singleback Ace vs Goal Line Defense / -Z / RT / presnap | STANCE_LOW: expected head <= 1.45m (3-point), now 1.58m | - | stance_gallery/OFF_Singleback_Ace_vs_Goal_Line_Defense_minusZ_presnap_top.png |

## CAM rows: user camera, director, HUD, flow

| ID | Status | Severity | Category | Where | Issue | Fix | Verified by |
|---|---|---|---|---|---|---|---|
| CAM-001 | fixed | S1 | camera | MatchCameraRig / UserCameraMath / MatchDirector.BuildUserView | Default user camera was the high sideline TV camera (players ~60 px, line of scrimmage running vertically). User note 3: Madden / 2K8 style behind the QB. | `CameraMode.Behind` now carries three presets driven by pure `UserCameraMath`: Classic (default, All-Pro 2K8: 9 m up, 17 m behind the line, ~26 deg down, 46 deg lens, whole formation + 15-20 yd downfield), Madden (5.4 m, 10.5 m, 48 deg) and Wide (14.5 m, 21 m). Offense: behind the offense looking downfield; defense: behind the defense looking at the offense (stick basis follows the view); kicking: behind the kicker; returning: behind the returner looking at the kicker. Direction changes between possessions / quarters are handled (view direction = attack direction of the user's side in the PlayFrame). | `D:\PIXFootball\Renders\stance_camera\camera\*_plusZ_*.png`, `*_minusZ_*.png`; EditMode StanceCameraTests (behind / pitch / mirror symmetry for both directions) |
| CAM-002 | fixed | S2 | camera | UserCameraMath | Snap behaviour: no pull-back, no follow. | At the snap the camera eases back 3.2 m, up 1.8 m and widens 3 deg over 0.9 s (setting "Zoom out at the snap" 0..1); after a handoff / catch it follows the ball carrier from behind (10.5 m back, 6.4 m up) with a 0.5 s lead of his velocity, tracking sideways tighter than along the field; the ball in the air is followed with a capped lead. | camera sequences pass_offense / run_offense (t_0.5 .. after_catch) |
| CAM-003 | fixed | S1 | camera | Settings (Camera page), PixSettings | Camera options: only Broadcast / Behind / First person. | New Settings page CAMERA: Camera = Classic / Madden / Wide / Broadcast / First person, height, distance, zoom-out at the snap and field of view trims, first-person facemask, pre-snap stars; pause menu selector with the same five choices; the View button cycles Classic > Madden > Wide > first person > broadcast. | code, UI |
| CAM-004 | fixed | S1 | camera | MatchDirector | Broadcast camera is the default and `View` could land on it by accident. | The TV camera is forced for CPU-vs-CPU games only (and is an option for the user); `DefaultCamera` is not overwritten by simulated games; the cycle order puts broadcast last. | code |
| CAM-005 | fixed | S1 | director | ShotRules / BroadcastDirector | Cinematic cuts (pre-snap establishing shots, post-whistle crowd / coach cutaways) fought the user camera and covered play selection. | With a human playing: no cut before the snap after the game intro, post-whistle cuts at most 3 s and 1.6 s each, no crowd cutaway except after a touchdown. | EditMode UserCameraDirectorTests |
| CAM-006 | fixed | S2 | camera | MatchDirector.Toss | Coin toss was a black modal with buttons. | On-field coin toss (see STN-824). | `D:\PIXFootball\Renders\stance_camera\hud\coin_toss.png` |
| CAM-007 | fixed | S2 | camera | ReplayView | Replay "Behind QB" sat 2 m behind the QB at ground level (QA-124); "High" was a steep top-down at a skewed yaw (QA-205). | Behind QB = the Classic framing (7.4 m up, 12.5 m back); High = 24 m up, 27 m back (~42 deg). | code |
| CAM-008 | fixed | S1 | first person | PlayerAvatar.HideHead | The first-person camera sat inside the player's own forearms and hands (skin-coloured blob filling the screen, QA-127 / QA-202). | Arms and hands are hidden from the first-person camera (shadow only), restored afterwards. | code |
| CAM-009 | fixed | S2 | camera | UserCameraView | Kick / punt / return cameras: kickoff return showed only the kickers (QA-224). | Kicking team: behind the kicker looking downfield; return team: behind the returner looking at the kicker; ball in flight followed with a capped lead. | `D:\PIXFootball\Renders\stance_camera\camera\kickoff_*`, `punt_*`, `fieldgoal_*` |
| CAM-010 | fixed | S3 | perf | MatchCameraRig | Five PlayerPrefs reads + preset struct rebuilt every frame (QA-682). | Cached, refreshed twice a second. | code |
| CAM-011 | fixed | S2 | camera | UserCameraMath | No clamp: ball in the red zone put the camera 76 m out, behind the stadium wall (QA-680). | Camera z clamped to +/-63 m. | EditMode |
| CAM-012 | fixed | S3 | camera | UserCameraMath | Defense camera had no tuning of its own (empty branch, QA-687). | Behind the defense: 10 % deeper, 8 % higher, look point 3 m further so the line and the backfield read. | EditMode DefenseCameraSits... |
| CAM-013 | fixed | S3 | flow | MatchDirector | One exception in a stage of the game loop killed the whole match coroutine (QA-732); handlers never unsubscribed (QA-733); overlapping fades (QA-734). | Each stage runs through `Safe()` (logs, recovers, back to the menu after 5 failures); handlers unsubscribed in OnDestroy; a newer fade supersedes an older one. | code |
| CAM-014 | fixed | S3 | presentation | ReplayPlayer / ShotLibrary / BroadcastCamera / CrowdReaction | Replay ball silently missing (QA-865); broadcast lens not restored (QA-867); StadiumWide drifted 20 m per shot (QA-869); crowd animated while paused (QA-878). | Sphere fallback + warning; lens restored in End(); variant wraps at 3; crowd freezes with the pause menu (replays keep it moving). | code |
| CAM-015 | fixed | S1 | replay | ReplayRecorder / ReplayPlayer | Replays record bone rotations relative to the avatar root, so the new facing correction (and the line setback) would have been lost: linemen backwards again in instant replays. | The recorder stores the avatar root's yaw and pull-back per player and sample; the player restores them. | code |
| CAM-016 | fixed | S2 | HUD | MatchHud (bottom bar) | Top-left scorebug only. | Full-width bottom bar (2K8 layout: away / home abbreviation + crest + score + possession, down & distance, ball spot, quarter, clock, play clock) is the default; the top block is an option (Settings > Gameplay > Scorebug, pause menu); hint bar, toast, flag strip, pre-snap prompt moved above it. | `D:\PIXFootball\Renders\stance_camera\hud\bottom_bar.png`, `top_block.png` |
| CAM-017 | fixed | S2 | HUD | KickHud | No kick meter, no wind indicator. | Kick meter (TIMING / AIM, display only) and wind compass + mph (top right) for user FG / punt / kickoff. Built display only (the simulation had no wind and no kick timing / aim input) (the engine track then wired both: SIM-021 wind, SIM-023 kick meter, SIM-131 live integration). | `D:\PIXFootball\Renders\stance_camera\hud\kick_meter_banner.png` |
| CAM-018 | fixed | S3 | HUD | TvOverlays.StatTable | Stat lower third was a tall card. | Slim two-row table strip (# NAME TEAM + columns by role: passer CMP/ATT/YDS/TD/INT, rusher ATT/YDS/AVG/LNG/TD, receiver REC/YDS/AVG/LNG/TD, defender TKL/SACK/INT) with 6 EditMode tests for the column choice. | EditMode StatTableTests |
| CAM-019 | open | S2 | camera | MatchCameraRig first person | `MoveBasisYaw` in first person follows the player's velocity (a held diagonal stick turns the basis, QA-681). Not the default camera any more. | - | - |
| CAM-020 | open | S3 | camera | BroadcastCamera / ShotLibrary | Depth-of-field focus on the wrong subject in tight shots (QA-220, QA-249), goal-line oblique broadcast angle (QA-144), broadcast sideline swaps with the offense direction (QA-685), fixed broadcast offsets (QA-684), replay sideline side (QA-866). Broadcast layer is now secondary (CPU-vs-CPU and option). | - | - |
| CAM-021 | wontfix | S3 | camera | various | `[DefaultExecutionOrder(-200)]` makes the first-person anchor read the head bone one frame early (QA-686); camera shake fields write-only (QA-688); director clears hints during shots (QA-870); autotest close-up ring / DoF cases (QA-233, QA-269). | By design / cosmetic: the user camera is not first person by default and the other cases are debug-only. | - |

## Audit crosswalk (`Docs/QA/audit.md` rows assigned to STN / CAM)

| QA row | Result | Where recorded |
|---|---|---|
| QA-018 | fixed | STN-805, STN-803 |
| QA-019 | fixed | STN-805 (neutral zone, DL 1.0 yd, 0.36 m setback) |
| QA-020 | fixed | STN-805 (tight symmetric splits), STN-806 |
| QA-021 | fixed | STN-801, STN-802 |
| QA-022 | fixed | STN-803 (per-position stances) |
| QA-023 | fixed | STN-803 (kickoff team faces the return team; spacing is data-even at 5 yd, the picture was the old facing) |
| QA-048 | fixed | STN-803 (PAT: facing + stances); holder / kicker clips STN-817 |
| QA-062 | fixed | STN-805 |
| QA-079 | fixed | STN-801 |
| QA-088 | open (ANI) | STN-819 |
| QA-091 | fixed | STN-801, STN-805 |
| QA-094 | open (SIM) | defensive alignment vs the offense's set: QA-456 |
| QA-125 | fixed | STN-801 (3-point stance now faces the line; "bowing" was the +133 deg clip) |
| QA-126 | fixed | STN-801 |
| QA-142 | fixed | STN-803 |
| QA-143 | wontfix | user-called Shotgun (QB and RB side by side is legal) |
| QA-175 | fixed | STN-803 |
| QA-177 | fixed | STN-801 |
| QA-203 | open (ANI) | STN-819 |
| QA-210 | fixed | STN-801, STN-805 |
| QA-213 | fixed | STN-803 |
| QA-215 | fixed | STN-803 |
| QA-223 | fixed | STN-803 |
| QA-224 | fixed | CAM-009 |
| QA-226 | fixed | STN-801, STN-805 |
| QA-227 | wontfix | legal Shotgun alignment |
| QA-241 | fixed | STN-820 |
| QA-262 | open | STN-826 |
| QA-263 | fixed | STN-801, STN-805 |
| QA-284 | fixed | STN-805, CAM-001 (2K8 look: OL shoulder to shoulder, DL nose to nose, LBs 2-point) |
| QA-306 | fixed (presentation) | STN-803: `StanceRules` derives the stance from position + alignment + formation; no engine stance state needed |
| QA-307 | fixed (presentation) | STN-801 / STN-803: pre-snap facing already correct in the sim (gallery SIM_FACING 100 %); CB aimed at his receiver is STN-818 |
| QA-308 | fixed | STN-808 |
| QA-367 | open (SIM) | STN-818 |
| QA-372 | fixed | STN-809 |
| QA-424 | open (SIM) | STN-818 |
| QA-452 | wontfix | returners DO face the kicker from the spawn heading (0,-1): correct |
| QA-455 | fixed | STN-805 (visual setback) |
| QA-456 | open (SIM) | defensive alignment adjustments |
| QA-628 | open (SIM) | STN-818 |
| QA-636 | wontfix | holder faces the LOS, kicker faces +Y: correct for the field-goal geometry |
| QA-643 | fixed | STN-813 |
| QA-644 | fixed | STN-814 |
| QA-645 | fixed | STN-815 |
| QA-649 | fixed (selection) / open (clips) | per-role keys by ANI v0.8 in `StanceRules.ClipKeys`, STN-816 for the clips |
| QA-650 | fixed (selection) / open (clips) | STN-816 |
| QA-651 | fixed (selection) / open (clips) | STN-816 |
| QA-652 | open (ANI) | STN-817 |
| QA-653 | fixed | STN-811 |
| QA-654 | wontfix | STN-825 |
| QA-671 | fixed | STN-810 |
| QA-672 | fixed | STN-808 |
| QA-673 | open | flip / shift teleports: a walk-to-spot blend is not implemented (S3) |
| QA-674 | fixed | STN-822 |
| QA-675 | fixed | STN-821 |
| QA-676 | wontfix | the preview build is what the play-call screen shows; the second build is the snap; harmless |
| QA-679 | open (ANI) | STN-819 |
| QA-510 | fixed | STN-806 |
| QA-524 | wontfix | STN-825 |
| QA-540 | fixed | STN-807 |
| QA-620 | open (SIM) | `PlayerBio.Hand` is not read by the sim |
| QA-654 | wontfix | STN-825 |
| QA-742 | fixed | the Formation Gallery asserts facing / stance numerically (headless) + EditMode StanceCameraTests |
| QA-899 | fixed | STN-801 / STN-803 (selection and facing); distinct OL / C / TE clips STN-816 |
| QA-900 | fixed | STN-801 / STN-803 |
| QA-901 | fixed | STN-801 (LBs face the line; `StanceClips` prefers the real LB take) |
| QA-926 | fixed | STN-812 |
| QA-1031 | fixed | STN-802 + STN-815: the closed loop converges even when a cleaned clip also gets an offset; rebuild the table after the clip clean / re-import |
| QA-1032 | fixed | STN-802 (heading is also measured every frame, not only at three instants) |
| QA-029 | fixed | CAM-001 |
| QA-124 | fixed | CAM-007 |
| QA-127 | fixed | CAM-008 |
| QA-128 | open | first-person at a lineman's helmet height looks at the grass: inherent to the mode |
| QA-144 | open | CAM-020 |
| QA-202 | fixed | CAM-008 |
| QA-204 | open | not reproducible (the first-person rig only adds <= 0.55 degrees of bob roll) |
| QA-205 | fixed | CAM-007 |
| QA-220 | open | CAM-020 |
| QA-233 | wontfix | CAM-021 |
| QA-249 | open | CAM-020 |
| QA-269 | wontfix | CAM-021 |
| QA-283 | fixed | CAM-001 |
| QA-298 | fixed | CAM-002, CAM-005 |
| QA-680 | fixed | CAM-011 |
| QA-681 | open | CAM-019 |
| QA-682 | fixed | CAM-010 |
| QA-683 | fixed | CAM-004 |
| QA-684 | open | CAM-020 |
| QA-685 | open | CAM-020 |
| QA-686 | wontfix | CAM-021 |
| QA-687 | fixed | CAM-012 |
| QA-688 | wontfix | CAM-021 |
| QA-732 | fixed | CAM-013 |
| QA-733 | fixed | CAM-013 |
| QA-734 | fixed | CAM-013 |
| QA-865 | fixed | CAM-014 |
| QA-866 | open | CAM-020 |
| QA-867 | fixed | CAM-014 |
| QA-869 | fixed | CAM-014 |
| QA-870 | wontfix | CAM-021 |
| QA-871 | open (not camera) | the two-minute-warning card belongs to the broadcast overlays / flow track |
| QA-878 | fixed | CAM-014 |

## Open items for other tracks
- ANI: distinct stance takes (OL / C / TE / DL 3 and 4 point, wide-9, press and off corner, kicker, holder, snapper, punter), rebuilding `PIX_PlayerLocomotion.controller` after the `CycleOffset` parameter was added (until it is rebuilt every locomotion pose is a T-pose and the runtime logs `Parameter ... does not exist`); after any clip clean / re-import run `PIX Football > Assets > Build Clip Facing`.
- SIM: route / zone / pre-engagement facing (STN-818), defensive alignment adjustments to the offense's set (QA-456), `PlayerBio.Hand` (QA-620).
- Lead / release: `PixClipFacing.asset` and the mirrored stance clips live under `Assets/_Licensed/Resources` (git-ignored like the rest of the licensed art): run `Build Clip Facing` in the release pipeline after `Build ArtLibrary`.
