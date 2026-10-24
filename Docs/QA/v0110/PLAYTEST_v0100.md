# v0.10.0 playtest notes (user, 2026-10-23) → v0.11.0

1. **Many match-engine bugs.** Find at least 100 things to fix and list them for the owner.
2. **Stances are broken:** players float, bodies are half missing, and the whole team disappears on kickoffs.
3. Keep working on the player models and their stances.
4. **Watch mode for now:** everything on the broadcast camera with no controller input. The owner wants to watch CPU vs CPU matches to judge the engine's realism and flow before controller input comes back.
5. **More mocap and much more cleanup.** Use plugins, products and tools. Ask the owner for any tools or assets needed so the skeletons, animations and mocap all look really good together.
6. **More simulation:**
   - Orchestrate more simulations, scenes and outcomes so the engine looks convincing and realistic.
   - Match flow must feel a lot better.
7. **Ball-carrier running:** the RB's movement with the ball must look like real running backs.
8. **QB animations are full of bugs.**
   - The QB is always in a square throwing stance, ball at the chest, looking and scanning, unless the user holds RT (sprint).
   - When he moves out of the pocket he stays in that stance.
   - For the CPU: same, unless the QB deliberately takes off to run.
9. **Animation flow, ball flight and receiver control:**
   - Smoother animations, and work on ball flight.
   - When the ball is thrown, receivers automatically track the ball's direction.
   - Users must not be able to move a receiver until he has the ball in his hands. Touching the stick during a throw currently ruins the pass.
10. **Real-life football reference.**
    - Real situations should happen in this game: big hits and really nice throws.
    - A realistic QB with correct footwork, especially in the pocket.
11. **More mocap cleanup:** feet, ankles and legs running with proper form.
12. **The match HUD is a mess.**
    - Pop-ups such as yards gained are off-centre, too big or placed all over.
    - The helper/button bar at the bottom can't be removed, is far too big, and blocks the camera and the play.
    - It must be minimizable and toggleable on and off.
13. **More sequences and a more simulated feel:** less arcade, so it feels like a proper football game.

Also still to do: the chosen menu direction #16 "Tile Hub Modern" (spec `D:\PIXFootball\Work\UI16\SPEC.md`). The owner's focus right now is the match engine.
