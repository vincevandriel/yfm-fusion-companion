using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record OpponentDeckPoolEntry(int CardId, int Weight);

public sealed record OpponentFusionThreat(
    Card Result,
    long OpportunityWeight,
    int StrongestMaterialPairAttack,
    IReadOnlyList<(int FirstCardId, int SecondCardId)> RepresentativePairs,
    string? FirstGuardianStar);

public sealed record OpponentThreatReport(
    Card StrongestBaseMonster,
    IReadOnlyList<OpponentFusionThreat> FusionThreats,
    long TotalDeckPoolWeight,
    string Methodology)
{
    public const string NonProbabilityMethodology =
        "Threat ordering uses weighted material-pair opportunity and reachable ATK. It is not a duel win probability or an exact generated-deck probability.";
}

public sealed class OpponentThreatEvaluator(FusionCatalog catalog)
{
    private readonly FusionCatalog _catalog = catalog;

    public OpponentThreatReport Evaluate(
        IEnumerable<OpponentDeckPoolEntry> deckPool,
        bool includeGlitches = false,
        int maximumFusionThreats = 20)
    {
        ArgumentNullException.ThrowIfNull(deckPool);
        if (maximumFusionThreats <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFusionThreats));
        }

        var entries = deckPool.ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("An opponent Deck pool cannot be empty.", nameof(deckPool));
        }

        var duplicate = entries.GroupBy(entry => entry.CardId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Opponent Deck pool contains duplicate card ID {duplicate.Key}.", nameof(deckPool));
        }

        foreach (var entry in entries)
        {
            _ = _catalog.GetCard(entry.CardId);
            if (entry.Weight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deckPool), "Opponent Deck-pool weights must be positive.");
            }
        }

        var monsters = entries
            .Select(entry => _catalog.GetCard(entry.CardId))
            .Where(card => card.Attack > 0 || card.Defense > 0)
            .ToArray();
        if (monsters.Length == 0)
        {
            throw new ArgumentException("The opponent Deck pool contains no monster cards.", nameof(deckPool));
        }

        var accumulated = new Dictionary<int, ThreatAccumulator>();
        for (var firstIndex = 0; firstIndex < entries.Length; firstIndex++)
        {
            for (var secondIndex = firstIndex; secondIndex < entries.Length; secondIndex++)
            {
                var first = entries[firstIndex];
                var second = entries[secondIndex];
                if (!_catalog.TryResolvePair(
                        first.CardId,
                        second.CardId,
                        includeGlitches,
                        out var resultCardId,
                        out _))
                {
                    continue;
                }

                if (!accumulated.TryGetValue(resultCardId, out var threat))
                {
                    threat = new ThreatAccumulator(_catalog.GetCard(resultCardId));
                    accumulated.Add(resultCardId, threat);
                }

                var opportunityWeight = checked((long)first.Weight * second.Weight);
                threat.OpportunityWeight = checked(threat.OpportunityWeight + opportunityWeight);
                threat.StrongestMaterialPairAttack = Math.Max(
                    threat.StrongestMaterialPairAttack,
                    Math.Max(_catalog.GetCard(first.CardId).Attack, _catalog.GetCard(second.CardId).Attack));
                if (threat.RepresentativePairs.Count < 3)
                {
                    threat.RepresentativePairs.Add((first.CardId, second.CardId));
                }
            }
        }

        var fusionThreats = accumulated.Values
            .OrderByDescending(threat => threat.Result.Attack)
            .ThenByDescending(threat => threat.OpportunityWeight)
            .ThenBy(threat => threat.Result.Id)
            .Take(maximumFusionThreats)
            .Select(threat => new OpponentFusionThreat(
                threat.Result,
                threat.OpportunityWeight,
                threat.StrongestMaterialPairAttack,
                threat.RepresentativePairs,
                threat.Result.GuardianStar1))
            .ToArray();

        return new OpponentThreatReport(
            monsters.OrderByDescending(card => card.Attack).ThenBy(card => card.Id).First(),
            fusionThreats,
            entries.Sum(entry => (long)entry.Weight),
            OpponentThreatReport.NonProbabilityMethodology);
    }

    private sealed class ThreatAccumulator(Card result)
    {
        public Card Result { get; } = result;

        public long OpportunityWeight { get; set; }

        public int StrongestMaterialPairAttack { get; set; }

        public List<(int FirstCardId, int SecondCardId)> RepresentativePairs { get; } = [];
    }
}
