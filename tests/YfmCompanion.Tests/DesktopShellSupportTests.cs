using YfmCompanion.Desktop;

namespace YfmCompanion.Tests;

public sealed class DesktopShellSupportTests
{
    [Fact]
    public void SettingsRoundTripWindowModeTopmostAndRememberedSavePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"yfm-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var expected = new DesktopSettings(120, 80, 760, 560, true, true, true, @"X:\Example\save.srm");

            DesktopSettingsStore.Save(expected, path);
            var actual = DesktopSettingsStore.Load(path);

            Assert.Equal(expected, actual);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MalformedSettingsFallBackWithoutThrowing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not json");
            Assert.Equal(new DesktopSettings(), DesktopSettingsStore.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DiagnosticsAreBoundedAndSingleLine()
    {
        var log = new LocalDiagnosticLog();
        for (var index = 0; index < 510; index++)
        {
            log.Add("Live state", $"State {index}", "first line\r\nsecond line");
        }

        var export = log.ExportText();
        var entryLines = export.Split('\n').Count(line => line.Contains(" | Live state | ", StringComparison.Ordinal));
        Assert.Equal(500, entryLines);
        Assert.DoesNotContain("State 0 |", export, StringComparison.Ordinal);
        Assert.Contains("State 509", export, StringComparison.Ordinal);
        Assert.DoesNotContain("first line\r", export, StringComparison.Ordinal);
        Assert.Contains("first line  second line", export, StringComparison.Ordinal);
    }
}
