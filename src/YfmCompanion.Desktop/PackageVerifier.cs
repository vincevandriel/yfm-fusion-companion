using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Desktop;

/// <summary>Explicit offline release self-check; never starts save discovery or an emulator connection.</summary>
internal static class PackageVerifier
{
    public static object Verify(string applicationDirectory)
    {
        var resources = RuntimeResources.FindRoot(applicationDirectory);
        var catalog = FusionCatalog.Load(Path.Combine(resources, "Data", "yfm.db"));
        if (catalog.Cards.Count != 722 || catalog.FusionPairs.Count != 25_146)
            throw new InvalidDataException("The package catalog is incomplete.");
        var research = CampaignResearchData.LoadBundled(applicationDirectory);
        var freeDuel = FreeDuelReferenceData.LoadBundled(applicationDirectory, catalog);
        var builds = CampaignDeckLibrary.ForCatalog(catalog);
        if (builds.Count != 6) throw new InvalidDataException("The reference deck library is incomplete.");
        var artwork = Path.Combine(resources, "Artwork");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(artwork, "manifest.json")));
        var ids = new HashSet<int>();
        foreach (var card in manifest.RootElement.GetProperty("Cards").EnumerateArray())
        {
            var id = card.GetProperty("CardId").GetInt32();
            var relative = card.GetProperty("Path").GetString()!;
            if (relative != $"{id:D3}.png" || !ids.Add(id))
                throw new InvalidDataException("The artwork manifest contains invalid or duplicate paths.");
            _ = catalog.GetCard(id);
            var path = Path.Combine(artwork, relative);
            using var input = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(input));
            if (!hash.Equals(card.GetProperty("SHA256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Artwork checksum failed for card {id}.");
            input.Position = 0;
            var decoded = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoded.PixelWidth <= 0 || decoded.PixelHeight <= 0)
                throw new InvalidDataException($"Artwork decoding failed for card {id}.");
        }
        if (ids.Count != catalog.Cards.Count) throw new InvalidDataException("The artwork collection is incomplete.");
        var portraitDirectory = Path.Combine(resources, "DuelistPortraits");
        using var portraitManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(portraitDirectory, "manifest.json")));
        var duelistIds = new HashSet<int>();
        foreach (var portrait in portraitManifest.RootElement.GetProperty("portraits").EnumerateArray())
        {
            var id = portrait.GetProperty("duelist_id").GetInt32();
            var relative = portrait.GetProperty("path").GetString()!;
            if (relative != $"{id:D2}.png" || !duelistIds.Add(id))
                throw new InvalidDataException("The duelist portrait manifest contains invalid or duplicate paths.");
            var path = Path.Combine(portraitDirectory, relative);
            using var input = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(input));
            if (!hash.Equals(portrait.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Portrait checksum failed for duelist {id}.");
            input.Position = 0;
            var decoded = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoded.PixelWidth != 48 || decoded.PixelHeight != 48)
                throw new InvalidDataException($"Portrait dimensions failed for duelist {id}.");
        }
        if (!duelistIds.SetEquals(Enumerable.Range(1, 39)))
            throw new InvalidDataException("The duelist portrait collection is incomplete.");
        return new
        {
            Passed = true,
            Version = typeof(App).Assembly.GetName().Version?.ToString(),
            ResourceLayout = Path.GetFileName(resources) == "Resources" ? "organized" : "developer",
            Cards = catalog.Cards.Count,
            FusionPairs = catalog.FusionPairs.Count,
            ArtworkVerified = ids.Count,
            ReferenceDecks = builds.Count,
            ResearchOpponents = research.Opponents.Count,
            FreeDuelOpponents = freeDuel.Duelists.Count,
            RewardTables = freeDuel.Duelists.Sum(d => d.RewardTables.Count),
            DuelistPortraitsVerified = duelistIds.Count,
            EmulatorAccessed = false,
            PersonalSettingsAccessed = false
        };
    }
}
