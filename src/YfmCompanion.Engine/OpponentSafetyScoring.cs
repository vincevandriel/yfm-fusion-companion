using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public static class OpponentSafetyScoring
{
    public static double CounterValue(Card candidate, OpponentSafetyContext? context)
    {
        if (context is null || context.Threats.Count == 0)
        {
            return 0;
        }

        var totalImportance = context.Threats.Sum(target => Math.Max(0, target.Importance));
        if (totalImportance <= 0)
        {
            return 0;
        }

        var weightedValue = 0.0;
        foreach (var target in context.Threats)
        {
            var importance = Math.Max(0, target.Importance);
            var value = CounterValueForTarget(candidate, target, context.ActiveFieldCardId);
            weightedValue += value * importance;
        }

        return weightedValue / totalImportance;
    }

    public static double CounterValueForTarget(
        Card candidate,
        DeckSafetyTarget target,
        int? activeFieldCardId = null)
    {
        if (candidate.Id == 337) // Raigeki: a direct answer to every active monster.
        {
            return 2_000;
        }

        if (candidate.Id is 686 or 661)
        {
            return 1_500;
        }

        if (candidate.Attack <= 0)
        {
            return 0;
        }

        var conservativeModifier = ConservativeModifier(candidate, target.PossibleGuardianStars);
        var candidateFieldModifier = activeFieldCardId is int fieldCardId
            ? ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(fieldCardId, candidate.PrimaryType)
            : 0;
        var threatFieldModifier = activeFieldCardId is int activeField && !string.IsNullOrWhiteSpace(target.ThreatPrimaryType)
            ? ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(activeField, target.ThreatPrimaryType)
            : 0;
        var effectiveAttack = candidate.Attack + conservativeModifier + candidateFieldModifier;
        var effectiveThreatAttack = target.Attack + threatFieldModifier;
        var margin = effectiveAttack - effectiveThreatAttack;
        return margin >= 0
            ? 650 + Math.Min(1_350, margin * 0.45)
            : margin >= -500
                ? Math.Max(0, 250 + (margin * 0.5))
                : 0;
    }

    public static int ConservativeModifier(Card candidate, IReadOnlyList<string> opponentGuardianStars)
    {
        var candidateStars = new[] { candidate.GuardianStar1, candidate.GuardianStar2 }
            .Where(star => !string.IsNullOrWhiteSpace(star))
            .Cast<string>()
            .Where(star => GuardianStarRules.TryGetSymbol(star, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nonBlankOpponentStars = opponentGuardianStars
            .Where(star => !string.IsNullOrWhiteSpace(star))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var opponentStars = nonBlankOpponentStars
            .Where(star => GuardianStarRules.TryGetSymbol(star, out _))
            .ToArray();
        if (candidateStars.Length == 0 || opponentStars.Length == 0)
        {
            return 0;
        }

        // The player can choose either star printed on their card. The opponent's
        // exact selection is not proven by the research data, so maximum-safety
        // scoring takes the worst result across all printed enemy options.
        var knownWorstCase = opponentStars.Min(opponentStar =>
            candidateStars.Max(candidateStar => GuardianStarRules.ModifierFor(candidateStar, opponentStar)));
        return opponentStars.Length == nonBlankOpponentStars.Length
            ? knownWorstCase
            : Math.Min(0, knownWorstCase);
    }
}
