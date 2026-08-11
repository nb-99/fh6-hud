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
            Assert.Equal("UPSHIFT", result.UpCueText);
            Assert.Equal("DOWNSHIFT", result.CueText);
            Assert.Equal("UPSHIFT", result.OverlapCueText);
            Assert.Equal("▲", result.UpCueArrow);
            Assert.Equal("▼", result.DownCueArrow);
            Assert.Equal(Visibility.Visible, result.UpCueVisibility);
            Assert.Equal(Visibility.Visible, result.DownCueVisibility);
            Assert.True(result.SimulatorSawUpshift);
            Assert.True(result.UpUsesDedicatedPill);
            Assert.True(result.DownUsesDedicatedPill);
            Assert.Equal(Visibility.Visible, result.ClickThroughCueVisibility);
            Assert.True(result.PlaceholderUsesSeparatePill);
            Assert.Equal(Visibility.Visible, result.PlaceholderArrowVisibility);
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
        SeedShiftAdvisor(state);
        var shiftCue = new ShiftCuePanel(state);
        shiftCue.Show();
        shiftCue.UpdateLayout();
        var render = typeof(ShiftCuePanel).GetMethod(
            "Render",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Fh6Packet) },
            modifiers: null)!;
        WaitForVisibleBlinkPhase();
        render.Invoke(shiftCue, new object[] { CreatePacket(gear: 1, rpm: 6500f, accel: 255) });
        string upCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        string upCueArrow = ((TextBlock)shiftCue.FindName("ShiftLightArrow")!).Text;
        Visibility upCueVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        bool upUsesDedicatedPill = ReferenceEquals(
            shiftCue.FindResource("ShiftUpFillBrush"),
            ((Border)shiftCue.FindName("ShiftLight")!).Background);
        var downshiftPacket = CreatePacket(gear: 2, rpm: 3800f, accel: 255);
        SetLiveState(state, downshiftPacket);
        WaitForVisibleBlinkPhase();
        shiftCue.RenderTick();
        string cueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        string downCueArrow = ((TextBlock)shiftCue.FindName("ShiftLightArrow")!).Text;
        Visibility downCueVisibility = ((Border)shiftCue.FindName("ShiftLight")!).Visibility;
        bool downUsesDedicatedPill = ReferenceEquals(
            shiftCue.FindResource("ShiftDownFillBrush"),
            ((Border)shiftCue.FindName("ShiftLight")!).Background);
        ForceOverlappingAdvisorState(state.ShiftAdvisor);
        SetLiveState(state, CreatePacket(gear: 2, rpm: 3500f, accel: 255));
        WaitForVisibleBlinkPhase();
        shiftCue.RenderTick();
        string overlapCueText = ((TextBlock)shiftCue.FindName("ShiftLightText")!).Text;
        bool simulatorSawUpshift = ReplaySimulator(state, shiftCue);
        PanelWindow.ToggleClickThroughAll();
        shiftCue.RenderTick();
        Visibility clickThroughCueVisibility = shiftCue.Visibility;
        PanelWindow.ToggleClickThroughAll();
        SetLiveState(state, downshiftPacket, live: false);
        shiftCue.RenderTick();
        Visibility placeholderArrowVisibility = ((TextBlock)shiftCue.FindName("PlaceholderArrow")!).Visibility;
        bool placeholderUsesSeparatePill =
            ((Border)shiftCue.FindName("ShiftPlaceholder")!).Visibility == Visibility.Visible
            && ((Border)shiftCue.FindName("ShiftLight")!).Visibility == Visibility.Collapsed;
        shiftCue.Close();
        state.Dispose();
        app.Shutdown();
        return new DragResult(
            initialLeft,
            finalLeft,
            initialX,
            width,
            upCueText,
            cueText,
            overlapCueText,
            upCueArrow,
            downCueArrow,
            upCueVisibility,
            downCueVisibility,
            simulatorSawUpshift,
            upUsesDedicatedPill,
            downUsesDedicatedPill,
            clickThroughCueVisibility,
            placeholderUsesSeparatePill,
            placeholderArrowVisibility);
    }

    private static void WaitForVisibleBlinkPhase()
    {
        while ((Environment.TickCount64 / 200) % 2 != 0)
        {
            Thread.Sleep(5);
        }
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
        double Width,
        string UpCueText,
        string CueText,
        string OverlapCueText,
        string UpCueArrow,
        string DownCueArrow,
        Visibility UpCueVisibility,
        Visibility DownCueVisibility,
        bool SimulatorSawUpshift,
        bool UpUsesDedicatedPill,
        bool DownUsesDedicatedPill,
        Visibility ClickThroughCueVisibility,
        bool PlaceholderUsesSeparatePill,
        Visibility PlaceholderArrowVisibility);
}
