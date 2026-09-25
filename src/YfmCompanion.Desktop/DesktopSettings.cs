using System.Text.Json;
using System.IO;
using YfmCompanion.RetroArch;

namespace YfmCompanion.Desktop;

internal sealed record DesktopSettings(
    double? Left = null,
    double? Top = null,
    double? Width = null,
    double? Height = null,
    bool IsMaximized = false,
    bool AlwaysOnTop = false,
    bool CompactMode = false,
    string? LastSavePath = null,
    CollectionSourceMode CollectionSourceMode = CollectionSourceMode.AutomaticNewest,
    IReadOnlyList<string>? KnownSaveLocations = null,
    string? ArtworkFolder = null,
    IReadOnlyDictionary<int, string>? ArtworkOverrides = null,
    IReadOnlyDictionary<int, int>? ManualQuantities = null);

internal static class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsDirectory => Environment.GetEnvironmentVariable("YFM_COMPANION_SETTINGS_DIRECTORY") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YFM Fusion Companion");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static DesktopSettings Load(string? settingsPath = null)
    {
        settingsPath ??= SettingsPath;
        try
        {
            return File.Exists(settingsPath)
                ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(settingsPath), JsonOptions) ?? new DesktopSettings()
                : new DesktopSettings();
        }
        catch (Exception)
        {
            return new DesktopSettings();
        }
    }

    public static void Save(DesktopSettings settings, string? settingsPath = null)
    {
        settingsPath ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? SettingsDirectory);
        var temporaryPath = settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, settingsPath, overwrite: true);
    }
}
