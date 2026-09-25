using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using YfmCompanion.Desktop;
using YfmCompanion.Desktop.Controls;
using YfmCompanion.Engine;

internal static class DesktopContractAudit
{
    public static void Run(string directory)
    {
        Progress();
        Thumbnails(directory);
        StableSelection();
        AdaptiveLayout();
        ProofRestart(directory);
    }

    private static void Progress()
    {
        var progress = new DeckBuildProgress(DeckBuildState.Verifying, "Evaluating hands", TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), 1, null, null, 658008, 658008);
        Require(OptimizerProgressPresenter.Present(progress).Value < 1, "Worker progress claimed Ready before installation.");
        var proof = OptimizerProgressPresenter.Present(progress with
        { State = DeckBuildState.Searching, SearchBudget = null, CompletedHands = null, TotalHands = null });
        Require(proof.IsIndeterminate, "Unknown proof work did not show activity.");
        var timed = OptimizerProgressPresenter.Present(progress with
        { State = DeckBuildState.Searching, CompletedHands = null, TotalHands = null });
        Require(timed.Detail.Contains("search budget", StringComparison.Ordinal), "Timed progress did not label the budget.");
    }

    private static void Thumbnails(string directory)
    {
        var first = Path.Combine(directory, "art-1.png");
        var second = Path.Combine(directory, "art-2.png");
        WriteImage(first, Colors.Blue);
        WriteImage(second, Colors.Gold);
        var cache = new ThumbnailCache(60_000); // Only one decoded 96×144 thumbnail fits.
        var image = cache.Load(first);
        Require(image is not null && ReferenceEquals(image, cache.Load(first)), "Thumbnail was not reused.");
        cache.Load(second);
        Require(cache.AccountedBytes <= cache.LimitBytes, "Thumbnail cache exceeded its configured accounting limit.");
        Require(!ReferenceEquals(image, cache.Load(first)), "LRU entry was not evicted.");
        var previous = cache.Load(first);
        var oldTime = File.GetLastWriteTimeUtc(first);
        WriteImage(first, Colors.Red);
        File.SetLastWriteTimeUtc(first, oldTime.AddSeconds(2));
        var replacement = cache.Load(first);
        Require(replacement is not null && !ReferenceEquals(previous, replacement), "Replaced art served a stale decoded image.");
        var converted = new FormatConvertedBitmap((BitmapSource)replacement!, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        Require(pixels[2] == 255 && pixels[0] == 0, "Replaced art retained stale URI-cached pixels.");
        File.Delete(first);
        Require(cache.Load(first) is null && cache.AccountedBytes == 0, "Deleted artwork retained cache accounting.");
        File.WriteAllText(first, "not an image");
        Require(cache.Load(first) is null, "Invalid image did not fall back safely.");
        var tiny = new ThumbnailCache(1);
        Require(tiny.Load(second) is null && tiny.AccountedBytes == 0, "Oversize image bypassed the cache cap.");
    }

    private static void WriteImage(string path, Color color)
    {
        var pixels = new byte[96 * 144 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(96, 144, 96, 96, PixelFormats.Bgra32, null, pixels, 96 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Row(int Slot, string Name);
    private static void ProofRestart(string directory)
    {
        var checkpoint = Path.Combine(directory, "proof-restart.json");
        File.WriteAllText(checkpoint, "synthetic-checkpoint");
        using (var lease = new FileStream(checkpoint + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            try { ProofCheckpointFiles.PreserveAndRestart(checkpoint); throw new InvalidOperationException("An active proof checkpoint was archived."); }
            catch (IOException) { }
        }
        var backup = ProofCheckpointFiles.PreserveAndRestart(checkpoint);
        Require(!File.Exists(checkpoint) && File.ReadAllText(backup!) == "synthetic-checkpoint", "Fresh proof did not preserve the previous checkpoint.");
    }
    private static void StableSelection()
    {
        var list = new ListBox();
        Row[] rows = [new(1, "First"), new(2, "Second"), new(3, "Third")];
        StableCardItems.Update(list, rows, row => row.Slot);
        var source = list.ItemsSource;
        list.SelectedItem = rows[1];
        StableCardItems.Update(list, rows.ToArray(), row => row.Slot);
        Require(ReferenceEquals(source, list.ItemsSource) && ReferenceEquals(rows[1], list.SelectedItem), "Unchanged live data reset selection.");
        StableCardItems.Update(list, new Row[] { new(3, "Third"), new(2, "Changed"), new(4, "New") }, row => row.Slot);
        Require(ReferenceEquals(source, list.ItemsSource) && list.SelectedItem is Row { Slot: 2, Name: "Changed" }, "Changed live data lost the selected slot.");
        StableCardItems.Update(list, new Row[] { new(4, "New") }, row => row.Slot);
        Require(list.SelectedItem is null && list.Items.Count == 1, "Removed live card left a ghost selection.");
    }

    private static void AdaptiveLayout()
    {
        var panel = new AdaptiveColumnsPanel { MinimumColumnWidth = 230, MaximumColumns = 3 };
        for (var index = 0; index < 5; index++) panel.Children.Add(new Border { Height = 60 });
        foreach (var width in new[] { 300d, 650d, 1200d })
        {
            panel.Measure(new Size(width, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
            var rectangles = panel.Children.Cast<UIElement>().Select(child =>
                new Rect(child.TranslatePoint(new Point(), panel), child.RenderSize)).ToArray();
            Require(rectangles.All(rect => rect.Right <= width && rect.X >= 0), "Adaptive cards overflowed horizontally.");
            for (var a = 0; a < rectangles.Length; a++)
                for (var b = a + 1; b < rectangles.Length; b++)
                    Require(!rectangles[a].IntersectsWith(rectangles[b]), "Adaptive cards overlapped.");
        }
    }
}
