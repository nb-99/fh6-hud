# FH6 HUD — Improvement Backlog

Proposed improvements from a full review of the repo (UI, functionality,
architecture, testing, telemetry data). **How to use:** tick the checkbox of
every item you want implemented (`- [x]`), then hand the file back — the
checked items become the work backlog. Unchecked items stay proposals.

Items are tagged with effort: **S** = small (< half a day), **M** = medium
(1–2 days), **L** = large (multi-day refactor). "Depends on" lists items that
must land first.

> Basis: review of commit `53d7020` (initial commit). Claims are grounded in
> the repo source and the official FH6 Data Out spec (`docs/fh6-data-out.md`);
> community-sourced facts are marked `[SRC]`. Tire ranges are approximations —
> FH6 does not expose the equipped compound over Data Out.

---

## Tires & telemetry data

- [ ] **DATA-1 — Car identity from `CarOrdinal`** *(M)*
  - The packet's `CarOrdinal` (S32 @ 212, parsed but unused) can be mapped to
    car make/model via community databases (`[SRC]`: HDR's FH6 ordinals gist,
    Nexus mods "FH6 Car ID List" CSV/JSON).
  - Ship a local lookup table → show the car name in the HUD header and detect
    car switches. Car-switch detection fixes the power-curve reset bug (today
    the curve only resets when `EngineMaxRpm` changes, so two cars sharing a
    redline keep the previous car's curve — see FUNC-3).
  - Deliverable: `CarDatabase` (embedded CSV), name in header, `CarOrdinal`
    change event.

- [ ] **DATA-2 — Complete tire compound set** *(S)*
  - The context menu (`MainWindow.xaml`) offers only Street / Sport / Race /
    Slick / Rally / Drag. FH6 also has Stock, Snow and Offroad compounds.
  - Add missing presets in `TireCompound.cs`. Community observations:
    stock tires run best ~180–195 °F (82–91 °C) `[SRC]` — consider making
    Stock the default instead of Race.

- [ ] **DATA-3 — °F / °C display toggle** *(S)*
  - Raw FH6 tire values are Fahrenheit-like; the in-game telemetry and the
    tuning community talk °F. Add a config/menu option that switches the tire
    card (and the TARGET range readout) between units.

- [ ] **DATA-4 — Document the Data Out "Format" setting** *(S)*
  - FH6's Data Out has a packet **Format selector: "Car Dash" vs "Sled"**
    `[SRC]` (satyajiit/forza-horizon-6-moza-bridge README). "Sled" sends a
    shorter packet that the listener silently drops (length != 324), which
    looks like "WAITING FOR TELEMETRY" forever.
  - Add "Format = Car Dash" to the README in-game setup + troubleshooting
    tables, and to `.opencode/skills/fh6-data-out/SKILL.md`.

- [ ] **DATA-5 — Verify FH5 vs FH6 layout claim in docs** *(S)*
  - `docs/fh6-data-out.md` claims FH6 shifted fields +12 bytes vs FH5. A
    community bridge reports FH6 Car Dash is byte-identical to FH5 `[SRC]`.
    Both are community claims; the HUD follows the official FH6 article
    (correct), but verify with a live capture before ever adding FH5 support.

---

## UI

- [ ] **UI-1 — Live packet rate + error count in footer** *(S)*
  - Footer hardcodes "DATA OUT | 60 HZ". `UdpTelemetryListener` already counts
    `PacketsReceived` / `ReceiveErrors` — show a measured Hz (delta over 1 s)
    and surface receive errors, which are currently invisible.

- [ ] **UI-2 — Gear, boost, fuel, torque readouts** *(S)*
  - `Gear`, `BoostPsi`, `Fuel`, `TorqueNm` are parsed but never rendered.
    A gear chip next to the speed readout is the highest-value add; boost/fuel
    under the engine card; torque next to the power values.

- [ ] **UI-3 — Per-tire delta vs optimal window** *(S)*
  - Tire card is only 3-state color (cold / in range / hot). Show how far a
    tire is off target (e.g. "+8° above target") — that is what tuning
    actually reacts to. Optional polish: a tick showing where the optimal band
    sits on the temperature scale.

- [ ] **UI-4 — Auto-scaling speed bar** *(S)*
  - Speed bar is fixed 0–300 km/h (`SpeedBarMaxKmh`), useless for cars that
    top out at 260. Auto-scale to sampled max speed or car-derived top speed.

- [ ] **UI-5 — Click-through affordance** *(S)*
  - When click-through is active, right-click passes to the game — the footer
    hint is the only way back. Toggle the footer text to
    "CTRL+ALT+H TO RESTORE" while click-through is on.

- [ ] **UI-6 — Hotkey registration failure handling** *(S)*
  - `RegisterHotKey` return value is ignored (`MainWindow.xaml.cs`). If
    Ctrl+Alt+H is taken by another app the toggle silently dies. Log the
    failure, retry with a fallback hotkey, or surface it in the footer.

- [ ] **UI-7 — Window position re-anchor** *(S)*
  - Position is set once in `OnLoaded` from `ActualHeight`; with
    `SizeToContent=Height` layout may not be final. Re-anchor on
    `SizeChanged` to avoid the window drifting from the corner.

---

## Functionality

- [ ] **FUNC-1 — Hold interval timers during telemetry gaps** *(M)*
  - *Bug.* `SpeedIntervalTimer` runs a real `Stopwatch`; `Update()` is only
    called while packets arrive, and the render loop returns early when data
    goes stale. A game pause/menu therefore keeps the clock running and a
    0–100 run can "complete" with an inflated time when data resumes.
  - Fix: freeze/hold timers while `stale`, or cap elapsed to the packet gap.
    Needs timer tests (see TEST-2).

- [ ] **FUNC-2 — Gate rendering on `IsRaceOn`** *(S)*
  - `IsRaceOn` is parsed but ignored. A packet with `IsRaceOn = 0` (race
    stopped) renders as live data. Gate the render path on it alongside the
    existing staleness check.

- [ ] **FUNC-3 — Reset power curve on car change** *(S)*
  - `PowerCurveTracker` resets only when `EngineMaxRpm` changes; two cars with
    the same redline keep the old curve. Reset on `CarOrdinal` change (see
    DATA-1). Also fold the public `IsDirty` setter into the tracker
    (`TryGetPendingCurve`-style) so the state machine is honest.

- [ ] **FUNC-4 — Persist config changes** *(S)*
  - `ApplyCompound` mutates the in-memory config but `HudConfig` has no
    `Save()` — compound/range changes made via the menu are lost on restart.
    Add save-on-change writing `config.json`.

- [ ] **FUNC-5 — Surface invalid config** *(S)*
  - `HudConfig.Load` silently falls back to defaults on malformed JSON. Show a
    status-line warning ("CONFIG INVALID, USING DEFAULTS") instead of
    surprising the user with different behavior.

- [ ] **FUNC-6 — Simulator cold-start temps** *(S)*
  - The `cruise` scenario starts tires at 60 °C (`Fh6Hud.Simulator/Program.cs`),
    so the COLD state never appears. Start below the lower bound (e.g. 40 °C)
    so the whole color spectrum is exercisable during development.

- [ ] **FUNC-7 — `Fh6PacketBuilder.Build()` defensive copy** *(S)*
  - `Build()` returns the internal buffer; mutation after build corrupts
    subsequent packets. Return a copy or document single-use.

---

## Architecture

- [ ] **ARCH-1 — Extract `Fh6Hud.Telemetry` class library** *(M)*
  - The simulator and the test project both reference the WPF `WinExe`,
    dragging WPF into the test host and tying the tool to Windows.
  - Move `Telemetry/` into a plain `net10.0` library. Effects:
    - parser/timer/config tests run on **any OS** → enables Linux CI (CI-1),
    - clean `MainWindow` ↔ `Telemetry` boundary.
  - *Depends on:* nothing. *Enables:* CI-1.

- [ ] **ARCH-2 — Extract `HudPresenter` from `MainWindow`** *(L)*
  - `MainWindow.xaml.cs` is a 386-line god object mixing P/Invoke window
    chrome, hotkey handling, telemetry rendering, compound menu and config.
  - Move the render pipeline (frame → UI state: temp classification,
    formatting, interval rows, power curve updates) into a testable
    `HudPresenter`; the window keeps chrome, hotkey and input only.
  - *Depends on:* nothing. *Enables:* TEST-5.

---

## Testing & CI

- [ ] **TEST-1 — `UdpTelemetryListener` loopback tests** *(M)*
  - Zero coverage today. Testable with real loopback UDP: bind port 0, send
    crafted 324-byte packets, assert `PacketReceived` + counters, and that
    non-324 datagrams are ignored (pins the Sled-format failure mode).

- [ ] **TEST-2 — `TimeProvider` injection for timer tests** *(M)*
  - `SpeedIntervalTimerTests` sleeps real milliseconds against a real
    `Stopwatch` — slow and potentially flaky. Inject `TimeProvider` (built-in
    since .NET 8) with a fake clock; tests become instant and deterministic.
  - *Depends on:* FUNC-1 touches the same state machine — do both together.

- [ ] **TEST-3 — `HudConfig` tests** *(S)*
  - Missing file → defaults, malformed JSON → defaults, valid round-trip,
    `SourcePath` set.

- [ ] **TEST-4 — `TireCompound` tests** *(S)*
  - `Find` case-insensitivity, unknown → null, preset sanity (min < max),
    complete compound set present (ties into DATA-2).

- [ ] **TEST-5 — Presenter/render-pipeline tests** *(M)*
  - The largest untested chunk is the render path in `MainWindow`. Once
    ARCH-2 lands, test temp-state boundaries (exactly at min/max → IN RANGE),
    stale-path behavior, and formatting.
  - *Depends on:* ARCH-2.

- [x] **CI-1 — GitHub Actions workflow** *(S, done)*
  - **Implemented:** `.github/workflows/ci.yml` runs `dotnet test
    Fh6Hud.slnx` on `windows-latest` for every push to `main` and PR. A
    Windows runner is required: the test project references the WPF
    `net10.0-windows` app, which only builds on Windows. Tests are headless
    (xUnit, no window instantiation), so they run normally on the runner.
  - **Follow-up (after ARCH-1):** add a fast `ubuntu-latest` job for
    parser/timer/config tests once `Fh6Hud.Telemetry` is a plain `net10.0`
    library; keep the Windows job for the full solution.
  - *Depends on (follow-up only):* ARCH-1.

---

## Suggested order

1. **Quick wins (bug-adjacent, independent):** FUNC-1, FUNC-2, FUNC-3,
   FUNC-4, DATA-4, FUNC-6, FUNC-7
2. **Cheap & visible:** UI-1, UI-2, UI-3, DATA-2, DATA-3, FUNC-5
3. **Structural (unlock CI + pipeline tests):** ARCH-1 → CI-1, TEST-1,
   TEST-2, TEST-3, TEST-4 → then ARCH-2 → TEST-5
4. **Polish:** UI-4, UI-5, UI-6, UI-7, DATA-1 (car identity can move up if
   the CSV is sourced early), DATA-5

## Open questions for selection

- Should the car-name database (DATA-1) ship **embedded** in the repo
  (offline, ~900 entries, needs license note from the CSV source) or be
  **downloaded** on first run?
- Should FUNC-4 persist only the compound/range, or all config including the
  window position and °F/°C unit (DATA-3)?
