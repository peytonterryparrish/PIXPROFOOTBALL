# Style reference: All-Pro Football 2K8 (user direction, 2026-10-21)

User: "Take a lot of inspiration from All-Pro 2K8 — smooth animations, realistic speed of game, calculated and clean."
20 reference screenshots are saved locally (git-ignored) in `Docs/Reference/AllPro2K8/1.webp … 20.webp` — open them with the Read tool.

## What the references show
**Camera (user play, offense)** — 1, 3, 4, 6, 7, 10, 12, 19, 20
- Behind the offense, centered on the ball, elevated roughly 6–9 m and 8–12 m behind the QB, pitched down ~25–35°.
- Frames the whole formation width plus ~15–20 yd downfield; the QB/RB sit in the lower third, the defense in the middle third.
- Fairly long lens (narrow-ish FOV); little perspective distortion; the field reads flat and clean.
- After the snap it pulls back/up slightly and tracks the ball carrier from behind.

**Formations & stances** — 3, 4, 6, 7, 17
- Tight, symmetric, believable splits: OL shoulder to shoulder in low 3-point stances, all facing downfield; DL in 3/4-point stances facing them nose to nose across the neutral zone; LBs 4–5 yd deep in 2-point; CBs square to their receivers; safeties deep.
- Everyone on the correct side and facing the correct way, every snap.

**Bodies** — 2, 5, 9, 14, 16, 18
- Bulky, padded football silhouettes: big shoulder pads, thick thighs and waists, linemen visibly wider than skill players. Never spindly.

**Animation & speed**
- Smooth, weighted, unhurried. Players move at believable football speed: not twitchy, no instant direction changes, clean blends between states.
- "Calculated and clean": readable plays, no chaos, no pile-ups of overlapping bodies, no popping.

**Presentation** — 1, 3, 4, 6, 7, 12, 19, 20
- Full-width scorebug strip along the BOTTOM of the screen: team abbreviations + logos + scores, down & distance, ball spot, quarter, game clock, play clock. Simple, solid colour, very legible.
- User player marker: a bright ring or star under the player's feet.
- Large painted team logos at midfield; bright, saturated, clean turf with crisp lines.

**Lighting/graphics feel**
- Clean and bright during play, with good readability. Drama belongs in night games, cinematics and replays, not in murky gameplay.

## Second batch (21–40) — additional details
- **Pre-snap markers** — 23, 25, 35, 37, 38: a gold/white **star icon under each eligible receiver** and the user player pre-snap; a teal ring/star under the controlled player.
- **Line stances up close** — 24, 38: C and guards low with the down hand planted, flat backs, heads up, weight forward; DL mirrored across the ball. Big readable jersey numbers front and back.
- **Uniforms** — 22, 29, 34, 40: large block numbers on chest, back and shoulders; player NAME bar on the back; team wordmark on the chest; sleeve stripes; glossy helmets with a stripe and a side logo.
- **Kicking** — 36: a simple kick meter panel ("TIMING Perfect / AIM Centered"); 39: a **wind indicator** (compass arrow + mph) in the top-right corner.
- **Coin toss** — 30: on-field scene: referee at midfield with team captains, caption "Rollers choose tails."
- **Stat lower-thirds** — 21, 24: a slim table strip (# NAME TEAM ATT YDS AVG YAH LNG TD) under a big play.
- **Team package editor** — 28: uniform/helmet/logo/colour editor (later version: team customization).
- **Action** — 26, 27, 33: open-field running and pursuit stay spaced out and readable; pass protection forms a clean pocket.

## Create Player & abilities (41_create_player.webp) — planned for v0.7.0 / v0.11.0
- Create Player screen: left column of tabs (Vitals / Abilities / General) with icon tiles, a scrolling ability list with check marks and a description panel, and the player model on a turntable at the right.
- **Ability (trait) system**: named special abilities with tiers (★★★) that change on-field behaviour, e.g. deep-ball threat, clutch on 3rd down, possession receiver, return specialist, elite route runner, ankle-breaker jukes, battering-ram runner, spin specialist, cannon arm, stop-on-a-dime cuts, tough over the middle. Each ability has a gameplay hook in the Sim (move success, catch in traffic, throw power, cut speed loss, 3rd-down boost…).
- Use: regen players get abilities by archetype and rating (v0.7.0 League); Career shows them on player cards (v0.8.0); Road to Heisman uses Create Player and unlocks abilities through progression (v0.11.0). Use our own ability names.

## Who uses what
- Stance/Camera track: camera framing above (it becomes the default "Madden/2K8" user camera), formation spacing, stances, and a bottom-bar scorebug option in the 2K8 layout using our UI kit and colours (default = bottom bar).
- Animation track: smoothness, blends, no popping, unhurried weight.
- Engine track: realistic game speed (top speeds, acceleration, play tempo), clean, readable plays without chaos.
- Models track: bulky, padded silhouettes; linemen visibly bigger.
- Graphics track: clean, bright, saturated turf and crisp lines during play; drama for night games and cinematics.
