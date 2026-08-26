using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class HudLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fh6hud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        HudLog.Initialize("", enabled: false);
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Disabled_WritesNoFile()
    {
        string path = Path.Combine(_dir, "hud.log");
        HudLog.Initialize(path, enabled: false);
        HudLog.Info("must not be written");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Disabled_WritesHealthRecords()
    {
        string path = Path.Combine(_dir, "hud.log");
        Directory.CreateDirectory(_dir);
        HudLog.Initialize(path, enabled: false);
        HudLog.Health("[HEALTH] still alive");

        string[] lines = File.ReadAllLines(path);
        Assert.Contains(lines, l => l.Contains("[HEALTH] [HEALTH] still alive", StringComparison.Ordinal));
    }

    [Fact]
    public void Disabled_WritesErrorRecords()
    {
        string path = Path.Combine(_dir, "hud.log");
        Directory.CreateDirectory(_dir);
        HudLog.Initialize(path, enabled: false);
        HudLog.Error("crash signal", new InvalidOperationException("nope"));

        string[] lines = File.ReadAllLines(path);
        Assert.Contains(lines, l => l.Contains("[ERROR] crash signal: System.InvalidOperationException: nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_WritesTimestampedLines()
    {
        string path = Path.Combine(_dir, "hud.log");
        Directory.CreateDirectory(_dir);
        HudLog.Initialize(path, enabled: true);
        HudLog.Info("hello");
        HudLog.Debug("world");
        HudLog.Error("boom", new InvalidOperationException("nope"));

        string[] lines = File.ReadAllLines(path);
        Assert.Contains(lines, l => l.Contains("[INFO] hello", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("[DEBUG] world", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("[ERROR] boom: System.InvalidOperationException: nope", StringComparison.Ordinal));
    }

    [Fact]
    public void DisabledAfterEnable_StopsWriting()
    {
        string path = Path.Combine(_dir, "hud.log");
        Directory.CreateDirectory(_dir);
        HudLog.Initialize(path, enabled: true);
        HudLog.Info("one");
        HudLog.Initialize(path, enabled: false);
        HudLog.Info("two");

        string[] lines = File.ReadAllLines(path);
        Assert.Contains(lines, l => l.Contains("one", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("two", StringComparison.Ordinal));
    }
}
