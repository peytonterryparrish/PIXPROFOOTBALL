# Owner notes added to v0.12 (2026-10-24, mid-round)

1. **Robotic movement.** Many players move like robots. Make walking, running and sprinting feel realistic for everybody: nobody walks, runs or sprints like a robot.
2. **Bodies and pads.**
   - There are many pad bugs. Make the pads a little smaller.
   - Players should look realistic, like Madden 27 or real life. The Burrow model he supplied looked more lifelike than the bodies on the field now.
   - Replicate that model's look, with different sizes for the bigger players (OL, DL). Body, rig and look only: never the real player's face or likeness, no team or league marks (`Docs/QA/photoreal.md`).
3. **Body, head and helmet bugs.**
   - Hips and heads turn too far when players look left or right.
   - Parts are missing on the helmets.
   - Many other body bugs.

Plan: these become **wave 2** of v0.12, ahead of the gap-analysis candidates. Each track starts as soon as a wave-1 track frees a slot (at most 4 at once).
- **BODIES** covers note 2 and the helmet parts of note 3. It can start first, because it shares no files with wave 1.
- **NATURAL** covers note 1 and the head/hip turn of note 3. It starts after GAIT and MOBILITY finish, because it builds on their locomotion changes.
- A short read-only **body-bug audit** of the baseline dataset feeds both tracks.
