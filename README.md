# FH6 HUD

An always-on-top telemetry overlay for **Forza Horizon 6**, built with C# / WPF
(.NET 10). It reads the game's "Data Out" UDP stream (324-byte packets, up to
60 Hz) and displays tire temperatures, interval timers, and engine/power data
over the game window.

![stack](https://img.shields.io/badge/C%23-.NET%2010%20(WPF)-512BD4)

## Features

- **Tire temperature monitor** — live per-corner temps (FL / FR / RL / RR),
  colored by whether each tire is *cold*, *in its optimal operating range*, or
  *too hot*, based on the selected tire compound.
- **Interval timers** — 0–100, 100–200, and 200–300 km/h, each auto-starting
  on the moment speed rises through the interval's start (braking can never
  trigger a run). Best time per interval is kept.
- **Engine & power** — current RPM, max RPM with a rev bar (redline zone),
  a live power curve built from telemetry samples (peak power per 100-RPM
  bucket), plus current and max power in PS.
- **Speed** — large current-speed readout and a 0–300 km/h progress bar.
- **Overlay behavior** — transparent, frameless, always-on-top, draggable,
  and one hotkey (`Ctrl+Alt+H`) away from full click-through so it never
  blocks the game. Right-click for a context menu (tire compound, reset
  timers, click-through, quit).
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
5. Allow inbound UDP on that port in Windows Firewall if prompted.
6. Data is only streamed **while driving** — it stops in menus, replays,
   rewinds, and after finishing a race.

## Run the HUD

```sh
dotnet run --project src/Fh6Hud
```

The HUD window appears at the bottom-left of the primary display. Drag it
anywhere with the left mouse button. `Ctrl+Alt+H` toggles click-through so
clicks pass to the game; right-click opens the HUD menu.

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
| `TireCompound` | `Race` | Compound preset used for the optimal temp range |
| `TireOptMinC` / `TireOptMaxC` | `86` / `104` | Manual optimal range override (°C) |

The tire compound can also be changed live via the HUD's right-click menu.
Compound presets (Street / Sport / Race / Slick / Rally / Drag) and their
optimal temperature ranges are defined in `src/Fh6Hud/Telemetry/TireCompound.cs`.

> Note: FH6's Data Out packet does **not** include the equipped tire compound,
> so it must be selected manually. The preset ranges are editable starting
> points — calibrate them against in-game behavior.

## Repository layout

```
src/Fh6Hud/                 WPF overlay app
  Telemetry/Fh6Packet.cs    324-byte FH6 Data Out parser
  Telemetry/UdpTelemetryListener.cs   UDP receiver (background thread)
  Telemetry/SpeedIntervalTimer.cs     interval timer state machines
  Telemetry/PowerCurveTracker.cs      power curve sampling
  Telemetry/TireCompound.cs           compound presets
  MainWindow.xaml(.cs)      HUD layout + rendering loop
  HudConfig.cs / config.json
tools/Fh6Hud.Simulator/     synthetic telemetry generator (no game needed)
tests/Fh6Hud.Tests/         unit tests (parser, timers, power curve)
docs/fh6-data-out.md        official FH6 Data Out spec snapshot
.opencode/skills/           opencode skills (FH6 telemetry + WPF overlay)
```

## Troubleshooting

- **No data / "WAITING FOR TELEMETRY"** — Data Out is off, the port doesn't
  match, the game isn't driving (menu/pause), or a firewall blocks the port.
  Verify packets arrive with a UDP dump (e.g. Wireshark filter
  `udp.port == 45000`) or confirm the simulator flow works.
- **Tire temps look wrong** — FH6 sends tire temperature as Fahrenheit-like
  raw values; the HUD converts them to °C. The optimal range depends on the
  compound you select.
- **Power curve is empty** — it fills in while driving; do a full-throttle
  pull through the rev range. The curve resets when you switch cars.
