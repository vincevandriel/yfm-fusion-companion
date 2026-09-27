using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CampaignDeckLibraryTests(DatabaseFixture fixture)
{
    private FusionCatalog Catalog => fixture.Catalog;

    [Fact]
    public void AllSixReferencesAreLegalObtainableFortyCardDecksWithRealRecipesAndCompatibleSupport()
    {
        var builds = CampaignDeckLibrary.ForCatalog(Catalog);
        Assert.Equal(6, builds.Count);
        Assert.Equal(6, builds.Select(b => b.HeroCardId).Distinct().Count());
        foreach (var build in builds)
        {
            Assert.Equal(40, build.Expand().Length);
            Assert.All(build.Entries, e => { Assert.InRange(e.Copies, 1, 3); Assert.True(Catalog.GetCard(e.CardId).Droppable); });
            Assert.All(build.Sources, source => Assert.StartsWith("https://", source));
            var targets = build.Entries.Select(e => e.CardId).Concat(build.Recipes.Select(r => r.ResultCardId)).Distinct().ToArray();
            Assert.Contains(build.Entries, e => ForbiddenMemoriesStrategyEvaluator.IsFieldCard(e.CardId));
            var equips = build.Entries.Where(e => Catalog.GetCard(e.CardId).PrimaryType == "Equip").ToArray();
            Assert.True(equips.Sum(e => e.Copies) >= 2);
            Assert.All(equips, e => Assert.Contains(targets, id => Catalog.CanEquip(e.CardId, id)));
            foreach (var recipe in build.Recipes)
            {
                Assert.All(recipe.Materials, id => Assert.Contains(build.Entries, e => e.CardId == id));
                var current = recipe.Materials[0];
                foreach (var material in recipe.Materials.Skip(1)) current = Catalog.Resolve(current, material, false)!.Result.Id;
                Assert.Equal(recipe.ResultCardId, current);
            }
        }
    }

    [Fact]
    public void OwnedCountsMatchPhysicalCopiesAndDoNotCountCreatableFusionIcons()
    {
        var build = CampaignDeckLibrary.ForCatalog(Catalog).Single(b => b.Id == "thunder-umi");
        Assert.Equal(0, build.OwnedCopies([new(613, 3)]));
        Assert.Equal(5, build.OwnedCopies([new(94, 2), new(94, 1), new(425, 2), new(337, -8)]));
        Assert.Equal(40, build.OwnedCopies(build.Entries.Select(e => new OwnedCardQuantity(e.CardId, 295))));
        Assert.Equal(39, build.OwnedCopies(build.Entries.Select(e => new OwnedCardQuantity(e.CardId, e.Copies - (e.CardId == 334 ? 1 : 0)))));
    }

    [Fact]
    public void AdaptationPreservesMissingCopyLimitsAndCancellation()
    {
        var build = CampaignDeckLibrary.ForCatalog(Catalog).Single(b => b.Id == "meteor-hybrid");
        var fallback = Catalog.Cards.Where(c => c.Attack > 0 && c.Id > 100).Take(40).Select(c => c.Id).Order().ToArray();
        var owned = fallback.Select(id => new OwnedCardQuantity(id, 1)).Concat([new(82, 1), new(712, 2)]).ToArray();
        var adapted = CampaignDeckLibrary.Adapt(build, owned, fallback);
        var space = new DeckQuantitySpace(Catalog, owned, new());
        Assert.True(space.IsLegal(adapted, out _));
        Assert.Equal(1, adapted.Count(id => id == 82));
        Assert.Equal(2, adapted.Count(id => id == 712));
        Assert.DoesNotContain(713, adapted);
        Assert.Equal(adapted, CampaignDeckLibrary.Adapt(build, owned.Reverse(), fallback));
        Assert.Throws<OperationCanceledException>(() => CampaignDeckLibrary.Adapt(build, owned, fallback, cancellationToken: new(true)));
    }

    [Fact]
    public async Task ChosenReferenceCreatesExactOwnedLegalPreviewAndChangesFrozenIdentity()
    {
        var build = CampaignDeckLibrary.ForCatalog(Catalog).Single(b => b.Id == "sand-mercury");
        var owned = build.Entries.Select(e => new OwnedCardQuantity(e.CardId, e.Copies)).ToArray();
        var request = new DeckBuildRequest(owned, new(RecommendedBuildId: build.Id), DeckSearchMode.Quick);
        var preview = DeckBuildJob.CreateLegalPreview(Catalog, request);
        Assert.Equal(build.Expand(), preview.Report.Deck.SelectMany(e => Enumerable.Repeat(e.Card.Id, e.Copies)).Order().ToArray());
        Assert.Equal(0, preview.Report.ExactAnalysis.TotalHands);
        Assert.Equal(build.Expand(), new OwnedDeckOptimizer(Catalog).CreateSeedDeck(owned, request.Options));
        var job = new DeckBuildJob(Catalog, request);
        var other = new DeckBuildJob(Catalog, request with { Options = new() });
        // Use each job's real input identity without consuming a timed search.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var first = await job.RunAsync(cancellationToken: cancellation.Token);
        var second = await other.RunAsync(cancellationToken: cancellation.Token);
        Assert.NotEqual(first.InputIdentity, second.InputIdentity);
    }

    [Fact]
    public void NaturalBodyAndExactTieAreSeparatedFromEndgamePower()
    {
        var analyzer = new DeckAnalyzer(Catalog);
        var tied = analyzer.Analyze([713, 657, 337, 336, 686], false);
        Assert.Equal(1, tied.HandsWith2800Body);
        Assert.Equal(1, tied.HandsWith4500Setup);
        Assert.Equal(0, tied.HandsWithEndgamePower);
        Assert.Equal(1, tied.HandsWithEndgamePowerOrBoardClear);
        Assert.Equal(0, tied.HandsWithNoMonster);
        var better = analyzer.Analyze([713, 657, 668, 336, 686], false);
        Assert.Equal(5000, better.MaximumSetupAttack);
        Assert.Equal(1, better.HandsWithEndgamePower);
        var brick = analyzer.Analyze([657, 668, 315, 334, 337], false);
        Assert.Equal(1, brick.HandsWithNoMonster);
        Assert.Equal(0, brick.HandsWith2800Body);
        Assert.Equal(1, brick.HandsWithEndgamePowerOrBoardClear);
        var wall = analyzer.Analyze([366, 657, 668, 334, 337], false);
        Assert.Equal(0, wall.HandsWithNoMonster);
    }

    [Fact]
    public void NewCountsMatchAnIndependentPhysicalHandEnumerationAndParallelMerge()
    {
        int[] deck = [713, 713, 657, 657, 668, 334, 337, 336];
        var serial = new DeckAnalyzer(Catalog).Analyze(deck, false);
        var counted = new long[4];
        for (var a = 0; a < deck.Length - 4; a++)
        for (var b = a + 1; b < deck.Length - 3; b++)
        for (var c = b + 1; c < deck.Length - 2; c++)
        for (var d = c + 1; d < deck.Length - 1; d++)
        for (var e = d + 1; e < deck.Length; e++)
        {
            var hand = new[] { deck[a], deck[b], deck[c], deck[d], deck[e] };
            var body = hand.Contains(713);
            var attack = body ? 3500 + hand.Count(id => id == 657) * 1000 + hand.Count(id => id == 668) * 500 : 0;
            if (attack > 4500) counted[0]++;
            if (attack > 4500 || hand.Any(id => id is 336 or 337)) counted[1]++;
            if (body) counted[2]++; else counted[3]++;
        }
        Assert.Equal(counted, new[] { serial.HandsWithEndgamePower, serial.HandsWithEndgamePowerOrBoardClear, serial.HandsWith2800Body, serial.HandsWithNoMonster });
        var build = CampaignDeckLibrary.ForCatalog(Catalog).Single(b => b.Id == "natural-bosses");
        var exact = new DeckAnalyzer(Catalog).Analyze(build.Expand(), false);
        var parallel = new DeckAnalyzer(Catalog, workerCount: 4).Analyze(build.Expand(), false);
        Assert.Equal(658008, exact.TotalHands);
        Assert.Equal(JsonSerializer.Serialize(exact), JsonSerializer.Serialize(parallel));
    }

    [Fact]
    public void PreferredTerrainNeverBecomesAnAlreadyActiveCampaignField()
    {
        var owned = Enumerable.Range(1, 14).Select(id => new OwnedCardQuantity(id, 3)).ToArray();
        var plan = new CampaignDeckOptimizer(Catalog, CampaignResearchData.LoadBundled(AppContext.BaseDirectory))
            .Optimize(owned, 0, false, CampaignOpponentScope.SpecificOpponent, 1,
                new(SampleHands: 16, ExactFinalists: 1, PreferredFieldCardId: 334));
        Assert.Null(plan.Context.Safety.ActiveFieldCardId);
    }

    [Fact]
    public void ScoringValuesNaturalBodiesEndgameAnswersAndAvoidsSupportOnlyBricks()
    {
        var comparer = new DeckObjectiveComparer(false, false);
        DeckObjective Rank(DeckAnalysisReport report) => new(report, null, null, [713]);
        var analyzer = new DeckAnalyzer(Catalog);
        var strong = analyzer.Analyze([713, 657, 668, 315, 334], false);
        var tied = analyzer.Analyze([713, 657, 334, 334, 334], false);
        var brick = analyzer.Analyze([657, 668, 315, 334, 337], false);
        Assert.True(comparer.Compare(Rank(strong), Rank(tied)) > 0);
        Assert.True(comparer.Compare(Rank(strong), Rank(brick)) > 0);
        var natural = analyzer.Analyze([713, 713, 713, 392, 392], false);
        Assert.Equal(0, natural.HandsWithAnyFusion);
        Assert.True(comparer.Compare(Rank(natural), Rank(analyzer.Analyze([4, 4, 4, 200, 200], false))) > 0);
    }
}
