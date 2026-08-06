# TODO

## Rev bar — red bar visual still an issue

The red zone at the right end of the RPM bar (engine panel) still reads as
an issue on screen.

Status of the investigation (2026-08-06, pixel-level capture at 200 ms over
40 s on the current build):

- The zone element itself is **static** (pinned at 90-100% of the track) and
  the blue fill is clamped to stop at its left edge — measured overlap is
  zero in every frame.
- The original "moving red" was the **shift-point marker line** on the power
  curve (red line drawn through the blue curve, jumping per gear / while
  learning). That marker has been removed and the curve's learning-phase
  "plunging tail" was fixed.
- Remaining suspects to check next, in order:
  1. The translucent red zone visually blending with the blue fill tip when
     the fill reaches ~90% (round-corner AA at the boundary).
  2. The zone's fixed 10% width vs the real redline (EngineMaxRpm) — the
     band may sit where the driver doesn't expect it.
  3. Any DPI-scaling rounding at the zone/fill boundary.
  4. Reconfirm with the user which exact element they still see moving.
