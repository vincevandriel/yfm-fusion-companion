using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YfmCompanion.Desktop;

internal sealed class ThumbnailCache(long maximumBytes = 64L * 1024 * 1024)
{
    private readonly Dictionary<string, (ImageSource Image, long Bytes, long FileLength, DateTime Modified, LinkedListNode<string> Node)> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = [];
    private long _bytes;
    public long AccountedBytes => _bytes;
    public long LimitBytes => maximumBytes;

    public ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Path.GetFullPath(path);
            var file = new FileInfo(path);
            if (!file.Exists) { Invalidate(path); return null; }
            if (_items.TryGetValue(path, out var found))
            {
                if (found.FileLength == file.Length && found.Modified == file.LastWriteTimeUtc)
                {
                    _recent.Remove(found.Node);
                    _recent.AddFirst(found.Node);
                    return found.Image;
                }
                Invalidate(path);
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            // StreamSource avoids WPF's independent URI cache serving replaced artwork.
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            var bytes = Math.Max(1L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4) + 512 + path.Length * 2L;
            if (bytes > maximumBytes) return null;
            while (_items.Count > 0 && _bytes + bytes > maximumBytes)
            {
                Invalidate(_recent.Last!.Value);
            }
            var node = _recent.AddFirst(path);
            _items[path] = (bitmap, bytes, file.Length, file.LastWriteTimeUtc, node);
            _bytes += bytes;
            return bitmap;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException)
        {
            Invalidate(path);
            return null;
        }
    }

    public void Invalidate(string path)
    {
        if (!_items.Remove(path, out var removed)) return;
        _recent.Remove(removed.Node);
        _bytes -= removed.Bytes;
    }
}
