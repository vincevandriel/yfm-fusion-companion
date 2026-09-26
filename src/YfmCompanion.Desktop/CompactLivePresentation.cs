using System.Globalization;
using YfmCompanion.Engine;

namespace YfmCompanion.Desktop;

internal sealed record CompactLiveRouteRow(
    string Result,
    int Attack,
    string Route,
    string GuardianStar1,
    string GuardianStar2,
    string GuardianOutcomes1,
    string GuardianOutcomes2,
    string GuardianVisual1,
    string GuardianVisual2);

internal static class CompactLivePresentation
{
    public static CompactLiveRouteRow CreateRow(
        TacticalRecommendation recommendation,
        GuardianLiveAdvice? guardianAdvice = null,
        IEnumerable<string?>? enemyGuardianStars = null)
    {
        var enemyStars = enemyGuardianStars?.Where(star => !string.IsNullOrWhiteSpace(star)).ToArray() ?? [];
        return new(
            recommendation.FinalCard.Name,
            recommendation.EffectiveAttack,
            FormatRoute(recommendation),
            guardianAdvice?.FirstChoice ?? "?",
            guardianAdvice?.SecondChoice ?? "?",
            guardianAdvice?.FirstChoiceOutcomes ?? "—",
            guardianAdvice?.SecondChoiceOutcomes ?? "—",
            GuardianVisual(recommendation.FinalCard.GuardianStar1, enemyStars),
            GuardianVisual(recommendation.FinalCard.GuardianStar2, enemyStars));
    }

    private static string GuardianVisual(string? playerStar, IReadOnlyList<string?> enemyStars)
    {
        if (!GuardianStarRules.TryGetSymbol(playerStar, out _)) return "Unknown";
        return enemyStars.Any(enemyStar => GuardianStarRules.Resolve(playerStar, enemyStar) != GuardianStarOutcome.Unknown)
            ? "Threat"
            : "Warm";
    }

    public static string FormatRoute(TacticalRecommendation recommendation)
    {
        var selections = new List<string>(recommendation.ConsumedHandSlots.Count + 1);
        if (recommendation.FieldTarget is FieldCard target)
        {
            var fieldPosition = target.Zone == FieldZone.Monster
                ? target.Slot
                : target.Slot + 5;
            selections.Add($"F({fieldPosition})");
        }

        selections.AddRange(recommendation.ConsumedHandSlots.Select(slot => slot.ToString(CultureInfo.InvariantCulture)));
        return string.Join('+', selections);
    }

}
