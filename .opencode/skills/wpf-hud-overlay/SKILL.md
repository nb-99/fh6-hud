---
name: wpf-hud-overlay
description: "Use when creating, styling, or fixing the HUD overlay window(s) in this repo — transparency, always-on-top, click-through, frameless windows, DPI awareness, 60 Hz UI updates, and WPF rendering performance for telemetry displays."
---

# WPF HUD Overlay Patterns

Reference for building game-overlay windows in WPF (.NET 10, `net10.0-windows`,
`UseWPF`). Apply these patterns to `MainWindow` and any future HUD panels.

## Window setup

For a transparent, always-on-top, frameless overlay:

- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`,
  `Topmost=True`, `ShowInTaskbar=False`, `ResizeMode=NoResize`,
  `SizeToContent=WidthAndHeight`.
- Draw the actual HUD inside a semi-transparent panel (e.g. a `Border` with
  `Background=#C0202020`), not the window itself.
- `AllowsTransparency=True` forces software composition of that window; avoid
  heavy effects (Blur/Shadows/DropShadow) inside it — they are slow. Use
  flat/solid colors and simple opacity.
- Position with `WindowStartupLocation=Manual` + `Left`/`Top` in
  `OnSourceInitialized`, relative to `SystemParameters.WorkArea`.

## Click-through (input passes to the game)

With `AllowsTransparency=True`, use window styles via `SetWindowLongPtr` in
`OnSourceInitialized`:

- `WS_EX_TRANSPARENT (0x20)` + `WS_EX_LAYERED (0x80000)` → fully click-through.
- `WS_EX_TOOLWINDOW (0x80)` → hidden from Alt-Tab/taskbar.
- Remove `WS_EX_TRANSPARENT` to make the window interactive (dragging, menus),
  re-add it to go back to click-through. Re-applying styles requires
  `SetWindowPos` with `SWP_FRAMECHANGED (0x20)`.

## Rendering loop (60 Hz telemetry)

- `CompositionTarget.Rendering` fires once per WPF frame and is the right
  place to refresh HUD values from the latest parsed frame; it is smoother
  than a `DispatcherTimer` and keeps updates aligned with vsync.
- The UDP listener must NOT touch UI elements: it runs on a background
  thread/async loop and publishes the latest frame; the UI reads it on the
  render tick (lock or `Interlocked` swap the reference).
- Avoid allocation churn in the render tick: reuse `StringBuilder`, pre-format
  strings only when values change, avoid LINQ, avoid creating brushes per frame
  (freeze and cache brushes).

## Style conventions for this repo

- Dark HUD panel (`#C0202020`), monospace digits (`Consolas`), accent colors
  defined once in `App.xaml` resources.
- Text updates at 60 Hz: set `TextBlock.Text` directly from code-behind on the
  render tick; do not rebuild visual trees per frame.
- If a section needs binding later, prefer `INotifyPropertyChanged` view
  models with throttled notifications; do not mix both patterns in one panel.
