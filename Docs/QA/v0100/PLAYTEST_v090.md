# v0.9.0 playtest notes (user, 2026-10-22) → v0.10.0

The user wants ALL of these fixed before moving on (no College Network, no other new features), then a new build to play.

1. **Running looks like galloping.** The right foot, the ankle and the whole skeleton look wrong while running.
2. **Defensive logic is broken.** Players leave their zones and their coverage.
3. **Plays need to look like Madden plays.**
4. **QB sequence, fully animated and connected:**
   - The QB walks up to the line, gets low, puts his hand under center, takes the hike, secures the ball and drops back.
   - In the drop and the pocket, the chest faces north (downfield), the ball is held in both hands at the chest, and the head scans the field.
   - Scrambling needs transition animations into the scramble.
   - **No gliding or floating anywhere.** Many players still glide or float.
   - Every animation is connected, and an animation plays out before the next one starts. Today players often keep floating across the field while an animation plays.
5. **The match engine must feel simulated, orchestrated and choreographed.** Much of it needs choreography to look good.
6. **Calculated and coded, not physics.** Determine sequences before they happen, and pick the right animations for each situation so everything looks convincing and realistic.
7. **Kickoffs are out of sync with the ball.**
8. **Use more of the Draft Fever Bowl animations** the user provided, and keep cleaning them up (hands, feet, skeletons) so everything looks correct.
9. **Linemen and bodies:**
   - **OL stances:**
     - Tackles sit a little more upright, with an arm on the thigh.
     - Guards (left and right) sit lower, also with an arm on the thigh.
     - The center is down over the ball.
   - **DL bodies** look weird: misshapen and deforming, the waist looks wrong, and the head is "all the way up in the sky".
   - **All models and skeletons:**
     - Human proportions.
     - No twisting waists, backs, hands or arms.
     - The ball must not be stuck to the arm or forearm when celebrating or walking.
10. **25 menu UI directions, 3 screens each**, so the user can pick a direction.
    - Mix and match the user's UI packs, and add new ideas that suit a Madden-style pro football game: brushed metal or clean, but clearly football with "Madden Pro" vibes.
    - Research the Madden games' menus to hit that reference.
