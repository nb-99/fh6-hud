using System.IO;

namespace Fh6Hud.Telemetry;

/// <summary>
/// Minimal file logger for diagnosing issues on machines that cannot be
/// inspected directly. Writes timestamped lines to a single log file; enabled
/// via the <c>DebugLog</c> config option or the <c>--debug</c> CLI flag.
/// Logging must never take the HUD down, so write failures are swallowed.
/// </summary>
public static class HudLog
{
    private static readonly object Sync = new();

    /// <summary>True once <see cref="Initialize"/> was called with enabled=true.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Absolute path of the log file (null until initialized).</summary>
    public static string? FilePath { get; private set; }

    /// <summary>Enables (or disables) logging and pins the log file path.</summary>
    public static void Initialize(string filePath, bool enabled)
    {
        FilePath = filePath;
        Enabled = enabled;
        if (enabled)
        {
            Write("INFO", $"HUD log started -> {filePath}");
        }
    }

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        if (!Enabled || string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        lock (Sync)
        {
            try
            {
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never break the HUD.
            }
        }
    }
}
