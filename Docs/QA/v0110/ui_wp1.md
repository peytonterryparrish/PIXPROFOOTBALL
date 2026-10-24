# UI-WP1 Foundation: Direction 16 "Tile Hub Modern" (v0.11.0)

Owner: UI-WP1. Spec: `D:\PIXFootball\Work\UI16\SPEC.md`. Mockups: `D:\PIXFootball\Renders\ui_directions\16_tile_hub_modern\`. Shots: `D:\PIXFootball\Renders\ui16\`.

## API summary (landed first so WP2 / WP4 / WATCH+HUD build against it)

Nothing below changes an existing signature. Tile belongs to the glass family: `UiTheme.IsGlass` is true for it, so every `if (UiTheme.IsGlass)` branch keeps working; `UiTheme.IsTile` is true only for Tile.
Write screens once: call the same `GlassKit` / `GlassWidgets` calls and read colours from `GlassTheme.T`. In Tile they draw the flat tile look.

### Theme
| What | Where |
|---|---|
| `UiThemeKind.Tile` (= 2; Glass 0, Classic 1 unchanged), `UiTheme.IsTile`, `-pixUiTheme tile` | `Glass/UiTheme.cs` |
| `UiTheme.Names = { Glass, Classic, Tile }`, index == enum value, so `SettingsPanel` needs no change | same |
| `GlassTheme.Current` / `GlassTheme.T` return the TileTheme tokens while Tile is active (`GlassTokens.Tile()`, asset `Resources/PixGlass/TileTheme`) | `Glass/GlassTheme.cs` |
| Tile tokens (gold, goldHi, onGold, bg*, topBar*, ticker/footer, tileFill, panelFill/Head, rowFill/Focus, tabOff, border*, hairline, shade, tagPass/Run, good/bad, field*, glyph*, sizes, motion, layout) | `Glass/GlassTokens.cs` (`t.flat` is true in Tile) |
| Existing tokens carry the tile values: `accent` = gold #F2A63A, `accentHot` = #FFCF7A, `textOnAction` = #17130A, corner radii 0, `edgeA/B/C` = border / panel border / hairline | |
| Tracking is TMP hundredths of an em: `GlassTokens.Em(px, fontSize)` converts the mock's CSS letter-spacing | |
| `UiColors.*` follow the tile tokens while Tile is active | `UiKit.cs` |

### Components (all in `PixFootball.UI.Glass`)
| Need | Entry point | Notes |
|---|---|---|
| Sizes and bands | `TileMetrics.Hero / Standard / Wide / WideShort / HalfTall / Half / Large / LargeShort`, `TopBar 112`, `Ticker 46`, `Footer 62`, `ContentTop 138`, `BottomClear 108`, `PlayCardW/H`, ... | `GlassKit.Tile.cs` |
| Screen backdrop | `GlassKit.TileBackdrop(parent)` | gradient + top glow, full-bleed; first child of a screen |
| Top bar with skewed tabs | `GlassWidgets.TopBar(parent, section, tabs, selected, onTab, bumpers)` (same signature) then `GlassWidgets.SetPlayerChip(nav, name, level, px, crest)` | 112 band, wordmark, tabs with LB / RB caps; 25 unit tabs from 6 tabs |
| Tab bar alone | `GlassKit.TabBar(parent, names, selected, onChanged, small, bumpers)`; `GlassKit.Tabs(...)` also returns skewed tabs in Tile | `GlassTabs.SetIndex / Changed / Buttons / Content` (set `Content` = panel that crossfades 0.20 s with a 24 unit LB / RB drift) |
| Underline tabs (play-call strip) | `GlassKit.StripTabs(parent, names, selected, onChanged)` | 22 caps, light gold + 4 unit gold underline |
| Tile (hero, standard, half, wide, flat, art, locked) | `GlassKit.Tile(parent, HubTileSpec{ Title, Subtitle, Badge, ArtSprite / ArtTexture, ArtFocal, IconKey, Flat, Locked, Hero, Pos, Size, Click })` | returns `HubTile` (Cell, Button, Center, Size); `TileFactory`; focus = 4 unit gold edge, ring, glow, sheen, scale 1.035 / hero 1.02, raised above neighbours |
| Ticker | `GlassKit.Ticker(parent, headlines, "NEWS")` or `GlassTicker.Build(parent, "NEWS", 46, 20)` + `SetHeadlines` | gold NEWS block, 96 units/s, pauses with a dialog, hidden when empty, `Tick(dt)` allocates nothing |
| Footer / glyph legend | `GlassWidgets.BottomBar(parent, rightText)` then `footer.Prompts.Set(PromptFooter.Item("glyph_a", "SELECT"), ...)` | A green, B red, X blue, Y gold discs, LB / RB / LT / RT grey caps, keyboard caps; live device switch |
| List row | `GlassKit.ListRow(parent, title, sub, width, height 124, onClick, thumb)` -> `GlassListRow` (`Thumb`, `SetTag`, `Selected`, `Title`, `Sub`) | focus fill + gold bar |
| Pause rail row | `GlassKit.MenuItem(...)` (same signature) | row look in Tile |
| Tag chip | `GlassKit.TagChip(parent, "PASS", TagKind.Pass / Run)` | |
| Badge chip | `GlassWidgets.Chip(parent, "FEATURED", true)` | |
| Panel with dark head | `GlassWidgets.Card(parent, title, w, h, name, shadow, density, goldRule)` -> body rect | |
| Static table | `GlassKit.TableBlock(parent, group, headers, rows, weights, width, highlightRow)` | interactive lists stay `GlassTable` |
| Stat tile | `GlassWidgets.StatTile(...)` | |
| Play card | `GlassKit.PlayCard(parent, title, tag, formation, w, h, onClick)` -> `Field` rect for the diagram | scale 1.04 on focus |
| Option rows | `GlassKit.Stepper / ToggleRow / SliderRow`, `GlassWidgets.Dropdown` (same signatures) | row look, gold bar, square track |
| Modal | `GlassWidgets.Modal(root, title, w, h)` -> `Box`, `Body`; `GlassWidgets.BuildDialog` (same signature, used by `UiDialog`) | |
| Toast | `GlassToast.Create(parent, bottom)` / `GlassKit.Toast` | gold bar, 0.2 s in, hold, 0.2 s out |
| Glyph pieces | `GlassKit.GlyphDisc("A", size)`, `GlassKit.GlyphCap("LB", height)` | |
| Frame surface | `GlassKind.Frame`, `GlassGraphic.FocusEdge`, `GlassGraphic.Skew`, `GlassFocusable.RaiseTarget`, `GlassFocusable.ShowState(focus, enabled)` | for custom tiles / art over a border |

Surface kinds in Tile: `Glass` = tile body, `Panel` = panel, `Row`, `Tab` / `TabOn`, `Key`, `Frame`, `Well` = dark head, `Solid` = flat tint, `Action` / `Accent` = gold plate.
`GlassKit.Panel(...)` already returns the Panel kind in Tile, so code that builds panels with `GlassKit.Panel` / `Surface` / `Solid` / `Well` is restyled for free.

### For the other tracks
- WP4: `GlassTable` keeps working (backing is the Panel kind); `GlassKit.Rule` is a 1 unit hairline in Tile; header text sizes are yours (20 minimum).
- WP2: `GlassKit.Tile` is optional; your `MenuTile` keeps working. If you want the border drawn over the art, put a `GlassKind.Frame` surface on top (see `TileFactory`).
- WATCH+HUD: `GlassKit.PlayCard`, `GlassKit.StripTabs`, `GlassKit.ListRow`, `GlassKit.TagChip`, `TileMetrics.Strip*`.

(The measurements, shots and acceptance results are appended below as they are produced.)

## Results (2026-10-23)

**Status:** foundation done. `UiTheme.DefaultKind` is now **Tile**; Glass and Classic still build (`ThemeSwitchKeepsAllThreeLooksBuildable`). Existing stored choices (Glass / Classic) are kept.

| Item | Result |
|---|---|
| Tests | `PixFootball.Tests.Presentation` 166 / 166 pass (private copy = git HEAD + WP1 files). New: `TileUiTests` (23): tokens vs spec, contrast, theme switch, no blur, tab plate, tile / locked, list row, ticker 0 GC + wrap + hidden when empty, footer, min text size, modal, prefabs + licence |
| Contrast (tested) | white on every tile / panel / row fill >= 7:1; muted #8D919B on row fill and footer >= 4.5:1; idle tab text >= 4.5:1; onGold on gold and light gold >= 7:1 |
| Shots | `D:\PIXFootball\Renders\ui16\` : `tile_p0..p4_1920x1080.png` (hub, franchise, play-call strip, controls, states) plus p0-p2 at 2560x1440, 3840x2160, 3440x1440, 1440x1080 |
| Scaling audit (1080p, 1440p, 4K, 21:9, 4:3) | `tile_audit.txt`: text below 20 units 0, text overflow 0, autofit hits 1 per page |
| Controller | `navaudit.txt`: every selectable reachable on all 5 pages and all sizes |
| GC | ticker 0 bytes / 600 frames, focus easing 0 bytes / 600 frames (also unit tests) |
| UI CPU (editor, UI camera submit) | steady canvas update 0.004 to 0.022 ms; camera submit is dominated by HDRP's fixed cost (25 to 55 ms, run-to-run jitter in the editor): hub 25 to 44 ms vs Glass hub sheet 33 to 36 ms, i.e. equal within noise. Tile has no blur pass (Glass: about 2.4 ms GPU in `glass_ui.md`) |
| Draw calls (hierarchy-order estimate) | hub 90 (Glass sheet 88), franchise 71, play call 97, controls 82, states 79 |
| SDF | PIX_Barlow_Bold + Medium: 1 atlas 1024x1024 each, 181 glyphs, static (no runtime generation), 0 missing of the Latin set; diamond and arrows from the DejaVu fallback |
| Font licence | Barlow Semi Condensed = SIL OFL 1.1 (licence string is in both TTFs' name table). TTFs + `LICENSE-Barlow-OFL.txt` are committed in `UI/Glass/Fonts`; the TileTheme asset references no `_Licensed` file |
| Licensing | tile prefabs (34, `UI/Glass/Prefabs/Tile`) never reference `_Licensed` or logos (test); gallery art is procedural; no real marks, no Draft Fever logos |

Not measured / not done: A-press to next-screen time and buttons-to-reach-page need the real screens (WP2 / WP4); play-mode player run of the hub with real art; footer 2-row wrapping (legend fits one row at all sizes).

Notes for integration: `MenuHubTests.TickerJoins...` now accepts the diamond separator (one line). `GlassImageSkin` leaves Images marked `GlassNoSkin` alone (`GlassKit.Plate`, `GlassKit.Bar`); in Tile it maps panels to the Panel kind and selected rows to the Row kind. Shaders: `fragFlat` in `PixGlassUI.shader` is the Tile branch (material `_Flat = 1`, no backdrop sampling).
