using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class OptimizerAssessmentReuseTests
{
    [Fact]
    public void PurchasePlanningAnalyzesCurrentDeckOnlyOnceAcrossAlternatives()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "One", 1200) with { Password = "12345678", StarchipCost = 5 },
             TestCatalogFactory.Card(2, "Two", 800) with { Password = "23456789", StarchipCost = 10 },
             TestCatalogFactory.Card(100, "Result", 2800)], [TestCatalogFactory.Pair(1, 2, 100)]);
        var current = Enumerable.Repeat(1, 39).Append(2).ToArray();
        var comparisons = 0;
        var plan = new StarChipDeckPlanner(catalog).Plan([new(1, 39), new(2, 1)], 10, true,
            new(CopyLimit: 40, SampleHands: 12, ExactFinalists: 1), current,
            new Callback<DeckOptimizationProgress>(p =>
            {
                if (p.Stage == "Exact current-deck comparison" && p.Completed == 0 && p.CompletedHands is null) comparisons++;
            }));
        Assert.Equal(1, comparisons);
        Assert.Equal(658008, plan.ResultingDeck.Comparison!.CurrentDeck.TotalHands);
        Assert.Equal(new DeckAnalyzer(catalog, 0).Analyze(current).TotalBestFusionAttack,
            plan.ResultingDeck.Comparison.CurrentDeck.TotalBestFusionAttack);
    }

    [Fact]
    public void RepeatedOpponentScoringMatchesIndependentValuesAndNewContextsRemainDistinct()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "One", 1200), TestCatalogFactory.Card(2, "Two", 800),
             TestCatalogFactory.Card(100, "Result", 2800)], [TestCatalogFactory.Pair(1, 2, 100)]);
        var context = new OpponentSafetyContext("Synthetic", [1, 2], ["Test"],
            [new(1, "Opponent A", 100, "Threat A", 2000, [], 1, false, "Test"),
             new(2, "Opponent B", 100, "Threat B", 1500, [], 1, false, "Test")], "Modeled");
        var owned = new OwnedCardQuantity[] { new(1, 39), new(2, 1) };
        var deck = Enumerable.Repeat(1, 39).Append(2).ToArray();
        var options = new DeckOptimizationOptions(CopyLimit: 40, SafetyContext: context, SecondarySafetyContext: context);
        var optimizer = new OwnedDeckOptimizer(catalog);
        var first = optimizer.EvaluateDeck(deck, owned, options);
        var before = optimizer.CacheDiagnostics;
        var repeated = optimizer.EvaluateDeck(deck, owned, options);
        Assert.Equal(first.SafetyAssessment, repeated.SafetyAssessment);
        Assert.Equal(first.SafetyAssessment, first.SecondarySafetyAssessment);
        Assert.Equal(2, first.SafetyAssessment!.SafeOpponentCount);
        // The singleton second material appears in exactly 5/40 hands. There is one result.
        Assert.Equal(81.25, first.SafetyAssessment.WorstOpponentScore, 8);
        Assert.Equal(131.25, first.SafetyAssessment.HeuristicScore, 8);
        Assert.Equal(.5, first.SafetyAssessment.EstimatedOpeningAnswerCoverage, 8);
        Assert.True(optimizer.CacheDiagnostics.Hits > before.Hits);
        Assert.InRange(optimizer.CacheDiagnostics.AccountedBytes, 0, 256L * 1024 * 1024);
        var changed = context with { Threats = [new(1, "Different", 100, "Impossible", 5000, [], 1, false, "Test")] };
        var different = optimizer.EvaluateDeck(deck, owned, options with { SafetyContext = changed, SecondarySafetyContext = null });
        Assert.Equal(0, different.SafetyAssessment!.SafeOpponentCount);
        Assert.Equal(0, different.SafetyAssessment.HeuristicScore);
    }
    private sealed class Callback<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
