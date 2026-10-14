using System.Text.Json;
using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.Core.Monitoring;

/// <summary>Keeps <see cref="LowBatteryPolicy.Alerted"/> in a JSON file, so a restart does not repeat an alert that
/// was already shown. A missing or unreadable file gives none; neither method throws for I/O problems.</summary>
public sealed class LowBatteryAlertStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Path { get; } = path;

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(Path)) return [];
            var keys = JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(Path), Options);
            return keys?.OfType<string>().ToList() ?? [];
        }
        catch (JsonException e)
        {
            Log.Warn($"Low-battery alert file {Path} is not a list of keys, ignoring it: {e.Message}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read low-battery alert file {Path}, ignoring it: {e.Message}");
        }
        return [];
    }

    /// <summary>Writes the keys to a temporary file and then replaces the old file, so a crash never leaves half a
    /// file behind.</summary>
    public void Save(IEnumerable<string> alerted)
    {
        var temp = Path + ".tmp";
        try
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(alerted.Order(StringComparer.Ordinal).ToList(), Options));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save low-battery alerts to {Path}: {e.Message}");
        }
    }
}
