# FH6 HUD — Improvement Backlog

Proposed improvements from a full review of the repo (UI, functionality,
architecture, testing, telemetry data). Checked items (`- [x]`) are the agreed
work backlog; unchecked items are declined or deferred proposals (decision
noted inline).

Items are tagged with effort: **S** = small (< half a day), **M** = medium
(1–2 days), **L** = large (multi-day refactor). "Depends on" lists items that
must land first.

> Basis: review of commit `53d7020` (initial commit). Claims are grounded in
> the repo source and the official FH6 Data Out spec (`docs/fh6-data-out.md`);
> community-sourced facts are marked `[SRC]`. Tire ranges are approximations —
> FH6 does not expose the equipped compound over Data Out.
>
> Selection walkthrough completed 2026-08-05: every item reviewed against the
> current source; decisions recorded below.

---

## Tires & telemetry data

- [x] **DATA-1 — Car-switch detection via `CarOrdinal`** *(S, rescoped)*
  - Decision: switch detection only — **no car-name database** (community CSVs
    are unlicensed, high-maintenance, and the car name adds no HUD value).
  - `CarOrdinal` (S32 @ 212, parsed but unused): on change, reset
    `PowerCurveTracker` (fixes FUNC-3) and any per-car cached state. Power
    curve accumulates per car; a new ordinal starts a fresh curve.

- [x] **DATA-2 — Complete tire compound set** *(S, rescoped)*
  - Presets (**default = Rally**): Rally, Semi-Slick, Slick, Offroad, Snow,
    Street, Sport, Standard, Vintage, Vintage Race.
  - Supply community-observed optimal ranges, marked as approximations `[SRC]`
    — FH6 does not expose the equipped compound over Data Out, so selection
    stays manual.
  - Update `Telemetry/TireCompound.cs` + context menu (`MainWindow.xaml`).

- [ ] **DATA-3 — °F / °C display toggle** *(S)*
  - **Declined:** HUD stays °C-only.

- [x] **DATA-4 — Document the Data Out "Format" setting** *(S)*
  - Sled is a strict prefix subset of Car Dash (ends at `NumCylinders`,
    ~232 bytes): RPM, motion, suspension, slip, car identity. It has **no**
    speed, tire temps, power, fuel, or gear — nothing this HUD uses.
  - Selection is in-game: SETTINGS > HUD AND GAMEPLAY > Data Out Packet
    Format = **Car Dash** `[SRC]` (the official FH6 article omits the
    selector).
  - Add "Format = Car Dash" to the README in-game setup + troubleshooting
    tables and to the fh6-data-out skill; symptom of Sled: permanent
    "WAITING FOR TELEMETRY" (the listener silently drops datagrams != 324
    bytes).

- [ ] **DATA-5 — Verify FH5 vs FH6 layout claim in docs** *(S)*
  - **Declined:** FH6-only scope.

---

## UI

- [ ] **UI-1 — Live packet rate + error count in footer** *(S)*
  - **Declined:** a permanent Hz readout spends screen space on a rare
    diagnostic. The existing stale status ("NO DATA | DRIVING?") already
    covers broken input. Counters stay available in `UdpTelemetryListener`
    if ever needed.

- [x] **UI-2 — Gear readout** *(S, rescoped)*
  - Gear chip next to the speed readout. Mapping (0=N, 20=R, 21=D, 22=P) is
    inferred from earlier titles — verify against live data before shipping.
  - Boost/fuel/torque **declined for now** (panel space); revisit if panels
    become customizable.

- [x] **UI-3 — Per-tire delta vs optimal window** *(S)*
  - Tire card is only 3-state color (cold / in range / hot). Show how far a
    tire is off target (e.g. "HOT +8°") in the existing state slot — that is
    what tuning actually reacts to.

- [ ] **UI-4 — Auto-scaling speed bar** *(S)*
  - **Postponed:** bar stays fixed 0–300 km/h (matches the timer zones); the
    speed number is already uncapped. The bar's value is marginal — revisit
    only if it earns its space, otherwise consider deleting it.

- [x] **UI-5 — Click-through affordance** *(S)*
  - When click-through is active, right-click passes to the game — the footer
    hint is the only way back. Toggle the footer text to
    "CTRL+ALT+H TO RESTORE" while click-through is on.

- [x] **UI-6 — Hotkey registration failure handling** *(S)*
  - `RegisterHotKey` return value is ignored (`MainWindow.xaml.cs`). If
    Ctrl+Alt+H is taken by another app the toggle silently dies. Surface the
    failure in the footer/status line.

- [x] **UI-7 — Window position re-anchor** *(S)*
  - Position is set once in `OnLoaded` from `ActualHeight`; with
    `SizeToContent=Height` layout may not be final. Re-anchor on
    `SizeChanged` to avoid the window drifting from the corner.

---

## Functionality

- [x] **FUNC-1 — Hold interval timers during telemetry gaps** *(M)*
  - *Bug.* `SpeedIntervalTimer` runs a wall-clock `Stopwatch`; `Update()` is
    only called while packets arrive, so a pause/menu mid-run inflates the
    time when data resumes.
  - **Fix approach:** drive elapsed from packet `TimestampMs` (U32 @ 4,
    parsed but unused) instead of a wall clock — gaps then cannot advance
    the timer by construction.
  - Add timer tests feeding synthetic timestamps (supersedes TEST-2).

- [x] **FUNC-2 — Gate rendering on `IsRaceOn`** *(S, rescoped)*
  - When `IsRaceOn == 0` (menu/garage/stopped): hide the HUD panel but keep
    a minimal status chip visible — hiding everything would remove the
    right-click target and strand the context menu.
  - Treat as one "no live data" state together with the stale path; timers
    hold via FUNC-1. (In menus the game usually stops sending entirely, so
    staleness covers most cases; `IsRaceOn` adds finish/replay/paused states
    where packets keep flowing.)

- [x] **FUNC-3 — Reset power curve on car change** *(S)*
  - `PowerCurveTracker` resets only when `EngineMaxRpm` changes; two cars with
    the same redline keep the old curve. Reset on `CarOrdinal` change
    (see DATA-1).

- [x] **FUNC-4 — Persist config changes** *(S)*
  - `ApplyCompound` mutates the in-memory config but `HudConfig` has no
    `Save()` — menu changes are lost on restart. Add save-on-change writing
    `config.json`.
  - Scope: persist all fields (compound, range, port) **except** window
    position (the corner anchor is by design, not user state). Note:
    `HudConfig.Load` already honors a hand-edited `config.json` today;
    this closes the loop for in-app changes.

- [x] **FUNC-5 — Surface invalid config** *(S)*
  - `HudConfig.Load` silently falls back to defaults on malformed JSON. Show a
    status-line warning ("CONFIG INVALID, USING DEFAULTS").

- [ ] **FUNC-6 — Simulator cold-start temps** *(S)*
  - **Declined:** premise was wrong — cruise starts at ~60 °C, which is below
    every preset's minimum, so the COLD state already appears and the sim
    warms through the full spectrum.

- [x] **FUNC-7 — `Fh6PacketBuilder.Build()` defensive copy** *(S)*
  - `Build()` returns the internal buffer; mutation after build corrupts
    subsequent packets. Return a copy.

---

## Architecture

- [x] **ARCH-1 — Extract `Fh6Hud.Telemetry` class library** *(M)*
  - The simulator and the test project both reference the WPF `WinExe`,
    dragging WPF into the test host and tying the tool to Windows.
  - Move `Telemetry/` (and `HudConfig`) into a plain `net10.0` library.
    Effects:
    - parser/timer/config tests run on **any OS** → enables the Linux CI
      follow-up (CI-1),
    - clean `MainWindow` ↔ `Telemetry` boundary.
  - *Depends on:* nothing. *Enables:* CI-1 (ubuntu job), TEST-1/3/4 on Linux.

- [ ] **ARCH-2 — Extract `HudPresenter` from `MainWindow`** *(L)*
  - **Deferred:** the stateful logic (`PowerCurveTracker`,
    `SpeedIntervalTimer`) is already extracted and tested; what remains in
    `MainWindow` is mostly formatting. Refactor only if the UI keeps growing
    (e.g. customizable panels) — record this decision in a short architecture
    note (README or docs) for future work.

---

## Testing & CI

- [x] **TEST-1 — `UdpTelemetryListener` loopback tests** *(M)*
  - Zero coverage today. Real loopback UDP: bind port 0, send crafted
    324-byte packets, assert `PacketReceived` + counters, and that non-324
    datagrams are ignored (pins the Sled-format failure mode). Tests real
    socket behavior, not mocks.

- [ ] **TEST-2 — `TimeProvider` injection for timer tests** *(M)*
  - **Declined:** superseded by FUNC-1's `TimestampMs` approach — timer tests
    feed synthetic packet timestamps, so they are instant and deterministic
    without a fake clock.

- [x] **TEST-3 — `HudConfig` tests** *(S)*
  - Missing file → defaults, malformed JSON → defaults + invalid flag
    (FUNC-5), valid round-trip including `Save()` (FUNC-4), `SourcePath` set.

- [x] **TEST-4 — `TireCompound` tests** *(S)*
  - `Find` case-insensitivity, unknown → null, preset sanity (min < max),
    the complete 10-compound set present, default = Rally (pins DATA-2).

- [ ] **TEST-5 — Presenter/render-pipeline tests** *(M)*
  - **Declined for now:** depends on ARCH-2, which is deferred.

- [x] **CI-1 — GitHub Actions workflow** *(S, done)*
  - **Implemented:** `.github/workflows/ci.yml` runs `dotnet test
    Fh6Hud.slnx` on `windows-latest` for every push to `main` and PR.
  - **Follow-up (after ARCH-1):** add a fast `ubuntu-latest` job for
    parser/timer/config tests once `Fh6Hud.Telemetry` is a plain `net10.0`
    library; keep the Windows job for the full solution.
  - *Depends on (follow-up only):* ARCH-1.

---

## Agreed implementation order

1. **Quick wins (bugs + hygiene):** FUNC-1 (TimestampMs timers), FUNC-3 +
   DATA-1 (ordinal reset), FUNC-2 (IsRaceOn gating), FUNC-4, FUNC-5, FUNC-7,
   DATA-4 (docs)
2. **Visible:** UI-2 (gear), UI-3 (tire delta), UI-5, UI-6, UI-7, DATA-2
   (compound set)
3. **Structural + tests:** ARCH-1 → CI-1 ubuntu follow-up, TEST-1, TEST-3,
   TEST-4
4. **Deferred/declined:** ARCH-2 + TEST-5 (revisit if the UI grows), UI-4
   (revisit or delete the bar), DATA-3, DATA-5, UI-1, FUNC-6, TEST-2

## Resolved decisions (was: open questions)

- **Car-name database:** dropped. Only ordinal-change detection ships; the
  power curve is cached per `CarOrdinal` and reset on change.
- **Config persistence:** persist all fields except window position.
  `HudConfig.Load` already supports a user-supplied `config.json`; FUNC-4
  adds `Save()` so in-app changes persist to the same file.
