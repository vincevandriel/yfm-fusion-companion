using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public static class OpponentSafetyScoring
{
    public static double CounterValue(Card candidate, OpponentSafetyContext? context)
    {
        if (context is null || context.Threats.Count == 0 || candidate.Attack <= 0)
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
            var conservativeModifier = ConservativeModifier(candidate, target.PossibleGuardianStars);
            var effectiveAttack = candidate.Attack + conservativeModifier;
            var margin = effectiveAttack - target.Attack;
            var value = margin >= 0
                ? 650 + Math.Min(1_350, margin * 0.45)
                : margin >= -500
                    ? Math.Max(0, 250 + (margin * 0.5))
                    : 0;
            weightedValue += value * importance;
        }

        return weightedValue / totalImportance;
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
