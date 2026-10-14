using System.Text.Json;
using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.Core.Settings;

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON. A missing or unreadable file gives the defaults;
/// neither method throws for I/O problems.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Path { get; } = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options);
            return settings?.Normalized() ?? new AppSettings();
        }
        catch (JsonException e)
        {
            Log.Warn($"Settings file {Path} is not valid JSON, using defaults: {e.Message}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read settings file {Path}, using defaults: {e.Message}");
        }
        return new AppSettings();
    }

    /// <summary>Writes the settings to a temporary file and then replaces the old file, so a crash never leaves
    /// half a file behind.</summary>
    public void Save(AppSettings settings)
    {
        var temp = Path + ".tmp";
        try
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save settings to {Path}: {e.Message}");
        }
    }
}
