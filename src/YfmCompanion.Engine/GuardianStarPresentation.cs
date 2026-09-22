using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public enum GuardianBattlePosition
{
    Unknown,
    Attack,
    Defense
}

public sealed record GuardianFieldTarget(
    int Slot,
    int? Attack,
    int? Defense,
    GuardianBattlePosition Position,
    string? SelectedGuardianStar);

public sealed record GuardianLiveAdvice(
    string FirstChoice,
    string SecondChoice,
    string FirstChoiceOutcomes,
    string SecondChoiceOutcomes);

public static class GuardianStarPresentation
{
    public static GuardianLiveAdvice Create(
        Card playerCard,
        int effectiveAttack,
        IEnumerable<GuardianFieldTarget> targets)
    {
        var orderedTargets = targets.OrderBy(target => target.Slot).ToArray();
        var (Chain, Outcomes) = DescribeChoice(playerCard.GuardianStar1, effectiveAttack, orderedTargets);
        var second = DescribeChoice(playerCard.GuardianStar2, effectiveAttack, orderedTargets);
        return new GuardianLiveAdvice(Chain, second.Chain, Outcomes, second.Outcomes);
    }

    private static (string Chain, string Outcomes) DescribeChoice(
        string? playerStar,
        int effectiveAttack,
        GuardianFieldTarget[] targets)
    {
        if (string.IsNullOrWhiteSpace(playerStar) || !GuardianStarRules.TryGetSymbol(playerStar, out _))
        {
            return ("?", FormatUnknownTargets(targets));
        }

        var chain = GuardianStarRules.GetChain(playerStar).CompactNotation;
        var outcomes = targets.Length == 0
            ? "—"
            : string.Join(" ", targets.Select(target => DescribeTarget(
                playerStar,
                effectiveAttack,
                target)));
        return (chain, outcomes);
    }

    private static string FormatUnknownTargets(GuardianFieldTarget[] targets) =>
        targets.Length == 0 ? "—" : string.Join(" ", targets.Select(target => $"F{target.Slot}?"));

    private static string DescribeTarget(
        string playerStar,
        int effectiveAttack,
        GuardianFieldTarget target)
    {
        if (target.Position == GuardianBattlePosition.Unknown ||
            string.IsNullOrWhiteSpace(target.SelectedGuardianStar) ||
            (target.Position == GuardianBattlePosition.Attack && target.Attack is null) ||
            (target.Position == GuardianBattlePosition.Defense && target.Defense is null))
        {
            return $"F{target.Slot}?";
        }

        var enemyValue = target.Position == GuardianBattlePosition.Defense ? target.Defense!.Value : target.Attack!.Value;
        var modifier = GuardianStarRules.ModifierFor(playerStar, target.SelectedGuardianStar);
        if (GuardianStarRules.Resolve(playerStar, target.SelectedGuardianStar) == GuardianStarOutcome.Unknown)
        {
            return $"F{target.Slot}?";
        }

        var comparison = effectiveAttack + modifier - enemyValue;
        return comparison switch
        {
            > 0 => $"F{target.Slot}✓",
            0 => $"F{target.Slot}=",
            _ => $"F{target.Slot}×"
        };
    }
}
