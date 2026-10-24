# UI-WP4 Franchise: Direction 16 "Tile Hub Modern" (v0.11.0)

Owner: UI-WP4. Spec: `D:\PIXFootball\Work\UI16\SPEC.md` section 4. Mock: `D:\PIXFootball\Renders\ui_directions\16_tile_hub_modern\third.png`. Shots: `D:\PIXFootball\Renders\v0110_franchise\` (`fr_<page>_1080.png`, `fr_<page>_219.png` for every page, `_1440`, `_43`, `_4k` for nine key pages, `navaudit.txt`). Tool: `Editor/Tools/FranchiseShots.cs` (new; `PIX_FR_SIZES` / `PIX_FR_SHOTS` env vars or `-pixSizes` / `-pixShots`).
No logic or data changes: every edit is layout, tokens, fonts and navigation wiring. College / Road to Heisman: nothing in `UI/Franchise` shows it (the player card prints the fictional school of a player and the draft board its prospects' schools, both plain data).

## What changed (files, all under `Scripts/UI/Franchise` unless noted)
| File | Change |
|---|---|
| `FranchiseUi` | New shell. Tabs HOME / NEWS / ACTIONS / OWNER / TEAM / LEAGUE (Madden ref 45) as the skewed `GlassKit.TabBar` with LB / RB caps, team chip (`GlassKit.PlayerChip`), sub-tab row = `GlassKit.StripTabs` (LT / RT), tabs without sub pages get a 30 unit title, caption at the right, full-bleed ticker (46) over the footer (62), design block 1800 x 800 centred (60 margins, never scaled below 1 on 16:9, 21:9, 4:3), 0.20 s crossfade with 24 unit drift on tab / sub change, gold plate snap in edit renders. |
| `FranchiseController` (`FranchiseNav`) | Tabs / sub tabs re-mapped: NEWS tab = old League > News, ACTIONS = cap, re-sign, free agency, trades, staff, OWNER tab = old Office > Owner, TEAM and LEAGUE unchanged (minus News). `Office` kept as an alias of `Actions`. |
| `FranchiseKit` | Every text helper clamps to 20 units (`Lay.MinText`), tracking arguments are CSS px converted with `GlassTokens.Em`, `Lay.Head` (dark head strip + gold hairline), `Lay.W/H` content size, action sheet / info modal heads, disabled sheet rows skipped by the pad (fixes an unreachable row). |
| `GlassTable` | Head 52 with the dark head fill and gold rule, 20 unit tracked headers, 24 unit bold rows, cursor row = rowFocus fill + 4 unit gold bar (dimmed when another control has focus), user row gold 16 percent fill, gold scroll thumb. |
| `HomePage` | Rebuilt on the mock: 900 x 410 hero tile (matchup or offseason step, gold Play / Sim buttons), three 286 x 292 status tiles (team, cap and roster, inbox), standings panel with DIVISION / CONFERENCE / LEAGUE tabs (crest rows, user row gold), owner goals panel. Pad wiring hero > tiles > standings tabs, up goes to the tab bar. |
| `TeamPages`, `LeaguePages`, `OfficePages`, `MarketPages`, `DraftPage`, `BoxScoreView`, `PlayerCardPanel`, `FranchiseTeamUi` | Re-laid out for the 1800 x 732 content area, tile body kind for clickable tiles, panel heads, bigger owner page text, wider OVR / POTENTIAL boxes. |
| `Tests/EditMode/Presentation/FranchiseUiTests` | Table height uses `GlassTable.HeaderHeight`. |

## Acceptance (SPEC section 4)
| Check | Result |
|---|---|
| Scaling 1920x1080, 2560x1440, 3840x2160, 3440x1440, 1440x1080 | Home, roster, depth, standings, schedule, trades, re-sign, draft and box score rendered at all five: nothing clipped or overlapping, block centred, ticker / footer full bleed. All 29 pages at 1080p and 21:9. |
| Text below 20 units | None: all helpers clamp to 20, table cells 24, headers 20; long texts ellipsize at 20 instead of shrinking (button labels shrink only inside `GlassKit.Button`, WP1). |
| Controller | `navaudit.txt`: every franchise screen "all reachable", 0 UNREACHABLE (the action sheet row bug found and fixed). LB / RB tabs, LT / RT sections, B backs one level, focus returns to the opener (sheets, modals). |
| Presses from HOME to a sub page | Tab ring of 6 plus sub ring (wrap): NEWS 1, OWNER 3, TEAM pages 2 to 3, LEAGUE pages 1 to 4 (Awards 4), ACTIONS pages 2 to 4 (Free agency 4, Trades 4). Two pages exceed 3 by one press; the hub tiles deep-link (cap tile, inbox to trades, hero button to re-sign / FA / draft / awards) so the common ones are 1 press. |
| Contrast | All colours come from the Tile tokens (white on tile, #8D919B muted, onGold on gold: WP1's contrast tests). No local colours except black head / zebra tints. |
| Licensing | No NFL / Madden / Draft Fever marks in the franchise code or shots (crests only; wordmark is the menu's). |
| Behaviour | No logic changes. FranchiseUiTests build every page of every tab on a real career. |

## Compared once against the mock (third.png vs fr_home_1080)
Same bones: skewed tab bar with gold plate and LB / RB caps, 900 unit hero tile with Play button, three tiles under it, standings panel with underline tabs and gold user row, owner goals, gold NEWS ticker, glyph footer. Differences: no game-render art behind the hero (needs WP1's art plates; the team colour wash is used), no "Media statements" tile (not in the career data), standings show 4 rows per scope instead of two division groups (the owner panel takes the rest), hero offseason step shows a pip bar instead of a big image.

## Still wrong / open
- Settings: there is no in-franchise settings page in the code (career options live in `CareerStartPanel`, WP2); nothing to restyle here.
- The standings, awards, records and news pages are plain tables with large empty areas early in a career (data, not layout).
- The stray tab sliver left of "HOME" behind the LB cap and the dark NEWS block of the ticker in some edit-mode renders are WP1 foundation details (`GlassKit.TabBar`, `GlassTicker`).
- `CareerUiShots` (not mine) still addresses the old tab indexes; `FranchiseShots` replaces it.
- FaOfferSheet keeps its own gold kicker instead of a head strip.

## EditMode run (private copy: git HEAD + WP4 + WP1's in-progress foundation, `-testCategory "!Slow"`)
1269 of 1270 pass, including every FranchiseUiTests case. The one failure is `MenuHubTests.TickerJoinsHeadlinesWithAnAccentSeparator`: WP1's new ticker uses diamond separators, so that test needs WP1's update (not a franchise test).
