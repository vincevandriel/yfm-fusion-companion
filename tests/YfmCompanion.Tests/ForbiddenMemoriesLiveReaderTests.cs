using System.Buffers.Binary;
using YfmCompanion.RetroArch;

namespace YfmCompanion.Tests;

public sealed class ForbiddenMemoriesLiveReaderTests
{
    [Fact]
    public async Task ReadsValidatedHandDeckLifePointsAndFiltersStalePlayerSlots()
    {
        var memory = CreateValidMemory();
        var client = new MemoryClient(memory, coreMemoryAvailable: true);
        var reader = new ForbiddenMemoriesLiveReader(client);

        var snapshot = await reader.ReadSnapshotAsync();

        Assert.Equal([101, 102, 103, 104, 105], snapshot.HandCardIds);
        Assert.Equal(40, snapshot.ShuffledDeckCardIds.Count);
        Assert.Equal(40, snapshot.ConstructedDeckCardIds.Count);
        Assert.Equal(722, snapshot.ChestQuantities.Count);
        Assert.True(snapshot.SaveDataAvailable);
        Assert.True(snapshot.DuelActive);
        Assert.Equal(0x0009, snapshot.GameState);
        Assert.Equal(8000, snapshot.PlayerLifePoints);
        Assert.Equal(7600, snapshot.OpponentLifePoints);
        var player = Assert.Single(snapshot.PlayerField);
        Assert.Equal(200, player.CardId);
        Assert.Equal(1450, player.Attack);
        Assert.Equal(1200, player.Defense);
        Assert.Equal(500, player.PowerModifier);
        var spell = Assert.Single(snapshot.PlayerSpellTrapField);
        Assert.Equal(333, spell.CardId);
        Assert.Equal(2, spell.Slot);
        Assert.Equal(300, Assert.Single(snapshot.OpponentField).CardId);
        Assert.Empty(snapshot.OpponentSpellTrapField);
        Assert.Equal(4, snapshot.TerrainId);
        Assert.StartsWith("READ_CORE_MEMORY", snapshot.MemoryCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecognizesDuelWhileActionWordIsNormalInteractiveBoardState()
    {
        var memory = CreateValidMemory();
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x9B23A), 0x8004);
        var reader = new ForbiddenMemoriesLiveReader(new MemoryClient(memory, coreMemoryAvailable: false));

        var snapshot = await reader.ReadSnapshotAsync();

        Assert.True(snapshot.DuelActive);
        Assert.Equal(0x8004, snapshot.GameState);
        Assert.Equal([101, 102, 103, 104, 105], snapshot.HandCardIds);
    }

    [Fact]
    public async Task WithholdsSnapshotWhenHandChangesAcrossSeparateMemoryReads()
    {
        var memory = CreateValidMemory();
        var client = new MemoryClient(memory, coreMemoryAvailable: true);
        var handReadCount = 0;
        client.BeforeRead = (physicalAddress, _) =>
        {
            if (physicalAddress == 0xEA00A && ++handReadCount == 2)
            {
                memory[0xEA00A] = 5;
                var entry = 0x1A7E20 + 5 * 6;
                BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(entry), 106);
                memory[entry + 2] = 5;
                memory[entry + 3] = 5;
            }
        };

        var exception = await Assert.ThrowsAsync<RetroArchTransientStateException>(
            () => new ForbiddenMemoriesLiveReader(client).ReadSnapshotAsync());

        Assert.Contains("wrong card", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AllowsActionWordToChangeWhenAdviceRelevantStateIsStable()
    {
        var memory = CreateValidMemory();
        var client = new MemoryClient(memory, coreMemoryAvailable: true);
        ushort actionWord = 0x1000;
        client.BeforeRead = (physicalAddress, _) =>
        {
            if (physicalAddress == 0x9B23A)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(physicalAddress), actionWord++);
            }
        };

        var snapshot = await new ForbiddenMemoriesLiveReader(client).ReadSnapshotAsync();

        Assert.True(snapshot.DuelActive);
        Assert.Equal([101, 102, 103, 104, 105], snapshot.HandCardIds);
        Assert.True(snapshot.GameState >= 0x1000);
    }

    [Fact]
    public async Task ReturnsPersistentDeckAndChestOutsideADuel()
    {
        var memory = CreateValidMemory();
        memory.AsSpan(0x177FE8, 80).Clear();
        memory.AsSpan(0xEA004, 0x22).Clear();
        memory[0x9B26C] = 0;
        memory[0x1D0250 + 57] = 2;
        var reader = new ForbiddenMemoriesLiveReader(new MemoryClient(memory, coreMemoryAvailable: false));

        var snapshot = await reader.ReadSnapshotAsync();

        Assert.False(snapshot.DuelActive);
        Assert.Empty(snapshot.ShuffledDeckCardIds);
        Assert.Equal(40, snapshot.ConstructedDeckCardIds.Count);
        Assert.Equal(2, snapshot.ChestQuantities[57]);
        Assert.True(snapshot.SaveDataAvailable);
        Assert.All(snapshot.HandCardIds, cardId => Assert.Equal(0, cardId));
        Assert.Empty(snapshot.PlayerField);
        Assert.Empty(snapshot.PlayerSpellTrapField);
        Assert.Empty(snapshot.OpponentField);
        Assert.Empty(snapshot.OpponentSpellTrapField);
        Assert.Equal(0, snapshot.TerrainId);
    }

    [Fact]
    public async Task IgnoresStaleDuelStructuresAfterReturningToStory()
    {
        var memory = CreateValidMemory();
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x9B23A), 0xE00D);
        memory[0x9B26C] = 0;
        var reader = new ForbiddenMemoriesLiveReader(new MemoryClient(memory, coreMemoryAvailable: false));

        var snapshot = await reader.ReadSnapshotAsync();

        Assert.False(snapshot.DuelActive);
        Assert.Equal(0xE00D, snapshot.GameState);
        Assert.Empty(snapshot.ShuffledDeckCardIds);
        Assert.All(snapshot.HandCardIds, cardId => Assert.Equal(0, cardId));
        Assert.Empty(snapshot.PlayerField);
        Assert.Empty(snapshot.PlayerSpellTrapField);
        Assert.Empty(snapshot.OpponentField);
        Assert.Empty(snapshot.OpponentSpellTrapField);
        Assert.Equal(0, snapshot.TerrainId);
        Assert.Equal(8000, snapshot.PlayerLifePoints);
        Assert.Equal(7600, snapshot.OpponentLifePoints);
    }

    [Fact]
    public async Task ConnectsAtTitleScreenBeforeSaveDataIsLoaded()
    {
        var memory = CreateValidMemory();
        memory.AsSpan(0x1D0200, 80 + 722).Clear();
        memory.AsSpan(0x177FE8, 80).Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x9B23A), 0xE001);
        memory[0x9B26C] = 0;
        var reader = new ForbiddenMemoriesLiveReader(new MemoryClient(memory, coreMemoryAvailable: false));

        var snapshot = await reader.ReadSnapshotAsync();

        Assert.False(snapshot.DuelActive);
        Assert.False(snapshot.SaveDataAvailable);
        Assert.Equal(0xE001, snapshot.GameState);
        Assert.Empty(snapshot.ConstructedDeckCardIds);
        Assert.Empty(snapshot.ChestQuantities);
        Assert.Empty(snapshot.ShuffledDeckCardIds);
        Assert.All(snapshot.HandCardIds, cardId => Assert.Equal(0, cardId));
    }

    [Fact]
    public async Task UsesReadCoreRamOnlyAfterCoreMemoryIsUnavailable()
    {
        var client = new MemoryClient(CreateValidMemory(), coreMemoryAvailable: false);
        var reader = new ForbiddenMemoriesLiveReader(client);

        var snapshot = await reader.ReadSnapshotAsync();

        Assert.StartsWith("READ_CORE_RAM", snapshot.MemoryCommand, StringComparison.Ordinal);
        Assert.True(client.CoreMemoryReadCount >= 2);
        Assert.True(client.CoreRamReadCount > 0);
    }

    [Fact]
    public async Task RejectsWrongGameBeforeAnyMemoryRead()
    {
        var client = new MemoryClient(CreateValidMemory(), coreMemoryAvailable: true)
        {
            Status = new RetroArchStatus(
                RetroArchPlaybackState.Playing,
                "Sony - PlayStation",
                "Another Game",
                null,
                "GET_STATUS PLAYING Sony - PlayStation,Another Game,crc32=0")
        };
        var reader = new ForbiddenMemoriesLiveReader(client);

        var exception = await Assert.ThrowsAsync<RetroArchWrongContentException>(() => reader.ReadSnapshotAsync());

        Assert.Contains("not the supported", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.CoreMemoryReadCount + client.CoreRamReadCount);
    }

    [Fact]
    public async Task ReportsContentlessRetroArchBeforeAnyMemoryRead()
    {
        var client = new MemoryClient(CreateValidMemory(), coreMemoryAvailable: true)
        {
            Status = new RetroArchStatus(
                RetroArchPlaybackState.Contentless,
                null,
                null,
                null,
                "GET_STATUS CONTENTLESS")
        };
        var reader = new ForbiddenMemoriesLiveReader(client);

        var exception = await Assert.ThrowsAsync<RetroArchNoContentException>(() => reader.ReadSnapshotAsync());

        Assert.Contains("no game is running", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.CoreMemoryReadCount + client.CoreRamReadCount);
    }

    private static byte[] CreateValidMemory()
    {
        var memory = new byte[0x200000];
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0xEA004), 8000);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0xEA024), 7600);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x9B23A), 0x0009);
        memory[0x9B26C] = 0xC3;
        memory[0x9B364] = 4;
        for (var index = 0; index < 5; index++)
        {
            memory[0xEA00A + index] = (byte)index;
            var entry = 0x1A7E20 + index * 6;
            BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(entry), (ushort)(101 + index));
            memory[entry + 2] = (byte)index;
            memory[entry + 3] = (byte)index;
        }

        for (var index = 0; index < 40; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x177FE8 + index * 2), (ushort)(index + 1));
            BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x1D0200 + index * 2), (ushort)(index + 1));
        }

        WriteFieldEntry(memory, 0x1A7B70, 28, 0, 200, 1450, 1200, active: true, powerModifier: 500);
        WriteFieldEntry(memory, 0x1A7B70, 28, 1, 201, 900, 700, active: false);
        WriteFieldEntry(memory, 0x1A7B70, 28, 6, 333, 0, 0, active: true);
        WriteFieldEntry(memory, 0x1A7D14, 28, 0, 300, 1800, 1600, active: true);
        WriteFieldEntry(memory, 0x1A7D14, 28, 1, 301, 1900, 1700, active: false);
        memory[0x1A7D14 + 28 + 11] = 0x0C;
        return memory;
    }

    private static void WriteFieldEntry(
        byte[] memory,
        int offset,
        int entrySize,
        int slot,
        ushort cardId,
        ushort attack,
        ushort defense,
        bool active,
        short powerModifier = 0)
    {
        var entry = memory.AsSpan(offset + slot * entrySize, entrySize);
        BinaryPrimitives.WriteUInt16LittleEndian(entry, cardId);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], attack);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], defense);
        BinaryPrimitives.WriteInt16LittleEndian(entry[6..], powerModifier);
        if (entrySize >= 12)
        {
            entry[11] = active ? (byte)0xC4 : (byte)0;
        }
    }

    private sealed class MemoryClient(byte[] memory, bool coreMemoryAvailable) : IRetroArchReadClient
    {
        public RetroArchStatus Status { get; set; } = new(
            RetroArchPlaybackState.Playing,
            "Sony - PlayStation",
            "Yu-Gi-Oh! Forbidden Memories (USA)",
            0x12345678,
            "GET_STATUS PLAYING Sony - PlayStation,Yu-Gi-Oh! Forbidden Memories (USA),crc32=12345678");

        public int CoreMemoryReadCount { get; private set; }

        public int CoreRamReadCount { get; private set; }

        public Action<int, int>? BeforeRead { get; set; }

        public Task<RetroArchStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<byte[]> ReadCoreMemoryAsync(uint address, int byteCount, CancellationToken cancellationToken = default)
        {
            CoreMemoryReadCount++;
            if (!coreMemoryAvailable)
            {
                throw new RetroArchProtocolException("no memory map defined");
            }

            return Task.FromResult(Read(address, byteCount));
        }

        public Task<byte[]> ReadCoreRamAsync(uint address, int byteCount, CancellationToken cancellationToken = default)
        {
            CoreRamReadCount++;
            return Task.FromResult(Read(address, byteCount));
        }

        private byte[] Read(uint address, int byteCount)
        {
            var physicalAddress = (int)(address & 0x1fffff);
            BeforeRead?.Invoke(physicalAddress, byteCount);
            return memory.AsSpan(physicalAddress, byteCount).ToArray();
        }
    }
}
