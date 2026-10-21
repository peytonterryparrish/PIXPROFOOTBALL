# PIX Football - UI Style Guide

Visual language and asset map for the in-game and menu UI. The look is taken from the user's own Figma export of
"Draft Fever Bowl" (`_AssetSource/DFB_UI/DFB_UI_Assets/`, 14,657 PNGs at @1x/@2x/@4x plus `manifest.csv`).
We reuse its **style and its generic parts**; we never ship its brand.

Curated library: `Assets/_Licensed/UI/` (git-ignored like all of `_Licensed`, see ARCHITECTURE golden rule 2).
Index of every file with source path, screen and notes: `Assets/_Licensed/UI/ui_manifest.csv`.
Import rules and 9-slice borders: `Assets/_Project/Scripts/Editor/UI/UiSpriteImporter.cs` + per-folder `slicing.json`.

---------------------------------------------------------------------------------------------------

## 1. Source survey (what the export contains)

| Folder | Era / content | How we use it |
|---|---|---|
| `UI-1` styleguide + screen flow | Oldest pass. Bright arcade look: pure yellow `#FFEA00` / green accents, black text on yellow, parallelogram ("angled") shapes. Full component kit: buttons, tabs, dropdowns, inputs, alerts, sliders, checkboxes, tables, badges, Xbox/PS/Switch glyphs, stat icons, scoreboard strips, jock-card frames, draft house, trades, scouting, team creation, 16 pro + ~150 college logos. | Component kit and icons/glyphs/badges/logos. Not the colour mood. |
| `UI-2` components + screen flow | Season schedule/leaderboards, **play-selection (play-call) UI** (RUN/PASS/BLITZ tags, play cards, timer rings, route-diagram icons), division badges, star ratings. | Play-call and schedule parts. |
| `UI-3` page_1 | **Newest, and our target look**: dark navy flat UI, blue + gold accents, team-colour wash on rows, 3D player renders in front of stadium fog. Main menu hub, roster, trades, draft house/board/night/map, scouting clipboard, coach clipboard (play call), pause menu, season leaders, standings, schedule rows, uniform customization. Mostly flat rectangles (0 KB PNGs) that we rebuild as tinted `Image`s. | Primary reference for layout and colour. |

Manifest: 14,655 rows (6,334 @1x, 4,509 @2x, 3,812 @4x); 12,867 of them PNG (the rest are SVG vectors). About 6 percent of the PNG rows point at files that do not exist on disk - the curation skips those.

Viewed while surveying: ~45 contact sheets covering all three folders (UI-3 full screens: main menu, roster, pause, trade x2, draft board,
coach clipboard, scout x2, leaders, cards, uniform; UI-2 play-selection kit, schedule/leaderboard screens; UI-1 buttons, tabs, inputs, alerts, modals,
icons, glyph atlases, scoreboard strips, jock cards, draft rows, trades, home, team creation, logo atlases).

---------------------------------------------------------------------------------------------------

## 2. Visual language

### 2.1 Mood
Broadcast sports-game UI at night: near-black navy surfaces, thin bright accents, condensed all-caps type, hard angled cuts
(parallelograms / chamfered corners), a blurred stadium + 3D player as the hero image behind a flat UI layer. Information first;
decoration is limited to a team-colour gradient wash and a subtle diamond-pattern background. No rounded "mobile" pills except the
controller glyphs.

### 2.2 Palette (hex sampled from the art)

| Token | Hex | Where it appears |
|---|---|---|
| `bg.deep` | `#11141C` | Page background behind panels (draft board, schedule) |
| `bg.nav` | `#1A252C` / `#111219` | Top navigation bar, left rail |
| `bg.panel` | `#1E1F28` | Modal body, large panels (trade modal) |
| `bg.panel.dark` | `#15161E` | Bottom sheets, footer areas |
| `bg.panel.side` | `#10161C` / `#12191F` / `#1B2329` | Clipboard / right-hand panels |
| `bg.black` | `#000000` | Table header bars, stat strips, play-chart field |
| `row.default` | `#2A2B37` | List rows, secondary buttons, filter chips |
| `row.hover` | `#272830` -> `#2A2B37` + 2 px accent outline | Hover/selected list row |
| `row.selected.teal` | `#114541` (+ 2 px `#2D6AED` outline) | Selected player row on the clipboard |
| `accent.blue` | `#2D5ABC` (hover/active `#2D6AED`, deep `#1D4190`, selection bar `#3068F0`) | Primary buttons in UI-3 (Trade, view), timeout pips, tab highlight |
| `accent.gold` | `#FFC837` | Positions (DE/LB), countdown timers, headline values, "Round 1" |
| `accent.orange` | `#FF9000` (XP bar) / `#F75535` (warning text) | XP/progress bar, "Ranked Games:" ticker keyword |
| `accent.yellow` (UI-1) | `#FFEA00` | Selected tab/row in UI-1 style, MAN tag, flag strip |
| `coin.gold` | `#D9960C` | Currency/coin icons |
| `state.success` | `#11F055` (UI-1 green; UI-3 marker `#20B858`) / bg `#05481A` | FIELD GOAL tag, success alert, "Watch live" CTA |
| `state.warn` | `#FFC837` text on `#4D4700` | Warning alert, injury/fatigue |
| `state.danger` | `#C20000` (flag/blitz) / row bg `#3E1B20` | BLITZ tag, error alert, notification count |
| `state.disabled` | `#333333` fill, `#6D6D6D` text | Disabled buttons |
| `text.primary` | `#FFFFFF` | Titles, active tab, numbers |
| `text.secondary` | `#C5C5C5` | Footer prompts, captions |
| `text.muted` | `#6D6D6D` | Disabled and inactive tabs |
| `side.offense` | `#E83060` (toggle bar; toggle bg `#303040`) | Offense toggle |
| `side.defense` | `#30E080` | Defense toggle |
| `side.special` | `#9040F0` | Special-teams toggle |
| `play.run` | `#FA7805` | Play category tags |
| `play.pass` | `#054EED` | |
| `play.rpo` | `#05ACED` | |
| `play.zone` | `#00287D` | |
| `play.man` | `#FFEA00` (black text) | |
| `play.blitz` | `#C20000` | |
| `play.kick` | `#1A1A1A` | |
| `play.punt` | `#B845FF` | |
| `play.fieldgoal` | `#11F055` (black text) | |
| `marker.ring` | `#8C00FF` violet ring + white arcs | In-game player/target ring reticles |
| `tier.*` | bronze `#D87850`, silver `#D8D8D8`, gold `#FFC000` (coin `#D9960C`), platinum `#10B0A8`, diamond `#50A0E8`, master `#C82830` | Rank/division badges |

Team colours are data driven: rows and the scorebug use a **team-colour wash** - a horizontal gradient of the team primary at ~35 percent
alpha fading to transparent over `row.default` (assets: `Panels/draft_team_color_*`, `HUD/sb_holder_*`). Never hard-code team colours in sprites.

### 2.3 Typography
Fonts already imported under `Assets/_Licensed/DraftFever/Fonts/Main/` (and the same files in `_AssetSource/DraftFever/Fonts`):

| Role | Font | Notes |
|---|---|---|
| Display / nav / headers / numerals / table headers / tags | **Saira Condensed** Bold + Medium (+ BoldItalic for banners) | ALL CAPS for nav, tags, banners; mixed case for names |
| Body / labels / row text / stats | **Barlow Semi Condensed** Medium + Bold | Mixed case |
| Pictograms | MaterialSymbolsSharp (variable) | Use for generic icons we lack |
| Jersey numerals | `HUD/JerseyNumbers/*` sprites (or Alfa Slab / Oswald from `Cosmetics/`) | |

Scale on the 1920x1080 reference: nav 22 / section title 28 / row name 24 bold / body 20 / caption 16 / hero numbers 36-64.
Create TextMeshPro SDF assets from those TTFs (Dynamic, Multi Atlas) and use Saira Condensed Bold for anything that is a number or a command.

### 2.4 Shapes and surfaces
* **Rectangles with hard corners** for panels, rows and buttons (UI-3). Corner radius 0-4 px.
* **Parallelogram / trapezoid cuts** for scorebug, banners, tags and clock holders (`Shapes/*`, `HUD/sb_*`): skew ~ 20 degrees, tint with team colour.
* **Chevrons/arrows** (`>`) for next/ drill-down, caret icons for dropdowns, filled triangles for ranking up/down (`Panels/coach_vector_51/52` red triangles).
* Selection = **2 px outline** in accent (blue/yellow/teal), not a glow. Hover = +8 percent lighter fill. Pressed = darker fill + 1 px down.
* Disabled = `#333333` fill, `#6D6D6D` text. Never reduce opacity of text only.
* Drop shadows: none on panels; buttons have a 3 px darker bottom edge (UI-1) - baked into the button PNGs (border `[12,16,12,12]`).
* Backgrounds: stadium fog + blurred player render (3D, ours) + `Backgrounds/bg_tactics_*` diamond pattern for data-heavy screens; always a 60-70 percent black `Backgrounds/bg_in_game_pause_overlay` style scrim under modals.

### 2.5 Layout grid (1920x1080 reference)
* Canvas Scaler: Scale With Screen Size, reference 1920x1080, match 0.5. We target 1440p: 1.33x the reference. All art is imported at **100 x source scale PPU** (2x art = 200 PPU) so `Set Native Size` reproduces the design pixel size.
* Safe margins 80 px left/right, 40 px top/bottom (TV safe). Content column edge x = 80 and x = 1840.
* Top navigation bar: 60 px tall, full width (`Panels/ui3_main_navigation_bar`): back button (left), team crest + two-line name, `LB` glyph, tabs, `RB` glyph, currency, profile, menu. Selected tab = white pill with black text.
* Footer prompt bar: y = 1020-1080, glyph (A Select, B Cancel, X/Y context actions, stick icons) + label pairs on the left, ticker centre, date/clock right. Always shows the real Xbox glyphs (`Glyphs/Xbox/xbox_ui3_*`).
* Page header row at y = 180-220: sub-tabs with `LT`/`RT` glyph bumpers on each side.
* Rows: 60 px (list) or 130 px (card rows with logo watermark and star rating), 10-20 px gutters, 2-column grid 597 px wide for boards, 3-column for cards.
* Panel padding 16-20 px; section gap 24 px; icon 24/32 px; logo in lists 40-64 px, in headers 128-256 px.
* Right-hand "detail" panel 490 px wide (player card/trade stats/actions), actions as full-width 50 px rows.
* Animations: 120-180 ms ease-out slide for panels, 80 ms colour fades for hover, a shimmer sweep for gold/rare items. No bounce.

### 2.6 Controller prompts
Xbox only for now. White pill glyphs from UI-3 (`xbox_ui3_a/b/x/y/lb/rb/lt/rt/analog_all/analog_click`), plus UI-1 stick/d-pad variants (`xbox_l_*`, `xbox_r_*`, `xbox_dpad_*`, coloured A/B/X/Y). Pass receivers on field: use `HUD/Markers/marker_ring_a..d` (violet reticles) with the button glyph centred.

---------------------------------------------------------------------------------------------------

## 3. Curated library layout

```
Assets/_Licensed/UI/
  ui_manifest.csv                     dest, source, category, screen, notes, scale, px   (1,465 rows)
  DFB/<Category>/<name>.png + slicing.json
  Logos/Pro, Logos/College            fictional pro (16) and college (7 named + 143 numbered) crests
```

| Category (DFB/) | PNGs | Contents |
|---|---:|---|
| Alerts | 44 | info / success / warning / error bars, popovers |
| Backgrounds | 13 | fog washes, team-colour wash, pause scrim, tactics diamond bg |
| Badges | 67 | tier badges, MVP/accomplishment awards, division badges, captain tags, my-team tag, notification count, DRAFTED/TAKEN stamps |
| Buttons | 95 | primary/secondary/tertiary blank buttons (sm/lg/xl, 4 states), icon buttons, UI-3 blue icon buttons |
| Cards | 32 | player-card frames, smoke/dirt/frame layers, stat panels, gradients (portraits excluded) |
| Controls | 84 | sliders, checkboxes, scrollbars, toggle buttons, UI-3 scroll tracks |
| Dividers / Indicators | 3 / 11 | |
| Draft | 82 | pick-order rows, results rows, board tables, trending flags, round tabs |
| Dropdowns | 77 | dropdown closed states + option rows |
| Glyphs | 60 | Xbox (55), keyboard keycaps (5) |
| HUD | 55 | scorebug parts (`sb_*`), possession arrow, flag strip, timeout pips, markers, atlases, jersey numerals, scorebug references |
| Icons | 116 | stat icons (TD/pass/sack/tackle...), currency, scout, trade, lock, result badges, star ratings (19) |
| Inputs | 44 | text fields (sm, 4 states) |
| Leaderboard / Schedule / Modals / Menus | 13 / 20 / 13 / 18 | season leaders, schedule rows, standings/season-end modal, settings tabs, home cards |
| Panels | 77 | UI-3 bases, rows, headers, team-colour overlays, nav bar |
| PlayCall | 60 | play tags, play-card frame, timer rings, field-chart icons/lines, arrows, penalty dialogs |
| Roster | 17 | offense/defense/special toggles, starter/2nd/3rd string toggles, player tags |
| Scouting / Trades / Tables / Tabs / Shapes / TeamCreation | 17 / 49 / 27 / 27 / 40 / 78 | |
| Screens | 60 | full-screen mock references (1x) and component matrices - **reference only** |
| Logos | 166 | 16 pro + 150 college |

Total on disk: **129.6 MB** (1,465 PNGs, 36 `slicing.json`, 1 CSV).

### 3.1 Scale / PPU policy
* Components copied at **@2x** (for 1440p+; scaled down 0.67x at 1080p). Hero/small crisp art (scorebug parts, marker rings, UI-3 glyphs, play clock) at **@4x**.
  Full-frame backgrounds and screen references stay **@1x** (1920x1080) to keep size sane; soft backgrounds upscale cleanly.
* `slicing.json` carries `defaultPpu` (200 for @2x folders, 100 for @1x) and per-file `ppu` overrides (400 for @4x).
* Estimated 9-slice borders `[left,bottom,right,top]` are in source pixels; tune in `slicing.json` - the importer re-imports siblings on save.
  Rules: blank buttons `[12,16,12,12]`; strips (w/h >= 6) `[w/8,0,w/8,0]` capped at 24; other panels `min(24, min(w,h)/4)` all sides. Items smaller than 24 px and non-stretchable art (icons, badges, glyphs, backgrounds) get no border.
* Importer (`UiSpriteImporter`): Sprite/Single, no mipmaps, bilinear, clamp, FullRect mesh; <= 512 px longest side = uncompressed, larger = BC7 (CompressedHQ); Screens capped at 2048. Menu `PIX Football/UI/Reimport UI Sprites`.

### 3.2 Baked text warning
Many UI-1/UI-2 component PNGs have **placeholder text baked in** ("Label", "Option", "Placeholder", sample names/stats). Use them for shape, colour and 9-slice
reference, or hide the text region and overlay TMP text. Truly blank parts: `Buttons/btn_*`, `HUD/sb_holder_*`, `HUD/sb_flag_1`, `HUD/sb_timerholder`,
`Panels/*`, `Backgrounds/*`, `Shapes/*`, `Controls/*` handles/tracks. Prefer **procedural UI-3 style** (flat `Image` + palette tokens above) for rows, panels and secondary buttons.

---------------------------------------------------------------------------------------------------

## 4. Screen map

Legend: **COVERED** = usable art + a full-screen reference; **PARTIAL** = some parts, rest procedural/new; **MISSING** = generate (see section 5).
Paths are relative to `Assets/_Licensed/UI/DFB/` unless they start with `Logos/`. "ref_" files are in `Screens/`.

| Our screen | Status | Chosen source assets | Adaptation |
|---|---|---|---|
| Main menu | COVERED | `ref_menu_home`, `Panels/ui3_main_navigation_bar`, `Panels/menu_*`, `Backgrounds/bg_main_menu_bg_fog`, `Glyphs/Xbox/xbox_ui3_*`, `Icons/icon_cash_*` | Left-aligned 3D player render over stadium fog; flat nav bar; footer glyph bar; card tiles from `Menus/card_home_next_game*` and `card_home_standings_preview*` (tiles only, our data) |
| Mode select (Play Now / Career / Road to Heisman / Settings) | PARTIAL | `Menus/title_strip`, `Menus/card_home_event*` (selection states default/hover/pressed), `ref_mode_select_bg`, `Backgrounds/bg_tactics_*` | 3-4 large tall cards with hover = yellow/blue 2 px outline; key art per mode must be generated (missing) |
| Team select | COVERED | `Logos/Pro/*` (16), `Logos/College/*` (150), `ref_scout_select_team` (logo grid), `Scouting/scouting_team_states_*`, `Panels/draft_team_color_*` | Grid of crests (64-96 px) with team-colour wash on hover; names/colours from our own team data (see 7) |
| Play-call (formation -> set -> play, offense and defense, audible) | COVERED | `PlayCall/play_tag_category_*`, `play_play_card*`, `play_tabs_*`, `play_team_section_*`, `play_field_chart_*`, `play_play_chart_*`, `PlayCall/clock_*`, `ref_playcall_*`, `ref_ui2_play_tags`, `Roster/team_toggle_*` | Clipboard on the left third (`ref_playcall_formation`): play-clock + timeout button, formation/player/strings tabs with `LB`/`RB`, field mini-diagram with position chips (procedural circles), play cards with category tag colours; audible/hot-route = glyph-labelled cards (Y/X/B) using Xbox glyphs |
| In-game HUD: scorebug | PARTIAL | `HUD/sb_holder_property_1_local/visitor`, `sb_timerholder`, `sb_flag_1`, `sb_timeoutactive_*`, `sb_arrow_1`, `sb_notification*`, `HUD/Ref/sbref_*` (reference strips), `Shapes/*` | Tint holders with team colours, our TMP for abbreviation/score/clock/down-and-distance; yellow flag strip for penalty/flag text; arrow for possession; orange pips for timeouts |
| HUD: play clock, markers, pass receivers | COVERED | `PlayCall/clock_timer_*`, `clock_rail_*`, `HUD/Markers/marker_ring_a..d`, `HUD/Atlases/atlas_ingame_elements` (ball-carrier chevron + glow ring), `Glyphs/Xbox/xbox_a/b/x/y/lb/rb`, `HUD/JerseyNumbers/*` | Rings projected over players; receiver icon = ring + glyph + jersey number; stamina = thin bar (procedural, tint blue->red) |
| HUD banners (touchdown / first down / field goal) | PARTIAL | `HUD/sb_flag_1`, `HUD/sb_notification`, `Badges/stamp_*` style, `Shapes/*`, `PlayCall/play_tag_category_field_goal` (colour) | Slide-in parallelogram banners tinted by event colour (TD gold, 1st & 10 blue, FG green, penalty yellow); big headline text procedural; celebratory art missing |
| Pause menu | COVERED | `ref_pause_menu`, `Backgrounds/bg_in_game_pause_overlay`, `Glyphs/Xbox/*` | Left 700 px rail on 70-90 percent black scrim, large caps items (selected = accent blue), footer prompts; **do not copy the logo that appears in the reference** |
| Replay overlay | MISSING | (`Glyphs/Xbox/*`, `HUD/sb_*` shapes, `Icons/icon_label_*`) | REW/PLAY/FF transport bar, "REPLAY" tag, angled lower-third - build from shapes; needs generated icons (see 5) |
| Halftime / postgame stats | PARTIAL | `Tables/*` headers, `Leaderboard/*`, `Panels/leaders_*`, `Icons/icon_stats_*`, `ref_leaders_screen`, `ref_leaders_table`, `ref_result_*` | Two-column team stat table with striped rows + top performers; MVP/stat icons; final score hero panel from result screens |
| Settings | COVERED | `Menus/settings*`, `Controls/*` (sliders, checkboxes, toggles), `Dropdowns/*`, `Tabs/*`, `ref_settings_modal` | Tabs gameplay/video/sound/controls; sliders for volumes; dropdown for resolution/quality |
| Loading screen | MISSING | `Backgrounds/bg_*` (fog), `Icons/icon_lock` | Needs key art + tip card + progress art (see 5) |
| Career: roster | COVERED | `ref_roster_starters`, `ref_roster_player_selected`, `Roster/*`, `Panels/stand_*`, `Cards/*` | 3D lineup (our players) over fog with position/name tags; bottom card strip |
| Career: depth chart | COVERED | `ref_playcall_strings`, `Roster/team_toggle_top_right_*` (Starters / 2nd / 3rd), `Roster/player_tag_*` | Drag-reorder rows with grip icon `Icons/icon_drag_horizontal` |
| Career: player card | PARTIAL | `Cards/*` frames, smoke, gradients, stat panels; `Badges/captain_badge_*`; `ref_draft_view_card` | Frame + our portrait render + stat grid; the supplied portraits are excluded (jersey branding) |
| Career: contracts / cap | MISSING | `Tables/*`, `Icons/icon_cash_*`, `Panels/*` | Generic cap table procedural; no contract-specific art (see 5) |
| Career: free agency | PARTIAL | `Draft/*` list rows, `Trades/*` rows, `Icons/icon_cash_*`, `ref_draft_prospects` | Prospect-style list + offer button; reuse trade modal for offers |
| Career: trades | COVERED | `ref_trade_*` (7 UI-3 screens + `ref_trade_main_full`), `Trades/*` (offer/request lists, team banners, balance titles, notifications), `Alerts/*`, `Buttons/ui3_*` | Two-column offer/request with team-colour banners; confirmation modal; toast on proposal events |
| Career: schedule | COVERED | `Panels/sched_*`, `Panels/season_*`, `Schedule/*`, `Icons/icon_label_game_*`, `ref_schedule_screen_*` | Week rows with opponent crest + result tag + "play/sim" action |
| Career: standings | COVERED | `Panels/stand_*`, `Modals/*standings*`, `Badges/badges_tiers_*`, `ref_standings_table` | Conference/division sections, move-arrows (`Panels/season_arrow`), highlight own team |
| Career: league leaders | COVERED | `Panels/leaders_*`, `Leaderboard/*`, `ref_leaders_screen`, `ref_leaders_passing_modal` | Category toggle (`Buttons/leaders_toggle`), top-5 list with logo + big yardage value |
| Career: awards | PARTIAL | `Badges/award_mvp_*` (7), `award_accomplishment_*` (20), `Badges/badges_tiers_*` | Award tiles; ring/trophy hero art missing (rings excluded) |
| Career: trophy case | MISSING | `Badges/award_*` | Shelf layout procedural; 3D/2D trophy art to generate |
| College Network: rankings | PARTIAL | `Leaderboard/*`, `Panels/stand_*`, `Logos/College/*` | Reuse standings/leaderboard rows |
| College Network: scouting | COVERED | `ref_scout_*`, `Scouting/*`, `Icons/icon_scout_*`, `Icons/Ratings/stars_*` | Team select grid then roster + rating stars |
| College Network: big board | COVERED | `ref_draft_board`, `Draft/*`, `Badges/stamp_*` | Numbered 2-column rows with team-colour wash and mascot watermark |
| College Network: prospect card | COVERED | `ref_draft_view_card`, `ref_scout_open_card`, `Cards/*`, `Icons/Ratings/*`, `Icons/icon_stats_*` | Right-hand detail panel with portrait, bio, stat strip, action list |
| Draft House: draft board | COVERED | `ref_draft_board`, `ref_draft_prospects`, `ref_draft_filters`, `ref_draft_notes`, `ref_draft_map*` | Tabs MAP / BOARD / DRAFT NIGHT with `LT`/`RT`; filters panel |
| Draft House: pick clock | PARTIAL | `PlayCall/clock_timer_*`, `ref_draft_night` (gold countdown), `Panels/draft_*` | Gold big timer (Saira Condensed Bold 64) + ring |
| Draft House: on-the-clock ticker | COVERED | `ref_draft_night`, `ref_draft_live_board_feed`, `Draft/*` (pick rows with logo/team banners), `Badges/stamp_*` | Latest picks feed on right, current round on left |
| Draft House: draft grades | MISSING | `Badges/badges_tiers_*` | Letter grades A+..F art and per-team report layout to generate |
| Road to Heisman: player creation | PARTIAL | `ref_create_step*`, `ref_customize_step02`, `ref_custom_uniform`, `TeamCreation/*`, `Inputs/*`, `Tabs/*`, `Buttons/*` | Step tabs + option grid + preview; avatar/body/face editor art missing |
| Road to Heisman: award race leaderboard | COVERED | `Leaderboard/*`, `Panels/leaders_*`, `ref_leaders_*` | Candidate rows with trend arrows and tier badge |
| Road to Heisman: win / lose result | COVERED | `ref_result_win_first`, `ref_result_top4/mid/lose`, `ref_result_final_*`, `Badges/badges_tiers_*`, `Badges/division_*` | Modal with big badge, headline, 2-3 line summary, `Next` button; trophy art missing |
| Generic: buttons / tabs / panels / rows | COVERED | `Buttons/*`, `Tabs/*`, `Panels/*`, `Tables/*`, `Roster/*` | UI-3 flat style + UI-1 buttons for CTA |
| Generic: sliders / toggles / dropdowns / checkboxes | COVERED | `Controls/*`, `Dropdowns/*` | |
| Generic: modal dialogs / notifications | COVERED | `Modals/*`, `PlayCall/penalty_*`, `Alerts/*`, `Trades/*notification*` | Scrim + 860 px modal, 2 buttons (primary blue, secondary `#2A2B37`) |
| Generic: icons | PARTIAL | `Icons/*` (stats, currency, scout, result, status), MaterialSymbols font | **No position icon set, no ratings icons (SPD/STR..)**, no weather/field icons |
| Generic: rank badges | COVERED | `Badges/badges_tiers_*`, `Badges/division_*`, `Badges/captain_*`, `Badges/badge_my_team_*` | |
| Generic: controller glyphs | COVERED | `Glyphs/Xbox/*`, `Glyphs/Keyboard/*` | PlayStation/Switch intentionally not copied |

Status counts (40 screen lines): COVERED 24, PARTIAL 11, MISSING 5 (replay overlay, loading screen, contracts/cap, trophy case, draft grades).

---------------------------------------------------------------------------------------------------

## 5. Gaps to generate with PixelLab (or other)

| Need | Why | Style target |
|---|---|---|
| Our game logo / title lockup (**PIX Football**) | Replaces the excluded Draft Fever logo everywhere (nav corner, pause, splash) | White/gold on transparent, angled, Saira-like caps |
| Replay overlay set: REPLAY tag, transport bar (rew/play/ff), camera-angle icons | no art in source | White glyphs, black pill outlines, same 2 px stroke |
| Loading screen key art + tip card + progress bar | none in source | Dark diamond bg + helmet/field fog, flat bar |
| Mode-select key art (Exhibition, Career, Road to Heisman, Settings) | UI-1 cards were DFB-mode specific | Tall 460x700 cards, duotone team-colour illustration |
| Position icons (QB RB WR TE OL DL LB CB S K P) + attribute icons (SPD STR AGI AWR...) | only stat icons exist | 32 px mint/white pictograms like `Icons/icon_stats_*` |
| Contract / cap / salary-bar art | none | bars `#2D5ABC` on `#2A2B37` |
| Draft grade letters (A+ to F) and report-card panel | none | Gold/silver tier palette |
| Trophy / Heisman-style statue / ring hero art | rings excluded (DFB mark), none otherwise | Golden hex tier look from `Badges/award_*` |
| Touchdown / first-down / field-goal / turnover banner art | only flag strip + generic shapes | Parallelograms with team-colour sweep + shimmer |
| Player-portrait placeholders (silhouette) | supplied portraits carry branding | Flat silhouettes in 3 skin-neutral greys |
| Weather / time-of-day / field-condition icons | none | 24/32 px white |
| Field mini-map / formation position chips | partially procedural | circles with position text (#222224 fill, gold text) |
| Stadium/city hero backgrounds per school | only fog + one pro stadium photo (excluded) | Blurred night stadium, we render in Unity |

---------------------------------------------------------------------------------------------------

## 6. Excluded Draft Fever branding and unusable content

User instruction: ignore the Draft Fever Bowl logos. Nothing below was copied as an asset (full list with manifest paths in section 6.1).
Screen references that merely *show* branding inside a mock-up are kept as `Screens/ref_*` (reference only, flagged in `ui_manifest.csv`; never ship).

### 6.1 Excluded source files (paths relative to `_AssetSource/DFB_UI/DFB_UI_Assets/`)
* `UI-1/components/.../styleguide_components/styleguide/logo/draft-fever` and `.../logo/bgs` (logo + its plate)
* `UI-1/exports/logos_game_pro_and_college_teams/logos/ui/logo/draft-fever`, `.../logos/image_3` (same wordmark), and the `logos` / `frame_846` sheets that embed it (not copied; individual crests were taken instead)
* `UI-1/exports/styleguide_and_screen_flow/home/ui/logo/draft-fever`, `UI-1/exports/misc_drafts/wip_refs/ui/logo/draft-fever`
* `UI-1/components/.../jock_cards/ui/card/jock/common/content/logo` (DFB logo on card)
* `UI-1/components/.../games_lobby/title-strip-text/property_1-fever|ranked|season`, `.../games/modes/type-fever`, `.../modes/group/feverbowl-locked|unlocked`, `UI-1/exports/.../games_lobby/ui/screen/games/feverbowl-*`, `UI-2/components/games_fever_bowl/*`
* `UI-3/exports/page_1/draft_house/draft_ngiht_logo` (Draft Night logo with the DFB runner mark)
* Sealed card packs (`draft/ui/new-card/*`, `scouting/team/cardsealed-*`, `draft/night/table/row/states/*pack-sealed*`): DFB logo on the pack art
* Championship ring art (`jock_cards/ui/jock-badges/rings/*`, `wc-wins-*`): the ring face carries the DFB runner mark
* All player-card/portrait images (`UI-3/page_1/player_cards_old|updated/*`, `jock_cards/.../model|thumb|variation|side-front*`, `draft/.../cardrevealed`, `home/.../season-pass`, `.../dev-news`): jerseys print "Draft Fever"; some use watermarked stock photos
* `UI-3/page_1/regular_season/image_season_leaders_01|02`: Adobe Stock watermarked photos
* Real-venue / NFL photography: `backgrounds_and_textures/stadium_top_down_1`, `backgrounds_and_textures/backgrounds` sheet, `simulation/tackle`, `simulation/masked_bg`, `simulation/bg_copy_*` (real Chiefs/Eagles photos), `ui/screen/gameplay/*` photo mocks
* Real-school-mascot team wallpapers (`ui/bg/team/side-left|right`)

Where DFB branding is visible only inside a full-screen reference we kept (31 `Screens/ref_*` rows, flagged `DFB logo/branding visible`): every UI-1/UI-2 screen (bottom-right wordmark and "Draft Fever update" ticker), `ref_pause_menu` (logo top-left + scorebug), `ref_draft_night` (Draft Night logo), `ref_menu_home`, `ref_roster_starters` (jersey print).

---------------------------------------------------------------------------------------------------

## 7. Legal / naming cautions
* Logo sets are the user's fictional crests, but several file names use **real school/state names + real-world mascots** (Notre Dame Leprechauns, Texas Bisons, Michigan Badgers...) and a few college logos strongly resemble real schools (e.g. Wisconsin badger, Purdue train, Ohio State "Brutus"). ARCHITECTURE rule 6 (original names only): keep the PNGs, **rename teams in data**, and treat the college pool as numbered generic crests (`Logos/College/college_001..143`); named files are `college_<name>.png`.
* Pro crest files are named `pro_<state>_<mascot>.png`; the game should assign its own city/mascot strings.
* Everything under `_Licensed` stays git-ignored; do not reference GUIDs from committed assets (use `ArtLibrary`).
* The reference screens are for layout only and must not be redistributed.
