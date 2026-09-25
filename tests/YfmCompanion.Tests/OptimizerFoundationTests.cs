using System.Numerics;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class OptimizerFoundationTests
{
    [Fact]
    public void ReusedAnalysisScratchDoesNotLeakCountsOrMutateEarlierReports()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "One"), TestCatalogFactory.Card(2, "Two"), TestCatalogFactory.Card(100, "Result", 2800)],
            [TestCatalogFactory.Pair(1, 2, 100)]);
        var analyzer = new DeckAnalyzer(catalog, 0);
        int[] fusionDeck = [.. Enumerable.Repeat(1, 39), 2];
        var first = analyzer.Analyze(fusionDeck);
        var dead = analyzer.Analyze(Enumerable.Repeat(1, 40));
        var repeated = analyzer.Analyze(fusionDeck);
        Assert.Equal(0, dead.TotalBestFusionAttack);
        Assert.Empty(dead.FusionResults);
        Assert.Equal(first.TotalBestFusionAttack, repeated.TotalBestFusionAttack);
        Assert.Single(first.FusionResults);
        Assert.Equal(82251, first.HandsWithAnyFusion);
        Assert.Equal(new[] { 1, 2 }, first.FusionResults[0].RepresentativeRoute.Materials.Select(c => c.Id));
    }

    [Fact]
    public void CachedUncachedAndIndependentPhysicalHandsAgree()
    {
        var catalog = TestCatalogFactory.Create(
            Enumerable.Range(1, 8).Select(id => TestCatalogFactory.Card(id, $"Material {id}", 500))
                .Concat([TestCatalogFactory.Card(100, "First", 1800), TestCatalogFactory.Card(101, "Chain", 2800), TestCatalogFactory.Card(102, "Glitch", 3200)]),
            [TestCatalogFactory.Pair(1, 2, 100), TestCatalogFactory.Pair(100, 3, 101), TestCatalogFactory.Pair(4, 5, 102, true)], [(8, 100), (8, 101)]);
        int[] deck = [1, 1, 2, 3, 4, 5, 6, 8];
        foreach (var glitches in new[] { false, true })
        {
            var cache = new DeckAnalyzer(catalog, 4096);
            var cached = cache.Analyze(deck, glitches);
            var uncached = new DeckAnalyzer(catalog, 0).Analyze(deck, glitches);
            var counts = new Dictionary<(int Card, int Bonus), long>();
            long any = 0, sum = 0;
            for (var a = 0; a < deck.Length - 4; a++)
                for (var b = a + 1; b < deck.Length - 3; b++)
                    for (var c = b + 1; c < deck.Length - 2; c++)
                        for (var d = c + 1; d < deck.Length - 1; d++)
                            for (var e = d + 1; e < deck.Length; e++)
                            {
                                int[] hand = [deck[a], deck[b], deck[c], deck[d], deck[e]];
                                var outcomes = new HashSet<(int Card, int Bonus)>();
                                void Explore(int current, HashSet<int> used)
                                {
                                    for (var next = 0; next < hand.Length; next++)
                                    {
                                        if (used.Contains(next)) continue;
                                        if (catalog.CanEquip(hand[next], current)) outcomes.Add((current, hand[next] == 657 ? 1000 : 500));
                                        var fusion = catalog.Resolve(current, hand[next], glitches);
                                        if (fusion is null) continue;
                                        outcomes.Add((fusion.Result.Id, 0));
                                        Explore(fusion.Result.Id, [.. used, next]);
                                    }
                                }
                                for (var start = 0; start < hand.Length; start++) Explore(hand[start], [start]);
                                if (outcomes.Count > 0) any++;
                                sum += outcomes.Select(o => catalog.GetCard(o.Card).Attack + o.Bonus).DefaultIfEmpty().Max();
                                foreach (var outcome in outcomes) counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
                            }
            Assert.Equal(56, cached.TotalHands);
            Assert.Equal(any, cached.HandsWithAnyFusion);
            Assert.Equal(sum, cached.TotalBestFusionAttack);
            Assert.Equal(cached.TotalBestFusionAttack, uncached.TotalBestFusionAttack);
            Assert.Equal(cached.FusionResults.Select(r => (r.Result.Id, r.AttackBonus, r.HandsContainingResult)),
                uncached.FusionResults.Select(r => (r.Result.Id, r.AttackBonus, r.HandsContainingResult)));
            Assert.Equal(counts.Count, cached.FusionResults.Count);
            Assert.All(cached.FusionResults, r => Assert.Equal(counts[(r.Result.Id, r.AttackBonus)], r.HandsContainingResult));
            Assert.InRange(cache.CacheDiagnostics.AccountedBytes, 0, 4096);
        }
    }

    [Fact]
    public void SamplingProvenanceCachingAndCancellationAreExplicit()
    {
        var catalog = TestCatalogFactory.Create([TestCatalogFactory.Card(1, "One"), TestCatalogFactory.Card(2, "Two"), TestCatalogFactory.Card(3, "Result", 2500)], [TestCatalogFactory.Pair(1, 2, 3)]);
        var analyzer = new DeckAnalyzer(catalog);
        int[] deck = [.. Enumerable.Repeat(1, 20), .. Enumerable.Repeat(2, 20)];
        var first = analyzer.AnalyzeSampled(deck, 400, 123);
        var second = analyzer.AnalyzeSampled(deck.Reverse(), 400, 123);
        Assert.Same(first, second);
        Assert.False(first.IsExact);
        Assert.Equal(400, first.SampleCount);
        Assert.InRange(first.ProbabilityMargin95, .048, .050);
        Assert.True(analyzer.CacheDiagnostics.Hits > 0);
        var progress = new List<DeckAnalysisProgress>();
        var exact = analyzer.Analyze(deck, progress: new SynchronousProgress<DeckAnalysisProgress>(progress.Add));
        Assert.True(exact.IsExact);
        Assert.Equal(0, exact.SampleCount);
        Assert.Equal(658008, progress[^1].CompletedHands);
        Assert.Equal(0, progress[0].CompletedHands);
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => analyzer.Analyze(deck,
            progress: new SynchronousProgress<DeckAnalysisProgress>(_ => cancellation.Cancel()), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ObjectiveIsTransitiveAtBinEdgesAndUsesNumericCardTieBreaks()
    {
        var comparer = new DeckObjectiveComparer(false, true);
        var objectives = new List<DeckObjective>();
        for (var count = 492; count <= 508; count++)
            for (var safety = 0; safety < 3; safety++)
            {
                var report = new DeckAnalysisReport(40, 5, 1000, count, count, count, count, 0, 2000, []) { TotalBestFusionAttack = 2_000_000 };
                objectives.Add(new(report, null, new("Secondary", safety, 1, "Model", 1, 1, safety, safety), [1, 2]));
            }
        foreach (var a in objectives)
            foreach (var b in objectives)
            {
                Assert.Equal(Math.Sign(comparer.Compare(a, b)), -Math.Sign(comparer.Compare(b, a)));
                foreach (var c in objectives)
                    if (comparer.Compare(a, b) > 0 && comparer.Compare(b, c) > 0) Assert.True(comparer.Compare(a, c) > 0);
            }
        var same = objectives[0];
        Assert.True(comparer.Compare(same with { Cards = [2] }, same with { Cards = [10] }) > 0);
        Assert.True(comparer.Compare(same with { RequiredStarChips = 0 }, same with { RequiredStarChips = 1 }) > 0);
    }

    [Fact]
    public void CampaignAnswerCoverageUsesFixedBinsBeforeGauntletAndExactMetricsAfterIt()
    {
        var report = new DeckAnalysisReport(40, 5, 1000, 500, 500, 500, 500, 0, 2000, []) { TotalBestFusionAttack = 2_000_000 };
        DeckObjective Candidate(double coverage, double gauntlet) => new(report,
            new("Primary", 100, 1, "Model", 1, 1, 100, coverage),
            new("Gauntlet", gauntlet, 1, "Model", 1, 1, gauntlet, 0), [1, 2]);
        var comparer = new DeckObjectiveComparer(true, true);
        var lower = Candidate(.501, 200);
        var higher = Candidate(.504, 100);
        Assert.True(comparer.Compare(lower, higher) > 0); // Same 0.500–0.505 group, better gauntlet.
        Assert.True(comparer.Compare(Candidate(.505, 100), lower) > 0); // Next primary group wins.
        Assert.True(comparer.Compare(Candidate(.504, 200), lower) > 0); // Equal gauntlet: exact primary wins.
        Assert.True(new DeckObjectiveComparer(true, false).Compare(higher, lower) > 0);
        Assert.True(comparer.Compare(lower with { Safety = lower.Safety! with { SafeOpponentCount = 2 } }, higher) > 0);
        var candidates = (from coverage in new[] { .499999, .5, .500001, .504999, .505 }
                          from gauntlet in new[] { 100.0, 101.0, 200.0 }
                          select Candidate(coverage, gauntlet)).ToArray();
        foreach (var a in candidates)
            foreach (var b in candidates)
                foreach (var c in candidates)
                    if (comparer.Compare(a, b) > 0 && comparer.Compare(b, c) > 0)
                        Assert.True(comparer.Compare(a, c) > 0);
    }

    [Fact]
    public void QuantitySpaceMatchesIndependentVectorsAndBudgetPruning()
    {
        var cards = Enumerable.Range(1, 4).Select(id => TestCatalogFactory.Card(id, $"Card {id}") with { Password = "12345678", StarchipCost = id * 5 }).ToArray();
        var catalog = TestCatalogFactory.Create(cards, []);
        OwnedCardQuantity[] owned = [new(1, 38), new(2, 1)];
        var options = new DeckOptimizationOptions(CopyLimit: 40, AlreadyRedeemedCardNames: new HashSet<string> { "card 4" });
        var space = new DeckQuantitySpace(catalog, owned, options, true, 10);
        var actual = new HashSet<string>();
        var ordinal = BigInteger.Zero;
        while (ordinal < space.CapacityVectorCount)
        {
            var resolved = space.Resolve(ordinal);
            Assert.True(resolved.NextOrdinal > ordinal);
            if (resolved.Cards is not null)
            {
                Assert.True(space.IsLegal(resolved.Cards, out var cost));
                Assert.Equal(cost, resolved.RequiredStarChips);
                Assert.True(actual.Add(string.Join(',', resolved.Cards)));
            }
            ordinal = resolved.NextOrdinal;
        }
        var expected = new HashSet<string>();
        for (var first = 0; first <= 39; first++)
            for (var second = 0; second <= 2; second++)
                for (var third = 0; third <= 1; third++)
                {
                    if (first + second + third != 40) continue;
                    var cost = Math.Max(0, first - 38) * 5 + Math.Max(0, second - 1) * 10 + third * 15;
                    if (cost <= 10) expected.Add(string.Join(',', Enumerable.Repeat(1, first).Concat(Enumerable.Repeat(2, second)).Concat(Enumerable.Repeat(3, third))));
                }
        Assert.Equal(expected.Order(), actual.Order());
        Assert.Equal(space.CapacityVectorCount, ordinal);
        Assert.True(space.CapacityVectorCount > actual.Count);
    }

    [Fact]
    public async Task ProofPausesResumesAndRejectsChangedInputs()
    {
        var catalog = TestCatalogFactory.Create([TestCatalogFactory.Card(1, "One"), TestCatalogFactory.Card(2, "Two"), TestCatalogFactory.Card(3, "Three"), TestCatalogFactory.Card(100, "Result", 2500)], [TestCatalogFactory.Pair(1, 2, 100)]);
        var request = new DeckBuildRequest([new(1, 39), new(2, 1), new(3, 1)], new(CopyLimit: 40, SampleHands: 4), DeckSearchMode.ProveOptimal);
        var directory = Path.Combine(Path.GetTempPath(), "yfm-proof-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "proof.json");
        try
        {
            using var cancel = new CancellationTokenSource();
            var paused = await new DeckProofSearch(catalog).RunAsync(request, path,
                new SynchronousProgress<DeckProofProgress>(p => { if (p.State == DeckBuildState.Searching) cancel.Cancel(); }), cancel.Token);
            Assert.Equal(DeckBuildState.Paused, paused.State);
            Assert.False(paused.ProvenOptimal);
            Assert.True(File.Exists(path));
            var result = await new DeckProofSearch(catalog).RunAsync(request, path);
            Assert.True(result.ProvenOptimal);
            Assert.Equal(new BigInteger(3), result.LegalDecksEvaluated);
            Assert.Equal(result.TotalSpace, result.ResolvedSpace);
            Assert.NotNull(result.Best);
            Assert.Equal(39, result.Best.Deck.Single(e => e.Card.Id == 1).Copies);
            Assert.Equal(1, result.Best.Deck.Single(e => e.Card.Id == 2).Copies);
            Assert.Equal(658008, result.Best.ExactAnalysis.TotalHands);
            await Assert.ThrowsAsync<InvalidDataException>(() => new DeckProofSearch(catalog).RunAsync(request with { StarChips = 1 }, path));
            var replay = await new DeckProofSearch(catalog).RunAsync(request, path);
            Assert.Equal(result.LegalDecksEvaluated, replay.LegalDecksEvaluated);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ProofMatchesIndependentThreeCopyInventoryEnumeration()
    {
        var catalog = TestCatalogFactory.Create(Enumerable.Range(1, 14)
            .Select(id => TestCatalogFactory.Card(id, $"Material {id}"))
            .Append(TestCatalogFactory.Card(100, "Result", 2800)), [TestCatalogFactory.Pair(1, 2, 100)]);
        var inventory = Enumerable.Range(1, 14).Select(id => new OwnedCardQuantity(id, 3)).ToArray();
        var physical = inventory.SelectMany(e => Enumerable.Repeat(e.CardId, e.Quantity)).ToArray();
        var independentlyEnumerated = new Dictionary<string, int[]>();
        for (var first = 0; first < physical.Length; first++)
        {
            for (var second = first + 1; second < physical.Length; second++)
            {
                var deck = physical.Where((_, index) => index != first && index != second).ToArray();
                independentlyEnumerated.TryAdd(string.Join(',', deck), deck);
            }
        }
        Assert.Equal(105, independentlyEnumerated.Count);
        // Closed-form oracle for the only recipe, independent of hand enumeration and shared comparator.
        long Score(int[] deck)
        {
            var first = deck.Count(id => id == 1);
            var second = deck.Count(id => id == 2);
            return 658008 - DeckAnalyzer.Choose(40 - first, 5) - DeckAnalyzer.Choose(40 - second, 5)
                + DeckAnalyzer.Choose(40 - first - second, 5);
        }
        var bestCount = independentlyEnumerated.Values.Max(Score);
        var path = Path.Combine(Path.GetTempPath(), "yfm-proof-tests", Guid.NewGuid().ToString("N"), "proof.json");
        try
        {
            var result = await new DeckProofSearch(catalog).RunAsync(new(inventory, new(), DeckSearchMode.ProveOptimal), path);
            Assert.True(result.ProvenOptimal);
            Assert.Equal(new BigInteger(independentlyEnumerated.Count), result.LegalDecksEvaluated);
            Assert.Equal(bestCount, result.Best!.ExactAnalysis.HandsWithAnyFusion);
            Assert.Equal(bestCount * 2800, result.Best.ExactAnalysis.TotalBestFusionAttack);
            var corrupted = File.ReadAllText(path).Replace("\"Checksum\":\"", "\"Checksum\":\"BAD", StringComparison.Ordinal);
            File.WriteAllText(path, corrupted);
            await Assert.ThrowsAsync<InvalidDataException>(() => new DeckProofSearch(catalog).RunAsync(new(inventory, new(), DeckSearchMode.ProveOptimal), path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
