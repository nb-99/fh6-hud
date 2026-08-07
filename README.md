# FH6 HUD

An always-on-top telemetry overlay for **Forza Horizon 6**, built with C# / WPF
(.NET 10). It reads the game's "Data Out" UDP stream (324-byte packets, up to
60 Hz) and displays tire temperatures, interval timers, and engine/power data
over the game window.

![stack](https://img.shields.io/badge/C%23-.NET%2010%20(WPF)-512BD4)

## Features

- **Movable panels** — the HUD is split into five independent panels
  (tires, engine, interval timers, speedometer, status). Drag any panel
  anywhere with the left mouse button; its position is saved to
  `config.json` as fractions of the screen, so the layout survives restarts
  and resolution changes. The default layout: tires bottom-left (20% in),
  engine bottom-right (right edge at 80% of screen width), interval timers on
  the right edge at 25% of screen height, speedometer below them, status
  bottom-middle.
- **Shift advisor with big shift lights** — the HUD learns the car's power
  curve and gear ratios from telemetry while you drive and computes the
  optimal shift points per gear (where the power you'd have after the shift
  equals the power in the current gear). The engine panel flashes a big
  "▲ UPSHIFT" pill (red, at ~full throttle) when you reach the upshift
  point, and a "▼ DOWNSHIFT" pill (blue, at ~half throttle) when a lower
  gear would make more power — e.g. stuck in a gear too high after a corner.
  Downshifts are only suggested when the post-shift RPM lands at least
  400 RPM below the lower gear's own shift point (and its redline), so the
  advice never bounces you off the rev limiter or into an immediate
  upshift/downshift loop — a little less power in the higher gear beats
  hitting the limiter. The title shows the learned point ("SHIFT @ 6400").
  It self-calibrates per car after a few full-throttle pulls — no car
  database.
- **Tire temperature monitor** — live per-corner temps (FL / FR / RL / RR),
  colored by whether each tire is *cold*, *in its optimal operating range*, or
  *too hot*, based on the selected tire compound.
- **Interval timers** — 0–100, 100–200, and 200–300 km/h, each auto-starting
  on the moment speed rises through the interval's start (braking can never
  trigger a run). Best time per interval is kept.
- **Engine & power** — current RPM, max RPM with a rev bar (redline zone),
  a live power curve built from telemetry samples (peak power per 100-RPM
  bucket), plus current and max power in PS with the estimated RPM at peak
  power.
- **Speed** — large current-speed readout and a 0–300 km/h progress bar.
- **Overlay behavior** — transparent, frameless, always-on-top panels, each
  draggable, and one hotkey (`Ctrl+Alt+H`) away from full click-through so
  nothing blocks the game. Right-click any panel for the context menu (tire
  compound, reset timers, click-through, quit).
- **Simulator** — develop/test without the game: streams synthetic telemetry
  (launch or cruise scenario).

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for
  running from source) — only the .NET 10 Desktop Runtime is needed to run a
  published build
- Forza Horizon 6 on PC

## In-game setup (FH6)

1. Launch Forza Horizon 6 and open **SETTINGS > HUD AND GAMEPLAY**.
2. Set **Data Out = On**.
3. Set **Data Out IP Address** to `127.0.0.1` (same PC) or the IP of the
   machine running the HUD.
4. Set **Data Out IP Port** to `45000` (must match `config.json`). Avoid
   ports 5200–5300 — the game binds its own outgoing socket in that range.
5. Set **Data Out Packet Format** to **Car Dash**. The shorter **Sled**
   format is not supported: the HUD only accepts 324-byte packets, so a
   Sled stream looks like "WAITING FOR TELEMETRY" forever.
6. Allow inbound UDP on that port in Windows Firewall if prompted.
7. Data is only streamed **while driving** — it stops in menus, replays,
   rewinds, and after finishing a race.

## Run the HUD

```sh
dotnet run --project src/Fh6Hud
```

The HUD listens on the `Port` from `config.json`. An optional `--port N`
argument overrides it for that process only (never saved back to
`config.json`). Add `"DebugLog": true` to the config or pass `--debug` to write
diagnostics to `hud.log` next to the executable. Run a test instance beside the
production app — e.g. fed by the simulator while the game keeps streaming to
the prod port — with:

```sh
dotnet run --project src/Fh6Hud -- --port 45001
dotnet run --project tools/Fh6Hud.Simulator --port 45001 --scenario launch
```

Five panels appear at their configured positions. Drag each one anywhere with
the left mouse button (the position is saved). `Ctrl+Alt+H` toggles
click-through so clicks pass to the game; right-click any panel opens the HUD
menu. While there is no live telemetry the content panels hide and the status
panel stays as a compact indicator.

### Run without the game (simulator)

```sh
dotnet run --project tools/Fh6Hud.Simulator --scenario launch --seconds 30 --rate 60
```

Scenarios: `launch` (repeated 0→300+ km/h pulls) and `cruise` (steady cruising
with tire warm-up).

### Tests

```sh
dotnet test Fh6Hud.slnx
```

## Configuration

All settings live in `src/Fh6Hud/config.json` (copied next to the executable
on build — edit the copy in the output folder to affect a published build):

| Key | Default | Meaning |
| --- | --- | --- |
| `Port` | `45000` | UDP port the HUD listens on (must match the game) |
| `DebugLog` | `false` | Write packet, render, state, panel, and exception diagnostics to `hud.log` |
| `TireCompound` | `Rally` | Compound preset used for the optimal temp range |
| `TireOptMinC` / `TireOptMaxC` | `72` / `90` | Manual optimal range override (°C) |
| `Panels` | *layout below* | Per-panel positions: `X`/`Y` are fractions of the work area (0–1), `Anchor` is the panel corner/edge those fractions refer to (`TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, `TopCenter`, `BottomCenter`) |

Default panel layout:

```json
"Panels": {
  "Tires":     { "X": 0.20, "Y": 0.80, "Anchor": "BottomLeft" },
  "Engine":    { "X": 0.80, "Y": 0.80, "Anchor": "BottomRight" },
  "Intervals": { "X": 1.00, "Y": 0.25, "Anchor": "TopRight" },
  "Speedo":    { "X": 1.00, "Y": 0.42, "Anchor": "TopRight" },
  "Status":    { "X": 0.50, "Y": 0.92, "Anchor": "BottomCenter" }
}
```

Positions are rewritten when you drag a panel; edit the file to set a layout
before first launch.

The tire compound can also be changed live via the HUD's right-click menu
(changes are saved back to `config.json`). Compound presets (Standard /
Street / Sport / Rally / Semi-Slick / Slick / Offroad / Snow / Vintage /
Vintage Race) and their optimal temperature ranges are defined in
`src/Fh6Hud.Telemetry/TireCompound.cs`.

> Note: FH6's Data Out packet does **not** include the equipped tire compound,
> so it must be selected manually. The preset ranges are editable starting
> points — calibrate them against in-game behavior.

## Repository layout

```
src/Fh6Hud.Telemetry/       telemetry library (plain net10.0, no WPF)
  Fh6Packet.cs              324-byte FH6 Data Out parser
  UdpTelemetryListener.cs   UDP receiver (background thread)
  SpeedIntervalTimer.cs     interval timer state machines
  PowerCurveTracker.cs      power curve sampling
  GearRatioTracker.cs       per-gear ratio learning (rpm per m/s)
  ShiftPointAdvisor.cs      optimal upshift RPM from curve + ratios
  TireCompound.cs           compound presets
  HudConfig.cs              config load/save (config.json)
  PanelPlacement.cs         panel layout model (fractions of work area)
src/Fh6Hud/                 WPF overlay app
  HudState.cs               shared telemetry state (listener, trackers, timers)
  PanelWindow.cs            panel base: drag/persist, click-through, menu
  Panels/                   TirePanel, EnginePanel, IntervalPanel,
                            SpeedoPanel, StatusPanel
tools/Fh6Hud.Simulator/     synthetic telemetry generator (no game needed)
tests/Fh6Hud.Tests/         unit tests (parser, timers, power curve, gear
                            ratios, shift advisor, config, compounds, UDP
                            listener)
docs/fh6-data-out.md        official FH6 Data Out spec snapshot
.opencode/skills/           opencode skills (FH6 telemetry + WPF overlay)
```

## Troubleshooting

- **No data / "WAITING FOR TELEMETRY"** — Data Out is off, **Data Out Packet
  Format is set to "Sled" instead of "Car Dash"**, the port doesn't match,
  the game isn't driving (menu/pause), or a firewall blocks the port. Verify
  packets arrive with a UDP dump (e.g. Wireshark filter `udp.port == 45000`)
  or confirm the simulator flow works.
- **Tire temps look wrong** — FH6 sends tire temperature as Fahrenheit-like
  raw values; the HUD converts them to °C. The optimal range depends on the
  compound you select.
- **Power curve is empty** — it fills in while driving; do a full-throttle
  pull through the rev range. The curve resets when you switch cars.
- **Shift indicator stays "SHIFT --" / no lights** — the advisor needs both
  the power curve and the gear ratios to be learned: do a few full-throttle
  pulls through the gears (one pull through every gear is enough; the engine
  panel learns gear ratios as rpm-per-speed while you drive with the clutch
  fully engaged and no wheelspin). It also needs the next gear's post-shift
  rev range to have been sampled — i.e. you must actually drive each gear.
  Advice only appears in manual gears; in `D` and in the top gear there is
  none. Everything resets when you switch cars. If the power curve keeps
  falling after its peak, the upshift point sits below redline; if the car
  pulls flat to the limiter, shifting at redline *is* optimal and the light
  flashes there. The downshift light additionally requires a real power gain
  (≥1.5%) and keeps 400 RPM of headroom below the lower gear's shift point,
  so it stays quiet near the boundary, on flat power curves, and right after
  an upshift at the limiter (no shift-down-into-limiter bounce).
