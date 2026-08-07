# TODO

## Rev bar — resolved (2026-08-07)

**Symptom:** the redline zone at the right end of the RPM bar "moves to the
left towards and eventually into the blue current RPM bar"; the red band
should sit at the car's actual redline and never move.

**Root cause (pre-`3fb635c` build):** the blue fill was drawn *on top of*
the redline zone and its width was unclamped (`rpm/maxRpm` up to 100%).
Above 90% max RPM the fill covered the zone's left part, so the visible red
band's left edge rode the fill — it swung left and right with every shift
near the limiter and vanished entirely at 100% (the blue swallowed it).
Measured on a 260 px track with the real palette: red-edge travel ≈ 18.5 px
per pull, plus complete disappearance at the limiter. The 2026-08-06 fix
(clamp at the zone edge via `RpmBarGeometry.FillWidthFraction`, zone drawn
last/on top) removes the mechanism; the layout model
(`tests/Fh6Hud.Tests/RevBarLayoutModelTests.cs`) goes red on the legacy
rules (30/114 frames) and green on the current ones (0 px travel, zero
overlap).

**2026-08-07 verification (current main):** a pixel-level composite of the
bar (exact palette, alpha compositing, rounded fill tip, 114-frame driving
profile) measures the perceived red edge at exactly 234.0 px in every frame
— the translucent zone did not move and did not blend at the junction.
Conclusion: on current main the red band cannot move; a report of sliding
after the fix means a stale instance (app started before the rebuild, or an
old published folder) or a misread of the (previously 35 % alpha, dim)
band.

**Fix landed on this branch:**
- `RedlineBrush` is now an **opaque yellow→orange→red gradient** — a crisp
  band reading "approaching the limiter", its right edge exactly at the
  redline (top 10 % of the rev range, anchored at `EngineMaxRpm`).
- Regression lock: `RevBarLayoutModelTests` asserts the zone's visible
  left edge is constant and the fill never enters the zone under a driving
  profile.
- Action for the reporter: rebuild and **restart the HUD instance**; if the
  band still slides, capture a short recording — no code on main can
  produce that motion.
