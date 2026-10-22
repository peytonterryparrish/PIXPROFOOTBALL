# v0.6.0 playtest notes (user, 2026-10-21) → v0.7.0

Verbatim intent of the 31 notes, grouped by owning track. "continue with fixes and proceed to v0.9.0".

## Track A — Animation, locomotion, QB, stances, contact, ball sync (`anim_qb.md`)
4. QB stances all wrong: needs proper under-center, receiving the snap, standing in the pocket with the ball at the chest, proper throwing animations.
5. Players move weird and janky: proper running animations and clean-up.
6. Mocap needs a lot more clean-up, work and precision.
7. Animations vs actions must feel more simulated and calculated, so the game looks forgiving and convincing.
9. Let animations fully play out, then smooth transition animations so it looks realistic.
12. WR stances wrong while waiting for the snap.
13. Running with and without the ball: the right foot splays out to the side.
14. QB needs scramble-out-of-the-pocket animations.
15. Many more animations + clean-up to connect as many actions as possible; the QB doesn't move/operate like a convincing QB.
17. OL stances need to look more realistic and convincing.
18. MLB and CB idle defensive stances need to look natural.
22. Lots of bugs on tackles.
24. Animation blending/fluidity is the priority for the match engine.
25. Gameplay more simulated so animations perform as intended and flow into each sequence.
26. Players glide/float when running — they need weight; no levitating.
29. Ball always correctly in hand, especially in TD celebrations.
30. Out-of-sync actions and ball play — fix desynced actions and ball movement.

## Track B — Engine pacing, play end, kicks (`engine_pace.md`)
3. Play ended with no tackle / no obvious reason.
8. Gameplay a touch too fast.
28. Field goals and kickoffs too fast and not convincing.
(+ shared with A: 7, 25, 30 — the sim must give animations time: commit windows, no instant state flips.)

## Track C — Controls, HUD, playbook, menus, replay (`ui_controls.md`)
1. Can't see who I'm controlling on defense — cursor ring under the player missing.
2. Resolution doesn't fit the screen; things cut off at the bottom.
10. Hold LT to see routes on the field.
11/20/23. Playbook / call sheets and navigation like Madden 27, easier to navigate.
16. As QB: HOLD the receiver button = bullet pass, TAP = lob (swap of current).
21. Two logos in the middle of the field must not happen.
27. Replay shows no players, just a floating football.
31. Menus should feel like a modern AAA football game.

## Track D — Graphics (`graphics_v070.md`)
19. Graphics need improvement.
