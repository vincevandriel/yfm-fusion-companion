using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record DeckFusionRoute(
    IReadOnlyList<Card> Materials,
    IReadOnlyList<Card> IntermediateResults,
    bool ContainsGlitch,
    bool EndsWithEquip = false,
    int EquipBonus = 0)
{
    public int MaterialCount => Materials.Count;
}

public sealed record DeckFusionResult(
    Card Result,
    long HandsContainingResult,
    long TotalHands,
    DeckFusionRoute RepresentativeRoute,
    int AttackBonus = 0,
    int DefenseBonus = 0)
{
    public double Probability => TotalHands == 0 ? 0 : (double)HandsContainingResult / TotalHands;
    public int EffectiveAttack => Result.Attack + AttackBonus;
    public int EffectiveDefense => Result.Defense + DefenseBonus;
    public bool IsEquipped => RepresentativeRoute.EndsWithEquip;
}

public sealed record DeckAnalysisProgress(long CompletedHands, long TotalHands)
{
    public double Fraction => TotalHands == 0 ? 1 : (double)CompletedHands / TotalHands;
}

public sealed record DeckAnalysisReport(
    int DeckSize,
    int HandSize,
    long TotalHands,
    long HandsWithAnyFusion,
    long HandsAtLeast2000,
    long HandsAtLeast2500,
    long HandsAtLeast2800,
    long HandsAtLeast3000,
    double ExpectedBestFusionAttack,
    IReadOnlyList<DeckFusionResult> FusionResults)
{
    public double AnyFusionProbability => Probability(HandsWithAnyFusion);
    public double AtLeast2000Probability => Probability(HandsAtLeast2000);
    public double AtLeast2500Probability => Probability(HandsAtLeast2500);
    public double AtLeast2800Probability => Probability(HandsAtLeast2800);
    public double AtLeast3000Probability => Probability(HandsAtLeast3000);
    public double DeadHandProbability => TotalHands == 0 ? 0 : 1 - AnyFusionProbability;

    private double Probability(long hands) => TotalHands == 0 ? 0 : (double)hands / TotalHands;
}
