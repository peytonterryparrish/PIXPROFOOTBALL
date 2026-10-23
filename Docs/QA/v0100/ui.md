# v0.10.0 UI track: 25 menu directions (owner note 10)

Output (outside the repo, images not tracked): `D:\PIXFootball\Renders\ui_directions\`
- `index.html` gallery, `<NN>_<slug>\{hub,playcall,third}.png`, `index.html` source per direction, `contact.png` per direction, `_shared\` (library, fonts, plates, logos, pack art, `render.sh`).
- No game code was touched. Everything is HTML/CSS rendered with headless Edge at 1920x1080.

## 1. Research: what defines each look

Method note: web search returned little menu-specific writing (mostly reviews and previews), so the points below combine the few sourced facts (marked "src") with the owner's own reference screenshots (`Docs/Reference/Madden/42-53`, `Docs/QA/REFERENCE_MaddenUI.md`, the 80 preview mockups) and general knowledge of the series (marked "known"). Nothing was downloaded; everything was viewed only.

### Madden 2003-2008 (PS2 / Xbox, then 360)
- **Layout:** a stacked vertical list of mode buttons on one side, a big player/hero render on the other, a team strip along the bottom; submenus are nested lists with a description pane (known). src: reviews called the 2005 opening menu its most impressive part, a mix of pulsing stills and video; the 360 version of 06 was re-laid-out for 16:9, more horizontal and less cluttered, so you could back out through levels more easily; the 07 main menu was sparse (Play Now / Mini-Games) in an early build.
- **Materials:** brushed metal, chrome bevels, carbon-ish dark panels with glossy highlights, heavy drop shadows (known; the owner's Madden-08 preview set shows exactly this).
- **Type / colour:** bold italic condensed caps, white on steel, one hot accent (orange/gold) for focus; team colours only in crests and bars.
- **Motion:** quick slide-in of the list, focused row flashes, hero art cross-fades (known).

### ESPN NFL 2K5 and All-Pro Football 2K8
- 2K5 (src): praised for a big feature set (VIP profile, the Crib, franchise captions). Menus were highly navigable with descriptive captions on every franchise item; the look was TV-sports: skewed colour bars, glossy slabs, ticker (known).
- All-Pro 2K8 (src): the weakest part was presentation: plain, "no glitz" main menus per one user review; praised: a live real-world ticker scrolling along the bottom; clean bright field during play. The game bar for on-field readability is in `REFERENCE_AllPro2K8.md` (bottom scorebug, star markers).
- **Take-away:** ticker plus caption-heavy navigation is the 2K recipe; glamour must come from the hero art and materials we add.

### Madden 09-13 (360 / PS3)
- src: 10 introduced a three-box play-call and pointer-style menus on Wii; 11 made team select richer (full-body figures, stadium, weather) and added GameFlow (one-press play suggestion); 12's franchise menus were criticised as muddled (six tabs behind Y); 13's front end was called polished and more logical.
- **Look (known):** dark brushed carbon with glowing orange/yellow focus, big player cut-outs, 3D team-select with helmets, a tabbed franchise "hub" with a hero tile.

### Madden 15-20 (PS4 / Xbox One)
- src: play-call styles in 19: Quick (one coach suggestion), Enhanced (three suggestions across Coach Suggestions / Formation / Concept / Play Type / Personnel / Recent Plays) and Slim (same choices in a broadcast-style presentation).
- **Look (known):** flatter, darker "app" look, big hero stills, tabs along the top, bright focus outlines; 21 (src: Push Square) leaned on X's and O's and bright neon colour for The Yard.

### Madden 21-26
- src: 26 console review calls the menu cleaner than 25; a mobile redesign moved core modes to a left menu with a big hero banner.
- **Reference screenshots 42/49/50 (owner):** top tabs (HOME / PLAY / SHARE / CUSTOMIZE, LB/RB), one large hero tile plus mixed-size tiles, focused tile glows gold, controller-glyph legend, news ticker along the bottom. 48: play-call as a translucent bottom strip with tabs and three big play-art cards, glyph legend (flip, adjustments, tempo). 44: matchup card with week strip, two crests, records, OVR/OFF/DEF. 45: franchise hub tabs + standings panel. 51/52/53: cheat sheet, compact side list, playbook list.

### NCAA 14, College Football 25/26
- src: NCAA 14 moved to large panels for main menu choices (away from NCAA 13's sweeping ESPN-style menus), loading screens show stats and info, halftime show and 500+ on-field vignettes; Dynasty UI was criticised as stale, recruiting as menu-heavy. CFB 25: menu and background change to match your school (flags, symbols); presentation leans on team rituals, run-outs, mascots; stamina/wear icons criticised as small and unclear. CFB 26: Dynasty history screen with yearly winners, coach shown at the right of the Dynasty screen, pre-snap substitution menu showing wear and tear.
- **Take-away:** team-colour-driven chrome, pennants/banners and tradition cues (our directions 10 and 17).

### Broadcast TV graphics (network NFL packages)
- src: the persistent score "bug" arrived with Fox's 1994 NFL debut and then became standard; Fox tends to reveal a new package at the Super Bowl; modern bugs are live information systems and brand markers (not only score).
- **Look (known):** white or glass bars with team-colour wedges, heavy condensed sans, sparing animation (wipes, slides), single-line lower thirds, tale-of-the-tape comparison strips, prime-time packages with chrome/ice and dark blue; day packages are brighter and flatter. Directions 02, 09, 15 take from this; we show none of the real brands.

### Distilled design rules (all directions)
1. Hero art plus one dominant list or tile field; the rest is secondary.
2. Exactly one focus colour per direction; team colours only in crests and thin bars.
3. Glyph legend always visible; ticker or status line on hubs.
4. Play-call screens leave the field visible and never cover the snap area.
5. Type is condensed caps for headings, readable body at 20 px minimum on the 1920x1080 canvas.

## 2. Inventory of the owner's UI art and assets used

| Asset | Where | Used as |
|---|---|---|
| Pack 1 Clean (300) | `D:\PIXFootball\Source\UI_Extracted\UIPack1` | banners / tabs frames (`_shared/pack/p1_*`) |
| Pack 2 Management (300) | `UIPack2` | sports symbols: jersey, cleat, goalpost, flag, headset, clipboard... (`p2_sym`) |
| Pack 3 Brush (500) | `UIPack3` | orange brush-stroke fills / underlines / buttons (`p3_brush`, `p3_tabs`) |
| Pack 4 Modern Mgmt (750) | `UIPack4` | structure reference only (depth exchange, budget layouts) |
| Pack 5 Scouting (750) | `UIPack5` | scouting/football symbol icons (`p5_sym`) |
| Pack 6 Glass (1000) | `UIPack6`, `Glass6` | line icons (`p6_line`), tactical play-diagram cards (`p6_diagram`, 56 offence+defence plays), bold glyphs |
| 80 preview mockups (4 sets of 20) | `UI_Collection\...\UI Previews - 80 Screens` | hero player art and composition reference (`_shared/plates`) |
| `DFB_UI_Export.zip` | `C:\Users\peyto\Desktop\Assets` | inspected only: Draft Fever logos and components plus Xbox glyph PNGs; **not used** (glyphs are drawn in CSS; no Draft Fever / Fever Bowl marks anywhere) |
| 32 pro crests (ProV6) | `Assets/_Licensed/UI/Logos/ProV6` | team crests on every screen and in the SVG helmets |
| Game renders | `D:\PIXFootball\Renders\gfx_v090\final_full` | real field/stadium frames for play-call screens and blurred hub backdrops |
| `PPF NEW ASSETS` | `C:\Users\peyto\Desktop\PPF NEW ASSETS` | 3D packs only (stadium, jumbotron, news studio, tunnel, trophies, players); no 2D UI art, not used here. Good candidates for real 3D menu stages later (tunnel, news studio, jumbotron, trophy). |
| Fonts | `Assets/_Licensed/DraftFever/Fonts` (open-licence families) + DejaVu | direction typography (Bebas, Oswald, Saira Condensed, Barlow Semi Condensed, Alfa Slab, Baskerville...) |

**Flag for the owner:** the ProV6 crest set reads as close parodies of real league marks (bird, bull, star, helmet, shield silhouettes). The task says to use it, so every direction does; before any public release these need a legal pass (already tracked as the logo clean-up in `AssetInventory.md`).

## 3. The 25 directions
See the table appended at the bottom (filled in after the renders were checked).

| # | Name | Pitch | Third screen |
|---|---|---|---|
| 01 | Steel Plate | Brushed-steel slabs, chrome bevels and rivets with one hot-orange focus: the classic Madden-era metal hub, rebuilt in 4K. | team select |
| 02 | Broadcast White | Clean white Sunday-broadcast package: team-colour wedges, heavy condensed type, tile mosaic hub and lower-third play call. | matchup |
| 03 | Dark Glass | The game's glass theme evolved: graphite blurred panels over the stadium, silver hairline, diagonal sheen, a single orange accent. | team select |
| 04 | Leather Stitch | Pebbled brown leather panels, white stitching and brass studs; tile hub, open playbook-binder play call, leather franchise hub. | franchise hub |
| 05 | Chalkboard | Coach's slate-green chalkboard in a wood frame: handwritten menu, hand-drawn X and O route diagrams, chalk-outlined crests. | matchup |
| 06 | Stadium Lights | Night floodlight rig drama: lens flares, light shafts through haze, white Bebas type with icy cyan accent, tower play list and lit helmets. | team select |
| 07 | Turf Lines | The turf is the interface: mowed stripes, painted yard-line ladder menu, top-down chalked play board and a franchise timeline on the field. | franchise hub |
| 08 | Retro 2K5 | Mid-2000s TV-sports menus: chunky glossy slanted bars, silver bevels, team-colour stripes, big italic caps, orange and blue. | team select |
| 09 | Sunday Night Cinema | Prime-time cinematic: 2.39:1 letterbox, teal-and-gold grade, film grain, elegant serif title and restrained thin UI. | matchup |
| 10 | College Pageantry | Saturday college-football pageantry: hanging pennant menu, varsity slab type, stitched patches, cream with crimson, navy and gold. | franchise hub |
| 11 | Carbon Telemetry | Motorsport-grade sim look: carbon weave, red and amber data, clipped panels, gauges, tick scales and mono telemetry. | matchup |
| 12 | Jumbotron LED | Stadium LED video-board look: dot-matrix amber type on black, visible pixel grid, segmented clocks and chasing bulb borders. | franchise hub |
| 13 | Locker Room | Team locker room: dark wood lockers, steel mesh vents, brass nameplates, jerseys and helmets on hooks, warm tungsten light. | team select |
| 14 | Trading Card Foil | Collectible rarity-tier foil cards for every mode, a fanned hand of play cards and a team-card collection grid with pack opening. | team select |
| 15 | Italic Speed | Aggressive heavy-italic Impact caps, diagonal-cut slabs and parallelogram play cards, with a lightning-bolt seam matchup. | matchup |
| 16 | Tile Hub Modern | Modern console football hub: skewed gold tab bar, mixed-size focus tiles, bottom ticker, translucent play-call strip and franchise standings. | franchise hub |
| 17 | Gameday Program | Printed matchday programme: cream stock, serif masthead, torn-edge cover photo, contents list, newspaper play sheet and two-page game preview. | matchup |
| 18 | Blueprint Tactical | Coach's war-room blueprint: cyan linework on navy, wireframe helmet call-outs, dimensioned tactical board and engineering franchise dashboard. | franchise hub |
| 19 | Hall of Fame Gold | Black marble and brushed gold trophy room: spotlit championship ring, gold-framed play cards on black glass, laurel legacy cards. | team select |
| 20 | Tunnel Entrance | Player tunnel before kickoff: blinding stadium doorway, haze, and menus as backlit overhead wayfinding signs. | matchup |
| 21 | Primetime Neon | High-energy night-game neon: glass-tube outlines in team colour pairs, glossy reflection floor, big outlined numerals. | team select |
| 22 | Wristband Tablet | Sideline coach tech: rugged tablet hub, a real QB wristband play grid beside the field, and a staff dashboard. | franchise hub |
| 23 | Pressbox Ledger | Press-box stat sheets: green-bar ledger paper, typewriter type, rubber stamps, paper clips and a wire-copy ticker. | matchup |
| 24 | Gridiron Films 70s | 1970s sports-documentary look: sunset sunbursts, slab type, film-strip frames, grain and dust, team sticker album. | team select |
| 25 | Brush Stroke Grit | Gritty street look: orange paint-swipe highlights on stencil type, rough concrete, taped paper notes and halftone hero cut-out. | franchise hub |

Top 5 picks (see the final report for the reasoning): 16 Tile Hub Modern, 01 Steel Plate, 03 Dark Glass, 02 Broadcast White, 15 Italic Speed. Suggested hybrid: Steel Plate materials on the hub, the Tile Hub (16) play-call strip, Dark Glass (03) panels for data screens.

Known minor blemishes (checked, left as is): 03 team-select backdrop shows a sliver of the baked game compass; 10 hub ticker clips at its edge; 11 playcall wind dial sits over the baked compass; 13 hub shows a small football icon under the open locker plate; 20 matchup doorways are flat glow arches; 21 hub floor is sparse; 06 crest chips are small (32 in one row). Hero art comes from the owner's mockup plates cropped and masked, so they are 1672x941 sources scaled up (slightly soft at 1080p).
