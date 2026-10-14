using System.Globalization;
using System.Text;

namespace BatteryInfo.Core.Diagnostics;

/// <summary>A small file log. Never throws. Device frames are logged only when tracing is on.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly TimeSpan Keep = TimeSpan.FromDays(7);
    private static string? _directory;

    public static bool TraceFrames { get; private set; }

    /// <summary>Raised for every line written (the CLI's --trace and tests listen here).</summary>
    public static event Action<string>? LineWritten;

    /// <summary>Sets where lines go. A null directory keeps lines in memory only (LineWritten).</summary>
    public static void Configure(string? directory, bool traceFrames)
    {
        lock (Gate)
        {
            _directory = directory;
            TraceFrames = traceFrames;
        }
        if (directory is null) return;

        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.GetFiles(directory, "batteryinfo-*.log"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Keep) File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    public static void Frame(string channel, bool outgoing, ReadOnlySpan<byte> data)
    {
        if (!TraceFrames) return;
        Write("FRAME", $"{channel} {(outgoing ? ">>" : "<<")} {Hex(data)}");
    }

    public static string Hex(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 3);
        foreach (var b in data)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static void Write(string level, string message)
    {
        var now = DateTimeOffset.Now;
        var line = $"{now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)} {level} {message}";
        lock (Gate)
        {
            if (_directory is not null)
            {
                try
                {
                    var path = Path.Combine(_directory, $"batteryinfo-{now:yyyyMMdd}.log");
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        LineWritten?.Invoke(line);
    }
}
