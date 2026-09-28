using System.Text.Json;
using System.Text.Json.Serialization;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record FreeDuelDrop(int DuelistId, string DuelistName, string TableId, string TableLabel, int CardId, int Weight)
{
    public const int Denominator = 2_048;
    public double Probability => (double)Weight / Denominator;
}

public sealed record FreeDuelRewardTable(string Id, string Label, IReadOnlyList<FreeDuelDrop> Drops);

public sealed record FreeDuelistReference(
    int Id,
    string Name,
    string PortraitFile,
    IReadOnlyList<FreeDuelRewardTable> RewardTables);

public sealed class FreeDuelReferenceData
{
    public const string FileName = "free_duel_reference.json";
    private readonly IReadOnlyDictionary<int, IReadOnlyList<FreeDuelDrop>> _dropsByCard;

    private FreeDuelReferenceData(
        string probabilityNote,
        IReadOnlyList<FreeDuelistReference> duelists,
        IReadOnlyDictionary<int, IReadOnlyList<FreeDuelDrop>> dropsByCard)
    {
        ProbabilityNote = probabilityNote;
        Duelists = duelists;
        _dropsByCard = dropsByCard;
    }

    public string ProbabilityNote { get; }
    public IReadOnlyList<FreeDuelistReference> Duelists { get; }

    public static FreeDuelReferenceData LoadBundled(string baseDirectory, FusionCatalog catalog) =>
        Load(Path.Combine(RuntimeResources.FindRoot(baseDirectory), CampaignResearchData.BundledDirectoryName, FileName), catalog);

    public static FreeDuelReferenceData Load(string path, FusionCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!File.Exists(path)) throw new FileNotFoundException("The Free Duel reward database is missing.", path);

        FreeDuelFile file;
        try
        {
            file = JsonSerializer.Deserialize<FreeDuelFile>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("The Free Duel reward database is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Free Duel reward database contains invalid JSON.", exception);
        }

        if (file.SchemaVersion != 1 || file.Duelists is null || file.Duelists.Count != 39 || string.IsNullOrWhiteSpace(file.ProbabilityNote))
            throw new InvalidDataException("The Free Duel reward database has an unsupported or incomplete schema.");
        if (!file.Duelists.Select(d => d.DuelistId).Order().SequenceEqual(Enumerable.Range(1, 39)))
            throw new InvalidDataException("The Free Duel reward database must contain duelists 1 through 39 exactly once.");

        var allDrops = new List<FreeDuelDrop>(8_666);
        var duelists = new List<FreeDuelistReference>(39);
        foreach (var item in file.Duelists.OrderBy(d => d.DuelistId))
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Portrait != $"{item.DuelistId:D2}.png" || item.RewardTables is null || item.RewardTables.Count != 3)
                throw new InvalidDataException($"Duelist {item.DuelistId} is missing its identity, portrait, or three reward tables.");

            var tables = new List<FreeDuelRewardTable>(3);
            foreach (var table in item.RewardTables)
            {
                if (table.Id is not ("SAPow" or "SATec" or "BCD") || string.IsNullOrWhiteSpace(table.Label) ||
                    table.Denominator != FreeDuelDrop.Denominator || table.Entries is null || table.Entries.Count == 0 ||
                    table.Entries.Sum(entry => (long)entry.Weight) != FreeDuelDrop.Denominator ||
                    table.Entries.Any(entry => entry.CardId is < 1 or > 722 || entry.Weight <= 0) ||
                    table.Entries.Select(entry => entry.CardId).Distinct().Count() != table.Entries.Count)
                    throw new InvalidDataException($"Duelist {item.DuelistId} has an invalid {table.Id} reward table.");

                var drops = table.Entries.Select(entry =>
                {
                    _ = catalog.GetCard(entry.CardId);
                    return new FreeDuelDrop(item.DuelistId, item.Name, table.Id, table.Label, entry.CardId, entry.Weight);
                }).OrderByDescending(drop => drop.Weight).ThenBy(drop => drop.CardId).ToArray();
                allDrops.AddRange(drops);
                tables.Add(new FreeDuelRewardTable(table.Id, table.Label, drops));
            }
            if (tables.Select(table => table.Id).Distinct(StringComparer.Ordinal).Count() != 3)
                throw new InvalidDataException($"Duelist {item.DuelistId} repeats a reward table.");
            duelists.Add(new FreeDuelistReference(item.DuelistId, item.Name, item.Portrait, tables));
        }

        var byCard = allDrops.GroupBy(drop => drop.CardId).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<FreeDuelDrop>)[.. group.OrderByDescending(drop => drop.Probability)
                .ThenBy(drop => drop.DuelistName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(drop => drop.TableLabel, StringComparer.OrdinalIgnoreCase)]);
        return new FreeDuelReferenceData(file.ProbabilityNote, duelists, byCard);
    }

    public IReadOnlyList<FreeDuelDrop> DropsForCard(int cardId) =>
        _dropsByCard.GetValueOrDefault(cardId) ?? [];

    public FreeDuelDrop? BestFarmForCard(int cardId) => DropsForCard(cardId).FirstOrDefault();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    private sealed record FreeDuelFile(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("probability_note")] string ProbabilityNote,
        [property: JsonPropertyName("duelists")] IReadOnlyList<DuelistItem> Duelists);

    private sealed record DuelistItem(
        [property: JsonPropertyName("duelist_id")] int DuelistId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("portrait")] string Portrait,
        [property: JsonPropertyName("reward_tables")] IReadOnlyList<RewardTableItem> RewardTables);

    private sealed record RewardTableItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("denominator")] int Denominator,
        [property: JsonPropertyName("entries")] IReadOnlyList<DropItem> Entries);

    private sealed record DropItem(
        [property: JsonPropertyName("card_id")] int CardId,
        [property: JsonPropertyName("weight")] int Weight);
}
