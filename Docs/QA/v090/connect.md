# v0.9.0 CONNECT track log

Track CONNECT of the v0.9.0 "Match Engine" release: transitions, contact pairs, catching, run game, blocking animation, timing / sync, pre-snap motion and the camera / presentation rows (137 rows of `ENGINE_AUDIT_issues.csv`, track = CONNECT). The user's complaint: "more connections between animations, feel more simulated, lots of animation issues".

Owned files: `ActionAnimator`, `Gameplay/Contact/**`, `BallFollower`, the catch / hand-off view logic, render interpolation, the pre-snap motion view and the transition logic of `SimAgentView` (new partials `SimAgentView.Connect.cs`, `PlayStage.Connect.cs`, `AnimKit.Connect.cs`). Small additive sim hooks: `PlaySimulation.Connect.cs` (step callback, QB hooks), `KickGeometry`, hand-off distance (`OffenseAI.Qb.cs`), kick geometry constants and rusher leaps (`SpecialTeamsAI.cs`). The camera and presentation-director rows were done by a delegated sub-agent (files under `Gameplay/Cameras`, `Presentation/Director`, `BroadcastDirector*`, `ReplayController`, `MatchAudio`, six lines of `MatchDirector`).

## What the connection layer is (one paragraph per mechanism)

- **Render interpolation (E-006).** The stage snapshots every agent and the ball after each fixed 60 Hz step (`PlaySimulation.AfterStep`) and draws between the last two states with the step phase (`StepAlpha`). A teleport (more than 1.2 m in one step) is never smeared.
- **Pop filter (E-250).** An inertialization pass on the pelvis and chest (`SimAgentView.PopFilterLate`): a jump of more than 800 deg/s (1300 for the chest) or 9 cm in one frame, whatever its cause (clip change, pair alignment, yaw offset), is absorbed by a decaying offset (about 0.09 s) instead of being drawn.
- **ActionAnimator** (E-260/262/264/265/266, E-286, E-376): upper-body mask re-decided every frame, the outgoing full-body clip gives the legs back to locomotion over the fade, the facing offset blends with the pose weights, a third clip request waits for the hidden slot's fade, per-clip fade floors (impact takes 0.08 s).
- **Snap and pre-snap** (E-248/249/251/252/258): the simulated Stance to Idle at the snap keeps the stance; the get-off is chosen by the FIRST action and its heading (`AnimKit.GetOffFor`); shifts, audibles, flips and the break from the huddle are walked (a jog / walk glide to the simulated spot); the snap waits for the break.
- **Blocks** (E-162 to E-174, E-185): one seed for both actors of an engagement, role- and style-specific plans (pass set footwork by velocity, run-block drive, hands-up stalk for receivers / backs / defenders), matched pair takes by leverage or pass-rush move, a hand-fight loop after the take, pull weights per agent with a 0.2 s decay and the double team handled, pancake finish for the blocker.
- **Contact** (E-205 to E-233, E-241, E-348/349, E-362, E-376 to E-378): momentum-aware take choice and a share of the carrier's speed slid through the hit, takes at the pace of the game, each actor released when his own take ends, a Broken take ended by the next tackler, struggling drags, gang tacklers delayed / matched / angled, body-size-scaled pair separation, impacts at the contact moment, ragdoll interpolated and pre-built, view root frozen under the ragdoll, a catch-up walk (view offset) instead of glides and pops, contested catch / jam / truck / sack-broken pairs.
- **Catching and the ball** (E-122 to E-137, E-253 to E-256, E-298, E-387): reach IK to the real ball, the displayed ball drawn into the catcher's hands over the last 0.2 s, catch-hold then tuck, hand-off exchange centred on the simulated exchange, forearm-relative carry pose and tuck IK, orientation blends, holder spot, punt drop, tee until the kick, snap between the legs.

## Tools (re-runnable)

- `Assets/_Project/Scripts/Editor/Tools/EngineAudit.cs` (`-pixEngineAudit -pixEngineAuditPhase metrics|strips`): clip changes per second, hip snaps, pelvis jumps.
- `Assets/_Project/Scripts/Editor/Tools/ConnectAudit.cs` (new, `-pixConnectAudit -pixConnectAuditTag before|after -pixConnectAuditPhase metrics|strips`): teleports, stance dropped at the snap, ball against hands at hand-offs and catches, the carrier's speed through tackles, rendered smoothness at 144 Hz, pre-snap flip / shift / huddle break, and the strips (tackle, hand-off, catch and run, OL / DL engagement, motion man). The "before" numbers come from the unchanged v0.8.0 tree (a private copy of the audit tree), the "after" numbers from the private copy with the track's changes.

## Counts

| Status | Rows |
|---|---|
| FIXED | 105 |
| PARTIAL | 24 |
| WONTFIX | 8 |
| **All CONNECT rows** | **137** |

## Per row

| ID | Sev | Title | Status | What was done / why not |
|---|---|---|---|---|
| E-122 | S1 | Contested-catch pair restarts the receiver's catch animation on the catch frame | FIXED | The stage asks the contact director for a contested pair when the catch is a duel (defender within 1 yd, receiver under 2 m/s, ball under 3 yd) and plays no plain catch over it; the pair starts so its contact frame is the catch frame. (PlayStage.Flow FlowAction, ContactDirector.WantsContestedCatch) |
| E-123 | S1 | The contested-catch pair owns the receiver for 2 to 3 s while the sim runs him downfield | FIXED | ReleaseAAfter = 0.35 s after the contact: the receiver leaves the take and runs on (view offset closes the gap), only the defender stays in it. |
| E-124 | S1 | Punt returners carry the ball in the quarterback hold pose | FIXED | QB-hold branch restricted to the quarterback / holder / punter before the kick (PlayStage.Connect ConnectHeld); returners and loose-ball runners use the carry pose. |
| E-125 | S2 | Ball is far from the hands at the moment of the catch and in the pocket | PARTIAL | Hands reach for the real ball from 0.32 s before the catch (reach IK) and the displayed ball is drawn into the catcher's hands over the last 0.3 s; from the catch frame the ball sits between both hands. Measured: worst ball-to-hand 0.05 to 0.4 s after the catch 0.176 to 0.140 m. NOT fixed: at the simulated catch frame the ball still arrives from about 0.6 m out (the simulation completes a catch about a metre from the body) and the pull makes its per-frame displacement larger (0.29 to 0.55 m). |
| E-126 | S2 | Ball jumps from the extended hands to the forearm at the catch | FIXED | BallFollower catch-hold mode: both hands 0.14 s, then a 0.3 s tuck to the carry arm (blend of position and orientation). |
| E-127 | S2 | Catch pose is chosen from variants only; hands do not reach the real ball | FIXED | SimAgentView.ReachBall / ArmIkLate: two-hand diamond (hands 7.5 cm either side of the ball) or one-hand reach, arm length clamped, weight 0.10 s in / 0.22 s out. |
| E-128 | S2 | Catch key list excludes dive, contested, one-hand, toe-tap, fair catch | FIXED | AnimKit.CatchFamily: catch_dive / catch_one_hand at full stretch, catch_toe_tap within 1.6 yd of a sideline, fair_catch / kick_return_catch for returners, interception for defenders, the contested pair in traffic, a drop plan; ties no longer go to the first key. |
| E-129 | S2 | Contested catch is picked for any defender within 1.9 yd and always "receiver wins" with a stationary jump pose | FIXED | Contested only for a duel (see E-122): carrier speed under 2 m/s, defender under 1.0 yd, ball under 3 yd (was 1.9 yd and always). |
| E-130 | S2 | No contested pair for interceptions, deflections, drops, breakups | PARTIAL | Reactions for Deflection (receiver flinch), Interception (receiver frustration take), Drop (hands never close: AnimKit.CatchFamily Dropped plan from the Drop event), defenders reach ahead (swat / interception takes). Open: the contested Failure / CritFailure PAIR takes are not in PixContact (the ContactClipBuilder pair list lacks catch_fail): needs a table rebuild. |
| E-131 | S2 | Fumbled ball pops when the displayed carrier is offset from the sim body | FIXED | BallFollower.SetFree blends the free ball from the hand over 0.2 s up to 3 m of offset (was zeroed above 1.5 m). |
| E-132 | S2 | Switching the carry arm teleports the ball across the body | FIXED | SetCarry with a changed side is an attach event: 0.2 s blend with a 7 cm arc across the chest. |
| E-133 | S2 | Defensive backs get no read-ahead for catches and jump balls are late | FIXED | PlayStage.Flow FindBestDefender: the defender with the smallest miss distance reaches ahead (swat for a ball above 2.6 yd, interception reach otherwise); height gate 4.9 yd, reach window 0.5 s. |
| E-137 | S3 | Ball orientation snaps at every attach | FIXED | Ball orientation is slerped from the last orientation over the attach blend (BallFollower.ApplyAttach), also on release. |
| E-139 | S1 | Hand-off pair has the same seed bug and no distance correction | PARTIAL | Both actors of a hand-off use one pair seed (PairSeed) so they play the matching A / B take; exchange timing synced (E-247). Not done: left / right choice by the back's side (the library hand-off takes carry no side tokens) and pulling the pair to the authored distance. |
| E-142 | S2 | Spin clip is a full-body override on a running carrier | FIXED | Spin is an upper-body plan over the locomotion legs (AnimKit.SpinDir). |
| E-144 | S2 | Catch commit window suppresses the juke/stiff-arm/truck/spin that follows a catch | FIXED | The catch commit ends at the hands-secure frame (0.25 to 0.6 s) and ranks 4, below the carrier moves (5). |
| E-145 | S2 | Spin direction is random and unrelated to the sim spin | FIXED | The spin take is chosen by the sign of its measured turn (MoveTakeTable turnDegrees) against the simulated spin direction. |
| E-146 | S2 | Ball carrier holds the ball in the arm "away from the defender" only for the ball, not the pose | FIXED | Carry tuck IK (SimAgentView.SetTuck): the arm that holds the ball folds it to the ribs, the other swings. |
| E-147 | S2 | Stiff-arm win ragdolls the defender even though the sim only stumbles him | FIXED | A won stiff arm no longer ragdolls the defender: he staggers with the simulated Stumble (new Stumble take). |
| E-148 | S2 | Carry pose is a forearm lerp with world-axis offsets, no per-body fit, no hand grip | FIXED | Carry pose measured from forearm, hand and chest bones (inward = toward the chest), plus the tuck IK nudge. |
| E-158 | S3 | Carrier moves rely on animator triggers: no commit and they can queue | FIXED | An unconsumed carrier-move trigger is reset 0.3 s after it was set (LocomotionAnimator.ResetTrigger). |
| E-159 | S3 | Hand-off ball slides between hands in 0.12 s | FIXED | Hand-off exchange mode: the ball travels from the quarterback's chest hold to the back's carry pose over 0.24 s (BallFollower.SetExchange). |
| E-162 | S1 | Engaged OL/DL pairs are picked from different takes | FIXED | Pair seed of an engagement = ids of the pair + play index, identical for both actors (and a double-team partner): AnimKit.EngagedPair. |
| E-163 | S1 | Pass-protection "Block" is a stationary pocket-stand loop for all blockers and for run plays | FIXED | AnimKit.BlockPlan: pass set = stand / kick-slide step / shuffle by velocity against facing; run block = ol_run_block footwork; the BlockStyle comes from the assignment. |
| E-164 | S1 | All blocking actions by DL, WR, CB and RB use the offensive-line pass-set loop | FIXED | WR / CB / S stalk and RB / DL / TE chip use hands-up upper-body plans over their own legs; no pocket loop for non-linemen. |
| E-165 | S1 | OL never gets the fire-out: Block plan is "valid" so get-off is skipped | FIXED | OL leaving the stance get the run-block footwork (run) or the pocket step (pass) as their first move (BlockPlan valid), never the idle. |
| E-169 | S2 | Mocap clips are authored facing other directions and the correction is visible in engagements: body yaw is 50-110 degrees off the sim facing in 5% of engaged frames | PARTIAL | Pair pulls turn both bodies face to face, the clip facing offsets now blend with the pose weights. Residual yaw error is reported in the audit; no per-clip hand-measured gap. |
| E-170 | S2 | Setback of 0.36 m eases out at 1.6 m/s after the snap: whole line skates forward | FIXED | The setback is given back over the first 1.5 m the lineman really travels (smoothstep), no longer at a fixed 1.6 m/s. |
| E-172 | S2 | Pancake knocks only the loser; the blocker plays nothing | FIXED | The pancaking blocker plays a drive-finish take (ol_block_push window) while the loser staggers 0.12 s and goes to the ragdoll. |
| E-173 | S2 | OL/DL engagement is a constant 0.78 m pull on the primary blocker only | FIXED | Pull distance by take type (run 0.72, pass 0.78) scaled by both body sizes, every blocker of a double team handled. |
| E-174 | S2 | Pulled pair pops back when the engagement ends | FIXED | Per-agent pull weight decays over 0.2 s after the engagement ends. |
| E-185 | S3 | Pass-rush moves: Rip and Speed play the swim take, wrong-arm never plays, last pose held | PARTIAL | Moves map 1:1 where the library has a pair (bull, swim, speed, spin, rip via the escape takes, shed); the hand fight loops after the take (no frozen last pose). Wrong-arm is not played: the simulation has no wrong-arm move. |
| E-188 | S1 | Defense press: jam hands exist only on the stance and drop at the snap | PARTIAL | Press jam hands stay up 0.5 s after the snap and the Jam event plays a short collision pair (receiver released at 0.4 s). Not wired: the wr_release take for the receiver. |
| E-189 | S2 | Defenders (interception, pass breakup) get their reach too late and with the wrong clip | FIXED | Defenders reach ahead of the ball with the swat / interception takes (see E-133). |
| E-191 | S2 | No turn-and-run, hip flip or break-on-ball for coverage | PARTIAL | Hip flip: man_turn_run 135 / 180 degree takes on the Backpedal / Shuffle to Run edge. Not wired: break-on-ball, zone drop, read step (LOCO / QBSTANCE clip owners). |
| E-205 | S1 | Every authored tackle is slower than the game: a sprinting carrier stops dead at contact | FIXED | Hard speed weight in take choice (0.55 per m/s of deficit) and momentum carry-through: the surplus of the carrier's speed over the take's own is slid through the hit with a 0.28 s time constant; lead-in prediction uses the full lead. |
| E-206 | S1 | A second tackler on a carrier who is still in a Broken take only adds a "gang" joiner; the carrier never goes down in the picture | FIXED | A tackler arriving on a carrier in a Broken / Truck / SackBroken take ends that take (blended into the wrap) and starts a real tackle. |
| E-207 | S1 | The beaten tackler of a Broken take is dragged along the ground by the carrier's sim progress | FIXED | The authority correction moves the carrier only; the beaten tackler gets at most 25 cm of it. |
| E-210 | S2 | Generic tackle: tackler and carrier choose independent A takes | FIXED | Tackler and carrier share one pair seed (AnimKit.TacklePair). |
| E-211 | S2 | Stumble action has no clip | FIXED | AnimKit.Stumble: the collision-and-stagger takes, over the legs once moving. |
| E-212 | S2 | Fallen has no get-up: the held fall pose pops to a run | FIXED | Fall plans are no longer held forever: fall, then the get-up take on the Fallen end, the view offset walks the body to the simulation. |
| E-213 | S2 | Tackle take starts a fixed 0.55 s in, not at its contact moment | FIXED | The generic tackle take starts 0.25 s before its measured impact moment when the timing table has one. |
| E-214 | S2 | View yaw of a contact actor never hands back to the simulation | FIXED | A carrier who runs on has his yaw handed back to the simulation with the authority ramp. |
| E-215 | S2 | Carrier is locked out of the match animation logic until the longer clip B finishes | FIXED | Each actor is released when HIS take ends (carrier at EndA), the other keeps playing. |
| E-216 | S2 | Get-up part of every tackle take is never seen: whistle wind and fade cut it | FIXED | The post-play window waits (budget 3.1 s) until nobody is in a take, get-up or ragdoll (ContactDirector.Busy). |
| E-217 | S2 | Tackle variant choice ignores tackler height, body size, closing direction and carrier carry state | FIXED | Score uses lowB against the tackle type, hip distance against the body sizes, a six-take history; closing direction through the bearing. |
| E-218 | S2 | No mirroring: a side that has no authored variant plays the wrong side take | WONTFIX | Needs mirrored pair data and mirrored contact clips: none exist (MirroredClips holds stance clips only). Mitigation: the pair alignment rotates the take to the real approach axis. |
| E-219 | S2 | Big hits reuse 3 takes, all from the carrier's left, and "Failure" takes are never used | PARTIAL | Variety history added and the BigHitShrug kind (Failure takes) exists in TackleVariants. Not routed: the simulation gives no signal for a shrugged big hit. |
| E-220 | S2 | SackBroken, Truck and Collision pair kinds are never produced | FIXED | BrokenTackle on the QB routes to SackBroken; the Truck event plays a Truck pair; the jam plays a Collision pair. |
| E-221 | S2 | Sack takes depend on the Sack event being the very next log entry | FIXED | The sack is found within the next 3 log entries or from the tackle itself (QB in the pocket). |
| E-222 | S2 | A dragged (held) take is a frozen statue that slides with the carrier | FIXED | A dragged take is a struggle: a 0.45 s window ping-pongs while the tackler hangs on, the pair stays pinned to the carrier. |
| E-223 | S2 | Gang tackle joiners are not matched to the carrier's actual pose, timeline or to each other | FIXED | Joiners start 0.25 s after the primary contact, avoid used takes, stand at their own angle. |
| E-224 | S2 | Pairs are aligned by hips only and ignore body size, so S/M/L and OL/DB hits gap or interpenetrate | FIXED | Pair separation scales with both body radii (0.85 to 1.25). Not done: tackler hand IK to the carrier's torso. |
| E-225 | S2 | The warp from sim to take is a 0.18 s position and yaw snap | FIXED | Lead-in 0.36 s (was 0.18 s) for the warp. |
| E-226 | S2 | Ragdoll runs in FixedUpdate at 50 Hz with no interpolation | FIXED | Ragdoll bones are drawn between the previous and the current physics state with the fixed-step phase. |
| E-227 | S2 | The ragdoll is built lazily on the first hit of each view and hitches that frame | FIXED | Stage.WarmAllBodies builds every roster body (size x base) and its ragdoll behind the loading screen. |
| E-228 | S2 | The ragdoll rides along with the sim agent through the view root (verify in play) | FIXED | The view root stays where the hit began while the ragdoll is active; the bones are no longer dragged by the simulated agent. |
| E-229 | S2 | Get-up take is a random pick over all pairs by face-up sign only | PARTIAL | Get-up chosen by how the body lies (chest normal) among its class. Dedicated getup_* clips not used (their alignment is not measured). |
| E-230 | S2 | Ragdoll recovery can freeze the body mid-tumble | FIXED | MaxActive 2.0 s with a 0.6 s speed-gated extension. |
| E-231 | S2 | Solo get-up glides to the sim position, up to several metres, in 0.7 s | FIXED | The get-up plays where the body lay and he walks on to the simulation at 3.2 m/s (view offset), no 0.7 s glide. |
| E-232 | S2 | QB hit after the throw and roughing have no reaction | FIXED | QbHit event: upper-body flinch take on the quarterback. |
| E-233 | S2 | Fumble and recovery have no strip, scramble or pile | PARTIAL | The recovering player dives (fumble_dive). Not done: strip reaction on the carrier, scramble clips for bystanders, the pile. |
| E-241 | S3 | Ending a drag with a broken tackle cuts the wrap pose instantly | FIXED | Breaking a drag blends the held pose into the broken take (no Stop then Play). |
| E-247 | S1 | Hand-off transfers the ball instantly with no blend, and can fire with the runner 2.8 yd away | FIXED | The ball travels hand to hand over 0.24 s, centred on the simulated exchange (the quarterback reads ahead by 0.12 s). The exchange distance was tightened to 1.15 yd by this track and then tuned to 1.3 yd by SIM (the outside runs missed the exchange 25 percent of the time at 1.15 / 1.7). |
| E-248 | S1 | At the snap every player drops through a 0.1-0.2 s Stance-to-Idle action before the first move (stands up out of the stance) | FIXED | The simulated Stance to Idle at the snap keeps the stance clip for 0.5 s; the get-off is chosen by the first real action. |
| E-249 | S1 | Pre-snap shifts, audibles, flips and motion setup teleport the players | FIXED | Shifts, audibles, flips and relayouts are walked: the view glides to the new spot at a walk / jog (PlayStage.Connect GlideStep); the man in motion leaves his stance with the wr_release take. |
| E-250 | S1 | Bone pops: pelvis and chest snap in the middle of play, mostly in the line and at block state changes | PARTIAL | Pop filter (SimAgentView.PopFilterLate): pelvis / chest jumps over 800 / 1300 deg/s or 9 cm are absorbed by a decaying offset. Measured: hips snaps over 900 deg/s 3,715 to 873, chest snaps 2,277 to 327; pelvis jumps over 10 cm 491 to 417 (target near 0 not met: residual snaps are the root yaw slew in the get-off clips and the stance pose layer of another track). |
| E-251 | S1 | Get-off plays a forward clip for backpedal and zone players, full body for up to 1.5 s | FIXED | AnimKit.GetOffFor chooses the get-off by the first action and heading (backpedal gets the back-turned break, shuffle the lateral one, linemen the fire-out), window 1.0 s. |
| E-252 | S1 | No huddle, no break to the line: players appear in formation after a black cut | FIXED | Huddle ring 8.5 yd behind the ball, break to the line at a jog, defense jogs up; the snap waits (MatchDirector gate). View-level only: the sim and the play clock are unchanged. Audit: no single-frame jump above 0.09 m in the break. |
| E-253 | S1 | Holder never puts the ball on the turf: it stays in a QB chest hold, then teleports to z=0.4 yd | FIXED | BallFollower spot mode: the holder steadies the ball upright on the turf; the sim launches from the same height (KickGeometry). |
| E-255 | S1 | Kickoff ball falls off the tee the moment the play goes live | FIXED | The tee ball stays on the tee until the kick is struck, at the launch height (KickGeometry.TeeHeightYd). |
| E-256 | S1 | Punt drop is missing: ball jumps from chest to 0.8 yd ahead at knee height | FIXED | The punter drops the ball for 0.34 s into the foot; sim contact point 0.55 yd ahead at 0.33 yd. |
| E-258 | S2 | A stance clip keeps playing while a receiver runs his route and a forward-sprint transition clip plays under the back-pedal | FIXED | The stance clip is replaced at the first real action by the get-off / route; no stance clip under a route. |
| E-259 | S2 | Clip thrash: linemen and quarterbacks change the playing clip about once a second and 1-5 times within 0.25 s of the previous change | PARTIAL | 0.4 s dwell for Block / Engaged / Shed / Idle / Stumble / locomotion flips, no restart of a pair that is already playing, run-block footwork settles into its own tail. Measured: flips under 0.25 s apart per play p95 OL 3 to 2, DL 5 to 2; the clip-change RATE went up (OL 1.19 to 1.47, WR 0.36 to 0.78) because get-offs and pair takes are new clips; the under-0.5 target is not met. |
| E-260 | S2 | Stationary full-body overrides over a body the sim keeps moving | FIXED | The upper-body mask is re-decided every frame by the simulated speed (2.0 / 1.2 m/s hysteresis). |
| E-262 | S2 | Masked clip started over a full-body clip: legs pop when the outgoing slot is dropped | FIXED | An outgoing full-body clip gives the legs back to locomotion over the same fade when the newcomer is upper-body masked. |
| E-263 | S2 | Block->Engaged->Shed flip without commit; each flip restarts a paired take | FIXED | Dwell plus no restart of the pair that is playing. |
| E-264 | S2 | Facing offset of a new clip is applied the moment it starts and slewed at 900 deg/s | FIXED | The facing offset is blended with the poses' own weights (ActionAnimator.FacingOffset). |
| E-265 | S2 | Loop yaw decays at 420 deg/s on leaving locomotion: body spins on Backpedal->Jump/Throw/Tackle | FIXED | The loop yaw leaves with the locomotion layer weight instead of unwinding at 420 deg/s. |
| E-266 | S2 | Third clip request recycles a still-fading slot: pops on rapid sequences | FIXED | A third request waits for the hidden slot's fade (0.1 s) instead of recycling it. |
| E-267 | S2 | Coverage matrix counts library clips, not playing clips | PARTIAL | Docs/AnimationCoverage.md gets a runtime-use section with the generated unused-key list (103 of 240 keys have no code reference; the QB, locomotion and stance ones belong to the other tracks). |
| E-268 | S2 | Every play is built twice for the user (preview + real) | FIXED | The call-screen preview is kept when the call did not change. |
| E-269 | S2 | Pools are pre-warmed with the Std body only; the other 4 bases are created mid-play | FIXED | Warm builds every roster body. |
| E-270 | S2 | A fade to black (about 0.5 s) bookends every play | FIXED | Fade dips shortened to 0.1 / 0.12 s (the walk-in carries the cut). |
| E-271 | S2 | After the whistle everyone just stops: no help-up, no walk back, no pile-on | PARTIAL | After the whistle the nearby players gather round the spot at a walk and the rest drift in; touchdowns and kicks keep their own choreography. Not done: the hand-up pair. |
| E-272 | S2 | A dropped pass and a catch play the same action, so the anim cannot show a drop | FIXED | The Drop event marks the receiver's catch as dropped (hint), the catch plan stops before its hands-secure frame. No new AgentAction (replays serialize the enum). |
| E-273 | S2 | AgentAction has no QB-specific phases (Dropback, Set, Windup, Scramble, Slide, Fake, Throw-away); the anim must infer from Backpedal / Idle / Run / Throw | WONTFIX | AnimHints.Qb already carries the QbPhase to the view (v0.7.0 flow); a new AgentAction would break every switch and the replay format. QBSTANCE consumes h.Qb. |
| E-274 | S2 | Animation hooks expose only drop point, step count, roll flag and hand-off target; missing: set time, drop progress, read target, release ETA, left / right hand, mesh point | FIXED | PlaySimulation.QbSetTime, QbDropProgress01, QbLookTargetId, QbReleaseEta, QbThrowsLeft, MeshPoint. |
| E-277 | S2 | Beaten at the line: the corner is "turned around" by a 0.25-0.40 s backward drift at 2 yd/s, then snaps into full-speed trail; no hip-flip transition | PARTIAL | View-side hip-flip take (E-191). The sim deceleration model of the beaten corner is SIM's. |
| E-279 | S2 | Pancake drops the defender to the ground instantly with his speed preserved; no stagger or knock-back | PARTIAL | View-side: the loser staggers 0.12 s before the ragdoll, the blocker follows through. The sim still sets Fall instantly (SIM). |
| E-280 | S2 | Shed ends the engagement with an instant 1.6 yd/s velocity kick and flips both states in the same tick; no hand-off pose | WONTFIX | Sim velocity kick and state flip: SIM owns BlockEngagement; the contact view eases the pull (E-174). |
| E-281 | S2 | Engagement starts the moment the bodies touch: the Engaged action begins without a punch frame, indistinguishable from the approach | WONTFIX | An EngagePunch window needs a new sim action state (SIM); the pair takes begin with their own punch frame. |
| E-283 | S2 | kick_return_catch clip is never played (generic WR catch is used) | FIXED | kick_return_catch is played for returners (CatchFamily ReturnCatch). |
| E-284 | S2 | Fair catch has no signal, no gesture, play just ends | PARTIAL | fair_catch take at the catch frame; the signal before the catch needs the fair-catch flag earlier than the sim sets it. |
| E-286 | S3 | Fade floor of 0.22 s softens contact and quick moves | FIXED | Per-category fade floor: impact takes (tackle, catch, hand-off) 0.08 s, locomotion hand-overs 0.22 s. |
| E-287 | S3 | Stance->Pocket plays the same clip again at another phase | FIXED | Stance to pocket continues the playing loop. |
| E-288 | S3 | Mesh/Loco switch stops the crouch with a plain fade, no stand-up transition | PARTIAL | The crouch is let go over 0.38 s (was 0.22 s) and the pop filter absorbs the rest. The under-center open-step clip is not authored. |
| E-289 | S3 | Dead plan builders and dead hints | PARTIAL | ReturnCatch wired through CatchFamily, MotionStart added. Cadence, CornerIdle, IdleVariant, Stop, Cut, BreakOnBall stay: owned by LOCO / QBSTANCE. |
| E-290 | S3 | Touchdown celebration walkers run straight lines at 6.3 m/s and stop dead | FIXED | Walk speed profile: accelerate, ease out over the last 2 m. |
| E-291 | S3 | A thrown exception in the game loop makes all 22 players vanish mid-play | FIXED | The views stay visible through a 0.15 s fade before the failure branch clears the stage. |
| E-292 | S3 | Contact director is reset after the views were already returned to the pool | FIXED | Contacts.Reset runs before the views go back to the pool. |
| E-293 | S3 | During the play-call screen the preview field is frozen after one Present | FIXED | The call-screen loop ticks Sim, Stage.Present and the officials. |
| E-297 | S3 | The Throw action is one 0.55 s override from wind-up start, with another 0.5 s reset at release, so the anim cannot tell cock, release and follow-through apart | PARTIAL | QbReleaseEta hook exposed; the Throw action itself is unchanged (QBSTANCE owns the throw flow). |
| E-298 | S3 | Snap ball spawns at the center's chest, not between his legs | FIXED | Snap ball starts between the center's legs (0.25 yd behind, 0.30 yd high). |
| E-299 | S3 | Touchback in the end zone is an instant EndPlay (no kneel) | FIXED | The returner kneels before a touchback ends the play. |
| E-308 | S2 | Kick returners and punt returners get no catch reach, fair catch or catch-ready | FIXED | Returners reach ahead of the kick (FlowAction) with the return-catch / fair-catch takes. |
| E-313 | S3 | Holder loops a 0.25 s slice and assumes the snap arrives 0.45 s after the snap event | PARTIAL | The holder holds the first frame of the stance take (was a looping 0.25 s slice). The take still starts at 0.12 s, not from the real snap ETA. |
| E-316 | S3 | Blocked kick has no block attempt visuals and uses one fixed rule | FIXED | Every rusher within 1.6 yd of the kick leaps. |
| E-348 | S3 | ContactImpact AgentA/AgentB mean different things per contact kind | FIXED | ContactImpact has Hitter / Victim with HasRoles; the replay focuses on the victim of a big hit. |
| E-349 | S3 | Closing speed is passed in yd/s but the table is in m/s | FIXED | Closing speed is converted from yd/s to m/s. |
| E-362 | S2 | Contact takes ignore GamePace.AnimationRate (0.88) and any future slow-down | FIXED | Contact takes play at GamePace.AnimationRate; the session clock follows the playing clip. |
| E-363 | S2 | Throw / catch / hand-off clips are not on the game-pace rate that GamePace says they should be | FIXED | Catch, hand-off, juke, spin, stiff arm, truck, stumble and engaged plans are paced; throw stays on its release frame. |
| E-372 | S2 | Catch animation starts after the ball is already caught (no lead-in for kicks) | FIXED | Returners' catch starts 0.5 s ahead from the predicted fall time. |
| E-373 | S2 | Kick approach path length does not match the clip's root travel (foot sliding) | WONTFIX | Kick approach path versus clip root travel belongs to the kicker owner (QBSTANCE): the sim path and the clip choice change together. |
| E-375 | S3 | Catch reach starts at ETA <= 0.34 s with a 0.22 s fade: hands not formed for short ETAs | FIXED | Catch reach window 0.5 s, fade floor 0.08 s, IK holds the hands on the ball. |
| E-376 | S3 | Requested 0.07 s contact fade-in is silently raised to 0.22 s | FIXED | ActionAnimator.Play takes a fade floor; contact takes ask for 0.08 s with a 0.14 s fade over the 0.36 s lead. |
| E-377 | S3 | Session clock and animation clock can drift | FIXED | The session clock is the playing clip's clock. |
| E-378 | S3 | Impact events (shake, rumble, hit sound) fire at session start, not at the hit | FIXED | Impacts fire at the contact moment of the session. |
| E-379 | S3 | The referee whistle is used as the audible/flip/switch sound | FIXED | MatchAudio.CallCue for audible / flip / switch (sub-agent). |
| E-380 | S3 | Post-play shot plan can run longer than the wind-down and delays the next play | FIXED | Post-play shot budget trimmed to the wind (sub-agent). |
| E-381 | S3 | A touchdown costs about 20 s of non-play time | PARTIAL | Replay B shortened and capped (about 2 s saved per touchdown); the replay is not made skippable for autopilot. |
| E-383 | S3 | The whistle plays twice on penalty plays | FIXED | One whistle owner per penalty play (sub-agent). |
| E-386 | S3 | Swat/jump timing: the Jump clip also starts at the moment of deflection (PlayAction(d, Jump, 0.4)), after the contact decision; TryJump only fires when 0.34 s from the point | PARTIAL | The best defender's swat / jump starts ahead of the contact point (view); the sim's own Jump at the deflection is unchanged. |
| E-387 | S3 | Kickoff tee ball height differs between view and launch point (visible pop) | FIXED | One tee height constant for the view and the launch point. |
| E-388 | S3 | FG and punt contact moments are guessed when the timing table has no entry | PARTIAL | AnimKit logs a warning once for a kick clip without a measured contact moment; the table entries still need ClipTimingBuilder. |
| E-390 | S1 | The pocket camera loses the ball on deep passes, punts and kicks | FIXED | Camera rises, pulls back and aims at the ball while it is airborne (sub-agent). Not done: the lower-third QB framing. |
| E-391 | S2 | Camera lurches to the carrier framing at every snap | FIXED | Snap state focuses the QB (sub-agent). |
| E-392 | S2 | After a turnover the camera stays behind the old offense and the returner runs at it | FIXED | View direction follows the returner after a turnover (sub-agent). |
| E-393 | S2 | Lateral clamp keeps sideline plays near the screen edge or out of it | FIXED | Lateral clamp 20 m, FOV widens with lateral offset (sub-agent). |
| E-394 | S2 | Post-play tight shots are rigidly locked to the moving player | FIXED | Tight shots smooth the subject and facing (sub-agent). |
| E-396 | S3 | Camera can sit inside the goalposts/stadium behind the end zone (verify in play) | FIXED | Camera z clamped 4 m behind the end line, raised near it, off the uprights (sub-agent). |
| E-397 | S3 | Default vertical FOV 46 is a wide lens; the reference asks for a long lens | WONTFIX | A long lens conflicts with StanceCameraTests and needs a play-test and a design call (sub-agent). |
| E-398 | S3 | Broadcast camera stacks three smoothers and clamps before the end zones | FIXED | One smoother removed, clamp 52 m (sub-agent). |
| E-399 | S3 | Broadcast camera swaps sidelines on every possession change | WONTFIX | The side swap is the deliberate left-to-right broadcast convention (sub-agent). |
| E-400 | S3 | Pressing View blends the camera through the field | FIXED | Cut blends between broadcast and user cameras (sub-agent). |
| E-401 | S3 | First-person camera lags the body and can disorient | FIXED | First-person yaw driven from the body heading with a head-turn limit (sub-agent). |
| E-402 | S3 | First-person anchor reads the head before the ground fit and IK run (verify in play) | WONTFIX | Execution order -200 is needed for the Cinemachine brain; verify in play (sub-agent). |

## Before / after (same plays, same seeds; "before" = the unchanged v0.8.0 tree)

Data: `D:\PIXFootball\Renders\connect_audit\connect_audit_before.txt` / `_after.txt`, `connect_engine_before\` / `connect_engine_after\` (EngineAudit, 48 plays), strips in `connect_audit\strips_before|after\`.

### ConnectAudit (24 plays + special scenarios)

| Metric | Before | After |
|---|---|---|
| Rendered smoothness at a 144 Hz display: runner frames with no movement | 58 % | 0 % |
| Same: per-frame displacement coefficient of variation (0 = even) | 1.21 | 0.16 |
| Pre-snap flip: largest single-frame displacement of a player | 32.0 m (6 player-frames over 0.45 m) | 0.087 m (0) |
| Pre-snap defense shift: largest single-frame displacement | 1.8 m (5 player-frames over 0.45 m) | 0.067 m (0) |
| Huddle break (new): largest single-frame displacement | n/a | 0.087 m (a 5.4 m/s jog), no jump |
| Frames where a body still in its stance stands in the locomotion idle in the first 0.5 s after the snap | 38 | 0 |
| View teleports during plays (frame displacement the simulation cannot explain), live / after the whistle | 17 / 11 | 10 / 1 |
| Sprinting carriers (over 4.5 m/s) that stop dead within 0.1 s of the tackle event | 6 of 19 | 0 of 15 |
| Carrier view speed 0.1 s after the tackle event / at the event (fast carriers) | 0.31 | 0.64 |
| Hand-off: largest change of the displayed ball's displacement in one frame | 1.06 m (the ball popped hand to hand) | 0.27 m |
| Hand-off: ball to the receiving hand at the simulated exchange | 0.04 m (after the pop) | 0.32 m (the transfer is centred on the exchange; it reaches the hand 0.12 s later) |
| Catch: worst ball-to-hand distance 0.05 to 0.4 s after the catch | 0.176 m | 0.140 m |
| Catch: ball to nearest hand at the simulated catch frame | 0.64 m | 0.58 m (NOT fixed) |
| Catch: largest per-frame change of the ball's displacement | 0.29 m | 0.55 m (worse: the ball is pulled into the hands) |

### EngineAudit (48 plays, bones sampled at 60 Hz)

| Metric (frames over 48 plays) | Before | After |
|---|---|---|
| Hips rotating over 900 deg/s, all groups | 3,715 (OL 2,139, DL 604) | 873 (OL 232, DL 318) |
| Chest rotating over 1,400 deg/s, all groups | 2,277 (OL 1,567) | 327 (OL 79) |
| Pelvis jumping over 10 cm in one frame (view root frame), all groups | 491 (OL 211, DL 137) | 417 (OL 152, DL 182; QB 25 and part of DL are the stance pose layer of another track, applied after the pop filter) |
| Clip changes per second, mean (OL / DL / QB / WR / CB) | 1.19 / 1.25 / 1.03 / 0.36 / 0.59 | 1.47 / 1.55 / 0.77 / 0.78 / 0.90 |
| Clip changes under 0.25 s apart, per play p95 (OL / DL) | 3 / 5 | 2 / 2 |

**Not met:** the target of under 0.5 clip changes per second and of hip snaps near zero. The rate went UP for receivers, corners and linemen because the connection layer plays clips where there were none (a get-off at the snap and its blend into locomotion are two changes per play per player, the run-block footwork and the matched engagement takes are new clips); the rapid flips (under 0.25 s apart) are down. The residual hip snaps are concentrated in the first frames of the get-off clips (`DlineTransition3ptRToRunFwd0deg`, 96 frames): they come from the root yaw slewing at 720 deg/s together with the clip facing offset, which the pelvis filter (it works relative to the view root) does not see. The ball-at-hand number at the simulated catch frame is not fixed: the simulation declares the catch about 0.6 to 1 m from the body.

## Strips (looked at once)

`D:\PIXFootball\Renders\connect_audit\strips_after\`: `tackle.png` (carrier after a hand-off, brought down), `handoff.png`, `catch_and_run.png`, `ol_dl_engagement.png` (the second row shows the matched pair: both linemen play the same take, then the escape take on the shed). The motion-man and huddle-break scenarios are covered by the pre-snap numbers above (no strip: the audit camera follows one player); they are not visually checked in a strip.

## Open items (for the lead)

1. Catch-frame hand distance and per-frame ball displacement (E-125): needs the simulation to complete the catch nearer to the body, or a catch frame earlier than the arrival.
2. Clip-change rate target (E-259) and residual hip snaps (E-250): see above; next step is to let the pelvis filter see the root yaw (LOCO owns the facing loop) and to blend the get-off into the gait cycle without a clip stop.
3. Contested Failure / CritFailure pair takes (E-130) and shrugged big hits (E-219) need `ContactClipBuilder` to include `catch_fail` and a sim signal; the mirror of contact takes (E-218) needs mirrored pair data.
4. Sim-side rows left for SIM: hip-flip deceleration (E-277), pancake fall delay (E-279), shed velocity kick (E-280), engage punch window (E-281).
5. Kick approach (E-373) for the kicker owner; QB flow rows consume the new hooks (`QbSetTime`, `QbDropProgress01`, `QbReleaseEta`, `MeshPoint`).
6. Coordination: two files of other tracks broke the shared tree while I verified (`WeatherSystem.cs` line 317, `SimAgentView.Stance.cs` `Gait` duplicate, `QbStanceProbe.cs` syntax); I patched or removed them only in my private copy `D:\PIXFootball\Work\Connect\PIX` (see `patchcopy.ps1`), never in the shared tree.
7. Not play-tested with a controller: the huddle break gate in `MatchDirector`, the call-screen tick and the post-whistle walk run only through the audit's PlayStage path; the first thing to check in a build is that the snap button works after the break (about 2 s) and that the play clock still behaves.

## EditMode run (one full run at the end, private copy, `-testCategory "!Slow"`)

1,151 tests: 1,120 passed, 31 failed (`D:\PIXFootball\Work\Connect\logs\editmode_results.xml`). All CONNECT tests pass (`ConnectTests`, the updated `ContactTests`). The 31 failures are all in simulation-calibration, stance / formation and UI layout tests (`AiTests`, `BallAndKickTests`, `CatchTests`, `SimCalibrationTests`, `PaceAndPlayEndTests`, `EngineRealismTests`, `PreSnapTests`, `StanceCameraTests` formation / line-up, `UiFlowPlayCallModelTests` chip overlap and so on): they exercise code that the SIM and QBSTANCE tracks were rewriting while the run was taken (`GamePace`, `AgentBody`, `OffenseAI.Qb`, formations); none touches a CONNECT-owned file. The one sim change of this track (hand-off distance in `OffenseAI.Qb.cs`) was retuned by SIM to 1.3 yd after outside runs missed the exchange; `PlayStage.Flow` follows that value.

Files touched by this track (shared tree): `ActionAnimator`, `AnimKit` (+ new `AnimKit.Connect.cs`, edits in `.Extra`), `SimAgentView` (+ new `SimAgentView.Connect.cs`; edits in `.Flow`), `LocomotionAnimator` (`ResetTrigger`), `StanceClips` (holder first frame), `PlayStage` (+ new `PlayStage.Connect.cs`, `PlayStage.Flow`), `BallFollower`, `MatchDirector` (+ `.PreSnap` three lines by the sub-agent), `CollisionFeel`, `Gameplay/Contact/*` (director, session, variants, ragdoll rig / state), `Sim/PlaySimulation.cs` (step callback), new `Sim/PlaySimulation.Connect.cs`, `Sim/KickGeometry.cs`, `Sim/OffenseAI.Hooks.cs`, `OffenseAI.Qb.cs`, `SpecialTeamsAI.cs`, `Editor/Tools/ConnectAudit.cs`, `Tests/EditMode/Gameplay/ConnectTests.cs`, `ContactTests.cs`, `Docs/AnimationCoverage.md`, `BroadcastDirector.cs` (victim focus), the camera / director files listed above.
