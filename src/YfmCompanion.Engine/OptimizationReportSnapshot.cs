namespace YfmCompanion.Engine;

/// <summary>Own immutable collections across worker, checkpoint, and presentation boundaries.</summary>
internal static class OptimizationReportSnapshot
{
    public static DeckOptimizationReport Freeze(DeckOptimizationReport report) => report with
    {
        Deck = Array.AsReadOnly(report.Deck.ToArray()),
        ExactAnalysis = Freeze(report.ExactAnalysis),
        ImportantTargets = Array.AsReadOnly(report.ImportantTargets.ToArray()),
        LimitedCards = Array.AsReadOnly(report.LimitedCards.ToArray()),
        ExcludedOrLowValueCards = Array.AsReadOnly(report.ExcludedOrLowValueCards.ToArray()),
        Comparison = report.Comparison is { } comparison ? comparison with { CurrentDeck = Freeze(comparison.CurrentDeck) } : null
    };

    private static DeckAnalysisReport Freeze(DeckAnalysisReport report) => report with
    {
        FusionResults = Array.AsReadOnly(report.FusionResults.Select(result => result with
        {
            RepresentativeRoute = result.RepresentativeRoute with
            {
                Materials = Array.AsReadOnly(result.RepresentativeRoute.Materials.ToArray()),
                IntermediateResults = Array.AsReadOnly(result.RepresentativeRoute.IntermediateResults.ToArray())
            }
        }).ToArray())
    };
}
