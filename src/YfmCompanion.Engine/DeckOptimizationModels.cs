using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record OwnedCardQuantity(int CardId, int Quantity);

public enum DeckStrategyProfile
{
    Balanced,
    FusionConsistency,
    MaximumPower,
    ControlAndSafety,
    FieldAndType,
    RitualExperiment
}

public enum CardViabilityTier
{
    Essential,
    Strong,
    Synergy,
    Situational,
    LowValue,
    NonViable
}

public sealed record DeckOptimizationOptions(
    int CopyLimit = 3,
    int SampleHands = 160,
    int ExactFinalists = 2,
    int RandomSeed = 0x59464D,
    bool IncludeGlitches = true,
    DeckStrategyProfile Profile = DeckStrategyProfile.Balanced,
    IReadOnlyList<string>? PreferredMonsterTypes = null,
    int? PreferredFieldCardId = null,
    IReadOnlyList<string>? OpponentMonsterTypes = null);

public sealed record CardStrategyAssessment(
    Card Card,
    CardViabilityTier Tier,
    string Role,
    string Rationale,
    double StrategicScore);

public sealed record DeckOptimizationProgress(string Stage, int Completed, int Total)
{
    public double Fraction => Total == 0 ? 1 : (double)Completed / Total;
}

public sealed record OptimizedDeckEntry(
    Card Card,
    int Copies,
    string ContributionReason);

public sealed record OptimizationTarget(
    Card Result,
    double Probability,
    int EffectiveAttack,
    bool IsEquipped,
    string RepresentativeRoute);

public sealed record LimitedCardExplanation(
    Card Card,
    int IncludedCopies,
    int OwnedCopies,
    string LimitingReason);

public sealed record DeckComparison(
    DeckAnalysisReport CurrentDeck,
    double AnyFusionProbabilityChange,
    double AtLeast2800ProbabilityChange,
    double ExpectedBestAttackChange);

public sealed record DeckOptimizationReport(
    IReadOnlyList<OptimizedDeckEntry> Deck,
    DeckAnalysisReport ExactAnalysis,
    IReadOnlyList<OptimizationTarget> ImportantTargets,
    IReadOnlyList<LimitedCardExplanation> LimitedCards,
    IReadOnlyList<CardStrategyAssessment> ExcludedOrLowValueCards,
    DeckComparison? Comparison,
    DeckStrategyProfile Profile,
    int RandomSeed)
{
    public int TotalCards => Deck.Sum(entry => entry.Copies);
}
