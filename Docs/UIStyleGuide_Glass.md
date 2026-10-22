# PIX Football UI style guide: Glass theme

The Glass theme is the second UI skin of PIX Football (the first is Classic, described in `UIStyleGuide.md`). It is a clean, glassy, modern, high resolution look: graphite translucent panels over a live blurred 3D scene, a thin silver edge, a diagonal reflection, white type and one restrained orange accent. The two themes share every screen, behaviour, navigation path and data wiring. Only the surface language changes.

Switch with Settings > Game > "UI theme (next menu)" (stored in PlayerPrefs `pix.uitheme`; takes effect on the next menu or scene) or pass `-pixUiTheme glass` (or `classic`) on the command line for one run. `UiTheme.DefaultKind` is **Glass** since v0.7.0 (every screen verified; Classic stays selectable). The v0.7 canvas scaler is "Expand" (the 1920 x 1080 reference rectangle is always fully visible), see `Docs/QA/v070/ui_controls.md`.

All lengths are canvas units on the 1920 x 1080 reference canvas (Canvas Scaler, match height 0.5). Values live in `GlassTokens` (pure data, JSON serialisable) and are exposed through the `GlassTheme` ScriptableObject (`Resources/PixGlass/GlassTheme`). No script hard-codes a glass colour, radius or duration.

## 1. Tokens

### Colour

| Token | Value | Use |
|---|---|---|
| surfaceTop / surfaceMid / surfaceBottom | #66666A / #242426 / #555558 | three-stop diagonal glass gradient (top left, middle, bottom right) |
| opacityTop / Mid / Bottom | 0.48 / 0.40 / 0.38 | glass tint opacity over the blurred backdrop |
| edgeA / edgeB / edgeC | #DBDBDE / #77777B / #E0E0E3 | silver hairline gradient |
| edgeWidth, edgeOpacity | 1.4, 0.9 | hairline thickness (canvas units) and strength |
| actionLeft / actionRight | #F3F3F3 / #C7C9CC | primary button (light glass) |
| frostDark / frostLight | #161719 / #333439 | static frosted fallback on the Low quality tier |
| textPrimary | #F5F5F5 | titles, values |
| textSecondary | #BFC1C4 | body, labels |
| textMuted | #8E9094 | hints, disabled |
| textOnAction | #1B1B1D | text on the light primary button |
| accent / accentHot / accentDeep | #F49A52 / #FFB070 / #C9722F | focus accent, active tab underline, progress fill, wordmark "PIX" |
| danger / success / warning | #E2685C / #86C9A0 / #F4C152 | penalties, positive deltas, cautions |

Contrast: `GlassTokens.Contrast` implements WCAG. `GlassUiTests` asserts textPrimary at least 6.5:1 on the brightest glass stop and 15:1 on the darkest, textSecondary at least 9:1 on the darkest, and dark type on the silver action surface at least 8:1.

### Glass material

| Token | Value | Notes |
|---|---|---|
| blurRadius | 16 (half-res pixels) | gaussian, `blurPasses = 2` |
| backdropDim | 0.58 | multiplies the blurred scene so white type stays readable on bright stadium shots |
| backdropDesaturate | 0.35 | keeps the glass graphite, not coloured by the pitch |
| refraction | 0.012 | tiny uv offset along the edge normal (lens feel) |
| sheen | 1.0 | diagonal reflection highlight strength |
| noise | 0.012 | fine film grain that hides banding |

Composition is done in gamma space (the pack's gradients were authored there; the project colour space is Linear). Material colours are Vectors so sRGB values are not converted.

### Shape and spacing

| Token | Value |
|---|---|
| cornerRadius / Small / Large | 12 / 8 / 18 |
| grid | 8 (all gaps, paddings and sizes are multiples of 8 or 4) |
| panelPadding / gutter / rowHeight | 28 / 16 / 64 |
| safeMarginX / Y | 96 / 54 (the 5 percent title-safe area) |
| focusScale | 1.025 |

### Typography

DejaVu Sans and DejaVu Sans Bold, bundled with the art pack, imported to `Assets/_Project/UI/Glass/Fonts` and baked into the TMP SDF assets `PIX_Glass_Title SDF` (Bold) and `PIX_Glass_Body SDF` (Regular). Text is always live TextMeshPro, never baked into sprites.

Licence: the Bitstream Vera licence with public domain DejaVu changes (`Assets/_Project/UI/Glass/Fonts/LICENSE-DejaVu.txt`). It permits embedding, redistribution and use in commercial products, provided the licence text ships with the fonts and the fonts are not sold on their own. The text is in the repository and in the build. The font may not be sold alone, which does not apply to us.

| Style | Size | Weight | Tracking | Use |
|---|---|---|---|---|
| Hero | 104 | Bold | 8 | loading and menu hero words |
| Title | 56 | Bold | 4 | screen titles, big numerals |
| Heading | 36 | Bold | 3 | panel headings |
| Body | 28 | Regular | 0 | paragraphs, rows |
| Label | 24 | Bold | 3 (caps) | buttons, tabs |
| Caption | 20 | Bold | 3 (caps) | eyebrows, hints, stat captions |

Minimum on-screen size is 20 canvas units (about 27 px at 1440p). Caps labels use tracking; mixed case body never does.

## 2. Motion

| Token | Seconds | Use |
|---|---|---|
| motionFast | 0.12 | focus glow, press, toggle thumb |
| motionBase | 0.16 | tab switch, row highlight, fades |
| motionSlow | 0.20 | panels revealing (fade, small slide and scale) |

All motion runs on unscaled time with ease-out or SmoothStep and never bounces. `GlassReveal` fades, slides (14 units) and scales (0.985 to 1) a panel in, with an optional per-element delay for staggering.

## 3. Gamepad focus

Gamepad first: every interactive element is a Selectable and every screen has an initial selection.

- Focused: the silver edge brightens, an orange accent glow rises around the shape (`exp(-d/7)`, scaled per element), the element scales to 1.025 and the label goes full white.
- Pressed: scale 0.985 and the surface brightens 12 percent.
- Disabled: 40 percent opacity, no glow, skipped by navigation.
- Navigation uses explicit links where a grid is involved (`GlassNav.GridMove`, 8 x 4 team crests) and automatic navigation elsewhere. LB and RB switch tabs. B backs out. The footer shows the real glyphs of the active device (`PromptFooter`).

## 4. Sound hooks

Existing `UiAudio` is reused, nothing new is authored: `UiAudio.Move()` on selection change and toggle or stepper changes, `UiAudio.Click()` on activate, tab switch and dropdown choose. `GlassFocusable.PlaySound` can mute a decorative element.

## 5. Real glass (the pipeline)

1. `GlassBlurPass` (HDRP CustomPass, AfterPostProcess) copies the camera colour, gaussian-blurs it into half-resolution RTHandles and publishes `_PixGlassBackdrop` (+ scale and on flag) as globals.
2. `PIX/UI/Glass` shader samples the blurred backdrop at the pixel's screen uv, dims and desaturates it, lays the tint gradient, the diagonal sheen, the silver edge and noise over it, and draws focus glow and a soft drop shadow.
3. The pass runs only while a glass surface is enabled (`GlassBackdropDriver`) and only on quality tier above Low. On Low the shader falls back to a flat frosted look (`frostDark`/`frostLight` gradient plus the same edge, sheen and noise) with no extra pass.
4. Behind the menus the scene is live: `GlassStage` loads `MenuBackdrop_Glass` additively (night stadium, crowd, floodlights, probes, volume) and slowly turns the camera around the photoreal player. `GlassPortraitStage` renders the player into a RenderTexture for player cards.

Surface kinds in the shader: Glass, Action (light primary), Solid, Well (inset dark), Accent (selected).

## 6. Component library

Scripts in `Scripts/UI/Glass/`, prefabs in `Assets/_Project/UI/Glass/Prefabs/`, all driven by `GlassTheme`.

Panel, well, solid, card, primary / secondary / accent / ghost button, menu item, tabs (underline), top bar and bottom bar (nav), toggle row, slider row, stepper (option selector), dropdown, modal dialog, toast, tooltip, progress bar, rating bar, stat tile, chip, compare row, player card (live portrait), crest tile and the controller-glyph footer (`PromptFooter`).

Prefabs never reference the licensed sprites: `GlassIconKey` stores a key and re-resolves the sprite from the `GlassIcons` registry at runtime. The registry lives in the git-ignored `Assets/_Licensed/Resources`.

## 7. How the packs intermix

The art collection has six packs and two preview sets. The decision: **Pack 6 Glass is the surface language for everything.** The other packs are mined for the things Pack 6 does not have (football icons, dense data layouts, scouting widgets), and everything borrowed is restyled to the glass tokens (graphite fill, silver edge, orange accent) and rebuilt as live components (TMP text, vector shader surfaces), never pasted in as baked bitmaps. Licensed art stays in `Assets/_Licensed` (git-ignored).

### Pack 6: 1000 Glass UI Assets (base)

| What we use it for | How it is adapted | What we do not use, and why |
|---|---|---|
| The whole surface language: translucent graphite panels, silver hairline edge, diagonal reflection, white type, restrained orange. Frames, buttons, tabs, inputs, toasts, tooltips, window frames define the component set. Its line icons (`fb_*`) are the base icon family. | Re-implemented as the `PIX/UI/Glass` shader and `GlassGraphic` (resolution independent, real backdrop blur) using the pack's `design_tokens.json` gradients and opacities. Icons rasterised from SVG to 128 px sprites by `Tools/Glass/rasterize_icons.ps1`. | Baked frame PNGs and 9-slice sprites: they cannot blur the live scene and soften at 4K. Baked sample text. Light-theme variants (we ship one graphite look). |

### Pack 1: 300 Clean Assets

| Use | Adaptation | Not used |
|---|---|---|
| Interface primitive icons (`ui_arrow_*`, `ui_close`, `ui_check`, `ui_info`, `ui_settings`, `ui_roster`) for nav chevrons, dropdown arrows, toggles, close buttons. Tab and banner proportions inform the underline tabs and the lower-third banners. | Recoloured to the silver icon colour (#E1E2E4) and rasterised at 128 px. Tabs use the underline-plus-accent pattern with the Pack 6 edge. | Its flat coloured buttons and banners (clash with glass), its stock photography frames. |

### Pack 2: 300 Management Assets

| Use | Adaptation | Not used |
|---|---|---|
| Sports symbols (`sym_*`): goalpost, penalty flag, yard chain, headset, stopwatch, weather, podium, medal. Used on the penalty panel, pre-snap strip, play clock and final screen. Layout references for roster and depth tables. | Silver line recolour; table rows follow the glass list row (64 high, hairline separators). | Its dense dashboards (the game has no front-office mode in this release). |

### Pack 3: 500 Brushed Assets

| Use | Adaptation | Not used |
|---|---|---|
| Layout ideas only: route diagrams (play-call diagram), star rating (rating bar), notification toast, dropdown and range selector shapes. | Rebuilt in glass; the play diagram keeps its own drawn graphic restyled with silver lines and category-coloured routes. | Brushed metal textures: they fight the soft glass and add noise at 4K. We tested them as an accent and rejected them. |

### Pack 4: 750 Modern Management Assets

| Use | Adaptation | Not used |
|---|---|---|
| Diagram structure: ring gauges and bars for the stat tiles, depth-chart layout for the play-call formation list, cap-bar and contract row proportions, nav and form controls for the settings page (steppers, sliders). | Hairline rules, flat graphite fills and orange emphasis match glass already, so they are rebuilt as `GlassBar`, `StatTile` and `CompareRow`. | Sankey, Gantt and cap diagrams (no screen needs them), and its frames (Pack 6 covers frames better). |

### Pack 5: 750 Scouting Assets

| Use | Adaptation | Not used |
|---|---|---|
| Scouting glyphs (`sc_*` and the football set: cleat, helmet, whistle, cone, binoculars, film reel, laces, field compass), the grade ring plus weighted bars and peer bars as the template for the player card and the postgame stat tables, the ledger table style for box scores. | Glyphs recoloured to silver; widgets become `RatingBar`, `CompareRow`, `StatTile` and the live-portrait `GlassPlayerCard`. | Draft board, report and radar templates: no scouting or draft mode exists yet, kept in the icon registry for the future. |

### Previews (80 screens)

Used as composition references: left-aligned stacked menu with a description card, hero lockup with big tracking, top bar with wordmark and bottom bar with glyph hints, 8 x 4 crest grids with HOME and AWAY slot cards, dense settings card with underline tabs, pause as a centred card, final screen as a score hero with a stat ladder. Nothing is copied pixel for pixel.

### Screen by screen map

| Screen | Components | Sources |
|---|---|---|
| Main menu | hero lockup, 4 menu items, description card, top and bottom bar, live stadium stage with turntable player | Pack 6 surfaces, previews layout, Pack 1 arrows |
| Mode select | menu items plus cards | Pack 6, Pack 5 football glyphs |
| Team select | HOME and AWAY slot cards, 8 x 4 crest tile grid, tabs TEAMS and MATCH OPTIONS, summary card, steppers | Pack 6, Pack 5 compare layout, previews |
| Settings (all pages) | dense card, underline tabs, toggle rows, slider rows, steppers, dropdowns | Pack 6 inputs, Pack 4 form controls, Pack 1 icons |
| Pause menu | centred glass card, menu items, state card, sound page | Pack 6, Pack 2 symbols |
| Coin toss caption | toss caption card | Pack 6 |
| Play call | formation list, set and play list rows, tag chips (category colours), diagram well, hint footer | Pack 6, Pack 3 route layout, Pack 4 depth layout |
| Pre-snap panel, audible list | card, keycap chips, tag chips | Pack 6, Pack 1 |
| Penalty panel | card, accept and decline buttons | Pack 6, Pack 2 flag symbol |
| Halftime and postgame shows, stat tables | stat tiles, compare rows, ledger table | Pack 5 ledger and peer bars, Pack 4 ring gauges |
| Replay HUD | transport bar, tag chip, keycap hint rows | Pack 6, Pack 1 |
| Final screen | score hero, stat ladder, buttons | Pack 6, previews |
| Loading screen | wordmark, matchup crests, progress bar, tip card | Pack 6, previews |
| In-match HUD | bottom bar scorebug (2K8 layout kept), top-left scorebug, banners, hint pill, markers, lower thirds, kick meter, flag strip, wind compass | Pack 6 surfaces with the game's own layout |
| Dialogs, toasts, tooltips | modal, toast, tooltip | Pack 6 |

## 8. Using the library

```csharp
var t = GlassTheme.T;                           // tokens
var card = GlassKit.Panel(parent, "Card", 1f);   // glass panel
var title = GlassKit.Text(card.rectTransform, "TITLE", GlassText.Heading, TextAlignmentOptions.Left);
var btn = GlassKit.Button(parent, "PLAY", onClick, GlassButtonStyle.Primary);
```

Existing Classic helpers (`UiKit.MenuButton`, `UiControls.Selector`, `UiDialog`) branch to the glass components when `UiTheme.IsGlass`, so screen code does not change. Flat Classic `Image`s that no helper builds are reskinned automatically by `GlassImageSkin` (classified as glass, well, solid or selected by colour; bars, sprites and scrims are skipped).

## 9. Do and do not

- Do keep one accent. Orange marks focus, selection and progress only.
- Do keep text on glass at textPrimary or textSecondary; textMuted only for hints.
- Do not put a glass panel over another glass panel with the same alpha; use `Well` or `Solid` for nested surfaces.
- Do not bake text into sprites and do not use real league, broadcaster or publisher names or logos.
- Do not add a second accent colour; team colours appear only in crests, bars under team names and category chips.
