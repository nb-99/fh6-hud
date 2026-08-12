# TODO

## [x] Upshift indicator improvement — resolved in issue #11

**"Steering-wheel light"-like experience:** Implement a visual cue similar to a steering-wheel light to indicate optimal upshift timing with an earlier signal that an upshift is coming up and how much time is left until the optimal upshift point. Sports cars often have a row of ligts in the top of the steering wheel that light up in sequence as the engine approaches the optimal upshift point. This could be implemented in the HUD as a series of lights or a bar that fills up as the engine approaches the optimal upshift point, providing a clear and intuitive visual cue for the driver, instead of only the existing flashing pill that only appears when the optimal upshift point is reached.

Shipped in issues #12–#14: six progressive lights (two yellow, two orange, two red) fill left-to-right during the final 20% of the RPM distance to the learned shift point; at the point all six turn red and "▲ UPSHIFT" reveals inside the same component, which then blinks. The engine title shows `SHIFT LEARNING` while a gear is being learned. Redline fallback (no power crossover) runs the same progression to the limiter.

## [ ] Engine/RPM panel axis marker

**Readability:** Currently the power over RPM curve does not have proper axis markers. An at a glance view works, but it is impossible to say e.g. "my car has max power between 5750 and 6250 RPM". A fairly basic grid in the background of the power curve would allow for a much better understanding of the engine's power characteristics and allow for better tuning of gears and shifting decisions.

## [ ] Configurable Panels

**Hideable:** Panels should be individually hideable/deactivatable, either manually or by certain conditions (e.g. when tire temp has not changed in the last 10 secs e.g. when in the tuning menu, hide the tire temp panel)
**Resizable:** Panels should be resizable via dragging their edges and their size persisted alongside their position in config.json
**Persistent Positioning:** Panels should be able to be moved around the screen and their dragged position saved. This would allow users to customize their HUD layout to their liking by dragging, instead of only via the config.json initial positioning.
**Auto-hide:** Panels should be able to auto-hide when not in use, e.g. when the car is stationary or when the player is in a menu, and reappear when the car is moving or when the player is back in the game. The `raceOn` data does not always match the actual state, e.g. in the tuning menu we can still rev the engine and data is sent out, but the HUD get's in the way of tuning data. Hide panels separately if their relevant data is stale for a certain amount of time.

## [ ] Update README

**Outdated:** The README is outdated and needs to be updated to reflect the current state of the project, including new features, installation instructions, and usage guidelines. It also should be shorter, more visually appealing, and include screenshots or gifs/videos.

## [ ] Tire temps

**Temperature Ranges:** Verify correct tire operating temperature ranges.
**Tire compound:** Show which tire compound is selected in the HUD.
**Visibility:** Resolved in issue #4: the state and signed delta are larger and sit above the current temperature.

## [ ] Makefile

**make build:** builds the exe
**make run:** builds + runs the exe
**make dev:** runs the devbuild
**make test:** runs tests

## [x] UI Readability — resolved in issue #4

**Visibility:** Many elements, especially text, are barely readable or out of primaty vision. Texts should not be as small as current smaller texts for data that is relevant durint racing, where primary attention is on driving and data should be easily visible at a glance.
**Positioning:** Some elements would benefit from being placed separately from their current panel. Primary example being the Upshift/Downshift indicator, which could be moved to the middle and be a bit larger to be placeable directly in the FOV of the road/car.

## [x] Hooks and CI for linting, validations etc — resolved in issue #4

## [x] Rev bar — resolved (2026-08-07)

**Symptom:** the redline zone at the right end of the RPM bar "moves to the
left towards and eventually into the blue current RPM bar"; the red band
should sit at the car's actual redline and never move.

**Root cause (pre-`3fb635c` build):** the blue fill was drawn _on top of_
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

**2026-08-07 verification (original fix):** a pixel-level composite of the
bar (exact palette, alpha compositing, rounded fill tip, 114-frame driving
profile) measures the perceived red edge at exactly 234.0 px in every frame.
The later gradient build exposed a separate WPF layout issue: because the
blue fill spanned both star columns, `SizeToContent` redistributed the columns
from the fill's current width. On a 260 px track this produced 144/116 px
columns at high RPM instead of 234/26 px, making the gradient grow left.

**Actual fix:** the blue fill is clamped at the redline start and stays in the
first (90 %) grid column; the opaque gradient occupies the fixed second
column. This prevents WPF `SizeToContent` from redistributing the columns as
RPM changes.

- `RedlineBrush` is now an **opaque yellow→orange→red gradient** — a crisp
  band reading "approaching the limiter", its right edge exactly at the
  redline (top 10 % of the rev range, anchored at `EngineMaxRpm`).
- Regression lock: `RevBarLayoutModelTests` asserts the zone's visible
  left edge is constant and the fill never enters the zone under a driving
  profile.
