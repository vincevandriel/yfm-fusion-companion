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
    // Potential setups using only cards in this hand; field/equips may need separate turns.
    // These are not immediate-play or duel-win probabilities.
    public long HandsWith3500Setup { get; init; }
    public long HandsWith4500Setup { get; init; }
    public long HandsWithEndgamePower { get; init; }
    public long HandsWithEndgamePowerOrBoardClear { get; init; }
    public long HandsWith2800Body { get; init; }
    public long HandsWithNoMonster { get; init; }
    public double EndgamePowerProbability => Probability(HandsWithEndgamePower);
    public double EndgameAnswerProbability => Probability(HandsWithEndgamePowerOrBoardClear);
    public double Body2800Probability => Probability(HandsWith2800Body);
    public double NoMonsterProbability => Probability(HandsWithNoMonster);
    public long HandsWithBoardClear { get; init; }
    public long HandsWithBroadRemoval { get; init; }
    public long HandsWith3500SetupOrBoardClear { get; init; }
    public int MaximumSetupAttack { get; init; }
    public long TotalBestSetupAttack { get; init; }
    public double Setup3500Probability => Probability(HandsWith3500Setup);
    public double Setup4500Probability => Probability(HandsWith4500Setup);
    public double BoardClearProbability => Probability(HandsWithBoardClear);
    public double SetupOrClearProbability => Probability(HandsWith3500SetupOrBoardClear);
    public long TotalBestFusionAttack { get; init; }
    public bool IsExact { get; init; } = true;
    public int SampleCount { get; init; }
    // Conservative 95% normal-approximation margin for sampled proportions, not a win rate.
    public double ProbabilityMargin95 => IsExact || SampleCount == 0 ? 0 : Math.Min(1, 1.96 * Math.Sqrt(0.25 / SampleCount));

    public double AnyFusionProbability => Probability(HandsWithAnyFusion);
    public double AtLeast2000Probability => Probability(HandsAtLeast2000);
    public double AtLeast2500Probability => Probability(HandsAtLeast2500);
    public double AtLeast2800Probability => Probability(HandsAtLeast2800);
    public double AtLeast3000Probability => Probability(HandsAtLeast3000);
    public double DeadHandProbability => TotalHands == 0 ? 0 : 1 - AnyFusionProbability;

    private double Probability(long hands) => TotalHands == 0 ? 0 : (double)hands / TotalHands;
}
