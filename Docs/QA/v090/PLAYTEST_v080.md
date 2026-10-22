# v0.8.0 playtest notes (user, 2026-10-22) → v0.9.0 "Match Engine"

College Network is PAUSED. Its unfinished UI is parked on the local branch `wip/v0.9-college-ui`; its committed logic stays in main. Focus entirely on the match engine.

1. All kickers are right-footed and set up to the LEFT side of the ball.
2. Players still look like they float when they run.
3. The graphics could look more high-end.
4. A lot of stances are still wrong.
5. The QB needs proper movement and stances in all areas.
6. The match engine feels very unconvincing and not realistic at all, with a lot of animation issues.
7. Needs a lot of animation clean-up, and many more animations added and used. There are no proper dropbacks or proper QB stances; it all feels wonky.
8. Must feel more simulated, with more connections between animations. **Find 300 issues in the match engine, list them all, then fix them all.**
10. Game speed is still a touch fast (after v0.7 slowed it ~12% via `GamePace.Scale` 0.88): slow it further, naturally, keeping animations matched to speed.
9. Focus only on the match engine: more animations and clean-up, better stances for ALL positions, much more convincing throwing.

Specs to obey: `Docs/QA/REFERENCE_Stances.md` (QB mechanics, WR/CB, OL/DL, RB), `Docs/QA/REFERENCE_AllPro2K8.md`, the images in `Docs/Reference/Madden/`.
