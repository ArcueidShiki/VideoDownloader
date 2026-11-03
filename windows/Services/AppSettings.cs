using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Persists user preferences to %LOCALAPPDATA%\AnyDownloader\settings.json.
/// Mirrors macOS UserDefaults usage for outputDirectory, maxConcurrent, etc.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "AnyDownloader");

    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.json");

    private static AppSettings? _instance;
    public static AppSettings Instance => _instance ??= Load();

    // ─────────────────────────────────────────────────────
    // Settings properties
    // ─────────────────────────────────────────────────────

    [JsonPropertyName("outputDirectory")]
    public string OutputDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        + Path.DirectorySeparatorChar + "Downloads";

    [JsonPropertyName("maxConcurrentDownloads")]
    public int MaxConcurrentDownloads { get; set; } = 3;

    [JsonPropertyName("defaultMergeToMp4")]
    public bool DefaultMergeToMp4 { get; set; } = true;

    [JsonPropertyName("defaultQuality")]
    public string DefaultQuality { get; set; } = "bestvideo+bestaudio/best";

    // ─────────────────────────────────────────────────────
    // Load / Save
    // ─────────────────────────────────────────────────────

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                    return settings;
            }
        }
        catch (Exception)
        {
            // Ignore; return defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception)
        {
            // Best-effort.
        }
    }
}
