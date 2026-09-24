using System.Diagnostics;
using System.Numerics;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record DeckProofPreview(BigInteger CapacityVectors, bool HasFeasibleDeck,
    double? MeasuredDeckMilliseconds, BigInteger? UnprunedWorkLowMilliseconds,
    BigInteger? UnprunedWorkHighMilliseconds, bool PotentiallyImpractical, string Methodology);

public static class DeckProofPreflight
{
    public static Task<DeckProofPreview> PreviewAsync(FusionCatalog catalog, DeckBuildRequest request,
        bool measureEvaluation = true, IProgress<DeckOptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var frozen = DeckProofSearch.Freeze(request);
        return Task.Run(() => Preview(catalog, frozen, measureEvaluation, progress, cancellationToken), CancellationToken.None);
    }

    private static DeckProofPreview Preview(FusionCatalog catalog, DeckBuildRequest request, bool measure,
        IProgress<DeckOptimizationProgress>? progress, CancellationToken token)
    {
        progress?.Report(new("Counting proof search space", 0, 1));
        var space = new DeckQuantitySpace(catalog, request.OwnedCards, request.Options, request.UseStarChips, request.StarChips, token);
        var deck = space.Capacities.SelectMany(c => Enumerable.Repeat(c.CardId, Math.Min(c.Owned, c.Capacity))).Take(40).ToList();
        long spent = 0;
        foreach (var card in space.Capacities.Where(c => c.Capacity > c.Owned && c.PurchaseCost is not null)
            .OrderBy(c => c.PurchaseCost).ThenBy(c => c.CardId))
        {
            token.ThrowIfCancellationRequested();
            if (deck.Count == 40 || spent + card.PurchaseCost!.Value > request.StarChips) break;
            deck.Add(card.CardId);
            spent += card.PurchaseCost.Value;
        }
        var feasible = space.IsLegal(deck, out _);
        double? duration = null;
        BigInteger? low = null, high = null;
        if (feasible && measure)
        {
            var analyzer = new DeckAnalyzer(catalog);
            var clock = Stopwatch.StartNew();
            analyzer.Analyze(deck, request.Options.IncludeGlitches,
                new InlineProgress<DeckAnalysisProgress>(p => progress?.Report(new("Measuring proof evaluation", 0, 1)
                { CompletedHands = p.CompletedHands, TotalHands = p.TotalHands })), token);
            duration = clock.Elapsed.TotalMilliseconds;
            var perDeck = new BigInteger(Math.Max(1, Math.Ceiling(duration.Value)));
            low = space.CapacityVectorCount * perDeck / 2;
            high = space.CapacityVectorCount * perDeck * 2;
        }
        progress?.Report(new("Proof preview available", 1, 1));
        return new(space.CapacityVectorCount, feasible, duration, low, high,
            high > new BigInteger(TimeSpan.FromDays(30).TotalMilliseconds),
            "Count includes every capacity-bounded quantity vector before budget pruning. Work range is a 0.5x–2x extrapolation of one measured deck's hand evaluation, not a confidence interval, wall-time guarantee or exact count of budget-feasible decks. Cache reuse and budget pruning may reduce work; deck complexity, scoring and checkpoint overhead may increase it. No measurement means no duration estimate.");
    }
}
