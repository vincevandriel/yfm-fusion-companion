namespace YfmCompanion.RetroArch;

public sealed class SaveSnapshot
{
    public SaveSnapshot(
        string filePath,
        DateTime lastWriteTimeUtc,
        int memoryCardBank,
        int blockNumber,
        string directoryFileName,
        string sourceFormat,
        IReadOnlyList<int> deckCardIds,
        IReadOnlyList<byte> chestQuantities,
        IReadOnlySet<int> libraryCardIds,
        IReadOnlyList<string> warnings)
    {
        if (deckCardIds.Count != Ps1MemoryCardReader.DeckSize)
        {
            throw new ArgumentException($"A Forbidden Memories deck must contain {Ps1MemoryCardReader.DeckSize} cards.", nameof(deckCardIds));
        }

        if (chestQuantities.Count != Ps1MemoryCardReader.CardCount)
        {
            throw new ArgumentException($"A Forbidden Memories chest must contain {Ps1MemoryCardReader.CardCount} quantity bytes.", nameof(chestQuantities));
        }

        FilePath = Path.GetFullPath(filePath);
        LastWriteTimeUtc = lastWriteTimeUtc;
        MemoryCardBank = memoryCardBank;
        BlockNumber = blockNumber;
        DirectoryFileName = directoryFileName;
        SourceFormat = sourceFormat;
        DeckCardIds = [.. deckCardIds];
        ChestQuantities = [.. chestQuantities];
        LibraryCardIds = new HashSet<int>(libraryCardIds);
        Warnings = [.. warnings];
    }

    public string FilePath { get; }

    public DateTime LastWriteTimeUtc { get; }

    public int MemoryCardBank { get; }

    public int BlockNumber { get; }

    public string DirectoryFileName { get; }

    public string SourceFormat { get; }

    public IReadOnlyList<int> DeckCardIds { get; }

    public IReadOnlyList<byte> ChestQuantities { get; }

    public IReadOnlySet<int> LibraryCardIds { get; }

    public IReadOnlyList<string> Warnings { get; }

    public int GetDeckQuantity(int cardId)
    {
        ValidateCardId(cardId);
        return DeckCardIds.Count(id => id == cardId);
    }

    public int GetChestQuantity(int cardId)
    {
        ValidateCardId(cardId);
        return ChestQuantities[cardId - 1];
    }

    public int GetTotalOwned(int cardId) => GetDeckQuantity(cardId) + GetChestQuantity(cardId);

    private static void ValidateCardId(int cardId)
    {
        if (cardId is < 1 or > Ps1MemoryCardReader.CardCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cardId));
        }
    }
}

public sealed record SaveReadResult(string FilePath, SaveSnapshot? Snapshot, string? Error)
{
    public bool IsValid => Snapshot is not null;

    public static SaveReadResult Success(SaveSnapshot snapshot) => new(snapshot.FilePath, snapshot, null);

    public static SaveReadResult Failure(string filePath, string error) => new(Path.GetFullPath(filePath), null, error);
}

public sealed record SaveDiscoveryResult(
    IReadOnlyList<SaveReadResult> Inspections,
    SaveSnapshot? SelectedSnapshot);
