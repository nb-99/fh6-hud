using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fh6Hud;
using Fh6Hud.Panels;
using Fh6Hud.Telemetry;
using Shapes = System.Windows.Shapes;

namespace Fh6Hud.Wpf.Tests;

public sealed class PanelWindowDragTests
{
    private static readonly Lock WpfTestLock = new();

    [Fact]
    public void WpfPanels_PersistAndRenderShiftCue()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fh6-hud-wpf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string configPath = Path.Combine(directory, HudConfig.FileName);

        try
        {
            var config = HudConfig.Load(configPath);
            config.Panels[PanelKeys.Status] = new PanelPlacement
            {
                X = 0.25,
                Y = 0.25,
                Anchor = PanelAnchor.Center,
            };
            config.Save(configPath);

            WpfResult result = RunOnSta(() => RunAll(configPath));
            var placement = HudConfig.Load(configPath).Panels[PanelKeys.Status];
            var workArea = SystemParameters.WorkArea;

            Assert.InRange(
                Math.Abs((result.Drag.InitialLeft + result.Drag.Width / 2) - (workArea.Left + workArea.Width * 0.25)),
                0,
                1);
            Assert.True(result.Drag.FinalLeft > result.Drag.InitialLeft + 20,
                $"Expected the panel to move right, but it moved from {result.Drag.InitialLeft:F1} to {result.Drag.FinalLeft:F1}.");
            Assert.True(placement.X > result.Drag.InitialX,
                $"Expected saved X to increase, but it moved from {result.Drag.InitialX:F4} to {placement.X:F4}.");
            Assert.InRange(
                Math.Abs((workArea.Left + placement.X * workArea.Width) - (result.Drag.FinalLeft + result.Drag.Width / 2)),
                0,
                1);
            Assert.Equal("UPSHIFT", result.LiveCue.UpCueText);
            Assert.Equal(Visibility.Visible, result.LiveCue.UpVisibility);
            Assert.Equal(Visibility.Collapsed, result.LiveCue.UpPlaceholderVisibility);
            Assert.Equal("DOWNSHIFT", result.LiveCue.DownCueText);
            Assert.Equal("UPSHIFT", result.LiveCue.OverlapCueText);
            Assert.Equal("\u25B2", result.LiveCue.UpCueArrow);
            Assert.Equal("\u25BC", result.LiveCue.DownCueArrow);
            Assert.Equal(Visibility.Visible, result.LiveCue.UpCueVisibility);
            Assert.Equal(Visibility.Visible, result.LiveCue.DownCueVisibility);
            Assert.True(result.LiveCue.SimulatorSawUpshift);
            Assert.True(result.LiveCue.UpUsesDedicatedPill);
            Assert.Equal(Visibility.Visible, result.LiveCue.DownVisibility);
            Assert.Equal(Visibility.Collapsed, result.LiveCue.DownPlaceholderVisibility);
            Assert.True(result.LiveCue.DownUsesDedicatedPill);
            Assert.Equal(Visibility.Visible, result.LiveCue.BlinkVisibility);
            Assert.Equal(0, result.LiveCue.BlinkOpacity);
            Assert.Equal(Visibility.Visible, result.LiveCue.ClickThroughLightVisibility);
            Assert.Equal(0, result.LiveCue.ClickThroughLightOpacity);
            Assert.Equal(Visibility.Visible, result.LiveCue.ClickThroughCueVisibility);
            Assert.True(result.LiveCue.PlaceholderUsesSeparatePill);
            Assert.Equal(Visibility.Visible, result.LiveCue.PlaceholderArrowVisibility);
            Assert.Equal("UPSHIFT", result.LiveCue.ForcedUpCueText);
            Assert.Equal("\u25B2", result.LiveCue.ForcedUpCueArrow);
            Assert.Equal(Visibility.Visible, result.LiveCue.ForcedUpVisibility);
            Assert.Equal("DOWNSHIFT", result.LiveCue.ForcedDownCueText);
            Assert.Equal("\u25BC", result.LiveCue.ForcedDownCueArrow);
            Assert.Equal(Visibility.Visible, result.LiveCue.ForcedDownVisibility);

            Assert.Equal(Visibility.Visible, result.Modes.PlaceholderWindowVisibility);
            Assert.Equal(Visibility.Visible, result.Modes.PlaceholderVisibility);
            Assert.Equal(Visibility.Collapsed, result.Modes.PlaceholderLightVisibility);
            Assert.Equal(Visibility.Visible, result.Modes.ClickThroughWindowVisibility);

            // Progressive approach cue (issue #12).
            Assert.Equal(Visibility.Visible, result.Approach.WindowStartApproachVisibility);
            Assert.Equal(1, result.Approach.WindowStartActiveLights);
            Assert.True(result.Approach.WindowStartFirstLightYellow);
            Assert.True(result.Approach.WindowStartSecondLightInactive);
            Assert.Equal(Visibility.Visible, result.Approach.MidApproachVisibility);
            Assert.Equal(3, result.Approach.MidActiveLights);
            Assert.True(result.Approach.MidFirstLightYellow);
            Assert.True(result.Approach.MidThirdLightOrange);
            Assert.True(result.Approach.MidFourthLightInactive);
            Assert.Equal(Visibility.Visible, result.Approach.TerminalPillVisibility);
            Assert.Equal("UPSHIFT", result.Approach.TerminalPillText);
            Assert.Equal(Visibility.Collapsed, result.Approach.TerminalApproachVisibility);
            Assert.Equal(Visibility.Collapsed, result.Approach.GatedApproachVisibility);
            Assert.Equal(Visibility.Visible, result.Approach.GatedPlaceholderVisibility);
            Assert.Equal(Visibility.Collapsed, result.Approach.BelowWindowApproachVisibility);
            Assert.Equal(Visibility.Collapsed, result.Approach.NoTargetApproachVisibility);
            Assert.Equal(Visibility.Visible, result.Approach.GearChangeApproachVisibility);
            Assert.Equal(3, result.Approach.GearChangeActiveLights);
            Assert.Equal(Visibility.Collapsed, result.Approach.NoDataApproachVisibility);
            Assert.Equal(Visibility.Collapsed, result.Approach.NoDataPillVisibility);
            Assert.Equal(Visibility.Visible, result.Approach.ReentryApproachVisibility);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // The WPF dispatcher may still be releasing the config handle.
            }
        }
    }

    private static WpfResult RunAll(string configPath)
    {
        var app = new App();
        app.InitializeComponent();
        try
        {
            return new WpfResult(
                RaiseMouseDrag(configPath),
                RenderShiftCue(),
                RenderApproachCue(),
                RenderShiftCueModes());
        }
        finally
        {
            app.Shutdown();
        }
    }

    private static DragResult RaiseMouseDrag(string configPath)
    {
        var state = new HudState();
        state.Initialize(portOverride: 0, configPath: configPath);

        var panel = new TestPanel(state)
        {
            DragDeltaX = 80,
            DragDeltaY = 30,
        };
        panel.Show();
        panel.UpdateLayout();

        double initialLeft = panel.Left;
        double width = panel.ActualWidth;
        double initialX = state.Config.Panels[PanelKeys.Status].X;
        var mouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseDownEvent,
        };
        panel.RaiseEvent(mouseDown);

        double finalLeft = panel.Left;
        panel.Close();
        state.Dispose();
        return new DragResult(initialLeft, finalLeft, initialX, width);
    }

    private static ShiftCueResult RenderShiftCue()
    {
        var state = new HudState();
        state.Initialize(portOverride: 0);
        SeedShiftAdvisor(state);
        EnsureClickThroughOff();

        long clock = 10_000;
        var shiftCue = new ShiftCuePanel(state, () => clock);
        shiftCue.Show();
        shiftCue.UpdateLayout();
        SetLiveState(state, CreatePacket(gear: 1, rpm: 6500f, accel: 255));
        shiftCue.RenderTick();
        string upCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        string upCueArrow = ((TextBlock)shiftCue.FindName("ShiftLightArrow")!).Text;
        Visibility upVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        Visibility upPlaceholderVisibility = ((Border)shiftCue.FindName("ShiftPlaceholder")!).Visibility;
        bool upUsesDedicatedPill = ReferenceEquals(
            shiftCue.FindResource("ShiftUpFillBrush"),
            ((Border)shiftCue.FindName("ShiftLight")!).Background);

        clock += 200;
        shiftCue.RenderTick();
        Visibility blinkVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        double blinkOpacity = ((Border)shiftCue.FindName("ShiftLight")!).Opacity;

        clock += 200;
        var downshiftPacket = CreatePacket(gear: 2, rpm: 3800f, accel: 255);
        SetLiveState(state, downshiftPacket);
        shiftCue.RenderTick();
        string downCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        string downCueArrow = ((TextBlock)shiftCue.FindName("ShiftLightArrow")!).Text;
        Visibility downVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        Visibility downPlaceholderVisibility = ((Border)shiftCue.FindName("ShiftPlaceholder")!).Visibility;
        bool downUsesDedicatedPill = ReferenceEquals(
            shiftCue.FindResource("ShiftDownFillBrush"),
            ((Border)shiftCue.FindName("ShiftLight")!).Background);

        ClickMenuItem(shiftCue, "Force UPSHIFT cue");
        SetLiveState(state, CreatePacket(gear: 2, rpm: 3500f, accel: 0));
        shiftCue.RenderTick();
        string forcedUpCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        string forcedUpCueArrow = ((TextBlock)shiftCue.FindName("ShiftLightArrow")!).Text;
        Visibility forcedUpVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        ClickMenuItem(shiftCue, "Force UPSHIFT cue");

        ClickMenuItem(shiftCue, "Force DOWNSHIFT cue");
        shiftCue.RenderTick();
        string forcedDownCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        string forcedDownCueArrow = ((TextBlock)shiftCue.FindName("ShiftLightArrow")!).Text;
        Visibility forcedDownVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        ClickMenuItem(shiftCue, "Force DOWNSHIFT cue");

        ForceOverlappingAdvisorState(state.ShiftAdvisor);
        SetLiveState(state, CreatePacket(gear: 2, rpm: 3500f, accel: 255));
        shiftCue.RenderTick();
        string overlapCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        bool simulatorSawUpshift = ReplaySimulator(state, shiftCue);
        clock += 200;
        PanelWindow.ToggleClickThroughAll();
        shiftCue.RenderTick();
        Visibility clickThroughCueVisibility = shiftCue.Visibility;
        Visibility clickThroughLightVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        double clickThroughLightOpacity = ((Border)shiftCue.FindName("ShiftLight")!).Opacity;
        PanelWindow.ToggleClickThroughAll();
        SetLiveState(state, downshiftPacket, live: false);
        shiftCue.RenderTick();
        Visibility placeholderArrowVisibility = ((TextBlock)shiftCue.FindName("PlaceholderArrow")!).Visibility;
        bool placeholderUsesSeparatePill =
            ((Border)shiftCue.FindName("ShiftPlaceholder")!).Visibility == Visibility.Visible
            && ((Border)shiftCue.FindName("ShiftLight")!).Visibility == Visibility.Collapsed;
        shiftCue.Close();
        state.Dispose();
        return new ShiftCueResult(
            upCueText,
            upVisibility,
            upPlaceholderVisibility,
            overlapCueText,
            upCueArrow,
            downCueArrow,
            upVisibility,
            downVisibility,
            simulatorSawUpshift,
            upUsesDedicatedPill,
            downCueText,
            downVisibility,
            downPlaceholderVisibility,
            downUsesDedicatedPill,
            blinkVisibility,
            blinkOpacity,
            clickThroughCueVisibility,
            clickThroughLightVisibility,
            clickThroughLightOpacity,
            placeholderUsesSeparatePill,
            placeholderArrowVisibility,
            forcedUpCueText,
            forcedUpCueArrow,
            forcedUpVisibility,
            forcedDownCueText,
            forcedDownCueArrow,
            forcedDownVisibility);
    }

    private static ShiftCueModeResult RenderShiftCueModes()
    {
        var state = new HudState();
        state.Initialize(portOverride: 0);
        EnsureClickThroughOff();

        var shiftCue = new ShiftCuePanel(state, () => 10_000);
        shiftCue.Show();
        shiftCue.UpdateLayout();
        shiftCue.RenderTick();
        Visibility placeholderWindowVisibility = shiftCue.Visibility;
        Visibility placeholderVisibility = ((Border)shiftCue.FindName("ShiftPlaceholder")!).Visibility;
        Visibility placeholderLightVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;

        PanelWindow.ToggleClickThroughAll();
        shiftCue.RenderTick();
        Visibility clickThroughWindowVisibility = shiftCue.Visibility;
        PanelWindow.ToggleClickThroughAll();

        shiftCue.Close();
        state.Dispose();
        return new ShiftCueModeResult(
            placeholderWindowVisibility,
            placeholderVisibility,
            placeholderLightVisibility,
            clickThroughWindowVisibility);
    }

    private static ApproachCueResult RenderApproachCue()
    {
        var state = new HudState();
        state.Initialize(portOverride: 0);
        SeedShiftAdvisor(state);
        EnsureClickThroughOff();

        long clock = 30_000;
        var shiftCue = new ShiftCuePanel(state, () => clock);
        shiftCue.Show();
        shiftCue.UpdateLayout();

        Border Approach() => (Border)shiftCue.FindName("ShiftApproach")!;
        Border Placeholder() => (Border)shiftCue.FindName("ShiftPlaceholder")!;
        Border Pill() => (Border)shiftCue.FindName("ShiftLight")!;
        Shapes.Ellipse Light(int index) => (Shapes.Ellipse)shiftCue.FindName($"ApproachLight{index}")!;
        // Gear 1 shift point is 6200 → approach window [4960, 6200).
        SetLiveState(state, CreatePacket(gear: 1, rpm: 4960f, accel: 255));
        shiftCue.RenderTick();
        Visibility windowStartApproachVisibility = Approach().Visibility;
        int windowStartActiveLights = CountActiveLights(shiftCue);
        bool windowStartFirstLightYellow =
            ReferenceEquals(shiftCue.FindResource("ShiftLightYellowBrush"), Light(1).Fill);
        bool windowStartSecondLightInactive =
            ReferenceEquals(shiftCue.FindResource("ShiftLightInactiveBrush"), Light(2).Fill);

        // 5580 → progress 0.5 → three lights: two yellow, one orange.
        SetLiveState(state, CreatePacket(gear: 1, rpm: 5580f, accel: 255));
        shiftCue.RenderTick();
        Visibility midApproachVisibility = Approach().Visibility;
        int midActiveLights = CountActiveLights(shiftCue);
        bool midFirstLightYellow = ReferenceEquals(shiftCue.FindResource("ShiftLightYellowBrush"), Light(1).Fill);
        bool midThirdLightOrange = ReferenceEquals(shiftCue.FindResource("ShiftLightOrangeBrush"), Light(3).Fill);
        bool midFourthLightInactive =
            ReferenceEquals(shiftCue.FindResource("ShiftLightInactiveBrush"), Light(4).Fill);

        // 6200 → the terminal latch owns the shift point: pill replaces lights.
        SetLiveState(state, CreatePacket(gear: 1, rpm: 6200f, accel: 255));
        shiftCue.RenderTick();
        Visibility terminalPillVisibility = Pill().Visibility;
        string terminalPillText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        Visibility terminalApproachVisibility = Approach().Visibility;

        // Throttle gate: below 200/255 the approach cue must stay neutral.
        SetLiveState(state, CreatePacket(gear: 1, rpm: 5580f, accel: 100));
        shiftCue.RenderTick();
        Visibility gatedApproachVisibility = Approach().Visibility;
        Visibility gatedPlaceholderVisibility = Placeholder().Visibility;

        // Below the approach window: no cue.
        SetLiveState(state, CreatePacket(gear: 1, rpm: 4800f, accel: 255));
        shiftCue.RenderTick();
        Visibility belowWindowApproachVisibility = Approach().Visibility;

        // Gear 3 has no learned shift point: no cue.
        SetLiveState(state, CreatePacket(gear: 3, rpm: 5580f, accel: 255));
        shiftCue.RenderTick();
        Visibility noTargetApproachVisibility = Approach().Visibility;

        // Gear change into gear 2 (shift point 5850, window [4680, 5850)):
        // 5265 is already inside that window → immediate recalculation.
        SetLiveState(state, CreatePacket(gear: 2, rpm: 5265f, accel: 255));
        shiftCue.RenderTick();
        Visibility gearChangeApproachVisibility = Approach().Visibility;
        int gearChangeActiveLights = CountActiveLights(shiftCue);

        // Stale telemetry clears the cue.
        SetLiveState(state, CreatePacket(gear: 2, rpm: 5265f, accel: 255), live: false);
        shiftCue.RenderTick();
        Visibility noDataApproachVisibility = Approach().Visibility;
        Visibility noDataPillVisibility = Pill().Visibility;

        // Re-entry after the neutral state works.
        SetLiveState(state, CreatePacket(gear: 1, rpm: 4960f, accel: 255));
        shiftCue.RenderTick();
        Visibility reentryApproachVisibility = Approach().Visibility;

        shiftCue.Close();
        state.Dispose();
        return new ApproachCueResult(
            windowStartApproachVisibility,
            windowStartActiveLights,
            windowStartFirstLightYellow,
            windowStartSecondLightInactive,
            midApproachVisibility,
            midActiveLights,
            midFirstLightYellow,
            midThirdLightOrange,
            midFourthLightInactive,
            terminalPillVisibility,
            terminalPillText,
            terminalApproachVisibility,
            gatedApproachVisibility,
            gatedPlaceholderVisibility,
            belowWindowApproachVisibility,
            noTargetApproachVisibility,
            gearChangeApproachVisibility,
            gearChangeActiveLights,
            noDataApproachVisibility,
            noDataPillVisibility,
            reentryApproachVisibility);
    }

    private static int CountActiveLights(ShiftCuePanel shiftCue)
    {
        int count = 0;
        for (int i = 1; i <= UpshiftApproach.LightCount; i++)
        {
            var fill = ((Shapes.Ellipse)shiftCue.FindName($"ApproachLight{i}")!).Fill;
            if (ReferenceEquals(fill, shiftCue.FindResource("ShiftLightYellowBrush"))
                || ReferenceEquals(fill, shiftCue.FindResource("ShiftLightOrangeBrush"))
                || ReferenceEquals(fill, shiftCue.FindResource("ShiftLightRedBrush")))
            {
                count++;
            }
        }

        return count;
    }

    private static void EnsureClickThroughOff()
    {
        if (PanelWindow.ClickThrough)
        {
            PanelWindow.ToggleClickThroughAll();
        }
    }

    private static void ClickMenuItem(ShiftCuePanel shiftCue, string header)
    {
        var item = shiftCue.ContextMenu!.Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void ForceOverlappingAdvisorState(ShiftPointAdvisor advisor)
    {
        var shiftRpmByGear = (Dictionary<int, float>)typeof(ShiftPointAdvisor)
            .GetField("_shiftRpmByGear", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(advisor)!;
        shiftRpmByGear[2] = 3000f;
        typeof(ShiftPointAdvisor).GetField("_downLatched", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(advisor, true);
        typeof(ShiftPointAdvisor).GetField("_downLatchedGear", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(advisor, 2);
    }

    private static bool ReplaySimulator(HudState state, ShiftCuePanel shiftCue)
    {
        state.PowerCurve.Reset();
        state.GearRatios.Reset();
        state.ShiftAdvisor.ResetLatch();
        for (int sample = 0; sample < 18 * 60; sample++)
        {
            double seconds = sample / 60d;
            var packet = CreateSimulatorPacket(seconds);
            SetLiveState(state, packet);
            state.Tick();
            shiftCue.RenderTick();

            var light = (Border)shiftCue.FindName("ShiftLight")!;
            var text = (TextBlock)shiftCue.FindName("ShiftLightText")!;
            if (text.Text == "UPSHIFT" && light.Visibility == Visibility.Visible)
            {
                return true;
            }
        }

        return false;
    }

    private static T RunOnSta<T>(Func<T> action)
    {
        lock (WpfTestLock)
        {
            T? result = default;
            Exception? failure = null;
            using var completed = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    completed.Set();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), "The WPF test thread did not exit.");
            thread.Join();
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return result!;
        }
    }

    private static void SeedShiftAdvisor(HudState state)
    {
        const float maxRpm = 7000f;
        state.PowerCurve.Configure(maxRpm);
        for (float rpm = 25f; rpm <= maxRpm; rpm += 25f)
        {
            float power = rpm <= 5000f
                ? 120_000f + (rpm - 1000f) * 45f
                : 300_000f - (rpm - 5000f) * 30f;
            state.PowerCurve.AddSample(rpm, power);
        }

        SeedGearRatio(state.GearRatios, gear: 1, rpm: 6000f, speedMs: 20f);
        SeedGearRatio(state.GearRatios, gear: 2, rpm: 6000f, speedMs: 30f);
        SeedGearRatio(state.GearRatios, gear: 3, rpm: 6000f, speedMs: 40f);
        state.ShiftAdvisor.Recalculate(maxRpm);
    }

    private static void SetLiveState(HudState state, Fh6Packet packet, bool live = true)
    {
        typeof(HudState).GetProperty(nameof(HudState.Latest))!.SetValue(state, packet);
        typeof(HudState).GetProperty(nameof(HudState.LastPacketAtUtc))!.SetValue(state, DateTime.UtcNow);
        typeof(HudState).GetProperty(nameof(HudState.Live))!.SetValue(state, live);
    }

    private static void SeedGearRatio(GearRatioTracker tracker, byte gear, float rpm, float speedMs)
    {
        var packet = CreatePacket(gear, rpm, accel: 0, speedMs: speedMs);
        for (int i = 0; i < GearRatioTracker.MinSamplesPerGear; i++)
        {
            tracker.AddSample(packet);
        }
    }

    private static Fh6Packet CreatePacket(byte gear, float rpm, byte accel, float speedMs = 30f) =>
        Fh6Packet.Parse(new Fh6PacketBuilder()
            .IsRaceOn(1)
            .EngineMaxRpm(7000f)
            .CurrentEngineRpm(rpm)
            .SpeedMs(speedMs)
            .PowerWatts(250_000f)
            .Accel(accel)
            .Gear(gear)
            .Build())!;

    private static Fh6Packet CreateSimulatorPacket(double seconds)
    {
        const float maxRpm = 7000f;
        const float simShiftRpm = 6900f;
        float[] gearTopSpeedMs = { 22.5f, 34.8f, 47.8f, 61.2f, 76.5f, 90f };
        double cycle = seconds % 10.0;
        float speed = cycle switch
        {
            < 0.5 => 0f,
            < 7.5 => (float)((cycle - 0.5) / 7.0 * 90f),
            < 8.5 => 90f,
            _ => (float)(90f * (1 - (cycle - 8.5) / 1.5)),
        };

        int gear = 1;
        while (gear < gearTopSpeedMs.Length
               && speed > gearTopSpeedMs[gear - 1] * (simShiftRpm / maxRpm))
        {
            gear++;
        }

        float topSpeed = gearTopSpeedMs[Math.Clamp(gear - 1, 0, gearTopSpeedMs.Length - 1)];
        float rpm = speed < 0.1f ? 900f : Math.Max(900f, speed / topSpeed * maxRpm);
        float power = rpm <= 5600f
            ? 150_000f + (rpm - 1000f) * ((320_000f - 150_000f) / 4600f)
            : 320_000f * (1f - 0.18f * (rpm - 5600f) / 1400f);

        return Fh6Packet.Parse(new Fh6PacketBuilder()
            .IsRaceOn(1)
            .TimestampMs((uint)(seconds * 1000) % 1_000_000u)
            .EngineMaxRpm(maxRpm)
            .CurrentEngineRpm(rpm)
            .SpeedMs(speed)
            .PowerWatts(power)
            .Accel(255)
            .Gear((byte)gear)
            .Build())!;
    }

    private sealed class TestPanel : PanelWindow
    {
        public TestPanel(HudState state)
            : base(state, PanelKeys.Status)
        {
            Content = new Border
            {
                Width = 240,
                Height = 80,
                Background = System.Windows.Media.Brushes.Transparent,
            };
        }

        public double DragDeltaX { get; set; }

        public double DragDeltaY { get; set; }

        protected override void MoveWindowForDrag()
        {
            Left += DragDeltaX;
            Top += DragDeltaY;
        }

        protected override void Render(Fh6Packet packet)
        {
        }
    }

    private readonly record struct DragResult(
        double InitialLeft,
        double FinalLeft,
        double InitialX,
        double Width);

    private readonly record struct WpfResult(
        DragResult Drag,
        ShiftCueResult LiveCue,
        ApproachCueResult Approach,
        ShiftCueModeResult Modes);

    private readonly record struct ShiftCueResult(
        string UpCueText,
        Visibility UpVisibility,
        Visibility UpPlaceholderVisibility,
        string OverlapCueText,
        string UpCueArrow,
        string DownCueArrow,
        Visibility UpCueVisibility,
        Visibility DownCueVisibility,
        bool SimulatorSawUpshift,
        bool UpUsesDedicatedPill,
        string DownCueText,
        Visibility DownVisibility,
        Visibility DownPlaceholderVisibility,
        bool DownUsesDedicatedPill,
        Visibility BlinkVisibility,
        double BlinkOpacity,
        Visibility ClickThroughCueVisibility,
        Visibility ClickThroughLightVisibility,
        double ClickThroughLightOpacity,
        bool PlaceholderUsesSeparatePill,
        Visibility PlaceholderArrowVisibility,
        string ForcedUpCueText,
        string ForcedUpCueArrow,
        Visibility ForcedUpVisibility,
        string ForcedDownCueText,
        string ForcedDownCueArrow,
        Visibility ForcedDownVisibility);

    private readonly record struct ShiftCueModeResult(
        Visibility PlaceholderWindowVisibility,
        Visibility PlaceholderVisibility,
        Visibility PlaceholderLightVisibility,
        Visibility ClickThroughWindowVisibility);

    private readonly record struct ApproachCueResult(
        Visibility WindowStartApproachVisibility,
        int WindowStartActiveLights,
        bool WindowStartFirstLightYellow,
        bool WindowStartSecondLightInactive,
        Visibility MidApproachVisibility,
        int MidActiveLights,
        bool MidFirstLightYellow,
        bool MidThirdLightOrange,
        bool MidFourthLightInactive,
        Visibility TerminalPillVisibility,
        string TerminalPillText,
        Visibility TerminalApproachVisibility,
        Visibility GatedApproachVisibility,
        Visibility GatedPlaceholderVisibility,
        Visibility BelowWindowApproachVisibility,
        Visibility NoTargetApproachVisibility,
        Visibility GearChangeApproachVisibility,
        int GearChangeActiveLights,
        Visibility NoDataApproachVisibility,
        Visibility NoDataPillVisibility,
        Visibility ReentryApproachVisibility);
}
