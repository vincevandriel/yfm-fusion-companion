using System.Buffers.Binary;

namespace YfmCompanion.RetroArch;

public sealed class ForbiddenMemoriesLiveReader(IRetroArchReadClient client)
{
    private const uint PlayerLifePointsOffset = 0x0EA004;
    private const uint OpponentLifePointsOffset = 0x0EA024;
    private const uint HandIndicesOffset = 0x0EA00A;
    private const uint ShuffledDeckOffset = 0x177FE8;
    private const uint ConstructedDeckOffset = 0x1D0200;
    private const uint ChestOffset = 0x1D0250;
    private const uint PlayerFieldOffset = 0x1A7B70;
    private const uint OpponentFieldOffset = 0x1A7D14;
    private const uint TerrainOffset = 0x09B364;
    private const uint GameStateOffset = 0x09B23A;
    private const uint DuelModeOffset = 0x09B26C;
    private const uint DrawHistoryOffset = 0x1A7E20;
    private const int MaximumCardId = 722;
    private MemoryAccessMode? _accessMode;

    public async Task<ForbiddenMemoriesLiveSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.HasContent)
        {
            throw new RetroArchNoContentException("RetroArch is connected, but no game is running. Load Yu-Gi-Oh! Forbidden Memories to resume live reading.");
        }

        if (!IsForbiddenMemories(status.GameBasename))
        {
            throw new RetroArchWrongContentException($"The running content is not the supported NTSC-U Forbidden Memories game ({status.GameBasename ?? "unknown content"}).");
        }

        _accessMode ??= await DiscoverMemoryAccessAsync(cancellationToken).ConfigureAwait(false);
        var mode = _accessMode.Value;
        try
        {
            var constructedDeck = await ReadAsync(mode, ConstructedDeckOffset, 40 * 2, cancellationToken).ConfigureAwait(false);
            var chest = await ReadAsync(mode, ChestOffset, MaximumCardId, cancellationToken).ConfigureAwait(false);
            var life = await ReadAsync(mode, PlayerLifePointsOffset, 0x22, cancellationToken).ConfigureAwait(false);
            var deck = await ReadAsync(mode, ShuffledDeckOffset, 40 * 2, cancellationToken).ConfigureAwait(false);
            var gameStateBytes = await ReadAsync(mode, GameStateOffset, 2, cancellationToken).ConfigureAwait(false);
            var duelMode = await ReadAsync(mode, DuelModeOffset, 1, cancellationToken).ConfigureAwait(false);

            var playerLifePoints = BinaryPrimitives.ReadUInt16LittleEndian(life);
            var opponentLifePoints = BinaryPrimitives.ReadUInt16LittleEndian(life.AsSpan(0x20));
            var gameState = BinaryPrimitives.ReadUInt16LittleEndian(gameStateBytes);
            var deckCards = ParseCardIds(deck, 40);
            var constructedDeckCards = ParseCardIds(constructedDeck, 40);
            var saveDataAvailable = constructedDeckCards.Count(IsValidCardId) == 40;

            // Duel RAM remains populated after a duel ends. 0x9B23A is the current
            // action word and legitimately changes throughout a duel (for example,
            // 0x8004 on the normal interactive board), so it cannot identify the
            // surrounding screen. The mode byte at 0x9B26C is 0xC3 for the duel
            // screen and changes when the campaign/title screen owns this memory.
            var duelActive = duelMode[0] == 0xC3 &&
                deckCards.Count(IsValidCardId) >= 35 &&
                playerLifePoints > 0 &&
                opponentLifePoints > 0;
            IReadOnlyList<int> handCards = [0, 0, 0, 0, 0];
            IReadOnlyList<LiveFieldCard> playerFieldCards = [];
            IReadOnlyList<LiveFieldCard> playerSpellTrapCards = [];
            IReadOnlyList<LiveFieldCard> opponentFieldCards = [];
            IReadOnlyList<LiveFieldCard> opponentSpellTrapCards = [];
            var terrainId = 0;
            if (duelActive)
            {
                var firstDuelState = await ReadDuelStateAsync(mode, cancellationToken).ConfigureAwait(false);
                var secondDuelState = await ReadDuelStateAsync(mode, cancellationToken).ConfigureAwait(false);
                if (!firstDuelState.ContentEquals(secondDuelState) ||
                    firstDuelState.DuelMode != duelMode[0] ||
                    firstDuelState.DuelMode != 0xC3)
                {
                    throw new RetroArchTransientStateException(
                        "The hand or field changed while it was being read. Fusion advice was withheld for this refresh to avoid associating a hand position with the wrong card.");
                }

                handCards = ParseHand(firstDuelState.HandIndices, firstDuelState.DrawHistory);
                playerFieldCards = ParseLiveField(firstDuelState.PlayerField, firstRecord: 0);
                playerSpellTrapCards = ParseLiveField(firstDuelState.PlayerField, firstRecord: 5);
                opponentFieldCards = ParseLiveField(firstDuelState.OpponentField, firstRecord: 0);
                opponentSpellTrapCards = ParseLiveField(firstDuelState.OpponentField, firstRecord: 5);
                terrainId = firstDuelState.Terrain[0] is <= 6 ? firstDuelState.Terrain[0] : 0;
                gameState = secondDuelState.GameState;
            }

            return new ForbiddenMemoriesLiveSnapshot(
                status,
                duelActive,
                handCards,
                playerFieldCards,
                playerSpellTrapCards,
                opponentFieldCards,
                opponentSpellTrapCards,
                terrainId,
                duelActive ? deckCards : [],
                saveDataAvailable ? constructedDeckCards : [],
                saveDataAvailable ? chest.Select(value => (int)value).ToArray() : [],
                saveDataAvailable,
                playerLifePoints,
                opponentLifePoints,
                gameState,
                DateTimeOffset.Now,
                mode.Label);
        }
        catch
        {
            _accessMode = null;
            throw;
        }
    }

    private async Task<MemoryAccessMode> DiscoverMemoryAccessAsync(CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            new MemoryAccessMode(false, 0x00000000, "READ_CORE_MEMORY / physical RAM"),
            new MemoryAccessMode(false, 0x80000000, "READ_CORE_MEMORY / PSX KSEG0"),
            new MemoryAccessMode(true, 0x00000000, "READ_CORE_RAM / achievement address")
        };

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                var deckFirst = await ReadAsync(candidate, ConstructedDeckOffset, 80, cancellationToken).ConfigureAwait(false);
                var deckSecond = await ReadAsync(candidate, ConstructedDeckOffset, 80, cancellationToken).ConfigureAwait(false);
                var validDeckCards = ParseCardIds(deckFirst, 40).Count(IsValidCardId);
                if (validDeckCards == 40 && deckFirst.AsSpan().SequenceEqual(deckSecond))
                {
                    return candidate;
                }

                // At boot/title, no player save is resident and the constructed-deck
                // signature is legitimately absent. A stable read of the game's state
                // dispatcher still proves that this supported memory command is usable.
                var stateFirst = await ReadAsync(candidate, GameStateOffset, 2, cancellationToken).ConfigureAwait(false);
                var stateSecond = await ReadAsync(candidate, GameStateOffset, 2, cancellationToken).ConfigureAwait(false);
                if (stateFirst.AsSpan().SequenceEqual(stateSecond) &&
                    !stateFirst.AsSpan().SequenceEqual(new byte[] { 0xFF, 0xFF }))
                {
                    return candidate;
                }

                errors.Add($"{candidate.Label}: values did not pass validation");
            }
            catch (Exception exception) when (exception is RetroArchProtocolException or TimeoutException)
            {
                errors.Add($"{candidate.Label}: {exception.Message}");
            }
        }

        throw new RetroArchProtocolException(
            "SwanStation did not expose valid PS1 RAM through RetroArch's supported read commands. " +
            string.Join(" | ", errors));
    }

    private Task<byte[]> ReadAsync(MemoryAccessMode mode, uint offset, int count, CancellationToken cancellationToken)
    {
        var address = checked(mode.AddressBase + offset);
        return mode.UseCoreRam
            ? client.ReadCoreRamAsync(address, count, cancellationToken)
            : client.ReadCoreMemoryAsync(address, count, cancellationToken);
    }

    private async Task<DuelDynamicState> ReadDuelStateAsync(
        MemoryAccessMode mode,
        CancellationToken cancellationToken)
    {
        var handIndices = await ReadAsync(mode, HandIndicesOffset, 5, cancellationToken).ConfigureAwait(false);
        var drawHistory = await ReadAsync(mode, DrawHistoryOffset, 60 * 6, cancellationToken).ConfigureAwait(false);
        var playerField = await ReadAsync(mode, PlayerFieldOffset, 10 * 28, cancellationToken).ConfigureAwait(false);
        var opponentField = await ReadAsync(mode, OpponentFieldOffset, 10 * 28, cancellationToken).ConfigureAwait(false);
        var terrain = await ReadAsync(mode, TerrainOffset, 1, cancellationToken).ConfigureAwait(false);
        var gameStateBytes = await ReadAsync(mode, GameStateOffset, 2, cancellationToken).ConfigureAwait(false);
        var duelMode = await ReadAsync(mode, DuelModeOffset, 1, cancellationToken).ConfigureAwait(false);
        return new DuelDynamicState(
            handIndices,
            drawHistory,
            playerField,
            opponentField,
            terrain,
            BinaryPrimitives.ReadUInt16LittleEndian(gameStateBytes),
            duelMode[0]);
    }

    private static bool IsForbiddenMemories(string? gameBasename) =>
        !string.IsNullOrWhiteSpace(gameBasename) &&
        gameBasename.Contains("Yu-Gi-Oh", StringComparison.OrdinalIgnoreCase) &&
        gameBasename.Contains("Forbidden Memories", StringComparison.OrdinalIgnoreCase);

    private static int[] ParseHand(ReadOnlySpan<byte> indices, ReadOnlySpan<byte> history)
    {
        var cards = new int[5];
        for (var handSlot = 0; handSlot < indices.Length; handSlot++)
        {
            var index = indices[handSlot];
            if (index >= 60)
            {
                continue;
            }

            var historyOffset = index * 6;
            var cardId = BinaryPrimitives.ReadUInt16LittleEndian(history[historyOffset..]);
            if (IsValidCardId(cardId) && history[historyOffset + 2] == index && history[historyOffset + 3] == index)
            {
                cards[handSlot] = cardId;
            }
        }

        return cards;
    }

    private static List<LiveFieldCard> ParseLiveField(ReadOnlySpan<byte> bytes, int firstRecord)
    {
        var cards = new List<LiveFieldCard>(5);
        for (var slot = 0; slot < 5; slot++)
        {
            var entry = bytes.Slice((firstRecord + slot) * 28, 28);
            var cardId = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            if (IsValidCardId(cardId) && (entry[11] & 0x80) != 0)
            {
                cards.Add(new LiveFieldCard(
                    slot + 1,
                    cardId,
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]),
                    BinaryPrimitives.ReadInt16LittleEndian(entry[6..])));
            }
        }

        return cards;
    }

    private static int[] ParseCardIds(ReadOnlySpan<byte> bytes, int count)
    {
        var cards = new int[count];
        for (var index = 0; index < count; index++)
        {
            cards[index] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index * 2)..]);
        }

        return cards;
    }

    private static bool IsValidCardId(int cardId) => cardId is >= 1 and <= MaximumCardId;

    private readonly record struct MemoryAccessMode(bool UseCoreRam, uint AddressBase, string Label);

    private sealed record DuelDynamicState(
        byte[] HandIndices,
        byte[] DrawHistory,
        byte[] PlayerField,
        byte[] OpponentField,
        byte[] Terrain,
        ushort GameState,
        byte DuelMode)
    {
        public bool ContentEquals(DuelDynamicState other) =>
            HandIndices.AsSpan().SequenceEqual(other.HandIndices) &&
            DrawHistory.AsSpan().SequenceEqual(other.DrawHistory) &&
            PlayerField.AsSpan().SequenceEqual(other.PlayerField) &&
            OpponentField.AsSpan().SequenceEqual(other.OpponentField) &&
            Terrain.AsSpan().SequenceEqual(other.Terrain) &&
            DuelMode == other.DuelMode;
    }
}
