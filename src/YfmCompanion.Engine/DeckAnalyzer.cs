using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed class DeckAnalyzer(FusionCatalog catalog)
{
    public DeckAnalysisReport Analyze(
        IEnumerable<int> deckCardIds,
        bool includeGlitches = true,
        IProgress<DeckAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var deck = deckCardIds.ToArray();
        if (deck.Length > 40)
        {
            throw new ArgumentException("A deck may contain at most 40 cards.", nameof(deckCardIds));
        }

        foreach (var cardId in deck)
        {
            _ = catalog.GetCard(cardId);
        }

        if (deck.Length < 2)
        {
            return new DeckAnalysisReport(deck.Length, deck.Length, 0, 0, 0, 0, 0, 0, 0, []);
        }

        var handSize = Math.Min(5, deck.Length);
        var totalHands = Choose(deck.Length, handSize);
        var accumulator = new AnalysisAccumulator(catalog, totalHands);
        var hand = new int[handSize];
        EnumerateHands(
            deck,
            hand,
            sourceStart: 0,
            handIndex: 0,
            includeGlitches,
            accumulator,
            cancellationToken,
            progress);
        progress?.Report(new DeckAnalysisProgress(totalHands, totalHands));
        return accumulator.CreateReport(deck.Length, handSize);
    }

    public static long Choose(int population, int selected)
    {
        if (selected < 0 || population < 0 || selected > population)
        {
            return 0;
        }

        selected = Math.Min(selected, population - selected);
        long result = 1;
        for (var index = 1; index <= selected; index++)
        {
            result = checked(result * (population - selected + index) / index);
        }

        return result;
    }

    private static void EnumerateHands(
        IReadOnlyList<int> deck,
        int[] hand,
        int sourceStart,
        int handIndex,
        bool includeGlitches,
        AnalysisAccumulator accumulator,
        CancellationToken cancellationToken,
        IProgress<DeckAnalysisProgress>? progress)
    {
        if (handIndex == hand.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accumulator.EvaluateHand(hand, includeGlitches);
            if ((accumulator.CompletedHands & 0xFFF) == 0)
            {
                progress?.Report(new DeckAnalysisProgress(accumulator.CompletedHands, accumulator.TotalHands));
            }

            return;
        }

        var remaining = hand.Length - handIndex;
        for (var sourceIndex = sourceStart; sourceIndex <= deck.Count - remaining; sourceIndex++)
        {
            hand[handIndex] = deck[sourceIndex];
            EnumerateHands(
                deck,
                hand,
                sourceIndex + 1,
                handIndex + 1,
                includeGlitches,
                accumulator,
                cancellationToken,
                progress);
        }
    }

    private sealed class AnalysisAccumulator(FusionCatalog catalog, long totalHands)
    {
        private const int EquippedOutcomeOffset = 723;
        private readonly long[] _handsByOutcome = new long[2_169];
        private readonly DeckFusionRoute?[] _representativeRoutes = new DeckFusionRoute?[2_169];
        private readonly int[] _seenStamp = new int[2_169];
        private readonly List<int> _seenOutcomes = new(64);
        private int _stamp;
        private long _handsWithAny;
        private long _hands2000;
        private long _hands2500;
        private long _hands2800;
        private long _hands3000;
        private long _sumBestAttack;

        public long CompletedHands { get; private set; }
        public long TotalHands => totalHands;

        public void EvaluateHand(int[] hand, bool includeGlitches)
        {
            _stamp++;
            _seenOutcomes.Clear();
            Span<byte> sequence = stackalloc byte[5];
            Span<int> intermediateResults = stackalloc int[4];
            Span<bool> glitches = stackalloc bool[4];
            for (var start = 0; start < hand.Length; start++)
            {
                sequence[0] = (byte)start;
                Explore(
                    hand,
                    hand[start],
                    usedMask: 1 << start,
                    depth: 1,
                    sequence,
                    intermediateResults,
                    glitches,
                    includeGlitches);
            }

            var bestAttack = 0;
            foreach (var outcomeKey in _seenOutcomes)
            {
                _handsByOutcome[outcomeKey]++;
                var resultId = OutcomeCardId(outcomeKey);
                var attackBonus = BonusForOutcome(outcomeKey);
                bestAttack = Math.Max(bestAttack, catalog.GetCard(resultId).Attack + attackBonus);
            }

            if (_seenOutcomes.Count > 0)
            {
                _handsWithAny++;
            }

            if (bestAttack >= 2_000) _hands2000++;
            if (bestAttack >= 2_500) _hands2500++;
            if (bestAttack >= 2_800) _hands2800++;
            if (bestAttack >= 3_000) _hands3000++;
            _sumBestAttack += bestAttack;
            CompletedHands++;
        }

        public DeckAnalysisReport CreateReport(int deckSize, int handSize)
        {
            var results = Enumerable.Range(1, 2_168)
                .Where(outcomeKey => outcomeKey % EquippedOutcomeOffset != 0 && _handsByOutcome[outcomeKey] > 0)
                .Select(outcomeKey => new DeckFusionResult(
                    catalog.GetCard(OutcomeCardId(outcomeKey)),
                    _handsByOutcome[outcomeKey],
                    totalHands,
                    _representativeRoutes[outcomeKey] ?? throw new InvalidDataException("Missing representative route."),
                    BonusForOutcome(outcomeKey),
                    BonusForOutcome(outcomeKey)))
                .OrderByDescending(result => result.EffectiveAttack)
                .ThenByDescending(result => result.EffectiveDefense)
                .ThenByDescending(result => result.Probability)
                .ThenBy(result => result.Result.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new DeckAnalysisReport(
                deckSize,
                handSize,
                totalHands,
                _handsWithAny,
                _hands2000,
                _hands2500,
                _hands2800,
                _hands3000,
                totalHands == 0 ? 0 : (double)_sumBestAttack / totalHands,
                results);
        }

        private void Explore(
            IReadOnlyList<int> hand,
            int currentCardId,
            int usedMask,
            int depth,
            Span<byte> sequence,
            Span<int> intermediateResults,
            Span<bool> glitches,
            bool includeGlitches)
        {
            RecordTerminalEquips(
                hand,
                currentCardId,
                usedMask,
                depth,
                sequence,
                intermediateResults,
                glitches);

            for (var next = 0; next < hand.Count; next++)
            {
                if ((usedMask & (1 << next)) != 0 ||
                    (depth == 1 && !IsCanonicalInitialPair(hand, sequence[0], next)) ||
                    !catalog.TryResolvePair(currentCardId, hand[next], includeGlitches, out var resultId, out var isGlitch))
                {
                    continue;
                }

                sequence[depth] = (byte)next;
                intermediateResults[depth - 1] = resultId;
                glitches[depth - 1] = isGlitch;
                RecordRoute(hand, resultId, depth + 1, sequence, intermediateResults, glitches);
                Explore(
                    hand,
                    resultId,
                    usedMask | (1 << next),
                    depth + 1,
                    sequence,
                    intermediateResults,
                    glitches,
                    includeGlitches);
            }
        }

        private void RecordRoute(
            IReadOnlyList<int> hand,
            int resultId,
            int materialCount,
            ReadOnlySpan<byte> sequence,
            ReadOnlySpan<int> intermediateResults,
            ReadOnlySpan<bool> glitches)
        {
            if (_seenStamp[resultId] != _stamp)
            {
                _seenStamp[resultId] = _stamp;
                _seenOutcomes.Add(resultId);
            }

            var containsGlitch = glitches[..(materialCount - 1)].Contains(true);
            var existing = _representativeRoutes[resultId];
            if (existing is not null &&
                (existing.ContainsGlitch.CompareTo(containsGlitch) < 0 ||
                 (existing.ContainsGlitch == containsGlitch && existing.MaterialCount <= materialCount)))
            {
                return;
            }

            var materials = new Card[materialCount];
            for (var index = 0; index < materialCount; index++)
            {
                materials[index] = catalog.GetCard(hand[sequence[index]]);
            }

            var results = new Card[materialCount - 1];
            for (var index = 0; index < results.Length; index++)
            {
                results[index] = catalog.GetCard(intermediateResults[index]);
            }

            _representativeRoutes[resultId] = new DeckFusionRoute(materials, results, containsGlitch);
        }

        private void RecordTerminalEquips(
            IReadOnlyList<int> hand,
            int currentCardId,
            int usedMask,
            int depth,
            ReadOnlySpan<byte> sequence,
            ReadOnlySpan<int> intermediateResults,
            ReadOnlySpan<bool> glitches)
        {
            for (var next = 0; next < hand.Count; next++)
            {
                if ((usedMask & (1 << next)) != 0)
                {
                    continue;
                }

                // An equip is a terminal material applied to the accumulated
                // monster. Starting with the equip would display the operation
                // backwards and could imply that its bonus survives a fusion.
                var equip = catalog.CanEquip(hand[next], currentCardId)
                    ? catalog.ResolveEquip(currentCardId, hand[next])
                    : null;
                if (equip is null)
                {
                    continue;
                }

                var outcomeKey = (equip.AttackBonus == 1_000 ? 2 * EquippedOutcomeOffset : EquippedOutcomeOffset) + equip.EquippedCard.Id;
                if (_seenStamp[outcomeKey] != _stamp)
                {
                    _seenStamp[outcomeKey] = _stamp;
                    _seenOutcomes.Add(outcomeKey);
                }

                var containsGlitch = glitches[..Math.Max(0, depth - 1)].Contains(true);
                var existing = _representativeRoutes[outcomeKey];
                if (existing is not null &&
                    (existing.ContainsGlitch.CompareTo(containsGlitch) < 0 ||
                     (existing.ContainsGlitch == containsGlitch && existing.MaterialCount <= depth + 1)))
                {
                    continue;
                }

                var materials = new Card[depth + 1];
                for (var index = 0; index < depth; index++)
                {
                    materials[index] = catalog.GetCard(hand[sequence[index]]);
                }

                materials[depth] = catalog.GetCard(hand[next]);
                var results = new Card[depth];
                for (var index = 0; index < depth - 1; index++)
                {
                    results[index] = catalog.GetCard(intermediateResults[index]);
                }

                results[^1] = equip.EquippedCard;
                _representativeRoutes[outcomeKey] = new DeckFusionRoute(
                    materials,
                    results,
                    containsGlitch,
                    EndsWithEquip: true,
                    EquipBonus: equip.AttackBonus);
            }
        }

        private static int OutcomeCardId(int outcomeKey) =>
            outcomeKey % EquippedOutcomeOffset;

        private static bool IsCanonicalInitialPair(
            IReadOnlyList<int> hand,
            int firstIndex,
            int secondIndex)
        {
            var firstCardId = hand[firstIndex];
            var secondCardId = hand[secondIndex];
            return firstCardId < secondCardId ||
                (firstCardId == secondCardId && firstIndex < secondIndex);
        }

        private static int BonusForOutcome(int outcomeKey) => outcomeKey switch
        {
            > 1_446 => 1_000,
            > EquippedOutcomeOffset => 500,
            _ => 0
        };
    }
}
