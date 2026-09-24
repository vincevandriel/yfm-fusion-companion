using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YfmCompanion.Desktop;

internal sealed class ThumbnailCache(long maximumBytes = 64L * 1024 * 1024)
{
    private readonly Dictionary<string, (ImageSource Image, long Bytes, LinkedListNode<string> Node)> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = [];
    private long _bytes;

    public ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (_items.TryGetValue(path!, out var found))
        {
            _recent.Remove(found.Node);
            _recent.AddFirst(found.Node);
            return found.Image;
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            var bytes = Math.Max(1L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4);
            while (_items.Count > 0 && _bytes + bytes > maximumBytes)
            {
                var oldest = _recent.Last!;
                var removed = _items[oldest.Value];
                _items.Remove(oldest.Value);
                _recent.RemoveLast();
                _bytes -= removed.Bytes;
            }
            var node = _recent.AddFirst(path);
            _items[path] = (bitmap, bytes, node);
            _bytes += bytes;
            return bitmap;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
