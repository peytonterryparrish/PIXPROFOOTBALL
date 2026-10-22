# Animation sequence check (v0.8)

Real match views (graph + AnimKit plans + BodyMotion) driven at 30 Hz through scripted sequences by `AnimSequenceCheck`. Per step: the largest single-frame rotation change of 14 bones (degrees per frame; a pop shows as a spike far above the mean), the mean change, the largest jump of the clip layer weight (a smooth cross-fade moves it by at most 0.23 per frame (0.22 s minimum fade); the verdict threshold is 0.24), the lowest foot point (m) and the clip that played. Raw trace: `Logs/anim_seq.txt`.

| Scenario | Step | Clip | max deg/frame | mean deg/frame | max weight jump | lowest foot y | min leg extension | verdict |
|---|---|---|---|---|---|---|---|---|
| WR | stance | 2pstance_halfback_001_M | 8.8 | 2.1 | 0.17 | 0.06 | 0.85 | ok |
| WR | snap + get-off | 2pstance_halfback_001_M | 50.7 | 12.8 | 0.22 | 0.06 | 0.77 | ok |
| WR | route sprint | (locomotion) | 28.7 | 20.6 | 0.00 | 0.15 | 0.52 | ok |
| WR | catch on the run | CatchNormalBackCHigh | 30.5 | 17.1 | 0.22 | 0.14 | 0.55 | ok |
| WR | carry sprint | (locomotion) | 34.5 | 20.7 | 0.00 | 0.13 | 0.69 | ok |
| WR | juke left | (locomotion) | 45.5 | 26.8 | 0.00 | 0.09 | 0.71 | ok |
| WR | carry | (locomotion) | 40.4 | 22.9 | 0.00 | 0.13 | 0.67 | ok |
| WR | stop | (locomotion) | 35.1 | 10.6 | 0.00 | 0.09 | 0.69 | ok |
| WR | tackled (fall) | CollisionStand0degR | 14.8 | 4.7 | 0.22 | 0.06 | 0.89 | ok |
| WR | idle | CollisionStand0degR | 4.7 | 0.8 | 0.22 | 0.09 | 0.99 | ok |
| OL | 3-point stance | 3pstance_aggressive_002 | 20.0 | 3.2 | 0.17 | 0.05 | 0.61 | ok |
| OL | pass set (block) | BGS__BGS_OLM_ShuffleStand_RStance_Loop | 20.0 | 2.7 | 0.00 | 0.10 | 0.61 | ok |
| OL | engaged | DLineMoves__DLINE_SwimR_Succes_002_B | 25.3 | 1.9 | 0.00 | 0.05 | 0.97 | ok |
| OL | idle | DLineMoves__DLINE_SwimR_Succes_002_B | 27.6 | 4.2 | 0.22 | 0.04 | 0.99 | ok |
| DL | 3-point stance | DlineIdle3ptR | 20.4 | 3.1 | 0.17 | 0.05 | 0.79 | ok |
| DL | get-off rush | DlineTransition3ptRToRunFwd0deg | 26.7 | 13.9 | 0.12 | 0.15 | 0.67 | ok |
| DL | tackle wrap | TackleWrapFrontCHighDmo_A | 45.2 | 13.5 | 0.22 | 0.04 | 0.63 | ok |
| DL | idle | TackleWrapFrontCHighDmo_A | 46.0 | 9.9 | 0.22 | 0.07 | 0.78 | ok |
| QB | shotgun stance | BONES_QB_shotgun_ready_to_catch_loop_004 | 15.7 | 2.3 | 0.17 | 0.08 | 0.96 | ok |
| QB | drop back | BONES_QB_shotgun_ready_to_catch_loop_004 | 17.1 | 4.8 | 0.22 | 0.09 | 0.89 | ok |
| QB | throw | QB_pass_bullet_001_s02 | 37.3 | 10.4 | 0.22 | 0.09 | 0.94 | ok |
| QB | scramble | (locomotion) | 48.8 | 12.2 | 0.00 | 0.10 | 0.67 | ok |
| QB | idle | (locomotion) | 51.1 | 12.6 | 0.00 | 0.10 | 0.73 | ok |
| DB | press stance | BGS__BGS_CB_Idle_M2M_M | 14.0 | 2.3 | 0.17 | 0.08 | 0.92 | ok |
| DB | backpedal | BGS__BGS_CB_IdleToSprint_Forward_Transition | 15.9 | 7.4 | 0.14 | 0.11 | 0.83 | ok |
| DB | break and run | (locomotion) | 69.2 | 21.6 | 0.00 | 0.09 | 0.52 | ok |
| DB | interception | IntNormalBackCHigh | 28.4 | 14.0 | 0.22 | 0.13 | 0.51 | ok |
| DB | idle | (locomotion) | 54.5 | 12.3 | 0.00 | 0.09 | 0.78 | ok |
| K | stand at the spot | Player_Standing_Idle_2 | 5.4 | 1.4 | 0.17 | 0.09 | 0.95 | ok |
| K | kick | YAN__K_kickoff_kick_00_Rework | 33.8 | 10.1 | 0.14 | 0.02 | 0.52 | ok |
| K | idle | (locomotion) | 1.2 | 0.2 | 0.00 | 0.09 | 0.99 | ok |

Steps flagged: 0 of 31.
