using System.Text.Json;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record CampaignDeckEntry(int CardId, int Copies, string Role);
public sealed record CampaignDeckRecipe(IReadOnlyList<int> Materials, int ResultCardId);
public sealed record CampaignDeckBlueprint(string Id, string Name, int HeroCardId, string Accent,
    string Stage, string Description, string Plan, string Cautions,
    IReadOnlyList<CampaignDeckEntry> Entries, IReadOnlyList<CampaignDeckRecipe> Recipes, IReadOnlyList<string> Sources)
{
    public int[] Expand() => [.. Entries.SelectMany(e => Enumerable.Repeat(e.CardId, e.Copies)).Order()];
    // Count physical required copies, not unique names or monsters that can be fused.
    public int OwnedCopies(IEnumerable<OwnedCardQuantity> inventory)
    {
        var owned = inventory.GroupBy(e => e.CardId).ToDictionary(g => g.Key, g => g.Sum(e => (long)Math.Max(0, e.Quantity)));
        return Entries.Sum(e => (int)Math.Min(e.Copies, owned.GetValueOrDefault(e.CardId)));
    }
}

/// <summary>App-authored vanilla reference builds informed by sourced community strategies.</summary>
public static class CampaignDeckLibrary
{
    public const string Version = "fan-campaign-library-v1";
    private static readonly IReadOnlyList<CampaignDeckBlueprint> Builds = Load();

    public static IReadOnlyList<CampaignDeckBlueprint> ForCatalog(FusionCatalog catalog)
    {
        var ids = catalog.Cards.Select(c => c.Id).ToHashSet();
        return Array.AsReadOnly(Builds.Where(b => b.Entries.All(e => ids.Contains(e.CardId)) && ids.Contains(b.HeroCardId)).ToArray());
    }

    public static int[] Adapt(CampaignDeckBlueprint build, IEnumerable<OwnedCardQuantity> inventory,
        IReadOnlyList<int> legalFallback, int copyLimit = 3, CancellationToken cancellationToken = default)
    {
        var capacities = inventory.GroupBy(e => e.CardId).ToDictionary(g => g.Key,
            g => (int)Math.Min(g.Sum(e => (long)Math.Max(0, e.Quantity)), OwnedDeckOptimizer.LegalCopyLimitForCard(g.Key, copyLimit)));
        var selected = new List<int>(40);
        var counts = new Dictionary<int, int>();
        foreach (var id in build.Expand().Concat(legalFallback))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selected.Count == 40) break;
            if (counts.GetValueOrDefault(id) >= capacities.GetValueOrDefault(id)) continue;
            selected.Add(id);
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        if (selected.Count != 40) throw new ArgumentException("The fallback must supply 40 legal available copies.", nameof(legalFallback));
        return [.. selected.Order()];
    }

    private static IReadOnlyList<CampaignDeckBlueprint> Load()
    {
        using var stream = typeof(CampaignDeckLibrary).Assembly.GetManifestResourceStream("YfmCompanion.Engine.Research.campaign-decks.json")
            ?? throw new InvalidDataException("Campaign deck library is missing.");
        var builds = JsonSerializer.Deserialize<CampaignDeckBlueprint[]>(stream) ?? throw new InvalidDataException("Campaign deck library is empty.");
        if (builds.Select(b => b.Id).Distinct().Count() != builds.Length || builds.Any(b =>
            b.Entries.Sum(e => e.Copies) != 40 || b.Entries.Any(e => e.Copies is < 1 or > 3) ||
            b.Entries.Select(e => e.CardId).Distinct().Count() != b.Entries.Count))
            throw new InvalidDataException("Campaign deck library contains invalid reference quantities.");
        return Array.AsReadOnly(builds.Select(b => b with
        {
            Entries = Array.AsReadOnly(b.Entries.ToArray()),
            Recipes = Array.AsReadOnly(b.Recipes.Select(r => r with { Materials = Array.AsReadOnly(r.Materials.ToArray()) }).ToArray()),
            Sources = Array.AsReadOnly(b.Sources.ToArray())
        }).ToArray());
    }
}
