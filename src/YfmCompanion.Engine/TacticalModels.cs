using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public enum FieldZone
{
    Monster,
    SpellTrap
}

public sealed record HandCard(int Slot, int CardId);

public sealed record FieldCard(FieldZone Zone, int Slot, int CardId);

public enum TacticalStepKind
{
    StartFromHand,
    StartFromField,
    FuseFromHand,
    FuseOntoField,
    EquipFromHand,
    EquipOntoField
}

public sealed record TacticalStep(
    TacticalStepKind Kind,
    int SourceSlot,
    FieldZone? SourceZone,
    Card Material,
    Card? PriorResult,
    Card Result,
    bool IsGlitch,
    int AttackBonus = 0,
    int DefenseBonus = 0);

public sealed record TacticalRecommendation(
    Card FinalCard,
    IReadOnlyList<TacticalStep> Steps,
    IReadOnlyList<int> ConsumedHandSlots,
    FieldCard? FieldTarget,
    bool ContainsGlitch,
    int AttackBonus = 0,
    int DefenseBonus = 0)
{
    public int FusionCount => Steps.Count(step => step.Kind is TacticalStepKind.FuseFromHand or TacticalStepKind.FuseOntoField);
    public int EquipCount => Steps.Count(step => step.Kind is TacticalStepKind.EquipFromHand or TacticalStepKind.EquipOntoField);
    public int EffectiveAttack => FinalCard.Attack + AttackBonus;
    public int EffectiveDefense => FinalCard.Defense + DefenseBonus;
}
