using System.Diagnostics;

namespace YfmCompanion.RetroArch;

public static class RetroArchSaveLocator
{
    private static readonly string[] SupportedExtensions = [".srm", ".mcr"];

    public static SaveDiscoveryResult Discover(string? explicitConfigPath = null,
        IEnumerable<string>? additionalLocations = null)
    {
        var paths = FindCandidatePaths(explicitConfigPath, additionalLocations);
        var inspections = paths.Select(Ps1MemoryCardReader.Inspect).ToArray();
        var selected = inspections
            .Where(result => result.IsValid)
            .Select(result => result.Snapshot!)
            .OrderByDescending(snapshot => snapshot.LastWriteTimeUtc)
            .ThenBy(snapshot => snapshot.FilePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return new SaveDiscoveryResult(inspections, selected);
    }

    public static IReadOnlyList<string> FindCandidatePaths(string? explicitConfigPath = null,
        IEnumerable<string>? additionalLocations = null)
    {
        var configPaths = FindConfigPaths(explicitConfigPath);
        var saveDirectories = configPaths
            .Select(ResolveSaveDirectory)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(path => new[] { path!, Path.Combine(path!, "SwanStation") })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToArray();

        var allDirectories = saveDirectories.Concat((additionalLocations ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path))
            .Where(Directory.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var explicitFiles = (additionalLocations ?? []).Where(File.Exists).Select(Path.GetFullPath);
        return [.. allDirectories
            .SelectMany(EnumerateSaveFiles)
            .Concat(explicitFiles)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> EnumerateSaveFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
        // RetroArch can sort saves by core or content directory. One level covers
        // those layouts without recursively walking an arbitrary drive.
        foreach (var child in Directory.EnumerateDirectories(directory))
            foreach (var file in Directory.EnumerateFiles(child)) yield return file;
    }

    public static string? ResolveSaveDirectory(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var setting = File.ReadLines(configPath)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("savefile_directory", StringComparison.OrdinalIgnoreCase));
        if (setting is null)
        {
            return Path.Combine(configDirectory, "saves");
        }

        var equalsIndex = setting.IndexOf('=');
        if (equalsIndex < 0)
        {
            return Path.Combine(configDirectory, "saves");
        }

        var configured = setting[(equalsIndex + 1)..].Trim().Trim('"');
        if (configured.Equals("default", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(configDirectory, "saves");
        }

        if (configured.StartsWith(":\\", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(configDirectory, configured[2..]));
        }

        return Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(configDirectory, configured));
    }

    private static string[] FindConfigPaths(string? explicitConfigPath)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            var fullPath = Path.GetFullPath(explicitConfigPath);
            return File.Exists(fullPath) ? [fullPath] : [];
        }

        try
        {
            foreach (var process in Process.GetProcessesByName("retroarch"))
            {
                using (process)
                {
                    var executable = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executable))
                    {
                        candidates.Add(Path.Combine(Path.GetDirectoryName(executable)!, "retroarch.cfg"));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Process inspection is an optional discovery aid. Other paths remain available.
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(appData, "RetroArch", "retroarch.cfg"));
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Emulation", "retroarch.cfg"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "RetroArch", "retroarch.cfg"));
        }

        return [.. candidates.Where(File.Exists).Order(StringComparer.OrdinalIgnoreCase)];
    }
}
