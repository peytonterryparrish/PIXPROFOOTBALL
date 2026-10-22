# Prompts for Claude Code: PIX Football (Unity)

Two prompts are below. **Prompt 1** builds the game. **Prompt 2** asks Claude to explain how it would build it, covering the match engine feel and the graphics pipeline. Paste each one into Claude Code as a separate message.

---

## PROMPT 1: Build the Game

```
You are the lead engineer, technical artist, and game designer on a new American football video game built in UNITY. Treat this as a long-running, multi-version project inside this repository.

=====================================================================
STEP 0: BEFORE YOU WRITE ANY CODE
=====================================================================
1. Read this whole prompt.
2. Then STOP and ask me numbered clarifying questions about anything that is unclear, ambiguous, or that you need me to decide (target platform, Unity version, scope, art sourcing, controls, and so on). Group the questions by topic. Wait for my answers before you start building.
3. After I answer, write a short design summary and a version roadmap, and ask me to approve them.
4. Only after I approve, start on v0.1.0.

Throughout the project, whenever something is unclear, ask me. Do not guess on major design decisions.

=====================================================================
1. PROJECT IDENTITY
=====================================================================
- Engine: Unity (latest LTS, e.g. Unity 6 LTS) using HDRP (High Definition Render Pipeline). Language: C#.
- Target: PC first (Windows), with gamepad as the primary input (Xbox/PlayStation controllers through the Unity Input System). Keyboard is supported as a secondary input.
- Working title: "PIX Football". Championship game: the "PIX Bowl".
- GAMEPLAY FEEL: modeled on the match engine and presentation style of ESPN NFL 2K5 (2004).
- VISUAL LOOK: modeled on the PS3-era look of NCAA Football 14 (2013), pushed further: high resolution, gritty, high-definition textures, with models and assets made in Blender 3.x.
- LEGAL/IP RULE: use only original names, logos, teams, players, stadiums, and branding. Do not use ESPN, NFL, NCAA, EA, 2K, Heisman, or any real school/team trademarks in game content. Real-world games are references for FEEL and LOOK only. Use original placeholders where needed (for example, an original college player-of-the-year trophy name for the Road to Heisman mode; "Road to Heisman" is only my working title).

=====================================================================
2. MATCH ENGINE: MAKE IT FEEL LIKE ESPN NFL 2K5
=====================================================================
Recreate these qualities:
- Weight and momentum: players have mass, acceleration curves, and believable cuts. No instant direction changes. Big players feel heavy, and fast players feel explosive.
- Physics-driven tackling: tackles are context-sensitive and blend animation with physics (wrap-ups, shoestring tackles, gang tackles, pile-ons, stumbles, and broken tackles based on ratings, angle, and momentum).
- Smart AI: zone and man coverage that reacts to routes, linemen engaging in 1-on-1 blocking battles, pursuit angles, and believable QB reads.
- Passing: lead passing, touch and bullet throws, catch-in-traffic animations, tipped balls, and interceptions resolved by ball position, not by dice rolls.
- Running game: lead blockers, hole reading, juke, spin, stiff-arm, truck, and a hurdle/dive button.
- Play calling: a full offensive and defensive playbook system (formations > sets > plays), audibles, hot routes, defensive line and linebacker shifts, and a coach-AI play caller for CPU teams.
- A first-person / player-locked camera option (in the spirit of 2K5's first-person mode) plus classic broadcast and behind-the-QB cameras.
- Broadcast presentation: TV-style overlays, replays, an original commentary-style text/audio hook system, halftime and postgame highlight shows, stat graphics, and sideline reactions.
- Rules: full American football rules (downs, penalties, the clock, timeouts, challenges, two-point conversions, overtime). Keep college and pro rule variants as data-driven rule sets.
- Ratings-driven outcomes: every action reads player attributes (speed, strength, awareness, catching, throw power and accuracy, tackling, block shedding, and so on).
- Simulation: a fast "sim game" engine that produces realistic box scores, used for CPU vs CPU games in seasons.

=====================================================================
3. GRAPHICS: MAKE IT LOOK LIKE NCAA 14 ON PS3, BUT HIGHER FIDELITY
=====================================================================
- HDRP with physically based materials, realistic lighting, volumetric fog, stadium floodlights, day/dusk/night/rain/snow weather, and wet and muddy surfaces.
- Gritty, high-resolution textures (2K-4K): grass wear and turf scuffing, dirt and grass stains that build up on uniforms during the game, jersey mesh, helmet scratches and decals, and sweat/skin shaders.
- Player models built in Blender 3.x: game-ready topology, LODs, PBR texture sets, and modular gear (helmets, facemasks, visors, gloves, sleeves, arm bands, cleats).
- Stadium atmosphere: animated crowds (impostors and LOD), student sections, bands, cheerleaders, mascots, sideline crews, banners, and camera flashes.
- Cinematic camera work: broadcast cameras, depth of field on replays, motion blur, and color grading in the style of 2010s college football broadcasts.
- An art pipeline document: Blender 3.x export settings (FBX, scale, axis), naming conventions, texture packing, and import presets in Unity.
- Until final art exists, use clean placeholder assets (capsules, greybox stadiums) so gameplay development is never blocked by art.

=====================================================================
4. GAME MODE 1: CAREER MODE (ENDLESS FRANCHISE)
=====================================================================
- An endless, forever career. It never ends, and seasons keep rolling.
- I take control of an existing, pre-built pro franchise in a fictional league and manage it to win as many PIX Bowls as possible.
- All players are fictional, procedurally generated "regen" players. Retiring players are replaced by generated prospects, so the league sustains itself forever.
- Management: depth chart, roster (53-man style), contracts, salary cap, free agency, trades, re-signings, injuries, player progression/regression, aging, retirement, coaching staff, and team morale.
- History: a record book, PIX Bowl trophy case, Hall of Fame, season awards, and franchise legacy score.

4a. COLLEGE NETWORK
- A full college football season is generated and simulated each year alongside the pro season, with fictional schools, conferences, rankings, bowl games, and a playoff.
- I can browse schedules, standings, stats, and box scores, and WATCH college games live (playable in spectator mode or condensed highlights).
- Scouting: assign scouts and spend scouting points to reveal hidden ratings. Prospect ratings start fuzzy and get more accurate as I scout.
- I can HIGHLIGHT/STAR prospects I like and add them to a personal big board.

4b. DRAFT HOUSE (OFFSEASON DRAFT EVENT)
- The draft is a real in-game event, not a menu: a stage, a podium, a studio desk with analysts, a crowd and fans reacting (cheers/boos), team tables, and a pick clock.
- Animations: the commissioner announcing picks, the player walking on stage, the jersey and cap reveal, handshakes, and the photo op.
- Trade-up/trade-down offers during the draft, CPU teams drafting by need and best player available, and live draft grades.
- After the draft, I sign the drafted players (rookie contracts) through a signing cutscene or screen, and they join my roster.

=====================================================================
5. GAME MODE 2: ROAD TO HEISMAN (WORKING TITLE)
=====================================================================
- I create one player who is a college FRESHMAN and play through 4 college years (freshman to senior).
- I choose a position, a school (from offers based on my recruiting rating), and an archetype.
- Each year: practice, depth chart battles, games where I only control my player (player-locked camera), stats, team ranking, media and hype, and skill progression.
- Award race: a season-long trophy race with weekly rankings of the top candidates.
- WIN CONDITION: I WIN the mode only if, by the end of my career, all three are true:
  1. My team is ranked #1,
  2. I win the player-of-the-year trophy, AND
  3. I am drafted / rated as the #1 overall prospect.
- If I miss any of these, I LOSE. Show a results screen with where I placed in each category (final team rank, trophy vote placing, draft rank), a career summary, and a "Try Again" option.
- Optional: a legendary player from Road to Heisman can be imported into Career Mode as a draft prospect.

=====================================================================
6. ARCHITECTURE AND ENGINEERING RULES
=====================================================================
- Clean folder structure: /Assets/_Project/{Scripts,Art,Animations,Audio,Prefabs,Scenes,Data,UI}.
- Data-driven design: ScriptableObjects for ratings, playbooks, rule sets, teams, and schools. Use JSON or SQLite for save files.
- Separate systems: MatchEngine, PlayerController, AI (Offense/Defense/Coach), Physics/Tackling, Animation (Animator + root motion; evaluate motion matching), Presentation/Broadcast, Simulation, Franchise, CollegeNetwork, Draft, RoadToHeisman, Generator (regen players), UI, Save/Load.
- Deterministic sim with a seed so seasons can be reproduced and tested.
- Unity Test Framework: write edit-mode tests for sim, contracts, draft logic, and win conditions.
- Performance goal: 60 FPS at 1440p on a mid-range gaming PC. Profile regularly.
- Keep a .gitignore for Unity, use Git LFS for large binary assets (.fbx, .png, .psd, .wav, .blend), and commit often with clear messages.

=====================================================================
7. VERSIONS, PATCH NOTES, AND GITHUB RELEASES (REQUIRED)
=====================================================================
- Use Semantic Versioning (v0.1.0, v0.2.0, ... v1.0.0).
- For EVERY version:
  1. Update CHANGELOG.md at the repo root (Keep a Changelog format: Added / Changed / Fixed / Removed / Known Issues).
  2. Write player-friendly PATCH NOTES in /PatchNotes/vX.Y.Z.md (headline features, gameplay changes, graphics changes, bug fixes, known issues, and what's next).
  3. Bump the version in Unity Player Settings (bundleVersion).
  4. Commit, then create an annotated git tag: git tag -a vX.Y.Z -m "vX.Y.Z"
  5. Push the commit and the tag.
  6. Create a GitHub Release using the GitHub CLI:
     gh release create vX.Y.Z --title "PIX Football vX.Y.Z – <name>" --notes-file PatchNotes/vX.Y.Z.md
     Attach a zipped Windows build when one exists.
  7. Mark versions below 1.0.0 as pre-releases (--prerelease).
- If the GitHub repo doesn't exist yet, ask me for the repo name and whether it should be public or private, then create it with gh repo create.

Suggested roadmap (adjust it and ask me to approve):
- v0.1.0: Unity HDRP project setup, folder structure, greybox field and stadium, one controllable player, camera, input.
- v0.2.0: 11v11 on the field, snap, basic run and pass plays, tackling v1, downs and clock.
- v0.3.0: Playbook system, play-call UI, offensive and defensive AI v1, penalties.
- v0.4.0: Physics tackling and momentum (2K5 feel pass), passing and catching polish.
- v0.5.0: Broadcast presentation: overlays, replays, cameras, first-person camera.
- v0.6.0: Graphics pass 1: Blender player pipeline, PBR uniforms, grass and weather, lighting.
- v0.7.0: Regen player generator, fictional league, quick sim engine.
- v0.8.0: Career Mode: franchise management, seasons, cap, contracts, PIX Bowl.
- v0.9.0: College Network: college season sim, watch games, scouting, big board.
- v0.10.0: Draft House event: stage, studio, crowd, animations, signings.
- v0.11.0: Road to Heisman: 4-year career, award race, win/lose screen.
- v0.12.0: Graphics pass 2: gritty textures, dynamic dirt and stains, crowd, cinematic grading.
- v1.0.0: Polish, balance, performance, save/load hardening, full release.

=====================================================================
8. HOW TO WORK WITH ME
=====================================================================
- Work one version at a time. At the end of each version, give me a short summary, how to test it in the Unity Editor, and the GitHub release link.
- Before large changes, explain your plan briefly and confirm with me.
- If you can't do something inside Claude Code (for example, creating 3D art in Blender or making animations), tell me exactly what I need to do or provide, give me step-by-step Blender instructions or Python scripts for Blender, and use placeholders in the meantime.

Remember: start with STEP 0. Ask me your clarifying questions first.
```

---

## PROMPT 2: Ask Claude to Explain How It Would Build It

```
I'm making an American football game in UNITY (HDRP, C#). I want its gameplay and match engine to FEEL like ESPN NFL 2K5, and its graphics to LOOK like NCAA Football 14 on PS3, but in higher definition with gritty, high-resolution textures and models made in Blender 3.x. All teams, players, and branding will be original.

Don't write the game yet. Explain, as a senior game developer would to me, exactly how you would build it. Cover:

1. WHAT MADE 2K5's MATCH ENGINE FEEL THE WAY IT DID
   Break down the feel: player weight and momentum, tackling, blocking, AI coverage, passing, pacing, camera, and presentation. Then explain how to recreate each one in Unity (Rigidbody vs CharacterController vs kinematic + root motion, animation blending, Animator state machines vs motion matching, active ragdoll / physics-blended tackles, steering and pursuit AI, and play/route systems).

2. WHAT MAKES NCAA 14 LOOK THE WAY IT DID, AND HOW TO SURPASS IT
   Lighting, stadium atmosphere, crowds, uniforms, grass, and broadcast color grading. Explain how to achieve a higher-fidelity, gritty version in Unity HDRP (PBR materials, decals for dirt and stains, layered/dynamic uniform materials, grass and turf shaders, volumetrics, post-processing, crowd impostors, and LODs).

3. THE BLENDER 3.x → UNITY ART PIPELINE
   Modeling and topology for players and gear, UVs, baking, texture resolution, rigging for Unity Humanoid, export settings, and how to keep it performant.

4. SYSTEM ARCHITECTURE
   A diagram (text is fine) of every major system: MatchEngine, AI, Physics, Animation, Presentation, Sim Engine, Franchise/Career, College Network, Draft House, Road to Heisman, Regen Generator, UI, and Save. Show how they communicate.

5. THE TWO GAME MODES
   How you would design the data and logic for:
   - Endless Career Mode (fictional pro league, regen players, PIX Bowl, college network with scouting and highlights, a live Draft House event with stage, studio, fans, and signings).
   - Road to Heisman (4-year college career; win only if the team is ranked #1, I win the player-of-the-year trophy, AND I'm the #1 draft prospect; otherwise show my placings and let me retry).

6. A REALISTIC ROADMAP AND RISKS
   Versions, what a solo dev plus Claude can realistically achieve, which parts are hardest (animation, AI, art volume), and where to use asset-store or placeholder content.

7. VERSIONING
   How you'll write patch notes for each version and publish each version as a GitHub release (tags, CHANGELOG.md, gh release create).

When you're done explaining, ask me numbered questions about anything you didn't understand or need me to decide.
```
