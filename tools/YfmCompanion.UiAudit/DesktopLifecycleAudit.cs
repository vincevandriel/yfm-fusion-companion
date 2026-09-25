using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using YfmCompanion.Data;
using YfmCompanion.Desktop;
using YfmCompanion.Desktop.Views;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;
using static Program;

internal static class DesktopLifecycleAudit
{
    internal static void Run(MainWindow current, string directory, string save)
    {
        Pinning(current, directory, save);
        Confirmation(current);
        var settings = DesktopSettingsStore.Load();
        try
        {
            DesktopSettingsStore.Save(settings with { CollectionSourceMode = CollectionSourceMode.PinnedFile, PinnedSavePath = save, LastSavePath = save, CompactMode = false });
            Watcher(save);
            CloseDuringProof(directory);
        }
        finally { DesktopSettingsStore.Save(settings); }
    }

    private static void Pinning(MainWindow window, string directory, string save)
    {
        Await((Task)Invoke(window, "SelectPinnedSaveAsync", save)!);
        var missing = Path.Combine(directory, "missing-pinned.srm");
        Await((Task)Invoke(window, "SelectPinnedSaveAsync", missing)!);
        Await((Task)Invoke(window, "RefreshCollectionAsync", false)!);
        if ((string?)Field(window, "_pinnedSavePath") != missing || Field(window, "_collectionSnapshot") is not CollectionSnapshot { IsStale: true })
            throw new InvalidOperationException("Retained data silently replaced a missing pinned source.");
        File.WriteAllBytes(missing, File.ReadAllBytes(save));
        Await((Task)Invoke(window, "RefreshCollectionAsync", false)!);
        if (Field(window, "_collectionSnapshot") is not CollectionSnapshot recovered || recovered.Save.FilePath != missing || recovered.IsStale)
            throw new InvalidOperationException("A recovered pinned file was not reloaded.");
        var oldRead = (Task)Invoke(window, "SelectPinnedSaveAsync", save)!;
        var newRead = (Task)Invoke(window, "SelectPinnedSaveAsync", missing)!;
        Await(Task.WhenAll(oldRead, newRead));
        if (Field(window, "_collectionSnapshot") is not CollectionSnapshot selected || selected.Save.FilePath != missing ||
            DesktopSettingsStore.Load().PinnedSavePath != missing)
            throw new InvalidOperationException("An in-flight refresh overwrote the newer pin selection.");
        Await((Task)Invoke(window, "SelectPinnedSaveAsync", save)!);
        var identity = ((CollectionSnapshot)Field(window, "_collectionSnapshot")!).ContentIdentity;
        var stamp = DateTime.UtcNow.AddMinutes(1);
        File.SetLastWriteTimeUtc(save, stamp);
        Await((Task)Invoke(window, "RefreshCollectionAsync", false)!);
        if (Field(window, "_saveSnapshot") is not SaveSnapshot fresh || fresh.LastWriteTimeUtc != File.GetLastWriteTimeUtc(save) ||
            ((CollectionSnapshot)Field(window, "_collectionSnapshot")!).ContentIdentity != identity)
            throw new InvalidOperationException("Unchanged save contents did not refresh displayed timestamp without changing identity.");
        var emptyPath = Path.Combine(directory, "empty-deck.srm");
        var empty = File.ReadAllBytes(save);
        foreach (var offset in new[] { Ps1MemoryCardReader.FirstSaveCopyOffset, Ps1MemoryCardReader.SecondSaveCopyOffset })
            Array.Clear(empty, Ps1MemoryCardReader.BlockSize + offset, Ps1MemoryCardReader.DeckSize * 2);
        File.WriteAllBytes(emptyPath, empty);
        var partial = Ps1MemoryCardReader.Inspect(emptyPath).Snapshot ?? throw new InvalidOperationException("Partial save fixture rejected.");
        Invoke(window, "LoadDeckAnalyzerFromSnapshot", partial);
        if (partial.HasCompleteDeck || ((DeckTrayViewModel)Field(window, "_deckTray")!).Cards.Count != 0 || !partial.ChestQuantities.Any(value => value > 0))
            throw new InvalidOperationException("Empty saved deck retained obsolete analyzer cards or lost chest ownership.");
    }

    private static void Confirmation(MainWindow owner)
    {
        foreach (var approve in new[] { false, true })
        {
            var dialog = new ProofConfirmationWindow(owner, 123456, "Measured estimate: synthetic fixture.", existing: true, restart: approve);
            dialog.ShowActivated = false;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
            {
                if (!dialog.SpaceText.Text.Contains("123", StringComparison.Ordinal) ||
                    !dialog.CheckpointText.Text.Contains(approve ? "backup" : "match", StringComparison.Ordinal))
                    throw new InvalidOperationException("Proof confirmation omitted space or recovery information.");
                if (approve) dialog.ConfirmProofButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else dialog.DialogResult = false;
            }, DispatcherPriority.Background);
            if ((dialog.ShowDialog() == true) != approve) throw new InvalidOperationException("Proof confirmation did not respect the choice.");
        }
    }

    private static MainWindow Open(bool watch = false)
    {
        var window = new MainWindow(backgroundServicesEnabled: watch, livePollingEnabled: false) { ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        WaitUntil(() => Field(window, "_collectionSnapshot") is not null, TimeSpan.FromSeconds(5), "Lifecycle fixture startup");
        return window;
    }

    private static void Watcher(string save)
    {
        var window = Open(watch: true);
        var bytes = File.ReadAllBytes(save);
        var identity = ((CollectionSnapshot)Field(window, "_collectionSnapshot")!).ContentIdentity;
        foreach (var offset in new[] { Ps1MemoryCardReader.FirstSaveCopyOffset, Ps1MemoryCardReader.SecondSaveCopyOffset })
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Ps1MemoryCardReader.BlockSize + offset + Ps1MemoryCardReader.StarChipsOffset, 4), 12346);
        File.WriteAllBytes(save, bytes);
        WaitUntil(() => Field(window, "_collectionSnapshot") is CollectionSnapshot result && result.ContentIdentity != identity,
            TimeSpan.FromSeconds(4), "Debounced folder watcher update");
        File.Delete(save);
        WaitUntil(() => Field(window, "_collectionSnapshot") is CollectionSnapshot { IsStale: true }, TimeSpan.FromSeconds(4), "Watcher deletion");
        File.WriteAllBytes(save, bytes);
        WaitUntil(() => Field(window, "_collectionSnapshot") is CollectionSnapshot { IsStale: false }, TimeSpan.FromSeconds(4), "Watcher recovery");
        window.Close();
        WaitUntil(() => !window.IsVisible, TimeSpan.FromSeconds(5), "Watcher shutdown");
    }

    private static void CloseDuringProof(string directory)
    {
        var window = Open();
        var catalog = (FusionCatalog)Field(window, "_catalog")!;
        var owned = Enumerable.Range(1, 14).Select(id => new OwnedCardQuantity(id, id == 14 ? 1 : 3)).ToArray();
        var request = new DeckBuildRequest(owned, new(IncludeGlitches: false), DeckSearchMode.ProveOptimal, SourceIdentity: "shutdown-fixture");
        var path = Path.Combine(directory, "shutdown-proof.json");
        var job = new DeckBuildJob(catalog, request, path);
        SetField(window, "_deckBuildJob", job);
        SetField(window, "_resultOwned", owned.ToDictionary(row => row.CardId, row => row.Quantity));
        var task = (Task)Invoke(window, "RunDeckBuildJobAsync", job, 0L, false)!;
        window.Close();
        WaitUntil(() => !window.IsVisible, TimeSpan.FromSeconds(5), "Proof checkpoint shutdown");
        task.GetAwaiter().GetResult();
        if (!task.IsCompleted || !File.Exists(path) || !((WindowActivityTracker)Field(window, "_activities")!).WhenIdle().IsCompleted)
            throw new InvalidOperationException("Window closed before proof work/checkpoint completed.");
        var resume = new DeckProofSearch(catalog).RunAsync(request, path);
        Await(resume);
        if (!resume.Result.ProvenOptimal) throw new InvalidOperationException("Proof saved during shutdown could not resume.");
    }
}
