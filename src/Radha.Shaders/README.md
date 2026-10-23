# Shaders

Three HLSL effects, compiled with `dxc -T ps_6_0` (or Win2D's effect compiler) and applied
through the Windows Composition API:

| File | Purpose | Applied to |
|---|---|---|
| `Blur.hlsl` | dual-Kawase backdrop blur | dashboard sheet, control centre, launcher |
| `Morph.hlsl` | rounded-rect SDF with per-corner radii | workspace pills, OSD capsule, session card |
| `Grain.hlsl` | grain + vignette | all glass surfaces |

They are **not** wired into the app in phase 1: WinUI's built-in `DesktopAcrylicBackdrop`
plus `SystemBackdrop` already gives a native blur, and composition effects require
precompiled `.cso` assets. Phase 2 adds:

```powershell
dxc -T ps_6_0 -E main src\Radha.Shaders\Blur.hlsl -Fo assets\Blur.cso
```

and a `CompositionEffectBrush` factory in `Radha.App/Composition/EffectFactory.cs`, with
the radius animated by a `ScalarNaturalMotionAnimation` spring.
