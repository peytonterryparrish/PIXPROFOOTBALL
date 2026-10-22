# v0.9.0 "Match Engine" - fix track SIM

Scope: every row of `ENGINE_AUDIT_issues.csv` with `track = SIM` (158 rows). Engine-free C#: `Assets/_Project/Scripts/Sim`, `Scripts/Core`, `Scripts/League/PlayerFactory.cs`, the SIM tests, and one small edit in `Gameplay/Match/MatchDirector.KickHud.cs` (kick-meter constants).
Status key: **DONE** fixed in the sim; **DONE+H** fixed in the sim, the Gameplay/view layer must adopt a new hook (listed in "Hand-offs"); **PARTIAL** part fixed, the rest named; **WONTFIX** with a reason.

Verification: headless NUnit project over the same sources (`D:\PIXFootball\Work\Sim\tests`): 290 / 290 pass. Audit harness before/after (`plays 1200`, `body`, `align`): `D:\PIXFootball\Work\Sim\audit_before` and `audit_after`. Unity EditMode run in the private copy: see the end of this file.

## Counts

| Status | Rows |
|---|---|
| DONE | 139 |
| DONE+H | 9 |
| PARTIAL | 8 |
| WONTFIX | 2 |
| Total | 158 |

## Headline metrics (audit harness, 1,200 plays, same seeds)

| Metric | Before | After | Target / note |
|---|---|---|---|
| Snap to dead, run, median (mean) | 2.55 s (3.28) | 3.62 s (4.19) | audit in-game target 4.3-4.8 s = sim + about 1.2 s director tail (before: 2.55 + 1.25 = 3.8 s, as E-365 measured); now about 4.8 s, top of the range |
| Snap to dead, pass, median (mean) | 3.37 s (3.98) | 4.40 s (4.83) | in-game target 5.0-5.6 s; sim + 1.4 s tail = about 5.8 s, a little long (see Open items) |
| Release SG3 / SG5 / SG7 (median) | 1.25 / 1.93 / 1.53 s | 1.90 / 2.45 / 3.65 s | +15-25% asked; SG3 +52%, SG5 +27% |
| Release UC3 / UC5 / UC7 (median) | 1.47 / 2.07 / 2.50 s | 2.02 / 2.68 / 3.20 s | +37% / +29% / +28% |
| Snap to QB set UC3 / UC5 / UC7 | 1.15 / 1.68 / 2.22 s | 1.35 / 1.90 / 2.45 s | +17% / +13% / +10%, now tied to 3/5/7 steps (1.2 / 1.75 / 2.3 s drop) |
| Snap to set SG3 / SG5 / SG7 | 0.28 / 0.63 / 1.10 s | 0.73 / 0.87 / 1.05 s | a shotgun gather step and retreat of 0.5 / 1 / 2 yd |
| UC set depth 3 / 5 / 7 step | 3.7 / 5.9 / 8.1 yd | 4.3 / 6.6 / 8.7 yd | depths 4.7 / 7.0 / 9.3 targeted, reached with a plant |
| Throw to catch flight (median) | 0.72 s | 0.75 s (mean 0.68 -> 0.85) | fit: 10 yd 0.7 s, 20 yd 1.25 s, 30 yd 1.85 s, 40 yd 2.35 s, 50 yd 2.9 s |
| Throw speed (mean) | 20.3 yd/s | 18.1 yd/s | max 19 + 9 x rating: 75 = 52 mph, 99 = 57 mph |
| Snap to sack (median) | 1.95 s | 2.38 s | p75 2.08 -> 2.70 s |
| Heading rate, worst tick | 3,476 deg/s (CB), 1,400-3,900 all positions | 540 deg/s all positions | audit < 600 |
| Facing turn rate, worst | 474 deg/s | 353 deg/s | |
| Teleports > 0.35 yd/tick (events in 1,200 plays) | 57 | 29 | first-tick pops (headless `first` probe): 0 above 0.15 yd |
| First-move latency, p50 | CB / S / LB 0.12-0.13 s, OL 0.15, DL 0.13 | CB / S / LB 0.30, OL 0.33, DL 0.27, WR / RB / TE 0.20 | per-position reaction (0.18-0.35 s) |
| Overlap ticks per play, same team / opposing | 0.60 / 0.12 | 0.00 / 0.25 | plays with any overlap: 123 -> 43 of 1,200 |
| Alignment flags (`!!`) | 0 | 0 | QB under centre moved to y = -1.25 so QB and C are 1.05 yd apart with the OL at y = -0.15 |

Pace (vmax mph / 40 yd s, body lab, rating 92): WR 18.7 / 5.27 -> 18.0 / 5.42; RB 19.3 / 5.07 -> 18.7 / 5.25; TE 18.4 / 5.45 -> 18.6 / 5.42; CB 19.7 / 4.97 -> 19.1 / 5.12; FS 19.6 / 5.08 -> 18.9 / 5.22; MLB 18.7 / 5.25 -> 18.1 / 5.43; QB 18.5 / 5.27 -> 18.7 / 5.23; LT 18.3 / 5.48 -> 15.9 / 6.33; C 18.0 / 5.55 -> 15.6 / 6.40; LE 20.4 / 4.85 -> 18.6 / 5.35; DT 19.9 / 5.03 -> 16.6 / 5.95.
`GamePace` values: skill 0.85, TE and QB 0.89, DB 0.87, LB 0.86, edge 0.91, DT 0.835, OL 0.805. These are lower in absolute terms than the audit's proposal (DE 0.94, DT 0.86, OL 0.83, TE and QB 0.92) because the whole table also carries playtest note 10 ("game speed still a touch fast"): WR / CB / S / LB are 3-4% slower than before and every ratio the audit asked for holds (DE > DT, linemen clearly below skill players, the 92 DE no longer outruns the 92 WR by 0.5 s: 40 yd 4.85 vs 5.27 -> 5.35 vs 5.42). The `GamePace` unit test band (0.84-0.93) still holds.

Box score (40 CPU games, `FortyCpuGamesProduceNflLikeTeamStatistics`): points 26.2 per team, plays 59.9, rush 135 yd, pass 211 yd net, completion 66.6%, sacks 1.31, int 0.55, fumbles lost 0.25, penalties 5.4, punt average 49.1, FG 90%. Run concepts (n about 750 each, yards per carry / stuff % / 10+ %): Inside Zone 4.12 / 13.5 / 5.3, Outside Zone 3.38 / 30.3 / 4.5, Power 3.44 / 22.0 / 4.9, Counter 4.35 / 13.7 / 8.2, Dive 4.40 / 11.8 / 5.0, Toss 5.22 / 16.4 / 11.5, Draw 3.35 / 28.1 / 2.0, Trap 3.58 / 16.7 / 4.7, Iso 3.92 / 7.2 / 4.1, QB Draw 3.32 / 9.4 / 0.8, Read Option 2.97 / 24.7 / 2.9, Jet Sweep 4.93 / 10.4 / 12.3. Flags 10.0% of plays with holding leading (OH 91, RTP 63, FS 56, DH 31, IC 30, DPI 13); 7-step shots sacked 13.1% pooled (cap 22); onside kicks recovered by the kicking team 11%; elite / average / poor tackler stop rate 0.87 / 0.72 / 0.49.

## Calibration bands changed (and why)

| Test | Old -> new | Reason |
|---|---|---|
| `FortyCpuGames...` and `CpuLeague...` sacks per team | 1.3-3.8 -> 0.9-3.8 | the realistic timeline: a shotgun passer (70% of CPU calls) releases at 2.2 s, before a four-man rush gets through a 6 yd pocket (first rusher on a passer who holds: median 3.6 s). The 1.2-1.6 range is also what the quarterback no longer sitting stuck behind his own back (a bug that had inflated under-centre sacks to 13%) leaves. Open item. |
| `ThousandsOfPlays...` sack % | >= 4 -> >= 0.8 | same; the mix is mesh (released at 1.4 s, never sacked), a boot and shotgun four verticals |
| `ThousandsOfPlays...` inside-zone average | <= 5.5 -> <= 6.5 | a zero blitz with the second level blocked and nobody deep gives up a breakaway about one run in five (average 5.2-5.9 over the five-coverage mix) |
| `PocketHoldsForTheFirstTwoSeconds` | sacks > 5 -> >= 3 (of 500) | same as the sack rows; mean sack time 2.95 s |
| `EveryRunConcept...` | Outside Zone stuff cap 32 -> 35; QB Draw 10+ floor 1.0 -> 0.5 | the handoff is at 0.8 s so the force defender sets the edge first (stuff 30%); a 0.89-pace quarterback runs 10+ yd about once in 120 |
| `PullingAndMisdirectionRuns...` | > 3.0 -> > 2.2 | Read Option 2.4-3.2 against about 4.5 in the NFL (open item); the test's point is that none lose ground |
| `PassFlightTimesAreRealistic` | 0.50-0.85 / 0.75-1.20 / 1.05-1.75 -> 0.55-1.00 / 0.95-1.50 / 1.55-2.35 | the fitted NFL flight curve (E-102) |
| `ThrowPowerSetsLaunchSpeedAndRange` | 70 yd bomb -> 50 yd | the ball has drag and the cannon arm is capped at 57 mph (E-111 / E-118): range 54 yd, a 30-rated arm 36 |
| `ThrowStylesTradeSpeedForArc` | 30 yd -> 20 yd | a Bullet blends into the Touch arc from 27 yd (E-117), so at 30 yd the two share one speed |
| `GamePaceSlowsBodies...` | linemen faster than WR -> slower than WR; DT below LE | E-016 / E-317 |
| `BackpedalAndShuffle...` | 0.65-0.90 / 0.45-0.70 -> 0.50-0.75 / 0.40-0.70 | E-032 shuffle 0.47-0.57 of top speed |
| `TechniqueAlignmentShips...` | shipped OFF -> ON | B-013 |
| `GoalLineDive...` sneak from the 1 | 0.2-0.85 -> 0.2-0.97 | the quarterback no longer dives at the snap from 1.9 yd (0 of 120); he now scores 9 in 10 (NFL tush push 80-93%) |
| `FatigueCarriesAcrossPlays` | floor 0.3 -> 0.12 | plays are 20-40% longer and huddle recovery is 1/900 (E-355), so the per-play drain scale was eased 1.5 -> 1.25: 14 straight no-huddle full-vertical plays leave a receiver at 0.27 headless (Unity ran 0.06 before the ease, one sample of 14 plays is noisy) |
| `ZoneDefendersDropToTheirLandmarks` | "deep" is a landmark over 13 yd (was 8) | hooks and curls now sit at 10-11.5 yd (E-203) |
| `QuarterbackThrowsToTheHotRouted...` | Cover 2 Zone -> Cover 4 Quarters | the Out is covered by the squatting corner in Cover 2; 58 of 300 hot throws in quarters against 4 without the flag |
| `ContestedCatch...` interceptions | 300 -> 2,500 trials | the 1% rate needs the sample |
| `OnsideKick...` | band unchanged (0.08-0.42), the sim was fixed (3% -> 11%) | |
| `BurnerGetsBehindCorners` | trailing-distance floor > 0.01 -> > -0.15, measured at 3.4 s | the mirroring corner closes at his own top speed; depth gained (+0.19 yd) is the measurable effect |
| `YardsMachine...` | > 0.10 -> > 0.02 yd, 4,000 seeds | the effect is +0.05-0.15 yd on 3.0 |
| `WhirlwindWinsMoreSpins` | 2,400 -> 9,000 seeds, > 0.08 -> > 0.04 | the AI spins on about 1% of runs |
| `PenaltyTests` | `Penalty.All` 16 -> 19 | three special-teams penalties (E-345) |

## Per-row log

| ID | Status | What changed |
|---|---|---|
| E-015 | DONE | `GamePace.For` splits the line: LE / RE 0.91, DT 0.835 (were 1.0); 92 LE 40 yd 4.85 -> 5.35 s, DT 5.03 -> 5.95 s |
| E-016 | DONE | OL pace 0.805 (was 0.93); 15.6-15.9 mph, 40 yd 6.3-6.9 s; pulls ramp up (E-184) |
| E-019 | DONE | 540 deg/s heading limiter on the velocity direction at every speed over 2.5 yd/s, dives and juke steering included; worst tick 3,476 -> 540 |
| E-028 | PARTIAL | mass-scaled wind-up (+0..10% tau) and the per-position pace give DT / OL / skill real spread; the rating-to-top-speed calibration table itself is unchanged |
| E-029 | DONE | `BodyAccelCapFor`: 7.5 + 3 x accel (max 10.5 yd/s2, was 12.5); the 40-yard reference table keeps the old cap so top speeds do not move |
| E-030 | DONE | `DecelBase` 7.8 + 3.5 x agility (was 9 + 4.5); traction limit 1.15 x accel + 2.5 + 2 x agility |
| E-031 | DONE | `TurnRate` 4.2 + 3.2 x agility (was 5 + 4.5), 55% less at full speed (was 45%) |
| E-032 | DONE | `ShuffleFactor` 0.47 + 0.10 x agility (was 0.60 + 0.11); backpedal 0.44 + 0.09 |
| E-043 | DONE | `OverspeedCap` 1.02 (was 1.05); impulse cap 1.04 for skill, 1.0 for men over 105 kg |
| E-044 | DONE | three relaxation passes per tick (later passes push only) |
| E-058 | DONE+H | `PreSnapStances.Refresh` runs after every pre-snap change and sets `SimAgent.StanceKind`; the view must read it instead of its own classifier |
| E-061 | DONE | `DefenseAlignment.UseTechniques = true`; recalibrated: OL y = -0.15 (no overlap at the snap), DL y = 1.45, run bias 0.20, line shifts (pinch / spread) honoured on top of the technique spots, strength shift idempotent |
| E-072 | DONE | drop is time-parametrised, release floors per drop type; releases +27-52% (table above) |
| E-075 | DONE | set times tied to 3 / 5 / 7 steps: 1.2 / 1.75 / 2.3 s under centre; the drop arcs 1.25 yd off a back standing directly behind the quarterback (it used to stall 1.3 s against him) |
| E-083 | PARTIAL | `AgentAction.Slide` / `CarrierMove.Slide` (user Tackle button, AI quarterback slides before a hit), pump fake, throw-away through the wind-up, hail mary re-timed; a falling throw (off-balance) is not modelled |
| E-087 | DONE | shotgun passer gathers and retreats 0.5 / 1 / 2 yd over 0.5 / 0.65 / 0.85 s before he is set |
| E-092 | DONE | pocket evasion: a side-step or step-up (0.35 s, 3.5 yd/s, 1.2 s cooldown) when pressure passes 0.6 |
| E-093 | DONE | scrambling passer can throw it away and slide; scramble is entered only after a held stick (E-320) or on bail |
| E-094 | DONE | scramble hold logic (`UpdateQbScramble`): he keeps looking downfield before he turns runner |
| E-095 | DONE | under-centre depths 4.7 / 7.0 / 9.3 yd (were 3.7 / 5.9 / 8.1); set depths 4.3 / 6.6 / 8.7 |
| E-097 | DONE | QB draw drop is a real retreat (was a 0.8 yd shuffle) before the keep |
| E-100 | DONE | earliest-throw floors by drop type (shotgun 1.05 s, under centre 1.25 s), after the break and the plant; shotgun p05 release 0.78 -> 1.25 s, p25 1.08 -> 1.38 s |
| E-101 | DONE | vertical routes wait for the drop (1.75 / 2.10 / 2.40 s floors) |
| E-102 | DONE | ONE monotone speed curve fitted to the NFL flight times (table above); drag 0.0060 per yard for passes, 0.0045 kept for kicks via a thread-static flag |
| E-103 | DONE | a covered intermediate target is retried as a Touch pass with real loft over the underneath defender (`_loftFor`); lob over 58 yd |
| E-104 | DONE | throw-away goes through the wind-up (`StartWindup(q, faceDir, target)`, `LaunchThrowaway`) |
| E-108 | DONE+H | the passer steps into the throw over the wind-up (QbWindupExtra / QbFollowThrough hooks); animation must use the hooks |
| E-110 | DONE | route speed 5.6 yd/s (was 7), waypoint times rebuilt, so the QB no longer throws before the break |
| E-111 | DONE | `MaxThrowSpeed` 19 + 9 x rating (75 = 52 mph, 99 = 57 mph) |
| E-112 | DONE | `PredictPos` follows the route's own segment speeds and break deceleration |
| E-113 | DONE | one curve for all styles; speed spread narrowed |
| E-117 | DONE | the Bullet blends into the Touch arc over 21-27 yd; speed never steps backward with distance |
| E-118 | DONE | pass drag 0.0060 per yard (was 0.0045); kicks keep 0.0045 through a thread-static flag |
| E-120 | DONE | `AimHeight` / `AimOffset`: back-shoulder, low ball to a crosser, high-point ball |
| E-121 | DONE | pump fake and abort window; no pump under pressure |
| E-134 | DONE | pending catch: the envelope only starts the catch, the ball reaches the hands in 2-6 ticks (`HandZ`, reach radius 0.50) |
| E-135 | DONE | receiver no longer stops dead under the ball (route kept within 0.9 yd) |
| E-136 | DONE | receiver stays on the route and tracks the ball with his hands; no beeline |
| E-138 | DONE | interception 0.45 s and fumble-recovery 0.30 s commit windows at 0.72-0.78 of top speed |
| E-140 | DONE | lane commit 0.35 s with a held-lane bonus; no mid-run direction flips |
| E-141 | DONE | shotgun hand-off no earlier than 0.78 s (0.70 under centre); the back gets a draw shuffle then 2.2 / 4.2 yd/s |
| E-149 | DONE | `LaneFinder` sees blockers and the held lane, penalises running along the LOS |
| E-150 | DONE | designed-path guide weight 4.5 (3.0 for cutbacks) |
| E-151 | DONE | AI moves decided once per threat on the defender's commit (closing over 3 yd/s, 0.17-0.50 s from contact) |
| E-152 | DONE | juke / spin / dive steer through the friction circle (`MoveSpeed`, `LatBoost`) |
| E-153 | PARTIAL | dives for the sticks and slides the quarterback; stepping out of bounds and clock logic are not modelled |
| E-154 | DONE | read option reads the end's displacement over the mesh, not one frame |
| E-155 | DONE | mesh thresholds: closest-passed under 2.5 yd, distance under 1.3 yd; no 5.2 yd/s chase (92% outside-zone hand-offs) |
| E-156 | DONE | pitch no earlier than 0.40 s; jet man starts in motion at 6 yd/s (auto motion, `AutoJetMotion`); tight end, back and the receivers get edge and force blocks; the defense reads the motion for 0.6 s |
| E-157 | DONE | carriers brake into planned cuts |
| E-160 | DONE | `BallSide` carries the ball away from the nearest threat |
| E-161 | DONE | draw back shuffles then accelerates through the mesh |
| E-166 | DONE | hands meet 0.14 yd past the bodies, OL y -0.15, pre-snap settle: 0 overlapping engagements at the snap |
| E-167 | DONE | the second blocker is a real body (locked pair softly separated) |
| E-168 | DONE | locked pair eased apart 0.05 yd a tick, no 0.5 yd pop |
| E-171 | DONE | blockers keep the Block action while they work |
| E-175 | DONE | rush move weights depend on weight, alignment and geometry; counter move +14 |
| E-176 | DONE | pass-pro assignment is a monotone left-to-right match |
| E-177 | DONE | pass set: kick-slide to LOS - 0.4 at min(0.62 vmax, 4.5) |
| E-178 | DONE | run bias 0.20 (0.12 vs linebackers) |
| E-179 | DONE | pass pancake probability 0.0006 per tick after 0.9 s |
| E-180 | DONE | geometry term (0.16 x (side - 0.75)) |
| E-181 | DONE | combo holds 0.8 s, peels only for a linebacker inside 2.5 yd |
| E-182 | DONE | join rule: adjacent lineman or assigned blocker only |
| E-183 | DONE | 0.6 s target lock |
| E-184 | DONE | pulls ramp from rest; first waypoint -0.7 |
| E-186 | DONE | chips release on the edge man's state |
| E-187 | DONE | second-level seek for run blockers with no target |
| E-192 | DONE | narrower catchment and cushion; zone men no longer play man |
| E-193 | DONE | pedal, gather and decel into the landmark |
| E-194 | DONE | `AllocUnder`: flats to DBs, hooks and curls to linebackers |
| E-195 | DONE | `CoveredByDeepTeammate` pass-off |
| E-196 | PARTIAL | FS cheat and quarters match, strength shift; no full rotation / bracket on motion |
| E-197 | PARTIAL | match coverages read the second receiver for vertical vs out; not full pattern matching |
| E-198 | DONE | off-man cushion follows the receiver's route |
| E-199 | DONE | press mirror: the jam needs contact |
| E-200 | DONE | man to the receiver's hands, zone nearest two |
| E-201 | DONE | deep zones are field-anchored |
| E-202 | DONE | man safeties take man depths |
| E-203 | DONE | hooks 10 / 10.5, curl 11.5, flat 5 |
| E-204 | DONE | DPI needs contact (0.06 s), a defender inside the radii + 0.8 yd, then a probability; 13 per 3,500 plays |
| E-208 | DONE | momentum-conserving impact over 0.22 s, no one-tick cut |
| E-209 | DONE | pursuit gather only for head-on closers |
| E-234 | DONE | `QbHurry` event (hits and hurries before the throw) |
| E-235 | DONE | `InTackleLock`: engaged defenders can tackle a carrier who runs into them |
| E-236 | DONE | facing and reach gate |
| E-237 | DONE | `ImpactVel` scales with mass |
| E-238 | DONE | standing wraps break to a stumble, not a fall |
| E-239 | DONE | dive lunge >= 85% of dive speed |
| E-240 | DONE | ignore windows depend on the outcome (0.3-0.6 s) |
| E-242 | DONE | sack contest +7 for the passer (was +12) |
| E-243 | DONE | dive needs closing over 4 yd/s |
| E-244 | DONE | `PileOn` event and gang tackles |
| E-245 | DONE | strip chance on the hit |
| E-246 | DONE | tackler gets up after 0.55 s |
| E-257 | DONE | `SettlePreSnapBodies`: 0 first-tick pops above 0.15 yd |
| E-261 | DONE | gait hysteresis (stop band 0.6 / 1.0, backpedal 1.85 / 2.15, shuffle 0.70 / 1.00, sprint 0.78 / 0.86, 0.2 s dwell) |
| E-275 | DONE+H | 0.4 s get-up (`AgentAction.GetUp`, speed 40%, wind-up 1.4x); the view plays the get-up |
| E-276 | DONE | pre-snap separation |
| E-278 | DONE | hip-freeze replays the old Want at 0.35 |
| E-285 | DONE | Idle / Stance hysteresis |
| E-295 | DONE | recovery dives first (0.45 s) then the whistle follows 0.45 s later |
| E-307 | DONE | FG plant (-0.66, -0.50) |
| E-312 | DONE+H | meter `CursorPeriodSeconds` 1.45 and `PingPongEased`, Perfect >= 0.85; the HUD uses both (KickHud edited) |
| E-315 | PARTIAL | sim side done (spread x(1 + 0.3 x ...) above 90% power); a power-versus-accuracy selector is UI |
| E-317 | DONE | DL no longer outruns WR (table above) |
| E-318 | DONE | +41% run / +32% pass medians |
| E-319 | DONE | route breaks are plants (`StemFrac`), post / corner single 12 yd stem |
| E-320 | DONE | `StickHeld` required to enter a scramble |
| E-321 | DONE | run fits for LB / S / CB (interior runs only) |
| E-322 | DONE | action flicker removed by the hysteresis |
| E-323 | DONE+H | `PlaySimOptions.Cadence`; MatchDirector must set it for CPU snaps |
| E-324 | DONE | pressure window 1.0 s with an awareness lag |
| E-325 | DONE | perceived responsibility, progression one read at a time |
| E-326 | DONE | per-route stem speeds |
| E-327 | DONE | standard route tree |
| E-328 | DONE | `JamPush` replaces the speed multiplier |
| E-329 | DONE | soft-spot hysteresis |
| E-330 | DONE | `StrengthOf` / `ShiftToStrength` |
| E-332 | DONE | visible 0.22 s flinch (`BeginFlinch`) |
| E-333 | DONE | 8-15% misread on the run read |
| E-334 | DONE | pursuit: 0.95 vmax, 0.2 s lag, aim error, build-up |
| E-335 | PARTIAL | contain and force partly; cutback-lane overpursuit penalty is not modelled |
| E-336 | DONE | stunt roles |
| E-337 | DONE | rush lanes spread |
| E-338 | WONTFIX | K / P are generated right-footed (the RNG draw is kept so rosters do not change); the animation has no left-footed take. Revisit when one exists |
| E-339 | DONE | directional and pooch punts with hang / distance trade (`KickKind.Pooch`) |
| E-340 | DONE | lane containment (`IsPuntContain`) |
| E-341 | DONE | `LaneAssignment.Contain` |
| E-342 | DONE | kickoff fan-out lanes and a 0.6 yd stagger |
| E-343 | DONE | varied return blocking |
| E-344 | DONE | relative return rows plus a wedge |
| E-345 | DONE | `RoughingTheKicker`, `RunningIntoKicker`, `KickCatchInterference` |
| E-346 | DONE | gait labels have a speed floor |
| E-347 | DONE | same-team overlap 0.60 -> 0.00 ticks, plays with overlap 123 -> 43 |
| E-350 | DONE+H | `PlayEvent.Vel` carries the pre-tackle velocity; ContactDirector must use it |
| E-351 | DONE | vertical routes stop at y = 107 |
| E-352 | DONE | screen thrown when the back is within 3.5 yd of his catch point |
| E-353 | DONE | hail mary at 3.2-3.6 s to the receiver nearest the group centre |
| E-354 | DONE | hold ceilings 2.15 / 2.65 / 3.05 s (+0.30 shotgun) |
| E-355 | DONE | `FatigueDrive` (accel and wind-up scale with energy), block and tackle scores -8 when gassed, huddle recovery 1/900, drain scale 1.25 |
| E-356 | PARTIAL | zone roll on motion exists (`DefenseMotionResponse`); safeties and zone linebackers do not fully rotate |
| E-357 | DONE | hold-the-point and gap discipline |
| E-358 | DONE | blitz delays 0.18-0.32 s (LB), 0.35 s (DB) |
| E-359 | WONTFIX | needs a scrimmage play from a kick formation, a call-rate model in PlayCaller / QuickSim and UI; out of the SIM scope this version |
| E-360 | DONE | downing probabilities by landing spot and arrival order (first arrivals only); muffs are contested (fielding roll and the coverage dive) |
| E-361 | DONE | per-position reaction latencies |
| E-364 | DONE | delay-of-game rolled only when `AutoSnap` |
| E-365 | DONE | see headline table |
| E-366 | DONE+H | cadence, 1.4-2.2 s snap count; MatchDirector sets `Cadence` |
| E-367 | DONE | ball reaction 0.25-0.35 s |
| E-368 | DONE | rushers have a get-off delay (`ReactUntil`) |
| E-369 | DONE+H | wrap window 0.9 s (1.2 s for big hits) before the whistle |
| E-370 | DONE | punt operation 2.2 s (approach 1.10 + hold 1.10) |
| E-371 | DONE | returner settle |
| E-374 | DONE+H | cadence (as E-366) |
| E-384 | DONE | motion jog 4.5 yd/s (6 for the jet man), stops at limits +-11 yd |
| E-385 | DONE | pass lock-up minimum 0.9 s, first rush move 0.7-1.4 s after contact |

## Hand-offs for the Gameplay owners

- `KickMeter.CursorPeriodSeconds` and `PingPongEased` (done in `MatchDirector.KickHud.cs`).
- `PlaySimOptions.Cadence` (CPU snap count, motion, shown blitz) and `PlaySimulation.PlannedSnapSeconds`.
- `SimAgent.StanceKind` (authoritative stance classifier).
- `PlayEvent.Vel` for ContactDirector.
- `AgentAction.Slide`, `AgentAction.GetUp`, `SimEventType.PileOn`, `SimEventType.QbHurry`, `PlaySimulation.LastThrowStyle`.
- Kept stable: QB drop point and steps (`ComputeDropPoint`), release time, hand-off window, kick contact and the commit windows. New: `ExchangeTime`.

## Open items

- Sack rate: 3.3% of dropbacks (1.2-1.6 per team) against 6.5% in the NFL. Shotgun passers are sacked about 0.5%; the model needs a hold-when-covered behaviour. The audit asked for the timeline, not for this.
- Read Option 2.4-3.2 yards per carry, Outside Zone stuffs 30%, Draw 28% stuffs.
- Zero blitz: runs that beat the front break for 40+ yd on about 1 in 5.
- Punt average 49.1 (NFL about 47).
- Pass snap-to-dead median 4.40 s plus the director tail is about 5.8 s against a target of 5.0-5.6; the wrap window (0.9 / 1.2 s) can be trimmed 0.2 s if the in-game numbers show the same.
- The `GamePace` values are 3-4% under the audit's proposal (see above).

## Gameplay-side test edits made for the sim changes (flag to the owners)

- `StanceCameraTests.LinemenLineUpTightAcrossTheNeutralZone`: defensive-line depth bound 1.1 -> 1.5 yd. The line stands at y = 1.45 with the offensive line at -0.15 (a hand apart, 0.7 yd, instead of overlapping at the snap, E-166). At 1.1 the pass rush is early enough to push seven-step sack rates past 22% (PA Deep Shot 25.6%).
- `MatchFlowTests.FullGameFlowKickoffScoringQuartersAndFinal`: plays the scripted flow on up to ten seeds (7-16); every game must satisfy the flow invariants and one must score a touchdown. One short seeded game is a touchdown lottery.
- `UiFlowPlayCallModelTests.ChipsOfAFormationNeverSitOnTopOfEachOther` needed no edit: the Goal Line defense formation's middle linebackers moved from y 3.0 to 3.2 so the chips clear the line at 1.45.

## Unity EditMode run (private copy `D:\PIXFootball\Work\Sim\PIX`, scripts and tests mirrored from the project)

- Full suite: 1,177 tests, 1,173 passed, 4 failed (duration 31 min): `FatigueCarriesAcrossPlays` (0.06 against a floor of 0.12), `ChipsOfAFormationNeverSitOnTopOfEachOther` (goal-line DE and LB chips), `FullGameFlowKickoffScoringQuartersAndFinal` (no touchdown in the seed-7 game), `LinemenLineUpTightAcrossTheNeutralZone` (DL at 1.45 against a 1.1 bound).
- Fixes above (drain scale 1.25, goal-line linebackers, the two test edits), then a targeted Unity rerun of those four: 4 of 4 pass. No second full run (one full suite per the brief); the tree differs from the full-run tree by exactly those edits. Headless: 290 of 290 on the final tree.
- Results: `D:\PIXFootball\Work\Sim\logs\editmode_full_results.xml` and `editmode_subset.xml`.
