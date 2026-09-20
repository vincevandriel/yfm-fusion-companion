using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public enum CampaignOpponentScope
{
    GeneralSafety,
    SpecificOpponent,
    FinalGauntlet
}

public sealed record CampaignOptimizationContext(
    CampaignOpponentScope Scope,
    OpponentSafetyContext Safety,
    IReadOnlyList<OpponentThreatReport> OpponentThreatReports);

public sealed record CampaignDeckPlan(
    CampaignOptimizationContext Context,
    StarChipDeckPlan DeckPlan,
    string Methodology)
{
    public const string BestFoundMethodology =
        "Best-found campaign plan using concrete fusion threats, conservative Guardian-Star counters, owned-card limits, and exact five-card finalist analysis. Opponent weights rank opportunities; they are not duel win probabilities.";
}

public sealed class CampaignOptimizationContextBuilder(
    FusionCatalog catalog,
    CampaignResearchData researchData)
{
    private readonly FusionCatalog _catalog = catalog;
    private readonly CampaignResearchData _researchData = researchData;
    private readonly OpponentThreatEvaluator _threatEvaluator = new(catalog);

    public CampaignOptimizationContext Build(
        CampaignOpponentScope scope,
        int? specificOpponentId = null,
        int threatsPerOpponent = 5)
    {
        if (threatsPerOpponent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threatsPerOpponent));
        }

        var opponentIds = scope switch
        {
            CampaignOpponentScope.GeneralSafety => _researchData.Policy.GeneralSafetyDuelistIds.Order().ToArray(),
            CampaignOpponentScope.FinalGauntlet => _researchData.Policy.FinalGauntletDuelistIds.Order().ToArray(),
            CampaignOpponentScope.SpecificOpponent when specificOpponentId is not null => [specificOpponentId.Value],
            CampaignOpponentScope.SpecificOpponent => throw new ArgumentException(
                "A specific opponent ID is required for opponent-specific optimization.", nameof(specificOpponentId)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        if (opponentIds.Any(id => !_researchData.Opponents.ContainsKey(id)))
        {
            throw new ArgumentOutOfRangeException(nameof(specificOpponentId), "The selected opponent is not present in the research data.");
        }

        var reports = new List<OpponentThreatReport>(opponentIds.Length);
        var targets = new List<DeckSafetyTarget>(opponentIds.Length * (threatsPerOpponent + 1));
        var monsterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var opponentId in opponentIds)
        {
            var opponent = _researchData.Opponents[opponentId];
            foreach (var entry in opponent.DeckPool)
            {
                var card = _catalog.GetCard(entry.CardId);
                if (card.Attack > 0 || card.Defense > 0)
                {
                    monsterTypes.Add(card.PrimaryType);
                }
            }

            var report = _threatEvaluator.Evaluate(
                opponent.DeckPool,
                includeGlitches: false,
                maximumFusionThreats: threatsPerOpponent);
            reports.Add(report);
            targets.Add(ToTarget(opponent, report.StrongestBaseMonster, 1.0, isFusionThreat: false));

            var highestOpportunity = Math.Max(1L, report.FusionThreats.Select(item => item.OpportunityWeight).DefaultIfEmpty(1).Max());
            foreach (var threat in report.FusionThreats)
            {
                var normalizedOpportunity = (double)threat.OpportunityWeight / highestOpportunity;
                targets.Add(ToTarget(
                    opponent,
                    threat.Result,
                    0.75 + normalizedOpportunity,
                    isFusionThreat: true));
            }
        }

        var label = scope switch
        {
            CampaignOpponentScope.GeneralSafety => "General campaign safety (33 opponents; final gauntlet excluded)",
            CampaignOpponentScope.FinalGauntlet => "Final gauntlet special configuration",
            CampaignOpponentScope.SpecificOpponent => $"Opponent-specific: {_researchData.Opponents[opponentIds[0]].Name}",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        var safety = new OpponentSafetyContext(
            label,
            opponentIds,
            monsterTypes.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            targets,
            "Counters use each concrete opponent Deck pool and reachable non-glitch fusion pairs. Guardian-Star scoring assumes the player chooses their better printed star and conservatively tests every printed enemy star. AI star choice is not inferred. Opportunity weights are not win probabilities.");
        return new CampaignOptimizationContext(scope, safety, reports);
    }

    private static DeckSafetyTarget ToTarget(
        OpponentReference opponent,
        Card card,
        double importance,
        bool isFusionThreat) => new(
            opponent.DuelistId,
            opponent.Name,
            card.Id,
            card.Name,
            card.Attack,
            new[] { card.GuardianStar1, card.GuardianStar2 }
                .Where(star => !string.IsNullOrWhiteSpace(star))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            importance,
            isFusionThreat);
}

public sealed class CampaignDeckOptimizer(
    FusionCatalog catalog,
    CampaignResearchData researchData)
{
    private readonly CampaignOptimizationContextBuilder _contextBuilder = new(catalog, researchData);
    private readonly StarChipDeckPlanner _deckPlanner = new(catalog);

    public CampaignDeckPlan Optimize(
        IEnumerable<OwnedCardQuantity> ownedCards,
        uint starChips,
        bool useStarChips,
        CampaignOpponentScope scope,
        int? specificOpponentId = null,
        DeckOptimizationOptions? options = null,
        IEnumerable<int>? currentDeckCardIds = null,
        IProgress<DeckOptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var context = _contextBuilder.Build(scope, specificOpponentId);
        var secondaryContext = scope == CampaignOpponentScope.GeneralSafety
            ? _contextBuilder.Build(CampaignOpponentScope.FinalGauntlet).Safety
            : null;
        var baseOptions = options ?? new DeckOptimizationOptions(
            IncludeGlitches: false,
            Profile: DeckStrategyProfile.ControlAndSafety);
        var campaignOptions = baseOptions with
        {
            OpponentMonsterTypes = context.Safety.OpponentMonsterTypes,
            SafetyContext = context.Safety,
            SecondarySafetyContext = secondaryContext
        };
        var plan = _deckPlanner.Plan(
            ownedCards,
            starChips,
            useStarChips,
            campaignOptions,
            currentDeckCardIds,
            progress,
            cancellationToken);
        return new CampaignDeckPlan(context, plan, CampaignDeckPlan.BestFoundMethodology);
    }
}
