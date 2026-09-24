using System.Numerics;

namespace YfmCompanion.Engine;

public sealed record DeckObjective(DeckAnalysisReport Analysis, DeckSafetyAssessment? Safety,
    DeckSafetyAssessment? SecondarySafety, IReadOnlyList<int> Cards, long RequiredStarChips = 0);

/// <summary>Positive means better. Compare only evaluations of the same frozen objective.</summary>
public sealed class DeckObjectiveComparer(bool campaign, bool gauntletTieBreak) : IComparer<DeckObjective>
{
    public const string Version = "campaign-lexicographic-v2";
    public int Compare(DeckObjective? x, DeckObjective? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int difference;
        if (campaign && (difference = CompareSafety(x.Safety, y.Safety)) != 0) return difference;
        if (gauntletTieBreak)
        {
            if ((difference = ComparePower(x.Analysis, y.Analysis, binned: true)) != 0) return difference;
            if ((difference = CompareSafety(x.SecondarySafety, y.SecondarySafety)) != 0) return difference;
        }
        if ((difference = ComparePower(x.Analysis, y.Analysis, binned: false)) != 0) return difference;
        if ((difference = y.RequiredStarChips.CompareTo(x.RequiredStarChips)) != 0) return difference;
        var first = x.Cards.Order().ToArray();
        var second = y.Cards.Order().ToArray();
        for (var i = 0; i < Math.Min(first.Length, second.Length); i++)
            if ((difference = second[i].CompareTo(first[i])) != 0) return difference;
        return second.Length.CompareTo(first.Length);
    }

    public static DeckObjective FromReport(DeckOptimizationReport report, long spent = 0) => new(
        report.ExactAnalysis, report.SafetyAssessment, report.SecondarySafetyAssessment,
        report.Deck.SelectMany(e => Enumerable.Repeat(e.Card.Id, e.Copies)).ToArray(), spent);

    private static int CompareSafety(DeckSafetyAssessment? x, DeckSafetyAssessment? y)
    {
        if (x is null || y is null) return (x is not null).CompareTo(y is not null);
        var difference = x.SafeOpponentCount.CompareTo(y.SafeOpponentCount);
        if (difference != 0) return difference;
        // Defined millionth-unit model scores, not approximate-equality tests between candidates.
        difference = Units(x.WorstOpponentScore).CompareTo(Units(y.WorstOpponentScore));
        if (difference != 0) return difference;
        difference = Units(x.EstimatedOpeningAnswerCoverage).CompareTo(Units(y.EstimatedOpeningAnswerCoverage));
        return difference != 0 ? difference : Units(x.HeuristicScore).CompareTo(Units(y.HeuristicScore));
    }

    private static BigInteger Units(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Objective scores must be finite.");
        return new BigInteger(decimal.Round((decimal)value * 1_000_000m, 0, MidpointRounding.ToEven));
    }

    private static int ComparePower(DeckAnalysisReport x, DeckAnalysisReport y, bool binned)
    {
        static long Denominator(DeckAnalysisReport report) => Math.Max(1, report.TotalHands);
        var xd = Denominator(x);
        var yd = Denominator(y);
        var first = new[] { x.HandsAtLeast3000, x.HandsAtLeast2800, x.HandsAtLeast2500, x.HandsAtLeast2000, x.HandsWithAnyFusion };
        var second = new[] { y.HandsAtLeast3000, y.HandsAtLeast2800, y.HandsAtLeast2500, y.HandsAtLeast2000, y.HandsWithAnyFusion };
        for (var i = 0; i < first.Length; i++)
        {
            var difference = binned
                ? ((BigInteger)first[i] * 200 / xd).CompareTo((BigInteger)second[i] * 200 / yd)
                : ((BigInteger)first[i] * yd).CompareTo((BigInteger)second[i] * xd);
            if (difference != 0) return difference;
        }
        return binned
            ? ((BigInteger)x.TotalBestFusionAttack / (xd * 25)).CompareTo((BigInteger)y.TotalBestFusionAttack / (yd * 25))
            : ((BigInteger)x.TotalBestFusionAttack * yd).CompareTo((BigInteger)y.TotalBestFusionAttack * xd);
    }
}
