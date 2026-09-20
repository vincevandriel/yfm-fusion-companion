using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed class ForbiddenMemoriesStrategyEvaluator(FusionCatalog catalog)
{
    private static readonly Dictionary<int, FieldRule> FieldRules = new()
    {
        [330] = new(["Beast-Warrior", "Insect", "Plant", "Beast"], []),
        [331] = new(["Zombie", "Dinosaur", "Rock"], []),
        [332] = new(["Dragon", "Winged Beast", "Thunder"], []),
        [333] = new(["Warrior", "Beast-Warrior"], []),
        [334] = new(["Aqua", "Fish", "Sea Serpent", "Thunder"], ["Machine", "Pyro"]),
        [335] = new(["Spellcaster", "Fiend"], ["Fairy"])
    };

    private static readonly Dictionary<int, string> TargetedRemovalTypes = new()
    {
        [329] = "Dragon",
        [653] = "Warrior",
        [656] = "Zombie",
        [660] = "Machine",
        [662] = "Insect",
        [663] = "Rock",
        [664] = "Fish"
    };

    public CardStrategyAssessment Assess(Card card, DeckOptimizationOptions options)
    {
        var preferredTypes = (options.PreferredMonsterTypes ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var opponentTypes = (options.OpponentMonsterTypes ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (card.Attack > 0 || card.Defense > 0)
        {
            var preferred = preferredTypes.Contains(card.PrimaryType);
            var score = preferred ? 1_800 : 0;
            return new CardStrategyAssessment(
                card,
                preferred ? CardViabilityTier.Synergy : CardViabilityTier.Situational,
                preferred ? "Preferred monster type" : "Monster / fusion material",
                preferred
                    ? $"Matches the requested {card.PrimaryType} focus."
                    : "Value depends on standalone strength and reachable fusion chains.",
                score);
        }

        if (card.PrimaryType.Equals("Equip", StringComparison.OrdinalIgnoreCase))
        {
            var details = catalog.GetAdvancedDetails(card.Id);
            var bonus = card.Id == 657 ? 1_000 : 500;
            var tier = details.CanEquipCount > 100 || card.Id is 657 or 668
                ? CardViabilityTier.Strong
                : CardViabilityTier.Synergy;
            return new CardStrategyAssessment(
                card,
                tier,
                "Terminal power-up",
                $"Adds +{bonus:N0} ATK/DEF to {details.CanEquipCount:N0} compatible monsters and is scored only as the final action.",
                bonus == 1_000 ? 5_000 : 1_200 + (details.CanEquipCount * 4));
        }

        if (FieldRules.TryGetValue(card.Id, out var field))
        {
            var aligned = field.BoostedTypes.Count(preferredTypes.Contains);
            var opposed = field.WeakenedTypes.Count(preferredTypes.Contains);
            var enemyBoosted = field.BoostedTypes.Count(opponentTypes.Contains);
            var enemyWeakened = field.WeakenedTypes.Count(opponentTypes.Contains);
            var explicitlySelected = options.PreferredFieldCardId == card.Id;
            var score = (aligned * 1_500) - (opposed * 2_000) +
                        (enemyWeakened * 1_500) - (enemyBoosted * 750) +
                        (explicitlySelected ? 5_000 : 250);
            return new CardStrategyAssessment(
                card,
                explicitlySelected || aligned > 0 || enemyWeakened > 0
                    ? CardViabilityTier.Strong
                    : CardViabilityTier.Situational,
                "Field spell",
                $"Boosts {string.Join(", ", field.BoostedTypes)} by 500; " +
                (field.WeakenedTypes.Count == 0 ? "has no listed penalty." : $"weakens {string.Join(", ", field.WeakenedTypes)} by 500.") +
                (enemyBoosted + enemyWeakened == 0
                    ? string.Empty
                    : $" Against the selected opponent scope it boosts {enemyBoosted} represented monster types and weakens {enemyWeakened}."),
                score);
        }

        if (TargetedRemovalTypes.TryGetValue(card.Id, out var targetType))
        {
            var matched = opponentTypes.Contains(targetType);
            return new CardStrategyAssessment(
                card,
                matched ? CardViabilityTier.Strong : CardViabilityTier.Situational,
                $"{targetType}-specific removal",
                matched
                    ? $"The selected opponent profile includes {targetType}; this removal is directly relevant."
                    : $"Powerful only when the opponent actually fields {targetType} monsters.",
                matched ? 5_000 : 150);
        }

        return card.Id switch
        {
            337 => Assessment(card, CardViabilityTier.Essential, "Asymmetric board clear", "Destroys every opposing monster without removing your own field.", 7_500),
            336 => Assessment(card, CardViabilityTier.Strong, "Symmetric board clear", "Clears every card in play, including your own; best as a recovery tool.", 5_000),
            348 => Assessment(card, CardViabilityTier.Strong, "Three-turn stall", "Reveals enemy monsters and prevents attacks for three turns.", 4_500),
            686 => Assessment(card, CardViabilityTier.Essential, "Universal attack trap", "Destroys the next opposing monster that attacks regardless of its printed strength.", 6_000),
            685 => Assessment(card, CardViabilityTier.Strong, "Attack-threshold trap", "Destroys an attacking monster at or below 3000 ATK.", 3_800),
            684 => Assessment(card, CardViabilityTier.Situational, "Attack-threshold trap", "Stops attackers only at or below 2000 ATK; it loses relevance late.", 1_100),
            683 => Assessment(card, CardViabilityTier.LowValue, "Attack-threshold trap", "The 1500-ATK ceiling is too low for most later opponents.", -1_000),
            682 => Assessment(card, CardViabilityTier.LowValue, "Attack-threshold trap", "The 1000-ATK ceiling makes it a weak general deck slot.", -2_000),
            681 => Assessment(card, CardViabilityTier.NonViable, "Attack-threshold trap", "The 500-ATK ceiling is non-viable outside a deliberately constructed niche.", -8_000),
            661 => Assessment(card, CardViabilityTier.Strong, "Mass high-ATK removal", "Eliminates opposing monsters with 1500 or more ATK.", 4_000),
            672 => Assessment(card, CardViabilityTier.Situational, "Back-row removal", "Destroys opposing Magic cards; most valuable against magic-heavy duelists.", 1_500),
            669 => Assessment(card, CardViabilityTier.Strong, "Mass debuff", "Reduces every opposing monster by two power levels.", 3_500),
            349 => Assessment(card, CardViabilityTier.Strong, "Mass debuff", "Reduces the power of all enemy monsters.", 3_000),
            320 => Assessment(card, CardViabilityTier.Situational, "Position control", "Forces an opposing defender into attack position.", 700),
            350 => Assessment(card, CardViabilityTier.LowValue, "Information", "Reveals cards but does not directly improve board strength.", -1_500),
            343 or 344 or 345 or 346 => Assessment(card, CardViabilityTier.LowValue, "Low direct damage", "Consumes a deck slot for too little damage to support a strong general strategy.", -3_000),
            347 => Assessment(card, CardViabilityTier.Situational, "Direct damage", "1000 damage can finish a duel or support ranking goals, but does not answer a monster.", 300),
            338 or 339 or 340 => Assessment(card, CardViabilityTier.LowValue, "Life recovery", "Small recovery is usually weaker than a fusion material, removal card, or compatible equip.", -2_500),
            341 or 342 => Assessment(card, CardViabilityTier.Situational, "Large life recovery", "Can extend a duel but does not create board control or fusion consistency.", 100),
            687 or 688 => Assessment(card, CardViabilityTier.LowValue, "Narrow counter trap", "Depends on the opponent using a specific burn or recovery effect and is usually a dead draw.", -4_000),
            689 => Assessment(card, CardViabilityTier.Situational, "Equip counter", "Useful against opponents that rely heavily on power-ups; otherwise inconsistent.", 400),
            690 => Assessment(card, CardViabilityTier.NonViable, "Bluff / rank tool", "Has no combat effect; keep it out unless deliberately pursuing a technical-rank setup.", -9_000),
            _ when card.PrimaryType.Equals("Ritual", StringComparison.OrdinalIgnoreCase) => RitualAssessment(card, options),
            _ => Assessment(card, CardViabilityTier.Situational, "Unmodeled utility", "No reliable general-purpose tactical value is assigned beyond database fusion interactions.", 0)
        };
    }

    public static int GetFieldModifier(int fieldCardId, string monsterType)
    {
        if (!FieldRules.TryGetValue(fieldCardId, out var rule))
        {
            return 0;
        }

        if (rule.BoostedTypes.Contains(monsterType, StringComparer.OrdinalIgnoreCase))
        {
            return 500;
        }

        return rule.WeakenedTypes.Contains(monsterType, StringComparer.OrdinalIgnoreCase) ? -500 : 0;
    }

    public static bool IsFieldCard(int cardId) => FieldRules.ContainsKey(cardId);

    private static CardStrategyAssessment RitualAssessment(Card card, DeckOptimizationOptions options) =>
        options.Profile == DeckStrategyProfile.RitualExperiment
            ? Assessment(card, CardViabilityTier.Situational, "Four-card ritual package", "Only consider with all three exact sacrifices and a payoff that justifies four dedicated cards.", 250)
            : Assessment(card, CardViabilityTier.NonViable, "Four-card ritual package", "Excluded by default because it requires this card plus three exact monsters established on the field.", -10_000);

    private static CardStrategyAssessment Assessment(
        Card card,
        CardViabilityTier tier,
        string role,
        string rationale,
        double score) => new(card, tier, role, rationale, score);

    private sealed record FieldRule(IReadOnlyList<string> BoostedTypes, IReadOnlyList<string> WeakenedTypes);
}
