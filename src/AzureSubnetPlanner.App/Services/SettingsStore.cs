using System.IO;
using System.Text.Json;

namespace AzureSubnetPlanner.App.Services;

public sealed class AppSettings
{
    public string? LocalNetworksText { get; set; }

    public string? SearchPoolsText { get; set; }

    public string? LastCsvPath { get; set; }

    public string? VnetName { get; set; }
}

/// <summary>Stores the user's inputs in %APPDATA%\AzureSubnetPlanner\settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzureSubnetPlanner",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them must not break the app.
        }
    }
}
