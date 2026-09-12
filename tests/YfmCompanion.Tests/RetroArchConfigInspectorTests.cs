using YfmCompanion.RetroArch;

namespace YfmCompanion.Tests;

public sealed class RetroArchConfigInspectorTests
{
    [Fact]
    public void ReadsEnabledStatePortAndSaveDirectory()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                # ignored comment
                network_cmd_enable = "true"
                network_cmd_port = "55356"
                savefile_directory = ":\saves"
                malformed line
                """);

            var configuration = RetroArchConfigInspector.Read(path);

            Assert.True(configuration.Exists);
            Assert.True(configuration.NetworkCommandsEnabled);
            Assert.Equal(55356, configuration.NetworkCommandPort);
            Assert.Equal(@":\saves", configuration.SaveDirectory);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void InvalidPortsFallBackToTheRetroArchDefault(string configuredPort)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"network_cmd_enable = \"false\"\nnetwork_cmd_port = \"{configuredPort}\"\n");

            var configuration = RetroArchConfigInspector.Read(path);

            Assert.False(configuration.NetworkCommandsEnabled);
            Assert.Equal(RetroArchConfigInspector.DefaultNetworkCommandPort, configuration.NetworkCommandPort);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingConfigurationIsRepresentedWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-retroarch-{Guid.NewGuid():N}.cfg");
        var configuration = RetroArchConfigInspector.Read(path);

        Assert.False(configuration.Exists);
        Assert.False(configuration.NetworkCommandsEnabled);
        Assert.Equal(RetroArchConfigInspector.DefaultNetworkCommandPort, configuration.NetworkCommandPort);
    }
}
