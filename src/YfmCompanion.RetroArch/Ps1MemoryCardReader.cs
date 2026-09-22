using System.Buffers.Binary;
using System.Text;

namespace YfmCompanion.RetroArch;

public static class Ps1MemoryCardReader
{
    public const int CardCount = 722;
    public const int DeckSize = 40;
    public const int MemoryCardBankSize = 128 * 1024;
    public const int BlockSize = 8 * 1024;
    public const int SaveCopyLength = 0x680;
    public const int FirstSaveCopyOffset = 0x200;
    public const int SecondSaveCopyOffset = 0x880;
    public const int ChestOffset = 0x50;
    public const int FlagsOffset = 0x418;
    public const int FreeDuelUnlocksOffset = 0x4F4;
    public const int StarChipsOffset = 0x5E0;
    public const uint MaximumValidatedStarChips = 999_999;
    public const int DuelistCount = 39;
    public const string ForbiddenMemoriesSaveName = "BASLUS-01411-YUGIOH";

    private const int DirectoryEntrySize = 0x80;
    private const byte ActiveFirstBlockState = 0x51;
    private const int MaximumReadAttempts = 3;

    public static SaveReadResult Inspect(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return SaveReadResult.Failure(Path.GetFullPath("."), "No memory-card file was specified.");
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            return SaveReadResult.Failure(fullPath, "The memory-card file does not exist.");
        }

        try
        {
            var bytes = ReadWithRetry(fullPath);
            var snapshots = Parse(fullPath, File.GetLastWriteTimeUtc(fullPath), bytes);
            if (snapshots.Count == 0)
            {
                return SaveReadResult.Failure(fullPath, "No valid NTSC-U Forbidden Memories save block was found.");
            }

            var selected = snapshots[0];
            if (snapshots.Count > 1)
            {
                selected = AddWarning(selected, $"This image contains {snapshots.Count} valid Forbidden Memories saves; bank {selected.MemoryCardBank}, block {selected.BlockNumber} was selected.");
            }

            return SaveReadResult.Success(selected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return SaveReadResult.Failure(fullPath, exception.Message);
        }
    }

    public static IReadOnlyList<SaveSnapshot> Parse(string filePath, DateTime lastWriteTimeUtc, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is not MemoryCardBankSize and not (MemoryCardBankSize * 2))
        {
            throw new InvalidDataException("Only raw 128 KiB or 256 KiB PlayStation memory-card images are supported.");
        }

        var results = new List<SaveSnapshot>();
        var bankCount = bytes.Length / MemoryCardBankSize;
        for (var bankIndex = 0; bankIndex < bankCount; bankIndex++)
        {
            var bankOffset = bankIndex * MemoryCardBankSize;
            if (bytes[bankOffset] != (byte)'M' || bytes[bankOffset + 1] != (byte)'C')
            {
                continue;
            }

            for (var blockNumber = 1; blockNumber <= 15; blockNumber++)
            {
                var directoryOffset = bankOffset + (blockNumber * DirectoryEntrySize);
                var directory = bytes.Slice(directoryOffset, DirectoryEntrySize);
                if (directory[0] != ActiveFirstBlockState || !HasValidDirectoryChecksum(directory))
                {
                    continue;
                }

                var saveSize = BinaryPrimitives.ReadInt32LittleEndian(directory.Slice(4, 4));
                var nextBlock = BinaryPrimitives.ReadUInt16LittleEndian(directory.Slice(8, 2));
                var fileName = ReadDirectoryFileName(directory.Slice(10, 20));
                if (saveSize != BlockSize || nextBlock != ushort.MaxValue ||
                    !fileName.Equals(ForbiddenMemoriesSaveName, StringComparison.Ordinal))
                {
                    continue;
                }

                var blockOffset = bankOffset + (blockNumber * BlockSize);
                if (bytes[blockOffset] != (byte)'S' || bytes[blockOffset + 1] != (byte)'C')
                {
                    continue;
                }

                var firstCopy = bytes.Slice(blockOffset + FirstSaveCopyOffset, SaveCopyLength);
                var secondCopy = bytes.Slice(blockOffset + SecondSaveCopyOffset, SaveCopyLength);
                if (!firstCopy.SequenceEqual(secondCopy))
                {
                    continue;
                }

                var deck = ReadDeck(firstCopy);
                if (deck.Any(cardId => cardId is < 0 or > CardCount))
                {
                    continue;
                }

                var chest = firstCopy.Slice(ChestOffset, CardCount).ToArray();
                var library = ReadLibrary(firstCopy);
                var encodedStarChips = BinaryPrimitives.ReadUInt32LittleEndian(firstCopy.Slice(StarChipsOffset, sizeof(uint)));
                uint? starChips = encodedStarChips <= MaximumValidatedStarChips ? encodedStarChips : null;
                var unlockedDuelists = ReadUnlockedDuelists(firstCopy);
                var warnings = BuildLegalityWarnings(deck, chest);
                if (!deck.All(cardId => cardId is >= 1 and <= CardCount))
                {
                    warnings.Add("The saved deck has empty slots. The chest and other snapshot data remain available, but this deck cannot be loaded into Deck Analyzer until it contains 40 cards.");
                }
                if (starChips is null)
                {
                    warnings.Add($"Star Chip value {encodedStarChips:N0} is outside the validated 0–{MaximumValidatedStarChips:N0} range and was withheld.");
                }
                var sourceFormat = bankCount == 1
                    ? "Raw PlayStation memory card (128 KiB)"
                    : "Raw dual-bank PlayStation memory card (256 KiB)";
                results.Add(new SaveSnapshot(
                    filePath,
                    lastWriteTimeUtc,
                    bankIndex + 1,
                    blockNumber,
                    fileName,
                    sourceFormat,
                    deck,
                    chest,
                    library,
                    starChips,
                    unlockedDuelists,
                    warnings));
            }
        }

        return results;
    }

    private static byte[] ReadWithRetry(string fullPath)
    {
        IOException? lastError = null;
        for (var attempt = 1; attempt <= MaximumReadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > MemoryCardBankSize * 2)
                {
                    throw new InvalidDataException("The selected file is larger than a supported raw memory-card image.");
                }

                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                return bytes;
            }
            catch (IOException exception)
            {
                lastError = exception;
                if (attempt < MaximumReadAttempts)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        throw new IOException("The memory-card file is temporarily locked or unavailable. The companion remains usable in manual mode.", lastError);
    }

    private static int[] ReadDeck(ReadOnlySpan<byte> saveCopy)
    {
        var deck = new int[DeckSize];
        for (var slot = 0; slot < DeckSize; slot++)
        {
            deck[slot] = BinaryPrimitives.ReadUInt16LittleEndian(saveCopy.Slice(slot * 2, 2));
        }

        return deck;
    }

    private static HashSet<int> ReadLibrary(ReadOnlySpan<byte> saveCopy)
    {
        var library = new HashSet<int>();
        for (var cardId = 1; cardId <= CardCount; cardId++)
        {
            var flagId = 0x120 + cardId;
            var byteOffset = flagId >> 3;
            var mask = (byte)(0x80 >> (flagId & 7));
            if ((saveCopy[FlagsOffset + byteOffset] & mask) != 0)
            {
                library.Add(cardId);
            }
        }

        return library;
    }

    private static HashSet<int> ReadUnlockedDuelists(ReadOnlySpan<byte> saveCopy)
    {
        var unlocked = new HashSet<int>();
        for (var duelistId = 1; duelistId <= DuelistCount; duelistId++)
        {
            var byteOffset = duelistId >> 3;
            var mask = (byte)(0x80 >> (duelistId & 7));
            if ((saveCopy[FreeDuelUnlocksOffset + byteOffset] & mask) != 0)
            {
                unlocked.Add(duelistId);
            }
        }

        return unlocked;
    }

    private static List<string> BuildLegalityWarnings(int[] deck, byte[] chest)
    {
        var warnings = new List<string>();
        for (var cardId = 1; cardId <= CardCount; cardId++)
        {
            var total = chest[cardId - 1] + deck.Count(id => id == cardId);
            var legalLimit = cardId is >= 17 and <= 21 ? 1 : 3;
            if (total > legalLimit)
            {
                warnings.Add($"Card #{cardId:000} has {total} total copies, above its normal {legalLimit}-copy limit.");
            }
        }

        return warnings;
    }

    private static bool HasValidDirectoryChecksum(ReadOnlySpan<byte> entry)
    {
        byte checksum = 0;
        for (var index = 0; index < entry.Length - 1; index++)
        {
            checksum ^= entry[index];
        }

        return checksum == entry[^1];
    }

    private static string ReadDirectoryFileName(ReadOnlySpan<byte> bytes)
    {
        var zeroIndex = bytes.IndexOf((byte)0);
        var length = zeroIndex >= 0 ? zeroIndex : bytes.Length;
        return Encoding.ASCII.GetString(bytes[..length]).TrimEnd(' ');
    }

    private static SaveSnapshot AddWarning(SaveSnapshot snapshot, string warning) => new(
        snapshot.FilePath,
        snapshot.LastWriteTimeUtc,
        snapshot.MemoryCardBank,
        snapshot.BlockNumber,
        snapshot.DirectoryFileName,
        snapshot.SourceFormat,
        snapshot.DeckCardIds,
        snapshot.ChestQuantities,
        snapshot.LibraryCardIds,
            snapshot.StarChips,
        snapshot.UnlockedDuelistIds,
        [.. snapshot.Warnings, warning]);
}
