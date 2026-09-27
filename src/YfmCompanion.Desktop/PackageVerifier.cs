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
            EmulatorAccessed = false,
            PersonalSettingsAccessed = false
        };
    }
}
