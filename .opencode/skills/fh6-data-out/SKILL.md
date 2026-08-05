---
name: fh6-data-out
description: "FH6 Data Out Expert. Use when parsing, decoding, or handling Forza Horizon 6 UDP telemetry packets, reading/validating the 324-byte 'Data Out' format, mapping packet fields/offsets, or configuring the in-game Data Out settings (SETTINGS > HUD AND GAMEPLAY). Also relevant for comparing FH6 vs FH5 vs Forza Motorsport telemetry formats."
---

# FH6 Data Out Expert

Forza Horizon 6 streams racing telemetry via one-way UDP to a configurable IP
and port. The canonical, full reference (field list, type table, byte offsets,
official notes) lives in this repo:

**`docs/fh6-data-out.md`** — read it for exact offsets before writing parsers
or modifying field handling.

## Key facts

- Packet size: exactly **324 bytes**, all values **little-endian**.
- The in-game **Data Out Packet Format** selector offers **Car Dash** (the
  324-byte layout documented here) and **Sled** (a shorter ~232-byte packet:
  a strict prefix subset ending at `NumCylinders`, with no speed, tire temps,
  power, fuel, or gear). Receivers that validate 324 bytes (like this HUD)
  silently drop Sled datagrams, so a Sled selection looks like no data at
  all. Always use **Car Dash**.
- Sent at the game's frame rate (up to ~60 Hz) — expect ~60 packets/sec.
- Data is only sent **while the player is actively driving** (not in menus,
  pauses, replays, rewinds, or after finishing a race).
- `IsRaceOn` (S32 @ offset 0) is 1 while racing, 0 in menus/stopped.
- Speed is in **meters/second** (`F32` @ 256); multiply by 3.6 for km/h.
- Tire temps are `F32` @ 268/272/276/280 (FL/FR/RL/RR). The official FH6
  article omits the unit; observed FH6 values are Fahrenheit-like, so convert
  with `(raw - 32) * 5 / 9` before displaying Celsius or applying thresholds.
  Example: raw `140` = `60 C`.
- Throttle/brake/clutch/handbrake/gear are `U8` @ 315-319; steering is `S8`
  @ 320 (-127..127). `Gear` 0 = neutral, 1-6+ = gears, 20 = reverse, 21 = drive (D),
  22 = park (P) as in earlier titles — verify against observed data.
- Lap fields: `LapNumber` U16 @ 312, `RacePosition` U8 @ 314, `CurrentRaceTime`
  F32 @ 308 (seconds since driving started).

## Game configuration checklist (for the user to verify data flow)

1. In-game: **SETTINGS > HUD AND GAMEPLAY > Data Out = On**.
2. **Data Out IP Address** = 127.0.0.1 (same PC) or the receiving machine's IP.
3. **Data Out IP Port** = match the app's listener; **avoid ports 5200-5300**
   (the game binds its own outgoing socket in that range).
4. **Data Out Packet Format** = **Car Dash** (NOT "Sled" — see key facts).
5. Firewall must allow inbound UDP on the chosen port.
6. Data begins as soon as the player starts driving.

## Parsing guidance

- Use `BinaryPrimitives` / `MemoryMarshal` over a `ReadOnlySpan<byte>` for
  little-endian reads; never rely on machine endianness.
- Validate buffer length == 324 before parsing; ignore shorter/longer datagrams
  (they may come from other sources or other Forza games with different sizes).
- FH6 layout differs from FH5's 324-byte packet: FH6 inserts `CarGroup`,
  `SmashableVelDiff`, `SmashableMass` after `NumCylinders`, shifting
  `PositionX..` onward by 12 bytes relative to FH5. Do not reuse FH5 offset
  tables blindly.
- A listener should run on its own background thread / async receive loop and
  marshal frames to the UI thread for display.

## When the user says the HUD shows no data

Walk through: Data Out toggle, IP/port match, port not in 5200-5300, **Data
Out Packet Format = Car Dash** (Sled is dropped), firewall, packet length
validation, and `IsRaceOn`/driving state (data only flows while driving).
Suggest a raw UDP dump (`dotnet`/PowerShell one-liner or Wireshark) to confirm
packets arrive at the expected port.
