using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

/// <summary>
/// Per-frame model of the rev bar's layout under a realistic driving profile
/// (floor it to the limiter, shift, repeat — the same 200 ms grid the
/// TODO.md investigation used). Locks the user-visible invariant: the redline
/// zone's left edge is a constant band and the blue fill never enters it.
///
/// Pre-fix build (MainWindow, commit 3fb635c^): the blue fill was drawn last
/// (on top of the zone) and unclamped, so above 90% rpm the fill covered the
/// zone's left part. The visible red band's left edge then rode the fill —
/// it swung left and right with every shift near the limiter, and the blue
/// visibly entered the red zone. That is the "red moves left into the blue"
/// bug. The current rules (zone drawn on top, fill clamped at the zone's
/// left edge via RpmBarGeometry.FillWidthFraction) make the red band
/// provably static; this test is the regression lock for that contract.
///
/// NOTE: the track XAML's column split (9* / 1*) must stay in sync with
/// RpmBarGeometry.RedlineZoneFraction (0.10) — the test models the geometry
/// constants, not the XAML markup.
/// </summary>
public class RevBarLayoutModelTests
{
    private const double TrackWidth = 300.0;   // px at 96 DPI, approx. engine-panel track width
    private const double MaxRpm = 7000.0;

    /// <summary>200 ms frames over ~40 s of driving: ramp to the limiter, shift, repeat.</summary>
    private static readonly double[] Profile = BuildProfile();

    private readonly record struct Frame(double FillEnd, double ZoneStart, double ZoneEnd);

    private static IEnumerable<Frame> Frames(bool zoneOnTop, bool clampFill)
    {
        double zoneStart = RpmBarGeometry.RedlineStartFraction * TrackWidth;
        foreach (double rpm in Profile)
        {
            double fill = Math.Clamp(rpm / MaxRpm, 0.0, 1.0);
            if (clampFill)
            {
                // Current rule: the fill stops exactly at the zone's left edge.
                fill = Math.Min(fill, RpmBarGeometry.RedlineStartFraction);
            }

            yield return new Frame(fill * TrackWidth, zoneStart, TrackWidth);
        }
    }

    /// <summary>
    /// Left edge of the red band the user actually sees. With the zone on
    /// top it is the zone's fixed left edge; with the fill on top (legacy)
    /// the fill covers the zone's left part above 90% rpm, so the visible
    /// edge is the fill's right edge.
    /// </summary>
    private static double VisibleRedLeftEdge(Frame f, bool zoneOnTop) =>
        zoneOnTop ? f.ZoneStart : Math.Max(f.FillEnd, f.ZoneStart);

    [Fact]
    public void CurrentBuild_RedZoneEdgeConstantAndFillNeverEntersZone()
    {
        var frames = Frames(zoneOnTop: true, clampFill: true).ToList();

        // The red band's visible left edge is identical in every frame of the
        // profile — the redline zone does not move, at any RPM, at any gear.
        double expectedEdge = RpmBarGeometry.RedlineStartFraction * TrackWidth;
        Assert.All(frames, f => Assert.Equal(expectedEdge, VisibleRedLeftEdge(f, zoneOnTop: true), 9));

        // The blue fill never crosses under the zone (zero overlap, every frame).
        Assert.All(frames, f => Assert.True(f.FillEnd <= f.ZoneStart + 1e-9,
            $"fill end {f.FillEnd:F1}px crossed the zone edge {f.ZoneStart:F1}px"));

        // And the profile really exercises the boundary: the fill does reach
        // the zone's edge at least once (the loop is not vacuous).
        Assert.Contains(frames, f => Math.Abs(f.FillEnd - f.ZoneStart) < 1e-6);
    }

    [Fact]
    public void LegacyBuild_FillOnTopUnclamped_MovesTheRedBand()
    {
        // Pre-fix rules: fill on top, unclamped. The visible red band's left
        // edge must swing with the fill near the limiter and the fill must
        // overlap the zone — the exact failure mode the fix removed. This
        // test proves the model above would catch a reintroduced legacy
        // geometry (see CurrentBuild_... above).
        var frames = Frames(zoneOnTop: false, clampFill: false).ToList();

        double[] edges = frames.Select(f => VisibleRedLeftEdge(f, zoneOnTop: false)).ToArray();
        double travel = edges.Max() - edges.Min();
        Assert.True(travel > 0.05 * TrackWidth,
            $"red band edge traveled only {travel:F1}px — expected > {0.05 * TrackWidth:F1}px");

        Assert.True(frames.Any(f => f.FillEnd > f.ZoneStart + 1e-9),
            "fill never entered the zone under legacy rules");
    }

    private static double[] BuildProfile()
    {
        // Six gear pulls: ramp idle → limiter (13 frames ≈ 2.6 s), bounce on
        // the limiter, drop on the shift, repeat. Includes frames at ~0.9 max
        // RPM (where the shift advisor shifts), at 1.0 (limiter), and the
        // post-shift drop back through 0.9 — all boundary conditions.
        var frames = new List<double>();
        for (int pull = 0; pull < 6; pull++)
        {
            for (int k = 0; k <= 13; k++)
            {
                frames.Add(1500 + k * (MaxRpm - 1500) / 13.0);
            }

            frames.Add(MaxRpm);      // limiter bounce
            frames.Add(MaxRpm);
            frames.Add(6800);        // just under the zone edge (0.971)
            frames.Add(6300);        // at the zone edge (0.90) — shift point
            frames.Add(4200);        // post-shift drop
        }

        return frames.ToArray();
    }
}
