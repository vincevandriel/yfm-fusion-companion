using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class CampaignResearchDataTests
{
    [Fact]
    public void BundledResearchDataLoadsAndRetainsTheRequiredScopeBoundary()
    {
        var data = CampaignResearchData.LoadBundled(AppContext.BaseDirectory);

        Assert.Equal(39, data.Opponents.Count);
        Assert.Equal(33, data.Policy.GeneralSafetyDuelistIds.Count);
        Assert.Equal(6, data.Policy.FinalGauntletDuelistIds.Count);
        Assert.False(data.Policy.StarChipModeDefaultEnabled);
        Assert.Equal("Simon Muran", data.Opponents[1].Name);
        Assert.NotEmpty(data.Opponents[1].DeckPool);
        Assert.Equal("ygofm_gamedata", data.OpponentSource);
        Assert.Equal(40, data.OpponentSourceRevision.Length);
    }

    [Fact]
    public void MissingResearchDirectoryFailsClosed()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<FileNotFoundException>(() => CampaignResearchData.Load(missing));
    }
}
