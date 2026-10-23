# 7. Design Philosophy & UI/UX

## 7.1 Movement

Every surface moves with intent, never with decoration.

| Element | Motion | Implementation |
|---|---|---|
| Panel open/close | spring, 0.92 damping / 0.46 s period | Composition animation list |
| Workspace pill | width + colour morph, 0.85 / 0.32 | Composition + `Morph.hlsl` |
| Tile press | scale 0.97 on pointer-down, spring back | Composition expression |
| OSD | fade + 8 px rise, auto-hide 1.6 s | `SpringSpec.Gentle` |
| Theme change | 650 ms cross-fade of colour roles | brush replacement + transition |

Spring presets live in `Radha.Core/Motion/SpringSpec.cs` and are unit-tested (settling time,
step response, clamping). `SpringSpec.Reduced` is used when the user enables reduced motion —
animations become near-instant rather than being removed, so layout never jumps.

**Rules**
1. No animation longer than 550 ms on a panel; no animation on a keystroke-triggered state.
2. Animations are additive: the UI is usable at the 0-frame (state changes immediately).
3. Nothing loops except the media visualiser and the boot indicator.

## 7.2 Colour

Material 3 semantics, not ad-hoc hex values:

- **Primary** — active states, selected pills, focus, main action.
- **Primary container** — tiles in the "on" state, chips.
- **Surface containers (lowest → highest)** — elevation ladder for cards inside panels.
- **On-* roles** — text/icon colours guaranteed ≥ 4.5:1 against their container (asserted in tests).
- **Error** — destructive session actions only.
- **Outline variant** — 1 px borders that survive both light and dark.

Wallpaper drives the palette. Seven variants let the user choose how literal the mapping is:
TonalSpot (default), Vibrant, Expressive, Fidelity, Content, Neutral, Monochrome.

## 7.3 Typography

- Display: Segoe UI Variable Display, SemiLight, tight letter-spacing for temperature/uptime numbers.
- Body: 11–12 px for labels, 9–10 px for secondary text (deliberate density — this is a shell,
  not a document).
- Mono: Cascadia Code for uptime, timers, and JSON output in the CLI.
- Numbers are tabular (`font-variant-numeric: tabular-nums` equivalent) so digits don't jitter.

## 7.4 Layout & density

- Bar: 42 px, three zones (left identity, centre media, right status).
- Panels: 1180 × 524 max, 30 px corner radius, 14 px padding, 12 px gutters.
- Tiles: minimum 96 × 72, 3–12 per grid depending on surface.
- Density scales with `appearance.scale` (0.6–2.0) and `roundingScale`; the token system carries it.

## 7.5 Interaction rules

1. **Every UI action has a command.** If a button exists, `radha <module> <command>` exists.
2. **One panel at a time.** Opening the dashboard closes the control centre.
3. **Click-away closes.** Scrim tap or `Esc`.
4. **Hover reveals, click commits.** Edge-hover opens the dashboard after a configurable delay.
5. **Keyboard first in the launcher.** `↑/↓` move, `Enter` opens, `Esc` clears, `>` switches to
   actions, `@` reserved for specials.
6. **No modal dialogs** except destructive session actions (which confirm).

## 7.6 Accessibility

- Colour pairs are contrast-checked in code (`MaterialScheme.HasReadableContrast`).
- Focus visuals are preserved on all interactive controls.
- Reduced motion honoured via `SpringSpec.Reduced`.
- Text scales independently (`appearance.fontScale`) from layout (`appearance.scale`).
- Known gap: UI Automation names are not yet authored for every custom control — tracked in
  [ROADMAP.md](ROADMAP.md) phase 3.

## 7.7 What the design explicitly avoids

- No glassmorphism noise: one blur layer, one grain layer, both subtle.
- No bouncing icons, no parallax, no hover-tilt.
- No walls of settings inside the shell — configuration lives in JSON, the UI shows state.
