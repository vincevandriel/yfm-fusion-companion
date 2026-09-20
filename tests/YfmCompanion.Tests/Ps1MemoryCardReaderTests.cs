using System.Buffers.Binary;
using System.Text;
using YfmCompanion.RetroArch;

namespace YfmCompanion.Tests;

public sealed class Ps1MemoryCardReaderTests
{
    [Fact]
    public void ParsesDeckChestLibraryAndNonFirstBlock()
    {
        var bytes = CreateMemoryCard(blockNumber: 5);
        var snapshots = Ps1MemoryCardReader.Parse("test.srm", DateTime.UnixEpoch, bytes);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(5, snapshot.BlockNumber);
        Assert.Equal(Enumerable.Range(1, 40), snapshot.DeckCardIds);
        Assert.Equal(2, snapshot.GetChestQuantity(41));
        Assert.Equal(2, snapshot.GetTotalOwned(41));
        Assert.Equal(1, snapshot.GetTotalOwned(1));
        Assert.Contains(1, snapshot.LibraryCardIds);
        Assert.Contains(40, snapshot.LibraryCardIds);
        Assert.DoesNotContain(41, snapshot.LibraryCardIds);
        Assert.Equal(12345U, snapshot.StarChips);
        Assert.Equal([1, 8, 32, 39], snapshot.UnlockedDuelistIds.Order());
        Assert.Empty(snapshot.Warnings);
    }

    [Fact]
    public void ParsesSaveFromSecondBankOfDualBankImage()
    {
        var bytes = CreateMemoryCard(blockNumber: 3, bankCount: 2, bankIndex: 1);
        var snapshot = Assert.Single(Ps1MemoryCardReader.Parse("dual.mcr", DateTime.UnixEpoch, bytes));

        Assert.Equal(2, snapshot.MemoryCardBank);
        Assert.Equal(3, snapshot.BlockNumber);
        Assert.Contains("256 KiB", snapshot.SourceFormat, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDirectoryWithBadChecksum()
    {
        var bytes = CreateMemoryCard(blockNumber: 2);
        bytes[(2 * 0x80) + 0x7F] ^= 0x01;

        Assert.Empty(Ps1MemoryCardReader.Parse("bad.srm", DateTime.UnixEpoch, bytes));
    }

    [Fact]
    public void RejectsTornSaveWhoseMirrorDoesNotMatch()
    {
        var bytes = CreateMemoryCard(blockNumber: 4);
        bytes[(4 * Ps1MemoryCardReader.BlockSize) + Ps1MemoryCardReader.SecondSaveCopyOffset] ^= 0x01;

        Assert.Empty(Ps1MemoryCardReader.Parse("torn.srm", DateTime.UnixEpoch, bytes));
    }

    [Fact]
    public void RejectsEmptyMemoryCard()
    {
        var bytes = Enumerable.Repeat((byte)0xFF, Ps1MemoryCardReader.MemoryCardBankSize).ToArray();
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'C';

        Assert.Empty(Ps1MemoryCardReader.Parse("empty.srm", DateTime.UnixEpoch, bytes));
    }

    [Fact]
    public void RejectsUnsupportedImageSizesAndMissingFilesSafely()
    {
        Assert.Throws<InvalidDataException>(() =>
            Ps1MemoryCardReader.Parse("short.srm", DateTime.UnixEpoch, new byte[1024]));

        var missing = Ps1MemoryCardReader.Inspect(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.srm"));
        Assert.False(missing.IsValid);
        Assert.Contains("does not exist", missing.Error, StringComparison.OrdinalIgnoreCase);

        var blank = Ps1MemoryCardReader.Inspect("   ");
        Assert.False(blank.IsValid);
        Assert.Contains("No memory-card file", blank.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarnsInsteadOfCrashingForEditedCopyCounts()
    {
        var bytes = CreateMemoryCard(blockNumber: 1);
        var firstCopy = Ps1MemoryCardReader.BlockSize + Ps1MemoryCardReader.FirstSaveCopyOffset;
        var secondCopy = Ps1MemoryCardReader.BlockSize + Ps1MemoryCardReader.SecondSaveCopyOffset;
        bytes[firstCopy + Ps1MemoryCardReader.ChestOffset] = 9;
        bytes[secondCopy + Ps1MemoryCardReader.ChestOffset] = 9;

        var snapshot = Assert.Single(Ps1MemoryCardReader.Parse("edited.srm", DateTime.UnixEpoch, bytes));
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("Card #001", StringComparison.Ordinal));
    }

    [Fact]
    public void LockedFileReturnsFailureAndLeavesCallerOperational()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(temporaryDirectory, "Yu-Gi-Oh! Forbidden Memories (USA).srm");
            File.WriteAllBytes(path, CreateMemoryCard(blockNumber: 1));
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = Ps1MemoryCardReader.Inspect(path);

            Assert.False(result.IsValid);
            Assert.Contains("locked or unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void LocatorResolvesPortableRetroArchSaveDirectoryAndSelectsValidSave()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(temporaryDirectory, "retroarch.cfg");
            File.WriteAllText(configPath, "savefile_directory = \":\\saves\"\n");
            var saveDirectory = Path.Combine(temporaryDirectory, "saves", "SwanStation");
            Directory.CreateDirectory(saveDirectory);
            var validPath = Path.Combine(saveDirectory, "Yu-Gi-Oh! Forbidden Memories (USA).srm");
            var emptyPath = Path.Combine(saveDirectory, "Yu-Gi-Oh! Forbidden Memories.srm");
            File.WriteAllBytes(validPath, CreateMemoryCard(blockNumber: 6));
            File.WriteAllBytes(emptyPath, new byte[Ps1MemoryCardReader.MemoryCardBankSize]);

            Assert.Equal(Path.Combine(temporaryDirectory, "saves"), RetroArchSaveLocator.ResolveSaveDirectory(configPath));
            var result = RetroArchSaveLocator.Discover(configPath);

            Assert.Equal(2, result.Inspections.Count);
            Assert.NotNull(result.SelectedSnapshot);
            Assert.Equal(6, result.SelectedSnapshot.BlockNumber);
            Assert.Equal(Path.GetFullPath(validPath), result.SelectedSnapshot.FilePath);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void CurrentSwanStationSaveParsesWhenPresent()
    {
        var path = Environment.GetEnvironmentVariable("YFM_INTEGRATION_SAVE_PATH");
        var configPath = Environment.GetEnvironmentVariable("YFM_RETROARCH_CONFIG_PATH");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(configPath))
        {
            return;
        }

        Assert.True(File.Exists(path), $"Configured integration save was not found: {path}");
        Assert.True(File.Exists(configPath), $"Configured RetroArch config was not found: {configPath}");

        var result = Ps1MemoryCardReader.Inspect(path);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Ps1MemoryCardReader.DeckSize, result.Snapshot!.DeckCardIds.Count);
        Assert.Equal(Ps1MemoryCardReader.ForbiddenMemoriesSaveName, result.Snapshot.DirectoryFileName);

        var discovery = RetroArchSaveLocator.Discover(configPath);
        Assert.Equal(2, discovery.Inspections.Count);
        Assert.Equal(Path.GetFullPath(path), discovery.SelectedSnapshot!.FilePath);
    }

    private static byte[] CreateMemoryCard(int blockNumber, int bankCount = 1, int bankIndex = 0)
    {
        var bytes = Enumerable.Repeat((byte)0xFF, Ps1MemoryCardReader.MemoryCardBankSize * bankCount).ToArray();
        var bankOffset = bankIndex * Ps1MemoryCardReader.MemoryCardBankSize;
        bytes[bankOffset] = (byte)'M';
        bytes[bankOffset + 1] = (byte)'C';

        var directoryOffset = bankOffset + (blockNumber * 0x80);
        Array.Clear(bytes, directoryOffset, 0x80);
        bytes[directoryOffset] = 0x51;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(directoryOffset + 4, 4), Ps1MemoryCardReader.BlockSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(directoryOffset + 8, 2), ushort.MaxValue);
        Encoding.ASCII.GetBytes(Ps1MemoryCardReader.ForbiddenMemoriesSaveName)
            .CopyTo(bytes, directoryOffset + 10);
        byte checksum = 0;
        for (var index = 0; index < 0x7F; index++)
        {
            checksum ^= bytes[directoryOffset + index];
        }

        bytes[directoryOffset + 0x7F] = checksum;

        var blockOffset = bankOffset + (blockNumber * Ps1MemoryCardReader.BlockSize);
        bytes[blockOffset] = (byte)'S';
        bytes[blockOffset + 1] = (byte)'C';
        var saveCopy = new byte[Ps1MemoryCardReader.SaveCopyLength];
        for (var slot = 0; slot < Ps1MemoryCardReader.DeckSize; slot++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(saveCopy.AsSpan(slot * 2, 2), (ushort)(slot + 1));
        }

        saveCopy[Ps1MemoryCardReader.ChestOffset + 40] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(
            saveCopy.AsSpan(Ps1MemoryCardReader.StarChipsOffset, sizeof(uint)),
            12345U);
        foreach (var duelistId in new[] { 1, 8, 32, 39 })
        {
            SetDuelistUnlocked(saveCopy, duelistId);
        }
        SetLibraryFlag(saveCopy, 1);
        SetLibraryFlag(saveCopy, 40);
        saveCopy.CopyTo(bytes, blockOffset + Ps1MemoryCardReader.FirstSaveCopyOffset);
        saveCopy.CopyTo(bytes, blockOffset + Ps1MemoryCardReader.SecondSaveCopyOffset);
        return bytes;
    }

    private static void SetLibraryFlag(Span<byte> saveCopy, int cardId)
    {
        var flagId = 0x120 + cardId;
        saveCopy[Ps1MemoryCardReader.FlagsOffset + (flagId >> 3)] |= (byte)(0x80 >> (flagId & 7));
    }

    private static void SetDuelistUnlocked(Span<byte> saveCopy, int duelistId)
    {
        saveCopy[Ps1MemoryCardReader.FreeDuelUnlocksOffset + (duelistId >> 3)] |=
            (byte)(0x80 >> (duelistId & 7));
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "phase6-test-temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
