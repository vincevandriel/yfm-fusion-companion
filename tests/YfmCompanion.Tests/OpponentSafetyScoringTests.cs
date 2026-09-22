using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class OpponentSafetyScoringTests
{
    [Fact]
    public void GuardianAdvantageThatCrossesAThreatThresholdRaisesCounterValue()
    {
        var safeCounter = Monster(1, "Safe Counter", 1_600, "Sun", null);
        var unsafeCounter = Monster(2, "Unsafe Counter", 1_900, "Venus", null);
        var context = Context([new DeckSafetyTarget(1, "Opponent", 100, "Threat", 2_000, ["Moon"], 1, true)]);

        Assert.Equal(500, OpponentSafetyScoring.ConservativeModifier(safeCounter, ["Moon"]));
        Assert.Equal(-500, OpponentSafetyScoring.ConservativeModifier(unsafeCounter, ["Moon"]));
        Assert.True(
            OpponentSafetyScoring.CounterValue(safeCounter, context) >
            OpponentSafetyScoring.CounterValue(unsafeCounter, context));
    }

    [Fact]
    public void MultipleEnemyStarsUseTheWorstOutcomeAfterThePlayerChoosesTheirBestStar()
    {
        var candidate = Monster(1, "Candidate", 2_000, "Sun", "Mars");

        Assert.Equal(500, OpponentSafetyScoring.ConservativeModifier(candidate, ["Moon"]));
        Assert.Equal(0, OpponentSafetyScoring.ConservativeModifier(candidate, ["Moon", "Saturn"]));
    }

    [Fact]
    public void UnknownGuardianStarsStayNeutralInsteadOfBeingGuessed()
    {
        var candidate = Monster(1, "Candidate", 2_000, null, null);
        var knownCandidate = Monster(2, "Known Candidate", 2_000, "Sun", null);

        Assert.Equal(0, OpponentSafetyScoring.ConservativeModifier(candidate, ["Moon"]));
        Assert.Equal(0, OpponentSafetyScoring.ConservativeModifier(candidate, []));
        Assert.Equal(0, OpponentSafetyScoring.ConservativeModifier(knownCandidate, ["Moon", "Not a star"]));
    }

    [Fact]
    public void SelectedFieldIsAppliedToBothCandidateAndThreatTypes()
    {
        var candidate = new Card(1, "Warrior", null, null, null, 4, "Warrior", null, 2_000, 1_000, null, null, true, true, true);
        var target = new DeckSafetyTarget(1, "Opponent", 100, "Dragon", 2_500, [], 1, false, "Dragon");

        var withoutField = OpponentSafetyScoring.CounterValueForTarget(candidate, target);
        var withSogen = OpponentSafetyScoring.CounterValueForTarget(candidate, target, 333);
        var withMountain = OpponentSafetyScoring.CounterValueForTarget(candidate, target, 332);

        Assert.Equal(0, withoutField);
        Assert.True(withSogen > withoutField);
        Assert.Equal(0, withMountain);
    }

    private static OpponentSafetyContext Context(IReadOnlyList<DeckSafetyTarget> targets) =>
        new("Test", [1], ["Dragon"], targets, "Test methodology");

    private static Card Monster(int id, string name, int attack, string? star1, string? star2) =>
        new(id, name, null, star1, star2, 4, "Warrior", null, attack, 1_000, null, 100, true, true, true);
}
