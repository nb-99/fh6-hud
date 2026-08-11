using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fh6Hud;
using Fh6Hud.Panels;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Wpf.Tests;

public sealed class PanelWindowDragTests
{
    private static readonly Lock WpfTestLock = new();

    [Fact]
    public void MouseDrag_SavesUpdatedPlacement()
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

            DragResult result = RunOnSta(() => RaiseMouseDrag(configPath));
            var placement = HudConfig.Load(configPath).Panels[PanelKeys.Status];
            var workArea = SystemParameters.WorkArea;

            Assert.InRange(
                Math.Abs((result.InitialLeft + result.Width / 2) - (workArea.Left + workArea.Width * 0.25)),
                0,
                1);
            Assert.True(result.FinalLeft > result.InitialLeft + 20,
                $"Expected the panel to move right, but it moved from {result.InitialLeft:F1} to {result.FinalLeft:F1}.");
            Assert.True(placement.X > result.InitialX,
                $"Expected saved X to increase, but it moved from {result.InitialX:F4} to {placement.X:F4}.");
            Assert.InRange(
                Math.Abs((workArea.Left + placement.X * workArea.Width) - (result.FinalLeft + result.Width / 2)),
                0,
                1);
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

    private static DragResult RaiseMouseDrag(string configPath)
    {
        var app = new App();
        app.InitializeComponent();

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
        app.Shutdown();
        return new DragResult(initialLeft, finalLeft, initialX, width);
    }

    [Fact]
    public void ShiftCue_ShowsLivePillBeforeBlinking()
    {
        ShiftCueResult result = RunOnSta(RenderShiftCue);

        Assert.Equal("UPSHIFT", result.UpCueText);
        Assert.Equal(Visibility.Visible, result.UpVisibility);
        Assert.Equal(Visibility.Collapsed, result.UpPlaceholderVisibility);
        Assert.True(result.UpUsesDedicatedPill);
        Assert.Equal("DOWNSHIFT", result.DownCueText);
        Assert.Equal(Visibility.Visible, result.DownVisibility);
        Assert.Equal(Visibility.Collapsed, result.DownPlaceholderVisibility);
        Assert.True(result.DownUsesDedicatedPill);
        Assert.Equal(Visibility.Hidden, result.BlinkVisibility);
    }

    [Fact]
    public void ShiftCue_ShowsPlaceholderWhenEditingAndHidesInClickThrough()
    {
        ShiftCueModeResult result = RunOnSta(RenderShiftCueModes);

        Assert.Equal(Visibility.Visible, result.PlaceholderWindowVisibility);
        Assert.Equal(Visibility.Visible, result.PlaceholderVisibility);
        Assert.Equal(Visibility.Collapsed, result.PlaceholderLightVisibility);
        Assert.Equal(Visibility.Collapsed, result.ClickThroughWindowVisibility);
    }

    private static ShiftCueResult RenderShiftCue()
    {
        var app = new App();
        app.InitializeComponent();
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
        Visibility upVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        Visibility upPlaceholderVisibility = ((Border)shiftCue.FindName("ShiftPlaceholder")!).Visibility;
        bool upUsesDedicatedPill = ReferenceEquals(
            shiftCue.FindResource("ShiftUpFillBrush"),
            ((Border)shiftCue.FindName("ShiftLight")!).Background);

        clock += 200;
        shiftCue.RenderTick();
        Visibility blinkVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;

        clock += 200;
        SetLiveState(state, CreatePacket(gear: 2, rpm: 3800f, accel: 255));
        shiftCue.RenderTick();
        string downCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        Visibility downVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        Visibility downPlaceholderVisibility = ((Border)shiftCue.FindName("ShiftPlaceholder")!).Visibility;
        bool downUsesDedicatedPill = ReferenceEquals(
            shiftCue.FindResource("ShiftDownFillBrush"),
            ((Border)shiftCue.FindName("ShiftLight")!).Background);

        shiftCue.Close();
        state.Dispose();
        app.Shutdown();
        return new ShiftCueResult(
            upCueText,
            upVisibility,
            upPlaceholderVisibility,
            upUsesDedicatedPill,
            downCueText,
            downVisibility,
            downPlaceholderVisibility,
            downUsesDedicatedPill,
            blinkVisibility);
    }

    private static ShiftCueModeResult RenderShiftCueModes()
    {
        var app = new App();
        app.InitializeComponent();
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
        app.Shutdown();
        return new ShiftCueModeResult(
            placeholderWindowVisibility,
            placeholderVisibility,
            placeholderLightVisibility,
            clickThroughWindowVisibility);
    }

    private static void EnsureClickThroughOff()
    {
        if (PanelWindow.ClickThrough)
        {
            PanelWindow.ToggleClickThroughAll();
        }
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

    private readonly record struct ShiftCueResult(
        string UpCueText,
        Visibility UpVisibility,
        Visibility UpPlaceholderVisibility,
        bool UpUsesDedicatedPill,
        string DownCueText,
        Visibility DownVisibility,
        Visibility DownPlaceholderVisibility,
        bool DownUsesDedicatedPill,
        Visibility BlinkVisibility);

    private readonly record struct ShiftCueModeResult(
        Visibility PlaceholderWindowVisibility,
        Visibility PlaceholderVisibility,
        Visibility PlaceholderLightVisibility,
        Visibility ClickThroughWindowVisibility);
}
