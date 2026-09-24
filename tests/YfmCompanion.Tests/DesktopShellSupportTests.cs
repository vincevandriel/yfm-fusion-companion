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
            var expected = new DesktopSettings(120, 80, 760, 560, true, true, true, @"X:\Example\save.srm",
                YfmCompanion.RetroArch.CollectionSourceMode.PinnedFile, [@"X:\Example"], @"D:\Card Art", new Dictionary<int, string> { [1] = @"D:\BlueEyes.png" });

            DesktopSettingsStore.Save(expected, path);
            var actual = DesktopSettingsStore.Load(path);

            Assert.Equal(expected.Left, actual.Left);
            Assert.Equal(expected.Top, actual.Top);
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            Assert.Equal(expected.IsMaximized, actual.IsMaximized);
            Assert.Equal(expected.AlwaysOnTop, actual.AlwaysOnTop);
            Assert.Equal(expected.CompactMode, actual.CompactMode);
            Assert.Equal(expected.LastSavePath, actual.LastSavePath);
            Assert.Equal(expected.CollectionSourceMode, actual.CollectionSourceMode);
            Assert.Equal(expected.KnownSaveLocations, actual.KnownSaveLocations);
            Assert.Equal(expected.ArtworkFolder, actual.ArtworkFolder);
            Assert.Equal(expected.ArtworkOverrides, actual.ArtworkOverrides);
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
    public void OptimizerProgressPresentationUsesRealWorkAndNeverInventsVerificationProgress()
    {
        var search = OptimizerProgressPresenter.Present(new(
            YfmCompanion.Engine.DeckBuildState.Searching, "Search budget consumed", TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 123, null, null));
        Assert.Equal(.4, search.Value, 3);
        Assert.False(search.IsIndeterminate);
        Assert.Contains("123 candidates", search.Detail);

        var verify = OptimizerProgressPresenter.Present(new(
            YfmCompanion.Engine.DeckBuildState.Verifying, "Evaluating hands", TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 123, null, null, 213472, 658008));
        Assert.Equal(213472d / 658008d, verify.Value, 6);
        Assert.Contains("213,472 / 658,008 hands", verify.Detail);

        var unknown = OptimizerProgressPresenter.Present(new(
            YfmCompanion.Engine.DeckBuildState.Preparing, "Preparing", TimeSpan.Zero, TimeSpan.Zero, null, 0, null, null));
        Assert.True(unknown.IsIndeterminate);
        Assert.Equal(0, unknown.Value);
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
