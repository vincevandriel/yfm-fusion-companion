using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed class TacticalFusionPlanner(FusionCatalog catalog)
{
    public IReadOnlyList<TacticalRecommendation> FindRecommendations(
        IEnumerable<HandCard> handCards,
        IEnumerable<FieldCard>? monsterFieldCards = null,
        IEnumerable<FieldCard>? spellTrapFieldCards = null,
        bool includeGlitches = true)
    {
        var hand = handCards.OrderBy(card => card.Slot).ToArray();
        var monsterField = (monsterFieldCards ?? []).ToArray();
        var spellTrapField = (spellTrapFieldCards ?? []).ToArray();
        ValidateInputs(hand, monsterField, spellTrapField);

        var field = monsterField.Concat(spellTrapField).ToArray();
        var recommendations = new Dictionary<string, TacticalRecommendation>(StringComparer.Ordinal);

        foreach (var first in hand)
        {
            var firstCard = catalog.GetCard(first.CardId);
            var steps = new List<TacticalStep>
            {
                new(TacticalStepKind.StartFromHand, first.Slot, null, firstCard, null, firstCard, false)
            };
            Explore(
                hand,
                [first.Slot],
                firstCard,
                steps,
                containsGlitch: false,
                includeGlitches,
                recommendations);
        }

        foreach (var target in field)
        {
            var targetCard = catalog.GetCard(target.CardId);
            ExploreFromField(
                hand,
                target,
                [],
                targetCard,
                [new(TacticalStepKind.StartFromField, target.Slot, target.Zone, targetCard, null, targetCard, false)],
                containsGlitch: false,
                includeGlitches,
                recommendations);
        }

        return [.. recommendations.Values
            .OrderByDescending(recommendation => recommendation.EffectiveAttack)
            .ThenByDescending(recommendation => recommendation.EffectiveDefense)
            .ThenBy(recommendation => recommendation.ConsumedHandSlots.Count)
            .ThenBy(recommendation => recommendation.FieldTarget is null ? 0 : 1)
            .ThenBy(recommendation => string.Join(',', recommendation.ConsumedHandSlots), StringComparer.Ordinal)];
    }

    private void Explore(
        IReadOnlyList<HandCard> hand,
        HashSet<int> consumedSlots,
        Card current,
        List<TacticalStep> steps,
        bool containsGlitch,
        bool includeGlitches,
        IDictionary<string, TacticalRecommendation> recommendations)
    {
        var fusionCount = steps.Count - 1;
        if (fusionCount > 0)
        {
            AddRecommendation(current, steps, null, containsGlitch, recommendations);
        }

        foreach (var next in hand)
        {
            if (consumedSlots.Contains(next.Slot))
            {
                continue;
            }

            var mayTryOrdinaryFusion = fusionCount > 0 ||
                IsCanonicalInitialOrdinaryFusion(steps[0], next);
            var resolution = mayTryOrdinaryFusion
                ? catalog.Resolve(current.Id, next.CardId, includeGlitches)
                : null;
            if (resolution is not null)
            {
                consumedSlots.Add(next.Slot);
                steps.Add(new TacticalStep(
                    TacticalStepKind.FuseFromHand,
                    next.Slot,
                    null,
                    catalog.GetCard(next.CardId),
                    current,
                    resolution.Result,
                    resolution.IsGlitch));

                Explore(
                    hand,
                    consumedSlots,
                    resolution.Result,
                    steps,
                    containsGlitch || resolution.IsGlitch,
                    includeGlitches,
                    recommendations);

                steps.RemoveAt(steps.Count - 1);
                consumedSlots.Remove(next.Slot);
            }

            var equip = catalog.CanEquip(next.CardId, current.Id)
                ? catalog.ResolveEquip(current.Id, next.CardId)
                : null;
            if (equip is null)
            {
                continue;
            }

            consumedSlots.Add(next.Slot);
            var equipSteps = new List<TacticalStep>(steps)
            {
                new(
                TacticalStepKind.EquipFromHand,
                next.Slot,
                null,
                catalog.GetCard(next.CardId),
                current,
                equip.EquippedCard,
                false,
                equip.AttackBonus,
                equip.DefenseBonus)
            };
            AddRecommendation(
                equip.EquippedCard,
                equipSteps,
                null,
                containsGlitch,
                recommendations,
                equip.AttackBonus,
                equip.DefenseBonus);
            consumedSlots.Remove(next.Slot);
        }
    }

    private void ExploreFromField(
        IReadOnlyList<HandCard> hand,
        FieldCard target,
        HashSet<int> consumedSlots,
        Card current,
        List<TacticalStep> steps,
        bool containsGlitch,
        bool includeGlitches,
        IDictionary<string, TacticalRecommendation> recommendations)
    {
        foreach (var next in hand)
        {
            if (consumedSlots.Contains(next.Slot))
            {
                continue;
            }

            var nextCard = catalog.GetCard(next.CardId);
            var resolution = catalog.Resolve(current.Id, next.CardId, includeGlitches);
            if (resolution is not null)
            {
                consumedSlots.Add(next.Slot);
                steps.Add(new TacticalStep(
                    TacticalStepKind.FuseFromHand,
                    next.Slot,
                    null,
                    nextCard,
                    current,
                    resolution.Result,
                    resolution.IsGlitch));
                AddRecommendation(
                    resolution.Result,
                    steps,
                    target,
                    containsGlitch || resolution.IsGlitch,
                    recommendations);
                ExploreFromField(
                    hand,
                    target,
                    consumedSlots,
                    resolution.Result,
                    steps,
                    containsGlitch || resolution.IsGlitch,
                    includeGlitches,
                    recommendations);
                steps.RemoveAt(steps.Count - 1);
                consumedSlots.Remove(next.Slot);
            }

            var equip = catalog.CanEquip(next.CardId, current.Id)
                ? catalog.ResolveEquip(current.Id, next.CardId)
                : null;
            if (equip is not null)
            {
                consumedSlots.Add(next.Slot);
                var equipSteps = new List<TacticalStep>(steps)
                {
                    new(
                        TacticalStepKind.EquipFromHand,
                        next.Slot,
                        null,
                        nextCard,
                        current,
                        equip.EquippedCard,
                        false,
                        equip.AttackBonus,
                        equip.DefenseBonus)
                };
                AddRecommendation(
                    equip.EquippedCard,
                    equipSteps,
                    target,
                    containsGlitch,
                    recommendations,
                    equip.AttackBonus,
                    equip.DefenseBonus);
                consumedSlots.Remove(next.Slot);
            }
        }
    }

    private static void AddRecommendation(
        Card result,
        IReadOnlyList<TacticalStep> steps,
        FieldCard? target,
        bool containsGlitch,
        IDictionary<string, TacticalRecommendation> recommendations,
        int attackBonus = 0,
        int defenseBonus = 0)
    {
        var copiedSteps = steps.ToArray();
        var slotsInSelectionOrder = copiedSteps
            .Where(step => step.Kind is TacticalStepKind.StartFromHand or TacticalStepKind.FuseFromHand or TacticalStepKind.EquipFromHand)
            .Select(step => step.SourceSlot)
            .ToArray();
        var targetKey = target is null ? "none" : $"{target.Zone}:{target.Slot}";
        var key = $"{string.Join('-', slotsInSelectionOrder)}|{targetKey}|{result.Id}|{attackBonus}|{defenseBonus}";
        recommendations.TryAdd(key, new TacticalRecommendation(
            result,
            copiedSteps,
            slotsInSelectionOrder,
            target,
            containsGlitch,
            attackBonus,
            defenseBonus));
    }

    private static void ValidateInputs(
        HandCard[] hand,
        FieldCard[] monsterField,
        FieldCard[] spellTrapField)
    {
        if (hand.Length > 5)
        {
            throw new ArgumentException("At most five hand cards are allowed.", nameof(hand));
        }

        if (monsterField.Length > 5 || monsterField.Any(card => card.Zone != FieldZone.Monster))
        {
            throw new ArgumentException("The monster field accepts at most five Monster-zone cards.", nameof(monsterField));
        }

        if (spellTrapField.Length > 5 || spellTrapField.Any(card => card.Zone != FieldZone.SpellTrap))
        {
            throw new ArgumentException("The spell/trap field accepts at most five SpellTrap-zone cards.", nameof(spellTrapField));
        }

        RequireUniqueSlots(hand.Select(card => card.Slot), "hand");
        RequireUniqueSlots(monsterField.Select(card => card.Slot), "monster field");
        RequireUniqueSlots(spellTrapField.Select(card => card.Slot), "spell/trap field");
        RequireSlotRange(hand.Select(card => card.Slot), "hand");
        RequireSlotRange(monsterField.Select(card => card.Slot), "monster field");
        RequireSlotRange(spellTrapField.Select(card => card.Slot), "spell/trap field");
    }

    private static void RequireUniqueSlots(IEnumerable<int> slots, string source)
    {
        var values = slots.ToArray();
        if (values.Distinct().Count() != values.Length)
        {
            throw new ArgumentException($"Duplicate {source} slot.");
        }
    }

    private static void RequireSlotRange(IEnumerable<int> slots, string source)
    {
        if (slots.Any(slot => slot is < 1 or > 5))
        {
            throw new ArgumentOutOfRangeException(source, "Slots must be numbered from 1 through 5.");
        }
    }

    private static bool IsCanonicalInitialOrdinaryFusion(TacticalStep firstStep, HandCard second)
    {
        if (firstStep.Material.Id != second.CardId)
        {
            return firstStep.Material.Id < second.CardId;
        }

        // Equal card IDs are still distinct physical copies. Slot order gives the
        // unordered initial pair one stable representation without collapsing copies.
        return firstStep.SourceSlot < second.Slot;
    }
}
