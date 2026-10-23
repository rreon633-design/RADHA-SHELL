# 4. Target Audience

## 4.1 Primary persona — "Max, the desktop builder"

- Runs Windows 11 with a curated wallpaper set, multiple monitors, a mechanical keyboard.
- Previously used Rainmeter, TranslucentTB and PowerToys; has tried Caelestia/Hyprland.
- Wants: wallpaper-derived theming, a fast launcher, live system stats, one control surface.
- Pains: Start menu is slow and cluttered, settings are scattered, existing widgets look static.
- Success for Max: everything re-tints from a wallpaper change; the dashboard opens instantly;
  `radha` commands are scriptable.

## 4.2 Secondary persona — "Aarav, the developer"

- Works in terminals and editors all day; wants a launcher that understands `>` commands.
- Wants: keyboard-first flow (`Win+Space` launcher), media control without leaving the keyboard,
  CPU/GPU/temperatures at a glance, a CLI that mirrors every UI action.
- Success: binding `radha media play-pause` into a script works identically to the button.

## 4.3 Secondary persona — "Rhea, the themer"

- Spends time on colour harmony and wallpapers; picks wallpapers for their palette.
- Wants: seven Material 3 scheme variants, exact hex values exposed, per-monitor overrides.
- Success: `radha scheme extract image.png --json` gives her the 35 roles to paste elsewhere.

## 4.4 Anti-personas (who this is not for)

| Not for | Why |
|---|---|
| Users who want a full Explorer replacement | out of scope; use StartAllBack/ExplorerPatcher |
| Enterprises with locked-down devices | unpackaged app + package managers required |
| Tablet/touch-first users | desktop-first design |
| People who want a web dashboard | the product is deliberately native |
| Anyone wanting a custom lock screen | excluded by decision |

## 4.5 Assumptions about the audience

- Comfortable installing a portable app and optionally editing a JSON file.
- Has a Windows 11 machine with a GPU that supports Composition (any DX11-class GPU).
- Accepts that a first Windows build may need `dotnet build` (this is a developer-facing project
  until installer phase 3).
