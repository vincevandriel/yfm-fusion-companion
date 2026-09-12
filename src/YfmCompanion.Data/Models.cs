namespace YfmCompanion.Data;

public sealed record Card(
    int Id,
    string Name,
    string? Description,
    string? GuardianStar1,
    string? GuardianStar2,
    int? Level,
    string PrimaryType,
    string? Attribute,
    int Attack,
    int Defense,
    string? Password,
    int? StarchipCost,
    bool Droppable,
    bool Fusible,
    bool StarterAvailable);

public sealed record FusionPair(
    int MaterialLowId,
    int MaterialHighId,
    int ResultCardId,
    bool IsIntended,
    bool IsGlitch);

public sealed record FusionRuleReference(
    int RuleId,
    int? TierId,
    string RuleKind,
    string Material1Expression,
    string Material2Expression);

public sealed record FusionResolution(
    Card FirstMaterial,
    Card SecondMaterial,
    Card Result,
    bool IsIntended,
    bool IsGlitch,
    IReadOnlyList<FusionRuleReference> RuleReferences);

public sealed record EquipResolution(
    Card EquipCard,
    Card EquippedCard,
    int AttackBonus,
    int DefenseBonus);

public sealed record CardAdvancedDetails(
    Card Card,
    IReadOnlyList<string> Categories,
    int FusionPartnerCount,
    int FusionRecipeCount,
    int CanEquipCount,
    int EquippedByCount);

public sealed record DataBuildReport(
    string SourcePath,
    string OutputPath,
    string SourceSha256,
    long SourceLength,
    IReadOnlyDictionary<string, long> ImportedRows,
    IReadOnlyDictionary<string, long> IntegrityCounts);
