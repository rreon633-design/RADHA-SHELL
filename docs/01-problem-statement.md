# 1. Problem Statement

## 1.1 The current state of Windows desktop customisation

Windows 11's shell is monolithic. Beyond accent colour, light/dark mode and taskbar alignment,
Explorer offers no supported extension point for the surfaces power users actually want:
a unified quick-settings shade, a live dashboard, an app launcher with commands, media transport
with a visualiser, and a coherent re-theming pipeline driven by the wallpaper.

Users who want those things today assemble them from tools that each solve one slice:

| Existing approach | Critical limitation |
|---|---|
| **Rainmeter** | GDI-based rendering, no GPU composition, static widgets, INI/Lua scripting, no morphing animation, no shell surface |
| **Cairo Shell** | WPF (legacy), no Material 3, no dynamic theming, minimal animation, stagnant |
| **TranslucentTB** | Single purpose (taskbar transparency) |
| **StartAllBack / Start11** | Proprietary, limited to Start-menu/taskbar reskins, no dashboard/launcher/OSD |
| **Lively Wallpaper** | Wallpapers only, no shell integration |
| **EliteBar / Zebar** | Electron/WebView2: heavy runtime, no native composition, status bars only |
| **PowerToys** | Utility collection, not a shell |
| **WSL + a Linux shell** | Cannot render native layered windows or touch DWM |

## 1.2 The gap

No Windows project combines all of:

1. GPU-composited, spring-driven animation through the Windows Composition API
2. Material 3 dynamic colour derived from the user's wallpaper
3. A complete panel ecosystem — bar, dashboard, launcher, notification centre, OSD, session
4. Real OS integration — media transport (SMTC), sensors, radios, virtual desktops, app-bar docking
5. A modern declarative UI stack (WinUI 3 / XAML), not a web runtime
6. Extensibility through a command/IPC surface and JSON configuration

Caelestia Shell demonstrates what this feels like on Linux/Hyprland. Windows has no equivalent.

## 1.3 Core problem

Windows power users and customisation enthusiasts lack a **native, fluid, Material 3-themed,
modular desktop shell** that behaves like a modern quick-settings shade and app drawer, without
resorting to web technologies, abandoned frameworks or fragile hacks.

## 1.4 Why the obvious shortcuts were rejected

- **Electron/Tauri/WebView2**: the shell would be a browser window. It cannot participate in DWM
  composition, cannot be a click-through layered window cheaply, and adds 150–250 MB of runtime to
  something that should sit idle at <1% CPU.
- **A Rainmeter skin**: no GPU animation, no real integration, no maintainable codebase.
- **Patching explorer.exe**: unsupported, breaks on feature updates, and puts the user's session at
  risk. RADHA SHELL deliberately does **not** replace Explorer.

## 1.5 Scope boundary

In scope: the overlay shell (bar, panels, OSD, theming, IPC, services).

Out of scope by decision: Explorer replacement, tiling window management, plugin marketplace, and
**any lock-screen / credential-provider / biometric-authentication feature**. The lock screen is
excluded at the product owner's explicit request and no such code exists in this repository
(enforced by a CI job — see [17-security.md](17-security.md)).
