# v0.9.0 engine audit: status of all 448 rows

Release status for `ENGINE_AUDIT_issues.csv` (IDs E-001..E-448), taken from the five track logs (`locomotion.md`, `qbstance.md`, `connect.md`, `sim.md`, `gfx.md`) and checked against the final shared tree. Metrics below are from ONE EngineAudit rerun on the release tree (`-pixEngineAudit -pixEngineAuditPhase metrics`, 36 real plays, 60 Hz bones, `D:/PIXFootball/Renders/engine_audit_v090_final`; "before" = `D:/PIXFootball/Renders/engine_audit` on the v0.8.0 tree) and from the track logs where the audit tool does not measure the item.

Status key: **fixed** (changed and measured or looked at), **partial** (part done, remainder in the note), **handed-over** (belongs to a later release or another owner and was not done here), **wontfix** (reason in the track log), **open** (works, not done). The sim's "DONE+H" (fixed in the sim, the view layer must adopt a hook) counts as fixed and is marked "(view hook)".

## Totals

By track:

| track | fixed | partial | handed-over | wontfix | open | total |
|---|---|---|---|---|---|---|
| LOCO | 7 | 14 | 11 | 0 | 5 | 37 |
| QBSTANCE | 59 | 6 | 0 | 0 | 0 | 65 |
| CONNECT | 105 | 24 | 0 | 8 | 0 | 137 |
| SIM | 148 | 8 | 0 | 2 | 0 | 158 |
| GFX | 42 | 2 | 3 | 4 | 0 | 51 |
| **all** | 361 | 54 | 14 | 14 | 5 | 448 |

By severity:

| sev | fixed | partial | handed-over | wontfix | open | total |
|---|---|---|---|---|---|---|
| S1 | 61 | 10 | 0 | 0 | 3 | 74 |
| S2 | 197 | 27 | 7 | 7 | 1 | 239 |
| S3 | 103 | 17 | 7 | 7 | 1 | 135 |
| **all** | 361 | 54 | 14 | 14 | 5 | 448 |

Notes on the non-fixed rows: the 14 handed-over rows are E-020, E-024, E-025, E-026, E-027, E-033, E-040, E-041, E-042, E-282, E-296 (LOCO handed them to SIM; the SIM log has no row for them, so they are not claimed as fixed; SIM's pace and body work touches several of them) and E-420, E-421, E-422 (GFX: per-frame animation work in other tracks' files). E-005 is partial: SIM retuned the cut grip, braking and turn rate (code tagged E-005) but the 90 degree cut was not re-measured. The 5 open rows are E-010, E-011, E-012, E-022 and E-034 (no stop / plant-and-cut / turn-in-place clips, idle child speed, per-role hip height).

## Headline metrics, before and after

| Metric | v0.8.0 (before) | v0.9.0 (after) | Target / note |
|---|---|---|---|
| Planted-foot slide, CB / LB / S sprint (contact min slide, mean m/s) | 2.12 / 2.22 / 1.93 | 0.23 / 0.22 / 0.23 | < 0.3, met |
| Planted-foot slide, CB / LB / S run | 0.80 / 0.58 / 0.65 | 0.37 / 0.28 / 0.28 | < 0.3, met for LB and S, CB close; RB carry 0.83 is the weakest cell |
| Sprint cadence 7.5 m/s (CB), steps/s, stride m | 1.88, 4.18 m | 4.74, 1.61 m | 4.3-4.7 steps/s, 2.0-2.4 m: cadence met, stride short (more steps) |
| Run cadence 4.5 m/s (CB), steps/s, stride m | 2.56, 2.05 m | 3.26, 1.62 m | 3.3 steps/s, 1.2-1.6 m: met |
| Flight fraction (both feet off the turf), CB sprint 7.5 / 6.0, CB run 4.5 | 0.71 / - / 0.19 | 0.36 (2.5 s sample) / 0.57 / 0.45 | <= 0.50; sprint came down, run went UP because the old tree glided with hovering feet and no real contact (take property, LOCO-009) |
| Float / hover: lowest toe above the turf, mean cm, CB / LB / S sprint | 13.2 / 10.8 / 12.5 | 11.4 / 11.4 / 11.2 | sprint takes still bound; not fixed by IK (both feet are in flight) |
| Float / hover: kicker feet above the turf, hover frames (field goal / kickoff) | 41 / 14 | 0 / 0 | fixed |
| Frames with both feet more than 10 cm up, 36 plays | 10,219 | 14,432 | counts more frames: the new cycles have real flight phases where the old tree hovered; per-cycle fractions above are the better measure |
| Knee deviation in stance, cm mean (real plays, sprint / run >= 5.5 m/s) | 1.9 / 4.5 | 1.6 / 1.6 | < 3, met (v0.8 treadmill jog 7.5, run 4.5 4.8) |
| Knee bend plane off the line, degrees mean (sprint / run >= 5.5) | 10.3 / 21.4 | 8.2 / 8.7 | about 9 by design, met |
| Foot toe-out at contact, degrees mean (sprint / run >= 5.5 / run 3-5.5) | 30.3 / 35.5 / 39.8 | 15.7 / 12.4 / 11.7 | within 10 degrees; met on the treadmill (3.2 / 2.1 / 7.5), real plays remain above because of cuts and turns (p95 39-46) |
| Ankle roll in stance, degrees mean (sprint >= 5.5) | 21.9 | 2.1 | within 8, met (jitter 4-8 -> 0.9 degrees per frame) |
| Hips rotation pops over 900 deg/s / chest pops over 1400 deg/s, frames | 3,723 / 2,271 | 712 / 321 | near zero: not met, 5x lower |
| Clip changes per second (OL / DL / WR / CB) | 1.19 / 1.25 / 0.36 / 0.59 | 1.38 / 1.45 / 0.74 / 0.83 | < 0.5, NOT met (the connection layer plays more clips; rapid flips are down) |
| Kicker pre-snap offset (kickoff / field goal / punt), m, negative = left | +1.65 / +1.65 / 0.00 | -3.29 / -1.65 / -0.41 | left of the ball, met; punt foot LEFT -> RIGHT |
| QB drop-back flight frames | 51-63 % of the drop | 0 of 556 | met |
| OL / DL lowest hand above the turf, m | 0.54 / 0.40 | 0.10 / 0.15 | hands down 0 -> 100 % |
| Snap to dead, run / pass, mean s | 3.28 / 3.98 | 4.19 / 4.83 | in-game target 4.3-4.8 / 5.0-5.6 incl. the director tail (about 4.8 / 5.8) |
| Snap to QB release, SG3 / UC3 / UC5 / UC7 median s | 1.25 / 1.47 / 2.07 / 2.50 | 1.90 / 2.02 / 2.68 / 3.20 | +15-25 % asked |
| Sack, snap to sack median s | 1.95 | 2.38 | 2.4-2.6 target, met |
| Sack rate, sacks per team per game / share of dropbacks | 1.3-3.8 band, under-centre 13 % (bug) | 1.31 / 3.3 % | NFL about 6.5 %: still low (open item in sim.md) |
| Punt average, yards | not recorded | 49.1 | NFL about 47 |
| Box score, 40 CPU games (points / plays / rush yd / net pass yd per team) | - | 26.2 / 59.9 / 135 / 211 | football-like |
| Player FPS, built player 2560x1440 High, 80 s live play, avg / 1 % low (RTX 5090 laptop) | 109 / 69 (v0.8.0 build, 2364x1330, exhibition match) | 73.3 / 51.5 at LOD 300 / 170 px, then 75.3 / 52.4 after the fallback LOD 400 / 200 px | target 1 % low >= 60: NOT met; editor GFX measurement was 95 avg / 51 1 % low |

## All 448 rows

| ID | Sev | Track | Status | Issue (shortened) | Note from the track log |
|---|---|---|---|---|---|
| E-001 | S1 | LOCO | partial | Sprint cycles are a bounding gait: the animated body is airborne 60-75% of th... | cadence 4.3-4.9 steps/s, flight 0.72 -> 0.42-0.53, stride 1.6-1.7 m; needs grounded spr... |
| E-002 | S1 | LOCO | partial | Run and jog cycles have a too-low cadence and a too-long stride at 4.5 m/s (g... | free-arm jog 2.6 m/s; 3-4.5 m/s band slide 0.4-1.0 m/s |
| E-003 | S1 | LOCO | fixed | Planted feet slide on every ground contact at run speeds (skating): minimum t... | 0.17-0.55 m/s mean contact slide at 4.5-6 m/s (was 1.0-1.7); inner stance 0.1-0.8 on th... |
| E-004 | S1 | LOCO | partial | No start-from-standstill clip in the shared tree | starts are now steps (cadence follows speed from the walk cycle, plant on every contact... |
| E-005 | S1 | LOCO | partial | 90 degree cut at sprint takes 0.88 s, loses 53 percent of speed and overshoot... | SIM retuned AgentBody lateral grip/braking/turn rate (code tagged E-005); 90-degree cut... |
| E-006 | S1 | LOCO | fixed | No interpolation between fixed 60 Hz sim steps and the render frame | render interpolation between the last two 60 Hz states (StepAlpha); runner frames with ... |
| E-007 | S1 | LOCO | fixed | Sprinting defenders slide backwards at about 2 m/s per contact (CB / LB / S S... | CB / LB sprint 6.0-7.5: 2.0-2.9 -> 0.16-0.58 |
| E-008 | S1 | LOCO | partial | Defensive back-pedal cadence is half of a real back-pedal and the stride is 2... | back-pedal ray, slide 0.09-0.65; cadence still 1.4-2.0 steps/s |
| E-009 | S1 | LOCO | fixed | Foot lock never engages where the skating is | FootPlant plants by contact (gait phase + height), no 0.65 m/s gate; old lock only for ... |
| E-010 | S1 | LOCO | open | No stop clip: runners skate into idle after every play | needs a stop take chosen by the deceleration (*_To_Stand_Relaxed takes are measured in ... |
| E-011 | S1 | LOCO | open | No plant-and-cut: cuts are a root yaw swing plus 17 deg bank | Sprint*_90_Deg_Turn_* takes measured, not wired |
| E-012 | S1 | LOCO | open | Turn in place has no feet: avatar root is rotated at 720 deg/s |  |
| E-013 | S1 | LOCO | partial | Back-pedal uses a 5 m/s CB shuffle: slides at 2-3.5 m/s | see E-008 |
| E-014 | S1 | LOCO | fixed | Stride cadence not matched at 4.5-6.5 m/s and closed-loop trim switched off | library of measured speeds + stance feedback + learned factors (LOCO-006) |
| E-015 | S1 | SIM | fixed | Defensive linemen are not slowed at all (pace 1.0): a rated-80 DE tops out at... | GamePace.For splits the line: LE / RE 0.91, DT 0.835 (were 1.0); 92 LE 40 yd 4.85 -> 5.... |
| E-016 | S1 | SIM | fixed | Offensive line pace 0.93 leaves a 315 lb OL at 18.0 mph, 1.1 mph under a WR; ... | OL pace 0.805 (was 0.93); 15.6-15.9 mph, 40 yd 6.3-6.9 s; pulls ramp up (E-184) |
| E-017 | S2 | LOCO | fixed | Route runners and ball carriers skate too: WR/TE/RB route contacts and carrie... | RB route 4.5 / 6.0: 1.23 / 1.72 -> 0.17 / 0.21; carry 1.0 -> 0.48 |
| E-018 | S2 | LOCO | partial | Lateral shuffle and back-pedal slide at speed and the sim labels sprinting si... | shuffle stays on the tree; planted + action ground pass |
| E-019 | S2 | SIM | fixed | Heading flips: velocity direction rotates up to 3,476 deg/s in one tick and 3... | 540 deg/s heading limiter on the velocity direction at every speed over 2.5 yd/s, dives... |
| E-020 | S2 | LOCO | handed-over | Body physics lab: a sprinter needs about 1.0 s to turn 90 degrees, 1.05-1.3 s... | SIM |
| E-021 | S2 | LOCO | partial | One locomotion tree for every body and role | cycle speeds scale with the body (leg length x measured size scale, learned per size); ... |
| E-022 | S2 | LOCO | open | Idle child of the blend tree plays 2.7x or faster when standing still | controller builder untouched |
| E-023 | S2 | LOCO | partial | Facing leads velocity by up to about 60 degrees mid-cut, so the body points s... | the gait yaw bias aligns the cycle's travel with the velocity; the sim facing is SIM |
| E-024 | S2 | LOCO | handed-over | A tackled player slides 2.9 yd from sprint speed (decel 14 yd/s squared), con... | SIM |
| E-025 | S2 | LOCO | handed-over | Pads factor and game pace stack: top speed is 84 percent of the combine table... | SIM |
| E-026 | S2 | LOCO | handed-over | QBs are slow for a position that must scramble: QB 75 runs 18.1 mph / 5.60 s ... | SIM |
| E-027 | S2 | LOCO | handed-over | Backpedal and lateral shuffle caps are too fast: backpedal 4.3-5.2 yd/s (9-10... | SIM |
| E-028 | S2 | SIM | partial | Top-speed range is compressed: calibrating each rating against "halfway to el... | mass-scaled wind-up (+0..10% tau) and the per-position pace give DT / OL / skill real s... |
| E-029 | S2 | SIM | fixed | Acceleration cap is too strong: 8.5 + 4 x accel01 reaches 12.5 yd/s2 (11.4 m/... | BodyAccelCapFor: 7.5 + 3 x accel (max 10.5 yd/s2, was 12.5); the 40-yard reference tabl... |
| E-030 | S2 | SIM | fixed | Braking and traction are above a human limit (DecelBase up to 13.5 yd/s2 = 1.... | DecelBase 7.8 + 3.5 x agility (was 9 + 4.5); traction limit 1.15 x accel + 2.5 + 2 x ag... |
| E-031 | S2 | SIM | fixed | Turn rate is 540 deg/s standing and about 270 deg/s at full speed: hips snap ... | TurnRate 4.2 + 3.2 x agility (was 5 + 4.5), 55% less at full speed (was 45%) |
| E-032 | S2 | SIM | fixed | Lateral shuffle is 0.60-0.71 of top speed: a CB shuffles 6.2-6.7 yd/s (about ... | ShuffleFactor 0.47 + 0.10 x agility (was 0.60 + 0.11); backpedal 0.44 + 0.09 |
| E-033 | S2 | LOCO | handed-over | The man in motion keeps his body facing upfield while sliding sideways at 3.4... | SIM sets facing; with the gait he would now walk the right way if the sim faced him alo... |
| E-034 | S3 | LOCO | open | Hips height while running is 1.07-1.12 m for everybody (no low centre of grav... | the pelvis compensation lowers the hips up to 7 cm while a leg is stretched, but no per... |
| E-035 | S3 | LOCO | partial | Planted-foot toe-out of the lateral shuffles is only clamped, not fixed | running gaits and back-pedal within 10 deg of the line; shuffles keep the old clamp |
| E-036 | S3 | LOCO | partial | Lean, foot lock and toe clamp turn OFF during every masked (upper-body) clip | action ground pass respects clip weight |
| E-037 | S3 | LOCO | partial | Facing loop chases arm-swing twist; yaw wobbles at stride rate | the bias removes the cycle's own offset; loop unchanged |
| E-038 | S3 | LOCO | partial | Hip/torso lean too small, braking lean almost absent | braking lean clamp -9 -> -11.7 deg, arm swing with speed |
| E-039 | S3 | LOCO | partial | FitGround only runs for Stance/Idle/Block; none while moving, and BodyMotion ... | FootPlant uses the view's ground height (GroundY), BodyMotion._gy and FitGround unchanged |
| E-040 | S3 | LOCO | handed-over | Peak acceleration cap 12.5 yd/s squared (1.16 g) for acceleration 99 exceeds ... | SIM |
| E-041 | S3 | LOCO | handed-over | Direction change from a near-standstill is instant: latCap is raised to the f... | SIM |
| E-042 | S3 | LOCO | handed-over | Fatigue is cosmetic: 12 percent maximum speed loss at zero energy and about 1... | SIM |
| E-043 | S3 | SIM | fixed | Overspeed allowances let bodies flash above their rated top: hard cap 1.05x, ... | OverspeedCap 1.02 (was 1.05); impulse cap 1.04 for skill, 1.0 for men over 105 kg |
| E-044 | S3 | SIM | fixed | Collision resolution is a single pass over pairs per tick, so a six-body pile... | three relaxation passes per tick (later passes push only) |
| E-045 | S1 | QBSTANCE | fixed | Linemen in the 3-point stance have no hand on the ground (the down hand is 0.... | Down-hand IK onto the turf (StanceIK, HandMode.Ground, deepening trunk then sinking hip... |
| E-046 | S1 | QBSTANCE | partial | OL 3-point stance is nearly upright: hips below the head, trunk 20-23 degrees... | Hips-level-with-shoulders, flat back, weight on the balls of the feet and head up are i... |
| E-047 | S1 | QBSTANCE | fixed | Quarterback under centre stands almost straight: trunk 10 degrees, hips 0.98 ... | QB under centre trunk 34, hips 0.81, feet pigeon-toed (-7 deg toe-out), hands 0.80 m at... |
| E-048 | S2 | QBSTANCE | fixed | DL 3-point stance has the hips LOWER than the head and the 4-point is a half ... | Hips above the shoulders (trunk 124 / 126), fingertips loaded: 3-point one hand, 4-poin... |
| E-049 | S2 | QBSTANCE | fixed | Shotgun QB stance is an upright idle: trunk 3 degrees, knee flexion 26 degrees | Shotgun hips 1.06 -> 0.92, trunk 3 -> 16, hands at the chest |
| E-050 | S2 | QBSTANCE | fixed | Receiver split stance is only 14 cm staggered and 11 degrees forward (spec: o... | Stagger 0.41 / 0.34 m (>= 0.30), back heel +7 cm, outside foot back per side (mirror), ... |
| E-051 | S2 | QBSTANCE | fixed | Press corner and off corner stances are the same pose; neither is a press sta... | Press: square (stagger 0.04), hands up at 1.12 m; off: in the chair (hips 0.81 vs 0.87)... |
| E-052 | S2 | QBSTANCE | fixed | Kicker and punter stand in a generic idle before the kick (no stance of their... | Kicker / punter / long snapper stances (Kicker, Punter, LongSnapper): relaxed upright, ... |
| E-053 | S2 | QBSTANCE | fixed | OL always 3-point: no 2-point set for shotgun and pass-heavy sets | OlTwoPoint when the QB is in shotgun/pistol and the play is a pass (StanceContext.PassP... |
| E-054 | S2 | QBSTANCE | fixed | Under-centre stance: no cadence, hip drop or head swivel | Cadence: head swivel + bounce + hips drop 0.15 m on the go (PlayStage.Stance.cs), windo... |
| E-055 | S2 | QBSTANCE | partial | No head/eye control anywhere (WR snap look, QB eyes downfield, DB eyes on QB,... | Head look layer (stance head yaw toward the ball for receivers / corners, QB eyes on th... |
| E-056 | S2 | QBSTANCE | fixed | Stances ignore the foot-back/hand side for everything except line and receivers | Outside foot back for CB / S / LB / WR via the pose mirror (StancePoses.For); RBs stay ... |
| E-057 | S2 | QBSTANCE | fixed | FB and goal-line backs use the 2-point halfback clip when the code says "3-po... | FB / goal-line back: real one-hand-down pose (ThreePoint with the RB role) |
| E-058 | S2 | SIM | fixed | Two stance classifiers that disagree; the sim's one is never used | (view hook) PreSnapStances.Refresh runs after every pre-snap change and sets SimAgent.S... |
| E-059 | S2 | QBSTANCE | fixed | Stance/idle loops: a 20% slice of long takes and un-seamed loops | Stance poses are solved, not looped: no 20% slice or loop seam; breathing is a sine; re... |
| E-060 | S2 | QBSTANCE | fixed | No hand-on-turf IK and no stance tells (run vs pass weight) for linemen | Hand IK + stance tells (Tell: run = weight on the hand, pass = sits back, light hand) |
| E-061 | S2 | SIM | fixed | Technique alignment (1-tech nose, 3-tech DT, 5/7-tech ends) is implemented bu... | DefenseAlignment.UseTechniques = true; recalibrated: OL y = -0.15 (no overlap at the sn... |
| E-062 | S2 | QBSTANCE | fixed | Holder clip has one variant and no side choice | Holder take mirrored so the placing hand is on the kicker's (left) side (AnimKit.Holder... |
| E-063 | S2 | QBSTANCE | fixed | Long snapper uses the line-center stance | StanceKind.LongSnapper (wide base, head down, hips high, hands on the ball) |
| E-064 | S3 | QBSTANCE | fixed | Safeties, kickoff runners and returners share one upright 2-point pose (trunk... | KickoffRunner (staggered, toes) / Returner (hands out) poses, no longer the shared safe... |
| E-065 | S3 | QBSTANCE | fixed | Wide-9 and edge stances are upright with no body tilt; no DE 4-point at all | Edge / contain / 7-9 technique tilts 14-22 degrees toward the quarterback (StanceSpec.T... |
| E-066 | S3 | QBSTANCE | fixed | Kicker, returner, kickoff teams: shared generic stances | Kicking line staggered, receiving line KickoffReceive (hands up), returner hands out |
| E-067 | S3 | QBSTANCE | fixed | LB, safeties and corners get coarse stances (CB press chosen by depth <= 3.2,... | Corner press from CoverageAssignment.Press (StanceContext), LinebackerBlitz for edge ba... |
| E-068 | S3 | QBSTANCE | fixed | All ten kickoff coverage men stand in the linebacker blitz crouch | Same as E-066 (no blitz crouch for ten kickoff coverage men) |
| E-069 | S1 | QBSTANCE | fixed | Quarterback hops during the drop: a back-pedal has a 51-63% flight phase | Drop on the pose layer + ProcGait: 0 of 556 drop frames with both toes off the turf (wa... |
| E-070 | S1 | QBSTANCE | fixed | QB drops back as a downfield-facing backpedal, not a turned-away reverse-pivo... | QbDropFacing: reverse pivot (0.24 s) under centre, shotgun keeps facing the line; test ... |
| E-071 | S1 | QBSTANCE | fixed | Snap is never received: no snap-catch animation at all | Snap received: hands go to the centre's rear and follow the ball in flight (StanceDyn.A... |
| E-072 | S1 | SIM | fixed | QB drop and release timeline is 15-25% short of real NFL timing | drop is time-parametrised, release floors per drop type; releases +27-52% (table above) |
| E-073 | S1 | QBSTANCE | fixed | Drop-back leaves the scrubbed clip 0.9 m early and glides the last metres | No clip scrub: the pose layer follows the simulated position, plant is a pose state; th... |
| E-074 | S1 | QBSTANCE | fixed | Pocket loop is stationary but the sim moves the QB (slide, climb, step-up) | Pocket slide / step-up are stepped by the planner in the velocity direction (ProcGait),... |
| E-075 | S1 | SIM | fixed | Drop and set times 15-20 percent short; no relation to the 3/5/7 step count | set times tied to 3 / 5 / 7 steps: 1.2 / 1.75 / 2.3 s under centre; the drop arcs 1.25 ... |
| E-076 | S2 | QBSTANCE | fixed | Shotgun 3-step drops are not drops: the QB receives the snap and is already S... | Shotgun drops: the view steps the retreat; the gather distance itself is the SIM track'... |
| E-077 | S2 | QBSTANCE | fixed | The quarterback pocket is the generic locomotion idle in about 90% of frames | Dedicated pocket pose (QbPocket: base wider than the shoulders, knee bend, ball high at... |
| E-078 | S2 | QBSTANCE | fixed | Drop pivot is cancelled by the facing loop and the sim never turns the QB | The sim turns the body (pivot) and the pose layer sets the hips yaw absolutely; QB body... |
| E-079 | S2 | QBSTANCE | fixed | "Catch the snap" part of the drop plays inside the first 6% of the distance | No compression: the catch / turn part is the stance -> pivot blend (0.2 s), not 6% of t... |
| E-080 | S2 | QBSTANCE | partial | Roll-out/scramble: Carry tree with a tucked ball plus ear-carriage IK fight e... | A passer on the move (drop, set, scramble, wind-up) no longer enters the tucked-ball Ca... |
| E-081 | S2 | QBSTANCE | partial | Pump fake is cosmetic, unsynced to the sim, and plays full-body | Pump plays in the pocket only (PocketLife) and the pose layer holds legs / trunk so the... |
| E-082 | S2 | QBSTANCE | fixed | Fake handoff (play-action) plays a solo hand-off take | QbPhase.Fake on the pose layer: the ball is carried out toward the runner and back (Che... |
| E-083 | S2 | SIM | partial | No QB slide, sneak, dive, hail mary, falling throw, hesitation; sim has no Sl... | AgentAction.Slide / CarrierMove.Slide (user Tackle button, AI quarterback slides before... |
| E-084 | S2 | QBSTANCE | fixed | Kneel and spike: unsynced take windowed [0, 4 s], held | The mocap kneel takes never leave standing (probe: hips 1.05 m throughout), so kneel an... |
| E-085 | S2 | QBSTANCE | fixed | QB cadence never plays and the snap is instant on the button | CadenceWindow before PrepareSnap: 0.6-1.0 s of cadence for the user's snap and the CPU's |
| E-086 | S2 | QBSTANCE | fixed | No plant or hitch at the top of the drop: set fires 0.55 yd early at about 3.... | Brake to 0.8 yd/s over the last yard, set at < 0.30 yd (was 0.55), pose layer plants; c... |
| E-087 | S2 | SIM | fixed | Shotgun QB is "set" the tick he catches the snap (3-step drop depth 5.0 = no ... | shotgun passer gathers and retreats 0.5 / 1 / 2 yd over 0.5 / 0.65 / 0.85 s before he i... |
| E-088 | S2 | QBSTANCE | fixed | Roll-out / boot ends at a fixed point (6 yd laterally, at most 4.5 yd deep) t... | A rolling QB keeps going to about 10 yd laterally at 0.8 vmax (QbSetMotion); test RollO... |
| E-089 | S2 | QBSTANCE | fixed | Throws off a roll-out / boot are planted: wind-up zeroes the QB's velocity un... | Want = 0.9 * Vel in the wind-up when rolling |
| E-090 | S2 | QBSTANCE | fixed | Pocket is static: with a clean pocket Want is zero from set to release; "feet... | Live feet: slow lateral slide in a clean pocket (period 1.6 s), step-up kept; test Clea... |
| E-091 | S2 | QBSTANCE | fixed | QB shoulders never track the read progression; facing is hard-locked downfiel... | Facing within 35 degrees of the current read (QbSetFacing), QbLookTargetId hook, head o... |
| E-092 | S2 | SIM | fixed | QB has no pocket evasion: AI moves are disabled on pass plays before the LOS ... | pocket evasion: a side-step or step-up (0.35 s, 3.5 yd/s, 1.2 s cooldown) when pressure... |
| E-093 | S2 | SIM | fixed | Scramble is only entered on bail, and a scrambling QB can neither throw it aw... | scrambling passer can throw it away and slide; scramble is entered only after a held st... |
| E-094 | S2 | SIM | fixed | Scrambling QB becomes a runner within about a second: scramble hands off to C... | scramble hold logic (UpdateQbScramble): he keeps looking downfield before he turns runner |
| E-095 | S3 | SIM | fixed | Under-centre drop depths are 0.5-1.5 yd shallow | under-centre depths 4.7 / 7.0 / 9.3 yd (were 3.7 / 5.9 / 8.1); set depths 4.3 / 6.6 / 8.7 |
| E-096 | S3 | QBSTANCE | fixed | QB freezes after a hand-off, fake or throw (no ride, no carry-out fake, no fo... | Carry-through after a hand-off / throw (0.9 s decay), step toward the runner on the fake |
| E-097 | S3 | SIM | fixed | QB-draw / draw "pass drop" is a 1.5-1.8 yd/s shuffle for under 0.85 s (about ... | QB draw drop is a real retreat (was a 0.8 yd shuffle) before the keep |
| E-098 | S1 | QBSTANCE | fixed | Throw direction is ignored: body is yawed at 720 deg/s, no closed front shoul... | Keyed throwing pose: front shoulder closed (+40 deg), stride planted toward the target,... |
| E-099 | S1 | QBSTANCE | fixed | Release is not gated by body alignment: the 0.20-0.26 s default wind-up is sh... | QbWindupExtra (time to turn) + QbAligned gate (release only within 20 degrees or after ... |
| E-100 | S1 | SIM | fixed | Quick-game release is far too early: the earliest-throw floor is 0.55 s, so s... | earliest-throw floors by drop type (shotgun 1.05 s, under centre 1.25 s), after the bre... |
| E-101 | S1 | SIM | fixed | Vertical routes can be thrown at about 1.5 s | vertical routes wait for the drop (1.75 / 2.10 / 2.40 s floors) |
| E-102 | S1 | SIM | fixed | Ball flight 20-50 yd is 20-30 percent too fast and too flat | ONE monotone speed curve fitted to the NFL flight times (table above); drag 0.0060 per ... |
| E-103 | S1 | SIM | fixed | Only the Lob style (58 yd and up) is an arc; every other throw uses the low-a... | a covered intermediate target is retried as a Touch pass with real loft over the undern... |
| E-104 | S1 | SIM | fixed | Throw-away launches instantly: no wind-up, ball leaves on the decision tick | throw-away goes through the wind-up (StartWindup(q, faceDir, target), LaunchThrowaway) |
| E-105 | S2 | QBSTANCE | fixed | Release sync is clamped to 0.8-1.8x clip speed against a 0.26 s sim wind-up | Clip speed 0.9-1.45 instead of 0.8-1.8 (lead cut from the front) |
| E-106 | S2 | QBSTANCE | partial | Off-platform, sidearm, jump and falling throws are not available | Off-platform / hurried throws use the moving take plus the on-the-run pose; no timed of... |
| E-107 | S2 | QBSTANCE | fixed | No left-handed QB (9%) and no left-footed kicker: the view never reads Hand | Left-handed QB: mirrored throw takes with timing, mirrored poses, ball in the left hand... |
| E-108 | S2 | SIM | fixed | Throw/step slides: the clips step forward in place while the sim QB stands | (view hook) the passer steps into the throw over the wind-up (QbWindupExtra / QbFollowT... |
| E-109 | S2 | QBSTANCE | fixed | QB ball jumps between carriage points on every state change | SetQbHold re-marks the attach blend when the throw / carriage state changes (the ball g... |
| E-110 | S2 | SIM | fixed | Route waypoint times assume 7 yd/s, receivers need 20-25 percent longer, so t... | route speed 5.6 yd/s (was 7), waypoint times rebuilt, so the QB no longer throws before... |
| E-111 | S2 | SIM | fixed | Max throw speed too high: rating 75 throws 63 mph, 99 throws 69 mph | MaxThrowSpeed 19 + 9 x rating (75 = 52 mph, 99 = 57 mph) |
| E-112 | S2 | SIM | fixed | Lead point ignores the break deceleration: PredictPos advances at a constant ... | PredictPos follows the route's own segment speeds and break deceleration |
| E-113 | S3 | SIM | fixed | Throw velocity and the flight time are fast for deep throws and slow for shor... | one curve for all styles; speed spread narrowed |
| E-114 | S3 | QBSTANCE | fixed | Throw-away: release is 0.14 s after clip start but the fade-in floor is 0.22 s | Throw-away starts 0.26 s before the release |
| E-115 | S3 | QBSTANCE | partial | Chest carriage IK: fixed metre offsets, no hand orientation, fades out in 0.1... | Pocket carriage is now the pose layer (offsets scaled by the body size, follow the shou... |
| E-116 | S3 | QBSTANCE | fixed | One spin rate for every pass and kick | BallSpinHint: bullet 3000, touch 2400, lob 1800 deg/s (one-line hook in the GFX BallFol... |
| E-117 | S3 | SIM | fixed | Ball speed is non-monotonic with distance (26 yd Bullet 25.7 yd/s, 27 yd Touc... | the Bullet blends into the Touch arc over 21-27 yd; speed never steps backward with dis... |
| E-118 | S3 | SIM | fixed | Drag is about 60 percent of a real football's; deep lofts hold their speed | pass drag 0.0060 per yard (was 0.0045); kicks keep 0.0045 through a thread-static flag |
| E-119 | S3 | GFX | fixed | Ball has no spin or orientation state, so a spiral, wobble, tumble or punt fl... | (the QBSTANCE track later made the pass spin follow the throw style through BallSpinHin... |
| E-120 | S3 | SIM | fixed | Every throw is aimed at the same chest height and the route position: no back... | AimHeight / AimOffset: back-shoulder, low ball to a crosser, high-point ball |
| E-121 | S3 | SIM | fixed | A committed throw cannot be pumped or aborted; the window is 0.26-0.42 s | pump fake and abort window; no pump under pressure |
| E-122 | S1 | CONNECT | fixed | Contested-catch pair restarts the receiver's catch animation on the catch frame | The stage asks the contact director for a contested pair when the catch is a duel (defe... |
| E-123 | S1 | CONNECT | fixed | The contested-catch pair owns the receiver for 2 to 3 s while the sim runs hi... | ReleaseAAfter = 0.35 s after the contact: the receiver leaves the take and runs on (vie... |
| E-124 | S1 | CONNECT | fixed | Punt returners carry the ball in the quarterback hold pose | QB-hold branch restricted to the quarterback / holder / punter before the kick (PlaySta... |
| E-125 | S2 | CONNECT | partial | Ball is far from the hands at the moment of the catch and in the pocket | Hands reach for the real ball from 0.32 s before the catch (reach IK) and the displayed... |
| E-126 | S2 | CONNECT | fixed | Ball jumps from the extended hands to the forearm at the catch | BallFollower catch-hold mode: both hands 0.14 s, then a 0.3 s tuck to the carry arm (bl... |
| E-127 | S2 | CONNECT | fixed | Catch pose is chosen from variants only; hands do not reach the real ball | SimAgentView.ReachBall / ArmIkLate: two-hand diamond (hands 7.5 cm either side of the b... |
| E-128 | S2 | CONNECT | fixed | Catch key list excludes dive, contested, one-hand, toe-tap, fair catch | AnimKit.CatchFamily: catch_dive / catch_one_hand at full stretch, catch_toe_tap within ... |
| E-129 | S2 | CONNECT | fixed | Contested catch is picked for any defender within 1.9 yd and always "receiver... | Contested only for a duel (see E-122): carrier speed under 2 m/s, defender under 1.0 yd... |
| E-130 | S2 | CONNECT | partial | No contested pair for interceptions, deflections, drops, breakups | Reactions for Deflection (receiver flinch), Interception (receiver frustration take), D... |
| E-131 | S2 | CONNECT | fixed | Fumbled ball pops when the displayed carrier is offset from the sim body | BallFollower.SetFree blends the free ball from the hand over 0.2 s up to 3 m of offset ... |
| E-132 | S2 | CONNECT | fixed | Switching the carry arm teleports the ball across the body | SetCarry with a changed side is an attach event: 0.2 s blend with a 7 cm arc across the... |
| E-133 | S2 | CONNECT | fixed | Defensive backs get no read-ahead for catches and jump balls are late | PlayStage.Flow FindBestDefender: the defender with the smallest miss distance reaches a... |
| E-134 | S2 | SIM | fixed | Catch is awarded while the ball is up to about 1 yd from the hands | pending catch: the envelope only starts the catch, the ball reaches the hands in 2-6 ti... |
| E-135 | S2 | SIM | fixed | Receiver stops dead under the ball: speed is forced to 0 within 0.35 yd of th... | receiver no longer stops dead under the ball (route kept within 0.9 yd) |
| E-136 | S2 | SIM | fixed | Receiver abandons his route and beelines to the landing spot, facing the dire... | receiver stays on the route and tracks the ball with his hands; no beeline |
| E-137 | S3 | CONNECT | fixed | Ball orientation snaps at every attach | Ball orientation is slerped from the last orientation over the attach blend (BallFollow... |
| E-138 | S3 | SIM | fixed | Interception and fumble-recovery catches have no commit window or speed cap | interception 0.45 s and fumble-recovery 0.30 s commit windows at 0.72-0.78 of top speed |
| E-139 | S1 | CONNECT | partial | Hand-off pair has the same seed bug and no distance correction | Both actors of a hand-off use one pair seed (PairSeed) so they play the matching A / B ... |
| E-140 | S1 | SIM | fixed | Ball carrier re-picks his lane every 0.07-0.23 s with only a weak continuity ... | lane commit 0.35 s with a held-lane bonus; no mid-run direction flips |
| E-141 | S1 | SIM | fixed | Shotgun / pistol hand-off happens at about 0.30 s with the back nearly standi... | shotgun hand-off no earlier than 0.78 s (0.70 under centre); the back gets a draw shuff... |
| E-142 | S2 | CONNECT | fixed | Spin clip is a full-body override on a running carrier | Spin is an upper-body plan over the locomotion legs (AnimKit.SpinDir). |
| E-143 | S2 | QBSTANCE | fixed | Pitch/toss uses an overhand lob clip | Procedural underhand pitch pose (QbToss) instead of the overhand lob take |
| E-144 | S2 | CONNECT | fixed | Catch commit window suppresses the juke/stiff-arm/truck/spin that follows a c... | The catch commit ends at the hands-secure frame (0.25 to 0.6 s) and ranks 4, below the ... |
| E-145 | S2 | CONNECT | fixed | Spin direction is random and unrelated to the sim spin | The spin take is chosen by the sign of its measured turn (MoveTakeTable turnDegrees) ag... |
| E-146 | S2 | CONNECT | fixed | Ball carrier holds the ball in the arm "away from the defender" only for the ... | Carry tuck IK (SimAgentView.SetTuck): the arm that holds the ball folds it to the ribs,... |
| E-147 | S2 | CONNECT | fixed | Stiff-arm win ragdolls the defender even though the sim only stumbles him | A won stiff arm no longer ragdolls the defender: he staggers with the simulated Stumble... |
| E-148 | S2 | CONNECT | fixed | Carry pose is a forearm lerp with world-axis offsets, no per-body fit, no han... | Carry pose measured from forearm, hand and chest bones (inward = toward the chest), plu... |
| E-149 | S2 | SIM | fixed | LaneFinder sees only defenders (no blockers, holes or cutback lanes), so runn... | LaneFinder sees blockers and the held lane, penalises running along the LOS |
| E-150 | S2 | SIM | fixed | Designed run paths have too little weight; power, counter and trap runs drop ... | designed-path guide weight 4.5 (3.0 for cutbacks) |
| E-151 | S2 | SIM | fixed | AI carrier moves fire on a 4.5 percent per-tick roll, at arbitrary distance (... | AI moves decided once per threat on the defender's commit (closing over 3 yd/s, 0.17-0.... |
| E-152 | S2 | SIM | fixed | Juke, spin and dive are velocity impulses that bypass the friction circle (4.... | juke / spin / dive steer through the friction circle (MoveSpeed, LatBoost) |
| E-153 | S2 | SIM | partial | Carrier AI has no clock, first-down or sideline logic: it avoids the sideline... | dives for the sticks and slides the quarterback; stepping out of bounds and clock logic... |
| E-154 | S2 | SIM | fixed | Read option is decided at the first frame the hands meet (about 0.3 s) from a... | read option reads the end's displacement over the mesh, not one frame |
| E-155 | S2 | SIM | fixed | The mesh point is wherever the QB chases the back to (QB walks toward the run... | mesh thresholds: closest-passed under 2.5 yd, distance under 1.3 yd; no 5.2 yd/s chase ... |
| E-156 | S2 | SIM | fixed | Toss / jet: QB stands still and pitches at 0.28 s; pitch flight can be as sho... | pitch no earlier than 0.40 s; jet man starts in motion at 6 yd/s (auto motion, AutoJetM... |
| E-157 | S2 | SIM | fixed | Carriers do not brake into planned cuts (only receivers do), so the cut model... | carriers brake into planned cuts |
| E-158 | S3 | CONNECT | fixed | Carrier moves rely on animator triggers: no commit and they can queue | An unconsumed carrier-move trigger is reset 0.3 s after it was set (LocomotionAnimator.... |
| E-159 | S3 | CONNECT | fixed | Hand-off ball slides between hands in 0.12 s | Hand-off exchange mode: the ball travels from the quarterback's chest hold to the back'... |
| E-160 | S3 | SIM | fixed | AI never protects the ball or switches the ball side; the ball is always at 0... | BallSide carries the ball away from the nearest threat |
| E-161 | S3 | SIM | fixed | Draw: the back stands still 0.75 s ("looks like pass pro") then runs from zer... | draw back shuffles then accelerates through the mesh |
| E-162 | S1 | CONNECT | fixed | Engaged OL/DL pairs are picked from different takes | Pair seed of an engagement = ids of the pair + play index, identical for both actors (a... |
| E-163 | S1 | CONNECT | fixed | Pass-protection "Block" is a stationary pocket-stand loop for all blockers an... | AnimKit.BlockPlan: pass set = stand / kick-slide step / shuffle by velocity against fac... |
| E-164 | S1 | CONNECT | fixed | All blocking actions by DL, WR, CB and RB use the offensive-line pass-set loop | WR / CB / S stalk and RB / DL / TE chip use hands-up upper-body plans over their own le... |
| E-165 | S1 | CONNECT | fixed | OL never gets the fire-out: Block plan is "valid" so get-off is skipped | OL leaving the stance get the run-block footwork (run) or the pocket step (pass) as the... |
| E-166 | S1 | SIM | fixed | DL and OL start touching: DT at y = +1.0 on the same x as the guard, radii 0.... | hands meet 0.14 yd past the bodies, OL y -0.15, pre-snap settle: 0 overlapping engageme... |
| E-167 | S1 | SIM | fixed | The second blocker has no body collision with the defender or the first block... | the second blocker is a real body (locked pair softly separated) |
| E-168 | S2 | SIM | fixed | Block engagements teleport bodies 0.35-0.51 yd in one tick when they lock up,... | locked pair eased apart 0.05 yd a tick, no 0.5 yd pop |
| E-169 | S2 | CONNECT | partial | Mocap clips are authored facing other directions and the correction is visibl... | Pair pulls turn both bodies face to face, the clip facing offsets now blend with the po... |
| E-170 | S2 | CONNECT | fixed | Setback of 0.36 m eases out at 1.6 m/s after the snap: whole line skates forward | The setback is given back over the first 1.5 m the lineman really travels (smoothstep),... |
| E-171 | S2 | SIM | fixed | OL/DL action thrash around the 2.5 yd/s Block threshold | blockers keep the Block action while they work |
| E-172 | S2 | CONNECT | fixed | Pancake knocks only the loser; the blocker plays nothing | The pancaking blocker plays a drive-finish take (ol_block_push window) while the loser ... |
| E-173 | S2 | CONNECT | fixed | OL/DL engagement is a constant 0.78 m pull on the primary blocker only | Pull distance by take type (run 0.72, pass 0.78) scaled by both body sizes, every block... |
| E-174 | S2 | CONNECT | fixed | Pulled pair pops back when the engagement ends | Per-agent pull weight decays over 0.2 s after the engagement ends. |
| E-175 | S2 | SIM | fixed | Rush moves are random picks with no link to alignment, matchup geometry or we... | rush move weights depend on weight, alignment and geometry; counter move +14 |
| E-176 | S2 | SIM | fixed | Pass-pro assignment is a greedy cost sort with no left-to-right order: blocke... | pass-pro assignment is a monotone left-to-right match |
| E-177 | S2 | SIM | fixed | Pass-pro blockers do not set or kick-slide: they chase the rusher to a point ... | pass set: kick-slide to LOS - 0.4 at min(0.62 vmax, 4.5) |
| E-178 | S2 | SIM | fixed | Run blocks win by default: bias = 0.30 (+0.22 vs LBs) gives an equilibrium Le... | run bias 0.20 (0.12 vs linebackers) |
| E-179 | S2 | SIM | fixed | Pass-mode pancake uses a per-tick probability: NextFloat() < 0.03f at 60 Hz i... | pass pancake probability 0.0006 per tick after 0.9 s |
| E-180 | S2 | SIM | fixed | Engagement is a single scalar with no geometry: no leverage, angle, depth of ... | geometry term (0.16 x (side - 0.75)) |
| E-181 | S2 | SIM | fixed | Combo (double-team) blocks last 0.25 s before one blocker peels, whether or n... | combo holds 0.8 s, peels only for a linebacker inside 2.5 yd |
| E-182 | S2 | SIM | fixed | Any free blocker can join any engaged defender: the comment says doubles are ... | join rule: adjacent lineman or assigned blocker only |
| E-183 | S2 | SIM | fixed | Zone blocking is a static 1:1 monotone match at the snap and then re-picks ev... | 0.6 s target lock |
| E-184 | S2 | SIM | fixed | Pulling blockers run their path from tick 1 at 0.95 vmax, the first waypoint ... | pulls ramp from rest; first waypoint -0.7 |
| E-185 | S3 | CONNECT | partial | Pass-rush moves: Rip and Speed play the swim take, wrong-arm never plays, las... | Moves map 1:1 where the library has a pair (bull, swim, speed, spin, rip via the escape... |
| E-186 | S3 | SIM | fixed | Chip blocks release by clock (0.9 s) regardless of the edge man's state, so t... | chips release on the edge man's state |
| E-187 | S3 | SIM | fixed | Run blockers with no target run to a point 4 yd past the carrier, dragging li... | second-level seek for run blockers with no target |
| E-188 | S1 | CONNECT | partial | Defense press: jam hands exist only on the stance and drop at the snap | Press jam hands stay up 0.5 s after the snap and the Jam event plays a short collision ... |
| E-189 | S2 | CONNECT | fixed | Defenders (interception, pass breakup) get their reach too late and with the ... | Defenders reach ahead of the ball with the swat / interception takes (see E-133). |
| E-190 | S2 | QBSTANCE | fixed | CB treated as "press" out to 3.2 yd | Press only within 1.2 yd (or by call) |
| E-191 | S2 | CONNECT | partial | No turn-and-run, hip flip or break-on-ball for coverage | Hip flip: man_turn_run 135 / 180 degree takes on the Backpedal / Shuffle to Run edge. N... |
| E-192 | S2 | SIM | fixed | Zone defenders play man: once a receiver is in the (width/2 + 2.5) catchment ... | narrower catchment and cushion; zone men no longer play man |
| E-193 | S2 | SIM | fixed | Zone drops: straight line to the landmark at 0.95 vmax from the snap, no peda... | pedal, gather and decel into the landmark |
| E-194 | S2 | SIM | fixed | Underneath assignments send a linebacker 11 yd to a flat: AllocUnder sorts by... | AllocUnder: flats to DBs, hooks and curls to linebackers |
| E-195 | S2 | SIM | fixed | Deep zones never pass receivers off; two deep defenders can both lock on the ... | CoveredByDeepTeammate pass-off |
| E-196 | S2 | SIM | partial | No bracket/help coverage or post-snap safety rotation: Cover 1 free safety is... | FS cheat and quarters match, strength shift; no full rotation / bracket on motion |
| E-197 | S2 | SIM | partial | Cover 4 / Match / Cover 6 are plain spot-drop zones; no pattern matching (rea... | match coverages read the second receiver for vertical vs out; not full pattern matching |
| E-198 | S2 | SIM | fixed | Off-man cushion collapses on a timer, not on the receiver: from 6+ yd to 1.5 ... | off-man cushion follows the receiver's route |
| E-199 | S2 | SIM | fixed | Press technique is stand-still plus dice: corner holds Want = 0 up to 0.32 s,... | press mirror: the jam needs contact |
| E-200 | S2 | SIM | fixed | Ball reaction: every responsible defender (resp >= 0.45) runs to the earliest... | man to the receiver's hands, zone nearest two |
| E-201 | S3 | SIM | fixed | Zone landmarks and regions are ball-relative, not field-relative: with the ba... | deep zones are field-anchored |
| E-202 | S3 | SIM | fixed | A safety matched in man keeps his formation depth (9-12 yd) regardless of the... | man safeties take man depths |
| E-203 | S3 | SIM | fixed | CurlLeft/Right zones are never assigned; hooks sit at 8-9 yd, shallow of the ... | hooks 10 / 10.5, curl 11.5, flat 5 |
| E-204 | S3 | SIM | fixed | Pass interference is one dice roll when the ball reaches the receiver: any no... | DPI needs contact (0.06 s), a defender inside the radii + 0.8 yd, then a probability; 1... |
| E-205 | S1 | CONNECT | fixed | Every authored tackle is slower than the game: a sprinting carrier stops dead... | Hard speed weight in take choice (0.55 per m/s of deficit) and momentum carry-through: ... |
| E-206 | S1 | CONNECT | fixed | A second tackler on a carrier who is still in a Broken take only adds a "gang... | A tackler arriving on a carrier in a Broken / Truck / SackBroken take ends that take (b... |
| E-207 | S1 | CONNECT | fixed | The beaten tackler of a Broken take is dragged along the ground by the carrie... | The authority correction moves the carrier only; the beaten tackler gets at most 25 cm ... |
| E-208 | S1 | SIM | fixed | A clean tackle sets the carrier Down, Fall(1.1), cuts his velocity to 55 % an... | momentum-conserving impact over 0.22 s, no one-tick cut |
| E-209 | S2 | SIM | fixed | Pass-rush and coverage closing speeds at contact show defenders arriving at f... | pursuit gather only for head-on closers |
| E-210 | S2 | CONNECT | fixed | Generic tackle: tackler and carrier choose independent A takes | Tackler and carrier share one pair seed (AnimKit.TacklePair). |
| E-211 | S2 | CONNECT | fixed | Stumble action has no clip | AnimKit.Stumble: the collision-and-stagger takes, over the legs once moving. |
| E-212 | S2 | CONNECT | fixed | Fallen has no get-up: the held fall pose pops to a run | Fall plans are no longer held forever: fall, then the get-up take on the Fallen end, th... |
| E-213 | S2 | CONNECT | fixed | Tackle take starts a fixed 0.55 s in, not at its contact moment | The generic tackle take starts 0.25 s before its measured impact moment when the timing... |
| E-214 | S2 | CONNECT | fixed | View yaw of a contact actor never hands back to the simulation | A carrier who runs on has his yaw handed back to the simulation with the authority ramp. |
| E-215 | S2 | CONNECT | fixed | Carrier is locked out of the match animation logic until the longer clip B fi... | Each actor is released when HIS take ends (carrier at EndA), the other keeps playing. |
| E-216 | S2 | CONNECT | fixed | Get-up part of every tackle take is never seen: whistle wind and fade cut it | The post-play window waits (budget 3.1 s) until nobody is in a take, get-up or ragdoll ... |
| E-217 | S2 | CONNECT | fixed | Tackle variant choice ignores tackler height, body size, closing direction an... | Score uses lowB against the tackle type, hip distance against the body sizes, a six-tak... |
| E-218 | S2 | CONNECT | wontfix | No mirroring: a side that has no authored variant plays the wrong side take | Needs mirrored pair data and mirrored contact clips: none exist (MirroredClips holds st... |
| E-219 | S2 | CONNECT | partial | Big hits reuse 3 takes, all from the carrier's left, and "Failure" takes are ... | Variety history added and the BigHitShrug kind (Failure takes) exists in TackleVariants... |
| E-220 | S2 | CONNECT | fixed | SackBroken, Truck and Collision pair kinds are never produced | BrokenTackle on the QB routes to SackBroken; the Truck event plays a Truck pair; the ja... |
| E-221 | S2 | CONNECT | fixed | Sack takes depend on the Sack event being the very next log entry | The sack is found within the next 3 log entries or from the tackle itself (QB in the po... |
| E-222 | S2 | CONNECT | fixed | A dragged (held) take is a frozen statue that slides with the carrier | A dragged take is a struggle: a 0.45 s window ping-pongs while the tackler hangs on, th... |
| E-223 | S2 | CONNECT | fixed | Gang tackle joiners are not matched to the carrier's actual pose, timeline or... | Joiners start 0.25 s after the primary contact, avoid used takes, stand at their own an... |
| E-224 | S2 | CONNECT | fixed | Pairs are aligned by hips only and ignore body size, so S/M/L and OL/DB hits ... | Pair separation scales with both body radii (0.85 to 1.25). Not done: tackler hand IK t... |
| E-225 | S2 | CONNECT | fixed | The warp from sim to take is a 0.18 s position and yaw snap | Lead-in 0.36 s (was 0.18 s) for the warp. |
| E-226 | S2 | CONNECT | fixed | Ragdoll runs in FixedUpdate at 50 Hz with no interpolation | Ragdoll bones are drawn between the previous and the current physics state with the fix... |
| E-227 | S2 | CONNECT | fixed | The ragdoll is built lazily on the first hit of each view and hitches that frame | Stage.WarmAllBodies builds every roster body (size x base) and its ragdoll behind the l... |
| E-228 | S2 | CONNECT | fixed | The ragdoll rides along with the sim agent through the view root (verify in p... | The view root stays where the hit began while the ragdoll is active; the bones are no l... |
| E-229 | S2 | CONNECT | partial | Get-up take is a random pick over all pairs by face-up sign only | Get-up chosen by how the body lies (chest normal) among its class. Dedicated getup_* cl... |
| E-230 | S2 | CONNECT | fixed | Ragdoll recovery can freeze the body mid-tumble | MaxActive 2.0 s with a 0.6 s speed-gated extension. |
| E-231 | S2 | CONNECT | fixed | Solo get-up glides to the sim position, up to several metres, in 0.7 s | The get-up plays where the body lay and he walks on to the simulation at 3.2 m/s (view ... |
| E-232 | S2 | CONNECT | fixed | QB hit after the throw and roughing have no reaction | QbHit event: upper-body flinch take on the quarterback. |
| E-233 | S2 | CONNECT | partial | Fumble and recovery have no strip, scramble or pile | The recovering player dives (fumble_dive). Not done: strip reaction on the carrier, scr... |
| E-234 | S2 | SIM | fixed | QB hit is only logged after the ball is released and only within 0.55 yd; hit... | QbHurry event (hits and hurries before the throw) |
| E-235 | S2 | SIM | fixed | Engaged defenders cannot tackle even if the ball carrier runs into their arms | InTackleLock: engaged defenders can tackle a carrier who runs into them |
| E-236 | S2 | SIM | fixed | Tackles resolve from any approach: a defender running away from the carrier o... | facing and reach gate |
| E-237 | S2 | SIM | fixed | Tackle momentum ignores mass: the same 0.55/0.35 factors for a 190 lb DB on a... | ImpactVel scales with mass |
| E-238 | S2 | SIM | fixed | Broken tackles drop the tackler flat for 0.9-1.3 s even on a standing wrap-up... | standing wraps break to a stumble, not a fall |
| E-239 | S2 | SIM | fixed | Dive tackle lunge speed (5.2 yd/s) is below running speed, so a defender who ... | dive lunge >= 85% of dive speed |
| E-240 | S2 | SIM | fixed | After a whiffed tackle both players ignore each other's bodies (0.6-0.7 s): t... | ignore windows depend on the outcome (0.3-0.6 s) |
| E-241 | S3 | CONNECT | fixed | Ending a drag with a broken tackle cuts the wrap pose instantly | Breaking a drag blends the held pose into the broken take (no Stop then Play). |
| E-242 | S3 | SIM | fixed | Sack is the normal tackle contest with a flat +12 for the passer and an insta... | sack contest +7 for the passer (was +12) |
| E-243 | S3 | SIM | fixed | Dive tackles are triggered by a flat 1-2.2 % per tick roll in a 1.5-2.7 yd wi... | dive needs closing over 4 yd/s |
| E-244 | S3 | SIM | fixed | Gang tackles exist only as a side effect of a drag; later arrivals while a wr... | PileOn event and gang tackles |
| E-245 | S3 | SIM | fixed | Forced fumbles only occur as a roll at tackle resolution; no strip attempt wh... | strip chance on the hit |
| E-246 | S3 | SIM | fixed | Tacklers stand still for the full Tackling state (0.75 s) because the AI skip... | tackler gets up after 0.55 s |
| E-247 | S1 | CONNECT | fixed | Hand-off transfers the ball instantly with no blend, and can fire with the ru... | The ball travels hand to hand over 0.24 s, centred on the simulated exchange (the quart... |
| E-248 | S1 | CONNECT | fixed | At the snap every player drops through a 0.1-0.2 s Stance-to-Idle action befo... | The simulated Stance to Idle at the snap keeps the stance clip for 0.5 s; the get-off i... |
| E-249 | S1 | CONNECT | fixed | Pre-snap shifts, audibles, flips and motion setup teleport the players | Shifts, audibles, flips and relayouts are walked: the view glides to the new spot at a ... |
| E-250 | S1 | CONNECT | partial | Bone pops: pelvis and chest snap in the middle of play, mostly in the line an... | Pop filter (SimAgentView.PopFilterLate): pelvis / chest jumps over 800 / 1300 deg/s or ... |
| E-251 | S1 | CONNECT | fixed | Get-off plays a forward clip for backpedal and zone players, full body for up... | AnimKit.GetOffFor chooses the get-off by the first action and heading (backpedal gets t... |
| E-252 | S1 | CONNECT | fixed | No huddle, no break to the line: players appear in formation after a black cut | Huddle ring 8.5 yd behind the ball, break to the line at a jog, defense jogs up; the sn... |
| E-253 | S1 | CONNECT | fixed | Holder never puts the ball on the turf: it stays in a QB chest hold, then tel... | BallFollower spot mode: the holder steadies the ball upright on the turf; the sim launc... |
| E-254 | S1 | QBSTANCE | fixed | Long snapper plays the quarterback throw clip | AnimHints.Snapper + AnimKit.LongSnapPlan (long_snap_00, released at its moment) |
| E-255 | S1 | CONNECT | fixed | Kickoff ball falls off the tee the moment the play goes live | The tee ball stays on the tee until the kick is struck, at the launch height (KickGeome... |
| E-256 | S1 | CONNECT | fixed | Punt drop is missing: ball jumps from chest to 0.8 yd ahead at knee height | The punter drops the ball for 0.34 s into the foot; sim contact point 0.55 yd ahead at ... |
| E-257 | S2 | SIM | fixed | Pre-snap bodies pop 0.36-0.41 yd (about 24 yd/s) on the first live tick: corn... | SettlePreSnapBodies: 0 first-tick pops above 0.15 yd |
| E-258 | S2 | CONNECT | fixed | A stance clip keeps playing while a receiver runs his route and a forward-spr... | The stance clip is replaced at the first real action by the get-off / route; no stance ... |
| E-259 | S2 | CONNECT | partial | Clip thrash: linemen and quarterbacks change the playing clip about once a se... | 0.4 s dwell for Block / Engaged / Shed / Idle / Stumble / locomotion flips, no restart ... |
| E-260 | S2 | CONNECT | fixed | Stationary full-body overrides over a body the sim keeps moving | The upper-body mask is re-decided every frame by the simulated speed (2.0 / 1.2 m/s hys... |
| E-261 | S2 | SIM | fixed | Run/Sprint/Idle/Backpedal/Shuffle chosen by raw sim thresholds, no hysteresis | gait hysteresis (stop band 0.6 / 1.0, backpedal 1.85 / 2.15, shuffle 0.70 / 1.00, sprin... |
| E-262 | S2 | CONNECT | fixed | Masked clip started over a full-body clip: legs pop when the outgoing slot is... | An outgoing full-body clip gives the legs back to locomotion over the same fade when th... |
| E-263 | S2 | CONNECT | fixed | Block->Engaged->Shed flip without commit; each flip restarts a paired take | Dwell plus no restart of the pair that is playing. |
| E-264 | S2 | CONNECT | fixed | Facing offset of a new clip is applied the moment it starts and slewed at 900... | The facing offset is blended with the poses' own weights (ActionAnimator.FacingOffset). |
| E-265 | S2 | CONNECT | fixed | Loop yaw decays at 420 deg/s on leaving locomotion: body spins on Backpedal->... | The loop yaw leaves with the locomotion layer weight instead of unwinding at 420 deg/s. |
| E-266 | S2 | CONNECT | fixed | Third clip request recycles a still-fading slot: pops on rapid sequences | A third request waits for the hidden slot's fade (0.1 s) instead of recycling it. |
| E-267 | S2 | CONNECT | partial | Coverage matrix counts library clips, not playing clips | Docs/AnimationCoverage.md gets a runtime-use section with the generated unused-key list... |
| E-268 | S2 | CONNECT | fixed | Every play is built twice for the user (preview + real) | The call-screen preview is kept when the call did not change. |
| E-269 | S2 | CONNECT | fixed | Pools are pre-warmed with the Std body only; the other 4 bases are created mi... | Warm builds every roster body. |
| E-270 | S2 | CONNECT | fixed | A fade to black (about 0.5 s) bookends every play | Fade dips shortened to 0.1 / 0.12 s (the walk-in carries the cut). |
| E-271 | S2 | CONNECT | partial | After the whistle everyone just stops: no help-up, no walk back, no pile-on | After the whistle the nearby players gather round the spot at a walk and the rest drift... |
| E-272 | S2 | CONNECT | fixed | A dropped pass and a catch play the same action, so the anim cannot show a drop | The Drop event marks the receiver's catch as dropped (hint), the catch plan stops befor... |
| E-273 | S2 | CONNECT | wontfix | AgentAction has no QB-specific phases (Dropback, Set, Windup, Scramble, Slide... | AnimHints.Qb already carries the QbPhase to the view (v0.7.0 flow); a new AgentAction w... |
| E-274 | S2 | CONNECT | fixed | Animation hooks expose only drop point, step count, roll flag and hand-off ta... | PlaySimulation.QbSetTime, QbDropProgress01, QbLookTargetId, QbReleaseEta, QbThrowsLeft,... |
| E-275 | S2 | SIM | fixed | Standing up after a fall is a free pop: Step returns early while FallenTime >... | (view hook) 0.4 s get-up (AgentAction.GetUp, speed 40%, wind-up 1.4x); the view plays t... |
| E-276 | S2 | SIM | fixed | Pre-snap bodies are never separated: DefenseAlignment writes Body.Pos directl... | pre-snap separation |
| E-277 | S2 | CONNECT | partial | Beaten at the line: the corner is "turned around" by a 0.25-0.40 s backward d... | View-side hip-flip take (E-191). The sim deceleration model of the beaten corner is SIM's. |
| E-278 | S2 | SIM | fixed | Hip-turn freeze replays the previous Want at full strength: the defender keep... | hip-freeze replays the old Want at 0.35 |
| E-279 | S2 | CONNECT | partial | Pancake drops the defender to the ground instantly with his speed preserved; ... | View-side: the loser staggers 0.12 s before the ragdoll, the blocker follows through. T... |
| E-280 | S2 | CONNECT | wontfix | Shed ends the engagement with an instant 1.6 yd/s velocity kick and flips bot... | Sim velocity kick and state flip: SIM owns BlockEngagement; the contact view eases the ... |
| E-281 | S2 | CONNECT | wontfix | Engagement starts the moment the bodies touch: the Engaged action begins with... | An EngagePunch window needs a new sim action state (SIM); the pair takes begin with the... |
| E-282 | S2 | LOCO | handed-over | Locomotion action is derived from raw angle between velocity and facing with ... | SIM |
| E-283 | S2 | CONNECT | fixed | kick_return_catch clip is never played (generic WR catch is used) | kick_return_catch is played for returners (CatchFamily ReturnCatch). |
| E-284 | S2 | CONNECT | partial | Fair catch has no signal, no gesture, play just ends | fair_catch take at the catch frame; the signal before the catch needs the fair-catch fl... |
| E-285 | S3 | SIM | fixed | Stance->action lookup (Idle vs Stance) flips at 0.5 yd/s | Idle / Stance hysteresis |
| E-286 | S3 | CONNECT | fixed | Fade floor of 0.22 s softens contact and quick moves | Per-category fade floor: impact takes (tackle, catch, hand-off) 0.08 s, locomotion hand... |
| E-287 | S3 | CONNECT | fixed | Stance->Pocket plays the same clip again at another phase | Stance to pocket continues the playing loop. |
| E-288 | S3 | CONNECT | partial | Mesh/Loco switch stops the crouch with a plain fade, no stand-up transition | The crouch is let go over 0.38 s (was 0.22 s) and the pop filter absorbs the rest. The ... |
| E-289 | S3 | CONNECT | partial | Dead plan builders and dead hints | ReturnCatch wired through CatchFamily, MotionStart added. Cadence, CornerIdle, IdleVari... |
| E-290 | S3 | CONNECT | fixed | Touchdown celebration walkers run straight lines at 6.3 m/s and stop dead | Walk speed profile: accelerate, ease out over the last 2 m. |
| E-291 | S3 | CONNECT | fixed | A thrown exception in the game loop makes all 22 players vanish mid-play | The views stay visible through a 0.15 s fade before the failure branch clears the stage. |
| E-292 | S3 | CONNECT | fixed | Contact director is reset after the views were already returned to the pool | Contacts.Reset runs before the views go back to the pool. |
| E-293 | S3 | CONNECT | fixed | During the play-call screen the preview field is frozen after one Present | The call-screen loop ticks Sim, Stage.Present and the officials. |
| E-294 | S3 | GFX | fixed | Officials only move during live frames, so they sprint to the new spot during... | The crew used to run 6 m/s across the field during the pre-snap after a long gain. Begi... |
| E-295 | S3 | SIM | fixed | A scoop / dog-pile recovery drops the recoverer instantly to Down | recovery dives first (0.45 s) then the whistle follows 0.45 s later |
| E-296 | S3 | LOCO | handed-over | After the whistle everyone brakes at full DecelBase (skates about 4 yd), with... | SIM |
| E-297 | S3 | CONNECT | partial | The Throw action is one 0.55 s override from wind-up start, with another 0.5 ... | QbReleaseEta hook exposed; the Throw action itself is unchanged (QBSTANCE owns the thro... |
| E-298 | S3 | CONNECT | fixed | Snap ball spawns at the center's chest, not between his legs | Snap ball starts between the center's legs (0.25 yd behind, 0.30 yd high). |
| E-299 | S3 | CONNECT | fixed | Touchback in the end zone is an instant EndPlay (no kneel) | The returner kneels before a touchback ends the play. |
| E-300 | S1 | QBSTANCE | fixed | Kicker stands and plants on the RIGHT of the ball (user note 1); kickoff and ... | Kickoff -3.29 m pre-snap, -0.29 at contact; FG -1.65 / -0.67; strips kick_*_Behind.png |
| E-301 | S1 | QBSTANCE | fixed | The punter kicks with his LEFT foot (user note 1: all kickers right-footed) | Punt kicks with the right foot (YAN__P_punt_00_remap, R 20.4 vs L 8.1 m/s); punter 0.41... |
| E-302 | S1 | QBSTANCE | fixed | Kickoff kicker starts on the RIGHT of the tee | KickoffStartSide = -3.6, KickoffStartBack = 5.2 |
| E-303 | S1 | QBSTANCE | fixed | Kickoff plant spot is on the right of the ball (plant foot, body and kicking ... | Plant (-0.40, -0.70) |
| E-304 | S1 | QBSTANCE | fixed | Field-goal / PAT kicker spawns right of the holder (teleported there in Prepare) | FG kicker (-1.8, -2.6) from the holder, formation slot (-1.5, -9.0) |
| E-305 | S1 | QBSTANCE | fixed | FG plant spot on the right of the ball; approach angle only from a short 3.2 ... | FG plant (-0.35, -0.55); approach cap 0.78 vmax; contact lateral -0.67 m (was +0.96) |
| E-306 | S1 | QBSTANCE | fixed | Kick clips are never chosen by foot: no mirror, no _m clip, no handedness model | AnimKit.KickPlan swaps the two left-footed takes (BGS__P_punt_*, onside_kick_00) for ri... |
| E-307 | S2 | SIM | fixed | The field-goal kicker's body passes through the holder at contact | FG plant (-0.66, -0.50) |
| E-308 | S2 | CONNECT | fixed | Kick returners and punt returners get no catch reach, fair catch or catch-ready | Returners reach ahead of the kick (FlowAction) with the return-catch / fair-catch takes. |
| E-309 | S2 | QBSTANCE | fixed | Kickoff approach angle is only about 17 degrees (reads as a straight run) | Approach angle about 35 degrees, shape = 1.6 |
| E-310 | S2 | GFX | fixed | All kicks tumble end-over-end at one fixed rate; punts should spiral | Punt: spiral about the long axis, 2700 deg/s; kickoff: end over end 900 deg/s; field go... |
| E-311 | S2 | QBSTANCE | fixed | Onside and squib kickoffs reuse the full run-up kick although dedicated clips... | Onside uses the dedicated take mirrored to the right foot (KickPlan), squib uses the ki... |
| E-312 | S2 | SIM | fixed | Kick meter "Perfect" window is about 46 ms | (view hook) meter CursorPeriodSeconds 1.45 and PingPongEased, Perfect >= 0.85; the HUD ... |
| E-313 | S3 | CONNECT | partial | Holder loops a 0.25 s slice and assumes the snap arrives 0.45 s after the sna... | The holder holds the first frame of the stance take (was a looping 0.25 s slice). The t... |
| E-314 | S3 | QBSTANCE | fixed | Punter footwork: constant 3 yd/s drift, facing fixed, no drop-step angle | Punter: two steps on a slight angle toward the aim (2.0 then 3.6 yd/s) |
| E-315 | S3 | SIM | partial | Kick power is always 1.0 (no power-versus-accuracy choice) | sim side done (spread x(1 + 0.3 x ...) above 90% power); a power-versus-accuracy select... |
| E-316 | S3 | CONNECT | fixed | Blocked kick has no block attempt visuals and uses one fixed rule | Every rusher within 1.6 yd of the kick leaps. |
| E-317 | S1 | SIM | fixed | Speed ordering is compressed: linemen run like skill players and the DL outru... | DL no longer outruns WR (table above) |
| E-318 | S1 | SIM | fixed | Plays end 20-25% sooner than NFL tempo: median run snap-to-whistle 2.5 s, pas... | +41% run / +32% pass medians |
| E-319 | S1 | SIM | fixed | Route breaks are rounded polylines, not plants: arrive radius 0.35-1.3 yd, Ro... | route breaks are plants (StemFrac), post / corner single 12 yd stem |
| E-320 | S1 | SIM | fixed | A nudge of the stick in the pocket flips the QB to Scramble, and every receiv... | StickHeld required to enter a scramble |
| E-321 | S1 | SIM | fixed | No run fits for LB/safeties/corners: only DL and blitzers have gaps; everyone... | run fits for LB / S / CB (interior runs only) |
| E-322 | S2 | SIM | fixed | Action labels flicker: shuffle/backpedal/run flips and 7 action changes per p... | action flicker removed by the hysteresis |
| E-323 | S2 | SIM | fixed | CPU pre-snap is static: random 2.6 to 4.2 s timer, no motion, no shift, no di... | (view hook) PlaySimOptions.Cadence; MatchDirector must set it for CPU snaps |
| E-324 | S2 | SIM | fixed | Pressure is perceived about 0.25 s before contact, shorter than the 0.42 s wi... | pressure window 1.0 s with an awareness lag |
| E-325 | S2 | SIM | fixed | QB is omniscient: reads the defense's coverage responsibilities, and after 1.... | perceived responsibility, progression one read at a time |
| E-326 | S2 | SIM | fixed | Receivers run every stem at about 98 percent of top speed regardless of route... | per-route stem speeds |
| E-327 | S2 | SIM | fixed | Route depths drift from the standard tree | standard route tree |
| E-328 | S2 | SIM | fixed | Press release is a speed multiplier: a jammed receiver runs at 38 percent for... | JamPush replaces the speed multiplier |
| E-329 | S2 | SIM | fixed | After the route, receivers wander: the soft-spot search re-picks among 15 cel... | soft-spot hysteresis |
| E-330 | S2 | SIM | fixed | The defense never adjusts to strength or formation: DL, LBs and the SS stand ... | StrengthOf / ShiftToStrength |
| E-331 | S2 | QBSTANCE | fixed | DL stance logic: the wide-9 TwoPoint branch needs | Edge flag from RushAssignment.Contain / BlitzAssignment.Edge, goal-line unit -> all DL ... |
| E-332 | S2 | SIM | fixed | False start, offside and encroachment are dice rolls at the snap with no geom... | visible 0.22 s flinch (BeginFlinch) |
| E-333 | S2 | SIM | fixed | Run reads use the play call: LBs and DBs know a run is a run (and who gets it... | 8-15% misread on the run read |
| E-334 | S2 | SIM | fixed | Pursuit is a perfect quadratic intercept at 0.985 vmax that ignores accelerat... | pursuit: 0.95 vmax, 0.2 s lag, aim error, build-up |
| E-335 | S2 | SIM | partial | No force/contain/edge-setting: contain nudges 0.9 yd laterally only for a def... | contain and force partly; cutback-lane overpursuit penalty is not modelled |
| E-336 | S2 | SIM | fixed | Stunts and twists do nothing: the partner slot is never read and the swap onl... | stunt roles |
| E-337 | S2 | SIM | fixed | Rush lanes: every free rusher beelines to the quarterback's current position;... | rush lanes spread |
| E-338 | S2 | SIM | wontfix | Left-handed kickers are generated but ignored (UI says Left, animation is rig... | K / P are generated right-footed (the RNG draw is kept so rosters do not change); the a... |
| E-339 | S2 | SIM | fixed | Directional punts are only an aim offset (no hang/distance trade-off, no coff... | directional and pooch punts with hang / distance trade (KickKind.Pooch) |
| E-340 | S2 | SIM | fixed | Punt coverage abandons all lanes and sprints at the landing spot | lane containment (IsPuntContain) |
| E-341 | S2 | SIM | fixed | Kickoff outside coverage men chase the ball spot, so containment disappears | LaneAssignment.Contain |
| E-342 | S2 | SIM | fixed | Kickoff coverage lanes are straight vertical segments with two waypoints | kickoff fan-out lanes and a 0.6 yd stagger |
| E-343 | S2 | SIM | fixed | Kick-return blocking: all 9 blockers get the same kind and target rule | varied return blocking |
| E-344 | S2 | SIM | fixed | Return wall rows are absolute yard lines (78 / 84), independent of the kick spot | relative return rows plus a wedge |
| E-345 | S2 | SIM | fixed | No special-teams penalties exist | RoughingTheKicker, RunningIntoKicker, KickCatchInterference |
| E-346 | S3 | SIM | fixed | Sim sets Action=Run / Sprint while the body is almost stationary (run-in-plac... | gait labels have a speed floor |
| E-347 | S3 | SIM | fixed | Body overlap in 10-11% of plays: bodies in the Normal state interpenetrate fo... | same-team overlap 0.60 -> 0.00 ticks, plays with overlap 123 -> 43 |
| E-348 | S3 | CONNECT | fixed | ContactImpact AgentA/AgentB mean different things per contact kind | ContactImpact has Hitter / Victim with HasRoles; the replay focuses on the victim of a ... |
| E-349 | S3 | CONNECT | fixed | Closing speed is passed in yd/s but the table is in m/s | Closing speed is converted from yd/s to m/s. |
| E-350 | S3 | SIM | fixed | Pre-tackle carrier speed is a hand-tuned x1.7 guess | (view hook) PlayEvent.Vel carries the pre-tackle velocity; ContactDirector must use it |
| E-351 | S3 | SIM | fixed | Vertical routes run past their last waypoint at full speed until the wall | vertical routes stop at y = 107 |
| E-352 | S3 | SIM | fixed | Screens are released on a fixed clock, whether or not the back has turned | screen thrown when the back is within 3.5 yd of his catch point |
| E-353 | S3 | SIM | fixed | Hail Mary: fixed 2.8 s hold, then a lob to the deepest man with no read | hail mary at 3.2-3.6 s to the receiver nearest the group centre |
| E-354 | S3 | SIM | fixed | Hold-time ceilings are long for the short drops (3-step 2.5 s, 5-step 2.95 s) | hold ceilings 2.15 / 2.65 / 3.05 s (+0.30 shotgun) |
| E-355 | S3 | SIM | fixed | Fatigue is cosmetic inside a play and nearly flat across plays: speed loses a... | FatigueDrive (accel and wind-up scale with energy), block and tackle scores -8 when gas... |
| E-356 | S3 | SIM | partial | Defensive motion response exists only for the man defender; the comment promi... | zone roll on motion exists (DefenseMotionResponse); safeties and zone linebackers do no... |
| E-357 | S3 | SIM | fixed | DL attacks the gap point once and then pursues, never holds the point or play... | hold-the-point and gap discipline |
| E-358 | S3 | SIM | fixed | Blitz: LBs start at the snap (Delay = 0) and creep at 0.35 vmax in the disgui... | blitz delays 0.18-0.32 s (LB), 0.35 s (DB) |
| E-359 | S3 | SIM | wontfix | No fake punts or fake field goals in the special-teams playbook | needs a scrimmage play from a kick formation, a call-rate model in PlayCaller / QuickSi... |
| E-360 | S3 | SIM | fixed | Punt downing is a flat coin flip and receiving-team muff has no recovery fight | downing probabilities by landing spot and arrival order (first arrivals only); muffs ar... |
| E-361 | S2 | SIM | fixed | Every one of the 22 players reacts to the snap with the same latency (robotic... | per-position reaction latencies |
| E-362 | S2 | CONNECT | fixed | Contact takes ignore GamePace.AnimationRate (0.88) and any future slow-down | Contact takes play at GamePace.AnimationRate; the session clock follows the playing clip. |
| E-363 | S2 | CONNECT | fixed | Throw / catch / hand-off clips are not on the game-pace rate that GamePace sa... | Catch, hand-off, juke, spin, stiff arm, truck, stumble and engaged plans are paced; thr... |
| E-364 | S2 | SIM | fixed | Delay of game is rolled as a random event at the snap (about 0.34 percent) ev... | delay-of-game rolled only when AutoSnap |
| E-365 | S2 | SIM | fixed | Snap-to-whistle averages 3.8 s run / 4.8 s pass against about 4.8 / 5.6 in th... | see headline table |
| E-366 | S2 | SIM | fixed | CPU/auto snap fires 0.6 s after the formation settles; no cadence, no read ti... | (view hook) cadence, 1.4-2.2 s snap count; MatchDirector sets Cadence |
| E-367 | S2 | SIM | fixed | Defenders break on the thrown ball after 0.16 s (elite) to 0.58 s: faster tha... | ball reaction 0.25-0.35 s |
| E-368 | S2 | SIM | fixed | Pass rushers move at full acceleration on tick 1 of the snap with no reaction... | rushers have a get-off delay (ReactUntil) |
| E-369 | S2 | SIM | fixed | Wrap window is 0.24 s from first contact to whistle (the carrier is already T... | (view hook) wrap window 0.9 s (1.2 s for big hits) before the whistle |
| E-370 | S2 | SIM | fixed | Punt snap-to-kick is about 1.2 s (NFL operation is 2.0-2.2 s) | punt operation 2.2 s (approach 1.10 + hold 1.10) |
| E-371 | S2 | SIM | fixed | Returner arrives at the catch on a constant run, no settle step | returner settle |
| E-372 | S2 | CONNECT | fixed | Catch animation starts after the ball is already caught (no lead-in for kicks) | Returners' catch starts 0.5 s ahead from the predicted fall time. |
| E-373 | S2 | CONNECT | wontfix | Kick approach path length does not match the clip's root travel (foot sliding) | Kick approach path versus clip root travel belongs to the kicker owner (QBSTANCE): the ... |
| E-374 | S3 | SIM | fixed | Pace between plays: the CPU snaps 0.6 s after the formation settles and the a... | (view hook) cadence (as E-366) |
| E-375 | S3 | CONNECT | fixed | Catch reach starts at ETA <= 0.34 s with a 0.22 s fade: hands not formed for ... | Catch reach window 0.5 s, fade floor 0.08 s, IK holds the hands on the ball. |
| E-376 | S3 | CONNECT | fixed | Requested 0.07 s contact fade-in is silently raised to 0.22 s | ActionAnimator.Play takes a fade floor; contact takes ask for 0.08 s with a 0.14 s fade... |
| E-377 | S3 | CONNECT | fixed | Session clock and animation clock can drift | The session clock is the playing clip's clock. |
| E-378 | S3 | CONNECT | fixed | Impact events (shake, rumble, hit sound) fire at session start, not at the hit | Impacts fire at the contact moment of the session. |
| E-379 | S3 | CONNECT | fixed | The referee whistle is used as the audible/flip/switch sound | MatchAudio.CallCue for audible / flip / switch (sub-agent). |
| E-380 | S3 | CONNECT | fixed | Post-play shot plan can run longer than the wind-down and delays the next play | Post-play shot budget trimmed to the wind (sub-agent). |
| E-381 | S3 | CONNECT | partial | A touchdown costs about 20 s of non-play time | Replay B shortened and capped (about 2 s saved per touchdown); the replay is not made s... |
| E-382 | S3 | GFX | fixed | Official signal variants use unseeded randomness | The gesture variant is seeded from PlayNumber x signal (same play, same gesture). The r... |
| E-383 | S3 | CONNECT | fixed | The whistle plays twice on penalty plays | One whistle owner per penalty play (sub-agent). |
| E-384 | S3 | SIM | fixed | The man in motion walks at 3.4 yd/s to +-16 yd and has no stop point at the f... | motion jog 4.5 yd/s (6 for the jet man), stops at limits +-11 yd |
| E-385 | S3 | SIM | fixed | A lock-up may be won/lost at 0.45 s and the first rush move can start at 0.46... | pass lock-up minimum 0.9 s, first rush move 0.7-1.4 s after contact |
| E-386 | S3 | CONNECT | partial | Swat/jump timing: the Jump clip also starts at the moment of deflection (Play... | The best defender's swat / jump starts ahead of the contact point (view); the sim's own... |
| E-387 | S3 | CONNECT | fixed | Kickoff tee ball height differs between view and launch point (visible pop) | One tee height constant for the view and the launch point. |
| E-388 | S3 | CONNECT | partial | FG and punt contact moments are guessed when the timing table has no entry | AnimKit logs a warning once for a kick clip without a measured contact moment; the tabl... |
| E-389 | S3 | GFX | fixed | LOD evaluated every 4th frame per player | PlayerLod.Update runs every frame (the work is a distance and a tan); the & 3 cadence i... |
| E-390 | S1 | CONNECT | fixed | The pocket camera loses the ball on deep passes, punts and kicks | Camera rises, pulls back and aims at the ball while it is airborne (sub-agent). Not don... |
| E-391 | S2 | CONNECT | fixed | Camera lurches to the carrier framing at every snap | Snap state focuses the QB (sub-agent). |
| E-392 | S2 | CONNECT | fixed | After a turnover the camera stays behind the old offense and the returner run... | View direction follows the returner after a turnover (sub-agent). |
| E-393 | S2 | CONNECT | fixed | Lateral clamp keeps sideline plays near the screen edge or out of it | Lateral clamp 20 m, FOV widens with lateral offset (sub-agent). |
| E-394 | S2 | CONNECT | fixed | Post-play tight shots are rigidly locked to the moving player | Tight shots smooth the subject and facing (sub-agent). |
| E-395 | S2 | GFX | fixed | No dedicated kick camera and the follow camera lags the ball | UserCameraInput.KickKind (1 kickoff, 2 field goal, 3 punt, set when the user kicks): ki... |
| E-396 | S3 | CONNECT | fixed | Camera can sit inside the goalposts/stadium behind the end zone (verify in play) | Camera z clamped 4 m behind the end line, raised near it, off the uprights (sub-agent). |
| E-397 | S3 | CONNECT | wontfix | Default vertical FOV 46 is a wide lens; the reference asks for a long lens | A long lens conflicts with StanceCameraTests and needs a play-test and a design call (s... |
| E-398 | S3 | CONNECT | fixed | Broadcast camera stacks three smoothers and clamps before the end zones | One smoother removed, clamp 52 m (sub-agent). |
| E-399 | S3 | CONNECT | wontfix | Broadcast camera swaps sidelines on every possession change | The side swap is the deliberate left-to-right broadcast convention (sub-agent). |
| E-400 | S3 | CONNECT | fixed | Pressing View blends the camera through the field | Cut blends between broadcast and user cameras (sub-agent). |
| E-401 | S3 | CONNECT | fixed | First-person camera lags the body and can disorient | First-person yaw driven from the body heading with a head-turn limit (sub-agent). |
| E-402 | S3 | CONNECT | wontfix | First-person anchor reads the head before the ground fit and IK run (verify i... | Execution order -200 is needed for the Cinemachine brain; verify in play (sub-agent). |
| E-403 | S3 | GFX | fixed | Hit camera shake is nearly invisible and there is no hit-stop | Shake MaxShiftMetres 0.06 -> 0.14, MaxShakeDegrees 0.45 -> 1.1, base trauma 0.30 (a nor... |
| E-404 | S1 | GFX | fixed | Player LODs are tuned so LOD0 never shows in gameplay; the main subjects sit ... | PlayerLod picks the level from the PROJECTED height of a 1.9 m player at a 1440 p refer... |
| E-405 | S2 | GFX | partial | LOD change is a hard SetActive swap with no cross-fade | Hysteresis is now 20 percent (was 10) on the size metric. A dithered cross-fade was not... |
| E-406 | S2 | GFX | fixed | Low/Medium tier LOD bias crushes silhouettes | Tier LOD bias floor 0.8 (Low 0.8, Medium 0.9, High 1.1, Ultra 1.4); the cost is cut on ... |
| E-407 | S2 | GFX | fixed | Shadow cascades waste 4 % of the range near the camera and the last cascade s... | The audit asks for 4 cascades at 110 m; measured 5-8 fps for that, so only Ultra gets 4... |
| E-408 | S2 | GFX | fixed | Directional and spot shadows use medium PCF filtering | directionalShadowFilteringQuality 2 (high, PCSS) in PixHDRP.asset, so angularDiameter 1... |
| E-409 | S2 | GFX | fixed | Floodlight player shadows are switched on and then switched off again by the ... | QualityTiers.ApplyScene no longer touches the Flood_* lamps; WeatherSystem.ApplyFloodSh... |
| E-410 | S2 | GFX | fixed | Night games: players only get shadows from a 90 lux moon, so they float | Night moon 90 -> 260 lux with shadow dimmer 0.6 (350 lamps-washed the fill: 260 keeps t... |
| E-411 | S2 | GFX | fixed | No contact shadow / blob under feet on low tiers; screen-space contact shadow... | FootShadow: a 0.95 m soft round blob (alpha 0.34, lighter under flat light) under every... |
| E-412 | S2 | GFX | fixed | Jersey numbers are small for the default camera | Back number +20 percent, front +25 percent (runtime scale of the placement in PixLook.S... |
| E-413 | S2 | GFX | fixed | Rain streaks die before they reach the ground | Streaks live 1.0-1.2 s from a spawn height of 18 m at 19 m/s, so they reach the grass a... |
| E-414 | S2 | GFX | fixed | Day sun is a fixed 58 degree high noon with a fixed azimuth | Day sun 42 degrees (was 58), azimuth 52, lux 126000 to keep the ground illuminance; it ... |
| E-415 | S2 | GFX | fixed | Dusk sun at 5.5 degrees throws 10x body-length shadows over the pitch | Dusk 14 degrees (was 5.5), 38000 lux, exposure 13.0; sinks to 60 percent over the game. |
| E-416 | S2 | GFX | wontfix | Crowd is static baked billboards with no animation or reaction | The crowd is merged, statically batched billboards on HDRP/Lit: a transform animation c... |
| E-417 | S2 | GFX | fixed | DLSS/FSR default Off, balanced quality, MSAA off: edges depend on 100 % TAA | Upscaler default is now "Auto" (4th choice): DLSS Quality (2) when the GPU reports DLSS... |
| E-418 | S2 | LOCO | fixed | Kick follow-through: the planted foot is not locked at contact | kicker hover frames 41 -> 0 (FG), 14 -> 0 (kickoff) |
| E-419 | S3 | GFX | fixed | Facing loop + clip facing table: 12 bone lookups x 22 players per frame | BodyHeading.MeasureThrottled: bones resolved once per animator, result refreshed at mos... |
| E-420 | S3 | GFX | handed-over | Wasted per-frame work: stride tracker with trim off, animator state polling | BodyMotion / LocoAnimator are in the LOCO track's active edit set (StrideMatchEnabled i... |
| E-421 | S3 | GFX | handed-over | Clip name strings and playable creation at every replan: GC and hitches at th... | Clip metadata cache and playable pooling are inside AnimKit / StanceClips / ActionAnima... |
| E-422 | S3 | GFX | handed-over | CullUpdateTransforms plus additive LateUpdate rotations accumulate off screen | Motion.Late / FitGround skip when culled: lives in SimAgentView / BodyMotion (LOCO track). |
| E-423 | S3 | GFX | fixed | Skinned updateWhenOffscreen = false with ragdoll/lying poses | Skinned bounds of every photo-player part padded by 0.8 m a side (a lying, diving or ra... |
| E-424 | S3 | GFX | wontfix | Ragdoll collides with the ground plane only, no feet/hands, no bodies | Contact/RagdollRig.cs is contact code (not this track's); foot / hand colliders, a soft... |
| E-425 | S3 | GFX | fixed | Ball at rest hovers 3 cm and rolls as a sphere | A resting ball is drawn 3 cm lower and lies flat along the way it last pointed instead ... |
| E-426 | S3 | GFX | fixed | Only the referee signals; other officials never gesture (dead code) | The crew shares the signals: first down = referee + line judge, incomplete = referee + ... |
| E-427 | S3 | GFX | fixed | The dead-ball signal and whistle animation are never triggered | OfficialSignal.DeadBall is fired for every whistle that has no other signal (not kickof... |
| E-428 | S3 | GFX | partial | Field markers are flat unlit strips 2 cm above the turf | Markers raised from 2.2 to 3 cm (no z-fight with the painted lines at grazing angles). ... |
| E-429 | S3 | GFX | fixed | Kicked ball orientation uses the flat (XZ) velocity: no nose pitch with the arc | Kick long axis follows the whole velocity (nose pitches with the arc), tumble about cro... |
| E-430 | S3 | GFX | fixed | Tier shadow distance only ever lowers; HDRP uses the volume value anyway | The weather volume's maximum shadow distance is the tier's (60 / 90 / 120 / 150, capped... |
| E-431 | S3 | GFX | fixed | No motion blur ever, although tiers advertise it | Weather volume: camera motion blur intensity 0.18 (max velocity 90, min 6), tier toggle... |
| E-432 | S3 | GFX | fixed | No depth of field in live play (flat image of the whole stadium) | Very subtle far-only depth of field in the live volume (manual mode, no near blur, 3 px... |
| E-433 | S3 | GFX | fixed | Footstep wear is stamped at the body root every 0.16 s, not at the feet | Wear is stamped at the foot bone that plants (descending then stopping, ankle below 17 ... |
| E-434 | S3 | GFX | fixed | Wear stamps are silently dropped when the queue is full | A full queue drops the oldest footstep, never a divot; 160 stamps per frame on High / U... |
| E-435 | S3 | GFX | fixed | Midfield logo re-rotates to the camera axis every play | The crest is oriented once per half (FaceCamera(cam, half)), no re-rotation between plays. |
| E-436 | S3 | GFX | fixed | Dirt is one soil colour with no kick, no cleaning and no sweat | Soil colour follows the surface (brown soil, darker when wet; black rubber grime on art... |
| E-437 | S3 | GFX | fixed | Ball has no flight cue (no trail / landing marker) and is shadow-only for vis... | Additive flight trail (0.35 s, alpha 0.25) on passes and kicks faster than 14 m/s above... |
| E-438 | S3 | GFX | fixed | Weather particles ignore time scale and scene lighting | Particles use scaled time (slow-motion replays keep rain slow, pause stops it); tint fo... |
| E-439 | S3 | GFX | fixed | Overcast shadow dimmer 0.45 leaves players with no grounding | Overcast shadow dimmer floor 0.7 and SSAO radius 1.0 under overcast / rain. |
| E-440 | S3 | GFX | fixed | Empty sky: no cloud layer or volumetric clouds | CloudLayer override in the weather volume (physically based sky presets), opacity 0.25 ... |
| E-441 | S3 | GFX | fixed | Film grain, vignette and bloom are on in the default grade (not "clean/bright") | Live grade: grain 0, vignette 0.12 (dusk / night 0.14-0.16), bloom 0.08 with threshold ... |
| E-442 | S3 | GFX | fixed | AA flips to FXAA for 3 frames after every broadcast cut | A broadcast cut resets the TAA history (HDCamera.Reset()), no FXAA flip. |
| E-443 | S3 | GFX | fixed | Pad shell colour lags a re-dressed player by up to 20 frames | PhotoPlayer.SetTeamColors syncs PhotoPadTint immediately; the poll interval is 60 frame... |
| E-444 | S3 | GFX | wontfix | LOS and first-down strips do not receive shadows | The strips use unlit materials (always readable under any light); lit transparent mater... |
| E-445 | S3 | GFX | fixed | SSR only reflects surfaces at smoothness 0.92+, so wet turf and puddles get n... | SSR override in the weather volume: min smoothness 0.7 in rain / overcast (0.92 dry). |
| E-446 | S3 | GFX | fixed | SSAO, SSR and contact-shadow tier toggles hit scene volumes only; the weather... | SSAO (0.9, radius 0.8 / 1.0), contact shadows (0.45 m), SSR, DoF and SSGI (Ultra) live ... |
| E-447 | S3 | GFX | wontfix | SSGI exists only on Ultra, the one tier with 1.4 LOD bias | SSGI was switched on for High (half-res override, fullResolutionSS off) and measured: i... |
| E-448 | S3 | GFX | fixed | Upscaler setup is skipped in the editor, so editor-captured audit renders nev... | The upscaler setup also runs in the editor when the command line has -pixUpscaler (asse... |
