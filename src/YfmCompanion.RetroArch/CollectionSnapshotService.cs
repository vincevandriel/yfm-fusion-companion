using System.Security.Cryptography;

namespace YfmCompanion.RetroArch;

public enum CollectionSourceMode { AutomaticNewest, PinnedFile, Manual }

public sealed record CollectionSnapshot(
    SaveSnapshot Save, string ContentIdentity, IReadOnlyList<int> OwnedQuantities,
    int DistinctOwnedCards, int TotalOwnedCopies, bool IsStale = false, string? StaleReason = null)
{
    public static CollectionSnapshot FromSave(SaveSnapshot save, string contentIdentity, bool stale = false, string? reason = null)
    {
        var owned = Enumerable.Range(1, Ps1MemoryCardReader.CardCount).Select(save.GetTotalOwned).ToArray();
        return new(save, contentIdentity, Array.AsReadOnly(owned), owned.Count(q => q > 0), owned.Sum(), stale, reason);
    }
}

public sealed record CollectionRefreshResult(CollectionSourceMode Mode, CollectionSnapshot? Snapshot,
    IReadOnlyList<SaveReadResult> Inspections, string Message, bool RetainedPrevious = false);

/// <summary>Single read-only source of ownership for all desktop workflows.</summary>
public sealed class CollectionSnapshotService
{
    public async Task<CollectionRefreshResult> RefreshAsync(CollectionSourceMode mode, string? pinnedFile,
        IEnumerable<string>? knownLocations, CollectionSnapshot? previous = null,
        CancellationToken cancellationToken = default)
    {
        if (mode == CollectionSourceMode.Manual)
            return new(mode, previous, [], "Manual collection is active; automatic saves will not overwrite it.", previous is not null);
        try
        {
            var stable = await StableDiscoverAsync(mode, pinnedFile, knownLocations, cancellationToken).ConfigureAwait(false);
            var discovery = stable.Discovery;
            var selected = stable.Selected;
            if (selected is null)
            {
                var detail = discovery.Inspections.Count == 0
                    ? "No supported memory-card files were found."
                    : string.Join(" • ", discovery.Inspections.Select(i => $"{Path.GetFileName(i.FilePath)}: {i.Error}"));
                return Retain(mode, previous, discovery.Inspections, detail);
            }
            var identity = stable.Identity!;
            var rejectedNewer = discovery.Inspections
                .Where(item => !item.IsValid && File.Exists(item.FilePath) && File.GetLastWriteTimeUtc(item.FilePath) >= selected.LastWriteTimeUtc)
                .Select(item => $"{Path.GetFileName(item.FilePath)} was newer but rejected: {item.Error}")
                .ToArray();
            var warning = rejectedNewer.Length == 0 ? string.Empty : $" Warning: {string.Join(" • ", rejectedNewer)}";
            return new(mode, CollectionSnapshot.FromSave(selected, identity), discovery.Inspections,
                $"Loaded {Path.GetFileName(selected.FilePath)} ({selected.LastWriteTimeUtc.ToLocalTime():g}).{warning}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Retain(mode, previous, [], error.Message);
        }
    }

    private static CollectionRefreshResult Retain(CollectionSourceMode mode, CollectionSnapshot? previous,
        IReadOnlyList<SaveReadResult> inspections, string reason) => previous is null
        ? new(mode, null, inspections, reason)
        : new(mode, previous with { IsStale = true, StaleReason = reason }, inspections,
            $"The previous collection is retained as stale: {reason}", true);

    private static async Task<(SaveDiscoveryResult Discovery, SaveSnapshot? Selected, string? Identity)> StableDiscoverAsync(CollectionSourceMode mode, string? pinnedFile,
        IEnumerable<string>? knownLocations, CancellationToken token)
    {
        SaveDiscoveryResult? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            last = mode == CollectionSourceMode.PinnedFile
                ? InspectPinned(pinnedFile)
                : RetroArchSaveLocator.Discover(additionalLocations: knownLocations);
            if (last.SelectedSnapshot is { } selected)
            {
                var before = new FileInfo(selected.FilePath);
                var length = before.Length;
                var write = before.LastWriteTimeUtc;
                var identity = await HashStableFileAsync(selected.FilePath, token).ConfigureAwait(false);
                var after = new FileInfo(selected.FilePath);
                if (after.Length == length && after.LastWriteTimeUtc == write && selected.LastWriteTimeUtc == write)
                    return (last, selected, identity);
            }
            if (attempt < 2) await Task.Delay(150 * (attempt + 1), token).ConfigureAwait(false);
        }
        if (last!.SelectedSnapshot is not null)
            throw new IOException("The selected save changed while it was being validated; the previous collection was retained.");
        return (last, null, null);
    }

    private static SaveDiscoveryResult InspectPinned(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new([SaveReadResult.Failure(path ?? Path.Combine(Path.GetTempPath(), "missing.srm"), "Pinned save file is missing.")], null);
        var result = Ps1MemoryCardReader.Inspect(path);
        return new([result], result.Snapshot);
    }

    private static async Task<string> HashStableFileAsync(string path, CancellationToken token)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = new FileInfo(path);
            var length = before.Length;
            var write = before.LastWriteTimeUtc;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            var after = new FileInfo(path);
            if (after.Length == length && after.LastWriteTimeUtc == write) return hash;
            if (attempt < 2) await Task.Delay(150 * (attempt + 1), token).ConfigureAwait(false);
        }
        throw new IOException("The save changed repeatedly while it was being read; retry after saving finishes.");
    }
}
