using System.IO;

namespace Fh6Hud.Telemetry;

/// <summary>
/// Minimal file logger for diagnosing issues on machines that cannot be
/// inspected directly. Writes timestamped lines to a single log file; enabled
/// via the <c>DebugLog</c> config option or the <c>--debug</c> CLI flag. Health
/// records are intentionally available even when verbose debug logging is off.
/// Logging must never take the HUD down, so write failures are swallowed.
/// </summary>
public static class HudLog
{
    private enum LogLevel
    {
        Debug,
        Info,
        Health,
        Error,
    }

    private static readonly object Sync = new();

    /// <summary>True once <see cref="Initialize"/> was called with enabled=true.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Absolute path of the log file (null until initialized).</summary>
    public static string? FilePath { get; private set; }

    /// <summary>Enables (or disables) logging and pins the log file path.</summary>
    public static void Initialize(string filePath, bool enabled)
    {
        bool writeStartLine;
        lock (Sync)
        {
            writeStartLine = enabled
                && (!Enabled || !string.Equals(FilePath, filePath, StringComparison.Ordinal));
            FilePath = filePath;
            Enabled = enabled;
        }

        if (writeStartLine)
        {
            Write(LogLevel.Info, $"HUD log started -> {filePath}");
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Info(string message) => Write(LogLevel.Info, message);

    /// <summary>Writes a throttled runtime health record even when debug is off.</summary>
    public static void Health(string message) => Write(LogLevel.Health, message, always: true);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(string message, Exception exception) =>
        Write(LogLevel.Error, $"{message}: {exception}");

    private static void Write(LogLevel level, string message, bool always = false)
    {
        lock (Sync)
        {
            string? path = FilePath;
            if ((!always && !Enabled) || string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never break the HUD.
            }
        }
    }
}
