using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Desktop;

internal static class ArtworkAudit
{
    public static void Run(string fixtureDirectory, string output)
    {
        var catalog = FusionCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "yfm.db"));
        var directory = Path.Combine(AppContext.BaseDirectory, "Artwork");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var cache = new ThumbnailCache();
        var count = 0;
        foreach (var entry in manifest.RootElement.GetProperty("Cards").EnumerateArray())
        {
            var id = entry.GetProperty("CardId").GetInt32();
            var file = Path.Combine(directory, $"{id:D3}.png");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            if (!hash.Equals(entry.GetProperty("SHA256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Artwork hash mismatch for card {id}.");
            var row = new OwnedCardRow(catalog.GetCard(id), _ => { });
            row.RefreshArtwork(null, cache);
            if (row.Artwork is null) throw new InvalidDataException($"Automatic artwork missing for card {id}.");
            count++;
        }
        if (count != 722 || cache.AccountedBytes > cache.LimitBytes) throw new InvalidDataException("Incomplete artwork or unbounded thumbnails.");
        var custom = Path.Combine(fixtureDirectory, "custom");
        Directory.CreateDirectory(custom);
        var replacement = Path.Combine(custom, "001.png");
        File.Copy(Path.Combine(directory, "002.png"), replacement);
        var selected = new OwnedCardRow(catalog.GetCard(1), _ => { });
        selected.RefreshArtwork(custom, cache);
        if (!ReferenceEquals(selected.Artwork, cache.Load(replacement))) throw new InvalidDataException("Custom folder override was ignored.");
        var explicitPath = Path.Combine(directory, "003.png");
        selected.RefreshArtwork(custom, cache, explicitPath);
        if (!ReferenceEquals(selected.Artwork, cache.Load(explicitPath))) throw new InvalidDataException("Explicit override precedence failed.");
        selected.RefreshArtwork(custom, cache, Path.Combine(custom, "missing.png"));
        if (!ReferenceEquals(selected.Artwork, cache.Load(replacement))) throw new InvalidDataException("Missing explicit override did not fall back to custom folder.");
        File.WriteAllText(replacement, "invalid image");
        selected.RefreshArtwork(custom, cache);
        var bundled = Path.Combine(directory, "001.png");
        if (!ReferenceEquals(selected.Artwork, cache.Load(bundled))) throw new InvalidDataException("Unreadable override did not fall back to bundled art.");
        File.Delete(replacement);
        selected.RefreshArtwork(custom, cache);
        if (!ReferenceEquals(selected.Artwork, cache.Load(bundled))) throw new InvalidDataException("Absent custom image did not use bundled art.");
        File.WriteAllText(Path.Combine(output, "artwork-verification.json"), JsonSerializer.Serialize(new
        {
            Passed = true,
            AutomaticImagesDecodedAndHashVerified = count,
            CustomAndExplicitOverridePrecedence = true,
            MissingAndUnreadableOverrideRecovery = true,
            RetainedCacheBytes = cache.AccountedBytes,
            CacheLimitBytes = cache.LimitBytes,
            SourceRevision = manifest.RootElement.GetProperty("Revision").GetString()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
